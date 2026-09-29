using System.Runtime.Versioning;

namespace Predator
{
    // Renders lighting on the per-key USB keyboard. Static and Off are sent once;
    // animated effects stream ~30 fps on a background thread (the controller handles 120+ fps).
    [SupportedOSPlatform("windows")]
    public sealed class KeyboardEffects : IDisposable
    {
        private const int FrameMs = 33;
        private const int Slots = AcerUsbKeyboard.SlotCount;

        private readonly AcerUsbKeyboard _kb = new();
        private readonly bool[] _hasLed = new bool[Slots];
        private readonly object _lock = new();
        private Thread? _thread;
        private volatile bool _stop;

        private LightEffect _effect;
        private byte _r, _g, _b, _brightness;

        public KeyboardEffects()
        {
            for (int i = 0; i < Slots; i++) _hasLed[i] = AcerUsbKeyboard.KeyNames[i] != null;
        }

        public static bool IsSupported => AcerUsbKeyboard.IsPresent();

        /// <param name="brightness">0..100</param>
        public void Apply(LightEffect effect, byte r, byte g, byte b, int brightness)
        {
            lock (_lock)
            {
                _effect = effect; _r = r; _g = g; _b = b;
                _brightness = (byte)Math.Round(Math.Clamp(brightness, 0, 100) * AcerUsbKeyboard.MaxBrightness / 100.0);
            }

            if (effect is LightEffect.Off or LightEffect.Static)
            {
                StopThread();
                Render(0);
            }
            else if (_thread == null)
            {
                _stop = false;
                _thread = new Thread(Loop) { IsBackground = true, Name = "KeyboardEffects", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
        }

        private void Loop()
        {
            long start = Environment.TickCount64;
            while (!_stop)
            {
                long tick = Environment.TickCount64;
                Render((tick - start) / 1000.0);
                int sleep = FrameMs - (int)(Environment.TickCount64 - tick);
                if (sleep > 0) Thread.Sleep(sleep);
            }
        }

        private void Render(double t)
        {
            LightEffect effect; byte r, g, b, bright;
            lock (_lock) { effect = _effect; r = _r; g = _g; b = _b; bright = _brightness; }

            var frame = new byte[Slots * 3];
            if (effect != LightEffect.Off)
            {
                for (int i = 0; i < Slots; i++)
                {
                    if (!_hasLed[i]) continue;
                    double x = (i / AcerUsbKeyboard.Rows) / (double)(AcerUsbKeyboard.Columns - 1);
                    (double cr, double cg, double cb) = effect switch
                    {
                        LightEffect.Breathing => Scale(r, g, b, 0.5 - 0.5 * Math.Cos(t * 0.5 * 2 * Math.PI)),
                        LightEffect.Wave => Hsv(x - t * 0.35),
                        LightEffect.Rainbow => Hsv(t * 0.1),
                        _ => (r, g, b)
                    };
                    frame[i * 3] = (byte)Math.Clamp(cr, 0, 255);
                    frame[i * 3 + 1] = (byte)Math.Clamp(cg, 0, 255);
                    frame[i * 3 + 2] = (byte)Math.Clamp(cb, 0, 255);
                }
            }
            _kb.SendFrame(frame, effect == LightEffect.Off ? (byte)0 : bright);
        }

        private static (double, double, double) Scale(byte r, byte g, byte b, double k) => (r * k, g * k, b * k);

        private static (double, double, double) Hsv(double h)
        {
            h = ((h % 1) + 1) % 1 * 6;
            int sector = (int)h;
            double f = h - sector, q = 1 - f;
            (double r, double g, double b) = sector switch
            {
                0 => (1.0, f, 0.0), 1 => (q, 1.0, 0.0), 2 => (0.0, 1.0, f), 3 => (0.0, q, 1.0), 4 => (f, 0.0, 1.0), _ => (1.0, 0.0, q)
            };
            return (r * 255, g * 255, b * 255);
        }

        private void StopThread()
        {
            var t = _thread;
            if (t == null) return;
            _stop = true;
            t.Join(500);
            _thread = null;
        }

        public void Dispose()
        {
            StopThread();
            _kb.Dispose();
        }
    }
}
