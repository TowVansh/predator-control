using System.Management;
using System.Runtime.Versioning;

namespace Predator
{
    // Acer battery "health mode": the EC stops charging at 80%. Interface is root\WMI BatteryControl
    // (GUID 79772EC5-04B1-4bfd-843C-61E7F77B6CC9), same as Linux's linuwu-sense battery_limiter.
    // Verified on PH16-71: functionList 0x03 (health + calibration), needs admin.
    [SupportedOSPlatform("windows")]
    public static class BatteryHealth
    {
        private const byte BatteryNo = 1;
        private const byte HealthMode = 1;

        /// <summary>True/false = limit on/off; null = interface unavailable.</summary>
        public static bool? IsLimitOn()
        {
            try
            {
                using var obj = Find();
                if (obj == null) return null;
                using var inParams = obj.GetMethodParameters("GetBatteryHealthControlStatus");
                inParams["uBatteryNo"] = BatteryNo;
                inParams["uFunctionQuery"] = (byte)1;
                inParams["uReserved"] = new byte[] { 0, 0 };
                using var outParams = obj.InvokeMethod("GetBatteryHealthControlStatus", inParams, null);
                if (outParams["uFunctionStatus"] is not byte[] status || status.Length == 0) return null;
                return status[0] != 0;
            }
            catch { return null; }
        }

        public static bool SetLimit(bool on)
        {
            try
            {
                using var obj = Find();
                if (obj == null) return false;
                using var inParams = obj.GetMethodParameters("SetBatteryHealthControl");
                inParams["uBatteryNo"] = BatteryNo;
                inParams["uFunctionMask"] = HealthMode;
                inParams["uFunctionStatus"] = (byte)(on ? 1 : 0);
                inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };
                using var outParams = obj.InvokeMethod("SetBatteryHealthControl", inParams, null);
                return Convert.ToUInt16(outParams["uReturn"]) == 0 && IsLimitOn() == on;
            }
            catch { return false; }
        }

        private static ManagementObject? Find()
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryControl");
            using var results = searcher.Get();
            return results.Cast<ManagementObject>().FirstOrDefault();
        }
    }
}
