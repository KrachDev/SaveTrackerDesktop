using SaveTracker.Resources.HELPERS;
using System;
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
        /// Analytics summary data structure for upload
        /// </summary>
        public class AnalyticsSummary
        {
            public string DeviceId { get; set; } = string.Empty;
            public string AppVersion { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public int TotalLaunches { get; set; }
            public int UniqueGames { get; set; }
            public int TotalFilesTracked { get; set; }
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
                var firestoreDoc = new
                {
                    fields = new
                    {
                        deviceId = new { stringValue = summary.DeviceId },
                        appVersion = new { stringValue = summary.AppVersion },
                        timestamp = new { timestampValue = summary.Timestamp.ToString("o") },
                        totalLaunches = new { integerValue = summary.TotalLaunches.ToString() },
                        uniqueGames = new { integerValue = summary.UniqueGames.ToString() },
                        totalFilesTracked = new { integerValue = summary.TotalFilesTracked.ToString() }
                    }
                };

                var json = JsonSerializer.Serialize(firestoreDoc);
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
