using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SaveTracker.Resources.LOGIC
{
    public class CloudLibraryService
    {
        private readonly RcloneExecutor _rcloneExecutor = new RcloneExecutor();
        private readonly CloudProviderHelper _providerHelper = new CloudProviderHelper();
        private readonly CloudLibraryCacheService _cacheService = CloudLibraryCacheService.Instance;

        public class CloudLibraryItem
        {
            public string Name { get; set; } = string.Empty;
            public bool IsInstalled { get; set; }
            public bool IsInCloud { get; set; }
            public string? LocalPath { get; set; }
            public TimeSpan PlayTime { get; set; }
            public long TotalSize { get; set; }
            public int FileCount { get; set; }
        }

        public async Task<List<CloudLibraryItem>> GetCloudLibraryAsync()
        {
            var mergedGames = new Dictionary<string, CloudLibraryItem>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. Load from Cache
                var cache = await _cacheService.GetCacheAsync();
                if (cache?.Games?.Any() == true)
                {
                    foreach (var entry in cache.Games)
                    {
                        mergedGames[entry.Name] = new CloudLibraryItem
                        {
                            Name = entry.Name,
                            IsInCloud = true,
                            IsInstalled = false,
                            PlayTime = entry.PlayTime,
                            TotalSize = entry.TotalSize,
                            FileCount = entry.FileCount
                        };
                    }
                }

                // 2. Load Local Games
                var localGames = await ConfigManagement.LoadAllGamesAsync();
                foreach (var game in localGames)
                {
                    if (mergedGames.TryGetValue(game.Name, out var existing))
                    {
                        existing.IsInstalled = true;
                        existing.LocalPath = game.InstallDirectory;
                    }
                    else
                    {
                        mergedGames[game.Name] = new CloudLibraryItem
                        {
                            Name = game.Name,
                            IsInstalled = true,
                            LocalPath = game.InstallDirectory
                        };
                    }
                }

                // 3. Scan Cloud (if needed, but usually we rely on cache + periodic syncs, but user expects fresh list)
                // For headless, we might want to be aggressive or lazy. Let's do a live scan if cache is cold or requested.
                // Replicating ViewModel logic:
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config.CloudConfig.Provider;
                string configPath = RclonePathHelper.GetConfigPath(provider);
                string remoteName = _providerHelper.GetProviderConfigName(provider);
                string remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                       $"lsd \"{remotePath}\" --config \"{configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                       TimeSpan.FromSeconds(15)
                   );

                if (result.Success)
                {
                    var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            string name = string.Join(" ", parts.Skip(4));
                            if (mergedGames.TryGetValue(name, out var existingItem))
                            {
                                existingItem.IsInCloud = true;
                            }
                            else
                            {
                                mergedGames[name] = new CloudLibraryItem
                                {
                                    Name = name,
                                    IsInCloud = true,
                                    IsInstalled = false
                                };
                            }
                        }
                    }
                    // Update cache implicitly?
                    if (cache == null)
                    {
                        cache = new CloudLibraryCache { Provider = provider.ToString() };
                    }

                    cache.Games = mergedGames.Values.Where(g => g.IsInCloud).Select(g => new CloudGameCacheEntry
                    {
                        Name = g.Name,
                        PlayTime = g.PlayTime,
                        TotalSize = g.TotalSize,
                        FileCount = g.FileCount
                    }).ToList();

                    await _cacheService.SaveCacheAsync(cache);
                }

                return mergedGames.Values.OrderBy(g => g.Name).ToList();

            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"[CloudLibraryService] Failed to get library: {ex.Message}");
                // Return whatever we have
                return mergedGames.Values.ToList();
            }
        }
    }
}
