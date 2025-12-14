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

        [ObservableProperty]
        private bool _isSelectionMode; // False = Launcher Selection, True = Game Selection

        [ObservableProperty]
        private ObservableCollection<PlayniteGameWrapper> _detectedGames = new();

        private List<PlayniteGame> _scannedGames = new(); // Raw results
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
                IconPath = "/Assets/playnite_icon.png",
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
            if (IsSelectionMode)
            {
                await ConfirmImportAsync();
                return;
            }

            // Step 1: Scan
            if (SelectedLauncher == null || !SelectedLauncher.IsAvailable) return;

            IsImporting = true;
            StatusText = $"Scanning {SelectedLauncher.Name} library...";
            DetectedGames.Clear();

            try
            {
                List<PlayniteGame> rawGames = new();

                if (SelectedLauncher.Type == LauncherType.Playnite)
                {
                    rawGames = await ScanPlayniteAsync();
                }

                if (rawGames.Count > 0)
                {
                    _scannedGames = rawGames;
                    foreach (var game in _scannedGames)
                    {
                        DetectedGames.Add(new PlayniteGameWrapper(game));
                    }
                    IsSelectionMode = true;
                    StatusText = $"Found {DetectedGames.Count} games. Select games to import.";
                }
                else
                {
                    StatusText = "No games found.";
                    if (SelectedLauncher.Type == LauncherType.Playnite)
                    {
                        if (System.Diagnostics.Process.GetProcessesByName("Playnite.DesktopApp").Length > 0)
                        {
                            StatusText = "No games found. Playnite is running, which might be locking the file. Please close Playnite.";
                        }
                    }
                    IsImporting = false;
                }
            }
            catch (Exception ex)
            {
                HandleImportError(ex);
                IsImporting = false;
            }
        }

        private async Task ConfirmImportAsync()
        {
            var selected = DetectedGames.Where(g => g.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusText = "No games selected.";
                return;
            }

            IsImporting = true;
            StatusText = $"Importing {selected.Count} games...";

            try
            {
                var result = new List<Game>();
                foreach (var wrapper in selected)
                {
                    var pg = wrapper.Game;
                    result.Add(new Game
                    {
                        Name = pg.DisplayName ?? pg.Name ?? "Unknown", // Use refined name
                        InstallDirectory = pg.InstallDirectory ?? "",
                        ExecutablePath = pg.ExecutablePath ?? "",
                        LastTracked = DateTime.MinValue,
                    });
                }

                StatusText = $"Successfully imported {result.Count} games.";
                await Task.Delay(1000);
                OnCloseRequest?.Invoke(result);
            }
            catch (Exception ex)
            {
                StatusText = $"Error importing: {ex.Message}";
                DebugConsole.WriteException(ex, "ConfirmImport failed");
            }
            finally
            {
                IsImporting = false;
            }
        }

        private async Task<List<PlayniteGame>> ScanPlayniteAsync()
        {
            return await Task.Run(async () =>
            {
                // 1. Locate games.db
                string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite", "library", "games.db");
                string dbPath = defaultPath;

                if (!File.Exists(dbPath))
                {
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

                        if (files.Count > 0) dbPath = files[0].Path.LocalPath;
                        else dbPath = string.Empty;
                    });
                }

                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                {
                    throw new FileNotFoundException("Playnite DB file not found or selected.");
                }

                // 2. Read Games
                DebugConsole.WriteInfo($"Reading Playnite DB from: {dbPath}");
                var playniteGames = _playniteReader.ReadGamesFromDb(dbPath);

                // 3. Post-Process (Find Exe & Refine Name)
                foreach (var pg in playniteGames)
                {
                    if (string.IsNullOrEmpty(pg.ExecutablePath) || pg.ExecutablePath == "N/A" || !File.Exists(pg.ExecutablePath))
                    {
                        pg.TryFindExecutable();
                    }

                    // Refine Name using EXE Metadata
                    pg.RefineNameFromExecutable();
                }

                // Filter valid ones
                var validGames = playniteGames.Where(g => g.IsInstalled && !string.IsNullOrEmpty(g.ExecutablePath) && File.Exists(g.ExecutablePath)).ToList();

                DebugConsole.WriteSuccess($"Found {validGames.Count} valid games.");
                return validGames;
            });
        }

        private void HandleImportError(Exception ex)
        {
            if (SelectedLauncher?.Type == LauncherType.Playnite &&
              (ex is IOException || ex.Message.Contains("locked")))
            {
                bool isRunning = System.Diagnostics.Process.GetProcessesByName("Playnite.DesktopApp").Length > 0;
                if (isRunning)
                    StatusText = "Error: Playnite is running and locking the database. Please close Playnite.";
                else
                    StatusText = "Error: Database file is locked by another process.";
            }
            else
            {
                StatusText = $"Error: {ex.Message}";
            }
            DebugConsole.WriteException(ex, "Import failed");
        }

        [RelayCommand]
        private void Cancel()
        {
            if (IsSelectionMode)
            {
                // Go back to launcher selection
                IsSelectionMode = false;
                DetectedGames.Clear();
                StatusText = "Select a launcher to import from.";
            }
            else
            {
                OnCloseRequest?.Invoke(null);
            }
        }
    }

    public partial class PlayniteGameWrapper : ObservableObject
    {
        public PlayniteGame Game { get; }

        public PlayniteGameWrapper(PlayniteGame game)
        {
            Game = game;
        }

        public string Name => Game.DisplayName ?? Game.Name ?? "Unknown";
        public string Path => Game.InstallDirectory ?? "";

        public bool IsSelected
        {
            get => Game.IsSelected;
            set
            {
                if (Game.IsSelected != value)
                {
                    Game.IsSelected = value;
                    OnPropertyChanged();
                }
            }
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
