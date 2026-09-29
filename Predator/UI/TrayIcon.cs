using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Predator
{
    // Draws the CPU temperature as the tray icon: accent below the fan threshold, amber near it, red at the limit.
    [SupportedOSPlatform("windows")]
    public static class TrayIcon
    {
        private static readonly Color Cool = Color.FromArgb(0, 200, 160);
        private static readonly Color Warm = Color.FromArgb(255, 190, 60);
        private static readonly Color Hot = Color.FromArgb(255, 90, 70);

        public static Icon Render(int temp, int limit, int size)
        {
            var color = temp >= limit ? Hot : temp >= limit - 8 ? Warm : Cool;
            string text = temp is > 0 and < 100 ? temp.ToString() : "--";

            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                using var bg = new SolidBrush(Color.FromArgb(235, 20, 20, 24));
                using var path = Rounded(new Rectangle(0, 0, size - 1, size - 1), Math.Max(2, size / 6));
                g.FillPath(bg, path);

                // Largest bold font whose two digits still fit the icon.
                float em = size;
                SizeF measured;
                Font font;
                do
                {
                    font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
                    measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
                    if (measured.Width <= size * 0.98f && measured.Height <= size * 1.05f) break;
                    font.Dispose();
                    em -= 0.5f;
                } while (em > 4);

                using (font)
                using (var brush = new SolidBrush(color))
                {
                    float x = (size - measured.Width) / 2f, y = (size - measured.Height) / 2f;
                    g.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
                }
            }

            IntPtr h = bmp.GetHicon();
            try { return (Icon)Icon.FromHandle(h).Clone(); }
            finally { DestroyIcon(h); }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
