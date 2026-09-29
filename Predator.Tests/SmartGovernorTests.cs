using Predator;
using Xunit;

public class SmartGovernorTests
{
    private static Telemetry T(int cpuT, double cpu = 3, double gpu = 2, int gpuT = 45, bool ac = true, bool game = false, bool fullscreen = false)
        => new(cpuT, gpuT, cpu, gpu, ac, game, fullscreen);

    private static Decision Run(SmartGovernor g, Telemetry t, int ticks)
    {
        Decision d = default;
        for (int i = 0; i < ticks; i++) d = g.Update(t);
        return d;
    }

    [Fact]
    public void Idle_is_quiet_with_boost_off()
    {
        var d = Run(new SmartGovernor(), T(60), 5);
        Assert.Equal(Workload.Idle, d.Workload);
        Assert.Equal(PowerMode.Quiet, d.Power);
        Assert.False(d.Boost);
        Assert.Equal(FanMode.Auto, d.Fan);
        Assert.Equal(100, d.MaxState);
    }

    [Fact]
    public void Gaming_on_ac_goes_turbo_with_boost()
    {
        var d = Run(new SmartGovernor(), T(75, cpu: 30, gpu: 90, gpuT: 70), 4);
        Assert.Equal(Workload.Gaming, d.Workload);
        Assert.Equal(PowerMode.Turbo, d.Power);
        Assert.True(d.Boost);
    }

    [Fact]
    public void Known_game_counts_even_at_low_load()
    {
        var d = Run(new SmartGovernor(), T(65, cpu: 5, gpu: 5, game: true), 3);
        Assert.Equal(Workload.Gaming, d.Workload);
    }

    [Fact]
    public void Fullscreen_video_is_not_a_game()
    {
        // Fullscreen with light GPU load (video) must not be treated as gaming.
        var d = Run(new SmartGovernor(), T(60, cpu: 8, gpu: 10, fullscreen: true), 6);
        Assert.NotEqual(Workload.Gaming, d.Workload);
    }

    [Fact]
    public void Battery_never_uses_turbo()
    {
        var d = Run(new SmartGovernor(), T(75, cpu: 70, gpu: 90, ac: false), 5);
        Assert.NotEqual(PowerMode.Turbo, d.Power);
    }

    [Fact]
    public void Heat_raises_fans_before_cutting_performance()
    {
        var g = new SmartGovernor { CpuTempLimit = 90 };
        Run(g, T(80, cpu: 80, gpu: 90), 4);
        var d = g.Update(T(88, cpu: 80, gpu: 90));
        Assert.Equal(FanMode.Max, d.Fan);
        Assert.Equal(PowerMode.Turbo, d.Power);
        Assert.Equal(0, d.ThermalLevel);
    }

    [Fact]
    public void Sustained_limit_steps_down_and_recovers_when_cool()
    {
        var g = new SmartGovernor { CpuTempLimit = 90 };
        Run(g, T(80, cpu: 80, gpu: 90), 4);
        var blip = Run(g, T(92, cpu: 80, gpu: 90), 3);
        Assert.Equal(0, blip.ThermalLevel);   // a few seconds at the limit only raises the fans
        Assert.Equal(FanMode.Max, blip.Fan);

        var hot = Run(g, T(92, cpu: 80, gpu: 90), 5);
        Assert.Equal(1, hot.ThermalLevel);
        Assert.False(hot.Boost);                        // heat is fought with boost...
        Assert.Equal(PowerMode.Turbo, hot.Power);       // ...while the GPU keeps Turbo's power

        var cool = Run(g, T(75, cpu: 80, gpu: 90), 12);
        Assert.Equal(0, cool.ThermalLevel);
        Assert.True(cool.Boost);
    }

    [Fact]
    public void Runaway_temperature_steps_down_every_tick()
    {
        var g = new SmartGovernor { CpuTempLimit = 90 };
        Run(g, T(80, cpu: 80, gpu: 90), 4);
        var d = Run(g, T(97, cpu: 80, gpu: 90), 4);
        Assert.Equal(3, d.ThermalLevel);
        Assert.False(d.Boost);
        Assert.Equal(70, d.MaxState);
    }

    [Fact]
    public void Idle_is_never_clock_capped_once_cool()
    {
        var g = new SmartGovernor();
        Run(g, T(99, cpu: 100, gpu: 100), 10);
        var d = Run(g, T(55), 60);
        Assert.Equal(Workload.Idle, d.Workload);
        Assert.Equal(100, d.MaxState);
        Assert.Equal(FanMode.Auto, d.Fan);
    }

    [Fact]
    public void Single_spike_past_the_limit_is_ignored()
    {
        var g = new SmartGovernor { CpuTempLimit = 90 };
        Run(g, T(80, cpu: 80, gpu: 90), 4);
        var d = g.Update(T(98, cpu: 80, gpu: 90));
        d = g.Update(T(80, cpu: 80, gpu: 90));
        Assert.Equal(0, d.ThermalLevel);
    }

    [Fact]
    public void Worst_case_caps_clocks_but_never_below_70()
    {
        var g = new SmartGovernor();
        var d = Run(g, T(99, cpu: 100, gpu: 100), 40);
        Assert.Equal(70, d.MaxState);
    }

    [Fact]
    public void Brief_load_spike_does_not_leave_idle()
    {
        var g = new SmartGovernor();
        Run(g, T(60), 5);
        var d = g.Update(T(60, cpu: 90));
        Assert.Equal(Workload.Idle, d.Workload);
    }

    [Fact]
    public void Load_drop_waits_before_downgrading()
    {
        var g = new SmartGovernor();
        Run(g, T(75, cpu: 30, gpu: 90), 4);
        var d = Run(g, T(60), 5);
        Assert.Equal(Workload.Gaming, d.Workload);
        d = Run(g, T(60), 15);
        Assert.Equal(Workload.Idle, d.Workload);
    }

    [Fact]
    public void Low_battery_forces_quiet_even_when_gaming()
    {
        var g = new SmartGovernor();
        var d = Run(g, new Telemetry(70, 60, 50, 90, false, true, true, 15), 4);
        Assert.Equal(PowerMode.Quiet, d.Power);
        Assert.False(d.Boost);
    }

    [Fact]
    public void Light_use_keeps_boost_when_it_stays_cool()
    {
        // Good paste: light work with boost stays well under 85°C, so boost is kept.
        var d = Run(new SmartGovernor(), T(70, cpu: 15, gpu: 5), 200);
        Assert.Equal(Workload.Light, d.Workload);
        Assert.Equal(PowerMode.Balanced, d.Power);
        Assert.True(d.Boost);
    }

    [Fact]
    public void Light_use_drops_boost_when_it_runs_hot_and_backs_off()
    {
        // Poor paste: a few threads with boost reach 91°C. Boost goes off and stays off for the backoff.
        var g = new SmartGovernor();
        Run(g, T(70, cpu: 15, gpu: 5), 3);
        var d = Run(g, T(91, cpu: 15, gpu: 5), 8);
        Assert.False(d.Boost);
        Assert.Contains("light boost paused", d.Reason);

        d = Run(g, T(70, cpu: 15, gpu: 5), 250);     // < 10 min later: still paused even though it cooled
        Assert.False(d.Boost);

        d = Run(g, T(70, cpu: 15, gpu: 5), 60);      // past 10 min: tries again
        Assert.True(d.Boost);
    }

    [Fact]
    public void Light_boost_backoff_doubles_on_repeated_failures()
    {
        var g = new SmartGovernor();
        Run(g, T(70, cpu: 15, gpu: 5), 3);
        Run(g, T(91, cpu: 15, gpu: 5), 8);            // fail #1 -> 300 ticks
        Run(g, T(70, cpu: 15, gpu: 5), 310);          // retry allowed
        Run(g, T(91, cpu: 15, gpu: 5), 8);            // fail #2 -> 600 ticks
        var d = Run(g, T(70, cpu: 15, gpu: 5), 400);
        Assert.False(d.Boost);                         // still inside the doubled window
        d = Run(g, T(70, cpu: 15, gpu: 5), 250);
        Assert.True(d.Boost);
    }

    [Fact]
    public void Light_boost_is_not_tried_on_battery()
    {
        var d = Run(new SmartGovernor(), T(60, cpu: 15, gpu: 5, ac: false), 10);
        Assert.False(d.Boost);
    }

    [Fact]
    public void Eight_busy_threads_count_as_heavy()
    {
        var d = Run(new SmartGovernor(), T(80, cpu: 28, gpu: 3), 5);
        Assert.Equal(Workload.Heavy, d.Workload);
        Assert.True(d.Boost);
    }

    [Fact]
    public void Default_limit_lets_the_firmware_plateau_run_at_full_boost()
    {
        // With boost the CPU parks at 91°C (firmware TCC). The default 93°C limit must not fight that.
        var g = new SmartGovernor();
        var d = Run(g, T(91, cpu: 100, gpu: 5), 30);
        Assert.Equal(0, d.ThermalLevel);
        Assert.True(d.Boost);
        Assert.Equal(FanMode.Max, d.Fan);
    }

    [Fact]
    public void Hot_gpu_steps_down_the_acer_mode_and_leaves_the_cpu_alone()
    {
        // Seen live: Rocket League + a local LLM on the GPU -> GPU 91°C while the CPU sat at 65°C.
        var g = new SmartGovernor();
        Run(g, T(65, cpu: 30, gpu: 95, gpuT: 75), 4);
        var d = Run(g, T(65, cpu: 30, gpu: 95, gpuT: 91), 8);
        Assert.Equal(PowerMode.Performance, d.Power);
        Assert.True(d.Boost);
        Assert.Equal(100, d.MaxState);
        Assert.Equal(0, d.ThermalLevel);
        Assert.Equal(FanMode.Max, d.Fan);

        d = Run(g, T(65, cpu: 30, gpu: 95, gpuT: 91), 6);
        Assert.Equal(PowerMode.Balanced, d.Power);

        d = Run(g, T(65, cpu: 30, gpu: 95, gpuT: 72), 30);   // cooled: back up to Turbo
        Assert.Equal(PowerMode.Turbo, d.Power);
    }

    [Fact]
    public void Curve_interpolates_and_clamps()
    {
        var c = FanCurve.DefaultCpu();
        Assert.Equal(0, FanCurve.Interpolate(c, 20));
        Assert.Equal(100, FanCurve.Interpolate(c, 99));
        Assert.Equal(15, FanCurve.Interpolate(c, 60));
        Assert.Equal(38, FanCurve.Interpolate(c, 74));    // between 70->30 and 78->45

        var g = FanCurve.DefaultGpu();
        Assert.Equal(100, FanCurve.Interpolate(g, 86));
        Assert.True(FanCurve.Interpolate(g, 80) > FanCurve.Interpolate(c, 80), "GPU curve ramps earlier than CPU");
    }
}
