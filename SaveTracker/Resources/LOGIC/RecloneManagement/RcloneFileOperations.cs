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
            CloudProvider? provider = null)
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
            string relativePath = PathContractor.ContractPath(filePath, gameDirectory);
            string fileName = Path.GetFileName(filePath);
            string remotePath = $"{remoteBasePath}/{relativePath}";

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

                bool needsUpload = await ShouldUploadFileWithChecksum(
                    filePath,
                    gameDirectory
                );

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
                bool uploadSuccess = await _transferService.UploadFileWithRetry(filePath, remotePath, fileName, effectiveProvider);

                if (uploadSuccess)
                {
                    // Update checksum tracking after successful upload
                    await _checksumService.UpdateFileChecksumRecord(filePath, gameDirectory);

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

        private async Task<bool> ShouldUploadFileWithChecksum(
            string localFilePath,
            string gameDirectory)
        {
            try
            {
                // Get current file checksum
                string currentChecksum = await _checksumService.GetFileChecksum(localFilePath);
                if (string.IsNullOrEmpty(currentChecksum))
                {
                    DebugConsole.WriteWarning(
                        "Could not compute local file checksum - uploading to be safe"
                    );
                    return true;
                }

                DebugConsole.WriteDebug($"Current file MD5: {currentChecksum}");

                // Get stored checksum from JSON in game directory
                string storedChecksum = await GetStoredFileChecksum(localFilePath, gameDirectory);

                if (string.IsNullOrEmpty(storedChecksum))
                {
                    DebugConsole.WriteInfo("No stored checksum found - upload needed");
                    return true;
                }

                DebugConsole.WriteDebug($"Stored MD5: {storedChecksum}");

                // Compare checksums
                bool different = !currentChecksum.Equals(
                    storedChecksum,
                    StringComparison.OrdinalIgnoreCase
                );

                DebugConsole.WriteInfo(different
                    ? "File has changed since last upload - upload needed"
                    : "File unchanged since last upload - skipping");

                return different;
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
                // Use contracted path as dictionary key (new format)
                string contractedPath = PathContractor.ContractPath(filePath, gameDirectory);

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
            await _checksumService.SaveChecksumData(data, game.InstallDirectory);
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

        public async Task<bool> DownloadWithChecksumAsync(string remotePath, Game game, CloudProvider? provider = null)
        {
            string stagingFolder = Path.Combine(Path.GetTempPath(), "SaveTracker_Download_" + Guid.NewGuid().ToString("N"));

            try
            {
                DebugConsole.WriteInfo($"Downloading cloud saves to staging folder: {stagingFolder}");
                Directory.CreateDirectory(stagingFolder);

                CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(game);
                var downloadResult = await _transferService.DownloadDirectory(remotePath, stagingFolder, effectiveProvider);

                if (!downloadResult)
                {
                    DebugConsole.WriteError($"Failed to download files to staging");
                    return false;
                }

                // The checksum file might be in a subdirectory due to path preservation
                // Search for it recursively
                string checksumFilePath = Path.Combine(stagingFolder, SaveFileUploadManager.ChecksumFilename);
                if (!File.Exists(checksumFilePath))
                {
                    // Search in subdirectories
                    var foundFiles = Directory.GetFiles(stagingFolder, SaveFileUploadManager.ChecksumFilename, SearchOption.AllDirectories);
                    if (foundFiles.Length > 0)
                    {
                        checksumFilePath = foundFiles[0];
                        DebugConsole.WriteInfo($"Found checksum file in subdirectory: {checksumFilePath}");
                    }
                }

                if (!File.Exists(checksumFilePath))
                {
                    DebugConsole.WriteWarning("Checksum file not found - using fallback");
                    await CopyDirectoryContents(stagingFolder, game.InstallDirectory);
                    return true;
                }

                DebugConsole.WriteInfo("Reading checksum file...");
                string checksumJson = await File.ReadAllTextAsync(checksumFilePath);
                var checksumData = JsonConvert.DeserializeObject<GameUploadData>(checksumJson);

                if (checksumData?.Files == null || checksumData.Files.Count == 0)
                {
                    DebugConsole.WriteWarning("Checksum file is empty or invalid");
                    return false;
                }

                int successCount = 0;
                int failCount = 0;

                foreach (var fileEntry in checksumData.Files)
                {
                    try
                    {
                        string relativePath = fileEntry.Key;
                        string fileName = Path.GetFileName(relativePath);
                        // Try the full relative path first (Rclone preserves structure)
                        string sourceFile = Path.Combine(stagingFolder, relativePath);
                        if (!File.Exists(sourceFile))
                        {
                            // Fallback to flat filename
                            sourceFile = Path.Combine(stagingFolder, fileName);
                        }

                        string targetPath = fileEntry.Value.GetAbsolutePath(game.InstallDirectory);
                        if (!File.Exists(sourceFile))
                        {
                            DebugConsole.WriteWarning($"File not found in staging: {fileName}");
                            failCount++;
                            continue;
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
                        DebugConsole.WriteError($"✗ Failed to restore {fileEntry.Key}: {ex.Message}");
                        failCount++;
                    }
                }

                // Copy checksum file to game install directory for future tracking
                try
                {
                    string targetChecksumPath = Path.Combine(game.InstallDirectory, SaveFileUploadManager.ChecksumFilename);
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
                string checksumLocalSpec = Path.Combine(game.InstallDirectory, SaveFileUploadManager.ChecksumFilename);
                string checksumRelative = PathContractor.ContractPath(checksumLocalSpec, game.InstallDirectory);
                string checksumRemotePath = $"{remotePath}/{checksumRelative}";
                string checksumLocalPath = Path.Combine(stagingFolder, SaveFileUploadManager.ChecksumFilename);

                var checksumResult = await _transferService.DownloadFileWithRetry(
                    checksumRemotePath,
                    checksumLocalPath,
                    SaveFileUploadManager.ChecksumFilename,
                    effectiveProvider);

                if (!checksumResult)
                {
                    DebugConsole.WriteWarning("Checksum file not found - will use relative paths for restore");
                }

                Dictionary<string, FileChecksumRecord> checksumFiles = null;
                string checksumFilePath = Path.Combine(stagingFolder, SaveFileUploadManager.ChecksumFilename);

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
                            // Fallback: Use InstallDirectory + RelativePath
                            targetPath = Path.Combine(game.InstallDirectory, relativePath);
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
                    string targetChecksumPath = Path.Combine(game.InstallDirectory, SaveFileUploadManager.ChecksumFilename);
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
                if (downloadResult != null) downloadResult.FailedCount++;
                return;
            }

            if (string.IsNullOrEmpty(remoteFile.Name))
            {
                DebugConsole.WriteError("ProcessDownloadFile: remoteFile.Name is null or empty");
                downloadResult?.FailedFiles?.Add("Unknown file (remoteFile.Name was null/empty)");
                if (downloadResult != null) downloadResult.FailedCount++;
                return;
            }

            if (string.IsNullOrEmpty(localDownloadPath))
            {
                DebugConsole.WriteError($"ProcessDownloadFile: localDownloadPath is null or empty for file {remoteFile.Name}");
                downloadResult?.FailedFiles?.Add(remoteFile.Name);
                if (downloadResult != null) downloadResult.FailedCount++;
                return;
            }

            if (downloadResult == null)
            {
                DebugConsole.WriteError($"ProcessDownloadFile: downloadResult is null for file {remoteFile.Name}");
                return;
            }

            if (downloadResult.FailedFiles == null)
            {
                downloadResult.FailedFiles = new List<string>();
            }

            string localFilePath;
            string remoteFilePath;

            try
            {
                localFilePath = Path.Combine(localDownloadPath, remoteFile.Name);
                remoteFilePath = $"{remoteBasePath}/{remoteFile.Name}";
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error creating file paths for {remoteFile.Name}: {ex.Message}");
                downloadResult.FailedCount++;
                downloadResult.FailedFiles.Add(remoteFile.Name);
                return;
            }

            string gameDirectory = game?.InstallDirectory;

            if (string.IsNullOrEmpty(gameDirectory))
            {
                DebugConsole.WriteWarning(
                    "Game directory not available - checksum tracking disabled for download"
                );
                gameDirectory = localDownloadPath;
            }

            DebugConsole.WriteSeparator('-', 40);
            DebugConsole.WriteInfo($"Processing: {remoteFile.Name}");
            DebugConsole.WriteKeyValue("Game directory", gameDirectory ?? "null");

            try
            {
                progressCallback?.Invoke($"Checking {remoteFile.Name}...");

                DebugConsole.WriteKeyValue("Remote file size", $"{remoteFile.Size:N0} bytes");

                if (remoteFile.ModTime != default(DateTime))
                {
                    DebugConsole.WriteKeyValue(
                        "Remote modified",
                        remoteFile.ModTime.ToString("yyyy-MM-dd HH:mm:ss")
                    );
                }

                bool shouldDownload = await ShouldDownloadFile(localFilePath, overwriteExisting, effectiveProvider);

                if (!shouldDownload)
                {
                    DebugConsole.WriteSuccess(
                        $"SKIPPED: {remoteFile.Name} - Local file is up to date"
                    );
                    downloadResult.SkippedCount++;
                    downloadResult.SkippedSize += remoteFile.Size;
                    return;
                }

                progressCallback?.Invoke($"Downloading {remoteFile.Name}...");

                DebugConsole.WriteInfo($"DOWNLOADING: {remoteFile.Name}");

                bool downloadSuccess = await _transferService.DownloadFileWithRetry(
                    remoteFilePath,
                    localFilePath,
                    remoteFile.Name,
                    effectiveProvider
                );

                if (downloadSuccess)
                {
                    if (!string.IsNullOrEmpty(gameDirectory))
                    {
                        try
                        {
                            await _checksumService.UpdateFileChecksumRecord(localFilePath, gameDirectory);
                        }
                        catch (Exception checksumEx)
                        {
                            DebugConsole.WriteWarning($"Failed to update checksum for {remoteFile.Name}: {checksumEx.Message}");
                        }
                    }

                    DebugConsole.WriteSuccess($"Download completed: {remoteFile.Name}");
                    downloadResult.DownloadedCount++;
                    downloadResult.DownloadedSize += remoteFile.Size;
                }
                else
                {
                    DebugConsole.WriteError($"Download failed after retries: {remoteFile.Name}");
                    downloadResult.FailedCount++;
                    downloadResult.FailedFiles.Add(remoteFile.Name);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Unexpected error processing {remoteFile.Name}");
                downloadResult.FailedCount++;
                downloadResult.FailedFiles.Add(remoteFile.Name);
            }
        }

        private Task<bool> ShouldDownloadFile(string localFilePath, bool overwriteExisting, CloudProvider? provider = null)
        {
            try
            {
                if (!File.Exists(localFilePath))
                {
                    DebugConsole.WriteInfo("Local file doesn't exist - download needed");
                    return Task.FromResult(true);
                }

                if (overwriteExisting)
                {
                    DebugConsole.WriteInfo("Overwrite existing enabled - download needed");
                    return Task.FromResult(true);
                }

                DebugConsole.WriteInfo(
                    "Local file exists and overwrite disabled - skipping download"
                );
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Download check failed - downloading to be safe");
                return Task.FromResult(true);
            }
        }


        public async Task<string> GetFileChecksum(string filePath)
        {
            return await _checksumService.GetFileChecksum(filePath);
        }

        public async Task<bool> DownloadFile(string remotePath, string localPath, CloudProvider? provider = null)
        {
            CloudProvider effectiveProvider = provider ?? await GetEffectiveProvider(_currentGame);
            return await _transferService.DownloadFileWithRetry(remotePath, localPath, Path.GetFileName(localPath), effectiveProvider);
        }

        public async Task CleanupChecksumRecords(Game game, TimeSpan maxAge)
        {
            await _checksumService.CleanupChecksumRecords(game.InstallDirectory, maxAge);
        }

        // Data classes moved to RcloneDataTypes.cs

    }
}