using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SaveTracker.ViewModels
{
    public partial class AppIdEntryViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<GameAppIdEntry> _games;

        public event Action<bool>? RequestClose;

        public AppIdEntryViewModel(string[] gameNames)
        {
            _games = new ObservableCollection<GameAppIdEntry>(
                gameNames.Select(name => new GameAppIdEntry(name))
            );
        }

        [RelayCommand]
        private void Confirm()
        {
            RequestClose?.Invoke(true);
        }

        [RelayCommand]
        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }
    }

    public partial class GameAppIdEntry : ObservableObject
    {
        public string GameName { get; }

        [ObservableProperty]
        private string _appId = "";

        public GameAppIdEntry(string gameName)
        {
            GameName = gameName;
        }
    }
}
