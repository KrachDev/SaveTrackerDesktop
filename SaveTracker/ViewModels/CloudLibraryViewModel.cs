using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Resources.LOGIC;
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

        private readonly CloudLibraryService _cloudLibraryService = new CloudLibraryService();
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

                // Get data from Service (merges Cache, Local, and Cloud)
                var items = await _cloudLibraryService.GetCloudLibraryAsync();

                // Quota check
                await Task.Run(async () =>
                {
                    try
                    {
                        var config = await ConfigManagement.LoadConfigAsync();
                        var provider = config.CloudConfig.Provider;
                        var quota = await new RcloneExecutor().GetCloudQuotaAsync(provider);
                        if (quota != null)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                CloudStorageQuota = $"Storage: {Misc.FormatFileSize(quota.Value.Used)} / {Misc.FormatFileSize(quota.Value.Total)}";
                            });
                        }
                    }
                    catch { }
                });

                foreach (var item in items)
                {
                    var guiItem = new LibraryGameItem
                    {
                        Name = item.Name,
                        IsInstalled = item.IsInstalled,
                        IsInCloud = item.IsInCloud,
                        LocalPath = item.LocalPath,
                        PlayTime = item.PlayTime,
                        TotalSize = item.TotalSize,
                        FileCount = item.FileCount
                    };

                    // Load icon
                    if (item.IsInstalled && !string.IsNullOrEmpty(item.LocalPath))
                    {
                        // Try to find the game to get EXE path for icon extraction
                        var game = (await ConfigManagement.LoadAllGamesAsync()).FirstOrDefault(g => g.Name == item.Name);
                        if (game != null)
                        {
                            byte[]? iconData = Misc.ExtractIconDataFromExe(game.ExecutablePath);
                            if (iconData != null)
                            {
                                try { using (var ms = new MemoryStream(iconData)) guiItem.Icon = new Avalonia.Media.Imaging.Bitmap(ms); } catch { }
                            }
                        }
                    }

                    if (guiItem.Icon == null && item.IsInCloud)
                    {
                        // Try loading from cloud cache
                        var iconPath = _cacheService.GetIconPath(item.Name);
                        if (iconPath != null && File.Exists(iconPath))
                        {
                            try { using var fs = File.OpenRead(iconPath); guiItem.Icon = new Avalonia.Media.Imaging.Bitmap(fs); } catch { }
                        }
                    }

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => LibraryGames.Add(guiItem));
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TotalPlayTime = LibraryGames.Aggregate(TimeSpan.Zero, (sum, game) => sum + game.PlayTime);
                    StatusMessage = $"Loaded {LibraryGames.Count} games ({LibraryGames.Count(g => g.IsInCloud)} in cloud)";
                });

            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = "Failed to load games";
                    DebugConsole.WriteException(ex, "CloudLibrary Load Error");
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
