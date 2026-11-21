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

        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

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

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Sub;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.AccentDark)) e.Graphics.FillRectangle(b, r);
            using (var p = new Pen(Theme.Accent, 2f))
            {
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, k = Math.Max(3, r.Width / 5);
                e.Graphics.DrawLines(p, new[] { new Point(cx - k - 1, cy), new Point(cx - 1, cy + k), new Point(cx + k + 1, cy - k) });
            }
        }

        sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.Hover; } }
            public override Color MenuItemSelected { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.Selected; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.Selected; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
            public override Color CheckBackground { get { return Theme.AccentDark; } }
            public override Color CheckSelectedBackground { get { return Theme.AccentDark; } }
            public override Color CheckPressedBackground { get { return Theme.AccentDark; } }
        }
    }

    /// <summary>Global hotkeys + the "open settings" message from a second launch. A message-only window.</summary>
    internal sealed class HubWindow : NativeWindow, IDisposable
    {
        public event Action<int> Hotkey;
        public event Action OpenSettingsRequested;
        readonly int openMsg = Native.RegisterWindowMessage(Program.OpenSettingsMessage);

        public HubWindow()
        {
            CreateHandle(new CreateParams { Caption = Program.HubTitle, Parent = Native.HWND_MESSAGE });
            // let the non-elevated launcher talk to us when we run elevated
            try { Native.ChangeWindowMessageFilterEx(Handle, openMsg, Native.MSGFLT_ALLOW, IntPtr.Zero); } catch { }
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
