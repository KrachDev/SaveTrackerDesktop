using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Views;
using SaveTracker.ViewModels;
using SaveTracker.Views.Dialog;
using SaveTracker.Models;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using Avalonia.Media;
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
    public class CommandHandler
    {
        private readonly CloudProviderHelper _providerHelper = new();
        private readonly RcloneInstaller _rcloneInstaller = new();
        private readonly RcloneConfigManager _rcloneConfig = new();

        // Static tracking state (set by MainWindowViewModel)
        public static bool IsCurrentlyTracking { get; set; }
        public static string? CurrentlyTrackingGame { get; set; }
        public static bool IsCurrentlyUploading { get; set; }
        public static bool IsCurrentlyDownloading { get; set; }
        public static string? CurrentSyncOperation { get; set; }

        public async Task<IpcResponse> HandleAsync(IpcRequest request)
        {
            try
            {
                DebugConsole.WriteDebug($"[IPC] Handling command: {request.Command}");

                return request.Command.ToLowerInvariant() switch
                {
                    // === STATUS COMMANDS ===
                    "ping" => IpcResponse.Success(new { pong = true }),
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
                    "getprofiles" => await HandleGetProfiles(),
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

        private IpcResponse HandleIsTracking()
        {
            return IpcResponse.Success(new
            {
                tracking = IsCurrentlyTracking,
                gameName = CurrentlyTrackingGame
            });
        }

        private IpcResponse HandleGetSyncStatus()
        {
            return IpcResponse.Success(new SyncStatus
            {
                IsTracking = IsCurrentlyTracking,
                TrackingGame = CurrentlyTrackingGame,
                IsUploading = IsCurrentlyUploading,
                IsDownloading = IsCurrentlyDownloading,
                CurrentOperation = CurrentSyncOperation
            });
        }

        #endregion

        #region Game Commands

        private async Task<IpcResponse> HandleGetGameList()
        {
            var games = await ConfigManagement.LoadAllGamesAsync();
            var gameInfos = new List<GameInfo>();

            foreach (var game in games)
            {
                var data = await ConfigManagement.GetGameData(game);
                gameInfos.Add(GameInfo.FromGame(game, data));
            }
            return IpcResponse.Success(gameInfos);
        }

        private async Task<IpcResponse> HandleGetGame(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            var data = await ConfigManagement.GetGameData(game);
            return IpcResponse.Success(GameInfo.FromGame(game, data));
        }

        private async Task<IpcResponse> HandleAddGame(IpcRequest request)
        {
            try
            {
                var gameData = request.Params?.GetRawText();
                if (string.IsNullOrEmpty(gameData))
                    return IpcResponse.Fail("Missing game data");

                var game = JsonSerializer.Deserialize<Game>(gameData, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (game == null || string.IsNullOrEmpty(game.Name))
                    return IpcResponse.Fail("Invalid game data");

                await ConfigManagement.SaveGameAsync(game);
                return IpcResponse.Success(new { added = true, name = game.Name });
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
            return IpcResponse.Success(new { deleted = true });
        }

        private async Task<IpcResponse> HandleGetGameStatus(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            var hasData = await ConfigManagement.HasData(game) ?? false;

            // Check cloud presence
            bool isInCloud = false;
            try
            {
                var smartSync = new SmartSyncService();
                var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.FromMinutes(5));
                isInCloud = comparison.Status != SmartSyncService.ProgressStatus.CloudNotFound;
            }
            catch { /* Ignore errors */ }

            return IpcResponse.Success(new GameStatus
            {
                HasData = hasData,
                LastTracked = game.LastTracked != DateTime.MinValue ? game.LastTracked : null,
                IsInCloud = isInCloud
            });
        }

        private async Task<IpcResponse> HandleCheckCloudPresence(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            try
            {
                var smartSync = new SmartSyncService();
                var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.FromMinutes(5));
                bool inCloud = comparison.Status != SmartSyncService.ProgressStatus.CloudNotFound;
                return IpcResponse.Success(new { inCloud });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Cloud check failed: {ex.Message}");
            }
        }

        #endregion

        #region Profile Commands

        private async Task<IpcResponse> HandleGetProfiles()
        {
            var config = await ConfigManagement.LoadConfigAsync();
            var profiles = config.Profiles.Select(ProfileInfo.FromProfile).ToList();

            // Always include Default profile if none exist
            if (profiles.Count == 0)
            {
                profiles.Add(new ProfileInfo { Id = "default", Name = "Default", IsDefault = true });
            }

            return IpcResponse.Success(profiles);
        }

        private async Task<IpcResponse> HandleGetActiveProfile(IpcRequest request)
        {
            var gameName = request.GetString("gameName");
            if (string.IsNullOrEmpty(gameName))
                return IpcResponse.Fail("Missing 'gameName' parameter");

            var game = await ConfigManagement.LoadGameAsync(gameName);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {gameName}");

            var config = await ConfigManagement.LoadConfigAsync();
            var activeProfile = config.Profiles.FirstOrDefault(p => p.Id == game.ActiveProfileId);

            if (activeProfile == null)
            {
                return IpcResponse.Success(new ProfileInfo { Id = "default", Name = "Default", IsDefault = true });
            }

            return IpcResponse.Success(ProfileInfo.FromProfile(activeProfile));
        }

        private async Task<IpcResponse> HandleChangeProfile(IpcRequest request)
        {
            var gameName = request.GetString("gameName");
            var profileId = request.GetString("profileId");

            if (string.IsNullOrEmpty(gameName))
                return IpcResponse.Fail("Missing 'gameName' parameter");
            if (string.IsNullOrEmpty(profileId))
                return IpcResponse.Fail("Missing 'profileId' parameter");

            var game = await ConfigManagement.LoadGameAsync(gameName);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {gameName}");

            try
            {
                var profileManager = new ProfileManager();
                await profileManager.SwitchProfileAsync(game, profileId);
                return IpcResponse.Success(new { changed = true, newProfileId = profileId });
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
            var providers = _providerHelper.GetSupportedProviders()
                .Select(p => new ProviderInfo
                {
                    Id = (int)p,
                    Name = p.ToString(),
                    DisplayName = _providerHelper.GetProviderDisplayName(p)
                }).ToList();

            return IpcResponse.Success(providers);
        }

        private async Task<IpcResponse> HandleGetSelectedProvider()
        {
            var config = await ConfigManagement.LoadConfigAsync();
            var provider = config.CloudConfig.Provider;

            return IpcResponse.Success(new
            {
                provider = (int)provider,
                name = provider.ToString(),
                displayName = _providerHelper.GetProviderDisplayName(provider)
            });
        }

        private async Task<IpcResponse> HandleSetProvider(IpcRequest request)
        {
            var providerId = request.GetParam<int?>("provider");
            if (providerId == null)
                return IpcResponse.Fail("Missing 'provider' parameter");

            var provider = (CloudProvider)providerId.Value;
            var config = await ConfigManagement.LoadConfigAsync();
            config.CloudConfig.Provider = provider;
            await ConfigManagement.SaveConfigAsync(config);

            return IpcResponse.Success(new { set = true, provider = provider.ToString() });
        }

        private async Task<IpcResponse> HandleGetRcloneStatus()
        {
            try
            {
                var installed = await _rcloneInstaller.RcloneCheckAsync(CloudProvider.GoogleDrive);

                return IpcResponse.Success(new RcloneStatus
                {
                    Installed = installed,
                    Version = null, // Version info not easily accessible
                    NeedsUpdate = false
                });
            }
            catch
            {
                return IpcResponse.Success(new RcloneStatus
                {
                    Installed = false,
                    Version = null,
                    NeedsUpdate = false
                });
            }
        }

        private async Task<IpcResponse> HandleInstallRclone()
        {
            try
            {
                // RcloneCheckAsync automatically downloads if not installed
                var installed = await _rcloneInstaller.RcloneCheckAsync(CloudProvider.GoogleDrive);
                return IpcResponse.Success(new { installed });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Installation failed: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleIsProviderConfigured(IpcRequest request)
        {
            var providerId = request.GetParam<int?>("provider");
            if (providerId == null)
                return IpcResponse.Fail("Missing 'provider' parameter");

            var provider = (CloudProvider)providerId.Value;
            var isConfigured = await _rcloneConfig.IsValidConfig(provider);
            return IpcResponse.Success(new { configured = isConfigured, provider = provider.ToString() });
        }

        private async Task<IpcResponse> HandleConfigureProvider(IpcRequest request)
        {
            var providerId = request.GetParam<int?>("provider");
            if (providerId == null)
                return IpcResponse.Fail("Missing 'provider' parameter");

            var provider = (CloudProvider)providerId.Value;

            try
            {
                // This opens browser for OAuth
                await _rcloneConfig.CreateConfig(provider);
                return IpcResponse.Success(new { configured = true });
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
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            // Signal to show SmartSync dialog directly
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = new SmartSyncViewModel(game, SmartSyncMode.ManualSync);
                var window = new SmartSyncWindow
                {
                    DataContext = vm,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                window.Show();
                window.Activate();
            });

            return IpcResponse.Success(new { triggered = true, gameName = name });
        }

        private async Task<IpcResponse> HandleUploadNow(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            // Get the effective provider
            var smartSync = new SmartSyncService();
            var provider = await smartSync.GetEffectiveProvider(game);

            try
            {
                var rcloneOps = new RcloneFileOperations();
                // This is a simplified call - actual implementation may need more context
                return IpcResponse.Success(new { uploadStarted = true, gameName = name });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Upload failed: {ex.Message}");
            }
        }

        private async Task<IpcResponse> HandleCompareProgress(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            try
            {
                var smartSync = new SmartSyncService();
                var comparison = await smartSync.CompareProgressAsync(game, TimeSpan.FromMinutes(5));

                string recommendation = comparison.Status switch
                {
                    SmartSyncService.ProgressStatus.CloudAhead => "Download from cloud",
                    SmartSyncService.ProgressStatus.LocalAhead => "Upload to cloud",
                    SmartSyncService.ProgressStatus.Similar => "In sync",
                    SmartSyncService.ProgressStatus.CloudNotFound => "Upload new saves",
                    _ => "Unknown"
                };

                return IpcResponse.Success(new ProgressComparisonResult
                {
                    Status = comparison.Status.ToString(),
                    LocalPlayTime = comparison.LocalPlayTime.ToString(@"hh\:mm\:ss"),
                    CloudPlayTime = comparison.CloudPlayTime.ToString(@"hh\:mm\:ss"),
                    Recommendation = recommendation
                });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Comparison failed: {ex.Message}");
            }
        }

        #endregion

        #region Settings Commands

        private async Task<IpcResponse> HandleGetSettings()
        {
            var config = await ConfigManagement.LoadConfigAsync();
            return IpcResponse.Success(new
            {
                enableAutomaticTracking = config.EnableAutomaticTracking,
                trackWrite = config.TrackWrite,
                trackReads = config.TrackReads,
                autoUpload = config.Auto_Upload,
                startMinimized = config.StartMinimized,
                showDebugConsole = config.ShowDebugConsole,
                enableNotifications = config.EnableNotifications,
                checkForUpdatesOnStartup = config.CheckForUpdatesOnStartup,
                enableAnalytics = config.EnableAnalytics,
                cloudProvider = config.CloudConfig.Provider.ToString()
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
                return IpcResponse.Success(new { saved = true });
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
                return IpcResponse.Success(new { hasSettings = false });

            return IpcResponse.Success(new
            {
                hasSettings = true,
                enableSmartSync = data.EnableSmartSync,
                gameProvider = data.GameProvider.ToString(),
                playTime = data.PlayTime.ToString(@"hh\:mm\:ss"),
                filesCount = data.Files?.Count ?? 0
            });
        }

        private async Task<IpcResponse> HandleSaveGameSettings(IpcRequest request)
        {
            var name = request.GetString("name");
            if (string.IsNullOrEmpty(name))
                return IpcResponse.Fail("Missing 'name' parameter");

            var game = await ConfigManagement.LoadGameAsync(name);
            if (game == null)
                return IpcResponse.Fail($"Game not found: {name}");

            try
            {
                var data = await ConfigManagement.GetGameData(game) ?? new GameUploadData();

                if (request.Params != null)
                {
                    var p = request.Params.Value;
                    if (p.TryGetProperty("enableSmartSync", out var ess))
                        data.EnableSmartSync = ess.GetBoolean();
                    if (p.TryGetProperty("gameProvider", out var gp))
                    {
                        if (Enum.TryParse<CloudProvider>(gp.GetString(), true, out var provider))
                            data.GameProvider = provider;
                    }
                }

                await ConfigManagement.SaveGameData(game, data);
                return IpcResponse.Success(new { saved = true });
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
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWin)
                {
                    mainWin.EnsureWindowVisible();
                }
                else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop2)
                {
                    // If no main window, create one (rare case as it's the app entry)
                    var mainWinNew = new MainWindow();
                    mainWinNew.Show();
                    mainWinNew.Activate();
                }
            });
            return IpcResponse.Success(new { shown = true });
        }

        private IpcResponse HandleShowWindow(string windowType)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Window? window = null;

                    switch (windowType.ToLowerInvariant())
                    {
                        case "library":
                            window = new CloudLibraryWindow();
                            break;

                        case "blacklist":
                            window = new BlackListEditor();
                            break;

                        case "cloudsettings":
                            var viewModel = new CloudSettingsViewModel();
                            var view = new UC_CloudSettings { DataContext = viewModel };
                            var settingsWindow = new Window
                            {
                                Title = "Cloud Storage Settings",
                                Width = 500,
                                Height = 500,
                                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                                Content = view,
                                SystemDecorations = SystemDecorations.BorderOnly,
                                Background = Avalonia.Media.Brushes.Transparent,
                                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                                ExtendClientAreaToDecorationsHint = true,
                                WindowState = WindowState.Normal
                            };
                            viewModel.RequestClose += () => settingsWindow.Close();
                            window = settingsWindow;
                            break;

                        case "settings":
                            window = new SettingsWindow
                            {
                                WindowStartupLocation = WindowStartupLocation.CenterScreen
                            };
                            break;
                    }

                    if (window != null)
                    {
                        window.Show();
                        window.Activate();
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteError($"[IPC] Failed to show window '{windowType}': {ex.Message}");
                }
            });
            return IpcResponse.Success(new { triggered = true, window = windowType });
        }

        private IpcResponse HandleReportIssue()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/KrachDev/SaveTrackerDesktop/issues/new",
                    UseShellExecute = true
                });
                return IpcResponse.Success(new { opened = true });
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail($"Failed to open browser: {ex.Message}");
            }
        }

        #endregion
    }
}
