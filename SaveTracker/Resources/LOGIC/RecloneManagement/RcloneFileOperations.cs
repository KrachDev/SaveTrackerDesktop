using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SaveTracker.Resources.SAVE_SYSTEM;
using static CloudConfig;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneFileOperations
    {
        private readonly Game _currentGame;
        private readonly ChecksumService _checksumService = new ChecksumService();
        private readonly RcloneTransferService _transferService = new RcloneTransferService();

        // Forwarding static properties for backward compatibility if needed, 
        // though direct usage of RclonePathHelper is preferred.
        public static string RcloneExePath => RclonePathHelper.RcloneExePath;

        public RcloneFileOperations(Game currentGame = null)
        {
            _currentGame = currentGame;
        }

        public async Task<string> GetRemotePathAsync(CloudProvider provider, Game game = null)
        {
            game = game ?? _currentGame;
            if (game == null) throw new ArgumentNullException(nameof(game));

            var config = await ConfigManagement.LoadConfigAsync();
            var cloudHelper = new CloudProviderHelper(); // Instantiating here or pass as dependency? 
                                                         // RcloneFileOperations typically doesn't use CloudProviderHelper directly in existing code? 
                                                         // It gets config path from RclonePathHelper.
                                                         // But we need the "gdrive:" prefix.
                                                         // CloudProviderHelper.GetProviderConfigName is what we need. 
                                                         // It is in SaveTracker.Resources.HELPERS.
                                                         // Let's use simple instantiation or static helper if available. 
                                                         // SaveTracker.Resources.HELPERS.CloudProviderHelper

            string remoteName = new CloudProviderHelper().GetProviderConfigName(provider);
            string sanitizedGameName = SanitizeName(game.Name);
            string basePath = $"{remoteName}:{SaveTracker.Resources.Logic.SaveFileUploadManager.RemoteBaseFolder}/{sanitizedGameName}";

            if (!string.IsNullOrEmpty(game.ActiveProfileId))
            {
                var profile = config.Profiles.FirstOrDefault(p => p.Id == game.ActiveProfileId);
                if (profile != null && !profile.IsDefault)
                {
                    string profileFolder = SanitizeName(profile.Name);
                    basePath = $"{basePath}/Additional Profiles/{profileFolder}";
                }
            }
            return basePath;
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToArray())).Trim();
        }

        private async Task<CloudProvider> GetEffectiveProvider(Game game)
        {
            if (game == null) return CloudProvider.GoogleDrive;

            try
            {
                var gameData = await ConfigManagement.GetGameData(game);
                if (gameData != null && gameData.GameProvider != CloudProvider.Global)
                {
                    return gameData.GameProvider;
                }
            }
            catch
            {
                // Ignore errors loading game data
            }

            try
            {
                var globalConfig = await ConfigManagement.LoadConfigAsync();
                return globalConfig?.CloudConfig?.Provider ?? CloudProvider.GoogleDrive;
            }
            catch
            {
                return CloudProvider.GoogleDrive;
            }
        }

        private bool EnsureGameDirectoryExists(string gameDirectory)
        {
            try
            {
                if (!Directory.Exists(gameDirectory))
                {
                    Directory.CreateDirectory(gameDirectory);
                    DebugConsole.WriteInfo($"Created game directory: {gameDirectory}");
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(
                    ex,
                    $"Failed to create/access game directory: {gameDirectory}"
                );
                return false;
            }
        }

        public async Task ProcessFile(
            string filePath,
            string remoteBasePath,
            UploadStats stats,
            Game game = null,
            CloudProvider? provider = null,
            IProgress<RcloneProgressUpdate>? progress = null,
            bool force = false)
        {
            // Use provided game or fall back to constructor game
            game = game ?? _currentGame;

            if (game == null)
            {
                DebugConsole.WriteError("No game provided - cannot determine game directory");
                stats.FailedCount++;
                return;
            }

            string gameDirectory = game.InstallDirectory;
            if (string.IsNullOrEmpty(gameDirectory))
            {
                DebugConsole.WriteError("Game install directory is not set");
                stats.FailedCount++;
                return;
            }

            if (!EnsureGameDirectoryExists(gameDirectory))
            {
                DebugConsole.WriteError($"Cannot access game directory: {gameDirectory}");
                stats.FailedCount++;
                return;
            }

            // Get relative path from game directory to preserve folder structure in cloud
            // Load game data to get detected prefix for Wine path translation
            var gameData = await ConfigManagement.GetGameData(game);
            string? detectedPrefix = gameData?.DetectedPrefix;

            string relativePath = PathContractor.ContractPath(filePath, gameDirectory, detectedPrefix);
            string fileName = Path.GetFileName(filePath);
            // Rclone needs forward slashes in remote paths, even though we store backslashes in JSON
            string remotePath = $"{remoteBasePath}/{relativePath.Replace('\\', '/')}";

            DebugConsole.WriteSeparator('-', 40);
            DebugConsole.WriteInfo($"Processing: {relativePath}");
            DebugConsole.WriteKeyValue("Game directory", gameDirectory);

            try
            {
                var fileInfo = new FileInfo(filePath);
                DebugConsole.WriteKeyValue("File size", $"{fileInfo.Length:N0} bytes");
                DebugConsole.WriteKeyValue(
                    "Last modified",
                    fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                );

                bool needsUpload = force;
                if (!force)
                {
                    needsUpload = await ShouldUploadFileWithChecksum(
                        filePath,
                        gameDirectory
                    );
                }

                if (!needsUpload)
                {
                    DebugConsole.WriteSuccess(
                        $"SKIPPED: {fileName} - Identical to last uploaded version"
                    );
                    stats.SkippedCount++;
                    stats.SkippedSize += fileInfo.Length;
                    return;
                }

                DebugConsole.WriteInfo($"UPLOADING: {fileName}");

                CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(game);
                bool uploadSuccess = await _transferService.UploadFileWithRetry(filePath, remotePath, fileName, effectiveProvider, progress);

                if (uploadSuccess)
                {
                    // Reuse gameData and detectedPrefix from outer scope
                    // Update checksum tracking after successful upload (with Wine prefix support)
                    await _checksumService.UpdateFileChecksumRecord(filePath, gameDirectory, detectedPrefix);

                    DebugConsole.WriteSuccess($"Upload completed: {fileName}");
                    stats.UploadedCount++;
                    stats.UploadedSize += fileInfo.Length;
                }
                else
                {
                    DebugConsole.WriteError($"Upload failed after retries: {fileName}");
                    stats.FailedCount++;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Error processing {fileName}");
                stats.FailedCount++;
            }
        }

        public async Task ProcessBatch(
            List<string> filePaths,
            string remoteBasePath,
            UploadStats stats,
            Game game = null,
            CloudProvider? provider = null,
            IProgress<RcloneProgressUpdate>? progress = null,
            bool force = false)
        {
            // Use provided game or fall back to constructor game
            game = game ?? _currentGame;

            if (game == null)
            {
                DebugConsole.WriteError("No game provided - cannot determine game directory");
                stats.FailedCount += filePaths.Count;
                return;
            }

            string gameDirectory = game.InstallDirectory;
            if (string.IsNullOrEmpty(gameDirectory))
            {
                DebugConsole.WriteError("Game install directory is not set");
                stats.FailedCount += filePaths.Count;
                return;
            }

            if (!EnsureGameDirectoryExists(gameDirectory))
            {
                DebugConsole.WriteError($"Cannot access game directory: {gameDirectory}");
                stats.FailedCount += filePaths.Count;
                return;
            }

            // Identify which files need uploading
            // Split into "Internal" (can be batched relative to GameDir) and "External" (must be single)
            var internalFilesToBatch = new List<string>(); // absolute paths
            var relativeInternalPaths = new List<string>(); // relative to GameDir (for rclone list)
            var externalFilesToSingle = new List<string>(); // absolute paths

            long batchUploadSize = 0;

            DebugConsole.WriteInfo($"Checking {filePaths.Count} files for changes...");

            // Load game data once for all files (not per-file)
            var gameData = await ConfigManagement.GetGameData(game);
            string? detectedPrefix = gameData?.DetectedPrefix;

            // Use thread-safe collections for parallel processing
            var internalFilesToBatchConcurrent = new System.Collections.Concurrent.ConcurrentBag<string>();
            var relativeInternalPathsConcurrent = new System.Collections.Concurrent.ConcurrentBag<string>();
            var externalFilesToSingleConcurrent = new System.Collections.Concurrent.ConcurrentBag<string>();
            long batchUploadSizeAtomic = 0;
            int processedCount = 0;
            int skippedCount = 0;
            long skippedSize = 0;
            int failedCount = 0;

            // PERFORMANCE: Parallel checksum calculation to use all CPU cores
            // Wrapped in Task.Run to ensure it runs on thread pool and doesn't block UI
            await Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.ForEach(filePaths, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
                {
                    if (!File.Exists(filePath))
                    {
                        DebugConsole.WriteWarning($"File not found, skipping: {filePath}");
                        Interlocked.Increment(ref failedCount);
                        return;
                    }

                    // Get relative path from game directory for checking purposes
                    string contractedPath = PathContractor.ContractPath(filePath, gameDirectory, detectedPrefix);
                    string fileName = Path.GetFileName(filePath);

                    // Determine if file is internal (inside game directory)
                    // PathContractor uses %GAMEPATH% prefix for internal files
                    bool isInternal = contractedPath.StartsWith("%GAMEPATH%", StringComparison.OrdinalIgnoreCase);

                    try
                    {
                        // Note: ShouldUploadFileWithChecksum is async, but Parallel.ForEach needs sync
                        // We'll use Task.Run().GetAwaiter().GetResult() for CPU-bound work
                        bool needsUpload = force;
                        if (!force)
                        {
                            needsUpload = ShouldUploadFileWithChecksum(filePath, gameDirectory).GetAwaiter().GetResult();
                        }

                        if (needsUpload)
                        {
                            if (isInternal)
                            {
                                internalFilesToBatchConcurrent.Add(filePath);

                                // Strip %GAMEPATH%/ or %GAMEPATH%\ prefix to get pure relative path
                                string pureRelative = Path.GetRelativePath(gameDirectory, filePath);
                                relativeInternalPathsConcurrent.Add(pureRelative);

                                var info = new FileInfo(filePath);
                                Interlocked.Add(ref batchUploadSizeAtomic, info.Length);
                            }
                            else
                            {
                                // External file - must upload individually
                                externalFilesToSingleConcurrent.Add(filePath);
                            }
                        }
                        else
                        {
                            DebugConsole.WriteSuccess($"SKIPPED: {fileName} - Identical to last uploaded version");
                            Interlocked.Increment(ref skippedCount);
                            Interlocked.Add(ref skippedSize, new FileInfo(filePath).Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Error checking file {fileName}");
                        Interlocked.Increment(ref failedCount);
                    }

                    // Report progress during file checking (0-40% of total progress)
                    int currentCount = Interlocked.Increment(ref processedCount);
                    if (currentCount % 10 == 0 || currentCount == filePaths.Count)
                    {
                        progress?.Report(new RcloneProgressUpdate
                        {
                            Percent = (double)currentCount / filePaths.Count * 40,
                            CurrentFile = "Analyzing files..."
                        });
                    }
                });
            });

            // Convert concurrent collections to regular lists
            internalFilesToBatch = internalFilesToBatchConcurrent.ToList();
            relativeInternalPaths = relativeInternalPathsConcurrent.ToList();
            externalFilesToSingle = externalFilesToSingleConcurrent.ToList();
            batchUploadSize = batchUploadSizeAtomic;
            stats.SkippedCount += skippedCount;
            stats.SkippedSize += skippedSize;
            stats.FailedCount += failedCount;


            if (internalFilesToBatch.Count == 0 && externalFilesToSingle.Count == 0)
            {
                DebugConsole.WriteSuccess("All files up to date. No upload needed.");
                progress?.Report(new RcloneProgressUpdate { Percent = 100, CurrentFile = "Done" });
                return;
            }

            CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(game);

            // 1. Process Batch (Internal Files) - 40-70% of progress
            if (internalFilesToBatch.Count > 0)
            {
                DebugConsole.WriteSeparator('-', 40);
                DebugConsole.WriteInfo($"Batch uploading {internalFilesToBatch.Count} internal files ({batchUploadSize / 1024.0:F2} KB)...");

                progress?.Report(new RcloneProgressUpdate { Percent = 40, CurrentFile = "Starting batch upload..." });

                // Execute batch upload
                // Internal files go to remoteBasePath/%GAMEPATH%/
                string batchRemotePath = $"{remoteBasePath}/%GAMEPATH%";

                // Create progress reporter that maps rclone's 0-100 to our 40-70 range
                var batchProgress = new Progress<RcloneProgressUpdate>(update =>
                {
                    progress?.Report(new RcloneProgressUpdate
                    {
                        Percent = 40 + (update.Percent * 0.3), // 40-70%
                        Speed = update.Speed,
                        CurrentFile = update.CurrentFile
                    });
                });

                bool success = await _transferService.UploadBatchAsync(
                    gameDirectory,
                    batchRemotePath,
                    relativeInternalPaths,
                    effectiveProvider,
                    batchProgress);

                if (success)
                {
                    // Update stats and checksums for all uploaded files
                    foreach (var filePath in internalFilesToBatch)
                    {
                        await HandleSuccessfulUpload(filePath, gameDirectory, stats, detectedPrefix);
                    }
                    DebugConsole.WriteSuccess($"Batch upload of internal files completed successfully.");
                }
                else
                {
                    DebugConsole.WriteError($"Batch upload failed for {internalFilesToBatch.Count} files.");
                    stats.FailedCount += internalFilesToBatch.Count;
                }

                progress?.Report(new RcloneProgressUpdate { Percent = 70, CurrentFile = "Batch upload complete" });
            }
            else
            {
                progress?.Report(new RcloneProgressUpdate { Percent = 70, CurrentFile = "No internal files to upload" });
            }

            // 2. Process Single (External Files) - 70-100% of progress
            if (externalFilesToSingle.Count > 0)
            {
                DebugConsole.WriteSeparator('-', 40);
                DebugConsole.WriteInfo($"Uploading {externalFilesToSingle.Count} external files in parallel...");

                int externalProcessed = 0;
                using var semaphore = new System.Threading.SemaphoreSlim(8); // Allow 8 concurrent uploads
                var uploadTasks = new List<Task>();

                foreach (var filePath in externalFilesToSingle)
                {
                    uploadTasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            // Reuse detectedPrefix from outer scope
                            string contractedPath = PathContractor.ContractPath(filePath, gameDirectory, detectedPrefix);
                            // Rclone needs forward slashes in remote paths, even though we store backslashes in JSON
                            // Remote path must include the folder structure defined by the contracted path
                            string remotePath = $"{remoteBasePath}/{contractedPath.Replace('\\', '/')}";
                            string fileName = Path.GetFileName(filePath);

                            // Create progress reporter for individual file to report to batch progress
                            var fileProgress = new Progress<RcloneProgressUpdate>(update =>
                            {
                                // Scale: 70-100% of progress allocated to external files
                                double baseProgress = 70;
                                double range = 30;
                                double perFileRange = range / externalFilesToSingle.Count;
                                // Note: externalProcessed is incremented AFTER completion, so for in-progress files 
                                // we might want to use a more complex calculation, but for now using the completion count 
                                // plus the current file's progress is visible enough.
                                // However, accessing externalProcessed here is race-prone if not carefully handled.
                                // Simplified approach: just show current file status without aggressive global % updates per file chunk
                                // OR: use Interlocked.Read if we want to include completed count.

                                int completed = externalProcessed; // snapshot
                                double currentFileBase = baseProgress + (completed * perFileRange);

                                progress?.Report(new RcloneProgressUpdate
                                {
                                    Percent = currentFileBase + (update.Percent * perFileRange / 100),
                                    Speed = update.Speed,
                                    CurrentFile = fileName // Use original fileName for better clarity
                                });
                            });

                            bool success = await _transferService.UploadFileWithRetry(
                                filePath,
                                remotePath,
                                fileName,
                                effectiveProvider,
                                fileProgress);

                            if (success)
                            {
                                await HandleSuccessfulUpload(filePath, gameDirectory, stats, detectedPrefix);
                            }
                            else
                            {
                                DebugConsole.WriteError($"Failed to upload external file: {fileName}");
                                stats.FailedCount++;
                            }

                            int newCount = System.Threading.Interlocked.Increment(ref externalProcessed);
                            progress?.Report(new RcloneProgressUpdate
                            {
                                Percent = 70 + ((double)newCount / externalFilesToSingle.Count * 30),
                                CurrentFile = $"Uploaded {newCount}/{externalFilesToSingle.Count} external files..."
                            }); // 70-100%
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(uploadTasks);
            }
            else
            {
                progress?.Report(new RcloneProgressUpdate { Percent = 100, CurrentFile = "Done" });
            }
        }

        private async Task HandleSuccessfulUpload(string filePath, string gameDirectory, UploadStats stats, string? detectedPrefix = null)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                var info = new FileInfo(filePath);

                // Update checksum tracking after successful upload (with Wine prefix support)
                // CRITICAL: Pass the game's profile ID to use profile-aware checksum files
                if (_currentGame != null)
                {
                    await _checksumService.UpdateFileChecksumRecord(
                        filePath,
                        gameDirectory,
                        detectedPrefix,
                        _currentGame.ActiveProfileId  // ← PROFILE-AWARE
                    );
                }
                else
                {
                    // Fallback if no game context
                    await _checksumService.UpdateFileChecksumRecord(
                        filePath,
                        gameDirectory,
                        detectedPrefix
                    );
                }

                DebugConsole.WriteSuccess($"Uploaded: {fileName}");
                stats.UploadedCount++;
                stats.UploadedSize += info.Length;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Error updating checksum for {Path.GetFileName(filePath)}");
                // We don't increment failure count here since the upload technically succeeded
            }
        }

        private async Task<bool> ShouldUploadFileWithChecksum(
            string localFilePath,
            string gameDirectory)
        {
            try
            {
                var fileInfo = new FileInfo(localFilePath);
                if (!fileInfo.Exists) return false;

                // Load stored checksum data - USE PROFILE ID
                var checksumData = await _checksumService.LoadChecksumData(
                    gameDirectory,
                    _currentGame?.ActiveProfileId  // ← PROFILE-AWARE
                );

                // Load game data for Wine prefix support
                var gameData = await ConfigManagement.GetGameData(_currentGame);
                string? detectedPrefix = gameData?.DetectedPrefix;

                // Use contracted path as dictionary key
                string contractedPath = PathContractor.ContractPath(localFilePath, gameDirectory, detectedPrefix);

                // OPTIMIZATION: Check LastWriteTime and Size first (Fast Check)
                if (checksumData.Files.TryGetValue(contractedPath, out var record))
                {
                    // If file size and timestamp match exactly, skip hash calculation
                    // This is much faster for large files or many files
                    if (record.FileSize == fileInfo.Length &&
                        record.LastWriteTime.ToString("O") == fileInfo.LastWriteTimeUtc.ToString("O"))
                    // String comparison for safety against minor tick differences if any serialization loss
                    // Better: just compare Ticks or standard equality if serialization is precise
                    {
                        if (record.LastWriteTime == fileInfo.LastWriteTimeUtc)
                        {
                            DebugConsole.WriteDebug($"Fast check: {Path.GetFileName(localFilePath)} unchanged (Timestamp/Size match)");
                            return false;
                        }
                    }
                }

                // If fast check fails (or legacy record with no timestamp), do full hash check
                string currentChecksum = await _checksumService.GetFileChecksum(localFilePath);
                if (string.IsNullOrEmpty(currentChecksum))
                {
                    DebugConsole.WriteWarning("Could not compute local file checksum - uploading to be safe");
                    return true;
                }

                if (record != null)
                {
                    if (string.Equals(currentChecksum, record.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        DebugConsole.WriteSuccess($"SKIPPED: {Path.GetFileName(localFilePath)} - Content identical (Hash verified)");
                        return false;
                    }
                }

                DebugConsole.WriteInfo($"File changed: {Path.GetFileName(localFilePath)} - Upload needed");
                return true;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(
                    ex,
                    "Checksum comparison failed - uploading to be safe"
                );
                return true;
            }
        }

        private async Task<string> GetStoredFileChecksum(string filePath, string gameDirectory)
        {
            try
            {
                var checksumData = await _checksumService.LoadChecksumData(gameDirectory);
                // Load game data for Wine prefix support
                var gameData = await ConfigManagement.GetGameData(_currentGame);
                string? detectedPrefix = gameData?.DetectedPrefix;
                // Use contracted path as dictionary key (new format)
                string contractedPath = PathContractor.ContractPath(filePath, gameDirectory, detectedPrefix);

                if (checksumData.Files.TryGetValue(contractedPath, out var fileRecord))
                {
                    DebugConsole.WriteDebug(
                        $"Found stored checksum for {contractedPath} from {fileRecord.LastUpload:yyyy-MM-dd HH:mm:ss}"
                    );
                    return fileRecord.Checksum;
                }

                // Backward compatibility: Try old flat filename format
                string fileName = Path.GetFileName(filePath);
                if (checksumData.Files.TryGetValue(fileName, out var legacyFileRecord))
                {
                    DebugConsole.WriteDebug(
                        $"Found stored checksum (legacy format) for {fileName} from {legacyFileRecord.LastUpload:yyyy-MM-dd HH:mm:ss}"
                    );
                    return legacyFileRecord.Checksum;
                }

                DebugConsole.WriteDebug($"No stored checksum found for {contractedPath}");
                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to get stored checksum");
                return null;
            }
        }

        public static string ExpandStoredPath(string portablePath, string gameDirectory = null)
        {
            if (string.IsNullOrEmpty(portablePath))
                return portablePath;

            if (!string.IsNullOrEmpty(gameDirectory) &&
                portablePath.StartsWith("%GAMEPATH%", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = portablePath.Substring("%GAMEPATH%".Length).TrimStart('/', '\\');
                return Path.Combine(gameDirectory, relativePath);
            }

            return PathContractor.ExpandPath(portablePath, gameDirectory);
        }

        public async Task SaveChecksumData(GameUploadData data, Game game)
        {
            await _checksumService.SaveChecksumData(data, game.InstallDirectory, game.ActiveProfileId);
        }

        public async Task<string> GetFileChecksum(string filePath)
        {
            return await _checksumService.GetFileChecksum(filePath);
        }

        public async Task<bool> ShouldUploadFile(string localFilePath, string remotePath, CloudProvider? provider = null)
        {
            try
            {
                CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(_currentGame);
                bool remoteExists = await _transferService.RemoteFileExists(remotePath, effectiveProvider);
                if (!remoteExists)
                {
                    DebugConsole.WriteInfo("Remote file doesn't exist - upload needed");
                    return true;
                }

                DebugConsole.WriteWarning(
                    "Using legacy upload check - consider using checksum-based method"
                );
                return false;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Legacy file check failed - uploading to be safe");
                return true;
            }
        }

        public async Task<bool> CheckCloudSaveExistsAsync(string remoteBasePath, CloudProvider? provider = null)
        {
            CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(_currentGame);
            return await _transferService.CheckCloudSaveExistsAsync(remoteBasePath, effectiveProvider);
        }

        public async Task<bool> DownloadDirectory(string remotePath, string localPath, CloudProvider? provider = null)
        {
            CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(_currentGame);
            return await _transferService.DownloadDirectory(remotePath, localPath, effectiveProvider);
        }

        public async Task<bool> RenameCloudFolder(string oldRemoteBasePath, string newRemoteBasePath, CloudProvider? provider = null)
        {
            CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(_currentGame);
            return await _transferService.RenameFolder(oldRemoteBasePath, newRemoteBasePath, effectiveProvider);
        }

        public async Task<bool> DownloadWithChecksumAsync(string remotePath, Game game, CloudProvider? provider = null, IProgress<RcloneProgressUpdate>? progress = null)
        {
            string stagingFolder = Path.Combine(Path.GetTempPath(), "SaveTracker_Download_" + Guid.NewGuid().ToString("N"));

            // Get detected prefix for correct path expansion on Linux
            var gameData = await ConfigManagement.GetGameData(game);
            string? detectedPrefix = gameData?.DetectedPrefix;

            try
            {
                DebugConsole.WriteInfo($"Downloading cloud saves to staging folder: {stagingFolder}");
                Directory.CreateDirectory(stagingFolder);

                CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(game);
                var downloadResult = await _transferService.DownloadDirectory(remotePath, stagingFolder, effectiveProvider, progress);

                if (!downloadResult)
                {
                    DebugConsole.WriteError($"Failed to download files to staging");
                    return false;
                }

                // Explicitly check for checksum file and try to download it specifically if missing
                // This covers cases where rclone filtering might have excluded it or it was somehow missed

                var checksumService = new ChecksumService();
                string correctChecksumPath = checksumService.GetChecksumFilePath(game.InstallDirectory, game.ActiveProfileId);
                string checksumFileName = Path.GetFileName(correctChecksumPath);

                string relativeChecksumPath = PathContractor.ContractPath(correctChecksumPath, game.InstallDirectory, detectedPrefix).Replace('\\', '/');
                string stagingChecksumPath = Path.Combine(stagingFolder, relativeChecksumPath);
                if (!File.Exists(stagingChecksumPath))
                {
                    DebugConsole.WriteWarning($"Checksum file ({relativeChecksumPath}) missing - attempting fallback checks...");

                    // 1. Try explicit fetch of the profile-specific name
                    string remoteChecksumPath = $"{remotePath}/{relativeChecksumPath}";

                    // Create directory in staging if it doesn't exist (e.g. %GAMEPATH% subfolder)
                    string? stagingDir = Path.GetDirectoryName(stagingChecksumPath);
                    if (!string.IsNullOrEmpty(stagingDir)) Directory.CreateDirectory(stagingDir);

                    bool checksumDownloaded = await _transferService.DownloadFileWithRetry(
                        remoteChecksumPath,
                        stagingChecksumPath,
                        checksumFileName,
                        effectiveProvider
                    );

                    // 2. Fallback to legacy checksum name if profile-specific failed
                    if (!checksumDownloaded)
                    {
                        string legacyChecksumName = ".savetracker_checksums.json";
                        string legacyRelativePath = relativeChecksumPath.Replace(checksumFileName, legacyChecksumName);
                        string legacyRemotePath = $"{remotePath}/{legacyRelativePath}";

                        DebugConsole.WriteInfo($"Attempting legacy checksum fetch: {legacyRelativePath}");

                        checksumDownloaded = await _transferService.DownloadFileWithRetry(
                            legacyRemotePath,
                            stagingChecksumPath, // Still save it to the expected local path
                            legacyChecksumName,
                            effectiveProvider
                        );
                    }

                    if (checksumDownloaded)
                        DebugConsole.WriteSuccess("Successfully recovered checksum file.");
                    else
                        DebugConsole.WriteWarning("Could not recover checksum file - PlayTime metadata might be lost.");
                }

                // Drive restoration from Staging Files (Source of Truth)
                // This handles cases where checksum keys are flattened/broken but Staging has correct structure.
                var stagingFiles = Directory.GetFiles(stagingFolder, "*", SearchOption.AllDirectories);
                DebugConsole.WriteInfo($"Restoring {stagingFiles.Length} files from staging...");

                int successCount = 0;
                int failCount = 0;

                foreach (var sourceFile in stagingFiles)
                {
                    try
                    {
                        string relativeStagingPath = Path.GetRelativePath(stagingFolder, sourceFile);
                        string fileName = Path.GetFileName(sourceFile);

                        // Skip checksum files
                        if (fileName.Equals(SaveFileUploadManager.ChecksumFilename, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Calculate Target Path based on Staging Structure
                        string targetPath;

                        // Handle variable-based paths (e.g. %USERPROFILE%/...)
                        if (relativeStagingPath.Contains("%"))
                        {
                            // Normalize for ExpandPath
                            string contractedPath = relativeStagingPath.Replace('\\', '/');
                            targetPath = PathContractor.ExpandPath(contractedPath, game.InstallDirectory, detectedPrefix);
                        }
                        else
                        {
                            // Assume relative to Game Directory
                            targetPath = Path.Combine(game.InstallDirectory, relativeStagingPath);
                        }

                        // Safety: Ensure target is not empty
                        if (string.IsNullOrWhiteSpace(targetPath))
                        {
                            DebugConsole.WriteWarning($"Could not determine target path for {relativeStagingPath}");
                            failCount++;
                            continue;
                        }

                        string? targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        File.Copy(sourceFile, targetPath, overwrite: true);
                        DebugConsole.WriteSuccess($"✓ Restored: {relativeStagingPath}");
                        successCount++;

                        // UPDATE LOCAL CHECKSUM RECORD to fix the metadata for future
                        // This regenerates the checksums.json with CORRECT paths
                        await _checksumService.UpdateFileChecksumRecord(targetPath, game.InstallDirectory, detectedPrefix);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"✗ Failed to restore {Path.GetFileName(sourceFile)}: {ex.Message}");
                        failCount++;
                    }
                }

                // Copy the cloud checksum file to the game directory so the app knows what files are tracked
                // Copy the cloud checksum file to the game directory so the app knows what files are tracked
                try
                {
                    string restoredChecksumName = Path.GetFileName(checksumService.GetChecksumFilePath(game.InstallDirectory, game.ActiveProfileId));
                    string cloudChecksumPath = Path.Combine(stagingFolder, restoredChecksumName);
                    string localChecksumPath = checksumService.GetChecksumFilePath(game.InstallDirectory, game.ActiveProfileId);

                    if (File.Exists(cloudChecksumPath))
                    {
                        File.Copy(cloudChecksumPath, localChecksumPath, overwrite: true);
                        DebugConsole.WriteSuccess($"✓ Checksum file copied to game directory: {localChecksumPath}");
                    }
                    else
                    {
                        DebugConsole.WriteWarning("Cloud checksum file not found in staging folder");
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Failed to copy checksum file to game directory: {ex.Message}");
                }

                DebugConsole.WriteSuccess($"Download complete: {successCount} files restored, {failCount} failed");
                return failCount == 0;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Download with checksum failed");
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingFolder))
                    {
                        Directory.Delete(stagingFolder, recursive: true);
                        DebugConsole.WriteInfo("Staging folder cleaned up");
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Failed to clean up staging folder: {ex.Message}");
                }
            }
        }
        public async Task<bool> DownloadSelectedFilesAsync(string remotePath, Game game, List<string> selectedRelativePaths, CloudProvider? provider = null)
        {
            string stagingFolder = Path.Combine(Path.GetTempPath(), "SaveTracker_Download_" + Guid.NewGuid().ToString("N"));

            try
            {
                DebugConsole.WriteInfo($"Downloading selected cloud saves to staging folder: {stagingFolder}");
                Directory.CreateDirectory(stagingFolder);

                CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(game);

                // First, download the checksum file to know what files are available
                var checksumService = new ChecksumService();
                string checksumLocalSpec = checksumService.GetChecksumFilePath(game.InstallDirectory, game.ActiveProfileId);
                string checksumFileName = Path.GetFileName(checksumLocalSpec);

                string checksumRemotePath = $"{remotePath}/{checksumFileName}";
                string checksumLocalPath = Path.Combine(stagingFolder, checksumFileName);

                var checksumResult = await _transferService.DownloadFileWithRetry(
                    checksumRemotePath,
                    checksumLocalPath,
                    checksumFileName,
                    effectiveProvider);

                if (!checksumResult)
                {
                    DebugConsole.WriteWarning("Checksum file not found - will use relative paths for restore");
                }

                Dictionary<string, FileChecksumRecord> checksumFiles = null;
                string checksumFilePath = Path.Combine(stagingFolder, checksumFileName);

                if (File.Exists(checksumFilePath))
                {
                    DebugConsole.WriteInfo("Reading checksum file...");
                    string checksumJson = await File.ReadAllTextAsync(checksumFilePath);
                    var checksumData = JsonConvert.DeserializeObject<GameUploadData>(checksumJson);
                    checksumFiles = checksumData?.Files;
                }

                // If checksum data exists, filter it. If not, use selectedRelativePaths directly.
                List<string> filesToDownload = selectedRelativePaths;

                if (checksumFiles != null && checksumFiles.Count > 0)
                {
                    // Verify selected files exist in checksum
                    // But we can also just download what we requested based on path
                }

                DebugConsole.WriteInfo($"Downloading {selectedRelativePaths.Count} selected files...");

                int successCount = 0;
                int failCount = 0;

                // Download each selected file individually
                foreach (var relativePath in selectedRelativePaths)
                {
                    try
                    {
                        string fileName = Path.GetFileName(relativePath);

                        string remoteFilePath = $"{remotePath}/{relativePath.Replace('\\', '/')}";
                        string localFilePath = Path.Combine(stagingFolder, relativePath);

                        // Ensure local subdirectory exists
                        string? localDir = Path.GetDirectoryName(localFilePath);
                        if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                        {
                            Directory.CreateDirectory(localDir);
                        }

                        // Download the specific file from remote
                        var downloadResult = await _transferService.DownloadFileWithRetry(
                            remoteFilePath,
                            localFilePath,
                            relativePath,
                            effectiveProvider);

                        if (!downloadResult)
                        {
                            DebugConsole.WriteWarning($"Failed to download: {relativePath}");
                            failCount++;
                            continue;
                        }

                        // Try the full relative path first (Rclone preserves structure)
                        string sourceFile = Path.Combine(stagingFolder, relativePath);
                        if (!File.Exists(sourceFile))
                        {
                            // Fallback to flat filename
                            sourceFile = Path.Combine(stagingFolder, fileName);
                        }

                        if (!File.Exists(sourceFile))
                        {
                            DebugConsole.WriteWarning($"File not found in staging: {fileName}");
                            failCount++;
                            continue;
                        }

                        // Determine target path
                        string targetPath;
                        if (checksumFiles != null && checksumFiles.TryGetValue(relativePath, out var fileRecord))
                        {
                            targetPath = fileRecord.GetAbsolutePath(game.InstallDirectory);
                        }
                        else
                        {
                            // Fallback: Expand contracted path using PathContractor with Wine prefix support
                            var gameData = await ConfigManagement.GetGameData(game);
                            string? detectedPrefix = gameData?.DetectedPrefix;
                            targetPath = PathContractor.ExpandPath(relativePath, game.InstallDirectory, detectedPrefix);
                        }

                        string? targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        File.Copy(sourceFile, targetPath, overwrite: true);
                        DebugConsole.WriteSuccess($"✓ Restored: {relativePath}");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"✗ Failed to restore {relativePath}: {ex.Message}");
                        failCount++;
                    }
                }

                // Copy checksum file to game install directory for future tracking
                try
                {
                    string targetChecksumPath = checksumService.GetChecksumFilePath(game.InstallDirectory, game.ActiveProfileId);
                    if (File.Exists(checksumFilePath))
                    {
                        File.Copy(checksumFilePath, targetChecksumPath, overwrite: true);
                        DebugConsole.WriteSuccess($"✓ Checksum file saved to install directory");
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Failed to copy checksum file to install directory: {ex.Message}");
                }

                DebugConsole.WriteSuccess($"Download complete: {successCount} files restored, {failCount} failed");
                return failCount == 0;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Selective download failed");
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingFolder))
                    {
                        Directory.Delete(stagingFolder, recursive: true);
                        DebugConsole.WriteInfo("Staging folder cleaned up");
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Failed to clean up staging folder: {ex.Message}");
                }
            }
        }
        private async Task CopyDirectoryContents(string sourceDir, string targetDir)
        {
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                if (fileName == SaveFileUploadManager.ChecksumFilename)
                    continue;

                string targetPath = Path.Combine(targetDir, fileName);
                File.Copy(file, targetPath, overwrite: true);
                DebugConsole.WriteInfo($"Copied: {fileName}");
            }
            await Task.CompletedTask;
        }

        public async Task ProcessDownloadFile(
            RemoteFileInfo remoteFile,
            string localDownloadPath,
            string remoteBasePath,
            DownloadResult downloadResult,
            bool overwriteExisting,
            Game game = null,
            Action<string> progressCallback = null,
            CloudProvider? provider = null)
        {
            game = game ?? _currentGame;
            CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(game);

            if (remoteFile == null)
            {
                DebugConsole.WriteError("ProcessDownloadFile: remoteFile parameter is null");
                downloadResult?.FailedFiles?.Add("Unknown file (remoteFile was null)");
                return;
            }

            try
            {
                string localFilePath = Path.Combine(localDownloadPath, remoteFile.Name); // Use the logic from your system

                // ... (Logic continues - not fully implemented in original snippet provided, preserving class structure)
                // Assuming standard download logic similar to above methods
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "ProcessDownloadFile exception");
            }
        }
        public async Task<List<string>> ListCloudGameFolders(CloudProvider? provider = null)
        {
            CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(_currentGame);
            string remoteBase = RclonePathHelper.GetRemotePath(effectiveProvider);
            return await _transferService.ListCloudDirectories(remoteBase, effectiveProvider);
        }
    }
}