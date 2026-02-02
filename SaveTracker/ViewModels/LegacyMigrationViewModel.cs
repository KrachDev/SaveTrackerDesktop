using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using SaveTracker.Models;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using static CloudConfig;

namespace SaveTracker.ViewModels
{
    public partial class LegacyMigrationViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<LegacyGameItem> _legacyGames = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isMigrating;

        [ObservableProperty]
        private string _statusMessage = "Click 'Scan' to discover legacy cloud saves";

        [ObservableProperty]
        private int _totalGames;

        [ObservableProperty]
        private int _migratedCount;

        [ObservableProperty]
        private int _failedCount;

        [ObservableProperty]
        private int _conflictCount;

        private readonly RcloneExecutor _rcloneExecutor = new();
        private readonly CloudProviderHelper _providerHelper = new();
        private readonly ChecksumService _checksumService = new();

        private string _configPath = "";
        private string _remoteName = "";
        private CloudProvider _provider;

        public LegacyMigrationViewModel()
        {
        }

        [RelayCommand]
        private async Task ScanForLegacyGamesAsync()
        {
            if (IsLoading) return;

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = true;
                    StatusMessage = "Scanning cloud for legacy games...";
                    LegacyGames.Clear();
                    TotalGames = 0;
                    ConflictCount = 0;
                });

                await Task.Run(async () =>
                {
                    try
                    {
                        // Get cloud config
                        var config = await ConfigManagement.LoadConfigAsync();
                        _provider = config.CloudConfig.Provider;
                        _configPath = RclonePathHelper.GetConfigPath(_provider);
                        _remoteName = _providerHelper.GetProviderConfigName(_provider);

                        // Legacy source: PlayniteCloudSave
                        string legacyBasePath = $"{_remoteName}:PlayniteCloudSave";
                        // New target: SaveTrackerCloudSave
                        string newBasePath = $"{_remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                        // List all game folders in legacy path
                        var listResult = await _rcloneExecutor.ExecuteRcloneCommand(
                            $"lsd \"{legacyBasePath}\" --config \"{_configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                            TimeSpan.FromSeconds(30)
                        );

                        if (!listResult.Success)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                StatusMessage = $"Failed to list cloud: {listResult.Error}";
                            });
                            return;
                        }

                        var folders = ParseFolderList(listResult.Output);
                        var legacyItems = new List<LegacyGameItem>();

                        foreach (var folder in folders)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                StatusMessage = $"Checking {folder}...";
                            });

                            var legacyItem = await CheckForLegacyFormat(folder, legacyBasePath, newBasePath);
                            if (legacyItem != null)
                            {
                                legacyItems.Add(legacyItem);
                            }
                        }

                        // Update UI with results
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var item in legacyItems.OrderBy(g => g.Name))
                            {
                                LegacyGames.Add(item);
                            }
                            TotalGames = legacyItems.Count;
                            ConflictCount = legacyItems.Count(g => g.HasNewFormat);
                            StatusMessage = TotalGames > 0
                                ? $"Found {TotalGames} legacy games ({ConflictCount} with conflicts)"
                                : "No legacy games found in PlayniteCloudSave ✓";
                        });
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, "Legacy scan error");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusMessage = $"Scan failed: {ex.Message}";
                        });
                    }
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }

        private async Task<LegacyGameItem?> CheckForLegacyFormat(string gameName, string legacyBasePath, string newBasePath)
        {
            try
            {
                string legacyGamePath = $"{legacyBasePath}/{gameName}";
                string newGamePath = $"{newBasePath}/{gameName}";

                // List files at root of legacy game folder
                var lsResult = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"ls \"{legacyGamePath}\" --max-depth 1 --config \"{_configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(15)
                );

                if (!lsResult.Success)
                    return null;

                var rootFiles = ParseFileList(lsResult.Output);

                // Check for legacy checksum file at root
                bool hasLegacyChecksum = rootFiles.Any(f =>
                    f.Name.Equals(ChecksumService.LegacyChecksumFilename, StringComparison.OrdinalIgnoreCase));

                if (!hasLegacyChecksum)
                    return null; // Not a legacy game

                // Check if new format folder exists (SaveTrackerCloudSave/GameName)
                // We check by trying to list it. If it exists, there is a conflict.
                var lsdNewResult = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"lsd \"{newGamePath}\" --config \"{_configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(10)
                );

                // Also check for files in new path, as it might be a flat folder without subdirs yet
                var lsNewResult = await _rcloneExecutor.ExecuteRcloneCommand(
                     $"ls \"{newGamePath}\" --max-depth 1 --config \"{_configPath}\"",
                     TimeSpan.FromSeconds(10)
                );

                bool hasNewFormat = lsdNewResult.Success || lsNewResult.Success;

                // Get list of save files (exclude checksum files)
                var saveFiles = rootFiles
                    .Where(f => !f.Name.StartsWith(".savetracker", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Try to download and parse legacy checksum for metadata
                DateTime? lastUpdated = null;
                GameUploadData? legacyData = null;
                try
                {
                    var checksumContent = await DownloadLegacyChecksumAsync(gameName, legacyBasePath);
                    if (!string.IsNullOrEmpty(checksumContent))
                    {
                        legacyData = JsonConvert.DeserializeObject<GameUploadData>(checksumContent);
                        lastUpdated = legacyData?.LastUpdated;
                    }
                }
                catch { /* Ignore parse errors */ }

                return new LegacyGameItem
                {
                    Name = gameName,
                    LegacyFiles = saveFiles.Select(f => f.Name).ToList(),
                    FileCount = saveFiles.Count,
                    TotalSize = saveFiles.Sum(f => f.Size),
                    HasNewFormat = hasNewFormat,
                    LastUpdated = lastUpdated,
                    LegacyChecksum = legacyData, // Store for migration
                    ConflictResolution = hasNewFormat ? ConflictResolution.Merge : ConflictResolution.KeepNew
                };
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error checking {gameName}: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> DownloadLegacyChecksumAsync(string gameName, string legacyBasePath)
        {
            string remotePath = $"{legacyBasePath}/{gameName}/{ChecksumService.LegacyChecksumFilename}";
            string tempPath = Path.Combine(Path.GetTempPath(), $"legacy_checksum_{Guid.NewGuid()}.json");

            try
            {
                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"copyto \"{remotePath}\" \"{tempPath}\" --config \"{_configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(15)
                );

                if (result.Success && File.Exists(tempPath))
                {
                    return await File.ReadAllTextAsync(tempPath);
                }
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
            return null;
        }

        [RelayCommand]
        private async Task MigrateSelectedAsync()
        {
            if (IsMigrating) return;

            var selectedGames = LegacyGames.Where(g => g.IsSelected && g.Status != MigrationStatus.Completed).ToList();
            if (!selectedGames.Any())
            {
                StatusMessage = "No games selected for migration";
                return;
            }

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsMigrating = true;
                    MigratedCount = 0;
                    FailedCount = 0;
                    StatusMessage = $"Migrating {selectedGames.Count} games...";
                });

                foreach (var game in selectedGames)
                {
                    await MigrateGameAsync(game);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Migration complete: {MigratedCount} succeeded, {FailedCount} failed";
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsMigrating = false);
            }
        }

        private async Task MigrateGameAsync(LegacyGameItem game)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                game.Status = MigrationStatus.InProgress;
                game.StatusMessage = "Starting migration...";
            });

            try
            {
                // PlayniteCloudSave (Legacy)
                string legacyBasePath = $"{_remoteName}:PlayniteCloudSave";
                string legacyGamePath = $"{legacyBasePath}/{game.Name}";

                // SaveTrackerCloudSave (New) - Default is at root of GameName folder
                string newBasePath = $"{_remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";
                string newGamePath = $"{newBasePath}/{game.Name}";

                // Handle conflicts
                if (game.HasNewFormat)
                {
                    switch (game.ConflictResolution)
                    {
                        case ConflictResolution.KeepNew:
                            // Skip - keep existing new format
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                game.Status = MigrationStatus.Skipped;
                                game.StatusMessage = "Kept existing new format";
                            });
                            return;

                        case ConflictResolution.Merge:
                            // Merge - copy missing files
                            await MergeFilesToNewFormatAsync(game, legacyGamePath, newGamePath);
                            break;

                        case ConflictResolution.ReplaceOld:
                            // Delete existing new folder and recreate
                            await ReplaceWithOldFormatAsync(game, legacyGamePath, newGamePath);
                            break;
                    }
                }
                else
                {
                    // No conflict - simple migration
                    await CopyToNewFormatAsync(game, legacyGamePath, newGamePath);
                }

                // Create new profile checksum
                await CreateNewChecksumAsync(game, newGamePath);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    game.Status = MigrationStatus.Completed;
                    game.StatusMessage = "Successfully migrated";
                    MigratedCount++;
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Migration failed for {game.Name}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    game.Status = MigrationStatus.Failed;
                    game.StatusMessage = ex.Message;
                    FailedCount++;
                });
            }
        }

        private async Task CopyToNewFormatAsync(LegacyGameItem game, string legacyGamePath, string newGamePath)
        {
            await Dispatcher.UIThread.InvokeAsync(() => game.StatusMessage = "Creating destination folder...");

            // Create new folder structure
            var mkdirResult = await _rcloneExecutor.ExecuteRcloneCommand(
                $"mkdir \"{newGamePath}\" --config \"{_configPath}\"",
                TimeSpan.FromSeconds(10)
            );

            // Copy files restoring structure from legacy checksum
            await CopyFilesRestoringStructureAsync(game, legacyGamePath, newGamePath, false);
        }

        private async Task MergeFilesToNewFormatAsync(LegacyGameItem game, string legacyGamePath, string newGamePath)
        {
            await Dispatcher.UIThread.InvokeAsync(() => game.StatusMessage = "Merging files...");

            // For merge, we only copy files unlikely to overwrite unless user specifically wanted to replace
            // But logic says Merge usually implies adding missing.
            // Since we restore structure, checking existence is tricky without listing all.
            // Rclone 'copy' (not copyto) by default skips identical files. 'ignore-existing' skips existing.

            await CopyFilesRestoringStructureAsync(game, legacyGamePath, newGamePath, true);
        }

        private async Task ReplaceWithOldFormatAsync(LegacyGameItem game, string legacyGamePath, string newGamePath)
        {
            await Dispatcher.UIThread.InvokeAsync(() => game.StatusMessage = "Removing existing data...");

            // Purge new location
            var deleteResult = await _rcloneExecutor.ExecuteRcloneCommand(
                $"purge \"{newGamePath}\" --config \"{_configPath}\"",
                TimeSpan.FromSeconds(30)
            );

            // Recreate
            await CopyToNewFormatAsync(game, legacyGamePath, newGamePath);
        }

        private async Task CopyFilesRestoringStructureAsync(LegacyGameItem game, string legacyGamePath, string newGamePath, bool ignoreExisting)
        {
            // If we have parsed legacy checksum data, use it to determine paths
            if (game.LegacyChecksum != null && game.LegacyChecksum.Files != null)
            {
                foreach (var kvp in game.LegacyChecksum.Files)
                {
                    // kvp.Key is usually filename or relative path in simple cases, but legacy used flat storage?
                    // User said: "legacy saves are in the same cloud remote but with a different structure"
                    // And: "migration MUST parse the legacy checksum to determine the correct subfolder structure"
                    // Suggesting legacy CLOUD storage was flat (File at Root), but Checksum has Path info.

                    // Legacy Checksum Path: "%GAMEPATH%/Saves/Save1.sav"
                    // Legacy Cloud File: "Save1.sav" (Flat at root of PlayniteCloudSave/GameName)

                    // We need to move: PlayniteCloudSave/GameName/Save1.sav -> SaveTrackerCloudSave/GameName/Saves/Save1.sav

                    string originalPath = kvp.Value.Path;
                    string legacyCloudFilename = Path.GetFileName(originalPath); // Assuming flat storage used filename

                    // If legacy checksum key is the filename, use that to find the flat file
                    string flatFilename = kvp.Key;

                    // Resolve relative path for NEW structure
                    string relativePath = ResolveRelativePath(originalPath);
                    if (string.IsNullOrEmpty(relativePath)) continue;

                    await Dispatcher.UIThread.InvokeAsync(() => game.StatusMessage = $"Copying {flatFilename}...");

                    // Source: Legacy Base + Flat Filename
                    string sourceFile = $"{legacyGamePath}/{flatFilename}";
                    // Target: New Base + Relative Path (Restored Structure)
                    string targetFile = $"{newGamePath}/{relativePath}";

                    string flags = RcloneExecutor.GetPerformanceFlags();
                    if (ignoreExisting) flags += " --ignore-existing";

                    var copyResult = await _rcloneExecutor.ExecuteRcloneCommand(
                        $"copyto \"{sourceFile}\" \"{targetFile}\" --config \"{_configPath}\" " + flags,
                        TimeSpan.FromSeconds(60)
                    );

                    if (!copyResult.Success)
                    {
                        DebugConsole.WriteWarning($"Failed to copy {flatFilename} -> {relativePath}: {copyResult.Error}");
                        // Don't throw, try next file
                    }
                }
            }
            else
            {
                // Fallback if no checksum: Copy all root files flat (best effort)
                foreach (var file in game.LegacyFiles)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => game.StatusMessage = $"Copying {file} (flat)...");

                    string sourceFile = $"{legacyGamePath}/{file}";
                    string targetFile = $"{newGamePath}/{file}";

                    string flags = RcloneExecutor.GetPerformanceFlags();
                    if (ignoreExisting) flags += " --ignore-existing";

                    await _rcloneExecutor.ExecuteRcloneCommand(
                        $"copyto \"{sourceFile}\" \"{targetFile}\" --config \"{_configPath}\" " + flags,
                        TimeSpan.FromSeconds(60)
                    );
                }
            }
        }

        private string ResolveRelativePath(string fullPath)
        {
            // Remove %GAMEPATH%, %USERPROFILE% etc from start
            if (fullPath.Contains("%GAMEPATH%", StringComparison.OrdinalIgnoreCase))
                return fullPath.Replace("%GAMEPATH%", "", StringComparison.OrdinalIgnoreCase).TrimStart('/', '\\');

            if (fullPath.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
                return fullPath.Replace("%USERPROFILE%", "", StringComparison.OrdinalIgnoreCase).TrimStart('/', '\\');

            // Check if it's an absolute path (e.g. C:\...) or UNC path (\\...)
            // If we can't resolve it via variables, fallback to just the filename (flatten)
            // This prevents creating "C/Users/..." folder structures in the cloud
            if (Path.IsPathRooted(fullPath) && !fullPath.StartsWith("%"))
            {
                return Path.GetFileName(fullPath);
            }

            // If it's already relative or unknown, verify it doesn't look like a drive path
            // e.g. "C:/Games/File" -> "File"
            if (fullPath.Contains(':'))
            {
                return Path.GetFileName(fullPath);
            }

            return fullPath.TrimStart('/', '\\');
        }

        private async Task CreateNewChecksumAsync(LegacyGameItem game, string newGamePath)
        {
            await Dispatcher.UIThread.InvokeAsync(() => game.StatusMessage = "Creating new checksum...");

            // Reuse legacy data if we have it, OR simple conversion
            if (game.LegacyChecksum == null) return;

            var oldData = game.LegacyChecksum;

            // Create new checksum with updated paths
            var newData = new GameUploadData
            {
                Files = new Dictionary<string, FileChecksumRecord>(),
                LastUpdated = DateTime.UtcNow,
                CanTrack = oldData.CanTrack,
                CanUploads = oldData.CanUploads,
                GameProvider = oldData.GameProvider,
                LastSyncStatus = "Migrated",
                AllowGameWatcher = oldData.AllowGameWatcher,
                EnableSmartSync = oldData.EnableSmartSync,
                PlayTime = oldData.PlayTime,
                DetectedPrefix = oldData.DetectedPrefix
            };

            foreach (var kvp in oldData.Files)
            {
                if (kvp.Key.Contains(".savetracker", StringComparison.OrdinalIgnoreCase)) continue;

                var record = kvp.Value;
                string newPath = record.Path;

                // Enforce %GAMEPATH% if the original path was absolute/weird and we flattened it
                // Logic: If ResolveRelativePath(original) == filename, then we put it at root.
                // So the new Path for the checksum should be "%GAMEPATH%/filename"

                string resolvedCloudPath = ResolveRelativePath(record.Path);

                if (string.Equals(resolvedCloudPath, Path.GetFileName(record.Path), StringComparison.OrdinalIgnoreCase)
                    && !record.Path.Contains("%GAMEPATH%", StringComparison.OrdinalIgnoreCase))
                {
                    // It was flattened, so we normalize the checksum path to be relative to GamePath
                    newPath = $"%GAMEPATH%/{Path.GetFileName(record.Path)}";
                }

                newData.Files[newPath] = new FileChecksumRecord
                {
                    Checksum = record.Checksum,
                    LastUpload = record.LastUpload,
                    Path = newPath,
                    FileSize = record.FileSize,
                    LastWriteTime = record.LastWriteTime
                };
            }

            // Save to temp file and upload
            string tempPath = Path.Combine(Path.GetTempPath(), $"new_checksum_{Guid.NewGuid()}.json");
            try
            {
                string json = JsonConvert.SerializeObject(newData, Formatting.Indented);
                await File.WriteAllTextAsync(tempPath, json);

                // New Name: .savetracker_profile_default.json
                string remotePath = $"{newGamePath}/{ChecksumService.ProfileChecksumFilenamePrefix}default{ChecksumService.ProfileChecksumFilenameSuffix}";

                var uploadResult = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"copyto \"{tempPath}\" \"{remotePath}\" --config \"{_configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(30)
                );

                if (!uploadResult.Success)
                {
                    throw new Exception($"Failed to upload new checksum: {uploadResult.Error}");
                }
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var game in LegacyGames)
            {
                game.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAll()
        {
            foreach (var game in LegacyGames)
            {
                game.IsSelected = false;
            }
        }

        private List<string> ParseFolderList(string output)
        {
            var folders = new List<string>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    // Format: "-1 2024-01-15 10:30:00 -1 FolderName With Spaces"
                    string name = string.Join(" ", parts.Skip(4));
                    folders.Add(name);
                }
            }
            return folders;
        }

        private List<(string Name, long Size)> ParseFileList(string output)
        {
            var files = new List<(string Name, long Size)>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Format: "12345 filename.ext" or "12345 path/filename.ext"
                var spaceIndex = trimmed.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    if (long.TryParse(trimmed.Substring(0, spaceIndex), out long size))
                    {
                        string path = trimmed.Substring(spaceIndex + 1).Trim();
                        // Only include files at root (no path separator)
                        if (!path.Contains('/') && !path.Contains('\\'))
                        {
                            files.Add((path, size));
                        }
                    }
                }
            }
            return files;
        }
    }
}
