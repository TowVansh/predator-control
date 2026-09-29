using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Predator
{
    // Per-key RGB keyboards (e.g. Predator Helios 16 PH16-71) don't respond to the WMI lighting calls;
    // their lighting lives on a USB HID controller (VID 04F2, PIDs 0117/0119/011A, usage page FF02).
    // Protocol reverse-engineered from Acer's AcerUSBController.dll:
    //   feature  12 00 00 08 00 00 00 chk        -> 8 data packets follow
    //   write    8 x 64 bytes; slot i = bytes [i*4 .. i*4+3] = { 0, R, G, B }
    //   feature  08 02 33 05 bright 08 01 chk    -> show the per-key frame (bright 0..50)
    //   chk = 0xFF - sum(previous 7 bytes)
    [SupportedOSPlatform("windows")]
    public sealed class AcerUsbKeyboard : IDisposable
    {
        public const int SlotCount = 128;
        public const int Rows = 6;
        public const int Columns = (SlotCount + Rows - 1) / Rows;
        public const byte MaxBrightness = 50;

        private static readonly string[] SupportedIds = { "vid_04f2&pid_0117", "vid_04f2&pid_0119", "vid_04f2&pid_011a" };

        // Slot -> key name, from the controller's own table. Slots run column by column, bottom row first
        // (slot % 6 == 0 is the Ctrl/space row, 5 is the F-key row). Null = no LED in that slot.
        public static readonly string?[] KeyNames =
        {
            "Left Control", "Left Shift", "Caps Lock", "Tab", "`", "Escape",
            "Left Fn", null, null, null, "1", "F1",
            "Left Windows", "\\", null, "Q", "2", "F2",
            "Left Alt", "Z", "A", "W", "3", "F3",
            "Space", "X", "S", "E", "4", "F4",
            null, "C", "D", "R", "5", "F5",
            null, "V", "F", "T", "6", "F6",
            null, "B", "G", "Y", "7", "F7",
            null, "N", "H", "U", "8", "F8",
            null, "M", "J", "I", "9", "F9",
            "Right Alt", ",", "K", "O", "0", "F10",
            "Left Space", ".", "L", "P", "-", "F11",
            "Option", "/", ";", "[", "=", "F12",
            "Right Control", null, "'", "]", "Backspace", "Print Screen",
            "Left Arrow", "Right Shift", "Left Enter", null, "Right Space", "Insert",
            "Up Arrow", null, null, null, null, "Delete",
            "Down Arrow", "Pause/Break", "Home", "Number Pad 7", "Number Pad 1", "Number Pad 4",
            "Right Arrow", "Mute", "Volume Down", "Volume Up", "PredatorSense", "Power",
            "Number Pad 0", "Number Pad 2", "Number Pad 5", "Number Pad 8", "Num Lock", "Page Up",
            "Number Pad .", "Number Pad 3", "Number Pad 6", "Number Pad 9", "Number Pad /", "Page Down",
            "Number Pad Enter", null, "Number Pad +", "Number Pad -", "Number Pad *", null,
            null, null,
        };

        private SafeFileHandle? _handle;
        private int _outputLen = 65;
        private int _featureLen = 9;
        private readonly object _lock = new();

        public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

        // After a failure, don't rescan USB on every frame (effects run at 30 fps).
        private long _nextOpenAttempt;
        private const int ReopenDelayMs = 5000;

        public bool Open()
        {
            lock (_lock)
            {
                if (IsOpen) return true;
                if (Environment.TickCount64 < _nextOpenAttempt) return false;
                _nextOpenAttempt = Environment.TickCount64 + ReopenDelayMs;
                string? path = FindDevicePath();
                if (path == null) return false;

                var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h.IsInvalid) { h.Dispose(); return false; }

                if (HidD_GetPreparsedData(h, out IntPtr pp))
                {
                    try
                    {
                        if (HidP_GetCaps(pp, out HIDP_CAPS caps) == HIDP_STATUS_SUCCESS)
                        {
                            if (caps.OutputReportByteLength > 0) _outputLen = caps.OutputReportByteLength;
                            if (caps.FeatureReportByteLength > 0) _featureLen = caps.FeatureReportByteLength;
                        }
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                _handle = h;
                _nextOpenAttempt = 0;
                return true;
            }
        }

        public static bool IsPresent() => FindDevicePath() != null;

        /// <summary>Send one frame. colors = SlotCount * 3 bytes (R,G,B per slot); brightness 0..MaxBrightness.</summary>
        public bool SendFrame(ReadOnlySpan<byte> colors, byte brightness)
        {
            lock (_lock)
            {
                if (!IsOpen && !Open()) return false;

                Span<byte> data = stackalloc byte[SlotCount * 4];
                data.Clear();
                for (int i = 0; i < SlotCount && i * 3 + 2 < colors.Length; i++)
                {
                    data[i * 4 + 1] = colors[i * 3];
                    data[i * 4 + 2] = colors[i * 3 + 1];
                    data[i * 4 + 3] = colors[i * 3 + 2];
                }

                bool ok = Feature(0x12, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00);
                byte[] packet = new byte[_outputLen];
                for (int p = 0; ok && p < 8; p++)
                {
                    Array.Clear(packet);
                    data.Slice(p * 64, 64).CopyTo(packet.AsSpan(1));
                    ok = WriteFile(_handle!, packet, packet.Length, out _, IntPtr.Zero);
                }
                ok = ok && Feature(0x08, 0x02, 0x33, 0x05, Math.Min(brightness, MaxBrightness), 0x08, 0x01);

                if (!ok) CloseHandle();
                return ok;
            }
        }

        private bool Feature(params byte[] body)
        {
            byte[] report = new byte[Math.Max(_featureLen, body.Length + 2)];
            int sum = 0;
            for (int i = 0; i < body.Length; i++) { report[i + 1] = body[i]; sum += body[i]; }
            report[body.Length + 1] = (byte)(0xFF - (sum & 0xFF));
            return HidD_SetFeature(_handle!, report, report.Length);
        }

        private void CloseHandle()
        {
            _handle?.Dispose();
            _handle = null;
        }

        public void Dispose()
        {
            lock (_lock) CloseHandle();
        }

        private static string? FindDevicePath()
        {
            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID_HANDLE_VALUE) return null;
            try
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref iface); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(buf, 8); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W on x64
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, buf, size, out _, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                        string lower = path.ToLowerInvariant();
                        // Interface 3 is the vendor lighting collection (usage page FF02).
                        if (lower.Contains("&mi_03") && SupportedIds.Any(lower.Contains))
                            return path;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }

        #region Native

        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        private const int HIDP_STATUS_SUCCESS = 0x00110000;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int requiredSize, IntPtr devInfo);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr overlapped);

        #endregion
    }
}
