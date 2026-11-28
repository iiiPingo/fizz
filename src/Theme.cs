using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fizz
{
    internal enum ThemeMode { Dark, Oled, Light }

    /// <summary>
    /// The settings window and tray menu colours: Dark (default, the same as before), OLED or Light, picked on the
    /// Advanced page (saved as "Theme" in settings.json). The overlay keeps its own look. Fizz's cherry-pink accent.
    /// </summary>
    internal static class Theme
    {
        static Palette p = Palette.Dark;
        public static Palette P { get { return p; } }
        public static ThemeMode Mode { get { return p.Mode; } }
        public static bool IsLight { get { return p.Mode == ThemeMode.Light; } }

        public static Color Bg { get { return p.Bg; } }             // page canvas
        public static Color Panel { get { return p.Panel; } }       // menus
        public static Color Card { get { return p.Card; } }         // buttons, inputs, lists
        public static Color Hover { get { return p.Hover; } }
        public static Color Selected { get { return p.Selected; } }
        public static Color Border { get { return p.Border; } }
        public static Color Text { get { return p.Text; } }
        public static Color Sub { get { return p.Sub; } }
        public static Color Dim { get { return p.Dim; } }
        public static Color Nav { get { return p.Nav; } }           // left side menu and footer
        public static Color NavSelected { get { return p.NavSelected; } }
        public static Color NavHover { get { return p.NavHover; } }
        public static Color Edge { get { return p.Edge; } }         // round button outline
        public static Color EdgeHover { get { return p.EdgeHover; } }
        public static Color HostEdge { get { return p.HostEdge; } } // card around lists and text boxes
        public static Color NumEdge { get { return p.NumEdge; } }   // number box outline (a bit clearer)
        public static Color NumDivider { get { return p.NumDivider; } }
        public static Color Thumb { get { return p.Thumb; } }       // list scroll thumb
        public static Color Warn { get { return p.Warn; } }
        public static Color Red { get { return p.Red; } }
        public static Color Hairline { get { return p.Hairline; } } // empty on Dark

        public static readonly Color Accent = Color.FromArgb(255, 92, 158);
        public static Color AccentDark { get { return IsLight ? Mix(Accent, Color.White, 0.55f) : Color.FromArgb(120, 38, 78); } }
        // accent used as text or a thin glyph: darker on Light so it stays readable on white
        public static Color AccentText { get { return IsLight ? Mix(Accent, Color.Black, 0.3f) : Accent; } }

        public static void SetMode(ThemeMode m) { p = Palette.For(m); }

        public static ThemeMode ParseMode(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "oled": return ThemeMode.Oled;
                case "light": return ThemeMode.Light;
                default: return ThemeMode.Dark;
            }
        }

        public static string ModeName(ThemeMode m) { return m == ThemeMode.Oled ? "OLED" : m == ThemeMode.Light ? "Light" : "Dark"; }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

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
        // the app window and tray menu use Outfit (built into Fizz.exe); Segoe UI if it couldn't be loaded. The overlay keeps its own font.
        public static Font Ui(float size) { return AppFonts.Regular != null ? new Font(AppFonts.Regular, size) : new Font(UiFamily, size); }
        public static Font UiSemi(float size)
        {
            if (AppFonts.SemiBold != null) return new Font(AppFonts.SemiBold, size);
            return UiSemiFamily != null ? new Font(UiSemiFamily, size) : new Font(UiFamily, size, FontStyle.Bold);
        }
        public static Font UiTitle(float size)
        {
            if (AppFonts.SemiBold != null) return new Font(AppFonts.SemiBold, size);
            return UiDisplaySemi != null ? new Font(UiDisplaySemi, size) : UiSemi(size);
        }

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

    /// <summary>One theme's colours. Dark is exactly Fizz's old palette; OLED and Light match Minibar's.</summary>
    internal sealed class Palette
    {
        public ThemeMode Mode;
        public Color Bg, Panel, Card, Hover, Selected, Border, Text, Sub, Dim, Nav, NavSelected, NavHover,
            Edge, EdgeHover, HostEdge, NumEdge, NumDivider, Thumb, Warn, Red, Hairline;

        static Color G(int v) { return Color.FromArgb(v, v, v); }
        static Color H(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static readonly Palette Dark = new Palette
        {
            Mode = ThemeMode.Dark,
            Bg = G(32), Panel = G(40), Card = G(46), Hover = G(58), Selected = G(70), Border = G(64),
            Text = G(242), Sub = G(165), Dim = G(115), Nav = G(27), NavSelected = G(45), NavHover = G(36),
            Edge = Color.FromArgb(38, 255, 255, 255), EdgeHover = Color.FromArgb(70, 255, 255, 255),
            HostEdge = G(58), NumEdge = G(78), NumDivider = G(70), Thumb = G(95),
            Warn = Color.FromArgb(255, 184, 0), Red = Color.FromArgb(255, 82, 82), Hairline = Color.Empty,
        };

        // Discord Onyx style: pure black canvas, near-black raised surfaces, #1F1F24 hairlines
        public static readonly Palette Oled = new Palette
        {
            Mode = ThemeMode.Oled,
            Bg = H(0x000000), Panel = H(0x0D0D10), Card = H(0x131316), Hover = H(0x1A1A1E), Selected = H(0x26262C), Border = H(0x1F1F24),
            Text = G(242), Sub = G(170), Dim = G(120), Nav = H(0x000000), NavSelected = H(0x131316), NavHover = H(0x0D0D10),
            Edge = H(0x1F1F24), EdgeHover = H(0x2C2C33),
            HostEdge = H(0x1F1F24), NumEdge = H(0x2C2C33), NumDivider = H(0x1F1F24), Thumb = H(0x3A3A42),
            Warn = Color.FromArgb(255, 184, 0), Red = Color.FromArgb(255, 82, 82), Hairline = H(0x1F1F24),
        };

        // Windows 11 Fluent light: #F3F3F3 canvas, white raised surfaces, #E0E0E0 hairlines, #1B1B1B / #5C5C5C text
        public static readonly Palette Light = new Palette
        {
            Mode = ThemeMode.Light,
            Bg = H(0xF3F3F3), Panel = H(0xFFFFFF), Card = H(0xFFFFFF), Hover = H(0xF0F0F0), Selected = H(0xE5E5E5), Border = H(0xE0E0E0),
            Text = H(0x1B1B1B), Sub = H(0x5C5C5C), Dim = H(0x8A8A8A), Nav = H(0xEBEBEB), NavSelected = H(0xFFFFFF), NavHover = H(0xF5F5F5),
            Edge = H(0xE0E0E0), EdgeHover = H(0xC8C8C8),
            HostEdge = H(0xE0E0E0), NumEdge = H(0xD0D0D0), NumDivider = H(0xE5E5E5), Thumb = H(0xB0B0B0),
            Warn = H(0x9D5D00), Red = H(0xC42B1C), Hairline = H(0xE0E0E0),
        };

        public static Palette For(ThemeMode m) { return m == ThemeMode.Oled ? Oled : m == ThemeMode.Light ? Light : Dark; }
    }

    /// <summary>
    /// Outfit (SIL Open Font License, src/fonts) loaded from the exe's resources, without installing it.
    /// GDI+ needs it in a PrivateFontCollection (Font objects); GDI needs AddFontMemResourceEx (TextRenderer, menus).
    /// If either half fails on Windows, both families stay null and Fizz uses Segoe UI.
    /// </summary>
    internal static class AppFonts
    {
        public static readonly FontFamily Regular, SemiBold;
        // one collection per file, so each weight is found by its file, not by a family name that differs between systems
        static readonly System.Drawing.Text.PrivateFontCollection[] pfc = { new System.Drawing.Text.PrivateFontCollection(), new System.Drawing.Text.PrivateFontCollection() };

        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pb, uint cb, IntPtr pdv, ref uint pcFonts);

        static AppFonts()
        {
            try
            {
                bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
                var names = new[] { "Fizz.Outfit-Regular.ttf", "Fizz.Outfit-SemiBold.ttf" };
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    byte[] data;
                    using (var s = typeof(AppFonts).Assembly.GetManifestResourceStream(name))
                    {
                        if (s == null) return;
                        data = new byte[s.Length];
                        int read = 0;
                        while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                    }
                    // the memory stays allocated for the life of the process (both GDI and GDI+ read from it)
                    IntPtr mem = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);
                    System.Runtime.InteropServices.Marshal.Copy(data, 0, mem, data.Length);
                    pfc[i].AddMemoryFont(mem, data.Length);
                    if (windows)
                    {
                        uint count = 0;
                        if (AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref count) == IntPtr.Zero) { Log.Warn("Outfit font: AddFontMemResourceEx failed"); return; }
                    }
                }
                if (pfc[0].Families.Length == 0 || pfc[1].Families.Length == 0) { Log.Warn("Outfit font: not loaded"); return; }
                Regular = pfc[0].Families[0];
                SemiBold = pfc[1].Families[0];
            }
            catch (Exception ex) { Regular = SemiBold = null; Log.Error("Outfit font", ex); }
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
