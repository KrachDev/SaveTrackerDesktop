using Avalonia.Controls;
using SaveTracker.ViewModels;

namespace SaveTracker.Views
{
    public partial class CloudLibraryWindow : Window
    {
        public CloudLibraryWindow()
        {
            InitializeComponent();
            DataContext = new CloudLibraryViewModel();
        }
    }
}
