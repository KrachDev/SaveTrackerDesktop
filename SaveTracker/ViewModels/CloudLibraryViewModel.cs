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
                IsLoading = true;
                StatusMessage = "Loading games...";
                LibraryGames.Clear();

                // 1. Get Local Games
                var localGames = await ConfigManagement.LoadAllGamesAsync();
                var mergedGames = new Dictionary<string, LibraryGameItem>(StringComparer.OrdinalIgnoreCase);

                foreach (var game in localGames)
                {
                    mergedGames[game.Name] = new LibraryGameItem
                    {
                        Name = game.Name,
                        IsInstalled = true,
                        LocalPath = game.InstallDirectory,
                        Icon = Misc.ExtractIconFromExe(game.ExecutablePath)
                        // Cloud status unknown yet
                    };
                }

                // 2. Get Cloud Games
                StatusMessage = "Fetching cloud list...";
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
                                mergedGames[name] = new LibraryGameItem
                                {
                                    Name = name,
                                    IsInstalled = false,
                                    IsInCloud = true
                                };
                            }
                        }
                    }
                }

                // 3. Populate Collection
                foreach (var item in mergedGames.Values.OrderBy(g => g.Name))
                {
                    LibraryGames.Add(item);
                }

                StatusMessage = $"Loaded {LibraryGames.Count} games ({LibraryGames.Count(g => g.IsInCloud)} in cloud)";
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to load games";
                DebugConsole.WriteException(ex, "CloudLibrary Load Error");
            }
            finally
            {
                IsLoading = false;
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
