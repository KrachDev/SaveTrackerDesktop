using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using Newtonsoft.Json;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SaveTracker
{
    public partial class BlackListEditor : Window
    {
        private BlacklistManager _manager => BlacklistManager.Instance;

        public BlackListEditor()
        {
            InitializeComponent();
            LoadBlacklists();
            SetupEventHandlers();
            PopulateUI();
        }

        private void LoadBlacklists()
        {
            // Data is managed by BlacklistManager.Instance
        }

        private void SetupEventHandlers()
        {
            AddDirectoryBtn.Click += AddDirectoryBtn_Click;
            AddExtensionBtn.Click += AddExtensionBtn_Click;
            AddFileNameBtn.Click += AddFileNameBtn_Click;
            AddKeywordBtn.Click += AddKeywordBtn_Click;

            SaveBtn.Click += SaveBtn_Click;
            ResetToDefaultBtn.Click += ResetToDefaultBtn_Click;
            ExportBtn.Click += ExportBtn_Click;
            ImportBtn.Click += ImportBtn_Click;

            // Search functionality
            DirectorySearchBox.TextChanged += (s, e) => FilterDirectories(DirectorySearchBox.Text);
        }

        #region UI Population

        private void PopulateUI()
        {
            PopulateDirectories();
            PopulateExtensions();
            PopulateFileNames();
            PopulateKeywords();
        }

        private void PopulateDirectories()
        {
            DirectoryListPanel.Children.Clear();
            var filteredDirs = _manager.Directories.OrderBy(d => d).ToList();

            foreach (var dir in filteredDirs)
            {
                var item = CreateListItem(dir, () => RemoveDirectory(dir));
                DirectoryListPanel.Children.Add(item);
            }

            DirectoryCountText.Text = $"{_manager.Directories.Count} directories";
        }

        private void PopulateExtensions()
        {
            ExtensionWrapPanel.Children.Clear();

            foreach (var ext in _manager.Extensions.OrderBy(e => e))
            {
                var chip = CreateChip(ext, () => RemoveExtension(ext));
                ExtensionWrapPanel.Children.Add(chip);
            }

            ExtensionCountText.Text = $"{_manager.Extensions.Count} extensions";
        }

        private void PopulateFileNames()
        {
            FileNameWrapPanel.Children.Clear();

            foreach (var fileName in _manager.FileNames.OrderBy(f => f))
            {
                var chip = CreateChip(fileName, () => RemoveFileName(fileName));
                FileNameWrapPanel.Children.Add(chip);
            }

            FileNameCountText.Text = $"{_manager.FileNames.Count} file names";
        }

        private void PopulateKeywords()
        {
            KeywordWrapPanel.Children.Clear();

            foreach (var keyword in _manager.Keywords.OrderBy(k => k))
            {
                var chip = CreateChip(keyword, () => RemoveKeyword(keyword));
                KeywordWrapPanel.Children.Add(chip);
            }

            KeywordCountText.Text = $"{_manager.Keywords.Count} keywords";
        }

        #endregion

        #region UI Helpers

        private Border CreateListItem(string text, Action onRemove)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#333333")),
                BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(textBlock, 0);

            var removeBtn = new Button
            {
                Content = "✕",
                Background = new SolidColorBrush(Color.Parse("#EF4444")),
                Foreground = Brushes.White,
                Padding = new Thickness(8, 4),
                CornerRadius = new CornerRadius(4),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            removeBtn.Click += (s, e) => onRemove();
            Grid.SetColumn(removeBtn, 1);

            grid.Children.Add(textBlock);
            grid.Children.Add(removeBtn);
            border.Child = grid;

            return border;
        }

        private Border CreateChip(string text, Action onRemove)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#374151")),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 6),
                Margin = new Thickness(5)
            };

            var stack = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8
            };

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 12
            };

            var removeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#EF4444")),
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(0),
                Width = 16,
                Height = 16,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            removeBtn.Click += (s, e) => onRemove();

            stack.Children.Add(textBlock);
            stack.Children.Add(removeBtn);
            border.Child = stack;

            return border;
        }

        #endregion

        #region Add Handlers

        private async void AddDirectoryBtn_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Directory to Ignore"
            };

            var result = await dialog.ShowAsync(this.VisualRoot as Window);

            if (!string.IsNullOrEmpty(result))
            {
                if (_manager.AddDirectory(result))
                {
                    PopulateDirectories();
                    DebugConsole.WriteSuccess($"Added directory: {result}");
                }
                else
                {
                    DebugConsole.WriteWarning($"Directory already in list: {result}");
                }
            }
        }

        private void AddExtensionBtn_Click(object? sender, RoutedEventArgs e)
        {
            var ext = ExtensionInput.Text?.Trim();

            if (string.IsNullOrEmpty(ext))
            {
                DebugConsole.WriteWarning("Please enter an extension");
                return;
            }

            // Ensure it starts with a dot
            if (!ext.StartsWith("."))
                ext = "." + ext;

            if (_manager.AddExtension(ext))
            {
                ExtensionInput.Text = string.Empty;
                PopulateExtensions();
                DebugConsole.WriteSuccess($"Added extension: {ext}");
            }
            else
            {
                DebugConsole.WriteWarning($"Extension already in list: {ext}");
            }
        }

        private void AddFileNameBtn_Click(object? sender, RoutedEventArgs e)
        {
            var fileName = FileNameInput.Text?.Trim();

            if (string.IsNullOrEmpty(fileName))
            {
                DebugConsole.WriteWarning("Please enter a file name");
                return;
            }

            if (_manager.AddFileName(fileName))
            {
                FileNameInput.Text = string.Empty;
                PopulateFileNames();
                DebugConsole.WriteSuccess($"Added file name: {fileName}");
            }
            else
            {
                DebugConsole.WriteWarning($"File name already in list: {fileName}");
            }
        }

        private void AddKeywordBtn_Click(object? sender, RoutedEventArgs e)
        {
            var keyword = KeywordInput.Text?.Trim().ToLower();

            if (string.IsNullOrEmpty(keyword))
            {
                DebugConsole.WriteWarning("Please enter a keyword");
                return;
            }

            if (_manager.AddKeyword(keyword))
            {
                KeywordInput.Text = string.Empty;
                PopulateKeywords();
                DebugConsole.WriteSuccess($"Added keyword: {keyword}");
            }
            else
            {
                DebugConsole.WriteWarning($"Keyword already in list: {keyword}");
            }
        }

        #endregion

        #region Remove Handlers

        private void RemoveDirectory(string dir)
        {
            if (_manager.RemoveDirectory(dir))
            {
                PopulateDirectories();
                DebugConsole.WriteInfo($"Removed directory: {dir}");
            }
        }

        private void RemoveExtension(string ext)
        {
            if (_manager.RemoveExtension(ext))
            {
                PopulateExtensions();
                DebugConsole.WriteInfo($"Removed extension: {ext}");
            }
        }

        private void RemoveFileName(string fileName)
        {
            if (_manager.RemoveFileName(fileName))
            {
                PopulateFileNames();
                DebugConsole.WriteInfo($"Removed file name: {fileName}");
            }
        }

        private void RemoveKeyword(string keyword)
        {
            if (_manager.RemoveKeyword(keyword))
            {
                PopulateKeywords();
                DebugConsole.WriteInfo($"Removed keyword: {keyword}");
            }
        }

        #endregion

        #region Action Handlers

        private async void SaveBtn_Click(object? sender, RoutedEventArgs e)
        {
            await _manager.SaveAsync();
            DebugConsole.WriteSuccess("Blacklist saved successfully!");
        }

        private void ResetToDefaultBtn_Click(object? sender, RoutedEventArgs e)
        {
            _manager.Load();
            PopulateUI();
            DebugConsole.WriteInfo("Reset to default blacklist");
        }

        private async void ExportBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = "Export Blacklist",
                        DefaultExtension = "json",
                        FileTypeChoices = new[]
                        {
                    new FilePickerFileType("JSON Files")
                    {
                        Patterns = new[] { "*.json" }
                    }
                        }
                    }
                );

                if (file != null)
                {
                    var data = new
                    {
                        Directories = _manager.Directories.ToList(),
                        Extensions = _manager.Extensions.ToList(),
                        FileNames = _manager.FileNames.ToList(),
                        Keywords = _manager.Keywords.ToList()
                    };

                    var json = JsonConvert.SerializeObject(data, Formatting.Indented);

                    await File.WriteAllTextAsync(file.Path.LocalPath, json);

                    var box = MessageBoxManager.GetMessageBoxStandard("File Path", file.Path.LocalPath);
                    await box.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var box = MessageBoxManager.GetMessageBoxStandard("Error", ex.Message);
                await box.ShowAsync();
            }
        }

        private async void ImportBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Import Blacklist",
                    AllowMultiple = false,
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter { Name = "JSON Files", Extensions = { "json" } }
                    }
                };

                var result = await dialog.ShowAsync(this.VisualRoot as Window);

                if (result != null && result.Length > 0)
                {
                    string json = await File.ReadAllTextAsync(result[0]);
                    var data = JsonConvert.DeserializeObject<BlacklistData>(json);

                    if (data != null)
                    {
                        _manager.Directories.Clear();
                        foreach (var d in data.Directories ?? new List<string>()) _manager.Directories.Add(d);

                        _manager.Extensions.Clear();
                        foreach (var ext in data.Extensions ?? new List<string>()) _manager.Extensions.Add(ext);

                        _manager.FileNames.Clear();
                        foreach (var fn in data.FileNames ?? new List<string>()) _manager.FileNames.Add(fn);

                        _manager.Keywords.Clear();
                        foreach (var k in data.Keywords ?? new List<string>()) _manager.Keywords.Add(k);

                        await _manager.SaveAsync();
                        PopulateUI();
                        DebugConsole.WriteSuccess($"Imported from: {result[0]}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to import blacklist");
            }
        }

        #endregion

        #region Search/Filter

        private void FilterDirectories(string? searchText)
        {
            DirectoryListPanel.Children.Clear();

            var filteredDirs = string.IsNullOrWhiteSpace(searchText)
                ? _manager.Directories.OrderBy(d => d)
                : _manager.Directories.Where(d => d.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                              .OrderBy(d => d);

            foreach (var dir in filteredDirs)
            {
                var item = CreateListItem(dir, () => RemoveDirectory(dir));
                DirectoryListPanel.Children.Add(item);
            }

            DirectoryCountText.Text = $"{filteredDirs.Count()} of {_manager.Directories.Count} directories";
        }

        #endregion

        // Helper class for JSON deserialization
        private class BlacklistData
        {
            public List<string> Directories { get; set; }
            public List<string> Extensions { get; set; }
            public List<string> FileNames { get; set; }
            public List<string> Keywords { get; set; }
        }
    }
}