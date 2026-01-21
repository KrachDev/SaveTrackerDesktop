using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.LOGIC.Steam;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Platform;

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
        private ObservableCollection<ImportGameViewModel> _detectedGames = new();

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
                IconPath = "/Assets/playnite_icon.svg",
                IsAvailable = true,
                Type = LauncherType.Playnite
            });

            Launchers.Add(new LauncherOption
            {
                Name = "Steam",
                Description = "Import from Steam Library",
                IconPath = "/Assets/steam_icon.svg",
                IsAvailable = true,
                Type = LauncherType.Steam
            });

            Launchers.Add(new LauncherOption
            {
                Name = "Epic Games",
                Description = "Import from Epic (Coming Soon)",
                IconPath = "/Assets/epic_icon.svg",
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
                List<ImportGameViewModel> rawGames = new();

                if (SelectedLauncher.Type == LauncherType.Playnite)
                {
                    rawGames = await ScanPlayniteAsync();
                }
                else if (SelectedLauncher.Type == LauncherType.Steam)
                {
                    rawGames = await ScanSteamAsync();
                }

                if (rawGames.Count > 0)
                {
                    foreach (var game in rawGames)
                    {
                        DetectedGames.Add(game);
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
                foreach (var vm in selected)
                {
                    var newGame = new Game
                    {
                        Name = vm.Name,
                        InstallDirectory = vm.Path,
                        ExecutablePath = vm.ExecutablePath,
                        LastTracked = DateTime.MinValue,
                        SteamAppId = vm.SteamAppId,
                        LaunchViaSteam = vm.LaunchViaSteam
                    };

                    // Save explicitly to config immediately? 
                    // Usually the caller (MainWindow) handles adding it to the main list
                    // But duplicates check happens here?
                    // Previous logic just returned list.

                    result.Add(newGame);
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

        private async Task<List<ImportGameViewModel>> ScanPlayniteAsync()
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

                // Check against existing games (generic duplicate check)
                var existingGames = await ConfigManagement.LoadAllGamesAsync();

                return validGames.Select(pg => new ImportGameViewModel
                {
                    Name = pg.DisplayName ?? pg.Name ?? "Unknown",
                    Path = pg.InstallDirectory ?? "",
                    ExecutablePath = pg.ExecutablePath ?? "",
                    Source = "Playnite",
                    IsSelected = !existingGames.Any(g => g.InstallDirectory.Equals(pg.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                }).ToList();
            });
        }

        private async Task<List<ImportGameViewModel>> ScanSteamAsync()
        {
            return await Task.Run(async () =>
            {
                var steamGames = SteamLibraryScanner.GetInstalledGames();
                var existingGames = await ConfigManagement.LoadAllGamesAsync();

                var results = new List<ImportGameViewModel>();

                foreach (var gameInfo in steamGames)
                {
                    // Scan for executable
                    var executables = SteamLibraryScanner.ScanForExecutables(gameInfo.InstallDirectory);
                    string bestExe = executables.FirstOrDefault() ?? "";

                    // Check if already imported
                    bool isAlreadyImported = existingGames.Any(g =>
                        (g.SteamAppId == gameInfo.AppId) ||
                        (g.Name.Equals(gameInfo.Name, StringComparison.OrdinalIgnoreCase)) ||
                        (g.InstallDirectory.Equals(gameInfo.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                    );

                    results.Add(new ImportGameViewModel
                    {
                        Name = gameInfo.Name,
                        Path = gameInfo.InstallDirectory,
                        ExecutablePath = bestExe, // Might be empty
                        SteamAppId = gameInfo.AppId,
                        LaunchViaSteam = true,
                        Source = "Steam",
                        IsSelected = !isAlreadyImported
                    });
                }

                return results;
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

    public partial class ImportGameViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string? SteamAppId { get; set; }
        public bool LaunchViaSteam { get; set; } = false;
        public string Source { get; set; } = "";

        // UI Helpers
        public string DisplayPath => Path;
        public string DisplayName => Name;
    }

    public class LauncherOption
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        private string _iconPath = "";
        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (_iconPath != value)
                {
                    _iconPath = value;
                    LoadIcon();
                }
            }
        }

        public bool IsAvailable { get; set; }
        public LauncherType Type { get; set; }

        public Avalonia.Media.IImage? Icon { get; private set; }

        private void LoadIcon()
        {
            if (string.IsNullOrEmpty(IconPath)) return;

            try
            {
                // Ensure the URI is correct (avares://SaveTracker/Assets/...)
                // If IconPath starts with /, remove it to append to base
                string cleanPath = IconPath.TrimStart('/');
                var uri = new Uri($"avares://SaveTracker/{cleanPath}");

                if (IconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    // Load SVG
                    var svg = new Avalonia.Svg.Skia.SvgImage
                    {
                        Source = Avalonia.Svg.Skia.SvgSource.Load(uri.ToString(), null)
                    };
                    Icon = svg;
                }
                else
                {
                    // Load Bitmap
                    Icon = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load icon: {IconPath} - {ex.Message}");
            }
        }
    }

    public enum LauncherType
    {
        Playnite,
        Steam,
        Epic
    }
}
