using DebugInterceptor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackUTIL.Services; // ← namespace с расширениями
using System.IO;
using System.Windows;

namespace StackUTIL
{
    public partial class App : System.Windows.Application
    {
        private IHost _host;
        private ILogger<App> _logger;
        private MainWindow? _mainWindow;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((context, services) =>
                {
                    // ✅ Вся регистрация — в одном методе
                    services.AddDebugInterceptorServices(context.Configuration);

                    // 📡 TrayService (фоновый сервис)
                    services.AddSingleton<TrayService>();
                    services.AddHostedService(sp => sp.GetRequiredService<TrayService>());
                })
                .Build();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _logger.LogDebug("🔧 App.ctor: хост построен");
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            _logger.LogInformation("🚀 App.OnStartup: начало");

            try
            {
                await _host.StartAsync();
                _logger.LogInformation("✅ Хост запущен");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "❌ Ошибка запуска хоста");
                System.Windows.MessageBox.Show($"Не удалось запустить приложение:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var trayService = _host.Services.GetRequiredService<TrayService>();
            _mainWindow = new MainWindow();
            trayService?.SetMainWindow(_mainWindow);

            InitializeHotkeysSafely(_mainWindow);
            base.OnStartup(e);
        }

        private void InitializeHotkeysSafely(Window mainWindow)
        {
            try
            {
                var interceptService = _host.Services.GetService<DebugInterceptService>();
                interceptService?.InitializeHotkeys(mainWindow);
                _logger.LogInformation("🎉 Горячие клавиши инициализированы");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка инициализации хоткеев");
                System.Windows.MessageBox.Show($"Ошибка инициализации:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _logger.LogInformation("🛑 App.OnExit: завершение");
            try { await _host.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка остановки"); }
            finally { _host?.Dispose(); }
            base.OnExit(e);
        }
    }
}