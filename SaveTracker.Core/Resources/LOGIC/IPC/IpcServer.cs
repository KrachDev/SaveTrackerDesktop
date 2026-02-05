using System;
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
    /// </summary>
    public static class IpcServer
    {
        private const string PipeName = "SaveTracker_Command_Pipe";
        private static CancellationTokenSource? _cts;
        private static CommandHandler? _handler;

        /// <summary>
        /// Starts the IPC server in a background loop
        /// </summary>
        public static async Task StartAsync(IWindowManager windowManager, CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _handler = new CommandHandler(windowManager);

            DebugConsole.WriteInfo("[IPC] Starting command server on pipe: " + PipeName);

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await HandleClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    try
                    {
                        DebugConsole.WriteError($"[IPC] Server error: {ex.Message}");
                    }
                    catch { /* Worst case: console logging failed */ }

                    // Brief delay before accepting next connection on error
                    await Task.Delay(100, _cts.Token);
                }
            }

            DebugConsole.WriteInfo("[IPC] Command server stopped");
        }

        private static async Task HandleClientAsync(CancellationToken ct)
        {
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            using var server = NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0, // In bound buffer size
                0, // Out bound buffer size
                pipeSecurity);

            await server.WaitForConnectionAsync(ct);
            DebugConsole.WriteDebug("[IPC] Client connected");

            try
            {
                // StreamReader/Writer removed to prevent blocking on BOM/Preamble checks

                // Debug: Read raw bytes to see what we get
                byte[] buffer = new byte[4096];
                int bytesRead = await server.ReadAsync(buffer, 0, buffer.Length, ct);

                if (bytesRead == 0) return; // Disconnected

                string requestLine = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                DebugConsole.WriteDebug($"[IPC] Raw string content: '{requestLine}'");

                // Trim to handle potentially missing newlines in this debug mode or extra nulls
                requestLine = requestLine.Trim();

                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    byte[] errorBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(IpcResponse.Fail("Empty request")) + "\n");
                    await server.WriteAsync(errorBytes, 0, errorBytes.Length, ct);
                    return;
                }

                DebugConsole.WriteDebug($"[IPC] Received: {requestLine}");

                // Parse and handle the request
                IpcResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<IpcRequest>(requestLine, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (request == null || string.IsNullOrEmpty(request.Command))
                    {
                        response = IpcResponse.Fail("Invalid request format");
                    }
                    else
                    {
                        response = await _handler!.HandleAsync(request);
                    }
                }
                catch (JsonException ex)
                {
                    response = IpcResponse.Fail($"JSON parse error: {ex.Message}");
                }

                // Send response
                var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                DebugConsole.WriteDebug($"[IPC] Sending: {responseJson}");
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");
                await server.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                await server.FlushAsync(ct);
            }
            catch (IOException ex)
            {
                DebugConsole.WriteWarning($"[IPC] Client disconnected: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the IPC server
        /// </summary>
        public static void Stop()
        {
            _cts?.Cancel();
        }
    }
}
