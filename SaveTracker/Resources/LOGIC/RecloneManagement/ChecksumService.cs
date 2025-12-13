using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class ChecksumService
    {
        private static readonly SemaphoreSlim _checksumFileLock = new SemaphoreSlim(1, 1);

        public async Task UpdatePlayTime(string gameDirectory, TimeSpan playTime)
        {
            try
            {
                var data = await LoadChecksumData(gameDirectory) ?? new GameUploadData();
                data.PlayTime = playTime;
                data.LastUpdated = DateTime.Now;

                string checksumPath = GetChecksumFilePath(gameDirectory);
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                await File.WriteAllLinesAsync(checksumPath, new[] { json });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update local PlayTime manually");
            }
        }

        public string GetChecksumFilePath(string gameDirectory)
        {
            return Path.Combine(gameDirectory, SaveFileUploadManager.ChecksumFilename);
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

        // Internal method without lock (for use within locked contexts)
        private async Task<GameUploadData> LoadChecksumDataInternal(string gameDirectory)
        {
            int maxRetries = 3;
            int delayMs = 500;

            for (int i = 0; i < maxRetries; i++)
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
                catch (IOException ioEx) when (i < maxRetries - 1)
                {
                    DebugConsole.WriteWarning($"File access error loading checksum (Attempt {i + 1}/{maxRetries}): {ioEx.Message}");
                    await Task.Delay(delayMs);
                    delayMs *= 2; // Exponential backoff
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
            return new GameUploadData();
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
            int maxRetries = 3;
            int delayMs = 500;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    string checksumFilePath = GetChecksumFilePath(gameDirectory);

                    if (!Directory.Exists(gameDirectory))
                    {
                        Directory.CreateDirectory(gameDirectory);
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
                    return; // Success
                }
                catch (IOException ioEx) when (i < maxRetries - 1)
                {
                    DebugConsole.WriteWarning($"File access error saving checksum (Attempt {i + 1}/{maxRetries}): {ioEx.Message}");
                    await Task.Delay(delayMs);
                    delayMs *= 2; // Exponential backoff
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Failed to save checksum data to {gameDirectory}");
                    // On final failure or non-IO exception, we might want to throw or just log. 
                    // Original code logged exception inside this method but caller also caught?
                    // Caller 'SaveChecksumData' catches and throws.
                    // If we return here, caller thinks success?
                    // We should rethrow if we can't save.
                    throw;
                }
            }
        }

        // Public method with lock
        public async Task SaveChecksumData(GameUploadData data, string gameDirectory)
        {
            await _checksumFileLock.WaitAsync();
            try
            {
                await SaveChecksumDataInternal(data, gameDirectory);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to save checksum data to {gameDirectory}");
                throw;
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }

        public async Task UpdateFileChecksumRecord(string filePath, string gameDirectory, string? detectedPrefix = null)
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

                var checksumData = await LoadChecksumDataInternal(gameDirectory);

                // Contract the path to portable format before storing (with Wine prefix support)
                string portablePath = PathContractor.ContractPath(filePath, gameDirectory, detectedPrefix);

                checksumData.Files[portablePath] = new FileChecksumRecord
                {
                    Checksum = checksum,
                    LastUpload = DateTime.UtcNow,
                    FileSize = new FileInfo(filePath).Length,
                    Path = portablePath
                };

                await SaveChecksumDataInternal(checksumData, gameDirectory);

                DebugConsole.WriteDebug(
                    $"Updated checksum record for {portablePath} in {gameDirectory}"
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

        /// <summary>
        /// Migrates absolute Linux Wine paths to Windows-style environment variables
        /// Called automatically when loading checksum data on Linux
        /// </summary>
        public async Task<bool> MigratePathsIfNeeded(string gameDirectory, string? detectedPrefix)
        {
            if (string.IsNullOrEmpty(detectedPrefix))
                return false; // Can't migrate without a prefix

            await _checksumFileLock.WaitAsync();
            try
            {
                var checksumData = await LoadChecksumDataInternal(gameDirectory);
                if (checksumData.Files.Count == 0)
                    return false; // Nothing to migrate

                bool needsMigration = false;
                var migratedFiles = new Dictionary<string, FileChecksumRecord>();

                foreach (var kvp in checksumData.Files)
                {
                    string currentPath = kvp.Value.Path;

                    // Check if this path needs migration (absolute Linux path)
                    if (currentPath.StartsWith("/") && currentPath.Contains("/drive_c/"))
                    {
                        needsMigration = true;

                        // Convert to Windows-style env var path
                        string migratedPath = PathContractor.ContractWinePath(currentPath, detectedPrefix, gameDirectory);

                        DebugConsole.WriteInfo($"Migrating path: {currentPath} → {migratedPath}");

                        // Update the record with new path
                        var record = kvp.Value;
                        record.Path = migratedPath;
                        migratedFiles[migratedPath] = record;
                    }
                    else
                    {
                        // Keep as-is
                        migratedFiles[kvp.Key] = kvp.Value;
                    }
                }

                if (needsMigration)
                {
                    checksumData.Files = migratedFiles;
                    await SaveChecksumDataInternal(checksumData, gameDirectory);

                    DebugConsole.WriteSuccess(
                        $"Successfully migrated {migratedFiles.Count} file paths to cross-platform format"
                    );
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to migrate checksum paths");
                return false;
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }

        public async Task CleanupChecksumRecords(string gameDirectory, TimeSpan maxAge)
        {
            await _checksumFileLock.WaitAsync();
            try
            {
                var checksumData = await LoadChecksumDataInternal(gameDirectory);
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
                    await SaveChecksumDataInternal(checksumData, gameDirectory);
                    DebugConsole.WriteInfo(
                        $"Cleaned up {filesToRemove.Count} old checksum records from {gameDirectory}"
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

        /// <summary>
        /// Count how many files referenced in the checksum data actually exist on disk
        /// </summary>
        public int CountExistingFiles(GameUploadData checksumData, string gameDirectory, string? detectedPrefix = null)
        {
            if (checksumData?.Files == null || checksumData.Files.Count == 0)
                return 0;

            int existingCount = 0;
            foreach (var fileRecord in checksumData.Files.Values)
            {
                try
                {
                    string absolutePath = fileRecord.GetAbsolutePath(gameDirectory, detectedPrefix);
                    if (File.Exists(absolutePath))
                    {
                        existingCount++;
                    }
                    else
                    {
                        DebugConsole.WriteDebug($"File not found: {absolutePath}");
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteDebug($"Error checking file existence: {ex.Message}");
                }
            }

            return existingCount;
        }
    }
}
