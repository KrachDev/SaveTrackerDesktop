using Avalonia.Controls;
using SaveTracker.ViewModels;

namespace SaveTracker.Views.Dialog
{
    public partial class AppIdEntryWindow : Window
    {
        public AppIdEntryWindow()
        {
            InitializeComponent();
        }

        public AppIdEntryWindow(AppIdEntryViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.RequestClose += (result) =>
            {
                Close(result);
            };
        }
    }
}
