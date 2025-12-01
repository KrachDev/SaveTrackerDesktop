using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using SaveTracker.Resources.HELPERS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker.Resources.LOGIC
{
    /// <summary>
    /// Manages the file tracking session for game save files
    /// </summary>
    public class SaveFileTrackerManager
    {
        // Constants
        private const int DIRECTORY_SCAN_INTERVAL_MS = 5 * 60 * 1000; // 5 minutes
        private const int PROCESS_SHUTDOWN_GRACE_PERIOD_MS = 5000; // 5 seconds - wait for filesystem to settle
        private const string ETW_SESSION_NAME = "SaveTrackerSession";

        // Settings
        bool canTrack = true;
        bool trackWrites = true;
        bool trackReads = false;
        // Session state
        private TrackingSession _currentSession;
        private List<string> _lastSessionUploadList;

        public SaveFileTrackerManager()
        {

        }

        /// <summary>
        /// Main tracking method - same signature as original for compatibility
        /// </summary>
        public async Task Track(Game gameArg)
        {
            DebugConsole.WriteLine("== SaveTracker DebugConsole Started ==");

            // Check if already tracking
            if (_currentSession != null && _currentSession.IsTracking)
            {
                DebugConsole.WriteLine("Already tracking.");
                return;
            }

            // Clean up any existing ETW sessions
            CleanupExistingEtwSessions();

            // Initialize new tracking session
            _currentSession = new TrackingSession(gameArg);

            try
            {
                // Validate and initialize
                if (!await _currentSession.Initialize())
                    return;

                // Start tracking
                await _currentSession.StartTracking();

                // Wait for completion
                await _currentSession.WaitForCompletion();

                // Display results
                var uploadFiles = _currentSession.GetUploadList();
                _lastSessionUploadList = uploadFiles; // Cache for external access
                DisplayResults(uploadFiles);
            }
            catch (UnauthorizedAccessException)
            {
                DebugConsole.WriteLine("Access denied. Run as Admin.");

            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[ERROR] Tracking failed: {ex.Message}");
                DebugConsole.WriteException(ex, "Tracking Exception");
            }
            finally
            {
                _currentSession?.Dispose();
                _currentSession = null;
            }
        }

        /// <summary>
        /// Clean up any orphaned ETW sessions
        /// </summary>
        private void CleanupExistingEtwSessions()
        {
            try
            {
                var existingSession = TraceEventSession.GetActiveSession(ETW_SESSION_NAME);
                if (existingSession != null)
                {
                    DebugConsole.WriteWarning("Found existing ETW session, stopping it...");
                    existingSession.Stop();
                    existingSession.Dispose();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error cleaning up ETW session: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the current tracking session
        /// </summary>
        public void StopTracking()
        {
            _currentSession?.Stop();
        }

        /// <summary>
        /// Gets the files to upload after resolution
        /// </summary>
        public List<string> GetUploadList()
        {
            if (_currentSession != null)
            {
                return _currentSession.GetUploadList();
            }
            return _lastSessionUploadList ?? new List<string>();
        }

        private void DisplayResults(List<string> uploadFiles)
        {
            if (uploadFiles.Count == 0)
            {
                DebugConsole.WriteWarning("No files were tracked during this session.");
                return;
            }

            uploadFiles.Sort();

            DebugConsole.WriteList("Files to Upload:", uploadFiles);
            DebugConsole.WriteInfo($"Total files to upload: {uploadFiles.Count}");
        }

        private async Task RestartAsAdmin()
        {
            var adminHelper = new AdminPrivilegeHelper();
            adminHelper.RestartAsAdmin();
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Represents a single file tracking session
    /// </summary>
    public class TrackingSession : IDisposable
    {
        private readonly Game _game;
        // ETW session
        private TraceEventSession _etwSession;
        private bool _isTracking;
        private bool _isDisposed;

        // Process tracking
        private ProcessMonitor _processMonitor;
        private int _initialProcessId;

        // File collection
        private FileCollector _fileCollector;

        // Cancellation
        private CancellationTokenSource _cancellationTokenSource;

        // Background tasks
        private readonly List<Task> _backgroundTasks = new List<Task>();

        // Temp file resolution - NON-STATIC to avoid cross-session contamination
        private readonly HashSet<string> _trackedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _uploadCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _listLock = new object();

        // Common temp extensions
        private static readonly HashSet<string> TempExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".bak", ".backup", ".~", ".old", ".orig"
        };

        bool trackWrites = true;
        bool trackReads = false;
        public bool IsTracking => _isTracking;

        public TrackingSession(Game game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _fileCollector = new FileCollector(_game);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task<bool> Initialize()
        {
            // Clear previous results
            lock (_listLock)
            {
                _trackedFiles.Clear();
                _uploadCandidates.Clear();
            }

            // Detect game process
            string detectedExe;
            try
            {
                detectedExe = await ProcessMonitor.GetProcessFromDir(_game.InstallDirectory);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Couldn't detect a running game process in install directory: {ex.Message}");
                return false;
            }

            string processName = Path.GetFileName(detectedExe);
            var cleanName = processName.ToLower().Replace(".exe", "");
            var procs = Process.GetProcessesByName(cleanName);

            if (procs.Length == 0)
            {
                DebugConsole.WriteLine($"No process with name {cleanName} found.");
                return false;
            }

            _initialProcessId = procs[0].Id;
            DebugConsole.WriteLine($"Initial Process: {procs[0].ProcessName} (PID: {_initialProcessId})");

            // Initialize process monitor
            _processMonitor = new ProcessMonitor(_game.InstallDirectory);
            await _processMonitor.Initialize(_initialProcessId);

            return true;
        }

        public async Task StartTracking()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TrackingSession));

            try
            {
                _etwSession = new TraceEventSession(ETW_SESSION_NAME);
                _isTracking = true;

                DebugConsole.WriteLine("Starting ETW session...");

                // Enable ETW providers
                _etwSession.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.FileIO
                        | KernelTraceEventParser.Keywords.FileIOInit
                        | KernelTraceEventParser.Keywords.Process
                );

                // Setup event handlers
                SetupEventHandlers();

                // Start background tasks
                StartBackgroundProcessing();

                DebugConsole.WriteSuccess("ETW tracking started successfully");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to start ETW session: {ex.Message}");
                _isTracking = false;
                throw;
            }
        }

        private void SetupEventHandlers()
        {
            if (_etwSession?.Source == null)
            {
                DebugConsole.WriteError("ETW session source is null");
                return;
            }

            // Monitor new processes
            _etwSession.Source.Kernel.ProcessStart += data =>
            {
                try
                {
                    if (_isTracking && !_isDisposed)
                    {
                        _processMonitor?.HandleNewProcess(data.ProcessID);
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Error handling new process: {ex.Message}");
                }
            };

            // Track process exits
            _etwSession.Source.Kernel.ProcessStop += data =>
            {
                try
                {
                    if (_isTracking && !_isDisposed)
                    {
                        _processMonitor?.HandleProcessExit(data.ProcessID);
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Error handling process exit: {ex.Message}");
                }
            };

            // File write tracking
            if (trackWrites)
            {
                _etwSession.Source.Kernel.FileIOWrite += data =>
                {
                    try
                    {
                        if (_isTracking && !_isDisposed && _processMonitor != null)
                        {
                            if (_processMonitor.IsTracked(data.ProcessID))
                            {
                                HandleFileAccess(data.FileName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Silently ignore individual file tracking errors
                    }
                };
            }

            // File read tracking
            if (trackReads)
            {
                _etwSession.Source.Kernel.FileIORead += data =>
                {
                    try
                    {
                        if (_isTracking && !_isDisposed && _processMonitor != null)
                        {
                            if (_processMonitor.IsTracked(data.ProcessID))
                            {
                                HandleFileAccess(data.FileName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Silently ignore individual file tracking errors
                    }
                };
            }

            DebugConsole.WriteInfo($"Track writes: {trackWrites}");
            DebugConsole.WriteInfo($"Track reads: {trackReads}");
        }

        private void HandleFileAccess(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || _isDisposed)
                return;

            try
            {
                // Normalize path
                string normalizedPath = filePath.Replace('/', '\\');

                // Check if should be ignored
                if (_fileCollector.ShouldIgnore(normalizedPath))
                    return;

                // Add to file collector
                if (!_fileCollector.AddFile(normalizedPath))
                    return;

                // Simple solution: Track the file AND version without last extension
                lock (_listLock)
                {
                    // Always track the file we detected
                    if (!_trackedFiles.Contains(normalizedPath))
                    {
                        _trackedFiles.Add(normalizedPath);
                        _uploadCandidates.Add(normalizedPath);
                        DebugConsole.WriteLine($"Tracked: {normalizedPath}");
                    }

                    // If file has ANY extension, also track version without that extension
                    string extension = Path.GetExtension(normalizedPath);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        string fileWithoutExt = Path.Combine(
                            Path.GetDirectoryName(normalizedPath) ?? "",
                            Path.GetFileNameWithoutExtension(normalizedPath)
                        );

                        // Only add if it still has an extension (was double extension)
                        if (!string.IsNullOrEmpty(Path.GetExtension(fileWithoutExt)))
                        {
                            if (!_uploadCandidates.Contains(fileWithoutExt))
                            {
                                _uploadCandidates.Add(fileWithoutExt);
                                DebugConsole.WriteLine($"  -> Also tracking: {fileWithoutExt}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error handling file access for {filePath}: {ex.Message}");
            }
        }

        private void StartBackgroundProcessing()
        {
            // ETW processing task
            var etwTask = Task.Run(() =>
            {
                try
                {
                    DebugConsole.WriteLine("ETW session processing started.");
                    _etwSession.Source.Process();
                    DebugConsole.WriteLine("ETW session processing ended.");
                }
                catch (Exception ex)
                {
                    if (!_isDisposed)
                    {
                        DebugConsole.WriteLine($"[ERROR] ETW Session: {ex.Message}");
                    }
                }
            }, _cancellationTokenSource.Token);

            _backgroundTasks.Add(etwTask);

            // Directory monitoring task
            var monitorTask = Task.Run(async () =>
            {
                try
                {
                    await _processMonitor.StartPeriodicScan(
                        DIRECTORY_SCAN_INTERVAL_MS,
                        _cancellationTokenSource.Token
                    );
                }
                catch (Exception ex)
                {
                    if (!_isDisposed)
                    {
                        DebugConsole.WriteWarning($"Directory scan error: {ex.Message}");
                    }
                }
            }, _cancellationTokenSource.Token);

            _backgroundTasks.Add(monitorTask);
        }

        public async Task WaitForCompletion()
        {
            try
            {
                // Wait for main process to exit
                var mainProcess = Process.GetProcessById(_initialProcessId);

                if (mainProcess.HasExited)
                {
                    DebugConsole.WriteLine("Process already exited.");
                }
                else
                {
                    DebugConsole.WriteLine("Waiting for process to exit...");
                    await WaitForProcessExitAsync(mainProcess, _cancellationTokenSource.Token);
                    DebugConsole.WriteLine("Main game process exited.");
                }

                // Grace period for filesystem to settle (renames, final writes)
                DebugConsole.WriteLine($"Waiting {PROCESS_SHUTDOWN_GRACE_PERIOD_MS}ms for filesystem to settle...");
                await Task.Delay(PROCESS_SHUTDOWN_GRACE_PERIOD_MS, _cancellationTokenSource.Token);
            }
            catch (ArgumentException)
            {
                DebugConsole.WriteLine("Process already exited (ArgumentException).");
            }
            catch (OperationCanceledException)
            {
                DebugConsole.WriteLine("Tracking was cancelled.");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error during wait: {ex.Message}");
            }
            finally
            {
                Stop();
            }

            DebugConsole.WriteLine("Tracking session complete.");
        }

        public void Stop()
        {
            if (!_isTracking || _isDisposed)
                return;

            DebugConsole.WriteLine("Stopping tracking session...");

            _isTracking = false;

            // Cancel all background tasks
            try
            {
                _cancellationTokenSource?.Cancel();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error cancelling tasks: {ex.Message}");
            }

            // Stop ETW session
            try
            {
                _etwSession?.Stop();
                DebugConsole.WriteLine("ETW session stopped.");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error stopping ETW session: {ex.Message}");
            }

            DebugConsole.WriteLine("Session stopped.");
        }

        /// <summary>
        /// Gets the final upload list - only files that exist after game closes
        /// </summary>
        public List<string> GetUploadList()
        {
            var finalList = new List<string>();

            lock (_listLock)
            {
                DebugConsole.WriteLine($"Resolving upload list from {_uploadCandidates.Count} candidates...");

                foreach (var candidate in _uploadCandidates)
                {
                    try
                    {
                        if (File.Exists(candidate))
                        {
                            finalList.Add(candidate);
                        }
                        else
                        {
                            DebugConsole.WriteLine($"  Skipped: {Path.GetFileName(candidate)} (no longer exists)");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Error checking {candidate}: {ex.Message}");
                    }
                }

                DebugConsole.WriteInfo($"Final upload list: {finalList.Count} files");
            }

            return finalList;
        }

        private static async Task WaitForProcessExitAsync(Process process, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            void OnExited(object s, EventArgs e) => tcs.TrySetResult(true);

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnExited;

                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    if (!process.HasExited)
                    {
                        await tcs.Task;
                    }
                }
            }
            finally
            {
                process.Exited -= OnExited;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            Stop();

            // Wait for background tasks to complete
            try
            {
                if (_backgroundTasks.Count > 0)
                {
                    DebugConsole.WriteLine("Waiting for background tasks to complete...");
                    Task.WaitAll(_backgroundTasks.ToArray(), TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error waiting for background tasks: {ex.Message}");
            }

            // Dispose resources
            try
            {
                _cancellationTokenSource?.Dispose();
            }
            catch { }

            try
            {
                _etwSession?.Dispose();
            }
            catch { }

            try
            {
                _processMonitor?.Dispose();
            }
            catch { }
        }

        private const string ETW_SESSION_NAME = "SaveTrackerSession";
        private const int DIRECTORY_SCAN_INTERVAL_MS = 5 * 60 * 1000;
        private const int PROCESS_SHUTDOWN_GRACE_PERIOD_MS = 5000;
    }
}