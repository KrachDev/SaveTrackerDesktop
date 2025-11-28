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

        public string GetChecksumFilePath(string gameDirectory)
        {
            return Path.Combine(gameDirectory, SaveFileUploadManager.CHECKSUM_FILENAME);
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

        public async Task UpdateFileChecksumRecord(string filePath, string gameDirectory)
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

                // Contract the path to portable format before storing
                string portablePath = PathContractor.ContractPath(filePath, gameDirectory);

                checksumData.Files[fileName] = new FileChecksumRecord
                {
                    Checksum = checksum,
                    LastUpload = DateTime.UtcNow,
                    FileSize = new FileInfo(filePath).Length,
                    Path = portablePath
                };

                await SaveChecksumDataInternal(checksumData, gameDirectory);

                DebugConsole.WriteDebug(
                    $"Updated checksum record for {fileName} in {gameDirectory}"
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
    }
}
