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
        [ObservableProperty]
        private ObservableCollection<DebugRecord> _records = new();

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        // 🔹 Результаты выполнения запросов
        [ObservableProperty]
        private ObservableCollection<QueryExecutionResult> _queryResults = new();

        [ObservableProperty]
        private bool _isExecuting = false;

        // 🔹 Сервисы
        private readonly IDatabaseService _dbService;
        private readonly IQueryGenerator _queryGenerator;

        public IRelayCommand CopySelectedCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public IRelayCommand ExecuteSelectedCommand { get; }
        public IRelayCommand ExecuteAllCommand { get; }
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

            // 🔹 Новые команды
            ExecuteSelectedCommand = new RelayCommand(ExecuteSelectedQueries, () => !IsExecuting);
            ExecuteAllCommand = new RelayCommand(ExecuteAllQueries, () => !IsExecuting);
            CopyResultCommand = new RelayCommand<QueryExecutionResult>(CopyResultValue);
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

        // 🔹 Выполнение выбранных запросов
        private async void ExecuteSelectedQueries()
        {
            var window = System.Windows.Application.Current.Windows
                .OfType<DebugResultWindow>()
                .FirstOrDefault(w => w.DataContext == this);

            if (window?.FindName("RecordsGrid") is not System.Windows.Controls.DataGrid grid)
                return;

            var selectedRecords = grid.SelectedItems.Cast<DebugRecord>().ToList();
            if (!selectedRecords.Any())
            {
                StatusMessage = "⚠ Выделите записи для выполнения";
                return;
            }

            await ExecuteQueriesAsync(selectedRecords);
        }

        // 🔹 Выполнение всех запросов
        private async void ExecuteAllQueries()
        {
            if (!Records.Any())
            {
                StatusMessage = "⚠ Нет записей для выполнения";
                return;
            }
            await ExecuteQueriesAsync(Records.ToList());
        }

        // 🔹 Общая логика выполнения
        private async Task ExecuteQueriesAsync(List<DebugRecord> records)
        {
            IsExecuting = true;
            StatusMessage = $"⏳ Выполнение {records.Count} запросов...";
            QueryResults.Clear();

            var sw = Stopwatch.StartNew();

            foreach (var record in records)
            {
                var result = new QueryExecutionResult
                {
                    SourceRecord = record,
                    ExecutedQuery = record.GeneratedQuery,
                    Status = QueryExecutionStatus.Running
                };
                QueryResults.Add(result);

                try
                {
                    var rows = await _dbService.ExecuteQueryAsync(record.GeneratedQuery);
                    result.Rows = rows;
                    result.Status = QueryExecutionStatus.Success;
                    result.ExecutionTimeMs = sw.ElapsedMilliseconds;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    result.Status = QueryExecutionStatus.Error;
                    result.ExecutionTimeMs = sw.ElapsedMilliseconds;
                }
            }

            sw.Stop();
            IsExecuting = false;

            var successCount = QueryResults.Count(r => r.Status == QueryExecutionStatus.Success);
            StatusMessage = $"✅ Готово: {successCount}/{records.Count} за {sw.ElapsedMilliseconds} мс";
        }

        // 🔹 Копирование значения из результата
        private void CopyResultValue(QueryExecutionResult? result)
        {
            if (result?.Rows?.Any() != true) return;

            var firstRow = result.Rows.First();
            var values = firstRow.Values.Select(v => v?.ToString()).Where(v => !string.IsNullOrEmpty(v));
            var text = string.Join(" | ", values);

            System.Windows.Clipboard.SetText(text);
            StatusMessage = $"📋 Скопировано: {text[..Math.Min(50, text.Length)]}...";
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