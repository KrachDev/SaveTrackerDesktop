using CommunityToolkit.Mvvm.ComponentModel;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker.Views.Dialog
{
    public partial class UC_AddGame_ViewModel : ObservableObject
    {
        [ObservableProperty]
        private Game _newGame = new Game();

        // Cloud Verification Properties
        [ObservableProperty]
        private string _cloudCheckStatus = "";

        [ObservableProperty]
        private string _cloudCheckColor = "#858585"; // Default Gray

        [ObservableProperty]
        private ObservableCollection<string> _availableCloudGames = new();

        private CancellationTokenSource? _cloudCheckCts;
        private readonly RcloneExecutor _rcloneExecutorInternal = new RcloneExecutor();
        private readonly CloudProviderHelper _providerHelper = new CloudProviderHelper();

        public UC_AddGame_ViewModel()
        {
            // Initialize suggestions in background
            _ = LoadCloudGameSuggestionsAsync();
        }

        // Hook into name changes to trigger cloud check
        partial void OnNewGameChanged(Game value)
        {
            // If the whole game object changes, we might need to verify the name if it's set
            if (!string.IsNullOrEmpty(value.Name))
            {
                TriggerGameNameCheck(value.Name);
            }
        }

        // We can't easily hook into properties of the nested NewGame object via ObservableProperty
        // So we will modify the UI to bind to a proxy property or handle the text changed event.
        // For simplicity in this refactor, I'll add a method that the View calls, or better, 
        // expose the Name as a direct property here that syncs with NewGame.Name.

        [ObservableProperty]
        private string _gameName = "";

        [ObservableProperty]
        private string _executablePath = "";

        [ObservableProperty]
        private string _installDirectory = "";

        partial void OnGameNameChanged(string value)
        {
            NewGame.Name = value;
            TriggerGameNameCheck(value);
        }

        partial void OnExecutablePathChanged(string value)
        {
            NewGame.ExecutablePath = value;
        }

        partial void OnInstallDirectoryChanged(string value)
        {
            NewGame.InstallDirectory = value;
        }

        // Method to update generic properties from external (e.g. file picker)
        public void UpdateGameInfo(string path, string dir, string name)
        {
            ExecutablePath = path;
            InstallDirectory = dir;
            GameName = name; // This triggers verification
        }

        private void TriggerGameNameCheck(string name)
        {
            _cloudCheckCts?.Cancel();
            _cloudCheckCts = new CancellationTokenSource();
            var token = _cloudCheckCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800, token); // Increased debounce to 800ms for faster typing
                    if (token.IsCancellationRequested) return;
                    await CheckCloudGameExistence(name);
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        private Task CheckCloudGameExistence(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                UpdateStatus("", "#858585");
                return Task.CompletedTask;
            }

            try
            {
                // Instant check against local cache
                string sanitizedName = SanitizeGameNameForCheck(gameName);

                // Case-insensitive check
                bool exists = AvailableCloudGames.Any(g => g.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                                                        || g.Equals(sanitizedName, StringComparison.OrdinalIgnoreCase));

                if (exists)
                    UpdateStatus("✓ Found in Cloud (Will Sync)", "#4CC9B0"); // Green-ish
                else
                    UpdateStatus("New to Cloud", "#858585"); // Gray
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Cloud check error");
                UpdateStatus("Error", "#C42B1C");
            }
            return Task.CompletedTask;
        }


        private void UpdateStatus(string text, string color)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CloudCheckStatus = text;
                CloudCheckColor = color;
            });
        }

        private async Task LoadCloudGameSuggestionsAsync()
        {
            try
            {
                // Load from local cache first (FAST)
                var cachedGames = await ConfigManagement.LoadCloudGamesAsync();

                if (cachedGames != null && cachedGames.Count > 0)
                {
                    // Cache exists and has data
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        AvailableCloudGames.Clear();
                        foreach (var g in cachedGames) AvailableCloudGames.Add(g);
                        DebugConsole.WriteSuccess($"Loaded {cachedGames.Count} cached cloud games");
                    });
                }
                else
                {
                    // Cache is empty or doesn't exist - fetch from cloud
                    DebugConsole.WriteInfo("Cloud games cache empty - fetching from provider...");
                    await FetchCloudGamesFromProvider();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load cloud suggestions from cache");
                // Fall back to fetching from provider
                _ = FetchCloudGamesFromProvider();
            }
        }

        private async Task FetchCloudGamesFromProvider()
        {
            try
            {
                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig == null)
                {
                    DebugConsole.WriteWarning("Cloud config not available");
                    return;
                }

                var provider = config.CloudConfig.Provider;
                string configPath = RclonePathHelper.GetConfigPath(provider);
                string remoteName = _providerHelper.GetProviderConfigName(provider);
                string remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                DebugConsole.WriteInfo($"Fetching cloud games from: {remotePath}");

                var result = await _rcloneExecutorInternal.ExecuteRcloneCommand(
                    $"lsd \"{remotePath}\" --config \"{configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(15),
                    allowedExitCodes: new[] { 3 }
                );

                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var games = new System.Collections.Generic.List<string>();

                    foreach (var line in lines)
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            // lsd output format: [size] [date] [time] [count] [name...]
                            string gameName = string.Join(" ", parts.Skip(4));
                            games.Add(gameName);
                        }
                    }

                    // Update UI and cache
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        AvailableCloudGames.Clear();
                        foreach (var g in games) AvailableCloudGames.Add(g);
                        
                        // Save to cache for next time
                        await ConfigManagement.SaveCloudGamesAsync(games);
                        
                        DebugConsole.WriteSuccess($"Loaded {games.Count} cloud games from provider");
                    });
                }
                else
                {
                    DebugConsole.WriteWarning("Failed to fetch cloud games from provider");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to fetch cloud games from provider");
            }
        }

        private static string SanitizeGameNameForCheck(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return "UnknownGame";
            var invalidChars = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            return invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_')).Trim();
        }
    }
}
