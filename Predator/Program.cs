using System.Runtime.InteropServices;

namespace Predator
{
    internal static class Program
    {
        // Same names as the upstream app, so the old and new builds can never run side by side.
        private const string MutexName = "PredatorControlApp_Unique_System_Mutex_999";
        internal static readonly uint WM_SHOWME = RegisterWindowMessage("PREDATOR_CONTROL_SHOW_INSTANCE");

        private static string LogPath => Path.Combine(Settings.Dir, "crash.log");

        internal static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(LogPath, $"[{DateTime.Now:u}] {ex}\n\n");
            }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            using var mutex = new Mutex(false, MutexName);
            bool owned;
            try { owned = mutex.WaitOne(TimeSpan.Zero, false); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                PostMessage((IntPtr)0xffff, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Log(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) Log(ex);
                // A crash must never leave the CPU clock-capped with nothing managing it.
                CpuBoost.SetMaxState(100);
            };

            using var controller = new Controller();
            using var form = new MainForm(controller, startHidden: args.Contains("-hidden"));
            controller.Start();
            Application.Run(form);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
