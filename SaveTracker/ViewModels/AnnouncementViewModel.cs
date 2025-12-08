using CommunityToolkit.Mvvm.ComponentModel;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SaveTracker.ViewModels
{
    public partial class AnnouncementViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _announcementContent = string.Empty;

        [ObservableProperty]
        private bool _neverShowAgain;

        private string _currentVersion = string.Empty;

        public AnnouncementViewModel()
        {
            LoadAnnouncementContent();

            // Get current version
            var updateChecker = new Resources.Logic.AutoUpdater.UpdateChecker();
            _currentVersion = updateChecker.GetCurrentVersion();

            // Default to unchecked
            NeverShowAgain = false;
        }

        private void LoadAnnouncementContent()
        {
            try
            {
                // Load from embedded resource
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "SaveTracker.announcement.md";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            AnnouncementContent = reader.ReadToEnd();
                        }
                    }
                    else
                    {
                        AnnouncementContent = "# Welcome to SaveTracker!\n\nNo announcements at this time.";
                        DebugConsole.WriteWarning($"Embedded resource '{resourceName}' not found");
                    }
                }
            }
            catch (Exception ex)
            {
                AnnouncementContent = "# Error\n\nFailed to load announcements.";
                DebugConsole.WriteException(ex, "Failed to load announcement.md from embedded resources");
            }
        }

        /// <summary>
        /// Called when the window is closing to save the current version
        /// </summary>
        public async Task OnWindowClosingAsync()
        {
            // Only mark version as seen if user checked "don't show again"
            if (NeverShowAgain)
            {
                await VersionManager.MarkVersionAsSeenAsync(_currentVersion);
            }
            else
            {
                DebugConsole.WriteInfo("User did not check 'don't show again' - announcement will show next time");
            }
        }
    }
}
