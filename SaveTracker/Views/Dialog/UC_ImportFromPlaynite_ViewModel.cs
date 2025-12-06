using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SaveTracker.Views.Dialog
{
    public partial class UC_ImportFromPlaynite_ViewModel : ObservableObject
    {


        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = "Enter your Playnite installation path to begin";

        [ObservableProperty]
        private int _installedGamesCount = 0;

        [ObservableProperty]
        private int _notInstalledGamesCount = 0;

        public ObservableCollection<PlayniteGameViewModel> InstalledGames { get; } = new();
        public ObservableCollection<PlayniteGameViewModel> NotInstalledGames { get; } = new();

        public UC_ImportFromPlaynite_ViewModel()
        {
        }

        public async void ImportFromJson()
        {
            try
            {
                // We need to resolve the top-level window to show the dialog
                // Since this is a ViewModel, we might need to rely on a view service or hackily get the active window
                // For this project structure, often `App.Current` or `(Application.Current as App).MainWindow` might work 
                // but let's see if we can use a more standard approach if available, or just try to get the storage provider from a static helper if one exists.
                // Given the context, we will try to find the TopLevel from the Application Lifetime.

                var topLevel = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var mainWindow = topLevel?.MainWindow;

                if (mainWindow == null)
                    return;

                var storageProvider = mainWindow.StorageProvider;

                var result = await storageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select Playnite Export JSON",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } },
                        new Avalonia.Platform.Storage.FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
                    }
                });

                if (result == null || result.Count == 0)
                    return;

                var file = result[0];
                var filePath = file.Path.LocalPath;

                IsLoading = true;
                StatusMessage = "Reading games from JSON...";
                // PlayniteLibraryPath = "Imported from JSON"; // Visual feedback (Removed)

                // Run on background thread
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var reader = new PlayniteLibraryReader("dummy"); // Path doesn't matter for JSON method
                        var playniteGames = reader.ReadGamesFromJson(filePath);

                        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                        {
                            // Clear existing lists
                            InstalledGames.Clear();
                            NotInstalledGames.Clear();

                            // Separate into installed and not installed
                            foreach (var game in playniteGames)
                            {
                                var vm = new PlayniteGameViewModel(game);

                                if (game.IsInstalled)
                                    InstalledGames.Add(vm);
                                else
                                    NotInstalledGames.Add(vm);
                            }

                            InstalledGamesCount = InstalledGames.Count;
                            NotInstalledGamesCount = NotInstalledGames.Count;

                            StatusMessage = $"✔ Imported {InstalledGamesCount} installed games from JSON";
                        });
                    }
                    catch (Exception ex)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                        {
                            StatusMessage = $"❌ Error reading JSON: {ex.Message}";
                        });
                        DebugConsole.WriteException(ex, "Failed to load Playnite games from JSON");
                    }
                    finally
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Invoke(() => IsLoading = false);
                    }
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to start file picker");
            }
        }


    }

    public partial class PlayniteGameViewModel : ObservableObject
    {
        public PlayniteGame Game { get; }

        [ObservableProperty]
        private bool _isSelected;

        public string Name => Game.Name ?? "Unknown";
        public string InstallDirectory => Game.InstallDirectory ?? "Not installed";
        public string ExecutablePath => Game.ExecutablePath ?? "N/A";
        public bool IsInstalled => Game.IsInstalled;

        public PlayniteGameViewModel(PlayniteGame game)
        {
            Game = game;
        }
    }
}
