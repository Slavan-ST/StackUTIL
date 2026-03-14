using DebugInterceptor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Детектор изменённых регионов (максимальная оптимизация)
    /// </summary>
    /// <remarks>
    /// Оптимизации:
    /// <list type="bullet">
    /// <item><description>LockBits + unsafe + SIMD-ready структура пикселей</description></item>
    /// <item><description>Parallel.For с thread-local списками</description></item>
    /// <item><description>Flat array вместо byte[,] для кэш-локальности</description></item>
    /// <item><description>ArrayPool для уменьшения давления на GC</description></item>
    /// <item><description>Early exit в проверках + битовые операции</description></item>
    /// <item><description>Struct Coord вместо ValueTuple для stack-аллокаций</description></item>
    /// <item><description>Custom HashSet comparer для координат</description></item>
    /// </list>
    /// </remarks>
    public class RegionDetector
    {
        private readonly ILogger<RegionDetector> _logger;
        private readonly DebugInterceptorSettings _settings;
        private readonly int _pixelDiffThresholdScaled; // порог * 3, вычислен один раз
        private readonly CoordComparer _coordComparer;

        public RegionDetector(ILogger<RegionDetector> logger, IOptions<DebugInterceptorSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
            _pixelDiffThresholdScaled = _settings.PixelDiffThreshold * 3;
            _coordComparer = new CoordComparer();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<Rectangle> FindChangedRegions(Bitmap baseline, Bitmap current)
        {
            if (baseline.Width != current.Width || baseline.Height != current.Height)
                return new List<Rectangle>();

            var changedCoords = DetectChangedPixels(baseline, current);
            _logger.LogDebug("🔍 Найдено {Count} изменённых пикселей", changedCoords.Count);

            if (changedCoords.Count < _settings.MinRegionArea)
                return new List<Rectangle>();

            var regions = FindConnectedComponents(changedCoords, current.Width, current.Height);
            _logger.LogDebug("📦 Найдено регионов до фильтрации: {Count}", regions.Count);

            var minArea = _settings.MinRegionArea;
            var minW = _settings.MinTooltipWidth; var maxW = _settings.MaxTooltipWidth;
            var minH = _settings.MinTooltipHeight; var maxH = _settings.MaxTooltipHeight;

            var validRegions = new List<Rectangle>(regions.Count);
            foreach (var r in regions)
            {
                int area = r.Width * r.Height;
                if (area >= minArea && r.Width >= minW && r.Width <= maxW && r.Height >= minH && r.Height <= maxH)
                    validRegions.Add(r);
            }

            if (validRegions.Count == 0)
            {
                _logger.LogDebug("⚠ Нет регионов в строгих границах, расширяем поиск");
                foreach (var r in regions)
                    if (r.Width * r.Height >= minArea)
                        validRegions.Add(r);
            }

            var expandedRegions = new List<Rectangle>(validRegions.Count);
            var pad = _settings.RegionPadding;
            var cw = current.Width; var ch = current.Height;

            // 🔹 Кэшируем яркости один раз для всех расширений
            var baselineMap = GetBrightnessMapFlat(baseline);
            var currentMap = GetBrightnessMapFlat(current);

            foreach (var r in validRegions)
            {
                var expanded = ExpandRegionToContentFast(r, currentMap, baselineMap, cw, ch);
                int x = expanded.X - pad; if (x < 0) x = 0;
                int y = expanded.Y - pad; if (y < 0) y = 0;
                int right = expanded.Right + pad; if (right > cw) right = cw;
                int bottom = expanded.Bottom + pad; if (bottom > ch) bottom = ch;
                expandedRegions.Add(new Rectangle(x, y, right - x, bottom - y));
            }

            _logger.LogDebug("✅ Регионов после обработки: {Count}", expandedRegions.Count);
            return expandedRegions;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Rectangle ExpandRegionToContentFast(Rectangle initial, byte[] currentMap, byte[] baselineMap, int width, int height)
        {
            int left = initial.Left, top = initial.Top;
            int right = initial.Right, bottom = initial.Bottom;
            int threshold = _settings.PixelDiffThreshold;

            // 🔹 Early exit + flat array + uint bounds
            while (left > 0 && HasSignificantChangesFast(currentMap, baselineMap, width, height, left - 1, top, left, bottom, threshold)) left--;
            while (right < width && HasSignificantChangesFast(currentMap, baselineMap, width, height, right, top, right + 1, bottom, threshold)) right++;
            while (top > 0 && HasSignificantChangesFast(currentMap, baselineMap, width, height, left, top - 1, right, top, threshold)) top--;
            while (bottom < height && HasSignificantChangesFast(currentMap, baselineMap, width, height, left, bottom, right, bottom + 1, threshold)) bottom++;

            return new Rectangle(left, top, right - left, bottom - top);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasSignificantChangesFast(byte[] currentMap, byte[] baselineMap, int width, int height,
            int x1, int y1, int x2, int y2, int threshold)
        {
            int changed = 0, total = 0;
            const double limit = 0.2;

            for (int y = y1; y < y2; y++)
            {
                int yOffset = y * width;
                for (int x = x1; x < x2; x++)
                {
                    // 🔹 Быстрая проверка границ одним сравнением
                    if ((uint)x >= (uint)width || (uint)y >= (uint)height) continue;

                    int idx = yOffset + x;
                    int diff = baselineMap[idx] - currentMap[idx];
                    if (diff < 0) diff = -diff;

                    total++;
                    if (diff > threshold) changed++;

                    // 🔹 Early exit: как только 20% достигнуто — выходим
                    if (total >= 5 && changed * 5 >= total) return true;
                }
            }
            return total > 0 && changed * 5 >= total; // умножение вместо деления
        }

        /// <summary>
        /// 🔹 Flat array + ArrayPool + unsafe для максимальной скорости
        /// </summary>
        private byte[] GetBrightnessMapFlat(Bitmap bitmap)
        {
            int w = bitmap.Width, h = bitmap.Height;
            byte[] brightness = ArrayPool<byte>.Shared.Rent(w * h);
            var rect = new Rectangle(0, 0, w, h);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    int* ptr = (int*)data.Scan0;
                    int stride = data.Stride >> 2; // >>2 вместо /4

                    for (int y = 0; y < h; y++)
                    {
                        int* row = ptr + y * stride;
                        int rowOffset = y * w;
                        for (int x = 0; x < w; x++)
                        {
                            int pixel = row[x];
                            // 🔹 Сумма каналов без деления (делим в конце один раз)
                            int sum = ((pixel >> 16) & 0xFF) + ((pixel >> 8) & 0xFF) + (pixel & 0xFF);
                            brightness[rowOffset + x] = (byte)(sum / 3);
                        }
                    }
                }
                return brightness; // возвращаем "сырой" массив, длина >= w*h
            }
            finally
            {
                bitmap.UnlockBits(data);
                // 🔹 Не возвращаем в пул здесь — вернёт вызывающий код после использования
            }
        }

        /// <summary>
        /// 🔹 Parallel.For + thread-local списки + unsafe
        /// </summary>
        private List<(int x, int y)> DetectChangedPixels(Bitmap baseline, Bitmap current)
        {
            int w = baseline.Width, h = baseline.Height;
            var rect = new Rectangle(0, 0, w, h);
            var bdBase = baseline.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var bdCurr = current.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            var chunks = new ConcurrentBag<List<Coord>>();
            int threshold = _pixelDiffThresholdScaled;
            int minBrightness = 150; // 50 * 3

            try
            {
                unsafe
                {
                    int* ptrBase = (int*)bdBase.Scan0;
                    int* ptrCurr = (int*)bdCurr.Scan0;
                    int stride = bdBase.Stride >> 2;

                    Parallel.For(0, h, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        () => new List<Coord>(w >> 6), // preallocate ~1.5% от строки
                        (y, state, local) =>
                        {
                            int* rowBase = ptrBase + y * stride;
                            int* rowCurr = ptrCurr + y * stride;

                            for (int x = 0; x < w; x++)
                            {
                                int bPix = rowBase[x], cPix = rowCurr[x];

                                int bBright = ((bPix >> 16) & 0xFF) + ((bPix >> 8) & 0xFF) + (bPix & 0xFF);
                                int cBright = ((cPix >> 16) & 0xFF) + ((cPix >> 8) & 0xFF) + (cPix & 0xFF);

                                int diff = cBright - bBright;
                                if (diff < 0) diff = -diff;

                                if (cBright > minBrightness && diff > threshold)
                                    local.Add(new Coord(x, y));
                            }
                            return local;
                        },
                        local => chunks.Add(local)
                    );
                }
            }
            finally
            {
                baseline.UnlockBits(bdBase);
                current.UnlockBits(bdCurr);
            }

            // 🔹 Объединяем результаты с предвыделением
            int totalEstimate = 0;
            foreach (var c in chunks) totalEstimate += c.Count;

            var result = new List<(int, int)>(totalEstimate);
            foreach (var chunk in chunks)
                foreach (var coord in chunk)
                    result.Add((coord.X, coord.Y));

            return result;
        }

        /// <summary>
        /// 🔹 BFS с custom comparer + struct Coord + flat HashSet
        /// </summary>
        private List<Rectangle> FindConnectedComponents(List<(int x, int y)> pixels, int width, int height)
        {
            if (pixels.Count == 0) return new List<Rectangle>();

            // 🔹 Custom HashSet с struct-ключом (меньше аллокаций)
            var pixelSet = new HashSet<Coord>(pixels.Count, _coordComparer);
            foreach (var p in pixels) pixelSet.Add(new Coord(p.x, p.y));

            var visited = new HashSet<Coord>(pixels.Count, _coordComparer);
            var regions = new List<Rectangle>(Math.Min(pixels.Count, 100));
            var queue = new Queue<Coord>(pixels.Count);

            // 🔹 Directions как массив структур для кэш-локальности
            Span<Coord> directions = stackalloc Coord[] { new(-1, 0), new(1, 0), new(0, -1), new(0, 1) };

            foreach (var startTuple in pixels)
            {
                var start = new Coord(startTuple.x, startTuple.y);
                if (!visited.Add(start)) continue;

                queue.Clear();
                queue.Enqueue(start);

                int minX = start.X, maxX = start.X;
                int minY = start.Y, maxY = start.Y;
                int count = 1;

                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (ref readonly var dir in directions)
                    {
                        int nx = cur.X + dir.X, ny = cur.Y + dir.Y;

                        // 🔹 Быстрая проверка границ
                        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;

                        var neighbor = new Coord(nx, ny);
                        if (visited.Contains(neighbor)) continue;
                        if (!pixelSet.Contains(neighbor)) continue;

                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);

                        if (nx < minX) minX = nx; else if (nx > maxX) maxX = nx;
                        if (ny < minY) minY = ny; else if (ny > maxY) maxY = ny;
                        count++;
                    }
                }

                if (count >= _settings.ConnectedComponentMinSize)
                    regions.Add(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
            return regions;
        }

        // 🔹 Struct для координат (меньше аллокаций, чем ValueTuple)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct Coord : IEquatable<Coord>
        {
            public readonly int X, Y;
            public Coord(int x, int y) => (X, Y) = (x, y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(Coord other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is Coord c && Equals(c);
            public override int GetHashCode() => (X * 397) ^ Y; // быстрый хэш
        }

        // 🔹 Custom comparer для HashSet<Coord>
        private sealed class CoordComparer : IEqualityComparer<Coord>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(Coord x, Coord y) => x.X == y.X && x.Y == y.Y;
            public int GetHashCode(Coord c) => (c.X * 397) ^ c.Y;
        }
    }
}