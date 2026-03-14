namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Композитный сервис: делегирует вызовы двум реализациям
    /// </summary>
    public class CompositeNotificationService : INotificationService
    {
        private readonly INotificationService _first;
        private readonly INotificationService _second;

        public CompositeNotificationService(INotificationService first, INotificationService second)
        {
            _first = first;
            _second = second;
        }

        public void ShowError(string message)
        {
            _first.ShowError(message);
            _second.ShowError(message);
        }

        public void ShowWarning(string message, string title = "Внимание")
        {
            _first.ShowWarning(message, title);
            _second.ShowWarning(message, title);
        }

        public void ShowInfo(string message, string title = "Инфо")
        {
            _first.ShowInfo(message, title);
            _second.ShowInfo(message, title);
        }
    }
}