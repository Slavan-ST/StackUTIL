using System.Text.Json.Serialization;

namespace DebugInterceptor.Models
{
    /// <summary>
    /// Целевая база данных для подключения
    /// </summary>
    public class DatabaseTarget
    {
        /// <summary>
        /// Уникальное имя цели (например, "Production", "TestDB")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Строка подключения к БД
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Таймаут запросов по умолчанию (сек)
        /// </summary>
        public int DefaultTimeoutSec { get; set; } = 30;

        /// <summary>
        /// Разрешено ли выполнять запросы на изменение данных
        /// </summary>
        public bool AllowWriteOperations { get; set; } = false;

        /// <summary>
        /// Дополнительные параметры подключения (опционально)
        /// </summary>
        public Dictionary<string, string>? ExtraParameters { get; set; }

        /// <summary>
        /// Описание цели (для отображения в интерфейсе)
        /// </summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// Настройки OCR-распознавания
    /// </summary>
    public class OcrSettings
    {
        /// <summary>
        /// Язык распознавания (код Tesseract: rus, eng, rus+eng)
        /// </summary>
        public string Language { get; set; } = "rus";

        /// <summary>
        /// Разрешать ли кэширование результатов OCR по хэшу изображения
        /// </summary>
        public bool EnableCache { get; set; } = true;

        /// <summary>
        /// Максимальный размер кэша (записей)
        /// </summary>
        public int MaxCacheSize { get; set; } = 100;

        /// <summary>
        /// Дополнительные параметры Tesseract (ключ-значение)
        /// </summary>
        public Dictionary<string, string>? EngineParameters { get; set; }
    }

    /// <summary>
    /// Настройки горячих клавиш
    /// </summary>
    public class HotkeySettings
    {
        /// <summary>
        /// Виртуальный код клавиши (по умолчанию F12 = 0x7B)
        /// </summary>
        public int VirtualKey { get; set; } = 0x7B;

        /// <summary>
        /// Использовать ли Alt как модификатор
        /// </summary>
        public bool UseAlt { get; set; } = true;

        /// <summary>
        /// Использовать ли Shift как модификатор
        /// </summary>
        public bool UseShift { get; set; } = true;

        /// <summary>
        /// Использовать ли Ctrl как модификатор
        /// </summary>
        public bool UseCtrl { get; set; } = false;

        /// <summary>
        /// Использовать ли Win как модификатор
        /// </summary>
        public bool UseWin { get; set; } = false;

        /// <summary>
        /// Строковое представление комбинации (только для чтения, генерируется)
        /// </summary>
        [JsonIgnore]
        public string CombinationDisplay =>
            $"{(UseCtrl ? "Ctrl+" : "")}{(UseAlt ? "Alt+" : "")}{(UseShift ? "Shift+" : "")}{(UseWin ? "Win+" : "")}{GetKeyName(VirtualKey)}";

        private static string GetKeyName(int vk) => vk switch
        {
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => $"VK_{vk:X}"
        };
    }

    /// <summary>
    /// Настройки безопасности выполнения запросов
    /// </summary>
    public class SecuritySettings
    {
        /// <summary>
        /// Блокировать ли потенциально опасные запросы (DROP, TRUNCATE, etc.)
        /// </summary>
        public bool BlockDangerousQueries { get; set; } = true;

        /// <summary>
        /// Список запрещённых ключевых слов (регистронезависимый поиск)
        /// </summary>
        public List<string> BlockedKeywords { get; set; } = new()
        {
            "DROP", "TRUNCATE", "DELETE FROM", "ALTER DATABASE", "ALTER TABLE",
            "CREATE LOGIN", "DROP LOGIN", "EXEC xp_", "EXEC sp_", "WAITFOR",
            "SHUTDOWN", "KILL ", "RESTORE", "BACKUP DATABASE"
        };

        /// <summary>
        /// Разрешить ли выполнение нескольких запросов в одном вызове (через ;)
        /// </summary>
        public bool AllowBatchQueries { get; set; } = false;

        /// <summary>
        /// Максимальное время выполнения запроса (сек), 0 = без лимита
        /// </summary>
        public int MaxExecutionTimeSec { get; set; } = 120;
    }

    /// <summary>
    /// Настройки интерфейса
    /// </summary>
    public class UiSettings
    {
        /// <summary>
        /// Показывать ли окно результатов поверх всех окон
        /// </summary>
        public bool TopmostResultWindow { get; set; } = true;

        /// <summary>
        /// Автоматически активировать окно результатов при показе
        /// </summary>
        public bool ActivateResultWindow { get; set; } = true;

        /// <summary>
        /// Шрифт для отображения SQL-запросов
        /// </summary>
        public string QueryFontFamily { get; set; } = "Consolas";

        /// <summary>
        /// Размер шрифта для запросов
        /// </summary>
        public int QueryFontSize { get; set; } = 11;

        /// <summary>
        /// Показывать ли уведомления об ошибках (MessageBox)
        /// </summary>
        public bool ShowErrorNotifications { get; set; } = true;
    }

    /// <summary>
    /// Настройки логирования
    /// </summary>
    public class LoggingSettings
    {
        /// <summary>
        /// Минимальный уровень логирования
        /// </summary>
        public string MinLevel { get; set; } = "Information";

        /// <summary>
        /// Писать ли логи в файл (дополнительно к Debug/Console)
        /// </summary>
        public bool WriteToFile { get; set; } = false;

        /// <summary>
        /// Путь к файлу лога (если включено)
        /// </summary>
        public string? LogFilePath { get; set; }

        /// <summary>
        /// Максимальный размер файла лога в МБ перед ротацией
        /// </summary>
        public int MaxLogFileSizeMb { get; set; } = 10;
    }

    /// <summary>
    /// Корневой класс настроек приложения
    /// </summary>
    public class AppSettings
    {
        // ═══════════════════════════════════════════════════════
        // 🔗 Подключения к базам данных
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Список целевых баз данных
        /// </summary>
        public List<DatabaseTarget> Targets { get; set; } = new();

        /// <summary>
        /// Имя цели по умолчанию (для быстрого доступа)
        /// </summary>
        public string DefaultTargetName { get; set; } = string.Empty;


        // ═══════════════════════════════════════════════════════
        // 🔍 OCR-распознавание
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Настройки OCR-движка
        /// </summary>
        public OcrSettings Ocr { get; set; } = new();


        // ═══════════════════════════════════════════════════════
        // ⌨️ Горячие клавиши
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Настройки комбинации перехвата
        /// </summary>
        public HotkeySettings Hotkeys { get; set; } = new();


        // ═══════════════════════════════════════════════════════
        // 🔐 Безопасность
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Настройки безопасности выполнения запросов
        /// </summary>
        public SecuritySettings Security { get; set; } = new();


        // ═══════════════════════════════════════════════════════
        // 🎨 Интерфейс
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Настройки пользовательского интерфейса
        /// </summary>
        public UiSettings Ui { get; set; } = new();


        // ═══════════════════════════════════════════════════════
        // 📝 Логирование
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Настройки системы логирования
        /// </summary>
        public LoggingSettings Logging { get; set; } = new();


        // ═══════════════════════════════════════════════════════
        // 🔧 Прочее
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Версия конфигурации (для миграций при обновлении)
        /// </summary>
        public string ConfigVersion { get; set; } = "1.0";

        /// <summary>
        /// Дополнительные параметры (для расширений)
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? ExtraData { get; set; }


        // ═══════════════════════════════════════════════════════
        // 🎯 Вспомогательные методы
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Получает целевую БД по имени (без учета регистра)
        /// </summary>
        public DatabaseTarget? GetTarget(string name) =>
            Targets.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Получает целевую БД по умолчанию
        /// </summary>
        public DatabaseTarget? GetDefaultTarget() =>
            string.IsNullOrEmpty(DefaultTargetName)
                ? Targets.FirstOrDefault()
                : GetTarget(DefaultTargetName);

        /// <summary>
        /// Проверяет, является ли запрос потенциально опасным
        /// </summary>
        public bool IsQueryDangerous(string sql)
        {
            if (!Security.BlockDangerousQueries) return false;

            var upper = sql.ToUpperInvariant();
            return Security.BlockedKeywords
                .Any(kw => upper.Contains(kw.ToUpperInvariant()));
        }

        /// <summary>
        /// Создаёт настройки по умолчанию (если файл не найден)
        /// </summary>
        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                Targets = new List<DatabaseTarget>
                {
                    new()
                    {
                        Name = "Local",
                        ConnectionString = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;",
                        DefaultTimeoutSec = 30,
                        AllowWriteOperations = false,
                        Description = "Локальный сервер разработки"
                    }
                },
                DefaultTargetName = "Local",
                ConfigVersion = "1.0"
            };
        }
    }
}