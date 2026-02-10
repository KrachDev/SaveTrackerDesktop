using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC.IPC
{
    /// <summary>
    /// Named Pipe server for handling IPC commands from external addons (Playnite, etc.)
    /// Supports concurrent connections via a pool of pipe servers
    /// </summary>
    public static class IpcServer
    {
        private const string PipeName = "SaveTracker_Command_Pipe";

        // Configuration constants
        private const int MaxConcurrentClients = 10;
        private const int MaxBufferSize = 16384;  // 16KB
        private const int DefaultBufferSize = 4096;
        private const int ClientTimeoutMs = 30000;  // 30 seconds

        private static CancellationTokenSource? _cts;
        private static CommandHandler? _handler;
        private static readonly List<Task> _serverTasks = new();

        /// <summary>
        /// Starts the IPC server with concurrent connection handling
        /// </summary>
        public static async Task StartAsync(IWindowManager windowManager, CancellationToken cancellationToken = default)
        {
            // Try to clean up stale pipes from previous crashed instances
            CleanupStalePipes();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _handler = new CommandHandler(windowManager);

            DebugConsole.WriteInfo($"[IPC] Starting command server on pipe: {PipeName}");
            DebugConsole.WriteInfo($"[IPC] Max concurrent clients: {MaxConcurrentClients}");

            // Create multiple server loops for concurrent client handling
            _serverTasks.Clear();
            for (int i = 0; i < MaxConcurrentClients; i++)
            {
                var serverId = i;
                _serverTasks.Add(Task.Run(async () => await ServerLoopAsync(serverId, _cts.Token), _cts.Token));
            }

            // Wait for all server tasks to complete (when cancelled)
            try
            {
                await Task.WhenAll(_serverTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"[IPC] Server error: {ex.Message}");
            }

            DebugConsole.WriteInfo("[IPC] Command server stopped");
        }

        /// <summary>
        /// Attempts to clean up stale pipes from crashed instances
        /// </summary>
        private static void CleanupStalePipes()
        {
            try
            {
                // Check if we can connect to existing pipe - if yes, don't clean
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    client.Connect(500); // Try to connect with 500ms timeout
                    DebugConsole.WriteInfo("[IPC] Existing pipe detected - app already running");
                    return; // Existing instance is running, keep the pipe
                }
            }
            catch (TimeoutException)
            {
                DebugConsole.WriteWarning("[IPC] Pipe exists but no one is listening - cleaning up stale pipe");
            }
            catch (FileNotFoundException)
            {
                DebugConsole.WriteDebug("[IPC] No existing pipe found - proceeding with normal startup");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteDebug($"[IPC] Cleanup check error (expected): {ex.Message}");
            }

            // If we get here, try to remove stale pipe by waiting a bit and retrying
            try
            {
                // Windows will clean up the pipe automatically after all handles are closed
                // But we can wait a bit to ensure cleanup
                System.Threading.Thread.Sleep(500);
                DebugConsole.WriteInfo("[IPC] Stale pipe cleanup completed");
            }
            catch { /* Best effort */ }
        }

        /// <summary>
        /// Individual server loop that continuously accepts and handles client connections
        /// </summary>
        private static async Task ServerLoopAsync(int serverId, CancellationToken ct)
        {
            DebugConsole.WriteDebug($"[IPC] Server {serverId} started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await HandleClientConnectionAsync(serverId, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    try
                    {
                        DebugConsole.WriteError($"[IPC] Server {serverId} error: {ex.Message}");
                    }
                    catch { /* Worst case: console logging failed */ }

                    // Brief delay before accepting next connection on error
                    await Task.Delay(100, ct);
                }
            }

            DebugConsole.WriteDebug($"[IPC] Server {serverId} stopped");
        }

        /// <summary>
        /// Handles client connections with each server creating its own pipe instance
        /// Windows handles multiple instances on the same name automatically
        /// </summary>
        private static async Task HandleClientConnectionAsync(int serverId, CancellationToken ct)
        {
            NamedPipeServerStream? server = null;

            try
            {
                // Define pipe security to allow everyone to connect but ensure owner has full control
                // This prevents "Access Denied" when creating subsequent instances
                var pipeSecurity = new PipeSecurity();

                // Grant current user FullControl
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        currentUser,
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));
                }

                // Grant Everyone ReadWrite access so external clients can connect
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));

                // Retry with exponential backoff for the first few attempts
                int retryCount = 0;
                const int maxRetries = 5; // Increased retries

                while (server == null && retryCount < maxRetries && !ct.IsCancellationRequested)
                {
                    try
                    {
                        server = NamedPipeServerStreamAcl.Create(
                            PipeName,
                            PipeDirection.InOut,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            DefaultBufferSize,
                            DefaultBufferSize,
                            pipeSecurity);

                        DebugConsole.WriteDebug($"[IPC] Server {serverId}: Pipe instance created");
                        break;
                    }
                    catch (UnauthorizedAccessException ex) when (retryCount < maxRetries - 1)
                    {
                        retryCount++;
                        int delay = (int)Math.Pow(2, retryCount) * 150; // 300ms, 600ms, etc.
                        DebugConsole.WriteDebug($"[IPC] Server {serverId}: Access denied, retrying in {delay}ms (attempt {retryCount}). Error: {ex.Message}");
                        await Task.Delay(delay, ct);
                    }
                }

                if (server == null)
                {
                    DebugConsole.WriteError($"[IPC] Server {serverId}: Failed to create pipe after {maxRetries} retries");
                    return;
                }

                // Keep serving clients on this server's pipe instance
                while (!ct.IsCancellationRequested && server != null)
                {
                    try
                    {
                        // Wait for a client to connect to THIS server's pipe instance
                        await server.WaitForConnectionAsync(ct);

                        var connectionTime = DateTime.Now;
                        DebugConsole.WriteDebug($"[IPC] Server {serverId}: Client connected");

                        try
                        {
                            // Process the client request with timeout
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            timeoutCts.CancelAfter(ClientTimeoutMs);

                            await ProcessClientRequestAsync(server, serverId, timeoutCts.Token);

                            var duration = (DateTime.Now - connectionTime).TotalMilliseconds;
                            DebugConsole.WriteDebug($"[IPC] Server {serverId}: Request completed in {duration:F1}ms");
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            DebugConsole.WriteWarning($"[IPC] Server {serverId}: Client timeout after {ClientTimeoutMs}ms");
                        }
                        catch (IOException ex)
                        {
                            DebugConsole.WriteDebug($"[IPC] Server {serverId}: Client disconnected: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteError($"[IPC] Server {serverId}: Error processing request: {ex.Message}");
                        }
                        finally
                        {
                            // Disconnect and prepare for next client
                            if (server.IsConnected)
                            {
                                try
                                {
                                    server.Disconnect();
                                }
                                catch { /* Best effort disconnect */ }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Server loop cancelled, exit gracefully
                        break;
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"[IPC] Server {serverId}: Unexpected error in connection loop: {ex.Message}");
                        // Brief delay before retrying
                        await Task.Delay(100, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"[IPC] Server {serverId}: Fatal error: {ex.Message}");
            }
            finally
            {
                server?.Dispose();
                DebugConsole.WriteDebug($"[IPC] Server {serverId}: Pipe instance disposed");
            }
        }

        /// <summary>
        /// Processes a single request from a connected client
        /// </summary>
        private static async Task ProcessClientRequestAsync(NamedPipeServerStream server, int serverId, CancellationToken ct)
        {
            // Read request with dynamic buffer sizing
            byte[] buffer = new byte[DefaultBufferSize];
            int bytesRead = await server.ReadAsync(buffer, 0, buffer.Length, ct);

            if (bytesRead == 0)
            {
                DebugConsole.WriteDebug($"[IPC] Server {serverId}: Empty request (client disconnected)");
                return;
            }

            // Parse request
            string requestLine = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            if (string.IsNullOrWhiteSpace(requestLine))
            {
                await SendResponseAsync(server, IpcResponse.Fail("Empty request"), ct);
                return;
            }

            DebugConsole.WriteDebug($"[IPC] Server {serverId}: Received: {requestLine}");

            // Handle the request
            IpcResponse response;
            try
            {
                var request = JsonSerializer.Deserialize(requestLine, IpcJsonContext.Default.IpcRequest);

                if (request == null || string.IsNullOrEmpty(request.Command))
                {
                    response = IpcResponse.Fail("Invalid request format");
                }
                else
                {
                    // Process the command using the handler
                    response = await _handler!.HandleAsync(request);
                }
            }
            catch (JsonException ex)
            {
                response = IpcResponse.Fail($"JSON parse error: {ex.Message}");
                DebugConsole.WriteWarning($"[IPC] Server {serverId}: JSON error: {ex.Message}");
            }

            // Send response
            await SendResponseAsync(server, response, ct);
        }

        /// <summary>
        /// Sends a JSON response to the client
        /// </summary>
        private static async Task SendResponseAsync(NamedPipeServerStream server, IpcResponse response, CancellationToken ct)
        {
            var responseJson = JsonSerializer.Serialize(response, IpcJsonContext.Default.IpcResponse);
            DebugConsole.WriteDebug($"[IPC] Sending: {responseJson}");

            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");

            // Check if response exceeds buffer size
            if (responseBytes.Length > MaxBufferSize)
            {
                DebugConsole.WriteWarning($"[IPC] Response size ({responseBytes.Length} bytes) exceeds max buffer ({MaxBufferSize} bytes)");
                // Send error response instead
                var errorResponse = IpcResponse.Fail("Response too large");
                var errorJson = JsonSerializer.Serialize(errorResponse, IpcJsonContext.Default.IpcResponse);
                responseBytes = Encoding.UTF8.GetBytes(errorJson + "\n");
            }

            await server.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
            await server.FlushAsync(ct);
        }

        /// <summary>
        /// Stops the IPC server and all connection handlers
        /// </summary>
        public static void Stop()
        {
            DebugConsole.WriteInfo("[IPC] Stopping command server...");
            _cts?.Cancel();

            // Wait a moment for clean shutdown
            try
            {
                if (_serverTasks.Count > 0)
                {
                    Task.WaitAll(_serverTasks.ToArray(), TimeSpan.FromSeconds(5));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[IPC] Error during shutdown: {ex.Message}");
            }

            _cts?.Dispose();
            DebugConsole.WriteInfo("[IPC] Command server stopped and cleaned up");
        }
    }
}
