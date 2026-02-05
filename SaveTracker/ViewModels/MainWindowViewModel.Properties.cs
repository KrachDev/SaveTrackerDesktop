using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.HELPERS.Linux;

using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Avalonia.Platform.Storage;

namespace SaveTracker.ViewModels
{
    public partial class MainWindowViewModel
    {
        // ========== GAME PROPERTIES TAB PROPERTIES ==========

        [ObservableProperty]
        private string _editableGameName = string.Empty;

        [ObservableProperty]
        private string _editableExecutablePath = "";

        [ObservableProperty]
        private string _editablePrefix = "";

        [ObservableProperty]
        private string _editableLaunchArguments = "";

        [ObservableProperty]
        private string _editableLinuxArguments = "";

        [ObservableProperty]
        private string _editableLinuxLaunchWrapper = "";

        [ObservableProperty]
        private string _selectedLauncher = "Custom";

        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<string> _availableLaunchers = new();

        partial void OnSelectedLauncherChanged(string value)
        {
            if (value == "Custom") return;

            // Template logic
            if (value == "System Wine")
            {
                EditableLinuxLaunchWrapper = "wine";
            }
            else if (value == "Heroic Games Launcher")
            {
                if (!EditableLinuxLaunchWrapper.Contains("heroic://"))
                {
                    string idPlaceholder = "GAME_ID_HERE";

                    // Attempt to auto-detect ID and Prefix
                    var detectedInfo = HeroicLibraryParser.FindGameInfo(EditableGameName);
                    if (detectedInfo != null && !string.IsNullOrEmpty(detectedInfo.AppId))
                    {
                        idPlaceholder = detectedInfo.AppId;
                        DebugConsole.WriteInfo($"Auto-detected Heroic Game ID: {detectedInfo.AppId}");

                        if (!string.IsNullOrEmpty(detectedInfo.WinePrefix))
                        {
                            EditablePrefix = detectedInfo.WinePrefix;
                            DebugConsole.WriteInfo($"Auto-detected Heroic Wine Prefix: {detectedInfo.WinePrefix}");
                        }
                    }

                    EditableLinuxLaunchWrapper = $"heroic \"heroic://launch/{idPlaceholder}\"";
                }
            }
            else if (value == "Lutris")
            {
                if (!EditableLinuxLaunchWrapper.Contains("lutris:"))
                    EditableLinuxLaunchWrapper = "lutris lutris:rungame/GAME_SLUG_HERE";
            }
            else if (value == "Bottles")
            {
                if (!EditableLinuxLaunchWrapper.Contains("bottles-cli"))
                    EditableLinuxLaunchWrapper = "bottles-cli run -b \"BOTTLE_NAME\" -p \"PROGRAM_NAME\"";
            }
        }

        [RelayCommand]
        private async Task BrowseForPrefixAsync()
        {
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
               ? desktop.MainWindow
               : null;

            if (mainWindow == null) return;

            var topLevel = TopLevel.GetTopLevel(mainWindow);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Wine Prefix Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                EditablePrefix = folders[0].Path.LocalPath;
            }
        }


        partial void OnEditableLinuxLaunchWrapperChanged(string value)
        {
            // If user types manually, switch dropdown to Custom unless it matches a known pure template
            if (SelectedLauncher != "Custom")
            {
                // Simple heuristic: if they are typing, force Custom to avoid overwriting their work with template again
                // But we must be careful not to trigger this when we programmatically set it.
                // PropertyChanged is triggered by program set too.
                // We'll rely on user explicit selection for templates. 
                // If they edit the text, we flip to Custom.

                // Actually, `OnSelectedLauncherChanged` sets `EditableLinuxLaunchWrapper`.
                // If that triggers `OnEditableLinuxLaunchWrapperChanged`, we might flip back.
                // We need a flag or check equality.

                if (value == "wine" && SelectedLauncher == "System Wine") return;
                // For complex templates, it's hard to match exactly.

                // Better approach: Just let it be. If they edit, meaningful only if we track "Custom".
                // Let's force "Custom" if they edit something that doesn't look like our standard auto-set.
                // For now, let's mostly trust the dropdown.

                // If they edit the wrapper and the dropdown says "System Wine" (which expects just "wine"), maybe switch to Custom.
                if (SelectedLauncher == "System Wine" && value != "wine")
                {
                    SelectedLauncher = "Custom";
                }
            }
        }


        // Read-only property for UI binding
        public bool IsLinux => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        public bool IsWindows => !IsLinux;


        [ObservableProperty]
        private string _cloudCheckStatus = "";

        [ObservableProperty]
        private string _cloudCheckColor = "#FFFFFF"; // Gray, Green, Red/Orange

        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<string> _availableCloudGames = new();

        private CancellationTokenSource? _cloudCheckCts;
        private readonly RcloneExecutor _rcloneExecutorInternal = new RcloneExecutor();

        // Cache for cloud game list (5-minute TTL)
        private List<string> _cachedCloudGamesList;
        private DateTime _cloudGamesCacheTime = DateTime.MinValue;
        private const int CloudGamesCacheTTLMinutes = 5;

        // Track last cloud check to avoid duplicate network calls
        private string _lastCheckedGameName = "";
        private DateTime _lastCloudCheckTime = DateTime.MinValue;
        private const int CloudCheckCooldownSeconds = 30;

        // Guard flag to prevent overlapping cloud suggestions loads
        private bool _isLoadingCloudGames = false;

        // ========== INITIALIZATION ==========

        private async void InitializeGameProperties(Game game)
        {
            EditableGameName = game.Name;
            EditableExecutablePath = game.ExecutablePath;
            EditableLaunchArguments = game.LaunchArguments ?? "";
            EditableLinuxArguments = game.LinuxArguments ?? "";
            EditableLinuxLaunchWrapper = game.LinuxLaunchWrapper ?? "";

            // Load detected prefix from game data
            try
            {
                var gameData = await ConfigManagement.GetGameData(game);
                EditablePrefix = gameData?.DetectedPrefix ?? "";
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load game prefix");
                EditablePrefix = "";
            }

            // Load cloud game suggestions for autocomplete dropdown
            await LoadCloudGameSuggestionsAsync();

            // Trigger cloud check
            OnEditableGameNameChanged(game.Name);

            // Populate Launchers
            if (IsLinux)
            {
                var detected = LauncherDetector.GetAvailableLaunchers();
                AvailableLaunchers.Clear();
                foreach (var l in detected) AvailableLaunchers.Add(l);

                // Try to deduce current selection
                if (string.IsNullOrEmpty(EditableLinuxLaunchWrapper))
                {
                    // Default, maybe Wine if available, or Custom?
                    // If empty, it uses standard detection in GameLauncher.cs.
                    // We'll leave it as "Custom" or maybe "Default" if we want to be explicit.
                    // But "Custom" is fine.
                    SelectedLauncher = "Custom";
                }
                else
                {
                    if (EditableLinuxLaunchWrapper == "wine") SelectedLauncher = "System Wine";
                    else if (EditableLinuxLaunchWrapper.Contains("heroic://")) SelectedLauncher = "Heroic Games Launcher";
                    else if (EditableLinuxLaunchWrapper.Contains("lutris:")) SelectedLauncher = "Lutris";
                    else if (EditableLinuxLaunchWrapper.Contains("bottles-cli")) SelectedLauncher = "Bottles";
                    else SelectedLauncher = "Custom";
                }
            }
        }



        [RelayCommand]
        private async Task RefreshCloudGamesAsync()
        {
            // Invalidate cache to force fresh fetch
            _cloudGamesCacheTime = DateTime.MinValue;
            await LoadCloudGameSuggestionsAsync();

            // Optional: Provide feedback
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                DebugConsole.WriteInfo($"Cloud game list refreshed. Found {AvailableCloudGames.Count} games.");
            });
        }

        private async Task LoadCloudGameSuggestionsAsync()
        {
            // Prevent overlapping loads
            if (_isLoadingCloudGames)
            {
                DebugConsole.WriteDebug("Cloud games load already in progress, skipping duplicate request");
                return;
            }

            // Check cache first (5-minute TTL)
            if (_cachedCloudGamesList != null &&
                (DateTime.Now - _cloudGamesCacheTime).TotalMinutes < CloudGamesCacheTTLMinutes)
            {
                DebugConsole.WriteDebug($"Using cached cloud games list ({_cachedCloudGamesList.Count} games)");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    AvailableCloudGames.Clear();
                    foreach (var d in _cachedCloudGamesList)
                        AvailableCloudGames.Add(d);
                });
                return;
            }

            _isLoadingCloudGames = true;
            try
            {
                DebugConsole.WriteDebug("Fetching cloud games list from remote...");
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config.CloudConfig.Provider;
                string configPath = RclonePathHelper.GetConfigPath(provider);
                string remoteName = _providerHelper.GetProviderConfigName(provider);
                string remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                var result = await _rcloneExecutorInternal.ExecuteRcloneCommand(
                    $"lsd \"{remotePath}\" --config \"{configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(15),
                    allowedExitCodes: new[] { 3 }
                );

                if (result.Success)
                {
                    var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var dirs = new System.Collections.Generic.List<string>();

                    foreach (var line in lines)
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            string name = string.Join(" ", parts.Skip(4));
                            dirs.Add(name);
                        }
                    }

                    // Cache the results
                    _cachedCloudGamesList = dirs;
                    _cloudGamesCacheTime = DateTime.Now;

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        AvailableCloudGames.Clear();
                        foreach (var d in dirs)
                            AvailableCloudGames.Add(d);
                    });

                    DebugConsole.WriteDebug($"Cloud games list cached with {dirs.Count} items");
                }
                else
                {
                    DebugConsole.WriteWarning("Failed to fetch cloud games list");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load cloud suggestions");
            }
            finally
            {
                _isLoadingCloudGames = false;
            }
        }

        partial void OnEditableGameNameChanged(string value)
        {
            // Debounce check - increase debounce to 1 second and add cooldown check
            _cloudCheckCts?.Cancel();
            _cloudCheckCts = new CancellationTokenSource();
            var token = _cloudCheckCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, token); // Increased debounce to 1 second
                    if (token.IsCancellationRequested) return;

                    // Skip if we've checked this name recently (within 30 seconds)
                    if (value == _lastCheckedGameName &&
                        (DateTime.Now - _lastCloudCheckTime).TotalSeconds < CloudCheckCooldownSeconds)
                    {
                        DebugConsole.WriteDebug("Cloud check skipped - recently checked");
                        return;
                    }

                    await CheckCloudGameExistence(value);
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        private async Task CheckCloudGameExistence(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CloudCheckStatus = "";
                    CloudCheckColor = "Gray";
                });
                return;
            }

            try
            {
                // Show checking status
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CloudCheckStatus = "Checking cloud...";
                    CloudCheckColor = "Gray";
                });

                // Get global provider
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config.CloudConfig.Provider;

                // Sanitize name to match how it's stored
                string sanitizedName = SanitizeGameNameForCheck(gameName);

                // First, try to use cached list if available
                if (_cachedCloudGamesList != null && _cachedCloudGamesList.Count > 0)
                {
                    bool exists = _cachedCloudGamesList.Any(name =>
                        name.Equals(sanitizedName, StringComparison.OrdinalIgnoreCase));

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (exists)
                        {
                            CloudCheckStatus = "✓ Found in Cloud";
                            CloudCheckColor = "#4CC9B0"; // Green-ish
                        }
                        else
                        {
                            CloudCheckStatus = "Not found in Cloud (New)";
                            CloudCheckColor = "Gray";
                        }
                    });

                    // Track this check
                    _lastCheckedGameName = gameName;
                    _lastCloudCheckTime = DateTime.Now;

                    DebugConsole.WriteDebug($"Cloud check via cache: {gameName} - {(exists ? "Found" : "Not found")}");
                    return;
                }

                // Cache miss - do fresh network call
                string configPath = RclonePathHelper.GetConfigPath(provider);
                string remoteName = _providerHelper.GetProviderConfigName(provider);
                string remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                var result = await _rcloneExecutorInternal.ExecuteRcloneCommand(
                    $"lsd \"{remotePath}\" --config \"{configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(10),
                    allowedExitCodes: new[] { 3 }
                );

                if (result.Success)
                {
                    var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    bool exists = lines.Any(line => line.TrimEnd().EndsWith($" {sanitizedName}", StringComparison.OrdinalIgnoreCase));

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (exists)
                        {
                            CloudCheckStatus = "✓ Found in Cloud";
                            CloudCheckColor = "#4CC9B0"; // Green-ish
                        }
                        else
                        {
                            CloudCheckStatus = "Not found in Cloud (New)";
                            CloudCheckColor = "Gray";
                        }
                    });

                    // Update cache with results
                    var dirs = new System.Collections.Generic.List<string>();
                    foreach (var line in lines)
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            string name = string.Join(" ", parts.Skip(4));
                            dirs.Add(name);
                        }
                    }
                    _cachedCloudGamesList = dirs;
                    _cloudGamesCacheTime = DateTime.Now;

                    DebugConsole.WriteDebug($"Cloud check via network: {gameName} - {(exists ? "Found" : "Not found")}");
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        CloudCheckStatus = "Check failed (Connection?)";
                        CloudCheckColor = "#C42B1C"; // Red
                    });
                    DebugConsole.WriteWarning("Cloud check failed - network error");
                }

                // Track this check
                _lastCheckedGameName = gameName;
                _lastCloudCheckTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CloudCheckStatus = "Check Error";
                    CloudCheckColor = "#C42B1C";
                });
                DebugConsole.WriteException(ex, "Cloud check error");
            }
        }

        private static string SanitizeGameNameForCheck(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            return invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_')).Trim();
        }

        // ========== COMMANDS ==========

        [RelayCommand]
        private async Task OpenSteamImportAsync()
        {
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
               ? desktop.MainWindow
               : null;

            if (mainWindow == null) return;

            var dialog = new SaveTracker.Views.Dialog.UC_SteamImport();

            // ShowDialog returns the result when Close(result) is called
            var importedGames = await dialog.ShowDialog<List<Game>>(mainWindow);

            if (importedGames != null && importedGames.Count > 0)
            {
                DebugConsole.WriteSuccess($"Imported {importedGames.Count} Steam games via dialog.");

                foreach (var game in importedGames)
                {
                    await OnGameAddedAsync(game);
                }

                _notificationService?.Show("Steam Import", $"Successfully imported {importedGames.Count} games from Steam.", SaveTracker.Resources.HELPERS.NotificationType.Success);
            }
        }

        [RelayCommand]
        private async Task BrowseForGameExecutableAsync()
        {
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
               ? desktop.MainWindow
               : null;

            if (mainWindow == null) return;

            var topLevel = TopLevel.GetTopLevel(mainWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Game Executable",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Executables") { Patterns = new[] { "*.exe" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                string path = file.Path.LocalPath;

                if (File.Exists(path))
                {
                    EditableExecutablePath = path;
                }
            }
        }

        [RelayCommand]
        private async Task SaveGamePropertiesAsync()
        {
            if (SelectedGame?.Game == null) return;

            try
            {
                var game = SelectedGame.Game;


                // Trigger cloud check for the new name
                if (!string.IsNullOrEmpty(EditableGameName))
                {
                    await CheckCloudGameExistence(EditableGameName);
                }
                string newName = EditableGameName.Trim();
                string newPath = EditableExecutablePath.Trim();
                string oldName = game.Name;

                if (string.IsNullOrEmpty(newName))
                {
                    await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                    {
                        ContentTitle = "Error",
                        ContentMessage = "Game name cannot be empty.",
                        Icon = Icon.Error
                    }).ShowAsync();
                    return;
                }
                if (string.IsNullOrEmpty(newPath) || !File.Exists(newPath))
                {
                    await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                    {
                        ContentTitle = "Error",
                        ContentMessage = "Valid executable path is required.",
                        Icon = Icon.Error
                    }).ShowAsync();
                    return;
                }

                // Check if name changed and if it conflicts
                if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                {
                    bool exists = await ConfigManagement.GameExistsAsync(newName);
                    if (exists)
                    {
                        await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                        {
                            ContentTitle = "Error",
                            ContentMessage = "A game with this name already exists locally.",
                            Icon = Icon.Error
                        }).ShowAsync();
                        return;
                    }

                    // --- CLOUD RENAME LOGIC ---

                    // 1. Sanitize Names
                    string oldSanitized = SanitizeGameNameForCheck(oldName);
                    string newSanitized = SanitizeGameNameForCheck(newName);

                    // 2. Prepare paths
                    var provider = game.LocalConfig.CloudConfig?.UseGlobalSettings == false
                        ? game.LocalConfig.CloudConfig.Provider
                        : (await ConfigManagement.LoadConfigAsync()).CloudConfig.Provider;

                    string configName = new CloudProviderHelper().GetProviderConfigName(provider);
                    string oldRemotePath = $"{configName}:{SaveFileUploadManager.RemoteBaseFolder}/{oldSanitized}";
                    string newRemotePath = $"{configName}:{SaveFileUploadManager.RemoteBaseFolder}/{newSanitized}";

                    // 3. Check if cloud folder exists
                    var rcloneOps = new RcloneFileOperations(game);
                    bool cloudExists = await rcloneOps.CheckCloudSaveExistsAsync(oldRemotePath, provider);

                    if (cloudExists)
                    {
                        // 4. Ask to rename
                        var moveResult = await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                        {
                            ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNoCancel,
                            ContentTitle = "Rename Cloud Folder?",
                            ContentMessage = $"Found matching cloud folder for '{oldName}'.\n\nDo you want to rename it to '{newName}' as well?\n\nYes: Rename local + cloud (Recommended)\nNo: Rename local ONLY (Unlinks from old cloud save)\nCancel: Abort entire operation",
                            Icon = Icon.Question
                        }).ShowAsync();

                        if (moveResult == MsBox.Avalonia.Enums.ButtonResult.Cancel) return;

                        if (moveResult == MsBox.Avalonia.Enums.ButtonResult.Yes)
                        {
                            // Status update
                            DebugConsole.WriteInfo("Renaming cloud folder...");

                            bool success = await rcloneOps.RenameCloudFolder(oldRemotePath, newRemotePath, provider);
                            if (!success)
                            {
                                await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                                {
                                    ContentTitle = "Error",
                                    ContentMessage = "Failed to rename cloud folder. Check internet connection or permissions.\n\nOperation aborted.",
                                    Icon = Icon.Error
                                }).ShowAsync();
                                return;
                            }
                        }
                    }
                }

                // Logic to update game
                var oldInstallDir = game.InstallDirectory;
                var newInstallDir = Path.GetDirectoryName(newPath);

                // Update properties on the object
                game.Name = newName;
                game.ExecutablePath = newPath;
                game.InstallDirectory = newInstallDir;
                game.LaunchArguments = EditableLaunchArguments;
                game.LinuxArguments = EditableLinuxArguments;
                game.LinuxLaunchWrapper = EditableLinuxLaunchWrapper;

                bool nameChanged = !oldName.Equals(newName, StringComparison.OrdinalIgnoreCase);

                if (nameChanged)
                {
                    await ConfigManagement.DeleteGameAsync(oldName);
                }

                // If Install Directory changed, try to move the local checksum file
                if (!string.Equals(oldInstallDir, newInstallDir, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (!Directory.Exists(newInstallDir))
                        {
                            Directory.CreateDirectory(newInstallDir);
                        }

                        // Move ALL profile checksum files (pattern: .savetracker_profile_*.json)
                        if (Directory.Exists(oldInstallDir))
                        {
                            var checksumFiles = Directory.GetFiles(oldInstallDir, ".savetracker_profile_*.json");
                            foreach (var oldPath in checksumFiles)
                            {
                                string filename = Path.GetFileName(oldPath);
                                string newChecksumPath = Path.Combine(newInstallDir, filename);

                                File.Copy(oldPath, newChecksumPath, true);
                                File.Delete(oldPath);
                                DebugConsole.WriteInfo($"Moved profile checksum: {filename}");
                            }

                            if (checksumFiles.Length > 0)
                            {
                                DebugConsole.WriteSuccess($"Moved {checksumFiles.Length} profile checksum file(s) to {newInstallDir}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to move local checksum file: {ex.Message}");
                    }
                }

                await ConfigManagement.SaveGameAsync(game);

                // Update ViewModel
                SelectedGame.Name = newName;
                SelectedGame.InstallDirectory = game.InstallDirectory;
                SelectedGame.ExecutablePath = game.ExecutablePath;
                SelectedGame.Icon = UiHelpers.ExtractIconFromExe(game.ExecutablePath);

                // Update Watcher
                var updatedGamesList = await ConfigManagement.LoadAllGamesAsync();
                _gameProcessWatcher?.UpdateGamesList(updatedGamesList);

                // Update local inputs
                EditableGameName = newName;
                EditableExecutablePath = newPath;

                // 3. Update Extra Data (Prefix)
                var gameData = await ConfigManagement.GetGameData(game);
                if (gameData == null) gameData = new GameUploadData();

                gameData.DetectedPrefix = EditablePrefix;
                await ConfigManagement.SaveGameData(game, gameData);

                DebugConsole.WriteSuccess($"Game properties updated for: {game.Name}");
                _notificationService?.Show("Success", "Game properties saved successfully.", SaveTracker.Resources.HELPERS.NotificationType.Success);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to save game properties");
                await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ContentTitle = "Error",
                    ContentMessage = $"Failed to save changes: {ex.Message}",
                    Icon = Icon.Error
                }).ShowAsync();
            }
        }
    }
}
