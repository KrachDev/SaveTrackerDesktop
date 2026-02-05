using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace SaveTracker.ViewModels
{
    public partial class CloudLibraryViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<LibraryGameItem> _libraryGames = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalPlayTimeFormatted))]
        private TimeSpan _totalPlayTime = TimeSpan.Zero;

        [ObservableProperty]
        private string _cloudStorageQuota = "Checking...";

        public string TotalPlayTimeFormatted => TotalPlayTime.TotalHours >= 1
            ? $"{(int)TotalPlayTime.TotalHours}h {TotalPlayTime.Minutes}m"
            : $"{TotalPlayTime.Minutes}m";

        private readonly RcloneExecutor _rcloneExecutor = new RcloneExecutor();
        private readonly CloudProviderHelper _providerHelper = new CloudProviderHelper();
        private readonly CloudLibraryCacheService _cacheService = CloudLibraryCacheService.Instance;

        public CloudLibraryViewModel()
        {
            // Auto-load on creation
            _ = LoadGamesAsync();
        }

        [RelayCommand]
        private async Task LoadGamesAsync()
        {
            if (IsLoading) return;

            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = true;
                    StatusMessage = "Loading games...";
                    LibraryGames.Clear();
                });

                await Task.Run(async () =>
                {
                    try
                    {
                        // Dictionary to hold final merged items
                        var mergedGames = new Dictionary<string, LibraryGameItem>(StringComparer.OrdinalIgnoreCase);

                        // STEP 1: Try to load from cache first for instant display of cloud games
                        var cache = await _cacheService.GetCacheAsync();
                        if (cache?.Games?.Any() == true)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                StatusMessage = $"Loading {cache.Games.Count} cached games...");

                            await LoadFromCacheAsync(cache, mergedGames);

                            DebugConsole.WriteInfo($"[CloudLibrary] Loaded {cache.Games.Count} games from cache");
                        }

                        // STEP 2: Load local games
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            StatusMessage = "Scanning local games...");

                        var localGames = await ConfigManagement.LoadAllGamesAsync();

                        // Heavy I/O work (icon extraction) in background
                        foreach (var game in localGames)
                        {
                            byte[]? iconData = Misc.ExtractIconDataFromExe(game.ExecutablePath);

                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                Avalonia.Media.Imaging.Bitmap? icon = null;
                                if (iconData != null)
                                {
                                    try { using (var ms = new MemoryStream(iconData)) icon = new Avalonia.Media.Imaging.Bitmap(ms); } catch { }
                                }

                                if (mergedGames.TryGetValue(game.Name, out var existing))
                                {
                                    // Update existing cloud-only entry with local info
                                    existing.IsInstalled = true;
                                    existing.LocalPath = game.InstallDirectory;
                                    if (icon != null) existing.Icon = icon;
                                }
                                else
                                {
                                    // Add new local-only game
                                    mergedGames[game.Name] = new LibraryGameItem
                                    {
                                        Name = game.Name,
                                        IsInstalled = true,
                                        LocalPath = game.InstallDirectory,
                                        Icon = icon
                                    };
                                }
                            });
                        }

                        // STEP 3: Check cloud for any games not in cache (fresh scan)
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            StatusMessage = "Checking cloud...");

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
                                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => existingItem.IsInCloud = true);
                                    }
                                    else
                                    {
                                        // New cloud-only item not in cache
                                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                        {
                                            mergedGames[name] = new LibraryGameItem
                                            {
                                                Name = name,
                                                IsInstalled = false,
                                                IsInCloud = true
                                            };
                                        });
                                    }
                                }
                            }
                        }

                        // Get Quota
                        try
                        {
                            var quota = await _rcloneExecutor.GetCloudQuotaAsync(provider);
                            if (quota != null)
                            {
                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    CloudStorageQuota = $"Storage: {Misc.FormatFileSize(quota.Value.Used)} / {Misc.FormatFileSize(quota.Value.Total)}";
                                });
                            }
                        }
                        catch { /* Ignore quota check errors */ }

                        // STEP 4: Populate Collection on UI Thread
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            LibraryGames.Clear();
                            foreach (var item in mergedGames.Values.OrderBy(g => g.Name))
                            {
                                LibraryGames.Add(item);
                            }

                            // Calculate total playtime
                            TotalPlayTime = LibraryGames.Aggregate(TimeSpan.Zero, (sum, game) => sum + game.PlayTime);

                            StatusMessage = $"Loaded {LibraryGames.Count} games ({LibraryGames.Count(g => g.IsInCloud)} in cloud)";
                        });

                        // Legacy icon scanning removed to prevent rate limits.
                        // Icons are now handled by the centralized CloudLibraryCacheService.
                    }
                    catch (Exception ex)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusMessage = "Failed to load games";
                            DebugConsole.WriteException(ex, "CloudLibrary Load Error");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = "Failed to start load";
                    DebugConsole.WriteException(ex, "CloudLibrary Load Start Error");
                });
            }
            finally
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Load games from cache into the merged dictionary
        /// </summary>
        private async Task LoadFromCacheAsync(CloudLibraryCache cache, Dictionary<string, LibraryGameItem> mergedGames)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var entry in cache.Games)
                {
                    // Load icon from cache if available
                    Avalonia.Media.Imaging.Bitmap? icon = null;
                    var iconPath = entry.CachedIconPath ?? _cacheService.GetIconPath(entry.Name);

                    if (iconPath != null && File.Exists(iconPath))
                    {
                        try
                        {
                            using var fs = File.OpenRead(iconPath);
                            icon = new Avalonia.Media.Imaging.Bitmap(fs);
                        }
                        catch { /* Ignore icon load error */ }
                    }

                    mergedGames[entry.Name] = new LibraryGameItem
                    {
                        Name = entry.Name,
                        IsInCloud = true,
                        IsInstalled = false, // Will be updated when we scan local games
                        PlayTime = entry.PlayTime,
                        TotalSize = entry.TotalSize,
                        FileCount = entry.FileCount,
                        Icon = icon
                    };
                }

                // Initial total playtime from cache
                TotalPlayTime = mergedGames.Values.Aggregate(TimeSpan.Zero, (sum, game) => sum + game.PlayTime);
            });
        }

    }

    public partial class LibraryGameItem : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private bool _isInstalled;

        [ObservableProperty]
        private bool _isInCloud;

        [ObservableProperty]
        private string? _localPath;

        [ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _icon;

        [ObservableProperty]
        private TimeSpan _playTime = TimeSpan.Zero;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalSizeFormatted))]
        private long _totalSize = 0;

        public string TotalSizeFormatted => Misc.FormatFileSize(TotalSize);

        [ObservableProperty]
        private int _fileCount = 0;

        public string StatusText => (IsInstalled, IsInCloud) switch
        {
            (true, true) => "Installed & Synced",
            (true, false) => "Installed Only",
            (false, true) => "Cloud Only",
            _ => "Unknown"
        };

        public string StatusColor => (IsInstalled, IsInCloud) switch
        {
            (true, true) => "#4CC9B0", // Green
            (true, false) => "#CCCCCC", // Gray
            (false, true) => "#007ACC", // Blue
            _ => "Red"
        };

        /// <summary>
        /// Formatted playtime string (e.g., "2h 30m" or "45m")
        /// </summary>
        public string PlayTimeFormatted => PlayTime.TotalHours >= 1
            ? $"{(int)PlayTime.TotalHours}h {PlayTime.Minutes}m"
            : PlayTime.TotalMinutes >= 1
                ? $"{(int)PlayTime.TotalMinutes}m"
                : PlayTime == TimeSpan.Zero ? "" : "< 1m";
    }
}
