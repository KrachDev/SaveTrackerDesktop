using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SaveTracker.ViewModels
{
    public partial class AnnouncementViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _announcementContent = string.Empty;

        [ObservableProperty]
        private bool _neverShowAgain;

        private Config? _currentConfig;
        private string _currentVersion = string.Empty;

        public AnnouncementViewModel()
        {
            LoadAnnouncementContent();
            LoadSettings();

            // Get current version
            var updateChecker = new Resources.Logic.AutoUpdater.UpdateChecker();
            _currentVersion = updateChecker.GetCurrentVersion();
        }

        private async void LoadSettings()
        {
            _currentConfig = await ConfigManagement.LoadConfigAsync();
            if (_currentConfig != null)
            {
                // Check if user has already seen this version's announcement
                NeverShowAgain = _currentConfig.LastSeenAnnouncementVersion == _currentVersion;
            }
        }

        private void LoadAnnouncementContent()
        {
            try
            {
                // Load from embedded resource
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "SaveTracker.announcement.md";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            AnnouncementContent = reader.ReadToEnd();
                        }
                    }
                    else
                    {
                        AnnouncementContent = "# Welcome to SaveTracker!\n\nNo announcements at this time.";
                        DebugConsole.WriteWarning($"Embedded resource '{resourceName}' not found");
                    }
                }
            }
            catch (Exception ex)
            {
                AnnouncementContent = "# Error\n\nFailed to load announcements.";
                DebugConsole.WriteException(ex, "Failed to load announcement.md from embedded resources");
            }
        }

        public async Task SaveSettingsAsync()
        {
            if (_currentConfig == null)
            {
                _currentConfig = new Config();
            }

            // Save current version as last seen
            _currentConfig.LastSeenAnnouncementVersion = _currentVersion;
            await ConfigManagement.SaveConfigAsync(_currentConfig);
            DebugConsole.WriteInfo($"Saved last seen announcement version: {_currentVersion}");
        }

        /// <summary>
        /// Called when the window is closing to save the current version
        /// </summary>
        public async Task OnWindowClosingAsync()
        {
            // Save current version as last seen (auto-enable behavior)
            await SaveSettingsAsync();
        }
    }
}
