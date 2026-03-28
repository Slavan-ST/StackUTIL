using DebugInterceptor.Models.Enums;
using StackUTIL.Models.Enums;
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
        public int RegionPadding { get; set; } = 0;

        // 🔹 Bounding box percentiles (отсечение выбросов)
        public int BoundingBoxLowerPercentile { get; set; } = 2;
        public int BoundingBoxUpperPercentile { get; set; } = 98;

        // 🔹 Dense pixel filtering (фильтрация шума/курсора)
        public int DensePixelNeighborRadius { get; set; } = 3;
        public int DensePixelMinNeighbors { get; set; } = 3;

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

        /// <summary>
        /// 🔹 Режим вывода уведомлений пользователю
        /// </summary>
        public NotificationMode NotificationMode { get; set; } = NotificationMode.MessageBox;

        // ═══════════════════════════════════════════════════════
        // Database
        // ═══════════════════════════════════════════════════════
        /// <summary> Тип базы данных (стратегия) </summary>
        public DatabaseType DatabaseType { get; set; } = DatabaseType.MsSql;

        /// <summary> Connection string для MSSQL </summary>
        public string MsSqlConnectionString { get; set; } =
            "Server=localhost;Database=YourDb;Trusted_Connection=True;TrustServerCertificate=True;";

        /// <summary> Таймаут запросов в секундах </summary>
        public int QueryTimeoutSeconds { get; set; } = 30;

        /// <summary> Максимальное количество строк для отображения в гриде </summary>
        public int MaxDisplayRows { get; set; } = 1000;
    }
}