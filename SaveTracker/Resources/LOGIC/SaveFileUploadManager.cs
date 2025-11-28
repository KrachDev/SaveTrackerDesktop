using Avalonia.Controls;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;

using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CloudConfig;

namespace SaveTracker.Resources.Logic
{
    /// <summary>
    /// Manages the upload process for game save files to cloud storage
    /// </summary>
    public class SaveFileUploadManager
    {
        // Constants
        public const string CHECKSUM_FILENAME = ".savetracker_checksums.json";
        public const string REMOTE_BASE_FOLDER = "SaveTrackerCloudSave";

        // Dependencies
        private readonly RcloneInstaller _rcloneInstaller;
        private readonly CloudProviderHelper _cloudProviderHelper;
        private readonly RcloneFileOperations _rcloneFileOperations;
        private readonly string _configPath;

        // Properties
        private static string RcloneExePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools",
            "rclone.exe"
        );

        // Event for progress updates
        public event Action<UploadProgressInfo> OnProgressChanged;
        public event Action<UploadResult> OnUploadCompleted;
        public event Func<Task<bool>>? OnCloudConfigRequired;

        public SaveFileUploadManager(
            RcloneInstaller rcloneInstaller,
            CloudProviderHelper cloudProviderHelper,
            RcloneFileOperations rcloneFileOperations,
            string configPath)
        {
            _rcloneInstaller = rcloneInstaller ?? throw new ArgumentNullException(nameof(rcloneInstaller));
            _cloudProviderHelper = cloudProviderHelper ?? throw new ArgumentNullException(nameof(cloudProviderHelper));
            _rcloneFileOperations = rcloneFileOperations ?? throw new ArgumentNullException(nameof(rcloneFileOperations));
            _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        }

        /// <summary>
        /// Main upload method
        /// </summary>
        public async Task<UploadResult> Upload(
            List<string> saveFiles,
            Game game,
            CloudProvider provider,
            CancellationToken cancellationToken = default)
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

            return await ExecuteUploadAsync(context, cancellationToken);
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

            // Validate rclone installation
            bool rcloneValid = await _rcloneInstaller.RcloneCheckAsync(context.Provider);

            if (!rcloneValid || !File.Exists(RcloneExePath) || !File.Exists(_configPath))
            {
                if (OnCloudConfigRequired != null)
                {
                    bool configured = await OnCloudConfigRequired.Invoke();
                    if (configured)
                    {
                        // Retry validation
                        rcloneValid = await _rcloneInstaller.RcloneCheckAsync(context.Provider);
                        if (rcloneValid && File.Exists(RcloneExePath) && File.Exists(_configPath))
                        {
                            return true;
                        }
                    }
                }

                string error = "Rclone is not installed or configured.";
                DebugConsole.WriteError(error);
                return false;
            }

            return true;
        }

        #endregion

        #region Main Upload Execution

        private async Task<UploadResult> ExecuteUploadAsync(
            UploadContext context,
            CancellationToken cancellationToken)
        {
            var session = new UploadSession(context, _cloudProviderHelper);
            session.Initialize();

            var fileManager = new UploadFileManager(context.SaveFiles);
            var checksumManager = new ChecksumFileManager(context.Game);

            // Prepare files
            fileManager.ValidateFiles();
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
                await PerformUploadAsync(
                    session,
                    fileManager,
                    checksumManager,
                    cancellationToken
                );

                session.Complete();

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
            CancellationToken cancellationToken)
        {
            // Phase 1: Upload save files
            await UploadSaveFilesAsync(
                session,
                fileManager.ValidFiles,
                cancellationToken
            );

            // Phase 2: Upload checksum file
            await UploadChecksumFileAsync(
                session,
                checksumManager,
                cancellationToken
            );
        }

        #endregion

        #region Phase 1: Save Files Upload

        private async Task UploadSaveFilesAsync(
            UploadSession session,
            List<string> validFiles,
            CancellationToken cancellationToken)
        {
            ReportProgress(new UploadProgressInfo
            {
                Status = "Processing save files...",
                TotalFiles = validFiles.Count,
                ProcessedFiles = 0
            });

            int processed = 0;
            foreach (var file in validFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    DebugConsole.WriteInfo("Upload cancelled by user");
                    return;
                }

                await UploadSingleFileAsync(file, session, processed, validFiles.Count);
                processed++;
            }
        }

        private async Task UploadSingleFileAsync(
            string file,
            UploadSession session,
            int currentIndex,
            int totalFiles)
        {
            string fileName = Path.GetFileName(file);

            ReportProgress(new UploadProgressInfo
            {
                Status = $"Uploading {fileName}...",
                CurrentFile = fileName,
                TotalFiles = totalFiles,
                ProcessedFiles = currentIndex
            });

            try
            {
                await _rcloneFileOperations.ProcessFile(
                    file,
                    session.RemoteBasePath,
                    session.Stats
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
            if (!checksumManager.HasChecksumFile)
                return;

            if (cancellationToken.IsCancellationRequested)
            {
                DebugConsole.WriteInfo("Upload cancelled before checksum upload");
                return;
            }

            ReportProgress(new UploadProgressInfo
            {
                Status = "Uploading checksum file...",
                CurrentFile = CHECKSUM_FILENAME
            });

            try
            {
                await _rcloneFileOperations.ProcessFile(
                    checksumManager.ChecksumFilePath,
                    session.RemoteBasePath,
                    session.Stats
                );

                DebugConsole.WriteSuccess("✓ Checksum file uploaded successfully");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"✗ Failed to upload checksum file: {ex.Message}");
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

        public void Initialize()
        {
            StartTime = DateTime.Now;
            DisplayName = Context.Game.Name;

            string sanitizedName = SanitizeGameName(Context.Game.Name);

            // Get the remote name from cloud provider (e.g., "gdrive:", "box:", "onedrive:")
            string remoteName = _cloudHelper.GetProviderConfigName(Context.Provider);
            RemoteBasePath = $"{remoteName}:{SaveFileUploadManager.REMOTE_BASE_FOLDER}/{sanitizedName}";
        }

        public void Complete()
        {
            TotalTime = DateTime.Now - StartTime;
        }

        private static string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

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
        public string ChecksumFilePath { get; private set; }
        public bool HasChecksumFile => !string.IsNullOrEmpty(ChecksumFilePath) && File.Exists(ChecksumFilePath);

        public ChecksumFileManager(Game game)
        {
            _game = game;
        }

        public async Task PrepareChecksumFileAsync(List<string> validFiles)
        {
            // Create checksum file logic here
            // For now, placeholder
            await Task.CompletedTask;
        }

        public void Cleanup()
        {
            // Cleanup temporary files if needed
        }
    }

    #endregion
}