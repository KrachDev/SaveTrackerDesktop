using SaveTracker.Resources.HELPERS;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaveTracker.Resources.SAVE_SYSTEM
{
    public class VersionInfo
    {
        public string CurrentVersion { get; set; } = "0.0.0";
        public string LastSeenVersion { get; set; } = "0.0.0";
    }

    public static class VersionManager
    {
        private static readonly string VERSION_PATH = Path.Combine(
            ConfigManagement.BASE_PATH,
            "Data",
            "version.json"
        );

        /// <summary>
        /// Checks if announcement should be shown (current version > last seen version)
        /// </summary>
        public static async Task<bool> ShouldShowAnnouncementAsync(string currentVersion)
        {
            var versionInfo = await LoadVersionInfoAsync();

            // Compare versions
            bool shouldShow = CompareVersions(currentVersion, versionInfo.LastSeenVersion) > 0;

            DebugConsole.WriteInfo($"[Version] Current: {currentVersion}, Last Seen: {versionInfo.LastSeenVersion}, Should Show: {shouldShow}");

            return shouldShow;
        }

        /// <summary>
        /// Marks the current version as seen
        /// </summary>
        public static async Task MarkVersionAsSeenAsync(string currentVersion)
        {
            var versionInfo = await LoadVersionInfoAsync();
            versionInfo.CurrentVersion = currentVersion;
            versionInfo.LastSeenVersion = currentVersion;
            await SaveVersionInfoAsync(versionInfo);

            DebugConsole.WriteInfo($"[Version] Marked {currentVersion} as seen");
        }

        /// <summary>
        /// Loads version info from file
        /// </summary>
        private static async Task<VersionInfo> LoadVersionInfoAsync()
        {
            try
            {
                if (!File.Exists(VERSION_PATH))
                {
                    return new VersionInfo();
                }

                string json = await File.ReadAllTextAsync(VERSION_PATH);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(json, JsonHelper.GetOptions());
                return versionInfo ?? new VersionInfo();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[Version] Failed to load version.json: {ex.Message}");
                return new VersionInfo();
            }
        }

        /// <summary>
        /// Saves version info to file
        /// </summary>
        private static async Task SaveVersionInfoAsync(VersionInfo versionInfo)
        {
            try
            {
                // Ensure Data directory exists
                var dataDir = Path.GetDirectoryName(VERSION_PATH);
                if (!string.IsNullOrEmpty(dataDir) && !Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }

                var json = JsonSerializer.Serialize(versionInfo, JsonHelper.DefaultIndented);

                await File.WriteAllTextAsync(VERSION_PATH, json);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "[Version] Failed to save version.json");
            }
        }

        /// <summary>
        /// Compares two version strings (e.g., "0.4.4" vs "0.4.3")
        /// Returns: 1 if v1 > v2, -1 if v1 < v2, 0 if equal
        /// </summary>
        private static int CompareVersions(string v1, string v2)
        {
            try
            {
                var version1 = new Version(v1);
                var version2 = new Version(v2);
                return version1.CompareTo(version2);
            }
            catch
            {
                // If parsing fails, treat as equal
                return 0;
            }
        }
    }
}
