using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DebugInterceptor.Models;
using DebugInterceptor.Services.Database;
using DebugInterceptor.Services.Query;
using DebugInterceptor.Views;
using Microsoft.Extensions.Options;
using StackUTIL.Models;
using StackUTIL.Services.Query;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DebugInterceptor.ViewModels
{
    public partial class DebugResultViewModel : ObservableObject
    {
        // 🔹 Новые свойства
        [ObservableProperty]
        private ObservableCollection<DatabaseConnectionConfig> _availableConnections = new();

        [ObservableProperty]
        private DatabaseConnectionConfig _selectedConnection;

        [ObservableProperty]
        private string _connectionStatus = "🔌 Не подключено";
        // 🔹 Новые свойства (добавить в класс)
        [ObservableProperty]
        private ObservableCollection<QueryResultDisplay> _executionResults = new();

        [ObservableProperty]
        private DebugRecord _selectedRecord;

        // 🔹 Новая команда
        public IRelayCommand ExecuteQueryCommand { get; }

        [ObservableProperty]
        private ObservableCollection<DebugRecord> _records = new();

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        // 🔹 Результаты выполнения запросов
        [ObservableProperty]
        private ObservableCollection<QueryExecutionResult> _queryResults = new();

        [ObservableProperty]
        private bool _isExecuting = false;
        public IRelayCommand ClearResultsCommand { get; }
        public IRelayCommand CopyFromResultsCommand { get; }

        // 🔹 Сервисы
        private readonly IDatabaseService _dbService;
        private readonly IQueryGenerator _queryGenerator;

        public IRelayCommand CopySelectedCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public IRelayCommand<QueryExecutionResult> CopyResultCommand { get; }
        // 🔹 Поле для настроек
        private readonly IOptions<DebugInterceptorSettings> _settings;


        public DebugResultViewModel(
            IDatabaseService dbService,
            IQueryGenerator queryGenerator,
            IOptions<DebugInterceptorSettings> settings)
        {
            _dbService = dbService;
            _queryGenerator = queryGenerator;
            _settings = settings;  // 🔹 Сохраняем настройки в поле

            // 🔹 Инициализация списка подключений
            LoadAvailableConnections();

            CopySelectedCommand = new RelayCommand(CopySelectedValues);
            CloseCommand = new RelayCommand(() =>
                System.Windows.Application.Current.Windows
                    .OfType<DebugResultWindow>()
                    .FirstOrDefault(w => w.DataContext == this)?.Close());

            ExecuteQueryCommand = new RelayCommand<DebugRecord>(
                 execute: async (record) => await ExecuteQueryForRecordAsync(record),
                 canExecute: (record) => record != null && !IsExecuting  // 🔹 Кнопка активна только при валидной записи
             );
            ClearResultsCommand = new RelayCommand(() => ExecutionResults.Clear());
            CopyFromResultsCommand = new RelayCommand<object>(CopyFromResults);
        }

        // 🔹 Общий метод копирования выбранных ячеек из любого DataGrid
        public void CopySelectedCellsFromGrid(System.Windows.Controls.DataGrid grid)
        {
            if (grid?.SelectedCells == null || !grid.SelectedCells.Any())
                return;

            var values = grid.SelectedCells
                .Select(cell => GetCellValueFromGridCell(cell))
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            if (values.Any())
            {
                System.Windows.Clipboard.SetText(string.Join("\t", values)); // Tab для удобной вставки в Excel
                StatusMessage = $"✅ Скопировано {values.Count} значений из результатов";
            }
        }

        // 🔹 Извлечение значения из ячейки результата (DataTable row)
        private string? GetCellValueFromGridCell(System.Windows.Controls.DataGridCellInfo cell)
        {
            if (cell.Item is not System.Data.DataRowView rowView || cell.Column == null)
                return null;

            var column = cell.Column as System.Windows.Controls.DataGridBoundColumn;
            if (column?.Binding is not System.Windows.Data.Binding binding || binding.Path?.Path is not string path)
                return null;

            // Для DataTable колонки — путь это имя колонки
            var value = rowView[path];
            return value == null || value == DBNull.Value ? null : value.ToString();
        }

        // 🔹 Обработчик команды копирования из результатов
        private void CopyFromResults(object parameter)
        {
            // parameter может быть DataGrid или QueryResultDisplay
            if (parameter is System.Windows.Controls.DataGrid grid)
            {
                CopySelectedCellsFromGrid(grid);
            }
            else if (parameter is QueryResultDisplay resultDisplay)
            {
                // Если передан результат — ищем соответствующий DataGrid в визуальном дереве
                // (упрощённо: копируем все данные результата в буфер)
                if (resultDisplay.Source?.Rows != null)
                {
                    var allValues = resultDisplay.Source.Rows
                        .SelectMany(row => row.Values)
                        .Where(v => v != null)
                        .Select(v => v.ToString())
                        .ToList();

                    if (allValues.Any())
                    {
                        System.Windows.Clipboard.SetText(string.Join("\r\n", allValues));
                        StatusMessage = $"✅ Скопировано {allValues.Count} значений";
                    }
                }
            }
        }

        // 🔹 Метод загрузки доступных подключений
        private void LoadAvailableConnections()
        {
            var configs = _settings.Value.AvailableConnections;

            // Если список пуст — добавляем текущее подключение как опцию
            if (!configs.Any())
            {
                configs.Add(new DatabaseConnectionConfig
                {
                    Name = "🔹 Текущее подключение",
                    ConnectionString = _settings.Value.MsSqlConnectionString,
                    DatabaseType = _settings.Value.DatabaseType,
                    IsDefault = true
                });
            }

            AvailableConnections = new ObservableCollection<DatabaseConnectionConfig>(configs);

            // 🔹 Авто-выбор: по умолчанию или первое
            SelectedConnection = AvailableConnections
                .FirstOrDefault(c => c.Name == _settings.Value.DefaultConnectionName)
                ?? AvailableConnections.FirstOrDefault();

            // 🔹 Обновляем статус
            UpdateConnectionStatus();
        }

        // 🔹 Обработчик смены подключения (вызывается из View при изменении SelectedConnection)
        partial void OnSelectedConnectionChanged(DatabaseConnectionConfig value)
        {
            if (value == null) return;

            UpdateConnectionStatus();

            // 🔹 Опционально: протестировать подключение при смене
            // TestSelectedConnectionAsync(); // асинхронно, без блокировки UI
        }

        // 🔹 Обновление статуса подключения для отображения
        private void UpdateConnectionStatus()
        {
            if (SelectedConnection == null)
            {
                ConnectionStatus = "🔌 Не выбрано";
                return;
            }

            var name = SelectedConnection.Name;
            var server = ExtractServerName(SelectedConnection.ConnectionString);
            var db = ExtractDatabaseName(SelectedConnection.ConnectionString);

            ConnectionStatus = !string.IsNullOrEmpty(server)
                ? $"🟢 {name} ({server}/{db})"
                : $"🟡 {name} (проверка...)";
        }

        // 🔹 Вспомогательные методы для парсинга connection string
        private string? ExtractServerName(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return null;

            var parts = connectionString.Split(';');
            var serverPart = parts.FirstOrDefault(p => p.StartsWith("Server=", StringComparison.OrdinalIgnoreCase)
                                                    || p.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase));

            return serverPart?.Split('=')[1]?.Trim();
        }

        private string? ExtractDatabaseName(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return null;

            var parts = connectionString.Split(';');
            var dbPart = parts.FirstOrDefault(p => p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase)
                                                || p.StartsWith("Initial Catalog=", StringComparison.OrdinalIgnoreCase));

            return dbPart?.Split('=')[1]?.Trim();
        }


        private async Task ExecuteQueryForRecordAsync(DebugRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.GeneratedQuery))
            {
                StatusMessage = "⚠ Нечего выполнять: выберите запись с запросом";
                return;
            }

            if (SelectedConnection == null)
            {
                StatusMessage = "⚠ Выберите подключение к БД";
                return;
            }

            IsExecuting = true;
            StatusMessage = $"⏳ Выполнение: {record.TableName}...";

            try
            {
                // 🔹 Получаем актуальный connection string для выбранного подключения
                var connectionString = string.IsNullOrEmpty(SelectedConnection.ConnectionString)
                    ? _settings.Value.MsSqlConnectionString  // Пустой = использовать дефолтный из настроек
                    : SelectedConnection.ConnectionString;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 🔹 Выполняем запрос через сервис с переданным connection string
                var rows = await _dbService.ExecuteQueryAsync(record.GeneratedQuery, connectionString);

                stopwatch.Stop();

                var result = new QueryExecutionResult
                {
                    SourceRecord = record,
                    ExecutedQuery = record.GeneratedQuery,
                    Rows = rows,
                    Status = QueryExecutionStatus.Success,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };

                ExecutionResults.Add(new QueryResultDisplay(result));
                StatusMessage = $"✅ {record.TableName}: {rows.Count} строк, {result.ExecutionTimeMs} мс";
            }
            catch (Exception ex)
            {
                var errorResult = new QueryExecutionResult
                {
                    SourceRecord = record,
                    ExecutedQuery = record.GeneratedQuery,
                    Error = ex.Message,
                    Status = QueryExecutionStatus.Error,
                    ExecutionTimeMs = 0
                };

                ExecutionResults.Add(new QueryResultDisplay(errorResult));
                StatusMessage = $"❌ Ошибка: {ex.Message}";

                System.Diagnostics.Debug.WriteLine($"[Query Error] {ex}");
            }
            finally
            {
                IsExecuting = false;
            }
        }


        public void LoadRecords(IEnumerable<DebugRecord> records)
        {
            Records.Clear();
            QueryResults.Clear();

            foreach (var r in records)
            {
                Records.Add(r);
                // 🔹 Предварительная генерация запросов
                r.GeneratedQuery = _queryGenerator.GenerateSelectQuery(r);
            }

            StatusMessage = $"📋 Найдено записей: {records.Count()}";
        }


        private void CopySelectedValues()
        {
            var window = System.Windows.Application.Current.Windows
                .OfType<DebugResultWindow>()
                .FirstOrDefault(w => w.DataContext == this);

            if (window?.FindName("RecordsGrid") is not System.Windows.Controls.DataGrid grid || !grid.SelectedCells.Any())
                return;

            var values = grid.SelectedCells
                .Select(cell => GetCellValue(cell))
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            if (values.Any())
            {
                System.Windows.Clipboard.SetText(string.Join("\r\n", values));
                StatusMessage = $"✅ Скопировано {values.Count} значений";
            }
        }

        private string? GetCellValue(System.Windows.Controls.DataGridCellInfo cell)
        {
            if (cell.Item is not DebugRecord record || cell.Column is not System.Windows.Controls.DataGridBoundColumn column)
                return null;

            if (column.Binding is not System.Windows.Data.Binding binding || binding.Path?.Path is not string path)
                return null;

            return path switch
            {
                "Row_Id" => record.RowId.ToString(),
                "TableName" => record.TableName,
                "GeneratedQuery" => record.GeneratedQuery,
                _ => null
            };
        }
    }
}