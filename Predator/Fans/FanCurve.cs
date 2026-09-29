using System.Drawing;

namespace Predator
{
    // Temperature (°C) -> fan speed (%) curves. The EC has no curve support we can write,
    // so Custom mode samples temps every tick and pushes the interpolated speed.
    public static class FanCurve
    {
        public static List<Point> Default() => new()
        {
            new(30, 10), new(45, 15), new(55, 30), new(65, 50), new(72, 65), new(80, 80), new(88, 92), new(95, 100)
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
