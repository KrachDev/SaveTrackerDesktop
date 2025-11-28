using System;

namespace SaveTracker.Resources.Logic.AutoUpdater
{
    /// <summary>
    /// Data model containing information about an available update
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// The new version number (e.g., "0.3.0")
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Direct download URL for the executable
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// Release notes/description from GitHub
        /// </summary>
        public string ReleaseNotes { get; set; } = string.Empty;

        /// <summary>
        /// When the release was published
        /// </summary>
        public DateTime PublishedAt { get; set; }

        /// <summary>
        /// Whether an update is available
        /// </summary>
        public bool IsUpdateAvailable { get; set; }

        /// <summary>
        /// Size of the download in bytes
        /// </summary>
        public long DownloadSize { get; set; }
    }
}
