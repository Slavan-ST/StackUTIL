using System.IO;
using System.Windows.Input;

namespace DebugInterceptor.Models
{
    /// <summary>
    /// 🔹 Настройки модуля перехвата отладочных данных
    /// </summary>
    public class DebugInterceptorSettings
    {
        // ═══════════════════════════════════════════════════════
        // Hotkey
        // ═══════════════════════════════════════════════════════
        public Key CaptureHotkey { get; set; } = Key.F12;
        public bool HotkeyAlt { get; set; } = true;
        public bool HotkeyShift { get; set; } = true;
        public bool HotkeyCtrl { get; set; } = false;
        public bool HotkeyWin { get; set; } = false;

        // ═══════════════════════════════════════════════════════
        // Capture
        // ═══════════════════════════════════════════════════════
        public int CaptureDelayMs { get; set; } = 500;

        // ═══════════════════════════════════════════════════════
        // Region Detection (RegionDetector)
        // ═══════════════════════════════════════════════════════
        public int PixelDiffThreshold { get; set; } = 5;
        public int MinRegionArea { get; set; } = 500;
        public int MinTooltipWidth { get; set; } = 100;
        public int MaxTooltipWidth { get; set; } = 900;
        public int MinTooltipHeight { get; set; } = 80;
        public int MaxTooltipHeight { get; set; } = 700;
        public int ConnectedComponentMinSize { get; set; } = 15;
        public int RegionPadding { get; set; } = 0;
        public int ExpansionMargin { get; set; } = 10;

        // ═══════════════════════════════════════════════════════
        // OCR
        // ═══════════════════════════════════════════════════════
        public string TessDataPath { get; set; } = "tessdata";
        public string OcrLanguage { get; set; } = "rus";

        // ═══════════════════════════════════════════════════════
        // Debug
        // ═══════════════════════════════════════════════════════
        public string DebugOutputPath { get; set; } = Path.Combine(Path.GetTempPath(), "DebugInterceptor");
        public bool SaveDebugImages { get; set; } = true;
    }
}