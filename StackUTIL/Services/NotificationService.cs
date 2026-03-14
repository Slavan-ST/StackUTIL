using Microsoft.Extensions.Logging;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DebugInterceptor.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ILogger<INotificationService> _logger;

        public NotificationService(ILogger<INotificationService> logger)
        {
            _logger = logger;
        }

        public void ShowError(string message) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show($"Ошибка:\n{message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error));

        public void ShowWarning(string message, string title = "Внимание") =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(message, title,
                    MessageBoxButton.OK, MessageBoxImage.Warning));

        public void ShowInfo(string message, string title = "Инфо") =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(message, title,
                    MessageBoxButton.OK, MessageBoxImage.Information));
    }
}