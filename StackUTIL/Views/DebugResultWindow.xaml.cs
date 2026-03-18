using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DebugInterceptor.Views
{
    public partial class DebugResultWindow : Window
    {
        // 🔹 WinAPI для принудительного получения фокуса
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        public DebugResultWindow()
        {
            InitializeComponent();

            // 🔹 Подписываемся на события для захвата фокуса
            SourceInitialized += DebugResultWindow_SourceInitialized;
            Loaded += DebugResultWindow_Loaded;
        }

        // 🔹 Вызывается когда окно получило HWND (хэндл)
        private void DebugResultWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // Разрешаем этому процессу установить фокус (обход блокировки Windows)
                AllowSetForegroundWindow(Environment.ProcessId);
                // Пытаемся вывести окно на передний план
                SetForegroundWindow(hwnd);
            }
        }

        // 🔹 Вызывается после полной загрузки визуального дерева
        private void DebugResultWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Активируем окно (получает фокус ввода)
            Activate();
            // Фокусируем само окно
            Focus();
            // Передаём фокус на DataGrid для работы клавиатуры (Escape, Ctrl+C и т.д.)
            RecordsGrid?.Focus();
        }

        // 🔹 Дополнительная страховка: фокус при каждой активации окна
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            RecordsGrid?.Focus();
        }

        // 🔹 Ваш существующий код — без изменений 👇

        private void DataGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid grid) return;

            var hit = VisualTreeHelper.HitTest(grid, e.GetPosition(grid));
            if (hit?.VisualHit is DependencyObject dep)
            {
                var cell = FindParent<System.Windows.Controls.DataGridCell>(dep);
                if (cell != null && cell.Column != null && cell.DataContext is DebugInterceptor.Models.DebugRecord record)
                {
                    bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                    if (isCtrl || isShift) return;

                    CopyCellValue(cell, record);
                }
            }
        }

        private void DataGrid_OnSelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
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
                        var preview = value?.Substring(0, Math.Min(40, value.Length));
                        vm.StatusMessage = $"📋 Скопировано: {preview}{(value?.Length > 40 ? "..." : "")}";
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