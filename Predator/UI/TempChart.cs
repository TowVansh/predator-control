using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace Predator
{
    // Rolling CPU/GPU temperature chart (one sample per controller tick) with the Smart limit as a dashed line.
    [SupportedOSPlatform("windows")]
    public sealed class TempChart : Control
    {
        private const int Capacity = 150;            // 5 minutes at a 2 s tick
        private const int MinTemp = 30, MaxTemp = 100;

        private static readonly Color Bg = Color.FromArgb(28, 28, 32);
        private static readonly Color Grid = Color.FromArgb(42, 42, 48);
        private static readonly Color CpuColor = Color.FromArgb(0, 200, 160);
        private static readonly Color GpuColor = Color.FromArgb(120, 150, 255);
        private static readonly Color LimitColor = Color.FromArgb(255, 110, 80);
        private static readonly Color Label = Color.FromArgb(100, 100, 110);

        private readonly Queue<(int Cpu, int Gpu)> _samples = new();

        public int Limit { get; set; } = 93;

        public TempChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Add(int cpu, int gpu)
        {
            _samples.Enqueue((cpu, gpu));
            while (_samples.Count > Capacity) _samples.Dequeue();
            if (Visible) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Bg);

            float Y(int t) => Height - 2 - (Math.Clamp(t, MinTemp, MaxTemp) - MinTemp) / (float)(MaxTemp - MinTemp) * (Height - 4);
            float X(int i) => Width - 1 - (Capacity - 1 - i) * (Width - 2) / (float)(Capacity - 1);

            using (var grid = new Pen(Grid))
            using (var font = new Font("Segoe UI", 7f))
            using (var label = new SolidBrush(Label))
            {
                foreach (int t in new[] { 50, 70, 90 })
                {
                    g.DrawLine(grid, 0, Y(t), Width, Y(t));
                    g.DrawString($"{t}°", font, label, 2, Y(t) - 12);
                }
            }

            using (var limit = new Pen(LimitColor, 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(limit, 0, Y(Limit), Width, Y(Limit));

            if (_samples.Count < 2) return;
            var arr = _samples.ToArray();
            int offset = Capacity - arr.Length;
            DrawSeries(g, arr.Select((s, i) => new PointF(X(i + offset), Y(s.Gpu))).ToArray(), GpuColor);
            DrawSeries(g, arr.Select((s, i) => new PointF(X(i + offset), Y(s.Cpu))).ToArray(), CpuColor);
        }

        private static void DrawSeries(Graphics g, PointF[] pts, Color color)
        {
            using var pen = new Pen(color, 1.6f);
            g.DrawLines(pen, pts);
        }
    }
}
