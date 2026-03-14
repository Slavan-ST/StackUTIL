using Microsoft.Extensions.Logging;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Сервис уведомлений, который пишет только в лог (без MessageBox)
    /// </summary>
    public class LoggingNotificationService : INotificationService
    {
        private readonly ILogger<LoggingNotificationService> _logger;

        /// <summary>
        /// 🔹 Инициализирует новый экземпляр
        /// </summary>
        public LoggingNotificationService(ILogger<LoggingNotificationService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void ShowError(string message) =>
            _logger.LogError("❌ Ошибка: {Message}", message);

        /// <inheritdoc />
        public void ShowWarning(string message, string title = "Внимание") =>
            _logger.LogWarning("⚠ {Title}: {Message}", title, message);

        /// <inheritdoc />
        public void ShowInfo(string message, string title = "Инфо") =>
            _logger.LogInformation("ℹ {Title}: {Message}", title, message);
    }
}