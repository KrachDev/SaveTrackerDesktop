using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Security.Cryptography;
using SaveTracker.Resources.SAVE_SYSTEM;
using static CloudConfig;
using SaveTracker.Resources.LOGIC.RecloneManagement;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneFileOperations
    {
        private readonly RcloneExecutor _executor = new RcloneExecutor();
        private readonly Game _currentGame;

        // Static semaphore to prevent concurrent file access
        private static readonly SemaphoreSlim _checksumFileLock = new SemaphoreSlim(1, 1);

        public static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");

        private static readonly string ToolsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );

        private readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");
        private readonly int _maxRetries = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(10);

        public RcloneFileOperations(Game currentGame = null)
        {
            _currentGame = currentGame;
        }

        private string GetChecksumFilePath(string gameDirectory)
        {
            return Path.Combine(gameDirectory, ".savetracker_checksums.json");
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
            Game game = null)
        {
            // Use provided game or fall back to constructor game
            game = game ?? _currentGame;

            string fileName = Path.GetFileName(filePath);
            string remotePath = $"{remoteBasePath}/{fileName}";

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

            DebugConsole.WriteSeparator('-', 40);
            DebugConsole.WriteInfo($"Processing: {fileName}");
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

                bool uploadSuccess = await UploadFileWithRetry(filePath, remotePath, fileName);

                if (uploadSuccess)
                {
                    // Update checksum tracking after successful upload
                    await UpdateFileChecksumRecord(filePath, game);

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
                string currentChecksum = await GetFileChecksum(localFilePath);
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

        private async Task UpdateFileChecksumRecord(string filePath, Game game)
        {
            await _checksumFileLock.WaitAsync();
            try
            {
                string fileName = Path.GetFileName(filePath);
                string checksum = await GetFileChecksum(filePath);

                if (string.IsNullOrEmpty(checksum))
                {
                    DebugConsole.WriteWarning(
                        $"Could not compute checksum for {fileName} - skipping record update"
                    );
                    return;
                }

                var checksumData = await LoadChecksumDataInternal(game.InstallDirectory);

                // Contract the path to portable format before storing
                string portablePath = PathContractor.ContractPath(filePath, game.InstallDirectory);

                checksumData.Files[fileName] = new FileChecksumRecord
                {
                    Checksum = checksum,
                    LastUpload = DateTime.UtcNow,
                    FileSize = new FileInfo(filePath).Length,
                    Path = portablePath
                };

                await SaveChecksumDataInternal(checksumData, game.InstallDirectory);

                DebugConsole.WriteDebug(
                    $"Updated checksum record for {fileName} in {game.InstallDirectory}"
                );
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update checksum record");
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }

        public async Task<string> GetFileChecksum(string filePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var hashBytes = await Task.Run(() => md5.ComputeHash(stream));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to compute file checksum");
                return null;
            }
        }

        private async Task<string> GetStoredFileChecksum(string filePath, string gameDirectory)
        {
            try
            {
                var checksumData = await LoadChecksumData(gameDirectory);
                string fileName = Path.GetFileName(filePath);

                if (checksumData.Files.TryGetValue(fileName, out var fileRecord))
                {
                    DebugConsole.WriteDebug(
                        $"Found stored checksum for {fileName} from {fileRecord.LastUpload:yyyy-MM-dd HH:mm:ss}"
                    );
                    return fileRecord.Checksum;
                }

                DebugConsole.WriteDebug($"No stored checksum found for {fileName}");
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

        // Internal method without lock (for use within locked contexts)
        private async Task<GameUploadData> LoadChecksumDataInternal(string gameDirectory)
        {
            try
            {
                string checksumFilePath = GetChecksumFilePath(gameDirectory);

                if (!File.Exists(checksumFilePath))
                {
                    DebugConsole.WriteDebug(
                        $"Checksum file doesn't exist at {checksumFilePath} - creating new one"
                    );
                    return new GameUploadData();
                }

                string jsonContent;
                using (var stream = new FileStream(checksumFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    jsonContent = await reader.ReadToEndAsync();
                }

                var data = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);

                DebugConsole.WriteDebug(
                    $"Loaded checksum data from {checksumFilePath} with {data?.Files?.Count ?? 0} file records"
                );
                return data ?? new GameUploadData();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(
                    ex,
                    $"Failed to load checksum data from {gameDirectory} - creating new"
                );
                return new GameUploadData();
            }
        }

        // Public method with lock
        public async Task<GameUploadData> LoadChecksumData(string gameDirectory)
        {
            await _checksumFileLock.WaitAsync();
            try
            {
                return await LoadChecksumDataInternal(gameDirectory);
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }

        // Internal method without lock (for use within locked contexts)
        private async Task SaveChecksumDataInternal(GameUploadData data, string gameDirectory)
        {
            string checksumFilePath = GetChecksumFilePath(gameDirectory);

            if (!EnsureGameDirectoryExists(gameDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Cannot access game directory: {gameDirectory}"
                );
            }

            string jsonContent = JsonConvert.SerializeObject(data, Formatting.Indented);

            using (var stream = new FileStream(checksumFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(jsonContent);
                await writer.FlushAsync();
            }

            DebugConsole.WriteDebug($"Saved checksum data to {checksumFilePath}");
            DebugConsole.WriteInfo(
                $"Checksum file updated with {data.Files.Count} file records"
            );
        }

        // Public method with lock
        public async Task SaveChecksumData(GameUploadData data, Game game)
        {
            await _checksumFileLock.WaitAsync();
            try
            {
                await SaveChecksumDataInternal(data, game.InstallDirectory);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to save checksum data to {game.InstallDirectory}");
                throw;
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }

        private async Task<bool> UploadFileWithRetry(
            string localPath,
            string remotePath,
            string fileName)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    DebugConsole.WriteDebug(
                        $"Upload attempt {attempt}/{_maxRetries} for {fileName}"
                    );

                    var result = await _executor.ExecuteRcloneCommand(
                        $"copyto \"{localPath}\" \"{remotePath}\" --config \"{_configPath}\" --progress",
                        _processTimeout
                    );

                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Upload successful on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");

                        if (attempt < _maxRetries)
                        {
                            DebugConsole.WriteInfo(
                                $"Waiting {_retryDelay.TotalSeconds} seconds before retry..."
                            );
                            await Task.Delay(_retryDelay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Upload attempt {attempt} exception");

                    if (attempt < _maxRetries)
                    {
                        await Task.Delay(_retryDelay);
                    }
                }
            }

            return false;
        }

        public async Task<bool> ShouldUploadFile(string localFilePath, string remotePath)
        {
            try
            {
                bool remoteExists = await RemoteFileExists(remotePath);
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

        private async Task<bool> RemoteFileExists(string remotePath)
        {
            try
            {
                var result = await _executor.ExecuteRcloneCommand(
                    $"lsl \"{remotePath}\" --config \"{_configPath}\"",
                    TimeSpan.FromSeconds(15)
                );
                return result.Success && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check remote file existence");
                return false;
            }
        }
        public async Task<bool> CheckCloudSaveExistsAsync(string remoteBasePath)
        {
            try
            {
                var result = await _executor.ExecuteRcloneCommand(
                    $"lsf \"{remoteBasePath}\" --config \"{_configPath}\" --max-depth 1",
                    TimeSpan.FromSeconds(15)
                );

                return result.Success && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check cloud save existence");
                return false;
            }
        }

        public async Task<bool> DownloadDirectory(string remotePath, string localPath)
        {
            try
            {
                DebugConsole.WriteInfo($"Downloading directory from {remotePath} to {localPath}");

                var result = await _executor.ExecuteRcloneCommand(
                    $"copy \"{remotePath}\" \"{localPath}\" --config \"{_configPath}\" --progress",
                    TimeSpan.FromMinutes(5)
                );

                if (result.Success)
                {
                    DebugConsole.WriteSuccess("Directory download successful");
                    return true;
                }
                else
                {
                    DebugConsole.WriteError($"Directory download failed: {result.Error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Directory download exception");
                return false;
            }
        }

        public async Task<bool> DownloadWithChecksumAsync(string remotePath, Game game)
        {
            string stagingFolder = Path.Combine(Path.GetTempPath(), "SaveTracker_Download_" + Guid.NewGuid().ToString("N"));

            try
            {
                DebugConsole.WriteInfo($"Downloading cloud saves to staging folder: {stagingFolder}");
                Directory.CreateDirectory(stagingFolder);

                var downloadResult = await _executor.ExecuteRcloneCommand(
                    $"copy \"{remotePath}\" \"{stagingFolder}\" --config \"{_configPath}\" --progress",
                    TimeSpan.FromMinutes(5)
                );

                if (!downloadResult.Success)
                {
                    DebugConsole.WriteError($"Failed to download files to staging: {downloadResult.Error}");
                    return false;
                }

                string checksumFilePath = Path.Combine(stagingFolder, ".savetracker_checksums.json");
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
                        string sourceFile = Path.Combine(stagingFolder, fileName);
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

        private async Task CopyDirectoryContents(string sourceDir, string targetDir)
        {
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                if (fileName == ".savetracker_checksums.json")
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
            Action<string> progressCallback = null)
        {
            game = game ?? _currentGame;

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

                bool shouldDownload = await ShouldDownloadFile(localFilePath, overwriteExisting);

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

                bool downloadSuccess = await DownloadFileWithRetry(
                    remoteFilePath,
                    localFilePath,
                    remoteFile.Name
                );

                if (downloadSuccess)
                {
                    if (!string.IsNullOrEmpty(gameDirectory))
                    {
                        try
                        {
                            await UpdateFileChecksumRecord(localFilePath, game);
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

        private Task<bool> ShouldDownloadFile(string localFilePath, bool overwriteExisting)
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

        public async Task<bool> DownloadFile(string remotePath, string localPath)
        {
            try
            {
                var result = await _executor.ExecuteRcloneCommand(
                    $"copyto \"{remotePath}\" \"{localPath}\" --config \"{_configPath}\"",
                    _processTimeout
                );

                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DownloadFileWithRetry(
            string remotePath,
            string localPath,
            string fileName)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    DebugConsole.WriteDebug(
                        $"Download attempt {attempt}/{_maxRetries} for {fileName}"
                    );

                    var result = await _executor.ExecuteRcloneCommand(
                        $"copyto \"{remotePath}\" \"{localPath}\" --config \"{_configPath}\" --progress",
                        _processTimeout
                    );

                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Download successful on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");

                        if (attempt < _maxRetries)
                        {
                            DebugConsole.WriteInfo(
                                $"Waiting {_retryDelay.TotalSeconds} seconds before retry..."
                            );
                            await Task.Delay(_retryDelay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Download attempt {attempt} exception");

                    if (attempt < _maxRetries)
                    {
                        await Task.Delay(_retryDelay);
                    }
                }
            }

            return false;
        }

        public async Task CleanupChecksumRecords(Game game, TimeSpan maxAge)
        {
            await _checksumFileLock.WaitAsync();
            try
            {
                var checksumData = await LoadChecksumDataInternal(game.InstallDirectory);
                var cutoffDate = DateTime.UtcNow - maxAge;

                var filesToRemove = checksumData.Files
                    .Where(kvp => kvp.Value.LastUpload < cutoffDate)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var fileName in filesToRemove)
                {
                    checksumData.Files.Remove(fileName);
                }

                if (filesToRemove.Any())
                {
                    await SaveChecksumDataInternal(checksumData, game.InstallDirectory);
                    DebugConsole.WriteInfo(
                        $"Cleaned up {filesToRemove.Count} old checksum records from {game.InstallDirectory}"
                    );
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to cleanup checksum records");
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }
    }

    #region Data Classes

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
    }

    public class FileChecksumRecord
    {
        public string Checksum { get; set; }
        public DateTime LastUpload { get; set; }
        public string Path { get; set; }
        public long FileSize { get; set; }

        public string GetAbsolutePath(string gameDirectory = null)
        {
            if (string.IsNullOrEmpty(Path))
                return Path;

            if (!string.IsNullOrEmpty(gameDirectory) &&
                Path.StartsWith("%GAMEPATH%", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = Path.Substring("%GAMEPATH%".Length).TrimStart('/', '\\');
                return System.IO.Path.Combine(gameDirectory, relativePath);
            }

            return PathContractor.ExpandPath(Path, gameDirectory);
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

    #endregion
}