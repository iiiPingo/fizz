using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fizz
{
    /// <summary>Same dark palette family as Pour, with Fizz's cherry-pink accent.</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 32, 32);
        public static readonly Color Panel = Color.FromArgb(40, 40, 40);
        public static readonly Color Card = Color.FromArgb(46, 46, 46);
        public static readonly Color Hover = Color.FromArgb(58, 58, 58);
        public static readonly Color Selected = Color.FromArgb(70, 70, 70);
        public static readonly Color Border = Color.FromArgb(64, 64, 64);
        public static readonly Color Text = Color.FromArgb(242, 242, 242);
        public static readonly Color Sub = Color.FromArgb(165, 165, 165);
        public static readonly Color Dim = Color.FromArgb(115, 115, 115);
        public static readonly Color Accent = Color.FromArgb(255, 92, 158);
        public static readonly Color AccentDark = Color.FromArgb(120, 38, 78);
        public static readonly Color Warn = Color.FromArgb(255, 184, 0);
        public static readonly Color Nav = Color.FromArgb(27, 27, 27);

        // the family accent colours (the same swatches every Pingo app offers), Fizz pink first
        public static readonly Color[] Swatches =
        {
            Color.FromArgb(255, 92, 158),   // Fizz pink
            Color.FromArgb(217, 154, 98),   // Echoa cocoa
            Color.FromArgb(76, 168, 255),   // blue
            Color.FromArgb(61, 219, 176),   // mint
            Color.FromArgb(95, 208, 104),   // Juice green
            Color.FromArgb(242, 193, 78),   // honey
            Color.FromArgb(255, 138, 61),   // Pulp tangerine
            Color.FromArgb(208, 100, 74),   // Pour terracotta
            Color.FromArgb(242, 95, 92),    // cherry red
            Color.FromArgb(176, 124, 255),  // ube
            Color.FromArgb(200, 200, 200),  // silver
        };

        // fonts: Segoe UI Variable on Windows 11, Segoe UI on Windows 10 (same as the other Pingo apps)
        static bool FontExists(string name)
        {
            try { using (var f = new Font(name, 10)) return f.Name == name; } catch { return false; }
        }
        static readonly string UiFamily = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        static readonly string UiSemiFamily = FontExists("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold"
            : FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : null;
        static readonly string UiDisplaySemi = FontExists("Segoe UI Variable Display Semibold") ? "Segoe UI Variable Display Semibold" : null;
        public static readonly string IconFont = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        public static Font Ui(float size) { return new Font(UiFamily, size); }
        public static Font UiSemi(float size) { return UiSemiFamily != null ? new Font(UiSemiFamily, size) : new Font(UiFamily, size, FontStyle.Bold); }
        public static Font UiTitle(float size) { return UiDisplaySemi != null ? new Font(UiDisplaySemi, size) : UiSemi(size); }

        // Segoe Fluent Icons / MDL2 glyphs
        public const string GStats = "\uE9D9", GLook = "\uE790", GGames = "\uE7FC", GAdvanced = "\uE713", GUp = "\uE70E", GDown = "\uE70D", GCheck = "\uE73E";

        public static bool Win11 { get { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000; } }

        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        // tray: just the can (no pink tile), so it sits at the same size as other tray icons
        public static Icon LoadTrayIcon(int size)
        {
            try
            {
                using (var s = typeof(Theme).Assembly.GetManifestResourceStream("Fizz.fizz-tray.ico"))
                    return s == null ? LoadIcon(size) : new Icon(s, size, size);
            }
            catch { return LoadIcon(size); }
        }

        public static Icon LoadIcon(int size)
        {
            try
            {
                using (var s = typeof(Theme).Assembly.GetManifestResourceStream("Fizz.fizz.ico"))
                    return s == null ? SystemIcons.Application : new Icon(s, size, size);
            }
            catch { return SystemIcons.Application; }
        }
    }

    /// <summary>
    /// Windows 11 style dark menus: rounded popup corners, rounded hover highlight, a plain accent tick
    /// instead of the old square check box, roomier rows.
    /// </summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Rounded popup corners (Windows 11; ignored on 10).</summary>
        static void RoundPopup(ToolStrip ts)
        {
            if (!(ts is ToolStripDropDown) || !ts.IsHandleCreated || !Theme.Win11) return;
            try { int round = 2; DwmSetWindowAttribute(ts.Handle, 33, ref round, 4); } catch { } // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            RoundPopup(e.ToolStrip);
            using (var b = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Windows 11 draws its own thin border around the rounded popup
            if (Theme.Win11 && e.ToolStrip is ToolStripDropDown) return;
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { } // no grey strip down the left

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            Ui.Fill(e.Graphics, r, e.Item.Pressed ? Theme.Selected : Theme.Hover, 4);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // the exe name shown on the right of the open-apps picker is dimmer than the title
            bool side = e.Item is ToolStripMenuItem && e.Text == ((ToolStripMenuItem)e.Item).ShortcutKeyDisplayString && e.Text != e.Item.Text;
            e.TextColor = !e.Item.Enabled ? Theme.Dim : side ? Theme.Sub : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            // thin chevron instead of the solid triangle
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ArrowRectangle;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Math.Max(3f, r.Height * 0.18f);
            using (var p = new Pen(Theme.Sub, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(p, new[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) });
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // just a tick in the accent colour, no box
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, k = Math.Max(3.5f, r.Width * 0.26f);
            using (var p = new Pen(Theme.Accent, Math.Max(1.8f, r.Width / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - k, cy + 0.1f * k), new PointF(cx - 0.3f * k, cy + 0.75f * k), new PointF(cx + k, cy - 0.7f * k) });
        }

        sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.Selected; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.Selected; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
            public override Color CheckBackground { get { return Color.Transparent; } }
            public override Color CheckSelectedBackground { get { return Color.Transparent; } }
            public override Color CheckPressedBackground { get { return Color.Transparent; } }
        }
    }

    /// <summary>Global hotkeys + the "open settings" message from a second launch. A message-only window.</summary>
    internal sealed class HubWindow : NativeWindow, IDisposable
    {
        public event Action<int> Hotkey;
        public event Action OpenSettingsRequested;
        public event Action ExitRequested;
        readonly int openMsg = Native.RegisterWindowMessage(Program.OpenSettingsMessage);
        readonly int exitMsg = Native.RegisterWindowMessage(Program.ExitMessage);

        public HubWindow()
        {
            CreateHandle(new CreateParams { Caption = Program.HubTitle, Parent = Native.HWND_MESSAGE });
            // let the non-elevated launcher talk to us when we run elevated
            try { Native.ChangeWindowMessageFilterEx(Handle, openMsg, Native.MSGFLT_ALLOW, IntPtr.Zero); } catch { }
            try { Native.ChangeWindowMessageFilterEx(Handle, exitMsg, Native.MSGFLT_ALLOW, IntPtr.Zero); } catch { }
        }

        /// <summary>Registers "Ctrl+Alt+F" style hotkeys. Returns null on success, else a reason.</summary>
        public string Register(int id, string spec)
        {
            Native.UnregisterHotKey(Handle, id);
            uint mods, vk;
            if (!ParseHotkey(spec, out mods, out vk)) return "can't read \"" + spec + "\"";
            if (!Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, vk)) return spec + " is already used by another app";
            return null;
        }

        public static bool ParseHotkey(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            foreach (var raw in spec.Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= Native.MOD_CONTROL;
                else if (p == "alt") mods |= Native.MOD_ALT;
                else if (p == "shift") mods |= Native.MOD_SHIFT;
                else if (p == "win") mods |= Native.MOD_WIN;
                else
                {
                    Keys k;
                    if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                    if (!Enum.TryParse(p, true, out k)) return false;
                    vk = (uint)k;
                }
            }
            return vk != 0 && mods != 0;
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == Native.WM_HOTKEY) { var h = Hotkey; if (h != null) h(m.WParam.ToInt32()); return; }
                if (m.Msg == openMsg) { var o = OpenSettingsRequested; if (o != null) o(); return; }
                if (m.Msg == exitMsg) { var x = ExitRequested; if (x != null) x(); return; }
            }
            catch (Exception ex) { Log.Error("Hub.WndProc", ex); }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(Handle, i);
            DestroyHandle();
        }
    }
}
