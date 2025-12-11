using System;
using System.Collections.Generic;
using SaveTracker.Resources.HELPERS;
using static CloudConfig;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class GameUploadData
    {
        public Dictionary<string, FileChecksumRecord> Files { get; set; } =
            new Dictionary<string, FileChecksumRecord>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public bool CanTrack { get; set; } = true;
        public bool CanUploads { get; set; } = true;
        public CloudProvider GameProvider { get; set; } = CloudProvider.Global;
        public Dictionary<string, FileChecksumRecord> Blacklist { get; set; } =
            new Dictionary<string, FileChecksumRecord>();
        public string LastSyncStatus { get; set; } = "Unknown";
        public bool AllowGameWatcher { get; set; } = true;
        public bool EnableSmartSync { get; set; } = true;
        public TimeSpan PlayTime { get; set; } = TimeSpan.Zero;
        public string? DetectedPrefix { get; set; }
    }

    public class FileChecksumRecord
    {
        public string Checksum { get; set; }
        public DateTime LastUpload { get; set; }
        public string Path { get; set; }
        public long FileSize { get; set; }

        public string GetAbsolutePath(string gameDirectory = null, string? detectedPrefix = null)
        {
            if (string.IsNullOrEmpty(Path))
                return Path;

            if (!string.IsNullOrEmpty(gameDirectory) &&
                Path.StartsWith("%GAMEPATH%", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = Path.Substring("%GAMEPATH%".Length).TrimStart('/', '\\');
                if (System.IO.Path.DirectorySeparatorChar == '/')
                {
                    relativePath = relativePath.Replace('\\', '/');
                }
                return System.IO.Path.Combine(gameDirectory, relativePath);
            }

            return PathContractor.ExpandPath(Path, gameDirectory, detectedPrefix);
        }

        public override string ToString()
        {
            return GetAbsolutePath();
        }
    }

    public class RemoteFileInfo
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime ModTime { get; set; }
    }

    public class DownloadResult
    {
        public int DownloadedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public long DownloadedSize { get; set; }
        public long SkippedSize { get; set; }
        public List<string> FailedFiles { get; set; } = new List<string>();
    }
}
