using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC.IPC;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.ViewModels;
using SaveTracker.Views;
using SaveTracker.Views.Dialog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SaveTracker
{
    public class AvaloniaWindowManager : IWindowManager
    {
        public void ShowMainWindow()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWin)
                {
                    mainWin.EnsureWindowVisible();
                }
                else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop2)
                {
                    var mainWinNew = new MainWindow();
                    mainWinNew.Show();
                    mainWinNew.Activate();
                    desktop2.MainWindow = mainWinNew;
                }
            });
        }

        public void ShowLibrary()
        {
            ShowGenericWindow("library", () => new CloudLibraryWindow());
        }

        public void ShowBlacklist()
        {
            ShowGenericWindow("blacklist", () => new BlackListEditor());
        }

        public void ShowCloudSettings()
        {
            Dispatcher.UIThread.Post(() =>
           {
               try
               {
                   var viewModel = new CloudSettingsViewModel();
                   var view = new UC_CloudSettings { DataContext = viewModel };
                   var settingsWindow = new Window
                   {
                       Title = "Cloud Storage Settings",
                       Width = 500,
                       Height = 500,
                       WindowStartupLocation = WindowStartupLocation.CenterScreen,
                       Content = view,
                       SystemDecorations = SystemDecorations.BorderOnly,
                       Background = Avalonia.Media.Brushes.Transparent,
                       TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                       ExtendClientAreaToDecorationsHint = true,
                       WindowState = WindowState.Normal
                   };
                   viewModel.RequestClose += () => settingsWindow.Close();
                   settingsWindow.Show();
                   settingsWindow.Activate();
               }
               catch (Exception ex)
               {
                   DebugConsole.WriteError($"[IPC] Failed to show CloudSettings: {ex.Message}");
               }
           });
        }

        public void ShowSettings()
        {
            ShowGenericWindow("settings", () => new SettingsWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen });
        }

        public void TriggerSmartSync(Game game)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var vm = new SmartSyncViewModel(game, SaveTracker.Models.SmartSyncMode.ManualSync);
                    var window = new SmartSyncWindow
                    {
                        DataContext = vm,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    };
                    window.Show();
                    window.Activate();
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteError($"[IPC] Failed to show SmartSync: {ex.Message}");
                }
            });
        }

        public void ReportIssue()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/KrachDev/SaveTrackerDesktop/issues/new",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to open browser: {ex.Message}");
            }
        }

        public async Task StartSession(Game game)
        {
            // GUI usually handles this via UI interactions, but we can potentially support it.
            // For now, allow Playnite to trigger tracking even in GUI mode?
            // This requires thread hopping and interacting with MainWindowViewModel.
            // Given the complexity and user focus on Headless for Playnite, we'll log a warning for now.
            DebugConsole.WriteWarning("External 'StartSession' command not yet fully supported in GUI mode.");
            await Task.CompletedTask;
        }

        public async Task EndSession(Game game)
        {
            DebugConsole.WriteWarning("External 'EndSession' command not yet fully supported in GUI mode.");
            await Task.CompletedTask;
        }

        private void ShowGenericWindow(string name, Func<Window> creator)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var window = creator();
                    window.Show();
                    window.Activate();
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteError($"[IPC] Failed to show window '{name}': {ex.Message}");
                }
            });
        }
    }
}
