using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DebugInterceptor.Models;
using DebugInterceptor.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace DebugInterceptor.ViewModels
{
    public partial class DebugResultViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<DebugRecord> _records = new();


        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public IRelayCommand CopySelectedCommand { get; }
        public IRelayCommand CloseCommand { get; }

        public DebugResultViewModel()
        {
            CopySelectedCommand = new RelayCommand(CopySelectedValues);
            CloseCommand = new RelayCommand(() =>
                System.Windows.Application.Current.Windows
                    .OfType<DebugResultWindow>()
                    .FirstOrDefault(w => w.DataContext == this)?.Close());
        }

        public void LoadRecords(IEnumerable<DebugRecord> records)
        {
            Records.Clear();
            foreach (var r in records)
                Records.Add(r);


            StatusMessage = $"Найдено записей: {records.Count()}";
        }

        private void CopySelectedValues()
        {
            // Получаем доступ к DataGrid через окно
            var window = System.Windows.Application.Current.Windows
                .OfType<DebugResultWindow>()
                .FirstOrDefault(w => w.DataContext == this);

            if (window?.FindName("RecordsGrid") is System.Windows.Controls.DataGrid grid && grid.SelectedCells.Any())
            {
                var values = new List<string>();
                foreach (var cell in grid.SelectedCells)
                {
                    if (cell.Item is DebugRecord record && cell.Column is DataGridBoundColumn column &&
                        column.Binding is System.Windows.Data.Binding binding && binding.Path?.Path is string path)
                    {
                        var value = path switch
                        {
                            "RowId" => record.RowId.ToString(),
                            "TableName" => record.TableName,
                            "GeneratedQuery" => record.GeneratedQuery,
                            _ => null
                        };
                        if (!string.IsNullOrEmpty(value))
                            values.Add(value);
                    }
                }

                if (values.Any())
                {
                    System.Windows.Clipboard.SetText(string.Join("\r\n", values));
                    StatusMessage = $"✅ Скопировано {values.Count} значений";
                }
            }
        }
    }
}