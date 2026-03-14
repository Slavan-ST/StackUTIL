using System.Drawing;
using System.Drawing.Imaging;

namespace DebugInterceptor.Services
{
    public class ScreenCaptureService
    {
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