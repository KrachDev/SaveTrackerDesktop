using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace SaveTracker.Resources.HELPERS
{
    public static class StartupManager
    {
        private const string AppName = "SaveTrackerDesktop";

        public static void SetStartup(bool enable)
        {
            try
            {
                string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (key == null)
                    {
                        DebugConsole.WriteError("Failed to open Registry Run key.");
                        return;
                    }

                    if (enable)
                    {
                        string? modulePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(modulePath))
                        {
                            // Add quotes to handle spaces in path
                            key.SetValue(AppName, $"\"{modulePath}\"");
                            DebugConsole.WriteSuccess($"Added {AppName} to startup.");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                        DebugConsole.WriteSuccess($"Removed {AppName} from startup.");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to change startup settings");
            }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, false))
                {
                    if (key == null) return false;
                    return key.GetValue(AppName) != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
