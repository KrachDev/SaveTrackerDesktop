using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Views;
using SaveTracker.Views.Dialog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace SaveTracker
{
    public partial class UC_AddGame : Window
    {
        public UC_AddGame_ViewModel ViewModel { get; }

        public UC_AddGame()
        {
            InitializeComponent();
            ViewModel = new UC_AddGame_ViewModel();
            DataContext = ViewModel; // Bind the ViewModel to the UI
        }

        // Browse for executable
        private async void OnBrowseExecutable(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
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
                DebugConsole.WriteLine(path);
                DebugConsole.WriteLine(directory);
                DebugConsole.WriteLine(name);
                try
                {
                    var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

                    DebugConsole.WriteSection("Executable Info");
                    DebugConsole.WriteKeyValue("Path", path);
                    DebugConsole.WriteKeyValue("FileDescription", info.FileDescription);
                    DebugConsole.WriteKeyValue("ProductName", info.ProductName);
                    DebugConsole.WriteKeyValue("CompanyName", info.CompanyName);
                    DebugConsole.WriteKeyValue("FileVersion", info.FileVersion);
                    DebugConsole.WriteKeyValue("InternalName", info.InternalName);
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "GetExecutableDescription");
                }



                // Update the ViewModel property
                ViewModel.NewGame.ExecutablePath = path;
                ViewModel.NewGame.InstallDirectory = directory;
                ViewModel.NewGame.Name = name;
            }
        }

        private void CancelBTN_Click(object? sender, RoutedEventArgs e)
        {
            DebugConsole.WriteInfo("Close AddWindow");
            this.Close();
        }

        private void AddGameBTN_Click(object? sender, RoutedEventArgs e)
        {
            if (!File.Exists(ViewModel.NewGame.ExecutablePath) ||
                string.IsNullOrEmpty(ViewModel.NewGame.Name) ||
                !Directory.Exists(ViewModel.NewGame.InstallDirectory))
            {
                DebugConsole.WriteError("Complete Data Set");
                return;
            }

            Close(ViewModel.NewGame); // return the completed Game object

        }

    }
}