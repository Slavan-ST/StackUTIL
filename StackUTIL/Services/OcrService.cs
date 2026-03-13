// Services/OcrService.cs
using Tesseract;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

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
                throw new FileNotFoundException($"rus.traineddata не найден: {trainedData}\n" +
                    $"Скачайте с: https://github.com/tesseract-ocr/tessdata");

            // 👇 EngineMode.Default — как в вашем примере
            _engine = new TesseractEngine(_tessDataPath, "rus", EngineMode.Default);

            // 👇 Базовые настройки (можно расширить при необходимости)
            _engine.DefaultPageSegMode = PageSegMode.SingleBlock;

            _logger.LogInformation("✅ Tesseract инициализирован (русский, EngineMode.Default)");
        }

        /// <summary>
        /// Распознаёт текст из Bitmap (паттерн: Bitmap → файл → Pix → распознавание)
        /// </summary>
        public string Recognize(Bitmap bitmap)
        {
            try
            {
                // 👇 1. Сохраняем Bitmap во временный PNG-файл
                var tempImagePath = Path.Combine(Path.GetTempPath(),
                    $"ocr_input_{Guid.NewGuid():N}.png");

                try
                {
                    bitmap.Save(tempImagePath, System.Drawing.Imaging.ImageFormat.Png);

                    // 👇 2. Загружаем через Pix.LoadFromFile (как в вашем примере)
                    using var pix = Pix.LoadFromFile(tempImagePath);

                    // 👇 3. Распознаём
                    using var page = _engine.Process(pix);

                    var text = page.GetText()?.Trim() ?? string.Empty;
                    var confidence = page.GetMeanConfidence();

                    _logger.LogInformation("🔤 Tesseract OCR: {Chars} символов, точность {Conf:F1}%",
                        text.Length, confidence);

                    _logger.LogDebug("📝 Распознанный текст:\n{Text}", text);

                    // 👇 4. Сохраняем для отладки
                    var debugPath = Path.Combine(Path.GetTempPath(),
                        $"ocr_result_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(debugPath, text);
                    _logger.LogDebug("💾 Результат сохранён: {Path}", debugPath);

                    return text;
                }
                finally
                {
                    // 👇 5. Удаляем временный файл изображения
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

        public void Dispose() => _engine?.Dispose();
    }
}