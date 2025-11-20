using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using SaveTracker.ViewModels;
using SaveTracker.Views;

namespace SaveTracker
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                DisableAvaloniaDataAnnotationValidation();

                // 1. Create the ViewModel instance explicitly
                var mainViewModel = new MainWindowViewModel();

                // 2. Pass initial command line args if they exist
                // This handles the case where you right-click a file -> Open With -> SaveTracker
                if (desktop.Args != null && desktop.Args.Length > 0)
                {
                    // Ensure your MainWindowViewModel has a method named ProcessStartupArgs 
                    // or reuse the method you created for the 'FilesDropped' event.
                    mainViewModel.ProcessStartupArgs(desktop.Args);
                }

                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }
}