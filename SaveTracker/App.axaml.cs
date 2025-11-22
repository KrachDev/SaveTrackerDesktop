using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.ViewModels;
using SaveTracker.Views;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;

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
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validation from both Avalonia and CT
                DisableAvaloniaDataAnnotationValidation();

                InitializeTrayIcon();

                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private bool IsRunningAsAdministrator()
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