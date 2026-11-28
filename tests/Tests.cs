using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace Fizz.Tests
{
    static class T
    {
        static int pass, fail;
        public static void Ok(bool cond, string name)
        {
            if (cond) pass++; else { fail++; Console.WriteLine("FAIL: " + name); }
        }
        public static void Near(float a, float b, float tol, string name)
        {
            Ok(!float.IsNaN(a) && Math.Abs(a - b) <= tol, name + " (got " + a + ", want " + b + "±" + tol + ")");
        }

        // Header exactly as PresentMon 2.6 writes it with Fizz's flags (display tracking on, no GPU/input tracking, default metrics).
        const string Header = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode," +
                              "TimeInSeconds,MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI,MsRenderPresentLatency,MsUntilDisplayed," +
                              "CPUStartTimeInSeconds,MsBetweenAppStart,MsCPUBusy,MsCPUWait,MsAnimationError,AnimationTime,MsFlipDelay";

        static string Row(string app, int pid, ulong swap, string mode, string ft)
        {
            return app + "," + pid + ",0x" + swap.ToString("X") + ",DXGI,1,0,0," + mode + ",12.3456,NA," + ft + "," + ft + ",0.1234,5.1234,8.0000,12.3456," + ft + ",4.0,2.9,0.0,12.3,0.5";
        }

        static int Main()
        {
            var sw = Stopwatch.StartNew();
            Parser();
            Splitter();
            Frames();
            Session();
            Formatting();
            SettingsTests();
            ThemeTests();
            Hotkeys();
            OverlayStaysPut();
            GameExceptions();
            Perf();
            Console.WriteLine("\n" + pass + " passed, " + fail + " failed (" + sw.ElapsedMilliseconds + " ms)");
            return fail == 0 ? 0 : 1;
        }

        static bool ParseOne(PmCsvParser p, string line, out PmRow r)
        {
            var b = Encoding.ASCII.GetBytes(line);
            return p.ParseLine(b, 0, b.Length, out r);
        }

        static void Parser()
        {
            var p = new PmCsvParser();
            PmRow r;
            Ok(!ParseOne(p, Row("game.exe", 5, 1, "Composed: Flip", "6.9"), out r), "rows before header are ignored");
            Ok(!ParseOne(p, Header, out r) && p.HasHeader, "header recognised");
            Ok(ParseOne(p, Row("eldenring.exe", 12345, 0x1A2B3C4D5E6FUL, "Hardware: Independent Flip", "6.9444"), out r), "row parses");
            Ok(r.Pid == 12345, "pid");
            Ok(r.Swap == 0x1A2B3C4D5E6FUL, "swapchain hex");
            Near(r.FrameMs, 6.9444f, 0.0001f, "frametime from MsBetweenPresents");
            Ok(r.Mode == PresentModes.IndependentFlip, "present mode iFlip");
            ParseOne(p, Row("a.exe", 1, 2, "Hardware Composed: Independent Flip", "8"), out r);
            Ok(r.Mode == PresentModes.HwComposedIndependentFlip, "present mode MPO");
            ParseOne(p, Row("a.exe", 1, 2, "Hardware: Legacy Flip", "8"), out r);
            Ok(r.Mode == PresentModes.LegacyFlip && PresentModes.IsExclusive(r.Mode), "legacy flip = exclusive");
            ParseOne(p, Row("a.exe", 1, 2, "Composed: Copy with GPU GDI", "8") + "\r", out r);
            Ok(r.Mode == PresentModes.ComposedCopyGpu, "CR stripped, copy mode");
            Ok(!ParseOne(p, Row("a.exe", 1, 2, "Composed: Flip", "NA"), out r), "NA frametime rejected");
            Ok(ParseOne(p, Row("my,weird,game.exe", 77, 3, "Composed: Flip", "16.6667"), out r) && r.Pid == 77 && Math.Abs(r.FrameMs - 16.6667f) < 0.001f, "exe name with commas");
            Ok(!ParseOne(p, "garbage line", out r), "garbage ignored");
            Ok(!ParseOne(p, "", out r), "empty ignored");
            Ok(!ParseOne(p, "error: failed to start trace session (access denied).", out r), "error text ignored");
            Ok(ParseOne(p, Row("a.exe", 9, 2, "Composed: Flip", "1.5e1"), out r) && Math.Abs(r.FrameMs - 15f) < 0.001f, "exponent number");

            var v2 = new PmCsvParser();
            ParseOne(v2, "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,CPUStartTime,FrameTime,CPUBusy,CPUWait,DisplayLatency,DisplayedTime,AnimationError,AnimationTime,MsFlipDelay", out r);
            Ok(ParseOne(v2, "x.exe,42,0x10,DXGI,0,0,1,Composed: Flip,100.5,4.1667,3.0,1.1,10,4.1,0,0,0", out r) && r.Pid == 42 && Math.Abs(r.FrameMs - 4.1667f) < 0.0001f, "v2 FrameTime column");

            var v1 = new PmCsvParser();
            ParseOne(v1, "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,WasBatched,DwmNotified,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msUntilRenderComplete,msUntilDisplayed", out r);
            Ok(v1.HasHeader && ParseOne(v1, "a.exe,3,0x1,DXGI,1,0,0,Composed: Flip,0,0,0,1.0,0.1,8.3333,4,9", out r) && Math.Abs(r.FrameMs - 8.3333f) < 0.001f, "PresentMon 1.x header also works");
        }

        static void Splitter()
        {
            var lines = new List<string>();
            var sp = new LineSplitter();
            LineSplitter.LineHandler h = (b, s, l) => lines.Add(Encoding.ASCII.GetString(b, s, l));
            var data = Encoding.ASCII.GetBytes("one\r\ntwo\nthr");
            sp.Feed(data, data.Length, h);
            var d2 = Encoding.ASCII.GetBytes("ee\n");
            sp.Feed(d2, d2.Length, h);
            Ok(lines.Count == 3 && lines[0] == "one\r" && lines[1] == "two" && lines[2] == "three", "ascii lines across chunks");

            // UTF-16 with BOM, fed in odd-sized chunks
            lines.Clear();
            var sp16 = new LineSplitter();
            var u = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(Header + "\r\n" + Row("g.exe", 5, 7, "Composed: Flip", "10") + "\r\n"));
            int pos = 0, chunk = 3;
            while (pos < u.Length) { int n = Math.Min(chunk, u.Length - pos); var c = new byte[n]; Array.Copy(u, pos, c, 0, n); sp16.Feed(c, n, h); pos += n; chunk = chunk == 3 ? 5 : 3; }
            var p = new PmCsvParser();
            PmRow r;
            ParseOne(p, lines[0].TrimEnd('\r'), out r);
            Ok(lines.Count == 2 && p.HasHeader && ParseOne(p, lines[1], out r) && r.Pid == 5 && r.FrameMs == 10f, "UTF-16 output with odd chunk boundaries");
        }

        static byte[] Concat(this byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }

        static void Feed(FrameStats fs, int pid, ulong swap, float ms, int count, ref long t, byte mode = PresentModes.IndependentFlip)
        {
            long step = (long)(ms / 1000.0 * fs.Freq);
            for (int i = 0; i < count; i++) { t += step; fs.Add(new PmRow { Pid = pid, Swap = swap, FrameMs = ms, Mode = mode }, t); }
        }

        static void Frames()
        {
            var fs = new FrameStats();
            long t = 1000000;
            Ok(float.IsNaN(fs.Compute(t).Fps), "no data -> NaN");
            fs.SetTarget(100);
            Feed(fs, 100, 1, 1000f / 144f, 144 * 3, ref t);
            var res = fs.Compute(t);
            Near(res.Fps, 144f, 0.5f, "steady 144 fps");
            Near(res.FrameTime, 6.94f, 0.02f, "frametime 6.94");
            Near(res.Low1, 144f, 0.5f, "1% low equals fps when steady");
            Ok(res.Mode == PresentModes.IndependentFlip, "mode reported");

            // 1% of frames are 30 ms stutters
            var fs2 = new FrameStats(); fs2.SetTarget(7); long t2 = 1000000;
            for (int s = 0; s < 8; s++) { Feed(fs2, 7, 1, 10f, 99, ref t2); Feed(fs2, 7, 1, 30f, 1, ref t2); }
            var r2 = fs2.Compute(t2);
            Near(r2.Low1, 1000f / 30f, 0.5f, "1% low catches stutter");
            Ok(r2.Fps > 90 && r2.Fps < 101, "avg fps with stutter ~98 (" + r2.Fps + ")");

            // other processes ignored, dominant swapchain wins
            var fs3 = new FrameStats(); fs3.SetTarget(1); long t3 = 1000000;
            for (int i = 0; i < 120; i++)
            {
                t3 += fs3.Freq / 120;
                fs3.Add(new PmRow { Pid = 1, Swap = 0xA, FrameMs = 1000f / 120f, Mode = 3 }, t3);
                if (i % 4 == 0) fs3.Add(new PmRow { Pid = 1, Swap = 0xB, FrameMs = 1000f / 30f, Mode = 4 }, t3);   // launcher overlay chain
                fs3.Add(new PmRow { Pid = 2, Swap = 0xC, FrameMs = 1000f / 60f, Mode = 4 }, t3);                   // browser
            }
            var r3 = fs3.Compute(t3);
            Near(r3.Fps, 120f, 1f, "dominant swapchain only (no mixing with 30 fps chain or other pid)");
            Ok(fs3.IsPresenting(2, t3) && fs3.IsPresenting(1, t3) && !fs3.IsPresenting(3, t3), "IsPresenting per pid");
            Ok(fs3.LastModeOf(2, t3) == 4, "last mode of other pid");

            // windowed-game detection uses the present rate
            Near(fs3.PresentRate(1, t3), 150f, 25f, "present rate of a 120+30 fps process");
            Near(fs3.PresentRate(2, t3), 120f, 20f, "present rate counts every frame the pid presents");
            var fs6 = new FrameStats(); long t6 = 1000000;
            Feed(fs6, 5, 1, 1000f / 60f, 60 * 5, ref t6);
            Near(fs6.PresentRate(5, t6), 60f, 6f, "60 fps windowed app rate");
            Ok(fs6.PresentRate(5, t6 + fs6.Freq * 2) == 0, "rate drops to 0 when it stops drawing");
            Feed(fs6, 6, 1, 100f, 30, ref t6);
            Ok(fs6.PresentRate(6, t6) < 25, "10 fps app stays below the windowed-game threshold");

            // stale: nothing for 3 s
            var r4 = fs3.Compute(t3 + fs3.Freq * 3);
            Ok(float.IsNaN(r4.Fps), "paused game -> --");
            Ok(!fs3.IsPresenting(1, t3 + fs3.Freq * 3), "not presenting after 3 s");

            // very low fps (loading screen 0.8 fps)
            var fs5 = new FrameStats(); fs5.SetTarget(9); long t5 = 1000000;
            Feed(fs5, 9, 1, 1250f, 4, ref t5);
            var r5 = fs5.Compute(t5);
            Near(r5.Fps, 0.8f, 0.05f, "sub-1 fps uses 2 s window");

            // target switch clears history
            fs.SetTarget(200);
            Ok(float.IsNaN(fs.Compute(t).Fps), "new target starts clean");
        }

        static void Session()
        {
            var s = new SessionStats();
            s.Reset("x.exe");
            for (int i = 0; i < 9900; i++) s.AddFrame(10f);
            for (int i = 0; i < 100; i++) s.AddFrame(40f);
            Near(s.AvgFps, 1000f * 10000 / (9900 * 10 + 100 * 40), 0.01f, "session avg fps");
            Near(s.Low1, 25f, 0.1f, "session 1% low = avg of slowest 1% (all 40 ms)");
            s.AddFrame(50f);
            Near(s.Low1, 1000f / ((50f + 99 * 40f) / 100f), 0.1f, "session 1% low includes the worst frame");
            s.AddTemps(70, float.NaN); s.AddTemps(82, 90); s.AddTemps(75, 88);
            Ok(s.MaxGpuTemp == 82 && s.MaxCpuTemp == 90, "max temps");
            Near(s.AvgGpuTemp, 75.6667f, 0.01f, "avg gpu temp over samples");
            Near(s.AvgCpuTemp, 89f, 0.01f, "avg cpu temp ignores missing samples");
        }

        static void Formatting()
        {
            var s = new Snapshot { GpuTemp = 64.4f, CpuTemp = 88.6f, GpuLoad = 57.2f, Fps = 143.6f, Low1 = 98.2f, FrameTime = 6.96f,
                RamUsedMb = 12.4f * 1024, RamTotalMb = 31.6f * 1024, VramUsedMb = 5.1f * 1024, VramTotalMb = 8188, NetDownBps = 1.5f * 1048576, NetUpBps = 120 * 1024, GpuClock = 1980, CpuClockMhz = 4250, GpuPower = 85.4f };
            Ok(Stats.Format(Stat.GpuTemp, s, true) == "64°C" && Stats.Format(Stat.GpuTemp, s, false) == "64°", "temp unit on/off");
            Ok(Stats.Format(Stat.Fps, s, false) == "144", "fps rounds");
            Ok(Stats.Format(Stat.FrameTime, s, true) == "7.0 ms", "frametime");
            Ok(Stats.Format(Stat.Ram, s, true) == "12.4/31.6 GB", "ram");
            Ok(Stats.Format(Stat.Vram, s, false) == "5.1/8.0", "vram no unit (" + Stats.Format(Stat.Vram, s, false) + ")");
            Ok(Stats.Format(Stat.Net, s, true) == "↓1.5 ↑0.1 MB/s", "net MB/s (" + Stats.Format(Stat.Net, s, true) + ")");
            Ok(Stats.Format(Stat.CpuClock, s, true) == "4.3 GHz" || Stats.Format(Stat.CpuClock, s, true) == "4.2 GHz", "cpu clock");
            Ok(Stats.Format(Stat.GpuClock, s, true) == "1980 MHz", "gpu clock");
            Ok(Stats.Format(Stat.CpuLoad, new Snapshot(), true) == "--", "NaN -> --");
            s.PresentMode = PresentModes.ComposedFlip;
            Ok(Stats.Format(Stat.PresentMode, s, true) == "Composed", "present mode text");

            var tGpu = new ThresholdSetting { Enabled = true, Amber = 75, Red = 85 };
            Ok(Stats.ToneFor(Stat.GpuTemp, s, tGpu) == Tone.Good, "64°C green");
            s.GpuTemp = 75; Ok(Stats.ToneFor(Stat.GpuTemp, s, tGpu) == Tone.Warn, "75°C amber");
            s.GpuTemp = 90; Ok(Stats.ToneFor(Stat.GpuTemp, s, tGpu) == Tone.Bad, "90°C red");
            var tFps = new ThresholdSetting { Enabled = true, Amber = 60, Red = 30 };
            Ok(Stats.ToneFor(Stat.Fps, s, tFps) == Tone.Normal, "144 fps normal");
            s.Fps = 45; Ok(Stats.ToneFor(Stat.Fps, s, tFps) == Tone.Warn, "45 fps amber");
            s.Fps = 20; Ok(Stats.ToneFor(Stat.Fps, s, tFps) == Tone.Bad, "20 fps red");
            Ok(Stats.ToneFor(Stat.Fps, s, new ThresholdSetting { Enabled = false }) == Tone.Normal, "colors off");
            Ok(Stats.ToneFor(Stat.Ram, s, new ThresholdSetting { Enabled = true, Amber = 85, Red = 95 }) == Tone.Normal, "ram 39% normal");
            Ok(Stats.ToneFor(Stat.PresentMode, s, new ThresholdSetting { Enabled = true }) == Tone.Warn, "composed amber");

            // ValueKey must change whenever the text changes (otherwise the overlay would show stale text)
            var rnd = new Random(1);
            int bad = 0;
            foreach (var d in Stats.All)
            {
                if (d.Id == Stat.Clock || d.Id == Stat.Game) continue;
                var seen = new Dictionary<long, string>();
                for (int i = 0; i < 20000; i++)
                {
                    var x = new Snapshot();
                    float v = (float)(rnd.NextDouble() * (rnd.Next(3) == 0 ? 10 : 3000));
                    if (rnd.Next(30) == 0) v = float.NaN;
                    x.Fps = x.Low1 = x.FrameTime = x.GpuTemp = x.GpuLoad = x.GpuClock = x.GpuPower = x.CpuTemp = x.CpuLoad = x.CoreMax = x.CpuPower = x.Disk = v;
                    x.CpuClockMhz = v * 3; x.VramUsedMb = v * 3; x.VramTotalMb = 8188; x.RamUsedMb = v * 10; x.RamTotalMb = 32000;
                    x.NetDownBps = v * 1000; x.NetUpBps = v * 37; x.PresentMode = (byte)rnd.Next(8);
                    bool unit = rnd.Next(2) == 0;
                    long key = Stats.ValueKey(d.Id, x, unit);
                    string text = Stats.Format(d.Id, x, unit), prev;
                    if (seen.TryGetValue(key, out prev) && prev != text) { bad++; if (bad < 5) Console.WriteLine("  key clash " + d.Key + ": '" + prev + "' vs '" + text + "'"); }
                    seen[key] = text;
                }
            }
            Ok(bad == 0, "value keys never hide a text change (" + bad + " clashes)");
        }

        static void ThemeTests()
        {
            Ok(Theme.ParseMode("dark") == ThemeMode.Dark && Theme.ParseMode("OLED") == ThemeMode.Oled && Theme.ParseMode(" light ") == ThemeMode.Light, "theme names parse");
            Ok(Theme.ParseMode(null) == ThemeMode.Dark && Theme.ParseMode("purple") == ThemeMode.Dark, "unknown theme is Dark");
            foreach (ThemeMode m in Enum.GetValues(typeof(ThemeMode))) Ok(Theme.ParseMode(Theme.ModeName(m)) == m, "theme name round trip " + m);

            var s = FizzSettings.CreateDefault();
            Ok(s.UiTheme == "Dark", "new settings use Dark");
            Ok(FizzSettings.FromJson("{\"Mode\":\"Auto\"}").UiTheme == "Dark", "older settings files (no Theme) stay Dark");
            Ok(FizzSettings.FromJson("{\"Theme\":\"OLED\"}").UiTheme == "OLED", "OLED loads");
            Ok(FizzSettings.FromJson("{\"Theme\":\"light\"}").UiTheme == "Light", "Light loads (any case)");
            Ok(FizzSettings.FromJson("{\"Theme\":\"Neon\"}").UiTheme == "Dark", "bad theme repaired to Dark");
            s.UiTheme = "Light";
            string json = s.ToJson();
            Ok(json.Contains("\"Theme\": \"Light\"") || json.Contains("\"Theme\":\"Light\""), "theme saved as \"Theme\"");
            Ok(FizzSettings.FromJson(json).UiTheme == "Light", "theme survives a save");

            // Dark is exactly the old palette
            Theme.SetMode(ThemeMode.Dark);
            Ok(Theme.Bg == Color.FromArgb(32, 32, 32) && Theme.Card == Color.FromArgb(46, 46, 46) && Theme.Nav == Color.FromArgb(27, 27, 27)
               && Theme.Text == Color.FromArgb(242, 242, 242) && Theme.Sub == Color.FromArgb(165, 165, 165) && Theme.Border == Color.FromArgb(64, 64, 64), "Dark palette unchanged");
            Ok(Theme.Hairline.IsEmpty && Theme.AccentText == Theme.Accent, "Dark: no hairlines, plain accent");
            Theme.SetMode(ThemeMode.Oled);
            Ok(Theme.Bg.ToArgb() == Color.Black.ToArgb() && Theme.Nav.ToArgb() == Color.Black.ToArgb() && Theme.Hairline == Color.FromArgb(0x1F, 0x1F, 0x24), "OLED: pure black, #1F1F24 hairlines");
            Theme.SetMode(ThemeMode.Light);
            Ok(Theme.IsLight && Theme.Bg == Color.FromArgb(0xF3, 0xF3, 0xF3) && Theme.Card.ToArgb() == Color.White.ToArgb() && Theme.Text == Color.FromArgb(0x1B, 0x1B, 0x1B), "Light: Windows 11 light");
            Ok(Lum(Theme.AccentText) < Lum(Theme.Accent), "Light: accent text darker than the accent");
            foreach (var m in new[] { ThemeMode.Dark, ThemeMode.Oled, ThemeMode.Light })
            {
                Theme.SetMode(m);
                Ok(Contrast(Theme.Text, Theme.Bg) >= 7 && Contrast(Theme.Text, Theme.Card) >= 7, m + ": text readable");
                Ok(Contrast(Theme.Sub, Theme.Bg) >= 4.5 && Contrast(Theme.Sub, Theme.Card) >= 4.5, m + ": secondary text readable");
                Ok(Contrast(Theme.Warn, Theme.Bg) >= 3 && Contrast(Theme.Red, Theme.Bg) >= 3, m + ": amber and red readable");
            }
            Theme.SetMode(ThemeMode.Dark);
        }

        static double Lum(Color c)
        {
            Func<int, double> ch = v => { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); };
            return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
        }
        static double Contrast(Color a, Color b) { double x = Lum(a), y = Lum(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }

        static void SettingsTests()
        {
            var s = FizzSettings.CreateDefault();
            Ok(s.Presets.Count == 3 && s.Active.Name == "Full", "default presets");
            Ok(s.Presets.TrueForAll(p => p.Vertical), "new presets are vertical");
            Ok(s.Screen == "Active" && !s.MainScreenOnly && s.PinInWindow, "placement defaults unchanged (follow window, inside windowed games)");
            Ok(s.Active.Items.Count == Stats.All.Length, "every stat present in preset");
            Ok(s.Threshold(Stat.GpuTemp).Amber == 75 && s.Threshold(Stat.GpuTemp).Red == 85, "gpu temp thresholds 75/85");
            Ok(s.IsNotGame("chrome.exe") && s.IsNotGame("CHROME.EXE") && !s.IsNotGame("eldenring.exe"), "browsers aren't games");
            string json = s.ToJson();
            var back = FizzSettings.FromJson(json);
            Ok(back.ToJson() == json, "JSON round trip is lossless");
            Ok(json.Contains("\n  "), "JSON is indented (readable)");

            var junk = FizzSettings.FromJson("{\"Mode\":\"Sideways\",\"Presets\":[{\"Name\":\"X\",\"FontSize\":999,\"Items\":[{\"Id\":\"fps\",\"On\":true},{\"Id\":\"nope\",\"On\":true},{\"Id\":\"fps\",\"On\":false}]}],\"ActivePreset\":\"Missing\",\"SampleMs\":7}");
            Ok(junk.Mode == "Auto", "bad mode repaired");
            Ok(junk.Presets.Count == 1 && junk.ActivePreset == "X", "missing active preset repaired");
            Ok(junk.Active.FontSize == 10.5f, "font size clamped");
            Ok(junk.Active.Items.Count == Stats.All.Length && junk.Active.Items[0].Id == "fps" && junk.Active.Items[0].On, "unknown/duplicate stats dropped, rest added");
            Ok(junk.SampleMs == 250, "sample rate snapped");
            Ok(junk.HotkeyToggle == "Ctrl+Alt+F", "missing fields get defaults");
            Ok(junk.Thresholds.Count > 5, "thresholds filled");
            Ok(junk.Screen == "Active" && junk.PinInWindow, "older settings files get the placement defaults");
            var placed = FizzSettings.FromJson("{\"Screen\":\"Main\",\"PinInWindow\":false,\"Presets\":[{\"Name\":\"H\",\"Layout\":\"Horizontal\"}]}");
            Ok(placed.MainScreenOnly && !placed.PinInWindow, "placement options load");
            Ok(!placed.Active.Vertical, "a saved horizontal preset stays horizontal");
            Ok(FizzSettings.FromJson("{\"Screen\":\"Moon\"}").Screen == "Active", "bad screen value repaired");

            Ok(s.Version == 2 && s.IsNotGame("SnippingTool.exe") && s.IsNotGame("steamwebhelper.exe"), "fresh settings include v2 non-games");
            var old = FizzSettings.FromJson("{\"Version\":1,\"FirstRunDone\":true,\"NotGames\":[\"chrome.exe\",\"mygame-launcher.exe\"]}");
            Ok(old.Version == 2 && old.IsNotGame("SnippingTool.exe") && old.IsNotGame("mygame-launcher.exe") && old.IsNotGame("chrome.exe"), "v1 settings migrate: keeps user entries, adds new ones");
            var again = FizzSettings.FromJson(old.ToJson());
            Ok(again.NotGames.Count == old.NotGames.Count, "migration runs once (no duplicates)");

            var a = new Native.RECT { Left = 0, Top = 0, Right = 100, Bottom = 30 };
            Ok(Native.Intersects(a, new Native.RECT { Left = 50, Top = 10, Right = 500, Bottom = 400 }), "rects overlap");
            Ok(!Native.Intersects(a, new Native.RECT { Left = 2560, Top = 0, Right = 4480, Bottom = 1080 }), "window on the other monitor doesn't cover the overlay");

            var g = s.GetOrAddGame("Valorant-Win64-Shipping.exe");
            g.Hide = true;
            Ok(s.FindGame("valorant-win64-shipping.exe").Hide, "game rule lookup is case-insensitive");
        }

        static void Hotkeys()
        {
            uint m, v;
            Ok(HubWindow.ParseHotkey("Ctrl+Alt+F", out m, out v) && m == (Native.MOD_CONTROL | Native.MOD_ALT) && v == 0x46, "Ctrl+Alt+F");
            Ok(HubWindow.ParseHotkey("ctrl + shift + f10", out m, out v) && v == 0x79, "ctrl+shift+f10");
            Ok(HubWindow.ParseHotkey("Alt+1", out m, out v) && v == 0x31, "Alt+1");
            Ok(!HubWindow.ParseHotkey("F", out m, out v), "needs a modifier");
            Ok(!HubWindow.ParseHotkey("Ctrl+Alt+Banana", out m, out v), "rejects junk");
        }

        static Native.RECT R(int l, int t, int r, int b) { return new Native.RECT { Left = l, Top = t, Right = r, Bottom = b }; }

        // Bug: with "Always on", after alt-tabbing away from a windowed game the overlay bounced between the
        // game window and the screen corner 4x a second, because the "is it covered?" check used its current spot.
        static void OverlayStaysPut()
        {
            var monitor = R(0, 0, 1920, 1080);
            var game = R(400, 200, 1520, 880);            // windowed game in the middle of the screen
            var front = R(300, 150, 900, 600);            // the app you switched to: covers the game's top-left, not the screen's
            var size = new System.Drawing.Size(300, 30);

            var onGame = OverlayWindow.PlacedRect("TopLeft", 10, game, size, 1f);
            var onScreen = OverlayWindow.PlacedRect("TopLeft", 10, monitor, size, 1f);
            Ok(onGame.Left == 410 && onGame.Top == 210 && onGame.Right == 710 && onGame.Bottom == 240, "overlay pinned inside the game window");
            var br = OverlayWindow.PlacedRect("BottomRight", 10, monitor, size, 1.5f);
            Ok(br.Right == 1905 && br.Bottom == 1065, "bottom-right corner with 150% scaling");

            // simulate ticks: the overlay goes on the game if StaysOnGame says so, else to the screen corner ("Always on")
            var pos = onGame;
            int moves = 0;
            for (int tick = 0; tick < 12; tick++)
            {
                bool stays = FizzApp.StaysOnGame(front, false, false, onGame);
                var next = stays ? onGame : onScreen;
                if (next.Left != pos.Left || next.Top != pos.Top) moves++;
                pos = next;
            }
            Ok(moves <= 1, "overlay moves at most once after alt-tab, never back and forth (moved " + moves + "x)");

            Ok(FizzApp.StaysOnGame(R(1920, 0, 3840, 1080), false, false, onGame), "app on the other monitor: overlay stays on the game");
            Ok(!FizzApp.StaysOnGame(front, false, false, onGame), "app over the overlay's spot: overlay leaves the game");
            Ok(FizzApp.StaysOnGame(front, true, false, onGame), "desktop in front: overlay stays on the game");
            Ok(!FizzApp.StaysOnGame(R(1920, 0, 3840, 1080), false, false, new Native.RECT()), "no game area: don't claim the overlay is on the game");

            // Glue: alt-tab or a window on top must not switch the overlay off or send it to the screen corner
            Ok(FizzApp.GluedToGame(true, true), "focused game: glued");
            Ok(FizzApp.GluedToGame(false, true), "unfocused or covered game: still glued");
            Ok(!FizzApp.GluedToGame(false, false), "minimized game: not glued");

            IntPtr above = new IntPtr(1234);
            Ok(OverlayWindow.InsertAfter(above, false) == above, "lands right under the window above the game, so right above the game");
            Ok(OverlayWindow.InsertAfter(above, true) == Native.HWND_TOP, "never inherits always-on-top from a window above the game");
            Ok(OverlayWindow.InsertAfter(IntPtr.Zero, false) == Native.HWND_TOP, "game at the very top: overlay goes above it");
        }

        static void GameExceptions()
        {
            Ok(SettingsForm.CleanExe("eldenring") == "eldenring.exe", "exe added when typed without .exe");
            Ok(SettingsForm.CleanExe(" \"C:\\Games\\Elden Ring\\eldenring.exe\" ") == "eldenring.exe", "full path in quotes -> exe name");
            Ok(SettingsForm.CleanExe("D:/Games/cs2.EXE") == "cs2.EXE", "forward slashes, keeps .EXE");
            Ok(SettingsForm.CleanExe("   ") == "" && SettingsForm.CleanExe(null) == "", "empty ignored");

            var apps = OpenApps.Clean(new[]
            {
                new OpenApp { Exe = "eldenring.exe", Title = "ELDEN RING" },
                new OpenApp { Exe = "ELDENRING.EXE", Title = "second window" },
                new OpenApp { Exe = "explorer.exe", Title = "Downloads" },
                new OpenApp { Exe = "", Title = "no exe" },
                new OpenApp { Exe = "Discord.exe", Title = "#general - Discord" },
                new OpenApp { Exe = "x.exe", Title = new string('a', 80) },
                null
            });
            Ok(apps.Count == 3, "one entry per exe, shells and blanks skipped (got " + apps.Count + ")");
            Ok(apps[0].Exe == "Discord.exe" && apps[1].Exe == "x.exe" && apps[2].Exe == "eldenring.exe", "sorted by title");
            Ok(apps[1].Title.Length == 48 && apps[1].Title.EndsWith("…"), "long titles shortened");

            Ok(AppFonts.Regular != null && AppFonts.Regular.Name == "Outfit", "Outfit regular loads from the exe");
            Ok(AppFonts.SemiBold != null && AppFonts.SemiBold.Name.StartsWith("Outfit"), "Outfit semibold loads from the exe");
            using (var f = Theme.Ui(10f)) Ok(f.Name == "Outfit", "window font is Outfit (" + f.Name + ")");
            Ok(FizzSettings.FromJson("{\"SampleMs\":333}").SampleMs == 333, "3x per second kept");
            Ok(FizzSettings.FromJson("{\"SampleMs\":350}").SampleMs == 333, "near 3x snaps to 3x");
            Ok(FizzSettings.FromJson("{\"SampleMs\":500}").SampleMs == 500 && FizzSettings.FromJson("{\"SampleMs\":250}").SampleMs == 250, "2x and 4x unchanged");
        }

        static void Perf()
        {
            var p = new PmCsvParser();
            PmRow r;
            ParseOne(p, Header, out r);
            var line = Encoding.ASCII.GetBytes(Row("eldenring.exe", 12345, 0x1A2B3C4D5E6FUL, "Hardware: Independent Flip", "6.9444"));
            var fs = new FrameStats(); fs.SetTarget(12345);
            long t = 1;
            // warm up
            for (int i = 0; i < 1000; i++) { p.ParseLine(line, 0, line.Length, out r); fs.Add(r, t += 1000); }
            int gc0 = GC.CollectionCount(0);
            var sw = Stopwatch.StartNew();
            const int N = 2000000;
            for (int i = 0; i < N; i++) { p.ParseLine(line, 0, line.Length, out r); fs.Add(r, t += fs.Freq / 240); }
            sw.Stop();
            double nsPerLine = sw.Elapsed.TotalMilliseconds * 1e6 / N;
            Console.WriteLine("  parse+add: " + nsPerLine.ToString("0") + " ns per frame line; at 240 fps that's " + (nsPerLine * 240 / 1e7).ToString("0.000") + "% of one core");
            Ok(GC.CollectionCount(0) == gc0, "no garbage per frame line (gen0 GCs: " + (GC.CollectionCount(0) - gc0) + ")");
            Ok(nsPerLine < 5000, "parse fast enough");

            gc0 = GC.CollectionCount(0);
            sw.Restart();
            FrameResult res = default(FrameResult);
            for (int i = 0; i < 2000; i++) res = fs.Compute(t);
            sw.Stop();
            Console.WriteLine("  compute (full 10 s window @240 fps): " + (sw.Elapsed.TotalMilliseconds * 1000 / 2000).ToString("0") + " µs per call (runs 2x/s)");
            Ok(GC.CollectionCount(0) == gc0, "no garbage per stats compute");
            Near(res.Fps, 144f, 0.5f, "fps comes from PresentMon frametimes, not arrival timing");
        }
    }
}
