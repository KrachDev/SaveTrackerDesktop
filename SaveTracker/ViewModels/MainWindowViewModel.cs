using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.Logic.AutoUpdater;
using SaveTracker.Resources.LOGIC;

using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Views;
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
        private bool _isSyncEnabled;

        partial void OnIsLaunchEnabledChanged(bool value)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                LaunchGameCommand.NotifyCanExecuteChanged();
            });
        }

        partial void OnIsSyncEnabledChanged(bool value)
        {
            SyncNowCommand.NotifyCanExecuteChanged();
        }

        [ObservableProperty]
        private bool _isSyncing;

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

        private UpdateInfo? _latestUpdateInfo;

        private Config? _mainConfig;
        private CloudProvider _selectedProvider;
        private Task? _trackingTask;

        private static string GetAppVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version;
            return version != null ? $"SaveTracker Desktop v{version.Major}.{version.Minor}.{version.Build}" : "SaveTracker Desktop";
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

        // ========== DEBUG HELPER METHODS ==========
        public int GetAddGameRequestedSubscriberCount() => OnAddGameRequested?.GetInvocationList().Length ?? 0;
        public int GetAddFilesRequestedSubscriberCount() => OnAddFilesRequested?.GetInvocationList().Length ?? 0;
        public int GetCloudSettingsRequestedSubscriberCount() => OnCloudSettingsRequested?.GetInvocationList().Length ?? 0;
        public int GetBlacklistRequestedSubscriberCount() => OnBlacklistRequested?.GetInvocationList().Length ?? 0;
        public int GetRcloneSetupRequiredSubscriberCount() => OnRcloneSetupRequired?.GetInvocationList().Length ?? 0;

        public MainWindowViewModel()
        {
            _configManagement = new ConfigManagement();
            _rcloneFileOperations = new RcloneFileOperations();
            _providerHelper = new CloudProviderHelper();
            InitializeGameSettingsProviders();  // ← ADD THIS LINE

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

                DebugConsole.WriteInfo("Is Admin: " + AdminHelper.IsAdministrator().Result.ToString());

                // Check for updates on startup if enabled
                if (_mainConfig?.CheckForUpdatesOnStartup ?? true)
                {
                    _ = CheckForUpdatesAsync();
                }
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
                if (gamelist != null)
                {
                    Games.Clear();
                    foreach (var game in gamelist)
                    {
                        // Skip deleted games - don't load them into the UI
                        if (game.IsDeleted)
                        {
                            DebugConsole.WriteInfo($"Skipping deleted game: {game.Name}");
                            continue;
                        }

                        try
                        {
                            Games.Add(new GameViewModel(game));
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteException(ex, $"Failed to add game: {game?.Name ?? "Unknown"}");
                        }
                    }
                }

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
                _ = LoadGameDetailsAsync(value);
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
                IsSyncEnabled = (bool)await ConfigManagement.HasData(game);

                // Load tracked files
                await UpdateTrackedListAsync(game);
                // Load game-specific settings
                await LoadGameSettingsAsync(game);  // ← ADD THIS LINE
                // Clear cloud files (load on demand when tab is selected)
                CloudFiles.Clear();
                CloudFilesCountText = "Click refresh to load";

                // Notify commands to update
                LaunchGameCommand.NotifyCanExecuteChanged();
                SyncNowCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Error loading game details");
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
        public async Task OnGameAddedAsync(Game newGame)
        {
            try
            {
                Games.Add(new GameViewModel(newGame));
                await ConfigManagement.SaveGameAsync(newGame);
                DebugConsole.WriteSuccess($"Game added: {newGame.Name}");
                UpdateGamesCount();

                // Update watcher with new games list
                var updatedGamesList = await ConfigManagement.LoadAllGamesAsync();
                _gameProcessWatcher?.UpdateGamesList(updatedGamesList);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to save new game");
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

                    if (!rcloneReady)
                    {
                        DebugConsole.WriteWarning("Rclone still not configured - proceeding without cloud check");
                    }
                }

                // Smart Sync: Check cloud vs local PlayTime before launching
                DebugConsole.WriteInfo($"Smart Sync check: rcloneReady={rcloneReady}");
                
                if (rcloneReady)
                {
                    // Check if Smart Sync is enabled for this game
                    var gameData = await ConfigManagement.GetGameData(game);
                    bool smartSyncEnabled = gameData?.EnableSmartSync ?? true;
                    
                    if (smartSyncEnabled)
                    {
                        var smartSync = new SmartSyncService();
                        var comparison = await smartSync.CompareProgressAsync(
                            game,
                            TimeSpan.FromMinutes(10),
                            effectiveProvider
                        );

                        DebugConsole.WriteInfo($"Smart Sync Result: {comparison.Status}");
                        DebugConsole.WriteInfo($"Local PlayTime: {FormatTimeSpan(comparison.LocalPlayTime)}");
                        DebugConsole.WriteInfo($"Cloud PlayTime: {FormatTimeSpan(comparison.CloudPlayTime)}");

                        // If cloud is ahead, offer to download before launching
                        if (comparison.Status == SmartSyncService.ProgressStatus.CloudAhead)
                        {
                            DebugConsole.WriteWarning("Cloud save is ahead! Prompting before launch...");
                            var result = await ShowSmartSyncPromptAsync(
                                "Smart Sync: Download Before Launch?",
                                $"Your cloud save has more playtime ({FormatTimeSpan(comparison.CloudPlayTime)}) than local ({FormatTimeSpan(comparison.LocalPlayTime)}).\n\n" +
                                $"Difference: {FormatTimeSpan(comparison.Difference)}\n\n" +
                                "Download cloud save before launching?",
                                "Download & Launch",
                                "Launch with Local"
                            );

                            if (result == true)
                            {
                                DebugConsole.WriteInfo("User chose to download cloud save before launch");
                                await DownloadCloudSaveAsync(game);
                            }
                            else
                            {
                                DebugConsole.WriteInfo("User chose to launch with local save");
                            }
                        }
                        else if (comparison.Status == SmartSyncService.ProgressStatus.CloudNotFound)
                        {
                            DebugConsole.WriteInfo("No cloud save found - proceeding with launch");
                        }
                        else
                        {
                            DebugConsole.WriteInfo($"Smart Sync: {comparison.Status} - proceeding with launch");
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

                _trackLogic = new SaveFileTrackerManager();
                _trackingCancellation = new CancellationTokenSource();

                string exeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
                var existingProcesses = Process.GetProcessesByName(exeName);
                Process? targetProcess = null;

                if (existingProcesses.Length > 0)
                {
                    targetProcess = existingProcesses[0];
                    DebugConsole.WriteInfo($"Found existing process: {game.Name} (PID: {targetProcess.Id})");
                }
                else
                {
                    DebugConsole.WriteInfo($"Launching {game.Name}...");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = game.ExecutablePath,
                        WorkingDirectory = game.InstallDirectory,
                        UseShellExecute = true
                    };

                    targetProcess = Process.Start(startInfo);

                    if (targetProcess != null)
                    {
                        DebugConsole.WriteSuccess($"{game.Name} started (PID: {targetProcess.Id})");
                    }
                }

                if (targetProcess != null)
                {
                    targetProcess.EnableRaisingEvents = true;
                    IsLaunchEnabled = false;
                    SyncStatusText = "Tracking...";

                    // Mark game as tracked to prevent duplicate auto-detection
                    _gameProcessWatcher?.MarkGameAsTracked(game.Name);

                    _trackingTask = TrackGameProcessAsync(targetProcess, _trackingCancellation.Token);

                    targetProcess.Exited += async (s, e) =>
                    {
                        try
                        {
                            await OnGameExitedAsync(targetProcess);
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteException(ex, "Error in process exit handler");
                        }
                        finally
                        {
                            IsLaunchEnabled = true;
                            SyncStatusText = "Idle";
                            // Unmark game when tracking stops
                            _gameProcessWatcher?.UnmarkGame(game.Name);
                        }
                    };

                    if (targetProcess.HasExited)
                    {
                        await OnGameExitedAsync(targetProcess);
                        IsLaunchEnabled = true;
                        SyncStatusText = "Idle";
                        _gameProcessWatcher?.UnmarkGame(game.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to launch game");
                IsLaunchEnabled = true;
                SyncStatusText = "Idle";
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

        private async Task OnGameExitedAsync(Process process)
        {
            var game = SelectedGame?.Game; // capture once for use in try/finally
            try
            {
                if (game == null) return;

                DebugConsole.WriteInfo($"{game.Name} closed. Exit code: {process.ExitCode}");

                _trackingCancellation?.Cancel();

                // Wait for tracking session to complete and resolve final file list
                if (_trackingTask != null)
                {
                    try
                    {
                        await _trackingTask;
                        DebugConsole.WriteInfo("Tracking session completed");
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Tracking task error: {ex.Message}");
                    }
                }

                game.LastTracked = DateTime.Now;
                await ConfigManagement.SaveGameAsync(game);

                var trackedFiles = _trackLogic?.GetUploadList();

                if (trackedFiles == null || trackedFiles.Count == 0)
                {
                    DebugConsole.WriteInfo("No files were tracked during gameplay - performing checksum-only upload");
                    trackedFiles = new List<string>(); // trigger checksum-only upload
                }

                DebugConsole.WriteInfo($"Processing {trackedFiles.Count} tracked files (checksum will always be uploaded)...");

                // Debug: Log upload eligibility
                DebugConsole.WriteInfo($"Upload check: CanUpload={CanUpload}, AreUploadsEnabledForGame()={AreUploadsEnabledForGame()}, _currentGameUploadData is null: {_currentGameUploadData == null}");

                if (CanUpload && AreUploadsEnabledForGame())
                {
                    DebugConsole.WriteInfo("Starting upload process...");
                    await UploadFilesAsync(trackedFiles, game);
                }
                else
                {
                    if (!CanUpload)
                    {
                        DebugConsole.WriteWarning("Upload skipped: CanUpload is false");
                    }
                    if (!AreUploadsEnabledForGame())
                    {
                        DebugConsole.WriteWarning("Upload skipped: Uploads are disabled for this game");
                    }
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

                // Unmark game in watcher
                if (SelectedGame?.Game != null)
                {
                    _gameProcessWatcher?.UnmarkGame(SelectedGame.Game.Name);
                }

                // Refresh PlayTime in UI after tracking stops (allow a brief moment for async save to complete)
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
            // Retry a few times to account for async save completion in TrackingSession.Stop()
            const int maxAttempts = 5;
            const int delayMs = 200;

            GameUploadData? lastData = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                lastData = await ConfigManagement.GetGameData(game);
                if (lastData != null && lastData.PlayTime > TimeSpan.Zero)
                    break;
                await Task.Delay(delayMs);
            }

            var play = lastData?.PlayTime ?? TimeSpan.Zero;
            if (play > TimeSpan.Zero)
                GamePlayTimeText = "Play Time: " + play.ToString(@"hh\:mm\:ss");
            else
                GamePlayTimeText = "Play Time: Never";
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
                
                // Smart Sync: Check if cloud is ahead before uploading
                DebugConsole.WriteInfo("=== Smart Sync Check ===");
                
                // Check if Smart Sync is enabled for this game
                var gameData = await ConfigManagement.GetGameData(game);
                bool smartSyncEnabled = gameData?.EnableSmartSync ?? true;
                
                if (smartSyncEnabled)
                {
                    var smartSync = new SmartSyncService();
                    var comparison = await smartSync.CompareProgressAsync(
                        game,
                        TimeSpan.FromMinutes(10)
                    );

                    DebugConsole.WriteInfo($"Smart Sync Result: {comparison.Status}");
                    DebugConsole.WriteInfo($"Local PlayTime: {FormatTimeSpan(comparison.LocalPlayTime)}");
                    DebugConsole.WriteInfo($"Cloud PlayTime: {FormatTimeSpan(comparison.CloudPlayTime)}");
                    DebugConsole.WriteInfo($"Difference: {FormatTimeSpan(comparison.Difference)}");
                    DebugConsole.WriteInfo($"Message: {comparison.Message}");

                    if (comparison.Status == SmartSyncService.ProgressStatus.CloudAhead)
                    {
                        DebugConsole.WriteWarning("Cloud is ahead! Showing prompt to user...");
                        var result = await ShowSmartSyncPromptAsync(
                            "Smart Sync: Download Instead?",
                            $"Your cloud save has more playtime ({FormatTimeSpan(comparison.CloudPlayTime)}) than local ({FormatTimeSpan(comparison.LocalPlayTime)}).\n\n" +
                            $"Difference: {FormatTimeSpan(comparison.Difference)}\n\n" +
                            "Download cloud save instead of uploading?",
                            "Download Cloud Save",
                            "Upload Local Save"
                        );

                        if (result == true)
                        {
                            DebugConsole.WriteInfo("User chose to download cloud save");
                            await DownloadCloudSaveAsync(game);
                            return;
                        }
                        else
                        {
                            DebugConsole.WriteInfo("User chose to upload local save");
                        }
                    }
                }
                else
                {
                    DebugConsole.WriteInfo("Smart Sync disabled for this game - skipping check");
                }

                var gameUploadData = await ConfigManagement.GetGameData(game);

                if (gameUploadData?.Files == null || gameUploadData.Files.Count == 0)
                {
                    DebugConsole.WriteWarning("No tracked files found for this game");
                    return;
                }

                var trackedFiles = new List<string>();
                foreach (var file in gameUploadData.Files)
                {
                    string absolutePath = file.Value.GetAbsolutePath(game.InstallDirectory);
                    if (File.Exists(absolutePath))
                    {
                        trackedFiles.Add(absolutePath);
                    }
                }

                if (trackedFiles.Count > 0)
                {
                    await UploadFilesAsync(trackedFiles, game);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to sync game files");
            }
            finally
            {
                IsSyncing = false;
                SyncButtonText = "☁️ Sync Now";
                SyncStatusText = "Idle";
            }
        }

        private async Task UploadFilesAsync(List<string> files, Game game)
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
                        // Re-check after dialog closes
                        rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);
                    }

                    if (!rcloneReady)
                    {
                        DebugConsole.WriteWarning("Rclone still not configured after prompt");
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

                var uploadResult = await _uploadManager.Upload(files, game, provider.Provider, CancellationToken.None);

                if (uploadResult.Success)
                {
                    DebugConsole.WriteSuccess($"Successfully uploaded {uploadResult.UploadedCount} files");
                    await UpdateTrackedListAsync(game);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Upload failed");
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

                var remoteName = _providerHelper.GetProviderConfigName(config.CloudConfig.Provider);
                var sanitizedGameName = SanitizeGameName(SelectedGame.Game.Name);

                // Build the remote path to the game's cloud folder
                string remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}/{sanitizedGameName}";

                // Extract just the relative paths (filenames) from selected files
                var selectedRelativePaths = selectedFiles.Select(f => f.Name).ToList();

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
                string gameDataFile = game.GetGameDataFile();

                GameUploadData data;
                if (File.Exists(gameDataFile))
                {
                    string json = await File.ReadAllTextAsync(gameDataFile);
                    data = System.Text.Json.JsonSerializer.Deserialize<GameUploadData>(json) ?? new GameUploadData();
                }
                else
                {
                    data = new GameUploadData();
                }

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
                string gameDataFile = game.GetGameDataFile();

                if (!File.Exists(gameDataFile)) return;

                string json = await File.ReadAllTextAsync(gameDataFile);
                var data = System.Text.Json.JsonSerializer.Deserialize<GameUploadData>(json);

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
                    if (!file.Key.Contains(SaveFileUploadManager.ChecksumFilename))
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
                    TimeSpan.FromSeconds(30)
                );

                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    var files = System.Text.Json.JsonSerializer.Deserialize<List<RcloneFileInfo>>(result.Output);
                    if (files != null)
                    {
                        var validFiles = files.Where(f => !f.IsDir && !f.Name.StartsWith(".")).ToList();
                        long totalSize = 0;

                        foreach (var file in validFiles)
                        {
                            CloudFiles.Add(new CloudFileViewModel(file));
                            totalSize += file.Size;
                        }

                        CloudFilesCountText = $"{validFiles.Count} files in cloud • {Misc.FormatFileSize(totalSize)}";
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

                // Smart Sync: Check if cloud save is ahead
                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig != null)
                {
                    // Check if Smart Sync is enabled for this game
                    var gameSettings = await ConfigManagement.GetGameData(game);
                    bool smartSyncEnabled = gameSettings?.EnableSmartSync ?? true;
                    
                    if (smartSyncEnabled)
                    {
                        try
                        {
                            var effectiveProvider = GetEffectiveProviderForGame();
                            var smartSync = new SmartSyncService();
                            var comparison = await smartSync.CompareProgressAsync(
                                game,
                                TimeSpan.FromMinutes(10),
                                effectiveProvider
                            );

                            DebugConsole.WriteInfo($"Smart Sync (Auto-Hook) Result: {comparison.Status}");

                            if (comparison.Status == SmartSyncService.ProgressStatus.CloudAhead)
                            {
                                DebugConsole.WriteWarning("Cloud save is ahead during auto-hook! Prompting user...");
                                
                                var result = await ShowSmartSyncPromptAsync(
                                    "Smart Sync: Restart Game with Cloud Save?",
                                    $"Your cloud save has more playtime ({FormatTimeSpan(comparison.CloudPlayTime)}) than local ({FormatTimeSpan(comparison.LocalPlayTime)}).\n\n" +
                                    $"Difference: {FormatTimeSpan(comparison.Difference)}\n\n" +
                                    "The game will close, sync cloud save, and relaunch automatically.\n\n" +
                                    "Restart with cloud save?",
                                    "Yes, Restart & Sync",
                                    "No, Use Local"
                                );

                                if (result == true)
                                {
                                    DebugConsole.WriteInfo("User chose to restart with cloud save");
                                    
                                    // Close the game
                                    try
                                    {
                                        var process = Process.GetProcessById(processId);
                                        if (process != null && !process.HasExited)
                                        {
                                            DebugConsole.WriteInfo($"Closing game process (PID: {processId})...");
                                            process.Kill();
                                            process.WaitForExit(5000); // Wait up to 5 seconds
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugConsole.WriteException(ex, "Failed to close game process");
                                    }

                                    // Download cloud save
                                    await DownloadCloudSaveAsync(game);

                                    // Relaunch the game
                                    if (File.Exists(game.ExecutablePath))
                                    {
                                        DebugConsole.WriteInfo("Relaunching game with cloud save...");
                                        Process.Start(game.ExecutablePath);
                                        
                                        // Don't continue with tracking the old process
                                        return;
                                    }
                                    else
                                    {
                                        DebugConsole.WriteError("Cannot relaunch - executable not found");
                                        return;
                                    }
                                }
                                else
                                {
                                    DebugConsole.WriteInfo("User chose to continue with local save");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteException(ex, "Smart Sync check failed during auto-hook");
                        }
                    }
                    else
                    {
                        DebugConsole.WriteInfo("Smart Sync disabled for this game - skipping auto-hook check");
                    }
                }

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

                        process.Exited += async (s, e) =>
                        {
                            try
                            {
                                await OnGameExitedAsync(process);
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
                            await OnGameExitedAsync(process);
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

        private async Task<bool?> ShowSmartSyncPromptAsync(
            string title,
            string message,
            string yesButtonText,
            string noButtonText)
        {
            try
            {
                var box = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                    ContentTitle = title,
                    ContentMessage = message,
                    Icon = MsBox.Avalonia.Enums.Icon.Question,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                });

                var result = await box.ShowAsync();
                return result == MsBox.Avalonia.Enums.ButtonResult.Yes;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show Smart Sync prompt");
                return null;
            }
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

                var remoteName = _providerHelper.GetProviderConfigName(config.CloudConfig.Provider);
                var sanitizedGameName = SanitizeGameName(game.Name);
                var remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}/{sanitizedGameName}";

                var rcloneOps = new RcloneFileOperations();
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
    }
}
