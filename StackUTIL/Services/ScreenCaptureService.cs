using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DebugInterceptor.Services
{
    public class ScreenCaptureService
    {
        // ═══════════════════════════════════════════════════════
        // WinAPI для надёжного захвата экрана
        // ═══════════════════════════════════════════════════════
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest,
            int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, CopyPixelOperation rop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// Делает скриншот всего первичного экрана с захватом всех слоёв (окна, оверлеи, прозрачность)
        /// </summary>
        public Bitmap CaptureFullScreen()
        {
            var screen = Screen.PrimaryScreen;
            var bounds = screen!.Bounds;

            IntPtr hdcScreen = GetWindowDC(GetDesktopWindow());
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, bounds.Width, bounds.Height);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            try
            {
                // 🔹 SRCCOPY | CAPTUREBLT — захватывает невидимые/прозрачные слои
                bool success = BitBlt(hdcMem, 0, 0, bounds.Width, bounds.Height,
                    hdcScreen, bounds.X, bounds.Y,
                    CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);

                if (!success)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                var bitmap = Image.FromHbitmap(hBitmap);
                return bitmap;
            }
            finally
            {
                SelectObject(hdcMem, hOld);
                DeleteObject(hBitmap);
                DeleteDC(hdcMem);
                ReleaseDC(GetDesktopWindow(), hdcScreen);
            }
        }
    }
}