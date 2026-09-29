using System.Management;
using System.Runtime.Versioning;

namespace Predator
{
    // Acer's special keys (Turbo, PredatorSense) arrive as AcerGenericEvent WMI events, which PredatorSense used
    // to consume. Event layout (from the Linux acer-wmi driver): byte 0 = function, byte 1 = key number.
    //   0x07 = gaming Turbo key.  0x01 = hotkey (key number in byte 1).
    // Every event is logged to keys.log so unknown keys can be mapped later.
    [SupportedOSPlatform("windows")]
    public sealed class AcerKeys : IDisposable
    {
        private const byte FuncHotkey = 0x01;
        private const byte FuncTurboKey = 0x07;

        private readonly ManagementEventWatcher? _watcher;
        private readonly SynchronizationContext? _ui;

        public event Action? TurboPressed;
        public event Action? PredatorPressed;

        /// <summary>Hotkey number of the PredatorSense key, once known (settings.json: PredatorKeyCode).</summary>
        public int? PredatorKeyCode { get; set; }

        public AcerKeys()
        {
            _ui = SynchronizationContext.Current;
            try
            {
                _watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"),
                    new EventQuery("SELECT * FROM AcerGenericEvent"));
                _watcher.EventArrived += OnEvent;
                _watcher.Start();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                _watcher = null;
            }
        }

        private void OnEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (e.NewEvent["EventDetail"] is not byte[] d || d.Length < 2) return;
                Log(d);

                Action? handler = d[0] switch
                {
                    FuncTurboKey => TurboPressed,
                    FuncHotkey when PredatorKeyCode == d[1] => PredatorPressed,
                    _ => null
                };
                if (handler == null) return;
                if (_ui != null) _ui.Post(_ => handler(), null);
                else handler();
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        private static void Log(byte[] detail)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(Path.Combine(Settings.Dir, "keys.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Convert.ToHexString(detail)}{Environment.NewLine}");
            }
            catch { }
        }

        public void Dispose()
        {
            try { _watcher?.Stop(); } catch { }
            _watcher?.Dispose();
        }
    }
}
