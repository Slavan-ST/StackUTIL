using System.Text.RegularExpressions;
using DebugInterceptor.Models;
using Microsoft.Extensions.Logging;

namespace DebugInterceptor.Services
{
    public class DebugDataParser
    {
        private readonly ILogger<DebugDataParser>? _logger;

        public DebugDataParser(ILogger<DebugDataParser>? logger = null)
        {
            _logger = logger;
        }

        public List<DebugRecord> Parse(string rawText)
        {
            var records = new List<DebugRecord>();
            if (string.IsNullOrWhiteSpace(rawText)) return records;

            _logger?.LogDebug("🔍 Исходный текст:\n{Text}", rawText);

            var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                // 🔧 Гибкая регулярка: допускаем русские буквы, похожие на цифры, в начале числа
                var match = Regex.Match(trimmedLine, @"^([\dЗ3]+)\s*:\s*(.+)$");

                if (match.Success)
                {
                    // 🔧 Очищаем ID: оставляем только цифры, З/з→3
                    var idRaw = match.Groups[1].Value.Trim();
                    var idClean = Regex.Replace(idRaw, @"[Зз]", "3");  // Только З→3
                    idClean = Regex.Replace(idClean, @"[^\d]", "");     // Остальное удаляем

                    if (long.TryParse(idClean, out var rowId))
                    {
                        var tableName = CleanTableName(match.Groups[2].Value.Trim());

                        if (string.IsNullOrEmpty(tableName) || !IsValidTableName(tableName))
                        {
                            _logger?.LogDebug("⚠ Пропущено: {Line}", trimmedLine);
                            continue;
                        }

                        records.Add(new DebugRecord
                        {
                            RowId = rowId,
                            TableName = tableName,
                            RawMatch = trimmedLine
                        });

                        _logger?.LogDebug("✅ {RowId} : {Table}", rowId, tableName);
                    }
                }
            }

            _logger?.LogDebug("📋 Всего записей: {Count}", records.Count);
            return records;
        }

        /// <summary>
        /// Очищает название таблицы от "мусора" OCR — БЕЗ агрессивных замен
        /// </summary>
        private string CleanTableName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            var result = new System.Text.StringBuilder();

            foreach (var c in rawName)
            {
                // Разрешаем: буквы (кириллица/латиница), цифры, пробел, подчёркивание, дефис
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
                {
                    result.Append(c);
                }
                else
                {
                    // Останавливаемся на первом "мусорном" символе
                    break;
                }
            }

            var cleaned = result.ToString().Trim();
            cleaned = Regex.Replace(cleaned, @"\s+", " "); // Убираем лишние пробелы

            _logger?.LogDebug("🧹 Очистка: '{Raw}' → '{Clean}'", rawName, cleaned);

            return cleaned;
        }

        /// <summary>
        /// Проверка валидности имени таблицы
        /// </summary>
        private bool IsValidTableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Length < 2) return false;
            if (name.Length > 128) return false;

            // Должно начинаться с буквы
            if (!char.IsLetter(name[0])) return false;

            // Не должно быть только цифр
            if (name.All(char.IsDigit)) return false;

            return true;
        }

        public string GenerateSelectQuery(DebugRecord record)
        {
            var safeTable = record.TableName
                .Replace("[", "")
                .Replace("]", "")
                .Replace("'", "''");

            return $"SELECT * FROM stack.[{safeTable}] WHERE [ROW_ID] = {record.RowId};";
        }
    }
}