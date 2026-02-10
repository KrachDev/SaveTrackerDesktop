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

        // Constants for checksum file naming
        public const string LegacyChecksumFilename = ".savetracker_checksums.json"; // Old global naming
        public const string ProfileChecksumFilenamePrefix = ".savetracker_profile_"; // New profile-specific naming
        public const string ProfileChecksumFilenameSuffix = ".json";

        public async Task UpdatePlayTime(string gameDirectory, TimeSpan playTime)
        {
            try
            {
                var data = await LoadChecksumData(gameDirectory).ConfigureAwait(false) ?? new GameUploadData();
                data.PlayTime = playTime;
                data.LastUpdated = DateTime.Now;

                string checksumPath = GetChecksumFilePath(gameDirectory);
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                await File.WriteAllLinesAsync(checksumPath, new[] { json }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update local PlayTime manually");
            }
        }

        /// <summary>
        /// Gets the checksum file path using the profile ID.
        /// The profile ID is converted to a readable filename.
        /// </summary>
        public string GetChecksumFilePath(string gameDirectory, string? profileId = null)
        {
            if (string.IsNullOrEmpty(gameDirectory))
                throw new ArgumentNullException(nameof(gameDirectory));

            // If no profileId, use DEFAULT profile
            if (string.IsNullOrEmpty(profileId))
            {
                profileId = "DEFAULT_PROFILE_ID";
            }

            // For DEFAULT_PROFILE_ID, always use "default"
            string profileName = profileId.Equals("DEFAULT_PROFILE_ID", StringComparison.OrdinalIgnoreCase)
                ? "default"
                : GetSanitizedProfileName(profileId);  // For GUIDs and custom IDs

            // UNIFIED NAMING: ALL profiles use .savetracker_profile_<NAME>.json
            string profileChecksumFilename = $"{ProfileChecksumFilenamePrefix}{profileName}{ProfileChecksumFilenameSuffix}";
            string profilePath = Path.Combine(gameDirectory, profileChecksumFilename);

            return profilePath;
        }

        /// <summary>
        /// Sanitizes a profile ID (usually a GUID) into a readable filename
        /// Keeps only alphanumeric characters and underscores
        /// </summary>
        private static string GetSanitizedProfileName(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return "profile";

            // Keep only alphanumeric characters - removes hyphens, underscores, etc.
            string sanitized = System.Text.RegularExpressions.Regex.Replace(
                profileId.ToLowerInvariant(),
                "[^a-z0-9]",  // Keep ONLY alphanumeric
                ""
            ).Trim();

            // Limit to 24 characters to keep filenames reasonable
            if (sanitized.Length > 24)
            {
                sanitized = sanitized.Substring(0, 24);
            }

            return string.IsNullOrEmpty(sanitized) ? "profile" : sanitized;
        }

        /// <summary>
        /// Gets the human-readable profile name from profile ID
        /// DEPRECATED - use GetSanitizedProfileName instead
        /// </summary>
        private static string GetProfileNameFromId(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return "default";

            // Handle special case for DEFAULT_PROFILE_ID
            if (profileId.Equals("DEFAULT_PROFILE_ID", StringComparison.OrdinalIgnoreCase))
                return "default";

            // For custom profiles: sanitize the name but keep it readable
            // Remove only invalid file name characters, keep spaces and numbers
            string sanitized = System.Text.RegularExpressions.Regex.Replace(
                profileId.ToLowerInvariant(),
                @"[^\w\s-]",  // Keep word chars, spaces, and hyphens
                ""
            ).Trim();

            // If we have spaces, replace with underscores for cleaner filenames
            sanitized = sanitized.Replace(" ", "_");

            // If completely empty after sanitization, use a fallback
            if (string.IsNullOrEmpty(sanitized))
                return "profile";

            return sanitized;
        }

        /// <summary>
        /// Migrates legacy global checksum file (.savetracker_checksums.json) 
        /// to the new unified profile-specific naming scheme.
        /// This runs automatically on first access to ensure data is migrated.
        /// </summary>
        public async Task<bool> MigrateFromLegacyIfNeeded(string gameDirectory, string? profileId = null)
        {
            // Normalize profileId - if null, treat as DEFAULT
            if (string.IsNullOrEmpty(profileId))
            {
                profileId = "DEFAULT_PROFILE_ID";
            }

            await _checksumFileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string legacyPath = Path.Combine(gameDirectory, LegacyChecksumFilename);
                string profilePath = GetChecksumFilePath(gameDirectory, profileId);

                // Check if migration is needed
                if (!File.Exists(legacyPath) || File.Exists(profilePath))
                {
                    return false; // Nothing to migrate
                }

                DebugConsole.WriteInfo($"[Checksum Migration] Migrating {LegacyChecksumFilename} to profile-specific: {Path.GetFileName(profilePath)}");

                try
                {
                    // Copy legacy file to profile-specific location
                    File.Copy(legacyPath, profilePath, overwrite: false);

                    DebugConsole.WriteSuccess($"[Checksum Migration] Successfully migrated checksum file to profile {profileId}");

                    // Optional: Delete legacy file after successful migration to all profiles
                    // For now, keep it for safety

                    return true;
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"[Checksum Migration] Failed to migrate checksum file: {ex.Message}");
                    return false;
                }
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }

        /// <summary>
        /// Explicitly migrates legacy .savetracker_checksums.json to all profiles in the game directory.
        /// Called during initialization to ensure complete migration.
        /// </summary>
        public async Task<int> MigrateAllLegacyChecksumsAsync(string gameDirectory)
        {
            int migratedCount = 0;
            string legacyPath = Path.Combine(gameDirectory, LegacyChecksumFilename);

            if (!File.Exists(legacyPath))
            {
                return 0; // No legacy file to migrate
            }

            await _checksumFileLock.WaitAsync();
            try
            {
                DebugConsole.WriteInfo($"[Checksum Migration] Found legacy checksum file, migrating to profile-specific naming...");

                // Load the legacy data
                string jsonContent;
                try
                {
                    using var stream = new FileStream(legacyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    jsonContent = await reader.ReadToEndAsync();
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteError($"[Checksum Migration] Failed to read legacy file: {ex.Message}");
                    return 0;
                }

                var legacyData = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);
                if (legacyData == null)
                {
                    DebugConsole.WriteWarning("[Checksum Migration] Legacy file contains no data");
                    return 0;
                }

                // Migrate to DEFAULT profile (.savetracker_profile_default.json)
                string defaultProfilePath = GetChecksumFilePath(gameDirectory, "DEFAULT_PROFILE_ID");
                if (!File.Exists(defaultProfilePath))
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(legacyData, Formatting.Indented);
                        using var stream = new FileStream(defaultProfilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                        using var writer = new StreamWriter(stream);
                        await writer.WriteAsync(json);
                        await writer.FlushAsync();

                        DebugConsole.WriteSuccess($"[Checksum Migration] Migrated legacy file to DEFAULT profile: {Path.GetFileName(defaultProfilePath)}");
                        migratedCount++;
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"[Checksum Migration] Failed to migrate to DEFAULT profile: {ex.Message}");
                        return migratedCount;
                    }
                }

                // After successful migration to DEFAULT, optionally remove legacy file
                // For maximum safety, keep it as backup
                if (migratedCount > 0)
                {
                    DebugConsole.WriteInfo("[Checksum Migration] Complete. Legacy file preserved as backup.");
                }

                return migratedCount;
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

        // Internal method without lock (for use within locked contexts)
        private async Task<GameUploadData> LoadChecksumDataInternal(string gameDirectory, string? profileId = null)
        {
            int maxRetries = 3;
            int delayMs = 500;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    string checksumFilePath = GetChecksumFilePath(gameDirectory, profileId);

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
                        jsonContent = await reader.ReadToEndAsync().ConfigureAwait(false);
                    }

                    var data = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);

                    DebugConsole.WriteDebug(
                        $"Loaded checksum data from {Path.GetFileName(checksumFilePath)} with {data?.Files?.Count ?? 0} file records"
                    );
                    return data ?? new GameUploadData();
                }
                catch (IOException ioEx) when (i < maxRetries - 1)
                {
                    DebugConsole.WriteWarning($"File access error loading checksum (Attempt {i + 1}/{maxRetries}): {ioEx.Message}");
                    await Task.Delay(delayMs).ConfigureAwait(false);
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

        // Public method with lock (legacy signature, uses DEFAULT profile)
        public async Task<GameUploadData> LoadChecksumData(string gameDirectory)
        {
            return await LoadChecksumData(gameDirectory, profileId: null);
        }

        // Public method with lock and profile support
        public async Task<GameUploadData> LoadChecksumData(string gameDirectory, string? profileId)
        {
            await _checksumFileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await LoadChecksumDataInternal(gameDirectory, profileId).ConfigureAwait(false);
            }
            finally
            {
                _checksumFileLock.Release();
            }
        }

        // Internal method without lock (for use within locked contexts)
        private async Task SaveChecksumDataInternal(GameUploadData data, string gameDirectory, string? profileId = null)
        {
            int maxRetries = 3;
            int delayMs = 500;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    string checksumFilePath = GetChecksumFilePath(gameDirectory, profileId);

                    if (!Directory.Exists(gameDirectory))
                    {
                        Directory.CreateDirectory(gameDirectory);
                    }

                    string jsonContent = JsonConvert.SerializeObject(data, Formatting.Indented);

                    using (var stream = new FileStream(checksumFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream))
                    {
                        await writer.WriteAsync(jsonContent).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                    }

                    DebugConsole.WriteDebug($"Saved checksum data to {Path.GetFileName(checksumFilePath)}");
                    DebugConsole.WriteInfo(
                        $"Checksum file updated with {data.Files.Count} file records"
                    );
                    return; // Success
                }
                catch (IOException ioEx) when (i < maxRetries - 1)
                {
                    DebugConsole.WriteWarning($"File access error saving checksum (Attempt {i + 1}/{maxRetries}): {ioEx.Message}");
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    delayMs *= 2; // Exponential backoff
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Failed to save checksum data to {gameDirectory}");
                    throw;
                }
            }
        }

        // Public method with lock (legacy signature, uses DEFAULT profile)
        public async Task SaveChecksumData(GameUploadData data, string gameDirectory)
        {
            await SaveChecksumData(data, gameDirectory, profileId: null);
        }

        // Public method with lock and profile support
        public async Task SaveChecksumData(GameUploadData data, string gameDirectory, string? profileId)
        {
            await _checksumFileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await SaveChecksumDataInternal(data, gameDirectory, profileId).ConfigureAwait(false);
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

        public async Task UpdateFileChecksumRecord(string filePath, string gameDirectory, string? detectedPrefix = null, string? profileId = null)
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

                var checksumData = await LoadChecksumDataInternal(gameDirectory, profileId);

                // Contract the path to portable format before storing (with Wine prefix support)
                string portablePath = PathContractor.ContractPath(filePath, gameDirectory, detectedPrefix);

                var fileInfo = new FileInfo(filePath);
                checksumData.Files[portablePath] = new FileChecksumRecord
                {
                    Checksum = checksum,
                    LastUpload = DateTime.UtcNow,
                    FileSize = fileInfo.Length,
                    Path = portablePath,
                    LastWriteTime = fileInfo.LastWriteTimeUtc // Store timestamp for future optimizations
                };

                await SaveChecksumDataInternal(checksumData, gameDirectory, profileId);

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
        public async Task<bool> MigratePathsIfNeeded(string gameDirectory, string? detectedPrefix, string? profileId = null)
        {
            if (string.IsNullOrEmpty(detectedPrefix))
                return false; // Can't migrate without a prefix

            await _checksumFileLock.WaitAsync();
            try
            {
                var checksumData = await LoadChecksumDataInternal(gameDirectory, profileId);
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
                    await SaveChecksumDataInternal(checksumData, gameDirectory, profileId);

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

        public async Task CleanupChecksumRecords(string gameDirectory, TimeSpan maxAge, string? profileId = null)
        {
            await _checksumFileLock.WaitAsync();
            try
            {
                var checksumData = await LoadChecksumDataInternal(gameDirectory, profileId);
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
                    await SaveChecksumDataInternal(checksumData, gameDirectory, profileId);
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
