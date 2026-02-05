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
        
        // Add other UI interactions here if necessary
    }
}
