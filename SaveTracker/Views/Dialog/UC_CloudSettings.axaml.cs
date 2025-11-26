using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SaveTracker.ViewModels;

namespace SaveTracker.Views.Dialog
{
    public partial class UC_CloudSettings : UserControl
    {
        public UC_CloudSettings()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
