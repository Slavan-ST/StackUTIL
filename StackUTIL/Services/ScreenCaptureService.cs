using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DebugInterceptor.Services
{
    public class ScreenCaptureService
    {
        // === WinAPI Imports ===
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest,
            int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, CopyPixelOperation rop);

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        /// <summary>Инициализация DPI-осведомлённости — вызвать один раз в Main()</summary>
        public static void InitializeDpiAwareness() => SetProcessDPIAware();

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
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            return CaptureRegion(new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height), hWnd);
        }

        /// <summary>Захват первичного экрана (обратная совместимость)</summary>
        public Bitmap CaptureFullScreen() => CaptureRegion(Screen.PrimaryScreen!.Bounds);

        /// <summary>Универсальный метод захвата области</summary>
        private Bitmap CaptureRegion(Rectangle bounds, IntPtr? windowHandle = null)
        {
            IntPtr hdcSrc = windowHandle.HasValue
                ? GetWindowDC(windowHandle.Value)
                : GetWindowDC(GetDesktopWindow());

            IntPtr hdcMem = CreateCompatibleDC(hdcSrc);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, bounds.Width, bounds.Height);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            try
            {
                int srcX = windowHandle.HasValue ? 0 : bounds.X;
                int srcY = windowHandle.HasValue ? 0 : bounds.Y;

                bool success = BitBlt(hdcMem, 0, 0, bounds.Width, bounds.Height,
                    hdcSrc, srcX, srcY,
                    CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);

                if (!success)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                var bitmap = Image.FromHbitmap(hBitmap);
                bitmap.SetResolution(300, 300); // Высокое DPI для качества
                return bitmap;
            }
            finally
            {
                SelectObject(hdcMem, hOld);
                DeleteObject(hBitmap);
                DeleteDC(hdcMem);

                if (windowHandle.HasValue)
                    ReleaseDC(windowHandle.Value, hdcSrc);
                else
                    ReleaseDC(GetDesktopWindow(), hdcSrc);
            }
        }
    }
}