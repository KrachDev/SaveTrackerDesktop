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

        private readonly RcloneExecutor _rcloneExecutor = new RcloneExecutor();
        private readonly CloudProviderHelper _providerHelper = new CloudProviderHelper();

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
                        // 1. Get Local Games
                        // Only load raw game data here, no icons yet
                        var localGames = await ConfigManagement.LoadAllGamesAsync();
                        var tempGameData = new List<(Game game, byte[]? iconData)>();

                        // Heavy I/O work (icon extraction) in background
                        foreach (var game in localGames)
                        {
                            byte[]? iconData = Misc.ExtractIconDataFromExe(game.ExecutablePath);
                            tempGameData.Add((game, iconData));
                        }

                        // Dictionary to hold final items
                        var mergedGames = new Dictionary<string, LibraryGameItem>(StringComparer.OrdinalIgnoreCase);

                        // Create ViewModels on UI thread (safe for Bitmap/ObservableObject)
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var (game, iconData) in tempGameData)
                            {
                                Avalonia.Media.Imaging.Bitmap? icon = null;
                                if (iconData != null)
                                {
                                    try { using (var ms = new System.IO.MemoryStream(iconData)) icon = new Avalonia.Media.Imaging.Bitmap(ms); } catch { }
                                }

                                mergedGames[game.Name] = new LibraryGameItem
                                {
                                    Name = game.Name,
                                    IsInstalled = true,
                                    LocalPath = game.InstallDirectory,
                                    Icon = icon
                                    // Cloud status unknown yet
                                };
                            }
                        });

                        // 2. Get Cloud Games
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusMessage = "Fetching cloud list...");

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
                                        // New cloud-only item
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

                        // 3. Populate Collection on UI Thread
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var item in mergedGames.Values.OrderBy(g => g.Name))
                            {
                                LibraryGames.Add(item);
                            }
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
    }
}
