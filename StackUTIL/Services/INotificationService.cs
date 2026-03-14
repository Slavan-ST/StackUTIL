namespace DebugInterceptor.Services
{
    public interface INotificationService
    {
        void ShowError(string message);
        void ShowWarning(string message, string title = "Внимание");
        void ShowInfo(string message, string title = "Инфо");
    }
}