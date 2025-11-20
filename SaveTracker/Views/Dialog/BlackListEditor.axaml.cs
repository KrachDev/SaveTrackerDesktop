using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Newtonsoft.Json;
using SaveTracker.Resources.HELPERS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SaveTracker
{
    public partial class BlackListEditor : Window
    {
        private HashSet<string> _directories;
        private HashSet<string> _extensions;
        private HashSet<string> _fileNames;
        private List<string> _keywords;

        public BlackListEditor()
        {
            InitializeComponent();
            LoadBlacklists();
            SetupEventHandlers();
            PopulateUI();
        }

        private void LoadBlacklists()
        {
            // Load from Ignorlist class
            _directories = new HashSet<string>(Ignorlist.IgnoredDirectoriesSet);
            _extensions = new HashSet<string>(Ignorlist.IgnoredExtensions);
            _fileNames = new HashSet<string>(Ignorlist.IgnoredFileNames);
            _keywords = new List<string>(Ignorlist.IgnoredKeywords);
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
            var filteredDirs = _directories.OrderBy(d => d).ToList();

            foreach (var dir in filteredDirs)
            {
                var item = CreateListItem(dir, () => RemoveDirectory(dir));
                DirectoryListPanel.Children.Add(item);
            }

            DirectoryCountText.Text = $"{_directories.Count} directories";
        }

        private void PopulateExtensions()
        {
            ExtensionWrapPanel.Children.Clear();

            foreach (var ext in _extensions.OrderBy(e => e))
            {
                var chip = CreateChip(ext, () => RemoveExtension(ext));
                ExtensionWrapPanel.Children.Add(chip);
            }

            ExtensionCountText.Text = $"{_extensions.Count} extensions";
        }

        private void PopulateFileNames()
        {
            FileNameWrapPanel.Children.Clear();

            foreach (var fileName in _fileNames.OrderBy(f => f))
            {
                var chip = CreateChip(fileName, () => RemoveFileName(fileName));
                FileNameWrapPanel.Children.Add(chip);
            }

            FileNameCountText.Text = $"{_fileNames.Count} file names";
        }

        private void PopulateKeywords()
        {
            KeywordWrapPanel.Children.Clear();

            foreach (var keyword in _keywords.OrderBy(k => k))
            {
                var chip = CreateChip(keyword, () => RemoveKeyword(keyword));
                KeywordWrapPanel.Children.Add(chip);
            }

            KeywordCountText.Text = $"{_keywords.Count} keywords";
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
                if (_directories.Add(result))
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

            if (_extensions.Add(ext))
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

            if (_fileNames.Add(fileName))
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

            if (!_keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                _keywords.Add(keyword);
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
            if (_directories.Remove(dir))
            {
                PopulateDirectories();
                DebugConsole.WriteInfo($"Removed directory: {dir}");
            }
        }

        private void RemoveExtension(string ext)
        {
            if (_extensions.Remove(ext))
            {
                PopulateExtensions();
                DebugConsole.WriteInfo($"Removed extension: {ext}");
            }
        }

        private void RemoveFileName(string fileName)
        {
            if (_fileNames.Remove(fileName))
            {
                PopulateFileNames();
                DebugConsole.WriteInfo($"Removed file name: {fileName}");
            }
        }

        private void RemoveKeyword(string keyword)
        {
            if (_keywords.Remove(keyword))
            {
                PopulateKeywords();
                DebugConsole.WriteInfo($"Removed keyword: {keyword}");
            }
        }

        #endregion

        #region Action Handlers

        private async void SaveBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Save to JSON file or update Ignorlist class
                var data = new
                {
                    Directories = _directories.ToList(),
                    Extensions = _extensions.ToList(),
                    FileNames = _fileNames.ToList(),
                    Keywords = _keywords.ToList()
                };

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                string filePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "blacklist_config.json"
                );

                await File.WriteAllTextAsync(filePath, json);

                DebugConsole.WriteSuccess("Blacklist saved successfully!");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to save blacklist");
            }
        }

        private void ResetToDefaultBtn_Click(object? sender, RoutedEventArgs e)
        {
            LoadBlacklists();
            PopulateUI();
            DebugConsole.WriteInfo("Reset to default blacklist");
        }

        private async void ExportBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export Blacklist",
                    DefaultExtension = "json",
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter { Name = "JSON Files", Extensions = { "json" } }
                    }
                };

                var result = await dialog.ShowAsync(this.VisualRoot as Window);

                if (!string.IsNullOrEmpty(result))
                {
                    var data = new
                    {
                        Directories = _directories.ToList(),
                        Extensions = _extensions.ToList(),
                        FileNames = _fileNames.ToList(),
                        Keywords = _keywords.ToList()
                    };

                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    await File.WriteAllTextAsync(result, json);

                    DebugConsole.WriteSuccess($"Exported to: {result}");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to export blacklist");
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
                        _directories = new HashSet<string>(data.Directories ?? new List<string>());
                        _extensions = new HashSet<string>(data.Extensions ?? new List<string>());
                        _fileNames = new HashSet<string>(data.FileNames ?? new List<string>());
                        _keywords = data.Keywords ?? new List<string>();

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
                ? _directories.OrderBy(d => d)
                : _directories.Where(d => d.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                              .OrderBy(d => d);

            foreach (var dir in filteredDirs)
            {
                var item = CreateListItem(dir, () => RemoveDirectory(dir));
                DirectoryListPanel.Children.Add(item);
            }

            DirectoryCountText.Text = $"{filteredDirs.Count()} of {_directories.Count} directories";
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