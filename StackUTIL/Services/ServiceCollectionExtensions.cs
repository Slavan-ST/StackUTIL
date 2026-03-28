using DebugInterceptor.Models;
using DebugInterceptor.Services;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackUTIL.Models.Enums;

namespace StackUTIL.Services
{
    /// <summary>
    /// 🔹 Расширения для регистрации сервисов DebugInterceptor
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDebugInterceptorServices(
    this IServiceCollection services,
    IConfiguration configuration)
        {
            // ⚙️ Настройки
            services.Configure<DebugInterceptorSettings>(
                configuration.GetSection("DebugInterceptor"));

            // 📦 Базовые сервисы
            services.AddSingleton<ScreenCaptureService>();
            services.AddSingleton<OcrService>();
            services.AddSingleton<DebugDataParser>();
            services.AddSingleton<BitmapUtility>();

            // 🔔 Реализации уведомлений (важно: регистрируем ДО интерфейса!)
            services.AddSingleton<NotificationService>();
            services.AddSingleton<LoggingNotificationService>();

            // 🔔 Интерфейс (фабрика выбора по настройке)
            services.AddSingleton<INotificationService>(CreateNotificationService);

            // 🔍 Сервисы с зависимостями от настроек
            services.AddSingleton<RegionDetector>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RegionDetector>>();
                var settings = sp.GetRequiredService<IOptions<DebugInterceptorSettings>>();
                return new RegionDetector(logger, settings);
            });

            services.AddSingleton<TooltipValidator>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<TooltipValidator>>();
                var settings = sp.GetRequiredService<IOptions<DebugInterceptorSettings>>();
                return new TooltipValidator(logger, settings);
            });


            services.AddSingleton<DebugResultProcessor>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<DebugResultProcessor>>();
                var ocr = sp.GetRequiredService<OcrService>();
                var parser = sp.GetRequiredService<DebugDataParser>();
                var bitmapUtil = sp.GetRequiredService<BitmapUtility>();
                var settings = sp.GetRequiredService<IOptions<DebugInterceptorSettings>>();
                return new DebugResultProcessor(logger, ocr, parser, sp, bitmapUtil, settings);
            });

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
                    sp.GetRequiredService<IOptions<DebugInterceptorSettings>>()
                );
            });

            // 🪟 UI
            services.AddTransient<DebugResultWindow>();
            services.AddTransient<DebugResultViewModel>();
            services.AddTransient<MainWindow>();

            return services;
        }

        /// <summary>
        /// 🔹 Фабрика создания INotificationService по настройке
        /// </summary>
        private static INotificationService CreateNotificationService(IServiceProvider sp)
        {
            var settings = sp.GetRequiredService<IOptions<DebugInterceptorSettings>>().Value;
            var logger = sp.GetRequiredService<ILogger<INotificationService>>();

            return settings.NotificationMode switch
            {
                NotificationMode.LogOnly => sp.GetRequiredService<LoggingNotificationService>(),

                NotificationMode.Both => new CompositeNotificationService(
                    sp.GetRequiredService<NotificationService>(),
                    sp.GetRequiredService<LoggingNotificationService>()),

                NotificationMode.MessageBox => sp.GetRequiredService<NotificationService>(),

                _ => sp.GetRequiredService<NotificationService>()
            };
        }
    }
}