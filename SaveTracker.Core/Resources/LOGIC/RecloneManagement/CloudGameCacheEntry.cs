using System;
using System.Collections.Generic;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    /// <summary>
    /// Represents cached metadata for a single cloud game
    /// </summary>
    public class CloudGameCacheEntry
    {
        /// <summary>
        /// Game name (folder name in cloud storage)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Total playtime accumulated across all sessions
        /// </summary>
        public TimeSpan PlayTime { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Number of save files in cloud storage
        /// </summary>
        public int FileCount { get; set; } = 0;

        /// <summary>
        /// Total size of all save files in bytes
        /// </summary>
        public long TotalSize { get; set; } = 0;

        /// <summary>
        /// When this game's cloud data was last modified
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Whether an icon is available in cache
        /// </summary>
        public bool HasIcon { get; set; } = false;

        /// <summary>
        /// Local path to cached icon (transient - not serialized)
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? CachedIconPath { get; set; }

        /// <summary>
        /// Last known modification time of the remote game files (for smart caching)
        /// </summary>
        public DateTime CloudModTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Manually provided Steam App ID (for Ver 1 games or override)
        /// </summary>
        public string? SteamAppId { get; set; }

        /// <summary>
        /// Flag indicating this game needs manual Steam App ID input
        /// </summary>
        public bool NeedsAppIdInput { get; set; } = false;
    }

    /// <summary>
    /// Represents the entire cloud library cache
    /// </summary>
    public class CloudLibraryCache
    {
        /// <summary>
        /// List of all cached games
        /// </summary>
        public List<CloudGameCacheEntry> Games { get; set; } = new();

        /// <summary>
        /// When this cache was last refreshed from cloud
        /// </summary>
        public DateTime LastRefresh { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Cloud provider name (GoogleDrive, OneDrive, etc.)
        /// </summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>
        /// Cache format version for future migrations
        /// </summary>
        public int Version { get; set; } = 1;
    }
}
