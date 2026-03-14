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
    }
}