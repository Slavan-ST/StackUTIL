using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DebugInterceptor.Models;
using DebugInterceptor.Views;
using System.Collections.ObjectModel;

namespace DebugInterceptor.ViewModels
{
    public partial class DebugResultViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<DebugRecord> _records = new();

        [ObservableProperty]
        private DebugRecord? _selectedRecord;

        [ObservableProperty]
        private string _statusMessage = string.Empty;


        public IRelayCommand CopySelectedQueryCommand { get; }
        public IRelayCommand CloseCommand { get; }

        public DebugResultViewModel()
        {
            CopySelectedQueryCommand = new RelayCommand(CopySelectedQuery, () => SelectedRecord != null);
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

            if (Records.Any())
                SelectedRecord = Records[0];

            StatusMessage = $"Найдено записей: {records.Count()}";
        }

        private void CopySelectedQuery()
        {
            if (SelectedRecord?.GeneratedQuery is string query && !string.IsNullOrEmpty(query))
            {
                System.Windows.Clipboard.SetText(query);
                StatusMessage = "✅ Запрос скопирован в буфер обмена";
            }
        }
    }
}