using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC
{
    /// <summary>
    /// Wrapper for Steam game with detected executables and launch options
    /// </summary>
    public class SteamGameWrapper
    {
        public string AppId { get; set; } = "";
        public string GameName { get; set; } = "";
        public string InstallDirectory { get; set; } = "";
        public string PrimaryExecutable { get; set; } = "";
        public List<string> AlternativeExecutables { get; set; } = new();
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// Generates the Steam URL for launching this game
        /// Format: steam://rungameid/{AppID}
        /// </summary>
        public string GetSteamLaunchUrl()
        {
            return $"steam://rungameid/{AppId}";
        }

        /// <summary>
        /// Gets the executable path to use for tracking
        /// Prefers primary, falls back to Steam URL format
        /// </summary>
        public string GetExecutablePathForTracking()
        {
            if (!string.IsNullOrEmpty(PrimaryExecutable) && File.Exists(PrimaryExecutable))
            {
                return PrimaryExecutable;
            }

            // Fallback: Use Steam URL as a pseudo-executable for identification
            // This allows tracking even when we can't directly invoke the exe
            return GetSteamLaunchUrl();
        }

        /// <summary>
        /// Tries to resolve the best executable to use for launching
        /// Returns the primary executable if it exists, with fallbacks
        /// </summary>
        public string GetBestLaunchPath()
        {
            // 1. Primary executable
            if (!string.IsNullOrEmpty(PrimaryExecutable) && File.Exists(PrimaryExecutable))
            {
                return PrimaryExecutable;
            }

            // 2. First alternative
            foreach (var alt in AlternativeExecutables)
            {
                if (File.Exists(alt))
                {
                    return alt;
                }
            }

            // 3. Steam URL (always available)
            return GetSteamLaunchUrl();
        }

        public override string ToString()
        {
            return $"{GameName} (AppID: {AppId}) - {InstallDirectory}";
        }
    }

    /// <summary>
    /// Orchestrates reading Steam library and finding game executables
    /// </summary>
    public class SteamGameImporter
    {
        private readonly SteamLibraryReader _libraryReader;
        private readonly SteamGameFinder _gameFinder;

        public SteamGameImporter()
        {
            _libraryReader = new SteamLibraryReader();
            _gameFinder = new SteamGameFinder();
        }

        /// <summary>
        /// Scans Steam libraries and returns wrapped games ready for import
        /// </summary>
        public List<SteamGameWrapper> ScanSteamLibrary(string steamPath = null)
        {
            var wrappedGames = new List<SteamGameWrapper>();

            try
            {
                DebugConsole.WriteInfo("Scanning Steam library for games...");

                // Step 1: Read games from Steam manifest files
                var steamGames = _libraryReader.ReadSteamGames(steamPath);

                if (steamGames.Count == 0)
                {
                    DebugConsole.WriteWarning("No Steam games found");
                    return wrappedGames;
                }

                DebugConsole.WriteSuccess($"Found {steamGames.Count} Steam games");

                // Step 2: Find executables for each game
                int processedCount = 0;
                foreach (var game in steamGames)
                {
                    try
                    {
                        var execInfo = _gameFinder.FindGameExecutable(game.InstallDirectory, game.GameName);

                        var wrapper = new SteamGameWrapper
                        {
                            AppId = game.AppId,
                            GameName = game.GameName,
                            InstallDirectory = game.InstallDirectory,
                            PrimaryExecutable = execInfo.PrimaryExecutable,
                            AlternativeExecutables = execInfo.AlternativeExecutables,
                            IsSelected = true
                        };

                        wrappedGames.Add(wrapper);
                        processedCount++;

                        DebugConsole.WriteDebug(
                            $"[{processedCount}/{steamGames.Count}] {game.GameName}: {execInfo}"
                        );
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to process {game.GameName}: {ex.Message}");
                    }
                }

                DebugConsole.WriteSuccess($"Processed {processedCount}/{steamGames.Count} games");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to scan Steam library");
            }

            return wrappedGames;
        }

        /// <summary>
        /// Converts a SteamGameWrapper to a Game object for SaveTracker import
        /// </summary>
        public Game ConvertToGame(SteamGameWrapper steamGame)
        {
            return new Game
            {
                Name = steamGame.GameName,
                ExecutablePath = steamGame.GetBestLaunchPath(),
                InstallDirectory = steamGame.InstallDirectory,
                LaunchViaSteam = true,
                // Additional properties will be set by the import dialog
            };
        }

        /// <summary>
        /// Filters games based on user selection
        /// </summary>
        public List<SteamGameWrapper> FilterSelectedGames(List<SteamGameWrapper> games)
        {
            return games.Where(g => g.IsSelected).ToList();
        }

        /// <summary>
        /// Gets import summary statistics
        /// </summary>
        public SteamImportStats GetImportStatistics(List<SteamGameWrapper> games)
        {
            var stats = new SteamImportStats
            {
                TotalGames = games.Count,
                GamesWithExecutable = games.Count(g => !string.IsNullOrEmpty(g.PrimaryExecutable)),
                GamesWithFallback = games.Count(g => string.IsNullOrEmpty(g.PrimaryExecutable)),
                SelectedGames = games.Count(g => g.IsSelected)
            };

            return stats;
        }
    }

    /// <summary>
    /// Statistics for Steam import operation
    /// </summary>
    public class SteamImportStats
    {
        public int TotalGames { get; set; }
        public int GamesWithExecutable { get; set; }
        public int GamesWithFallback { get; set; }
        public int SelectedGames { get; set; }

        public override string ToString()
        {
            return $"Total: {TotalGames}, Direct Exe: {GamesWithExecutable}, " +
                   $"Steam URL Fallback: {GamesWithFallback}, Selected: {SelectedGames}";
        }
    }
}
