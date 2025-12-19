using Avalonia.Controls;
using SaveTracker.Resources.HELPERS;
using System;

namespace SaveTracker.Views.Dialog
{
    public partial class UC_SteamImport : Window
    {
        public UC_SteamImport()
        {
            InitializeComponent();
            var vm = new UC_SteamImport_ViewModel();
            DataContext = vm;

            vm.OnGamesImported += (games) =>
            {
                DebugConsole.WriteSuccess($"Successfully imported {games.Count} Steam games");
                Close(games); // Return the list of games as dialog result if needed
            };
        }
    }
}
