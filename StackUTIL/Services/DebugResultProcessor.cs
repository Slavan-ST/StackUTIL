using DebugInterceptor.Models;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Обработчик результата: OCR → парсинг → показ окна
    /// </summary>
    public class DebugResultProcessor
    {
        private readonly ILogger<DebugResultProcessor> _logger;
        private readonly OcrService _ocrService;
        private readonly DebugDataParser _parser;
        private readonly IServiceProvider _serviceProvider;
        private readonly BitmapUtility _bitmapUtility;
        private readonly IOptions<DebugInterceptorSettings> _settings;

        public DebugResultProcessor(
            ILogger<DebugResultProcessor> logger,
            OcrService ocrService,
            DebugDataParser parser,
            IServiceProvider serviceProvider,
            BitmapUtility bitmapUtility,
            IOptions<DebugInterceptorSettings> settings)
        {
            _logger = logger;
            _ocrService = ocrService;
            _parser = parser;
            _serviceProvider = serviceProvider;
            _bitmapUtility = bitmapUtility;
            _settings = settings;
        }

        /// <summary>
        /// 🔹 Полный пайплайн обработки региона (только обрезанный Bitmap)
        /// </summary>
        public async Task ProcessRegionAsync(Bitmap croppedRegion)
        {
            // 🔹 Дебаг уже сохранён в CropChangedRegions, здесь только логика

            var rawText = _ocrService.Recognize(croppedRegion);
            _logger.LogDebug("📝 OCR:\n{Text}", rawText?.Trim());

            var records = _parser.Parse(rawText);
            _logger.LogDebug("📋 Записей: {Count}", records.Count);

            if (records.Any())
            {
                foreach (var r in records) r.GeneratedQuery = _parser.GenerateSelectQuery(r);
                await ShowResultsWindow(records);
                _logger.LogInformation("✅ Показано {Count} записей", records.Count);
            }
            else
            {
                ShowOcrFallback(rawText);
            }
        }

        /// <summary>
        /// 🔹 Показ окна с результатами
        /// </summary>
        private async Task ShowResultsWindow(List<DebugRecord> records) =>
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = _serviceProvider.GetRequiredService<DebugResultWindow>();
                var vm = _serviceProvider.GetRequiredService<DebugResultViewModel>();
                vm.LoadRecords(records);
                window.DataContext = vm;

                GetCursorPos(out var cursorPos);

                window.Left = cursorPos.X;
                window.Top = cursorPos.Y - (window.Height / 2);

                var screenWidth = SystemParameters.WorkArea.Width;
                var screenHeight = SystemParameters.WorkArea.Height;
                var windowWidth = window.Width;
                var windowHeight = window.Height;

                if (cursorPos.X + windowWidth > screenWidth)
                    window.Left = Math.Max(0, cursorPos.X - windowWidth);
                if (window.Top < 0) window.Top = 0;
                if (window.Top + windowHeight > screenHeight)
                    window.Top = Math.Max(0, screenHeight - windowHeight);

                window.Show();
                window.Activate();
                window.Topmost = true;
            });

        /// <summary>
        /// 🔹 Фоллбэк при пустом результате парсинга
        /// </summary>
        private void ShowOcrFallback(string? rawText) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                $"Распознано:\n---\n{rawText?[..Math.Min(300, rawText.Length)]}...\n---\n\nНе найдено записей вида '12345 : Таблица'.",
                "Результат", MessageBoxButton.OK, MessageBoxImage.Information));

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
    }
}