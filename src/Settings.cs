using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Fizz
{
    [DataContract]
    internal sealed class StatItem
    {
        [DataMember(Order = 1)] public string Id;
        [DataMember(Order = 2)] public bool On;
        [DataMember(Order = 3)] public bool Label = true;
        [DataMember(Order = 4)] public bool Unit = true;

        public StatItem Clone() { return new StatItem { Id = Id, On = On, Label = Label, Unit = Unit }; }
    }

    [DataContract]
    internal sealed class Preset
    {
        [DataMember(Order = 1)] public string Name;
        [DataMember(Order = 2)] public string Layout = "Vertical";     // Vertical (default) | Horizontal
        [DataMember(Order = 3)] public float FontSize = 10.5f;          // points at 100% scaling
        [DataMember(Order = 4)] public int Opacity = 95;                // whole overlay, 20..100 %
        [DataMember(Order = 5)] public bool Background = true;
        [DataMember(Order = 6)] public string Accent = "#FF5C9E";
        [DataMember(Order = 7)] public List<StatItem> Items = new List<StatItem>();

        public bool Vertical { get { return string.Equals(Layout, "Vertical", StringComparison.OrdinalIgnoreCase); } }

        public Preset Clone(string newName)
        {
            var p = new Preset { Name = newName, Layout = Layout, FontSize = FontSize, Opacity = Opacity, Background = Background, Accent = Accent };
            foreach (var i in Items) p.Items.Add(i.Clone());
            return p;
        }
    }

    [DataContract]
    internal sealed class ThresholdSetting
    {
        [DataMember(Order = 1)] public string Id;
        [DataMember(Order = 2)] public bool Enabled;
        [DataMember(Order = 3)] public float Amber;
        [DataMember(Order = 4)] public float Red;
    }

    [DataContract]
    internal sealed class GameRule
    {
        [DataMember(Order = 1)] public string Exe;        // e.g. "valorant-win64-shipping.exe"
        [DataMember(Order = 2)] public bool Hide;         // never show the overlay in this game
        [DataMember(Order = 3)] public string Preset = ""; // "" = use the active preset
        [DataMember(Order = 4)] public bool Summary;      // show the performance summary when this game closes
    }

    [DataContract]
    internal sealed class SensorPick
    {
        [DataMember(Order = 1)] public string Identifier = ""; // LibreHardwareMonitor sensor identifier, "" = automatic
        [DataMember(Order = 2)] public string Hardware = "";   // fallback match by names
        [DataMember(Order = 3)] public string Name = "";
        public bool IsAuto { get { return string.IsNullOrEmpty(Identifier); } }
    }

    [DataContract]
    internal sealed class FizzSettings
    {
        [DataMember(Order = 1)] public int Version = 1;
        [DataMember(Order = 2)] public string Mode = "Auto";          // Auto (games only) | Always
        [DataMember(Order = 3)] public string Corner = "TopLeft";     // TopLeft | TopRight | BottomLeft | BottomRight
        [DataMember(Order = 4)] public int Margin = 12;               // px at 100% scaling
        [DataMember(Order = 5)] public string ActivePreset = "Full";
        [DataMember(Order = 6)] public List<Preset> Presets = new List<Preset>();
        [DataMember(Order = 7)] public List<ThresholdSetting> Thresholds = new List<ThresholdSetting>();
        [DataMember(Order = 8)] public List<GameRule> Games = new List<GameRule>();
        [DataMember(Order = 9)] public List<string> NotGames = new List<string>();
        [DataMember(Order = 10)] public SensorPick CpuTempSensor = new SensorPick();
        [DataMember(Order = 11)] public SensorPick GpuTempSensor = new SensorPick();
        [DataMember(Order = 12)] public bool StartWithWindows = true;
        [DataMember(Order = 13)] public int SampleMs = 500;
        [DataMember(Order = 14)] public string HotkeyToggle = "Ctrl+Alt+F";
        [DataMember(Order = 15)] public string HotkeyLayout = "Ctrl+Alt+L";
        [DataMember(Order = 16)] public string HotkeyPreset = "Ctrl+Alt+P";
        [DataMember(Order = 17)] public bool FirstRunDone;
        [DataMember(Order = 18)] public bool SessionSummary = true;
        [DataMember(Order = 19)] public bool CsvLog;
        [DataMember(Order = 20)] public string Screen = "Active";     // Active (screen of the window in front) | Main (always the main screen)
        [DataMember(Order = 21)] public bool PinInWindow = true;      // windowed games: true = inside the game window, false = screen corner

        [OnDeserializing]
        void OnDeserializing(StreamingContext c) { ApplyFieldDefaults(); }

        void ApplyFieldDefaults()
        {
            // DataContractSerializer skips constructors; reset field initializers for members missing from older files.
            Version = 1; Mode = "Auto"; Corner = "TopLeft"; Margin = 12; ActivePreset = "Full";
            Presets = new List<Preset>(); Thresholds = new List<ThresholdSetting>(); Games = new List<GameRule>();
            NotGames = new List<string>(); CpuTempSensor = new SensorPick(); GpuTempSensor = new SensorPick();
            StartWithWindows = true; SampleMs = 500; HotkeyToggle = "Ctrl+Alt+F"; HotkeyLayout = "Ctrl+Alt+L"; HotkeyPreset = "Ctrl+Alt+P";
            SessionSummary = true; Screen = "Active"; PinInWindow = true;
        }

        // ---------- helpers ----------

        public bool AlwaysShow { get { return string.Equals(Mode, "Always", StringComparison.OrdinalIgnoreCase); } }
        public bool MainScreenOnly { get { return string.Equals(Screen, "Main", StringComparison.OrdinalIgnoreCase); } }

        public Preset FindPreset(string name)
        {
            if (!string.IsNullOrEmpty(name))
                foreach (var p in Presets) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public Preset Active { get { return FindPreset(ActivePreset) ?? Presets[0]; } }

        public GameRule FindGame(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return null;
            foreach (var g in Games) if (string.Equals(g.Exe, exe, StringComparison.OrdinalIgnoreCase)) return g;
            return null;
        }

        public GameRule GetOrAddGame(string exe)
        {
            var g = FindGame(exe);
            if (g == null) { g = new GameRule { Exe = exe }; Games.Add(g); }
            return g;
        }

        public bool IsNotGame(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            foreach (var n in NotGames) if (string.Equals(n, exe, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public ThresholdSetting Threshold(Stat id)
        {
            string key = Stats.Get(id).Key;
            foreach (var t in Thresholds) if (t.Id == key) return t;
            return null;
        }

        // ---------- defaults & repair ----------

        static readonly string[] DefaultNotGames =
        {
            "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "opera_gx.exe", "vivaldi.exe", "arc.exe", "zen.exe",
            "vlc.exe", "mpc-hc64.exe", "mpc-be64.exe", "potplayermini64.exe", "mpv.exe", "Video.UI.exe", "Microsoft.Media.Player.exe",
            "Spotify.exe", "Discord.exe", "Teams.exe", "ms-teams.exe", "Zoom.exe", "POWERPNT.EXE", "obs64.exe",
            "explorer.exe", "ApplicationFrameHost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe",
            "LockApp.exe", "Code.exe", "devenv.exe", "WindowsTerminal.exe", "Claude.exe"
        };

        // Added in settings v2 (windowed-game detection): launchers, chat apps, capture tools, office apps.
        static readonly string[] NotGamesV2 =
        {
            "SnippingTool.exe", "ScreenClippingHost.exe", "ScreenSketch.exe", "PowerToys.exe", "PowerToys.ScreenRuler.exe",
            "steam.exe", "steamwebhelper.exe", "EpicGamesLauncher.exe", "EpicWebHelper.exe", "Battle.net.exe", "RiotClientUx.exe",
            "RiotClientServices.exe", "EADesktop.exe", "upc.exe", "GalaxyClient.exe", "XboxPcApp.exe", "msedgewebview2.exe",
            "WhatsApp.exe", "Telegram.exe", "Notion.exe", "Figma.exe", "notepad.exe", "WINWORD.EXE", "EXCEL.EXE", "ONENOTE.EXE",
            "OUTLOOK.EXE", "olk.exe", "Taskmgr.exe", "SystemSettings.exe", "mmc.exe", "Juice.exe", "Pour.exe", "Fizz.exe",
            "LibreHardwareMonitor.exe", "MSI Center.exe", "MSI.CentralServer.exe", "Photos.exe", "obs32.exe", "ShareX.exe"
        };

        public static FizzSettings CreateDefault()
        {
            var s = new FizzSettings();
            s.Repair();
            return s;
        }

        static Preset MakePreset(string name, params Stat[] on)
        {
            var p = new Preset { Name = name };
            var onSet = new HashSet<Stat>(on);
            foreach (var st in on) p.Items.Add(NewItem(st, true));
            foreach (var d in Stats.All) if (!onSet.Contains(d.Id)) p.Items.Add(NewItem(d.Id, false));
            return p;
        }

        static StatItem NewItem(Stat id, bool on)
        {
            var d = Stats.Get(id);
            return new StatItem { Id = d.Key, On = on, Label = true, Unit = d.DefaultUnit };
        }

        /// <summary>Makes any loaded settings safe to use: fills gaps, drops junk, clamps ranges.</summary>
        public void Repair()
        {
            if (Presets == null) Presets = new List<Preset>();
            if (Thresholds == null) Thresholds = new List<ThresholdSetting>();
            if (Games == null) Games = new List<GameRule>();
            if (NotGames == null) NotGames = new List<string>();
            if (CpuTempSensor == null) CpuTempSensor = new SensorPick();
            if (GpuTempSensor == null) GpuTempSensor = new SensorPick();
            Presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            Games.RemoveAll(g => g == null || string.IsNullOrWhiteSpace(g.Exe));
            NotGames.RemoveAll(string.IsNullOrWhiteSpace);

            if (Presets.Count == 0)
            {
                Presets.Add(MakePreset("Full", Stat.Fps, Stat.Low1, Stat.FrameTime, Stat.GpuTemp, Stat.GpuLoad, Stat.Vram,
                    Stat.CpuTemp, Stat.CpuLoad, Stat.Ram));
                Presets.Add(MakePreset("Minimal", Stat.Fps));
                Presets.Add(MakePreset("Temps", Stat.Fps, Stat.GpuTemp, Stat.CpuTemp));
            }
            if (!FirstRunDone && NotGames.Count == 0) NotGames.AddRange(DefaultNotGames);
            if (Version < 2)
            {
                foreach (var n in NotGamesV2) if (!IsNotGame(n)) NotGames.Add(n);
                Version = 2;
            }

            foreach (var p in Presets)
            {
                if (p.Items == null) p.Items = new List<StatItem>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                p.Items.RemoveAll(i => { Stat x; return i == null || !Stats.TryParse(i.Id, out x) || !seen.Add(i.Id); });
                foreach (var d in Stats.All) if (!seen.Contains(d.Key)) p.Items.Add(NewItem(d.Id, false));
                if (p.FontSize < 7 || p.FontSize > 28 || float.IsNaN(p.FontSize)) p.FontSize = 10.5f;
                p.Opacity = Math.Max(20, Math.Min(100, p.Opacity));
                if (p.Layout != "Horizontal") p.Layout = "Vertical";
                System.Drawing.Color c;
                if (!TryColor(p.Accent, out c)) p.Accent = "#FF5C9E";
            }
            if (FindPreset(ActivePreset) == null) ActivePreset = Presets[0].Name;

            foreach (var d in Stats.All)
            {
                if (!d.HasThreshold && d.Id != Stat.PresentMode) continue;
                if (Threshold(d.Id) == null)
                    Thresholds.Add(new ThresholdSetting { Id = d.Key, Enabled = d.DefaultColors || d.Id == Stat.PresentMode, Amber = d.DefaultAmber, Red = d.DefaultRed });
            }

            if (Mode != "Always") Mode = "Auto";
            if (Screen != "Main") Screen = "Active";
            if (Corner != "TopRight" && Corner != "BottomLeft" && Corner != "BottomRight") Corner = "TopLeft";
            Margin = Math.Max(0, Math.Min(200, Margin));
            SampleMs = SampleMs <= 290 ? 250 : SampleMs <= 415 ? 333 : SampleMs <= 750 ? 500 : 1000; // 4x, 3x, 2x, 1x per second
            if (string.IsNullOrWhiteSpace(HotkeyToggle)) HotkeyToggle = "Ctrl+Alt+F";
            if (string.IsNullOrWhiteSpace(HotkeyLayout)) HotkeyLayout = "Ctrl+Alt+L";
            if (string.IsNullOrWhiteSpace(HotkeyPreset)) HotkeyPreset = "Ctrl+Alt+P";
        }

        public static bool TryColor(string hex, out System.Drawing.Color c)
        {
            c = System.Drawing.Color.Empty;
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.Trim().TrimStart('#');
            int v;
            if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            c = System.Drawing.Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
            return true;
        }

        // ---------- persistence ----------

        public static string FilePath { get { return Path.Combine(Log.Dir, "settings.json"); } }

        public static FizzSettings Load()
        {
            string path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    using (var fs = File.OpenRead(path))
                    {
                        var s = (FizzSettings)new DataContractJsonSerializer(typeof(FizzSettings)).ReadObject(fs);
                        if (s != null) { s.Repair(); return s; }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings.Load", ex);
                try { File.Copy(path, Path.Combine(Log.Dir, "settings.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true); } catch { }
            }
            return CreateDefault();
        }

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(FizzSettings)).WriteObject(w, this);
                    w.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static FizzSettings FromJson(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var s = (FizzSettings)new DataContractJsonSerializer(typeof(FizzSettings)).ReadObject(ms);
                s.Repair();
                return s;
            }
        }

        /// <summary>Atomic save: write a temp file, then swap it in. A crash mid-save can't corrupt settings.</summary>
        public void Save()
        {
            try
            {
                string path = FilePath, tmp = path + ".tmp";
                File.WriteAllText(tmp, ToJson(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception ex) { Log.Error("Settings.Save", ex); }
        }
    }
}
