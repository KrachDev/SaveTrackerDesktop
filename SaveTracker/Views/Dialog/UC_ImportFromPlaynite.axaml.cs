using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SaveTracker.Views.Dialog
{
    public partial class UC_ImportFromPlaynite : Window
    {
        public UC_ImportFromPlaynite_ViewModel ViewModel { get; }
        public List<Game>? ImportedGames { get; private set; }

        public UC_ImportFromPlaynite()
        {
            try
            {
                DebugConsole.WriteInfo("UC_ImportFromPlaynite constructor starting...");
                InitializeComponent();
                DebugConsole.WriteInfo("InitializeComponent completed");

                ViewModel = new UC_ImportFromPlaynite_ViewModel();
                DataContext = ViewModel;

                DebugConsole.WriteSuccess("UC_ImportFromPlaynite initialized successfully");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "UC_ImportFromPlaynite constructor failed");
            }
        }

        private void OnImportJson(object? sender, RoutedEventArgs e)
        {
            try
            {
                ViewModel.ImportFromJson();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to initiate JSON import");
            }
        }

        private void OnSelectAllInstalled(object? sender, RoutedEventArgs e)
        {
            try
            {
                var isChecked = (sender as CheckBox)?.IsChecked ?? false;
                foreach (var game in ViewModel.InstalledGames)
                {
                    game.IsSelected = isChecked;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to select all");
            }
        }

        private void OnDeselectAllInstalled(object? sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var game in ViewModel.InstalledGames)
                {
                    game.IsSelected = false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to deselect all");
            }
        }

        private void OnAddSelected(object? sender, RoutedEventArgs e)
        {
            try
            {
                var selectedGames = ViewModel.InstalledGames
                    .Where(g => g.IsSelected)
                    .ToList();

                if (selectedGames.Count == 0)
                {
                    ViewModel.StatusMessage = "⚠️ Please select at least one game to import";
                    return;
                }

                // Convert to SaveTracker Game objects
                ImportedGames = new List<Game>();
                foreach (var gameVm in selectedGames)
                {
                    // Skip games with missing data
                    if (string.IsNullOrEmpty(gameVm.Game.Name) ||
                        string.IsNullOrEmpty(gameVm.Game.InstallDirectory) ||
                        string.IsNullOrEmpty(gameVm.Game.ExecutablePath))
                        continue;

                    var game = new Game
                    {
                        Name = gameVm.Name,
                        InstallDirectory = gameVm.Game.InstallDirectory,
                        ExecutablePath = gameVm.Game.ExecutablePath,
                        LastTracked = DateTime.MinValue
                    };
                    ImportedGames.Add(game);
                }

                DebugConsole.WriteSuccess($"Selected {ImportedGames.Count} games to import");
                Close(ImportedGames);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to add selected games");
            }
        }

        private void OnCancel(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
