// Services/DebugInterceptService.cs
using DebugInterceptor.Models;
using DebugInterceptor.ViewModels;
using DebugInterceptor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Tesseract;
using System.IO;

namespace DebugInterceptor.Services
{
    public class DebugInterceptService : BackgroundService
    {
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
        private const int PixelDiffThreshold = 25;
        private const int MinRegionArea = 500;
        private const int MinTooltipW = 100, MaxTooltipW = 900;
        private const int MinTooltipH = 80, MaxTooltipH = 700;
        private const int ConnectedComponentMinSize = 15;
        private const int RegionPadding = 60;

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

        // ═══════════════════════════════════════════════════════
        // 🔹 Единый обработчик: базовый → задержка → текущий → анализ
        // ═══════════════════════════════════════════════════════
        private void OnCaptureHotkeyPressed(CancellationToken token) => Task.Run(async () =>
        {
            Bitmap? baseline = null, current = null, result = null;
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

                var diffRect = FindChangedRegion(baseline, current);
                if (diffRect == null) { ShowNoChangesWarning(); return; }

                _logger.LogInformation("📐 Регион: {X},{Y} {W}x{H}",
                    diffRect.Value.X, diffRect.Value.Y, diffRect.Value.Width, diffRect.Value.Height);

                // 🔹 Извлекаем ТОЛЬКО из current
                result = CropBitmap(current, diffRect.Value);

                if (!ContainsTooltipHeader(result))
                    _logger.LogWarning("⚠ Заголовок 'Структура записи' не найден, продолжаем...");

                await ProcessAndShowResult(result);
            }
            catch (OperationCanceledException) { _logger.LogDebug("⚠ Отменено"); }
            catch (Exception ex) { _logger.LogError(ex, "❌ Ошибка захвата"); ShowError(ex.Message); }
            finally { baseline?.Dispose(); current?.Dispose(); result?.Dispose(); }
        }, token);

        // ═══════════════════════════════════════════════════════
        // 🔹 Diff-логика: baseline → только для детекции, current → только для извлечения
        // ═══════════════════════════════════════════════════════
        private Rectangle? FindChangedRegion(Bitmap baseline, Bitmap current, bool saveDebugImage = true)
        {
            if (baseline.Width != current.Width || baseline.Height != current.Height) return null;

            // 1️⃣ Детекция: сравниваем, чтобы найти КООРДИНАТЫ изменений
            var changedCoords = DetectChangedPixels(baseline, current);

            _logger.LogDebug("🔍 Найдено {Count} изменённых пикселей", changedCoords.Count);
            if (changedCoords.Count < MinRegionArea) return null;

            // 2️⃣ Отладка детекции (с маркерами)
            if (saveDebugImage)
                SaveDetectionDebug(current, changedCoords, "detection_overlay");

            // 3️⃣ Границы всех изменений
            var globalBounds = GetBoundingRectangle(changedCoords, current.Width, current.Height);
            if (globalBounds.IsEmpty) return null;

            // 4️⃣ Поиск связанных регионов
            var regions = FindConnectedComponents(changedCoords, current.Width, current.Height);
            _logger.LogDebug("📦 Найдено регионов: {Count}", regions.Count);

            // 5️⃣ Выбор кандидата
            var candidate = regions.FirstOrDefault(IsValidTooltipRegion);
            if (candidate.IsEmpty)
                candidate = regions.FirstOrDefault(r => r.Width * r.Height >= MinRegionArea);

            // 6️⃣ Финальный прямоугольник
            Rectangle finalRect;
            if (!candidate.IsEmpty)
            {
                var pad = RegionPadding;
                int x = Math.Max(0, candidate.X - pad);
                int y = Math.Max(0, candidate.Y - pad);
                int right = Math.Min(current.Width, candidate.Right + pad);
                int bottom = Math.Min(current.Height, candidate.Bottom + pad * 2);
                finalRect = new Rectangle(x, y, right - x, bottom - y);
            }
            else
            {
                var pad = 20;
                int x = Math.Max(0, globalBounds.X - pad);
                int y = Math.Max(0, globalBounds.Y - pad);
                int right = Math.Min(current.Width, globalBounds.Right + pad);
                int bottom = Math.Min(current.Height, globalBounds.Bottom + pad);
                finalRect = new Rectangle(x, y, right - x, bottom - y);
            }

            // 7️⃣ Сохраняем ЧИСТЫЙ кроп из current (без маркеров) — именно он идёт в OCR
            if (saveDebugImage)
            {
                using var cleanCrop = CropBitmap(current, finalRect);
                SaveCleanCrop(cleanCrop, "region_for_ocr");
            }

            return finalRect;
        }

        /// <summary>
        /// 🔹 Только детекция: возвращает координаты изменённых пикселей
        /// </summary>
        private List<(int x, int y)> DetectChangedPixels(Bitmap baseline, Bitmap current)
        {
            var changed = new List<(int x, int y)>();

            for (int y = 0; y < baseline.Height; y++)
            {
                for (int x = 0; x < baseline.Width; x++)
                {
                    var b = baseline.GetPixel(x, y);
                    var c = current.GetPixel(x, y);

                    int bBrightness = (b.R + b.G + b.B) / 3;
                    int cBrightness = (c.R + c.G + c.B) / 3;

                    // Детектируем только появление нового контента (не исчезновение)
                    if (cBrightness > 50 && Math.Abs(bBrightness - cBrightness) > PixelDiffThreshold)
                        changed.Add((x, y));
                }
            }
            return changed;
        }

        /// <summary>
        /// 🔹 Сохраняет отладочное изображение с маркерами детекции
        /// </summary>
        private void SaveDetectionDebug(Bitmap source, List<(int x, int y)> changed, string suffix)
        {
            try
            {
                var debugDir = Path.Combine(Path.GetTempPath(), "DebugInterceptor");
                Directory.CreateDirectory(debugDir);

                using var debugImg = (Bitmap)source.Clone();
                using var g = Graphics.FromImage(debugImg);
                using var pen = new Pen(Color.FromArgb(100, Color.Lime), 1);

                var byRow = changed.GroupBy(p => p.y).Take(300);
                foreach (var row in byRow)
                {
                    var xs = row.Select(p => p.x).OrderBy(x => x).ToArray();
                    for (int i = 0; i < xs.Length;)
                    {
                        int start = xs[i], end = xs[i];
                        while (i + 1 < xs.Length && xs[i + 1] == end + 1) { end = xs[++i]; }
                        if (end - start > 1) g.DrawLine(pen, start, row.Key, end, row.Key);
                        else g.DrawRectangle(pen, start, row.Key, 1, 1);
                        i++;
                    }
                }
                g.DrawRectangle(Pens.Red, 0, 0, debugImg.Width - 1, debugImg.Height - 1);

                var path = Path.Combine(debugDir, $"debug_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                debugImg.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                _logger.LogDebug("💾 Детекция: {Path}", path);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка сохранения детекции"); }
        }

        /// <summary>
        /// 🔹 Сохраняет ЧИСТЫЙ кроп без маркеров — именно этот контент идёт в OCR
        /// </summary>
        private void SaveCleanCrop(Bitmap cleanImage, string suffix)
        {
            try
            {
                var debugDir = Path.Combine(Path.GetTempPath(), "DebugInterceptor");
                Directory.CreateDirectory(debugDir);

                var path = Path.Combine(debugDir, $"clean_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                cleanImage.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                _logger.LogDebug("💾 Чистый регион: {Path} ({W}x{H})", path, cleanImage.Width, cleanImage.Height);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка сохранения чистого кропа"); }
        }

        /// <summary>
        /// Вычисляет ограничивающий прямоугольник для списка точек
        /// </summary>
        private Rectangle GetBoundingRectangle(List<(int x, int y)> pixels, int maxWidth, int maxHeight)
        {
            if (pixels.Count == 0) return Rectangle.Empty;
            int minX = pixels.Min(p => p.x), maxX = pixels.Max(p => p.x);
            int minY = pixels.Min(p => p.y), maxY = pixels.Max(p => p.y);
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private bool IsValidTooltipRegion(Rectangle r) =>
            r.Width * r.Height >= MinRegionArea &&
            r.Width is >= MinTooltipW and <= MaxTooltipW &&
            r.Height is >= MinTooltipH and <= MaxTooltipH;

        private List<Rectangle> FindConnectedComponents(List<(int x, int y)> pixels, int width, int height)
        {
            var visited = new HashSet<(int, int)>();
            var regions = new List<Rectangle>();
            var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

            foreach (var start in pixels)
            {
                if (visited.Contains(start)) continue;
                var queue = new Queue<(int, int)>(); queue.Enqueue(start); visited.Add(start);
                int minX = start.x, maxX = start.x, minY = start.y, maxY = start.y, count = 1;

                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();
                    foreach (var (dx, dy) in directions)
                    {
                        var (nx, ny) = (x + dx, y + dy);
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var key = (nx, ny);
                        if (!visited.Contains(key) && pixels.Contains(key))
                        {
                            visited.Add(key); queue.Enqueue(key);
                            minX = Math.Min(minX, nx); maxX = Math.Max(maxX, nx);
                            minY = Math.Min(minY, ny); maxY = Math.Max(maxY, ny);
                            count++;
                        }
                    }
                }
                if (count >= ConnectedComponentMinSize)
                    regions.Add(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
            return regions;
        }

        private Bitmap CropBitmap(Bitmap source, Rectangle area)
        {
            var cropped = new Bitmap(area.Width, area.Height);
            using var g = Graphics.FromImage(cropped);
            g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);
            return cropped;
        }

        // ═══════════════════════════════════════════════════════
        // 🔹 OCR-валидация
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

        private async Task ShowResultsWindow(List<DebugRecord> records) =>
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = _serviceProvider.GetRequiredService<DebugResultWindow>();
                var vm = _serviceProvider.GetRequiredService<DebugResultViewModel>();
                vm.LoadRecords(records);
                window.DataContext = vm;
                window.Show(); window.Activate(); window.Topmost = true;
            });

        private void ShowOcrFallback(string? rawText) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show($"Распознано:\n---\n{rawText?[..Math.Min(300, rawText.Length)]}...\n---\n\nНе найдено записей вида '12345 : Таблица'.",
                "Результат", MessageBoxButton.OK, MessageBoxImage.Information));

        private void ShowNoChangesWarning() => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show("Не найдено изменений, похожих на тултип.\n\n" +
                "Убедитесь, что тултип открылся после звукового сигнала.",
                "Нет изменений", MessageBoxButton.OK, MessageBoxImage.Warning));

        private void ShowError(string message) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show($"Ошибка:\n{message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));

        private static bool LooksLikeDebugWindow(string? text) =>
            !string.IsNullOrEmpty(text) && Regex.IsMatch(text, @"-?\d+\s*:\s*\w+");
    }

    // ═══════════════════════════════════════════════════════
    // 🔹 Вспомогательный метод для Rectangle.Contains
    // ═══════════════════════════════════════════════════════
    public static class RectangleExtensions
    {
        public static bool Contains(this Rectangle r, int x, int y) =>
            x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }
}