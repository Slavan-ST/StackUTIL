using DebugInterceptor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Детектор изменённых регионов — с фильтрацией выбросов
    /// </summary>
    public class RegionDetector
    {
        private readonly ILogger<RegionDetector> _logger;
        private readonly DebugInterceptorSettings _settings;
        private readonly int _pixelDiffThresholdScaled;
        private readonly string _debugOutputPath;

        private readonly int _boundingBoxLowerPercentile;
        private readonly int _boundingBoxUpperPercentile;
        private readonly int _densePixelNeighborRadius;
        private readonly int _densePixelMinNeighbors;

        public RegionDetector(ILogger<RegionDetector> logger, IOptions<DebugInterceptorSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
            _pixelDiffThresholdScaled = _settings.PixelDiffThreshold * 3;
            _debugOutputPath = _settings.DebugOutputPath;

            // 🔹 Читаем из конфига
            _boundingBoxLowerPercentile = _settings.BoundingBoxLowerPercentile;
            _boundingBoxUpperPercentile = _settings.BoundingBoxUpperPercentile;
            _densePixelNeighborRadius = _settings.DensePixelNeighborRadius;
            _densePixelMinNeighbors = _settings.DensePixelMinNeighbors;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<Rectangle> FindChangedRegions(Bitmap baseline, Bitmap current, bool saveDebug = false)
        {
            if (baseline.Width != current.Width || baseline.Height != current.Height)
                return new List<Rectangle>();

            var changedPixels = DetectChangedPixels(baseline, current);
            _logger.LogDebug("🔍 Изменённых пикселей: {Count}", changedPixels.Count);

            if (changedPixels.Count < _settings.MinRegionArea)
            {
                SaveChangePixelsDebug(current, changedPixels, [], "no_changes", saveDebug);
                return [];
            }

            // 🔹 ФИЛЬТРАЦИЯ: оставляем только пиксели в плотных областях
            var densePixels = FilterDensePixels(changedPixels, current.Width, current.Height);
            _logger.LogDebug("🔍 Плотных пикселей: {Count}", densePixels.Count);

            if (densePixels.Count < _settings.MinRegionArea)
            {
                SaveChangePixelsDebug(current, densePixels, [], "too_sparse", saveDebug);
                return [];
            }

            // 🔹 BOUNDING BOX ПО ПРОЦЕНТИЛЯМ (отсекаем выбросы)
            var (minX, minY, maxX, maxY) = GetPercentileBoundingBox( densePixels,
                                                                    _boundingBoxLowerPercentile,
                                                                    _boundingBoxUpperPercentile);

            var boundingBox = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            _logger.LogDebug("📦 Bounding box (5-95%): {X},{Y} {W}x{H}",
                minX, minY, boundingBox.Width, boundingBox.Height);

            // 🔹 Проверяем размер
            if (boundingBox.Width < _settings.MinTooltipWidth ||
                boundingBox.Height < _settings.MinTooltipHeight)
            {
                // Пробуем с меньшим порогом процентилей
                var (minX2, minY2, maxX2, maxY2) = GetPercentileBoundingBox(densePixels, 2, 98);
                boundingBox = new Rectangle(minX2, minY2, maxX2 - minX2 + 1, maxY2 - minY2 + 1);

                if (boundingBox.Width < _settings.MinTooltipWidth ||
                    boundingBox.Height < _settings.MinTooltipHeight)
                {
                    SaveChangePixelsDebug(current, densePixels, [boundingBox], "too_small", saveDebug);
                    return [];
                }
            }

            if (boundingBox.Width > _settings.MaxTooltipWidth ||
                boundingBox.Height > _settings.MaxTooltipHeight)
            {
                _logger.LogWarning("⚠ Регион слишком большой: {W}x{H}",
                    boundingBox.Width, boundingBox.Height);
            }

            // 🔹 Добавляем небольшой паддинг
            var result = new List<Rectangle>
            {
                PadRectangle(boundingBox, 3, current.Width, current.Height)
            };

            SaveChangePixelsDebug(current, densePixels, result, "final", saveDebug);
            _logger.LogDebug("✅ Регион: {X},{Y} {W}x{H}",
                result[0].X, result[0].Y, result[0].Width, result[0].Height);

            return result;
        }

        /// <summary>
        /// 🔹 Фильтрует пиксели, оставляя только те, что в плотных областях
        /// </summary>
        private List<(int x, int y)> FilterDensePixels(List<(int x, int y)> pixels, int width, int height)
        {
            if (pixels.Count < 10) return pixels;

            var pixelSet = new HashSet<(int, int)>(pixels);
            var densePixels = new List<(int x, int y)>(pixels.Count);
            int neighborRadius = _densePixelNeighborRadius;
            int minNeighbors = _densePixelMinNeighbors;

            foreach (var (px, py) in pixels)
            {
                int neighbors = 0;

                // Считаем соседей в радиусе
                for (int dy = -neighborRadius; dy <= neighborRadius && neighbors < minNeighbors; dy++)
                {
                    for (int dx = -neighborRadius; dx <= neighborRadius; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = px + dx, ny = py + dy;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height &&
                            pixelSet.Contains((nx, ny)))
                        {
                            neighbors++;
                        }
                    }
                }

                // Оставляем только если есть достаточно соседей
                if (neighbors >= minNeighbors)
                    densePixels.Add((px, py));
            }

            return densePixels;
        }

        /// <summary>
        /// 🔹 Вычисляет bounding box по процентилям (отсекает выбросы)
        /// </summary>
        private (int minX, int minY, int maxX, int maxY) GetPercentileBoundingBox(
            List<(int x, int y)> pixels, int lowerPercentile, int upperPercentile)
        {
            if (pixels.Count == 0)
                return (0, 0, 0, 0);

            // Сортируем координаты
            var sortedX = pixels.Select(p => p.x).OrderBy(x => x).ToList();
            var sortedY = pixels.Select(p => p.y).OrderBy(y => y).ToList();

            // Вычисляем индексы процентилей
            int lowerIdx = (int)(pixels.Count * lowerPercentile / 100.0);
            int upperIdx = (int)(pixels.Count * upperPercentile / 100.0) - 1;

            // Ограничиваем индексы
            lowerIdx = Math.Max(0, Math.Min(lowerIdx, pixels.Count - 1));
            upperIdx = Math.Max(0, Math.Min(upperIdx, pixels.Count - 1));

            int minX = sortedX[lowerIdx];
            int maxX = sortedX[upperIdx];
            int minY = sortedY[lowerIdx];
            int maxY = sortedY[upperIdx];

            return (minX, minY, maxX, maxY);
        }

        public List<Bitmap> CropChangedRegions(Bitmap current, List<Rectangle> regions, bool saveDebug = false)
        {
            var result = new List<Bitmap>(regions.Count);

            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                try
                {
                    var cropped = current.Clone(region, current.PixelFormat);

                    if (saveDebug && !string.IsNullOrEmpty(_debugOutputPath))
                        SaveScreenDebug(cropped, $"region_{i + 1}");

                    result.Add(cropped);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠ Не удалось обрезать регион {Region}", region);
                }
            }
            return result;
        }

        private void SaveScreenDebug(Bitmap cropped, string suffix)
        {
            try
            {
                Directory.CreateDirectory(_debugOutputPath);
                var filename = Path.Combine(_debugOutputPath,
                    $"debug_screen_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                cropped.Save(filename, ImageFormat.Png);
                _logger.LogDebug("💾 Screen debug: {Path}", filename);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠ Ошибка сохранения screen-дебага");
            }
        }

        private void SaveChangePixelsDebug(Bitmap current,
            List<(int x, int y)> changedPixels, List<Rectangle> regions, string stage, bool saveDebug)
        {
            if (!saveDebug || string.IsNullOrEmpty(_debugOutputPath)) return;

            try
            {
                Directory.CreateDirectory(_debugOutputPath);

                using var debug = new Bitmap(current.Width, current.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(debug);
                g.DrawImageUnscaled(current, 0, 0);

                if (changedPixels.Count > 0 && changedPixels.Count < 50000)
                {
                    using var pixelBrush = new SolidBrush(Color.FromArgb(120, Color.Red));
                    foreach (var (x, y) in changedPixels)
                        g.FillRectangle(pixelBrush, x, y, 1, 1);
                }
                else if (changedPixels.Count >= 50000)
                {
                    using var denseBrush = new SolidBrush(Color.FromArgb(100, Color.Orange));
                    for (int i = 0; i < changedPixels.Count; i += 4)
                    {
                        var (x, y) = changedPixels[i];
                        g.FillRectangle(denseBrush, x, y, 2, 2);
                    }
                }

                if (regions.Count > 0)
                {
                    using var regionPen = new Pen(Color.Lime, 2);
                    using var font = new Font("Consolas", 8, FontStyle.Bold);
                    using var textBrush = new SolidBrush(Color.Lime);

                    for (int i = 0; i < regions.Count; i++)
                    {
                        var r = regions[i];
                        g.DrawRectangle(regionPen, r);
                        g.DrawString($"#{i + 1}", font, textBrush, r.X, r.Y - 14);
                    }
                }

                using var legendBg = new SolidBrush(Color.FromArgb(180, 20, 20, 20));
                g.FillRectangle(legendBg, 8, 8, 220, 50);
                using var legendPen = new Pen(Color.Gray, 1);
                g.DrawRectangle(legendPen, 8, 8, 220, 50);
                using var legendFont = new Font("Consolas", 7);
                using var whiteBrush = new SolidBrush(Color.White);
                g.DrawString($"Stage: {stage}", legendFont, whiteBrush, 14, 14);
                g.DrawString($"Pixels: {changedPixels.Count}, Regions: {regions.Count}", legendFont, whiteBrush, 14, 28);

                var filename = Path.Combine(_debugOutputPath,
                    $"debug_change_pixels_{stage}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                debug.Save(filename, ImageFormat.Png);
                _logger.LogDebug("💾 Change pixels debug: {Path}", filename);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠ Ошибка сохранения change-pixels дебага");
            }
        }

        private List<(int x, int y)> DetectChangedPixels(Bitmap baseline, Bitmap current)
        {
            int w = baseline.Width, h = baseline.Height;
            var rect = new Rectangle(0, 0, w, h);
            var bdBase = baseline.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var bdCurr = current.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            var chunks = new ConcurrentBag<List<Coord>>();
            int threshold = _pixelDiffThresholdScaled;

            try
            {
                unsafe
                {
                    int* ptrBase = (int*)bdBase.Scan0;
                    int* ptrCurr = (int*)bdCurr.Scan0;
                    int stride = bdBase.Stride >> 2;

                    Parallel.For(0, h, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        () => new List<Coord>(128),
                        (y, state, local) =>
                        {
                            int* rowBase = ptrBase + y * stride;
                            int* rowCurr = ptrCurr + y * stride;

                            for (int x = 0; x < w; x++)
                            {
                                int b = rowBase[x], c = rowCurr[x];
                                int bSum = ((b >> 16) & 0xFF) + ((b >> 8) & 0xFF) + (b & 0xFF);
                                int cSum = ((c >> 16) & 0xFF) + ((c >> 8) & 0xFF) + (c & 0xFF);
                                int diff = Math.Abs(cSum - bSum);

                                if (diff > (cSum < 180 ? threshold * 2 / 3 : threshold))
                                    local.Add(new Coord(x, y));
                            }
                            return local;
                        },
                        local => chunks.Add(local));
                }
            }
            finally
            {
                baseline.UnlockBits(bdBase);
                current.UnlockBits(bdCurr);
            }

            int total = chunks.Sum(c => c.Count);
            var result = new List<(int, int)>(total);
            foreach (var chunk in chunks)
                foreach (var coord in chunk)
                    result.Add((coord.X, coord.Y));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Rectangle PadRectangle(Rectangle r, int pad, int maxW, int maxH)
        {
            if (pad <= 0) return r;
            int x = Math.Max(0, r.X - pad);
            int y = Math.Max(0, r.Y - pad);
            int r2 = Math.Min(maxW, r.Right + pad);
            int b = Math.Min(maxH, r.Bottom + pad);
            return new Rectangle(x, y, r2 - x, b - y);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct Coord : IEquatable<Coord>
        {
            public readonly int X, Y;
            public Coord(int x, int y) => (X, Y) = (x, y);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(Coord o) => X == o.X && Y == o.Y;
            public override bool Equals(object o) => o is Coord c && Equals(c);
            public override int GetHashCode() => (X * 397) ^ Y;
        }
    }
}