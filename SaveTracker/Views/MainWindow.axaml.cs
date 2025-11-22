using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using SaveTracker.ViewModels;
using SaveTracker.Views.Dialog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SaveTracker.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _viewModel;

        public MainWindow()
        {
            DebugConsole.WriteInfo("=== MainWindow Constructor START ===");

            InitializeComponent();

            DebugConsole.WriteInfo("InitializeComponent completed");

            // DataContext will be set by App.axaml.cs, so we wait for it
            DataContextChanged += OnDataContextChanged;

            DebugConsole.WriteSuccess("=== MainWindow Constructor COMPLETE ===");
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // Unsubscribe to prevent multiple calls
            DataContextChanged -= OnDataContextChanged;

            if (DataContext is MainWindowViewModel viewModel)
            {
                _viewModel = viewModel;
                DebugConsole.WriteInfo($"ViewModel received via DataContext: {_viewModel != null}");

                // Subscribe to events
                DebugConsole.WriteInfo("Subscribing to ViewModel events...");

                _viewModel.OnAddGameRequested += ShowAddGameDialog;
                DebugConsole.WriteInfo($"- OnAddGameRequested subscribed. Handler count: {_viewModel.GetAddGameRequestedSubscriberCount()}");

                _viewModel.OnAddFilesRequested += ShowAddFilesDialog;
                DebugConsole.WriteInfo($"- OnAddFilesRequested subscribed");

                _viewModel.OnCloudSettingsRequested += ShowCloudSettings;
                DebugConsole.WriteInfo($"- OnCloudSettingsRequested subscribed");

                _viewModel.OnBlacklistRequested += ShowBlacklist;
                DebugConsole.WriteInfo($"- OnBlacklistRequested subscribed");

                _viewModel.OnRcloneSetupRequired += ShowRcloneSetup;
                DebugConsole.WriteInfo($"- OnRcloneSetupRequired subscribed");

                DebugConsole.WriteSuccess("ViewModel event subscriptions complete");
            }
            else
            {
                DebugConsole.WriteError("DataContext is not MainWindowViewModel!");
            }
        }

        private async void ShowAddGameDialog()
        {
            if (_viewModel == null) return;

            try
            {
                DebugConsole.WriteInfo("=== ShowAddGameDialog CALLED ===");
                DebugConsole.WriteInfo($"MainWindow IsVisible: {IsVisible}");

                // Ensure we're on UI thread and window is ready
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        DebugConsole.WriteInfo("Creating UC_AddGame window...");
                        var window = new UC_AddGame();

                        DebugConsole.WriteInfo($"Window created. About to show dialog...");
                        DebugConsole.WriteInfo($"Parent window (this) IsVisible: {this.IsVisible}");
                        DebugConsole.WriteInfo($"Parent window (this) IsActive: {this.IsActive}");

                        // Make sure parent window is active
                        if (!this.IsActive)
                        {
                            this.Activate();
                            await Task.Delay(100); // Small delay to ensure activation
                        }

                        // Show as modal dialog
                        var result = await window.ShowDialog<Game?>(this);
                        DebugConsole.WriteInfo("ShowDialog returned");

                        // Check the result after dialog closes
                        if (result != null)
                        {
                            DebugConsole.WriteSuccess($"Game returned: {result.Name}");
                            await _viewModel.OnGameAddedAsync(result);
                        }
                        else
                        {
                            DebugConsole.WriteInfo("No game was added (cancelled or no result)");
                        }

                        DebugConsole.WriteSuccess("Dialog closed successfully!");
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, "Failed in dialog show (inner)");
                        DebugConsole.WriteError($"Exception: {ex.Message}");
                        DebugConsole.WriteError($"Stack: {ex.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show add game dialog");
                DebugConsole.WriteError($"Exception: {ex.Message}");
                DebugConsole.WriteError($"Stack: {ex.StackTrace}");
            }
        }

        private async void ShowAddFilesDialog()
        {
            if (_viewModel == null) return;

            try
            {
                DebugConsole.WriteInfo("=== ShowAddFilesDialog CALLED ===");

                if (_viewModel.SelectedGame == null)
                {
                    DebugConsole.WriteWarning("No game selected");
                    return;
                }

                var fileDialog = new OpenFileDialog
                {
                    Title = "Select Files to Track",
                    AllowMultiple = true,
                    Directory = _viewModel.SelectedGame.Game.InstallDirectory
                };

                DebugConsole.WriteInfo("Opening file dialog...");
                var selectedFiles = await fileDialog.ShowAsync(this);

                if (selectedFiles != null && selectedFiles.Length > 0)
                {
                    DebugConsole.WriteInfo($"User selected {selectedFiles.Length} files");
                    await _viewModel.OnFilesAddedAsync(selectedFiles);
                    DebugConsole.WriteSuccess($"Added {selectedFiles.Length} file(s)");
                }
                else
                {
                    DebugConsole.WriteInfo("No files selected");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to show add files dialog");
            }
        }

        private async void ShowCloudSettings()
        {
            try
            {
                DebugConsole.WriteInfo("=== ShowCloudSettings CALLED ===");
                await Misc.RcloneSetup(this);
                DebugConsole.WriteInfo("Cloud settings closed");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open cloud settings");
            }
        }

        private void ShowBlacklist()
        {
            try
            {
                DebugConsole.WriteInfo("=== ShowBlacklist CALLED ===");

                var blistEditor = new BlackListEditor
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                DebugConsole.WriteInfo("BlackListEditor instance created, showing as dialog...");

                // Show as modal dialog
                _ = blistEditor.ShowDialog(this);

                DebugConsole.WriteSuccess("BlackListEditor shown");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open blacklist editor");
            }
        }

        private async void ShowRcloneSetup()
        {
            try
            {
                DebugConsole.WriteInfo("=== ShowRcloneSetup CALLED ===");
                await Misc.RcloneSetup(this);
                DebugConsole.WriteInfo("Rclone setup closed");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to setup Rclone");
            }
        }

        private void TrackedFile_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (sender is Border border && border.DataContext is TrackedFileViewModel fileVm)
            {
                fileVm.OpenFileLocation();
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == WindowStateProperty)
            {
                if (WindowState == WindowState.Minimized)
                {
                    // Hide the window when minimized
                    Hide();
                    DebugConsole.WriteInfo("Window minimized to tray");
                }
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (_viewModel != null)
            {
                _viewModel.OnAddGameRequested -= ShowAddGameDialog;
                _viewModel.OnAddFilesRequested -= ShowAddFilesDialog;
                _viewModel.OnCloudSettingsRequested -= ShowCloudSettings;
                _viewModel.OnBlacklistRequested -= ShowBlacklist;
                _viewModel.OnRcloneSetupRequired -= ShowRcloneSetup;
            }
        }
    }
}