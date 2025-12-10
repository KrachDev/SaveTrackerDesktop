using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SaveTracker.ViewModels
{
    public partial class ImportSelectionWindowViewModel : ObservableObject
    {
        public event Action<List<Game>?>? OnCloseRequest;

        [ObservableProperty]
        private ObservableCollection<LauncherOption> _launchers = new();

        [ObservableProperty]
        private LauncherOption? _selectedLauncher;

        [ObservableProperty]
        private bool _isImporting;

        [ObservableProperty]
        private string _statusText = "Select a launcher to import from.";

        private readonly PlayniteLibraryReader _playniteReader;

        public ImportSelectionWindowViewModel()
        {
            _playniteReader = new PlayniteLibraryReader();
            InitializeLaunchers();
        }

        private void InitializeLaunchers()
        {
            Launchers.Add(new LauncherOption
            {
                Name = "Playnite",
                Description = "Import from Playnite Library",
                IconPath = "/Assets/playnite_icon.png", // Need to ensure icon exists or use generic
                IsAvailable = true,
                Type = LauncherType.Playnite
            });

            Launchers.Add(new LauncherOption
            {
                Name = "Steam",
                Description = "Import from Steam (Coming Soon)",
                IsAvailable = false,
                Type = LauncherType.Steam
            });

            Launchers.Add(new LauncherOption
            {
                Name = "Epic Games",
                Description = "Import from Epic (Coming Soon)",
                IsAvailable = false,
                Type = LauncherType.Epic
            });

            // Select default
            SelectedLauncher = Launchers.FirstOrDefault(l => l.Type == LauncherType.Playnite);
        }

        [RelayCommand]
        private async Task ImportAsync()
        {
            if (SelectedLauncher == null || !SelectedLauncher.IsAvailable) return;

            IsImporting = true;
            StatusText = $"Importing from {SelectedLauncher.Name}...";

            try
            {
                List<Game> importedGames = new();

                if (SelectedLauncher.Type == LauncherType.Playnite)
                {
                    importedGames = await ImportFromPlayniteAsync();
                }

                if (importedGames.Count > 0)
                {
                    StatusText = $"Successfully imported {importedGames.Count} games.";
                    await Task.Delay(1000); // Brief delay to show success
                    OnCloseRequest?.Invoke(importedGames);
                }
                else
                {
                    StatusText = "No games found.";

                    if (SelectedLauncher.Type == LauncherType.Playnite)
                    {
                        bool isAdmin = false;
                        try
                        {
                            isAdmin = await AdminHelper.IsAdministrator();
                        }
                        catch { }

                        if (!isAdmin)
                            StatusText += " File is locked. Please Restart SaveTracker as Administrator.";
                        else
                            StatusText += " File is locked even with Admin privileges. Please Close Playnite.";
                    }
                    IsImporting = false;
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                DebugConsole.WriteException(ex, "Import failed");
                IsImporting = false;
            }
        }

        private async Task<List<Game>> ImportFromPlayniteAsync()
        {
            return await Task.Run(async () =>
            {
                // 1. Locate games.db
                string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite", "library", "games.db");
                string dbPath = defaultPath;

                if (!File.Exists(dbPath))
                {
                    // Prompt user to find it via UI thread
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
                        if (mainWindow == null) return;
                        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(mainWindow);
                        if (topLevel == null) return;

                        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                        {
                            Title = "Select Playnite Database (games.db)",
                            AllowMultiple = false,
                            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Playnite Database") { Patterns = new[] { "*.db" } } }
                        });

                        if (files.Count > 0)
                        {
                            dbPath = files[0].Path.LocalPath;
                        }
                        else
                        {
                            dbPath = string.Empty;
                        }
                    });
                }

                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                {
                    DebugConsole.WriteWarning("Playnite DB file not found or selected.");
                    return new List<Game>();
                }

                // 2. Read Games
                DebugConsole.WriteInfo($"Reading Playnite DB from: {dbPath}");
                var playniteGames = _playniteReader.ReadGamesFromDb(dbPath);

                // 3. Convert to SaveTracker Games
                // Identify executables if missing
                foreach (var pg in playniteGames)
                {
                    if (string.IsNullOrEmpty(pg.ExecutablePath) || pg.ExecutablePath == "N/A" || !File.Exists(pg.ExecutablePath))
                    {
                        pg.TryFindExecutable();
                    }
                }

                // Filter valid ones
                var validGames = playniteGames.Where(g => g.IsInstalled && !string.IsNullOrEmpty(g.ExecutablePath) && File.Exists(g.ExecutablePath)).ToList();

                DebugConsole.WriteSuccess($"Found {validGames.Count} valid games out of {playniteGames.Count} total entries.");

                // Convert
                var result = new List<Game>();
                foreach (var pg in validGames)
                {
                    result.Add(new Game
                    {
                        Name = pg.Name ?? "Unknown",
                        InstallDirectory = pg.InstallDirectory ?? "",
                        ExecutablePath = pg.ExecutablePath ?? "",
                        LastTracked = DateTime.MinValue,
                        // Could store source info here if Game model supported it
                    });
                }

                return result;
            });
        }

        [RelayCommand]
        private void Cancel()
        {
            OnCloseRequest?.Invoke(null);
        }
    }

    public class LauncherOption
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconPath { get; set; } = "";
        public bool IsAvailable { get; set; }
        public LauncherType Type { get; set; }
    }

    public enum LauncherType
    {
        Playnite,
        Steam,
        Epic
    }
}
