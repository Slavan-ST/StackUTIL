using DebugInterceptor.Models;
using DebugInterceptor.Services;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
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
                    // ⚙️ Настройки через IOptions<T>
                    // ==========================================
                    services.Configure<DebugInterceptorSettings>(
                        context.Configuration.GetSection("DebugInterceptor"));

                    // ==========================================
                    // 📦 Основные сервисы (Singleton)
                    // ==========================================
                    services.AddSingleton<ScreenCaptureService>();
                    services.AddSingleton<OcrService>();
                    services.AddSingleton<DebugDataParser>();
                    services.AddSingleton<BitmapUtility>();
                    services.AddSingleton<INotificationService, NotificationService>();

                    // Сервисы, зависящие от настроек (передаём IOptions<T>, не .Value!)
                    services.AddSingleton<RegionDetector>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<RegionDetector>>();
                        var settings = sp.GetRequiredService<IOptions<DebugInterceptorSettings>>();
                        return new RegionDetector(logger, settings);  // ← IOptions<T>
                    });

                    services.AddSingleton<TooltipValidator>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<TooltipValidator>>();
                        var settings = sp.GetRequiredService<IOptions<DebugInterceptorSettings>>();
                        // Путь вычисляется внутри конструктора TooltipValidator
                        return new TooltipValidator(logger, settings);
                    });

                    services.AddSingleton<DebugResultProcessor>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<DebugResultProcessor>>();
                        var ocr = sp.GetRequiredService<OcrService>();
                        var parser = sp.GetRequiredService<DebugDataParser>();
                        var serviceProvider = sp;
                        var bitmapUtil = sp.GetRequiredService<BitmapUtility>();
                        return new DebugResultProcessor(logger, ocr, parser, serviceProvider, bitmapUtil);
                    });

                    // ==========================================
                    // 🔥 DebugInterceptService (оркестратор)
                    // ==========================================
                    services.AddSingleton<DebugInterceptService>(sp =>
                    {
                        return new DebugInterceptService(
                            sp.GetRequiredService<ILogger<DebugInterceptService>>(),
                            sp.GetRequiredService<ScreenCaptureService>(),
                            sp.GetRequiredService<OcrService>(),
                            sp.GetRequiredService<DebugDataParser>(),
                            sp.GetRequiredService<RegionDetector>(),
                            sp.GetRequiredService<BitmapUtility>(),
                            sp.GetRequiredService<TooltipValidator>(),
                            sp.GetRequiredService<DebugResultProcessor>(),
                            sp,
                            sp.GetRequiredService<INotificationService>(),
                            sp.GetRequiredService<IOptions<DebugInterceptorSettings>>()  // ← передаём IOptions<T>
                        );
                    });

                    // ==========================================
                    // 🪟 UI: Окна и ViewModel (Transient)
                    // ==========================================
                    services.AddTransient<DebugResultWindow>();
                    services.AddTransient<DebugResultViewModel>();
                    services.AddTransient<MainWindow>();

                    // ==========================================
                    // 📡 TrayService (фоновый сервис)
                    // ==========================================
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
            _logger.LogDebug($"🔍 TrayService из контейнера: {trayService.GetHashCode()}");

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