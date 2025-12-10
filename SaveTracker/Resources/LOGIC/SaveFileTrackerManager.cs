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
using SaveTracker.Resources.SAVE_SYSTEM;

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
        public async Task Track(Game gameArg, bool probeForPrefix = false)
        {
            DebugConsole.WriteLine("== SaveTracker DebugConsole Started ==");
            var gamedata = ConfigManagement.GetGameData(gameArg);
            var StartDate = DateTime.Now;
            // Check if already tracking
            if (_currentSession != null && _currentSession.IsTracking)
            {
                DebugConsole.WriteLine("Already tracking.");
                return;
            }

            // Clean up any existing ETW sessions
            CleanupExistingEtwSessions();

            // Initialize new tracking session
            _currentSession = new TrackingSession(gameArg, probeForPrefix);

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
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return;
            }

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
        private readonly bool _probeForPrefix;
        // ETW session
        private TraceEventSession _etwSession;
        private bool _isTracking;
        private bool _isDisposed;
        private DateTime _trackingStartUtc = DateTime.MinValue;
        private bool _playtimeCommitted = false;
        private DateTime _processExitUtc = DateTime.MinValue;

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

        // Linux Tracking
        private string? _detectedPrefix;
        private FileSystemWatcher? _linuxInstallWatcher;
        private FileSystemWatcher? _linuxPrefixWatcher;


        bool trackWrites = true;
        bool trackReads = false;
        public bool IsTracking => _isTracking;

        public TrackingSession(Game game, bool probeForPrefix = false)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _probeForPrefix = probeForPrefix;
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

            // Use the new Tracking Engine
            var tracker = SaveTracker.Resources.LOGIC.Tracking.GameProcessTrackerFactory.Create();

            // Try to find the process via the new engine first (more robust on Linux)
            // But we need to know what we are looking for.
            // The existing logic used `ProcessMonitor.GetProcessFromDir`.

            // Enhanced Detection via new Engine - Try this FIRST without relying on ProcessMonitor scan
            SaveTracker.Resources.LOGIC.Tracking.ProcessInfo? processInfo = null;
            try
            {
                DebugConsole.WriteInfo($"Scanning for process matching game: {_game.Name} / {_game.ExecutablePath}");
                // We use the full executable path if available, or just the game name logic internal to tracker
                string searchTarget = _game.ExecutablePath;
                if (string.IsNullOrEmpty(searchTarget)) searchTarget = _game.InstallDirectory; // Fallback

                processInfo = await tracker.FindGameProcess(searchTarget);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Tracking engine scan error: {ex.Message}");
            }

            if (processInfo != null)
            {
                DebugConsole.WriteSuccess($"Tracking Engine identified process: {processInfo.Name} ({processInfo.Id})");
                _initialProcessId = processInfo.Id;

                // Detect Launcher
                string launcher = await tracker.DetectLauncher(processInfo);
                DebugConsole.WriteInfo($"Launcher detected: {launcher}");

                // Detect Prefix
                _detectedPrefix = await tracker.DetectGamePrefix(processInfo);
                if (!string.IsNullOrEmpty(_detectedPrefix))
                {
                    DebugConsole.WriteSuccess($"Game Prefix detected: {_detectedPrefix}");

                    // PERSIST PREFIX
                    try
                    {
                        var data = await ConfigManagement.GetGameData(_game) ?? new SaveTracker.Resources.Logic.RecloneManagement.GameUploadData();
                        if (data.DetectedPrefix != _detectedPrefix)
                        {
                            data.DetectedPrefix = _detectedPrefix;
                            await ConfigManagement.SaveGameData(_game, data);
                            DebugConsole.WriteInfo("Persisted detected prefix to game data.");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to persist prefix: {ex.Message}");
                    }

                    // PROBE MODE LOGIC
                    if (_probeForPrefix)
                    {
                        DebugConsole.WriteInfo("Probe Mode: Prefix found. Terminating game process...");
                        try
                        {
                            var proc = Process.GetProcessById(_initialProcessId);
                            proc.Kill();
                            DebugConsole.WriteSuccess("Game process terminated successfully.");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteWarning($"Failed to kill process during probe: {ex.Message}");
                        }
                        return false; // Stop tracking session here
                    }
                }
            }
            else
            {
                // Fallback to old directory scan logic ONLY if direct detection failed
                try
                {
                    DebugConsole.WriteInfo("Direct detection returned nothing, trying directory scan...");
                    string detectedExe = await ProcessMonitor.GetProcessFromDir(_game.InstallDirectory);

                    // If we found something in the dir, try to resolve its ID logic again (or just take it if we implemented that)
                    // But ProcessMonitor.GetProcessFromDir returns a path.
                    // We can try to feed that back to the tracker.
                    processInfo = await tracker.FindGameProcess(detectedExe);
                    if (processInfo != null)
                    {
                        _initialProcessId = processInfo.Id;
                        DebugConsole.WriteSuccess($"Directory scan found process: {processInfo.Name} ({processInfo.Id})");
                    }
                    else
                    {
                        DebugConsole.WriteWarning("Could not identify process ID even after directory scan.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Process detection failed: {ex.Message}");
                    return false;
                }
            }

            DebugConsole.WriteLine($"Initial Process: {_initialProcessId}");

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
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    DebugConsole.WriteLine("Starting ETW session...");
                    _etwSession = new TraceEventSession(ETW_SESSION_NAME);

                    // Enable ETW providers
                    _etwSession.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.FileIO
                            | KernelTraceEventParser.Keywords.FileIOInit
                            | KernelTraceEventParser.Keywords.Process
                    );

                    // Setup event handlers
                    SetupEventHandlers();
                }
                else
                {
                    DebugConsole.WriteLine("Non-Windows OS detected: ETW disabled. Using periodic directory scan only.");
                }

                _isTracking = true;
                _trackingStartUtc = DateTime.UtcNow;

                // Start background tasks
                StartBackgroundProcessing();

                SetupLinuxFileTracking();

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    DebugConsole.WriteSuccess("ETW tracking started successfully");
                }
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

                // FIRST: Check for double extensions and track both versions
                string extension = Path.GetExtension(normalizedPath);
                string companionFile = null;

                if (!string.IsNullOrEmpty(extension))
                {
                    string fileWithoutExt = Path.Combine(
                        Path.GetDirectoryName(normalizedPath) ?? "",
                        Path.GetFileNameWithoutExtension(normalizedPath)
                    );

                    // If removing extension still leaves an extension, it's a double extension
                    if (!string.IsNullOrEmpty(Path.GetExtension(fileWithoutExt)))
                    {
                        companionFile = fileWithoutExt;
                    }
                }

                // NOW check if should be ignored
                if (_fileCollector.ShouldIgnore(normalizedPath))
                {
                    // Even if THIS file is ignored, track the companion if it exists
                    if (companionFile != null && !_fileCollector.ShouldIgnore(companionFile))
                    {
                        lock (_listLock)
                        {
                            if (!_uploadCandidates.Contains(companionFile))
                            {
                                _uploadCandidates.Add(companionFile);
                                DebugConsole.WriteLine($"Tracked companion: {companionFile}");
                            }
                        }
                    }
                    return;
                }

                // Add to file collector
                if (!_fileCollector.AddFile(normalizedPath))
                    return;

                // Track the main file
                lock (_listLock)
                {
                    if (!_trackedFiles.Contains(normalizedPath))
                    {
                        _trackedFiles.Add(normalizedPath);
                        _uploadCandidates.Add(normalizedPath);
                        DebugConsole.WriteLine($"Tracked: {normalizedPath}");
                    }

                    // Also track companion file if it's a double extension
                    if (companionFile != null && !_uploadCandidates.Contains(companionFile))
                    {
                        _uploadCandidates.Add(companionFile);
                        DebugConsole.WriteLine($"  -> Also tracking: {companionFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error handling file access for {filePath}: {ex.Message}");
            }
        }

        // FileSystemWatcher for Linux
        private void SetupLinuxFileTracking()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return;

            try
            {
                // Watch Install Directoy
                if (Directory.Exists(_game.InstallDirectory))
                {
                    DebugConsole.WriteInfo($"Starting Install Dir Watcher: {_game.InstallDirectory}");
                    _linuxInstallWatcher = new FileSystemWatcher(_game.InstallDirectory);
                    _linuxInstallWatcher.IncludeSubdirectories = true;
                    _linuxInstallWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
                    _linuxInstallWatcher.Changed += OnLinuxFileEvent;
                    _linuxInstallWatcher.Created += OnLinuxFileEvent;
                    _linuxInstallWatcher.Renamed += OnLinuxFileEvent;
                    _linuxInstallWatcher.EnableRaisingEvents = true;
                }

                // If we detected a prefix, watch the users folder where saves usually live
                if (!string.IsNullOrEmpty(_detectedPrefix) && Directory.Exists(_detectedPrefix))
                {
                    string driveC = Path.Combine(_detectedPrefix, "drive_c");
                    // We can narrow it down to users to avoid noise from windows system files if possible, 
                    // but some games save in ProgramData or elsewhere. drive_c recursively is safely broad.

                    if (Directory.Exists(driveC))
                    {
                        DebugConsole.WriteInfo($"Starting Prefix Watcher: {driveC}");
                        _linuxPrefixWatcher = new FileSystemWatcher(driveC);
                        _linuxPrefixWatcher.IncludeSubdirectories = true;
                        _linuxPrefixWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
                        _linuxPrefixWatcher.Changed += OnLinuxFileEvent;
                        _linuxPrefixWatcher.Created += OnLinuxFileEvent;
                        _linuxPrefixWatcher.Renamed += OnLinuxFileEvent;
                        _linuxPrefixWatcher.EnableRaisingEvents = true;
                    }
                }

                if (_linuxInstallWatcher != null || _linuxPrefixWatcher != null)
                {
                    DebugConsole.WriteSuccess("Linux file tracking active via FileSystemWatcher");
                }
                else
                {
                    DebugConsole.WriteWarning("No valid directories found to watch for Linux tracking.");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to setup Linux file tracking: {ex.Message}");
            }
        }

        private void OnLinuxFileEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (_isTracking && !_isDisposed)
                {
                    HandleFileAccess(e.FullPath);
                }
            }
            catch { }
        }
        private void StartBackgroundProcessing()
        {
            // ETW processing task
            var etwTask = Task.Run(() =>
            {
                try
                {
                    if (_etwSession != null)
                    {
                        DebugConsole.WriteLine("ETW session processing started.");
                        _etwSession.Source.Process();
                        DebugConsole.WriteLine("ETW session processing ended.");
                    }
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
                    try
                    {
                        // Process.ExitTime is in local time
                        _processExitUtc = mainProcess.ExitTime.ToUniversalTime();
                    }
                    catch
                    {
                        _processExitUtc = DateTime.UtcNow;
                    }
                }
                else
                {
                    DebugConsole.WriteLine("Waiting for process to exit...");
                    await WaitForProcessExitAsync(mainProcess, _cancellationTokenSource.Token);
                    DebugConsole.WriteLine("Main game process exited.");
                    try
                    {
                        _processExitUtc = mainProcess.ExitTime.ToUniversalTime();
                    }
                    catch
                    {
                        _processExitUtc = DateTime.UtcNow;
                    }
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

            // Calculate and persist playtime (fire-and-forget to keep Stop() sync)
            if (!_playtimeCommitted && _trackingStartUtc != DateTime.MinValue)
            {
                _playtimeCommitted = true; // ensure we only add once per session
                var sessionEndUtc = _processExitUtc != DateTime.MinValue ? _processExitUtc : DateTime.UtcNow;
                var sessionDuration = sessionEndUtc - _trackingStartUtc;
                if (sessionDuration < TimeSpan.Zero)
                {
                    sessionDuration = TimeSpan.Zero;
                }
                if (sessionDuration > TimeSpan.Zero)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var data = await ConfigManagement.GetGameData(_game);
                            if (data == null)
                            {
                                // Avoid explicit type to not require additional using
                                data = new SaveTracker.Resources.Logic.RecloneManagement.GameUploadData();
                            }
                            data.PlayTime += sessionDuration;
                            await ConfigManagement.SaveGameData(_game, data);
                            DebugConsole.WriteLine($"PlayTime updated for '{_game.Name}': start={_trackingStartUtc:o}, end={sessionEndUtc:o}, +{sessionDuration}, total {data.PlayTime}");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteWarning($"Failed to update PlayTime: {ex.Message}");
                        }
                    });
                }
            }

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
                if (_etwSession != null)
                {
                    _etwSession.Stop();
                    DebugConsole.WriteLine("ETW session stopped.");
                }
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