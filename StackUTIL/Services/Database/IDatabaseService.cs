using DebugInterceptor.Models.Enums;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace DebugInterceptor.Services.Database
{
    /// <summary>
    /// 🔹 Интерфейс сервиса работы с БД (стратегия)
    /// </summary>
    public interface IDatabaseService
    {
        /// <summary> Тип базы данных </summary>
        DatabaseType DatabaseType { get; }

        /// <summary> Проверка доступности соединения </summary>
        Task<bool> TestConnectionAsync();

        /// <summary> Выполнение запроса, возвращающего данные </summary>
        Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql, int? timeoutSeconds = null);

        /// <summary> Выполнение запроса без возврата данных (INSERT/UPDATE/DELETE) </summary>
        Task<int> ExecuteNonQueryAsync(string sql, int? timeoutSeconds = null);
    }
}