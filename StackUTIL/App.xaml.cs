// App.xaml.cs
using DebugInterceptor.Services;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using DebugInterceptor.Models;
using System;
using System.Linq;
using System.Collections.Generic;

namespace StackUTIL
{
    public partial class App : System.Windows.Application
    {
        private IHost _host;
        private ILogger<App> _logger;
        private MainWindow? _mainWindow; // 👈 Храним ссылку на главное окно

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

                    // ==========================================
                    // 🔥 DebugInterceptService
                    // ==========================================
                    services.AddSingleton<DebugInterceptService>();

                    // ==========================================
                    // 🪟 UI: Окна и ViewModel (Transient)
                    // ==========================================
                    services.AddTransient<DebugResultWindow>();
                    services.AddTransient<DebugResultViewModel>();

                    // 👇 Регистрация фабрики MainWindow для TrayService
                    services.AddTransient<MainWindow>();
                    services.AddTransient<Func<MainWindow>>(sp =>
                        () => sp.GetRequiredService<MainWindow>());

                    // ==========================================
                    // 📡 TrayService (фоновый сервис)
                    // ==========================================
                    services.AddHostedService<TrayService>();

                    // ==========================================
                    // ⚙️ Настройки: SettingsManager<T>
                    // ==========================================
                    services.AddSingleton<SettingsManager<AppSettings>>(sp =>
                        new SettingsManager<AppSettings>(
                            "appsettings.json",
                            sp.GetRequiredService<ILogger<SettingsManager<AppSettings>>>()));
                })
                .Build();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _logger.LogDebug("🔧 App.ctor: хост построен");
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            _logger.LogInformation("🚀 App.OnStartup: начало запуска");

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

            // 👇 Создаём и показываем главное окно (как раньше)
            _mainWindow = new MainWindow();
            _logger.LogTrace("🪟 MainWindow создан");

            // Инициализация горячих клавиш после появления хэндла
            _mainWindow.Loaded += (s, args) =>
            {
                _logger.LogTrace("🪟 MainWindow.Loaded: инициализация хоткеев");
                InitializeHotkeysSafely(_mainWindow);

                // 👇 После инициализации передаём окно в TrayService для управления
                var trayService = _host.Services.GetService<TrayService>();
                trayService?.SetMainWindow(_mainWindow);
            };

            _mainWindow.Show();
            _logger.LogInformation("🪟 MainWindow показан");
            base.OnStartup(e);
        }

        /// <summary>
        /// Безопасная инициализация горячих клавиш
        /// </summary>
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
            _logger.LogInformation("🛑 App.OnExit: завершение работы");

            try
            {
                await _host.StopAsync();
                _logger.LogInformation("✅ Хост остановлен");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠ Ошибка остановки хоста");
            }
            finally
            {
                _host?.Dispose();
                _logger.LogDebug("✅ Ресурсы освобождены");
            }

            base.OnExit(e);
        }
    }
}