using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if WINDOWS
using System.Management;
#endif
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public class ProcessMonitor : IDisposable
    {
        private readonly string _installDirectory;
        // Replace ConcurrentHashSet with ConcurrentDictionary
        private readonly ConcurrentDictionary<int, byte> _trackedProcessIds = new();
        private readonly object _lock = new object();


        public ProcessMonitor(string installDirectory)
        {
            _installDirectory = installDirectory ?? throw new ArgumentNullException(nameof(installDirectory));
        }

        /// <summary>
        /// Initializes the monitor with the initial process and scans directory
        /// </summary>
        public Task Initialize(int initialProcessId)
        {
            _trackedProcessIds.TryAdd(initialProcessId, 0);
            DebugConsole.WriteLine($"Initialized process monitor with PID: {initialProcessId}");

            // Also scan for all other processes in the directory
            var directoryProcesses = GetProcessesFromDirectory(_installDirectory);
            foreach (var pid in directoryProcesses)
            {
                _trackedProcessIds.TryAdd(pid, 0);
                DebugConsole.WriteLine($"Tracking directory process: PID {pid}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Explicitly adds a PID to tracking, bypassing parent/directory checks.
        /// Used for external tools like Steam.
        /// </summary>
        public void AddExplicitlyTrackedPid(int processId)
        {
            lock (_lock)
            {
                if (_trackedProcessIds.TryAdd(processId, 0))
                {
                    //DebugConsole.WriteLine($"[ProcessMonitor] Explicitly tracking external PID {processId}");
                }
            }
        }

        /// <summary>
        /// Handles a new process being started
        /// </summary>
        /// <summary>
        /// Handles a new process being started
        /// </summary>
        public void HandleNewProcess(int processId, int parentProcessId = -1)
        {
            try
            {
                // 1. Parent Inheritance Logic (Most Robust)
                if (parentProcessId != -1 && IsTracked(parentProcessId))
                {
                    lock (_lock)
                    {
                        if (_trackedProcessIds.TryAdd(processId, 0))
                        {
                            DebugConsole.WriteSuccess($"Tracking child process: PID {processId} (Parent: {parentProcessId})");
                            return; // Successfully tracked via inheritance
                        }
                    }
                }

                // 2. Directory Logic (Fallback for detached processes)
                string exePath = GetProcessExecutablePath(processId);

                if (!string.IsNullOrEmpty(exePath) &&
                    exePath.StartsWith(_installDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    lock (_lock)
                    {
                        if (_trackedProcessIds.TryAdd(processId, 0))
                        {
                            DebugConsole.WriteLine($"Now tracking directory process: {Path.GetFileName(exePath)} (PID: {processId})");
                        }
                    }
                }
                else
                {
                    // Debug logging to diagnose why directory check failed if we expected it to pass
                    // Only log if we have a path but it didn't match (to avoid noise for system processes)
                    if (!string.IsNullOrEmpty(exePath) && exePath.Contains("SaveTracker", StringComparison.OrdinalIgnoreCase) == false)
                    {
                        // Using WriteLine instead of WriteDebug to ensure visibility
                        // DebugConsole.WriteLine($"[ProcessMonitor] Ignored PID {processId} Path: '{exePath}' (Parent {parentProcessId} IsTracked: {IsTracked(parentProcessId)})");
                    }
                }
            }
            catch (ArgumentException)
            {
                // Process already exited
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error handling new process {processId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a process exit
        /// </summary>
        public void HandleProcessExit(int processId)
        {
            lock (_lock)
            {
                if (_trackedProcessIds.TryRemove(processId, out _))
                {
                    DebugConsole.WriteLine($"Process {processId} exited and removed from tracking");
                }
            }
        }

        /// <summary>
        /// Checks if a process ID is being tracked
        /// </summary>
        public bool IsTracked(int processId)
        {
            lock (_lock)
            {
                return _trackedProcessIds.ContainsKey(processId);
            }
        }

        /// <summary>
        /// Periodically scans the install directory for new processes
        /// </summary>
        public async Task StartPeriodicScan(int intervalMs, CancellationToken cancellationToken)
        {
            DebugConsole.WriteLine($"Starting periodic directory scan every {intervalMs}ms");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(intervalMs, cancellationToken);
                    ScanForProcessesInDirectory();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Error during periodic scan: {ex.Message}");
                }
            }

            DebugConsole.WriteLine("Periodic scan stopped");
        }

        /// <summary>
        /// Scans for processes in the install directory using WMI.
        /// Public so it can be called manually (e.g. after ETW start) to catch startup races.
        /// </summary>
        public void ScanForProcessesInDirectory()
        {
            try
            {
                var processIds = GetProcessesFromDirectory(_installDirectory);

                lock (_lock)
                {
                    foreach (var pid in processIds)
                    {
                        if (_trackedProcessIds.TryAdd(pid, 0))
                        {
                            DebugConsole.WriteLine($"Found related process during scan: PID {pid}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error scanning for processes: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all currently running processes from the specified directory using WMI
        /// </summary>
        /// <summary>
        /// Gets all currently running processes from the specified directory
        /// </summary>
        private List<int> GetProcessesFromDirectory(string directory)
        {
            var processIds = new List<int>();

#if WINDOWS
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
                            }
                        }
                        catch
                        {
                            // Skip processes we can't access
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[ERROR] Getting directory processes (WMI): {ex.Message}");
            }
#else
            try
            {
                var processes = System.Diagnostics.Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.MainModule != null &&
                            !string.IsNullOrEmpty(proc.MainModule.FileName) &&
                            proc.MainModule.FileName.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                        {
                            processIds.Add(proc.Id);
                        }
                    }
                    catch
                    {
                        // Ignore access denied
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[ERROR] Getting directory processes (native): {ex.Message}");
            }
#endif

            return processIds;
        }

        /// <summary>
        /// Explicitly scans for existing child processes of a given PID.
        /// This fixes race conditions where a child process starts before ETW tracing is fully active.
        /// </summary>
        public void ScanForChildren(int parentPid)
        {
#if WINDOWS
            try
            {
                // Query path must be correct: Win32_Process where ParentProcessId = X
                string query = $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentPid}";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            int childPid = Convert.ToInt32(obj["ProcessId"]);
                            DebugConsole.WriteInfo($"[ProcessMonitor] Found existing child process: PID {childPid} (Parent: {parentPid})");
                            HandleNewProcess(childPid, parentPid);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error scanning for children of PID {parentPid}: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Gets executable path for a specific process ID using WMI
        /// </summary>
        /// <summary>
        /// Gets executable path for a specific process ID
        /// </summary>
        private string GetProcessExecutablePath(int processId)
        {
#if WINDOWS
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
                DebugConsole.WriteWarning($"Error getting executable path for PID {processId}: {ex.Message}");
            }
            return "";
#else
            try
            {
                using (var proc = System.Diagnostics.Process.GetProcessById(processId))
                {
                    return proc.MainModule?.FileName ?? "";
                }
            }
            catch
            {
                return "";
            }
#endif
        }

        /// <summary>
        /// Static helper to detect a running process executable from a directory.
        /// Instead of returning any .exe from the folder, this scans currently running
        /// processes via WMI and returns the ExecutablePath of a process whose path
        /// starts with the provided install directory. This avoids selecting unrelated
        /// executables that reside in the folder but aren't actually running.
        /// </summary>
        public static async Task<string> GetProcessFromDir(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Directory not found: {directory}");

            // Normalize directory for comparison
            string normalizedDir = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Poll for a short period to allow the game process to spin up
            var timeout = TimeSpan.FromSeconds(10);
            var pollInterval = TimeSpan.FromMilliseconds(400);
            var start = DateTime.UtcNow;

            while (DateTime.UtcNow - start < timeout)
            {
#if WINDOWS
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
                                if (!string.IsNullOrEmpty(execPath))
                                {
                                    // Some processes may have forward slashes, normalize
                                    var normalizedExec = execPath.Replace('/', '\\');
                                    if (normalizedExec.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return normalizedExec;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore processes we can't read
                                continue;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Error while scanning for running process in directory '{normalizedDir}': {ex.Message}");
                }
#else
                try
                {
                    var processes = System.Diagnostics.Process.GetProcesses();
                    foreach (var proc in processes)
                    {
                        try
                        {
                            if (proc.MainModule != null && !string.IsNullOrEmpty(proc.MainModule.FileName))
                            {
                                if (proc.MainModule.FileName.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    return proc.MainModule.FileName;
                                }
                            }
                        }
                        catch { continue; }
                        finally { proc.Dispose(); }
                    }
                }
                catch { }
#endif

                // If not found yet, wait a bit and retry
                await Task.Delay(pollInterval);
            }

            // Fallback behavior: if no running process was found, keep previous UX by
            // providing a meaningful error rather than returning the first exe arbitrarily.
            throw new InvalidOperationException("No running process found in the specified directory within the timeout window.");
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _trackedProcessIds.Clear();
            }
        }
    }
}