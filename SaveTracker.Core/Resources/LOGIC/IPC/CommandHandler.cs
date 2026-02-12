using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Models;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaveTracker.Resources.LOGIC.IPC
{
    /// <summary>
    /// Handles IPC commands and dispatches to appropriate services
    /// </summary>
    /// <summary>
    /// Handles IPC commands and dispatches to appropriate services
    /// </summary>
    public class CommandHandler
    {
        private readonly CloudProviderHelper _providerHelper = new();
        private readonly RcloneInstaller _rcloneInstaller = new();
        private readonly RcloneConfigManager _rcloneConfig = new();
        private readonly IWindowManager _windowManager;

        // Static tracking state (set by MainWindowViewModel or active service)
        public static bool IsCurrentlyTracking { get; set; }
        public static string? CurrentlyTrackingGame { get; set; }
        public static bool IsCurrentlyUploading { get; set; }
        public static bool IsCurrentlyDownloading { get; set; }
        public static string? CurrentSyncOperation { get; set; }

        public CommandHandler(IWindowManager windowManager)
        {
            _windowManager = windowManager;
        }

        public async Task<IpcResponse> HandleAsync(IpcRequest request)
        {
            try
            {
                DebugConsole.WriteDebug($"[IPC] Handling command: {request.Command}");

                return request.Command.ToLowerInvariant() switch
                {
                    // === STATUS COMMANDS ===
                    "ping" => IpcResponse.Success(new PingResponse { Pong = true }),
                    "help" => HandleHelp(),
                    "istracking" => HandleIsTracking(),
                    "getsyncstatus" => HandleGetSyncStatus(),

                    // === GAME COMMANDS ===
                    "getgamelist" => await HandleGetGameList(),
                    "getgame" => await HandleGetGame(request),
                    "addgame" => await HandleAddGame(request),
                    "deletegame" => await HandleDeleteGame(request),
                    "getgamestatus" => await HandleGetGameStatus(request),
                    "checkcloudpresence" => await HandleCheckCloudPresence(request),

                    // === PROFILE COMMANDS ===
                    "getprofiles" => await HandleGetProfiles(request),
                    "getactiveprofile" => await HandleGetActiveProfile(request),
                    "changeprofile" => await HandleChangeProfile(request),

                    // === CLOUD / RCLONE COMMANDS ===
                    "getproviders" => HandleGetProviders(),
                    "getselectedprovider" => await HandleGetSelectedProvider(),
                    "setprovider" => await HandleSetProvider(request),
                    "getrclonestatus" => await HandleGetRcloneStatus(),
                    "installrclone" => await HandleInstallRclone(),
                    "isproviderconfigured" => await HandleIsProviderConfigured(request),
                    "configureprovider" => await HandleConfigureProvider(request),

                    // === SYNC COMMANDS ===
                    "triggersync" => await HandleTriggerSync(request),
                    "uploadnow" => await HandleUploadNow(request),
                    "compareprogress" => await HandleCompareProgress(request),

                    // === SETTINGS COMMANDS ===
                    "getsettings" => await HandleGetSettings(),
                    "savesettings" => await HandleSaveSettings(request),
                    "getgamesettings" => await HandleGetGameSettings(request),
                    "savegamesettings" => await HandleSaveGameSettings(request),

                    // === WINDOW COMMANDS ===
                    "showmainwindow" => HandleShowMainWindow(),
                    "showlibrary" => HandleShowWindow("library"),
                    "showblacklist" => HandleShowWindow("blacklist"),
                    "showcloudsettings" => HandleShowWindow("cloudsettings"),
                    "showsettings" => HandleShowWindow("settings"),
                    "reportissue" => HandleReportIssue(),


                    // === SESSION COMMANDS ===
                    "startsession" => await HandleStartSession(request),
                    "endsession" => await HandleEndSession(request),
                    "stopsession" => await HandleEndSession(request), // Alias

                    // === DATA API COMMANDS ===
                    "getblacklist" => HandleGetBlacklist(),
                    "addblacklist" => await HandleAddBlacklist(request),
                    "removeblacklist" => await HandleRemoveBlacklist(request),
                    "getcloudlibrary" => await HandleGetCloudLibrary(),

                    _ => IpcResponse.Fail($"Unknown command: {request.Command}")
                };
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"[IPC] Command error: {ex.Message}");
                return IpcResponse.Fail(ex.Message);
            }
        }

        #region Status Commands

        private IpcResponse HandleHelp()
        {
            var help = new HelpResponse();

            // Add Version Info
            help.Categories.Add(new CommandCategory
            {
                Name = "System Info",
                Commands = new List<CommandInfo>
                {
                    new CommandInfo { Command = "Version", Description = "Headless v0.1 (based on SaveTracker v0.5.0)" }
                }
            });

            help.Categories.Add(new CommandCategory
            {
                Name = "Status Commands",
                Commands = new List<CommandInfo>
                {
                    new CommandInfo { Command = "ping", Description = "Check if IPC server is alive" },
                    new CommandInfo { Command = "help", Description = "Show this help message" },
                    new CommandInfo { Command = "istracking", Description = "Check if any game is currently being tracked" },
                    new CommandInfo { Command = "getsyncstatus", Description = "Get current upload/download status" }
                }
            });

            help.Categories.Add(new CommandCategory
            {
                Name = "Game Commands",
                Commands = new List<CommandInfo>
                {
                    new CommandInfo { Command = "getgamelist", Description = "Get list of all games in SaveTracker" },
                    new CommandInfo { Command = "getgame", Description = "Get details for a specific game", Params = "name" },
                    new CommandInfo { Command = "addgame", Description = "Add a new game or update existing one", Params = "{game object}" },
                    new CommandInfo { Command = "deletegame", Description = "Remove a game from SaveTracker", Params = "name" },
                    new CommandInfo { Command = "getgamestatus", Description = "Get tracking status for a specific game", Params = "name" },
                    new CommandInfo { Command = "checkcloudpresence", Description = "Check if game has saves in the cloud", Params = "name" }
                }
            });

            help.Categories.Add(new CommandCategory
            {
                Name = "Session Commands",
                Commands = new List<CommandInfo>
                {
                    new CommandInfo { Command = "startsession", Description = "Manually start tracking a game", Params = "name" },
                    new CommandInfo { Command = "endsession", Description = "Manually stop tracking and trigger upload", Params = "name" }
                }
            });

            help.Categories.Add(new CommandCategory
            {
                Name = "Blacklist Commands",
                Commands = new List<CommandInfo>
                {
                    new CommandInfo { Command = "getblacklist", Description = "Get current global blacklist config" },
                    new CommandInfo { Command = "addblacklist", Description = "Add item to blacklist", Params = "type, value (directory, extension, filename, keyword)" },
                    new CommandInfo { Command = "removeblacklist", Description = "Remove item from blacklist", Params = "type, value" }
                }
            });

            help.Categories.Add(new CommandCategory
            {
                Name = "Data API Commands",
                Commands = new List<CommandInfo>
                {
                    new CommandInfo { Command = "getcloudlibrary", Description = "Get merged list of local and cloud games with stats" }
                }
            });

            help.Categories.Add(new CommandCategory
            {
                Name = "Window Commands (GUI only)",
                Commands = new List<CommandInfo>
                {
                    new CommandInfo { Command = "showmainwindow", Description = "Bring main window to front" },
                    new CommandInfo { Command = "showlibrary", Description = "Open Library window" },
                    new CommandInfo { Command = "showblacklist", Description = "Open Blacklist editor" },
                    new CommandInfo { Command = "showcloudsettings", Description = "Open Cloud settings" },
                    new CommandInfo { Command = "showsettings", Description = "Open App settings" }
                }
            });

            return IpcResponse.Success(help);
        }

        private IpcResponse HandleIsTracking()
        {
            return IpcResponse.Success(new TrackingStatusResponse
            {
                Tracking = IsCurrentlyTracking,
                GameName = CurrentlyTrackingGame
            });
        }

        private IpcResponse HandleGetSyncStatus()
        {
            return IpcResponse.Success(new SyncStatusResponse
            {
                IsUploading = IsCurrentlyUploading,
                IsDownloading = IsCurrentlyDownloading,
                CurrentOperation = CurrentSyncOperation
            });
        }

        #endregion

        #region Game Commands

        private async Task<IpcResponse> HandleGetGameList()
        {
            try
            {
                var games = await ConfigManagement.LoadAllGamesAsync();
                return IpcResponse.Success(games);
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to load games: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleGetGame(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null)
                    return IpcResponse.Fail($"Game not found: {name}");

                return IpcResponse.Success(game);
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to load game: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleAddGame(IpcRequest request)
        {
            try
            {
                var gameData = request.Params?.GetRawText();
                if (string.IsNullOrEmpty(gameData))
                    return IpcResponse.Fail("Missing game data");

                // Deserialize using Context
                Game? game = null;
                try
                {
                    game = JsonSerializer.Deserialize(gameData, IpcJsonContext.Default.Game);
                }
                catch
                {
                    // Fallback or specific error
                }

                if (game == null || string.IsNullOrEmpty(game.Name))
                    return IpcResponse.Fail("Invalid game data structure");

                await ConfigManagement.SaveGameAsync(game);
                return IpcResponse.Success(new GameAddedResponse { Added = true, Name = game.Name });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to add game: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleDeleteGame(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            await ConfigManagement.DeleteGameAsync(name);
            return IpcResponse.Success(new GameDeletedResponse { Deleted = true });
        }

        private async Task<IpcResponse> HandleGetGameStatus(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            // Simple status for now
            bool isTracking = IsCurrentlyTracking && string.Equals(CurrentlyTrackingGame, name, StringComparison.OrdinalIgnoreCase);

            return IpcResponse.Success(new TrackingStatusResponse
            {
                Tracking = isTracking,
                GameName = name
            });
        }

        private async Task<IpcResponse> HandleCheckCloudPresence(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                var smartSync = new SmartSyncService();
                var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.FromMinutes(5));
                bool inCloud = comparison.Status != SmartSyncService.ProgressStatus.CloudNotFound;

                return IpcResponse.Success(new CloudPresenceResponse { InCloud = inCloud });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Cloud check failed: {ex.Message}");
            }
        }

        #endregion

        #region Profile Commands

        private async Task<IpcResponse> HandleGetProfiles(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                // Stub: ProfileManager methods don't exist yet, return default profile
                var profiles = new List<string> { "default" };
                return IpcResponse.Success(new ProfileListResponse { Profiles = profiles, ActiveProfileId = "default" });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to get profiles: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleGetActiveProfile(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                // Stub: Return default profile
                return IpcResponse.Success(new ActiveProfileResponse { ActiveProfileId = "default" });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to get active profile: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleChangeProfile(IpcRequest request)
        {
            var name = request.GetString("name");
            var profileId = request.GetString("profileId");

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(profileId))
                return IpcResponse.Fail("Missing parameters");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                var profileManager = new ProfileManager();
                await profileManager.SwitchProfileAsync(game, profileId);
                return IpcResponse.Success(new ProfileChangedResponse { Changed = true, NewProfileId = profileId });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to change profile: {ex.Message}");
            }
        }

        #endregion

        #region Cloud / Rclone Commands

        private IpcResponse HandleGetProviders()
        {
            var providers = Enum.GetNames(typeof(CloudProvider)).ToList();
            return IpcResponse.Success(new ProvidersResponse { Providers = providers });
        }

        private async Task<IpcResponse> HandleGetSelectedProvider()
        {
            var config = await ConfigManagement.LoadConfigAsync();
            var provider = config.CloudConfig.Provider;

            return IpcResponse.Success(new SelectedProviderResponse
            {
                Provider = (int)provider,
                Name = provider.ToString(),
                // DisplayName = _providerHelper.GetProviderDisplayName(provider) // Commented out in model, verify if needed
            });
        }

        private async Task<IpcResponse> HandleSetProvider(IpcRequest request)
        {
            // Update GetParam usage later
            var providerId = request.GetParam<int?>("provider", IpcJsonContext.Default.NullableInt32);
            if (providerId == null)
                return IpcResponse.Fail("Missing 'provider' parameter");

            var provider = (CloudProvider)providerId.Value;
            var config = await ConfigManagement.LoadConfigAsync();
            config.CloudConfig.Provider = provider;
            await ConfigManagement.SaveConfigAsync(config);

            return IpcResponse.Success(new ProviderSetResponse { Set = true, Provider = provider.ToString() });
        }

        private async Task<IpcResponse> HandleGetRcloneStatus()
        {
            bool installed = await _rcloneInstaller.RcloneCheckAsync(CloudProvider.GoogleDrive);
            return IpcResponse.Success(new InstallRcloneResponse { Installed = installed });
        }

        private async Task<IpcResponse> HandleInstallRclone()
        {
            try
            {
                var installed = await _rcloneInstaller.RcloneCheckAsync(CloudProvider.GoogleDrive);
                return IpcResponse.Success(new InstallRcloneResponse { Installed = installed });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Installation failed: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleIsProviderConfigured(IpcRequest request)
        {
            var providerId = request.GetParam<int?>("provider", IpcJsonContext.Default.NullableInt32);
            if (providerId == null)
                return IpcResponse.Fail("Missing 'provider' parameter");

            var provider = (CloudProvider)providerId.Value;
            var isConfigured = await _rcloneConfig.IsValidConfig(provider);
            return IpcResponse.Success(new ConfiguredResponse { Configured = isConfigured, Provider = provider.ToString() });
        }

        private async Task<IpcResponse> HandleConfigureProvider(IpcRequest request)
        {
            var providerId = request.GetParam<int?>("provider", IpcJsonContext.Default.NullableInt32);
            if (providerId == null)
                return IpcResponse.Fail("Missing 'provider' parameter");

            var provider = (CloudProvider)providerId.Value;

            try
            {
                await _rcloneConfig.CreateConfig(provider);
                return IpcResponse.Success(new ConfiguredResponse { Configured = true });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Configuration failed: {ex.Message}");
            }
        }

        #endregion

        #region Sync Commands

        private async Task<IpcResponse> HandleTriggerSync(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name)) return IpcResponse.Fail("Missing name");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                _windowManager.TriggerSmartSync(game);
                return IpcResponse.Success(new SyncTriggeredResponse { Triggered = true, GameName = name });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail(ex.Message);
            }
        }

        private async Task<IpcResponse> HandleUploadNow(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name)) return IpcResponse.Fail("Missing name");

            try
            {
                // Assuming WindowManager has method for this or we should trigger logic directly
                // _windowManager.TriggerUpload(game); // If exists
                // For now, let's treat it as sync
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                _windowManager.TriggerSmartSync(game); // Reusing smart sync for now
                return IpcResponse.Success(new UploadStartedResponse { UploadStarted = true, GameName = name });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail(ex.Message);
            }
        }

        private async Task<IpcResponse> HandleCompareProgress(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name)) return IpcResponse.Fail("Missing name");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                var smartSync = new SmartSyncService();
                var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.FromMinutes(5));

                // Need a DTO for comparison result?
                // IPC Context has ProgressComparisonResult? No I removed it.
                // Let's mapping it to a simple object or re-add the DTO.
                // For now, let's just return a bool or simple string status

                return IpcResponse.Success(new CompareProgressResponse
                {
                    Status = comparison.Status.ToString(),
                    LocalTime = comparison.LocalPlayTime.ToString(),
                    CloudTime = comparison.CloudPlayTime.ToString()
                });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail(ex.Message);
            }
        }

        #endregion

        #region Settings Commands

        private async Task<IpcResponse> HandleGetSettings()
        {
            var config = await ConfigManagement.LoadConfigAsync();
            return IpcResponse.Success(new GlobalSettingsResponse
            {
                EnableAutomaticTracking = config.EnableAutomaticTracking,
                TrackWrite = config.TrackWrite,
                TrackReads = config.TrackReads,
                AutoUpload = config.Auto_Upload,
                StartMinimized = config.StartMinimized,
                ShowDebugConsole = config.ShowDebugConsole,
                EnableNotifications = config.EnableNotifications,
                CheckForUpdatesOnStartup = config.CheckForUpdatesOnStartup,
                EnableAnalytics = config.EnableAnalytics,
                CloudProvider = config.CloudConfig.Provider.ToString()
            });
        }

        private async Task<IpcResponse> HandleSaveSettings(IpcRequest request)
        {
            try
            {
                var config = await ConfigManagement.LoadConfigAsync();

                // Update fields from request params
                if (request.Params != null)
                {
                    var p = request.Params.Value;
                    if (p.TryGetProperty("enableAutomaticTracking", out var eat))
                        config.EnableAutomaticTracking = eat.GetBoolean();
                    if (p.TryGetProperty("trackWrite", out var tw))
                        config.TrackWrite = tw.GetBoolean();
                    if (p.TryGetProperty("trackReads", out var tr))
                        config.TrackReads = tr.GetBoolean();
                    if (p.TryGetProperty("autoUpload", out var au))
                        config.Auto_Upload = au.GetBoolean();
                    if (p.TryGetProperty("startMinimized", out var sm))
                        config.StartMinimized = sm.GetBoolean();
                    if (p.TryGetProperty("enableNotifications", out var en))
                        config.EnableNotifications = en.GetBoolean();
                }

                await ConfigManagement.SaveConfigAsync(config);
                return IpcResponse.Success(new SavedResponse { Saved = true });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to save settings: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleGetGameSettings(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            var data = await ConfigManagement.GetGameData(game);
            if (data == null)
                return IpcResponse.Success(new GameSettingsResponse { HasSettings = false });

            return IpcResponse.Success(new GameSettingsResponse
            {
                HasSettings = true,
                EnableSmartSync = data.EnableSmartSync,
                GameProvider = data.GameProvider.ToString(),
                PlayTime = data.PlayTime.ToString(@"hh\:mm\:ss"),
                FilesCount = data.Files?.Count ?? 0
            });
        }

        private async Task<IpcResponse> HandleSaveGameSettings(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail("Game not found");

                var data = await ConfigManagement.GetGameData(game) ?? new GameUploadData();
                if (request.Params != null)
                {
                    var p = request.Params.Value;
                    if (p.TryGetProperty("enableSmartSync", out var ess))
                        data.EnableSmartSync = ess.GetBoolean();
                    // Add more fields if needed
                }

                await ConfigManagement.SaveGameData(game, data);
                return IpcResponse.Success(new SavedResponse { Saved = true });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to save game settings: {ex.Message}");
            }
        }

        #endregion

        #region Window Commands

        private IpcResponse HandleShowMainWindow()
        {
            _windowManager.ShowMainWindow();
            return IpcResponse.Success(new WindowShownResponse { Shown = true });
        }

        private IpcResponse HandleShowWindow(string windowType)
        {
            // Simple mapping
            switch (windowType)
            {
                case "library": _windowManager.ShowLibrary(); break;
                case "blacklist": _windowManager.ShowBlacklist(); break;
                case "cloudsettings": _windowManager.ShowCloudSettings(); break;
                case "settings": _windowManager.ShowSettings(); break; // If exists
                default: return IpcResponse.Fail("Unknown window type");
            }

            return IpcResponse.Success(new WindowShownResponse { Triggered = true, Window = windowType });
        }

        private IpcResponse HandleReportIssue()
        {
            _windowManager.ReportIssue();
            return IpcResponse.Success(new IssueReportedResponse { Opened = true });
        }

        private async Task<IpcResponse> HandleStartSession(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail($"Game not found: {name}");

                await _windowManager.StartSession(game);
                return IpcResponse.Success(new { started = true, game = game.Name });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to start session: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleEndSession(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            try
            {
                var game = await ConfigManagement.LoadGameAsync(name);
                if (game == null) return IpcResponse.Fail($"Game not found: {name}");

                await _windowManager.EndSession(game);
                return IpcResponse.Success(new { ended = true, game = game.Name });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to end session: {ex.Message}");
            }
        }

        #region Blacklist & Cloud Library Commands

        private IpcResponse HandleGetBlacklist()
        {
            var bm = BlacklistManager.Instance;
            return IpcResponse.Success(new BlacklistResponse
            {
                Directories = bm.Directories.ToList(),
                Extensions = bm.Extensions.ToList(),
                FileNames = bm.FileNames.ToList(),
                Keywords = bm.Keywords.ToList()
            });
        }

        private async Task<IpcResponse> HandleAddBlacklist(IpcRequest request)
        {
            var type = request.GetString("type")?.ToLower();
            var value = request.GetString("value");

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(value))
                return IpcResponse.Fail("Missing 'type' or 'value' parameter");

            bool success = false;
            string msg = "";

            switch (type)
            {
                case "directory":
                    success = BlacklistManager.Instance.AddDirectory(value);
                    msg = success ? "Directory added" : "Directory already exists";
                    break;
                case "extension":
                    if (!value.StartsWith(".")) value = "." + value;
                    success = BlacklistManager.Instance.AddExtension(value);
                    msg = success ? "Extension added" : "Extension already exists";
                    break;
                case "filename":
                    success = BlacklistManager.Instance.AddFileName(value);
                    msg = success ? "Filename added" : "Filename already exists";
                    break;
                case "keyword":
                    success = BlacklistManager.Instance.AddKeyword(value);
                    msg = success ? "Keyword added" : "Keyword already exists";
                    break;
                default:
                    return IpcResponse.Fail("Invalid type. Use: directory, extension, filename, keyword");
            }

            return IpcResponse.Success(new BlacklistActionResponse { Success = success, Message = msg });
        }

        private async Task<IpcResponse> HandleRemoveBlacklist(IpcRequest request)
        {
            var type = request.GetString("type")?.ToLower();
            var value = request.GetString("value");

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(value))
                return IpcResponse.Fail("Missing 'type' or 'value' parameter");

            bool success = false;
            string msg = "";

            switch (type)
            {
                case "directory":
                    success = BlacklistManager.Instance.RemoveDirectory(value);
                    break;
                case "extension":
                    if (!value.StartsWith(".")) value = "." + value;
                    success = BlacklistManager.Instance.RemoveExtension(value);
                    break;
                case "filename":
                    success = BlacklistManager.Instance.RemoveFileName(value);
                    break;
                case "keyword":
                    success = BlacklistManager.Instance.RemoveKeyword(value);
                    break;
                default:
                    return IpcResponse.Fail("Invalid type");
            }

            msg = success ? "Item removed" : "Item not found";
            return IpcResponse.Success(new BlacklistActionResponse { Success = success, Message = msg });
        }

        private async Task<IpcResponse> HandleGetCloudLibrary()
        {
            try
            {
                var service = new CloudLibraryService();
                var items = await service.GetCloudLibraryAsync();

                var dtos = items.Select(i => new CloudLibraryItemDto
                {
                    Name = i.Name,
                    IsInstalled = i.IsInstalled,
                    IsInCloud = i.IsInCloud,
                    LocalPath = i.LocalPath,
                    PlayTime = i.PlayTime.ToString(@"hh\:mm\:ss"), // Simple formatting
                    TotalSize = i.TotalSize,
                    FileCount = i.FileCount
                }).ToList();

                return IpcResponse.Success(dtos);
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to get cloud library: {ex.Message}");
            }
        }

        #endregion

        #endregion
    }

}
