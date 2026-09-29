using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Predator
{
    // CPU / GPU load via PDH (the same counters Task Manager reads). Reading the GPU Engine counters
    // doesn't wake the NVIDIA GPU, unlike NVML or nvidia-smi, so this is safe to poll on battery.
    [SupportedOSPlatform("windows")]
    public sealed class LoadMonitor : IDisposable
    {
        private IntPtr _query;
        private IntPtr _cpuCounter, _gpuCounter;
        private bool _primed;

        public LoadMonitor()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }
            PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _cpuCounter);
            PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpuCounter);
            PdhCollectQueryData(_query);
        }

        /// <summary>Samples since the previous call. Returns (cpu%, gpu%); the first call returns zeros.</summary>
        public (double Cpu, double Gpu) Sample()
        {
            if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return (0, 0);
            if (!_primed) { _primed = true; return (0, 0); }
            return (Math.Clamp(Single(_cpuCounter), 0, 100), Math.Clamp(BusiestAdapter(_gpuCounter), 0, 100));
        }

        private static double Single(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return 0;
            return PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var v) == 0 ? v.doubleValue : 0;
        }

        // The wildcard returns one instance per process per engine ("pid_123_luid_0x.._phys_0_eng_0_engtype_3D").
        // Task Manager's GPU % is the per-adapter sum; take the busiest adapter so iGPU desktop work doesn't mask the dGPU.
        private static double BusiestAdapter(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return 0;
            int size = 0;
            int status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out int count, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return 0;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return 0;
                var perAdapter = new Dictionary<string, double>();
                int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buf + i * itemSize);
                    if (item.value.CStatus != 0) continue;
                    string name = Marshal.PtrToStringUni(item.szName) ?? "";
                    int l = name.IndexOf("luid_", StringComparison.Ordinal);
                    string luid = l >= 0 ? name.Substring(l, Math.Min(26, name.Length - l)) : "";
                    perAdapter[luid] = perAdapter.GetValueOrDefault(luid) + item.value.doubleValue;
                }
                return perAdapter.Count == 0 ? 0 : perAdapter.Values.Max();
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Exe name of the foreground window if it covers a whole monitor (fullscreen game/video).</summary>
        public static string? FullscreenForegroundExe()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return null;

            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is "WorkerW" or "Progman") return null;

            if (!GetWindowRect(hwnd, out RECT r)) return null;
            IntPtr mon = MonitorFromWindow(hwnd, 2);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return null;
            var m = mi.rcMonitor;
            if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return null;

            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                using var p = Process.GetProcessById((int)pid);
                return p.ProcessName + ".exe";
            }
            catch { return null; }
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }

        #region Native

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

        [StructLayout(LayoutKind.Explicit)]
        private struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double doubleValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE_ITEM
        {
            public IntPtr szName;
            public PDH_FMT_COUNTERVALUE value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref int size, out int count, IntPtr items);
        [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        #endregion
    }
}
