using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SaveTracker.Resources.HELPERS
{
    public class FilePathFilter
    {
        private readonly HashSet<string> _allowedBasePaths;
        private readonly HashSet<string> _deniedBasePaths;

        public FilePathFilter(string gamePath, int userId = 0, int appId = 0)
        {
            _allowedBasePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Game Install Directory
            if (!string.IsNullOrEmpty(gamePath))
            {
                _allowedBasePaths.Add(Path.GetFullPath(gamePath));
            }

            // 2. Common Save Locations
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // Add standard save paths
            // We can't just allow ALL of AppData, but we can allow the game's specific folders if we knew them.
            // Since we don't know the exact folder name the game uses in AppData, we might have to be slightly permissive 
            // BUT strict on the deny list.
            // OR: The user's code implies we track everything the process touches, but we rely on the DENY list to filter junk.
            // THE USER SAID: "Must be in an allowed path"

            // Let's add the specific paths usually associated with games
            _allowedBasePaths.Add(Path.Combine(documents, "My Games"));
            _allowedBasePaths.Add(Path.Combine(documents, "Saved Games"));

            // We'll allow the root of Documents/AppData but rely on Process PID to filter usually.
            // But since the user wants STRICT path filtering, we should probably allow the variable paths.
            // Wait, if I restrict to "GameName" subfolder in AppData, I need to know the GameName used on disk.
            // Use the Game object's Name? often matches.

            // User provided example:
            // Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameName"),
            // Since we might not know the exact folder name (e.g. "Create" vs "Creative Assembly"), 
            // maybe we should allow the parent roots but enforce extensions/denylist?
            // Refined approach: Allow standard roots, but DENY everything else.

            _allowedBasePaths.Add(appData);
            _allowedBasePaths.Add(localAppData);
            _allowedBasePaths.Add(documents);
            _allowedBasePaths.Add(Path.Combine(home, "Saved Games"));

            // 3. Steam Cloud (if applicable)
            if (userId > 0 && appId > 0)
            {
                string steamPath = @"C:\Program Files (x86)\Steam\userdata";
                // Try to find actual steam path if possible, but hardcoding provided by user example
                // Better: allow the specific userdata path
                string steamUserPath = Path.Combine(steamPath, userId.ToString(), appId.ToString(), "remote");
                _allowedBasePaths.Add(steamUserPath);
            }

            _deniedBasePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\Windows",
                @"C:\Program Files", // Too broad, but we allowed GameInstallDir specifically which overrides
                @"C:\Program Files (x86)",
                @"C:\ProgramData",
                @"C:\$Extend",
                Path.Combine(home, "AppData", "Local", "BraveSoftware"),
                Path.Combine(home, "AppData", "Local", "Google"),
                Path.Combine(home, "AppData", "Local", "Microsoft"),
                Path.Combine(home, "AppData", "Roaming", "Microsoft"),
                Path.Combine(home, "AppData", "Local", "Temp")
            };
        }

        public bool ShouldTrack(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(filePath);
            }
            catch
            {
                return false;
            }

            // 1. Check PERMITTED lists first (The Game Install Dir is the most important)
            // If it's in the Game Install Directory, we generally trust it (unless it's a denied subfolder? unlikely)
            bool isExplicitlyAllowed = _allowedBasePaths.Any(allowed =>
                fullPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase));

            // Small fix: If the allowed path is a parent of a denied path (e.g. Program Files vs Program Files/Brave),
            // We need to be careful.
            // Actually, the Denied list should take precedence.

            // 2. Check DENIED lists
            if (_deniedBasePaths.Any(denied => fullPath.StartsWith(denied, StringComparison.OrdinalIgnoreCase)))
            {
                // Exception: If the denied path is a PARENT of an Allowed path (rare), we might have an issue.
                // But generally:
                // Allowed: C:\Games\MyGame
                // Denied: C:\Games (hypothetically) -> Blocked.

                // Exception: If the file is strictly inside the Game Install Directory, we ALWAYS allow it
                // (Unless the game install dir itself is in a bad place like Windows folder? Unlikely).
                bool insideInstallDir = _allowedBasePaths.FirstOrDefault() != null &&
                                        fullPath.StartsWith(_allowedBasePaths.First(), StringComparison.OrdinalIgnoreCase);

                if (insideInstallDir) return true;

                return false;
            }

            return isExplicitlyAllowed;
        }
    }
}
