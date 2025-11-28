using SaveTracker.Resources.HELPERS;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic.Analytics
{
    /// <summary>
    /// Simple analytics service using GitHub Gist as a free backend
    /// Tracks anonymous user count without requiring a server
    /// </summary>
    public class AnalyticsService
    {
        // This is a public gist that acts as our "database"
        // You'll need to create a gist and put the ID here
        private const string GistId = "YOUR_GIST_ID_HERE"; // TODO: Replace with actual gist ID
        private const string GitHubToken = "YOUR_GITHUB_TOKEN_HERE"; // TODO: Replace with GitHub PAT

        private static bool _hasReported = false;

        /// <summary>
        /// Reports app launch to analytics (once per session)
        /// </summary>
        public static async Task ReportAppLaunchAsync()
        {
            if (_hasReported) return;

            try
            {
                string hardwareId = HardwareId.GetHardwareId();
                string version = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString(3) ?? "unknown";

                await SendAnalyticsAsync(hardwareId, version);
                _hasReported = true;

                DebugConsole.WriteInfo($"Analytics reported: User {hardwareId.Substring(0, 8)}...");
            }
            catch (Exception ex)
            {
                // Silently fail - analytics shouldn't break the app
                DebugConsole.WriteDebug($"Analytics failed (non-critical): {ex.Message}");
            }
        }

        private static async Task SendAnalyticsAsync(string userId, string version)
        {
            if (string.IsNullOrEmpty(GitHubToken) || GitHubToken == "YOUR_GITHUB_TOKEN_HERE")
            {
                DebugConsole.WriteDebug("Analytics not configured - skipping");
                return;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SaveTracker-Analytics/1.0");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GitHubToken);

                // Create a simple log entry
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                string logEntry = $"{timestamp}|{userId}|{version}|{Environment.OSVersion.Platform}\n";

                // Append to gist file
                string jsonContent = $$"""
                {
                    "files": {
                        "users.txt": {
                            "content": "{{logEntry}}"
                        }
                    }
                }
                """;

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                await client.PatchAsync($"https://api.github.com/gists/{GistId}", content);
            }
            catch
            {
                // Silently fail
            }
        }
    }
}
