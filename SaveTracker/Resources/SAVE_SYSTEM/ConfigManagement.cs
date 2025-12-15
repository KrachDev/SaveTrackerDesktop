using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SaveTracker.Resources.Logic;

namespace SaveTracker.Resources.SAVE_SYSTEM
{
    public class ConfigManagement
    {
        public static string BASE_PATH = AppContext.BaseDirectory;
        public static string CONFIG_PATH = Path.Combine(BASE_PATH, "Data", "config.json");
        public static string GAMESLIST_PATH = Path.Combine(BASE_PATH, "Data", "gameslist.json");
        public static string CLOUD_GAMES_PATH = Path.Combine(BASE_PATH, "Data", "cloud_games.json");


        public ConfigManagement()
        {
            // Create Data directory if it doesn't exist
            string dataDirectory = Path.Combine(BASE_PATH, "Data");
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
                DebugConsole.WriteInfo($"Created Data directory: {dataDirectory}");
            }

            // Create config.json if it doesn't exist
            if (!File.Exists(CONFIG_PATH))
            {
                var defaultConfig = new Config();
                var json = JsonSerializer.Serialize(defaultConfig, JsonHelper.DefaultIndented);
                File.WriteAllText(CONFIG_PATH, json);
                DebugConsole.WriteSuccess("Created default config.json");
            }

            // Create gameslist.json if it doesn't exist
            if (!File.Exists(GAMESLIST_PATH))
            {
                var emptyList = new List<Game>();
                var json = JsonSerializer.Serialize(emptyList, JsonHelper.DefaultIndented);
                File.WriteAllText(GAMESLIST_PATH, json);
                DebugConsole.WriteSuccess("Created empty gameslist.json");
            }
        }
        #region Game Save/Load Methods

        /// <summary>
        /// Saves a single game to the games list. If game already exists (by Name), it updates it.
        /// </summary>
        public static async Task SaveGameAsync(Game game)
        {
            try
            {
                var games = await LoadAllGamesAsync();

                // Check if game already exists
                var existingGame = games.FirstOrDefault(g => g.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

                if (existingGame != null)
                {
                    // Update existing game
                    var index = games.IndexOf(existingGame);
                    games[index] = game;
                }
                else
                {
                    // Add new game
                    games.Add(game);
                }

                // Save the entire list
                await SaveAllGamesAsync(games);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save game '{game.Name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves a single game synchronously
        /// </summary>
        public static void SaveGame(Game game)
        {
            SaveGameAsync(game).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Saves the entire games list to JSON
        /// </summary>
        public static async Task SaveAllGamesAsync(List<Game> games)
        {
            try
            {
                var json = JsonSerializer.Serialize(games, JsonHelper.DefaultIndentedCaseInsensitive);

                await File.WriteAllTextAsync(GAMESLIST_PATH, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save games list: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads a single game by name
        /// </summary>
        public static async Task<Game?> LoadGameAsync(string gameName)
        {
            try
            {
                var games = await LoadAllGamesAsync();
                return games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load game '{gameName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads a single game synchronously
        /// </summary>
        public static Game? LoadGame(string gameName)
        {
            return LoadGameAsync(gameName).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Loads all games from the JSON file
        /// </summary>
        public static async Task<List<Game>> LoadAllGamesAsync()
        {
            try
            {
                if (!File.Exists(GAMESLIST_PATH))
                {
                    return new List<Game>();
                }

                var json = await File.ReadAllTextAsync(GAMESLIST_PATH);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<Game>();
                }

                var games = JsonSerializer.Deserialize<List<Game>>(json, JsonHelper.DefaultCaseInsensitive);

                if (games == null || games.Count == 0)
                {
                    return new List<Game>();
                }

                // Check if each game's executable still exists and remove if missing
                var gamesToRemove = new System.Collections.Concurrent.ConcurrentBag<Game>();
                bool hasCheckChanges = false;

                // Use Parallel.ForEach for faster file checks on large libraries
                Parallel.ForEach(games, (game) =>
                {
                    bool executableExists = !string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath);

                    if (!executableExists)
                    {
                        gamesToRemove.Add(game);
                        hasCheckChanges = true;
                    }
                });

                if (!gamesToRemove.IsEmpty)
                {
                    foreach (var game in gamesToRemove)
                    {
                        DebugConsole.WriteWarning($"Game '{game.Name}' not found (detected deleted) - Removing from library.");
                        games.Remove(game);
                    }
                }

                // Save the updated list if any games were removed
                if (hasCheckChanges)
                {
                    try
                    {
                        await SaveAllGamesAsync(games);
                        DebugConsole.WriteInfo("Updated gameslist.json to remove missing games.");
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"Failed to save gameslist after cleanup: {ex.Message}");
                    }
                }

                return games;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load games list: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads all games synchronously
        /// </summary>
        public static List<Game> LoadAllGames()
        {
            return LoadAllGamesAsync().GetAwaiter().GetResult();
        }
        /// <summary>
        /// Loads Game Upload Data
        /// </summary>
        public static async Task<GameUploadData> GetGameData(Game game)
        {
            try
            {
                string filePath = Path.Combine(game.InstallDirectory, SaveFileUploadManager.ChecksumFilename);

                if (!File.Exists(filePath))
                {
                    return null;
                }

                string jsonContent = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<GameUploadData>(jsonContent, JsonHelper.GetOptions());
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"{game.Name} Has No Data ");
                return null;
            }
        }
        public static async Task<bool?> HasData(Game game)
        {
            string filePath = Path.Combine(game.InstallDirectory, SaveFileUploadManager.ChecksumFilename);

            // File doesn't exist → return null
            if (!File.Exists(filePath))
                return false;

            // Read JSON
            string json = await File.ReadAllTextAsync(filePath);

            if (string.IsNullOrWhiteSpace(json))
                return false;

            // Deserialize
            GameUploadData data;
            try
            {
                data = JsonSerializer.Deserialize<GameUploadData>(json, JsonHelper.GetOptions());
            }
            catch
            {
                // Corrupt JSON → treat as "no data"
                return false;
            }

            if (data == null)
                return false;

            // ✔ Must have at least ONE file
            return data.Files != null && data.Files.Count > 0;
        }

        /// <summary>
        /// Deletes a game from the list by name
        /// </summary>
        public static async Task DeleteGameAsync(string gameName)
        {
            try
            {
                var games = await LoadAllGamesAsync();
                var gameToRemove = games.FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

                if (gameToRemove != null)
                {
                    games.Remove(gameToRemove);
                    await SaveAllGamesAsync(games);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete game '{gameName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Checks if a game exists in the list
        /// </summary>
        public static async Task<bool> GameExistsAsync(string gameName)
        {
            var game = await LoadGameAsync(gameName);
            return game != null;
        }

        #endregion

        #region Config Save/Load Methods

        /// <summary>
        /// Loads the global config
        /// </summary>
        public static async Task<Config> LoadConfigAsync()
        {
            try
            {
                if (!File.Exists(CONFIG_PATH))
                {
                    DebugConsole.WriteWarning("[PERSISTENCE] Config file not found, returning default.");
                    return new Config();
                }

                var json = await File.ReadAllTextAsync(CONFIG_PATH).ConfigureAwait(false);
                // DebugConsole.WriteDebug($"[PERSISTENCE] Read JSON: {json}"); // Too verbose maybe?

                var config = JsonSerializer.Deserialize<Config>(json, JsonHelper.DefaultCaseInsensitive);

                if (config?.CloudConfig != null)
                {
                    DebugConsole.WriteDebug($"[PERSISTENCE] Loaded Config. Provider: {config.CloudConfig.Provider} ({(int)config.CloudConfig.Provider})");
                }
                else
                {
                    DebugConsole.WriteWarning("[PERSISTENCE] Loaded Config but CloudConfig is null/default.");
                }

                // Removed forced TrackReads enable as per user request
                return config ?? new Config();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load config: {ex.Message}", ex);
            }
        }
        public static async Task SaveGameData(Game game, GameUploadData data)
        {
            try
            {
                string filePath = Path.Combine(game.InstallDirectory, SaveFileUploadManager.ChecksumFilename);
                string jsonContent = JsonSerializer.Serialize(data, JsonHelper.DefaultIndented);

                await File.WriteAllTextAsync(filePath, jsonContent);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to save game data for {game.Name}");
            }
        }
        /// <summary>
        /// Saves the global config
        /// </summary>
        public static async Task SaveConfigAsync(Config config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, JsonHelper.DefaultIndented);

                // DEBUG LOGGING FOR PERSISTENCE
                DebugConsole.WriteDebug($"[PERSISTENCE] Saving Config. Provider: {config.CloudConfig.Provider} ({(int)config.CloudConfig.Provider})");
                DebugConsole.WriteDebug($"[PERSISTENCE] JSON Content: {json}");

                await File.WriteAllTextAsync(CONFIG_PATH, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save config: {ex.Message}", ex);
            }
        }

        #endregion

        #region Cloud Cache Methods

        public static async Task SaveCloudGamesAsync(List<string> games)
        {
            try
            {
                var json = JsonSerializer.Serialize(games, JsonHelper.DefaultIndented);
                await File.WriteAllTextAsync(CLOUD_GAMES_PATH, json);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to save cloud games cache");
            }
        }

        public static async Task<List<string>> LoadCloudGamesAsync()
        {
            try
            {
                if (!File.Exists(CLOUD_GAMES_PATH)) return new List<string>();
                var json = await File.ReadAllTextAsync(CLOUD_GAMES_PATH);
                if (string.IsNullOrWhiteSpace(json)) return new List<string>();
                return JsonSerializer.Deserialize<List<string>>(json, JsonHelper.GetOptions()) ?? new List<string>();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load cloud games cache");
                return new List<string>();
            }
        }

        #endregion
    }

}

public class Game : INotifyPropertyChanged
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private string _installDirectory = "";
    public string InstallDirectory
    {
        get => _installDirectory;
        set { _installDirectory = value; OnPropertyChanged(); }
    }

    private string _executablePath = "";
    public string ExecutablePath
    {
        get => _executablePath;
        set { _executablePath = value; OnPropertyChanged(); }
    }

    private DateTime _lasttracked = DateTime.MinValue;
    public DateTime LastTracked
    {
        get => _lasttracked;
        set { _lasttracked = value; OnPropertyChanged(); }
    }

    private Config _localConfig = new Config();

    public Config LocalConfig
    {
        get => _localConfig;
        set { _localConfig = value; OnPropertyChanged(); }
    }

    private bool _isDeleted = false;
    public bool IsDeleted
    {
        get => _isDeleted;
        set { _isDeleted = value; OnPropertyChanged(); }
    }

    private string _launchArguments = "";
    public string LaunchArguments
    {
        get => _launchArguments;
        set { _launchArguments = value; OnPropertyChanged(); }
    }

    private string _linuxArguments = "";
    public string LinuxArguments
    {
        get => _linuxArguments;
        set { _linuxArguments = value; OnPropertyChanged(); }
    }

    private string _linuxLaunchWrapper = "";
    public string LinuxLaunchWrapper
    {
        get => _linuxLaunchWrapper;
        set { _linuxLaunchWrapper = value; OnPropertyChanged(); }
    }


    public string GetGameDataFile()
    {
        return Path.Combine(InstallDirectory, SaveFileUploadManager.ChecksumFilename);
    }



    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class Config
{
    public bool EnableAutomaticTracking { get; set; } = true;
    public bool TrackWrite { get; set; } = true;
    public bool TrackReads { get; set; } = false; // Reverted to false
    public bool Auto_Upload { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool ShowDebugConsole { get; set; } = false;

    // Notification settings
    public bool EnableNotifications { get; set; } = true;

    // Auto-updater settings
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
    public string SkipVersion { get; set; } = string.Empty;

    // Analytics settings (opt-out, privacy-focused)
    public bool EnableAnalytics { get; set; } = true;

    // Announcement settings - track last seen version instead of never show again
    public string LastSeenAnnouncementVersion { get; set; } = string.Empty;

    // Last time analytics were uploaded to Firebase
    public DateTime? LastAnalyticsUpload { get; set; } = null;

    public CloudConfig CloudConfig { get; set; } = new CloudConfig();
}


public class CloudConfig
{


    // Whether to use global settings or override per-game
    public bool UseGlobalSettings { get; set; } = true;

    // The cloud provider for this config (default is Google Drive)
    public CloudProvider Provider { get; set; } = CloudProvider.GoogleDrive;
}

