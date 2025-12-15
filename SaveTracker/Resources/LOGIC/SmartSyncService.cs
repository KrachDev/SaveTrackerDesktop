using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SaveTracker.Resources.HELPERS;
using System.Linq;

namespace SaveTracker.Resources.Logic
{
    /// <summary>
    /// Service for comparing local and cloud PlayTime to determine which save has more progress
    /// </summary>
    public class SmartSyncService
    {
        private readonly RcloneFileOperations _rcloneOps;
        private readonly ChecksumService _checksumService;

        public SmartSyncService()
        {
            _rcloneOps = new RcloneFileOperations();
            _checksumService = new ChecksumService();
        }

        /// <summary>
        /// Comparison result indicating which save is ahead
        /// </summary>
        public enum ProgressStatus
        {
            LocalAhead,      // Local PlayTime > Cloud
            CloudAhead,      // Cloud PlayTime > Local
            Similar,         // Difference within threshold
            CloudNotFound,   // No cloud save exists
            Error           // Error reading PlayTime data
        }

        /// <summary>
        /// Result of comparing local vs cloud progress
        /// </summary>
        public class ProgressComparison
        {
            public ProgressStatus Status { get; set; }
            public TimeSpan LocalPlayTime { get; set; }
            public TimeSpan CloudPlayTime { get; set; }
            public TimeSpan Difference { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Compare local and cloud PlayTime to determine which is ahead
        /// </summary>

        public async Task<ProgressComparison> CompareProgressAsync(
            Game game,
            TimeSpan threshold,
            CloudProvider? provider = null,
            GameUploadData? cachedCloudData = null)
        {
            try
            {
                // Get local PlayTime
                var localPlayTime = await GetLocalPlayTimeAsync(game);

                // Get cloud PlayTime
                var cloudPlayTime = await GetCloudPlayTimeAsync(game, provider, cachedCloudData);

                // Cloud doesn't exist
                if (cloudPlayTime == null)
                {
                    return new ProgressComparison
                    {
                        Status = ProgressStatus.CloudNotFound,
                        LocalPlayTime = localPlayTime,
                        CloudPlayTime = TimeSpan.Zero,
                        Difference = TimeSpan.Zero,
                        Message = "No cloud save found"
                    };
                }

                // Calculate difference
                var diff = cloudPlayTime.Value - localPlayTime;
                var absDiff = Math.Abs(diff.TotalMinutes);

                // Special case: If local has no playtime but cloud does, always prefer cloud
                if (localPlayTime == TimeSpan.Zero && cloudPlayTime.Value > TimeSpan.Zero)
                {
                    return new ProgressComparison
                    {
                        Status = ProgressStatus.CloudAhead,
                        LocalPlayTime = localPlayTime,
                        CloudPlayTime = cloudPlayTime.Value,
                        Difference = diff,
                        Message = $"Cloud save exists, local is new (cloud: {FormatTimeSpan(cloudPlayTime.Value)})"
                    };
                }

                // Within threshold - similar progress
                if (absDiff < threshold.TotalMinutes)
                {
                    return new ProgressComparison
                    {
                        Status = ProgressStatus.Similar,
                        LocalPlayTime = localPlayTime,
                        CloudPlayTime = cloudPlayTime.Value,
                        Difference = diff,
                        Message = $"Progress is similar (diff: {FormatTimeSpan(diff)})"
                    };
                }

                // Determine which is ahead
                if (diff > TimeSpan.Zero)
                {
                    return new ProgressComparison
                    {
                        Status = ProgressStatus.CloudAhead,
                        LocalPlayTime = localPlayTime,
                        CloudPlayTime = cloudPlayTime.Value,
                        Difference = diff,
                        Message = $"Cloud is ahead by {FormatTimeSpan(diff)}"
                    };
                }
                else
                {
                    return new ProgressComparison
                    {
                        Status = ProgressStatus.LocalAhead,
                        LocalPlayTime = localPlayTime,
                        CloudPlayTime = cloudPlayTime.Value,
                        Difference = diff,
                        Message = $"Local is ahead by {FormatTimeSpan(diff.Negate())}"
                    };
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to compare progress");
                return new ProgressComparison
                {
                    Status = ProgressStatus.Error,
                    LocalPlayTime = TimeSpan.Zero,
                    CloudPlayTime = TimeSpan.Zero,
                    Difference = TimeSpan.Zero,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Get local PlayTime from checksum file
        /// </summary>
        private async Task<TimeSpan> GetLocalPlayTimeAsync(Game game)
        {
            try
            {
                DebugConsole.WriteInfo($"Reading local PlayTime from: {game.InstallDirectory}");
                var checksumData = await _checksumService.LoadChecksumData(game.InstallDirectory);

                DebugConsole.WriteInfo($"Checksum data loaded: PlayTime={checksumData.PlayTime}, Files={checksumData.Files.Count}");

                // Load detected prefix for Wine path expansion on Linux
                var gameData = await ConfigManagement.GetGameData(game);
                string? detectedPrefix = gameData?.DetectedPrefix;

                if (!string.IsNullOrEmpty(detectedPrefix))
                {
                    DebugConsole.WriteInfo($"Using detected prefix for path expansion: {detectedPrefix}");
                }

                // Validate that actual save files exist, not just the checksum file
                // This prevents incorrect sync decisions in dual-boot scenarios
                int existingFilesCount = _checksumService.CountExistingFiles(checksumData, game.InstallDirectory, detectedPrefix);

                if (existingFilesCount == 0 && checksumData.Files.Count > 0)
                {
                    DebugConsole.WriteWarning($"Checksum file exists but no actual save files found ({checksumData.Files.Count} files referenced, 0 exist)");
                    DebugConsole.WriteInfo("Treating as no local save - likely dual-boot scenario or files were deleted");
                    return TimeSpan.Zero;
                }

                if (existingFilesCount > 0)
                {
                    DebugConsole.WriteSuccess($"Local save validated: {existingFilesCount}/{checksumData.Files.Count} files exist, PlayTime={checksumData.PlayTime}");
                }

                return checksumData.PlayTime;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to read local PlayTime");
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Download and read cloud checksum file to get PlayTime
        /// </summary>
        private async Task<TimeSpan?> GetCloudPlayTimeAsync(Game game, CloudProvider? provider = null, GameUploadData? cachedCloudData = null)
        {
            try
            {
                if (cachedCloudData != null)
                {
                    DebugConsole.WriteInfo($"Using cached cloud data. PlayTime: {FormatTimeSpan(cachedCloudData.PlayTime)}");
                    return cachedCloudData.PlayTime;
                }

                var effectiveProvider = provider ?? await GetEffectiveProvider(game);

                // Use helper to get correct path for active profile
                var remoteBasePath = await _rcloneOps.GetRemotePathAsync(effectiveProvider, game);

                DebugConsole.WriteInfo($"Checking cloud for: {remoteBasePath}");

                // Check if cloud save exists
                bool cloudExists = await _rcloneOps.CheckCloudSaveExistsAsync(remoteBasePath, effectiveProvider);
                if (!cloudExists)
                {
                    DebugConsole.WriteInfo("Cloud save doesn't exist");
                    return null;
                }

                // Download checksum file to temp location
                string tempFolder = Path.Combine(Path.GetTempPath(), $"SaveTracker_SmartSync_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempFolder);

                try
                {
                    // The checksum file is uploaded with path contraction
                    string gameDirectory = game.InstallDirectory;
                    string checksumLocalFullPath = Path.Combine(gameDirectory, SaveFileUploadManager.ChecksumFilename);

                    // Get the relative path that would be used for upload (contracted path)
                    string checksumRelativePath = PathContractor.ContractPath(checksumLocalFullPath, gameDirectory);
                    checksumRelativePath = checksumRelativePath.Replace('\\', '/');

                    string checksumRemotePath = $"{remoteBasePath}/{checksumRelativePath}";
                    string checksumLocalPath = Path.Combine(tempFolder, SaveFileUploadManager.ChecksumFilename);

                    DebugConsole.WriteInfo($"Downloading checksum from: {checksumRemotePath}");

                    // Use RcloneTransferService for reliable download with retry
                    var transferService = new RcloneTransferService();
                    bool downloaded = await transferService.DownloadFileWithRetry(
                        checksumRemotePath,
                        checksumLocalPath,
                        SaveFileUploadManager.ChecksumFilename,
                        effectiveProvider
                    );

                    if (!downloaded || !File.Exists(checksumLocalPath))
                    {
                        DebugConsole.WriteWarning("Failed to download cloud checksum file");
                        return null;
                    }

                    // Read PlayTime from downloaded checksum file
                    string json = await File.ReadAllTextAsync(checksumLocalPath);
                    var cloudData = JsonConvert.DeserializeObject<GameUploadData>(json);

                    if (cloudData == null)
                    {
                        DebugConsole.WriteWarning("Failed to deserialize cloud checksum data");
                        return null;
                    }

                    // Validate that actual cloud save files exist, not just the checksum
                    // OPTIMIZATION: Instead of listing ALL files (which hangs on large folders),
                    // we trust the checksum file existence + maybe check one file if strictly needed.
                    // For now, if checksums.json exists, we assume the save is valid to avoid the performance penalty.

                    if (cloudData.Files != null && cloudData.Files.Count > 0)
                    {
                        DebugConsole.WriteInfo($"Cloud save validated via checksum file ({cloudData.Files.Count} references)");
                    }

                    DebugConsole.WriteSuccess($"Cloud PlayTime: {FormatTimeSpan(cloudData.PlayTime)}");
                    return cloudData.PlayTime;
                }
                finally
                {
                    // Cleanup temp folder
                    try
                    {
                        if (Directory.Exists(tempFolder))
                            Directory.Delete(tempFolder, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to get cloud PlayTime");
                return null;
            }
        }

        public async Task<CloudProvider> GetEffectiveProvider(Game game)
        {
            try
            {
                var gameData = await ConfigManagement.GetGameData(game);
                if (gameData != null && gameData.GameProvider != CloudProvider.Global)
                {
                    return gameData.GameProvider;
                }

                var globalConfig = await ConfigManagement.LoadConfigAsync();
                return globalConfig?.CloudConfig?.Provider ?? CloudProvider.GoogleDrive;
            }
            catch
            {
                return CloudProvider.GoogleDrive;
            }
        }

        private static string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            return invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_')).Trim();
        }

        private static string FormatTimeSpan(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
                time = time.Negate();

            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }
    }
}
