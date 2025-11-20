using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaveTracker.Views.Dialog
{
    public class UC_AddGame_ViewModel : INotifyPropertyChanged
    {
        private Game _newGame = new Game();
        public Game NewGame
        {
            get => _newGame;
            set
            {
                _newGame = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
