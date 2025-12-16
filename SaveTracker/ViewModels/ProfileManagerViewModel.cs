using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.ViewModels
{
    public partial class ProfileManagerViewModel : ObservableObject
    {
        private readonly Game _game;
        private readonly ProfileManager _profileManager;

        [ObservableProperty]
        private ObservableCollection<Profile> _profiles = new ObservableCollection<Profile>();

        [ObservableProperty]
        private Profile? _selectedProfile;

        [ObservableProperty]
        private string _activeProfileName = "Unknown";

        [ObservableProperty]
        private string _newProfileName = "";

        public ProfileManagerViewModel(Game game)
        {
            _game = game;
            _profileManager = new ProfileManager();

            _ = LoadProfilesAsync();
        }

        private async Task LoadProfilesAsync()
        {
            Profiles.Clear();
            var profiles = await _profileManager.GetProfilesAsync();
            foreach (var p in profiles)
            {
                Profiles.Add(p);
            }

            // Determine active profile name
            var activeId = _game.ActiveProfileId;
            var active = profiles.FirstOrDefault(p => p.Id == activeId) ?? profiles.FirstOrDefault(p => p.IsDefault);
            ActiveProfileName = active?.Name ?? "Default";

            // Auto-select the active one
            SelectedProfile = active;
        }

        [RelayCommand]
        private async Task CreateProfile()
        {
            if (string.IsNullOrWhiteSpace(NewProfileName)) return;

            try
            {
                await _profileManager.AddProfileAsync(NewProfileName);
                NewProfileName = ""; // Clear input
                await LoadProfilesAsync(); // Refresh list
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to create profile");
            }
        }

        [RelayCommand]
        private async Task DeleteProfile()
        {
            if (SelectedProfile == null || SelectedProfile.IsDefault) return;

            try
            {
                await _profileManager.DeleteProfileAsync(SelectedProfile.Id);
                await LoadProfilesAsync();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to delete profile");
            }
        }

        [RelayCommand]
        private async Task SwitchProfile()
        {
            if (SelectedProfile == null) return;

            try
            {
                await _profileManager.SwitchProfileAsync(_game, SelectedProfile.Id);
                await LoadProfilesAsync(); // Refresh active status
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to switch profile");
            }
        }

        [RelayCommand]
        private async Task OpenQuarantine()
        {
            try
            {
                var vm = new QuarantineViewModel(_game);
                var win = new SaveTracker.Views.QuarantineWindow { DataContext = vm };

                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var owner = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
                    if (owner != null) await win.ShowDialog(owner);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open quarantine manager");
            }
        }
    }
}
