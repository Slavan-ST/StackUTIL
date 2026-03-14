using DebugInterceptor.Services;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using DebugInterceptor.Models;

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
                    // ==========================================
                    // 📦 Основные сервисы (Singleton)
                    // ==========================================
                    services.AddSingleton<ScreenCaptureService>();
                    services.AddSingleton<OcrService>();
                    services.AddSingleton<DebugDataParser>();
                    services.AddSingleton<RegionDetector>();
                    services.AddSingleton<BitmapUtility>();

                    // ==========================================
                    // 🔥 DebugInterceptService
                    // ==========================================
                    services.AddSingleton<DebugInterceptService>();

                    // ==========================================
                    // 🪟 UI: Окна и ViewModel (Transient)
                    // ==========================================
                    services.AddTransient<DebugResultWindow>();
                    services.AddTransient<DebugResultViewModel>();

                    // ==========================================
                    // 📡 TrayService (фоновый сервис)
                    // ==========================================
                    // 1. Регистрируем как Singleton — для ручного получения
                    services.AddSingleton<TrayService>();
                    // 2. Регистрируем как IHostedService — для автозапуска
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

            // 👇 1. Получаем TrayService из контейнера (теперь работает!)
            var trayService = _host.Services.GetRequiredService<TrayService>();
            _logger.LogDebug($"🔍 TrayService из контейнера: {trayService.GetHashCode()}");

            // 👇 2. Создаём окно и передаём ему ссылку на сервис
            _mainWindow = new MainWindow();
            trayService?.SetMainWindow(_mainWindow);
            _logger.LogTrace($"🪟 MainWindow создан (HashCode: {_mainWindow.GetHashCode()})");


            _logger.LogTrace("🪟 MainWindow.Loaded: инициализация хоткеев");
            InitializeHotkeysSafely(_mainWindow);

            base.OnStartup(e);
        }

        private void InitializeHotkeysSafely(Window mainWindow)
        {
            try
            {
                var interceptService = _host.Services.GetService<DebugInterceptService>();
                if (interceptService == null)
                {
                    _logger.LogWarning("⚠️ interceptService не найден");
                    return;
                }
                interceptService.InitializeHotkeys(mainWindow);
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