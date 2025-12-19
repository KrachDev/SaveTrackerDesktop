using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC.Steam;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SaveTracker.Views.Dialog
{
    public partial class UC_SteamImport_ViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<SteamGameSelectItem> _detectedGames = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private bool _hasGames;

        public event Action<List<Game>>? OnGamesImported;

        public UC_SteamImport_ViewModel()
        {
            _ = ScanForSteamGames();
        }

        [RelayCommand]
        private async Task ScanForSteamGames()
        {
            IsLoading = true;
            StatusMessage = "Scanning Steam libraries...";
            DetectedGames.Clear();
            HasGames = false;

            try
            {
                await Task.Run(async () =>
                {
                    var games = SteamLibraryScanner.GetInstalledGames();
                    var existingGames = await ConfigManagement.LoadAllGamesAsync();

                    var items = new List<SteamGameSelectItem>();

                    foreach (var gameInfo in games)
                    {
                        // Check if already imported (by AppID or Name/InstallDir match)
                        bool isAlreadyImported = existingGames.Any(g =>
                            (g.SteamAppId == gameInfo.AppId) ||
                            (g.Name.Equals(gameInfo.Name, StringComparison.OrdinalIgnoreCase)) ||
                            (g.InstallDirectory.Equals(gameInfo.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                        );

                        // Scan for executables to pick the best one
                        var executables = SteamLibraryScanner.ScanForExecutables(gameInfo.InstallDirectory);
                        string bestExe = executables.FirstOrDefault() ?? "";

                        items.Add(new SteamGameSelectItem
                        {
                            GameInfo = gameInfo,
                            IsSelected = !isAlreadyImported, // Select by default if not imported
                            AlreadyImported = isAlreadyImported,
                            DetectedExecutable = bestExe
                        });
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in items)
                        {
                            DetectedGames.Add(item);
                        }

                        HasGames = DetectedGames.Count > 0;
                        StatusMessage = HasGames
                            ? $"Found {DetectedGames.Count} Steam games ({DetectedGames.Count(g => g.AlreadyImported)} already tracked)"
                            : "No Steam games found.";
                        IsLoading = false;
                    });
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to scan Steam games");
                StatusMessage = "Error scanning Steam libraries.";
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ImportSelected()
        {
            var selectedItems = DetectedGames.Where(g => g.IsSelected && !g.AlreadyImported).ToList();
            if (selectedItems.Count == 0)
                return;

            IsLoading = true;
            StatusMessage = $"Importing {selectedItems.Count} games...";

            var importedGames = new List<Game>();

            try
            {
                foreach (var item in selectedItems)
                {
                    var game = new Game
                    {
                        Name = item.GameInfo.Name,
                        InstallDirectory = item.GameInfo.InstallDirectory,
                        ExecutablePath = item.DetectedExecutable, // Might be empty if no exe found
                        SteamAppId = item.GameInfo.AppId,
                        LaunchViaSteam = true, // Default to launching via Steam
                        LastTracked = DateTime.Now
                    };

                    // If we couldn't find an executable, we still want to add it, 
                    // process watcher will rely on directory scanning.
                    // But Game model requires non-null ExecutablePath? 
                    // Let's ensure it's at least not null.
                    if (string.IsNullOrEmpty(game.ExecutablePath))
                    {
                        // Use a placeholder if absolutely no exe found (rare for valid games)
                        // specific logic might be needed in ConfigManagement to allow this?
                        // But ScanForExecutables usually finds something. 
                        // If not, maybe use a dummy file in the directory?
                        game.ExecutablePath = System.IO.Path.Combine(game.InstallDirectory, "game.exe");
                    }

                    await ConfigManagement.SaveGameAsync(game);
                    importedGames.Add(game);
                }

                OnGamesImported?.Invoke(importedGames);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to import Steam games");
                StatusMessage = "Error occurred during import.";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    public partial class SteamGameSelectItem : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _alreadyImported;

        public SteamGameInfo GameInfo { get; set; } = new();

        public string Name => GameInfo.Name;
        public string AppId => GameInfo.AppId;
        public string InstallDir => GameInfo.InstallDirectory;
        public string DetectedExecutable { get; set; } = "";

        public string StatusText => AlreadyImported ? "Already Tracked" : "Ready to Import";
        public string StatusColor => AlreadyImported ? "#858585" : "#4CC9B0";
    }
}
