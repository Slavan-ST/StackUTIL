// Services/DebugInterceptService.cs
using DebugInterceptor.Models;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// Фоновый сервис для перехвата отладочной информации по горячей клавише.
    /// Выполняет захват экрана, OCR-распознавание и отображение результатов.
    /// </summary>
    public class DebugInterceptService : BackgroundService
    {
        private readonly ILogger<DebugInterceptService> _logger;
        private readonly ScreenCaptureService _captureService;
        private readonly OcrService _ocrService;
        private readonly DebugDataParser _parser;
        private readonly IServiceProvider _serviceProvider;

        private HotkeyService? _hotkeyService;
        private int _hotkeyId;
        private CancellationToken _stoppingToken;

        // Ключевые слова для поиска целевого окна, если оно не является активным
        private static readonly string[] DebugWindowKeywords = {
            "Договор", "Организации", "Категории", "Классификаторы", "Структура"
        };

        public DebugInterceptService(
            ILogger<DebugInterceptService> logger,
            ScreenCaptureService captureService,
            OcrService ocrService,
            DebugDataParser parser,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _captureService = captureService;
            _ocrService = ocrService;
            _parser = parser;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Инициализирует горячие клавиши после создания главного окна.
        /// </summary>
        /// <param name="mainWindow">Главное окно приложения для привязки хэндла.</param>
        public void InitializeHotkeys(System.Windows.Window mainWindow)
        {
            if (_hotkeyService != null) return;

            _hotkeyService = new HotkeyService(mainWindow);
            _hotkeyId = _hotkeyService.RegisterCombo(
                alt: true, shift: true, ctrl: false, win: false,
                virtualKey: 0x7A, // VK_F11
                callback: () => OnDebugHotkeyPressed(_stoppingToken));

            _logger.LogInformation("✅ Горячая клавиша Alt+Shift+F11 зарегистрирована");
        }

        /// <inheritdoc />
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            // Сервис работает по событию, фоновая задача не требуется
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _hotkeyService?.Unregister(_hotkeyId);
            _hotkeyService?.Dispose();
            _logger.LogInformation("🛑 Сервис перехвата остановлен");
            await base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Обработчик нажатия горячей клавиши перехвата.
        /// </summary>
        private void OnDebugHotkeyPressed(CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogDebug("🔔 Активирован перехват отладки");
                    await Task.Delay(200, token); // Ожидание появления целевого окна

                    // 1. Захват скриншота (цепочка стратегий)
                    var screenshot = CaptureSmartScreenshot();
                    if (screenshot == null)
                    {
                        _logger.LogWarning("⚠ Не удалось сделать скриншот");
                        return;
                    }

                    // 2. OCR и парсинг
                    var rawText = _ocrService.Recognize(screenshot);
                    var records = _parser.Parse(rawText);
                    _logger.LogDebug("📋 Найдено записей: {Count}", records.Count);

                    // 3. Обработка результатов
                    if (records.Any())
                    {
                        foreach (var record in records)
                            record.GeneratedQuery = _parser.GenerateSelectQuery(record);

                        await ShowResultWindowAsync(records);
                        _logger.LogInformation("✅ Окно результатов показано");
                    }
                    else
                    {
                        _logger.LogWarning("⚠ Записи не найдены, показан исходный текст OCR");
                        await ShowOcrDebugInfoAsync(rawText);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("⚠ Операция отменена");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка при обработке перехвата");
                    await ShowErrorAsync(ex);
                }
            }, token);
        }

        /// <summary>
        /// Умный захват скриншота: активное окно → поиск по тексту → весь экран.
        /// </summary>
        private System.Drawing.Bitmap? CaptureSmartScreenshot()
        {
            // Попытка 1: Активное окно
            var screenshot = _captureService.CaptureLastActiveWindow();
            if (screenshot != null) return screenshot;

            // Попытка 2: Поиск по ключевым словам в заголовках
            foreach (var keyword in DebugWindowKeywords)
            {
                screenshot = _captureService.CaptureWindowByText(keyword, foregroundOnly: false);
                if (screenshot != null)
                {
                    _logger.LogDebug("✅ Найдено окно по ключу: {Keyword}", keyword);
                    return screenshot;
                }
            }

            // Fallback: Весь экран
            _logger.LogDebug("⚠ Фолбэк: захват всего экрана");
            return _captureService.CaptureFullScreen();
        }

        /// <summary>
        /// Показывает окно с результатами парсинга в UI-потоке.
        /// </summary>
        private async Task ShowResultWindowAsync(List<DebugRecord> records)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = _serviceProvider.GetRequiredService<DebugResultWindow>();
                var viewModel = _serviceProvider.GetRequiredService<DebugResultViewModel>();

                viewModel.LoadRecords(records);
                window.DataContext = viewModel;
                window.Show();
                window.Activate();
                window.Topmost = true;
            });
        }

        /// <summary>
        /// Показывает отладочную информацию с распознанным текстом, если парсинг не удался.
        /// </summary>
        private async Task ShowOcrDebugInfoAsync(string? rawText)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var preview = rawText?.Substring(0, Math.Min(400, rawText?.Length ?? 0));
                System.Windows.MessageBox.Show(
                    $"OCR распознал:\n---\n{preview}\n---\n\n" +
                    $"Не найдено записей вида '12345: table'.\n\n" +
                    $"Проверьте:\n• Язык распознавания (rus)\n• Контрастность текста",
                    "Результат OCR",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
        }

        /// <summary>
        /// Показывает сообщение об ошибке пользователю.
        /// </summary>
        private async Task ShowErrorAsync(Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Ошибка перехвата:\n{ex.Message}",
                    "Ошибка",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// Проверяет, содержит ли текст паттерн отладочного окна ("число: текст").
        /// </summary>
        private static bool LooksLikeDebugWindow(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return Regex.IsMatch(text, @"-?\d+\s*:\s*\w+");
        }
    }

    // ═══════════════════════════════════════════════════════
    // 🔧 Вынесенные нативные методы (рекомендация)
    // ═══════════════════════════════════════════════════════
    // Эти методы можно вынести в отдельный файл NativeMethods.cs,
    // чтобы не загромождать основной сервис.

    internal static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    }
}