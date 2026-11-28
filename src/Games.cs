using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Fizz
{
    internal sealed class ForegroundInfo
    {
        public IntPtr Hwnd;
        public int Pid;
        public string Exe = "";
        public bool CoversMonitor;   // borderless or fullscreen sized
        public bool IsShell;         // desktop, taskbar, start menu, alt-tab...
        public bool IsOwn;           // one of Fizz's windows
        public bool IsDesktop;       // the wallpaper (the game, if any, is still drawn above it)
        public bool IsMinimized;
        public Native.RECT WindowRect, ClientRect; // screen pixels
        public IntPtr Monitor;
        public Native.RECT MonitorRect;

        public void CopyFrom(ForegroundInfo o)
        {
            Hwnd = o.Hwnd; Pid = o.Pid; Exe = o.Exe; CoversMonitor = o.CoversMonitor; IsShell = o.IsShell; IsOwn = o.IsOwn;
            IsDesktop = o.IsDesktop; IsMinimized = o.IsMinimized; WindowRect = o.WindowRect; ClientRect = o.ClientRect;
            Monitor = o.Monitor; MonitorRect = o.MonitorRect;
        }
    }

    /// <summary>
    /// Watches the game window's position from its own thread. Idle: about 60 cheap reads a second. While the window
    /// moves: about every millisecond. It only reads and posts a message, so nothing touches the game.
    /// </summary>
    internal sealed class WindowFollower
    {
        readonly IntPtr notify;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile IntPtr target;
        volatile bool stop;
        int posted;

        public WindowFollower(IntPtr notifyWindow)
        {
            notify = notifyWindow;
            new Thread(Loop) { IsBackground = true, Name = "Fizz.Follower", Priority = ThreadPriority.AboveNormal }.Start();
        }

        public IntPtr Target
        {
            set { if (target == value) return; target = value; if (value != IntPtr.Zero) wake.Set(); }
        }

        /// <summary>Call when the posted message is handled, so the next move can post again.</summary>
        public void Handled() { Interlocked.Exchange(ref posted, 0); }

        public void Stop() { stop = true; wake.Set(); }

        void Loop()
        {
            var last = new Native.RECT();
            IntPtr lastTarget = IntPtr.Zero;
            bool fine = false;
            long lastMove = 0, settle = System.Diagnostics.Stopwatch.Frequency * 2 / 5;
            while (!stop)
            {
                try
                {
                    IntPtr h = target;
                    if (h == IntPtr.Zero)
                    {
                        if (fine) { fine = false; Native.timeEndPeriod(1); }
                        lastTarget = IntPtr.Zero;
                        wake.WaitOne();
                        continue;
                    }
                    Native.RECT r;
                    if (Native.GetWindowRect(h, out r))
                    {
                        bool same = h == lastTarget && r.Left == last.Left && r.Top == last.Top && r.Right == last.Right && r.Bottom == last.Bottom;
                        if (!same)
                        {
                            bool wasKnown = h == lastTarget;
                            last = r; lastTarget = h;
                            if (wasKnown)
                            {
                                lastMove = System.Diagnostics.Stopwatch.GetTimestamp();
                                if (Interlocked.CompareExchange(ref posted, 1, 0) == 0 &&
                                    !Native.PostMessage(notify, Native.WM_FIZZ_FOLLOW, IntPtr.Zero, IntPtr.Zero)) Handled();
                            }
                        }
                    }
                    bool moving = System.Diagnostics.Stopwatch.GetTimestamp() - lastMove < settle;
                    if (moving != fine)
                    {
                        // 1 ms timing only during a drag; back to normal as soon as the window settles
                        fine = moving;
                        if (fine) Native.timeBeginPeriod(1); else Native.timeEndPeriod(1);
                    }
                    Thread.Sleep(fine ? 1 : 15);
                }
                catch (Exception ex) { Log.Throttled("follower", "Follower: " + ex.Message, 120); Thread.Sleep(100); }
            }
            if (fine) Native.timeEndPeriod(1);
        }
    }

    /// <summary>Cheap foreground-window inspection (runs 4x/s on the UI thread, allocation-light).</summary>
    internal sealed class ForegroundWatcher
    {
        readonly int ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        readonly StringBuilder cls = new StringBuilder(128);
        readonly Dictionary<int, KeyValuePair<string, DateTime>> exeCache = new Dictionary<int, KeyValuePair<string, DateTime>>();

        static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
            "ForegroundStaging", "MultitaskingViewFrame", "TaskSwitcherWnd", "#32768", "LockScreenControllerProxyWindow"
        };

        public void Poll(ForegroundInfo f)
        {
            f.Hwnd = Native.GetForegroundWindow();
            f.Pid = 0; f.Exe = ""; f.CoversMonitor = false; f.IsShell = false; f.IsOwn = false; f.IsDesktop = false; f.IsMinimized = false;
            f.Monitor = IntPtr.Zero; f.WindowRect = new Native.RECT(); f.ClientRect = new Native.RECT();
            if (f.Hwnd == IntPtr.Zero) { f.IsShell = true; return; }

            int pid;
            Native.GetWindowThreadProcessId(f.Hwnd, out pid);
            f.Pid = pid;
            f.IsOwn = pid == ownPid;

            cls.Length = 0;
            Native.GetClassName(f.Hwnd, cls, cls.Capacity);
            string c = cls.ToString();
            f.IsShell = ShellClasses.Contains(c);
            f.IsDesktop = c == "Progman" || c == "WorkerW";

            f.Monitor = Native.MonitorFromWindow(f.Hwnd, Native.MONITOR_DEFAULTTONEAREST);
            var mi = new Native.MONITORINFO { cbSize = 40 };
            if (f.Monitor != IntPtr.Zero && Native.GetMonitorInfo(f.Monitor, ref mi)) f.MonitorRect = mi.rcMonitor;

            Native.RECT r;
            f.IsMinimized = Native.IsIconic(f.Hwnd);
            if (!f.IsMinimized && Native.GetWindowRect(f.Hwnd, out r))
            {
                f.WindowRect = r;
                const int tol = 2;
                f.CoversMonitor = r.Left <= mi.rcMonitor.Left + tol && r.Top <= mi.rcMonitor.Top + tol &&
                                  r.Right >= mi.rcMonitor.Right - tol && r.Bottom >= mi.rcMonitor.Bottom - tol;
                Native.RECT cr;
                f.ClientRect = Native.ClientRectOnScreen(f.Hwnd, out cr) ? cr : r;
            }
            f.Exe = ExeName(pid);
        }

        public string ExeName(int pid)
        {
            if (pid <= 4) return "";
            KeyValuePair<string, DateTime> e;
            DateTime now = DateTime.UtcNow;
            if (exeCache.TryGetValue(pid, out e) && (now - e.Value).TotalSeconds < 30) return e.Key;
            string path = Native.ProcessImagePath(pid);
            string name = path == null ? "" : Path.GetFileName(path);
            if (exeCache.Count > 256) exeCache.Clear();
            exeCache[pid] = new KeyValuePair<string, DateTime>(name, now);
            return name;
        }

        /// <summary>True while the process is running (no Process object allocation).</summary>
        public static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { int code; return Native.GetExitCodeProcess(h, out code) && code == 259; }
            finally { Native.CloseHandle(h); }
        }

        /// <summary>Windows' own "a D3D app is in exclusive fullscreen" flag (works without admin).</summary>
        public static bool ShellSaysExclusiveFullscreen()
        {
            try { int st; return Native.SHQueryUserNotificationState(out st) == 0 && st == Native.QUNS_RUNNING_D3D_FULL_SCREEN; }
            catch { return false; }
        }
    }
}

namespace Fizz
{
    internal sealed class OpenApp
    {
        public string Exe = "", Title = "";
        public override string ToString() { return Title + " (" + Exe + ")"; }
    }

    /// <summary>The apps that have a window open right now, for the "Add open app" pickers on the Games page.</summary>
    internal static class OpenApps
    {
        // background/system processes with windows nobody would pick
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe", "ApplicationFrameHost.exe", "TextInputHost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe",
            "ShellExperienceHost.exe", "LockApp.exe", "SystemSettingsBroker.exe", "Fizz.exe"
        };

        delegate bool EnumProc(IntPtr hwnd, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

        /// <summary>Visible top-level app windows, one entry per exe, sorted by title.</summary>
        public static List<OpenApp> List()
        {
            var raw = new List<OpenApp>();
            int ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var sb = new StringBuilder(256);
            var names = new Dictionary<int, string>();
            try
            {
                EnumWindows((h, l) =>
                {
                    try
                    {
                        if (!Native.IsWindowVisible(h) || GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
                        if ((GetWindowLongPtr(h, -20 /* GWL_EXSTYLE */).ToInt64() & Native.WS_EX_TOOLWINDOW) != 0) return true;
                        int cloaked;
                        if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out cloaked, 4) == 0 && cloaked != 0) return true; // hidden store-app shells
                        sb.Length = 0;
                        GetWindowText(h, sb, sb.Capacity);
                        if (sb.Length == 0) return true;
                        int pid;
                        Native.GetWindowThreadProcessId(h, out pid);
                        if (pid == ownPid || pid <= 4) return true;
                        string exe;
                        if (!names.TryGetValue(pid, out exe))
                        {
                            string path = Native.ProcessImagePath(pid);
                            names[pid] = exe = path == null ? "" : System.IO.Path.GetFileName(path);
                        }
                        raw.Add(new OpenApp { Exe = exe, Title = sb.ToString() });
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { Log.Error("OpenApps", ex); }
            return Clean(raw);
        }

        /// <summary>One entry per exe (first window's title), no system shells, sorted by title.</summary>
        public static List<OpenApp> Clean(IEnumerable<OpenApp> raw)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<OpenApp>();
            foreach (var a in raw)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Exe) || Skip.Contains(a.Exe) || !seen.Add(a.Exe)) continue;
                string t = (a.Title ?? "").Trim();
                if (t.Length == 0) t = a.Exe;
                if (t.Length > 48) t = t.Substring(0, 47) + "…";
                list.Add(new OpenApp { Exe = a.Exe, Title = t });
            }
            list.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Title, y.Title));
            return list;
        }
    }
}
