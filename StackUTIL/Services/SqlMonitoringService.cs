// Services/SqlMonitoringService.cs
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using DebugInterceptor.Models;
using System.Data;
using System.Text.RegularExpressions;

namespace DebugInterceptor.Services
{
    public class SqlMonitoringService : ISqlMonitoringService
    {
        private readonly ILogger<SqlMonitoringService> _logger;
        private readonly SettingsManager<AppSettings> _settingsManager;

        public SqlMonitoringService(
            ILogger<SqlMonitoringService> logger,
            SettingsManager<AppSettings> settingsManager)
        {
            _logger = logger;
            _settingsManager = settingsManager;
        }

        /// <summary>
        /// Выполняет запрос по строке подключения (прямой вызов)
        /// </summary>
        public async Task<QueryResult> ExecuteQueryAsync(
            string connectionString,
            string sql,
            int timeoutSec = 30,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 🔒 Проверка на опасные запросы (ИСПРАВЛЕНО: Security.BlockedKeywords)
                if (_settingsManager.Settings.Security.BlockDangerousQueries && IsDangerousQuery(sql))
                {
                    _logger.LogWarning("⚠ Заблокирован потенциально опасный запрос: {Sql}", sql);
                    return QueryResult.Error("Запрос заблокирован политикой безопасности.");
                }

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                using var command = new SqlCommand(sql, connection)
                {
                    CommandTimeout = timeoutSec,
                    CommandType = CommandType.Text
                };

                if (IsSelectQuery(sql))
                {
                    using var adapter = new SqlDataAdapter(command);
                    var table = new DataTable();
                    adapter.Fill(table);

                    stopwatch.Stop();
                    _logger.LogInformation("✅ Запрос выполнен за {Ms} мс, строк: {Rows}",
                        stopwatch.ElapsedMilliseconds, table.Rows.Count);

                    return QueryResult.Success(table, table.Rows.Count, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                    stopwatch.Stop();
                    _logger.LogInformation("✅ Запрос выполнен за {Ms} мс, затронуто строк: {Affected}",
                        stopwatch.ElapsedMilliseconds, affected);

                    return QueryResult.Success(new DataTable(), affected, stopwatch.ElapsedMilliseconds);
                }
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                _logger.LogError(ex, "⏱ Таймаут выполнения запроса");
                return QueryResult.Error($"Таймаут запроса ({timeoutSec} сек).");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "❌ Ошибка SQL: {Message}", ex.Message);
                return QueryResult.Error($"SQL Error ({ex.Number}): {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Неожиданная ошибка");
                return QueryResult.Error($"Ошибка: {ex.Message}");
            }
        }

        /// <summary>
        /// Выполняет запрос по имени цели из настроек
        /// </summary>
        public async Task<QueryResult> ExecuteQueryByTargetAsync(
            string targetName,
            string sql,
            int timeoutSec = 30,
            CancellationToken cancellationToken = default)
        {
            var target = _settingsManager.Settings.GetTarget(targetName);

            if (target == null)
                return QueryResult.Error($"Цель '{targetName}' не найдена в настройках.");

            if (!target.AllowWriteOperations && !IsSelectQuery(sql))
                return QueryResult.Error($"Запросы на изменение данных запрещены для цели '{targetName}'.");

            return await ExecuteQueryAsync(
                target.ConnectionString,
                sql,
                timeoutSec == 30 ? target.DefaultTimeoutSec : timeoutSec,
                cancellationToken);
        }

        public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ Не удалось подключиться к БД");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════
        // Вспомогательные методы (ИСПРАВЛЕНО: Security.BlockedKeywords)
        // ═══════════════════════════════════════════════════════

        private bool IsDangerousQuery(string sql)
        {
            var upper = sql.ToUpperInvariant();
            // ИСПРАВЛЕНО: было _settingsManager.Settings.BlockedKeywords
            return _settingsManager.Settings.Security.BlockedKeywords
                .Any(kw => upper.Contains(kw.ToUpperInvariant()));
        }

        private bool IsSelectQuery(string sql)
        {
            var trimmed = sql.TrimStart().ToUpperInvariant();
            return trimmed.StartsWith("SELECT") || trimmed.StartsWith("WITH");
        }
    }
}