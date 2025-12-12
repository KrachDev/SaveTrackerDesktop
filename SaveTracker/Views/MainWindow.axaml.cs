using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Platform.Storage;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.ViewModels;
using SaveTracker.Views.Dialog;
using System;
using System.Linq;
using System.Threading.Tasks;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Security.Principal;
using System.Diagnostics;

namespace SaveTracker.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _viewModel;
        public Avalonia.Controls.Notifications.WindowNotificationManager? NotificationManager { get; private set; }

        public MainWindow()
        {
            DebugConsole.WriteInfo("=== MainWindow Constructor START ===");
            InitializeComponent();
            DebugConsole.WriteInfo("InitializeComponent completed");

            // Initialize notification manager
            NotificationManager = new Avalonia.Controls.Notifications.WindowNotificationManager(this)
            {
                Position = Avalonia.Controls.Notifications.NotificationPosition.TopRight,
                MaxItems = 3
            };

            DataContextChanged += OnDataContextChanged;
            DebugConsole.WriteSuccess("=== MainWindow Constructor COMPLETE ===");
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            DebugConsole.WriteInfo("MainWindow OnOpened fired.");

            bool isAdmin = IsAdministrator();
            DebugConsole.WriteInfo($"IsAdministrator: {isAdmin}");

            if (!isAdmin)
            {
                DebugConsole.WriteInfo("Showing Admin Warning Dialog...");
                try
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                    {
                        ButtonDefinitions = ButtonEnum.YesNo,
                        ContentTitle = "Administrator Rights Required",
                        ContentHeader = "Restart as Administrator?",
                        ContentMessage = "SaveTracker requires Administrator privileges to monitor game processes correctly.\n\n" +
                                         "Do you want to restart the application as Administrator?",
                        Icon = MsBox.Avalonia.Enums.Icon.Warning,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    });

                    ButtonResult result;
                    EnsureWindowVisible();
                    result = await box.ShowWindowDialogAsync(this);

                    DebugConsole.WriteInfo($"Admin Dialog Result: {result}");

                    if (result == ButtonResult.Yes)
                    {
                        DebugConsole.WriteInfo("User accepted restart. Restarting as Admin...");
                        RestartAsAdmin();
                    }
                    else
                    {
                        DebugConsole.WriteWarning("User declined to restart as Administrator. Some features may not work.");
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "Failed to show admin dialog");
                }
            }
        }

        private bool IsAdministrator()
        {
            var adminHelper = new AdminPrivilegeHelper();
            return adminHelper.IsAdministrator();
        }

        private void RestartAsAdmin()
        {
            var adminHelper = new AdminPrivilegeHelper();
            adminHelper.RestartAsAdmin();
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            DataContextChanged -= OnDataContextChanged;

            if (DataContext is MainWindowViewModel viewModel)
            {
                _viewModel = viewModel;
                DebugConsole.WriteInfo($"ViewModel received via DataContext: {_viewModel != null}");

                DebugConsole.WriteInfo("Subscribing to ViewModel events...");

                // Use local variable 'viewModel' to avoid potential null reference warnings
                viewModel.OnAddGameRequested += ShowAddGameDialog;
                viewModel.OnAddFilesRequested += ShowAddFilesDialog;
                viewModel.OnCloudSettingsRequested += ShowCloudSettings;
                viewModel.OnBlacklistRequested += ShowBlacklist;
                viewModel.OnRcloneSetupRequired += ShowRcloneSetup;
                viewModel.OnSettingsRequested += ShowSettingsDialog;
                viewModel.RequestMinimize += MinimizeWindow;
                viewModel.OnCloudSaveFound += ShowCloudSaveFoundDialog;
                viewModel.OnUpdateAvailable += ShowUpdateDialog;
                viewModel.OnSmartSyncRequested += ShowSmartSyncWindow;

                DebugConsole.WriteSuccess("ViewModel event subscriptions complete");
            }
            else
            {
                DebugConsole.WriteError("DataContext is not MainWindowViewModel!");
            }
        }

        private async void ShowAddGameDialog()
        {
            if (_viewModel == null) return;
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    EnsureWindowVisible();
                    var window = new UC_AddGame();
                    var result = await window.ShowDialog<Game?>(this);
                    if (result != null)
                    {
                        await _viewModel.OnGameAddedAsync(result);
                    }
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show add game dialog");
            }
        }

        private async void ShowAddFilesDialog()
        {
            if (_viewModel == null) return;
            try
            {
                if (_viewModel.SelectedGame == null) return;

                var startLocation = await this.StorageProvider.TryGetFolderFromPathAsync(_viewModel.SelectedGame.Game.InstallDirectory);

                var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Files to Track",
                    AllowMultiple = true,
                    SuggestedStartLocation = startLocation
                });

                if (files.Count > 0)
                {
                    var selectedFiles = files.Select(f => f.Path.LocalPath).ToArray();
                    await _viewModel.OnFilesAddedAsync(selectedFiles);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show add files dialog");
            }
        }

        private void EnsureWindowVisible()
        {
            if (!this.IsVisible)
            {
                this.Show();
            }
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
        }

        private async void ShowCloudSettings()
        {
            try
            {
                await OpenCloudSettingsDialog();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open cloud settings");
            }
        }

        private void ShowBlacklist()
        {
            try
            {
                var blistEditor = new BlackListEditor
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                _ = blistEditor.ShowDialog(this);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open blacklist editor");
            }
        }

        private async Task ShowRcloneSetup()
        {
            try
            {
                await OpenCloudSettingsDialog();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to setup Rclone");
            }
        }

        private async Task<bool> ShowCloudSaveFoundDialog(string gameName)
        {
            try
            {
                return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                    {
                        ButtonDefinitions = ButtonEnum.YesNo,
                        ContentTitle = "Cloud Save Found",
                        ContentHeader = "Download Cloud Save?",
                        ContentMessage = $"A cloud save for {gameName} was found. Do you want to download it before launching?\n\nThis will overwrite local files.",
                        Icon = MsBox.Avalonia.Enums.Icon.Question,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                    EnsureWindowVisible();
                    var result = await box.ShowWindowDialogAsync(this);
                    return result == ButtonResult.Yes;
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show cloud save dialog");
                return false;
            }
        }

        private async void ShowUpdateDialog(SaveTracker.Resources.Logic.AutoUpdater.UpdateInfo info)
        {
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // First, ask user if they want to update
                    var confirmBox = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                    {
                        ButtonDefinitions = ButtonEnum.YesNo,
                        ContentTitle = "Update Available",
                        ContentHeader = $"Version {info.Version} is available!",
                        ContentMessage = $"A new version of SaveTracker is available.\n\n" +
                                         $"New Version: {info.Version}\n" +
                                         $"Size: {SaveTracker.Resources.HELPERS.Misc.FormatFileSize(info.DownloadSize)}\n\n" +
                                         $"Do you want to download and install it now?\n\n" +
                                         $"The application will close and restart automatically after the update.",
                        Icon = MsBox.Avalonia.Enums.Icon.Info,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                    EnsureWindowVisible();
                    var result = await confirmBox.ShowWindowDialogAsync(this);

                    if (result == ButtonResult.Yes)
                    {
                        // Create and show progress window
                        var progressWindow = new UpdateProgressWindow();

                        // Start download in background
                        _ = Task.Run(async () =>
                {
                    try
                    {
                        progressWindow.UpdateStatus("Downloading update...");

                        var downloader = new SaveTracker.Resources.Logic.AutoUpdater.UpdateDownloader();
                        downloader.DownloadProgressChanged += (sender, progress) =>
                        {
                            progressWindow.UpdateProgress(progress);
                        };

                        string downloadedFilePath = await downloader.DownloadUpdateAsync(info.DownloadUrl);

                        progressWindow.UpdateStatus("Installing update...");
                        progressWindow.UpdateProgress(100);

                        await Task.Delay(500); // Brief pause to show 100%

                        // Install and restart
                        var installer = new SaveTracker.Resources.Logic.AutoUpdater.UpdateInstaller();
                        await installer.InstallUpdateAsync(downloadedFilePath);

                        // App will exit in InstallUpdateAsync
                    }
                    catch (Exception ex)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            progressWindow.Close();

                            var errorBox = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                            {
                                ButtonDefinitions = ButtonEnum.Ok,
                                ContentTitle = "Update Failed",
                                ContentHeader = "Failed to install update",
                                ContentMessage = $"Error: {ex.Message}",
                                Icon = MsBox.Avalonia.Enums.Icon.Error,
                                WindowStartupLocation = WindowStartupLocation.CenterOwner
                            });

                            await errorBox.ShowWindowDialogAsync(this);
                        });
                    }
                });

                        // Show modal progress window (blocks interaction)
                        await progressWindow.ShowDialog(this);
                    }
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show update dialog");
            }
        }

        private async Task OpenCloudSettingsDialog()
        {
            var viewModel = new CloudSettingsViewModel();
            var view = new UC_CloudSettings
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
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
            };

            viewModel.RequestClose += () => dialog.Close();
            await dialog.ShowDialog(this);

            if (_viewModel != null)
            {
                await _viewModel.ReloadConfigAsync();
            }
        }

        private async void ShowSettingsDialog()
        {
            try
            {
                var settingsWindow = new SettingsWindow
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                EnsureWindowVisible();
                await settingsWindow.ShowDialog(this);
                if (_viewModel != null)
                {
                    await _viewModel.ReloadConfigAsync();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open settings dialog");
            }
        }

        private async Task ShowSmartSyncWindow(SmartSyncViewModel vm)
        {
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    EnsureWindowVisible();
                    var window = new SmartSyncWindow
                    {
                        DataContext = vm,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    await window.ShowDialog(this);
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show Smart Sync window");
            }
        }

        private void TrackedFile_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (sender is Border border && border.DataContext is TrackedFileViewModel fileVm)
            {
                fileVm.OpenFileLocation();
            }
        }

        private void GamePath_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            // TODO: Re-add OpenInstallDirectoryCommand
            //if (_viewModel != null)
            //{
            //    _viewModel.OpenInstallDirectoryCommand.Execute(null);
            //}
        }

        private void MinimizeWindow()
        {
            WindowState = WindowState.Minimized;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WindowStateProperty)
            {
                if (WindowState == WindowState.Minimized)
                {
                    Hide();
                }
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (_viewModel != null)
            {
                _viewModel.OnAddGameRequested -= ShowAddGameDialog;
                _viewModel.OnAddFilesRequested -= ShowAddFilesDialog;
                _viewModel.OnCloudSettingsRequested -= ShowCloudSettings;
                _viewModel.OnBlacklistRequested -= ShowBlacklist;
                _viewModel.OnRcloneSetupRequired -= ShowRcloneSetup;
                _viewModel.OnSettingsRequested -= ShowSettingsDialog;
                _viewModel.RequestMinimize -= MinimizeWindow;
                _viewModel.OnCloudSaveFound -= ShowCloudSaveFoundDialog;
                _viewModel.OnUpdateAvailable -= ShowUpdateDialog;
                _viewModel.OnSmartSyncRequested -= ShowSmartSyncWindow;
            }
        }
    }
}