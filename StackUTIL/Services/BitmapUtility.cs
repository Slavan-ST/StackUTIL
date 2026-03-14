using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Утилиты для работы с Bitmap: кроп, отладочная отрисовка, сохранение
    /// </summary>
    public class BitmapUtility
    {
        private readonly ILogger<BitmapUtility> _logger;

        public BitmapUtility(ILogger<BitmapUtility> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 🔹 Вырезает область из битмапа
        /// </summary>
        public Bitmap CropBitmap(Bitmap source, Rectangle area)
        {
            var cropped = new Bitmap(area.Width, area.Height);
            using var g = Graphics.FromImage(cropped);
            g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);
            return cropped;
        }

        /// <summary>
        /// 🔹 Сохраняет скриншот с обводкой региона для отладки
        /// </summary>
        public void SaveDebugWithRegion(Bitmap source, Rectangle region, string suffix, string? customLabel = null)
        {
            try
            {
                var debugDir = Path.Combine(Path.GetTempPath(), "DebugInterceptor");
                Directory.CreateDirectory(debugDir);

                using var debugImg = (Bitmap)source.Clone();
                using var g = Graphics.FromImage(debugImg);
                using var pen = new Pen(Color.FromArgb(200, Color.Red), 3);
                g.DrawRectangle(pen, region.X, region.Y, region.Width, region.Height);

                var label = customLabel ?? $"[{region.X},{region.Y}] {region.Width}x{region.Height}";
                using var font = new Font("Segoe UI", 12, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.Yellow);
                using var bgBrush = new SolidBrush(Color.FromArgb(180, Color.Black));

                var textSize = g.MeasureString(label, font);
                g.FillRectangle(bgBrush, region.X, region.Y - 25, textSize.Width + 8, 22);
                g.DrawString(label, font, textBrush, region.X + 4, region.Y - 23);

                var path = Path.Combine(debugDir, $"debug_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                debugImg.Save(path, ImageFormat.Png);
                _logger.LogDebug("💾 Отладка региона: {Path}", path);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка сохранения отладки"); }
        }
    }
}