using DebugInterceptor.Models;
using DebugInterceptor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Windows;
using System.Windows.Input;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Фоновый сервис перехвата отладочных данных через скриншоты и OCR
    /// </summary>
    /// <remarks>
    /// Сервис работает как <see cref="BackgroundService"/>, регистрирует глобальный хоткей,
    /// делает два скриншота с задержкой, находит изменённые регионы, валидирует их через OCR
    /// и показывает результат в окне <see cref="Views.DebugResultWindow"/>.
    /// </remarks>
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

        /// <summary>
        /// 🔹 Инициализирует новый экземпляр <see cref="DebugInterceptService"/>
        /// </summary>
        /// <param name="logger">Логгер сервиса</param>
        /// <param name="captureService">Сервис захвата скриншотов</param>
        /// <param name="ocrService">Сервис распознавания текста (Tesseract)</param>
        /// <param name="parser">Парсер распознанного текста в структурированные записи</param>
        /// <param name="regionDetector">Детектор изменённых регионов на скриншотах</param>
        /// <param name="bitmapUtility">Утилиты для работы с Bitmap (кроп, отладка)</param>
        /// <param name="tooltipValidator">Валидатор заголовка тултипа через OCR</param>
        /// <param name="resultProcessor">Обработчик результата: OCR → парсинг → показ UI</param>
        /// <param name="serviceProvider">Провайдер сервисов для разрешения зависимостей UI</param>
        /// <param name="notifier">Сервис показа уведомлений пользователю</param>
        /// <param name="settings">Настройки модуля из <see cref="IOptions{DebugInterceptorSettings}"/></param>
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

        /// <summary>
        /// 🔹 Инициализирует глобальные хоткеи для перехвата
        /// </summary>
        /// <param name="mainWindow">Главное окно приложения (владелец хоткеев)</param>
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

        /// <inheritdoc />
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _hotkeyService?.Unregister(_captureHotkeyId);
            _hotkeyService?.Dispose();
            await base.StopAsync(cancellationToken);
        }

        #endregion

        #region Capture Handler

        /// <summary>
        /// 🔹 Обработчик нажатия хоткея: захват → анализ → результат
        /// </summary>
        /// <param name="token">Токен отмены для асинхронных операций</param>
        /// <remarks>
        /// Алгоритм:
        /// <list type="bullet">
        /// <item><description>Делает базовый скриншот</description></item>
        /// <item><description>Ждёт <see cref="DebugInterceptorSettings.CaptureDelayMs"/> мс</description></item>
        /// <item><description>Делает текущий скриншот</description></item>
        /// <item><description>Находит изменённые регионы через <see cref="RegionDetector"/></description></item>
        /// <item><description>Для каждого региона: кроп → валидация заголовка → обработка результата</description></item>
        /// </list>
        /// </remarks>
        private void OnCaptureHotkeyPressed(CancellationToken token) => Task.Run(async () =>
        {
            Bitmap? baseline = null, current = null;
            try
            {
                _logger.LogDebug("🔔 Запуск авто-захвата...");

                baseline = _captureService.CaptureFullScreen();
                if (baseline == null) { _notifier.ShowError("Не удалось сделать базовый скриншот"); return; }

                await Task.Delay(_settings.CaptureDelayMs, token);

                current = _captureService.CaptureFullScreen();
                if (current == null) { _notifier.ShowError("Не удалось сделать текущий скриншот"); return; }

                var regions = _regionDetector.FindChangedRegions(baseline, current, saveDebug: _settings.SaveDebugImages);

                if (regions.Count == 0 && _settings.CaptureDelayMs < 800)
                {
                    _logger.LogDebug("🔄 Повторный захват...");
                    await Task.Delay(150, token);
                    using var retry = _captureService.CaptureFullScreen();
                    if (retry != null)
                        regions = _regionDetector.FindChangedRegions(baseline, retry, saveDebug: _settings.SaveDebugImages);
                }

                if (regions.Count == 0)
                {
                    _notifier.ShowWarning("Изменения не найдены", "Нет данных");
                    return;
                }

                _logger.LogInformation("📦 Найдено регионов: {Count}", regions.Count);

                // 🔹 Обрезаем регионы + сохраняем чистые debug_screen_*.png
                var croppedRegions = _regionDetector.CropChangedRegions(current, regions, saveDebug: _settings.SaveDebugImages);

                foreach (var cropped in croppedRegions)
                {
                    using (cropped)
                    {
                        if (!_tooltipValidator.ContainsTooltipHeader(cropped))
                        {
                            _logger.LogDebug("⚠ Пропущен регион: не найден заголовок");
                            continue;
                        }
                        await _resultProcessor.ProcessRegionAsync(cropped);
                    }
                }
            }
            catch (OperationCanceledException) { _logger.LogDebug("⚠ Отменено"); }
            catch (Exception ex) { _logger.LogError(ex, "❌ Ошибка захвата"); _notifier.ShowError(ex.Message); }
            finally { baseline?.Dispose(); current?.Dispose(); }
        }, token);

        #endregion
    }
}