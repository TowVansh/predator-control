using System.Drawing;

namespace Predator
{
    // Temperature (°C) -> fan speed (%) curves. The EC has no curve support we can write,
    // so Custom mode samples temps every tick and pushes the interpolated speed.
    public static class FanCurve
    {
        // Measured on the PH16-71 (both fans alike): 0-15% = ~1730 rpm floor (fans never stop), 25% ~1880,
        // 40% ~2950, 60% ~4270, 80% ~5420, 100% ~6490. So the curves start at the floor and only spend
        // range where it changes something.

        /// <summary>CPU: idles at 60-65°C with the current paste, so stay at the floor until then;
        /// full speed at 91°C, where the firmware parks a boosting CPU anyway.</summary>
        public static List<Point> DefaultCpu() => new()
        {
            new(30, 0), new(60, 15), new(70, 30), new(78, 45), new(84, 65), new(88, 85), new(91, 100), new(100, 100)
        };

        /// <summary>GPU: poor paste here too (90°C at only ~36 W), and GPU heat builds steadily, so ramp early
        /// and hit full speed well before NVIDIA's 87°C throttle point.</summary>
        public static List<Point> DefaultGpu() => new()
        {
            new(30, 0), new(50, 15), new(60, 40), new(67, 60), new(73, 80), new(78, 95), new(81, 100), new(100, 100)
        };

        public static int Interpolate(List<Point> curve, int temp)
        {
            if (curve.Count == 0) return 100;
            if (temp <= curve[0].X) return curve[0].Y;
            for (int i = 0; i < curve.Count - 1; i++)
            {
                var (a, b) = (curve[i], curve[i + 1]);
                if (temp <= b.X)
                    return b.X == a.X ? b.Y : (int)Math.Round(a.Y + (temp - a.X) * (b.Y - a.Y) / (double)(b.X - a.X));
            }
            return curve[^1].Y;
        }

        /// <summary>Parses the old app's "x,y;x,y" registry format.</summary>
        public static List<Point>? Parse(string? data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            var pts = new List<Point>();
            foreach (var pair in data.Split(';'))
            {
                var parts = pair.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                    pts.Add(new Point(x, y));
            }
            return pts.Count >= 2 ? FanCurveGraph.Normalize(pts) : null;
        }
    }
}
