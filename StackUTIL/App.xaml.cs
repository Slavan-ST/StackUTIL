using DebugInterceptor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackUTIL.Services;
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
            // Глобальный перехват необработанных исключений
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                var path = Path.Combine(AppContext.BaseDirectory, "fatal_error.log");
                File.WriteAllText(path, ex?.ToString());
            };

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddDebugInterceptorServices(context.Configuration);
                    services.AddSingleton<TrayService>();
                    services.AddHostedService(sp => sp.GetRequiredService<TrayService>());
                })
                .Build();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                await _host.StartAsync();
            }
            catch (Exception ex)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "startup_error.log");
                File.WriteAllText(path, ex.ToString()); // Пишет всё дерево исключений

                System.Windows.MessageBox.Show($"Ошибка:\n{ex.InnerException?.Message ?? ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            try { await _host.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка остановки"); }
            finally { _host?.Dispose(); }
            base.OnExit(e);
        }
    }
}