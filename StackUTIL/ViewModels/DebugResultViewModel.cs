using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DebugInterceptor.Models;
using DebugInterceptor.Services.Database;
using DebugInterceptor.Services.Query;
using DebugInterceptor.Views;
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

        // 🔹 Сервисы
        private readonly IDatabaseService _dbService;
        private readonly IQueryGenerator _queryGenerator;

        public IRelayCommand CopySelectedCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public IRelayCommand<QueryExecutionResult> CopyResultCommand { get; }

        public DebugResultViewModel(
            IDatabaseService dbService,
            IQueryGenerator queryGenerator)
        {
            _dbService = dbService;
            _queryGenerator = queryGenerator;

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
        }

        private async Task ExecuteQueryForRecordAsync(DebugRecord record)
        {
            // 🔹 Дополнительная защита
            if (record == null || string.IsNullOrWhiteSpace(record.GeneratedQuery))
            {
                StatusMessage = "⚠ Нечего выполнять: выберите запись с запросом";
                return;
            }


            IsExecuting = true;
            StatusMessage = $"⏳ Выполнение: {record.TableName}...";

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var rows = await _dbService.ExecuteQueryAsync(record.GeneratedQuery);

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

                // Логирование
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
                "RowId" => record.RowId.ToString(),
                "TableName" => record.TableName,
                "GeneratedQuery" => record.GeneratedQuery,
                _ => null
            };
        }
    }
}