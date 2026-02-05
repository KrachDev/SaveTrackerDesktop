using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SaveTracker.Resources.HELPERS.Linux
{
    public static class LauncherDetector
    {
        public static List<string> GetAvailableLaunchers()
        {
            var launchers = new List<string>();

            // Always add System Wine if detected
            if (IsCommandAvailable("wine"))
            {
                launchers.Add("System Wine");
            }

            // Heroic
            if (IsCommandAvailable("heroic") || IsFlatpakInstalled("com.heroicgameslauncher.hgl"))
            {
                launchers.Add("Heroic Games Launcher");
            }

            // Lutris
            if (IsCommandAvailable("lutris") || IsFlatpakInstalled("net.lutris.Lutris"))
            {
                launchers.Add("Lutris");
            }

            // Bottles
            if (IsCommandAvailable("bottles-cli") || IsFlatpakInstalled("com.usebottles.bottles"))
            {
                launchers.Add("Bottles");
            }

            // Custom is always available
            launchers.Add("Custom");

            return launchers;
        }

        private static bool IsCommandAvailable(string command)
        {
            try
            {
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(':');
                if (paths == null) return false;

                return paths.Any(path => File.Exists(Path.Combine(path, command)));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFlatpakInstalled(string appId)
        {
            try
            {
                // Simple heuristic: check user or system flatpak data dirs
                // Robust way: 'flatpak list --app --columns=application' but requires process call.
                // Faster way: Check directory existence? 
                // /var/lib/flatpak/app/{appId} or ~/.local/share/flatpak/app/{appId}

                string userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "flatpak", "app", appId);
                string systemPath = Path.Combine("/var/lib/flatpak/app", appId);

                return Directory.Exists(userPath) || Directory.Exists(systemPath);
            }
            catch
            {
                return false;
            }
        }
    }
}
