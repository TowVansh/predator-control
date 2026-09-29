using System.Runtime.Versioning;

namespace Predator
{
    // Two draggable temperature -> fan speed curves (CPU and GPU) with Save / Reset.
    [SupportedOSPlatform("windows")]
    public sealed class CurveDialog : Form
    {
        private readonly FanCurveGraph _cpu, _gpu;

        public List<Point> CpuCurve => _cpu.Points;
        public List<Point> GpuCurve => _gpu.Points;

        public CurveDialog(List<Point> cpu, List<Point> gpu, int cpuTemp, int gpuTemp)
        {
            float scale = DeviceDpi / 96f;
            int S(int px) => (int)Math.Round(px * scale);

            Text = "Fan curves";
            BackColor = Color.FromArgb(22, 22, 26);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            int pad = S(16), w = S(440), h = S(220);
            _cpu = new FanCurveGraph { Location = new Point(pad, pad), Size = new Size(w, h), FanLabel = "CPU FAN", Points = new List<Point>(cpu), CurrentTemp = cpuTemp };
            _gpu = new FanCurveGraph { Location = new Point(pad, pad * 2 + h), Size = new Size(w, h), FanLabel = "GPU FAN", Points = new List<Point>(gpu), CurrentTemp = gpuTemp, CurveColor = Color.FromArgb(120, 200, 80) };
            Controls.Add(_cpu);
            Controls.Add(_gpu);

            int by = pad * 3 + h * 2, bw = S(110), bh = S(34);
            var reset = new PredatorButton { Text = "Reset", Location = new Point(pad, by), Size = new Size(bw, bh) };
            reset.Click += (_, _) => { _cpu.Points = FanCurve.Default(); _gpu.Points = FanCurve.Default(); };
            var save = new PredatorButton { Text = "Save", Location = new Point(pad + w - bw, by), Size = new Size(bw, bh), IsActive = true };
            save.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(reset);
            Controls.Add(save);

            ClientSize = new Size(w + pad * 2, by + bh + pad);
        }
    }
}
