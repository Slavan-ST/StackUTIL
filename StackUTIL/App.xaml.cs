// App.xaml.cs
using DebugInterceptor.Services;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting.Internal;
using System.Windows;
using System.Diagnostics;
using DebugInterceptor.Models;

namespace StackUTIL
{
    public partial class App : System.Windows.Application
    {
        private IHost _host;

        public App()
        {
            Debug.WriteLine("🔧 App.ctor начался");

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((context, services) =>
                {
                    Debug.WriteLine("🔧 Регистрация сервисов...");

                    // ❌ HotkeyService НЕ регистрируем в DI — создаётся вручную с окном

                    // Основные сервисы
                    services.AddSingleton<ScreenCaptureService>();
                    services.AddSingleton<OcrService>();
                    services.AddSingleton<DebugDataParser>();

                    // 🔥 ВАЖНО: регистрируем DebugInterceptService ДВУМЯ способами:
                    // 1. Как обычный сервис — чтобы можно было получить по типу
                    services.AddSingleton<DebugInterceptService>();
                    // 2. Как фоновый сервис — чтобы запустился автоматически
                    services.AddHostedService<DebugInterceptService>();

                    // Окна и ViewModel (создаются новые экземпляры при запросе)
                    services.AddTransient<DebugResultWindow>();
                    services.AddTransient<DebugResultViewModel>();// В App.xaml.cs, внутри ConfigureServices:
                    services.AddSingleton<ISqlMonitoringService, SqlMonitoringService>(); // 👈 Ваш реализация

                    // Остальные сервисы перехвата:
                    services.AddSingleton<ScreenCaptureService>();
                    services.AddSingleton<OcrService>();
                    services.AddSingleton<DebugDataParser>();
                    services.AddSingleton<DebugInterceptService>();
                    services.AddHostedService<DebugInterceptService>();

                    services.AddTransient<DebugResultWindow>();
                    services.AddTransient<DebugResultViewModel>();
                    // В ConfigureServices:
                    services.AddSingleton<SettingsManager<AppSettings>>(sp =>
                        new SettingsManager<AppSettings>("appsettings.json",
                            sp.GetRequiredService<ILogger<SettingsManager<AppSettings>>>()));

                    services.AddSingleton<ISqlMonitoringService, SqlMonitoringService>();

                    // Остальные сервисы:
                    services.AddSingleton<ScreenCaptureService>();
                    services.AddSingleton<OcrService>();
                    services.AddSingleton<DebugDataParser>();
                    services.AddSingleton<DebugInterceptService>();
                    services.AddHostedService<DebugInterceptService>();

                    services.AddTransient<DebugResultWindow>();
                    services.AddTransient<DebugResultViewModel>();

                    Debug.WriteLine("✅ Регистрация сервисов завершена");
                })
                .Build();

            Debug.WriteLine("✅ App.ctor завершён");
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            Debug.WriteLine("🚀 App.OnStartup начался");

            // 1. Запуск хоста с обработкой ошибок
            try
            {
                await _host.StartAsync();
                Debug.WriteLine("✅ Хост запущен успешно");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ КРИТИЧЕСКАЯ ОШИБКА запуска хоста: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                System.Windows.MessageBox.Show($"Не удалось запустить приложение:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // 2. Проверка ключевых сервисов
            try
            {
                var ocr = _host.Services.GetService<OcrService>();
                Debug.WriteLine($"🔍 OcrService: {(ocr != null ? "OK" : "NULL")}");

                var parser = _host.Services.GetService<DebugDataParser>();
                Debug.WriteLine($"📝 DebugDataParser: {(parser != null ? "OK" : "NULL")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка при проверке сервисов: {ex.Message}");
            }

            // 3. Создание и показ главного окна
            var mainWindow = new MainWindow();

            // 👇 Инициализируем горячие клавиши ТОЛЬКО после того, как окно создало хэндл
            mainWindow.Loaded += (s, args) =>
            {
                Debug.WriteLine("🪟 MainWindow.Loaded сработал");
                InitializeHotkeysSafely(mainWindow);
            };

            mainWindow.Show();
            Debug.WriteLine("🪟 MainWindow.Show() вызван");

            base.OnStartup(e);
            Debug.WriteLine("✅ App.OnStartup завершён");
        }

        /// <summary>
        /// Безопасная инициализация горячих клавиш с подробным логированием
        /// </summary>
        private void InitializeHotkeysSafely(Window mainWindow)
        {
            try
            {
                Debug.WriteLine("🔑 Начало инициализации горячих клавиш...");

                // Получаем сервис перехвата
                var interceptService = _host.Services.GetService<DebugInterceptService>();

                if (interceptService == null)
                {
                    Debug.WriteLine("❌ interceptService = NULL");

                    // Диагностика: какие сервисы вообще есть?
                    var hosted = _host.Services.GetServices<IHostedService>().ToList();
                    Debug.WriteLine($"📋 Найдено IHostedService: {hosted.Count}");
                    foreach (var h in hosted)
                        Debug.WriteLine($"   - {h.GetType().Name}");

                    return;
                }

                Debug.WriteLine("✅ interceptService найден");

                // Вызываем инициализацию
                interceptService.InitializeHotkeys(mainWindow);
                Debug.WriteLine("🎉 Горячие клавиши инициализированы успешно");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Ошибка инициализации горячих клавиш: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                System.Windows.MessageBox.Show($"Ошибка инициализации:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            Debug.WriteLine("🛑 App.OnExit начался");

            try
            {
                await _host.StopAsync();
                Debug.WriteLine("✅ Хост остановлен");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Ошибка остановки хоста: {ex.Message}");
            }
            finally
            {
                _host?.Dispose();
                Debug.WriteLine("✅ App.OnExit завершён");
            }

            base.OnExit(e);
        }
    }
}