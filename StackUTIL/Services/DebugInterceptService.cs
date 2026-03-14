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
        private readonly RegionDetector _regionDetector;
        private readonly BitmapUtility _bitmapUtility;
        private readonly TooltipValidator _tooltipValidator;  // ← новое поле
        private readonly DebugResultProcessor _resultProcessor;  // ← новое поле
        private readonly INotificationService _notifier;

        #endregion

        #region Constructor & Lifecycle

        public DebugInterceptService(
            ILogger<DebugInterceptService> logger,
            ScreenCaptureService captureService,
            OcrService ocrService,
            DebugDataParser parser,
            RegionDetector regionDetector,
            BitmapUtility bitmapUtility,  // ← новый параметр
            TooltipValidator tooltipValidator,  // ← новый параметр
            DebugResultProcessor resultProcessor,  // ← новый параметр
            IServiceProvider serviceProvider,
            INotificationService notifier)
        {
            _logger = logger;
            _captureService = captureService;
            _ocrService = ocrService;
            _parser = parser;
            _bitmapUtility = bitmapUtility;  // ← инициализация
            _tooltipValidator = tooltipValidator;  // ← инициализация
            _resultProcessor = resultProcessor;  // ← инициализация
            _regionDetector = regionDetector;
            _serviceProvider = serviceProvider;
            _notifier = notifier;
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
                if (baseline == null) { _notifier.ShowError("Не удалось сделать базовый скриншот"); return; }
                _logger.LogDebug("📸 Базовый: {W}x{H}", baseline.Width, baseline.Height);

                _logger.LogInformation("⏳ Ожидание {Ms} мс...", CaptureDelayMs);
                await Task.Delay(CaptureDelayMs, token);

                current = _captureService.CaptureFullScreen();
                if (current == null) { _notifier.ShowError("Не удалось сделать текущий скриншот"); return; }
                _logger.LogDebug("📸 Текущий: {W}x{H}", current.Width, current.Height);

                var regions = _regionDetector.FindChangedRegions(baseline, current);
                if (regions.Count == 0) { _notifier.ShowWarning("Не найдено изменений, похожих на тултип.\n\nУбедитесь, что тултип открылся после звукового сигнала.", "Нет изменений"); return; }

                _logger.LogInformation("📦 Найдено регионов: {Count}", regions.Count);

                foreach (var region in regions)
                {
                    _logger.LogInformation("📐 Регион: {X},{Y} {W}x{H}",
                        region.X, region.Y, region.Width, region.Height);

                    using var cropped = _bitmapUtility.CropBitmap(current, region);

                    if (!_tooltipValidator.ContainsTooltipHeader(cropped))
                        _logger.LogWarning("⚠ Заголовок 'Структура записи' не найден, продолжаем...");

                    await _resultProcessor.ProcessRegionAsync(cropped, region, current);
                }
            }
            catch (OperationCanceledException) { _logger.LogDebug("⚠ Отменено"); }
            catch (Exception ex) { _logger.LogError(ex, "❌ Ошибка захвата"); _notifier.ShowError(ex.Message); }
            finally { baseline?.Dispose(); current?.Dispose(); }
        }, token);
    }
}