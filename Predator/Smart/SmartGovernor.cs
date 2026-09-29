namespace Predator
{
    public enum Workload { Idle, Light, Heavy, Gaming }

    public readonly record struct Telemetry(
        int CpuTemp, int GpuTemp, double CpuLoad, double GpuLoad, bool OnAc, bool KnownGame, bool Fullscreen,
        int BatteryPercent = 100);

    public readonly record struct Decision(
        PowerMode Power, bool Boost, int MaxState, FanMode Fan, Workload Workload, int ThermalLevel, string Reason);

    // Decides power mode, CPU boost, max processor state and fan mode from load + temperature.
    // Pure logic (no hardware calls) so it can be unit-tested; Controller applies the result.
    //
    // Measured on the PH16-71 (i9-13900HX) under all-core load:
    //   - With boost on, the CPU holds exactly 91°C in Turbo, Performance *and* Balanced: that's the
    //     firmware's TCC limit. The Acer mode doesn't move CPU temperature; it sets GPU power / clocks.
    //   - Boost off: ~75°C at ~25% lower multi-core clocks. Fans Max: no extra clocks over Auto.
    // So the workload picks the Acer mode, and heat is handled by boost, then by a clock cap.
    public sealed class SmartGovernor
    {
        private readonly record struct Rung(PowerMode Power, bool Boost, int MaxState);

        private static readonly Rung[] Ladder =
        {
            new(PowerMode.Turbo, true, 100),        // 0: gaming / heavy on AC
            new(PowerMode.Performance, true, 100),  // 1: heavy on battery
            // 2-3: light use. Boost off is this laptop's stand-in for an undervolt (the BIOS locks real
            // undervolting): ~70°C instead of 91°C on a few busy threads, at ~86% of base clock.
            new(PowerMode.Balanced, false, 100),    // 2: light use on AC
            new(PowerMode.Balanced, false, 100),    // 3: light use on battery
            new(PowerMode.Quiet, false, 100),       // 4: idle
        };

        // Thermal steps, applied on top of whatever rung the workload chose.
        private static readonly (bool KeepBoost, int MaxState)[] Cooling =
        {
            (true, 100),    // 0: no intervention
            (false, 100),   // 1: boost off (~-15°C)
            (false, 85),    // 2: boost off + clock cap: only if boost-off alone can't hold the limit
            (false, 70),    // 3
        };

        public const int LowBatteryPercent = 20;
        public const int GpuTempLimit = 87;        // NVIDIA's own laptop target; above it the GPU throttles anyway
        public const int GpuFanMaxTemp = 80;       // poor GPU paste: full airflow well before the throttle zone
        public const int CpuFanMaxTemp = 85;       // full airflow from 85°C, well under the 100°C rating
        private const int TicksToUpgrade = 2;      // workload rises quickly (~4 s at a 2 s tick)
        private const int TicksToDowngrade = 8;    // and falls slowly (~16 s) so it doesn't flap between frames
        private const int HotTicksToThrottle = 4;   // smoothed temp at the limit ~8 s after the last step
        private const int CoolTicksToRelax = 6;    // ~12 s below the limit before stepping back up (favours speed)
        private const int EmergencyTicks = 2;      // raw temp far past the limit on 2 ticks in a row
        private const int FanReleaseTicks = 5;
        private const double TempAlpha = 0.4;      // ~5 s smoothing; the HX CPU spikes several °C per second

        public int CpuTempLimit { get; set; } = 93;

        private double _cpuLoad, _gpuLoad, _cpuTemp;
        private bool _seeded;
        private int _emergencyTicks;
        private Workload _workload = Workload.Idle;
        private Workload _candidate = Workload.Idle;
        private int _candidateTicks;
        private int _thermal;
        private int _hotTicks, _coolTicks, _fanCoolTicks;
        private bool _fanMax;

        public Decision Update(Telemetry t)
        {
            Smooth(t);
            UpdateWorkload(Classify(t));
            UpdateThermal(t);
            UpdateGpuThermal();
            TickLightCooldown();

            // Low battery: stretch what's left, whatever the workload.
            bool saver = !t.OnAc && t.BatteryPercent < LowBatteryPercent;
            var r = Ladder[saver ? Ladder.Length - 1 : BaseRung(_workload, t.OnAc)];
            var cool = Cooling[_thermal];
            bool lightTrial = !saver && t.OnAc && _workload == Workload.Light && LightBoostAllowed();
            bool boost = (r.Boost || lightTrial) && cool.KeepBoost;
            int maxState = Math.Min(r.MaxState, cool.MaxState);
            var fan = _fanMax ? FanMode.Max : FanMode.Auto;
            var power = StepDown(r.Power, _gpuThermal);

            string reason = $"{_workload} · {Label(power)}{(boost ? " + boost" : "")}" +
                            (maxState < 100 ? $" · capped {maxState}%" : "") +
                            (_thermal > 0 ? $" · cooling {_thermal}" : "") +
                            (_gpuThermal > 0 ? $" · GPU cooling {_gpuThermal}" : "") +
                            (_fanMax ? " · fans max" : "") +
                            (saver ? " · battery saver" : "") +
                            (_workload == Workload.Light && _lightCooldown > 0 ? " · light boost paused (too hot)" : "");
            return new Decision(power, boost, maxState, fan, _workload, _thermal, reason);
        }

        // GPU heat is handled with the Acer mode, which is what sets GPU power (and Turbo's GPU overclock).
        // Cutting CPU boost or clocks for a hot GPU just costs frames without cooling the GPU.
        private static readonly PowerMode[] GpuSteps = { PowerMode.Turbo, PowerMode.Performance, PowerMode.Balanced };
        private const int GpuHotTicks = 4, GpuCoolTicks = 10;
        private double _gpuTemp;
        private int _gpuThermal, _gpuHot, _gpuCool;

        private static PowerMode StepDown(PowerMode mode, int steps)
        {
            int i = Array.IndexOf(GpuSteps, mode);
            return i < 0 || steps == 0 ? mode : GpuSteps[Math.Min(i + steps, GpuSteps.Length - 1)];
        }

        private void UpdateGpuThermal()
        {
            if (_gpuTemp >= GpuTempLimit)
            {
                _gpuCool = 0;
                if (++_gpuHot >= GpuHotTicks && _gpuThermal < GpuSteps.Length - 1) { _gpuThermal++; _gpuHot = 0; }
            }
            else
            {
                _gpuHot = 0;
                if (_gpuThermal > 0 && _gpuTemp < GpuTempLimit - 7 && ++_gpuCool >= GpuCoolTicks) { _gpuThermal--; _gpuCool = 0; }
            }
        }

        // Self-tuning boost for light work. With good paste, a few busy threads with boost stay well under
        // LightBoostMaxTemp and boost is simply kept. With poor paste (this laptop today) they hit 91°C within
        // seconds, so boost is dropped and retried later with exponential backoff — the app notices a repaste
        // by itself instead of needing a setting changed.
        public const int LightBoostMaxTemp = 85;
        private const int LightBackoffBase = 300;       // 10 min at a 2 s tick
        private const int LightBackoffMax = 3600;       // 2 h
        private const int LightProvenTicks = 150;       // 5 min cool with boost resets the backoff
        private int _lightCooldown;
        private int _lightBackoff = LightBackoffBase;
        private int _lightGoodTicks;

        private bool LightBoostAllowed()
        {
            if (_lightCooldown > 0) return false;
            if (_cpuTemp >= LightBoostMaxTemp)
            {
                _lightCooldown = _lightBackoff;
                _lightBackoff = Math.Min(_lightBackoff * 2, LightBackoffMax);
                _lightGoodTicks = 0;
                return false;
            }
            if (++_lightGoodTicks >= LightProvenTicks) _lightBackoff = LightBackoffBase;
            return true;
        }

        private void TickLightCooldown()
        {
            if (_lightCooldown > 0) _lightCooldown--;
        }

        public static string Label(PowerMode m) => m switch
        {
            PowerMode.Quiet => "Quiet",
            PowerMode.Balanced => "Balanced",
            PowerMode.Performance => "Performance",
            PowerMode.Turbo => "Turbo",
            _ => m.ToString()
        };

        private void Smooth(Telemetry t)
        {
            if (!_seeded) { _cpuLoad = t.CpuLoad; _gpuLoad = t.GpuLoad; _cpuTemp = t.CpuTemp; _gpuTemp = t.GpuTemp; _seeded = true; return; }
            const double a = 0.35;
            _cpuLoad += a * (t.CpuLoad - _cpuLoad);
            _gpuLoad += a * (t.GpuLoad - _gpuLoad);
            _cpuTemp += TempAlpha * (t.CpuTemp - _cpuTemp);
            _gpuTemp += TempAlpha * (t.GpuTemp - _gpuTemp);
        }

        private Workload Classify(Telemetry t)
        {
            if (t.KnownGame || (t.Fullscreen && _gpuLoad > 25) || _gpuLoad > 60) return Workload.Gaming;
            // 32 logical CPUs: 25% is ~8 busy threads (a compile, an export), which deserves boost.
            if (_cpuLoad > 25 || _gpuLoad > 35) return Workload.Heavy;
            if (_cpuLoad > 8 || _gpuLoad > 12) return Workload.Light;
            return Workload.Idle;
        }

        private void UpdateWorkload(Workload raw)
        {
            if (raw == _workload) { _candidateTicks = 0; return; }
            if (raw != _candidate) { _candidate = raw; _candidateTicks = 0; }
            _candidateTicks++;
            int needed = raw > _workload ? TicksToUpgrade : TicksToDowngrade;
            if (_candidateTicks >= needed) { _workload = raw; _candidateTicks = 0; }
        }

        private void UpdateThermal(Telemetry t)
        {
            int limit = CpuTempLimit;
            bool hot = _cpuTemp >= limit;   // CPU only; the GPU has its own step-down (UpdateGpuThermal)

            // Fans first: raw temp near the limit sends them to Max at once; release only once clearly below it.
            if (t.CpuTemp >= CpuFanMaxTemp || t.GpuTemp >= GpuFanMaxTemp) { _fanMax = true; _fanCoolTicks = 0; }
            else if (_fanMax && t.CpuTemp < CpuFanMaxTemp - 5 && t.GpuTemp < GpuFanMaxTemp - 5 && ++_fanCoolTicks >= FanReleaseTicks)
                _fanMax = false;

            _emergencyTicks = t.CpuTemp >= limit + 5 ? _emergencyTicks + 1 : 0;
            if (_emergencyTicks >= EmergencyTicks)
            {
                // Well past the limit and staying there: step down every tick, no waiting.
                _thermal = Math.Min(_thermal + 1, Cooling.Length - 1);
                _hotTicks = _coolTicks = 0;
            }
            else if (hot)
            {
                _coolTicks = 0;
                if (++_hotTicks >= HotTicksToThrottle && _fanMax)
                {
                    _thermal = Math.Min(_thermal + 1, Cooling.Length - 1);
                    _hotTicks = 0;
                }
            }
            else
            {
                _hotTicks = 0;
                if (_thermal > 0 && _cpuTemp < limit - 5 && ++_coolTicks >= CoolTicksToRelax)
                {
                    _thermal--;
                    _coolTicks = 0;
                }
            }
        }

        // On battery Turbo is pointless (no adapter headroom) and just drains the pack.
        private static int BaseRung(Workload w, bool onAc) => w switch
        {
            Workload.Gaming or Workload.Heavy => onAc ? 0 : 1,
            Workload.Light => onAc ? 2 : 3,
            _ => 4
        };
    }
}
