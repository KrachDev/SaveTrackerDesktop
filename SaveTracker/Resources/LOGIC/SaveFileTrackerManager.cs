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

        // Common temp extensions
        private static readonly HashSet<string> TempExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".bak", ".backup", ".~", ".old", ".orig"
        };

        // Settings
        bool canTrack = true;
        bool trackWrites = true;
        bool trackReads = false;
        // Session state
        private TrackingSession _currentSession;

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
                DisplayResults(uploadFiles);
            }
            catch (UnauthorizedAccessException)
            {
                DebugConsole.WriteLine("Access denied. Run as Admin.");
                await RestartAsAdmin();
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
            return TrackingSession.GetGlobalUploadList();
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
            // var adminHelper = new AdminPrivilegeHelper();
            // await adminHelper.RestartAsAdmin();
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

        // Process tracking
        private ProcessMonitor _processMonitor;
        private int _initialProcessId;

        // File collection
        private FileCollector _fileCollector;

        // Cancellation
        private CancellationTokenSource _cancellationTokenSource;

        // Background tasks
        private readonly List<Task> _backgroundTasks = new List<Task>();

        // Temp file resolution
        private static readonly HashSet<string> _trackedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _uploadCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _listLock = new object();

        // Common temp extensions
        private static readonly HashSet<string> TempExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".bak", ".backup", ".~", ".old", ".orig"
        };

        bool canTrack = true;
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
            _etwSession = new TraceEventSession(ETW_SESSION_NAME);
            _isTracking = true;

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
        }

        private void SetupEventHandlers()
        {
            // Monitor new processes
            _etwSession.Source.Kernel.ProcessStart += data =>
            {
                try
                {
                    _processMonitor.HandleNewProcess(data.ProcessID);
                }
                catch (Exception ex)
                {
                    // Process might have already exited
                }
            };

            // Track process exits
            _etwSession.Source.Kernel.ProcessStop += data =>
            {
                _processMonitor.HandleProcessExit(data.ProcessID);
            };

            // File write tracking
            if (trackWrites)
            {
                _etwSession.Source.Kernel.FileIOWrite += data =>
                {
                    if (_processMonitor.IsTracked(data.ProcessID))
                    {
                        HandleFileAccess(data.FileName);
                    }
                };
            }

            // File read tracking
            if (trackReads)
            {
                _etwSession.Source.Kernel.FileIORead += data =>
                {
                    if (_processMonitor.IsTracked(data.ProcessID))
                    {
                        HandleFileAccess(data.FileName);
                    }
                };
            }

            DebugConsole.WriteWarning($"Track writes: {trackWrites}");
            DebugConsole.WriteWarning($"Track reads: {trackReads}");
        }

        private void HandleFileAccess(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            // Normalize path
            string normalizedPath = filePath.Replace('/', '\\');

            // Check if should be ignored
            if (_fileCollector.ShouldIgnore(normalizedPath))
                return;

            // Add to file collector
            if (!_fileCollector.AddFile(normalizedPath))
                return;

            // Smart temp file resolution
            lock (_listLock)
            {
                // Track this file
                _trackedFiles.Add(normalizedPath);

                // Always add the file itself as upload candidate
                _uploadCandidates.Add(normalizedPath);

                // Check if it has double extension (temp file pattern)
                if (HasDoubleExtension(normalizedPath))
                {
                    // Find base file
                    string baseFile = GetBaseFileName(normalizedPath);
                    if (!string.IsNullOrEmpty(baseFile))
                    {
                        _uploadCandidates.Add(baseFile);
                        DebugConsole.WriteLine($"Detected: {normalizedPath}");
                        DebugConsole.WriteLine($"  -> Will also check: {baseFile}");
                    }
                }
                // If it's a base file, check for temp variants we've tracked
                else
                {
                    foreach (var tempExt in TempExtensions)
                    {
                        string tempVariant = normalizedPath + tempExt;
                        if (_trackedFiles.Contains(tempVariant))
                        {
                            _uploadCandidates.Add(tempVariant);
                            DebugConsole.WriteLine($"Detected: {normalizedPath}");
                            DebugConsole.WriteLine($"  -> Also uploading tracked temp: {tempVariant}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if file has double extension (e.g., save.dat.tmp)
        /// </summary>
        private bool HasDoubleExtension(string filePath)
        {
            return has2extensions(filePath);
        }

        /// <summary>
        /// Checks if filepath has 2 extensions (e.g., save.dat.tmp, config.ini.bak)
        /// </summary>
        public static bool has2extensions(string filepath)
        {
            if (string.IsNullOrEmpty(filepath))
                return false;

            // Get the last extension
            string extension = Path.GetExtension(filepath);
            if (string.IsNullOrEmpty(extension))
                return false;

            // Check if it's a known temp extension
            if (!TempExtensions.Contains(extension))
                return false;

            // Remove the extension and check if there's still an extension
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filepath);
            string remainingExtension = Path.GetExtension(fileNameWithoutExt);

            // If there's still an extension after removing the temp extension, it's a double extension
            return !string.IsNullOrEmpty(remainingExtension);
        }

        /// <summary>
        /// Gets base filename by removing temp extension
        /// save.dat.tmp -> save.dat
        /// </summary>
        private string GetBaseFileName(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(fileName);

            if (string.IsNullOrEmpty(extension))
                return null;

            // Remove temp extension
            string baseFileName = fileName.Substring(0, fileName.Length - extension.Length);

            // Check if still has an extension
            if (!baseFileName.Contains("."))
                return null;

            return Path.Combine(directory, baseFileName);
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
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteLine($"[ERROR] ETW Session: {ex.Message}");
                }
            }, _cancellationTokenSource.Token);

            _backgroundTasks.Add(etwTask);

            // Directory monitoring task
            var monitorTask = Task.Run(async () =>
            {
                await _processMonitor.StartPeriodicScan(
                    DIRECTORY_SCAN_INTERVAL_MS,
                    _cancellationTokenSource.Token
                );
            }, _cancellationTokenSource.Token);

            _backgroundTasks.Add(monitorTask);
        }

        public async Task WaitForCompletion()
        {
            try
            {
                // Wait for main process to exit
                var mainProcess = Process.GetProcessById(_initialProcessId);
                await WaitForProcessExitAsync(mainProcess, _cancellationTokenSource.Token);

                DebugConsole.WriteLine("Main game process exited, waiting for filesystem to settle...");

                // Grace period for filesystem to settle (renames, final writes)
                await Task.Delay(PROCESS_SHUTDOWN_GRACE_PERIOD_MS, _cancellationTokenSource.Token);
            }
            catch (ArgumentException)
            {
                DebugConsole.WriteLine("Process already exited.");
            }
            catch (OperationCanceledException)
            {
                DebugConsole.WriteLine("Tracking was cancelled.");
            }
            finally
            {
                Stop();
            }

            DebugConsole.WriteLine("Tracking session complete.");
        }

        public void Stop()
        {
            if (!_isTracking)
                return;

            DebugConsole.WriteLine("Stopping tracking session...");

            _isTracking = false;

            // Cancel all background tasks
            _cancellationTokenSource?.Cancel();

            // Stop ETW session
            try
            {
                _etwSession?.Stop();
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
            }

            return finalList;
        }

        public static List<string> GetGlobalUploadList()
        {
            var finalList = new List<string>();

            lock (_listLock)
            {
                foreach (var candidate in _uploadCandidates)
                {
                    try
                    {
                        if (File.Exists(candidate))
                        {
                            finalList.Add(candidate);
                        }
                    }
                    catch { }
                }
            }

            return finalList;
        }

        private static async Task WaitForProcessExitAsync(Process process, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(true);

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                if (!process.HasExited)
                {
                    await tcs.Task;
                }
            }
        }

        public void Dispose()
        {
            Stop();

            _cancellationTokenSource?.Dispose();
            _etwSession?.Dispose();
            _processMonitor?.Dispose();

            // Wait for background tasks to complete
            try
            {
                Task.WaitAll(_backgroundTasks.ToArray(), TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error waiting for background tasks: {ex.Message}");
            }
        }

        private const string ETW_SESSION_NAME = "SaveTrackerSession";
        private const int DIRECTORY_SCAN_INTERVAL_MS = 5 * 60 * 1000;
        private const int PROCESS_SHUTDOWN_GRACE_PERIOD_MS = 5000;
    }
}