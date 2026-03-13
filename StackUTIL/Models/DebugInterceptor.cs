// Models/QueryResult.cs
using System.Data;

namespace DebugInterceptor.Models
{
    /// <summary>
    /// Результат выполнения SQL-запроса
    /// </summary>
    public class QueryResult
    {
        /// <summary>
        /// Таблица с данными (если запрос возвращал результат)
        /// </summary>
        public DataTable? Rows { get; set; }

        /// <summary>
        /// Количество затронутых строк (для INSERT/UPDATE/DELETE)
        /// </summary>
        public int RowsAffected { get; set; }

        /// <summary>
        /// Время выполнения запроса в миллисекундах
        /// </summary>
        public long ExecutionTimeMs { get; set; }

        /// <summary>
        /// Сообщение об ошибке (если запрос не выполнен)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Был ли запрос успешным
        /// </summary>
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// Создаёт результат с ошибкой
        /// </summary>
        public static QueryResult Error(string message) => new() { ErrorMessage = message };

        /// <summary>
        /// Создаёт успешный результат с данными
        /// </summary>
        public static QueryResult Success(DataTable rows, int affected, long elapsedMs) =>
            new() { Rows = rows, RowsAffected = affected, ExecutionTimeMs = elapsedMs };
    }
}