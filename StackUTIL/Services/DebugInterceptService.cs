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
        private const int PixelDiffThreshold = 5;
        private const int MinRegionArea = 500;
        private const int MinTooltipW = 100, MaxTooltipW = 900;
        private const int MinTooltipH = 80, MaxTooltipH = 700;
        private const int ConnectedComponentMinSize = 15;
        private const int RegionPadding = 0;
        private const int ExpansionMargin = 10; // 🔹 Новое: доп. расширение

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
        // 🔹 Единый обработчик: скрин1 → задержка → скрин2 → анализ
        // ═══════════════════════════════════════════════════════
        private void OnCaptureHotkeyPressed(CancellationToken token) => Task.Run(async () =>
        {
            Bitmap? baseline = null, current = null;
            try
            {
                _logger.LogDebug("🔔 Запуск авто-захвата...");

                // 1️⃣ Скриншот 1 (базовый)
                baseline = _captureService.CaptureFullScreen();
                if (baseline == null) { ShowError("Не удалось сделать базовый скриншот"); return; }
                _logger.LogDebug("📸 Базовый: {W}x{H}", baseline.Width, baseline.Height);

                _logger.LogInformation("⏳ Ожидание {Ms} мс...", CaptureDelayMs);
                await Task.Delay(CaptureDelayMs, token);

                // 2️⃣ Скриншот 2 (текущий)
                current = _captureService.CaptureFullScreen();
                if (current == null) { ShowError("Не удалось сделать текущий скриншот"); return; }
                _logger.LogDebug("📸 Текущий: {W}x{H}", current.Width, current.Height);

                // 3️⃣ Сравнение: находим регионы во 2-м скрине, отличные от 1-го
                var regions = FindChangedRegions(baseline, current);
                if (regions.Count == 0) { ShowNoChangesWarning(); return; }

                _logger.LogInformation("📦 Найдено регионов: {Count}", regions.Count);

                // 4️⃣ Обработка каждого региона: из current → OCR + отладка
                foreach (var region in regions)
                {
                    _logger.LogInformation("📐 Регион: {X},{Y} {W}x{H}",
                        region.X, region.Y, region.Width, region.Height);

                    // 🔹 Вырезаем область из ТЕКУЩЕГО скрина (не baseline!)
                    using var cropped = CropBitmap(current, region);

                    // 🔹 Сохраняем отладочное изображение с обводкой региона
                    SaveDebugWithRegion(current, region, "region_debug");

                    // 🔹 Передаём в OCR
                    if (!ContainsTooltipHeader(cropped))
                        _logger.LogWarning("⚠ Заголовок 'Структура записи' не найден, продолжаем...");

                    await ProcessAndShowResult(cropped);
                }
            }
            catch (OperationCanceledException) { _logger.LogDebug("⚠ Отменено"); }
            catch (Exception ex) { _logger.LogError(ex, "❌ Ошибка захвата"); ShowError(ex.Message); }
            finally { baseline?.Dispose(); current?.Dispose(); }
        }, token);

        // ═══════════════════════════════════════════════════════
        // 🔹 Поиск изменённых регионов: сравниваем два скрина
        // ═══════════════════════════════════════════════════════
        private List<Rectangle> FindChangedRegions(Bitmap baseline, Bitmap current)
        {
            if (baseline.Width != current.Width || baseline.Height != current.Height)
                return new List<Rectangle>();

            // 1️⃣ Находим координаты всех изменённых пикселей
            var changedCoords = DetectChangedPixels(baseline, current);
            _logger.LogDebug("🔍 Найдено {Count} изменённых пикселей", changedCoords.Count);

            if (changedCoords.Count < MinRegionArea)
                return new List<Rectangle>();

            // 2️⃣ Группируем в связанные компоненты (регионы)
            var regions = FindConnectedComponents(changedCoords, current.Width, current.Height);
            _logger.LogDebug("📦 Найдено регионов до фильтрации: {Count}", regions.Count);

            // 3️⃣ Фильтруем по размеру
            var validRegions = regions.Where(r =>
            {
                int area = r.Width * r.Height;
                return area >= MinRegionArea &&
                       r.Width >= MinTooltipW && r.Width <= MaxTooltipW &&
                       r.Height >= MinTooltipH && r.Height <= MaxTooltipH;
            }).ToList();

            if (validRegions.Count == 0)
            {
                _logger.LogDebug("⚠ Нет регионов в строгих границах, расширяем поиск");
                validRegions = regions.Where(r => r.Width * r.Height >= MinRegionArea).ToList();
            }

            // 4️⃣  Расширяем каждый регион + padding
            var expandedRegions = validRegions.Select(r =>
            {
                // 🔹 Сначала расширяем на основе анализа границ
                var expanded = ExpandRegionToContent(r, current, baseline);

                // 🔹 Затем добавляем фиксированный padding
                var pad = RegionPadding;
                int x = Math.Max(0, expanded.X - pad);
                int y = Math.Max(0, expanded.Y - pad);
                int right = Math.Min(current.Width, expanded.Right + pad);
                int bottom = Math.Min(current.Height, expanded.Bottom + pad);

                return new Rectangle(x, y, right - x, bottom - y);
            }).ToList();

            _logger.LogDebug("✅ Регионов после обработки: {Count}", expandedRegions.Count);
            return expandedRegions;
        }

        /// <summary>
        /// 🔹 Расширяет регион до границ контента (пока есть отличия от baseline)
        /// </summary>
        private Rectangle ExpandRegionToContent(Rectangle initial, Bitmap current, Bitmap baseline)
        {
            int left = initial.Left;
            int top = initial.Top;
            int right = initial.Right;
            int bottom = initial.Bottom;

            // 🔹 Расширяем влево
            while (left > 0 && HasSignificantChanges(current, baseline, left - 1, top, left, bottom))
                left--;

            // 🔹 Расширяем вправо
            while (right < current.Width && HasSignificantChanges(current, baseline, right, top, right + 1, bottom))
                right++;

            // 🔹 Расширяем вверх
            while (top > 0 && HasSignificantChanges(current, baseline, left, top - 1, right, top))
                top--;

            // 🔹 Расширяем вниз
            while (bottom < current.Height && HasSignificantChanges(current, baseline, left, bottom, right, bottom + 1))
                bottom++;

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// 🔹 Проверяет, есть ли значимые изменения в указанной полосе
        /// </summary>
        private bool HasSignificantChanges(Bitmap current, Bitmap baseline, int x1, int y1, int x2, int y2)
        {
            int changedPixels = 0;
            int totalPixels = 0;

            for (int y = y1; y < y2; y++)
            {
                for (int x = x1; x < x2; x++)
                {
                    if (x < 0 || y < 0 || x >= current.Width || y >= current.Height) continue;

                    var b = baseline.GetPixel(x, y);
                    var c = current.GetPixel(x, y);

                    int diff = Math.Abs((b.R + b.G + b.B) - (c.R + c.G + c.B)) / 3;
                    totalPixels++;

                    if (diff > PixelDiffThreshold)
                        changedPixels++;
                }
            }

            // Если хотя бы 20% пикселей в полосе изменились — расширяем
            return totalPixels > 0 && (double)changedPixels / totalPixels > 0.2;
        }

        /// <summary>
        /// 🔹 Детекция изменённых пикселей: ищем появление нового контента
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

                    // Детектируем появление нового контента (тултип обычно светлее)
                    if (cBrightness > 50 && Math.Abs(bBrightness - cBrightness) > PixelDiffThreshold)
                        changed.Add((x, y));
                }
            }
            return changed;
        }

        /// <summary>
        /// 🔹 Поиск связанных компонентов (регионов) среди изменённых пикселей
        /// </summary>
        private List<Rectangle> FindConnectedComponents(List<(int x, int y)> pixels, int width, int height)
        {
            var visited = new HashSet<(int, int)>();
            var regions = new List<Rectangle>();
            var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

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

                    foreach (var (dx, dy) in directions)
                    {
                        var (nx, ny) = (x + dx, y + dy);
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

                if (count >= ConnectedComponentMinSize)
                    regions.Add(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
            return regions;
        }

        /// <summary>
        /// 🔹 Сохраняет скриншот с обводкой региона для отладки
        /// </summary>
        private void SaveDebugWithRegion(Bitmap source, Rectangle region, string suffix)
        {
            try
            {
                var debugDir = Path.Combine(Path.GetTempPath(), "DebugInterceptor");
                Directory.CreateDirectory(debugDir);

                using var debugImg = (Bitmap)source.Clone();
                using var g = Graphics.FromImage(debugImg);

                // 🔴 Красная рамка региона
                using var pen = new Pen(Color.FromArgb(200, Color.Red), 3);
                g.DrawRectangle(pen, region.X, region.Y, region.Width, region.Height);

                // 📝 Подпись с координатами
                var label = $"[{region.X},{region.Y}] {region.Width}x{region.Height}";
                using var font = new Font("Segoe UI", 12, System.Drawing.FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.Yellow);
                using var bgBrush = new SolidBrush(Color.FromArgb(180, Color.Black));

                var textSize = g.MeasureString(label, font);
                g.FillRectangle(bgBrush, region.X, region.Y - 25, textSize.Width + 8, 22);
                g.DrawString(label, font, textBrush, region.X + 4, region.Y - 23);

                var path = Path.Combine(debugDir, $"debug_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                debugImg.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                _logger.LogDebug("💾 Отладка региона: {Path}", path);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка сохранения отладки"); }
        }

        /// <summary>
        /// 🔹 Вырезает область из битмапа
        /// </summary>
        private Bitmap CropBitmap(Bitmap source, Rectangle area)
        {
            var cropped = new Bitmap(area.Width, area.Height);
            using var g = Graphics.FromImage(cropped);
            g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);
            return cropped;
        }

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
    // 🔹 Вспомогательный метод для Rectangle
    // ═══════════════════════════════════════════════════════
    public static class RectangleExtensions
    {
        public static bool Contains(this Rectangle r, int x, int y) =>
            x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }
}