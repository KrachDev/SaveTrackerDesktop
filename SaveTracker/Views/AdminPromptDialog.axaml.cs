using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SaveTracker.Views
{
    public partial class AdminPromptDialog : Window
    {
        public bool ShouldRestartAsAdmin { get; set; }

        public AdminPromptDialog()
        {
            InitializeComponent();
        }

        private void YesButton_Click(object? sender, RoutedEventArgs e)
        {
            ShouldRestartAsAdmin = true;
            Close();
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e)
        {
            ShouldRestartAsAdmin = false;
            Close();
        }
    }
}