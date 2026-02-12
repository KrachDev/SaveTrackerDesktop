using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SaveTracker.Resources.Logic
{
    /// <summary>
    /// Manages the upload process for game save files to cloud storage
    /// </summary>
    public class SaveFileUploadManager(
        RcloneInstaller rcloneInstaller,
        CloudProviderHelper cloudProviderHelper,
        RcloneFileOperations rcloneFileOperations)
    {
        // Constants
        public const string ChecksumFilename = ".savetracker_checksums.json";
        public const string RemoteBaseFolder = "SaveTrackerCloudSave";

        // Dependencies
        private readonly RcloneInstaller _rcloneInstaller = rcloneInstaller ?? throw new ArgumentNullException(nameof(rcloneInstaller));
        private readonly CloudProviderHelper _cloudProviderHelper = cloudProviderHelper ?? throw new ArgumentNullException(nameof(cloudProviderHelper));
        private readonly RcloneFileOperations _rcloneFileOperations = rcloneFileOperations ?? throw new ArgumentNullException(nameof(rcloneFileOperations));
        private readonly RcloneConfigManager _configManager = new RcloneConfigManager();
        private readonly SaveArchiver _archiver = new SaveArchiver();

        // Properties
        private static string RcloneExePath => RclonePathHelper.GetRclonePath();

        // Caching
        private static (bool IsValid, DateTime LastChecked) _validationCache = (false, DateTime.MinValue);
        private static readonly TimeSpan ValidationCacheDuration = TimeSpan.FromMinutes(1);

        public static void InvalidateValidationCache()
        {
            _validationCache = (false, DateTime.MinValue);
            DebugConsole.WriteInfo("Validation cache invalidated.");
        }

        // Event for progress updates
        public event Action<UploadProgressInfo>? OnProgressChanged;
        public event Action<UploadResult>? OnUploadCompleted;
        public event Func<Task<bool>>? OnCloudConfigRequired;

        /// <summary>
        /// Main upload method
        /// </summary>
        public async Task<UploadResult> Upload(
            List<string> saveFiles,
            Game game,
            CloudProvider provider,
            CancellationToken cancellationToken = default,
            bool force = false)
        {
            var context = new UploadContext(saveFiles, game, provider);

            if (!await ValidateUploadAsync(context))
            {
                return new UploadResult
                {
                    Success = false,
                    Message = "Upload validation failed"
                };
            }

            return await ExecuteUploadAsync(context, cancellationToken, force);
        }

        #region Validation

        private async Task<bool> ValidateUploadAsync(UploadContext context)
        {
            DebugConsole.WriteList("Staging Files To Be Uploaded: ", context.SaveFiles);

            // Check if uploads are allowed for this game
            if (!context.Game.LocalConfig.Auto_Upload)
            {
                DebugConsole.WriteInfo("Upload bypassed - Auto upload disabled for this game");
                return false;
            }

            // Load app config properly
            var appConfig = await ConfigManagement.LoadConfigAsync();
            var provider = appConfig.CloudConfig.Provider;

            // Override provider if game has specific one
            var gameCloudConfig = context.Game.LocalConfig.CloudConfig;
            if (gameCloudConfig != null && !gameCloudConfig.UseGlobalSettings)
            {
                context.Provider = gameCloudConfig.Provider;
            }
            else
            {
                context.Provider = provider;
            }

            // Validate rclone installation and config (with caching)
            bool rcloneValid = false;
            bool configValid = false;

            // Check cache
            if (_validationCache.IsValid && (DateTime.UtcNow - _validationCache.LastChecked) < ValidationCacheDuration)
            {
                DebugConsole.WriteDebug("Using cached validation result (Valid)");
                return true;
            }

            rcloneValid = await _rcloneInstaller.RcloneCheckAsync(context.Provider);
            configValid = await _configManager.IsValidConfig(context.Provider);

            if (!rcloneValid || !File.Exists(RcloneExePath) || !configValid)
            {
                if (OnCloudConfigRequired != null)
                {
                    bool configured = await OnCloudConfigRequired.Invoke();
                    if (configured)
                    {
                        // Retry validation
                        rcloneValid = await _rcloneInstaller.RcloneCheckAsync(context.Provider);
                        configValid = await _configManager.IsValidConfig(context.Provider);
                        if (rcloneValid && File.Exists(RcloneExePath) && configValid)
                        {
                            _validationCache = (true, DateTime.UtcNow);
                            return true;
                        }
                    }
                }

                string error = "Rclone is not installed or configured.";
                DebugConsole.WriteError(error);
                return false;
            }

            _validationCache = (true, DateTime.UtcNow);
            return true;
        }

        #endregion

        #region Main Upload Execution

        private async Task<UploadResult> ExecuteUploadAsync(
            UploadContext context,
            CancellationToken cancellationToken,
            bool force = false)
        {
            var session = new UploadSession(context, _cloudProviderHelper);
            await session.InitializeAsync();

            var fileManager = new UploadFileManager(context.SaveFiles);

            // Pass the active profile ID to ChecksumFileManager so it uses profile-specific checksum files
            string? profileId = context.Game.ActiveProfileId;
            var checksumManager = new ChecksumFileManager(context.Game, profileId);

            // Prepare files
            fileManager.ValidateFiles();
            DebugConsole.WriteInfo("step 1 tracking ====================");
            await checksumManager.PrepareChecksumFileAsync(fileManager.ValidFiles);

            // Notify upload start
            ReportProgress(new UploadProgressInfo
            {
                Status = "Starting upload...",
                TotalFiles = fileManager.ValidFiles.Count,
                ProcessedFiles = 0
            });

            try
            {
                DebugConsole.WriteInfo("step 2 uploading ====================");
                await PerformUploadAsync(
                    session,
                    fileManager,
                    checksumManager,
                    cancellationToken,
                    force
                );

                session.Complete();
                DebugConsole.WriteInfo("step 3 ======================done");

                // Summary statistics block
                DebugConsole.WriteInfo("=================");
                DebugConsole.WriteInfo($"Total files tracked: {fileManager.ValidFiles.Count + (checksumManager.HasChecksumFile ? 1 : 0)}");
                DebugConsole.WriteInfo($"Total files uploaded: {session.Stats.UploadedCount}");
                DebugConsole.WriteInfo($"Total size: {FormatBytes(session.Stats.UploadedSize)}");
                DebugConsole.WriteInfo($"Total time: {session.TotalTime:mm\\:ss}");
                DebugConsole.WriteInfo("=================");

                var result = new UploadResult
                {
                    Success = true,
                    UploadedCount = session.Stats.UploadedCount,
                    SkippedCount = session.Stats.SkippedCount,
                    FailedCount = session.Stats.FailedCount,
                    TotalSize = session.Stats.UploadedSize,
                    Duration = session.TotalTime,
                    Message = $"Upload complete: {session.Stats.UploadedCount} uploaded, " +
                             $"{session.Stats.SkippedCount} skipped, {session.Stats.FailedCount} failed"
                };

                OnUploadCompleted?.Invoke(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                DebugConsole.WriteInfo("Upload operation was cancelled by user");
                return new UploadResult { Success = false, Message = "Upload cancelled by user" };
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Unexpected error during upload: {ex.Message}");
                return new UploadResult { Success = false, Message = ex.Message };
            }
            finally
            {
                checksumManager.Cleanup();
            }
        }

        private async Task PerformUploadAsync(
            UploadSession session,
            UploadFileManager fileManager,
            ChecksumFileManager checksumManager,
            CancellationToken cancellationToken,
            bool force = false)
        {
            ReportProgress(new UploadProgressInfo
            {
                Status = "Preparing archive (.sta)...",
                TotalFiles = fileManager.ValidFiles.Count,
                ProcessedFiles = 0
            });

            string tempArchivePath = Path.Combine(Path.GetTempPath(), $"upload_{Guid.NewGuid()}.sta");

            try
            {
                // 1. Load metadata
                var checksumService = new ChecksumService();
                var metadata = await checksumService.LoadChecksumData(
                    session.Context.Game.InstallDirectory,
                    session.Context.Game.ActiveProfileId
                );

                // 1b. Populate metadata with current files (CRITICAL for .sta header)
                // We must ensure the metadata in the archive header matches the files we are packing.
                if (metadata.Files == null) metadata.Files = new Dictionary<string, FileChecksumRecord>();

                foreach (var filePath in fileManager.ValidFiles)
                {
                    try
                    {
                        // Use existing ChecksumService logic to get checksum
                        string checksum = await checksumService.GetFileChecksum(filePath);
                        if (string.IsNullOrEmpty(checksum)) continue;

                        string portablePath = PathContractor.ContractPath(filePath, session.Context.Game.InstallDirectory, metadata.DetectedPrefix);
                        var fileInfo = new FileInfo(filePath);

                        metadata.Files[portablePath] = new FileChecksumRecord
                        {
                            Checksum = checksum,
                            LastUpload = DateTime.UtcNow,
                            FileSize = fileInfo.Length,
                            Path = portablePath,
                            LastWriteTime = fileInfo.LastWriteTimeUtc
                        };
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to prep metadata for {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }

                // 2. Pack the archive
                var packResult = await _archiver.PackAsync(
                    tempArchivePath,
                    fileManager.ValidFiles,
                    session.Context.Game.InstallDirectory,
                    metadata,
                    metadata.DetectedPrefix
                );

                if (!packResult.Success)
                {
                    throw new Exception($"Failed to bundle files: {packResult.Error}");
                }

                // 3. Determine archive filename
                string archiveFileName = _rcloneFileOperations.GetArchiveFileName(
                    session.Context.Game.ActiveProfileId
                );

                ReportProgress(new UploadProgressInfo
                {
                    Status = $"Uploading {archiveFileName}...",
                    CurrentFile = archiveFileName,
                    TotalFiles = 1,
                    ProcessedFiles = 0
                });

                // 4. Upload archive DIRECTLY (bypass ProcessFile to avoid path contraction)
                string remoteArchivePath = $"{session.RemoteBasePath}/{archiveFileName}";
                string configPath = RclonePathHelper.GetConfigPath(session.Context.Provider);
                var executor = new RcloneExecutor();

                DebugConsole.WriteInfo($"Uploading to: {remoteArchivePath}");

                var uploadResult = await executor.ExecuteRcloneCommand(
                    $"copyto \"{tempArchivePath}\" \"{remoteArchivePath}\" --config \"{configPath}\"",
                    TimeSpan.FromMinutes(5),
                    hideWindow: true
                );

                if (!uploadResult.Success)
                {
                    throw new Exception($"Archive upload failed: {uploadResult.Error}");
                }

                DebugConsole.WriteSuccess($"✓ {archiveFileName} uploaded successfully (Atomic)");
                session.Stats.UploadedCount = fileManager.ValidFiles.Count;
                session.Stats.UploadedSize = new FileInfo(tempArchivePath).Length;

                // 4b. Update checksum records using BATCH update
                // Collect file records that were just uploaded
                var updates = new Dictionary<string, FileChecksumRecord>();
                foreach (var filePath in fileManager.ValidFiles)
                {
                    string portablePath = PathContractor.ContractPath(filePath, session.Context.Game.InstallDirectory, metadata.DetectedPrefix);
                    if (metadata.Files != null && metadata.Files.TryGetValue(portablePath, out var record))
                    {
                        updates[portablePath] = record;
                    }
                }

                await checksumService.UpdateBatchChecksumRecords(
                    updates,
                    session.Context.Game.InstallDirectory,
                    session.Context.Game.ActiveProfileId
                );

                // 5. Upload icon & 6. Legacy cleanup (PARALLEL)
                DebugConsole.WriteDebug("Starting post-upload tasks (Icon + Cleanup)...");
                var iconTask = CheckAndUploadIconAsync(session, cancellationToken);
                var cleanupTask = CleanupLegacyFiles(session, archiveFileName);
                await Task.WhenAll(iconTask, cleanupTask);
            }
            finally
            {
                if (File.Exists(tempArchivePath))
                {
                    try { File.Delete(tempArchivePath); } catch { }
                }
            }
        }

        /// <summary>
        /// Deletes legacy individual files after successful .sta upload
        /// </summary>
        private async Task CleanupLegacyFiles(UploadSession session, string archiveFileName)
        {
            try
            {
                DebugConsole.WriteInfo("Checking for legacy files to clean up...");

                string configPath = RclonePathHelper.GetConfigPath(session.Context.Provider);
                var executor = new RcloneExecutor();

                // Delete all files EXCEPT .sta files and icon.png
                var deleteResult = await executor.ExecuteRcloneCommand(
                    $"delete \"{session.RemoteBasePath}\" " +
                    $"--exclude \"*.sta\" " +
                    $"--exclude \"icon.png\" " +
                    $"--config \"{configPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (deleteResult.Success)
                {
                    DebugConsole.WriteSuccess("✓ Legacy individual files cleaned up");
                }

                // Check for legacy "Additional Profiles" folder
                string legacyProfilesPath = $"{session.RemoteBasePath}/Additional Profiles";
                var checkResult = await executor.ExecuteRcloneCommand(
                    $"lsf \"{legacyProfilesPath}\" --config \"{configPath}\"",
                    TimeSpan.FromSeconds(5)
                );

                if (checkResult.Success && !string.IsNullOrWhiteSpace(checkResult.Output))
                {
                    DebugConsole.WriteInfo("Found legacy Additional Profiles folder. Triggering migration...");

                    // Run migration in background (non-blocking)
                    _ = Task.Run(async () =>
                    {
                        await MigrateLegacyProfiles(
                            session.Context.Game,
                            session.Context.Provider
                        );
                    });
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Legacy cleanup failed (non-critical): {ex.Message}");
            }
        }

        /// <summary>
        /// Migrates legacy "Additional Profiles" folder structure to .sta files
        /// </summary>
        private async Task MigrateLegacyProfiles(Game game, CloudProvider provider)
        {
            try
            {
                DebugConsole.WriteInfo("Starting legacy profile migration...");

                string basePath = await _rcloneFileOperations.GetRemotePathAsync(provider, game);
                string legacyProfilesPath = $"{basePath}/Additional Profiles";

                string configPath = RclonePathHelper.GetConfigPath(provider);
                var executor = new RcloneExecutor();

                // List all profile folders
                var listResult = await executor.ExecuteRcloneCommand(
                    $"lsf \"{legacyProfilesPath}\" --dirs-only --config \"{configPath}\"",
                    TimeSpan.FromSeconds(10)
                );

                if (!listResult.Success || string.IsNullOrWhiteSpace(listResult.Output))
                {
                    DebugConsole.WriteInfo("No legacy profiles found to migrate");
                    return;
                }

                var profileFolders = listResult.Output
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim().TrimEnd('/'))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                DebugConsole.WriteInfo($"Found {profileFolders.Count} profiles to migrate");

                int migratedCount = 0;
                foreach (var profileName in profileFolders)
                {
                    bool success = await MigrateSingleProfile(
                        game,
                        provider,
                        profileName,
                        legacyProfilesPath,
                        basePath
                    );

                    if (success) migratedCount++;
                }

                // Delete the old Additional Profiles folder
                if (migratedCount == profileFolders.Count)
                {
                    await executor.ExecuteRcloneCommand(
                        $"purge \"{legacyProfilesPath}\" --config \"{configPath}\"",
                        TimeSpan.FromSeconds(30)
                    );

                    DebugConsole.WriteSuccess($"✓ Successfully migrated {migratedCount} profiles");
                }
                else
                {
                    DebugConsole.WriteWarning($"Partially migrated {migratedCount}/{profileFolders.Count} profiles");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Profile migration failed");
            }
        }

        /// <summary>
        /// Migrates a single legacy profile folder to .sta format
        /// </summary>
        private async Task<bool> MigrateSingleProfile(
            Game game,
            CloudProvider provider,
            string profileName,
            string legacyBasePath,
            string newBasePath)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), $"ProfileMigration_{Guid.NewGuid():N}");
            string tempArchive = Path.Combine(Path.GetTempPath(), $"{profileName}_{Guid.NewGuid()}.sta");

            try
            {
                Directory.CreateDirectory(tempFolder);

                // 1. Download legacy profile folder
                string profileFolder = $"{legacyBasePath}/{profileName}";
                var transferService = new RcloneTransferService();

                DebugConsole.WriteInfo($"Downloading legacy profile '{profileName}'...");

                bool downloaded = await transferService.DownloadDirectory(
                    profileFolder,
                    tempFolder,
                    provider,
                    null
                );

                if (!downloaded)
                {
                    DebugConsole.WriteWarning($"Failed to download profile '{profileName}'");
                    return false;
                }

                // 2. Find and load checksum file
                var checksumFiles = Directory.GetFiles(tempFolder, ".savetracker_checksums*.json", SearchOption.AllDirectories);
                if (checksumFiles.Length == 0)
                {
                    DebugConsole.WriteWarning($"No checksum file found for profile '{profileName}'");
                    return false;
                }

                string json = await File.ReadAllTextAsync(checksumFiles[0]);
                var metadata = JsonConvert.DeserializeObject<GameUploadData>(json);

                if (metadata == null)
                {
                    DebugConsole.WriteWarning($"Failed to parse metadata for profile '{profileName}'");
                    return false;
                }

                // 3. Get all save files (exclude checksum files)
                var files = Directory.GetFiles(tempFolder, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".json"))
                    .ToList();

                if (files.Count == 0)
                {
                    DebugConsole.WriteWarning($"No save files found for profile '{profileName}'");
                    return false;
                }

                // 4. Pack into .sta
                DebugConsole.WriteInfo($"Packing profile '{profileName}' into archive...");

                var packResult = await _archiver.PackAsync(
                    tempArchive,
                    files,
                    tempFolder,
                    metadata,
                    metadata.DetectedPrefix
                );

                if (!packResult.Success)
                {
                    DebugConsole.WriteWarning($"Failed to pack profile '{profileName}': {packResult.Error}");
                    return false;
                }

                // 5. Upload to new location DIRECTLY (bypass ProcessFile to avoid path contraction)
                string newArchiveName = _rcloneFileOperations.GetArchiveFileName(profileName);
                string remoteArchivePath = $"{newBasePath}/{newArchiveName}";
                string configPath = RclonePathHelper.GetConfigPath(provider);
                var migrationExecutor = new RcloneExecutor();

                DebugConsole.WriteInfo($"Uploading {newArchiveName} to {remoteArchivePath}...");

                var migrationResult = await migrationExecutor.ExecuteRcloneCommand(
                    $"copyto \"{tempArchive}\" \"{remoteArchivePath}\" --config \"{configPath}\"",
                    TimeSpan.FromMinutes(5),
                    hideWindow: true
                );

                if (!migrationResult.Success)
                {
                    DebugConsole.WriteWarning($"Failed to upload migrated profile '{profileName}': {migrationResult.Error}");
                    return false;
                }

                DebugConsole.WriteSuccess($"✓ Migrated profile '{profileName}' → {newArchiveName}");
                return true;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to migrate profile '{profileName}': {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                    if (File.Exists(tempArchive)) File.Delete(tempArchive);
                }
                catch { }
            }
        }

        #endregion

        #region Phase 1: Save Files Upload

        private async Task UploadSaveFilesAsync(
            UploadSession session,
            List<string> validFiles,
            CancellationToken cancellationToken,
            bool force = false)
        {
            DebugConsole.WriteInfo($"DEBUG: UploadSaveFilesAsync with {validFiles.Count} files");
            ReportProgress(new UploadProgressInfo
            {
                Status = "Processing save files...",
                TotalFiles = validFiles.Count,
                ProcessedFiles = 0
            });

            // Use the new batch process for faster uploads
            try
            {
                // Create a progress reporter for the batch process
                var batchProgress = new Progress<RcloneProgressUpdate>(update =>
                {
                    ReportProgress(new UploadProgressInfo
                    {
                        Status = "Uploading save files...",
                        CurrentFile = update.CurrentFile,
                        Speed = update.Speed,
                        TotalFiles = validFiles.Count,
                        ProcessedFiles = (int)(validFiles.Count * update.Percent / 100),
                    });
                });

                await _rcloneFileOperations.ProcessBatch(
                    validFiles,
                    session.RemoteBasePath,
                    session.Stats,
                    session.Context.Game,
                    session.Context.Provider,
                    batchProgress,
                    force
                );
                DebugConsole.WriteInfo("DEBUG: ProcessBatch returned");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Batch upload failed");
                session.Stats.FailedCount += validFiles.Count;
            }
        }

        private async Task UploadSingleFileAsync(
            string file,
            UploadSession session,
            int currentIndex,
            int totalFiles)
        {
            string fileName = Path.GetFileName(file);

            // Initial progress report for the file
            ReportProgress(new UploadProgressInfo
            {
                Status = $"Uploading {fileName}...",
                CurrentFile = fileName,
                TotalFiles = totalFiles,
                ProcessedFiles = currentIndex
            });

            try
            {
                var fileProgress = new Progress<RcloneProgressUpdate>(update =>
                {
                    ReportProgress(new UploadProgressInfo
                    {
                        Status = $"Uploading {fileName}...",
                        CurrentFile = fileName,
                        Speed = update.Speed,
                        TotalFiles = totalFiles,
                        ProcessedFiles = currentIndex
                    });
                });

                await _rcloneFileOperations.ProcessFile(
                    file,
                    session.RemoteBasePath,
                    session.Stats,
                    session.Context.Game,
                    session.Context.Provider,
                    fileProgress
                );

                DebugConsole.WriteSuccess($"✓ {fileName} uploaded successfully");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"✗ Failed to upload {fileName}: {ex.Message}");
            }
        }

        #endregion

        #region Phase 2: Checksum Upload

        private async Task UploadChecksumFileAsync(
            UploadSession session,
            ChecksumFileManager checksumManager,
            CancellationToken cancellationToken)
        {
            DebugConsole.WriteInfo($"DEBUG: UploadChecksumFileAsync called. HasFile: {checksumManager.HasChecksumFile}");
            DebugConsole.WriteInfo($"DEBUG: Checksum path: {checksumManager.ChecksumFilePath}");

            if (!checksumManager.HasChecksumFile)
            {
                DebugConsole.WriteWarning("DEBUG: No checksum file to upload.");
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                DebugConsole.WriteInfo("Upload cancelled before checksum upload");
                return;
            }

            ReportProgress(new UploadProgressInfo
            {
                Status = "Uploading checksum file...",
                CurrentFile = ChecksumFilename
            });

            try
            {
                await _rcloneFileOperations.ProcessFile(
                    checksumManager.ChecksumFilePath,
                    session.RemoteBasePath,
                    session.Stats,
                    session.Context.Game,
                    session.Context.Provider,
                    progress: null, // No progress for checksum file
                    force: true
                );

                DebugConsole.WriteSuccess("✓ Checksum file uploaded successfully");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"✗ Failed to upload checksum file: {ex.Message}");
            }
        }

        #endregion

        #region Phase 3: Icon Upload

        private async Task CheckAndUploadIconAsync(
            UploadSession session,
            CancellationToken cancellationToken)
        {
            try
            {
                // Define remote icon path (at game root)
                string providerPrefix = _cloudProviderHelper.GetProviderConfigName(session.Context.Provider);
                string sanitizedGameName = SanitizeGameName(session.Context.Game.Name);
                string gameRootPath = $"{providerPrefix}:{RemoteBaseFolder}/{sanitizedGameName}";
                string remoteIconPath = $"{gameRootPath}/icon.png";

                // Check if icon exists using a local executor
                var executor = new RcloneExecutor();
                string configPath = RclonePathHelper.GetConfigPath(session.Context.Provider);

                var checkResult = await executor.ExecuteRcloneCommand(
                    $"ls \"{remoteIconPath}\" --config \"{configPath}\"",
                    TimeSpan.FromSeconds(5)
                );

                if (checkResult.Success && !string.IsNullOrWhiteSpace(checkResult.Output))
                {
                    // Icon exists, skip
                    return;
                }

                // Icon missing, extract locally
                DebugConsole.WriteInfo("Icon missing in cloud, extracting from executable...");
                string exePath = session.Context.Game.ExecutablePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                byte[]? iconData = Misc.ExtractIconDataFromExe(exePath);
                if (iconData == null)
                {
                    DebugConsole.WriteInfo("No icon data extracted.");
                    return;
                }

                // Save to temp file
                string tempIconPath = Path.Combine(Path.GetTempPath(), $"icon_{Guid.NewGuid()}.png");
                try
                {
                    using (var ms = new MemoryStream(iconData))
                    using (var bitmap = new System.Drawing.Bitmap(ms))
                    {
                        bitmap.Save(tempIconPath, System.Drawing.Imaging.ImageFormat.Png);
                    }

                    // Upload
                    // Use `copyto` to ensure name is icon.png
                    await executor.ExecuteRcloneCommand(
                        $"copyto \"{tempIconPath}\" \"{remoteIconPath}\" --config \"{configPath}\"",
                        TimeSpan.FromSeconds(30)
                    );

                    DebugConsole.WriteSuccess("Uploaded game icon to cloud.");
                }
                finally
                {
                    if (File.Exists(tempIconPath)) File.Delete(tempIconPath);
                }
            }
            catch (Exception ex)
            {
                // Non-critical, just log warning
                DebugConsole.WriteWarning($"Failed to upload icon: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private void ReportProgress(UploadProgressInfo progress)
        {
            OnProgressChanged?.Invoke(progress);
        }

        private static string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            string sanitized = invalidChars.Aggregate(
                gameName,
                (current, c) => current.Replace(c, '_')
            );
            return sanitized.Trim();
        }

        private static string CalculateFileChecksum(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return $"{dblSByte:0.##} {Suffix[i]}";
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Context for upload operation
    /// </summary>
    public class UploadContext
    {
        public List<string> SaveFiles { get; }
        public Game Game { get; }
        public CloudProvider Provider { get; set; }

        public UploadContext(List<string> saveFiles, Game game, CloudProvider provider)
        {
            SaveFiles = saveFiles ?? throw new ArgumentNullException(nameof(saveFiles));
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Provider = provider;
        }
    }

    /// <summary>
    /// Session information for an upload
    /// </summary>
    public class UploadSession
    {
        public UploadContext Context { get; }
        public string DisplayName { get; private set; }
        public string RemoteBasePath { get; private set; }
        public UploadStats Stats { get; }
        public DateTime StartTime { get; private set; }
        public TimeSpan TotalTime { get; private set; }
        private readonly CloudProviderHelper _cloudHelper;

        public UploadSession(UploadContext context, CloudProviderHelper cloudHelper)
        {
            Context = context;
            Stats = new UploadStats();
            _cloudHelper = cloudHelper;
        }

        public async Task InitializeAsync()
        {
            StartTime = DateTime.Now;
            DisplayName = Context.Game.Name;

            string sanitizedName = SanitizeGameName(Context.Game.Name);

            // Get the remote name from cloud provider (e.g., "gdrive:", "box:", "onedrive:")
            string remoteName = _cloudHelper.GetProviderConfigName(Context.Provider);
            string basePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}/{sanitizedName}";

            // Handle Profiles
            if (!string.IsNullOrEmpty(Context.Game.ActiveProfileId))
            {
                var config = await ConfigManagement.LoadConfigAsync();
                var profile = config.Profiles.FirstOrDefault(p => p.Id == Context.Game.ActiveProfileId);

                // If found and NOT default
                if (profile != null && !profile.IsDefault)
                {
                    string profileFolder = SanitizeGameName(profile.Name); // Sanitize profile name too
                    basePath = $"{basePath}/Additional Profiles/{profileFolder}";
                    DebugConsole.WriteInfo($"[Profile Cloud] Uploading to profile path: {profileFolder}");
                }
            }

            RemoteBasePath = basePath;
        }

        public void Complete()
        {
            TotalTime = DateTime.Now - StartTime;
        }

        private static string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "Unknown";

            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            return invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_')).Trim();
        }
    }

    /// <summary>
    /// Upload statistics
    /// </summary>
    public class UploadStats
    {
        public int UploadedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public long UploadedSize { get; set; }
        public long SkippedSize { get; set; }
    }

    /// <summary>
    /// Progress information
    /// </summary>
    public class UploadProgressInfo
    {
        public string Status { get; set; }
        public string CurrentFile { get; set; }
        public string? Speed { get; set; }
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int PercentComplete => TotalFiles > 0 ? (ProcessedFiles * 100 / TotalFiles) : 0;
    }

    /// <summary>
    /// Upload result
    /// </summary>
    public class UploadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int UploadedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public long TotalSize { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Manages file validation
    /// </summary>
    public class UploadFileManager
    {
        public List<string> ValidFiles { get; private set; }
        public List<string> InvalidFiles { get; private set; }

        public UploadFileManager(List<string> files)
        {
            ValidFiles = new List<string>();
            InvalidFiles = new List<string>();

            foreach (var file in files)
            {
                if (File.Exists(file))
                    ValidFiles.Add(file);
                else
                    InvalidFiles.Add(file);
            }
        }

        public void ValidateFiles()
        {
            DebugConsole.WriteInfo($"Valid files: {ValidFiles.Count}, Invalid: {InvalidFiles.Count}");
        }
    }

    /// <summary>
    /// Manages checksum file
    /// </summary>
    public class ChecksumFileManager
    {
        private readonly Game _game;
        private readonly ChecksumService _checksumService;
        private readonly string? _profileId;
        public string ChecksumFilePath { get; private set; }
        public bool HasChecksumFile => !string.IsNullOrEmpty(ChecksumFilePath) && File.Exists(ChecksumFilePath);

        public ChecksumFileManager(Game game, string? profileId = null)
        {
            _game = game;
            _profileId = profileId; // Will be null for DEFAULT profile
            _checksumService = new ChecksumService();
        }

        public async Task PrepareChecksumFileAsync(List<string> validFiles)
        {
            try
            {
                // Get the game's save directory
                string gameDirectory = _game.InstallDirectory;
                if (string.IsNullOrEmpty(gameDirectory))
                {
                    DebugConsole.WriteWarning("Game save directory is not set - skipping checksum file creation");
                    return;
                }

                // Atomically ensure checksum file exists, migrates if needed, and is ready for upload
                // This replaces the previous non-atomic Load/Save sequence
                ChecksumFilePath = await _checksumService.EnsureChecksumFileAsync(gameDirectory, !string.IsNullOrEmpty(_profileId) ? _profileId : "DEFAULT_PROFILE_ID");

                DebugConsole.WriteSuccess($"Checksum file prepared: {Path.GetFileName(ChecksumFilePath)} (checksums will be updated after successful uploads)");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to prepare checksum file");
                ChecksumFilePath = null;
            }
        }

        public void Cleanup()
        {
            // Cleanup temporary files if needed
        }
    }

    #endregion
}