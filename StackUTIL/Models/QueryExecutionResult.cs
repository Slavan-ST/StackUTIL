using System;
using System.Collections.Generic;

namespace DebugInterceptor.Models
{
    /// <summary>
    /// 🔹 Результат выполнения запроса
    /// </summary>
    public class QueryExecutionResult
    {
        /// <summary> Исходная запись </summary>
        public DebugRecord SourceRecord { get; set; } = null!;

        /// <summary> Выполненный SQL-запрос </summary>
        public string ExecutedQuery { get; set; } = string.Empty;

        /// <summary> Данные результата (строки × колонки) </summary>
        public List<Dictionary<string, object>>? Rows { get; set; }

        /// <summary> Ошибка выполнения (если была) </summary>
        public string? Error { get; set; }

        /// <summary> Время выполнения в мс </summary>
        public long ExecutionTimeMs { get; set; }

        /// <summary> Статус выполнения </summary>
        public QueryExecutionStatus Status { get; set; }

        /// <summary> Количество возвращённых строк </summary>
        public int RowCount => Rows?.Count ?? 0;
    }

    /// <summary>
    /// 🔹 Статус выполнения запроса
    /// </summary>
    public enum QueryExecutionStatus
    {
        Pending = 0,
        Running = 1,
        Success = 2,
        Error = 3,
        Cancelled = 4
    }
}