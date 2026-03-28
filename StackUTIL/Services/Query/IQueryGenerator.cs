using DebugInterceptor.Models;

namespace StackUTIL.Services.Query
{
    /// <summary>
    /// 🔹 Интерфейс генератора запросов (стратегия)
    /// </summary>
    public interface IQueryGenerator
    {
        /// <summary> Генерация SELECT-запроса для записи </summary>
        string GenerateSelectQuery(DebugRecord record);

        /// <summary> Экранирование имени таблицы для конкретной БД </summary>
        string EscapeIdentifier(string identifier);
    }
}