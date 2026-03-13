// Services/ISqlMonitoringService.cs
using DebugInterceptor.Models;

namespace DebugInterceptor.Services
{
    public interface ISqlMonitoringService
    {
        /// <summary>
        /// Выполняет SQL-запрос по строке подключения
        /// </summary>
        Task<QueryResult> ExecuteQueryAsync(
            string connectionString,
            string sql,
            int timeoutSec = 30,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Выполняет запрос по имени цели из настроек
        /// </summary>
        Task<QueryResult> ExecuteQueryByTargetAsync(
            string targetName,
            string sql,
            int timeoutSec = 30,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет доступность подключения
        /// </summary>
        Task<bool> TestConnectionAsync(string connectionString, CancellationToken cancellationToken = default);
    }
}