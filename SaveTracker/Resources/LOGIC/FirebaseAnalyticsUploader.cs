using SaveTracker.Resources.HELPERS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic
{
    /// <summary>
    /// Handles uploading anonymous analytics to Firebase Firestore
    /// </summary>
    public static class FirebaseAnalyticsUploader
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Analytics summary data structure for upload to Firebase
        /// Includes game launch events WITHOUT play duration (privacy)
        /// </summary>
        public class AnalyticsSummary
        {
            public string DeviceId { get; set; } = string.Empty;
            public string AppVersion { get; set; } = string.Empty;
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public DateTime Timestamp { get; set; }
            public int TotalLaunches { get; set; }
            public List<GameLaunchEventUpload> GameLaunches { get; set; } = new List<GameLaunchEventUpload>();
        }

        /// <summary>
        /// Game launch event for Firebase upload (excludes play duration)
        /// </summary>
        public class GameLaunchEventUpload
        {
            public string GameName { get; set; } = string.Empty;
            public string ExecutableName { get; set; } = string.Empty;
            public DateTime LaunchedAt { get; set; }
            public int TrackedFilesCount { get; set; }
            public int LaunchCount { get; set; } // Aggregated count
        }

        /// <summary>
        /// Uploads analytics summary to Firebase Firestore
        /// </summary>
        public static async Task<bool> UploadAnalyticsAsync(AnalyticsSummary summary)
        {
            try
            {
                // Build Firestore document path
                var documentPath = $"{FirebaseConfig.ANALYTICS_COLLECTION}/{summary.DeviceId}";
                var url = $"{FirebaseConfig.FirestoreEndpoint}/{documentPath}?key={FirebaseConfig.API_KEY}";

                // Convert summary to Firestore format
                var gameLaunchesArray = summary.GameLaunches.Select(g => new
                {
                    mapValue = new
                    {
                        fields = new
                        {
                            gameName = new { stringValue = g.GameName },
                            executableName = new { stringValue = g.ExecutableName },
                            launchedAt = new { timestampValue = g.LaunchedAt.ToString("o") },
                            trackedFilesCount = new { integerValue = g.TrackedFilesCount.ToString() },
                            launchCount = new { integerValue = g.LaunchCount.ToString() }
                        }
                    }
                }).ToArray();

                var firestoreDoc = new
                {
                    fields = new
                    {
                        deviceId = new { stringValue = summary.DeviceId },
                        appVersion = new { stringValue = summary.AppVersion },
                        firstSeen = new { timestampValue = summary.FirstSeen.ToString("o") },
                        lastSeen = new { timestampValue = summary.LastSeen.ToString("o") },
                        timestamp = new { timestampValue = summary.Timestamp.ToString("o") },
                        totalLaunches = new { integerValue = summary.TotalLaunches.ToString() },
                        gameLaunches = new { arrayValue = new { values = gameLaunchesArray } }
                    }
                };

                var json = JsonSerializer.Serialize(firestoreDoc, JsonHelper.GetOptions());
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Use PATCH to create or update document
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    DebugConsole.WriteInfo($"[Firebase] Analytics uploaded successfully for device {summary.DeviceId.Substring(0, 8)}...");
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    DebugConsole.WriteWarning($"[Firebase] Upload failed: {response.StatusCode} - {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Fail silently - analytics upload should never crash the app
                DebugConsole.WriteWarning($"[Firebase] Upload exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if it's time to upload analytics (>24h since last upload)
        /// </summary>
        public static bool ShouldUpload(DateTime? lastUploadTime)
        {
            if (lastUploadTime == null)
                return true;

            return DateTime.UtcNow - lastUploadTime.Value >= FirebaseConfig.UPLOAD_INTERVAL;
        }
    }
}
