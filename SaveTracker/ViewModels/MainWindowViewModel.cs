using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace SaveTracker.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            // Subscribe to the event from Program.cs for when files are dropped/opened
            Program.FilesDropped += ProcessStartupArgs;
        }

        public void ProcessStartupArgs(string[] args)
        {
            // Use the helper method to get the cleaned path
            string? gameExePath = GetGamePathFromArgs(args);

            if (!string.IsNullOrEmpty(gameExePath))
            {
                Debug.WriteLine($"Game EXE detected: {gameExePath}");

                // Now you have the string, you can do whatever you want with it
                // Example: LaunchGame(gameExePath);
            }
        }

        // --- The method you asked for ---
        private string? GetGamePathFromArgs(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }

            // The first argument is the file path
            return args[0];
        }
    }
}