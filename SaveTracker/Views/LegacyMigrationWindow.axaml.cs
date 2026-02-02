using Avalonia.Controls;
using SaveTracker.ViewModels;

namespace SaveTracker.Views
{
    public partial class LegacyMigrationWindow : Window
    {
        public LegacyMigrationWindow()
        {
            InitializeComponent();
            DataContext = new LegacyMigrationViewModel();
        }
    }
}
