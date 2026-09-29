using Predator;
using Xunit;
using Xunit.Abstractions;

// Hardware-facing sanity checks; they read real counters on this machine.
public class LoadMonitorTests(ITestOutputHelper output)
{
    [Fact]
    public void Samples_are_in_range_and_cpu_is_nonzero()
    {
        using var m = new LoadMonitor();
        m.Sample();
        double cpuMax = 0, gpuMax = 0;
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(500);
            var (cpu, gpu) = m.Sample();
            output.WriteLine($"cpu {cpu:0.0}%  gpu {gpu:0.0}%");
            Assert.InRange(cpu, 0, 100);
            Assert.InRange(gpu, 0, 100);
            cpuMax = Math.Max(cpuMax, cpu);
            gpuMax = Math.Max(gpuMax, gpu);
        }
        output.WriteLine($"max cpu {cpuMax:0.0} gpu {gpuMax:0.0}");
        Assert.True(cpuMax > 0, "CPU counter never moved");
    }
}
