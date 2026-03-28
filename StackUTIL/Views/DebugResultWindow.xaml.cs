using DebugInterceptor.Models;
using DebugInterceptor.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DebugInterceptor.Views
{
    public partial class DebugResultWindow : Window
    {
        // 🔹 WinAPI для работы с фокусом
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetFocus(IntPtr hWnd);

        // 🔹 Kernel32 для получения ID текущего потока
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        // 🔹 Поле для сохранения исходного фокуса
        private IntPtr _originalForegroundWindow;

        public DebugResultWindow()
        {
            InitializeComponent();

            // 🔹 Запоминаем активное окно ДО показа нашего
            _originalForegroundWindow = GetForegroundWindow();

            SourceInitialized += DebugResultWindow_SourceInitialized;
            Loaded += DebugResultWindow_Loaded;
            Closed += DebugResultWindow_Closed;
        }

        private void DebugResultWindow_Closed(object? sender, EventArgs e)
        {
            // 🔹 Используем Dispatcher для выполнения после полного закрытия
            Dispatcher.BeginInvoke(new Action(RestoreFocusToOriginalWindow),
                DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Восстанавливает фокус на исходное окно
        /// </summary>
        private void RestoreFocusToOriginalWindow()
        {
            try
            {
                if (_originalForegroundWindow != IntPtr.Zero &&
                    IsWindowVisible(_originalForegroundWindow))
                {
                    // 🔹 Восстанавливаем окно если свёрнуто
                    ShowWindow(_originalForegroundWindow, SW_RESTORE);

                    // 🔹 Получаем ID потоков ПРАВИЛЬНО
                    uint foreThread = GetWindowThreadProcessId(_originalForegroundWindow, out _);
                    uint appThread = GetCurrentThreadId(); // ✅ Правильный способ!

                    if (foreThread != 0 && appThread != 0 && foreThread != appThread)
                    {
                        AttachThreadInput(appThread, foreThread, true);
                        SetForegroundWindow(_originalForegroundWindow);
                        SetFocus(_originalForegroundWindow); // 🔹 Дополнительно ставим фокус ввода
                        AttachThreadInput(appThread, foreThread, false);
                    }
                    else
                    {
                        SetForegroundWindow(_originalForegroundWindow);
                        SetFocus(_originalForegroundWindow);
                    }
                }
            }
            catch
            {
                // Игнорируем
            }
        }

        private void DebugResultWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                AllowSetForegroundWindow(Environment.ProcessId);
                ForceActivateWindow(hwnd);
            }
        }

        private void DebugResultWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ForceActivateWindow(hwnd);
            }

            Activate();
            Focus();
            RecordsGrid?.Focus();
        }

        private void ForceActivateWindow(IntPtr hwnd)
        {
            try
            {
                ShowWindow(hwnd, SW_SHOW);

                var foregroundWnd = GetForegroundWindow();
                if (foregroundWnd == hwnd)
                    return;

                uint foreThread = GetWindowThreadProcessId(foregroundWnd, out _);
                uint appThread = GetCurrentThreadId(); // ✅ Исправлено здесь тоже

                if (foreThread != 0 && appThread != 0 && foreThread != appThread)
                {
                    AttachThreadInput(appThread, foreThread, true);
                    SetForegroundWindow(hwnd);
                    ShowWindow(hwnd, SW_RESTORE);
                    AttachThreadInput(appThread, foreThread, false);
                }
                else
                {
                    SetForegroundWindow(hwnd);
                }

                Activate();
            }
            catch { }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            RecordsGrid?.Focus();
        }

        // 🔹 Остальной код без изменений...
        private void DataGrid_OnSelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            if (grid.DataContext is not DebugResultViewModel vm) return;

            var firstCell = grid.SelectedCells.FirstOrDefault();
            if (firstCell.Item != null)
            {
                vm.SelectedRecord = firstCell.Item is DebugRecord record ? record : new();
            }
        }

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

        private void ResultDataGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid grid) return;

            var hit = VisualTreeHelper.HitTest(grid, e.GetPosition(grid));
            if (hit?.VisualHit is DependencyObject dep)
            {
                var cell = FindParent<System.Windows.Controls.DataGridCell>(dep);
                if (cell != null && cell.Column != null)
                {
                    bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                    if (isCtrl || isShift) return;
                    CopyResultCellValue(cell);
                }
            }
        }

        private void CopyResultCellValue(System.Windows.Controls.DataGridCell cell)
        {
            if (cell.DataContext is not System.Data.DataRowView rowView)
                return;

            if (cell.Column is not System.Windows.Controls.DataGridTextColumn textColumn)
                return;

            var columnName = textColumn.Header?.ToString();
            if (string.IsNullOrEmpty(columnName))
                return;

            var value = rowView[columnName];
            var text = value == null || value == DBNull.Value ? string.Empty : value.ToString();

            if (!string.IsNullOrEmpty(text))
            {
                System.Windows.Clipboard.SetText(text);
                if (DataContext is DebugResultViewModel vm)
                {
                    var preview = text?.Substring(0, Math.Min(40, text.Length));
                    vm.StatusMessage = $"📋 Скопировано: {preview}{(text?.Length > 40 ? "..." : "")}";
                }
            }
        }

        private void ResultDataGrid_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (sender is DataGrid grid && DataContext is DebugResultViewModel vm)
                {
                    vm.CopySelectedCellsFromGrid(grid);
                    e.Handled = true;
                }
            }
        }
    }
}