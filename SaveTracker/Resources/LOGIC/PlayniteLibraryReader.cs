using LiteDB;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SaveTracker.Resources.LOGIC
{
    /// <summary>
    /// Reads game data from Playnite's LiteDB database
    /// </summary>
    public class PlayniteLibraryReader
    {
        public PlayniteLibraryReader(string playniteInstallPath)
        {
            // Constructor kept empty or removed if no longer needed, 
            // but ReadGamesFromJson is instance method so we might need a default ctor or change to static.
            // For now, let's keep a default constructor.
        }



        /// <summary>
        /// Reads games from a Playnite JSON export file
        /// </summary>
        /// <summary>
        /// Reads games from a Playnite JSON export file
        /// </summary>
        public List<PlayniteGame> ReadGamesFromJson(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
                throw new FileNotFoundException($"JSON file not found at: {jsonFilePath}");

            var games = new List<PlayniteGame>();

            try
            {
                var jsonContent = File.ReadAllText(jsonFilePath);
                var jsonGames = Newtonsoft.Json.JsonConvert.DeserializeObject<List<dynamic>>(jsonContent);

                if (jsonGames == null)
                    return games;

                DebugConsole.WriteInfo($"Found {jsonGames.Count} games in JSON file");

                foreach (var jsonGame in jsonGames)
                {
                    try
                    {
                        var game = new PlayniteGame();

                        // Parse basic info
                        if (jsonGame.Id != null) game.Id = jsonGame.Id.ToString();
                        if (jsonGame.Name != null) game.Name = jsonGame.Name.ToString();

                        // Parse install directory
                        if (jsonGame.InstallDirectory != null)
                            game.InstallDirectory = jsonGame.InstallDirectory.ToString();

                        // Parse install state
                        bool jsonIsInstalled = false;
                        if (jsonGame.IsInstalled != null)
                            bool.TryParse(jsonGame.IsInstalled.ToString(), out jsonIsInstalled);

                        // Strict check: We need both the directory and the installed flag
                        if (string.IsNullOrEmpty(game.InstallDirectory) || !jsonIsInstalled)
                        {
                            game.IsInstalled = false;
                        }
                        else
                        {
                            // Verify directory actually exists
                            game.IsInstalled = Directory.Exists(game.InstallDirectory);
                        }

                        // Parse Executable from GameActions
                        // We look for the first action that has a "Path" defined
                        string exePath = string.Empty;
                        if (game.IsInstalled && jsonGame.GameActions != null)
                        {
                            foreach (var action in jsonGame.GameActions)
                            {
                                if (action.Path != null)
                                {
                                    string rawPath = action.Path.ToString();

                                    // Resolve {InstallDir} placeholder
                                    if (rawPath.Contains("{InstallDir}") && !string.IsNullOrEmpty(game.InstallDirectory))
                                    {
                                        rawPath = rawPath.Replace("{InstallDir}", game.InstallDirectory);
                                    }

                                    // Check if this looks like an executable and exists
                                    if (File.Exists(rawPath)) // Strict check
                                    {
                                        exePath = rawPath;
                                        break; // Found one!
                                    }
                                }
                            }
                        }

                        game.ExecutablePath = exePath;

                        // Final validity check: Must have Name, InstallDir, and an Executable found
                        if (!string.IsNullOrEmpty(game.Name) &&
                            !string.IsNullOrEmpty(game.InstallDirectory) &&
                            !string.IsNullOrEmpty(game.ExecutablePath))
                        {
                            // Logic continues...
                        }
                        else
                        {
                            game.IsInstalled = false;
                        }

                        // We add everything, let the ViewModel sort them into Installed/NotInstalled lists
                        if (!string.IsNullOrEmpty(game.Name))
                        {
                            games.Add(game);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to parse a game from JSON: {ex.Message}");
                    }
                }

                DebugConsole.WriteSuccess($"Successfully parsed {games.Count} valid games from JSON");
                return games;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to read Playnite JSON export");
                throw;
            }
        }

        /// <summary>
        /// Converts PlayniteGames to SaveTracker Game objects
        /// </summary>
        public static List<Game> ConvertToSaveTrackerGames(List<PlayniteGame> playniteGames)
        {
            var games = new List<Game>();

            foreach (var pg in playniteGames)
            {
                // Skip games without install directory or executable
                if (string.IsNullOrEmpty(pg.InstallDirectory) || string.IsNullOrEmpty(pg.ExecutablePath))
                    continue;

                var game = new Game
                {
                    Name = pg.Name,
                    InstallDirectory = pg.InstallDirectory,
                    ExecutablePath = pg.ExecutablePath,
                    LastTracked = DateTime.MinValue
                };

                games.Add(game);
            }

            return games;
        }
    }

    /// <summary>
    /// Represents a game from Playnite's library
    /// </summary>
    public class PlayniteGame
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? InstallDirectory { get; set; }
        public string? ExecutablePath { get; set; }
        public string? GameImagePath { get; set; }
        public bool IsInstalled { get; set; }
    }
}
