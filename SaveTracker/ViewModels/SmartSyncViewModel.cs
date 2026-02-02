using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Models;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using System.IO;
using System.Collections.Generic;

namespace SaveTracker.ViewModels
{
    public partial class SmartSyncViewModel : ObservableObject
    {
        private readonly Game _game;
        private readonly SmartSyncMode _mode;
        private SmartSyncService.ProgressComparison? _comparison;
        private GameUploadData? _cachedCloudData; // Cache to avoid downloading twice

        [ObservableProperty]
        private string _gameName = string.Empty;

        [ObservableProperty]
        private Bitmap? _gameIconBitmap;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _loadingMessage = "Loading...";

        // Playtime Comparison
        [ObservableProperty]
        private string _localPlayTime = "00:00:00";

        [ObservableProperty]
        private string _cloudPlayTime = "00:00:00";

        [ObservableProperty]
        private string _playTimeDifference = "00:00:00";

        [ObservableProperty]
        private string _playTimeIndicator = ""; // "↑" or "↓"

        // File Counts
        [ObservableProperty]
        private int _localFileCount;

        [ObservableProperty]
        private int _cloudFileCount;

        [ObservableProperty]
        private int _newInLocal;

        [ObservableProperty]
        private int _newInCloud;

        [ObservableProperty]
        private int _modifiedCount;

        // Suggestion
        [ObservableProperty]
        private string _suggestedAction = "Skip";

        [ObservableProperty]
        private string _suggestionReason = "No action needed";

        [ObservableProperty]
        private string _suggestionIcon = "ℹ️";

        // Progress
        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string _currentOperation = "Ready";

        [ObservableProperty]
        private string _currentFile = "";

        [ObservableProperty]
        private string _fileProgress = ""; // e.g., "(3/10)"

        [ObservableProperty]
        private string _speed = "";

        [ObservableProperty]
        private string _eta = "";

        [ObservableProperty]
        private bool _isOperationInProgress;

        // UI State
        [ObservableProperty]
        private int _selectedTabIndex;

        [ObservableProperty]
        private bool _canDownload;

        [ObservableProperty]
        private bool _canUpload;

        // Collections
        public ObservableCollection<FileComparisonItem> FileComparisonList { get; } = new();
        public ObservableCollection<string> OperationLog { get; } = new();

        public SmartSyncViewModel(Game game, SmartSyncMode mode, SmartSyncService.ProgressComparison? preCalculated = null)
        {
            _game = game;
            _mode = mode;
            _comparison = preCalculated;

            GameName = game.Name;

            // Initialize in background
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // UI updates must happen on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = true;
                    LoadingMessage = "Analyzing save data...";
                });
                DebugConsole.WriteInfo($"Initializing Smart Sync window for {_game.Name}");

                // Load Icon
                await Task.Run(async () =>
                {
                    try
                    {
                        var bitmap = Misc.ExtractIconFromExe(_game.ExecutablePath);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            GameIconBitmap = bitmap;
                        });
                    }
                    catch { }
                });

                // Perform heavy comparison logic on background thread
                await Task.Run(async () =>
                {
                    try
                    {
                        var smartSync = new SmartSyncService();
                        var provider = await smartSync.GetEffectiveProvider(_game);

                        // OPTIMIZATION: Download cloud checksum ONCE and cache it
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            LoadingMessage = "Downloading cloud data...";
                        });

                        _cachedCloudData = await DownloadCloudChecksumOnce(provider);

                        // comparison logic - now uses cached data
                        if (_comparison == null)
                        {
                            _comparison = await smartSync.CompareProgressAsync(_game, TimeSpan.Zero, provider, _cachedCloudData);
                        }

                        await UpdateComparisonUIAsync(_comparison);

                        // Load file comparison details - reuses cached cloud data
                        await CompareChecksumsInternalAsync();
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, "Error during async init calculation");
                    }
                });

                await Dispatcher.UIThread.InvokeAsync(() => CalculateSuggestion());

                // Auto-Decision with 5s Timer found in InitializeAsync
                if (_mode == SmartSyncMode.GameExit || _mode == SmartSyncMode.GameLaunch)
                {
                    StartAutoActionTimer();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to initialize Smart Sync window");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoadingMessage = "Error loading data.";
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
                DebugConsole.WriteSuccess("Smart Sync window initialized");
            }
        }

        private async Task UpdateComparisonUIAsync(SmartSyncService.ProgressComparison comparison)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LocalPlayTime = FormatTimeSpan(comparison.LocalPlayTime);
                CloudPlayTime = FormatTimeSpan(comparison.CloudPlayTime);
                PlayTimeDifference = FormatTimeSpan(comparison.Difference);

                if (comparison.CloudPlayTime > comparison.LocalPlayTime)
                {
                    PlayTimeIndicator = "↑ Cloud ahead";
                }
                else if (comparison.LocalPlayTime > comparison.CloudPlayTime)
                {
                    PlayTimeIndicator = "↓ Local ahead";
                }
                else
                {
                    PlayTimeIndicator = "= Equal";
                }

                CanDownload = comparison.CloudPlayTime > TimeSpan.Zero;
                CanUpload = comparison.LocalPlayTime > TimeSpan.Zero;
            });
        }

        private async Task<GameUploadData?> DownloadCloudChecksumOnce(CloudProvider provider)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), $"SaveTracker_SyncCheck_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempFolder);

            try
            {
                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig != null)
                {
                    var rcloneOps = new RcloneFileOperations(_game);

                    // Use helper to get correct path for active profile
                    var remotePath = await rcloneOps.GetRemotePathAsync(provider, _game);

                    var checksumService = new ChecksumService();
                    string gameDirectory = _game.InstallDirectory;
                    // Uses profile-specific filename logic
                    string realChecksumPath = checksumService.GetChecksumFilePath(gameDirectory, _game.ActiveProfileId);
                    string realChecksumName = Path.GetFileName(realChecksumPath);

                    var gameData = await ConfigManagement.GetGameData(_game);
                    string? detectedPrefix = gameData?.DetectedPrefix;

                    // Get the relative path that would be used for upload (contracted path)
                    string relativeChecksumPath = PathContractor.ContractPath(realChecksumPath, gameDirectory, detectedPrefix).Replace('\\', '/');
                    string checksumRemotePath = $"{remotePath}/{relativeChecksumPath}";
                    string checksumLocalPath = Path.Combine(tempFolder, relativeChecksumPath);

                    // Create local folder if needed
                    string? localDir = Path.GetDirectoryName(checksumLocalPath);
                    if (!string.IsNullOrEmpty(localDir)) Directory.CreateDirectory(localDir);

                    var transferService = new RcloneTransferService();
                    bool downloaded = await transferService.DownloadFileWithRetry(
                        checksumRemotePath,
                        checksumLocalPath,
                        realChecksumName,
                        provider
                    );

                    // Fallback to legacy checksum name if profile-specific failed
                    if (!downloaded)
                    {
                        string legacyChecksumName = ".savetracker_checksums.json";
                        string legacyRelativePath = relativeChecksumPath.Replace(realChecksumName, legacyChecksumName);
                        string legacyRemotePath = $"{remotePath}/{legacyRelativePath}";
                        
                        DebugConsole.WriteInfo($"Attempting legacy checksum fetch: {legacyRelativePath}");
                        
                        downloaded = await transferService.DownloadFileWithRetry(
                            legacyRemotePath,
                            checksumLocalPath, // Save it to the same local path for deserialization
                            legacyChecksumName,
                            provider
                        );
                    }

                    if (downloaded && File.Exists(checksumLocalPath))
                    {
                        string json = await File.ReadAllTextAsync(checksumLocalPath);
                        return Newtonsoft.Json.JsonConvert.DeserializeObject<GameUploadData>(json);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to download cloud checksum: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                }
                catch { }
            }

            return null;
        }

        private async Task CompareChecksumsInternalAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DebugConsole.WriteInfo("Comparing local and cloud checksums...");
                FileComparisonList.Clear();
            });

            // Load local checksum
            var checksumService = new ChecksumService();
            var localData = await checksumService.LoadChecksumData(_game.InstallDirectory);

            // OPTIMIZATION: Reuse cached cloud data instead of re-downloading
            GameUploadData cloudData = _cachedCloudData ?? new GameUploadData();

            // Prepare items for UI
            var items = new List<FileComparisonItem>();

            var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (localData?.Files != null) allPaths.UnionWith(localData.Files.Keys);
            if (cloudData?.Files != null) allPaths.UnionWith(cloudData.Files.Keys);

            int modified = 0;
            int newInLocal = 0;
            int newInCloud = 0;

            foreach (var path in allPaths)
            {
                var localFile = localData?.Files?.ContainsKey(path) == true ? localData.Files[path] : null;
                var cloudFile = cloudData?.Files?.ContainsKey(path) == true ? cloudData.Files[path] : null;

                var item = new FileComparisonItem { FileName = Path.GetFileName(path) };

                if (localFile != null && cloudFile != null)
                {
                    if (localFile.Checksum != cloudFile.Checksum)
                    {
                        item.Status = "Modified";
                        item.Icon = "⚠️";
                        modified++;
                    }
                    else
                    {
                        item.Status = "Synced";
                        item.Icon = "✓";
                    }
                }
                else if (localFile != null)
                {
                    item.Status = "New Local";
                    item.Icon = "✨";
                    newInLocal++;
                }
                else if (cloudFile != null)
                {
                    item.Status = "New Cloud";
                    item.Icon = "☁️";
                    newInCloud++;
                }

                items.Add(item);
            }

            // Update UI on UI Thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LocalFileCount = localData?.Files?.Count ?? 0;
                CloudFileCount = cloudData?.Files?.Count ?? 0;
                ModifiedCount = modified;
                NewInLocal = newInLocal;
                NewInCloud = newInCloud;

                foreach (var item in items)
                {
                    FileComparisonList.Add(item);
                }

                DebugConsole.WriteInfo($"Comparison done: {modified} mod, {newInLocal} local, {newInCloud} cloud");
            });
        }

        public async Task CompareChecksumsAsync()
        {
            await Task.Run(CompareChecksumsInternalAsync);
        }

        private void CalculateSuggestion()
        {
            if (_comparison == null) return;

            if (_comparison.Status == SmartSyncService.ProgressStatus.CloudAhead)
            {
                SuggestedAction = "Download";
                SuggestionIcon = "⬇️";
                SuggestionReason = $"Cloud: +{FormatTimeSpan(_comparison.Difference)} playtime.";
            }
            else if (_comparison.Status == SmartSyncService.ProgressStatus.LocalAhead)
            {
                SuggestedAction = "Upload";
                SuggestionIcon = "⬆️";
                SuggestionReason = $"Local: +{FormatTimeSpan(_comparison.Difference)} playtime.";
            }
            else if (_comparison.Status == SmartSyncService.ProgressStatus.CloudNotFound)
            {
                SuggestedAction = "Upload";
                SuggestionIcon = "⬆️";
                SuggestionReason = "Cloud save missing.";
            }
            else
            {
                SuggestedAction = "Skip";
                SuggestionIcon = "✓";
                SuggestionReason = "Saves in sync.";
            }
        }

        [RelayCommand]
        private async Task DownloadCloudSaveAsync()
        {
            StopAutoAction(); // Cancels timer if running
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = true;
                    LoadingMessage = "Initializing download...";
                    IsOperationInProgress = true;
                    CanDownload = false; // Disable buttons
                    CanUpload = false;
                });

                DebugConsole.WriteInfo("Starting cloud save download...");

                // Run heavy work on background thread
                await Task.Run(async () =>
                {
                    try
                    {
                        var rcloneOps = new RcloneFileOperations(_game);
                        var config = await ConfigManagement.LoadConfigAsync();

                        // Short initialization delay to let UI show "Initializing" briefly if needed, 
                        // but mainly we want to switch to Progress Tab and hide overlay for the actual transfer.
                        await Task.Delay(500);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            IsLoading = false; // HIDE OVERLAY
                            SelectedTabIndex = 2; // Switch to Progress tab
                            CurrentOperation = "Downloading";
                            ProgressValue = 0;
                            OperationLog.Clear();
                            OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Starting download...");
                        });

                        if (config?.CloudConfig == null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                DebugConsole.WriteError("Cloud configuration not found");
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ Error: Cloud not configured");
                            });
                            return;
                        }

                        var remotePath = await rcloneOps.GetRemotePathAsync(config.CloudConfig.Provider, _game);

                        // Added Check for existence before download
                        if (!await rcloneOps.CheckCloudSaveExistsAsync(remotePath, config.CloudConfig.Provider))
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                           {
                               OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ No cloud save found for this profile.");
                               DebugConsole.WriteWarning("No cloud save found - aborting download");
                               CanDownload = false; // Disable button
                               CurrentOperation = "Failed: No Cloud Save";
                           });
                            return;
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Downloading from {remotePath}...");
                        });

                        var progress = new Progress<RcloneProgressUpdate>(update =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                ProgressValue = update.Percent;
                                LoadingMessage = $"Downloading... {update.Percent:F0}%";
                                Speed = update.Speed ?? "";

                                // Simple progress indicator for download
                                if (update.Percent < 100)
                                {
                                    CurrentFile = update.CurrentFile ?? "Downloading files...";
                                    FileProgress = $"{update.Percent:F0}%";
                                }
                                else
                                {
                                    CurrentFile = "Download complete!";
                                    FileProgress = "";
                                }
                            });
                        });

                        bool success = await rcloneOps.DownloadWithChecksumAsync(remotePath, _game, config.CloudConfig.Provider, progress);

                        if (success)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ✓ Download complete!");
                                DebugConsole.WriteSuccess("Cloud save downloaded successfully");
                            });

                            // Force update local PlayTime with the cloud value we just synced
                            // This ensures the UI updates immediately even if file system reads lag or fail
                            if (_comparison?.CloudPlayTime != null && _comparison.CloudPlayTime > TimeSpan.Zero)
                            {
                                DebugConsole.WriteInfo($"Updating local PlayTime to match cloud: {_comparison.CloudPlayTime}");
                                var checksum = new ChecksumService();
                                await checksum.UpdatePlayTime(_game.InstallDirectory, _comparison.CloudPlayTime);
                            }

                            // Refresh logic
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                IsLoading = true;
                                LoadingMessage = "Refreshing data...";
                            });

                            var smartSync = new SmartSyncService();
                            _comparison = await smartSync.CompareProgressAsync(_game, TimeSpan.Zero, config.CloudConfig.Provider);

                            // INVALIDATE CACHE AND RE-FETCH
                            _cachedCloudData = await DownloadCloudChecksumOnce(config.CloudConfig.Provider);

                            await UpdateComparisonUIAsync(_comparison);
                            await CompareChecksumsInternalAsync();
                            await Dispatcher.UIThread.InvokeAsync(() => CalculateSuggestion());
                        }
                        else
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ Download failed");
                                DebugConsole.WriteError("Failed to download cloud save");
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            DebugConsole.WriteException(ex, "Download process failed");
                            OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ Error: {ex.Message}");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Download failed");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsOperationInProgress = false;
                    CurrentOperation = "Complete";
                    IsLoading = false;
                });
            }
        }

        [RelayCommand]
        private async Task UploadLocalSaveAsync()
        {
            StopAutoAction(); // Cancels timer if running
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = true;
                    LoadingMessage = "Initializing upload...";
                    IsOperationInProgress = true;
                    CanDownload = false;
                    CanUpload = false;
                });

                DebugConsole.WriteInfo("Starting local save upload...");

                // Run heavy work on background thread
                await Task.Run(async () =>
                {
                    try
                    {
                        var rcloneOps = new RcloneFileOperations(_game);
                        var config = await ConfigManagement.LoadConfigAsync();

                        await Task.Delay(500);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                       {
                           IsLoading = false; // HIDE OVERLAY
                           SelectedTabIndex = 2; // Switch to Progress tab
                           CurrentOperation = "Uploading";
                           ProgressValue = 0;
                           OperationLog.Clear();
                           OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Starting upload...");
                       });

                        if (config?.CloudConfig == null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ Error: Cloud config missing");
                            });
                            return;
                        }

                        var remoteName = GetProviderConfigName(config.CloudConfig.Provider);
                        var sanitizedGameName = SanitizeGameName(_game.Name);
                        var remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}/{sanitizedGameName}";

                        // CRITICAL: Only upload files that are tracked in checksums.json
                        // DO NOT scan entire game directory - that includes executables!
                        var checksumService = new ChecksumService();
                        var checksumData = await checksumService.LoadChecksumData(_game.InstallDirectory);

                        if (checksumData?.Files == null || checksumData.Files.Count == 0)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ No tracked files found to upload");
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Please add files to track first");
                            });
                            return;
                        }

                        // Load game data for prefix
                        var gameData = await ConfigManagement.GetGameData(_game);
                        string? detectedPrefix = gameData?.DetectedPrefix;

                        // Convert tracked paths to absolute paths
                        var trackedFiles = new List<string>();
                        foreach (var fileRecord in checksumData.Files.Values)
                        {
                            try
                            {
                                string absolutePath = fileRecord.GetAbsolutePath(_game.InstallDirectory, detectedPrefix);
                                if (File.Exists(absolutePath))
                                {
                                    trackedFiles.Add(absolutePath);
                                }
                                else
                                {
                                    DebugConsole.WriteWarning($"Tracked file not found: {absolutePath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteWarning($"Error resolving tracked file path: {ex.Message}");
                            }
                        }

                        if (trackedFiles.Count == 0)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ No valid tracked files exist on disk");
                            });
                            return;
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Found {trackedFiles.Count} tracked files to upload");
                            OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Processing batch upload...");
                        });

                        var stats = new UploadStats();

                        // Create progress reporter with file-level granularity
                        int totalFiles = trackedFiles.Count;

                        var progress = new Progress<RcloneProgressUpdate>(update =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                ProgressValue = update.Percent;
                                Speed = update.Speed ?? "";

                                // RcloneProgressUpdate now includes the actual filename being processed
                                if (update.Percent < 100)
                                {
                                    CurrentFile = update.CurrentFile ?? "Uploading...";

                                    // Calculate which file we're on for the count display
                                    int estimatedFileIndex = (int)(update.Percent / 100.0 * totalFiles);
                                    if (estimatedFileIndex >= totalFiles) estimatedFileIndex = totalFiles - 1;
                                    FileProgress = $"({estimatedFileIndex + 1}/{totalFiles})";
                                }
                                else
                                {
                                    CurrentFile = "Upload complete!";
                                    FileProgress = "";
                                }
                            });
                        });


                        // Determine if we should force upload (e.g. if cloud is missing)
                        bool forceUpload = _comparison?.Status == SmartSyncService.ProgressStatus.CloudNotFound;
                        if (forceUpload)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Cloud save not found - forcing upload of all files.");
                            });
                        }

                        await rcloneOps.ProcessBatch(
                            trackedFiles,
                            remotePath,
                            stats,
                            _game,
                            config.CloudConfig.Provider,
                            progress,
                            forceUpload
                        );

                        // Explicitly upload checksum file
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Uploading checksum file...");
                            // Use generic name in UI or fetch actual name. Generic is fine.
                            CurrentFile = "Metadata (Checksums)";
                            FileProgress = "";
                        });

                        // CRITICAL: Pass profile ID to get correct checksum file
                        string checksumFile = checksumService.GetChecksumFilePath(_game.InstallDirectory, _game.ActiveProfileId);
                        if (File.Exists(checksumFile))
                        {
                            await rcloneOps.ProcessFile(
                                checksumFile,
                                remotePath,
                                stats,
                                _game,
                                config.CloudConfig.Provider,
                                progress: null, // No detailed progress for metadata
                                force: true
                            );
                            await Dispatcher.UIThread.InvokeAsync(() =>
                           {
                               OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ✓ Checksum file uploaded");
                           });
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (stats.FailedCount == 0)
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ✓ Upload complete!");
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Stats: {stats.UploadedCount} uploaded, {stats.SkippedCount} skipped");
                                DebugConsole.WriteSuccess("Local save uploaded successfully");
                                CurrentFile = "Upload complete!";
                                FileProgress = "";
                            }
                            else
                            {
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ Upload finished with {stats.FailedCount} errors");
                                OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] Stats: {stats.UploadedCount} uploaded, {stats.SkippedCount} skipped");
                                CurrentFile = "Upload completed with errors";
                                FileProgress = "";
                            }
                        });

                        if (stats.FailedCount == 0)
                        {
                            // Refresh comparison
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                IsLoading = true;
                                LoadingMessage = "Refreshing data...";
                            });

                            var smartSync = new SmartSyncService();
                            _comparison = await smartSync.CompareProgressAsync(_game, TimeSpan.Zero, config.CloudConfig.Provider);

                            // INVALIDATE CACHE AND RE-FETCH
                            _cachedCloudData = await DownloadCloudChecksumOnce(config.CloudConfig.Provider);

                            await UpdateComparisonUIAsync(_comparison);
                            await CompareChecksumsInternalAsync();
                            await Dispatcher.UIThread.InvokeAsync(() => CalculateSuggestion());
                        }
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            DebugConsole.WriteException(ex, "Upload process failed");
                            OperationLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ Error: {ex.Message}");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Upload failed");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsOperationInProgress = false;
                    CurrentOperation = "Complete";
                    IsLoading = false;
                });
            }
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            return timeSpan.ToString(@"hh\:mm\:ss");
        }

        private static string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

            var invalidChars = System.IO.Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            return invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_')).Trim();
        }

        // Auto-Action Logic
        [ObservableProperty]
        private string _autoActionCountdown = "";

        [ObservableProperty]
        private bool _isAutoActionPending;

        [ObservableProperty]
        private bool _isAutoActionPaused;

        private DispatcherTimer? _autoActionTimer;
        private int _secondsRemaining = 5;

        private void StartAutoActionTimer()
        {
            if (SuggestedAction == "Skip") return;

            IsAutoActionPending = true;
            _secondsRemaining = 5;
            UpdateCountdownText();

            _autoActionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _autoActionTimer.Tick += AutoActionTimer_Tick;
            _autoActionTimer.Start();
        }

        private void UpdateCountdownText()
        {
            if (_secondsRemaining > 0)
                AutoActionCountdown = $"Auto-{SuggestedAction.ToLower()} in {_secondsRemaining}s...";
            else
                AutoActionCountdown = "Starting...";
        }

        private async void AutoActionTimer_Tick(object? sender, EventArgs e)
        {
            if (IsAutoActionPaused) return;

            _secondsRemaining--;
            UpdateCountdownText();

            if (_secondsRemaining <= 0)
            {
                _autoActionTimer?.Stop();
                IsAutoActionPending = false;

                // Execute Action
                if (SuggestedAction == "Download")
                {
                    await DownloadCloudSaveAsync();
                }
                else if (SuggestedAction == "Upload")
                {
                    await UploadLocalSaveAsync();
                }
            }
        }

        [RelayCommand]
        private void StopAutoAction()
        {
            _autoActionTimer?.Stop();
            IsAutoActionPending = false;
            IsAutoActionPaused = true;
            AutoActionCountdown = "Auto-action cancelled";
        }

        private static string GetProviderConfigName(CloudProvider provider)
        {
            return provider switch
            {
                CloudProvider.GoogleDrive => "gdrive",
                CloudProvider.OneDrive => "onedrive",
                CloudProvider.Dropbox => "dropbox",
                CloudProvider.Box => "box",
                _ => "gdrive"
            };
        }
    }
}
