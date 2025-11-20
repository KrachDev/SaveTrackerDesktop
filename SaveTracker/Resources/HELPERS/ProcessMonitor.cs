using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
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
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();


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
        /// Handles a new process being started
        /// </summary>
        public void HandleNewProcess(int processId)
        {
            try
            {
                string exePath = GetProcessExecutablePath(processId);

                if (!string.IsNullOrEmpty(exePath) &&
                    exePath.StartsWith(_installDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    lock (_lock)
                    {
                        if (_trackedProcessIds.TryAdd(processId, 0))
                        {
                            DebugConsole.WriteLine($"Now tracking child process: {Path.GetFileName(exePath)} (PID: {processId})");
                        }
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
                    ScanForNewProcesses();
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
        /// Scans for processes in the install directory using WMI
        /// </summary>
        private void ScanForNewProcesses()
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
                DebugConsole.WriteLine($"[ERROR] Getting directory processes: {ex.Message}");
            }

            return processIds;
        }

        /// <summary>
        /// Gets executable path for a specific process ID using WMI
        /// </summary>
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
                DebugConsole.WriteWarning($"Error getting executable path for PID {processId}: {ex.Message}");
            }

            return "";
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
            _lock?.Dispose();
        }
    }
}