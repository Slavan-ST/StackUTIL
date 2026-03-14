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

            // Разбиваем на строки и обрабатываем каждую
            var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;

                // 👇 Ищем паттерн "число : текст"
                var match = Regex.Match(trimmedLine, @"^(-?\d+)\s*:\s*(.+)$");

                if (match.Success)
                {
                    if (long.TryParse(match.Groups[1].Value, out var rowId))
                    {
                        var tableName = CleanTableName(match.Groups[2].Value.Trim());

                        // Пропускаем пустые или невалидные имена
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
        /// Очищает название таблицы от "мусора" OCR
        /// </summary>
        private string CleanTableName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            // Удаляем всё после первого невалидного символа
            // Валидные: буквы (кириллица/латиница), цифры, пробелы, подчёркивания, дефисы
            var result = new System.Text.StringBuilder();

            foreach (var c in rawName)
            {
                // Разрешаем: буквы, цифры, пробел, подчёркивание, дефис
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
                {
                    result.Append(c);
                }
                else
                {
                    // Останавливаемся на первом "мусорном" символе
                    // (Ё, в, Я, 3 и т.д. — это артефакты OCR)
                    break;
                }
            }

            var cleaned = result.ToString().Trim();

            // Дополнительно: убираем повторяющиеся пробелы
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            _logger?.LogDebug("🧹 Очистка: '{Raw}' → '{Clean}'", rawName, cleaned);

            return cleaned;
        }

        /// <summary>
        /// Проверяет, валидно ли имя таблицы
        /// </summary>
        private bool IsValidTableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Length < 2) return false; // Слишком короткое
            if (name.Length > 128) return false; // Слишком длинное

            // Имя должно начинаться с буквы или подчёркивания
            if (!char.IsLetter(name[0]) && name[0] != '_') return false;

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