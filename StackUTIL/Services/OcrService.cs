using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Tesseract;

namespace DebugInterceptor.Services
{
    public class OcrService : IDisposable
    {
        private readonly TesseractEngine _engine;
        private readonly ILogger<OcrService> _logger;
        private readonly string _tessDataPath;

        public OcrService(ILogger<OcrService> logger)
        {
            _logger = logger;
            _tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

            if (!Directory.Exists(_tessDataPath))
                throw new DirectoryNotFoundException($"tessdata не найдена: {_tessDataPath}");

            var trainedData = Path.Combine(_tessDataPath, "rus.traineddata");
            if (!File.Exists(trainedData))
                throw new FileNotFoundException($"rus.traineddata не найден: {trainedData}");

            _engine = new TesseractEngine(_tessDataPath, "rus", EngineMode.Default);
            _engine.DefaultPageSegMode = PageSegMode.SingleBlock;

            _logger.LogInformation("✅ Tesseract инициализирован");
        }

        public string Recognize(Bitmap bitmap)
        {
            try
            {
                using var processedBitmap = PreprocessImage(bitmap);

                var tempImagePath = Path.Combine(Path.GetTempPath(),
                    $"ocr_input_{Guid.NewGuid():N}.png");

                try
                {
                    processedBitmap.Save(tempImagePath, System.Drawing.Imaging.ImageFormat.Png);

                    using var pix = Pix.LoadFromFile(tempImagePath);
                    using var page = _engine.Process(pix);

                    var text = page.GetText()?.Trim() ?? string.Empty;
                    var confidence = page.GetMeanConfidence();

                    _logger.LogInformation("🔤 Tesseract OCR: {Chars} символов, точность {Conf:F1}%",
                        text.Length, confidence);

                    _logger.LogDebug("📝 Распознанный текст:\n{Text}", text);

                    var debugPath = Path.Combine(Path.GetTempPath(),
                        $"ocr_result_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(debugPath, text);
                    _logger.LogDebug("💾 Результат сохранён: {Path}", debugPath);

                    return text;
                }
                finally
                {
                    if (File.Exists(tempImagePath))
                        File.Delete(tempImagePath);
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка Tesseract OCR");
                return string.Empty;
            }
        }

        /// <summary>
        /// Минимальная предобработка - только легкое улучшение
        /// </summary>
        private Bitmap PreprocessImage(Bitmap source)
        {
            // 👇 Просто увеличиваем в 2 раза без сложной обработки
            int newWidth = source.Width * 2;
            int newHeight = source.Height * 2;

            var result = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);

            using (var gfx = Graphics.FromImage(result))
            {
                gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gfx.Clear(Color.White);
                gfx.DrawImage(source, 0, 0, newWidth, newHeight);
            }

            // 👇 Очень легкая коррекция - только для улучшения читаемости
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    Color pixel = result.GetPixel(x, y);
                    int gray = (int)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);

                    // 👇 Минимальная обработка - только очень светлое делаем белым
                    if (gray > 240)
                        gray = 255;
                    // Остальное оставляем как есть

                    Color newColor = Color.FromArgb(gray, gray, gray);
                    result.SetPixel(x, y, newColor);
                }
            }

            return result;
        }

        public void Dispose() => _engine?.Dispose();
    }
}