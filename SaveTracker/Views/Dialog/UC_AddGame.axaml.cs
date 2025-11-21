using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.Views.Dialog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SaveTracker
{
    public partial class UC_AddGame : Window
    {
        public UC_AddGame_ViewModel ViewModel { get; }
        public Game? ResultGame { get; private set; }

        public UC_AddGame()
        {
            try
            {
                DebugConsole.WriteInfo("UC_AddGame constructor starting...");
                InitializeComponent();
                DebugConsole.WriteInfo("InitializeComponent completed");

                ViewModel = new UC_AddGame_ViewModel();
                DataContext = ViewModel;

                DebugConsole.WriteSuccess("UC_AddGame initialized successfully");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "UC_AddGame constructor failed");
                throw;
            }
        }

        private async void OnBrowseExecutable(object sender, RoutedEventArgs e)
        {
            try
            {
                DebugConsole.WriteInfo("OnBrowseExecutable called");

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                {
                    DebugConsole.WriteError("TopLevel is null");
                    return;
                }

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Game Executable",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Executable Files") { Patterns = new[] { "*.exe" } }
                    }
                });

                if (files.Count >= 1)
                {
                    var path = files[0].Path.LocalPath;
                    var directory = Path.GetDirectoryName(path);
                    var name = Misc.GetExecutableDescription(path);

                    DebugConsole.WriteInfo($"Selected file: {path}");
                    DebugConsole.WriteInfo($"Directory: {directory}");
                    DebugConsole.WriteInfo($"Name: {name}");

                    try
                    {
                        var info = FileVersionInfo.GetVersionInfo(path);
                        DebugConsole.WriteSection("Executable Info");
                        DebugConsole.WriteKeyValue("Path", path);
                        DebugConsole.WriteKeyValue("FileDescription", info.FileDescription);
                        DebugConsole.WriteKeyValue("ProductName", info.ProductName);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, "GetExecutableDescription");
                    }

                    ViewModel.NewGame.ExecutablePath = path;
                    ViewModel.NewGame.InstallDirectory = directory ?? string.Empty;
                    ViewModel.NewGame.Name = name;

                    DebugConsole.WriteSuccess("Game info updated in ViewModel");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "OnBrowseExecutable failed");
            }
        }

        private void CancelBTN_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                DebugConsole.WriteInfo("Cancel button clicked");
                ResultGame = null;
                Close();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "CancelBTN_Click failed");
            }
        }

        private void AddGameBTN_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                DebugConsole.WriteInfo("Add Game button clicked");
                DebugConsole.WriteInfo($"ExecutablePath: {ViewModel.NewGame.ExecutablePath}");
                DebugConsole.WriteInfo($"Name: {ViewModel.NewGame.Name}");
                DebugConsole.WriteInfo($"InstallDirectory: {ViewModel.NewGame.InstallDirectory}");

                if (!File.Exists(ViewModel.NewGame.ExecutablePath))
                {
                    DebugConsole.WriteError("Executable path does not exist");
                    return;
                }

                if (string.IsNullOrEmpty(ViewModel.NewGame.Name))
                {
                    DebugConsole.WriteError("Game name is empty");
                    return;
                }

                if (!Directory.Exists(ViewModel.NewGame.InstallDirectory))
                {
                    DebugConsole.WriteError("Install directory does not exist");
                    return;
                }

                DebugConsole.WriteSuccess("All validation passed - saving game");
                ResultGame = ViewModel.NewGame;
                Close();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "AddGameBTN_Click failed");
            }
        }
    }
}