using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SaveTracker.Views
{
    public partial class QuarantineWindow : Window
    {
        public QuarantineWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
