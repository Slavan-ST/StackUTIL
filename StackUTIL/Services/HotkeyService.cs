// Services/HotkeyService.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DebugInterceptor.Services
{
    public class HotkeyService : IDisposable
    {
        // ═══════════════════════════════════════════════════════
        // WinAPI для низкоуровневого хука клавиатуры
        // ═══════════════════════════════════════════════════════
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc? _proc;
        private IntPtr _hookID = IntPtr.Zero;

        private readonly Dictionary<KeyCombination, Action> _callbacks = new();
        private readonly object _lock = new();
        private bool _disposed;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern short GetKeyState(int nVirtKey);

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [Flags]
        private enum KeyModifiers : uint
        {
            None = 0,
            Alt = 1,
            Ctrl = 2,
            Shift = 4,
            Win = 8
        }

        private readonly record struct KeyCombination(Keys Key, KeyModifiers Modifiers);

        // ═══════════════════════════════════════════════════════
        // Публичный API
        // ═══════════════════════════════════════════════════════
        public HotkeyService(Window? owner = null)
        {
            // Хук устанавливается при первой регистрации хоткея
        }

        /// <summary>
        /// Регистрирует комбинацию клавиш. 
        /// Хоткей НЕ блокируется — событие передаётся дальше в систему.
        /// </summary>
        public int RegisterCombo(bool alt, bool shift, bool ctrl, bool win, Keys virtualKey, Action callback)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));

            var modifiers = KeyModifiers.None;
            if (alt) modifiers |= KeyModifiers.Alt;
            if (shift) modifiers |= KeyModifiers.Shift;
            if (ctrl) modifiers |= KeyModifiers.Ctrl;
            if (win) modifiers |= KeyModifiers.Win;

            var combo = new KeyCombination(virtualKey, modifiers);

            lock (_lock)
            {
                _callbacks[combo] = callback;

                if (_hookID == IntPtr.Zero)
                {
                    _proc = HookCallback;
                    using var curProcess = Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule;
                    _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc!,
                        GetModuleHandle(curModule?.ModuleName), 0);

                    if (_hookID == IntPtr.Zero)
                        throw new InvalidOperationException("Не удалось установить хук клавиатуры");
                }
            }
            return combo.GetHashCode();
        }

        public void Unregister(int hotkeyId)
        {
            lock (_lock)
            {
                var toRemove = _callbacks.Keys.FirstOrDefault(k => k.GetHashCode() == hotkeyId);
                if (toRemove != default) _callbacks.Remove(toRemove);

                // Если больше нет хоткеев — снимаем хук
                if (_callbacks.Count == 0 && _hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                    _proc = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
                _callbacks.Clear();
                _proc = null;
            }
            _disposed = true;
        }

        // ═══════════════════════════════════════════════════════
        // Обработчик хука
        // ═══════════════════════════════════════════════════════
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var key = (Keys)kbStruct.vkCode;

                var modifiers = KeyModifiers.None;
                if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) modifiers |= KeyModifiers.Shift;
                if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) modifiers |= KeyModifiers.Ctrl;
                if ((GetKeyState(VK_MENU) & 0x8000) != 0) modifiers |= KeyModifiers.Alt;
                if ((GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0)
                    modifiers |= KeyModifiers.Win;

                var combo = new KeyCombination(key, modifiers);

                Action? callback;
                lock (_lock)
                {
                    _callbacks.TryGetValue(combo, out callback);
                }

                // 🔹 ВЫЗЫВАЕМ колбэк, если комбинация совпала
                if (callback != null)
                {
                    // Асинхронно, чтобы не блокировать поток ввода
                    Task.Run(() =>
                    {
                        try { callback(); }
                        catch (Exception ex)
                        {
                            // Логирование через DI было бы лучше, но здесь минималистично
                            Debug.WriteLine($"[HotkeyService] Error: {ex.Message}");
                        }
                    });
                    // 🔹 НЕ возвращаем 1 — пропускаем событие дальше!
                }
            }

            // 🔹 Всегда передаём событие следующему хуку / приложению
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
    }
}