using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DebugInterceptor.Views
{
    public partial class DebugResultWindow : Window
    {
        public DebugResultWindow()
        {
            InitializeComponent();
        }

        private void DataGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid grid) return;

            // Определяем, по какой ячейке кликнули
            var hit = VisualTreeHelper.HitTest(grid, e.GetPosition(grid));
            if (hit?.VisualHit is DependencyObject dep)
            {
                var cell = FindParent<System.Windows.Controls.DataGridCell>(dep);
                if (cell != null && cell.Column != null && cell.DataContext is DebugInterceptor.Models.DebugRecord record)
                {
                    // Проверяем модификаторы
                    bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                    // Если нажат Ctrl или Shift — просто выделяем (стандартное поведение)
                    if (isCtrl || isShift) return;

                    // Одиночный клик — копируем значение ячейки
                    CopyCellValue(cell, record);
                }
            }
        }

        private void DataGrid_OnSelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            // Обновляем статус при изменении выделения
            if (DataContext is DebugInterceptor.ViewModels.DebugResultViewModel vm &&
                sender is System.Windows.Controls.DataGrid grid)
            {
                var count = grid.SelectedCells.Count;
                if (count > 1)
                {
                    vm.StatusMessage = $"📋 Выделено ячеек: {count} (нажмите кнопку для копирования)";
                }
            }
        }

        private void CopyCellValue(System.Windows.Controls.DataGridCell cell, DebugInterceptor.Models.DebugRecord record)
        {
            if (cell.Column is DataGridBoundColumn column &&
                column.Binding is System.Windows.Data.Binding binding &&
                binding.Path?.Path is string path)
            {
                var value = path switch
                {
                    "RowId" => record.RowId.ToString(),
                    "TableName" => record.TableName,
                    "GeneratedQuery" => record.GeneratedQuery,
                    _ => null
                };

                if (!string.IsNullOrEmpty(value))
                {
                    System.Windows.Clipboard.SetText(value);
                    if (DataContext is DebugInterceptor.ViewModels.DebugResultViewModel vm)
                    {
                        vm.StatusMessage = $"📋 Скопировано: {value?.Substring(0, Math.Min(40, value.Length))}...";
                    }
                }
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T t ? t : FindParent<T>(parent);
        }
    }
}