using DebugInterceptor.Models;
using DebugInterceptor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Windows;
using System.Windows.Input;

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
        // Вынесенные сервисы
        // ═══════════════════════════════════════════════════════
        private readonly RegionDetector _regionDetector;
        private readonly BitmapUtility _bitmapUtility;
        private readonly TooltipValidator _tooltipValidator;
        private readonly DebugResultProcessor _resultProcessor;
        private readonly INotificationService _notifier;

        // ═══════════════════════════════════════════════════════
        // Настройки из IOptions<T>
        // ═══════════════════════════════════════════════════════
        private readonly DebugInterceptorSettings _settings;

        #endregion

        #region Constructor & Lifecycle

        public DebugInterceptService(
            ILogger<DebugInterceptService> logger,
            ScreenCaptureService captureService,
            OcrService ocrService,
            DebugDataParser parser,
            RegionDetector regionDetector,
            BitmapUtility bitmapUtility,
            TooltipValidator tooltipValidator,
            DebugResultProcessor resultProcessor,
            IServiceProvider serviceProvider,
            INotificationService notifier,
            IOptions<DebugInterceptorSettings> settings)
        {
            _logger = logger;
            _captureService = captureService;
            _ocrService = ocrService;
            _parser = parser;
            _regionDetector = regionDetector;
            _bitmapUtility = bitmapUtility;
            _tooltipValidator = tooltipValidator;
            _resultProcessor = resultProcessor;
            _serviceProvider = serviceProvider;
            _notifier = notifier;
            _settings = settings.Value;
        }

        public void InitializeHotkeys(Window mainWindow)
        {
            if (_hotkeyService != null) return;
            _hotkeyService = new HotkeyService(mainWindow);

            _captureHotkeyId = _hotkeyService.RegisterCombo(
                alt: _settings.HotkeyAlt,
                shift: _settings.HotkeyShift,
                ctrl: _settings.HotkeyCtrl,
                win: _settings.HotkeyWin,
                virtualKey: (Keys)KeyInterop.VirtualKeyFromKey(_settings.CaptureHotkey),
                callback: () => OnCaptureHotkeyPressed(_stoppingToken));

            _logger.LogInformation("✅ Хоткей: {Combo} (pass-through, авто-захват)",
                $"{(_settings.HotkeyCtrl ? "Ctrl+" : "")}{(_settings.HotkeyAlt ? "Alt+" : "")}{(_settings.HotkeyShift ? "Shift+" : "")}{_settings.CaptureHotkey}");
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

                _logger.LogInformation("⏳ Ожидание {Ms} мс...", _settings.CaptureDelayMs);
                await Task.Delay(_settings.CaptureDelayMs, token);

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