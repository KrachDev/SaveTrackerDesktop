using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.AutoUpdater;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Threading.Tasks;

namespace SaveTracker.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private Config? _currentConfig;

        [ObservableProperty]
        private bool _enableAutomaticTracking;

        [ObservableProperty]
        private bool _startWithWindows;

        [ObservableProperty]
        private bool _startMinimized;

        [ObservableProperty]
        private bool _showDebugConsole;

        // Auto-updater properties
        [ObservableProperty]
        private bool _checkForUpdatesOnStartup;

        [ObservableProperty]
        private string _currentVersion = "Unknown";

        [ObservableProperty]
        private bool _updateAvailable;

        [ObservableProperty]
        private string _updateVersion = string.Empty;

        [ObservableProperty]
        private bool _isCheckingForUpdates;

        private UpdateInfo? _latestUpdateInfo;

        public SettingsViewModel()
        {
            LoadSettings();

            // Get current version
            var updateChecker = new UpdateChecker();
            CurrentVersion = updateChecker.GetCurrentVersion();
        }

        private async void LoadSettings()
        {
            _currentConfig = await ConfigManagement.LoadConfigAsync();
            if (_currentConfig != null)
            {
                EnableAutomaticTracking = _currentConfig.EnableAutomaticTracking;
                StartMinimized = _currentConfig.StartMinimized;
                ShowDebugConsole = _currentConfig.ShowDebugConsole;
                CheckForUpdatesOnStartup = _currentConfig.CheckForUpdatesOnStartup;
            }
            StartWithWindows = StartupManager.IsStartupEnabled();
        }

        [RelayCommand]
        private async Task SaveSettings()
        {
            if (_currentConfig == null)
            {
                _currentConfig = new Config();
            }

            _currentConfig.EnableAutomaticTracking = EnableAutomaticTracking;
            _currentConfig.StartMinimized = StartMinimized;
            _currentConfig.ShowDebugConsole = ShowDebugConsole;
            _currentConfig.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;

            await ConfigManagement.SaveConfigAsync(_currentConfig);
            StartupManager.SetStartup(StartWithWindows);

            // Notify main window about console visibility change if needed
            if (ShowDebugConsole)
            {
                DebugConsole.ShowConsole();
            }
            else
            {
                DebugConsole.HideConsole();
            }
        }

        [RelayCommand]
        private async Task CheckForUpdatesAsync()
        {
            if (IsCheckingForUpdates) return;

            try
            {
                IsCheckingForUpdates = true;
                UpdateAvailable = false;
                UpdateVersion = "Checking...";

                var updateChecker = new UpdateChecker();
                _latestUpdateInfo = await updateChecker.CheckForUpdatesAsync();

                if (_latestUpdateInfo.IsUpdateAvailable)
                {
                    UpdateAvailable = true;
                    UpdateVersion = $"v{_latestUpdateInfo.Version} available";
                    DebugConsole.WriteSuccess($"Update available: v{_latestUpdateInfo.Version}");
                }
                else
                {
                    UpdateAvailable = false;
                    UpdateVersion = "Up to date";
                    DebugConsole.WriteInfo("No updates available");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check for updates");
                UpdateVersion = "Check failed";
                UpdateAvailable = false;
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        [RelayCommand]
        private async Task InstallUpdateAsync()
        {
            if (_latestUpdateInfo == null || !_latestUpdateInfo.IsUpdateAvailable)
            {
                DebugConsole.WriteWarning("No update available to install");
                return;
            }

            try
            {
                DebugConsole.WriteSection($"Installing Update v{_latestUpdateInfo.Version}");

                // Download the update
                var downloader = new UpdateDownloader();
                downloader.DownloadProgressChanged += (sender, progress) =>
                {
                    UpdateVersion = $"Downloading... {progress}%";
                };

                string downloadedFilePath = await downloader.DownloadUpdateAsync(_latestUpdateInfo.DownloadUrl);

                // Install the update (this will exit the application)
                var installer = new UpdateInstaller();
                await installer.InstallUpdateAsync(downloadedFilePath);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to install update");
                UpdateVersion = "Install failed";
            }
        }
    }
}
