using Avalonia.Controls;
using Avalonia.Interactivity;
using SaveTracker.ViewModels;

namespace SaveTracker.Views.Dialog
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = new SettingsViewModel();
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
