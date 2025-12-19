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
        private const int DIRECTORY_SCAN_INTERVAL_MS = 30 * 1000; // 30 seconds - Faster scan to catch game process after launcher
        private const int PROCESS_SHUTDOWN_GRACE_PERIOD_MS = 5000; // 5 seconds - wait for filesystem to settle
        private const string ETW_SESSION_NAME = "SaveTrackerSession";

        // Settings
        bool canTrack = true;
        bool trackWrites = true;
        bool trackReads = false;
        // Session state
        private TrackingSession _currentSession;
        private List<string> _lastSessionUploadList;
        private static readonly SemaphoreSlim _trackingLock = new SemaphoreSlim(1, 1);

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
            if (!await _trackingLock.WaitAsync(0))
            {
                DebugConsole.WriteWarning("Tracking request ignored: Another tracking session is initializing or active.");
                return;
            }

            try
            {
                // Check if already tracking (double check inside lock, though sempahore handles it)
                if (_currentSession != null && _currentSession.IsTracking)
                {
                    DebugConsole.WriteLine("Already tracking.");
                    _trackingLock.Release();
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
            finally
            {
                _trackingLock.Release();
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

        // Filtering & Limits
        private FilePathFilter _pathFilter;
        private readonly HashSet<int> _steamPids = new HashSet<int>();
        private const int MAX_TRACKED_FILES = 500;
        private const long MAX_TOTAL_SIZE_BYTES = 100 * 1024 * 1024; // 100 MB
        private long _currentTotalSizeBytes = 0;


        bool trackWrites = true;
        bool trackReads = false;
        public bool IsTracking => _isTracking;

        public TrackingSession(Game game, bool probeForPrefix = false)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _probeForPrefix = probeForPrefix;
            _fileCollector = new FileCollector(_game);
            _cancellationTokenSource = new CancellationTokenSource();
            _pathFilter = new FilePathFilter(_game.InstallDirectory);
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

            // Critical: Scan for children immediately IN StartTracking.
            // Moving it here caused a race condition where we scanned BEFORE ETW was ready.
            // _processMonitor.ScanForChildren(_initialProcessId);

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

                    // CRITICAL: Give child processes a moment to spawn
                    // Many games launch child processes within 100-500ms
                    await Task.Delay(250);

                    // STEP 1: Scan for direct children first (most accurate)
                    DebugConsole.WriteInfo($"Scanning for child processes of PID {_initialProcessId}...");
                    _processMonitor.ScanForChildren(_initialProcessId);

                    // STEP 2: Scan entire directory for any related processes (catches detached processes)
                    DebugConsole.WriteInfo("Scanning for all processes in install directory...");
                    _processMonitor.ScanForProcessesInDirectory();

                    // STEP 3: Do another child scan to catch late spawners
                    await Task.Delay(100);
                    DebugConsole.WriteInfo($"Final child process scan for PID {_initialProcessId}...");
                    _processMonitor.ScanForChildren(_initialProcessId);
                }

                // Load previously tracked files (checksums)
                try
                {
                    DebugConsole.WriteInfo("Loading previously tracked files from checksums...");
                    var checksumService = new SaveTracker.Resources.Logic.RecloneManagement.ChecksumService();
                    var checksumData = await checksumService.LoadChecksumData(_game.InstallDirectory);

                    if (checksumData != null && checksumData.Files != null)
                    {
                        int restoredCount = 0;
                        foreach (var fileRecord in checksumData.Files)
                        {
                            // Reconstruct absolute path
                            string fullPath = SaveTracker.Resources.Logic.RecloneManagement.RcloneFileOperations.ExpandStoredPath(fileRecord.Key, _game.InstallDirectory);

                            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                            {
                                HandleFileAccess(fullPath); // Thread-safe adds to _trackedFiles
                                restoredCount++;
                            }
                        }
                        DebugConsole.WriteSuccess($"Restored {restoredCount} previously tracked files.");
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Failed to load previous files: {ex.Message}");
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
                        // DEBUG: Log every process start to debug detection logic
                        // Only log if potentially relevant (parent is tracked, or related to our tracking)
                        bool parentTracked = _processMonitor != null && _processMonitor.IsTracked(data.ParentID);
                        if (parentTracked)
                        {
                            DebugConsole.WriteDebug($"[ETW-PROC] Process Start: {data.ProcessName} ({data.ProcessID}) - Parent: {data.ParentID} (TRACKED)");
                        }

                        _processMonitor?.HandleNewProcess(data.ProcessID, data.ParentID);
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
                // Steam Integration: explicit tracking of Steam process
                // User Request: Always track Steam (with filter), but DO NOT add to ProcessMonitor to avoid tracking its children (browser, overlay etc).
                try
                {
                    var steamProcs = System.Diagnostics.Process.GetProcessesByName("steam");
                    foreach (var p in steamProcs)
                    {
                        _steamPids.Add(p.Id);
                        DebugConsole.WriteInfo($"[Steam Integration] Found Steam process (PID: {p.Id}) - Tracking writes to userdata only.");
                    }
                }
                catch { }

                _etwSession.Source.Kernel.FileIOWrite += data =>
                {
                    try
                    {
                        if (_isTracking && !_isDisposed)
                        {
                            // Check if it's the Steam process
                            if (_steamPids.Contains(data.ProcessID))
                            {
                                // Steam writes lots of junk. Only accept USERDATA/REMOTE.
                                // "remote" is the standard steam cloud folder structure: userdata/id/appid/remote/
                                // Use simple string check for performance
                                if (data.FileName.Contains("userdata", StringComparison.OrdinalIgnoreCase) &&
                                    data.FileName.Contains("remote", StringComparison.OrdinalIgnoreCase))
                                {
                                    HandleFileAccess(data.FileName);
                                }
                                return;
                            }

                            // Normal Game Process -> Check strictly against ProcessMonitor
                            if (_processMonitor != null && _processMonitor.IsTracked(data.ProcessID))
                            {
                                HandleFileAccess(data.FileName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Silence excessive logging for high-volume IO errors
                        // DebugConsole.WriteWarning($"ETW Write Error: {ex.Message}");
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
                        DebugConsole.WriteWarning($"ETW Read Error: {ex.Message}");
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
                // Normalize path - use forward slashes on Linux, backslashes on Windows
                string normalizedPath;
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    normalizedPath = filePath.Replace('/', '\\');
                }
                else
                {
                    normalizedPath = filePath.Replace('\\', '/');
                }

                // FIRST: Check against Strict Path Filter
                if (!_pathFilter.ShouldTrack(normalizedPath))
                {
                    // For Steam games, let's keep an eye on userdata blocks if debugging
                    if (normalizedPath.Contains("userdata", StringComparison.OrdinalIgnoreCase))
                    {
                        DebugConsole.WriteDebug($"[Filter] Blocked (PathFilter): {normalizedPath}");
                    }
                    return;
                }

                // CHECK: Emergency Brake - Count
                lock (_listLock)
                {
                    if (_trackedFiles.Count >= MAX_TRACKED_FILES)
                    {
                        if (_trackedFiles.Count == MAX_TRACKED_FILES) // Log only once
                        {
                            DebugConsole.WriteError($"[EMERGENCY STOP] Tracking paused - exceeded {MAX_TRACKED_FILES} files.");
                            // Notify user somehow? For now, just Log.
                            _trackedFiles.Add("TRACKING_LIMIT_EXCEEDED_PLACEHOLDER");
                        }
                        return;
                    }
                }

                // CHECK: Emergency Brake - Size
                try
                {
                    var fileInfo = new FileInfo(normalizedPath);
                    if (fileInfo.Exists)
                    {
                        // Basic check, might be updated later but good for initial filter
                        if (_currentTotalSizeBytes + fileInfo.Length > MAX_TOTAL_SIZE_BYTES)
                        {
                            DebugConsole.WriteError($"[EMERGENCY STOP] Tracking paused - exceeded {MAX_TOTAL_SIZE_BYTES / 1024 / 1024}MB.");
                            return;
                        }
                    }
                }
                catch { }

                // Check for double extensions and track both versions
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
                    // DebugConsole.WriteDebug($"[Filter] Blocked (IgnoreList): {normalizedPath}");

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
                        try
                        {
                            if (File.Exists(normalizedPath))
                                _currentTotalSizeBytes += new FileInfo(normalizedPath).Length;
                        }
                        catch { }
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
                DebugConsole.WriteLine("Analyzing session data..."); // Feedback for user
                await StopAsync();
            }

            DebugConsole.WriteLine("Tracking session complete.");
        }

        /// <summary>
        /// Asynchronously stops the tracking session and commits PlayTime updates.
        /// This method MUST be awaited to ensure PlayTime is persisted before Smart Sync reads it.
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isTracking || _isDisposed)
                return;

            DebugConsole.WriteLine("Stopping tracking session...");

            _isTracking = false;

            // CRITICAL: Await PlayTime commit to prevent race condition with Smart Sync
            try
            {
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
                        await UpdatePlayTimeAsync(sessionDuration);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to update PlayTime during stop: {ex.Message}");
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
        /// Synchronous version of Stop for backward compatibility (e.g., Dispose).
        /// Prefer StopAsync() when possible.
        /// </summary>
        public void Stop()
        {
            // For Dispose and cleanup paths that can't await
            StopAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates PlayTime in both GameUploadData config AND checksums.json.
        /// CRITICAL: Smart Sync reads from checksums.json, so both must be updated atomically.
        /// Also adds tracked files to checksum data so they appear in Smart Sync.
        /// </summary>
        private async Task UpdatePlayTimeAsync(TimeSpan sessionDuration)
        {
            try
            {
                // 1. Update GameUploadData.json (per-game config)
                var data = await ConfigManagement.GetGameData(_game);
                if (data == null)
                {
                    data = new SaveTracker.Resources.Logic.RecloneManagement.GameUploadData();
                }

                var oldPlayTime = data.PlayTime;
                data.PlayTime += sessionDuration;
                // REMOVED: Unsafe direct write that conflicts with ChecksumService
                // await ConfigManagement.SaveGameData(_game, data);

                // 2. CRITICAL: Update checksums.json in game install directory
                // This is what Smart Sync reads via SmartSyncService.GetLocalPlayTimeAsync()!
                // We perform the PlayTime update HERE safely using the service lock.
                var checksumService = new SaveTracker.Resources.Logic.RecloneManagement.ChecksumService();

                // Load existing data safely
                var checksumData = await checksumService.LoadChecksumData(_game.InstallDirectory);

                // Update PlayTime
                checksumData.PlayTime = data.PlayTime; // Set to total
                checksumData.LastUpdated = DateTime.Now;

                // 3. Add tracked files to checksum data so they appear in Smart Sync
                var uploadList = GetUploadList();
                DebugConsole.WriteInfo($"Processing {uploadList.Count} tracked file(s) for checksum data...");
                int filesAdded = 0;
                int filesSkipped = 0;
                foreach (var filePath in uploadList)
                {
                    try
                    {
                        if (!File.Exists(filePath))
                        {
                            DebugConsole.WriteDebug($"Skipping {Path.GetFileName(filePath)}: file no longer exists");
                            filesSkipped++;
                            continue;
                        }

                        // Contract the path to portable format
                        string portablePath = PathContractor.ContractPath(filePath, _game.InstallDirectory);

                        // Skip if already exists
                        if (checksumData.Files.ContainsKey(portablePath))
                        {
                            DebugConsole.WriteDebug($"Skipping {Path.GetFileName(filePath)}: already in checksum data");
                            filesSkipped++;
                            continue;
                        }

                        // Compute checksum and add to checksum data
                        string checksum = await checksumService.GetFileChecksum(filePath);
                        if (string.IsNullOrEmpty(checksum))
                        {
                            DebugConsole.WriteWarning($"Could not compute checksum for {Path.GetFileName(filePath)} - skipping");
                            filesSkipped++;
                            continue;
                        }

                        var fileInfo = new FileInfo(filePath);
                        checksumData.Files[portablePath] = new SaveTracker.Resources.Logic.RecloneManagement.FileChecksumRecord
                        {
                            Path = portablePath,
                            Checksum = checksum,
                            FileSize = fileInfo.Length,
                            LastUpload = DateTime.UtcNow,
                            LastWriteTime = fileInfo.LastWriteTimeUtc
                        };
                        DebugConsole.WriteDebug($"Added {Path.GetFileName(filePath)} to checksum data (path: {portablePath})");
                        filesAdded++;
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to add tracked file {Path.GetFileName(filePath)} to checksum: {ex.Message}");
                        filesSkipped++;
                    }
                }

                await checksumService.SaveChecksumData(checksumData, _game.InstallDirectory);

                DebugConsole.WriteSuccess(
                    $"PlayTime committed for '{_game.Name}': {oldPlayTime} + {sessionDuration} = {data.PlayTime} (saved to both config and checksums)"
                );
                if (filesAdded > 0)
                {
                    DebugConsole.WriteSuccess($"Added {filesAdded} tracked file(s) to checksum data for Smart Sync (skipped {filesSkipped})");
                }
                else if (uploadList.Count > 0)
                {
                    DebugConsole.WriteWarning($"No tracked files were added to checksum data (all {uploadList.Count} were skipped)");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "CRITICAL: Failed to update PlayTime - Smart Sync may use stale value!");
                // Re-throw to ensure caller knows PlayTime commit failed
                throw;
            }
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
        private const int PROCESS_SHUTDOWN_GRACE_PERIOD_MS = 1000;
    }
}