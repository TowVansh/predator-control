using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Predator
{
    [SupportedOSPlatform("windows")]
    public sealed class MainForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(22, 22, 26);
        private static readonly Color Dim = Color.FromArgb(110, 110, 120);
        private static readonly Color Accent = Color.FromArgb(0, 200, 160);
        private static readonly Color Hot = Color.FromArgb(255, 110, 80);
        private static readonly Font FontHeader = new("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBody = new("Segoe UI", 9.5f);
        private static readonly Font FontBig = new("Segoe UI", 15f, FontStyle.Bold);

        private static readonly (string Label, PowerMode Mode)[] Modes =
        {
            ("Quiet", PowerMode.Quiet), ("Balanced", PowerMode.Balanced),
            ("Perf", PowerMode.Performance), ("Turbo", PowerMode.Turbo)
        };
        private static readonly (string Label, FanMode Mode)[] Fans =
            { ("Auto", FanMode.Auto), ("Max", FanMode.Max), ("Curve", FanMode.Custom) };
        private static readonly LightEffect[] Effects =
            { LightEffect.Off, LightEffect.Static, LightEffect.Breathing, LightEffect.Wave, LightEffect.Rainbow };

        private readonly Controller _c;
        private readonly bool _startHidden;
        private readonly float _scale;
        private bool _exiting;

        private Label _cpuTemp = null!, _gpuTemp = null!, _cpuInfo = null!, _gpuInfo = null!, _summary = null!;
        private PredatorButton _btnSmart = null!, _btnCurve = null!;
        private readonly List<(PredatorButton Btn, PowerMode Mode)> _modeBtns = new();
        private readonly List<(PredatorButton Btn, FanMode Mode)> _fanBtns = new();
        private PredatorDropDown _effect = null!;
        private PredatorButton _btnColor = null!;
        private PredatorSlider _brightness = null!;
        private ListBox _games = null!;
        private PredatorSwitch _startup = null!;

        private readonly NotifyIcon _tray = new();
        private readonly ContextMenuStrip _trayMenu = new();
        private ToolStripMenuItem _traySmart = null!;
        private readonly List<(ToolStripMenuItem Item, PowerMode Mode)> _trayModes = new();

        public MainForm(Controller controller, bool startHidden)
        {
            _c = controller;
            _startHidden = startHidden;
            _scale = DeviceDpi / 96f;

            Text = "Predator Control";
            BackColor = Bg;
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUI();
            BuildTray();
            RefreshControls();
            _c.StatusChanged += OnStatus;
        }

        private int S(int px) => (int)Math.Round(px * _scale);

        #region Layout

        private void BuildUI()
        {
            int w = S(460), pad = S(20), gap = S(6), btnH = S(34), inner = w - pad * 2;
            int y = S(16);

            // Live stats
            int half = inner / 2;
            AddLabel("CPU", pad, y, FontHeader, Dim);
            AddLabel("GPU", pad + half, y, FontHeader, Dim);
            y += S(18);
            _cpuTemp = AddLabel("--°C", pad, y, FontBig, Color.White);
            _gpuTemp = AddLabel("--°C", pad + half, y, FontBig, Color.White);
            y += S(32);
            _cpuInfo = AddLabel("", pad, y, FontBody, Dim);
            _gpuInfo = AddLabel("", pad + half, y, FontBody, Dim);
            y += S(24);
            _summary = AddLabel("", pad, y, FontBody, Accent);
            _summary.AutoSize = false;
            _summary.Size = new Size(inner, S(20));
            y += S(32);

            // Mode
            AddLabel("MODE", pad, y, FontHeader, Dim);
            y += S(20);
            int modeW = (inner - gap * 4) / 5;
            _btnSmart = AddButton("Smart", pad, y, modeW, btnH);
            _btnSmart.Click += (_, _) => { _c.SetSmart(); RefreshControls(); };
            for (int i = 0; i < Modes.Length; i++)
            {
                var (label, mode) = Modes[i];
                var b = AddButton(label, pad + (modeW + gap) * (i + 1), y, modeW, btnH);
                b.Click += (_, _) => { _c.SetManualPower(mode); RefreshControls(); };
                _modeBtns.Add((b, mode));
            }
            y += btnH + S(6);
            AddLabel($"Smart runs mode, CPU boost and fans itself · CPU kept ≤ {_c.Settings.CpuTempLimit}°C",
                pad, y, FontBody, Dim);
            y += S(34);

            // Fans (manual only)
            AddLabel("FANS  (manual modes)", pad, y, FontHeader, Dim);
            y += S(20);
            int fanW = (inner - gap * 3) / 4;
            for (int i = 0; i < Fans.Length; i++)
            {
                var (label, mode) = Fans[i];
                var b = AddButton(label, pad + (fanW + gap) * i, y, fanW, btnH);
                b.Click += (_, _) => { _c.SetManualFan(mode); RefreshControls(); };
                _fanBtns.Add((b, mode));
            }
            _btnCurve = AddButton("Edit curve", pad + (fanW + gap) * 3, y, fanW, btnH);
            _btnCurve.Click += (_, _) => EditCurves();
            y += btnH + S(22);

            // Lighting
            if (_c.HasKeyboard)
            {
                int third = (inner - gap * 2) / 3;
                AddLabel("KEYBOARD LIGHTING", pad, y, FontHeader, Dim);
                AddLabel("BRIGHTNESS", pad + (third + gap) * 2, y, FontHeader, Dim);
                y += S(20);
                _effect = new PredatorDropDown { Location = new Point(pad, y), Size = new Size(third, btnH) };
                foreach (var e in Effects) _effect.Items.Add(e.ToString());
                Controls.Add(_effect);
                _effect.SelectedIndexChanged += (_, _) => PushLighting();

                _btnColor = AddButton("Color", pad + third + gap, y, third, btnH);
                _btnColor.Click += (_, _) => PickColor();

                _brightness = new PredatorSlider
                {
                    Location = new Point(pad + (third + gap) * 2, y + (btnH - S(28)) / 2),
                    Size = new Size(third, S(28)), Minimum = 0, Maximum = 100
                };
                Controls.Add(_brightness);
                _brightness.ValueCommitted += (_, _) => PushLighting();
                y += btnH + S(22);
            }

            // Games
            AddLabel("GAMES  (always treated as gaming; others are detected from load)", pad, y, FontHeader, Dim);
            y += S(20);
            _games = new ListBox
            {
                Location = new Point(pad, y), Size = new Size(inner - S(110), S(78)),
                BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle, Font = FontBody, IntegralHeight = false
            };
            Controls.Add(_games);
            var add = AddButton("Add .exe", pad + inner - S(104), y, S(104), btnH);
            add.Click += (_, _) => AddGame();
            var remove = AddButton("Remove", pad + inner - S(104), y + btnH + S(8), S(104), btnH);
            remove.Click += (_, _) => RemoveGame();
            y += S(78) + S(20);

            // Startup
            var lbl = AddLabel("Start with Windows", pad, y + S(5), FontBody, Color.Gainsboro);
            _startup = new PredatorSwitch { Location = new Point(pad + inner - S(48), y), Size = new Size(S(48), S(30)) };
            Controls.Add(_startup);
            _startup.CheckedChanged += (_, _) =>
            {
                if (Startup.IsEnabled() != _startup.Checked && !Startup.SetEnabled(_startup.Checked))
                    _startup.Checked = !_startup.Checked;
            };
            y += S(30) + S(16);

            ClientSize = new Size(w, y);
        }

        private Label AddLabel(string text, int x, int y, Font font, Color color)
        {
            var l = new Label { Text = text, Location = new Point(x, y), Font = font, ForeColor = color, AutoSize = true, BackColor = Color.Transparent };
            Controls.Add(l);
            return l;
        }

        private PredatorButton AddButton(string text, int x, int y, int w, int h)
        {
            var b = new PredatorButton { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            Controls.Add(b);
            return b;
        }

        #endregion

        #region State -> UI

        private void RefreshControls()
        {
            var s = _c.Settings;
            _btnSmart.IsActive = s.Smart;
            foreach (var (b, m) in _modeBtns) b.IsActive = !s.Smart && s.Power == m;
            foreach (var (b, m) in _fanBtns) { b.IsActive = !s.Smart && s.Fan == m; b.Enabled = !s.Smart; }
            _btnCurve.Enabled = !s.Smart;

            if (_c.HasKeyboard)
            {
                _effect.SelectedIndex = Array.IndexOf(Effects, s.Light);
                _brightness.Value = s.LightBrightness;
                _btnColor.CustomActiveColor = s.LightColor;
                _btnColor.IsActive = s.Light is LightEffect.Static or LightEffect.Breathing;
            }

            _games.Items.Clear();
            foreach (var g in s.Games) _games.Items.Add(g);

            _traySmart.Checked = s.Smart;
            foreach (var (item, m) in _trayModes) item.Checked = !s.Smart && s.Power == m;
        }

        private void OnStatus()
        {
            var st = _c.Status;
            _cpuTemp.Text = $"{st.CpuTemp}°C";
            _gpuTemp.Text = $"{st.GpuTemp}°C";
            _cpuTemp.ForeColor = st.CpuTemp >= _c.Settings.CpuTempLimit ? Hot : Color.White;
            _gpuTemp.ForeColor = st.GpuTemp >= SmartGovernor.GpuTempLimit ? Hot : Color.White;
            _cpuInfo.Text = $"{st.CpuLoad:0}% load · {st.CpuFanRpm} rpm";
            _gpuInfo.Text = $"{st.GpuLoad:0}% load · {st.GpuFanRpm} rpm";
            _summary.Text = st.Summary + (st.OnAc ? "" : " · on battery");

            string tip = $"CPU {st.CpuTemp}°C · GPU {st.GpuTemp}°C\n{SmartGovernor.Label(st.Power)}";
            _tray.Text = tip.Length > 63 ? tip[..63] : tip;

            // Clock capping means boost-off alone couldn't hold the limit: usually dust, a blocked vent,
            // or a soft surface. Worth telling the user, but at most every 30 minutes.
            if (st.MaxState < 100 && DateTime.Now - _lastHeatWarning > TimeSpan.FromMinutes(30))
            {
                _lastHeatWarning = DateTime.Now;
                _tray.ShowBalloonTip(8000, "Running hot",
                    $"CPU at {st.CpuTemp}°C even with boost off, so clocks are capped to {st.MaxState}%. " +
                    "Check the vents aren't blocked.", ToolTipIcon.Warning);
            }
        }

        private DateTime _lastHeatWarning = DateTime.MinValue;

        #endregion

        #region Actions

        private void PushLighting()
        {
            if (_effect.SelectedIndex < 0) return;
            var c = _c.Settings;
            _c.SetLighting(Effects[_effect.SelectedIndex], c.LightColor, _brightness.Value);
            _btnColor.IsActive = c.Light is LightEffect.Static or LightEffect.Breathing;
        }

        private void PickColor()
        {
            using var dlg = new ColorDialog { FullOpen = true, Color = _c.Settings.LightColor };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var effect = _c.Settings.Light is LightEffect.Off or LightEffect.Wave or LightEffect.Rainbow
                ? LightEffect.Static : _c.Settings.Light;
            _c.SetLighting(effect, dlg.Color, _c.Settings.LightBrightness);
            RefreshControls();
        }

        private void EditCurves()
        {
            using var dlg = new CurveDialog(_c.Settings.CpuCurve, _c.Settings.GpuCurve, _c.Status.CpuTemp, _c.Status.GpuTemp);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _c.SetCurves(dlg.CpuCurve, dlg.GpuCurve);
                _c.SetManualFan(FanMode.Custom);
                RefreshControls();
            }
        }

        private void AddGame()
        {
            using var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick a game executable" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _c.SetGames(_c.Settings.Games.Append(Path.GetFileName(dlg.FileName)));
            RefreshControls();
        }

        private void RemoveGame()
        {
            if (_games.SelectedItem is not string exe) return;
            _c.SetGames(_c.Settings.Games.Where(g => !g.Equals(exe, StringComparison.OrdinalIgnoreCase)));
            RefreshControls();
        }

        #endregion

        #region Tray + window lifetime

        private void BuildTray()
        {
            _traySmart = new ToolStripMenuItem("Smart", null, (_, _) => { _c.SetSmart(); RefreshControls(); });
            _trayMenu.Items.Add(_traySmart);
            foreach (var (label, mode) in Modes)
            {
                var item = new ToolStripMenuItem(SmartGovernor.Label(mode), null, (_, _) => { _c.SetManualPower(mode); RefreshControls(); });
                _trayModes.Add((item, mode));
                _trayMenu.Items.Add(item);
            }
            _trayMenu.Items.Add(new ToolStripSeparator());
            if (_c.HasKeyboard)
            {
                var smartLights = new ToolStripMenuItem("Smart lighting (idle-off, dim on battery)") { Checked = _c.Settings.SmartLighting, CheckOnClick = true };
                smartLights.CheckedChanged += (_, _) => _c.SetSmartLighting(smartLights.Checked);
                _trayMenu.Items.Add(smartLights);
                _trayMenu.Items.Add("Lights on/off", null, (_, _) =>
                {
                    var s = _c.Settings;
                    _c.SetLighting(s.Light == LightEffect.Off ? LightEffect.Static : LightEffect.Off, s.LightColor, s.LightBrightness);
                    RefreshControls();
                });
            }
            _trayMenu.Items.Add("Open", null, (_, _) => ShowWindow());
            _trayMenu.Items.Add("Exit", null, (_, _) => { _exiting = true; Close(); });

            _tray.Icon = Icon ?? SystemIcons.Application;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Text = "Predator Control";
            _tray.Visible = true;
            _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        }

        protected override void SetVisibleCore(bool value)
        {
            // Start straight in the tray without flashing the window.
            if (_startHidden && !IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Task.Run(Startup.IsEnabled).ContinueWith(t =>
            {
                if (!IsDisposed) BeginInvoke(() => _startup.Checked = t.Result);
            });
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_SHOWME) ShowWindow();
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // The close button hides to tray; only the tray's Exit (or Windows shutting down) quits.
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _c.StatusChanged -= OnStatus;
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosing(e);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        #endregion
    }
}
