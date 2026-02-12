using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC.IPC;
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.Headless
{
    public class HeadlessWindowManager : IWindowManager
    {
        public void ShowMainWindow()
        {
            DebugConsole.WriteWarning("Received 'ShowMainWindow' command, but running in Headless mode.");
        }

        public void ShowLibrary()
        {
            DebugConsole.WriteWarning("Received 'ShowLibrary' command, but running in Headless mode.");
        }

        public void ShowBlacklist()
        {
            DebugConsole.WriteWarning("Received 'ShowBlacklist' command, but running in Headless mode.");
        }

        public void ShowCloudSettings()
        {
            DebugConsole.WriteWarning("Received 'ShowCloudSettings' command, but running in Headless mode.");
        }

        public void ShowSettings()
        {
            DebugConsole.WriteWarning("Received 'ShowSettings' command, but running in Headless mode.");
        }

        public void TriggerSmartSync(Game game)
        {
            DebugConsole.WriteWarning($"Received 'TriggerSmartSync' for {game.Name}, but running in Headless mode. UI interaction required for manual sync.");
        }

        public void ReportIssue()
        {
            DebugConsole.WriteInfo("Received 'ReportIssue'. Opening browser...");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/KrachDev/SaveTrackerDesktop/issues/new",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public async Task StartSession(Game game)
        {
            await HeadlessGameService.Instance.StartTrackingAsync(game);
        }

        public async Task EndSession(Game game)
        {
            await HeadlessGameService.Instance.StopTrackingAsync(game);
        }
    }
}
