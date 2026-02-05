using Avalonia.Controls.Notifications;
using Avalonia.Controls;
using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.HELPERS
{
    public class NotificationService : INotificationService
    {
        private readonly WindowNotificationManager? _windowNotificationManager;
        private readonly Window? _mainWindow;

        public NotificationService(
            WindowNotificationManager? windowManager,
            object? nativeManager, // Kept for compatibility but not used
            Window? mainWindow)
        {
            _windowNotificationManager = windowManager;
            _mainWindow = mainWindow;

            // Register the app for Windows notifications
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                try
                {
                    ToastNotificationManagerCompat.OnActivated += toastArgs =>
                    {
                        // Handle notification activation (when user clicks the toast)
                        DebugConsole.WriteInfo($"Toast notification activated: {toastArgs.Argument}");
                    };

                    DebugConsole.WriteInfo("Windows toast notifications registered successfully");
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "Failed to register Windows toast notifications");
                }
            }
        }

        public void Show(string title, string message, NotificationType type = NotificationType.Information)
        {
            // Run async method synchronously
            _ = ShowAsync(title, message, type);
        }

        public async Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information)
        {
            try
            {
                // Check if notifications are enabled in settings
                var config = await ConfigManagement.LoadConfigAsync();
                if (config != null && !config.EnableNotifications)
                {
                    DebugConsole.WriteInfo($"[Notification Disabled] {title}: {message}");
                    return;
                }

                // Check window visibility on UI thread
                bool isWindowVisible = false;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    isWindowVisible = _mainWindow != null && _mainWindow.IsVisible;
                });

                DebugConsole.WriteInfo($"ShowAsync called: {title} - Window visible: {isWindowVisible}");

                // If window is visible and in-app notification manager exists, show in-app toast
                if (isWindowVisible && _windowNotificationManager != null)
                {
                    DebugConsole.WriteInfo("Showing in-app notification");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var avaloniaType = MapToAvaloniaType(type);
                        _windowNotificationManager.Show(new Notification(title, message, avaloniaType));
                    });
                }
                // Otherwise, show native Windows toast notification
                else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
                {
                    DebugConsole.WriteInfo("Showing Windows toast notification");
                    ShowWindowsToast(title, message, type);
                }
                else if (OperatingSystem.IsLinux())
                {
                    DebugConsole.WriteInfo("Showing Linux system notification");
                    ShowLinuxNotification(title, message, type);
                }
                else
                {
                    // Fallback: log to debug console
                    DebugConsole.WriteInfo($"[Notification Fallback] {title}: {message}");
                }
            }
            catch (Exception ex)
            {
                // Fallback to debug console if notification fails
                DebugConsole.WriteException(ex, $"Failed to show notification: {title}");
            }
        }

        private Avalonia.Controls.Notifications.NotificationType MapToAvaloniaType(NotificationType type)
        {
            return type switch
            {
                NotificationType.Information => Avalonia.Controls.Notifications.NotificationType.Information,
                NotificationType.Success => Avalonia.Controls.Notifications.NotificationType.Success,
                NotificationType.Warning => Avalonia.Controls.Notifications.NotificationType.Warning,
                NotificationType.Error => Avalonia.Controls.Notifications.NotificationType.Error,
                _ => Avalonia.Controls.Notifications.NotificationType.Information
            };
        }

        private void ShowLinuxNotification(string title, string message, NotificationType type)
        {
            try
            {
                // Map NotificationType to standard Linux icon names
                string iconName = type switch
                {
                    NotificationType.Error => "dialog-error",
                    NotificationType.Warning => "dialog-warning",
                    NotificationType.Success => "emblem-default",
                    _ => "dialog-information"
                };

                // Use ProcessStartInfo with ArgumentList for safe argument handling (no shell code injection)
                var info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notify-send",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                info.ArgumentList.Add(title);
                info.ArgumentList.Add(message);
                info.ArgumentList.Add("-i");
                info.ArgumentList.Add(iconName);
                info.ArgumentList.Add("-a");
                info.ArgumentList.Add("SaveTracker");

                using (var process = System.Diagnostics.Process.Start(info))
                {
                    process?.WaitForExit(1000);
                }

                DebugConsole.WriteSuccess($"Linux notification dispatched: {title}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to dispatch Linux notification (notify-send).");
            }
        }

        private void ShowWindowsToast(string title, string message, NotificationType type)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                try
                {
                    DebugConsole.WriteInfo($"Building Windows toast: {title}");

                    var toastBuilder = new ToastContentBuilder()
                        .AddText(title)
                        .AddText(message);

                    toastBuilder.Show();

                    DebugConsole.WriteSuccess($"Windows toast notification shown: {title}");
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "Failed to show Windows toast notification");
                    DebugConsole.WriteInfo($"[Notification] {title}: {message}");
                }
            }
            else
            {
                DebugConsole.WriteInfo($"[Notification - Non-Windows] {title}: {message}");
            }
        }
    }
}
