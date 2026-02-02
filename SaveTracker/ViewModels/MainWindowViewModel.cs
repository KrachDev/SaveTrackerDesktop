using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.Logic.AutoUpdater;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.LOGIC.Launching;
using SaveTracker.Resources.LOGIC.IPC;

using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Views;
using SaveTracker.ViewModels;
using SaveTracker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MsBox.Avalonia;
using SaveTracker.Views.Dialog;

namespace SaveTracker.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ConfigManagement _configManagement;
        private readonly RcloneFileOperations _rcloneFileOperations;
        private readonly CloudProviderHelper _providerHelper;
        private GameProcessWatcher? _gameProcessWatcher;
        private SaveFileTrackerManager _trackLogic;
        private SaveFileUploadManager _uploadManager;
        private CancellationTokenSource _trackingCancellation;

        // Observable Properties
        [ObservableProperty]
        private ObservableCollection<GameViewModel> _games = new();

        // Backing list for search filtering
        private List<GameViewModel> _allGames = new();

        [ObservableProperty]
        private string _searchText = "";

        partial void OnSearchTextChanged(string value)
        {
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            string query = SearchText?.Trim() ?? "";

            Games.Clear();

            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (var game in _allGames)
                {
                    Games.Add(game);
                }
            }
            else
            {
                var filtered = _allGames.Where(g => g.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var game in filtered)
                {
                    Games.Add(game);
                }
            }

            // If selected game was filtered out, deselect it? 
            // Better behavior: keep it if possible, but standard behavior usually deselects if removed from view. 
            // Avalonia/WPF might handle this automatically or keep the selection object even if not in view.
        }

        [ObservableProperty]
        private GameViewModel? _selectedGame;

        [ObservableProperty]
        private ObservableCollection<TrackedFileViewModel> _trackedFiles = new();

        [ObservableProperty]
        private ObservableCollection<CloudFileViewModel> _cloudFiles = new();

        [ObservableProperty]
        private string _gameTitleText = "Select a game";

        [ObservableProperty]
        private string _gamePathText = "No game selected";

        [ObservableProperty]
        private string _gamePlayTimeText = "Play Time: Never";

        [ObservableProperty]
        private string _gameProfileText = "Profile: Default";

        [ObservableProperty]
        private string _cloudStorageText = "Not configured";

        [ObservableProperty]
        private string _filesTrackedText = "0 Files Tracked";

        [ObservableProperty]
        private string _cloudFilesCountText = "0 files in cloud";

        [ObservableProperty]
        private string _gamesCountText = "0 games tracked";

        [ObservableProperty]
        private string _syncStatusText = "Idle";

        [ObservableProperty]
        private bool _isLaunchEnabled;

        [ObservableProperty]
        private bool _areAllTrackedFilesSelected;

        [ObservableProperty]
        private bool _areAllCloudFilesSelected;

        partial void OnAreAllTrackedFilesSelectedChanged(bool value)
        {
            if (TrackedFiles != null)
            {
                foreach (var file in TrackedFiles)
                {
                    file.IsSelected = value;
                }
            }
        }

        partial void OnAreAllCloudFilesSelectedChanged(bool value)
        {
            if (CloudFiles != null)
            {
                foreach (var file in CloudFiles)
                {
                    file.IsSelected = value;
                }
            }
        }

        [ObservableProperty]
        private bool _isSyncEnabled;

        partial void OnIsLaunchEnabledChanged(bool value)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                LaunchGameCommand.NotifyCanExecuteChanged();
                OpenProfileManagerCommand.NotifyCanExecuteChanged();
            });
        }

        partial void OnIsSyncEnabledChanged(bool value)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SyncNowCommand.NotifyCanExecuteChanged();
            });
        }

        [ObservableProperty]
        private bool _isSyncing;

        partial void OnIsSyncingChanged(bool value)
        {
            // Update IPC State (simplified, assuming upload for now or generally busy)
            // Precise distinction between upload/download would require more granular state in VM
            // but IsSyncing covers the "busy" state.
            if (value)
            {
                CommandHandler.CurrentSyncOperation = "Syncing";
            }
            else
            {
                CommandHandler.IsCurrentlyUploading = false;
                CommandHandler.IsCurrentlyDownloading = false;
                CommandHandler.CurrentSyncOperation = null;
            }
        }

        [ObservableProperty]
        private string _syncButtonText = "☁️ Sync Now";

        [ObservableProperty]
        private bool _canUpload = true;

        [ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _gameIcon;

        [ObservableProperty]
        private string _appVersion = GetAppVersion();

        // Auto-updater properties
        [ObservableProperty]
        private bool _updateAvailable;

        [ObservableProperty]
        private string _updateVersion = string.Empty;

        [ObservableProperty]
        private bool _isCheckingForUpdates;
        private bool _skipNextExitUpload = false;
        private UpdateInfo? _latestUpdateInfo;

        private Config? _mainConfig;
        private CloudProvider _selectedProvider;
        private Task? _trackingTask;

        private static string GetAppVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version;
            // Uses generated BuildInfo class
            string verStr = version != null ? $"SaveTracker Desktop v{version.Major}.{version.Minor}.{version.Build}" : "SaveTracker Desktop";
            try
            {
                verStr += $" | Build: {SaveTracker.Resources.SAVE_SYSTEM.BuildInfo.BuildNumber}";
            }
            catch { } // Ignore if BuildInfo not found (e.g. fresh clone)
            return verStr;
        }
        // ========== SIMPLIFIED EVENTS ==========
        public event Action? OnAddGameRequested;
        public event Action? OnAddFilesRequested;
        public event Action? OnCloudSettingsRequested;
        public event Action? OnBlacklistRequested;
        public event Func<Task>? OnRcloneSetupRequired;
        public event Action? OnSettingsRequested;
        public event Action? RequestMinimize;
        public event Func<string, Task<bool>>? OnCloudSaveFound;
        public event Action<UpdateInfo>? OnUpdateAvailable;

        // Smart Sync Integration
        public event Func<SmartSyncViewModel, Task>? OnSmartSyncRequested;

        // Legacy Migration
        public event Action? OnLegacyMigrationRequested;

        // ========== DEBUG HELPER METHODS ==========
        public int GetAddGameRequestedSubscriberCount() => OnAddGameRequested?.GetInvocationList().Length ?? 0;
        public int GetAddFilesRequestedSubscriberCount() => OnAddFilesRequested?.GetInvocationList().Length ?? 0;
        public int GetCloudSettingsRequestedSubscriberCount() => OnCloudSettingsRequested?.GetInvocationList().Length ?? 0;
        public int GetBlacklistRequestedSubscriberCount() => OnBlacklistRequested?.GetInvocationList().Length ?? 0;
        public int GetRcloneSetupRequiredSubscriberCount() => OnRcloneSetupRequired?.GetInvocationList().Length ?? 0;

        private readonly INotificationService? _notificationService;

        public MainWindowViewModel(INotificationService? notificationService = null)
        {
            _notificationService = notificationService;
            _configManagement = new ConfigManagement();
            _rcloneFileOperations = new RcloneFileOperations();
            _providerHelper = new CloudProviderHelper();
            InitializeGameSettingsProviders();

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                // Enable console engine but don't show window yet
                DebugConsole.Enable(true);

                await LoadDataAsync();

                // Apply settings
                if (_mainConfig != null)
                {
                    if (_mainConfig.ShowDebugConsole)
                    {
                        DebugConsole.ShowConsole();
                        DebugConsole.WriteLine("Console Started!");
                    }
                    else
                    {
                        DebugConsole.HideConsole();
                    }

                    if (_mainConfig.StartMinimized)
                    {
                        RequestMinimize?.Invoke();
                    }
                }

                DebugConsole.WriteInfo("Is Admin: " + (await AdminHelper.IsAdministrator()).ToString());

                // Check for updates on startup if enabled
                if (_mainConfig?.CheckForUpdatesOnStartup ?? true)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (await NetworkHelper.IsInternetAvailableAsync())
                            {
                                await CheckForUpdatesAsync();
                            }
                            else
                            {
                                DebugConsole.WriteInfo("Offline: Skipping update check.");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteWarning($"Background update check failed: {ex.Message}");
                        }
                    });
                }

                // Update Cloud Game Cache in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (await NetworkHelper.IsInternetAvailableAsync())
                        {
                            await UpdateCloudGameCacheAsync();
                        }
                        else
                        {
                            DebugConsole.WriteInfo("Offline: Skipping cloud game cache update.");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Background cloud cache update failed: {ex.Message}");
                    }
                });

            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to initialize MainWindowViewModel");
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                // Load games
                var gamelist = await ConfigManagement.LoadAllGamesAsync();

                // Populate _allGames list
                _allGames.Clear();
                if (gamelist != null)
                {
                    foreach (var game in gamelist)
                    {
                        // Skip deleted games (though LoadAllGamesAsync already handles removal)
                        if (game.IsDeleted) continue;

                        try
                        {
                            _allGames.Add(new GameViewModel(game));
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteException(ex, $"Failed to add game: {game?.Name ?? "Unknown"}");
                        }
                    }
                }

                // Populate visible Games collection via filter
                ApplySearchFilter();

                UpdateGamesCount();

                // Load config
                _mainConfig = await ConfigManagement.LoadConfigAsync();
                if (_mainConfig != null)
                {
                    _selectedProvider = _mainConfig.CloudConfig.Provider;
                    CloudStorageText = _providerHelper.GetProviderDisplayName(_selectedProvider);
                }

                // Initialize and start game process watcher
                if (_mainConfig?.EnableAutomaticTracking ?? true)
                {
                    _gameProcessWatcher = new GameProcessWatcher();
                    _gameProcessWatcher.GameProcessDetected += OnGameAutoDetected;
                    _gameProcessWatcher.StartWatching(gamelist);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load data");
            }
        }

        public async Task ReloadConfigAsync()
        {
            try
            {
                DebugConsole.WriteInfo("Reloading configuration...");

                // Reload config
                _mainConfig = await ConfigManagement.LoadConfigAsync();
                if (_mainConfig != null)
                {
                    _selectedProvider = _mainConfig.CloudConfig.Provider;
                    CloudStorageText = _providerHelper.GetProviderDisplayName(_selectedProvider);

                    // Update effective provider text for the currently selected game
                    UpdateEffectiveProviderText();

                    // Update game process watcher if automatic tracking setting changed
                    var gamelist = await ConfigManagement.LoadAllGamesAsync();
                    if (_mainConfig.EnableAutomaticTracking && _gameProcessWatcher == null)
                    {
                        _gameProcessWatcher = new GameProcessWatcher();
                        _gameProcessWatcher.GameProcessDetected += OnGameAutoDetected;
                        _gameProcessWatcher.StartWatching(gamelist);
                    }
                    else if (!_mainConfig.EnableAutomaticTracking && _gameProcessWatcher != null)
                    {
                        _gameProcessWatcher.StopWatching();
                        _gameProcessWatcher.GameProcessDetected -= OnGameAutoDetected;
                        _gameProcessWatcher = null;
                    }
                }

                DebugConsole.WriteSuccess("Configuration reloaded");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to reload configuration");
            }
        }

        partial void OnSelectedGameChanged(GameViewModel? value)
        {
            if (value != null)
            {
                // Run game details loading on background thread to prevent UI freeze
                _ = Task.Run(() => LoadGameDetailsAsync(value));
            }
            else
            {
                GameTitleText = "Select a game";
                GamePathText = "No game selected";
                GameIcon = null;
                IsLaunchEnabled = false;
                IsSyncEnabled = false;
                TrackedFiles.Clear();
                CloudFiles.Clear();
            }

            // Notify commands to re-evaluate CanExecute
            LaunchGameCommand.NotifyCanExecuteChanged();
            SyncNowCommand.NotifyCanExecuteChanged();
        }

        private async Task LoadGameDetailsAsync(GameViewModel gameViewModel)
        {
            try
            {
                var game = gameViewModel.Game;
                var data = await ConfigManagement.GetGameData(game);

                // UI updates must be on the UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    GameTitleText = game.Name;

                    // Check if data exists before accessing properties
                    if (data != null && data.PlayTime != TimeSpan.Zero)
                        GamePlayTimeText = "Play Time: " + data.PlayTime.ToString(@"hh\:mm\:ss");
                    else
                        GamePlayTimeText = "Play Time: Never";
                    GamePathText = $"Install Path: {game.InstallDirectory}";
                    IsLaunchEnabled = true;

                    // Load icon
                    GameIcon = Misc.ExtractIconFromExe(game.ExecutablePath);

                    // Check if sync is available
                    IsSyncEnabled = true;

                    // Load tracked files
                    await UpdateTrackedListAsync(game);
                    // Load game-specific settings
                    await LoadGameSettingsAsync(game);
                    InitializeGameProperties(game);
                    // Update profile display
                    await UpdateProfileDisplayAsync(game);

                    // Clear cloud files
                    CloudFiles.Clear();
                    CloudFilesCountText = "Click refresh to load";

                    // Notify commands to update
                    LaunchGameCommand.NotifyCanExecuteChanged();
                    SyncNowCommand.NotifyCanExecuteChanged();
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Error loading game details");
            }
        }

        private async Task UpdateProfileDisplayAsync(Game game)
        {
            try
            {
                var globalConfig = await ConfigManagement.LoadConfigAsync();
                if (globalConfig != null && globalConfig.Profiles != null)
                {
                    var profiles = globalConfig.Profiles;
                    var activeId = game.ActiveProfileId;
                    var activeProfile = profiles.FirstOrDefault(p => p.Id == activeId)
                                        ?? profiles.FirstOrDefault(p => p.IsDefault);

                    string profileName = activeProfile?.Name ?? "Default";
                    GameProfileText = $"Profile: {profileName}";
                }
                else
                {
                    GameProfileText = "Profile: Default";
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to update profile display: {ex.Message}");
                GameProfileText = "Profile: Unknown";
            }
        }

        [RelayCommand]
        private void OpenCloudLibrary()
        {
            try
            {
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
               ? desktop.MainWindow
               : null;

                if (mainWindow != null)
                {
                    if (!mainWindow.IsVisible)
                    {
                        mainWindow.Show();
                    }
                    if (mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = WindowState.Normal;
                    }
                    mainWindow.Activate();

                    var window = new SaveTracker.Views.CloudLibraryWindow();
                    window.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open cloud library");
            }
        }

        [RelayCommand]
        private void OpenLegacyMigration()
        {
            try
            {
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                   ? desktop.MainWindow
                   : null;

                if (mainWindow != null)
                {
                    if (!mainWindow.IsVisible)
                    {
                        mainWindow.Show();
                    }
                    if (mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = WindowState.Normal;
                    }
                    mainWindow.Activate();

                    var window = new SaveTracker.Views.LegacyMigrationWindow();
                    window.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open legacy migration window");
            }
        }

        [RelayCommand]
        private async Task AddGameAsync()
        {
            try
            {
                DebugConsole.WriteInfo("=== AddGameCommand executed ===");

                // Get the main window
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow as MainWindow
                    : null;

                if (mainWindow == null)
                {
                    DebugConsole.WriteError("Could not get MainWindow reference");
                    return;
                }

                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                mainWindow.Activate();

                DebugConsole.WriteInfo("Creating UC_AddGame dialog...");
                var dialog = new UC_AddGame();

                DebugConsole.WriteInfo("Showing dialog...");
                await dialog.ShowDialog(mainWindow);

                DebugConsole.WriteInfo("Dialog closed");

                // Check if a game was added
                if (dialog.ResultGame != null)
                {
                    DebugConsole.WriteSuccess($"Game added: {dialog.ResultGame.Name}");
                    await OnGameAddedAsync(dialog.ResultGame);
                }
                else
                {
                    DebugConsole.WriteInfo("No game was added");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show add game dialog");
            }
        }

        [RelayCommand]
        private void OpenInstallDirectory()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                string path = SelectedGame.Game.InstallDirectory;
                if (Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    DebugConsole.WriteWarning($"Install directory not found: {path}");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open install directory");
            }
        }

        [RelayCommand]
        private async Task DeleteGameAsync()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                var game = SelectedGame.Game;

                // Prompt user for confirmation
                var box = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                    ContentTitle = "Delete Game",
                    ContentMessage = $"Are you sure you want to delete '{game.Name}'?\n\nThis will remove it from SaveTracker but will NOT delete any local files on your computer.",
                    Icon = MsBox.Avalonia.Enums.Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                });

                var result = await box.ShowAsync();

                if (result == MsBox.Avalonia.Enums.ButtonResult.Yes)
                {
                    DebugConsole.WriteInfo($"Deleting game: {game.Name}");

                    // 1. Delete from config/storage
                    await ConfigManagement.DeleteGameAsync(game.Name);

                    // 2. Remove game watcher for this game if active
                    _gameProcessWatcher?.UnmarkGame(game.Name);

                    // 3. Remove from Master List and UI list
                    var vmToRemove = _allGames.FirstOrDefault(g => g.Game.Name == game.Name);
                    if (vmToRemove != null) _allGames.Remove(vmToRemove);

                    Games.Remove(SelectedGame);

                    // 4. Update UI
                    SelectedGame = null;
                    UpdateGamesCount();

                    // 5. Update watcher list
                    var updatedGamesList = await ConfigManagement.LoadAllGamesAsync();
                    _gameProcessWatcher?.UpdateGamesList(updatedGamesList);

                    DebugConsole.WriteSuccess($"Game '{game.Name}' deleted successfully");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to delete game");
            }
        }

        [RelayCommand]
        private async Task ImportGamesAsync()
        {
            try
            {
                DebugConsole.WriteInfo("=== ImportGamesCommand executed ===");
                // Get the main window
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow as MainWindow
                    : null;
                if (mainWindow == null)
                {
                    DebugConsole.WriteError("Could not get MainWindow reference");
                    return;
                }

                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                mainWindow.Activate();

                var window = new ImportSelectionWindow();
                var result = await window.ShowDialog<List<Game>?>(mainWindow);

                if (result != null && result.Count > 0)
                {
                    DebugConsole.WriteSuccess($"Importing {result.Count} games");

                    foreach (var game in result)
                    {
                        await OnGameAddedAsync(game);
                    }

                    DebugConsole.WriteSuccess($"Successfully imported {result.Count} games");
                    _notificationService?.Show("Import Successful", $"Imported {result.Count} games.", NotificationType.Success);
                }
                else
                {
                    DebugConsole.WriteInfo("No games were imported");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show Import Selection dialog");
            }
        }

        public async Task OnGameAddedAsync(Game newGame)
        {
            try
            {
                // Check if game already exists in _allGames list by Name OR ExecutablePath
                var existingVM = _allGames.FirstOrDefault(vm =>
                    vm.Game.Name.Equals(newGame.Name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(newGame.ExecutablePath) &&
                     vm.Game.ExecutablePath.Equals(newGame.ExecutablePath, StringComparison.OrdinalIgnoreCase)));

                if (existingVM != null)
                {
                    DebugConsole.WriteInfo($"Game found (Name: '{existingVM.Game.Name}', Path: '{existingVM.Game.ExecutablePath}'). Updating with new details from '{newGame.Name}'...");

                    // 1. Update backing Game object
                    // Preserve LocalConfig from existing game to keep user settings
                    newGame.LocalConfig = existingVM.Game.LocalConfig;

                    // Update ALL fields to match the new source
                    existingVM.Game.Name = newGame.Name;
                    existingVM.Game.InstallDirectory = newGame.InstallDirectory;
                    existingVM.Game.ExecutablePath = newGame.ExecutablePath;
                    existingVM.Game.LastTracked = newGame.LastTracked != DateTime.MinValue ? newGame.LastTracked : existingVM.Game.LastTracked;

                    // 2. Update ViewModel properties to notify UI
                    existingVM.Name = newGame.Name; // Update Name in UI
                    existingVM.InstallDirectory = newGame.InstallDirectory;

                    // Force refresh icon
                    try
                    {
                        existingVM.Icon = Misc.ExtractIconFromExe(newGame.ExecutablePath);
                    }
                    catch
                    {
                        existingVM.Icon = null;
                        DebugConsole.WriteWarning($"Could not extract icon for updated game {newGame.Name}");
                    }

                    // 3. Save updated game
                    await ConfigManagement.SaveGameAsync(existingVM.Game);

                    DebugConsole.WriteSuccess($"Game updated: {newGame.Name}");
                }
                else
                {
                    // Add new game
                    var newVM = new GameViewModel(newGame);
                    _allGames.Add(newVM);

                    // Add to UI if it matches filter
                    string query = SearchText?.Trim() ?? "";
                    if (string.IsNullOrEmpty(query) || newGame.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        Games.Add(newVM);
                    }

                    await ConfigManagement.SaveGameAsync(newGame);
                    DebugConsole.WriteSuccess($"Game added: {newGame.Name}");
                    UpdateGamesCount();
                }

                // Update watcher with new games list
                var updatedGamesList = await ConfigManagement.LoadAllGamesAsync();
                _gameProcessWatcher?.UpdateGamesList(updatedGamesList);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to save new game");
            }
        }
        [RelayCommand(CanExecute = nameof(IsSyncEnabled))]
        private async Task SyncNowAsync()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                IsSyncing = true;
                SyncButtonText = "⏳ Syncing...";
                SyncStatusText = "Syncing...";

                var game = SelectedGame.Game;

                DebugConsole.WriteInfo("=== Smart Sync (Manual) ===");

                // Check dependencies (Rclone)
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = GetEffectiveProviderForGame();
                var rcloneInstaller = new RcloneInstaller();

                bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider);
                if (!rcloneReady)
                {
                    if (OnRcloneSetupRequired != null) await OnRcloneSetupRequired.Invoke();
                    return;
                }

                // Open Smart Sync Window directly (async init)
                DebugConsole.WriteInfo($"Opening Smart Sync window (Manual Mode)...");
                var vm = new SmartSyncViewModel(game, SmartSyncMode.ManualSync);
                if (OnSmartSyncRequested != null)
                {
                    await OnSmartSyncRequested.Invoke(vm);
                }

                // RefreshUI
                await LoadGameDetailsAsync(SelectedGame);

                DebugConsole.WriteInfo("=== Smart Sync Complete ===");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Sync Now failed");
                _notificationService?.Show("Sync Failed", ex.Message, NotificationType.Error);
            }
            finally
            {
                IsSyncing = false;
                SyncButtonText = "Sync Now";
                SyncStatusText = "Ready";
            }
        }

        [RelayCommand(CanExecute = nameof(IsLaunchEnabled))]
        private async Task OpenProfileManager()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow as Window
                    : null;

                if (mainWindow != null)
                {
                    var vm = new ProfileManagerViewModel(SelectedGame.Game);
                    var window = new ProfileManagerWindow
                    {
                        DataContext = vm
                    };
                    await window.ShowDialog(mainWindow);

                    // Refresh game details after window closes (in case profile switched)
                    await LoadGameDetailsAsync(SelectedGame);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open profile manager");
            }
        }

        [RelayCommand(CanExecute = nameof(IsLaunchEnabled))]
        private async Task LaunchGameAsync()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                var game = SelectedGame.Game;

                // Check Rclone Config
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config?.CloudConfig;
                var effectiveProvider = GetEffectiveProviderForGame();
                var rcloneInstaller = new RcloneInstaller();

                bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(effectiveProvider);

                if (!rcloneReady)
                {
                    DebugConsole.WriteWarning("Rclone not configured - prompting user");
                    if (OnRcloneSetupRequired != null)
                    {
                        await OnRcloneSetupRequired.Invoke();
                        // Reload config and re-check
                        config = await ConfigManagement.LoadConfigAsync();
                        provider = config?.CloudConfig;
                        effectiveProvider = GetEffectiveProviderForGame();
                        rcloneReady = await rcloneInstaller.RcloneCheckAsync(effectiveProvider);
                    }
                }

                // SMART SYNC - FIRST LAUNCH PROBE (Linux Only)
                // If on Linux, Smart Sync is enabled, and we don't have a prefix yet -> Probe first.
                bool checkSmartSync = true;
                bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
                var gameData = await ConfigManagement.GetGameData(game);
                bool smartSyncEnabled = gameData?.EnableSmartSync ?? true;

                if (isLinux && rcloneReady && smartSyncEnabled && string.IsNullOrEmpty(gameData?.DetectedPrefix))
                {
                    DebugConsole.WriteInfo("First Launch on Linux with Smart Sync enabled. Initiating Probe...");
                    _notificationService?.Show("First Launch Setup", "SaveTracker needs to detect the game save location first.\nThe game will launch and close automatically, then sync.", NotificationType.Information);

                    // 1. Launch Game for Probe
                    string probeExeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
                    GameLauncher.Launch(game);

                    // 2. Track in Probe Mode (waits for detection, then kills game)
                    _trackLogic = new SaveFileTrackerManager();
                    _trackingCancellation = new CancellationTokenSource();
                    await _trackLogic.Track(game, probeForPrefix: true);

                    // 3. Probe Complete
                    DebugConsole.WriteSuccess("Probe complete. Prefix should be saved.");
                    _notificationService?.Show("Setup Complete", "Save location detected. Checking cloud saves...", NotificationType.Success);

                    // Refresh data to get the new prefix
                    gameData = await ConfigManagement.GetGameData(game);
                }

                // Smart Sync: Check cloud vs local PlayTime before launching
                DebugConsole.WriteInfo($"Smart Sync check: rcloneReady={rcloneReady}");

                if (rcloneReady && checkSmartSync)
                {
                    // Check if Smart Sync is enabled for this game (re-read data as it might have changed)
                    smartSyncEnabled = gameData?.EnableSmartSync ?? true;

                    if (smartSyncEnabled)
                    {
                        // Smart Sync Check
                        var smartSync = new SmartSyncService();
                        var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.Zero, effectiveProvider);

                        if (comparison.Status == SmartSyncService.ProgressStatus.CloudAhead)
                        {
                            DebugConsole.WriteInfo($"Smart Sync: Cloud save is newer ({comparison.Difference}). Opening Smart Sync window...");

                            var vm = new SmartSyncViewModel(game, SmartSyncMode.GameLaunch, comparison);
                            if (OnSmartSyncRequested != null)
                            {
                                await OnSmartSyncRequested.Invoke(vm);
                            }


                            // After window closes, proceed with launch.
                            // The user might have downloaded the save in the window.
                            // We just launch now.

                            // Refresh game data just in case
                            await LoadGameDetailsAsync(SelectedGame);
                        }
                        else
                        {
                            // If local is ahead or sync, just launch?
                            // Original code only prompted if CloudAhead.
                            if (comparison.Status == SmartSyncService.ProgressStatus.CloudNotFound)
                            {
                                DebugConsole.WriteInfo("No cloud save found - proceeding with launch");
                            }
                            else
                            {
                                DebugConsole.WriteInfo($"Smart Sync: {comparison.Status} - proceeding with launch");
                            }
                        }
                    }
                    else
                    {
                        DebugConsole.WriteInfo("Smart Sync disabled for this game - skipping check");
                    }
                }
                else
                {
                    DebugConsole.WriteWarning("Rclone not ready - skipping Smart Sync check");
                }
                if (!File.Exists(game.ExecutablePath))
                {
                    DebugConsole.WriteError($"Executable not found: {game.ExecutablePath}");
                    return;
                }

                // Normal Launch
                DebugConsole.WriteInfo("Launching game normally...");
                SaveTracker.Resources.LOGIC.Launching.GameLauncher.Launch(game);

                // Record game launch in analytics (privacy-focused, opt-in)
                _ = AnalyticsService.RecordGameLaunchAsync(game.Name, game.ExecutablePath);

                // NOTE: Game is already launched via GameLauncher above.
                // We just need to start the tracker now.

                // Prevent GameWatcher from trying to auto-track this game
                _gameProcessWatcher?.MarkGameAsTracked(game.Name);

                _trackLogic = new SaveFileTrackerManager();
                _trackingCancellation = new CancellationTokenSource();

                IsLaunchEnabled = false;
                SyncStatusText = "Tracking...";

                // Update IPC State
                CommandHandler.IsCurrentlyTracking = true;
                CommandHandler.CurrentlyTrackingGame = game.Name;

                // Start tracking (standard mode)
                // We don't await this so UI remains responsive
                _trackingTask = Task.Run(async () =>
                {
                    try
                    {
                        await _trackLogic.Track(game);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, "Tracking task failed in Task.Run");
                        throw; // Propagate to continuation
                    }
                }, _trackingCancellation.Token).ContinueWith(async t =>
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception?.InnerException ?? t.Exception;
                        DebugConsole.WriteException(ex!, "Tracking Background Task Crashed");
                    }

                    // Cleanup and trigger post-game logic (Upload/Smart Sync)
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            // Safely trigger exit logic
                            // Pass null process (unused anyway) and the specific game instance
                            await OnGameExitedAsync(null!, game);
                        }
                        catch (Exception exitEx)
                        {
                            DebugConsole.WriteException(exitEx, "Failed to execute post-game exit logic");
                        }
                        finally
                        {
                            IsLaunchEnabled = true;
                            SyncStatusText = "Idle";

                            // Update IPC State
                            CommandHandler.IsCurrentlyTracking = false;
                            CommandHandler.CurrentlyTrackingGame = null;
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to launch game");
                IsLaunchEnabled = true;
                SyncStatusText = "Idle";

                // Update IPC State
                CommandHandler.IsCurrentlyTracking = false;
                CommandHandler.CurrentlyTrackingGame = null;
            }
        }

        private async Task TrackGameProcessAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                DebugConsole.WriteInfo("Starting file tracking...");

                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Resolve Profile Name
                        // This logic is now handled by UpdateProfileDisplayAsync
                        if (SelectedGame?.Game != null)
                        {
                            await UpdateProfileDisplayAsync(SelectedGame.Game);
                        }

                        if (SelectedGame?.Game != null && _trackLogic != null)
                        {
                            if (IsTrackingEnabledForGame())
                            {
                                await _trackLogic.Track(SelectedGame.Game);
                            }
                        }

                        await Task.Delay(5000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("Process has exited"))
                        {
                            DebugConsole.WriteWarning($"Tracking error: {ex.Message}");
                        }
                    }
                }

                DebugConsole.WriteInfo("File tracking stopped");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Tracking process failed: {ex.Message}");
            }
        }

        private async Task OnGameExitedAsync(Process? process, Game? matchingGame = null)
        {
            if (_skipNextExitUpload)
            {
                DebugConsole.WriteInfo("Skipping OnGameExitedAsync upload due to Smart Sync termination.");
                return;
            }

            var game = matchingGame ?? SelectedGame?.Game;
            try
            {
                if (game == null) return;
                DebugConsole.WriteInfo($"{game.Name} closed. Smart Sync Exit Check...");

                _trackingCancellation?.Cancel();
                // Wait for tracking session to complete
                if (_trackingTask != null)
                {
                    try
                    {
                        await _trackingTask;
                        DebugConsole.WriteSuccess("Tracking session completed - PlayTime committed to disk");
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Tracking task error: {ex.Message}");
                    }
                }

                game.LastTracked = DateTime.Now;
                await ConfigManagement.SaveGameAsync(game);

                // Smart Sync Check
                // Smart Sync Check
                var gameData = await ConfigManagement.GetGameData(game);
                if (gameData?.EnableSmartSync ?? true)
                {
                    DebugConsole.WriteInfo("Checking Smart Sync status before opening window...");
                    var smartSync = new SmartSyncService();
                    var provider = await smartSync.GetEffectiveProvider(game);

                    // Hidden check with 30s threshold
                    var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.FromSeconds(30), provider);

                    if (comparison.Status == SmartSyncService.ProgressStatus.Similar)
                    {
                        DebugConsole.WriteSuccess("Smart Sync: already synced. Skipping window.");
                    }
                    else if ((comparison.Status == SmartSyncService.ProgressStatus.LocalAhead ||
                              comparison.Status == SmartSyncService.ProgressStatus.CloudNotFound) &&
                             (await ConfigManagement.LoadConfigAsync())?.Auto_Upload == true)
                    {
                        // AUTO-UPLOAD: If local is newer or cloud doesn't exist, and Auto-Upload is ON, just upload!
                        DebugConsole.WriteInfo($"Smart Sync Status: {comparison.Status}. Auto-Upload enabled -> Uploading...");

                        // We need the file list. The tracker logic has resolved it.
                        // But _trackLogic is accessible here.
                        var filesToUpload = _trackLogic?.GetUploadList();
                        if (filesToUpload != null && filesToUpload.Count > 0)
                        {
                            bool forceUpload = comparison.Status == SmartSyncService.ProgressStatus.CloudNotFound;
                            if (forceUpload)
                            {
                                DebugConsole.WriteInfo("Forcing upload because cloud save is missing.");
                            }
                            await UploadFilesAsync(filesToUpload, game, forceUpload);
                        }
                        else
                        {
                            DebugConsole.WriteWarning("Auto-Upload requested but no files in upload list.");
                        }
                    }
                    else
                    {
                        DebugConsole.WriteInfo($"Smart Sync Status: {comparison.Status}. Opening window...");
                        var vm = new SmartSyncViewModel(game, SmartSyncMode.GameExit, comparison);
                        if (OnSmartSyncRequested != null)
                        {
                            await OnSmartSyncRequested.Invoke(vm);
                        }
                    }
                }
                else
                {
                    DebugConsole.WriteWarning("Smart Sync disabled for game. Sync skipped.");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to process game exit");
            }
            finally
            {
                _trackingCancellation?.Dispose();
                _trackingCancellation = null;

                if (game != null)
                {
                    _gameProcessWatcher?.UnmarkGame(game.Name);
                }

                if (game != null)
                {
                    try
                    {
                        await RefreshPlayTimeAsync(game);
                    }
                    catch (Exception refreshEx)
                    {
                        DebugConsole.WriteWarning($"Failed to refresh PlayTime: {refreshEx.Message}");
                    }
                }
            }
        }

        private async Task RefreshPlayTimeAsync(Game game)
        {
            // PlayTime update is now properly awaited, so no retry needed
            var lastData = await ConfigManagement.GetGameData(game);
            var play = lastData?.PlayTime ?? TimeSpan.Zero;

            if (play > TimeSpan.Zero)
                GamePlayTimeText = "Play Time: " + play.ToString(@"hh\:mm\:ss");
            else
                GamePlayTimeText = "Play Time: Never";
        }



        private async Task UploadFilesAsync(List<string> files, Game game, bool force = false)
        {
            try
            {
                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig == null) return;

                var provider = config.CloudConfig;
                var rcloneInstaller = new RcloneInstaller();

                bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);
                if (!rcloneReady)
                {
                    DebugConsole.WriteWarning("Rclone not configured");
                    if (OnRcloneSetupRequired != null)
                    {
                        await OnRcloneSetupRequired.Invoke();

                        // Reload config to get updated provider settings
                        config = await ConfigManagement.LoadConfigAsync();
                        provider = config.CloudConfig;

                        // Re-check after dialog closes with updated provider
                        rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);

                        if (rcloneReady)
                        {
                            // Notify user that setup is complete and they should sync again
                            _notificationService?.Show(
                                "Cloud Setup Complete",
                                "Cloud storage configured successfully! Please click 'Sync Now' again to upload your saves.",
                                NotificationType.Success
                            );
                        }
                    }

                    if (!rcloneReady)
                    {
                        DebugConsole.WriteWarning("Rclone still not configured after prompt");
                        _notificationService?.Show(
                            "Cloud Setup Incomplete",
                            "Cloud storage setup was not completed. Please configure it in Settings to enable sync.",
                            NotificationType.Warning
                        );
                        return;
                    }
                    else
                    {
                        // Setup was completed, but we're exiting to let user click sync again
                        return;
                    }
                }

                _uploadManager = new SaveFileUploadManager(
                    rcloneInstaller,
                    _providerHelper,
                    new RcloneFileOperations(game)
                );

                _uploadManager.OnCloudConfigRequired += async () =>
                {
                    if (OnRcloneSetupRequired != null)
                    {
                        await OnRcloneSetupRequired.Invoke();
                        return true;
                    }
                    return false;
                };

                _uploadManager.OnProgressChanged += (progress) =>
                {
                    SyncStatusText = $"{progress.Status} ({progress.PercentComplete}%)";
                };

                var uploadResult = await _uploadManager.Upload(files, game, provider.Provider, CancellationToken.None, force);

                if (uploadResult.Success)
                {
                    DebugConsole.WriteSuccess($"Successfully uploaded {uploadResult.UploadedCount} files");
                    await UpdateTrackedListAsync(game);

                    // Upload analytics to Firebase after successful save upload
                    _ = Task.Run(async () => await AnalyticsService.UploadToFirebaseAsync());

                    // Notify user of successful upload
                    if (uploadResult.FailedCount == 0)
                    {
                        _notificationService?.Show(
                            "Upload Complete",
                            $"Successfully uploaded {uploadResult.UploadedCount} file(s) for {game.Name}",
                            NotificationType.Success
                        );
                    }
                    else
                    {
                        _notificationService?.Show(
                            "Upload Completed with Errors",
                            $"Uploaded {uploadResult.UploadedCount} file(s), but {uploadResult.FailedCount} file(s) failed for {game.Name}",
                            NotificationType.Warning
                        );
                    }
                }
                else
                {
                    // Notify user of upload failure
                    _notificationService?.Show(
                        "Upload Failed",
                        $"Failed to upload saves for {game.Name}",
                        NotificationType.Error
                    );
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Upload failed");

                // Notify user of exception
                _notificationService?.Show(
                    "Upload Error",
                    $"An error occurred while uploading saves for {game?.Name ?? "game"}",
                    NotificationType.Error
                );
            }
        }

        [RelayCommand]
        private async Task RefreshCloudFilesAsync()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                CloudFilesCountText = "Loading...";
                await LoadCloudFilesAsync(SelectedGame.Game);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to refresh cloud files");
                CloudFilesCountText = "Error loading files";
            }
        }

        [RelayCommand]
        private async Task DownloadSelectedFilesAsync()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                var selectedFiles = CloudFiles.Where(f => f.IsSelected).ToList();
                var game = SelectedGame.Game;

                var rcloneOps = new RcloneFileOperations(game);

                if (selectedFiles.Count == 0)
                {
                    DebugConsole.WriteWarning("No files selected");
                    return;
                }

                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig == null) return;

                // Get the remote path (handles profiles correctly)
                string remotePath = await rcloneOps.GetRemotePathAsync(config.CloudConfig.Provider, SelectedGame.Game);

                // Verify cloud save exists for this profile before attempting download
                if (!await rcloneOps.CheckCloudSaveExistsAsync(remotePath, config.CloudConfig.Provider))
                {
                    DebugConsole.WriteWarning($"No cloud save found at {remotePath}");
                    _notificationService?.Show("Download Failed", "No cloud save found for this profile.", NotificationType.Warning);
                    return;
                }

                // Extract just the relative paths (filenames) from selected files
                var selectedRelativePaths = selectedFiles.Select(f => f.Path).ToList();

                DebugConsole.WriteInfo($"Downloading {selectedRelativePaths.Count} selected files for {SelectedGame.Game.Name}");

                // Use the new selective download method
                bool success = await rcloneOps.DownloadSelectedFilesAsync(
                    remotePath,
                    SelectedGame.Game,
                    selectedRelativePaths);

                if (success)
                {
                    DebugConsole.WriteSuccess($"Successfully downloaded all selected files");
                    await UpdateTrackedListAsync(SelectedGame.Game);
                }
                else
                {
                    DebugConsole.WriteWarning("Some files failed to download");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to download selected files");
            }
        }

        [RelayCommand]
        private async Task AddFilesAsync()
        {
            try
            {
                DebugConsole.WriteInfo("=== AddFilesCommand executed ===");

                if (SelectedGame == null)
                {
                    DebugConsole.WriteWarning("No game selected");
                    return;
                }

                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow as MainWindow
                    : null;

                if (mainWindow == null)
                {
                    DebugConsole.WriteError("Could not get MainWindow reference");
                    return;
                }

                var fileDialog = new OpenFileDialog
                {
                    Title = "Select Files to Track",
                    AllowMultiple = true,
                    Directory = SelectedGame.Game.InstallDirectory
                };

                DebugConsole.WriteInfo("Opening file dialog...");
                var selectedFiles = await fileDialog.ShowAsync(mainWindow);

                if (selectedFiles != null && selectedFiles.Length > 0)
                {
                    DebugConsole.WriteInfo($"User selected {selectedFiles.Length} files");
                    await OnFilesAddedAsync(selectedFiles);
                    DebugConsole.WriteSuccess($"Added {selectedFiles.Length} file(s)");
                }
                else
                {
                    DebugConsole.WriteInfo("No files selected");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show add files dialog");
            }
        }

        public async Task OnFilesAddedAsync(string[] filePaths)
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                var game = SelectedGame.Game;

                // Use profile-aware data loader
                var data = await ConfigManagement.GetGameData(game) ?? new GameUploadData();

                foreach (var filePath in filePaths)
                {
                    if (!File.Exists(filePath)) continue;

                    string contractedPath = PathContractor.ContractPath(filePath, game.InstallDirectory);
                    if (data.Files.ContainsKey(contractedPath)) continue;

                    string checksum = await _rcloneFileOperations.GetFileChecksum(filePath);
                    var fileInfo = new FileInfo(filePath);

                    data.Files[contractedPath] = new FileChecksumRecord
                    {
                        Path = contractedPath,
                        Checksum = checksum,
                        FileSize = fileInfo.Length,
                        LastUpload = DateTime.UtcNow
                    };
                }

                data.LastUpdated = DateTime.UtcNow;
                await _rcloneFileOperations.SaveChecksumData(data, game);
                await UpdateTrackedListAsync(game);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to add files");
            }
        }

        [RelayCommand]
        private async Task RemoveSelectedFilesAsync()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                var selectedFiles = TrackedFiles.Where(f => f.IsSelected).ToList();
                if (selectedFiles.Count == 0) return;

                var game = SelectedGame.Game;

                // Use profile-aware data loader
                var data = await ConfigManagement.GetGameData(game);

                if (data == null) return;

                foreach (var fileVm in selectedFiles)
                {
                    var matchingEntry = data.Files.FirstOrDefault(kvp =>
                        kvp.Value.GetAbsolutePath(game.InstallDirectory)
                           .Equals(fileVm.AbsolutePath, StringComparison.OrdinalIgnoreCase));

                    if (!matchingEntry.Equals(default(KeyValuePair<string, FileChecksumRecord>)))
                    {
                        data.Files.Remove(matchingEntry.Key);
                    }

                    TrackedFiles.Remove(fileVm);
                }

                await _rcloneFileOperations.SaveChecksumData(data, game);
                UpdateFilesTrackedCount();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to remove files");
            }
        }

        [RelayCommand]
        private async Task OpenCloudSettingsAsync()
        {
            try
            {
                DebugConsole.WriteInfo("=== OpenCloudSettings executed ===");

                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow as MainWindow
                    : null;

                if (mainWindow == null)
                {
                    DebugConsole.WriteError("Could not get MainWindow reference");
                    return;
                }

                var viewModel = new CloudSettingsViewModel();
                var view = new SaveTracker.Views.Dialog.UC_CloudSettings
                {
                    DataContext = viewModel
                };

                var dialog = new Window
                {
                    Title = "Cloud Storage Settings",
                    Width = 500,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = view,
                    SystemDecorations = SystemDecorations.BorderOnly, // Custom chrome in UC
                    Background = Avalonia.Media.Brushes.Transparent,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
                };

                viewModel.RequestClose += () => dialog.Close();

                await dialog.ShowDialog(mainWindow);

                // Refresh cloud status after dialog closes
                if (_mainConfig != null)
                {
                    _mainConfig = await ConfigManagement.LoadConfigAsync();
                    if (_mainConfig != null)
                    {
                        _selectedProvider = _mainConfig.CloudConfig.Provider;
                        CloudStorageText = _providerHelper.GetProviderDisplayName(_selectedProvider);
                    }
                }

                DebugConsole.WriteInfo("Cloud settings closed");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open cloud settings");
            }
        }

        [RelayCommand]
        private async Task OpenBlacklistAsync()
        {
            try
            {
                DebugConsole.WriteInfo("=== OpenBlacklist executed ===");

                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow as MainWindow
                    : null;

                if (mainWindow == null)
                {
                    DebugConsole.WriteError("Could not get MainWindow reference");
                    return;
                }

                var blistEditor = new BlackListEditor
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                DebugConsole.WriteInfo("BlackListEditor instance created, showing as dialog...");
                await blistEditor.ShowDialog(mainWindow);
                DebugConsole.WriteSuccess("BlackListEditor closed");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open blacklist editor");
            }
        }

        [RelayCommand]
        private void OpenSettings()
        {
            try
            {
                DebugConsole.WriteInfo("=== OpenSettings executed ===");
                OnSettingsRequested?.Invoke();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to trigger settings dialog");
            }
        }

        private async Task UpdateTrackedListAsync(Game game)
        {
            try
            {
                var gameUploadData = await ConfigManagement.GetGameData(game);
                if (gameUploadData?.Files == null)
                {
                    TrackedFiles.Clear();
                    FilesTrackedText = "0 Files Tracked";
                    return;
                }

                TrackedFiles.Clear();
                foreach (var file in gameUploadData.Files)
                {
                    // Exclude any file that looks like a SaveTracker metadata file
                    if (!file.Key.Contains(".savetracker", StringComparison.OrdinalIgnoreCase))
                    {
                        TrackedFiles.Add(new TrackedFileViewModel(file.Value, game, gameUploadData.LastSyncStatus));
                    }
                }

                UpdateFilesTrackedCount();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update tracked list");
            }
        }

        private async Task LoadCloudFilesAsync(Game game)
        {
            try
            {
                CloudFiles.Clear();

                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig == null) return;

                var rcloneInstaller = new RcloneInstaller();
                bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(config.CloudConfig.Provider);
                if (!rcloneReady) return;

                var executor = new RcloneExecutor();
                var configManager = new RcloneConfigManager();
                var configPath = await configManager.ResolveConfigPath(config.CloudConfig.Provider);
                var remoteName = _providerHelper.GetProviderConfigName(config.CloudConfig.Provider);
                var sanitizedGameName = SanitizeGameName(game.Name);
                var remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}/{sanitizedGameName}";

                var result = await executor.ExecuteRcloneCommand(
                    $"lsjson \"{remotePath}\" --recursive --config \"{configPath}\"",
                    TimeSpan.FromSeconds(30),
                    hideWindow: true,
                    allowedExitCodes: new[] { 3 }
                );

                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    try
                    {
                        var files = System.Text.Json.JsonSerializer.Deserialize<List<RcloneFileInfo>>(result.Output, JsonHelper.GetOptions());
                        if (files != null)
                        {
                            var validFiles = files.Where(f => !f.IsDir && !f.Name.StartsWith(".")).ToList();
                            long totalSize = 0;

                            CloudFiles.Clear();
                            foreach (var file in validFiles)
                            {
                                CloudFiles.Add(new CloudFileViewModel(file));
                                totalSize += file.Size;
                            }

                            CloudFilesCountText = $"{validFiles.Count} files in cloud • {Misc.FormatFileSize(totalSize)}";
                        }
                        else
                        {
                            CloudFilesCountText = "0 files in cloud";
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"Failed to parse cloud files JSON: {ex.Message}");
                        DebugConsole.WriteDebug($"Raw Output (Exit: {result.ExitCode}): {result.Output}");
                        if (!string.IsNullOrEmpty(result.Error))
                        {
                            DebugConsole.WriteError($"Rclone Error Output: {result.Error}");
                        }
                        CloudFilesCountText = "Error loading files";

                        // DIAGNOSTIC CHECKS
                        if (!string.IsNullOrEmpty(result.Error) && result.Error.Contains("directory not found"))
                        {
                            DebugConsole.WriteWarning("Running diagnostics on cloud path...");
                            try
                            {
                                // Check if base folder exists
                                var diagnosticPath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";
                                DebugConsole.WriteInfo($"Checking parent folder: {diagnosticPath}");
                                var diagResult = await executor.ExecuteRcloneCommand(
                                    $"lsd \"{diagnosticPath}\" --config \"{configPath}\"",
                                    TimeSpan.FromSeconds(10),
                                    hideWindow: true
                                );
                                if (diagResult.Success)
                                {
                                    DebugConsole.WriteSuccess($"Parent folder exists. Output:\n{diagResult.Output}");

                                    // SMART RESOLUTION
                                    try
                                    {
                                        var lines = diagResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                        string bestMatch = null;
                                        double bestScore = 0.0;

                                        foreach (var line in lines)
                                        {
                                            // Format: "          -1 2025-12-09 23:22:48        -1 Marvel's Spider-Man 2"
                                            // We need to extract the name. Rclone lsd output usually has fixed width columns or tab separated?
                                            // Actually it seems to be: size (10 chars), date (10), time (8), count (9), name (rest)
                                            // Or simplified: it ends with the name.

                                            // A robust way is to split by spaces and take likely the last part, but names have spaces.
                                            // Rclone lsd output typically:
                                            // <size> <date> <time> <count> <name>
                                            //      0 2025-12-10 12:00:00        -1 Folder Name

                                            var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                            if (parts.Length >= 5)
                                            {
                                                // Reconstruct name from index 4 onwards
                                                string folderName = string.Join(" ", parts.Skip(4));
                                                // Check similarity
                                                double score = SaveTracker.Resources.HELPERS.StringUtils.CalculateSimilarity(game.Name, folderName);

                                                if (score > bestScore)
                                                {
                                                    bestScore = score;
                                                    bestMatch = folderName;
                                                }
                                            }
                                        }

                                        if (bestMatch != null && bestScore > 0.4) // Threshold
                                        {
                                            DebugConsole.WriteSuccess($"Smart Resolution: Found potential match '{bestMatch}' (Score: {bestScore:F2})");

                                            // Retry with new path
                                            var newRemotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}/{bestMatch}";
                                            DebugConsole.WriteInfo($"Retrying with corrected path: {newRemotePath}");

                                            var retryResult = await executor.ExecuteRcloneCommand(
                                                $"lsjson \"{newRemotePath}\" --recursive --config \"{configPath}\"",
                                                TimeSpan.FromSeconds(30),
                                                hideWindow: true
                                            );

                                            if (retryResult.Success && !string.IsNullOrWhiteSpace(retryResult.Output))
                                            {
                                                // SUCCESS! Process this instead
                                                result = retryResult;
                                                // Clear global error flag if we recovered? 
                                                // Actually we just proceed to parse `result` which is now the good one.
                                                // We need to re-enter the success block.
                                                // Since we are inside the 'directory not found' block which is inside the catch/error block of the MAIN logic...
                                                // We can't easily "jump" back. We must copy the parsing logic here or extract it.

                                                // For now, let's just parse it here to fix the UI
                                                var files = System.Text.Json.JsonSerializer.Deserialize<List<RcloneFileInfo>>(result.Output, JsonHelper.GetOptions());
                                                if (files != null)
                                                {
                                                    var validFiles = files.Where(f => !f.IsDir && !f.Name.StartsWith(".")).ToList();
                                                    long totalSize = 0;

                                                    CloudFiles.Clear();
                                                    foreach (var file in validFiles)
                                                    {
                                                        CloudFiles.Add(new CloudFileViewModel(file));
                                                        totalSize += file.Size;
                                                    }
                                                    CloudFilesCountText = $"{validFiles.Count} files in cloud • {Misc.FormatFileSize(totalSize)}";
                                                    DebugConsole.WriteSuccess("Smart Resolution: Successfully loaded files from corrected path.");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception resolutionEx)
                                    {
                                        DebugConsole.WriteWarning($"Smart resolution failed: {resolutionEx.Message}");
                                    }
                                }
                                else
                                {
                                    DebugConsole.WriteWarning($"Parent folder check failed. Checking root {remoteName}: ...");
                                    var rootResult = await executor.ExecuteRcloneCommand(
                                        $"lsd \"{remoteName}:\" --config \"{configPath}\"",
                                        TimeSpan.FromSeconds(10),
                                        hideWindow: true
                                    );
                                    if (rootResult.Success) DebugConsole.WriteSuccess($"Root check passed. Output:\n{rootResult.Output}");
                                    else DebugConsole.WriteError($"Root check failed: {rootResult.Error}");
                                }
                            }
                            catch { }
                        }
                    }
                }
                else
                {
                    CloudFilesCountText = "0 files in cloud";
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load cloud files");
                CloudFilesCountText = "Error loading files";
            }
        }

        private void UpdateGamesCount()
        {
            GamesCountText = $"{Games.Count} games tracked";
        }

        private void UpdateFilesTrackedCount()
        {
            FilesTrackedText = $"{TrackedFiles.Count} Files Tracked";
        }

        private string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            return invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_')).Trim();
        }

        // ========== GAME PROCESS WATCHER ==========
        private async void OnGameAutoDetected(Game game, int processId)
        {
            try
            {
                // Check if watcher is allowed for this game
                var gameData = await ConfigManagement.GetGameData(game);
                if (gameData != null && !gameData.AllowGameWatcher)
                {
                    DebugConsole.WriteInfo($"Auto-detection ignored for {game.Name} (disabled in settings)");
                    return;
                }

                DebugConsole.WriteSuccess($"Auto-detected running game: {game.Name} (PID: {processId})");

                // Record game launch in analytics (privacy-focused, opt-in)
                _ = AnalyticsService.RecordGameLaunchAsync(game.Name, game.ExecutablePath);

                // Find the corresponding GameViewModel
                var gameViewModel = Games.FirstOrDefault(g => g.Game.Name == game.Name);
                if (gameViewModel == null)
                {
                    DebugConsole.WriteWarning($"Game '{game.Name}' not found in Games collection");
                    return;
                }

                // Select the game in the UI
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SelectedGame = gameViewModel;
                });

                // Start tracking the detected process
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (process != null && !process.HasExited)
                    {
                        _trackLogic = new SaveFileTrackerManager();
                        _trackingCancellation = new CancellationTokenSource();

                        process.EnableRaisingEvents = true;
                        IsLaunchEnabled = false;
                        SyncStatusText = "Auto-tracking...";

                        // Mark game as tracked to prevent duplicate auto-detection
                        _gameProcessWatcher?.MarkGameAsTracked(game.Name);

                        _ = TrackGameProcessAsync(process, _trackingCancellation.Token);

                        // Notify user that tracking has started
                        _notificationService?.Show(
                            "Game Tracking Started",
                            $"Now tracking save files for {game.Name}",
                            NotificationType.Information
                        );

                        // [Linux Specific] Detect & Save Prefix immediately if missing
                        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                        {
                            var prefixData = await ConfigManagement.GetGameData(game);
                            if (string.IsNullOrEmpty(prefixData?.DetectedPrefix))
                            {
                                try
                                {
                                    DebugConsole.WriteInfo("Auto-detected game has no stored prefix. Attempting to detect...");
                                    var tracker = SaveTracker.Resources.LOGIC.Tracking.GameProcessTrackerFactory.Create();
                                    var pInfo = new SaveTracker.Resources.LOGIC.Tracking.ProcessInfo
                                    {
                                        Id = processId,
                                        Name = game.Name,
                                        ExecutablePath = game.ExecutablePath
                                    };

                                    string prefix = await tracker.DetectGamePrefix(pInfo);

                                    if (!string.IsNullOrEmpty(prefix))
                                    {
                                        DebugConsole.WriteSuccess($"Auto-detected Prefix via Process ID: {prefix}");
                                        if (prefixData == null) prefixData = new SaveTracker.Resources.Logic.RecloneManagement.GameUploadData();

                                        prefixData.DetectedPrefix = prefix;
                                        await ConfigManagement.SaveGameData(game, prefixData);
                                        DebugConsole.WriteInfo("Prefix persisted to game data.");
                                    }
                                    else
                                    {
                                        DebugConsole.WriteWarning("Could not auto-detect prefix for this process.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    DebugConsole.WriteWarning($"Prefix detection error: {ex.Message}");
                                }
                            }
                        }

                        // Check Smart Sync for auto-hooked games
                        var gd = await ConfigManagement.GetGameData(game);
                        if (gd?.EnableSmartSync ?? true)
                        {
                            _ = CheckSmartSyncForAutoHookAsync(game, process);
                        }

                        process.Exited += async (s, e) =>
                        {
                            try
                            {
                                await OnGameExitedAsync(process, game);
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteException(ex, "Error in auto-detected process exit handler");
                            }
                            finally
                            {
                                IsLaunchEnabled = true;
                                SyncStatusText = "Idle";
                                // Unmark game when tracking stops
                                _gameProcessWatcher?.UnmarkGame(game.Name);
                            }
                        };

                        if (process.HasExited)
                        {
                            await OnGameExitedAsync(process, game);
                            IsLaunchEnabled = true;
                            SyncStatusText = "Idle";
                            _gameProcessWatcher?.UnmarkGame(game.Name);
                        }
                    }
                }
                catch (ArgumentException)
                {
                    DebugConsole.WriteWarning($"Process {processId} no longer exists");
                    _gameProcessWatcher?.UnmarkGame(game.Name);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to handle auto-detected game");
                _gameProcessWatcher?.UnmarkGame(game.Name);
            }
        }

        // ========== AUTO-UPDATER COMMANDS ==========

        [RelayCommand]
        private async Task CheckForUpdatesAsync()
        {
            if (IsCheckingForUpdates) return;

            try
            {
                IsCheckingForUpdates = true;
                DebugConsole.WriteInfo("Checking for updates...");

                var updateChecker = new UpdateChecker();
                _latestUpdateInfo = await updateChecker.CheckForUpdatesAsync();

                if (_latestUpdateInfo.IsUpdateAvailable)
                {
                    UpdateAvailable = true;
                    UpdateVersion = _latestUpdateInfo.Version;
                    UpdateVersion = _latestUpdateInfo.Version;
                    DebugConsole.WriteSuccess($"Update available: v{_latestUpdateInfo.Version}");

                    // Notify UI to show dialog
                    OnUpdateAvailable?.Invoke(_latestUpdateInfo);

                    // Update last check time
                    if (_mainConfig != null)
                    {
                        _mainConfig.LastUpdateCheck = DateTime.Now;
                        await ConfigManagement.SaveConfigAsync(_mainConfig);
                    }
                }
                else
                {
                    UpdateAvailable = false;
                    UpdateVersion = string.Empty;
                    DebugConsole.WriteInfo("No updates available");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check for updates");
                UpdateAvailable = false;
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        [RelayCommand]
        private async Task DownloadAndInstallUpdateAsync()
        {
            if (_latestUpdateInfo == null || !_latestUpdateInfo.IsUpdateAvailable)
            {
                DebugConsole.WriteWarning("No update available to install");
                return;
            }

            try
            {
                DebugConsole.WriteSection($"Installing Update v{_latestUpdateInfo.Version}");

                // Download the update
                var downloader = new UpdateDownloader();
                downloader.DownloadProgressChanged += (sender, progress) =>
                {
                    DebugConsole.WriteInfo($"Download progress: {progress}%");
                };

                string downloadedFilePath = await downloader.DownloadUpdateAsync(_latestUpdateInfo.DownloadUrl);

                // Install the update (this will exit the application)
                var installer = new UpdateInstaller();
                await installer.InstallUpdateAsync(downloadedFilePath);

                // This line will never be reached as the app will exit
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to download and install update");
            }
        }

        [RelayCommand]
        private void ReportIssue()
        {
            try
            {
                var url = "https://github.com/KrachDev/SaveTrackerDesktop/issues/new";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                DebugConsole.WriteInfo("Opened GitHub issues page");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open issues page");
            }
        }

        #region Smart Sync Helper Methods

        private async Task CheckSmartSyncForAutoHookAsync(Game game, Process process)
        {
            try
            {
                // Load fresh config/data
                var gameData = await ConfigManagement.GetGameData(game);
                if (!(gameData?.EnableSmartSync ?? true)) return;

                var smartSync = new SmartSyncService();
                var provider = await smartSync.GetEffectiveProvider(game);

                var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.Zero, provider);

                if (comparison.Status == SmartSyncService.ProgressStatus.CloudAhead)
                {
                    DebugConsole.WriteInfo($"Auto-Hook: Cloud save is newer ({comparison.Difference}). Opening Smart Sync window...");

                    // Use ManualSync mode as game is running.
                    var vm = new SmartSyncViewModel(game, SmartSyncMode.ManualSync);

                    if (OnSmartSyncRequested != null)
                    {
                        await OnSmartSyncRequested.Invoke(vm);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Auto-hook Smart Sync check failed");
            }
        }

        private async Task<bool?> ShowSmartSyncPromptAsync(
            string title,
            string message,
            string yesButtonText,
            string noButtonText)
        {
            try
            {
                // Get the main window reference
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                var box = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                    ContentTitle = title,
                    ContentMessage = message,
                    Icon = MsBox.Avalonia.Enums.Icon.Question,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                });

                // Show with parent window to ensure it appears
                var result = mainWindow != null
                    ? await box.ShowWindowDialogAsync(mainWindow)
                    : await box.ShowAsync();

                return result == MsBox.Avalonia.Enums.ButtonResult.Yes;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show Smart Sync prompt");
                return null;
            }
        }

        /// <summary>
        /// Determines if a file should be filtered out from sync operations
        /// (e.g., browser files, temp files, etc.)
        /// </summary>
        private bool ShouldFilterFile(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return true;

            string normalizedPath = absolutePath.Replace('\\', '/').ToLowerInvariant();

            // Filter out browser-related files
            string[] browserPatterns = new[]
            {
                "/appdata/local/google/chrome/",
                "/appdata/local/microsoft/edge/",
                "/appdata/local/bravesoftware/brave-browser/",
                "/appdata/local/mozilla/firefox/",
                "/appdata/roaming/opera",
                "/.config/google-chrome/",
                "/.config/brave/",
                "/.mozilla/firefox/",
                "/library/application support/google/chrome/",
                "/library/application support/brave",
            };

            foreach (var pattern in browserPatterns)
            {
                if (normalizedPath.Contains(pattern))
                {
                    return true;
                }
            }

            // Filter out temp files
            if (normalizedPath.Contains("/temp/") ||
                normalizedPath.Contains("/tmp/") ||
                normalizedPath.EndsWith(".tmp") ||
                normalizedPath.EndsWith(".temp"))
            {
                return true;
            }

            return false;
        }

        private async Task DownloadCloudSaveAsync(Game game)
        {
            try
            {
                DebugConsole.WriteInfo("Downloading cloud save (Smart Sync)...");

                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig == null)
                {
                    DebugConsole.WriteError("Cloud configuration not found");
                    return;
                }

                var rcloneOps = new RcloneFileOperations();
                var remotePath = await rcloneOps.GetRemotePathAsync(config.CloudConfig.Provider, game);

                if (!await rcloneOps.CheckCloudSaveExistsAsync(remotePath, config.CloudConfig.Provider))
                {
                    DebugConsole.WriteWarning($"No cloud save found at {remotePath} - skipping download");
                    return;
                }

                bool success = await rcloneOps.DownloadWithChecksumAsync(remotePath, game, config.CloudConfig.Provider);

                if (success)
                {
                    DebugConsole.WriteSuccess("Cloud save downloaded successfully");
                }
                else
                {
                    DebugConsole.WriteError("Failed to download cloud save");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to download cloud save");
            }
        }

        private static string FormatTimeSpan(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
                time = time.Negate();

            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        #endregion
        private async Task UpdateCloudGameCacheAsync()
        {
            try
            {
                DebugConsole.WriteInfo("Starting background cloud game cache update...");
                // Check if Rclone is ready
                var rcloneInstaller = new RcloneInstaller();
                var config = await ConfigManagement.LoadConfigAsync();

                CloudProvider provider = CloudProvider.GoogleDrive;
                if (config?.CloudConfig != null)
                {
                    provider = config.CloudConfig.Provider;
                }

                var checkConfig = new CloudConfig { Provider = provider };

                if (await rcloneInstaller.RcloneCheckAsync(provider))
                {
                    // Fetch list
                    var games = await _rcloneFileOperations.ListCloudGameFolders(provider);
                    if (games != null && games.Count > 0)
                    {
                        DebugConsole.WriteSuccess($"Cloud cache updated: Found {games.Count} games.");
                        await ConfigManagement.SaveCloudGamesAsync(games);
                    }
                    else
                    {
                        DebugConsole.WriteInfo("Cloud cache update: No games found or empty list.");
                    }
                }
                else
                {
                    DebugConsole.WriteInfo("Skipping cloud cache update: Rclone not configured.");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update cloud game cache");
            }
        }

    }
}

