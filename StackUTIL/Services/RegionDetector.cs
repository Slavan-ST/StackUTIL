using DebugInterceptor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Детектор изменённых регионов на скриншотах
    /// </summary>
    /// <remarks>
    /// Класс реализует алгоритм поиска областей, изменившихся между двумя скриншотами:
    /// <list type="number">
    /// <item><description>Попиксельное сравнение с порогом яркости</description></item>
    /// <item><description>Поиск связанных компонентов (4-связность)</description></item>
    /// <item><description>Фильтрация по размеру и габаритам</description></item>
    /// <item><description>Расширение границ до контента</description></item>
    /// </list>
    /// Используется в модуле перехвата отладочных тултипов.
    /// </remarks>
    public class RegionDetector
    {
        private readonly ILogger<RegionDetector> _logger;
        private readonly DebugInterceptorSettings _settings;

        /// <summary>
        /// 🔹 Инициализирует новый экземпляр <see cref="RegionDetector"/>
        /// </summary>
        /// <param name="logger">Экземпляр логгера для диагностики</param>
        /// <param name="settings">Настройки детектора из <see cref="IOptions{DebugInterceptorSettings}"/></param>
        public RegionDetector(ILogger<RegionDetector> logger, IOptions<DebugInterceptorSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
        }

        /// <summary>
        /// 🔹 Находит изменённые регионы между двумя скриншотами
        /// </summary>
        /// <param name="baseline">Базовый скриншот (состояние "до")</param>
        /// <param name="current">Текущий скриншот (состояние "после")</param>
        /// <returns>
        /// Список прямоугольников <see cref="Rectangle"/>, описывающих изменённые области.
        /// Пустой список, если изменения не найдены или не прошли фильтрацию.
        /// </returns>
        /// <remarks>
        /// Алгоритм:
        /// <list type="bullet">
        /// <item><description>Проверка размеров изображений</description></item>
        /// <item><description>Детекция изменённых пикселей (<see cref="DetectChangedPixels"/>)</description></item>
        /// <item><description>Поиск связанных компонентов (<see cref="FindConnectedComponents"/>)</description></item>
        /// <item><description>Фильтрация по минимальной площади и габаритам тултипа</description></item>
        /// <item><description>Расширение границ до реального контента (<see cref="ExpandRegionToContent"/>)</description></item>
        /// </list>
        /// </remarks>
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

            var validRegions = regions.Where(r =>
            {
                int area = r.Width * r.Height;
                return area >= _settings.MinRegionArea &&
                       r.Width >= _settings.MinTooltipWidth && r.Width <= _settings.MaxTooltipWidth &&
                       r.Height >= _settings.MinTooltipHeight && r.Height <= _settings.MaxTooltipHeight;
            }).ToList();

            if (validRegions.Count == 0)
            {
                _logger.LogDebug("⚠ Нет регионов в строгих границах, расширяем поиск");
                validRegions = regions.Where(r => r.Width * r.Height >= _settings.MinRegionArea).ToList();
            }

            var expandedRegions = validRegions.Select(r =>
            {
                var expanded = ExpandRegionToContent(r, current, baseline);
                var pad = _settings.RegionPadding;
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
        /// 🔹 Расширяет регион до границ визуального контента
        /// </summary>
        /// <param name="initial">Начальный прямоугольник региона</param>
        /// <param name="current">Текущий скриншот</param>
        /// <param name="baseline">Базовый скриншот для сравнения</param>
        /// <returns>Расширенный <see cref="Rectangle"/>, включающий весь видимый контент</returns>
        /// <remarks>
        /// Метод пошагово расширяет границы региона влево/вправо/вверх/вниз,
        /// пока в добавляемой полосе есть значимые изменения (см. <see cref="HasSignificantChanges"/>).
        /// Позволяет захватить весь тултип, даже если изначально детектирована только его часть.
        /// </remarks>
        private Rectangle ExpandRegionToContent(Rectangle initial, Bitmap current, Bitmap baseline)
        {
            int left = initial.Left, top = initial.Top;
            int right = initial.Right, bottom = initial.Bottom;

            while (left > 0 && HasSignificantChanges(current, baseline, left - 1, top, left, bottom)) left--;
            while (right < current.Width && HasSignificantChanges(current, baseline, right, top, right + 1, bottom)) right++;
            while (top > 0 && HasSignificantChanges(current, baseline, left, top - 1, right, top)) top--;
            while (bottom < current.Height && HasSignificantChanges(current, baseline, left, bottom, right, bottom + 1)) bottom++;

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// 🔹 Проверяет наличие значимых изменений в указанной полосе пикселей
        /// </summary>
        /// <param name="current">Текущий скриншот</param>
        /// <param name="baseline">Базовый скриншот</param>
        /// <param name="x1">Левая граница полосы (включительно)</param>
        /// <param name="y1">Верхняя граница полосы (включительно)</param>
        /// <param name="x2">Правая граница полосы (исключительно)</param>
        /// <param name="y2">Нижняя граница полосы (исключительно)</param>
        /// <returns>
        /// <c>true</c>, если более 20% пикселей в полосе имеют разницу яркости 
        /// выше <see cref="DebugInterceptorSettings.PixelDiffThreshold"/>; иначе <c>false</c>.
        /// </returns>
        private bool HasSignificantChanges(Bitmap current, Bitmap baseline, int x1, int y1, int x2, int y2)
        {
            int changedPixels = 0, totalPixels = 0;

            for (int y = y1; y < y2; y++)
                for (int x = x1; x < x2; x++)
                {
                    if (x < 0 || y < 0 || x >= current.Width || y >= current.Height) continue;

                    var b = baseline.GetPixel(x, y);
                    var c = current.GetPixel(x, y);
                    int diff = Math.Abs((b.R + b.G + b.B) - (c.R + c.G + c.B)) / 3;
                    totalPixels++;

                    if (diff > _settings.PixelDiffThreshold) changedPixels++;
                }

            return totalPixels > 0 && (double)changedPixels / totalPixels > 0.2;
        }

        /// <summary>
        /// 🔹 Детектирует пиксели, изменившиеся между двумя скриншотами
        /// </summary>
        /// <param name="baseline">Базовый скриншот</param>
        /// <param name="current">Текущий скриншот</param>
        /// <returns>
        /// Список кортежей <c>(x, y)</c> с координатами изменённых пикселей.
        /// Пиксель считается изменённым, если:
        /// <list type="bullet">
        /// <item><description>Его яркость в <paramref name="current"/> &gt; 50</description></item>
        /// <item><description>Разница яркости с <paramref name="baseline"/> &gt; <see cref="DebugInterceptorSettings.PixelDiffThreshold"/></description></item>
        /// </list>
        /// </returns>
        private List<(int x, int y)> DetectChangedPixels(Bitmap baseline, Bitmap current)
        {
            var changed = new List<(int x, int y)>();

            for (int y = 0; y < baseline.Height; y++)
                for (int x = 0; x < baseline.Width; x++)
                {
                    var b = baseline.GetPixel(x, y);
                    var c = current.GetPixel(x, y);

                    int bBrightness = (b.R + b.G + b.B) / 3;
                    int cBrightness = (c.R + c.G + c.B) / 3;

                    if (cBrightness > 50 && Math.Abs(bBrightness - cBrightness) > _settings.PixelDiffThreshold)
                        changed.Add((x, y));
                }
            return changed;
        }

        /// <summary>
        /// 🔹 Находит связанные компоненты среди изменённых пикселей (алгоритм заливки)
        /// </summary>
        /// <param name="pixels">Список координат изменённых пикселей</param>
        /// <param name="width">Ширина изображения</param>
        /// <param name="height">Высота изображения</param>
        /// <returns>
        /// Список ограничивающих прямоугольников <see cref="Rectangle"/> для каждого компонента,
        /// содержащего не менее <see cref="DebugInterceptorSettings.ConnectedComponentMinSize"/> пикселей.
        /// </returns>
        /// <remarks>
        /// Используется поиск в ширину (BFS) с 4-связностью (верх/низ/лево/право).
        /// Для каждого компонента вычисляется минимальный ограничивающий прямоугольник (bounding box).
        /// </remarks>
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

                if (count >= _settings.ConnectedComponentMinSize)
                    regions.Add(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
            return regions;
        }
    }
}