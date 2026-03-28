using Dapper;
using DebugInterceptor.Models;
using DebugInterceptor.Models.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;

namespace DebugInterceptor.Services.Database
{
    /// <summary>
    /// 🔹 Реализация для Microsoft SQL Server (Dapper)
    /// </summary>
    public class MsSqlDatabaseService : IDatabaseService
    {
        public DatabaseType DatabaseType => DatabaseType.MsSql;

        private readonly ILogger<MsSqlDatabaseService> _logger;
        private readonly string _connectionString;
        private readonly int _defaultTimeout;

        public MsSqlDatabaseService(
            ILogger<MsSqlDatabaseService> logger,
            IOptions<DebugInterceptorSettings> settings)
        {
            _logger = logger;
            _connectionString = settings.Value.MsSqlConnectionString;
            _defaultTimeout = settings.Value.QueryTimeoutSeconds;
        }

        public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(
    string sql,
    string? connectionString = null,
    int? timeoutSeconds = null)
        {
            // 🔹 Используем переданный connection string или дефолтный
            var cs = string.IsNullOrEmpty(connectionString) ? _connectionString : connectionString;

            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();

            var command = new CommandDefinition(
                sql,
                commandTimeout: timeoutSeconds ?? _defaultTimeout,
                commandType: CommandType.Text);

            var results = new List<Dictionary<string, object>>();

            using var multi = await connection.QueryMultipleAsync(command);

            while (!multi.IsConsumed)
            {
                var rows = await multi.ReadAsync();
                foreach (IDictionary<string, object> row in rows)
                {
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in row)
                        dict[kvp.Key] = kvp.Value == DBNull.Value ? null : kvp.Value;
                    results.Add(dict);
                }
            }

            _logger.LogDebug("✅ Выполнен запрос, строк: {Count}", results.Count);
            return results;
        }

        // 🔹 Аналогично обновляем TestConnectionAsync и ExecuteNonQueryAsync
        public async Task<bool> TestConnectionAsync(string? connectionString = null)
        {
            var cs = string.IsNullOrEmpty(connectionString) ? _connectionString : connectionString;

            try
            {
                await using var connection = new SqlConnection(cs);
                await connection.OpenAsync();
                _ = await connection.ExecuteScalarAsync("SELECT 1");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠ Не удалось подключиться к БД");
                return false;
            }
        }
        public async Task<int> ExecuteNonQueryAsync(string sql, string? connectionString = null, int? timeoutSeconds = null)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            return await connection.ExecuteAsync(new CommandDefinition(
                sql,
                commandTimeout: timeoutSeconds ?? _defaultTimeout,
                commandType: CommandType.Text));
        }
    }
}