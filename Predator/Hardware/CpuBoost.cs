using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Predator
{
    // CPU turbo boost is a Windows power-plan setting (PERFBOOSTMODE), not an Acer one.
    // On the i9-13900HX, boost at idle keeps the CPU near 80°C; off drops idle temps ~10°C.
    [SupportedOSPlatform("windows")]
    public static class CpuBoost
    {
        private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
        private const uint Disabled = 0, Aggressive = 2;

        public static bool? IsEnabled()
        {
            try
            {
                if (ActiveScheme() is not Guid scheme) return null;
                Guid sub = SubProcessor, setting = PerfBoostMode;
                if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint value) != 0) return null;
                return value != Disabled;
            }
            catch { return null; }
        }

        public static bool Set(bool enabled) => Write(PerfBoostMode, enabled ? Aggressive : Disabled);

        // PROCTHROTTLEMAX: "maximum processor state" %. The last-resort thermal lever: below 100 it caps
        // clocks for every core. Always restored to 100 on exit.
        private static readonly Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

        public static bool SetMaxState(int percent) => Write(ProcThrottleMax, (uint)Math.Clamp(percent, 50, 100));

        private static bool Write(Guid settingGuid, uint value)
        {
            try
            {
                if (ActiveScheme() is not Guid scheme) return false;
                Guid sub = SubProcessor, setting = settingGuid;
                if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value) != 0) return false;
                if (PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value) != 0) return false;
                return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
            }
            catch { return false; }
        }

        private static Guid? ActiveScheme()
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) != 0) return null;
            try { return Marshal.PtrToStructure<Guid>(ptr); }
            finally { LocalFree(ptr); }
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint dcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
