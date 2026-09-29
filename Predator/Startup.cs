using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Predator
{
    // Starts the app at logon via a scheduled task, the only way to launch an admin app without a UAC prompt.
    internal static class Startup
    {
        private const string TaskName = "PredatorControl";

        public static bool IsEnabled() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

        public static bool SetEnabled(bool enable)
        {
            if (!enable)
                return Schtasks($"/Delete /TN \"{TaskName}\" /F") == 0 || !IsEnabled();

            string xmlPath = Path.Combine(Path.GetTempPath(), "PredatorControlStartup.xml");
            try
            {
                File.WriteAllText(xmlPath, TaskXml(), Encoding.Unicode);
                return Schtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0;
            }
            catch { return false; }
            finally { try { File.Delete(xmlPath); } catch { } }
        }

        private static string TaskXml()
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string exe = SecurityElement.Escape(Application.ExecutablePath);
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Starts Predator Control at logon with administrator rights.</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT15S</Delay></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author""><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{exe}</Command><Arguments>-hidden</Arguments></Exec></Actions>
</Task>";
        }

        private static int Schtasks(string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                });
                if (p == null) return -1;
                p.WaitForExit(15000);
                return p.HasExited ? p.ExitCode : -1;
            }
            catch { return -1; }
        }
    }
}
