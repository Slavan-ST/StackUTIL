using DebugInterceptor.Models;
using StackUTIL.Services.Query;
using System.Text.RegularExpressions;

namespace DebugInterceptor.Services.Query
{
    /// <summary>
    /// 🔹 Генератор запросов для Microsoft SQL Server
    /// </summary>
    public class MsSqlQueryGenerator : IQueryGenerator
    {
        /// <summary>
        /// 🔹 Экранирование идентификатора для MSSQL: [schema].[table]
        /// </summary>
        public string EscapeIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return "[]";

            // Убираем уже существующие скобки
            var clean = identifier.Replace("[", "").Replace("]", "");
            // Экранируем вложенные ] удвоением
            clean = clean.Replace("]", "]]");
            return $"[{clean}]";
        }

        /// <summary>
        /// 🔹 Генерация безопасного SELECT-запроса
        /// </summary>
        public string GenerateSelectQuery(DebugRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TableName) || record.RowId <= 0)
                return "-- Ошибка: некорректные данные записи";

            var safeTable = EscapeIdentifier(record.TableName);

            // 🔹 Поддержка schema.table
            //var parts = record.TableName.Split('.', 2);
            //string schemaPart = parts.Length > 1 ? EscapeIdentifier(parts[0]) + "." : "";
            //string tablePart = parts.Length > 1 ? EscapeIdentifier(parts[1]) : safeTable;

            return $"SELECT * FROM [stack].[{record.TableName}] WHERE [ROW_ID] = {record.RowId};";
        }
    }
}