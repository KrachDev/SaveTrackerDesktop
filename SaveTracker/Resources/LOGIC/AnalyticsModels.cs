using System;
using System.Collections.Generic;

namespace SaveTracker.Resources.Logic
{
    /// <summary>
    /// Analytics data model - stores anonymous usage statistics
    /// NO PERSONAL INFORMATION IS COLLECTED
    /// </summary>
    public class AnalyticsData
    {
        /// <summary>
        /// Anonymous device identifier (SHA256 hash of hardware components)
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// First time analytics were recorded on this device
        /// </summary>
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last time analytics were recorded on this device
        /// </summary>
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Total number of game launches recorded
        /// </summary>
        public int TotalLaunches { get; set; } = 0;

        /// <summary>
        /// List of game launch events (game name only, no paths)
        /// </summary>
        public List<GameLaunchEvent> GameLaunches { get; set; } = new List<GameLaunchEvent>();
    }

    /// <summary>
    /// Represents a single game launch event
    /// </summary>
    public class GameLaunchEvent
    {
        /// <summary>
        /// Game name only (NO executable path, NO install directory)
        /// </summary>
        public string GameName { get; set; } = string.Empty;

        /// <summary>
        /// Executable filename only (e.g., "game.exe") - NO full path
        /// </summary>
        public string ExecutableName { get; set; } = string.Empty;

        /// <summary>
        /// When the game was launched (UTC)
        /// </summary>
        public DateTime LaunchedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of files tracked (count only, NO file names or paths)
        /// </summary>
        public int TrackedFilesCount { get; set; } = 0;

        /// <summary>
        /// Duration the game was played (if available)
        /// </summary>
        public TimeSpan PlayDuration { get; set; } = TimeSpan.Zero;
    }

    /// <summary>
    /// Summary statistics for display purposes
    /// </summary>
    public class AnalyticsSummary
    {
        public string DeviceId { get; set; } = string.Empty;
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int TotalLaunches { get; set; }
        public int UniqueGamesLaunched { get; set; }
        public int TotalFilesTracked { get; set; }
        public TimeSpan TotalPlayTime { get; set; }
    }
}
