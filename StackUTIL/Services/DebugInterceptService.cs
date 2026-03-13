// Services/DebugInterceptService.cs
using DebugInterceptor.Models;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;

namespace DebugInterceptor.Services
{
    public class DebugInterceptService : Microsoft.Extensions.Hosting.BackgroundService
    {
        // ═══════════════════════════════════════════════════════
        // WinAPI импорты
        // ═══════════════════════════════════════════════════════

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(System.IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(System.IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, System.IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(System.IntPtr hWnd);

        private delegate bool EnumWindowsProc(System.IntPtr hWnd, System.IntPtr lParam);

        // ═══════════════════════════════════════════════════════
        // Поля и зависимости
        // ═══════════════════════════════════════════════════════

        private readonly ILogger<DebugInterceptService> _logger;
        private readonly ScreenCaptureService _captureService;
        private readonly OcrService _ocrService;
        private readonly DebugDataParser _parser;
        private readonly System.IServiceProvider _serviceProvider;

        private HotkeyService? _hotkeyService;
        private int _hotkeyId;
        private System.Threading.CancellationToken _stoppingToken;

        // Текст для поиска в окне отладки (так как у окна нет заголовка)
        private readonly string[] _debugWindowKeywords = {
            "Договор",
            "Организации",
            "Категории",
            "Классификаторы",
            "Структура"
        };

        public DebugInterceptService(
            ILogger<DebugInterceptService> logger,
            ScreenCaptureService captureService,
            OcrService ocrService,
            DebugDataParser parser,
            System.IServiceProvider serviceProvider)
        {
            _logger = logger;
            _captureService = captureService;
            _ocrService = ocrService;
            _parser = parser;
            _serviceProvider = serviceProvider;
        }

        // ═══════════════════════════════════════════════════════
        // Инициализация горячих клавиш
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Инициализирует горячие клавиши после создания главного окна
        /// </summary>
        public void InitializeHotkeys(System.Windows.Window mainWindow)
        {
            if (_hotkeyService != null) return;

            _hotkeyService = new HotkeyService(mainWindow);

            // Регистрируем Alt+Shift+F11 для перехвата
            _hotkeyId = _hotkeyService.RegisterCombo(
                alt: true, shift: true, ctrl: false, win: false,
                virtualKey: 0x7A, // VK_F11
                callback: () => OnDebugHotkeyPressed(_stoppingToken));

            _logger.LogInformation("✅ Горячая клавиша Alt+Shift+F11 зарегистрирована для перехвата");
        }

        // ═══════════════════════════════════════════════════════
        // BackgroundService интерфейс
        // ═══════════════════════════════════════════════════════

        protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
        {
            // Сохраняем токен для использования в колбэках
            _stoppingToken = stoppingToken;

            // Сервис работает в фоне, основная логика — по событию горячей клавиши
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken)
        {
            _hotkeyService?.Unregister(_hotkeyId);
            _hotkeyService?.Dispose();
            _logger.LogInformation("🛑 Сервис перехвата остановлен");
            await base.StopAsync(cancellationToken);
        }

        // ═══════════════════════════════════════════════════════
        // Обработчик горячей клавиши
        // ═══════════════════════════════════════════════════════

        private void OnDebugHotkeyPressed(System.Threading.CancellationToken token)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    _logger.LogDebug("🔔 Активирован перехват отладки");

                    // ⏱ Ждём появления всплывающего окна целевого приложения
                    await System.Threading.Tasks.Task.Delay(200, token);

                    // 🔍 Захватываем активное окно (тултип без заголовка)
                    // Если не удалось — пробуем найти по ключевым словам
                    System.Drawing.Bitmap? screenshot = null;

                    // Способ 1: Активное окно
                    screenshot = _captureService.CaptureLastActiveWindow();

                    // Способ 2: Поиск по тексту внутри окна (если первое не сработало)
                    if (screenshot == null)
                    {
                        foreach (var keyword in _debugWindowKeywords)
                        {
                            screenshot = _captureService.CaptureWindowByText(keyword, foregroundOnly: false);
                            if (screenshot != null)
                            {
                                _logger.LogDebug("✅ Найдено окно по ключу: {Keyword}", keyword);
                                break;
                            }
                        }
                    }

                    // Fallback: весь экран (если ничего не найдено)
                    screenshot ??= _captureService.CaptureFullScreen();

                    if (screenshot == null)
                    {
                        _logger.LogWarning("⚠ Не удалось сделать скриншот");
                        return;
                    }

                    _logger.LogDebug("📸 Скриншот: {Width}x{Height}", screenshot.Width, screenshot.Height);

                    // 👇 Сохраняем для отладки
                    var debugPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        $"debug_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
                    screenshot.Save(debugPath, System.Drawing.Imaging.ImageFormat.Png);
                    _logger.LogDebug("💾 Скриншот сохранён: {Path}", debugPath);

                    // 2. OCR-распознавание
                    _logger.LogDebug("🔍 Запускаем OCR...");
                    var rawText = _ocrService.Recognize(screenshot);
                    _logger.LogDebug("📝 OCR результат:\n{Text}", rawText?.Trim());

                    // 3. Парсинг записей
                    var records = _parser.Parse(rawText);
                    _logger.LogDebug("📋 Найдено записей: {Count}", records.Count);

                    if (records.Any())
                    {
                        // Генерируем SQL-запросы для каждой записи
                        foreach (var record in records)
                        {
                            record.GeneratedQuery = _parser.GenerateSelectQuery(record);
                        }

                        // Показываем окно результатов в потоке UI
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

                        _logger.LogInformation("✅ Окно результатов показано, записей: {Count}", records.Count);
                    }
                    else
                    {
                        _logger.LogWarning("⚠ Не найдено записей в формате 'число: слово'");

                        // Показываем распознанный текст для отладки
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            System.Windows.MessageBox.Show(
                                $"OCR распознал:\n---\n{rawText?.Substring(0, System.Math.Min(400, rawText?.Length ?? 0))}\n---\n\n" +
                                $"Не найдено записей вида '12345: table'.\n\n" +
                                $"Проверьте:\n• Язык распознавания (rus)\n• Контрастность текста в окне",
                                "Результат OCR",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information);
                        });
                    }
                }
                catch (System.OperationCanceledException)
                {
                    _logger.LogDebug("⚠ Операция отменена");
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка при обработке перехвата");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.MessageBox.Show(
                            $"Ошибка перехвата:\n{ex.Message}",
                            "Ошибка",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    });
                }
                finally
                {
                    // Освобождаем ресурсы скриншота
                    // (using уже отработает, но на всякий случай)
                }
            }, token);
        }

        // ═══════════════════════════════════════════════════════
        // Вспомогательные методы
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Проверяет, похоже ли содержимое на отладочное окно
        /// (ищет паттерн "число : текст")
        /// </summary>
        private static bool LooksLikeDebugWindow(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Регулярка: цифры, возможно с минусом, двоеточие, слово
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"-?\d+\s*:\s*\w+");
        }
    }
}