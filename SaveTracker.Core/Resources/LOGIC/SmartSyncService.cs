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
        private readonly SaveArchiver _archiver;

        public SmartSyncService()
        {
            _rcloneOps = new RcloneFileOperations();
            _checksumService = new ChecksumService();
            _archiver = new SaveArchiver();
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
            public string Message { get; set; } = string.Empty;
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
                DebugConsole.WriteInfo($"Reading local PlayTime from: {game.InstallDirectory} (Profile: {game.ActiveProfileId})");
                var checksumData = await _checksumService.LoadChecksumData(game.InstallDirectory, game.ActiveProfileId);

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

        // Helper to get remote file ModTime using lsjson
        private async Task<DateTime?> GetRemoteFileModTime(string remotePath, CloudProvider provider)
        {
            try
            {
                var configPath = RclonePathHelper.GetConfigPath(provider);
                // Use lsjson to get specific file metadata
                // Note: lsjson on a full path usually returns the file info
                var command = $"lsjson \"{remotePath}\" --config \"{configPath}\" --no-mimetype --files-only " + RcloneExecutor.GetPerformanceFlags();

                var result = await new RcloneExecutor().ExecuteRcloneCommand(command, TimeSpan.FromSeconds(15));
                if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return null;

                var items = JsonConvert.DeserializeObject<List<RcloneFileItem>>(result.Output);
                return items?.FirstOrDefault()?.ModTime;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteDebug($"Failed to get remote ModTime for {remotePath}: {ex.Message}");
                return null;
            }
        }

        // Inner class for lsjson parsing
        private class RcloneFileItem
        {
            public DateTime ModTime { get; set; }
        }

        /// <summary>
        /// Download and read cloud checksum file to get PlayTime
        /// </summary>
        public async Task<TimeSpan?> GetCloudPlayTimeAsync(Game game, CloudProvider? provider = null, GameUploadData? cachedCloudData = null)
        {
            try
            {
                if (cachedCloudData != null)
                {
                    DebugConsole.WriteInfo($"Using cached cloud data. PlayTime: {FormatTimeSpan(cachedCloudData.PlayTime)}");
                    return cachedCloudData.PlayTime;
                }

                if (string.IsNullOrWhiteSpace(game?.InstallDirectory))
                {
                    DebugConsole.WriteWarning("Invalid game or missing install directory");
                    return null;
                }

                var effectiveProvider = provider ?? await GetEffectiveProvider(game);
                var remoteBasePath = await _rcloneOps.GetRemotePathAsync(effectiveProvider, game);

                DebugConsole.WriteInfo($"Checking cloud for: {remoteBasePath}");

                // === HIGH PERFORMANCE PEEK (.sta) ===
                var peekedData = await PeekCloudMetadataAsync(remoteBasePath, effectiveProvider, game.ActiveProfileId);
                if (peekedData != null)
                {
                    DebugConsole.WriteSuccess($"Cloud Peek SUCCESS: PlayTime: {FormatTimeSpan(peekedData.PlayTime)}");
                    return peekedData.PlayTime;
                }

                DebugConsole.WriteInfo("Archive not found or peek failed. Falling back to legacy JSON check...");

                // === LEGACY FALLBACK (JSON) ===
                return await GetLegacyCloudPlayTime(game, effectiveProvider, remoteBasePath);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to get cloud PlayTime");
                return null;
            }
        }

        /// <summary>
        /// Legacy method - checks old JSON checksum files
        /// </summary>
        private async Task<TimeSpan?> GetLegacyCloudPlayTime(
            Game game,
            CloudProvider provider,
            string remoteBasePath)
        {
            try
            {
                // Determine file names
                string gameDirectory = game.InstallDirectory;
                string checksumLocalFullPath = _checksumService.GetChecksumFilePath(gameDirectory, game.ActiveProfileId);
                string checksumFileName = Path.GetFileName(checksumLocalFullPath);

                var gameData = await ConfigManagement.GetGameData(game);
                string? detectedPrefix = gameData?.DetectedPrefix;
                string relativeChecksumPath = PathContractor.ContractPath(checksumLocalFullPath, gameDirectory, detectedPrefix).Replace('\\', '/');

                string primaryRemotePath = $"{remoteBasePath}/{relativeChecksumPath}";
                string legacyChecksumName = ".savetracker_checksums.json";
                string legacyRelativePath = relativeChecksumPath.Replace(checksumFileName, legacyChecksumName);
                string legacyRemotePath = $"{remoteBasePath}/{legacyRelativePath}";

                // Check Local Cache first for legacy files
                try
                {
                    var cacheDir = CloudLibraryCacheService.Instance.GetGameCacheDirectory(game.Name);
                    if (Directory.Exists(cacheDir))
                    {
                        string cachedPrimary = Path.Combine(cacheDir, checksumFileName);
                        if (File.Exists(cachedPrimary))
                        {
                            var modTime = await GetRemoteFileModTime(primaryRemotePath, provider);
                            if (modTime.HasValue)
                            {
                                var localInfo = new FileInfo(cachedPrimary);
                                if (Math.Abs((localInfo.LastWriteTime - modTime.Value).TotalSeconds) < 2)
                                {
                                    DebugConsole.WriteSuccess($"Cache HIT: Using local cached checksum for {checksumFileName}");
                                    string json = await File.ReadAllTextAsync(cachedPrimary);
                                    var data = JsonConvert.DeserializeObject<GameUploadData>(json);
                                    return data?.PlayTime;
                                }
                            }
                        }
                    }
                }
                catch { }

                // Check if cloud save exists (Directory check)
                bool cloudExists = await _rcloneOps.CheckCloudSaveExistsAsync(remoteBasePath, provider);
                if (!cloudExists)
                {
                    DebugConsole.WriteInfo("Cloud save doesn't exist");
                    return null;
                }

                // Download legacy checksum file
                string tempFolder = Path.Combine(Path.GetTempPath(), $"SaveTracker_SmartSync_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempFolder);

                try
                {
                    string checksumLocalPath = Path.Combine(tempFolder, relativeChecksumPath);
                    string? localDir = Path.GetDirectoryName(checksumLocalPath);
                    if (!string.IsNullOrEmpty(localDir)) Directory.CreateDirectory(localDir);

                    var transferService = new RcloneTransferService();
                    bool downloaded = await transferService.DownloadFileWithRetry(primaryRemotePath, checksumLocalPath, checksumFileName, provider);

                    if (!downloaded)
                    {
                        downloaded = await transferService.DownloadFileWithRetry(legacyRemotePath, checksumLocalPath, legacyChecksumName, provider);
                    }

                    if (downloaded && File.Exists(checksumLocalPath))
                    {
                        string json = await File.ReadAllTextAsync(checksumLocalPath);
                        var cloudData = JsonConvert.DeserializeObject<GameUploadData>(json);
                        return cloudData?.PlayTime;
                    }
                }
                finally
                {
                    try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); } catch { }
                }

                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Legacy cloud check failed");
                return null;
            }
        }

        /// <summary>
        /// Peeks at the .sta archive metadata using binary-safe execution
        /// </summary>
        public async Task<GameUploadData?> PeekCloudMetadataAsync(
            string remoteBasePath,
            CloudProvider provider,
            string? profileId)
        {
            try
            {
                // Determine archive filename based on profile
                string archiveFileName = _rcloneOps.GetArchiveFileName(profileId);
                string remoteArchivePath = $"{remoteBasePath}/{archiveFileName}";

                // Fast-fail if archive doesn't exist
                if (!await _rcloneOps.RemoteFileExistsAsync(remoteArchivePath, provider))
                {
                    return null;
                }

                string configPath = RclonePathHelper.GetConfigPath(provider);
                var executor = new RcloneExecutor();

                // Peek first 65,664 bytes (128B header + 64KB metadata buffer)
                byte[] bytes = await executor.ExecuteRcloneBinaryAsync(
                    $"cat \"{remoteArchivePath}\" --count 65664 --config \"{configPath}\"",
                    TimeSpan.FromSeconds(10),
                    65664
                );

                if (bytes == null || bytes.Length < 128)
                {
                    return null;
                }

                using (var ms = new MemoryStream(bytes))
                {
                    return await _archiver.PeekMetadataAsync(ms);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteDebug($"Peek failed: {ex.Message}");
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
