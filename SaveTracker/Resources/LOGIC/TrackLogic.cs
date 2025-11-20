using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using SaveTracker.Resources.HELPERS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker.Resources.LOGIC
{
  

    public class TrackLogic
    {
        private static TraceEventSession _session;
        private static bool _isTracking;
        private static List<string> _saveFilesList = new List<string>();
        private static readonly string UserProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile
        );


        // Settings
        bool canTrack = true;
        bool trackWrites = true;
        bool trackReads = false;

        // Events for UI notification
        public event Action<string> OnMessage;
        public event Action<string> OnError;

        public static List<string> NormalGetList()
        {
            lock (_saveFilesList)
            {
                return new List<string>(_saveFilesList);
            }
        }

        public static List<string> FormattedGetList()
        {
            lock (_saveFilesList)
            {
                return PathExpander.ContractPaths(_saveFilesList);
            }
        }

        private static bool IsObviousSystemFile(string fileName)
        {
            if (fileName.StartsWith("~") || fileName.StartsWith("."))
                return true;
            return false;
        }

        private static readonly HashSet<string> LoggedFiles = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        private static bool ShouldIgnoreFile(string filePath, Game game = null)
        {
            if (string.IsNullOrEmpty(filePath))
                return true;

            string normalizedPath = filePath.Replace('/', '\\');
            bool shouldLog = LoggedFiles.Add(normalizedPath);

           /* // 1. Game-specific blacklist check
            if (game?.Blacklist != null)
            {
                foreach (var blacklistItem in game.Blacklist)
                {
                    string normalizedBlacklist = blacklistItem.Path.Replace('/', '\\');

                    if (string.Equals(normalizedPath, normalizedBlacklist, StringComparison.OrdinalIgnoreCase))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (Game Blacklist - Exact): {filePath}");
                        return true;
                    }

                    if (normalizedPath.StartsWith(normalizedBlacklist + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (Game Blacklist - Directory): {filePath}");
                        return true;
                    }
                }
            }
           */
            try
            {
                // 2. Quick directory check
                foreach (var ignoredDir in Ignorlist.IgnoredDirectoriesSet)
                {
                    if (normalizedPath.StartsWith(ignoredDir + "\\", StringComparison.OrdinalIgnoreCase) ||
                        normalizedPath.Equals(ignoredDir, StringComparison.OrdinalIgnoreCase))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (System Directory): {filePath}");
                        return true;
                    }
                }

                // 3. File name and extension checks
                string fileName = Path.GetFileName(normalizedPath);
                string fileExtension = Path.GetExtension(normalizedPath);

                if (Ignorlist.IgnoredFileNames.Contains(fileName))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (Ignored Filename): {filePath}");
                    return true;
                }

                if (Ignorlist.IgnoredExtensions.Contains(fileExtension))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (Ignored Extension): {filePath}");
                    return true;
                }

                // 4. Keyword filtering
                string lowerPath = normalizedPath.ToLower();
                string lowerFileName = fileName.ToLower();

                foreach (var keyword in Ignorlist.IgnoredKeywords)
                {
                    if (lowerFileName.Contains(keyword))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (Keyword in Filename '{keyword}'): {filePath}");
                        return true;
                    }

                    if (lowerPath.Contains($"\\{keyword}\\"))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (Keyword in Path '{keyword}'): {filePath}");
                        return true;
                    }
                }

                // 5. System file heuristics
                if (IsObviousSystemFile(fileName))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (System File Heuristic): {filePath}");
                    return true;
                }

                LoggedFiles.Remove(normalizedPath);
                return false;
            }
            catch (Exception ex)
            {
                if (shouldLog)
                    DebugConsole.WriteWarning($"Skipped (Path Processing Error): {filePath} - {ex.Message}");
                return false;
            }
        }

        public async Task Track(Game gameObject)
        {
            lock (_saveFilesList)
            {
                _saveFilesList = new List<string>();
            }

            DebugConsole.WriteLine("== SaveTracker DebugConsole Started ==");

            if (_isTracking)
            {
                DebugConsole.WriteLine("Already tracking.");
                OnMessage?.Invoke("Already tracking a process. Stop current tracking first.");
                return;
            }

            var detectedExe = await ProcessMonitor.GetProcessFromDir(gameObject.InstallDirectory);
            string processName = Path.GetFileName(detectedExe);
            var cleanName = processName.ToLower().Replace(".exe", "");
            var procs = Process.GetProcessesByName(cleanName);

            if (procs.Length == 0)
            {
                DebugConsole.WriteLine($"No process with name {cleanName} found.");
                OnMessage?.Invoke($"No process with name {cleanName} found.");
                return;
            }

            int initialPid = procs[0].Id;
            DebugConsole.WriteLine($"Initial Process: {procs[0].ProcessName} (PID: {initialPid})");

            var directoryProcesses = GetProcessesFromDirectory(gameObject.InstallDirectory);
            var trackedPids = new ConcurrentDictionary<int, byte>();

            foreach (var pid in directoryProcesses)
            {
                trackedPids.TryAdd(pid, 0);
                DebugConsole.WriteLine($"Tracking directory process: PID {pid}");
            }

            try
            {
                _session = new TraceEventSession("SaveTrackerSession");
                _isTracking = true;

                _session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.FileIO
                        | KernelTraceEventParser.Keywords.FileIOInit
                        | KernelTraceEventParser.Keywords.Process
                );

                // Monitor new processes
                _session.Source.Kernel.ProcessStart += data =>
                {
                    try
                    {
                        string exePath = GetProcessExecutablePath(data.ProcessID);

                        if (!string.IsNullOrEmpty(exePath) &&
                            exePath.StartsWith(gameObject.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            if (trackedPids.TryAdd(data.ProcessID, 0))
                            {
                                DebugConsole.WriteLine($"New directory process started: {Path.GetFileName(exePath)} (PID: {data.ProcessID})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Process might have already exited
                    }
                };

                // Clean up when processes exit
                _session.Source.Kernel.ProcessStop += data =>
                {
                    if (trackedPids.ContainsKey(data.ProcessID))
                    {
                        DebugConsole.WriteLine($"Directory process exited: PID {data.ProcessID}");
                    }
                };

                void HandleFileWrite(string filePath)
                {
                    if (string.IsNullOrEmpty(filePath))
                        return;

                    string normalizedPath = filePath.Replace('/', '\\');

                    if (ShouldIgnoreFile(normalizedPath, gameObject))
                        return;

                    lock (_saveFilesList)
                    {
                        string portablePath = normalizedPath;

                        if (!_saveFilesList.Contains(portablePath))
                        {
                            _saveFilesList.Add(portablePath);
                            DebugConsole.WriteLine($"Detected: {portablePath}");
                        }
                    }
                }

                if (trackWrites)
                {
                    _session.Source.Kernel.FileIOWrite += data =>
                    {
                        if (trackedPids.ContainsKey(data.ProcessID))
                        {
                            HandleFileWrite(data.FileName);
                        }
                    };
                }

                DebugConsole.WriteWarning("Track writes: " + trackWrites);
                DebugConsole.WriteWarning("Track reads: " + trackReads);

                if (trackReads)
                {
                    _session.Source.Kernel.FileIORead += data =>
                    {
                        if (trackedPids.ContainsKey(data.ProcessID))
                        {
                            HandleFileWrite(data.FileName);
                        }
                    };
                }

                await Task.Run(() =>
                {
                    var gameProc = Process.GetProcessById(initialPid);

                    // Start ETW processing
                    Task.Run(() =>
                    {
                        try
                        {
                            DebugConsole.WriteLine("ETW session processing started.");
                            _session.Source.Process();
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteLine($"[ERROR] ETW Session: {ex.Message}");
                        }
                    });

                    // Periodically rescan directory
                    Task.Run(async () =>
                    {
                        while (!gameProc.HasExited && _isTracking)
                        {
                            try
                            {
                                var currentDirectoryProcesses = GetProcessesFromDirectory(gameObject.InstallDirectory);
                                foreach (var pid in currentDirectoryProcesses)
                                {
                                    if (trackedPids.TryAdd(pid, 0))
                                    {
                                        DebugConsole.WriteLine($"Found new directory process: PID {pid}");
                                    }
                                }

                                await Task.Delay(5 * 60 * 1000); // Check every 5 minutes
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteLine($"[ERROR] Directory monitoring: {ex.Message}");
                            }
                        }
                    });

                    // Wait for main process
                    gameProc.WaitForExit();
                    DebugConsole.WriteLine("Main game process exited, waiting for other directory processes...");

                    Thread.Sleep(3000);

                    StopTracking();

                    lock (_saveFilesList)
                    {
                        var trackedFiles = new List<string>(_saveFilesList);
                        trackedFiles.Sort();
                        DebugConsole.WriteList("List Of tracked Files:", trackedFiles);
                    }

                    _isTracking = false;
                    DebugConsole.WriteLine("Tracking session complete.");
                });
            }
            catch (UnauthorizedAccessException)
            {
                _isTracking = false;
                DebugConsole.WriteLine("Access denied. Run as Admin.");
                OnError?.Invoke("Administrator privileges required. Please restart as administrator.");
                await RestartAsAdmin();
            }
            catch (Exception e)
            {
                _isTracking = false;
                DebugConsole.WriteLine($"[ERROR] Setup failed: {e.Message}");
                OnError?.Invoke($"Tracking failed: {e.Message}");
            }
        }

        private List<int> GetProcessesFromDirectory(string directory)
        {
            var processIds = new List<int>();

            try
            {
                string query = "SELECT ProcessId, ExecutablePath FROM Win32_Process";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            string execPath = obj["ExecutablePath"]?.ToString();
                            if (!string.IsNullOrEmpty(execPath) &&
                                execPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                            {
                                int processId = Convert.ToInt32(obj["ProcessId"]);
                                processIds.Add(processId);
                                DebugConsole.WriteLine($"Found directory process: {Path.GetFileName(execPath)} (PID: {processId})");
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[ERROR] Getting directory processes: {ex.Message}");
            }

            return processIds;
        }

        private string GetProcessExecutablePath(int processId)
        {
            try
            {
                string query = $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["ExecutablePath"]?.ToString() ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[WARNING] Error getting executable path for PID {processId}: {ex.Message}");
            }

            return "";
        }

        public Task<bool> IsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return Task.FromResult(principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
        }

        public async Task RestartAsAdmin()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                string executablePath = currentProcess.MainModule?.FileName;

                if (string.IsNullOrEmpty(executablePath))
                {
                    DebugConsole.WriteError("Executable path not found.");
                    OnError?.Invoke("Executable path not found.");
                    return;
                }

                DebugConsole.WriteInfo("Starting elevated process...");

                var processInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                    Arguments = "--restart"
                };

                var newProcess = Process.Start(processInfo);

                if (newProcess == null)
                {
                    throw new InvalidOperationException("Failed to start elevated process");
                }

                DebugConsole.WriteInfo("Elevated process started successfully.");

                await Task.Delay(1000);
                Environment.Exit(0);
            }
            catch (System.ComponentModel.Win32Exception win32Ex) when (win32Ex.NativeErrorCode == 1223)
            {
                DebugConsole.WriteWarning("User cancelled elevation request.");
                OnMessage?.Invoke("Administrator privileges are required.");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error restarting with elevated permissions: {ex.Message}");
                OnError?.Invoke($"Restart failed: {ex.Message}");
            }
        }

        public void StopTracking()
        {
            if (_session != null && _isTracking)
            {
                DebugConsole.WriteLine("Stopping ETW session...");
                _session.Stop();
                _session.Dispose();
                _session = null;
                _isTracking = false;
                DebugConsole.WriteLine("Session stopped.");
            }
        }
    }
}