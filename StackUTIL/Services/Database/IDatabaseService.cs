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
        DatabaseType DatabaseType { get; }

        Task<bool> TestConnectionAsync(string? connectionString = null);  // 🔹 Параметр для переопределения

        Task<List<Dictionary<string, object>>> ExecuteQueryAsync(
            string sql,
            string? connectionString = null,  // 🔹 Параметр для переопределения
            int? timeoutSeconds = null);

        Task<int> ExecuteNonQueryAsync(
            string sql,
            string? connectionString = null,  // 🔹 Параметр для переопределения
            int? timeoutSeconds = null);
    }
}