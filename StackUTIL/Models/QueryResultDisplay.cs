using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace DebugInterceptor.Models
{
    /// <summary>
    /// 🔹 Обёртка результата запроса для отображения в UI
    /// </summary>
    public class QueryResultDisplay
    {
        public QueryExecutionResult Source { get; }

        /// <summary> DataTable для биндинга к DataGrid (авто-колонки) </summary>
        public DataTable Data { get; }

        /// <summary> Отображаемое имя результата </summary>
        public string DisplayName => $"{Source.SourceRecord.TableName} (RowId: {Source.SourceRecord.RowId})";

        /// <summary> Время выполнения для отображения </summary>
        public string ExecutionTimeDisplay => $"{Source.ExecutionTimeMs} мс";

        public QueryResultDisplay(QueryExecutionResult result)
        {
            Source = result;
            Data = ConvertToDataTable(result.Rows);
        }

        private DataTable ConvertToDataTable(List<Dictionary<string, object>> rows)
        {
            var table = new DataTable();

            if (rows == null || rows.Count == 0)
                return table;

            // Извлекаем колонки из первой строки (порядок сохранится при последовательном добавлении)
            var columns = rows[0].Keys.ToList();
            foreach (var col in columns)
                table.Columns.Add(col, typeof(object));

            foreach (var row in rows)
            {
                var dataRow = table.NewRow();
                foreach (var col in columns)
                {
                    dataRow[col] = row.TryGetValue(col, out var value) && value != null
                        ? value
                        : DBNull.Value;
                }
                table.Rows.Add(dataRow);
            }

            return table;
        }
    }
}