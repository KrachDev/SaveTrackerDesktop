using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SaveTracker.Views
{
    public partial class SmartSyncWindow : Window
    {
        public SmartSyncWindow()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
