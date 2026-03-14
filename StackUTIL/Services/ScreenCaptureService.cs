using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace DebugInterceptor.Services
{
    public class ScreenCaptureService
    {
        // ═══════════════════════════════════════════════════════
        // WinAPI импорты
        // ═══════════════════════════════════════════════════════

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, System.IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(System.IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(System.IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(System.IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern System.IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(System.IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(System.IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(System.IntPtr hWndParent, EnumWindowsProc lpEnumFunc, System.IntPtr lParam);

        private delegate bool EnumWindowsProc(System.IntPtr hWnd, System.IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
            public Rectangle ToRectangle() => new Rectangle(Left, Top, Width, Height);
        }

        // ═══════════════════════════════════════════════════════
        // Модель информации об окне
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Информация об окне для поиска и захвата
        /// </summary>
        public class WindowInfo
        {
            public System.IntPtr Hwnd { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ClassName { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public Rectangle Bounds { get; set; }
        }

        // ═══════════════════════════════════════════════════════
        // Поиск окон
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Находит окно по частичному совпадению заголовка
        /// </summary>
        public System.IntPtr? FindWindowByTitle(string titleContains, bool foregroundOnly = false)
        {
            System.IntPtr? foundHwnd = null;
            var searchLower = titleContains.ToLowerInvariant();

            EnumWindows((hWnd, lParam) =>
            {
                if (foregroundOnly && hWnd != GetForegroundWindow())
                    return true;

                var length = GetWindowTextLength(hWnd);
                if (length == 0) return true;

                var buffer = new StringBuilder(length + 1);
                GetWindowText(hWnd, buffer, buffer.Capacity);
                var windowTitle = buffer.ToString();

                if (windowTitle.ToLowerInvariant().Contains(searchLower))
                {
                    foundHwnd = hWnd;
                    return false;
                }
                return true;
            }, System.IntPtr.Zero);

            return foundHwnd;
        }

        /// <summary>
        /// Находит окно по тексту ВНУТРИ него (для окон без заголовка, тултипов)
        /// </summary>
        public WindowInfo? FindWindowByText(string textContains, bool foregroundOnly = false)
        {
            var searchLower = textContains.ToLowerInvariant();
            WindowInfo? foundWindow = null;

            EnumWindows((hWnd, lParam) =>
            {
                if (foregroundOnly && hWnd != GetForegroundWindow())
                    return true;

                var info = GetWindowInfoInternal(hWnd);
                if (info == null) return true;

                // Проверяем текст в окне
                if (info.Text.ToLowerInvariant().Contains(searchLower))
                {
                    foundWindow = info;
                    return false;
                }

                // Проверяем дочерние элементы (для сложных контролов)
                EnumChildWindows(hWnd, (childHwnd, childLParam) =>
                {
                    var childInfo = GetWindowInfoInternal(childHwnd);
                    if (childInfo != null &&
                        childInfo.Text.ToLowerInvariant().Contains(searchLower))
                    {
                        foundWindow = childInfo;
                        return false;
                    }
                    return true;
                }, System.IntPtr.Zero);

                return foundWindow == null;
            }, System.IntPtr.Zero);

            return foundWindow;
        }

        /// <summary>
        /// Возвращает последнее активное окно (для тултипов и всплывающих окон)
        /// </summary>
        public WindowInfo? GetLastActiveWindow()
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == System.IntPtr.Zero) return null;
            return GetWindowInfoInternal(hWnd);
        }

        /// <summary>
        /// Внутренний метод получения полной информации об окне
        /// </summary>
        private WindowInfo? GetWindowInfoInternal(System.IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd)) return null;

            var bounds = GetWindowBounds(hWnd);
            if (bounds == null || bounds.Value.Width < 50 || bounds.Value.Height < 30)
                return null; // Пропускаем слишком мелкие элементы

            // Заголовок
            var titleLen = GetWindowTextLength(hWnd);
            var title = titleLen > 0 ? GetWindowTextString(hWnd, titleLen) : string.Empty;

            // Класс окна
            var classNameBuffer = new StringBuilder(256);
            GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity);
            var className = classNameBuffer.ToString();

            // Текст (для статических контролов)
            var text = string.IsNullOrEmpty(title) ? GetWindowTextString(hWnd, 1024) : title;

            return new WindowInfo
            {
                Hwnd = hWnd,
                Title = title,
                ClassName = className,
                Text = text,
                Bounds = bounds.Value
            };
        }

        private string GetWindowTextString(System.IntPtr hWnd, int maxLength)
        {
            var buffer = new StringBuilder(maxLength);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        /// <summary>
        /// Получает прямоугольник окна в экранных координатах
        /// </summary>
        public Rectangle? GetWindowBounds(System.IntPtr hWnd)
        {
            if (GetWindowRect(hWnd, out var rect))
                return rect.ToRectangle();
            return null;
        }

        // ═══════════════════════════════════════════════════════
        // Захват экрана и окон
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Делает скриншот всего первичного экрана
        /// </summary>
        public Bitmap CaptureFullScreen()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            var bounds = screen.Bounds;
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }
            return bitmap;
        }

        /// <summary>
        /// Делает скриншот указанного окна по хэндлу
        /// </summary>
        public Bitmap? CaptureWindow(System.IntPtr hWnd)
        {
            var bounds = GetWindowBounds(hWnd);
            if (bounds == null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0)
                return null;

            var bitmap = new Bitmap(bounds.Value.Width, bounds.Value.Height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.Value.X, bounds.Value.Y,
                    0, 0,
                    bounds.Value.Size,
                    CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        /// <summary>
        /// Делает скриншот указанной области (в координатах экрана)
        /// </summary>
        public Bitmap CaptureRegion(System.Windows.Rect region)
        {
            var width = (int)region.Width;
            var height = (int)region.Height;
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    (int)region.X, (int)region.Y,
                    0, 0,
                    new Size(width, height));
            }
            return bitmap;
        }

        /// <summary>
        /// Ищет и захватывает окно по заголовку
        /// </summary>
        public Bitmap? CaptureWindowByTitle(string titleContains, bool foregroundOnly = false)
        {
            var hWnd = FindWindowByTitle(titleContains, foregroundOnly);
            if (hWnd == null) return null;
            return CaptureWindow(hWnd.Value);
        }

        /// <summary>
        /// Ищет и захватывает окно по тексту внутри (для тултипов)
        /// </summary>
        public Bitmap? CaptureWindowByText(string textContains, bool foregroundOnly = false)
        {
            var windowInfo = FindWindowByText(textContains, foregroundOnly);
            if (windowInfo == null) return null;
            return CaptureWindow(windowInfo.Hwnd);
        }

        /// <summary>
        /// Захватывает последнее активное окно (универсальный метод для тултипов)
        /// </summary>
        public Bitmap? CaptureLastActiveWindow()
        {
            var windowInfo = GetLastActiveWindow();
            if (windowInfo == null) return null;
            return CaptureWindow(windowInfo.Hwnd);
        }
    }
}