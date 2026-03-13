// ViewModels/DebugResultViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DebugInterceptor.Models;
using DebugInterceptor.Services;
using DebugInterceptor.Views;
using System.Collections.ObjectModel;
using System.Windows;

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

        private readonly ISqlMonitoringService? _sqlService;
        private readonly SettingsManager<AppSettings>? _settingsManager;

        public IRelayCommand CopySelectedQueryCommand { get; }
        public IRelayCommand ExecuteSelectedQueryCommand { get; }
        public IRelayCommand CloseCommand { get; }

        public DebugResultViewModel(
            ISqlMonitoringService? sqlService = null,
            SettingsManager<AppSettings>? settingsManager = null)
        {
            _sqlService = sqlService;
            _settingsManager = settingsManager;

            CopySelectedQueryCommand = new RelayCommand(CopySelectedQuery, () => SelectedRecord != null);
            ExecuteSelectedQueryCommand = new RelayCommand(ExecuteSelectedQuery, () => SelectedRecord != null && _sqlService != null);
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

        private async void ExecuteSelectedQuery()
        {
            if (SelectedRecord == null || _sqlService == null) return;

            try
            {
                StatusMessage = "⏳ Выполнение запроса...";

                // ИСПРАВЛЕНО: используем ExecuteQueryByTargetAsync
                var targetName = _settingsManager?.Settings.DefaultTargetName ??
                                 _settingsManager?.Settings.GetDefaultTarget()?.Name ??
                                 "Local";

                var result = await _sqlService.ExecuteQueryByTargetAsync(
                    targetName,
                    SelectedRecord.GeneratedQuery);

                if (result.IsSuccess)
                    StatusMessage = $"✅ Выполнено за {result.ExecutionTimeMs} мс, строк: {result.Rows?.Rows.Count ?? 0}";
                else
                    StatusMessage = $"❌ Ошибка: {result.ErrorMessage}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Ошибка: {ex.Message}";
                System.Windows.MessageBox.Show($"Ошибка выполнения запроса:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}