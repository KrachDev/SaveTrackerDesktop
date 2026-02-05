using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC
{
    /// <summary>
    /// Reads Steam library configuration and game manifests
    /// </summary>
    public class SteamLibraryReader
    {
        private static readonly string SteamDefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam"
        );

        public SteamLibraryReader()
        {
        }

        /// <summary>
        /// Finds all Steam library folders from libraryfolders.vdf
        /// </summary>
        public List<string> FindSteamLibraryFolders(string steamPath = null)
        {
            steamPath ??= SteamDefaultPath;
            var libraryFolders = new List<string> { steamPath }; // Default library

            try
            {
                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdfPath))
                {
                    DebugConsole.WriteWarning($"libraryfolders.vdf not found at {vdfPath}");
                    return libraryFolders;
                }

                string content = File.ReadAllText(vdfPath);
                
                // Parse VDF format: "path"		"C:\\Games\\Steam"
                var matches = Regex.Matches(content, @"""path""\s+""(.+?)""");
                foreach (Match match in matches)
                {
                    string path = match.Groups[1].Value;
                    // Unescape double backslashes
                    path = path.Replace("\\\\", "\\");
                    
                    if (Directory.Exists(path) && !libraryFolders.Contains(path))
                    {
                        libraryFolders.Add(path);
                        DebugConsole.WriteInfo($"Found Steam library: {path}");
                    }
                }

                return libraryFolders;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to read Steam library folders");
                return libraryFolders;
            }
        }

        /// <summary>
        /// Reads all games from Steam libraries
        /// </summary>
        public List<SteamGameInfo> ReadSteamGames(string steamPath = null)
        {
            steamPath ??= SteamDefaultPath;
            var games = new List<SteamGameInfo>();

            try
            {
                var libraryFolders = FindSteamLibraryFolders(steamPath);

                foreach (var libraryPath in libraryFolders)
                {
                    string steamappsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamappsPath))
                        continue;

                    var libGames = ReadGamesFromLibrary(steamappsPath);
                    games.AddRange(libGames);
                }

                DebugConsole.WriteSuccess($"Found {games.Count} Steam games across all libraries");
                return games;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to read Steam games");
                return games;
            }
        }

        /// <summary>
        /// Reads games from a single library folder (steamapps)
        /// </summary>
        private List<SteamGameInfo> ReadGamesFromLibrary(string steamappsPath)
        {
            var games = new List<SteamGameInfo>();

            try
            {
                // Find all appmanifest_*.acf files
                var manifestFiles = Directory.GetFiles(steamappsPath, "appmanifest_*.acf");

                foreach (var manifestFile in manifestFiles)
                {
                    try
                    {
                        var game = ParseManifestFile(manifestFile, steamappsPath);
                        if (game != null && !string.IsNullOrEmpty(game.GameName))
                        {
                            games.Add(game);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to parse manifest {manifestFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to read games from library {steamappsPath}");
            }

            return games;
        }

        /// <summary>
        /// Parses appmanifest_*.acf file to extract game info
        /// </summary>
        private SteamGameInfo ParseManifestFile(string manifestPath, string steamappsPath)
        {
            try
            {
                string content = File.ReadAllText(manifestPath);
                
                string appId = ExtractVdfValue(content, "appid");
                string gameName = ExtractVdfValue(content, "name");
                string installDir = ExtractVdfValue(content, "installdir");
                string stateFlags = ExtractVdfValue(content, "StateFlags");

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(gameName))
                    return null;

                // Only include installed games (StateFlags should be 4)
                if (stateFlags == "4")
                {
                    string fullInstallPath = Path.Combine(steamappsPath, "common", installDir);

                    return new SteamGameInfo
                    {
                        AppId = appId,
                        GameName = gameName.Trim(),
                        InstallDirectory = fullInstallPath,
                        IsInstalled = Directory.Exists(fullInstallPath),
                        ManifestPath = manifestPath
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error parsing manifest {manifestPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts value from VDF format: "key"		"value"
        /// </summary>
        private string ExtractVdfValue(string content, string key)
        {
            try
            {
                // Match: "key"  <whitespace>  "value"
                var pattern = $@"""{key}""\s+""(.+?)""";
                var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
                
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Steam game information extracted from manifest
    /// </summary>
    public class SteamGameInfo
    {
        public string AppId { get; set; } = "";
        public string GameName { get; set; } = "";
        public string InstallDirectory { get; set; } = "";
        public bool IsInstalled { get; set; }
        public string ManifestPath { get; set; } = "";
        public string ExecutablePath { get; set; } = ""; // Set by SteamGameFinder
        public List<string> AlternativeExecutables { get; set; } = new();

        public override string ToString()
        {
            return $"{GameName} (AppID: {AppId}) - {InstallDirectory}";
        }
    }
}
