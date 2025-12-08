using Avalonia.Controls;
using Avalonia.Interactivity;
using SaveTracker.ViewModels;
using System;

namespace SaveTracker.Views.Dialog
{
    public partial class AnnouncementWindow : Window
    {
        private AnnouncementViewModel? _viewModel;

        public AnnouncementWindow()
        {
            InitializeComponent();
            _viewModel = new AnnouncementViewModel();
            DataContext = _viewModel;
        }

        private async void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            // Auto-enable "never show again" when closing
            if (_viewModel != null)
            {
                await _viewModel.OnWindowClosingAsync();
            }
            Close();
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            // Also handle when user closes via X button or Alt+F4
            if (_viewModel != null)
            {
                await _viewModel.OnWindowClosingAsync();
            }
            base.OnClosing(e);
        }
    }
}
