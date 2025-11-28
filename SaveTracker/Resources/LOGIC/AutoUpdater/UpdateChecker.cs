using Newtonsoft.Json.Linq;
using SaveTracker.Resources.HELPERS;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic.AutoUpdater
{
    /// <summary>
    /// Service to check for application updates via GitHub API
    /// </summary>
    public class UpdateChecker
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/KrachDev/SaveTrackerDesktop/releases/latest";
        private readonly string _currentVersion;

        public UpdateChecker()
        {
            // Get current version from assembly
            _currentVersion = Assembly.GetExecutingAssembly()
                .GetName()
                .Version?
                .ToString(3) ?? "0.0.0"; // Major.Minor.Patch
        }

        /// <summary>
        /// Checks GitHub for the latest release and compares with current version
        /// </summary>
        /// <returns>UpdateInfo object with details about available update</returns>
        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            DebugConsole.WriteSection("Checking for Updates");
            DebugConsole.WriteKeyValue("Current Version", _currentVersion);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SaveTracker-AutoUpdater/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);

                DebugConsole.WriteInfo("Fetching latest release from GitHub...");
                var json = await client.GetStringAsync(GitHubApiUrl);

                var release = JObject.Parse(json);

                // Extract version from tag_name (e.g., "v0.3.0" -> "0.3.0")
                var tagName = release["tag_name"]?.ToString() ?? "";
                var latestVersion = tagName.TrimStart('v');
                DebugConsole.WriteKeyValue("Latest Version", latestVersion);

                // Find the .exe asset
                string? downloadUrl = null;
                long downloadSize = 0;

                if (release["assets"] is JArray assets)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString();
                        if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString();
                            downloadSize = asset["size"]?.ToObject<long>() ?? 0;
                            DebugConsole.WriteKeyValue("Found Asset", name);
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    DebugConsole.WriteWarning("No .exe asset found in latest release");
                    return new UpdateInfo { IsUpdateAvailable = false };
                }

                // Compare versions
                bool isNewer = CompareVersions(latestVersion, _currentVersion);

                var updateInfo = new UpdateInfo
                {
                    Version = latestVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = release["body"]?.ToString() ?? "",
                    PublishedAt = release["published_at"]?.ToObject<DateTime>() ?? DateTime.Now,
                    IsUpdateAvailable = isNewer,
                    DownloadSize = downloadSize
                };

                if (isNewer)
                {
                    DebugConsole.WriteSuccess($"Update available: {latestVersion}");
                }
                else
                {
                    DebugConsole.WriteInfo("Application is up to date");
                }

                return updateInfo;
            }
            catch (HttpRequestException ex)
            {
                DebugConsole.WriteException(ex, "Failed to check for updates (network error)");
                return new UpdateInfo { IsUpdateAvailable = false };
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check for updates");
                return new UpdateInfo { IsUpdateAvailable = false };
            }
        }

        /// <summary>
        /// Compares two version strings (e.g., "0.3.0" vs "0.2.2")
        /// </summary>
        /// <param name="newVersion">The new version to compare</param>
        /// <param name="currentVersion">The current version</param>
        /// <returns>True if newVersion is greater than currentVersion</returns>
        private bool CompareVersions(string newVersion, string currentVersion)
        {
            try
            {
                var newParts = newVersion.Split('.').Select(int.Parse).ToArray();
                var currentParts = currentVersion.Split('.').Select(int.Parse).ToArray();

                // Ensure both have at least 3 parts (major.minor.patch)
                var newVer = new Version(
                    newParts.Length > 0 ? newParts[0] : 0,
                    newParts.Length > 1 ? newParts[1] : 0,
                    newParts.Length > 2 ? newParts[2] : 0
                );

                var currentVer = new Version(
                    currentParts.Length > 0 ? currentParts[0] : 0,
                    currentParts.Length > 1 ? currentParts[1] : 0,
                    currentParts.Length > 2 ? currentParts[2] : 0
                );

                return newVer > currentVer;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to compare versions");
                return false;
            }
        }

        /// <summary>
        /// Gets the current application version
        /// </summary>
        public string GetCurrentVersion() => _currentVersion;
    }
}
