using System;
using System.Diagnostics;
using System.Security.Principal;

namespace SaveTracker.Resources.HELPERS
{
    public class AdminPrivilegeHelper
    {
        public bool IsAdministrator()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                return true; // Assume admin on non-Windows for now or handle appropriately
            }
            catch
            {
                return false;
            }
        }

        public void RestartAsAdmin()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = exePath ?? "SaveTracker.exe",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(processInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to restart as admin");
            }
        }
    }
}
