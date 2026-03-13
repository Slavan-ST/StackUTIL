// Services/SettingsManager.cs
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DebugInterceptor.Services
{
    /// <summary>
    /// Менеджер настроек с авто-сохранением и валидацией
    /// </summary>
    public class SettingsManager<T> where T : new()
    {
        private readonly string _filePath;
        private readonly ILogger<SettingsManager<T>> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        private T? _settings;

        public SettingsManager(string fileName, ILogger<SettingsManager<T>> logger)
        {
            _logger = logger;
            _filePath = Path.Combine(AppContext.BaseDirectory, fileName);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Загружаем или создаём настройки
            Settings = LoadOrCreate();
        }

        public T Settings
        {
            get => _settings ??= new T();
            set
            {
                _settings = value;
                Save();
            }
        }

        private T LoadOrCreate()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                    _logger.LogInformation("📄 Настройки загружены: {Path}", _filePath);
                    return settings ?? new T();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠ Не удалось загрузить настройки, создаём новые");
            }

            var defaults = new T();
            Save(defaults);
            return defaults;
        }

        public void Save() => Save(Settings);

        public void Save(T settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(_filePath, json);
                _logger.LogDebug("💾 Настройки сохранены: {Path}", _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка сохранения настроек");
            }
        }

        public void Reload() => _settings = LoadOrCreate();
    }
}