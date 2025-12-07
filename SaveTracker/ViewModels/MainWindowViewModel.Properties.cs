using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using System;
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
        private string _editableExecutablePath = string.Empty;

        [ObservableProperty]
        private string _cloudCheckStatus = "";

        [ObservableProperty]
        private string _cloudCheckColor = "Gray"; // Gray, Green, Red/Orange

        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<string> _availableCloudGames = new();

        private CancellationTokenSource? _cloudCheckCts;
        private readonly RcloneExecutor _rcloneExecutorInternal = new RcloneExecutor();

        // ========== INITIALIZATION ==========

        private void InitializeGameProperties(Game game)
        {
            EditableGameName = game.Name;
            EditableExecutablePath = game.ExecutablePath;
            // Fetch potential matches for autocomplete
            _ = LoadCloudGameSuggestionsAsync();

            // Trigger check initially? Maybe not, or yes.
            OnEditableGameNameChanged(game.Name);
        }

        [RelayCommand]
        private async Task RefreshCloudGamesAsync()
        {
            await LoadCloudGameSuggestionsAsync();

            // Optional: Provide feedback
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                DebugConsole.WriteInfo($"Refreshed cloud game list. Found {AvailableCloudGames.Count} games.");
            });
        }

        private async Task LoadCloudGameSuggestionsAsync()
        {
            try
            {
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config.CloudConfig.Provider;
                string configPath = RclonePathHelper.GetConfigPath(provider);
                string remoteName = _providerHelper.GetProviderConfigName(provider);
                string remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                var result = await _rcloneExecutorInternal.ExecuteRcloneCommand(
                    $"lsd \"{remotePath}\" --config \"{configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(15)
                );

                if (result.Success)
                {
                    // Output format: "      -1 2023-10-26 12:00:00        -1 DirectoryName"
                    var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var dirs = new System.Collections.Generic.List<string>();

                    foreach (var line in lines)
                    {
                        // Extract directory name (last part of the line)
                        // lsd output usually ends with the name. 
                        // We can trim start and split by space, taking the rest? 
                        // Or just take the last part after the last space index that is part of the metadata columns?
                        // lsd columns are fixed width-ish but better to parse carefully.
                        // Format: [Size] [Date] [Time] [Count] [Name]
                        // -1 2022-01-01 12:00:00 -1 My Game Name

                        // Robust approach: split by spaces, skip the first 4 tokens (Size, Date, Time, Count seems to be standard for lsd, or sometimes just -1 -1)
                        // Actually, 'lsd' output: 
                        //           -1 2023-12-07 18:00:00        -1 GameName
                        // The ' -1' is size/count. 

                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            // Rejoin from index 4 to end (0-based) to get the name (which might have spaces)
                            string name = string.Join(" ", parts.Skip(4));
                            dirs.Add(name);
                        }
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        AvailableCloudGames.Clear();
                        foreach (var d in dirs) AvailableCloudGames.Add(d);
                    });
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to load cloud suggestions");
            }
        }

        partial void OnEditableGameNameChanged(string value)
        {
            // Debounce check
            _cloudCheckCts?.Cancel();
            _cloudCheckCts = new CancellationTokenSource();
            var token = _cloudCheckCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token); // 500ms debounce
                    if (token.IsCancellationRequested) return;

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

                // Get global provider (assuming global for now as per request)
                var config = await ConfigManagement.LoadConfigAsync();
                var provider = config.CloudConfig.Provider;

                // Sanitize name to match how it's stored
                string sanitizedName = SanitizeGameNameForCheck(gameName);

                // Get config path
                string configPath = RclonePathHelper.GetConfigPath(provider);
                string remoteName = _providerHelper.GetProviderConfigName(provider);

                // Command: rclone lsd remote:SaveTrackerCloudSave --config ...
                // Note: We scan the ROOT folder (SaveTrackerCloudSave) to see if our game folder exists within it.
                string remotePath = $"{remoteName}:{SaveFileUploadManager.RemoteBaseFolder}";

                // We use lsd to list directories in the root
                var result = await _rcloneExecutorInternal.ExecuteRcloneCommand(
                    $"lsd \"{remotePath}\" --config \"{configPath}\" " + RcloneExecutor.GetPerformanceFlags(),
                    TimeSpan.FromSeconds(10)
                );

                if (result.Success)
                {
                    // Output format: "      -1 2023-10-26 12:00:00        -1 DirectoryName"
                    // Parse line by line
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
                            CloudCheckColor = "Gray"; // or a neutral color/CheckIcon
                        }
                    });
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                   {
                       // If root folder doesn't exist, lsd might fail or return nothing.
                       // Assume not found or error.
                       CloudCheckStatus = "Check failed (Connection?)";
                       CloudCheckColor = "#C42B1C"; // Red
                   });
                }
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
                string newName = EditableGameName.Trim();
                string newPath = EditableExecutablePath.Trim();

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
                if (!game.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
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
                }

                // Logic to update game
                var oldName = game.Name;
                bool nameChanged = !oldName.Equals(newName, StringComparison.OrdinalIgnoreCase);

                // Update properties on the object
                game.Name = newName;
                game.ExecutablePath = newPath;
                game.InstallDirectory = Path.GetDirectoryName(newPath);

                if (nameChanged)
                {
                    await ConfigManagement.DeleteGameAsync(oldName);
                }

                await ConfigManagement.SaveGameAsync(game);

                // Update ViewModel
                SelectedGame.Name = newName;
                // Manually notify changes if needed, but the observable property on GameViewModel wraps Game object fields which are not notifying?
                // GameViewModel implementation copies values.
                // So we need to set them on SelectedGame (GameViewModel).
                // I need to add public setters or methods to GameViewModel to update these, OR re-create the GameViewModel.

                // Since I added _executablePath with ObservableProperty in GameViewModel, I can set it if I expose it.
                // But Name is also there.
                // Assuming I can set them:
                // SelectedGame.Name = newName; // This calls setter on GameViewModel!
                // BUT GameViewModel.Name setter just sets the private field? No, it sets _name.
                // Does it push back to Game object?
                // Checking GameViewModel.cs:
                // public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
                // It does NOT update the underlying Game object automatically unless I linked them.
                // But I updated the underlying Game object above (`game.Name = newName`).
                // So now I just update the UI ViewModel to match.

                // Update Icon
                SelectedGame.Icon = Misc.ExtractIconFromExe(game.ExecutablePath);

                // Update Watcher
                var updatedGamesList = await ConfigManagement.LoadAllGamesAsync();
                _gameProcessWatcher?.UpdateGamesList(updatedGamesList);

                // Update local inputs
                EditableGameName = newName;
                EditableExecutablePath = newPath;

                DebugConsole.WriteSuccess($"Game properties updated: {newName}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to save game properties");
            }
        }
    }
}
