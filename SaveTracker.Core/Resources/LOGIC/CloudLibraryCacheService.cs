using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SaveTracker.Models;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using static CloudConfig;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    /// <summary>
    /// Singleton service to manage cloud library cache.
    /// Caches game metadata (PlayTime, FileCount) and icons locally in a structure mirroring the cloud.
    /// </summary>
    public class CloudLibraryCacheService
    {
        // Singleton instance
        private static CloudLibraryCacheService? _instance;
        private static readonly object _lock = new object();

        public static CloudLibraryCacheService Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new CloudLibraryCacheService();
                }
            }
        }

        // Cache paths
        public static string CacheDirectory => Path.Combine(ConfigManagement.BASE_PATH, "Data", "CloudCache");
        public static string AchievementsCacheDirectory => Path.Combine(CacheDirectory, "Achievements");
        public static string MetadataPath => Path.Combine(CacheDirectory, "metadata.json");

        // In-memory cache
        private CloudLibraryCache? _memoryCache;
        private readonly object _cacheLock = new object();

        // Rclone helper instances
        private readonly RcloneExecutor _rcloneExecutor = new RcloneExecutor();
        private readonly CloudProviderHelper _providerHelper = new CloudProviderHelper();

        // Refresh state
        private bool _isRefreshing = false;
        public bool IsRefreshing => _isRefreshing;

        private CloudLibraryCacheService()
        {
            EnsureDirectoriesExist();
        }

        /// <summary>
        /// Ensures cache base directory exists
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to create cache directories: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the local cache directory for a specific game
        /// </summary>
        public string GetGameCacheDirectory(string gameName)
        {
            return Path.Combine(CacheDirectory, SanitizeFileName(gameName));
        }

        /// <summary>
        /// Gets the current cache (from memory or disk)
        /// </summary>
        public async Task<CloudLibraryCache?> GetCacheAsync()
        {
            // Return memory cache if available
            lock (_cacheLock)
            {
                if (_memoryCache != null)
                {
                    return _memoryCache;
                }
            }

            // Load from disk
            try
            {
                if (!File.Exists(MetadataPath))
                {
                    DebugConsole.WriteDebug("No cloud cache file found");
                    return null;
                }

                var json = await File.ReadAllTextAsync(MetadataPath);
                var cache = JsonSerializer.Deserialize<CloudLibraryCache>(json, JsonHelper.DefaultCaseInsensitive);

                if (cache != null)
                {
                    // Update cache entries to verify file existence
                    foreach (var game in cache.Games)
                    {
                        game.CachedIconPath = GetIconPath(game.Name);
                    }


                    // Repair cache if needed (e.g. missing sizes)
                    await RepairCacheData(cache);

                    lock (_cacheLock)
                    {
                        _memoryCache = cache;
                    }

                    DebugConsole.WriteInfo($"[CloudCache] Loaded cache with {cache.Games.Count} game(s), last refresh: {cache.LastRefresh}");
                    return cache;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[CloudCache] Failed to load cache: {ex.Message}");
            }

            return null;
        }

        private async Task RepairCacheData(CloudLibraryCache cache)
        {
            bool cacheUpdated = false;
            foreach (var game in cache.Games)
            {
                // If size is missing but we have files
                if (game.TotalSize == 0)
                {
                    try
                    {
                        string gamePath = Path.Combine(CacheDirectory, game.Name);
                        if (Directory.Exists(gamePath))
                        {
                            // Find any profile json
                            var jsonFiles = Directory.GetFiles(gamePath, ".savetracker_profile_*.json");
                            var checksumFile = jsonFiles.FirstOrDefault(f => f.Contains("default", StringComparison.OrdinalIgnoreCase))
                                               ?? jsonFiles.FirstOrDefault();

                            if (checksumFile != null)
                            {
                                var jsonContent = await File.ReadAllTextAsync(checksumFile);
                                var data = System.Text.Json.JsonSerializer.Deserialize<GameUploadData>(jsonContent, JsonHelper.DefaultCaseInsensitive);
                                if (data?.Files != null)
                                {
                                    game.TotalSize = data.Files.Sum(f => f.Value.FileSize);
                                    cacheUpdated = true;
                                    DebugConsole.WriteDebug($"[CloudCache] Repaired size for {game.Name}: {game.TotalSize} bytes");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteDebug($"[CloudCache] Failed to repair {game.Name}: {ex.Message}");
                    }
                }
            }

            if (cacheUpdated)
            {
                await SaveCacheAsync(cache);
            }
        }

        /// <summary>
        /// Refreshes the cache from cloud storage using Smart Caching.
        /// Uses lsjson to get file timestamps and only re-downloads games deemed "stale".
        /// Returns achievement caching summary, or null if refresh failed/skipped.
        /// </summary>
        public async Task<AchievementCacheSummary?> RefreshCacheAsync(IProgress<string>? progress = null)
        {
            if (_isRefreshing)
            {
                DebugConsole.WriteInfo("[CloudCache] Refresh already in progress, skipping");
                return new AchievementCacheSummary(); // Return empty summary
            }

            _isRefreshing = true;

            try
            {
                progress?.Report("Connecting to cloud storage...");
                DebugConsole.WriteInfo("[CloudCache] Starting cache refresh (Smart Mode)...");

                // Get cloud provider config
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config.CloudConfig.Provider;
                string configPath = RclonePathHelper.GetConfigPath(provider);
                string remoteName = _providerHelper.GetProviderConfigName(provider);
                string remoteBasePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                // Load existing cache to compare timestamps
                var existingCache = await GetCacheAsync() ?? new CloudLibraryCache { Provider = provider.ToString() };

                var newCache = new CloudLibraryCache
                {
                    LastRefresh = DateTime.Now,
                    Provider = provider.ToString()
                };

                // Step 1: Scan all files recursively (max depth 4 to capture GameName/File)
                // We use lsjson to get ModTime for smart comparison
                progress?.Report("Scanning cloud metadata...");

                string listCommand = $"lsjson \"{remoteBasePath}\" --recursive --max-depth 4 --files-only --no-mimetype --config \"{configPath}\" " + RcloneExecutor.GetPerformanceFlags();

                var result = await _rcloneExecutor.ExecuteRcloneCommand(listCommand, TimeSpan.FromSeconds(45));

                if (!result.Success)
                {
                    DebugConsole.WriteWarning($"[CloudCache] Failed to list cloud files: {result.Error}");
                    return null;
                }

                // Group files by Game Name (first component of path)
                var gameGroups = ParseLsJsonAndGroup(result.Output);
                DebugConsole.WriteInfo($"[CloudCache] Found {gameGroups.Count} game(s) in cloud");

                // Step 2: Process each game
                int processed = 0;
                int skipped = 0;
                int synced = 0;

                foreach (var group in gameGroups)
                {
                    string gameName = group.Key;
                    var files = group.Value;
                    processed++;

                    // Determine the latest ModTime for this game from its critical files
                    // We look for files we actually care about: .json, .png, .ini
                    var relevantFiles = files.Where(f =>
                        f.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        f.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        f.Path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                    ).ToList();

                    // If no relevant files, user might have an empty folder or junk, we skip or treat as empty
                    if (!relevantFiles.Any()) continue;

                    DateTime latestCloudMod = relevantFiles.Max(f => f.ModTime);

                    // Check if we have this game cached and if it's up to date
                    bool isStale = true;
                    var existingEntry = existingCache.Games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

                    if (existingEntry != null)
                    {
                        // Tolerance of 2 seconds for float/rounding differences
                        if (Math.Abs((existingEntry.CloudModTime - latestCloudMod).TotalSeconds) < 2)
                        {
                            isStale = false;
                        }
                    }

                    var entry = new CloudGameCacheEntry
                    {
                        Name = gameName,
                        CloudModTime = latestCloudMod
                    };
                    string localGamePath = GetGameCacheDirectory(gameName);

                    if (isStale)
                    {
                        synced++;
                        progress?.Report($"Syncing ({processed}/{gameGroups.Count}): {gameName}");
                        DebugConsole.WriteInfo($"[CloudCache] Syncing {gameName} (New/Modified)");

                        string remoteGamePath = $"{remoteBasePath}/{gameName}";

                        try
                        {
                            Directory.CreateDirectory(localGamePath);

                            // Snapshot Copy
                            var copyCommand = $"copy \"{remoteGamePath}\" \"{localGamePath}\" " +
                                              $"--include \"icon.png\" " +
                                              $"--include \".savetracker_profile_*.json\" " +
                                              $"--include \".savetracker_checksums.json\" " +
                                              $"--include \"Achievements.json\" " +
                                              $"--include \"Achievements.ini\" " +
                                              $"--config \"{configPath}\" " +
                                              $"--ignore-case " +
                                              RcloneExecutor.GetPerformanceFlags();

                            var copyResult = await _rcloneExecutor.ExecuteRcloneCommand(copyCommand, TimeSpan.FromSeconds(20));

                            if (copyResult.Success)
                            {
                                FlattenGameCacheDirectory(localGamePath);
                                await UpdateEntryFromDisk(entry, localGamePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteDebug($"[CloudCache] Failed to sync {gameName}: {ex.Message}");
                        }
                    }
                    else
                    {
                        skipped++;
                        // Use existing cached data, just update the entry object
                        // We assume local files are intact if cache says so
                        entry.PlayTime = existingEntry!.PlayTime;
                        entry.FileCount = existingEntry.FileCount;
                        entry.TotalSize = existingEntry.TotalSize;
                        entry.LastUpdated = existingEntry.LastUpdated;
                        entry.HasIcon = existingEntry.HasIcon;
                        entry.SteamAppId = existingEntry.SteamAppId; // Preserve manual App ID
                        entry.NeedsAppIdInput = existingEntry.NeedsAppIdInput; // Preserve flag

                        // Re-verify icon existence locally just in case user deleted cache files manually
                        entry.CachedIconPath = GetIconPath(gameName);
                        if (entry.HasIcon && entry.CachedIconPath == null) entry.HasIcon = false;
                    }

                    newCache.Games.Add(entry);
                }

                // Step 3: Post-process achievements from local cache
                progress?.Report("Processing achievements...");
                DebugConsole.WriteInfo("[CloudCache] Starting post-cache achievement processing...");

                int achievementsProcessed = 0;
                foreach (var game in newCache.Games)
                {
                    string localGamePath = GetGameCacheDirectory(game.Name);
                    if (Directory.Exists(localGamePath))
                    {
                        try
                        {
                            await ProcessAchievements(game.Name, localGamePath);
                            achievementsProcessed++;
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteDebug($"[CloudCache] Achievement processing failed for {game.Name}: {ex.Message}");
                        }
                    }
                }

                DebugConsole.WriteInfo($"[CloudCache] Processed achievements for {achievementsProcessed} game(s)");

                // Step 4: Save metadata index
                progress?.Report("Saving cache index...");
                await SaveCacheAsync(newCache);

                progress?.Report($"Refresh complete: {synced} synced, {skipped} up-to-date");
                DebugConsole.WriteSuccess($"[CloudCache] Smart Refresh: {synced} synced, {skipped} up-to-date");
                var summary = new AchievementCacheSummary
                {
                    Results = newCache.Games
                        .Where(g => g.NeedsAppIdInput)
                        .Select(g => new AchievementCacheResult { GameName = g.Name, Status = "NeedsInput" })
                        .ToList(),
                    SuccessCount = 0,
                    NeedsInputCount = newCache.Games.Count(g => g.NeedsAppIdInput),
                    FailedCount = 0
                };
                return summary;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "[CloudCache] Cache refresh failed");
                progress?.Report($"Cache refresh failed: {ex.Message}");
                return new AchievementCacheSummary(); // Return empty summary on error
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        /// <summary>
        /// Gets list of games that need manual Steam App ID input
        /// </summary>
        public async Task<List<string>> GetGamesNeedingAppIdAsync()
        {
            var cache = await GetCacheAsync();
            if (cache == null) return new List<string>();

            return cache.Games
                .Where(g => g.NeedsAppIdInput)
                .Select(g => g.Name)
                .ToList();
        }

        public async Task RetryAchievementProcessing(string gameName)
        {
            string localPath = GetGameCacheDirectory(gameName);
            if (Directory.Exists(localPath))
            {
                await ProcessAchievements(gameName, localPath);
            }
        }

        private async Task UpdateEntryFromDisk(CloudGameCacheEntry entry, string localGamePath)
        {
            // Processing metadata from downloaded files
            // Prioritize: 
            // 1. Default Profile (.savetracker_profile_default.json)
            // 2. Legacy Checksums (.savetracker_checksums.json)
            // 3. Any other profile (.savetracker_profile_*.json)
            string? checksumFileToRead = null;
            string defaultProfilePath = Path.Combine(localGamePath, ".savetracker_profile_default.json");
            string legacyChecksumPath = Path.Combine(localGamePath, ".savetracker_checksums.json");

            if (File.Exists(defaultProfilePath))
            {
                checksumFileToRead = defaultProfilePath;
            }
            else if (File.Exists(legacyChecksumPath))
            {
                checksumFileToRead = legacyChecksumPath;
            }
            else
            {
                var jsonFiles = Directory.GetFiles(localGamePath, ".savetracker_profile_*.json");
                checksumFileToRead = jsonFiles.FirstOrDefault();
            }

            if (checksumFileToRead != null)
            {
                try
                {
                    var jsonContent = await File.ReadAllTextAsync(checksumFileToRead);
                    var checksumData = JsonSerializer.Deserialize<GameUploadData>(jsonContent, JsonHelper.DefaultCaseInsensitive);
                    if (checksumData != null)
                    {
                        entry.PlayTime = checksumData.PlayTime;
                        entry.FileCount = checksumData.Files?.Count ?? 0;
                        entry.LastUpdated = checksumData.LastUpdated;
                        entry.TotalSize = checksumData.Files?.Sum(f => f.Value.FileSize) ?? 0;
                    }
                }
                catch { }
            }
            else
            {
                // Ver 1: No metadata files - mark as needing manual App ID input
                entry.NeedsAppIdInput = true;
                DebugConsole.WriteDebug($"[CloudCache] Ver 1 detected for {entry.Name} - needs manual App ID");
            }

            string iconPath = Path.Combine(localGamePath, "icon.png");
            if (File.Exists(iconPath))
            {
                entry.HasIcon = true;
                entry.CachedIconPath = iconPath;
            }
        }

        private class RcloneFileItem
        {
            public string Path { get; set; } = "";
            public DateTime ModTime { get; set; }
        }

        private Dictionary<string, List<RcloneFileItem>> ParseLsJsonAndGroup(string jsonOutput)
        {
            var groups = new Dictionary<string, List<RcloneFileItem>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(jsonOutput)) return groups;

            try
            {
                var items = JsonSerializer.Deserialize<List<RcloneFileItem>>(jsonOutput, JsonHelper.DefaultCaseInsensitive);
                if (items == null) return groups;

                foreach (var item in items)
                {
                    if (string.IsNullOrEmpty(item.Path)) continue;

                    // Path is "GameName/File.ext" or "GameName/SubFolder/File.ext"
                    int slashIdx = item.Path.IndexOf('/');
                    if (slashIdx <= 0) continue; // Skip root files if any (shouldn't be based on remote structure)

                    string gameName = item.Path.Substring(0, slashIdx);

                    if (!groups.ContainsKey(gameName))
                        groups[gameName] = new List<RcloneFileItem>();

                    groups[gameName].Add(item);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[CloudCache] Failed to parse lsjson: {ex.Message}");
            }
            return groups;
        }

        /// <summary>
        /// Saves cache to disk
        /// </summary>
        /// <summary>
        /// Saves cache to disk
        /// </summary>
        public async Task SaveCacheAsync(CloudLibraryCache cache)
        {
            try
            {
                EnsureDirectoriesExist();

                var json = JsonSerializer.Serialize(cache, JsonHelper.DefaultIndented);
                await File.WriteAllTextAsync(MetadataPath, json);

                // Update memory cache
                lock (_cacheLock)
                {
                    _memoryCache = cache;
                }

                DebugConsole.WriteInfo($"[CloudCache] Cache index saved: {cache.Games.Count} games");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "[CloudCache] Failed to save cache index");
            }
        }

        /// <summary>
        /// Gets the local path to a cached icon
        /// </summary>
        public string? GetIconPath(string gameName)
        {
            var iconPath = Path.Combine(GetGameCacheDirectory(gameName), "icon.png");
            return File.Exists(iconPath) ? iconPath : null;
        }

        /// <summary>
        /// Gets metadata for a specific game from cache
        /// </summary>
        public CloudGameCacheEntry? GetGameMetadata(string gameName)
        {
            lock (_cacheLock)
            {
                return _memoryCache?.Games.FirstOrDefault(g =>
                    g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Clears the cache (both memory and disk)
        /// </summary>
        public async Task ClearCacheAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Clear memory
                    lock (_cacheLock)
                    {
                        _memoryCache = null;
                    }

                    // Delete cache directory recursively
                    if (Directory.Exists(CacheDirectory))
                    {
                        Directory.Delete(CacheDirectory, true);
                        Directory.CreateDirectory(CacheDirectory);
                    }

                    DebugConsole.WriteInfo("[CloudCache] Cache cleared");
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"[CloudCache] Failed to clear cache: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Sets a manual Steam App ID for a game (for Ver 1 games or manual override)
        /// </summary>
        public async Task SetManualAppId(string gameName, string appId)
        {
            var cache = await GetCacheAsync();
            if (cache == null) return;

            var entry = cache.Games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.SteamAppId = appId;
                entry.NeedsAppIdInput = false;
                await SaveCacheAsync(cache);
                DebugConsole.WriteInfo($"[CloudCache] Set manual App ID for {gameName}: {appId}");
            }
        }


        /// <summary>
        /// Sanitizes a filename for safe file system usage
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        private async Task ProcessAchievements(string gameName, string localGamePath)
        {
            try
            {
                var achievementFiles = Directory.GetFiles(localGamePath, "Achievements.*", SearchOption.AllDirectories)
                                                .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                                            f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));

                if (!achievementFiles.Any()) return;

                // Priority 1: Check if manual App ID is already set
                var cacheEntry = GetGameMetadata(gameName);
                string? manualAppId = cacheEntry?.SteamAppId;

                if (!string.IsNullOrEmpty(manualAppId))
                {
                    // Use manual App ID
                    Directory.CreateDirectory(AchievementsCacheDirectory);
                    foreach (var file in achievementFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        string targetDir = Path.Combine(AchievementsCacheDirectory, manualAppId);
                        Directory.CreateDirectory(targetDir);
                        string targetPath = Path.Combine(targetDir, fileName);

                        if (File.Exists(targetPath)) File.Delete(targetPath);
                        File.Move(file, targetPath);
                        DebugConsole.WriteInfo($"[CloudCache] Cached Achievement (Manual): {gameName} -> {manualAppId}/{fileName}");
                    }
                    return;
                }

                // Priority 2: Try to extract from metadata
                var jsonFiles = Directory.GetFiles(localGamePath, ".savetracker_profile_*.json");
                string? checksumFile = jsonFiles.FirstOrDefault(f => f.Contains("default", StringComparison.OrdinalIgnoreCase))
                                       ?? jsonFiles.FirstOrDefault()
                                       ?? Directory.GetFiles(localGamePath, ".savetracker_checksums.json").FirstOrDefault();

                GameUploadData? metadata = null;
                if (checksumFile != null)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(checksumFile);
                        metadata = JsonSerializer.Deserialize<GameUploadData>(json, JsonHelper.DefaultCaseInsensitive);
                    }
                    catch { /* Best effort */ }
                }

                Directory.CreateDirectory(AchievementsCacheDirectory);

                foreach (var file in achievementFiles)
                {
                    string fileName = Path.GetFileName(file);
                    string? appId = null;

                    DebugConsole.WriteDebug($"[CloudCache] Processing achievement file: {file}");

                    // Strategy 2A: Try metadata path extraction (works for Ver 2 & 3)
                    if (metadata?.Files != null)
                    {
                        DebugConsole.WriteDebug($"[CloudCache] Searching metadata with {metadata.Files.Count} file entries");

                        // Search for achievement file in metadata
                        var metadataEntry = metadata.Files.FirstOrDefault(kvp =>
                            kvp.Key.Contains("achievement", StringComparison.OrdinalIgnoreCase) ||
                            (kvp.Value?.Path != null && kvp.Value.Path.Contains("achievement", StringComparison.OrdinalIgnoreCase)));

                        if (metadataEntry.Value != null)
                        {
                            // Extract AppID from path like ".../2358720/achievements.json"
                            string path = metadataEntry.Value.Path ?? metadataEntry.Key;
                            DebugConsole.WriteDebug($"[CloudCache] Found metadata entry with path: {path}");

                            var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

                            // Reverse search for the first purely numeric segment
                            appId = segments.Reverse().FirstOrDefault(s => s.All(char.IsDigit) && s.Length > 2);

                            if (appId != null)
                                DebugConsole.WriteDebug($"[CloudCache] Extracted AppID from metadata: {appId}");
                        }
                        else
                        {
                            DebugConsole.WriteDebug("[CloudCache] No achievement entry found in metadata");
                        }

                        // Strategy 2B: Ver 2 Fallback - Search ALL metadata paths for numeric AppID
                        // Ver 2 uses "%GAMEPATH%/achievements.json" which has no AppID, but other files might
                        if (appId == null && metadata?.Files != null)
                        {
                            DebugConsole.WriteDebug("[CloudCache] Trying Ver 2 fallback - scanning all metadata paths");

                            foreach (var kvp in metadata.Files)
                            {
                                string path = kvp.Value?.Path ?? kvp.Key;
                                var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                                appId = segments.Reverse().FirstOrDefault(s => s.All(char.IsDigit) && s.Length > 2);

                                if (appId != null)
                                {
                                    DebugConsole.WriteDebug($"[CloudCache] Found AppID from alternate file: {path} -> {appId}");
                                    break;
                                }
                            }
                        }
                    }

                    // Strategy 2C: Check local directory structure (if rclone preserved it)
                    if (appId == null)
                    {
                        DebugConsole.WriteDebug($"[CloudCache] Trying directory structure extraction from: {file}");
                        var segments = file.Split(Path.DirectorySeparatorChar);
                        appId = segments.Reverse().FirstOrDefault(s => s.All(char.IsDigit) && s.Length > 2);

                        if (appId != null)
                            DebugConsole.WriteDebug($"[CloudCache] Extracted AppID from directory: {appId}");
                    }

                    // Strategy 2D: Check manual mappings (for games with known App IDs)
                    if (appId == null)
                    {
                        DebugConsole.WriteDebug($"[CloudCache] Trying manual mapping for: {gameName}");
                        appId = SteamAppIdResolver.GetFromManualMapping(gameName);
                        if (appId != null)
                            DebugConsole.WriteDebug($"[CloudCache] Found App ID via manual mapping: {appId}");
                    }

                    // Strategy 2E: Query Steam API as last resort
                    if (appId == null)
                    {
                        DebugConsole.WriteDebug($"[CloudCache] Querying Steam API for: {gameName}");
                        appId = await SteamAppIdResolver.ResolveAppIdAsync(gameName);
                        if (appId != null)
                            DebugConsole.WriteDebug($"[CloudCache] Resolved App ID via Steam API: {appId}");
                    }

                    if (appId != null)
                    {
                        string targetDir = Path.Combine(AchievementsCacheDirectory, appId);
                        Directory.CreateDirectory(targetDir);
                        string targetPath = Path.Combine(targetDir, fileName);

                        if (File.Exists(targetPath)) File.Delete(targetPath);
                        File.Move(file, targetPath);
                        DebugConsole.WriteInfo($"[CloudCache] Cached Achievement: {gameName} -> {appId}/{fileName}");
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"[CloudCache] Could not determine AppID for {fileName} in {gameName} - manual input required");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[CloudCache] Achievement processing failed for {gameName}: {ex.Message}");
            }
        }


        /// <summary>
        /// Moves all files from subdirectories to the root path and deletes subdirectories.
        /// </summary>
        private void FlattenGameCacheDirectory(string rootPath)
        {
            try
            {
                var files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);

                    // SKIP Achievement files (they should have been processed, or are orphans)
                    if (fileName.Equals("Achievements.json", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("Achievements.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Skip files already at root
                    if (Path.GetDirectoryName(file)?.TrimEnd(Path.DirectorySeparatorChar).Equals(rootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // DebugConsole.WriteDebug($"[CloudCache] File already at root: {Path.GetFileName(file)}");
                        continue;
                    }

                    string destPath = Path.Combine(rootPath, fileName);

                    // Overwrite if exists (taking the one from subfolder)
                    if (File.Exists(destPath))
                    {
                        DebugConsole.WriteDebug($"[CloudCache] Overwriting root file with subfolder version: {fileName}");
                        File.Delete(destPath);
                    }

                    DebugConsole.WriteDebug($"[CloudCache] Flattening: {fileName} -> Root");
                    File.Move(file, destPath);
                }

                // Delete all subdirectories
                var subDirs = Directory.GetDirectories(rootPath);
                foreach (var dir in subDirs)
                {
                    // Don't delete directories if they still have files (e.g. Achievements that we skipped)
                    if (Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                    {
                        Directory.Delete(dir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"[CloudCache] Failed to flatten directory {rootPath}: {ex.Message}");
            }
        }
    }
}

