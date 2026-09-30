using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Predator
{
    public enum LightEffect { Off, Static, Breathing, Wave, Rainbow }

    public sealed class Settings
    {
        /// <summary>Smart = the governor picks power, boost and fans itself. Otherwise Power/Fan are fixed.</summary>
        public bool Smart { get; set; } = true;
        public int CpuTempLimit { get; set; } = 93;   // firmware TCC already holds ~91°C with boost
        public PowerMode Power { get; set; } = PowerMode.Balanced;
        public FanMode Fan { get; set; } = FanMode.Auto;
        public List<Point> CpuCurve { get; set; } = FanCurve.DefaultCpu();
        public List<Point> GpuCurve { get; set; } = FanCurve.DefaultGpu();

        public LightEffect Light { get; set; } = LightEffect.Static;
        public int LightR { get; set; } = 0;
        public int LightG { get; set; } = 150;
        public int LightB { get; set; } = 255;
        public int LightBrightness { get; set; } = 100;   // 0..100
        /// <summary>Stop charging at 80% (Acer battery health mode). This battery was at 70% health after 108 cycles.</summary>
        public bool BatteryLimit80 { get; set; } = true;

        /// <summary>Hotkey number the PredatorSense key sends (see keys.log); opens the window.</summary>
        public int? PredatorKeyCode { get; set; }

        /// <summary>Lights off after a few idle minutes, dimmer on battery.</summary>
        public bool SmartLighting { get; set; } = true;

        /// <summary>Exe names Smart always treats as games, on top of what it detects from load.</summary>
        public List<string> Games { get; set; } = new();

        [JsonIgnore] public Color LightColor
        {
            get => Color.FromArgb(LightR, LightG, LightB);
            set { LightR = value.R; LightG = value.G; LightB = value.B; }
        }

        public static string Dir
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(string.IsNullOrEmpty(root) ? Path.GetTempPath() : root, "PredatorControl");
            }
        }

        private static string FilePath => Path.Combine(Dir, "settings.json");

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(), new PointConverter() }
        };

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
            }
            catch { }

            var migrated = MigrateFromOldApp();
            migrated.Save();
            return migrated;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
                File.Move(tmp, FilePath, true);
            }
            catch { }
        }

        // First run after the upstream app: carry over its registry state and game list.
        private static Settings MigrateFromOldApp()
        {
            var s = new Settings();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key != null)
                {
                    int power = Int(key, "Power", 1);
                    s.Power = power switch { 0 => PowerMode.Quiet, 4 => PowerMode.Performance, 5 => PowerMode.Turbo, _ => PowerMode.Balanced };
                    int fan = Int(key, "Fan", 1);
                    s.Fan = fan switch { 2 => FanMode.Max, 3 => FanMode.Custom, _ => FanMode.Auto };
                    s.CpuCurve = FanCurve.Parse(key.GetValue("CpuCurve") as string) ?? s.CpuCurve;
                    s.GpuCurve = FanCurve.Parse(key.GetValue("GpuCurve") as string) ?? s.GpuCurve;
                    s.LightR = Math.Clamp(Int(key, "RGB_R", 0), 0, 255);
                    s.LightG = Math.Clamp(Int(key, "RGB_G", 150), 0, 255);
                    s.LightB = Math.Clamp(Int(key, "RGB_B", 255), 0, 255);
                    s.LightBrightness = Math.Clamp(Int(key, "Brightness", 100), 0, 100);
                    s.Light = Int(key, "RGB_Mode", 0) switch { 1 => LightEffect.Breathing, 2 => LightEffect.Rainbow, 3 => LightEffect.Wave, _ => LightEffect.Static };
                }

                string games = Path.Combine(Dir, "game_sync.json");
                if (File.Exists(games))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(games));
                    if (doc.RootElement.TryGetProperty("Profiles", out var list) && list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in list.EnumerateArray())
                        {
                            string exe = p.TryGetProperty("ExecutableName", out var e) ? e.GetString() ?? "" : "";
                            if (exe.Length > 0) s.Games.Add(exe);
                        }
                    }
                }
            }
            catch { }
            return s;
        }

        private static int Int(RegistryKey key, string name, int fallback)
        {
            object? raw = key.GetValue(name);
            return raw is int i ? i : raw != null && int.TryParse(raw.ToString(), out int p) ? p : fallback;
        }

        private sealed class PointConverter : JsonConverter<Point>
        {
            public override Point Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                var a = JsonSerializer.Deserialize<int[]>(ref reader) ?? new[] { 0, 0 };
                return new Point(a.Length > 0 ? a[0] : 0, a.Length > 1 ? a[1] : 0);
            }

            public override void Write(Utf8JsonWriter writer, Point p, JsonSerializerOptions options)
                => JsonSerializer.Serialize(writer, new[] { p.X, p.Y });
        }
    }
}
