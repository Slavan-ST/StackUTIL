using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.ComponentModel;

namespace DebugInterceptor.Services
{
    public class ScreenCaptureService
    {
        // === WinAPI Imports: GDI32 ===
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest,
            int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, CopyPixelOperation rop);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        // === WinAPI Imports: USER32 ===
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(int dpiContext);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        // === Константы ===
        private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        // === Структуры ===
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public int[] bmiColors;
        }

        /// <summary>
        /// Инициализация DPI-осведомлённости — вызвать один раз в начале Main()
        /// </summary>
        public static void InitializeDpiAwareness()
        {
            try
            {
                // Windows 10 1607+ (build 14393)
                if (Environment.OSVersion.Version >= new Version(10, 0, 14393))
                {
                    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
                else
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                // Игнорируем ошибки, если API недоступен
            }
        }

        /// <summary>Захват всех мониторов</summary>
        public Bitmap CaptureAllScreens()
        {
            var bounds = Rectangle.Empty;
            foreach (var screen in Screen.AllScreens)
                bounds = Rectangle.Union(bounds, screen.Bounds);
            return CaptureRegion(bounds);
        }

        /// <summary>Захват активного окна (на любом мониторе)</summary>
        public Bitmap CaptureActiveWindow()
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось получить активное окно");

            if (!GetWindowRect(hWnd, out var rect))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var bounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);

            // Пробуем PrintWindow для лучшего качества окон (особенно WPF/браузеры)
            try
            {
                return CaptureWindowWithPrintWindow(hWnd, bounds);
            }
            catch
            {
                // Fallback на BitBlt если PrintWindow не сработал
                return CaptureRegion(bounds, hWnd);
            }
        }

        /// <summary>Захват первичного экрана (обратная совместимость)</summary>
        public Bitmap CaptureFullScreen() => CaptureRegion(Screen.PrimaryScreen!.Bounds);

        /// <summary>Захват окна через PrintWindow (лучшее качество для сложных окон)</summary>
        private Bitmap CaptureWindowWithPrintWindow(IntPtr hWnd, Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var gfx = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = gfx.GetHdc();
                try
                {
                    // Пробуем с флагом рендеринга полного контента (Windows 8.1+)
                    bool success = PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                    if (!success)
                    {
                        // Fallback без флага
                        success = PrintWindow(hWnd, hdc, 0);
                    }

                    if (!success)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    gfx.ReleaseHdc(hdc);
                }
            }
            return bitmap;
        }

        /// <summary>Универсальный метод захвата области через BitBlt</summary>
        private Bitmap CaptureRegion(Rectangle bounds, IntPtr? windowHandle = null)
        {
            // Создаём Bitmap с явным форматом 32bppArgb для лучшего качества
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

            IntPtr hdcSrc = IntPtr.Zero;
            IntPtr hdcMem = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;

            try
            {
                hdcSrc = windowHandle.HasValue
                    ? GetWindowDC(windowHandle.Value)
                    : GetWindowDC(GetDesktopWindow());

                if (hdcSrc == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                hdcMem = CreateCompatibleDC(hdcSrc);
                if (hdcMem == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                // Используем CreateCompatibleBitmap для совместимости
                // (CreateDIBSection даёт больше контроля, но сложнее в использовании)
                hBitmap = CreateCompatibleBitmap(hdcSrc, bounds.Width, bounds.Height);
                if (hBitmap == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                hOld = SelectObject(hdcMem, hBitmap);
                if (hOld == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                int srcX = windowHandle.HasValue ? 0 : bounds.X;
                int srcY = windowHandle.HasValue ? 0 : bounds.Y;

                bool success = BitBlt(hdcMem, 0, 0, bounds.Width, bounds.Height,
                    hdcSrc, srcX, srcY,
                    CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);

                if (!success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                // Копируем из HBITMAP в управляемый Bitmap
                using (var tempBmp = Image.FromHbitmap(hBitmap))
                {
                    using (var gfx = Graphics.FromImage(bitmap))
                    {
                        gfx.DrawImage(tempBmp, 0, 0, bounds.Width, bounds.Height);
                    }
                }

                return bitmap;
            }
            finally
            {
                // Освобождаем ресурсы в обратном порядке
                if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                    SelectObject(hdcMem, hOld);

                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);

                if (hdcMem != IntPtr.Zero)
                    DeleteDC(hdcMem);

                if (hdcSrc != IntPtr.Zero)
                {
                    if (windowHandle.HasValue)
                        ReleaseDC(windowHandle.Value, hdcSrc);
                    else
                        ReleaseDC(GetDesktopWindow(), hdcSrc);
                }
            }
        }

        /// <summary>
        /// Простой метод захвата через Graphics.CopyFromScreen (альтернатива)
        /// Может не работать с защищённым контентом, но проще в использовании
        /// </summary>
        public Bitmap CaptureRegionSimple(Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var gfx = Graphics.FromImage(bitmap))
            {
                gfx.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }
            return bitmap;
        }
    }
}