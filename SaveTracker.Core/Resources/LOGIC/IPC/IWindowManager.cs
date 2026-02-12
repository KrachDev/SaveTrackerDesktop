using SaveTracker.Resources.SAVE_SYSTEM;
using System.Threading.Tasks;

namespace SaveTracker.Resources.LOGIC.IPC
{
    public interface IWindowManager
    {
        void ShowMainWindow();
        void ShowLibrary();
        void ShowBlacklist();
        void ShowCloudSettings();
        void ShowSettings();
        void TriggerSmartSync(Game game);
        void ReportIssue();


        // Session Management
        Task StartSession(Game game);
        Task EndSession(Game game);

        // Add other UI interactions here if necessary
    }
}
