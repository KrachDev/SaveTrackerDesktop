using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Diagnostics;
using SaveTracker.ViewModels;
using System.Collections.Generic;

namespace SaveTracker.Views
{
    public partial class ImportSelectionWindow : Window
    {
        public ImportSelectionWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            var vm = new ImportSelectionWindowViewModel();
            DataContext = vm;

            // Handle close request to return result
            vm.OnCloseRequest += (games) =>
            {
                Close(games);
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
