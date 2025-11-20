using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MsBox.Avalonia.Enums;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.LOGIC.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public class Misc
    {
        public static string GetExecutableDescription(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return string.Empty;

            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

            // First, try embedded metadata
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
                return info.FileDescription;
            if (!string.IsNullOrWhiteSpace(info.ProductName))
                return info.ProductName;

            // Fallback: filename
            string name = Path.GetFileNameWithoutExtension(path);

            // Optional: insert spaces for CamelCase / PascalCase
            name = System.Text.RegularExpressions.Regex.Replace(name, "(\\B[A-Z])", " $1");

            return name;
        }
        public static ListBoxItem CreateGame(Game game)
        {
            var item = new ListBoxItem();
            var border = new Border
            {
                Padding = new Avalonia.Thickness(10, 8),
                CornerRadius = new CornerRadius(4)
            };
            var grid = new Grid();
            grid.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");

            // Left icon (exe icon)
            Image iconImage = new Image
            {
                Width = 40,
                Height = 40,
                Stretch = Avalonia.Media.Stretch.Uniform,
            };

            // Try to load exe icon
            var iconBitmap = ExtractIconFromExe(game.ExecutablePath);
            iconImage.Source = iconBitmap ;

            var iconBorder = new Border
            {
                Width = 40,
                Height = 40,
                Background = Avalonia.Media.Brushes.Black,
                CornerRadius = new CornerRadius(4),
                Margin = new Avalonia.Thickness(0, 0, 10, 0),
                Child = iconImage
            };
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // Middle stackpanel
            var stackPanel = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(stackPanel, 1);
            stackPanel.Children.Add(new TextBlock
            {
                Text = game.Name,
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = $"Last tracked: {game.LastTracked}",
                FontSize = 11,
                Foreground = Avalonia.Media.Brushes.Gray
            });
            grid.Children.Add(stackPanel);

            // Right indicator
            var indicator = new Border
            {
                Background = Avalonia.Media.Brushes.Gray,
                CornerRadius = new CornerRadius(10),
                Width = 10,
                Height = 10,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(indicator, 2);
            grid.Children.Add(indicator);

            border.Child = grid;
            item.Content = border;
            item.Tag = game;
            return item;
        }

        public static Bitmap? ExtractIconFromExe(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return null;

            try
            {
                // Windows-specific icon extraction
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return ExtractIconWindows(exePath);
                }
                // Linux - no native icon extraction, return null for fallback
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // On Linux, executables don't have embedded icons
                    // You might want to check for .desktop files or icon themes instead
                    return null;
                }
            }
            catch
            {
                // If extraction fails, return null for fallback
            }

            return null;
        }

        private static Bitmap? ExtractIconWindows(string exePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            try
            {
                // Use Windows-specific System.Drawing.Common (requires NuGet package)
                // Install: dotnet add package System.Drawing.Common
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon == null)
                    return null;

                using var ms = new MemoryStream();
                icon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        public static async Task RcloneSetup(Window owner)
        {
            try
            {
                var dialog = new Window
                {
                    Title = "Cloud Storage Settings",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var stackPanel = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 15
                };

                // Provider selection
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Select Cloud Provider:",
                    FontWeight = Avalonia.Media.FontWeight.Bold
                });

                var providerCombo = new ComboBox
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };

                // Populate ComboBox with helper
                var helper = new CloudProviderHelper();
                foreach (var provider in helper.GetSupportedProviders())
                {
                    providerCombo.Items.Add(new ComboBoxItem
                    {
                        Content = helper.GetProviderDisplayName(provider),
                        Tag = provider
                    });
                }
                providerCombo.SelectedIndex = 1; // Box (or adjust based on your preferred default)

                stackPanel.Children.Add(providerCombo);

                // Configure button
                var configBtn = new Button
                {
                    Content = "Configure Now",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Padding = new Avalonia.Thickness(10)
                };

                configBtn.Click += async (s, e) =>
                {
                    // Get selected provider from ComboBox item Tag
                    var selectedItem = providerCombo.SelectedItem as ComboBoxItem;
                    if (selectedItem?.Tag is not CloudProvider selectedProvider)
                    {
                        DebugConsole.WriteError("Please select a cloud provider");
                        return;
                    }

                    configBtn.IsEnabled = false;
                    configBtn.Content = $"Configuring {helper.GetProviderDisplayName(selectedProvider)}...";

                    var rcloneInstaller = new RcloneInstaller();
                    bool success = await rcloneInstaller.SetupConfigAsync(selectedProvider);

                    if (success)
                    {
                        DebugConsole.WriteSuccess($"{helper.GetProviderDisplayName(selectedProvider)} configuration successful!");
                        var config = await ConfigManagement.LoadConfigAsync();
                        config.CloudConfig.Provider = selectedProvider;
                        await ConfigManagement.SaveConfigAsync(config);
                        DebugConsole.WriteSuccess("gloabl Config updated");
                        dialog.Close();
                    }
                    else
                    {
                        configBtn.Content = "Configure Now";
                        configBtn.IsEnabled = true;
                        DebugConsole.WriteError($"{helper.GetProviderDisplayName(selectedProvider)} configuration failed");
                    }
                };

                stackPanel.Children.Add(configBtn);

                // Status text
                var statusText = new TextBlock
                {
                    Text = "ℹ️ Your browser will open for authentication",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.Gray
                };
                stackPanel.Children.Add(statusText);

                dialog.Content = stackPanel;
                await dialog.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open cloud settings");
            }
        }
        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        public static (string text, string color) GetStatusInfo(FileChecksumRecord file, string lastSyncStatus)
        {
            if (string.IsNullOrEmpty(file.Checksum))
                return ("Pending", "#F59E0B"); // Orange

            if (lastSyncStatus == "Failed")
                return ("Failed", "#EF4444"); // Red

            return ("Synced", "#10B981"); // Green
        }
        public static bool HasDoubleExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            string fileName = Path.GetFileName(filePath);
            if (fileName == null)
                return false;

            // Count the dots in the filename
            int dotCount = fileName.Split('.').Length - 1;

            return dotCount >= 2;
        }
        public static string RemoveLastExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileName = Path.GetFileName(filePath);

            int lastDot = fileName.LastIndexOf('.');
            if (lastDot <= 0) // no extension or hidden files like ".gitignore"
                return filePath;

            string newFileName = fileName.Substring(0, lastDot);
            return Path.Combine(directory, newFileName);
        }
    }
    
}
