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
using Tesseract;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace DebugInterceptor.Services
{
    public class DebugInterceptService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly ILogger<DebugInterceptService> _logger;
        private readonly ScreenCaptureService _captureService;
        private readonly OcrService _ocrService;
        private readonly DebugDataParser _parser;
        private readonly IServiceProvider _serviceProvider;

        private HotkeyService? _hotkeyService;
        private int _baselineHotkeyId;
        private int _captureHotkeyId;
        private CancellationToken _stoppingToken;

        private Bitmap? _baselineScreenshot;
        private readonly object _baselineLock = new();

        // 🔧 УМЕНЬШЕН порог для лучшего обнаружения серого фона
        private const int PixelDiffThreshold = 25;
        private const int MinRegionArea = 500;
        // 🔧 РАСШИРЕНЫ диапазоны размеров
        private const int MinTooltipW = 100, MaxTooltipW = 900;
        private const int MinTooltipH = 80, MaxTooltipH = 700;

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

        public void InitializeHotkeys(System.Windows.Window mainWindow)
        {
            if (_hotkeyService != null) return;

            _hotkeyService = new HotkeyService(mainWindow);

            _baselineHotkeyId = _hotkeyService.RegisterCombo(
                alt: true, shift: true, ctrl: false, win: false,
                virtualKey: 0x00,
                callback: () => OnBaselineHotkeyPressed());

            _captureHotkeyId = _hotkeyService.RegisterCombo(
                alt: true, shift: true, ctrl: false, win: false,
                virtualKey: 0x7A,
                callback: () => OnDebugHotkeyPressed(_stoppingToken));

            _logger.LogInformation("✅ Горячие клавиши: Shift+Alt (база), Shift+Alt+F11 (захват)");
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _hotkeyService?.Unregister(_baselineHotkeyId);
            _hotkeyService?.Unregister(_captureHotkeyId);
            _hotkeyService?.Dispose();
            lock (_baselineLock) { _baselineScreenshot?.Dispose(); _baselineScreenshot = null; }
            await base.StopAsync(cancellationToken);
        }

        private void OnBaselineHotkeyPressed()
        {
            Task.Run(() =>
            {
                try
                {
                    var baseline = _captureService.CaptureFullScreen();
                    if (baseline == null)
                    {
                        _logger.LogWarning("⚠ Не удалось сделать базовый скриншот");
                        return;
                    }

                    lock (_baselineLock)
                    {
                        _baselineScreenshot?.Dispose();
                        _baselineScreenshot = baseline;
                    }

                    _logger.LogInformation("✅ Базовый скриншот: {W}x{H}", baseline.Width, baseline.Height);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка сохранения базового скриншота");
                }
            }, _stoppingToken);
        }

        private Rectangle? FindChangedRegion(Bitmap baseline, Bitmap current)
        {
            if (baseline.Width != current.Width || baseline.Height != current.Height)
                return null;

            var changed = new List<(int x, int y)>();
            for (int y = 0; y < baseline.Height; y++)
            {
                for (int x = 0; x < baseline.Width; x++)
                {
                    var b = baseline.GetPixel(x, y);
                    var c = current.GetPixel(x, y);
                    int diff = Math.Abs((b.R + b.G + b.B) - (c.R + c.G + c.B)) / 3;
                    if (diff > PixelDiffThreshold)
                        changed.Add((x, y));
                }
            }

            _logger.LogDebug("🔍 Найдено {Count} изменённых пикселей", changed.Count);

            if (changed.Count < MinRegionArea)
                return null;

            var regions = FindConnectedComponents(changed, baseline.Width, baseline.Height);
            _logger.LogDebug("📦 Найдено {Count} регионов", regions.Count);

            // 🔧 ФИЛЬТРУЕМ по размеру, но более мягко
            var candidates = regions.Where(r =>
            {
                int area = r.Width * r.Height;
                return area >= MinRegionArea &&
                       r.Width >= MinTooltipW && r.Width <= MaxTooltipW &&
                       r.Height >= MinTooltipH && r.Height <= MaxTooltipH;
            }).ToList();

            if (candidates.Count == 0)
            {
                // 🔧 Если нет кандидатов — берём ЛЮБОЙ регион подходящего размера
                candidates = regions.Where(r => r.Width * r.Height >= MinRegionArea).ToList();
                _logger.LogDebug("⚠ Точных кандидатов нет, используем {Count} регионов без строгой фильтрации", candidates.Count);
            }

            if (candidates.Count == 0)
                return null;

            var best = candidates.OrderByDescending(r =>
            {
                int count = 0;
                foreach (var p in changed)
                    if (r.Contains(p.x, p.y)) count++;
                return (double)count / (r.Width * r.Height);
            }).First();

            // 🔧 УВЕЛИЧЕН запас для захвата всего тултипа
            int pad = 60;
            return new Rectangle(
                Math.Max(0, best.X - pad),
                Math.Max(0, best.Y - pad),
                Math.Min(baseline.Width, best.Width + pad),
                Math.Min(baseline.Height, best.Height + pad * 2));
        }

        private List<Rectangle> FindConnectedComponents(List<(int x, int y)> pixels, int width, int height)
        {
            var visited = new HashSet<(int, int)>();
            var regions = new List<Rectangle>();

            foreach (var start in pixels)
            {
                if (visited.Contains(start)) continue;

                var queue = new Queue<(int, int)>();
                queue.Enqueue(start);
                visited.Add(start);

                int minX = start.x, maxX = start.x;
                int minY = start.y, maxY = start.y;
                int count = 1;

                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();

                    foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    {
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var key = (nx, ny);
                        if (!visited.Contains(key) && pixels.Contains(key))
                        {
                            visited.Add(key);
                            queue.Enqueue(key);
                            minX = Math.Min(minX, nx); maxX = Math.Max(maxX, nx);
                            minY = Math.Min(minY, ny); maxY = Math.Max(maxY, ny);
                            count++;
                        }
                    }
                }

                if (count >= 15)
                    regions.Add(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }

            return regions;
        }

        private Bitmap CropBitmap(Bitmap source, Rectangle area)
        {
            var cropped = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(source,
                    new Rectangle(0, 0, area.Width, area.Height),
                    area,
                    GraphicsUnit.Pixel);
            }
            return cropped;
        }

        /// <summary>
        /// 🔧 ИСПРАВЛЕНО: используем PageSegMode.SingleBlock вместо SingleLine
        /// </summary>
        private bool ContainsTooltipHeader(Bitmap bitmap)
        {
            try
            {
                var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
                using var engine = new TesseractEngine(tessDataPath, "rus", EngineMode.Default);
                // 🔧 SingleBlock лучше для поиска заголовка
                engine.DefaultPageSegMode = PageSegMode.SingleBlock;

                var tempPath = Path.Combine(Path.GetTempPath(), $"chk_{Guid.NewGuid():N}.png");
                try
                {
                    bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                    using var pix = Pix.LoadFromFile(tempPath);
                    using var page = engine.Process(pix);
                    var text = page.GetText()?.ToLower() ?? "";

                    _logger.LogDebug("🔍 Проверка заголовка: '{Text}'", text.Substring(0, Math.Min(100, text.Length)));

                    return text.Contains("структура") && text.Contains("запис");
                }
                finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠ Ошибка проверки заголовка");
                return false;
            }
        }

        private void OnDebugHotkeyPressed(CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                Bitmap? baseline = null;
                Bitmap? current = null;
                Bitmap? result = null;

                try
                {
                    _logger.LogDebug("🔔 Запрос захвата тултипа");

                    lock (_baselineLock)
                    {
                        if (_baselineScreenshot == null)
                        {
                            _logger.LogWarning("⚠ Базовый скриншот не сохранён!");
                            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show(
                                    "Сначала нажмите 🔹 Shift+Alt для сохранения базового скриншота,\n" +
                                    "затем откройте тултип и нажмите 🔹 Shift+Alt+F11",
                                    "StackUTIL", MessageBoxButton.OK, MessageBoxImage.Information);
                            });
                            return;
                        }
                        baseline = new Bitmap(_baselineScreenshot);
                    }

                    current = _captureService.CaptureFullScreen();
                    if (current == null)
                    {
                        _logger.LogWarning("⚠ Не удалось сделать текущий скриншот");
                        return;
                    }

                    var diffRect = FindChangedRegion(baseline, current);
                    if (diffRect == null)
                    {
                        _logger.LogWarning("⚠ Изменения не найдены или не похожи на тултип");
                        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(
                                "Не найдено изменений, похожих на тултип.\n\n" +
                                "Проверьте:\n" +
                                "• Тултип 'Структура записи' открыт и виден целиком\n" +
                                "• Между нажатиями не было других изменений",
                                "Нет изменений", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                        return;
                    }

                    _logger.LogInformation("📐 Регион изменений: {X},{Y} {W}x{H}",
                        diffRect.Value.X, diffRect.Value.Y, diffRect.Value.Width, diffRect.Value.Height);

                    result = CropBitmap(current, diffRect.Value);

                    // 🔧 Теперь это ПРЕДУПРЕЖДЕНИЕ, а не блокировка
                    if (!ContainsTooltipHeader(result))
                    {
                        _logger.LogWarning("⚠ В регионе не найдено 'Структура записи', но продолжаем...");
                        // Не прерываем выполнение!
                    }

                    var debugPath = Path.Combine(Path.GetTempPath(),
                        $"diff_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    result.Save(debugPath, System.Drawing.Imaging.ImageFormat.Png);
                    _logger.LogDebug("💾 Diff-скриншот: {Path}", debugPath);

                    _logger.LogDebug("🔍 OCR...");
                    var rawText = _ocrService.Recognize(result);
                    _logger.LogDebug("📝 Текст:\n{Text}", rawText?.Trim());

                    var records = _parser.Parse(rawText);
                    _logger.LogDebug("📋 Записей: {Count}", records.Count);

                    if (records.Any())
                    {
                        foreach (var record in records)
                            record.GeneratedQuery = _parser.GenerateSelectQuery(record);

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
                        _logger.LogInformation("✅ Показано {Count} записей", records.Count);
                    }
                    else
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(
                                $"Распознано:\n---\n{rawText?.Substring(0, Math.Min(300, rawText?.Length ?? 0))}...\n---\n\n" +
                                "Не найдено записей вида '12345 : Таблица'.",
                                "Результат", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("⚠ Отменено");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка");
                    _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show($"Ошибка:\n{ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    baseline?.Dispose();
                    current?.Dispose();
                    result?.Dispose();
                }
            }, token);
        }

        private static bool LooksLikeDebugWindow(string? text) =>
            !string.IsNullOrEmpty(text) && Regex.IsMatch(text, @"-?\d+\s*:\s*\w+");
    }
}