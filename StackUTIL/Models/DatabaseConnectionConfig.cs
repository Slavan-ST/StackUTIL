using DebugInterceptor.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackUTIL.Models
{
    // 🔹 Новый класс для описания подключения
    public class DatabaseConnectionConfig
    {
        public string Name { get; set; } = string.Empty;      // Отображаемое имя: "Prod - SQL01"
        public string ConnectionString { get; set; } = string.Empty;
        public DatabaseType DatabaseType { get; set; } = DatabaseType.MsSql;
        public bool IsDefault { get; set; } = false;
    }

}
