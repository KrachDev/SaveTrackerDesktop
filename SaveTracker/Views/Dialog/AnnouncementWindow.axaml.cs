using Avalonia.Controls;
using Avalonia.Interactivity;
using SaveTracker.ViewModels;
using System;

namespace SaveTracker.Views.Dialog
{
    public partial class AnnouncementWindow : Window
    {
        private AnnouncementViewModel? _viewModel;
        private bool _hasBeenSaved = false;

        public AnnouncementWindow()
        {
            InitializeComponent();
            _viewModel = new AnnouncementViewModel();
            DataContext = _viewModel;
        }

        private async void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            // Save settings based on checkbox state
            if (_viewModel != null && !_hasBeenSaved)
            {
                _hasBeenSaved = true;
                await _viewModel.OnWindowClosingAsync();
            }
            Close();
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            // Also handle when user closes via X button or Alt+F4
            if (_viewModel != null && !_hasBeenSaved)
            {
                _hasBeenSaved = true;
                await _viewModel.OnWindowClosingAsync();
            }
            base.OnClosing(e);
        }
    }
}
