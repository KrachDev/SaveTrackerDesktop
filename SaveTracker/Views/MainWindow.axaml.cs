using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
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
        private CancellationTokenSource trackingCancellation;

        // SETTINGS
        bool canUpload = false;

        public Game SelectedGame;
        public MainWindow()
        {
            InitializeComponent();
            DebugConsole.Enable(true);
            DebugConsole.ShowConsole();
            DebugConsole.WriteLine("Console Started!");
            configManagement = new ConfigManagement();
            rcloneFileOperations = new RcloneFileOperations();
            LoadGames();
        }
        public async Task LoadGames()
        {
            var gamelist = await ConfigManagement.LoadAllGamesAsync(); // Add await here!
            foreach (var game in gamelist)
            {
                GamesList.Items.Add(Misc.CreateGame(game));
            }
        }

        private async void AddGameBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var window = new UC_AddGame();
            DebugConsole.WriteInfo("Show AddWindow");
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var newGame = await window.ShowDialog<Game>(this); // Get the returned Game object
            if (newGame != null)
            {
                // Add it to your list
                GamesList.Items.Add(Misc.CreateGame(newGame));
                await ConfigManagement.SaveGameAsync(newGame);
                DebugConsole.WriteSuccess($"Game added: {newGame.Name}");
            }
            else
            {
                DebugConsole.WriteInfo("Game addition canceled");
            }
        }


        private async void LaunchBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (SelectedGame != null)
            {
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

                    DebugConsole.WriteInfo($"Launching {SelectedGame.Name}...");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = SelectedGame.ExecutablePath,
                        WorkingDirectory = SelectedGame.InstallDirectory,
                        UseShellExecute = true
                    };

                    var process = Process.Start(startInfo);

                    if (process != null)
                    {
                        process.EnableRaisingEvents = true;
                        DebugConsole.WriteSuccess($"{SelectedGame.Name} started (PID: {process.Id})");

                        // Disable on UI thread (we're already on it)
                        LaunchBTN.IsEnabled = false;

                        // Track while game is running
                        _ = TrackGameProcess(process, trackingCancellation.Token);

                        // Handle process exit - MUST use Dispatcher for UI updates
                        process.Exited += async (s, e) =>
                        {
                            try
                            {
                                await OnGameExited(process);
                            }
                            finally
                            {
                                // Re-enable button on UI thread
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    LaunchBTN.IsEnabled = true;
                                });
                            }
                        };
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteError($"Failed to launch game: {ex.Message}");
                    trackingCancellation?.Cancel();

                    // Ensure button is re-enabled on error
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LaunchBTN.IsEnabled = true;
                    });
                }
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
                        // Track file operations
                        await trackLogic.Track(SelectedGame);

                        // Log memory usage periodically (optional) - with safety check
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

                        await Task.Delay(5000, cancellationToken); // Check every 5 seconds
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Don't log if it's just about the process exiting
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
        private async Task OnGameExited(Process process)
        {
            try
            {
                DebugConsole.WriteInfo($"{SelectedGame.Name} closed. Exit code: {process.ExitCode}");

                trackingCancellation?.Cancel();

                SelectedGame.LastTracked = DateTime.Now;
                await ConfigManagement.SaveGameAsync(SelectedGame);

                var trackedFiles = trackLogic.GetUploadList();

                if (trackedFiles == null || trackedFiles.Count == 0)
                {
                    DebugConsole.WriteWarning("No files were tracked during gameplay");
                    return;
                }

                // Check if all files exist
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
                    // Optional: return or handle missing files
                }
                else
                {
                    DebugConsole.WriteInfo("All tracked files exist.");
                }


                DebugConsole.WriteInfo($"Processing {trackedFiles.Count} tracked files...");

                var config = await ConfigManagement.LoadConfigAsync();
                if (canUpload)
                {
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
                        // Wrap in Dispatcher - this event fires from background thread
                        Dispatcher.UIThread.Post(() =>
                        {
                            DebugConsole.WriteInfo($"Upload: {progress.Status} ({progress.PercentComplete}%)");
                        });
                    };

                    uploadManager.OnUploadCompleted += (result) =>
                    {
                        // Wrap in Dispatcher - this event fires from background thread
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

                        // Update UI on UI thread
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await UpdateTrackedList(SelectedGame);
                        });
                    }
                }
                else
                {
                    DebugConsole.WriteLine("Upload DISBALED");
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
        private async void OpenCloudSettingsBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await Misc.RcloneSetup(this);
        }

        private async void StatisticsBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {

            try
            {
                var executor = new RcloneExecutor();
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");

                // Create a test file
                var testFile = Path.Combine(Path.GetTempPath(), "test_upload.txt");
                File.WriteAllText(testFile, $"Test upload at {DateTime.Now}");

                DebugConsole.WriteInfo($"Test file created: {testFile}");

                // Try uploading with verbose output
                var result = await executor.ExecuteRcloneCommand(
                    $"copyto \"{testFile}\" \"gdrive:TestUpload/test_upload.txt\" --config \"{configPath}\" -vv --progress",
                    TimeSpan.FromMinutes(2)
                );

                DebugConsole.WriteInfo($"Exit Code: {result.ExitCode}");
                DebugConsole.WriteInfo($"Success: {result.Success}");
                DebugConsole.WriteInfo($"Output:\n{result.Output}");
                DebugConsole.WriteInfo($"Error:\n{result.Error}");

                // Try to verify
                await Task.Delay(2000); // Wait a bit

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

                // Also check what remotes exist
                var remotesResult = await executor.ExecuteRcloneCommand(
                    $"listremotes --config \"{configPath}\"",
                    TimeSpan.FromSeconds(10)
                );

                DebugConsole.WriteInfo($"Configured remotes:\n{remotesResult.Output}");

                // List root of gdrive
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
            // Check if something is selected
            if (GamesList.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is Game game)
            {
                DebugConsole.WriteLine(game.Name);
                LaunchBTN.IsEnabled = true;
                SelectedGame = game;
                // Update the title
                GameTitleBox.Text = game.Name;

                // Update the path
                GamePAthBox.Text = $"Install Path: {game.InstallDirectory}";
                SyncBTN.IsEnabled = (bool)await ConfigManagement.HasData(game);

                // Update the icon
                var iconBitmap = Misc.ExtractIconFromExe(game.ExecutablePath);
                if (iconBitmap != null)
                {
                    GameIconImage.Source = iconBitmap;
                }
                else
                {
                    // Fallback to emoji if no icon
                    GameIconImage.Source = null;
                    // You can use a TextBlock for emoji fallback if needed
                }
                await UpdateTrackedList(game);

            }
            else
            {
                // Nothing selected - reset to defaults
                GameTitleBox.Text = "Select a game";
                GamePAthBox.Text = "No game selected";
                GameIconImage.Source = null;
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

                // Load tracked files from the checksum file
                var gameUploadData = await ConfigManagement.GetGameData(SelectedGame);

                if (gameUploadData == null || gameUploadData.Files == null || gameUploadData.Files.Count == 0)
                {
                    DebugConsole.WriteWarning("No tracked files found for this game");
                    return;
                }

                DebugConsole.WriteInfo($"Found {gameUploadData.Files.Count} tracked files for {SelectedGame.Name}");

                // Convert to list of file paths
                var trackedFiles = new List<string>();
                foreach (var file in gameUploadData.Files)
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

                if (trackedFiles.Count == 0)
                {
                    DebugConsole.WriteError("None of the tracked files exist on disk");
                    return;
                }

                DebugConsole.WriteInfo($"Processing {trackedFiles.Count} files for upload...");

                // Load cloud configuration
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config.CloudConfig;

                // Check if Rclone is configured
                var rcloneInstaller = new RcloneInstaller();
                bool rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);

                if (!rcloneReady)
                {
                    DebugConsole.WriteWarning($"Rclone is not configured for {provider.Provider}");
                    await Misc.RcloneSetup(this);

                    // Check again after setup
                    rcloneReady = await rcloneInstaller.RcloneCheckAsync(provider.Provider);
                    if (!rcloneReady)
                    {
                        DebugConsole.WriteError("Upload cancelled - cloud storage not configured");
                        return;
                    }
                }

                // Disable sync button during upload
                SyncBTN.IsEnabled = false;
                SyncBTN.Content = "Syncing...";

                // Setup upload manager
                var cloudHelper = new CloudProviderHelper();
                var rcloneFileOps = new RcloneFileOperations(SelectedGame);
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");

                uploadManager = new SaveFileUploadManager(
                    rcloneInstaller,
                    cloudHelper,
                    rcloneFileOps,
                    configPath
                );

                // Progress handler
                uploadManager.OnProgressChanged += (progress) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        DebugConsole.WriteInfo($"Upload: {progress.Status} ({progress.PercentComplete}%)");
                    });
                };

                // Completion handler
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

                // Perform upload
                var uploadResult = await uploadManager.Upload(
                    trackedFiles,
                    SelectedGame,
                    provider.Provider,
                    CancellationToken.None
                );

                if (uploadResult.Success)
                {
                    DebugConsole.WriteSuccess($"Successfully uploaded {uploadResult.UploadedCount} files");

                    // Update the checksum file with new upload timestamps
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

                    // Save updated checksum data
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
                // Re-enable sync button
                Dispatcher.UIThread.Post(() =>
                {
                    SyncBTN.IsEnabled = true;
                    SyncBTN.Content = "Sync Now";
                });
            }
        }

        public async Task UpdateTrackedList(Game game)
        {
            var gameUploadData = await ConfigManagement.GetGameData(game);

            if (gameUploadData == null || gameUploadData.Files == null)
            {
                DebugConsole.WriteWarning("No game data found");
                GameTrackedFileList.Children.Clear(); // Clear existing items
                FilesTrackedTxt.Text = $"{GameTrackedFileList.Children.Count} Files Track";

                return;
            }

            GameTrackedFileList.Children.Clear(); // Clear existing items

            foreach (var file in gameUploadData.Files)
            {
                var fileRecord = file.Value;
                var fileName = System.IO.Path.GetFileName(fileRecord.Path);
                var filePath = fileRecord.Path;

                // Create a border for each file row
                var fileBorder = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#252526")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
                    BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                    Padding = new Avalonia.Thickness(0, 10)
                };

                // Create grid matching the header columns
                var fileGrid = new Grid
                {
                    Margin = new Avalonia.Thickness(15, 0),
                    ColumnDefinitions = new ColumnDefinitions("40,2*,3*,100,150,120")
                };

                // Checkbox (Column 0)
                var checkbox = new CheckBox
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Tag = fileRecord
                };
                Grid.SetColumn(checkbox, 0);
                fileGrid.Children.Add(checkbox);

                // File Name (Column 1)
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

                // Path (Column 2)
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
                        // Get the absolute path
                        string absolutePath = fileRecord.GetAbsolutePath(game.InstallDirectory);

                        if (File.Exists(absolutePath))
                        {
                            // Open file location and select the file
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

                // Size (Column 3)
                var sizeText = new TextBlock
                {
                    Text = Misc.FormatFileSize(fileRecord.FileSize),
                    FontSize = 12,
                    Foreground = Avalonia.Media.Brushes.White,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(sizeText, 3);
                fileGrid.Children.Add(sizeText);

                // Last Modified (Column 4)
                var modifiedText = new TextBlock
                {
                    Text = fileRecord.LastUpload.ToString("MM/dd/yyyy HH:mm"),
                    FontSize = 12,
                    Foreground = Avalonia.Media.Brushes.White,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Grid.SetColumn(modifiedText, 4);
                fileGrid.Children.Add(modifiedText);

                // Status (Column 5)
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
                GameTrackedFileList.Children.Add(fileBorder);
                FilesTrackedTxt.Text = $"{GameTrackedFileList.Children.Count} Files Track";
            }
        }

        private void BlackListBTN_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var blistEditor = new BlackListEditor();
            blistEditor.ShowDialog(this);
        }
    }
}