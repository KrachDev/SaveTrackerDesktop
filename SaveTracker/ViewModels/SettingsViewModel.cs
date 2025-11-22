using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
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

        public SettingsViewModel()
        {
            LoadSettings();
        }

        private async void LoadSettings()
        {
            _currentConfig = await ConfigManagement.LoadConfigAsync();
            if (_currentConfig != null)
            {
                EnableAutomaticTracking = _currentConfig.EnableAutomaticTracking;
                StartMinimized = _currentConfig.StartMinimized;
                ShowDebugConsole = _currentConfig.ShowDebugConsole;
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
    }
}
