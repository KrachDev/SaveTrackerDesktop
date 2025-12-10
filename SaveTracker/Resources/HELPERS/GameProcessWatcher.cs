using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Linq;
#if WINDOWS
using System.Management;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    /// <summary>
    /// Background service that monitors running processes and detects when tracked games are launched
    /// </summary>
    public class GameProcessWatcher : IDisposable
    {
        private List<Game> _trackedGames = new();
        private readonly HashSet<string> _currentlyTrackedGames = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _watcherTask;
        private readonly int _scanIntervalMs;
        private bool _isRunning;

        /// <summary>
        /// Event raised when a tracked game process is detected
        /// </summary>
        public event Action<Game, int>? GameProcessDetected;

        public GameProcessWatcher(int scanIntervalMs = 5000)
        {
            _scanIntervalMs = scanIntervalMs;
        }

        /// <summary>
        /// Starts watching for game processes
        /// </summary>
        public void StartWatching(List<Game> games)
        {
            if (_isRunning)
            {
                DebugConsole.WriteWarning("GameProcessWatcher is already running");
                return;
            }

            _trackedGames = games;
            _cancellationTokenSource = new CancellationTokenSource();
            _isRunning = true;

            _watcherTask = Task.Run(async () => await WatchProcessesAsync(_cancellationTokenSource.Token));

            DebugConsole.WriteSuccess($"GameProcessWatcher started - monitoring {games.Count} games");
        }

        /// <summary>
        /// Stops the watcher
        /// </summary>
        public void StopWatching()
        {
            if (!_isRunning)
            {
                return;
            }

            DebugConsole.WriteInfo("Stopping GameProcessWatcher...");

            _cancellationTokenSource?.Cancel();
            _watcherTask?.Wait(TimeSpan.FromSeconds(2));

            _isRunning = false;
            _currentlyTrackedGames.Clear();

            DebugConsole.WriteSuccess("GameProcessWatcher stopped");
        }

        /// <summary>
        /// Updates the list of games being monitored
        /// </summary>
        public void UpdateGamesList(List<Game> games)
        {
            _trackedGames = games;
            DebugConsole.WriteInfo($"GameProcessWatcher updated - now monitoring {games.Count} games");
        }

        /// <summary>
        /// Marks a game as currently being tracked to prevent duplicate detection
        /// </summary>
        public void MarkGameAsTracked(string gameName)
        {
            lock (_currentlyTrackedGames)
            {
                _currentlyTrackedGames.Add(gameName);
                DebugConsole.WriteInfo($"GameProcessWatcher: {gameName} marked as tracked");
            }
        }

        /// <summary>
        /// Unmarks a game when tracking stops
        /// </summary>
        public void UnmarkGame(string gameName)
        {
            lock (_currentlyTrackedGames)
            {
                _currentlyTrackedGames.Remove(gameName);
                DebugConsole.WriteInfo($"GameProcessWatcher: {gameName} unmarked");
            }
        }

        /// <summary>
        /// Main watching loop that scans for processes
        /// </summary>
        private async Task WatchProcessesAsync(CancellationToken cancellationToken)
        {
            DebugConsole.WriteInfo("GameProcessWatcher loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_scanIntervalMs, cancellationToken);
                    ScanForGameProcesses();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "Error in GameProcessWatcher loop");
                }
            }

            DebugConsole.WriteInfo("GameProcessWatcher loop ended");
        }

        /// <summary>
        /// Scans all running processes and matches them against tracked games
        /// </summary>
        private void ScanForGameProcesses()
        {
            try
            {
                // Get all running processes with executable paths
                var runningProcesses = GetRunningProcessesWithPaths();

                foreach (var game in _trackedGames)
                {
                    // Skip if already tracking this game
                    lock (_currentlyTrackedGames)
                    {
                        if (_currentlyTrackedGames.Contains(game.Name))
                        {
                            continue;
                        }
                    }

                    // Check if this game's executable is running
                    var matchingProcess = runningProcesses.FirstOrDefault(p =>
                        p.ExecutablePath.Equals(game.ExecutablePath, StringComparison.OrdinalIgnoreCase));

                    if (matchingProcess.ProcessId != 0)
                    {
                        DebugConsole.WriteSuccess($"GameProcessWatcher detected: {game.Name} (PID: {matchingProcess.ProcessId})");

                        // Mark as tracked immediately to prevent duplicate events
                        lock (_currentlyTrackedGames)
                        {
                            _currentlyTrackedGames.Add(game.Name);
                        }

                        // Raise the event
                        GameProcessDetected?.Invoke(game, matchingProcess.ProcessId);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error scanning for game processes: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all running processes with their executable paths
        /// </summary>
        private List<(int ProcessId, string ExecutablePath)> GetRunningProcessesWithPaths()
        {
            var processes = new List<(int ProcessId, string ExecutablePath)>();

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
                            int processId = Convert.ToInt32(obj["ProcessId"]);
                            string? execPath = obj["ExecutablePath"]?.ToString();

                            if (!string.IsNullOrEmpty(execPath))
                            {
                                processes.Add((processId, execPath));
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
                DebugConsole.WriteWarning($"Error querying Win32_Process: {ex.Message}");
            }
#else
            try
            {
                // Cross-platform implementation using System.Diagnostics.Process
                var allProcesses = System.Diagnostics.Process.GetProcesses();
                foreach (var proc in allProcesses)
                {
                    try
                    {
                        if (proc.MainModule != null && !string.IsNullOrEmpty(proc.MainModule.FileName))
                        {
                            processes.Add((proc.Id, proc.MainModule.FileName));
                        }
                    }
                    catch
                    {
                        // Some processes (like system/root processes) won't allow access to MainModule
                        continue;
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error scanning processes: {ex.Message}");
            }
#endif

            return processes;
        }

        public void Dispose()
        {
            StopWatching();
            _cancellationTokenSource?.Dispose();
        }
    }
}
