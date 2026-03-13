// Services/HotkeyService.cs
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DebugInterceptor.Services
{
    public class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly IntPtr _hwnd;
        private readonly HwndSource _source;
        private readonly Dictionary<int, Action> _callbacks = new();
        private int _idCounter = 1000;

        // Модификаторы
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_WIN = 0x0008;

        // Виртуальные коды
        public const int VK_F12 = 0x7B;
        public const int VK_F11 = 0x7A;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public HotkeyService(Window owner)
        {
            var helper = new WindowInteropHelper(owner);
            _hwnd = helper.EnsureHandle();
            _source = HwndSource.FromHwnd(_hwnd);
            _source.AddHook(WndProc);
        }

        /// <summary>
        /// Регистрация комбинации: возвращает ID для возможной отмены
        /// </summary>
        public int Register(uint modifiers, uint virtualKey, Action callback)
        {
            var id = _idCounter++;
            if (RegisterHotKey(_hwnd, id, modifiers, virtualKey))
            {
                _callbacks[id] = callback;
                System.Diagnostics.Debug.WriteLine($"✅ Hotkey registered: id={id}, mod={modifiers}, vk={virtualKey}");
                return id;
            }

            var error = Marshal.GetLastWin32Error();
            var msg = $"❌ Не удалось зарегистрировать горячую клавишу (Win32 error {error})";
            System.Diagnostics.Debug.WriteLine(msg);
            System.Windows.MessageBox.Show(msg, "Ошибка регистрации",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

            throw new InvalidOperationException($"{msg}. Возможно, комбинация уже занята.");
        }

        /// <summary>
        /// Удобный метод для регистрации сложных комбинаций
        /// </summary>
        public int RegisterCombo(bool alt, bool shift, bool ctrl, bool win, int virtualKey, Action callback)
        {
            uint modifiers = 0;
            if (alt) modifiers |= MOD_ALT;
            if (shift) modifiers |= MOD_SHIFT;
            if (ctrl) modifiers |= MOD_CONTROL;
            if (win) modifiers |= MOD_WIN;
            return Register(modifiers, (uint)virtualKey, callback);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var id = wParam.ToInt32();
                if (_callbacks.TryGetValue(id, out var callback))
                {
                    // Выполняем в потоке UI
                    System.Windows.Application.Current.Dispatcher.Invoke(callback);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Unregister(int id)
        {
            if (_callbacks.Remove(id))
            {
                UnregisterHotKey(_hwnd, id);
            }
        }

        public void Dispose()
        {
            foreach (var id in _callbacks.Keys.ToList())
                Unregister(id);
            _source?.RemoveHook(WndProc);
        }
    }
}