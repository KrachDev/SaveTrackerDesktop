using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.LOGIC.RecloneManagement;
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

namespace SaveTracker.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ConfigManagement _configManagement;
        private readonly RcloneFileOperations _rcloneFileOperations;
        private readonly CloudProviderHelper _providerHelper;
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

        private Config? _mainConfig;
        private CloudProvider _selectedProvider;

        // ========== SIMPLIFIED EVENTS ==========
        public event Action? OnAddGameRequested;
        public event Action? OnAddFilesRequested;
        public event Action? OnCloudSettingsRequested;
        public event Action? OnBlacklistRequested;
        public event Action? OnRcloneSetupRequired;

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

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                DebugConsole.Enable(true);
                DebugConsole.ShowConsole();
                DebugConsole.WriteLine("Console Started!");
                DebugConsole.WriteInfo("Is Admin: " + AdminHelper.IsAdministrator().Result.ToString());

                await LoadDataAsync();
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
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load data");
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

                GameTitleText = game.Name;
                GamePathText = $"Install Path: {game.InstallDirectory}";
                IsLaunchEnabled = true;

                // Load icon
                GameIcon = Misc.ExtractIconFromExe(game.ExecutablePath);

                // Check if sync is available
                IsSyncEnabled = (bool)await ConfigManagement.HasData(game);

                // Load tracked files
                await UpdateTrackedListAsync(game);

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

                    _ = TrackGameProcessAsync(targetProcess, _trackingCancellation.Token);

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
                        }
                    };

                    if (targetProcess.HasExited)
                    {
                        await OnGameExitedAsync(targetProcess);
                        IsLaunchEnabled = true;
                        SyncStatusText = "Idle";
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
                            await _trackLogic.Track(SelectedGame.Game);
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
            try
            {
                var game = SelectedGame?.Game;
                if (game == null) return;

                DebugConsole.WriteInfo($"{game.Name} closed. Exit code: {process.ExitCode}");

                _trackingCancellation?.Cancel();

                game.LastTracked = DateTime.Now;
                await ConfigManagement.SaveGameAsync(game);

                var trackedFiles = _trackLogic?.GetUploadList();

                if (trackedFiles == null || trackedFiles.Count == 0)
                {
                    DebugConsole.WriteWarning("No files were tracked during gameplay");
                    return;
                }

                DebugConsole.WriteInfo($"Processing {trackedFiles.Count} tracked files...");

                if (CanUpload)
                {
                    await UploadFilesAsync(trackedFiles, game);
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
                    OnRcloneSetupRequired?.Invoke();
                    return;
                }

                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");

                _uploadManager = new SaveFileUploadManager(
                    rcloneInstaller,
                    _providerHelper,
                    new RcloneFileOperations(game),
                    configPath
                );

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
                if (selectedFiles.Count == 0)
                {
                    DebugConsole.WriteWarning("No files selected");
                    return;
                }

                var config = await ConfigManagement.LoadConfigAsync();
                if (config?.CloudConfig == null) return;

                var executor = new RcloneExecutor();
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");
                var remoteName = _providerHelper.GetProviderConfigName(config.CloudConfig.Provider);
                var sanitizedGameName = SanitizeGameName(SelectedGame.Game.Name);

                foreach (var fileVm in selectedFiles)
                {
                    string remotePath = $"{remoteName}:SaveTrackerCloudSave/{sanitizedGameName}/{fileVm.Name}";
                    string localPath = Path.Combine(SelectedGame.Game.InstallDirectory, fileVm.Name);

                    var result = await executor.ExecuteRcloneCommand(
                        $"copyto \"{remotePath}\" \"{localPath}\" --config \"{configPath}\" -v",
                        TimeSpan.FromMinutes(5)
                    );

                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Downloaded: {fileVm.Name}");
                    }
                }

                await UpdateTrackedListAsync(SelectedGame.Game);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to download files");
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

                await Misc.RcloneSetup(mainWindow);
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
                    TrackedFiles.Add(new TrackedFileViewModel(file.Value, game, gameUploadData.LastSyncStatus));
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
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");
                var remoteName = _providerHelper.GetProviderConfigName(config.CloudConfig.Provider);
                var sanitizedGameName = SanitizeGameName(game.Name);
                var remotePath = $"{remoteName}:SaveTrackerCloudSave/{sanitizedGameName}";

                var result = await executor.ExecuteRcloneCommand(
                    $"lsjson \"{remotePath}\" --config \"{configPath}\"",
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
    }
}