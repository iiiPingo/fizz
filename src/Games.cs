using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
