using System.Management;
using System.Runtime.Versioning;

namespace Predator
{
    public enum PowerMode : byte { Quiet = 0x00, Balanced = 0x01, Performance = 0x04, Turbo = 0x05 }

    public enum FanMode : byte { Auto = 0x01, Max = 0x02, Custom = 0x03 }

    // Acer's AcerGamingFunction WMI interface (root\WMI). Every encoding here was checked live on a PH16-71.
    // Each call returns gmOutput; the low byte is 0 on success.
    [SupportedOSPlatform("windows")]
    public sealed class AcerWmi : IDisposable
    {
        private const byte FanCpu = 0x01, FanGpu = 0x04;
        private const ulong SensorCpuTemp = 0x01, SensorCpuFan = 0x02, SensorGpuFan = 0x06, SensorGpuTemp = 0x0A;

        private ManagementObject? _obj;
        private readonly object _lock = new();

        public bool SetPowerMode(PowerMode mode) => Call("SetGamingMiscSetting", 0x0BUL | ((ulong)mode << 8));

        public PowerMode? GetPowerMode()
        {
            ulong? raw = CallRaw("GetGamingMiscSetting", 0x0B);
            return raw is ulong v && (v & 0xFF) == 0 ? (PowerMode)((v >> 8) & 0xFF) : null;
        }

        // Bits 16-21 set the CPU fan mode, 22-27 the GPU fan; 0x09 selects both fans.
        public bool SetFanMode(FanMode mode) =>
            Call("SetGamingFanBehavior", 0x09UL | ((ulong)mode << 16) | ((ulong)mode << 22));

        /// <summary>Only honoured in Custom fan mode. The EC ignores the CPU fan target while the CPU runs hot.</summary>
        public bool SetFanSpeeds(int cpuPercent, int gpuPercent) =>
            Call("SetGamingFanSpeed", FanCpu | ((ulong)Math.Clamp(cpuPercent, 0, 100) << 8)) &
            Call("SetGamingFanSpeed", FanGpu | ((ulong)Math.Clamp(gpuPercent, 0, 100) << 8));

        public int CpuTemp => Sensor(SensorCpuTemp);
        public int GpuTemp => Sensor(SensorGpuTemp);
        public int CpuFanRpm => Sensor(SensorCpuFan);
        public int GpuFanRpm => Sensor(SensorGpuFan);

        private int Sensor(ulong id)
        {
            ulong? raw = CallRaw("GetGamingSysInfo", 0x01UL | (id << 8));
            return raw is ulong v && (v & 0xFF) == 0 ? (int)((v >> 8) & 0xFFFF) : 0;
        }

        private bool Call(string method, ulong input) => CallRaw(method, input) is ulong v && (v & 0xFF) == 0;

        private ulong? CallRaw(string method, ulong input)
        {
            lock (_lock)
            {
                try
                {
                    _obj ??= FindObject();
                    if (_obj == null) return null;
                    using var inParams = _obj.GetMethodParameters(method);
                    inParams["gmInput"] = input;
                    using var outParams = _obj.InvokeMethod(method, inParams, null);
                    return Convert.ToUInt64(outParams["gmOutput"]);
                }
                catch
                {
                    // The cached instance goes stale after sleep; drop it and retry on the next call.
                    _obj?.Dispose();
                    _obj = null;
                    return null;
                }
            }
        }

        private static ManagementObject? FindObject()
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
            using var results = searcher.Get();
            return results.Cast<ManagementObject>().FirstOrDefault();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _obj?.Dispose();
                _obj = null;
            }
        }
    }
}
