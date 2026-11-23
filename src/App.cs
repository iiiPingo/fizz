using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Fizz
{
    internal static class ChildHost
    {
        public static int Run(string[] args)
        {
            string hb = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--hb") hb = args[i + 1];

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            var uiErrors = new Queue<DateTime>();
            Application.ThreadException += (s, e) =>
            {
                Log.Error("UI", e.Exception);
                uiErrors.Enqueue(DateTime.UtcNow);
                while (uiErrors.Count > 0 && (DateTime.UtcNow - uiErrors.Peek()).TotalSeconds > 60) uiErrors.Dequeue();
                if (uiErrors.Count > 30)
                {
                    Log.Crash("UI", "30+ UI errors in a minute; restarting Fizz to recover");
                    Environment.Exit(2); // the watchdog brings us back fresh
                }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Crash("Unhandled (" + (e.IsTerminating ? "fatal" : "non-fatal") + ")", e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) => { Log.Error("Task", e.Exception); e.SetObserved(); };

            bool created;
            Mutex m;
            try { m = new Mutex(true, @"Local\Fizz.App", out created); }
            catch (UnauthorizedAccessException) { return 0; }
            using (m)
            {
                if (!created) return 0;
                var app = new FizzApp(hb);
                Application.Run(app);
                return app.ExitCode;
            }
        }
    }

    internal sealed class FizzApp : ApplicationContext
    {
        public int ExitCode;
        public FizzSettings Settings;
        public readonly FrameStats Frames = new FrameStats();
        public readonly PresentMonService PresentMon;
        public readonly SensorService Sensors = new SensorService();
        public readonly bool Elevated = Startup.IsElevated;
        public bool PawnIo = Startup.PawnIoInstalled;

        readonly OverlayWindow overlay = new OverlayWindow();
        readonly HubWindow hub = new HubWindow();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 700 };
        readonly ForegroundWatcher fgWatcher = new ForegroundWatcher();
        readonly ForegroundInfo fg = new ForegroundInfo();
        readonly Thread sampler;
        readonly AutoResetEvent samplerWake = new AutoResetEvent(false);
        volatile bool stopping;
        MemoryMappedFile hbFile;
        MemoryMappedViewAccessor hbView;
        SettingsForm settingsForm;

        // UI → sampler
        volatile int needFlags, sampleMs = 500;
        volatile bool samplerActive, pmWanted, gameInFrontFlag, sessionActiveFlag, reopenRequested;
        volatile string csvPath;
        volatile object pendingPicks;
        public volatile bool CatalogWanted;

        // sampler → UI
        readonly object snapGate = new object();
        readonly Snapshot sampled = new Snapshot();
        readonly Snapshot ui = new Snapshot();

        // game state (UI thread)
        int gamePid;
        IntPtr gameHwnd;
        string gameExe = "", lastGameExe = "";
        long lastGameFrontTicks, lastCandidateTicks;
        DateTime gameStartUtc, lastAliveCheckUtc = DateTime.MinValue;
        bool userHidden;
        readonly HashSet<string> warnedExclusive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long presetFlashUntil;
        Action balloonAction;
        DateTime lastStatusUtc = DateTime.MinValue, lastTooltipUtc = DateTime.MinValue;
        readonly Stopwatch cpuClock = Stopwatch.StartNew();
        TimeSpan lastCpuTime;
        int lastPmPid;
        double lastPmCpu;
        public string HotkeyProblem;

        // overlay content + string caches (allocation-free when values are steady)
        readonly OverlayContent content = new OverlayContent();
        readonly long[] valueKeys = new long[Stats.All.Length];
        readonly string[] valueTexts = new string[Stats.All.Length];
        readonly string[,] templates = new string[Stats.All.Length, 2];
        static readonly long Freq = Stopwatch.Frequency;

        public FizzApp(string heartbeatName)
        {
            Settings = FizzSettings.Load();
            PresentMon = new PresentMonService(Frames);
            for (int i = 0; i < valueKeys.Length; i++) valueKeys[i] = long.MinValue;
            for (int i = 0; i < Stats.All.Length; i++) { templates[i, 1] = Stats.All[i].Template; templates[i, 0] = StripUnit(Stats.All[i].Template); }

            var forceHandle = overlay.Handle; // BeginInvoke target must exist before any background callback
            if (heartbeatName != null)
            {
                try { hbFile = MemoryMappedFile.OpenExisting(heartbeatName); hbView = hbFile.CreateViewAccessor(0, 16); }
                catch (Exception ex) { Log.Error("Heartbeat", ex); }
            }
            Beat();

            tray.Icon = Theme.LoadTrayIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = "Fizz";
            menu.Renderer = new DarkMenuRenderer();
            menu.Font = Theme.Ui(10f);
            menu.ShowImageMargin = true;
            menu.Opening += (s, e) => BuildMenu();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
            tray.BalloonTipClicked += (s, e) => { var a = balloonAction; balloonAction = null; if (a != null) Safe("balloon", a); };
            tray.Visible = true;

            hub.Hotkey += OnHotkey;
            hub.OpenSettingsRequested += OpenSettings;
            hub.ExitRequested += () =>
            {
                // after this message is handled, not inside it
                var t = new System.Windows.Forms.Timer { Interval = 10 };
                t.Tick += (o, e) => { t.Stop(); t.Dispose(); ExitApp(0); };
                t.Start();
            };
            RegisterHotkeys();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            ApplySettingsToWorkers();
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Settings.Save(); };
            uiTimer.Tick += (s, e) => Safe("tick", Tick);
            uiTimer.Start();

            sampler = new Thread(SamplerLoop) { IsBackground = true, Name = "Fizz.Sampler" };
            sampler.Start();

            Log.Info("Fizz started. Elevated=" + Elevated + " PawnIO=" + PawnIo + " PresentMon=" + (PresentMonService.ExePath ?? "missing") +
                     " OS=" + Environment.OSVersion);
            var startup = new System.Windows.Forms.Timer { Interval = 2500 };
            startup.Tick += (s, e) => { startup.Stop(); startup.Dispose(); Safe("firstrun", FirstRunChecks); };
            startup.Start();
        }

        static string StripUnit(string t)
        {
            if (string.IsNullOrEmpty(t)) return t;
            foreach (var u in new[] { " ms", " MHz", " GHz", " W", " GB", " KB/s", " MB/s", "%" }) if (t.EndsWith(u)) return t.Substring(0, t.Length - u.Length);
            return t.Replace("°C", "°");
        }

        static void Safe(string where, Action a)
        {
            try { a(); } catch (Exception ex) { Log.Error(where, ex); }
        }

        void Beat()
        {
            try { if (hbView != null) hbView.Write(0, DateTime.UtcNow.Ticks); } catch { }
        }

        // ================= startup checks / notifications =================

        void FirstRunChecks()
        {
            if (Elevated && Settings.StartWithWindows)
            {
                // (re)create the logon task in the background so it always points at this exe
                ThreadPool.QueueUserWorkItem(_ => { string err = Startup.EnableStartWithWindows(); if (err != null) Log.Warn(err); });
            }

            if (Elevated && !File.Exists(Path.Combine(Log.Dir, "sensors.txt"))) WriteSensorDump();

            var missing = MissingFeatures();
            if (!Settings.FirstRunDone)
            {
                Settings.FirstRunDone = true;
                SaveSoon();
                Balloon("Fizz is running",
                    "It appears when a game is in front (borderless/windowed fullscreen). Ctrl+Alt+F shows/hides it, Ctrl+Alt+P switches presets. Left-click the tray icon for settings." +
                    (missing.Count > 0 ? "\n\nMissing: " + string.Join("; ", missing) : ""), null);
                return;
            }
            if (!Elevated)
                Balloon("Fizz is running without admin", "FPS and CPU temperature are off. Click here to restart Fizz as admin.", RestartAsAdmin);
            else if (!PawnIo)
                Balloon("CPU temperature needs PawnIO", "Click here to install the PawnIO driver (one-time, asks for permission).", InstallPawnIo);
            if (HotkeyProblem != null) Balloon("Hotkey problem", HotkeyProblem, OpenSettings);
        }

        /// <summary>Human-readable list of what's not working and why.</summary>
        public List<string> MissingFeatures()
        {
            var l = new List<string>();
            if (!Elevated) l.Add("not running as admin, so FPS and CPU temperature are off");
            else if (!PawnIo) l.Add("PawnIO driver not installed, so CPU temperature, clock and power are off");
            if (PresentMonService.ExePath == null) l.Add("PresentMon.exe missing from the tools folder, so FPS is off");
            else if (PresentMon.Problem != null && Elevated) l.Add(PresentMon.Problem);
            if (Sensors.Problem != null) l.Add(Sensors.Problem);
            return l;
        }

        void Balloon(string title, string text, Action onClick)
        {
            balloonAction = onClick;
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text.Length > 250 ? text.Substring(0, 247) + "..." : text;
            tray.BalloonTipIcon = ToolTipIcon.None;
            tray.ShowBalloonTip(6000);
        }

        public void RestartAsAdmin()
        {
            if (Elevated) return;
            if (Startup.RelaunchElevated("--replace")) ExitApp(0);
        }

        public void InstallPawnIo()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
                string setup = Directory.Exists(dir) ? Directory.GetFiles(dir, "PawnIO*setup*.exe").FirstOrDefault() : null;
                if (setup == null) { Process.Start("https://pawnio.eu/"); return; }
                var p = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas" });
                // when it finishes, pick the driver up without restarting Fizz
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { if (p != null) p.WaitForExit(); } catch { }
                    bool now = Startup.PawnIoInstalled;
                    if (now && !PawnIo) { PawnIo = true; reopenRequested = true; Log.Info("PawnIO installed; reopening sensors"); }
                });
            }
            catch (Exception ex) { Log.Error("InstallPawnIo", ex); }
        }

        // ================= hotkeys =================

        void RegisterHotkeys()
        {
            var problems = new List<string>();
            string e1 = hub.Register(1, Settings.HotkeyToggle), e2 = hub.Register(2, Settings.HotkeyLayout), e3 = hub.Register(3, Settings.HotkeyPreset);
            if (e1 != null) problems.Add("toggle: " + e1);
            if (e2 != null) problems.Add("layout: " + e2);
            if (e3 != null) problems.Add("preset: " + e3);
            HotkeyProblem = problems.Count == 0 ? null : "Hotkeys not set: " + string.Join("; ", problems);
            if (HotkeyProblem != null) Log.Warn(HotkeyProblem);
        }

        void OnHotkey(int id)
        {
            if (id == 1) { userHidden = !userHidden; Log.Info("Overlay " + (userHidden ? "hidden" : "shown") + " by hotkey"); }
            else if (id == 2)
            {
                var p = EffectivePreset(currentRule);
                p.Layout = p.Vertical ? "Horizontal" : "Vertical";
                SettingsChanged();
            }
            else if (id == 3) CyclePreset();
            Safe("tick", Tick);
        }

        void CyclePreset()
        {
            int i = Settings.Presets.IndexOf(Settings.Active);
            Settings.ActivePreset = Settings.Presets[(i + 1) % Settings.Presets.Count].Name;
            presetFlashUntil = Stopwatch.GetTimestamp() + Freq * 3 / 2;
            SettingsChanged();
        }

        // ================= settings plumbing =================

        /// <param name="fromSettingsWindow">true when the settings window made the change (it's already up to date).</param>
        public void SettingsChanged(bool fromSettingsWindow = false)
        {
            Settings.Repair();
            ApplySettingsToWorkers();
            overlay.ResetCache(false);
            SaveSoon();
            if (!fromSettingsWindow && settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshFromApp();
        }

        public void HotkeysChanged() { RegisterHotkeys(); }

        void SaveSoon() { saveTimer.Stop(); saveTimer.Start(); }

        void ApplySettingsToWorkers()
        {
            sampleMs = Settings.SampleMs;
            pendingPicks = new[] { Settings.CpuTempSensor, Settings.GpuTempSensor };
            samplerWake.Set();
        }

        public void OpenSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                try { settingsForm = new SettingsForm(this); }
                catch (Exception ex)
                {
                    Log.Error("OpenSettings", ex);
                    settingsForm = null;
                    Balloon("Settings couldn't open", ex.Message + " (details in fizz.log)", null);
                    return;
                }
                settingsForm.FormClosed += (s, e) => { settingsForm = null; Settings.Save(); };
            }
            settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
        }

        public string LastGameExe { get { return lastGameExe; } }

        public void StartWithWindowsChanged(bool on)
        {
            Settings.StartWithWindows = on;
            SaveSoon();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = on ? Startup.EnableStartWithWindows() : Startup.DisableStartWithWindows();
                if (err != null) BeginInvokeUi(() => Balloon("Start with Windows", err, null));
            });
        }

        void BeginInvokeUi(Action a)
        {
            try { overlay.BeginInvoke(a); } catch { }
        }

        // ================= power / display =================

        void OnPowerModeChanged(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Log.Info("Resumed from sleep: reopening sensors and PresentMon");
                reopenRequested = true;
                BeginInvokeUi(() => overlay.ResetCache(true));
            }
        }

        void OnDisplayChanged(object s, EventArgs e) { BeginInvokeUi(() => overlay.ResetCache(true)); }

        // ================= the 4 Hz UI tick =================

        GameRule currentRule;

        void Tick()
        {
            Beat();
            long now = Stopwatch.GetTimestamp();
            fgWatcher.Poll(fg);
            CheckPawnIo();

            bool pmLive = PresentMon.Running;
            bool pmOff = PresentMon.Unavailable; // no admin / missing exe: can't confirm frames, so trust the window shape
            bool appWindow = fg.Pid > 4 && !fg.IsShell && !fg.IsOwn && !fg.IsMinimized && !Settings.IsNotGame(fg.Exe);
            var fgRule = Settings.FindGame(fg.Exe);
            bool fullCandidate = appWindow && fg.CoversMonitor;
            bool winCandidate = appWindow && !fg.CoversMonitor && fg.ClientRect.Width >= 640 && fg.ClientRect.Height >= 360;
            if (fullCandidate || winCandidate) lastCandidateTicks = now;

            // Is the window in front a game?
            //  - the current game keeps that status through loading screens and window-mode switches
            //  - games listed on the Games page always count (any window mode)
            //  - fullscreen/borderless: anything PresentMon sees drawing frames
            //  - windowed: must draw 25+ fps steadily (so normal apps don't trigger it)
            bool gameInFront;
            if (!appWindow) gameInFront = false;
            else if (fg.Pid == gamePid) gameInFront = true;
            else if (fgRule != null) gameInFront = true;
            else if (fullCandidate) gameInFront = pmOff || (pmLive && Frames.IsPresenting(fg.Pid, now));
            else if (winCandidate) gameInFront = pmLive && Frames.PresentRate(fg.Pid, now) >= 25;
            else gameInFront = false;

            // an app you excluded while it was the current "game": drop it right away
            if (gamePid != 0 && Settings.IsNotGame(gameExe)) EndGame("excluded");

            if (gameInFront)
            {
                if (fg.Pid != gamePid) StartGame(fg.Pid, fg.Exe);
                gameHwnd = fg.Hwnd;
                lastGameFrontTicks = now;
            }
            if (gamePid != 0 && (DateTime.UtcNow - lastAliveCheckUtc).TotalSeconds >= 2)
            {
                lastAliveCheckUtc = DateTime.UtcNow;
                if (!ForegroundWatcher.IsAlive(gamePid)) EndGame("exited");
            }

            // Alt-tabbed away: keep the overlay on the game while the game is still visible
            // and the window you switched to doesn't sit on top of the overlay (e.g. Discord on the other monitor).
            // Checked against where the overlay sits ON THE GAME, never where it is right now: in "Always" mode
            // the overlay jumps to the screen corner when this says no, and checking that spot made it say yes
            // again next tick, so it bounced between the game window and the screen edge 4 times a second.
            bool overGame = false;
            if (!gameInFront && gamePid != 0 && gameHwnd != IntPtr.Zero && Native.IsWindow(gameHwnd) && Native.IsWindowVisible(gameHwnd) && !Native.IsIconic(gameHwnd))
            {
                var gameArea = GameAnchor(gameHwnd);
                var onGame = overlay.BoundsPinnedTo(gameArea, Settings.Corner, Settings.Margin);
                overGame = StaysOnGame(fg.WindowRect, fg.IsDesktop, fg.IsMinimized, onGame);
                if (overGame) lastGameFrontTicks = now;
            }
            bool gameShown = gameInFront || overGame;

            // exclusive fullscreen: PresentMon's present mode is exact; Windows' own flag is the no-admin fallback
            bool exclusive = false;
            if (fullCandidate)
            {
                byte mode = pmLive ? Frames.LastModeOf(fg.Pid, now) : PresentModes.Unknown;
                if (mode != PresentModes.Unknown) exclusive = PresentModes.IsExclusive(mode);
                else if (pmOff) exclusive = ForegroundWatcher.ShellSaysExclusiveFullscreen();
                if (exclusive) WarnExclusive(fg.Exe);
            }

            bool preview = settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible && settingsForm.WindowState != FormWindowState.Minimized;
            currentRule = gameShown ? Settings.FindGame(gameExe) : (Settings.AlwaysShow ? fgRule : null);
            bool hiddenByRule = currentRule != null && currentRule.Hide;
            bool excludedInFront = !gameShown && Settings.IsNotGame(fg.Exe);
            bool show = !userHidden && (preview || (!hiddenByRule && !exclusive && !excludedInFront && (Settings.AlwaysShow || gameShown)));
            Preset preset = EffectivePreset(preview ? null : currentRule);

            // FPS target: the current game, or (always-on mode) whatever is in front and drawing frames
            int target = gamePid;
            if (target == 0 && Settings.AlwaysShow && !fg.IsShell && !fg.IsOwn && pmLive && Frames.IsPresenting(fg.Pid, now)) target = fg.Pid;
            Frames.SetTarget(target);

            // tell the sampler what to do
            bool sessionActive = gamePid != 0 && now - lastGameFrontTicks < Freq * 60;
            sessionActiveFlag = sessionActive && gameShown;
            bool pickerOpen = CatalogWanted;
            bool wasActive = samplerActive;
            samplerActive = show || sessionActive || pickerOpen;
            if (samplerActive && !wasActive) samplerWake.Set();
            pmWanted = now - lastCandidateTicks < Freq * 60 || sessionActive || Settings.AlwaysShow || preview;
            gameInFrontFlag = gameShown || fullCandidate;
            Need need = show ? NeedsFor(preset) : Need.None;
            if (sessionActive) need |= Need.Cpu | Need.Gpu;
            if (pickerOpen) need |= Need.Catalog | Need.Cpu | Need.Gpu;
            needFlags = (int)need;
            csvPath = Settings.CsvLog && sessionActive ? csvFile : null;

            if (show)
            {
                lock (snapGate) ui.CopyFrom(sampled);
                ui.Game = GameLabel(gamePid != 0 ? gameExe : fg.Exe);
                BuildContent(preset, now);
                IntPtr mon = Settings.MainScreenOnly ? PrimaryMonitor() : fg.Monitor;
                var anchor = new Native.RECT(); // empty = whole monitor
                if (gameShown)
                {
                    mon = Native.MonitorFromWindow(gameHwnd, Native.MONITOR_DEFAULTTONEAREST);
                    anchor = GameAnchor(gameHwnd);
                }
                else if (preview && fg.IsOwn && !Settings.MainScreenOnly) mon = Native.MonitorFromWindow(settingsForm.Handle, Native.MONITOR_DEFAULTTONEAREST);
                overlay.Render(content, mon, anchor);
            }
            else overlay.HideOverlay();

            if ((DateTime.UtcNow - lastTooltipUtc).TotalSeconds > 2)
            {
                lastTooltipUtc = DateTime.UtcNow;
                string t = "Fizz · " + (userHidden ? "hidden (Ctrl+Alt+F)" : show ? "showing" + (gamePid != 0 ? " · " + GameLabel(gameExe) : "")
                    : exclusive ? "can't draw over exclusive fullscreen" : hiddenByRule ? "hidden in this game" : "waiting for a game");
                tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            }
            if ((DateTime.UtcNow - lastStatusUtc).TotalSeconds >= 60) { lastStatusUtc = DateTime.UtcNow; WriteStatus(); }
        }

        static IntPtr PrimaryMonitor() { return Native.MonitorFromPoint(new Native.POINT(0, 0), Native.MONITOR_DEFAULTTOPRIMARY); }

        /// <summary>
        /// The area the overlay is pinned into while a game shows: inside the game window (windowed games), or the
        /// corner of the game's screen when "inside windowed games" is off. Borderless games fill the screen either way.
        /// </summary>
        Native.RECT GameAnchor(IntPtr hwnd)
        {
            Native.RECT r;
            if (Settings.PinInWindow && Native.ClientRectOnScreen(hwnd, out r)) return r;
            var mi = new Native.MONITORINFO { cbSize = 40 };
            Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref mi);
            return mi.rcMonitor;
        }

        /// <summary>
        /// After alt-tab: does the overlay stay on the game? Yes unless the window now in front overlaps the
        /// overlay's spot on the game. <paramref name="overlayOnGame"/> must not depend on where the overlay is now.
        /// </summary>
        public static bool StaysOnGame(Native.RECT frontWindow, bool frontIsDesktop, bool frontMinimized, Native.RECT overlayOnGame)
        {
            if (overlayOnGame.Width <= 0 || overlayOnGame.Height <= 0) return false;
            return frontIsDesktop || frontMinimized || !Native.Intersects(frontWindow, overlayOnGame);
        }

        DateTime lastPawnCheckUtc = DateTime.MinValue;

        /// <summary>Notice PawnIO being installed while Fizz runs (no restart needed).</summary>
        void CheckPawnIo()
        {
            if (PawnIo || (DateTime.UtcNow - lastPawnCheckUtc).TotalSeconds < 10) return;
            lastPawnCheckUtc = DateTime.UtcNow;
            if (Startup.PawnIoInstalled)
            {
                PawnIo = true;
                reopenRequested = true;
                Log.Info("PawnIO detected; reopening sensors");
            }
        }

        static string GameLabel(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            string n = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe.Substring(0, exe.Length - 4) : exe;
            return n.Length > 22 ? n.Substring(0, 21) + "…" : n;
        }

        public Preset EffectivePreset(GameRule rule)
        {
            if (rule != null && !string.IsNullOrEmpty(rule.Preset))
            {
                var p = Settings.FindPreset(rule.Preset);
                if (p != null) return p;
            }
            return Settings.Active;
        }

        static Need NeedsFor(Preset p)
        {
            Need n = Need.None;
            foreach (var it in p.Items)
            {
                if (!it.On) continue;
                Stat id;
                if (!Stats.TryParse(it.Id, out id)) continue;
                switch (id)
                {
                    case Stat.GpuTemp: case Stat.GpuLoad: case Stat.GpuClock: case Stat.GpuPower: case Stat.Vram: n |= Need.Gpu; break;
                    case Stat.CpuTemp: case Stat.CpuLoad: case Stat.CoreMax: case Stat.CpuClock: case Stat.CpuPower: n |= Need.Cpu; break;
                    case Stat.Ram: n |= Need.Ram; break;
                    case Stat.Disk: n |= Need.Disk; break;
                    case Stat.Net: n |= Need.Net; break;
                }
            }
            return n;
        }

        void BuildContent(Preset p, long now)
        {
            content.GroupCount = 0;
            content.Vertical = p.Vertical;
            content.Background = p.Background;
            content.FontPt = p.FontSize;
            content.Opacity = p.Opacity;
            Color accent;
            content.Accent = FizzSettings.TryColor(p.Accent, out accent) ? accent : Theme.Accent;
            content.Corner = Settings.Corner;
            content.Margin = Settings.Margin;

            if (now < presetFlashUntil)
            {
                var g = content.NextGroup();
                g.Label = "PRESET"; g.Texts.Add(p.Name); g.Templates.Add(""); g.Tones.Add(Tone.Normal);
            }

            OverlayGroup cur = null;
            string curLabel = null;
            foreach (var it in p.Items)
            {
                if (!it.On) continue;
                Stat id;
                if (!Stats.TryParse(it.Id, out id)) continue;
                var def = Stats.Get(id);
                string label = it.Label ? def.Label : "";
                if (cur == null || label.Length == 0 || !string.Equals(label, curLabel))
                {
                    cur = content.NextGroup();
                    cur.Label = label;
                    curLabel = label;
                }
                int k = (int)id;
                long key = Stats.ValueKey(id, ui, it.Unit);
                if (key != valueKeys[k] || valueTexts[k] == null || id == Stat.Game) { valueTexts[k] = Stats.Format(id, ui, it.Unit); valueKeys[k] = key; }
                cur.Texts.Add(valueTexts[k]);
                cur.Templates.Add(templates[k, it.Unit ? 1 : 0]);
                cur.Tones.Add(Stats.ToneFor(id, ui, Settings.Threshold(id)));
            }
            if (content.GroupCount == 0)
            {
                var g = content.NextGroup();
                g.Label = "FIZZ"; g.Texts.Add("no stats on"); g.Templates.Add(""); g.Tones.Add(Tone.Dim);
            }
        }

        // ================= game sessions =================

        string csvFile;

        void StartGame(int pid, string exe)
        {
            if (gamePid != 0) EndGame("switched");
            gamePid = pid; gameExe = exe; lastGameExe = exe;
            gameStartUtc = DateTime.UtcNow;
            Frames.SetTarget(pid);
            Frames.ResetSession(exe);
            csvFile = Path.Combine(Log.Dir, "logs", SafeName(GameLabel(exe)) + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            Log.Info("Game detected: " + exe + " (pid " + pid + ")");
        }

        static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length == 0 ? "game" : s;
        }

        void EndGame(string why)
        {
            string exe = gameExe;
            gamePid = 0;
            gameHwnd = IntPtr.Zero;
            Frames.SetTarget(0);
            csvPath = null;
            var sum = Frames.GetSession();
            TimeSpan len = DateTime.UtcNow - gameStartUtc;
            Log.Info("Game ended (" + why + "): " + exe + " after " + (int)len.TotalMinutes + " min, " + sum.Frames + " frames");
            var rule = Settings.FindGame(exe);
            bool wantSummary = rule != null && rule.Summary; // opt-in per game (Games page / tray)
            if (why != "excluded" && Settings.SessionSummary && wantSummary && len.TotalSeconds >= 60 && sum.Frames >= 300)
            {
                var inv = CultureInfo.InvariantCulture;
                string length = len.TotalHours >= 1 ? (int)len.TotalHours + "h " + len.Minutes + "m" : (int)Math.Max(1, len.TotalMinutes) + " min";
                string text = "GPU  avg " + Fmt(sum.AvgGpuTemp) + "°C · max " + Fmt(sum.MaxGpuTemp) + "°C\n" +
                              "CPU  avg " + Fmt(sum.AvgCpuTemp) + "°C · max " + Fmt(sum.MaxCpuTemp) + "°C\n" +
                              "Avg " + Fmt(sum.AvgFps) + " FPS · 1% low " + Fmt(sum.Low1);
                Balloon(GameLabel(exe) + " · " + length, text, null);
            }
        }

        static string Fmt(float v) { return float.IsNaN(v) ? "--" : Stats.Int(v); }

        void WarnExclusive(string exe)
        {
            if (string.IsNullOrEmpty(exe) || !warnedExclusive.Add(exe)) return;
            Log.Info("Exclusive fullscreen detected: " + exe);
            Balloon("Fizz can't show over exclusive fullscreen",
                "Switch " + GameLabel(exe) + " to borderless (windowed fullscreen) in its display settings.", null);
        }

        // ================= sampler thread =================

        void SamplerLoop()
        {
            var work = new Snapshot();
            var blank = new Snapshot();
            StreamWriter csv = null;
            string csvOpen = null;
            int csvLines = 0;
            while (!stopping)
            {
                bool active = samplerActive;
                try
                {
                    if (reopenRequested)
                    {
                        reopenRequested = false;
                        Sensors.RequestReopen(4);
                        PresentMon.Ensure(false, false);
                    }
                    var picks = pendingPicks as SensorPick[];
                    if (picks != null) { pendingPicks = null; Sensors.SetPicks(picks[0], picks[1]); }

                    PresentMon.Ensure(pmWanted, gameInFrontFlag);

                    if (active)
                    {
                        Need need = (Need)needFlags;
                        work.CopyFrom(blank); // stale values never linger: anything not read this round shows "--"
                        Sensors.Sample(work, need);
                        var fr = Frames.Compute(Stopwatch.GetTimestamp());
                        work.Fps = fr.Fps; work.Low1 = fr.Low1; work.FrameTime = fr.FrameTime; work.PresentMode = fr.Mode;
                        if (sessionActiveFlag) Frames.AddSessionTemps(work.GpuTemp, work.CpuTemp);
                        lock (snapGate) sampled.CopyFrom(work);

                        // optional per-game CSV (Stage 2)
                        string want = csvPath;
                        if (want != csvOpen)
                        {
                            if (csv != null) { try { csv.Dispose(); } catch { } csv = null; }
                            csvOpen = want;
                            if (want != null)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(want));
                                csv = new StreamWriter(want, true, new UTF8Encoding(false));
                                csv.WriteLine("time,fps,low1_fps,frametime_ms,gpu_temp_c,gpu_load_pct,vram_used_mb,cpu_temp_c,cpu_load_pct,ram_used_mb");
                            }
                        }
                        if (csv != null && sessionActiveFlag)
                        {
                            var inv = CultureInfo.InvariantCulture;
                            csv.WriteLine(DateTime.Now.ToString("HH:mm:ss.f", inv) + "," + C(work.Fps) + "," + C(work.Low1) + "," + C(work.FrameTime) + "," +
                                          C(work.GpuTemp) + "," + C(work.GpuLoad) + "," + C(work.VramUsedMb) + "," + C(work.CpuTemp) + "," + C(work.CpuLoad) + "," + C(work.RamUsedMb));
                            if (++csvLines % 20 == 0) csv.Flush();
                        }
                    }
                    else if (csv != null && csvPath == null) { try { csv.Dispose(); } catch { } csv = null; csvOpen = null; }
                }
                catch (Exception ex) { Log.Error("Sampler", ex); }
                samplerWake.WaitOne(active ? sampleMs : 500);
            }
            if (csv != null) { try { csv.Dispose(); } catch { } }
        }

        static string C(float v) { return float.IsNaN(v) ? "" : v.ToString("0.#", CultureInfo.InvariantCulture); }

        // ================= tray menu =================

        Font headFont;

        void BuildMenu()
        {
            // the menu is rebuilt on every open: free the old items (and their submenus) instead of leaking them
            var old = new ToolStripItem[menu.Items.Count];
            menu.Items.CopyTo(old, 0);
            menu.Items.Clear();
            foreach (var it in old) it.Dispose();
            var head = new ToolStripMenuItem("Fizz" + (Elevated ? "" : "  (limited: not admin)")) { Enabled = false, Padding = new Padding(0, 4, 0, 4) };
            head.Font = headFont ?? (headFont = Theme.UiSemi(10f));
            menu.Items.Add(head);
            menu.Items.Add(new ToolStripSeparator());

            var show = Item("Overlay", null);
            show.DropDownItems.Add(Check("Only in games (auto)", !Settings.AlwaysShow, () => { Settings.Mode = "Auto"; SettingsChanged(); }));
            show.DropDownItems.Add(Check("Always on", Settings.AlwaysShow, () => { Settings.Mode = "Always"; SettingsChanged(); }));
            show.DropDownItems.Add(new ToolStripSeparator());
            show.DropDownItems.Add(Check("Hidden for now    " + Settings.HotkeyToggle, userHidden, () => { userHidden = !userHidden; }));
            menu.Items.Add(show);

            var presets = Item("Preset", null);
            foreach (var p in Settings.Presets)
            {
                var name = p.Name;
                presets.DropDownItems.Add(Check(name, p == Settings.Active, () => { Settings.ActivePreset = name; presetFlashUntil = Stopwatch.GetTimestamp() + Freq * 3 / 2; SettingsChanged(); }));
            }
            menu.Items.Add(presets);

            var active = Settings.Active;
            var layout = Item("Layout", null);
            layout.DropDownItems.Add(Check("Horizontal", !active.Vertical, () => { Settings.Active.Layout = "Horizontal"; SettingsChanged(); }));
            layout.DropDownItems.Add(Check("Vertical", active.Vertical, () => { Settings.Active.Layout = "Vertical"; SettingsChanged(); }));
            menu.Items.Add(layout);

            var pos = Item("Position", null);
            foreach (var c in new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" })
            {
                string corner = c;
                pos.DropDownItems.Add(Check(CornerName(c), Settings.Corner == c, () => { Settings.Corner = corner; SettingsChanged(); }));
            }
            pos.DropDownItems.Add(new ToolStripSeparator());
            pos.DropDownItems.Add(Check("Main screen when not in a game", Settings.MainScreenOnly, () => { Settings.Screen = Settings.MainScreenOnly ? "Active" : "Main"; SettingsChanged(); }));
            pos.DropDownItems.Add(Check("Inside windowed games", Settings.PinInWindow, () => { Settings.PinInWindow = !Settings.PinInWindow; SettingsChanged(); }));
            menu.Items.Add(pos);

            if (!string.IsNullOrEmpty(lastGameExe))
            {
                var rule = Settings.FindGame(lastGameExe);
                string exe = lastGameExe;
                menu.Items.Add(Check("Hide in " + GameLabel(exe), rule != null && rule.Hide, () =>
                {
                    var r = Settings.GetOrAddGame(exe);
                    r.Hide = !r.Hide;
                    SettingsChanged();
                }));
            }

            if (!string.IsNullOrEmpty(lastGameExe))
            {
                var r = Settings.FindGame(lastGameExe);
                string exe = lastGameExe;
                menu.Items.Add(Check("Summary for " + GameLabel(exe), r != null && r.Summary, () =>
                {
                    var g = Settings.GetOrAddGame(exe);
                    g.Summary = !g.Summary;
                    SettingsChanged();
                }));
            }
            if (!string.IsNullOrEmpty(lastGameExe) && !Settings.IsNotGame(lastGameExe))
            {
                string exe = lastGameExe;
                menu.Items.Add(Item(GameLabel(exe) + " isn't a game", () => MarkNotGame(exe)));
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Settings…", OpenSettings));
            menu.Items.Add(Check("Start with Windows", Settings.StartWithWindows, () => StartWithWindowsChanged(!Settings.StartWithWindows)));
            if (!Elevated) menu.Items.Add(Item("Restart as admin (enables FPS + CPU temp)", RestartAsAdmin));
            if (!PawnIo) menu.Items.Add(Item("Install PawnIO (CPU temperature)", InstallPawnIo));
            menu.Items.Add(Item("Open Fizz data folder", () => Process.Start("explorer.exe", "\"" + Log.Dir + "\"")));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Exit", () => ExitApp(0)));
        }

        /// <summary>Never treat this exe as a game again (also ends its session now).</summary>
        public void MarkNotGame(string exe)
        {
            if (string.IsNullOrEmpty(exe) || Settings.IsNotGame(exe)) return;
            Settings.NotGames.Add(exe);
            if (string.Equals(gameExe, exe, StringComparison.OrdinalIgnoreCase) && gamePid != 0) EndGame("excluded");
            if (string.Equals(lastGameExe, exe, StringComparison.OrdinalIgnoreCase)) lastGameExe = "";
            Log.Info("Marked as not a game: " + exe);
            SettingsChanged();
        }

        public static string CornerName(string c)
        {
            switch (c) { case "TopRight": return "Top right"; case "BottomLeft": return "Bottom left"; case "BottomRight": return "Bottom right"; }
            return "Top left";
        }

        static ToolStripMenuItem Item(string text, Action a)
        {
            var i = new ToolStripMenuItem(text) { ForeColor = Theme.Text, Padding = new Padding(0, 4, 0, 4) };
            if (a != null) i.Click += (s, e) => Safe("menu", a);
            return i;
        }

        static ToolStripMenuItem Check(string text, bool on, Action a)
        {
            var i = Item(text, a);
            i.Checked = on;
            return i;
        }

        // ================= diagnostics =================

        public void WriteStatus()
        {
            try
            {
                var me = Process.GetCurrentProcess();
                TimeSpan cpu = me.TotalProcessorTime;
                double wall = cpuClock.Elapsed.TotalSeconds;
                double cpuPct = wall > 0 ? (cpu - lastCpuTime).TotalSeconds / wall * 100.0 / Environment.ProcessorCount : 0;
                lastCpuTime = cpu; cpuClock.Restart();
                var sb = new StringBuilder();
                sb.AppendLine("Fizz status " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Elevated: " + Elevated + "   PawnIO: " + PawnIo);
                sb.AppendLine("Fizz CPU (avg since last status, % of whole CPU): " + cpuPct.ToString("0.00", CultureInfo.InvariantCulture));
                sb.AppendLine("Fizz memory: " + Native.PrivateWorkingSetMb(me.Handle).ToString("0.0", CultureInfo.InvariantCulture) + " MB as Task Manager shows it (private working set); committed " +
                              (me.PrivateMemorySize64 / 1048576) + " MB, working set incl. shared DLLs " + (me.WorkingSet64 / 1048576) + " MB, GC heap " + (GC.GetTotalMemory(false) / 1048576) + " MB");
                sb.AppendLine("PresentMon: " + (PresentMon.Running ? "running" : "not running") + (PresentMon.Problem != null ? " (" + PresentMon.Problem + ")" : "") + ", rows parsed " + PresentMon.Rows);
                sb.AppendLine("PresentMon header: " + (PresentMon.Header ?? "(none yet)"));
                try
                {
                    foreach (var p in Process.GetProcesses().Where(x => x.ProcessName.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase)))
                    {
                        double t = p.TotalProcessorTime.TotalSeconds, pct = 0;
                        if (p.Id == lastPmPid && wall > 0) pct = (t - lastPmCpu) / wall * 100.0 / Environment.ProcessorCount;
                        lastPmPid = p.Id; lastPmCpu = t;
                        sb.AppendLine("  " + p.ProcessName + " pid " + p.Id + ": memory " + Native.PrivateWorkingSetMb(p.Handle).ToString("0.0", CultureInfo.InvariantCulture) +
                                      " MB, CPU " + (pct > 0 ? pct.ToString("0.00", CultureInfo.InvariantCulture) + "% of whole CPU (since last status)" : "(measuring)"));
                    }
                }
                catch { }
                sb.AppendLine("CPU: " + Sensors.CpuName + "   temp sensor " + Sensors.CpuTempSource);
                sb.AppendLine("GPU: " + Sensors.GpuName + "   temp sensor " + Sensors.GpuTempSource);
                if (Sensors.Problem != null) sb.AppendLine("Sensor problem: " + Sensors.Problem);
                sb.AppendLine("Game: " + (gamePid != 0 ? gameExe + " pid " + gamePid : "(none)") + "   foreground: " + fg.Exe + (fg.CoversMonitor ? " [fills monitor]" : ""));
                lock (snapGate)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Last sample: fps {0:0.#} low1 {1:0.#} ft {2:0.##} mode {3} | gpu {4:0}C {5:0}% {6:0}MHz {7:0.#}W vram {8:0}/{9:0}MB | cpu {10:0}C {11:0}% core {12:0}% {13:0}MHz {14:0.#}W | ram {15:0}/{16:0}MB",
                        sampled.Fps, sampled.Low1, sampled.FrameTime, PresentModes.Short[sampled.PresentMode], sampled.GpuTemp, sampled.GpuLoad, sampled.GpuClock, sampled.GpuPower,
                        sampled.VramUsedMb, sampled.VramTotalMb, sampled.CpuTemp, sampled.CpuLoad, sampled.CoreMax, sampled.CpuClockMhz, sampled.CpuPower, sampled.RamUsedMb, sampled.RamTotalMb));
                var miss = MissingFeatures();
                sb.AppendLine("Missing: " + (miss.Count == 0 ? "nothing" : string.Join("; ", miss)));
                if (HotkeyProblem != null) sb.AppendLine(HotkeyProblem);
                File.WriteAllText(Path.Combine(Log.Dir, "status.txt"), sb.ToString());
            }
            catch (Exception ex) { Log.Error("WriteStatus", ex); }
        }

        /// <summary>Full list of every CPU/GPU sensor with live values, in a separate short-lived process.</summary>
        public void WriteSensorDump()
        {
            try { Process.Start(new ProcessStartInfo(Startup.ExePath, "--dump-sensors") { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception ex) { Log.Error("SensorDump", ex); }
        }

        // ================= shutdown =================

        public void ExitApp(int code)
        {
            ExitCode = code;
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            try
            {
                stopping = true;
                samplerWake.Set();
                uiTimer.Stop();
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
                Settings.Save();
                tray.Visible = false;
                overlay.HideOverlay();
                sampler.Join(3000);
                PresentMon.Dispose();
                Sensors.Dispose();
                hub.Dispose();
                tray.Dispose();
                overlay.Dispose();
                if (hbView != null) hbView.Dispose();
                if (hbFile != null) hbFile.Dispose();
                Log.Info("Fizz exited (code " + ExitCode + ")");
            }
            catch (Exception ex) { Log.Error("Exit", ex); }
            base.ExitThreadCore();
        }
    }
}
