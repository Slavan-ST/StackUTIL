using DebugInterceptor.Models;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Tesseract;

namespace DebugInterceptor.Services
{
    public class DebugInterceptService : BackgroundService
    {
        #region Fields & Settings

        private readonly ILogger<DebugInterceptService> _logger;
        private readonly ScreenCaptureService _captureService;
        private readonly OcrService _ocrService;
        private readonly DebugDataParser _parser;
        private readonly IServiceProvider _serviceProvider;

        private HotkeyService? _hotkeyService;
        private int _captureHotkeyId;
        private CancellationToken _stoppingToken;

        // ═══════════════════════════════════════════════════════
        // Настройки
        // ═══════════════════════════════════════════════════════
        private const Keys VK_F12 = Keys.F12;
        private const int CaptureDelayMs = 500;
        private const int PixelDiffThreshold = 5;
        private const int MinRegionArea = 500;
        private const int MinTooltipW = 100, MaxTooltipW = 900;
        private const int MinTooltipH = 80, MaxTooltipH = 700;
        private const int ConnectedComponentMinSize = 15;
        private const int RegionPadding = 0;
        private const int ExpansionMargin = 10;
        private readonly RegionDetector _regionDetector;
        private readonly BitmapUtility _bitmapUtility;  

        #endregion

        #region Constructor & Lifecycle

        public DebugInterceptService(
            ILogger<DebugInterceptService> logger,
            ScreenCaptureService captureService,
            OcrService ocrService,
            DebugDataParser parser,
            RegionDetector regionDetector,
            BitmapUtility bitmapUtility,  // ← новый параметр
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _captureService = captureService;
            _ocrService = ocrService;
            _parser = parser;
            _serviceProvider = serviceProvider;
            _bitmapUtility = bitmapUtility;  // ← инициализация
            _regionDetector = regionDetector;  
        }

        public void InitializeHotkeys(Window mainWindow)
        {
            if (_hotkeyService != null) return;
            _hotkeyService = new HotkeyService(mainWindow);

            _captureHotkeyId = _hotkeyService.RegisterCombo(
                alt: true, shift: true, ctrl: false, win: false,
                virtualKey: VK_F12,
                callback: () => OnCaptureHotkeyPressed(_stoppingToken));

            _logger.LogInformation("✅ Хоткей: Shift+Alt+F12 (pass-through, авто-захват)");
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _hotkeyService?.Unregister(_captureHotkeyId);
            _hotkeyService?.Dispose();
            await base.StopAsync(cancellationToken);
        }

        #endregion

        #region Capture Handler

        // ═══════════════════════════════════════════════════════
        // 🔹 Единый обработчик: скрин1 → задержка → скрин2 → анализ
        // ═══════════════════════════════════════════════════════
        private void OnCaptureHotkeyPressed(CancellationToken token) => Task.Run(async () =>
        {
            Bitmap? baseline = null, current = null;
            try
            {
                _logger.LogDebug("🔔 Запуск авто-захвата...");

                baseline = _captureService.CaptureFullScreen();
                if (baseline == null) { ShowError("Не удалось сделать базовый скриншот"); return; }
                _logger.LogDebug("📸 Базовый: {W}x{H}", baseline.Width, baseline.Height);

                _logger.LogInformation("⏳ Ожидание {Ms} мс...", CaptureDelayMs);
                await Task.Delay(CaptureDelayMs, token);

                current = _captureService.CaptureFullScreen();
                if (current == null) { ShowError("Не удалось сделать текущий скриншот"); return; }
                _logger.LogDebug("📸 Текущий: {W}x{H}", current.Width, current.Height);

                var regions = _regionDetector.FindChangedRegions(baseline, current);
                if (regions.Count == 0) { ShowNoChangesWarning(); return; }

                _logger.LogInformation("📦 Найдено регионов: {Count}", regions.Count);

                foreach (var region in regions)
                {
                    _logger.LogInformation("📐 Регион: {X},{Y} {W}x{H}",
                        region.X, region.Y, region.Width, region.Height);

                    using var cropped = _bitmapUtility.CropBitmap(current, region);
                    _bitmapUtility.SaveDebugWithRegion(current, region, "region_debug");

                    if (!ContainsTooltipHeader(cropped))
                        _logger.LogWarning("⚠ Заголовок 'Структура записи' не найден, продолжаем...");

                    await ProcessAndShowResult(cropped);
                }
            }
            catch (OperationCanceledException) { _logger.LogDebug("⚠ Отменено"); }
            catch (Exception ex) { _logger.LogError(ex, "❌ Ошибка захвата"); ShowError(ex.Message); }
            finally { baseline?.Dispose(); current?.Dispose(); }
        }, token);

        #endregion


        #region OCR & Processing

        // ═══════════════════════════════════════════════════════
        // 🔹 OCR-валидация заголовка
        // ═══════════════════════════════════════════════════════
        private bool ContainsTooltipHeader(Bitmap bitmap)
        {
            try
            {
                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
                using var engine = new TesseractEngine(tessDataPath, "rus", Tesseract.EngineMode.Default);
                engine.DefaultPageSegMode = Tesseract.PageSegMode.SingleBlock;

                var tempPath = Path.Combine(Path.GetTempPath(), $"chk_{Guid.NewGuid():N}.png");
                try
                {
                    bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                    using var pix = Tesseract.Pix.LoadFromFile(tempPath);
                    using var page = engine.Process(pix);
                    var text = page.GetText()?.ToLower() ?? "";
                    _logger.LogDebug("🔍 Заголовок: '{Text}'", text[..Math.Min(100, text.Length)]);
                    return text.Contains("структура") && text.Contains("запис");
                }
                finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка проверки заголовка"); return false; }
        }

        // ═══════════════════════════════════════════════════════
        // 🔹 Обработка результата
        // ═══════════════════════════════════════════════════════
        private async Task ProcessAndShowResult(Bitmap region)
        {
            var debugPath = Path.Combine(Path.GetTempPath(), $"diff_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            region.Save(debugPath, System.Drawing.Imaging.ImageFormat.Png);
            _logger.LogDebug("💾 Diff: {Path}", debugPath);

            var rawText = _ocrService.Recognize(region);
            _logger.LogDebug("📝 OCR:\n{Text}", rawText?.Trim());

            var records = _parser.Parse(rawText);
            _logger.LogDebug("📋 Записей: {Count}", records.Count);

            if (records.Any())
            {
                foreach (var r in records) r.GeneratedQuery = _parser.GenerateSelectQuery(r);
                await ShowResultsWindow(records);
                _logger.LogInformation("✅ Показано {Count} записей", records.Count);
            }
            else ShowOcrFallback(rawText);
        }

        #endregion

        #region UI & Messages

        // ═══════════════════════════════════════════════════════
        // 🔹 Показ окна с результатами (середина левой стороны на курсоре)
        // ═══════════════════════════════════════════════════════
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

                var screenWidth = System.Windows.SystemParameters.WorkArea.Width;
                var screenHeight = System.Windows.SystemParameters.WorkArea.Height;
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

        private void ShowOcrFallback(string? rawText) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                $"Распознано:\n---\n{rawText?[..Math.Min(300, rawText.Length)]}...\n---\n\nНе найдено записей вида '12345 : Таблица'.",
                "Результат", MessageBoxButton.OK, MessageBoxImage.Information));

        private void ShowNoChangesWarning() => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                "Не найдено изменений, похожих на тултип.\n\nУбедитесь, что тултип открылся после звукового сигнала.",
                "Нет изменений", MessageBoxButton.OK, MessageBoxImage.Warning));

        private void ShowError(string message) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show($"Ошибка:\n{message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));

        private static bool LooksLikeDebugWindow(string? text) =>
            !string.IsNullOrEmpty(text) && Regex.IsMatch(text, @"-?\d+\s*:\s*\w+");

        #endregion

        #region WinAPI

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        #endregion
    }
}