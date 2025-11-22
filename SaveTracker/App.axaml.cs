using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.ViewModels;
using SaveTracker.Views;
using SaveTracker.Views.Dialog;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using Avalonia.Platform;

namespace SaveTracker
{
    public partial class App : Application
    {
        public IRelayCommand OpenCommand { get; }
        public IRelayCommand ExitCommand { get; }

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
                var config = ConfigManagement.LoadConfigAsync().GetAwaiter().GetResult();

                // Apply Debug Console Setting
                DebugConsole.Enable(config.ShowDebugConsole);
                if (config.ShowDebugConsole)
                {
                    DebugConsole.WriteInfo("Debug Console Enabled via Config");
                }

                // Check Admin Privileges
                if (!IsAdministrator())
                {
                    // If not admin, we might want to prompt. 
                    // Since we can't easily await a dialog here, we'll log it for now.
                    // Ideally, we would show a dialog or restart.
                    // For now, let's just log a warning.
                    DebugConsole.WriteWarning("Application is not running as Administrator. Some features may not work.");

                    // TODO: Implement proper Admin Prompt here if needed, possibly by setting MainWindow to the prompt first.
                }

                // Initialize Main Window
                var mainWindow = new MainWindow();
                var viewModel = new MainWindowViewModel();
                mainWindow.DataContext = viewModel;
                desktop.MainWindow = mainWindow;

                // Apply Start Minimized Setting
                if (config.StartMinimized)
                {
                    // We set it to minimized. MainWindow.axaml.cs has logic to Hide() when minimized.
                    mainWindow.WindowState = WindowState.Minimized;
                    DebugConsole.WriteInfo("Starting application minimized.");
                }

                // Initialize Tray Icon
                InitializeTrayIcon();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void RestartAsAdmin()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    UseShellExecute = true,
                    Verb = "runas" // Request elevation
                };

                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to restart as admin: {ex.Message}");
            }
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