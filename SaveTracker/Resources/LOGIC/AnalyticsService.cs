using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic
{
    /// <summary>
    /// Privacy-focused analytics service
    /// Collects anonymous usage statistics with user consent
    /// NO PERSONAL INFORMATION IS COLLECTED
    /// </summary>
    public static class AnalyticsService
    {
        private static readonly string ANALYTICS_PATH = Path.Combine(
            ConfigManagement.BASE_PATH,
            "Data",
            "analytics.json"
        );

        private static readonly object _fileLock = new object();

        /// <summary>
        /// Checks if analytics are enabled in user settings
        /// </summary>
        public static async Task<bool> IsEnabledAsync()
        {
            try
            {
                var config = await ConfigManagement.LoadConfigAsync();
                return config?.EnableAnalytics ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Records a game launch event
        /// Only stores: game name, executable filename, timestamp - no personal data or paths
        /// </summary>
        public static async Task RecordGameLaunchAsync(string gameName, string executablePath)
        {
            if (!await IsEnabledAsync())
                return;

            if (string.IsNullOrWhiteSpace(gameName))
                return;

            try
            {
                var data = await LoadAnalyticsDataAsync();

                // Extract only the filename from the executable path (no directory path)
                string executableName = string.Empty;
                if (!string.IsNullOrWhiteSpace(executablePath))
                {
                    executableName = Path.GetFileName(executablePath);
                }

                var launchEvent = new GameLaunchEvent
                {
                    GameName = gameName,
                    ExecutableName = executableName,
                    LaunchedAt = DateTime.UtcNow,
                    TrackedFilesCount = 0 // Will be updated later
                };

                data.GameLaunches.Add(launchEvent);
                data.TotalLaunches++;
                data.LastSeen = DateTime.UtcNow;

                await SaveAnalyticsDataAsync(data);

                DebugConsole.WriteInfo($"[Analytics] Recorded launch: {gameName} ({executableName})");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[Analytics] Failed to record launch: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the tracked files count for the most recent launch of a game
        /// Only stores the count, NOT file names or paths
        /// </summary>
        public static async Task RecordTrackedFilesAsync(string gameName, int fileCount, TimeSpan playDuration)
        {
            if (!await IsEnabledAsync())
                return;

            if (string.IsNullOrWhiteSpace(gameName) || fileCount < 0)
                return;

            try
            {
                var data = await LoadAnalyticsDataAsync();

                // Find the most recent launch event for this game
                var recentLaunch = data.GameLaunches
                    .Where(e => e.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.LaunchedAt)
                    .FirstOrDefault();

                if (recentLaunch != null)
                {
                    recentLaunch.TrackedFilesCount = fileCount;
                    recentLaunch.PlayDuration = playDuration;
                    data.LastSeen = DateTime.UtcNow;

                    await SaveAnalyticsDataAsync(data);

                    DebugConsole.WriteInfo($"[Analytics] Updated {gameName}: {fileCount} files, {playDuration.TotalMinutes:F1}m played");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[Analytics] Failed to record tracked files: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current analytics data
        /// </summary>
        public static async Task<AnalyticsData> GetAnalyticsDataAsync()
        {
            return await LoadAnalyticsDataAsync();
        }

        /// <summary>
        /// Gets a summary of analytics for display
        /// </summary>
        public static async Task<AnalyticsSummary> GetSummaryAsync()
        {
            var data = await LoadAnalyticsDataAsync();

            return new AnalyticsSummary
            {
                DeviceId = data.DeviceId,
                FirstSeen = data.FirstSeen,
                LastSeen = data.LastSeen,
                TotalLaunches = data.TotalLaunches,
                UniqueGamesLaunched = data.GameLaunches
                    .Select(e => e.GameName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                TotalFilesTracked = data.GameLaunches.Sum(e => e.TrackedFilesCount),
                TotalPlayTime = TimeSpan.FromTicks(data.GameLaunches.Sum(e => e.PlayDuration.Ticks))
            };
        }

        /// <summary>
        /// Clears all analytics data
        /// </summary>
        public static async Task ClearAnalyticsDataAsync()
        {
            try
            {
                lock (_fileLock)
                {
                    if (File.Exists(ANALYTICS_PATH))
                    {
                        File.Delete(ANALYTICS_PATH);
                    }
                }

                DebugConsole.WriteSuccess("[Analytics] Data cleared");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "[Analytics] Failed to clear data");
                throw;
            }
        }

        /// <summary>
        /// Loads analytics data from disk, creates new if doesn't exist
        /// </summary>
        private static async Task<AnalyticsData> LoadAnalyticsDataAsync()
        {
            try
            {
                lock (_fileLock)
                {
                    if (!File.Exists(ANALYTICS_PATH))
                    {
                        return CreateNewAnalyticsData();
                    }
                }

                string json = await File.ReadAllTextAsync(ANALYTICS_PATH);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return CreateNewAnalyticsData();
                }

                var data = JsonSerializer.Deserialize<AnalyticsData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return data ?? CreateNewAnalyticsData();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[Analytics] Failed to load data, creating new: {ex.Message}");
                return CreateNewAnalyticsData();
            }
        }

        /// <summary>
        /// Saves analytics data to disk
        /// </summary>
        private static async Task SaveAnalyticsDataAsync(AnalyticsData data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Ensure Data directory exists
                var dataDir = Path.GetDirectoryName(ANALYTICS_PATH);
                if (!string.IsNullOrEmpty(dataDir) && !Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }

                lock (_fileLock)
                {
                    File.WriteAllText(ANALYTICS_PATH, json);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "[Analytics] Failed to save data");
                throw;
            }
        }

        /// <summary>
        /// Creates a new analytics data object with device ID
        /// </summary>
        private static AnalyticsData CreateNewAnalyticsData()
        {
            return new AnalyticsData
            {
                DeviceId = HardwareId.GetHardwareId(),
                FirstSeen = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                TotalLaunches = 0,
                GameLaunches = new List<GameLaunchEvent>()
            };
        }
    }
}
