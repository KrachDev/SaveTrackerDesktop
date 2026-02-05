using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SaveTracker.Resources.HELPERS;

#if WINDOWS
using Microsoft.Win32;
#endif

namespace SaveTracker.Resources.LOGIC.Steam
{
    /// <summary>
    /// Scans the Steam installation for installed games.
    /// </summary>
    public static class SteamLibraryScanner
    {
        private const string LIBRARY_FOLDERS_VDF = "libraryfolders.vdf";
        private const string STEAM_APPS_FOLDER = "steamapps";

        /// <summary>
        /// Gets the Steam installation path from the system.
        /// </summary>
        public static string? GetSteamInstallPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetSteamPathWindows();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return GetSteamPathLinux();
            }

            DebugConsole.WriteWarning("[SteamScanner] Unsupported OS for Steam detection");
            return null;
        }

        private static string? GetSteamPathWindows()
        {
#if WINDOWS
            try
            {
                // Try 64-bit registry first
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam");
                if (key != null)
                {
                    var path = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        DebugConsole.WriteDebug($"[SteamScanner] Found Steam at: {path}");
                        return path;
                    }
                }

                // Try 32-bit registry
                using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key32 != null)
                {
                    var path = key32.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        DebugConsole.WriteDebug($"[SteamScanner] Found Steam at: {path}");
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "[SteamScanner] Registry access failed");
            }
#endif
            // Fallback to common paths
            var defaultPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
            };

            foreach (var path in defaultPaths)
            {
                if (Directory.Exists(path))
                {
                    DebugConsole.WriteDebug($"[SteamScanner] Found Steam at fallback path: {path}");
                    return path;
                }
            }

            return null;
        }

        private static string? GetSteamPathLinux()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var possiblePaths = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "debian-installation"),
                "/usr/share/steam",
                "/usr/local/share/steam"
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    DebugConsole.WriteDebug($"[SteamScanner] Found Steam at: {path}");
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all Steam library folders from libraryfolders.vdf
        /// </summary>
        public static List<string> GetLibraryFolders(string? steamPath = null)
        {
            var folders = new List<string>();

            steamPath ??= GetSteamInstallPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                DebugConsole.WriteWarning("[SteamScanner] Steam not found");
                return folders;
            }

            // The main Steam folder is always a library
            string mainSteamApps = Path.Combine(steamPath, STEAM_APPS_FOLDER);
            if (Directory.Exists(mainSteamApps))
            {
                folders.Add(mainSteamApps);
            }

            // Parse libraryfolders.vdf for additional libraries
            string vdfPath = Path.Combine(steamPath, STEAM_APPS_FOLDER, LIBRARY_FOLDERS_VDF);

            if (!File.Exists(vdfPath))
            {
                DebugConsole.WriteWarning($"[SteamScanner] libraryfolders.vdf not found at: {vdfPath}");
                return folders;
            }

            var vdf = VdfParser.ParseFile(vdfPath);
            if (vdf == null)
            {
                DebugConsole.WriteWarning("[SteamScanner] Failed to parse libraryfolders.vdf");
                return folders;
            }

            // The root node contains "libraryfolders" child
            var libraryFolders = vdf.GetChild("libraryfolders") ?? vdf;

            // Each library is numbered (0, 1, 2, etc.)
            foreach (var kvp in libraryFolders.Children)
            {
                // Check if key is a number (library index)
                if (int.TryParse(kvp.Key, out _))
                {
                    var libraryNode = kvp.Value;
                    string? libraryPath = libraryNode.GetValue("path");

                    if (!string.IsNullOrEmpty(libraryPath))
                    {
                        string steamAppsPath = Path.Combine(libraryPath, STEAM_APPS_FOLDER);
                        if (Directory.Exists(steamAppsPath) && !folders.Contains(steamAppsPath, StringComparer.OrdinalIgnoreCase))
                        {
                            folders.Add(steamAppsPath);
                            DebugConsole.WriteDebug($"[SteamScanner] Found library folder: {steamAppsPath}");
                        }
                    }
                }
            }

            DebugConsole.WriteInfo($"[SteamScanner] Found {folders.Count} Steam library folder(s)");
            return folders;
        }

        /// <summary>
        /// Scans all library folders for installed games.
        /// </summary>
        public static List<SteamGameInfo> GetInstalledGames(string? steamPath = null)
        {
            var games = new List<SteamGameInfo>();
            var libraryFolders = GetLibraryFolders(steamPath);

            foreach (var libraryPath in libraryFolders)
            {
                try
                {
                    // Find all appmanifest_*.acf files
                    var manifestFiles = Directory.GetFiles(libraryPath, "appmanifest_*.acf");

                    foreach (var manifestPath in manifestFiles)
                    {
                        var gameInfo = ParseAppManifest(manifestPath, libraryPath);
                        if (gameInfo != null)
                        {
                            games.Add(gameInfo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"[SteamScanner] Error scanning library: {libraryPath}");
                }
            }

            DebugConsole.WriteSuccess($"[SteamScanner] Found {games.Count} installed Steam game(s)");
            return games.OrderBy(g => g.Name).ToList();
        }

        /// <summary>
        /// Parses an appmanifest_*.acf file to extract game information.
        /// </summary>
        private static SteamGameInfo? ParseAppManifest(string manifestPath, string libraryPath)
        {
            try
            {
                var vdf = VdfParser.ParseFile(manifestPath);
                if (vdf == null)
                    return null;

                // The root contains "AppState" child
                var appState = vdf.GetChild("AppState") ?? vdf;

                string? appId = appState.GetValue("appid");
                string? name = appState.GetValue("name");
                string? installDir = appState.GetValue("installdir");

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir))
                {
                    return null;
                }

                // Construct the full install path
                string fullInstallPath = Path.Combine(libraryPath, "common", installDir);

                // Verify the directory exists
                if (!Directory.Exists(fullInstallPath))
                {
                    DebugConsole.WriteDebug($"[SteamScanner] Install directory not found for {name}: {fullInstallPath}");
                    return null;
                }

                return new SteamGameInfo
                {
                    AppId = appId,
                    Name = name,
                    InstallDirectory = fullInstallPath,
                    LibraryPath = libraryPath
                };
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"[SteamScanner] Failed to parse manifest: {manifestPath}");
                return null;
            }
        }

        /// <summary>
        /// Scans a game's install directory for executable files.
        /// Returns the main exe if identifiable, or a list of all exes.
        /// </summary>
        public static List<string> ScanForExecutables(string installDirectory)
        {
            var executables = new List<string>();

            if (string.IsNullOrEmpty(installDirectory) || !Directory.Exists(installDirectory))
                return executables;

            try
            {
                // Get all .exe files in the install directory and subdirectories
                var exeFiles = Directory.GetFiles(installDirectory, "*.exe", SearchOption.AllDirectories);

                // Filter out common non-game executables
                var excludePatterns = new[]
                {
                    "unins", "setup", "redist", "vcredist", "dxsetup", "dotnet",
                    "UnityCrashHandler", "CrashReporter", "launcher", "updater",
                    "UE4PrereqSetup", "UEPrereqSetup"
                };

                foreach (var exe in exeFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();

                    // Skip common non-game executables
                    bool shouldExclude = excludePatterns.Any(pattern =>
                        fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase));

                    if (!shouldExclude)
                    {
                        executables.Add(exe);
                    }
                }

                // Sort by likelihood of being the main executable
                // (files in root directory first, then by name similarity to folder name)
                string folderName = Path.GetFileName(installDirectory)?.ToLowerInvariant() ?? "";
                executables = executables
                    .OrderBy(e => Path.GetDirectoryName(e)?.Equals(installDirectory, StringComparison.OrdinalIgnoreCase) == true ? 0 : 1)
                    .ThenBy(e => LevenshteinDistance(Path.GetFileNameWithoutExtension(e).ToLowerInvariant(), folderName))
                    .ToList();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"[SteamScanner] Failed to scan for executables: {installDirectory}");
            }

            return executables;
        }

        /// <summary>
        /// Simple Levenshtein distance for sorting executables by name similarity.
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int[,] d = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s1.Length, s2.Length];
        }
    }

    /// <summary>
    /// Represents information about an installed Steam game.
    /// </summary>
    public class SteamGameInfo
    {
        /// <summary>
        /// Steam Application ID.
        /// </summary>
        public string AppId { get; set; } = "";

        /// <summary>
        /// Display name of the game.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Full path to the game's installation directory.
        /// </summary>
        public string InstallDirectory { get; set; } = "";

        /// <summary>
        /// Path to the Steam library containing this game.
        /// </summary>
        public string LibraryPath { get; set; } = "";

        /// <summary>
        /// Detected executable files in the install directory.
        /// </summary>
        public List<string> DetectedExecutables { get; set; } = new();

        /// <summary>
        /// Returns the Steam URL to launch this game.
        /// </summary>
        public string GetSteamLaunchUrl() => $"steam://rungameid/{AppId}";

        public override string ToString() => $"{Name} (AppID: {AppId})";
    }
}
