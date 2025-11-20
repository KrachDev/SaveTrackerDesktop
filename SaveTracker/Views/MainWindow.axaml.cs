using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Newtonsoft.Json;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.LOGIC.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Process = System.Diagnostics.Process;

namespace SaveTracker.Views
{
    public partial class MainWindow : Window
    {
        public ConfigManagement configManagement;
        public SaveFileTrackerManager trackLogic;
        public SaveFileUploadManager uploadManager;
        public RcloneFileOperations rcloneFileOperations;
        public CloudProviderHelper ProviderHelper;
        private CancellationTokenSource trackingCancellation;

        // SETTINGS
        bool canUpload = true;

        public Game SelectedGame;
        private List<FileChecksumRecord> SelectTrackedFiles = new();
        private CloudProvider SelectedProvider;
        private Config MainConfig;
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                DebugConsole.Enable(true);
                DebugConsole.ShowConsole();
                DebugConsole.WriteLine("Console Started!");
                configManagement = new ConfigManagement();
                rcloneFileOperations = new RcloneFileOperations();
                ProviderHelper = new CloudProviderHelper();

                LoadData();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Fatal error in MainWindow constructor");
                throw; // Re-throw to prevent app from running in broken state
            }
        }

        public async Task LoadData()
        {
            // Load Games
            try
            {
                var gamelist = await ConfigManagement.LoadAllGamesAsync();

                if (gamelist == null)
                {
                    DebugConsole.WriteWarning("No games found to load");
                    return;
                }

                foreach (var game in gamelist)
                {
                    try
                    {
                        GamesList.Items.Add(Misc.CreateGame(game));
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Failed to add game: {game?.Name ?? "Unknown"}");
                    }
                }

                await UpdateGamesList();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load games");
                // Don't throw - allow app to continue
            }

            // Load Data
            try
            {
                MainConfig = await ConfigManagement.LoadConfigAsync();
                SelectedProvider = MainConfig.CloudConfig.Provider;

                CloudStorageTXT.Text = ProviderHelper.GetProviderDisplayName(SelectedProvider);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex);
                throw;
            }
        }

        private async void AddGameBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                var window = new UC_AddGame();
                DebugConsole.WriteInfo("Show AddWindow");
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                var newGame = await window.ShowDialog<Game>(this);

                if (newGame != null)
                {
                    GamesList.Items.Add(Misc.CreateGame(newGame));
                    await ConfigManagement.SaveGameAsync(newGame);
                    DebugConsole.WriteSuccess($"Game added: {newGame.Name}");
                    await UpdateGamesList();
                }
                else
                {
                    DebugConsole.WriteInfo("Game addition canceled");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to add game");
                // TODO: Show error dialog to user
            }
        }

        public async void LaunchBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (SelectedGame == null)
            {
                DebugConsole.WriteWarning("No game selected");
                return;
            }

            try
            {
                if (!File.Exists(SelectedGame.ExecutablePath))
                {
                    DebugConsole.WriteError($"Executable not found: {SelectedGame.ExecutablePath}");
                    return;
                }

                // Initialize tracking
                trackLogic = new SaveFileTrackerManager();
                trackingCancellation = new CancellationTokenSource();

                string exeName = Path.GetFileNameWithoutExtension(SelectedGame.ExecutablePath);
                var existingProcesses = Process.GetProcessesByName(exeName);
                Process targetProcess = null;

                if (existingProcesses.Length > 0)
                {
                    targetProcess = existingProcesses[0];
                    DebugConsole.WriteInfo($"Found existing process: {SelectedGame.Name} (PID: {targetProcess.Id})");
                    DebugConsole.WriteSuccess($"Hooking to existing process...");
                }
                else
                {
                    DebugConsole.WriteInfo($"Launching {SelectedGame.Name}...");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = SelectedGame.ExecutablePath,
                        WorkingDirectory = SelectedGame.InstallDirectory,
                        UseShellExecute = true
                    };

                    targetProcess = Process.Start(startInfo);

                    if (targetProcess != null)
                    {
                        DebugConsole.WriteSuccess($"{SelectedGame.Name} started (PID: {targetProcess.Id})");
                    }
                }

                if (targetProcess != null)
                {
                    targetProcess.EnableRaisingEvents = true;
                    LaunchBTN.IsEnabled = false;

                    _ = TrackGameProcess(targetProcess, trackingCancellation.Token);

                    targetProcess.Exited += async (s, e) =>
                    {
                        try
                        {
                            await OnGameExited(targetProcess);
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteException(ex, "Error in process exit handler");
                        }
                        finally
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                LaunchBTN.IsEnabled = true;
                            });
                        }
                    };

                    if (targetProcess.HasExited)
                    {
                        DebugConsole.WriteWarning($"Process already exited");
                        await OnGameExited(targetProcess);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            LaunchBTN.IsEnabled = true;
                        });
                    }
                }
                else
                {
                    DebugConsole.WriteError("Failed to start or find game process");
                    trackingCancellation?.Cancel();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to launch/hook game");
                trackingCancellation?.Cancel();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LaunchBTN.IsEnabled = true;
                });
            }
        }

        private async Task OnGameExited(Process process)
        {
            try
            {
                DebugConsole.WriteInfo($"{SelectedGame?.Name ?? "Game"} closed. Exit code: {process.ExitCode}");

                trackingCancellation?.Cancel();

                if (SelectedGame == null)
                {
                    DebugConsole.WriteError("SelectedGame is null in OnGameExited");
                    return;
                }

                SelectedGame.LastTracked = DateTime.Now;
                await ConfigManagement.SaveGameAsync(SelectedGame);

                var trackedFiles = trackLogic?.GetUploadList();

                if (trackedFiles == null || trackedFiles.Count == 0)
                {
                    DebugConsole.WriteWarning("No files were tracked during gameplay");
                    return;
                }

                bool allExist = true;
                foreach (var file in trackedFiles)
                {
                    if (!File.Exists(file))
                    {
                        DebugConsole.WriteWarning($"Tracked file missing: {file}");
                        allExist = false;
                    }
                }

                if (!allExist)
                {
                    DebugConsole.WriteWarning("Some tracked files are missing!");
                }
                else
                {
                    DebugConsole.WriteInfo("All tracked files exist.");
                }

                DebugConsole.WriteInfo($"Processing {trackedFiles.Count} tracked files...");

                var config = await ConfigManagement.LoadConfigAsync();

                if (config == null)
                {
                    DebugConsole.WriteError("Failed to load config");
                    return;
                }

                if (canUpload)
                {
                    var provider = config.CloudConfig;

                    if (provider == null)
                    {
                        DebugConsole.WriteError("Cloud provider config is null");
                        return;
                    }

                    var rcloneInstaller = new RcloneInstaller();
                    bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);

                    if (!rcloneReady)
                    {
                        DebugConsole.WriteWarning($"Rclone is not configured for {provider.Provider}");
                        await Misc.RcloneSetup(this);

                        rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);

                        if (!rcloneReady)
                        {
                            DebugConsole.WriteError("Upload cancelled - cloud storage not configured");
                            return;
                        }
                    }

                    var cloudHelper = new CloudProviderHelper();
                    var rcloneFileOps = new RcloneFileOperations(SelectedGame);
                    var configPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "ExtraTools",
                        "rclone.conf"
                    );

                    uploadManager = new SaveFileUploadManager(
                        rcloneInstaller,
                        cloudHelper,
                        rcloneFileOps,
                        configPath
                    );

                    uploadManager.OnProgressChanged += (progress) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            DebugConsole.WriteInfo($"Upload: {progress.Status} ({progress.PercentComplete}%)");
                        });
                    };

                    uploadManager.OnUploadCompleted += (result) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (result.Success)
                            {
                                DebugConsole.WriteSuccess($"Upload completed: {result.Message}");
                            }
                            else
                            {
                                DebugConsole.WriteError($"Upload failed: {result.Message}");
                            }
                        });
                    };

                    var uploadResult = await uploadManager.Upload(
                        trackedFiles,
                        SelectedGame,
                        provider.Provider,
                        CancellationToken.None
                    );

                    if (uploadResult.Success)
                    {
                        DebugConsole.WriteSuccess($"Successfully uploaded {uploadResult.UploadedCount} files");

                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await UpdateTrackedList(SelectedGame);
                        });
                    }
                }
                else
                {
                    DebugConsole.WriteLine("Upload DISABLED");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to process game exit");
            }
            finally
            {
                trackingCancellation?.Dispose();
                trackingCancellation = null;
            }
        }

        private async Task TrackGameProcess(Process process, CancellationToken cancellationToken)
        {
            try
            {
                DebugConsole.WriteInfo("Starting file tracking...");

                while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (SelectedGame == null)
                        {
                            DebugConsole.WriteError("SelectedGame became null during tracking");
                            break;
                        }

                        if (trackLogic != null)
                        {
                            await trackLogic.Track(SelectedGame);
                        }

                        try
                        {
                            if (!process.HasExited && process.WorkingSet64 > 0)
                            {
                                DebugConsole.WriteDebug($"Game memory: {process.WorkingSet64 / 1024 / 1024} MB");
                            }
                        }
                        catch
                        {
                            // Process already exited, ignore
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

        private async void OpenCloudSettingsBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                await Misc.RcloneSetup(this);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open cloud settings");
            }
        }

        private async void StatisticsBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                var executor = new RcloneExecutor();
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");

                var testFile = Path.Combine(Path.GetTempPath(), "test_upload.txt");
                File.WriteAllText(testFile, $"Test upload at {DateTime.Now}");

                DebugConsole.WriteInfo($"Test file created: {testFile}");

                var result = await executor.ExecuteRcloneCommand(
                    $"copyto \"{testFile}\" \"gdrive:TestUpload/test_upload.txt\" --config \"{configPath}\" -vv --progress",
                    TimeSpan.FromMinutes(2)
                );

                DebugConsole.WriteInfo($"Exit Code: {result.ExitCode}");
                DebugConsole.WriteInfo($"Success: {result.Success}");
                DebugConsole.WriteInfo($"Output:\n{result.Output}");
                DebugConsole.WriteInfo($"Error:\n{result.Error}");

                await Task.Delay(2000);

                var verifyResult = await executor.ExecuteRcloneCommand(
                    $"ls \"gdrive:TestUpload\" --config \"{configPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (verifyResult.Success && !string.IsNullOrWhiteSpace(verifyResult.Output))
                {
                    DebugConsole.WriteSuccess("File verified in Google Drive!");
                    DebugConsole.WriteInfo(verifyResult.Output);
                }
                else
                {
                    DebugConsole.WriteError("File NOT found in Google Drive after upload!");
                }

                var remotesResult = await executor.ExecuteRcloneCommand(
                    $"listremotes --config \"{configPath}\"",
                    TimeSpan.FromSeconds(10)
                );

                DebugConsole.WriteInfo($"Configured remotes:\n{remotesResult.Output}");

                var rootResult = await executor.ExecuteRcloneCommand(
                    $"lsd gdrive: --config \"{configPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                DebugConsole.WriteInfo($"Google Drive root folders:\n{rootResult.Output}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Test upload failed");
            }
        }

        private async void GamesList_SelectionChanged_1(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (GamesList.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is Game game)
                {
                    DebugConsole.WriteLine(game.Name);
                    LaunchBTN.IsEnabled = true;
                    SelectedGame = game;
                    SelectTrackedFiles = new();
                    GameTitleBox.Text = game.Name;
                    GamePAthBox.Text = $"Install Path: {game.InstallDirectory}";

                    SyncBTN.IsEnabled = (bool)await ConfigManagement.HasData(game);

                    var iconBitmap = Misc.ExtractIconFromExe(game.ExecutablePath);
                    GameIconImage.Source = iconBitmap;

                    await UpdateTrackedList(game);

                    // Also load cloud files for the Cloud Folder tab
                    _ = LoadCloudFiles(game); // Fire and forget - don't wait for it
                }
                else
                {
                    GameTitleBox.Text = "Select a game";
                    GamePAthBox.Text = "No game selected";
                    GameIconImage.Source = null;

                    // Clear cloud file list
                    CloudFileList.Children.Clear();
                    CloudFilesCountTxt.Text = "0 files in cloud";
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Error in game selection changed");
            }
        }

        private async void SyncBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (SelectedGame == null)
                {
                    DebugConsole.WriteWarning("No game selected");
                    return;
                }

                var gameUploadData = await ConfigManagement.GetGameData(SelectedGame);

                if (gameUploadData == null || gameUploadData.Files == null || gameUploadData.Files.Count == 0)
                {
                    DebugConsole.WriteWarning("No tracked files found for this game");
                    return;
                }

                DebugConsole.WriteInfo($"Found {gameUploadData.Files.Count} tracked files for {SelectedGame.Name}");

                var trackedFiles = new List<string>();
                foreach (var file in gameUploadData.Files)
                {
                    try
                    {
                        string absolutePath = file.Value.GetAbsolutePath(SelectedGame.InstallDirectory);

                        if (File.Exists(absolutePath))
                        {
                            trackedFiles.Add(absolutePath);
                        }
                        else
                        {
                            DebugConsole.WriteWarning($"File not found: {file.Key}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Error processing file: {file.Key}");
                    }
                }

                if (trackedFiles.Count == 0)
                {
                    DebugConsole.WriteError("None of the tracked files exist on disk");
                    return;
                }

                DebugConsole.WriteInfo($"Processing {trackedFiles.Count} files for upload...");

                var config = await ConfigManagement.LoadConfigAsync();

                if (config == null || config.CloudConfig == null)
                {
                    DebugConsole.WriteError("Failed to load cloud configuration");
                    return;
                }

                var provider = config.CloudConfig;

                var rcloneInstaller = new RcloneInstaller();
                bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);

                if (!rcloneReady)
                {
                    DebugConsole.WriteWarning($"Rclone is not configured for {provider.Provider}");
                    await Misc.RcloneSetup(this);

                    rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);
                    if (!rcloneReady)
                    {
                        DebugConsole.WriteError("Upload cancelled - cloud storage not configured");
                        return;
                    }
                }

                SyncBTN.IsEnabled = false;
                SyncBTN.Content = "Syncing...";

                var cloudHelper = new CloudProviderHelper();
                var rcloneFileOps = new RcloneFileOperations(SelectedGame);
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");

                uploadManager = new SaveFileUploadManager(
                    rcloneInstaller,
                    cloudHelper,
                    rcloneFileOps,
                    configPath
                );

                uploadManager.OnProgressChanged += (progress) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        DebugConsole.WriteInfo($"Upload: {progress.Status} ({progress.PercentComplete}%)");
                    });
                };

                uploadManager.OnUploadCompleted += (result) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (result.Success)
                        {
                            DebugConsole.WriteSuccess($"Upload completed: {result.Message}");
                        }
                        else
                        {
                            DebugConsole.WriteError($"Upload failed: {result.Message}");
                        }
                    });
                };

                var uploadResult = await uploadManager.Upload(
                    trackedFiles,
                    SelectedGame,
                    provider.Provider,
                    CancellationToken.None
                );

                if (uploadResult.Success)
                {
                    DebugConsole.WriteSuccess($"Successfully uploaded {uploadResult.UploadedCount} files");

                    foreach (var filePath in trackedFiles)
                    {
                        string relativePath = filePath.Replace(SelectedGame.InstallDirectory, "%GAMEPATH%");

                        if (gameUploadData.Files.ContainsKey(relativePath))
                        {
                            gameUploadData.Files[relativePath].LastUpload = DateTime.UtcNow;
                        }
                    }

                    gameUploadData.LastUpdated = DateTime.UtcNow;
                    gameUploadData.LastSyncStatus = "Success";

                    await ConfigManagement.SaveGameData(SelectedGame, gameUploadData);
                }
                else
                {
                    DebugConsole.WriteError($"Upload failed: {uploadResult.Message}");
                    gameUploadData.LastSyncStatus = "Failed";
                    await ConfigManagement.SaveGameData(SelectedGame, gameUploadData);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to sync game files");
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SyncBTN.IsEnabled = true;
                    SyncBTN.Content = "Sync Now";
                });
            }
        }

        public async Task UpdateTrackedList(Game game)
        {
            try
            {
                if (game == null)
                {
                    DebugConsole.WriteWarning("Cannot update tracked list: game is null");
                    return;
                }

                var gameUploadData = await ConfigManagement.GetGameData(game);

                if (gameUploadData == null || gameUploadData.Files == null)
                {
                    DebugConsole.WriteWarning("No game data found");
                    GameTrackedFileList.Children.Clear();
                    FilesTrackedTxt.Text = $"{GameTrackedFileList.Children.Count} Files Track";
                    return;
                }

                GameTrackedFileList.Children.Clear();

                foreach (var file in gameUploadData.Files)
                {
                    try
                    {
                        var fileRecord = file.Value;
                        var fileName = System.IO.Path.GetFileName(fileRecord.Path);
                        var filePath = fileRecord.Path;

                        var fileBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.Parse("#252526")),
                            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
                            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                            Padding = new Avalonia.Thickness(0, 10)
                        };

                        var fileGrid = new Grid
                        {
                            Margin = new Avalonia.Thickness(15, 0),
                            ColumnDefinitions = new ColumnDefinitions("40,2*,3*,100,150,120")
                        };

                        var checkbox = new CheckBox
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Tag = fileRecord
                        };

                        checkbox.IsCheckedChanged += (s, e) =>
                        {
                            try
                            {
                                if (checkbox.IsChecked == true && !SelectTrackedFiles.Contains(fileRecord))
                                {
                                    SelectTrackedFiles.Add(fileRecord);
                                }
                                else if (checkbox.IsChecked == false)
                                {
                                    SelectTrackedFiles.Remove(fileRecord);
                                }
                                DebugConsole.WriteList<FileChecksumRecord>("Selected Tracked List", SelectTrackedFiles);
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteException(ex, "Error in checkbox change");
                            }
                        };

                        Grid.SetColumn(checkbox, 0);
                        fileGrid.Children.Add(checkbox);

                        var nameText = new TextBlock
                        {
                            Text = fileName,
                            FontSize = 13,
                            Foreground = Avalonia.Media.Brushes.White,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                        };
                        Grid.SetColumn(nameText, 1);
                        fileGrid.Children.Add(nameText);

                        var pathText = new TextBlock
                        {
                            Text = filePath,
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Color.Parse("#858585")),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                        };

                        pathText.DoubleTapped += (s, e) =>
                        {
                            try
                            {
                                string absolutePath = fileRecord.GetAbsolutePath(game.InstallDirectory);

                                if (File.Exists(absolutePath))
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "explorer.exe",
                                        Arguments = $"/select,\"{absolutePath}\"",
                                        UseShellExecute = true
                                    });
                                }
                                else
                                {
                                    DebugConsole.WriteWarning($"File not found: {absolutePath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteException(ex, "Failed to open file location");
                            }
                        };

                        Grid.SetColumn(pathText, 2);
                        fileGrid.Children.Add(pathText);

                        var sizeText = new TextBlock
                        {
                            Text = Misc.FormatFileSize(fileRecord.FileSize),
                            FontSize = 12,
                            Foreground = Avalonia.Media.Brushes.White,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                        Grid.SetColumn(sizeText, 3);
                        fileGrid.Children.Add(sizeText);

                        var modifiedText = new TextBlock
                        {
                            Text = fileRecord.LastUpload.ToString("MM/dd/yyyy HH:mm"),
                            FontSize = 12,
                            Foreground = Avalonia.Media.Brushes.White,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                        Grid.SetColumn(modifiedText, 4);
                        fileGrid.Children.Add(modifiedText);

                        var (statusText, statusColor) = Misc.GetStatusInfo(fileRecord, gameUploadData.LastSyncStatus);

                        var statusBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.Parse(statusColor)),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Avalonia.Thickness(8, 4),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };

                        var statusTextBlock = new TextBlock
                        {
                            Text = statusText,
                            FontSize = 11,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = Avalonia.Media.Brushes.White
                        };

                        statusBorder.Child = statusTextBlock;
                        Grid.SetColumn(statusBorder, 5);
                        fileGrid.Children.Add(statusBorder);

                        fileBorder.Child = fileGrid;
                        fileBorder.Tag = fileRecord;
                        GameTrackedFileList.Children.Add(fileBorder);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Error adding file to UI: {file.Key}");
                    }
                }

                FilesTrackedTxt.Text = $"{GameTrackedFileList.Children.Count} Files Track";
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update tracked list");
            }
        }

        public async Task UpdateGamesList()
        {
            try
            {
                GamesCountTxt.Text = $"{GamesList.Items.Count} games tracked";
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update games list");
            }
        }

        private void BlackListBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                var blistEditor = new BlackListEditor();
                blistEditor.ShowDialog(this);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open blacklist editor");
            }
        }

        private async void RemoveFilesBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                DebugConsole.WriteLine("RemoveFilesBTN_Click invoked.");

                if (SelectedGame == null)
                {
                    DebugConsole.WriteWarning("No game selected");
                    return;
                }

                if (SelectTrackedFiles == null || SelectTrackedFiles.Count == 0)
                {
                    DebugConsole.WriteLine("No files selected for removal.");
                    return;
                }

                DebugConsole.WriteLine($"Selected files count: {SelectTrackedFiles.Count}");

                string gameDataFile = SelectedGame.GetGameDataFile();
                DebugConsole.WriteLine($"GameDataFile path: {gameDataFile}");

                if (!File.Exists(gameDataFile))
                {
                    DebugConsole.WriteLine("GameDataFile does not exist.");
                    return;
                }

                string json = await File.ReadAllTextAsync(gameDataFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    DebugConsole.WriteLine("GameDataFile is empty.");
                    return;
                }

                var data = System.Text.Json.JsonSerializer.Deserialize<GameUploadData>(json);
                if (data == null)
                {
                    DebugConsole.WriteLine("Failed to deserialize GameUploadData.");
                    return;
                }

                DebugConsole.WriteLine($"Files before removal: {data.Files.Count}");

                var filesToRemove = SelectTrackedFiles.ToList();

                foreach (var fileRecord in filesToRemove)
                {
                    try
                    {
                        string targetAbs = fileRecord.GetAbsolutePath(SelectedGame.InstallDirectory);
                        DebugConsole.WriteLine($"Looking for: {targetAbs}");

                        var matchingEntry = data.Files
                            .FirstOrDefault(kvp =>
                            {
                                string expandedPath = kvp.Value.GetAbsolutePath(SelectedGame.InstallDirectory);
                                return expandedPath.Equals(targetAbs, StringComparison.OrdinalIgnoreCase);
                            });

                        if (!matchingEntry.Equals(default(KeyValuePair<string, FileChecksumRecord>)))
                        {
                            data.Files.Remove(matchingEntry.Key);
                            DebugConsole.WriteLine($"✓ Removed: {matchingEntry.Key}");
                        }
                        else
                        {
                            DebugConsole.WriteLine($"⚠ Not found in Files dictionary: {fileRecord.Path}");
                        }

                        var uiElement = GameTrackedFileList.Children
                            .OfType<Border>()
                            .FirstOrDefault(b => b.Tag == fileRecord);

                        if (uiElement != null)
                        {
                            GameTrackedFileList.Children.Remove(uiElement);
                            DebugConsole.WriteLine("✓ UI element removed.");
                        }

                        SelectTrackedFiles.Remove(fileRecord);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Error removing file: {fileRecord?.Path ?? "Unknown"}");
                    }
                }

                FilesTrackedTxt.Text = $"{GameTrackedFileList.Children.Count} Files Tracked";

                DebugConsole.WriteLine($"Files after removal: {data.Files.Count}");

                await rcloneFileOperations.SaveChecksumData(data, SelectedGame);

                DebugConsole.WriteLine("✓ Save completed successfully!");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to remove files");
            }
        }

        private async void AddFileBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                DebugConsole.WriteLine("AddFileBTN_Click invoked.");

                if (SelectedGame == null)
                {
                    DebugConsole.WriteLine("No game selected.");
                    return;
                }

                var fileDialog = new OpenFileDialog
                {
                    Title = "Select Files to Track",
                    AllowMultiple = true,
                    Directory = SelectedGame.InstallDirectory
                };

                var selectedFiles = await fileDialog.ShowAsync(this);

                if (selectedFiles == null || selectedFiles.Length == 0)
                {
                    DebugConsole.WriteLine("No files selected.");
                    return;
                }

                DebugConsole.WriteLine($"User selected {selectedFiles.Length} file(s).");

                string gameDataFile = SelectedGame.GetGameDataFile();
                DebugConsole.WriteLine($"GameDataFile path: {gameDataFile}");

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

                DebugConsole.WriteLine($"Files before addition: {data.Files.Count}");

                int addedCount = 0;
                int skippedCount = 0;

                foreach (var filePath in selectedFiles)
                {
                    try
                    {
                        DebugConsole.WriteLine($"Processing: {filePath}");

                        if (!File.Exists(filePath))
                        {
                            DebugConsole.WriteLine($"File does not exist: {filePath}");
                            continue;
                        }

                        string contractedPath = PathContractor.ContractPath(filePath, SelectedGame.InstallDirectory);
                        DebugConsole.WriteLine($"Contracted path: {contractedPath}");

                        if (data.Files.ContainsKey(contractedPath))
                        {
                            DebugConsole.WriteLine($"File already tracked, skipping: {contractedPath}");
                            skippedCount++;
                            continue;
                        }

                        DebugConsole.WriteLine("Calculating checksum...");
                        string checksum = await rcloneFileOperations.GetFileChecksum(filePath);

                        var fileInfo = new FileInfo(filePath);

                        var record = new FileChecksumRecord
                        {
                            Path = contractedPath,
                            Checksum = checksum,
                            FileSize = fileInfo.Length,
                            LastUpload = DateTime.UtcNow
                        };

                        data.Files[contractedPath] = record;
                        DebugConsole.WriteLine($"✓ Added: {contractedPath}");

                        addedCount++;
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Error processing file: {filePath}");
                    }
                }

                DebugConsole.WriteLine($"Files after addition: {data.Files.Count} (added: {addedCount}, skipped: {skippedCount})");

                data.LastUpdated = DateTime.UtcNow;

                await rcloneFileOperations.SaveChecksumData(data, SelectedGame);
                DebugConsole.WriteLine("✓ Data saved.");

                await UpdateTrackedList(SelectedGame);
                DebugConsole.WriteLine($"✓ Successfully added {addedCount} file(s)!");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to add files");
            }
        }

        private async void RefreshCloudFilesBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (SelectedGame == null)
                {
                    DebugConsole.WriteWarning("No game selected");
                    return;
                }

                // Disable button during refresh
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "🔄 Refreshing...";
                }

                DebugConsole.WriteInfo("Refreshing cloud files...");
                await LoadCloudFiles(SelectedGame);

                // Re-enable button
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "🔄 Refresh";
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to refresh cloud files");

                // Ensure button is re-enabled on error
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "🔄 Refresh";
                }
            }
        }

        private async void DownloadSelectedFilesBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (SelectedGame == null)
                {
                    DebugConsole.WriteWarning("No game selected");
                    return;
                }

                // Get selected files from checkboxes
                var selectedFiles = new List<RcloneFileInfo>();

                foreach (var child in CloudFileList.Children.OfType<Border>())
                {
                    if (child.Child is Grid grid)
                    {
                        var checkbox = grid.Children.OfType<CheckBox>().FirstOrDefault();
                        if (checkbox?.IsChecked == true && checkbox.Tag is RcloneFileInfo fileInfo)
                        {
                            selectedFiles.Add(fileInfo);
                        }
                    }
                }

                if (selectedFiles.Count == 0)
                {
                    DebugConsole.WriteWarning("No files selected for download");
                    return;
                }

                DebugConsole.WriteInfo($"Downloading {selectedFiles.Count} file(s)...");

                var config = await ConfigManagement.LoadConfigAsync();
                if (config == null || config.CloudConfig == null)
                {
                    DebugConsole.WriteError("Failed to load cloud configuration");
                    return;
                }

                var provider = config.CloudConfig;
                var executor = new RcloneExecutor();
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");

                var cloudHelper = new CloudProviderHelper();
                string remoteName = cloudHelper.GetProviderConfigName(provider.Provider);
                string sanitizedGameName = SanitizeGameName(SelectedGame.Name);

                // Disable button during download
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "⬇️ Downloading...";
                }

                int successCount = 0;
                int failCount = 0;

                foreach (var fileInfo in selectedFiles)
                {
                    try
                    {
                        string fileName = fileInfo.Name;
                        string remotePath = $"{remoteName}:SaveTrackerCloudSave/{sanitizedGameName}/{fileName}";
                        string localPath = Path.Combine(SelectedGame.InstallDirectory, fileName);

                        DebugConsole.WriteInfo($"Downloading: {fileName}");
                        DebugConsole.WriteDebug($"From: {remotePath}");
                        DebugConsole.WriteDebug($"To: {localPath}");

                        var result = await executor.ExecuteRcloneCommand(
                            $"copyto \"{remotePath}\" \"{localPath}\" --config \"{configPath}\" -v",
                            TimeSpan.FromMinutes(5)
                        );

                        if (result.Success)
                        {
                            DebugConsole.WriteSuccess($"✓ Downloaded: {fileName}");
                            successCount++;
                        }
                        else
                        {
                            DebugConsole.WriteError($"✗ Failed to download: {fileName}");
                            DebugConsole.WriteError($"Error: {result.Error}");
                            failCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Error downloading file: {fileInfo.Name}");
                        failCount++;
                    }
                }

                DebugConsole.WriteSuccess($"Download complete: {successCount} succeeded, {failCount} failed");

                // Refresh tracked list to show updated files
                if (successCount > 0)
                {
                    await UpdateTrackedList(SelectedGame);
                }

                // Re-enable button
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "⬇️ Download Selected";
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to download files");

                // Ensure button is re-enabled on error
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "⬇️ Download Selected";
                }
            }
        }

        public async Task LoadCloudFiles(Game game)
        {
            try
            {
                if (game == null)
                {
                    DebugConsole.WriteWarning("Cannot load cloud files: game is null");
                    return;
                }

                CloudFileList.Children.Clear();
                CloudFilesCountTxt.Text = "Loading...";

                var config = await ConfigManagement.LoadConfigAsync();
                if (config == null || config.CloudConfig == null)
                {
                    DebugConsole.WriteError("Failed to load cloud configuration");
                    CloudFilesCountTxt.Text = "0 files in cloud";

                    // Show message in UI
                    var messageText = new TextBlock
                    {
                        Text = "Cloud storage not configured. Click 'Cloud Config' to set up.",
                        Foreground = new SolidColorBrush(Color.Parse("#858585")),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 50, 0, 0),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                    CloudFileList.Children.Add(messageText);
                    return;
                }

                var provider = config.CloudConfig;

                // Check if rclone is configured
                var rcloneInstaller = new RcloneInstaller();
                bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);

                if (!rcloneReady)
                {
                    DebugConsole.WriteWarning($"Rclone is not configured for {provider.Provider}");
                    CloudFilesCountTxt.Text = "Cloud not configured";

                    var messageText = new TextBlock
                    {
                        Text = $"Rclone not configured for {provider.Provider}. Click 'Cloud Config' to set up.",
                        Foreground = new SolidColorBrush(Color.Parse("#858585")),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 50, 0, 0),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                    CloudFileList.Children.Add(messageText);
                    return;
                }

                var executor = new RcloneExecutor();
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");

                // Use the CloudProviderHelper to get the correct remote name
                var cloudHelper = new CloudProviderHelper();
                string remoteName = cloudHelper.GetProviderConfigName(provider.Provider);
                string sanitizedGameName = SanitizeGameName(game.Name);
                string remotePath = $"{remoteName}:SaveTrackerCloudSave/{sanitizedGameName}";

                DebugConsole.WriteInfo($"Listing files from: {remotePath}");

                var result = await executor.ExecuteRcloneCommand(
                    $"lsjson \"{remotePath}\" --config \"{configPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (!result.Success)
                {
                    if (result.Error.Contains("directory not found") || result.Error.Contains("not found"))
                    {
                        DebugConsole.WriteInfo("No cloud folder found for this game yet");
                        CloudFilesCountTxt.Text = "0 files in cloud";

                        var messageText = new TextBlock
                        {
                            Text = "No files uploaded yet. Upload tracked files to see them here.",
                            Foreground = new SolidColorBrush(Color.Parse("#858585")),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(0, 50, 0, 0)
                        };
                        CloudFileList.Children.Add(messageText);
                        return;
                    }

                    DebugConsole.WriteError($"Failed to list cloud files: {result.Error}");
                    CloudFilesCountTxt.Text = "Error loading files";

                    var errorText = new TextBlock
                    {
                        Text = $"Error loading files: {result.Error}",
                        Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 50, 0, 0),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                    CloudFileList.Children.Add(errorText);
                    return;
                }

                if (string.IsNullOrWhiteSpace(result.Output) || result.Output.Trim() == "[]")
                {
                    DebugConsole.WriteInfo("No files found in cloud");
                    CloudFilesCountTxt.Text = "0 files in cloud";

                    var messageText = new TextBlock
                    {
                        Text = "No files uploaded yet. Upload tracked files to see them here.",
                        Foreground = new SolidColorBrush(Color.Parse("#858585")),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 50, 0, 0)
                    };
                    CloudFileList.Children.Add(messageText);
                    return;
                }

                var files = System.Text.Json.JsonSerializer.Deserialize<List<RcloneFileInfo>>(result.Output);

                if (files == null || files.Count == 0)
                {
                    DebugConsole.WriteWarning("No files found in cloud");
                    CloudFilesCountTxt.Text = "0 files in cloud";

                    var messageText = new TextBlock
                    {
                        Text = "No files uploaded yet. Upload tracked files to see them here.",
                        Foreground = new SolidColorBrush(Color.Parse("#858585")),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 50, 0, 0)
                    };
                    CloudFileList.Children.Add(messageText);
                    return;
                }

                // Filter out directories and system files
                files = files.Where(f => !f.IsDir && !f.Name.StartsWith(".")).ToList();

                long totalSize = 0;
                foreach (var file in files)
                {
                    try
                    {
                        totalSize += file.Size;

                        var fileBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.Parse("#252526")),
                            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
                            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                            Padding = new Avalonia.Thickness(0, 10),
                            Tag = file.Path
                        };

                        var fileGrid = new Grid
                        {
                            Margin = new Avalonia.Thickness(15, 0),
                            ColumnDefinitions = new ColumnDefinitions("40,3*,150,150,120")
                        };

                        var checkbox = new CheckBox
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Tag = file
                        };
                        Grid.SetColumn(checkbox, 0);
                        fileGrid.Children.Add(checkbox);

                        var nameText = new TextBlock
                        {
                            Text = file.Name,
                            FontSize = 13,
                            Foreground = Avalonia.Media.Brushes.White,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                        };
                        Grid.SetColumn(nameText, 1);
                        fileGrid.Children.Add(nameText);

                        var sizeText = new TextBlock
                        {
                            Text = Misc.FormatFileSize(file.Size),
                            FontSize = 12,
                            Foreground = Avalonia.Media.Brushes.White,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                        Grid.SetColumn(sizeText, 2);
                        fileGrid.Children.Add(sizeText);

                        var modifiedText = new TextBlock
                        {
                            Text = file.ModTime.ToLocalTime().ToString("MM/dd/yyyy HH:mm"),
                            FontSize = 12,
                            Foreground = Avalonia.Media.Brushes.White,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                        Grid.SetColumn(modifiedText, 3);
                        fileGrid.Children.Add(modifiedText);

                        var statusBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.Parse("#0E7490")),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Avalonia.Thickness(8, 4),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };

                        var statusTextBlock = new TextBlock
                        {
                            Text = "In Cloud",
                            FontSize = 11,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = Avalonia.Media.Brushes.White
                        };

                        statusBorder.Child = statusTextBlock;
                        Grid.SetColumn(statusBorder, 4);
                        fileGrid.Children.Add(statusBorder);

                        fileBorder.Child = fileGrid;
                        CloudFileList.Children.Add(fileBorder);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Error adding cloud file to UI: {file.Name}");
                    }
                }

                CloudFilesCountTxt.Text = $"{files.Count} files in cloud • {Misc.FormatFileSize(totalSize)}";
                DebugConsole.WriteSuccess($"Loaded {files.Count} cloud files ({Misc.FormatFileSize(totalSize)})");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load cloud files");
                CloudFilesCountTxt.Text = "Error loading files";

                CloudFileList.Children.Clear();
                var errorText = new TextBlock
                {
                    Text = $"Error: {ex.Message}",
                    Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 50, 0, 0),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                };
                CloudFileList.Children.Add(errorText);
            }
        }

        private string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            return invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_')).Trim();
        }

        private void CloudGamesBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
        }
    }

    public class RcloneFileInfo
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime ModTime { get; set; }
        public bool IsDir { get; set; }
    }
}