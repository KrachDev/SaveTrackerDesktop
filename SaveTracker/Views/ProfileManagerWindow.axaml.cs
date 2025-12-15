using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SaveTracker.Views
{
    public partial class ProfileManagerWindow : Window
    {
        public ProfileManagerWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
