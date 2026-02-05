using System;

namespace SaveTracker.Resources.Logic
{
    /// <summary>
    /// Firebase configuration for analytics upload
    /// </summary>
    public static class FirebaseConfig
    {
        // Firebase project configuration
        public const string PROJECT_ID = "savetrackerdesktop";
        public const string API_KEY = "AIzaSyDA915_k9aUbGKsD1pAK-npeqetCoawKZw";

        // Firestore REST API endpoint
        public static string FirestoreEndpoint =>
            $"https://firestore.googleapis.com/v1/projects/{PROJECT_ID}/databases/(default)/documents";

        // Collection name for analytics data
        public const string ANALYTICS_COLLECTION = "analytics";

        // Upload interval for app startup throttling (24 hours)
        // Note: Uploads also happen after save uploads without throttling
        public static readonly TimeSpan UPLOAD_INTERVAL = TimeSpan.FromHours(24);
    }
}
