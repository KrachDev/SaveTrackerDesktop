using Avalonia.Controls.Notifications;
using Avalonia.Controls;
using System;
using System.Threading.Tasks;
using Avalonia.Threading;
#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.Resources.HELPERS
{
    public interface INotificationService
    {
        void Show(string title, string message, NotificationType type = NotificationType.Information);
        Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information);
    }

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
#if WINDOWS
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
#endif
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
                        _windowNotificationManager.Show(new Notification(title, message, type));
                    });
                }
                // Otherwise, show native Windows toast notification
                else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
                {
                    DebugConsole.WriteInfo("Showing Windows toast notification");
                    ShowWindowsToast(title, message, type);
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

        private void ShowWindowsToast(string title, string message, NotificationType type)
        {
#if WINDOWS
            try
            {
                DebugConsole.WriteInfo($"Building Windows toast: {title}");

                // Build the toast notification
                var toastBuilder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message);

                // Don't try to add app logo override as it requires packaged app
                // Just show simple toast

                DebugConsole.WriteInfo("Calling toastBuilder.Show()");

                // Show the toast
                toastBuilder.Show();

                DebugConsole.WriteSuccess($"Windows toast notification shown: {title}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show Windows toast notification");
                // Final fallback to debug console
                DebugConsole.WriteInfo($"[Notification] {title}: {message}");
            }
#else
            // Fallback for non-Windows platforms
            DebugConsole.WriteInfo($"[Notification - Non-Windows] {title}: {message}");
#endif
        }
    }
}
