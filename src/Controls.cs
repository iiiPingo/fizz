using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Fizz
{
    internal static class Ui
    {
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, Color c, float rad)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, Color c, float rad, float w = 1f)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark title bar + rounded corners on Windows 11 (ignored on older builds).</summary>
        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int caption = ColorTranslator.ToWin32(Theme.Nav);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4); // caption color (Win11)
            }
            catch { }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Windows 10/11 dark scrollbars for a native control (best effort).</summary>
        public static void DarkScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        /// <summary>Dark, owner-drawn combo box (closed box and dropdown list).</summary>
        public static void StyleCombo(ComboBox c)
        {
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Theme.Card;
            c.ForeColor = Theme.Text;
            c.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var b = new SolidBrush(sel ? Theme.Selected : Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(c.Items[e.Index]), c.Font, r, c.Enabled ? Theme.Text : Theme.Dim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
        }
    }

    /// <summary>Rounded flat button.</summary>
    internal class RoundButton : Button
    {
        bool hover, down;
        public bool Selected;     // nav buttons: accent bar + highlight
        public bool NavStyle;
        public Color HoverColor = Theme.Hover;
        public string Glyph;          // optional Segoe Fluent Icons glyph (nav buttons, arrow buttons)
        public bool Flat;             // no outline or fill until hovered (the − / + inside number boxes)
        public char Sign;             // '-' or '+': drawn as lines, exactly centred (font glyphs sit off-centre)
        Font glyphFont;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Height * 0.28f;
            Color navBg = Parent != null ? Parent.BackColor : Theme.Nav;
            Color fill = NavStyle ? (Selected ? Color.FromArgb(45, 45, 45) : hover ? Color.FromArgb(36, 36, 36) : navBg)
                                  : (down ? Theme.Selected : hover ? HoverColor : BackColor);
            if (NavStyle) rad = Height * 0.18f;
            if (Flat)
            {
                if (hover || down) Ui.Fill(g, new RectangleF(2, 2, Width - 4, Height - 4), down ? Theme.Selected : Theme.Hover, Math.Min(6, Height * 0.25f));
            }
            else
            {
                Ui.Fill(g, r, fill, rad);
                if (!NavStyle) Ui.Stroke(g, r, Color.FromArgb(hover ? 70 : 38, 255, 255, 255), rad);
            }
            if (NavStyle && Selected)
                Ui.Fill(g, new RectangleF(2, Height * 0.3f, 3, Height * 0.4f), Theme.Accent, 1.5f);
            Color fg = !Enabled ? Theme.Dim : NavStyle && Selected ? Theme.Text : ForeColor;
            if (Sign == '-' || Sign == '+')
            {
                float cx = Width / 2f, cy = Height / 2f, k = (float)Math.Round(Math.Max(4f, Height * 0.2f));
                using (var p = new Pen(fg, Math.Max(1.5f, Height / 18f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(p, cx - k, cy, cx + k, cy);
                    if (Sign == '+') g.DrawLine(p, cx, cy - k, cx, cy + k);
                }
                return;
            }
            int textX = NavStyle ? (int)(Height * 0.42f) : 0;
            if (!string.IsNullOrEmpty(Glyph))
            {
                float gs = Math.Max(8f, Font.SizeInPoints * (NavStyle ? 1.1f : 1f));
                if (glyphFont == null || Math.Abs(glyphFont.SizeInPoints - gs) > 0.01f) { if (glyphFont != null) glyphFont.Dispose(); glyphFont = new Font(Theme.IconFont, gs); }
                var gr = NavStyle ? new Rectangle(textX, 0, (int)(Height * 0.6f), Height) : string.IsNullOrEmpty(Text) ? ClientRectangle : new Rectangle(0, 0, Height, Height);
                Color gc = !Enabled ? Theme.Dim : NavStyle && Selected ? Theme.Accent : fg;
                TextRenderer.DrawText(g, Glyph, glyphFont, gr, gc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (NavStyle) textX += (int)(Height * 0.6f) + (int)(Height * 0.2f);
            }
            if (string.IsNullOrEmpty(Text)) return;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | (NavStyle ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
            var tr = NavStyle ? new Rectangle(textX, 0, Width - textX, Height) : ClientRectangle;
            TextRenderer.DrawText(g, Text, Font, tr, fg, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && glyphFont != null) { glyphFont.Dispose(); glyphFont = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>On/off switch that replaces checkboxes (same Checked / CheckedChanged API).</summary>
    internal sealed class Toggle : CheckBox
    {
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
        }

        public void FitWidth(int height)
        {
            Height = height;
            Width = (int)(height * 1.9f) + 10 + TextRenderer.MeasureText(Text, Font).Width + 4;
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
            float h = Height * 0.62f, w = h * 1.85f, y = (Height - h) / 2f;
            var track = new RectangleF(1, y, w, h);
            Color on = Enabled ? Theme.Accent : Theme.AccentDark;
            Ui.Fill(g, track, Checked ? on : Theme.Card, h / 2);
            if (!Checked) Ui.Stroke(g, track, Theme.Dim, h / 2);
            float k = h - 6, kx = Checked ? track.Right - k - 3 : track.X + 3;
            using (var b = new SolidBrush(Checked ? Color.White : Theme.Sub)) g.FillEllipse(b, kx, y + 3, k, k);
            var tr = new Rectangle((int)(w + 10), 0, Width - (int)(w + 10), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Flat slider (drop-in for the TrackBar members Fizz uses).</summary>
    internal sealed class Slider : Control
    {
        int min, max = 100, val;
        bool drag, hover;
        public event EventHandler ValueChanged;

        public Slider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Minimum { get { return min; } set { min = value; Value = val; } }
        public int Maximum { get { return max; } set { max = value; Value = val; } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v;
                Invalidate();
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        float Pad { get { return Height * 0.32f; } }

        void SetFromX(int x)
        {
            float t = (x - Pad) / Math.Max(1f, Width - 2 * Pad);
            Value = min + (int)Math.Round(Math.Max(0, Math.Min(1, t)) * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); drag = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? 1 : -1; base.OnMouseWheel(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value--;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value++;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
            float pad = Pad, th = Math.Max(4f, Height * 0.16f), cy = Height / 2f;
            float t = max > min ? (val - min) / (float)(max - min) : 0;
            float x = pad + t * (Width - 2 * pad);
            Ui.Fill(g, new RectangleF(pad, cy - th / 2, Width - 2 * pad, th), Theme.Card, th / 2);
            Ui.Fill(g, new RectangleF(pad, cy - th / 2, x - pad, th), Theme.Accent, th / 2);
            float r = Height * (hover || drag ? 0.3f : 0.26f);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(Theme.Accent, 2f)) g.DrawEllipse(p, x - r, cy - r, r * 2, r * 2);
        }
    }
}

namespace Fizz
{
    /// <summary>
    /// Accent picker, same as Minibar's: the family colours plus a last circle that opens a custom colour
    /// (shows "+" until a custom colour is picked, then that colour).
    /// </summary>
    internal sealed class SwatchRow : Control
    {
        Color value = Theme.Accent;
        int hover = -1;
        Font iconFont;
        public event EventHandler Picked;

        public SwatchRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public Color Value
        {
            get { return value; }
            set { this.value = Color.FromArgb(255, value.R, value.G, value.B); Invalidate(); }
        }

        int Count { get { return Theme.Swatches.Length + 1; } }
        int CustomIndex { get { return Theme.Swatches.Length; } }
        bool IsPreset { get { return Array.Exists(Theme.Swatches, p => p.ToArgb() == value.ToArgb()); } }
        Rectangle Cell(int i) { return new Rectangle(i * Height, 0, Height, Height); }
        int Hit(Point p) { for (int i = 0; i < Count; i++) if (Cell(i).Contains(p)) return i; return -1; }

        /// <summary>Width that fits every circle at the current height.</summary>
        public int PreferredWidth { get { return Count * Height; } }

        static void Disc(Graphics g, PointF c, float r, Color col)
        {
            using (var b = new SolidBrush(col)) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = Parent != null ? Parent.BackColor : Theme.Bg;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            float k = Height / 36f;
            for (int i = 0; i < Count; i++)
            {
                var c = Cell(i);
                var center = new PointF(c.X + c.Width / 2f, c.Y + c.Height / 2f);
                bool custom = i == CustomIndex;
                Color col = custom ? (IsPreset ? Theme.Card : value) : Theme.Swatches[i];
                bool sel = custom ? !IsPreset : Theme.Swatches[i].ToArgb() == value.ToArgb();
                if (sel || i == hover) Disc(g, center, 15 * k, sel ? Theme.Text : Theme.Border);
                Disc(g, center, 13 * k, bg);
                Disc(g, center, 11 * k, col);
                if (custom && IsPreset)
                {
                    if (iconFont == null) iconFont = new Font(Theme.IconFont, 10f * k);
                    TextRenderer.DrawText(g, "\uE710", iconFont, c, Theme.Sub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding); // Add
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = Hit(e.Location);
            if (h < 0 || e.Button != MouseButtons.Left) return;
            if (h < CustomIndex) { Value = Theme.Swatches[h]; Fire(); return; }
            using (var dlg = new ColorDialog { Color = value, FullOpen = true, AnyColor = true })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                Value = dlg.Color;
                Fire();
            }
        }

        void Fire() { var p = Picked; if (p != null) p(this, EventArgs.Empty); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && iconFont != null) { iconFont.Dispose(); iconFont = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>Rounded card panel that hosts inputs/lists (paints its own rounded background).</summary>
    internal class RoundHost : Panel
    {
        public float Radius = 8;
        public Color Fill = Theme.Card;
        public Color Edge = Color.FromArgb(58, 58, 58);
        public RoundHost()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Ui.Fill(g, r, Fill, Radius);
            Ui.Stroke(g, r, Edge, Radius);
        }
    }

    /// <summary>Thin scroll thumb for a <see cref="ListBox"/> whose own (white) scrollbar is hidden.</summary>
    internal sealed class ScrollRail : Control
    {
        readonly ListBox list;
        bool drag;
        int dragOffset;
        public ScrollRail(ListBox list)
        {
            this.list = list;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        int Visible_ { get { return Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight)); } }
        bool Needed { get { return list.Items.Count > Visible_; } }
        RectangleF Thumb()
        {
            int n = list.Items.Count, vis = Visible_;
            float h = Math.Max(Height * vis / (float)Math.Max(1, n), 24);
            int maxTop = Math.Max(1, n - vis);
            float y = (Height - h) * Math.Min(1f, list.TopIndex / (float)maxTop);
            return new RectangleF(1, y, Width - 2, h);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card)) g.FillRectangle(bg, ClientRectangle);
            if (!Needed) return;
            var t = Thumb();
            Ui.Fill(g, t, drag ? Theme.Accent : Color.FromArgb(95, 95, 95), t.Width / 2);
        }
        void ScrollTo(int y)
        {
            int n = list.Items.Count, vis = Visible_;
            var t = Thumb();
            float span = Math.Max(1, Height - t.Height);
            int top = (int)Math.Round((y - dragOffset) / span * Math.Max(0, n - vis));
            list.TopIndex = Math.Max(0, Math.Min(Math.Max(0, n - vis), top));
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!Needed) return;
            var t = Thumb();
            drag = true;
            dragOffset = t.Contains(e.Location) ? (int)(e.Y - t.Y) : (int)(t.Height / 2);
            ScrollTo(e.Y);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) ScrollTo(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Invalidate(); }
    }

    /// <summary>Dark dropdown (replaces ComboBox, whose arrow box stays white).</summary>
    internal sealed class DarkCombo : Control
    {
        public readonly List<object> Items = new List<object>();
        int index = -1;
        bool hover;
        public event EventHandler SelectedIndexChanged;
        public int ItemHeight { get; set; }

        public DarkCombo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int SelectedIndex
        {
            get { return index; }
            set
            {
                if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException("value");
                if (value == index) return;
                index = value;
                Invalidate();
                var h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return index >= 0 && index < Items.Count ? Items[index] : null; }
            set
            {
                int i = -1;
                for (int k = 0; k < Items.Count; k++) if (Equals(Items[k], value)) { i = k; break; }
                SelectedIndex = i;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled || Items.Count == 0) return;
            var m = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Font, Padding = new Padding(0, 4, 0, 4) };
            for (int i = 0; i < Items.Count; i++)
            {
                int k = i;
                var it = new ToolStripMenuItem(Convert.ToString(Items[i])) { Checked = i == index, ForeColor = Theme.Text, AutoSize = false, Width = Width, Height = (int)(Height * 0.95f) };
                it.Click += (s, a) => SelectedIndex = k;
                m.Items.Add(it);
            }
            m.Closed += (s, a) => BeginInvoke(new Action(m.Dispose));
            m.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Math.Min(8, Height * 0.3f);
            Ui.Fill(g, r, hover && Enabled ? Theme.Hover : Theme.Card, rad);
            Ui.Stroke(g, r, Color.FromArgb(hover ? 90 : 58, 255, 255, 255), rad);
            int pad = (int)(Height * 0.35f);
            var tr = new Rectangle(pad, 0, Width - pad * 2 - Height / 2, Height);
            TextRenderer.DrawText(g, Convert.ToString(SelectedItem), Font, tr, Enabled ? Theme.Text : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            float cx = Width - pad - Height * 0.15f, cy = Height / 2f + 1, s = Height * 0.12f;
            using (var p = new Pen(Enabled ? Theme.Sub : Theme.Dim, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(p, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
        }
    }

    /// <summary>Dark number box with − / + (replaces NumericUpDown and its white spinner).</summary>
    internal sealed class DarkNum : RoundHost
    {
        readonly TextBox box = new TextBox();
        readonly RoundButton minus = new RoundButton(), plus = new RoundButton();
        decimal val, min, max = 100, inc = 1;
        int decimals;
        bool settingText;
        public event EventHandler ValueChanged;

        public DarkNum()
        {
            BackColor = Theme.Card;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Text;
            box.TextAlign = HorizontalAlignment.Center;
            Edge = Color.FromArgb(78, 78, 78); // a clearer outline than other cards
            foreach (var b in new[] { minus, plus }) { b.BackColor = Theme.Card; b.ForeColor = Theme.Text; b.TabStop = false; b.Flat = true; }
            minus.Sign = '-'; plus.Sign = '+';
            minus.Click += (s, e) => Value -= inc;
            plus.Click += (s, e) => Value += inc;
            box.Leave += (s, e) => Commit();
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { Value += inc; e.Handled = true; }
                else if (e.KeyCode == Keys.Down) { Value -= inc; e.Handled = true; }
            };
            box.MouseWheel += (s, e) => Value += e.Delta > 0 ? inc : -inc;
            Controls.Add(box); Controls.Add(minus); Controls.Add(plus);
            ShowText();
        }

        public decimal Minimum { get { return min; } set { min = value; Value = val; } }
        public decimal Maximum { get { return max; } set { max = value; Value = val; } }
        public decimal Increment { get { return inc; } set { inc = value; } }
        public int DecimalPlaces { get { return decimals; } set { decimals = value; ShowText(); } }
        public decimal Value
        {
            get { return val; }
            set
            {
                decimal v = Math.Max(min, Math.Min(max, Math.Round(value, decimals)));
                if (v == val) { ShowText(); return; }
                val = v;
                ShowText();
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        void ShowText()
        {
            settingText = true;
            box.Text = val.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), System.Globalization.CultureInfo.CurrentCulture);
            settingText = false;
        }

        void Commit()
        {
            if (settingText) return;
            decimal d;
            if (decimal.TryParse(box.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out d) ||
                decimal.TryParse(box.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out d)) Value = d;
            else ShowText();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = Height, bw = (int)(h * 0.9f);
            minus.SetBounds(1, 1, bw, h - 2);
            plus.SetBounds(Width - bw - 1, 1, bw, h - 2);
            box.SetBounds(bw + 4, (h - box.PreferredHeight) / 2 + 1, Width - 2 * bw - 8, box.PreferredHeight);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // thin dividers between − | value | +
            int bw = (int)(Height * 0.9f) + 1, inset = Math.Max(5, Height / 5);
            using (var p = new Pen(Color.FromArgb(70, 70, 70)))
            {
                e.Graphics.DrawLine(p, bw, inset, bw, Height - inset - 1);
                e.Graphics.DrawLine(p, Width - bw - 1, inset, Width - bw - 1, Height - inset - 1);
            }
        }
    }
}
