using DebugInterceptor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Tesseract;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// 🔹 Валидатор заголовка тултипа через OCR
    /// </summary>
    public class TooltipValidator
    {
        private readonly ILogger<TooltipValidator> _logger;
        private readonly DebugInterceptorSettings _settings;
        private readonly string _tessDataPath;

        public TooltipValidator(
            ILogger<TooltipValidator> logger,
            IOptions<DebugInterceptorSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
            _tessDataPath = Path.Combine(AppContext.BaseDirectory, _settings.TessDataPath);
        }

        /// <summary>
        /// 🔹 Проверяет, содержит ли изображение заголовок "Структура записи"
        /// </summary>
        public bool ContainsTooltipHeader(Bitmap bitmap)
        {
            try
            {
                using var engine = new TesseractEngine(_tessDataPath, _settings.OcrLanguage, EngineMode.Default);
                engine.DefaultPageSegMode = PageSegMode.SingleBlock;

                var tempPath = Path.Combine(Path.GetTempPath(), $"chk_{Guid.NewGuid():N}.png");
                try
                {
                    bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                    using var pix = Pix.LoadFromFile(tempPath);
                    using var page = engine.Process(pix);
                    var text = page.GetText()?.ToLower() ?? "";
                    _logger.LogDebug("🔍 Заголовок: '{Text}'", text[..Math.Min(100, text.Length)]);
                    return text.Contains("структура") && text.Contains("запис");
                }
                finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "⚠ Ошибка проверки заголовка"); return false; }
        }
    }
}