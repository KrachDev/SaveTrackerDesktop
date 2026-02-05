using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.ViewModels;
using SaveTracker.Views;
using SaveTracker.Views.Dialog;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SaveTracker
{
    public partial class App : Application
    {
        public IRelayCommand OpenCommand { get; }
        public IRelayCommand ExitCommand { get; }

        public TrayIcon? TrayIcon => _trayIcon;
        private TrayIcon? _trayIcon;

        public App()
        {
            OpenCommand = new RelayCommand(OpenWindow);
            ExitCommand = new RelayCommand(ExitApp);
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Disable data validation
            DisableAvaloniaDataAnnotationValidation();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Load Config
                Config config;
                try
                {
                    // Use ConfigureAwait(false) to avoid deadlock on UI thread
                    config = ConfigManagement.LoadConfigAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to load config on startup: {ex.Message}");
                    config = new Config();
                }

                // Apply Debug Console Setting
                Console.WriteLine($"[DEBUG] Loaded Config ShowDebugConsole: {config.ShowDebugConsole}");
                DebugConsole.Enable(config.ShowDebugConsole);
                if (config.ShowDebugConsole)
                {
                    DebugConsole.WriteInfo("Debug Console Enabled via Config");
                }

                // Initialize Main Window
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                // Create notification service
                var notificationService = new NotificationService(
                    mainWindow.NotificationManager,
                    null,
                    mainWindow
                );

                // Initialize ViewModel with notification service
                var viewModel = new MainWindowViewModel(notificationService);
                mainWindow.DataContext = viewModel;

                // Apply Start Minimized Setting
                if (config.StartMinimized)
                {
                    // We set it to minimized. MainWindow.axaml.cs has logic to Hide() when minimized.
                    mainWindow.WindowState = WindowState.Minimized;
                    DebugConsole.WriteInfo("Starting application minimized.");
                }

                // Initialize Tray Icon
                InitializeTrayIcon();

                // Show announcement window if new version
                var updateChecker = new Resources.Logic.AutoUpdater.UpdateChecker();
                var currentVersion = updateChecker.GetCurrentVersion();

                _ = Task.Run(async () =>
                {
                    if (await SaveTracker.Resources.SAVE_SYSTEM.VersionManager.ShouldShowAnnouncementAsync(currentVersion))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            var announcementWindow = new AnnouncementWindow();
                            announcementWindow.Show();
                            DebugConsole.WriteInfo($"Showing announcement window for version {currentVersion}");
                        });
                    }
                });

                // Upload analytics to Firebase if due
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (await NetworkHelper.IsInternetAvailableAsync())
                        {
                            await SaveTracker.Resources.Logic.AnalyticsService.UploadToFirebaseAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Background analytics upload failed: {ex.Message}");
                    }
                });

                // Cloud Library Cache specific startup logic
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var cacheService = SaveTracker.Resources.Logic.RecloneManagement.CloudLibraryCacheService.Instance;
                        string metadataPath = SaveTracker.Resources.Logic.RecloneManagement.CloudLibraryCacheService.MetadataPath;

                        // Check if cache exists
                        bool cacheExists = System.IO.File.Exists(metadataPath);

                        if (!cacheExists)
                        {
                            // If no cache, start caching immediately (but still in background to not block startup)
                            DebugConsole.WriteInfo("[CloudCache] No local cache found. Starting initial cache build...");
                            if (await NetworkHelper.IsInternetAvailableAsync())
                            {
                                var summary = await cacheService.RefreshCacheAsync();
                                
                                // Show App ID input dialog if needed
                                if (summary?.NeedsInputCount > 0)
                                {
                                    var gamesNeedingInput = await cacheService.GetGamesNeedingAppIdAsync();
                                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                                    {
                                        var dialogVm = new AppIdEntryViewModel(gamesNeedingInput.ToArray());
                                        var dialog = new Views.Dialog.AppIdEntryWindow(dialogVm);

                                        // Try to find a parent window
                                        Window? parent = null;
                                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                                        {
                                            parent = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
                                        }

                                        var result = await dialog.ShowDialog<bool?>(parent);

                                        if (result == true)
                                        {
                                            DebugConsole.WriteInfo("[CloudCache] Processing manual App IDs from startup...");
                                            foreach (var game in dialogVm.Games)
                                            {
                                                if (!string.IsNullOrWhiteSpace(game.AppId))
                                                {
                                                    await cacheService.SetManualAppId(game.GameName, game.AppId);
                                                    await cacheService.RetryAchievementProcessing(game.GameName);
                                                }
                                            }
                                        }
                                    });
                                }
                            }
                        }
                        else
                        {
                            // If cache exists, wait 10 minutes before refreshing
                            await Task.Delay(TimeSpan.FromMinutes(10));

                            if (await NetworkHelper.IsInternetAvailableAsync())
                            {
                                DebugConsole.WriteInfo("[CloudCache] Starting scheduled cache refresh (10 min after startup)");
                                var summary = await cacheService.RefreshCacheAsync();
                                
                                // Show App ID input dialog if needed
                                if (summary?.NeedsInputCount > 0)
                                {
                                    var gamesNeedingInput = await cacheService.GetGamesNeedingAppIdAsync();
                                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                                    {
                                        var dialogVm = new AppIdEntryViewModel(gamesNeedingInput.ToArray());
                                        var dialog = new Views.Dialog.AppIdEntryWindow(dialogVm);

                                        // Try to find a parent window
                                        Window? parent = null;
                                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                                        {
                                            parent = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
                                        }

                                        var result = await dialog.ShowDialog<bool?>(parent);

                                        if (result == true)
                                        {
                                            DebugConsole.WriteInfo("[CloudCache] Processing manual App IDs from scheduled refresh...");
                                            foreach (var game in dialogVm.Games)
                                            {
                                                if (!string.IsNullOrWhiteSpace(game.AppId))
                                                {
                                                    await cacheService.SetManualAppId(game.GameName, game.AppId);
                                                    await cacheService.RetryAchievementProcessing(game.GameName);
                                                }
                                            }
                                        }
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Background cloud cache refresh failed: {ex.Message}");
                    }
                });
            }


            base.OnFrameworkInitializationCompleted();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                var iconUri = new Uri("avares://SaveTracker/Assets/avalonia-logo.ico");
                var icon = new WindowIcon(AssetLoader.Open(iconUri));

                _trayIcon = new TrayIcon
                {
                    Icon = icon,
                    ToolTipText = "SaveTracker Desktop",
                    Command = OpenCommand
                };

                var menu = new NativeMenu();

                var openItem = new NativeMenuItem("Open");
                openItem.Command = OpenCommand;
                menu.Items.Add(openItem);

                menu.Items.Add(new NativeMenuItemSeparator());

                var exitItem = new NativeMenuItem("Exit");
                exitItem.Command = ExitCommand;
                menu.Items.Add(exitItem);

                _trayIcon.Menu = menu;

                // Add to application tray icons
                var trayIcons = TrayIcon.GetIcons(this);
                trayIcons?.Add(_trayIcon);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize tray icon: {ex.Message}");
            }
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }

        private void OpenWindow()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow != null)
                {
                    desktop.MainWindow.Show();
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.Activate();
                }
            }
        }

        private void ExitApp()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}