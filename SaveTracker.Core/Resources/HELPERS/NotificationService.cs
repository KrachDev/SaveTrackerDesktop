using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public enum NotificationType
    {
        Information,
        Success,
        Warning,
        Error
    }

    public interface INotificationService
    {
        void Show(string title, string message, NotificationType type = NotificationType.Information);
        Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information);
    }
}
