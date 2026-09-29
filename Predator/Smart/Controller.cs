using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Predator
{
    public readonly record struct Status(
        int CpuTemp, int GpuTemp, int CpuFanRpm, int GpuFanRpm, double CpuLoad, double GpuLoad,
        bool OnAc, PowerMode Power, FanMode Fan, bool Boost, int MaxState, string Summary);

    // Owns the hardware and runs the 2-second loop: read sensors, decide (Smart) or hold (manual), apply.
    // Hardware is only touched when a value actually changes.
    [SupportedOSPlatform("windows")]
    public sealed class Controller : IDisposable
    {
        private const int TickMs = 2000;

        public Settings Settings { get; }
        public Status Status { get; private set; }
        public bool HasKeyboard { get; }
        public event Action? StatusChanged;

        private readonly AcerWmi _wmi = new();
        private readonly LoadMonitor _load = new();
        private readonly SmartGovernor _governor = new();
        private readonly KeyboardEffects? _lights;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = TickMs };

        // Last values written to hardware; null = unknown, forces a write.
        private PowerMode? _appliedPower;
        private FanMode? _appliedFan;
        private bool? _appliedBoost;
        private int? _appliedMaxState;
        private (int Cpu, int Gpu)? _appliedFanSpeed;

        public Controller()
        {
            Settings = Settings.Load();
            _governor.CpuTempLimit = Settings.CpuTempLimit;
            HasKeyboard = KeyboardEffects.IsSupported;
            if (HasKeyboard) _lights = new KeyboardEffects();

            _timer.Tick += (_, _) => Tick();
            _lightsTimer.Tick += (_, _) => LightsWakeTick();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private readonly System.Windows.Forms.Timer _lightsTimer = new() { Interval = 250 };

        public void Start()
        {
            ApplyLighting();
            Tick();
            _timer.Start();
            if (_lights != null) _lightsTimer.Start();
        }

        #region User actions

        public void SetSmart()
        {
            Settings.Smart = true;
            Settings.Save();
            Tick();
        }

        public void SetManualPower(PowerMode mode)
        {
            Settings.Smart = false;
            Settings.Power = mode;
            Settings.Save();
            Tick();
        }

        public void SetManualFan(FanMode mode)
        {
            Settings.Fan = mode;
            Settings.Save();
            Tick();
        }

        public void SetCurves(List<Point> cpu, List<Point> gpu)
        {
            Settings.CpuCurve = cpu;
            Settings.GpuCurve = gpu;
            Settings.Save();
            _appliedFanSpeed = null;
            Tick();
        }

        public void SetLighting(LightEffect effect, Color color, int brightness)
        {
            Settings.Light = effect;
            Settings.LightColor = color;
            Settings.LightBrightness = brightness;
            Settings.Save();
            ApplyLighting();
        }

        public void SetSmartLighting(bool on)
        {
            Settings.SmartLighting = on;
            Settings.Save();
            _lightsIdleOff = false;
            ApplyLighting();
        }

        public void SetGames(IEnumerable<string> exes)
        {
            Settings.Games = exes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Settings.Save();
        }

        #endregion

        private const int LightsIdleOffSeconds = 180;
        private const double BatteryLightScale = 0.4;
        private (LightEffect Effect, Color Color, int Brightness)? _appliedLight;
        private bool _lightsIdleOff;
        private bool _onAc = true;

        private void ApplyLighting() => ApplyLighting(force: true);

        // Effective lighting = the user's choice, adjusted by Smart Lighting (idle-off, dim on battery).
        private void ApplyLighting(bool force)
        {
            if (_lights == null) return;
            var effect = Settings.Light;
            int brightness = Settings.LightBrightness;
            if (Settings.SmartLighting)
            {
                if (_lightsIdleOff) effect = LightEffect.Off;
                else if (!_onAc) brightness = (int)Math.Round(brightness * BatteryLightScale);
            }

            var want = (effect, Settings.LightColor, brightness);
            if (!force && _appliedLight == want) return;
            _appliedLight = want;
            _lights.Apply(effect, want.LightColor.R, want.LightColor.G, want.LightColor.B, brightness);
        }

        // Runs every 250 ms but only does work while lights are idle-off, so a keypress wakes them instantly.
        private void LightsWakeTick()
        {
            if (!_lightsIdleOff) return;
            if (IdleSeconds() < LightsIdleOffSeconds)
            {
                _lightsIdleOff = false;
                ApplyLighting(force: false);
            }
        }

        private void UpdateSmartLighting(bool gaming)
        {
            if (!Settings.SmartLighting || _lights == null) return;
            // Gamepad input doesn't count as input to Windows, so never idle-off while a game runs.
            if (gaming) _lightsIdleOff = false;
            else if (!_lightsIdleOff && IdleSeconds() >= LightsIdleOffSeconds) _lightsIdleOff = true;
            ApplyLighting(force: false);
        }

        private static uint IdleSeconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return 0;
            return ((uint)Environment.TickCount - info.dwTime) / 1000;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        private void Tick()
        {
            try
            {
                int cpuT = _wmi.CpuTemp, gpuT = _wmi.GpuTemp;
                var (cpuLoad, gpuLoad) = _load.Sample();
                var ps = SystemInformation.PowerStatus;
                bool onAc = ps.PowerLineStatus != PowerLineStatus.Offline;
                int battery = ps.BatteryLifePercent is >= 0 and <= 1 ? (int)Math.Round(ps.BatteryLifePercent * 100) : 100;
                _onAc = onAc;

                string? fullscreen = LoadMonitor.FullscreenForegroundExe();
                bool knownGame = KnownGameRunning(fullscreen);
                bool gaming = knownGame;

                PowerMode power; FanMode fan; bool boost; int maxState; string summary;
                if (Settings.Smart)
                {
                    var d = _governor.Update(new Telemetry(cpuT, gpuT, cpuLoad, gpuLoad, onAc, knownGame, fullscreen != null, battery));
                    (power, fan, boost, maxState, summary) = (d.Power, d.Fan, d.Boost, d.MaxState, "Smart: " + d.Reason);
                    gaming = d.Workload == Workload.Gaming;
                }
                else
                {
                    power = Settings.Power;
                    fan = Settings.Fan;
                    boost = power is PowerMode.Performance or PowerMode.Turbo;
                    maxState = 100;
                    summary = $"Manual: {SmartGovernor.Label(power)} · fans {fan}";
                }

                Apply(power, fan, boost, maxState, cpuT, gpuT);
                UpdateSmartLighting(gaming);
                if (summary != Status.Summary)
                    LogDecision($"{summary} | CPU {cpuT}°C {cpuLoad:0}% · GPU {gpuT}°C {gpuLoad:0}% · {(onAc ? "AC" : "battery")}");

                Status = new Status(cpuT, gpuT, _wmi.CpuFanRpm, _wmi.GpuFanRpm, cpuLoad, gpuLoad, onAc,
                    power, fan, boost, maxState, summary);
                StatusChanged?.Invoke();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        // One line per change of decision, so "why did it go Turbo?" has an answer. Rotates at 512 KB.
        private static void LogDecision(string line)
        {
            try
            {
                string path = Path.Combine(Settings.Dir, "smart.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
            catch { }
        }

        private bool KnownGameRunning(string? fullscreenExe)
        {
            if (Settings.Games.Count == 0) return false;
            if (fullscreenExe != null && Settings.Games.Contains(fullscreenExe, StringComparer.OrdinalIgnoreCase)) return true;
            foreach (var exe in Settings.Games)
            {
                var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
                bool running = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (running) return true;
            }
            return false;
        }

        private void Apply(PowerMode power, FanMode fan, bool boost, int maxState, int cpuT, int gpuT)
        {
            if (_appliedPower != power && _wmi.SetPowerMode(power))
            {
                _appliedPower = power;
                SyncWindowsOverlay(power);
            }
            if (_appliedBoost != boost && CpuBoost.Set(boost)) _appliedBoost = boost;
            if (_appliedMaxState != maxState && CpuBoost.SetMaxState(maxState)) _appliedMaxState = maxState;

            if (_appliedFan != fan && _wmi.SetFanMode(fan))
            {
                _appliedFan = fan;
                _appliedFanSpeed = null;
            }
            if (fan == FanMode.Custom)
            {
                var speeds = (FanCurve.Interpolate(Settings.CpuCurve, cpuT), FanCurve.Interpolate(Settings.GpuCurve, gpuT));
                if (_appliedFanSpeed != speeds && _wmi.SetFanSpeeds(speeds.Item1, speeds.Item2)) _appliedFanSpeed = speeds;
            }
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            // The EC and keyboard controller forget state across sleep: force a full re-apply.
            _appliedPower = null; _appliedFan = null; _appliedBoost = null; _appliedMaxState = null; _appliedFanSpeed = null;
            ApplyLighting();
        }

        // Keep the Windows power-mode slider in step with the Acer profile.
        private static void SyncWindowsOverlay(PowerMode mode)
        {
            Guid overlay = mode switch
            {
                PowerMode.Quiet => new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"),        // best power efficiency
                PowerMode.Performance or PowerMode.Turbo => new Guid("ded574b5-45a0-4f42-8737-46345c09c238"), // best performance
                _ => Guid.Empty                                                                // balanced
            };
            try { PowerSetActiveOverlayScheme(overlay); } catch { }
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            _lightsTimer.Dispose();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            // Never leave a clock cap behind when the app isn't running to manage it.
            CpuBoost.SetMaxState(100);
            _lights?.Dispose();
            _load.Dispose();
            _wmi.Dispose();
        }
    }
}
