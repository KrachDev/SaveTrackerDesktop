using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Resources.HELPERS;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using static CloudConfig;

namespace SaveTracker.ViewModels
{
    /// <summary>
    /// Partial class containing game settings tab functionality
    /// </summary>
    public partial class MainWindowViewModel
    {
        // ========== GAME SETTINGS TAB PROPERTIES ==========

        [ObservableProperty]
        private bool _gameCanTrack = true;

        [ObservableProperty]
        private bool _gameCanUploads = true;

        [ObservableProperty]
        private CloudProvider _gameProvider = CloudProvider.Global;

        [ObservableProperty]
        private bool _gameAllowWatcher = true;

        [ObservableProperty]
        private ObservableCollection<CloudProviderItem> _availableProviders = new();

        /// <summary>
        /// Helper property for ComboBox binding - syncs with GameProvider
        /// </summary>
        public CloudProviderItem? SelectedProviderItem
        {
            get => AvailableProviders.FirstOrDefault(p => p.Provider == GameProvider);
            set
            {
                if (value != null && value.Provider != GameProvider)
                {
                    GameProvider = value.Provider;
                    OnPropertyChanged(nameof(SelectedProviderItem));
                    UpdateEffectiveProviderText();
                }
            }
        }

        [ObservableProperty]
        private string _effectiveProviderText = string.Empty;

        private GameUploadData? _currentGameUploadData;

        // ========== INITIALIZATION ==========

        /// <summary>
        /// Initialize available cloud providers for the settings ComboBox
        /// </summary>
        private void InitializeGameSettingsProviders()
        {
            AvailableProviders.Clear();

            // Add "Global" option first
            AvailableProviders.Add(new CloudProviderItem(CloudProvider.Global, "Use Global Setting"));

            // Add all supported providers using CloudProviderHelper
            var supportedProviders = _providerHelper.GetSupportedProviders();
            foreach (var provider in supportedProviders)
            {
                var displayName = _providerHelper.GetProviderDisplayName(provider);
                AvailableProviders.Add(new CloudProviderItem(provider, displayName));
            }
        }

        // ========== LOAD GAME SETTINGS ==========

        /// <summary>
        /// Load game-specific settings when a game is selected
        /// </summary>
        private async Task LoadGameSettingsAsync(Game game)
        {
            try
            {
                _currentGameUploadData = await ConfigManagement.GetGameData(game);

                if (_currentGameUploadData != null)
                {
                    // Update UI properties from loaded data
                    GameCanTrack = _currentGameUploadData.CanTrack;
                    GameCanUploads = _currentGameUploadData.CanUploads;
                    GameProvider = _currentGameUploadData.GameProvider;
                    GameAllowWatcher = _currentGameUploadData.AllowGameWatcher;

                    UpdateEffectiveProviderText();

                    DebugConsole.WriteInfo($"Loaded settings for {game.Name}: Track={GameCanTrack}, Upload={GameCanUploads}, Provider={GameProvider}");
                }
                else
                {
                    // No data exists yet, use defaults
                    GameCanTrack = true;
                    GameCanUploads = true;
                    GameProvider = CloudProvider.Global;
                    GameAllowWatcher = true;

                    UpdateEffectiveProviderText();

                    DebugConsole.WriteInfo($"No settings found for {game.Name}, using defaults");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load game settings");

                // Use defaults on error
                GameCanTrack = true;
                GameCanUploads = true;
                GameProvider = CloudProvider.Global;
                GameAllowWatcher = true;
            }
        }

        // ========== SAVE GAME SETTINGS COMMAND ==========

        [RelayCommand]
        private async Task SaveGameSettingsAsync()
        {
            if (SelectedGame?.Game == null)
            {
                DebugConsole.WriteWarning("No game selected, cannot save settings");
                return;
            }

            try
            {
                var game = SelectedGame.Game;

                // Load current data or create new
                var data = await ConfigManagement.GetGameData(game);
                if (data == null)
                {
                    data = new GameUploadData();
                }

                // Update with current UI values
                data.CanTrack = GameCanTrack;
                data.CanUploads = GameCanUploads;
                data.GameProvider = GameProvider;
                data.AllowGameWatcher = GameAllowWatcher;
                data.LastUpdated = DateTime.UtcNow;

                // Save to file
                await _rcloneFileOperations.SaveChecksumData(data, game);

                // Update cached data
                _currentGameUploadData = data;

                DebugConsole.WriteSuccess($"Game settings saved for {game.Name}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to save game settings");
            }
        }

        [RelayCommand]
        private void ResetGameSettingsToDefaults()
        {
            GameCanTrack = true;
            GameCanUploads = true;
            GameProvider = CloudProvider.Global;
            GameAllowWatcher = true;

            DebugConsole.WriteInfo("Game settings reset to defaults");
        }

        // ========== HELPER METHODS ==========

        /// <summary>
        /// Check if tracking is enabled for the current game
        /// </summary>
        private bool IsTrackingEnabledForGame()
        {
            return _currentGameUploadData?.CanTrack ?? true;
        }

        /// <summary>
        /// Check if uploads are enabled for the current game
        /// </summary>
        private bool AreUploadsEnabledForGame()
        {
            return _currentGameUploadData?.CanUploads ?? true;
        }

        /// <summary>
        /// Get the cloud provider for the current game (respects Global setting)
        /// </summary>
        private CloudProvider GetEffectiveProviderForGame()
        {
            if (_currentGameUploadData?.GameProvider == CloudProvider.Global)
            {
                // Use global provider
                return _selectedProvider;
            }

            return _currentGameUploadData?.GameProvider ?? _selectedProvider;
        }

        /// <summary>
        /// Check if game watcher should detect this game
        /// </summary>
        private bool IsGameWatcherAllowedForGame(Game game)
        {
            // This would need to load the game's settings
            // For now, return true as default
            return _currentGameUploadData?.AllowGameWatcher ?? true;
        }

        /// <summary>
        /// Updates the text showing which provider is actually being used
        /// </summary>
        public void UpdateEffectiveProviderText()
        {
            var effective = GetEffectiveProviderForGame();
            var providerName = _providerHelper.GetProviderDisplayName(effective);

            if (GameProvider == CloudProvider.Global)
            {
                EffectiveProviderText = $"Using Global ({providerName})";
            }
            else
            {
                EffectiveProviderText = $"Using {providerName}";
            }

            // Also update the main window status text if this is the selected game
            if (SelectedGame?.Game != null)
            {
                if (effective != _selectedProvider)
                {
                    CloudStorageText = $"{_providerHelper.GetProviderDisplayName(_selectedProvider)} (Game: {providerName})";
                }
                else
                {
                    CloudStorageText = _providerHelper.GetProviderDisplayName(_selectedProvider);
                }
            }
        }
    }
}
