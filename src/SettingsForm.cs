using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Fizz
{
    /// <summary>Dark owner-drawn list with optional checkboxes and drag-to-reorder.</summary>
    internal sealed class DarkList : ListBox
    {
        public bool Checkable, Reorderable;
        public Func<object, bool> IsChecked;
        public Action<object> ToggleChecked;
        public Func<object, string> TextOf;
        public Func<object, string> SubTextOf;
        public event Action Reordered;
        int dragFrom = -1;
        Point dragStart;

        public ScrollRail Rail;

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style &= ~0x00200000; return cp; } // no WS_VSCROLL: Fizz draws its own thin scrollbar
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // wheel / keys / programmatic scrolling all end up here
            if (Rail != null && (m.Msg == 0x020A || m.Msg == 0x0100 || m.Msg == 0x0115 || m.Msg == 0x0197 || m.Msg == 0x0180 || m.Msg == 0x0184 || m.Msg == 0x0182)) Rail.Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            TopIndex = Math.Max(0, TopIndex - Math.Sign(e.Delta) * lines);
            if (Rail != null) Rail.Invalidate();
        }

        public DarkList(int itemHeight)
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = itemHeight;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            IntegralHeight = false;
            AllowDrop = true;
        }

        Rectangle CheckRect(Rectangle b)
        {
            int s = (int)(b.Height * 0.5);
            return new Rectangle(b.X + s / 2 + 2, b.Y + (b.Height - s) / 2, s, s);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(Theme.Card)) g.FillRectangle(bg, e.Bounds);
            if (sel) Ui.Fill(g, new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 1, e.Bounds.Width - 4, e.Bounds.Height - 2), Theme.Selected, 6);
            object item = Items[e.Index];
            int x = e.Bounds.X + 8;
            if (Checkable)
            {
                var cr = CheckRect(e.Bounds);
                bool on = IsChecked != null && IsChecked(item);
                Ui.Fill(g, cr, on ? Theme.Accent : Theme.Card, cr.Width * 0.3f);
                if (!on) Ui.Stroke(g, cr, Theme.Dim, cr.Width * 0.3f, 1.2f);
                if (on)
                    using (var p = new Pen(Color.White, Math.Max(1.6f, cr.Width / 7f)))
                        g.DrawLines(p, new[] { new PointF(cr.X + cr.Width * .22f, cr.Y + cr.Height * .52f), new PointF(cr.X + cr.Width * .43f, cr.Y + cr.Height * .73f), new PointF(cr.X + cr.Width * .8f, cr.Y + cr.Height * .3f) });
                x = cr.Right + 10;
            }
            string text = TextOf != null ? TextOf(item) : item.ToString();
            string sub = SubTextOf != null ? SubTextOf(item) : null;
            var tr = new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x - 6, e.Bounds.Height);
            TextRenderer.DrawText(g, text, Font, tr, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(sub))
                TextRenderer.DrawText(g, sub, Font, tr, Theme.Sub, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);

        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = IndexFromPoint(e.Location);
            if (i < 0) return;
            if (Checkable && e.Button == MouseButtons.Left && CheckRect(GetItemRectangle(i)).Contains(e.Location) && ToggleChecked != null)
            {
                ToggleChecked(Items[i]);
                Invalidate(GetItemRectangle(i));
                return;
            }
            if (Reorderable) { dragFrom = i; dragStart = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragFrom >= 0 && e.Button == MouseButtons.Left && Math.Abs(e.Y - dragStart.Y) > 4)
            {
                int from = dragFrom;
                dragFrom = -1;
                DoDragDrop(from, DragDropEffects.Move);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragFrom = -1; }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = e.Data.GetDataPresent(typeof(int)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (!e.Data.GetDataPresent(typeof(int))) return;
            int from = (int)e.Data.GetData(typeof(int));
            int to = IndexFromPoint(PointToClient(new Point(e.X, e.Y)));
            if (to < 0) to = Items.Count - 1;
            MoveItem(from, to);
        }

        public void MoveItem(int from, int to)
        {
            if (from < 0 || from >= Items.Count || to < 0 || to >= Items.Count || from == to) return;
            object it = Items[from];
            Items.RemoveAt(from);
            Items.Insert(to, it);
            SelectedIndex = to;
            var r = Reordered; if (r != null) r();
        }
    }

    internal sealed class SettingsForm : Form
    {
        readonly FizzApp app;
        FizzSettings S { get { return app.Settings; } }
        readonly float k;
        readonly Panel nav = new Panel(), body = new Panel(), footer = new Panel();
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly List<Button> navButtons = new List<Button>();
        readonly Label status = new Label();
        bool loading;
        SensorPickerForm picker;

        // stats page
        DarkCombo presetBox;
        DarkList statList;
        Label statTitle, thrHint;
        CheckBox showLabel, showUnit, colorsOn;
        DarkNum amber, red;
        Panel thrPanel;
        // look page
        DarkCombo layoutBox;
        Slider fontBar, opacityBar;
        Label fontVal, opacityVal, lookFor;
        CheckBox bgCheck;
        SwatchRow accentRow;
        DarkCombo cornerBox, modeBox, rateBox;
        DarkNum marginBox;
        // games page
        DarkList gameList;
        TextBox gameInput, notGamesBox;
        DarkCombo gamePresetBox;
        CheckBox gameHide;
        Button addCurrent, notGameBtn;
        // advanced
        TextBox hkToggle, hkLayout, hkPreset;
        Label hkProblem;
        CheckBox startWin, summaryCheck, csvCheck, gameSummary;

        int S96(float v) { return (int)Math.Round(v * k); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.DarkTitleBar(this); }

        public SettingsForm(FizzApp app)
        {
            this.app = app;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            Text = "Fizz settings";
            Icon = Theme.LoadIcon(32);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S96(840), S96(576));
            AutoScaleMode = AutoScaleMode.None;

            nav.SetBounds(0, 0, S96(176), S96(524)); nav.BackColor = Theme.Nav;
            body.SetBounds(S96(176), 0, S96(664), S96(524)); body.BackColor = Theme.Bg;
            footer.SetBounds(0, S96(524), S96(840), S96(52)); footer.BackColor = Theme.Nav;
            Controls.AddRange(new Control[] { nav, body, footer });

            var mark = new PictureBox { Image = Theme.LoadIcon(S96(28)).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(S96(18), S96(18), S96(28), S96(28)), BackColor = Color.Transparent };
            var logo = new Label { Text = "Fizz", Font = Theme.UiTitle(15f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(S96(52), S96(16)), BackColor = Color.Transparent };
            nav.Controls.Add(mark);
            nav.Controls.Add(logo);
            if (IsHandleCreated) Ui.DarkTitleBar(this); // the DPI check above already created the window
            BuildStatsPage(); BuildLookPage(); BuildGamesPage(); BuildAdvancedPage();
            int y = S96(70);
            var glyphs = new[] { Theme.GStats, Theme.GLook, Theme.GGames, Theme.GAdvanced };
            var names = new[] { "Stats", "Look", "Games", "Advanced" };
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                var b = NavButton(n, glyphs[i], y);
                b.Click += (s, e) => ShowPage(n);
                nav.Controls.Add(b); navButtons.Add(b);
                y += S96(42);
            }

            status.SetBounds(S96(18), S96(8), S96(700), S96(36));
            status.ForeColor = Theme.Sub;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.UseMnemonic = false;
            footer.Controls.Add(status);
            var close = Btn("Close", S96(736), S96(11), S96(88));
            close.Click += (s, e) => Close();
            footer.Controls.Add(close);

            var tick = new Timer { Interval = 1000 };
            tick.Tick += (s, e) => UpdateStatus();
            tick.Start();
            FormClosed += (s, e) => { tick.Dispose(); if (picker != null && !picker.IsDisposed) picker.Close(); };

            RefreshFromApp();
            ShowPage("Stats");
        }

        // ---------- small control factories ----------

        Button NavButton(string text, string glyph, int y)
        {
            return new RoundButton
            {
                Text = text, Glyph = glyph, NavStyle = true, ForeColor = Theme.Sub, BackColor = Theme.Nav, Font = Theme.Ui(10.5f),
                Bounds = new Rectangle(S96(10), y, S96(156), S96(38)), Tag = text
            };
        }

        Button Btn(string text, int x, int y, int w)
        {
            return new RoundButton { Text = text, ForeColor = Theme.Text, BackColor = Theme.Card, Bounds = new Rectangle(x, y, w, S96(30)) };
        }

        static Label Lbl(Control parent, string text, int x, int y, Color? color = null, Font font = null)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(x, y), ForeColor = color ?? Theme.Sub, BackColor = Color.Transparent, UseMnemonic = false };
            if (font != null) l.Font = font;
            parent.Controls.Add(l);
            return l;
        }

        CheckBox Chk(Control parent, string text, int x, int y, EventHandler onChange)
        {
            var c = new Toggle { Text = text, Location = new Point(x, y - S96(2)), ForeColor = Theme.Text, Font = Font };
            c.FitWidth(S96(24));
            c.CheckedChanged += (s, e) => { if (!loading) onChange(s, e); };
            parent.Controls.Add(c);
            return c;
        }

        DarkCombo Combo(Control parent, int x, int y, int w, EventHandler onChange)
        {
            var c = new DarkCombo { Bounds = new Rectangle(x, y - S96(2), w, S96(30)), Font = Font };
            c.SelectedIndexChanged += (s, e) => { if (!loading) onChange(s, e); };
            parent.Controls.Add(c);
            return c;
        }

        DarkNum Num(Control parent, int x, int y, decimal min, decimal max, decimal step, int decimals, EventHandler onChange)
        {
            var n = new DarkNum { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, Font = Font, Bounds = new Rectangle(x, y - S96(2), S96(96), S96(30)) };
            n.ValueChanged += (s, e) => { if (!loading) onChange(s, e); };
            parent.Controls.Add(n);
            return n;
        }

        TextBox Txt(Control parent, int x, int y, int w)
        {
            var host = new RoundHost { Bounds = new Rectangle(x, y - S96(2), w, S96(30)), BackColor = Theme.Card };
            var t = new TextBox { BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Font };
            t.SetBounds(S96(8), (host.Height - t.PreferredHeight) / 2 + 1, w - S96(16), t.PreferredHeight);
            host.Controls.Add(t);
            host.Click += (s, e) => t.Focus();
            parent.Controls.Add(host);
            return t;
        }

        /// <summary>Rounded card holding a dark list plus its thin scrollbar.</summary>
        DarkList MakeList(Control parent, Rectangle bounds)
        {
            var host = new RoundHost { Bounds = bounds, BackColor = Theme.Card, Radius = 10 };
            var list = new DarkList(S96(30)) { Font = Font };
            int pad = S96(6), rail = S96(6);
            list.SetBounds(pad, pad, bounds.Width - pad * 2 - rail - S96(4), bounds.Height - pad * 2);
            list.Rail = new ScrollRail(list) { Bounds = new Rectangle(bounds.Width - pad - rail, pad, rail, bounds.Height - pad * 2), BackColor = Theme.Card };
            host.Controls.Add(list);
            host.Controls.Add(list.Rail);
            parent.Controls.Add(host);
            return list;
        }

        Panel Page(string name)
        {
            var p = new Panel { Bounds = new Rectangle(0, 0, body.Width, body.Height), BackColor = Theme.Bg, Visible = false };
            body.Controls.Add(p);
            pages[name] = p;
            Lbl(p, name, S96(24), S96(14), Theme.Text, Theme.UiTitle(15f));
            return p;
        }

        void ShowPage(string name)
        {
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var b in navButtons)
            {
                var rb = b as RoundButton;
                if (rb != null) { rb.Selected = (string)b.Tag == name; rb.Invalidate(); }
            }
        }

        void Changed() { if (!loading) app.SettingsChanged(true); }

        Preset EditingPreset { get { return S.FindPreset(presetBox != null ? presetBox.SelectedItem as string : null) ?? S.Active; } }

        // ================= Stats =================

        void BuildStatsPage()
        {
            var p = Page("Stats");
            Lbl(p, "Preset", S96(24), S96(58));
            presetBox = Combo(p, S96(80), S96(54), S96(170), (s, e) =>
            {
                S.ActivePreset = presetBox.SelectedItem as string ?? S.ActivePreset;
                Changed();
                LoadPresetIntoUi();
            });
            var nw = Btn("New", S96(260), S96(53), S96(60)); nw.Click += (s, e) => NewPreset(); p.Controls.Add(nw);
            var rn = Btn("Rename", S96(326), S96(53), S96(70)); rn.Click += (s, e) => RenamePreset(); p.Controls.Add(rn);
            var dl = Btn("Delete", S96(402), S96(53), S96(64)); dl.Click += (s, e) => DeletePreset(); p.Controls.Add(dl);
            Lbl(p, "Ctrl+Alt+P cycles", S96(476), S96(58), Theme.Dim);

            Lbl(p, "Tick what to show. Drag to reorder.", S96(24), S96(94), Theme.Dim);
            statList = MakeList(p, new Rectangle(S96(24), S96(116), S96(290), S96(340)));
            statList.Checkable = true; statList.Reorderable = true;
            statList.TextOf = o => Stats.Get(ItemStat((StatItem)o)).Name;
            statList.SubTextOf = o => { var d = Stats.Get(ItemStat((StatItem)o)); return d.Label; };
            statList.IsChecked = o => ((StatItem)o).On;
            statList.ToggleChecked = o => { ((StatItem)o).On = !((StatItem)o).On; Changed(); };
            statList.Reordered += () =>
            {
                var pr = EditingPreset;
                pr.Items.Clear();
                foreach (StatItem it in statList.Items) pr.Items.Add(it);
                Changed();
            };
            statList.SelectedIndexChanged += (s, e) => LoadStatDetails();
            var up = Btn("", S96(24), S96(462), S96(40)); ((RoundButton)up).Glyph = Theme.GUp; up.Click += (s, e) => statList.MoveItem(statList.SelectedIndex, statList.SelectedIndex - 1); p.Controls.Add(up);
            var dn = Btn("", S96(70), S96(462), S96(40)); ((RoundButton)dn).Glyph = Theme.GDown; dn.Click += (s, e) => statList.MoveItem(statList.SelectedIndex, statList.SelectedIndex + 1); p.Controls.Add(dn);

            int x = S96(336);
            statTitle = Lbl(p, "", x, S96(116), Theme.Text, Theme.UiSemi(10.5f));
            showLabel = Chk(p, "Show label (e.g. \"GPU\")", x, S96(150), (s, e) => { var it = SelectedItem(); if (it != null) { it.Label = showLabel.Checked; Changed(); } });
            showUnit = Chk(p, "Show unit (64°C vs 64°)", x, S96(178), (s, e) => { var it = SelectedItem(); if (it != null) { it.Unit = showUnit.Checked; Changed(); } });

            thrPanel = new Panel { Bounds = new Rectangle(x, S96(218), S96(284), S96(170)), BackColor = Theme.Bg };
            p.Controls.Add(thrPanel);
            colorsOn = Chk(thrPanel, "Color by value", 0, 0, (s, e) => SaveThreshold());
            Lbl(thrPanel, "Amber", 0, S96(38), Theme.Warn);
            amber = Num(thrPanel, S96(50), S96(34), -1000, 100000, 1, 1, (s, e) => SaveThreshold());
            amber.Width = S96(88);
            Lbl(thrPanel, "Red", S96(154), S96(38), Color.FromArgb(255, 82, 82));
            red = Num(thrPanel, S96(186), S96(34), -1000, 100000, 1, 1, (s, e) => SaveThreshold());
            red.Width = S96(88);
            thrHint = Lbl(thrPanel, "", 0, S96(76), Theme.Dim);
            thrHint.AutoSize = false; thrHint.Size = new Size(S96(284), S96(90));
            Lbl(p, "Colors apply to every preset.", x, S96(392), Theme.Dim);
        }

        static Stat ItemStat(StatItem it) { Stat s; Stats.TryParse(it.Id, out s); return s; }
        StatItem SelectedItem() { return statList.SelectedItem as StatItem; }

        void LoadPresetIntoUi()
        {
            loading = true;
            try
            {
                var pr = EditingPreset;
                int sel = Math.Max(0, statList.SelectedIndex);
                statList.Items.Clear();
                foreach (var it in pr.Items) statList.Items.Add(it);
                if (statList.Items.Count > 0) statList.SelectedIndex = Math.Min(sel, statList.Items.Count - 1);
                layoutBox.SelectedIndex = pr.Vertical ? 1 : 0;
                fontBar.Value = Math.Max(fontBar.Minimum, Math.Min(fontBar.Maximum, (int)Math.Round(pr.FontSize * 2)));
                fontVal.Text = pr.FontSize.ToString("0.#") + " pt";
                opacityBar.Value = Math.Max(opacityBar.Minimum, Math.Min(opacityBar.Maximum, pr.Opacity));
                opacityVal.Text = pr.Opacity + "%";
                bgCheck.Checked = pr.Background;
                Color c; FizzSettings.TryColor(pr.Accent, out c);
                accentRow.Value = c.IsEmpty ? Theme.Accent : c;
                lookFor.Text = "Look of preset \"" + pr.Name + "\"";
            }
            finally { loading = false; }
            LoadStatDetails();
        }

        void LoadStatDetails()
        {
            var it = SelectedItem();
            bool was = loading;
            loading = true;
            try
            {
                if (it == null) { statTitle.Text = ""; thrPanel.Visible = false; return; }
                var id = ItemStat(it);
                var d = Stats.Get(id);
                statTitle.Text = d.Name;
                showLabel.Checked = it.Label;
                showLabel.Enabled = d.Label.Length > 0;
                showUnit.Checked = it.Unit;
                var t = S.Threshold(id);
                thrPanel.Visible = d.HasThreshold || id == Stat.PresentMode;
                if (t != null)
                {
                    colorsOn.Checked = t.Enabled;
                    bool numeric = d.HasThreshold;
                    amber.Visible = red.Visible = numeric;
                    foreach (Control c in thrPanel.Controls) if (c is Label && c != thrHint) c.Visible = numeric;
                    if (numeric)
                    {
                        amber.Value = Clamp((decimal)t.Amber, amber);
                        red.Value = Clamp((decimal)t.Red, red);
                        thrHint.Text = (d.LowerIsWorse ? "Turns amber, then red, when it drops below these." : "Turns amber, then red, once it reaches these.") +
                                       (d.IsTemp ? "\nBelow amber it shows green." : "");
                    }
                    else thrHint.Text = "Green: iFlip / MPO iFlip (lowest latency)\nAmber: Composed (DWM adds a frame)\nRed: Copy (slowest path)";
                    thrHint.Top = numeric ? S96(76) : S96(32);
                }
            }
            finally { loading = was; }
        }

        static decimal Clamp(decimal v, DarkNum n) { return Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        void SaveThreshold()
        {
            var it = SelectedItem();
            if (it == null || loading) return;
            var t = S.Threshold(ItemStat(it));
            if (t == null) return;
            t.Enabled = colorsOn.Checked;
            t.Amber = (float)amber.Value;
            t.Red = (float)red.Value;
            Changed();
        }

        void NewPreset()
        {
            string name = Prompt("New preset", "Name for the new preset (copies the current one):", "My preset");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (S.FindPreset(name) != null) { Notice("A preset with that name already exists."); return; }
            S.Presets.Add(EditingPreset.Clone(name));
            S.ActivePreset = name;
            Changed();
            RefreshFromApp();
        }

        void RenamePreset()
        {
            var pr = EditingPreset;
            string name = Prompt("Rename preset", "New name:", pr.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == pr.Name) return;
            name = name.Trim();
            if (S.FindPreset(name) != null) { Notice("A preset with that name already exists."); return; }
            foreach (var g in S.Games) if (string.Equals(g.Preset, pr.Name, StringComparison.OrdinalIgnoreCase)) g.Preset = name;
            pr.Name = name;
            S.ActivePreset = name;
            Changed();
            RefreshFromApp();
        }

        void DeletePreset()
        {
            if (S.Presets.Count <= 1) { Notice("You need at least one preset."); return; }
            var pr = EditingPreset;
            if (!Confirm("Delete preset \"" + pr.Name + "\"?", "Delete")) return;
            S.Presets.Remove(pr);
            foreach (var g in S.Games) if (string.Equals(g.Preset, pr.Name, StringComparison.OrdinalIgnoreCase)) g.Preset = "";
            S.ActivePreset = S.Presets[0].Name;
            Changed();
            RefreshFromApp();
        }

        Form DialogForm(string title, int h)
        {
            var f = new Form
            {
                Text = title, BackColor = Theme.Bg, ForeColor = Theme.Text, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(S96(360), h), Font = Font, AutoScaleMode = AutoScaleMode.None
            };
            f.HandleCreated += (s, e) => Ui.DarkTitleBar(f);
            return f;
        }

        string Prompt(string title, string text, string value)
        {
            using (var f = DialogForm(title, S96(128)))
            {
                Lbl(f, text, S96(16), S96(16), Theme.Text);
                var t = Txt(f, S96(16), S96(44), S96(328)); t.Text = value;
                var ok = Btn("OK", S96(178), S96(86), S96(80)); ok.DialogResult = DialogResult.OK; f.Controls.Add(ok);
                var cancel = Btn("Cancel", S96(264), S96(86), S96(80)); cancel.DialogResult = DialogResult.Cancel; f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
            }
        }

        /// <summary>Dark yes/no question (replaces the white Windows message box).</summary>
        bool Confirm(string text, string yes)
        {
            using (var f = DialogForm("Fizz", S96(118)))
            {
                var l = Lbl(f, text, S96(16), S96(18), Theme.Text);
                l.AutoSize = false; l.Size = new Size(S96(328), S96(44));
                var ok = Btn(yes, S96(178), S96(76), S96(80)); ok.DialogResult = DialogResult.OK; f.Controls.Add(ok);
                var cancel = Btn("Cancel", S96(264), S96(76), S96(80)); cancel.DialogResult = DialogResult.Cancel; f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK;
            }
        }

        void Notice(string text)
        {
            using (var f = DialogForm("Fizz", S96(118)))
            {
                var l = Lbl(f, text, S96(16), S96(18), Theme.Text);
                l.AutoSize = false; l.Size = new Size(S96(328), S96(44));
                var ok = Btn("OK", S96(264), S96(76), S96(80)); ok.DialogResult = DialogResult.OK; f.Controls.Add(ok);
                f.AcceptButton = ok; f.CancelButton = ok;
                f.ShowDialog(this);
            }
        }

        // ================= Look =================

        void BuildLookPage()
        {
            var p = Page("Look");
            lookFor = Lbl(p, "", S96(24), S96(52), Theme.Dim);
            int x = S96(24), x2 = S96(160), y = S96(84);
            Lbl(p, "Layout", x, y + 4);
            layoutBox = Combo(p, x2, y, S96(160), (s, e) => { EditingPreset.Layout = layoutBox.SelectedIndex == 1 ? "Vertical" : "Horizontal"; Changed(); });
            layoutBox.Items.AddRange(new object[] { "Horizontal", "Vertical" });
            Lbl(p, "Ctrl+Alt+L switches", x2 + S96(210), y + 4, Theme.Dim);

            y += S96(40);
            Lbl(p, "Font size", x, y + 6);
            fontBar = new Slider { Minimum = 14, Maximum = 44, Bounds = new Rectangle(x2, y, S96(260), S96(30)), BackColor = Theme.Bg };
            fontBar.ValueChanged += (s, e) => { fontVal.Text = (fontBar.Value / 2f).ToString("0.#") + " pt"; if (!loading) { EditingPreset.FontSize = fontBar.Value / 2f; Changed(); } };
            p.Controls.Add(fontBar);
            fontVal = Lbl(p, "", x2 + S96(270), y + 6, Theme.Text);

            y += S96(40);
            Lbl(p, "Opacity", x, y + 6);
            opacityBar = new Slider { Minimum = 20, Maximum = 100, Bounds = new Rectangle(x2, y, S96(260), S96(30)), BackColor = Theme.Bg };
            opacityBar.ValueChanged += (s, e) => { opacityVal.Text = opacityBar.Value + "%"; if (!loading) { EditingPreset.Opacity = opacityBar.Value; Changed(); } };
            p.Controls.Add(opacityBar);
            opacityVal = Lbl(p, "", x2 + S96(270), y + 6, Theme.Text);

            y += S96(42);
            bgCheck = Chk(p, "Dark background behind the stats", x2, y, (s, e) => { EditingPreset.Background = bgCheck.Checked; Changed(); });

            y += S96(36);
            Lbl(p, "Accent color", x, y + 5);
            int sw = S96(24);
            accentRow = new SwatchRow { Bounds = new Rectangle(x2, y + S96(3), (int)Math.Ceiling(sw * 1.18f * (Theme.Swatches.Length - 1)) + sw + 2, sw), BackColor = Theme.Bg };
            accentRow.Picked += (s, e) => { EditingPreset.Accent = Theme.Hex(accentRow.Value); Changed(); };
            p.Controls.Add(accentRow);

            y += S96(52);
            Lbl(p, "These apply to all presets:", x, y, Theme.Dim);
            y += S96(28);
            Lbl(p, "Show overlay", x, y + 4);
            modeBox = Combo(p, x2, y, S96(220), (s, e) => { S.Mode = modeBox.SelectedIndex == 1 ? "Always" : "Auto"; Changed(); });
            modeBox.Items.AddRange(new object[] { "Only when a game is in front", "Always" });
            y += S96(36);
            Lbl(p, "Position", x, y + 4);
            cornerBox = Combo(p, x2, y, S96(140), (s, e) => { S.Corner = new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }[cornerBox.SelectedIndex]; Changed(); });
            cornerBox.Items.AddRange(new object[] { "Top left", "Top right", "Bottom left", "Bottom right" });
            Lbl(p, "Margin", x2 + S96(156), y + 4);
            marginBox = Num(p, x2 + S96(208), y, 0, 200, 2, 0, (s, e) => { S.Margin = (int)marginBox.Value; Changed(); });
            y += S96(36);
            Lbl(p, "Update rate", x, y + 4);
            rateBox = Combo(p, x2, y, S96(140), (s, e) => { S.SampleMs = new[] { 250, 500, 1000 }[rateBox.SelectedIndex]; Changed(); });
            rateBox.Items.AddRange(new object[] { "4x per second", "2x per second", "1x per second" });
        }

        // ================= Games =================

        void BuildGamesPage()
        {
            var p = Page("Games");
            Lbl(p, "Rules per game (by exe name). Hidden games never draw the overlay: zero added latency.", S96(24), S96(52), Theme.Dim);
            gameList = MakeList(p, new Rectangle(S96(24), S96(78), S96(330), S96(220)));
            gameList.TextOf = o => ((GameRule)o).Exe;
            gameList.SubTextOf = o =>
            {
                var g = (GameRule)o;
                var tags = new List<string>();
                if (g.Hide) tags.Add("hidden");
                if (g.Summary) tags.Add("summary");
                if (!string.IsNullOrEmpty(g.Preset)) tags.Add(g.Preset);
                return string.Join(" · ", tags);
            };
            gameList.SelectedIndexChanged += (s, e) => LoadGameDetails();

            int x = S96(372);
            gameHide = Chk(p, "Hide overlay in this game", x, S96(80), (s, e) => { var g = gameList.SelectedItem as GameRule; if (g != null) { g.Hide = gameHide.Checked; Changed(); gameList.Invalidate(); } });
            gameSummary = Chk(p, "Performance summary when it closes", x, S96(112), (s, e) => { var g = gameList.SelectedItem as GameRule; if (g != null) { g.Summary = gameSummary.Checked; Changed(); gameList.Invalidate(); } });
            Lbl(p, "Preset in this game", x, S96(150));
            gamePresetBox = Combo(p, x, S96(172), S96(200), (s, e) =>
            {
                var g = gameList.SelectedItem as GameRule;
                if (g == null) return;
                g.Preset = gamePresetBox.SelectedIndex <= 0 ? "" : (string)gamePresetBox.SelectedItem;
                Changed(); gameList.Invalidate();
            });
            var remove = Btn("Remove rule", x, S96(214), S96(110));
            remove.Click += (s, e) => { var g = gameList.SelectedItem as GameRule; if (g != null) { S.Games.Remove(g); Changed(); LoadGames(); } };
            p.Controls.Add(remove);

            gameInput = Txt(p, S96(24), S96(308), S96(180));
            var add = Btn("Add exe", S96(212), S96(307), S96(84));
            add.Click += (s, e) => AddGame(gameInput.Text);
            p.Controls.Add(add);
            addCurrent = Btn("Add last game", S96(304), S96(307), S96(196));
            addCurrent.Click += (s, e) => AddGame(app.LastGameExe);
            p.Controls.Add(addCurrent);
            notGameBtn = Btn("Not a game", S96(508), S96(307), S96(104));
            notGameBtn.Click += (s, e) => { app.MarkNotGame(app.LastGameExe); LoadGames(); };
            p.Controls.Add(notGameBtn);

            Lbl(p, "Never treat these as games (one exe per line). Browsers and video players are here by default:", S96(24), S96(350), Theme.Dim);
            var ngHost = new RoundHost { Bounds = new Rectangle(S96(24), S96(374), S96(588), S96(120)), BackColor = Theme.Card };
            notGamesBox = new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Font,
                Bounds = new Rectangle(S96(10), S96(8), S96(570), S96(104))
            };
            notGamesBox.HandleCreated += (s, e) => Ui.DarkScrollbars(notGamesBox);
            var ngTimer = new Timer { Interval = 600 };
            ngTimer.Tick += (s, e) => { ngTimer.Stop(); SaveNotGames(); };
            notGamesBox.TextChanged += (s, e) => { if (!loading) { ngTimer.Stop(); ngTimer.Start(); } };
            notGamesBox.Leave += (s, e) => SaveNotGames();
            FormClosing += (s, e) => { ngTimer.Stop(); SaveNotGames(); };
            ngHost.Controls.Add(notGamesBox);
            p.Controls.Add(ngHost);
        }

        void SaveNotGames()
        {
            if (notGamesBox == null) return;
            var list = notGamesBox.Lines.Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0)
                .Select(l => l.Contains("\\") ? System.IO.Path.GetFileName(l) : l)
                .Select(l => l.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? l : l + ".exe")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.SequenceEqual(S.NotGames, StringComparer.OrdinalIgnoreCase)) return;
            S.NotGames = list;
            Changed();
        }

        void AddGame(string exe)
        {
            exe = (exe ?? "").Trim().Trim('"');
            if (exe.Length == 0) return;
            if (exe.Contains("\\")) exe = System.IO.Path.GetFileName(exe);
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            var g = S.GetOrAddGame(exe);
            Changed();
            LoadGames();
            gameList.SelectedItem = g;
            gameInput.Text = "";
        }

        void LoadGames()
        {
            loading = true;
            try
            {
                // fill the preset dropdown first: selecting a row reads it
                gamePresetBox.Items.Clear();
                gamePresetBox.Items.Add("(use active preset)");
                foreach (var pr in S.Presets) gamePresetBox.Items.Add(pr.Name);
                var sel = gameList.SelectedItem;
                gameList.Items.Clear();
                foreach (var g in S.Games) gameList.Items.Add(g);
                if (sel != null && gameList.Items.Contains(sel)) gameList.SelectedItem = sel;
                else if (gameList.Items.Count > 0) gameList.SelectedIndex = 0;
                notGamesBox.Text = string.Join(Environment.NewLine, S.NotGames);
                string last = app.LastGameExe;
                addCurrent.Text = string.IsNullOrEmpty(last) ? "Add last game" : "Add " + last;
                addCurrent.Enabled = !string.IsNullOrEmpty(last);
                notGameBtn.Enabled = !string.IsNullOrEmpty(last);
            }
            finally { loading = false; }
            LoadGameDetails();
        }

        void LoadGameDetails()
        {
            var g = gameList.SelectedItem as GameRule;
            bool was = loading;
            loading = true;
            try
            {
                gameHide.Enabled = gamePresetBox.Enabled = gameSummary.Enabled = g != null;
                if (g == null || gamePresetBox.Items.Count == 0) return;
                gameHide.Checked = g.Hide;
                gameSummary.Checked = g.Summary;
                int idx = string.IsNullOrEmpty(g.Preset) ? 0 : gamePresetBox.Items.IndexOf(g.Preset);
                gamePresetBox.SelectedIndex = idx < 0 ? 0 : idx;
            }
            finally { loading = was; }
        }

        // ================= Advanced =================

        void BuildAdvancedPage()
        {
            var p = Page("Advanced");
            int x = S96(24), x2 = S96(170), y = S96(56);
            Lbl(p, "Hotkeys", x, y, Theme.Text);
            y += S96(26);
            Lbl(p, "Show / hide", x, y + 4); hkToggle = Txt(p, x2, y, S96(140));
            y += S96(32);
            Lbl(p, "Switch layout", x, y + 4); hkLayout = Txt(p, x2, y, S96(140));
            y += S96(32);
            Lbl(p, "Next preset", x, y + 4); hkPreset = Txt(p, x2, y, S96(140));
            var apply = Btn("Apply hotkeys", x2 + S96(152), y, S96(120));
            apply.Click += (s, e) =>
            {
                uint m, v;
                foreach (var t in new[] { hkToggle, hkLayout, hkPreset })
                    if (!HubWindow.ParseHotkey(t.Text, out m, out v)) { hkProblem.Text = "Can't read \"" + t.Text + "\". Use something like Ctrl+Alt+F."; return; }
                S.HotkeyToggle = hkToggle.Text.Trim(); S.HotkeyLayout = hkLayout.Text.Trim(); S.HotkeyPreset = hkPreset.Text.Trim();
                app.HotkeysChanged();
                Changed();
                hkProblem.Text = app.HotkeyProblem ?? "Hotkeys set.";
            };
            p.Controls.Add(apply);
            y += S96(32);
            hkProblem = Lbl(p, "", x2, y, Theme.Warn);

            y += S96(30);
            startWin = Chk(p, "Start with Windows (runs as admin via a scheduled task, no UAC prompt)", x, y, (s, e) => app.StartWithWindowsChanged(startWin.Checked));
            y += S96(28);
            summaryCheck = Chk(p, "Session summaries (only for games you turn it on for, on the Games page)", x, y, (s, e) => { S.SessionSummary = summaryCheck.Checked; Changed(); });
            y += S96(28);
            csvCheck = Chk(p, "Log stats to a CSV file per game session", x, y, (s, e) => { S.CsvLog = csvCheck.Checked; Changed(); });
            var openLogs = new LinkLabel { Text = "open logs", AutoSize = true, Location = new Point(csvCheck.Right + S96(8), y + 1), LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, LinkBehavior = LinkBehavior.HoverUnderline };
            openLogs.Click += (s, e) => { string d = System.IO.Path.Combine(Log.Dir, "logs"); System.IO.Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); };
            p.Controls.Add(openLogs);

            y += S96(40);
            Lbl(p, "Lowest latency in borderless games", x, y, Theme.Text);
            y += S96(22);
            var tip = Lbl(p, "Windows Settings → System → Display → Graphics → Advanced graphics settings (or \"Change default graphics settings\"): turn on " +
                             "\"Optimizations for windowed games\" and \"Hardware-accelerated GPU scheduling\". Add competitive games to the Games page as hidden.", x, y, Theme.Sub);
            tip.AutoSize = false; tip.Size = new Size(S96(610), S96(66));

            y += S96(78);
            var diag = Btn("Write diagnostics", x, y, S96(140));
            diag.Click += (s, e) => { app.WriteStatus(); app.WriteSensorDump(); Process.Start("explorer.exe", "/select,\"" + System.IO.Path.Combine(Log.Dir, "status.txt") + "\""); };
            p.Controls.Add(diag);
            var folder = Btn("Open data folder", x + S96(150), y, S96(140));
            folder.Click += (s, e) => Process.Start("explorer.exe", "\"" + Log.Dir + "\"");
            p.Controls.Add(folder);
            // the "hidden" sensor picker: a quiet link, not a big button
            var pick = new LinkLabel { Text = "sensor picker…", AutoSize = true, Location = new Point(S96(520), y + 6), LinkColor = Theme.Dim, ActiveLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline };
            pick.Click += (s, e) =>
            {
                if (picker == null || picker.IsDisposed) picker = new SensorPickerForm(app);
                picker.Show(this);
                picker.Activate();
            };
            p.Controls.Add(pick);
        }

        // ================= refresh / status =================

        public void RefreshFromApp()
        {
            if (IsDisposed) return;
            loading = true;
            try
            {
                presetBox.Items.Clear();
                foreach (var pr in S.Presets) presetBox.Items.Add(pr.Name);
                presetBox.SelectedItem = S.Active.Name;
                modeBox.SelectedIndex = S.AlwaysShow ? 1 : 0;
                cornerBox.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }, S.Corner));
                marginBox.Value = Clamp(S.Margin, marginBox);
                rateBox.SelectedIndex = S.SampleMs <= 250 ? 0 : S.SampleMs <= 500 ? 1 : 2;
                hkToggle.Text = S.HotkeyToggle; hkLayout.Text = S.HotkeyLayout; hkPreset.Text = S.HotkeyPreset;
                hkProblem.Text = app.HotkeyProblem ?? "";
                startWin.Checked = S.StartWithWindows;
                summaryCheck.Checked = S.SessionSummary;
                csvCheck.Checked = S.CsvLog;
            }
            finally { loading = false; }
            LoadPresetIntoUi();
            LoadGames();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var miss = app.MissingFeatures();
            if (miss.Count == 0)
            {
                status.ForeColor = Theme.Sub;
                status.Text = "Everything's working. The overlay is previewing while this window is open.";
            }
            else
            {
                status.ForeColor = Theme.Warn;
                status.Text = "Missing: " + string.Join("; ", miss) + ".";
            }
        }
    }

    /// <summary>Lists every temperature sensor with live values so a wrong default can be overridden.</summary>
    internal sealed class SensorPickerForm : Form
    {
        readonly FizzApp app;
        readonly DarkList list;
        readonly Label current;
        readonly Timer timer = new Timer { Interval = 1000 };
        readonly float k;
        int S96(float v) { return (int)Math.Round(v * k); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.DarkTitleBar(this); }

        public SensorPickerForm(FizzApp app)
        {
            this.app = app;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            Text = "Fizz sensor picker";
            Icon = Theme.LoadIcon(32);
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui(10f);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S96(620), S96(470));

            Controls.Add(new Label { Text = "Temperature sensors (live). Pick one if a default reads wrong. Picking a GPU sensor also makes that GPU the source for GPU load, clock, power and VRAM.", AutoSize = false, Bounds = new Rectangle(S96(14), S96(10), S96(590), S96(36)), ForeColor = Theme.Sub });
            list = new DarkList(S96(26)) { Bounds = new Rectangle(S96(14), S96(52), S96(592), S96(300)) };
            list.TextOf = o => { var x = (SensorInfo)o; return x.Hardware + "  ·  " + x.Name; };
            list.SubTextOf = o =>
            {
                var x = (SensorInfo)o;
                string tag = x.Identifier == app.Settings.CpuTempSensor.Identifier ? "CPU pick   " : x.Identifier == app.Settings.GpuTempSensor.Identifier ? "GPU pick   " : "";
                return tag + (float.IsNaN(x.Value) ? "--" : x.Value.ToString("0.0") + " °C");
            };
            Controls.Add(list);

            current = new Label { AutoSize = false, Bounds = new Rectangle(S96(14), S96(360), S96(592), S96(40)), ForeColor = Theme.Text };
            Controls.Add(current);

            AddBtn("Use for CPU temp", S96(14), () => Pick(true));
            AddBtn("Use for GPU temp", S96(150), () => Pick(false));
            AddBtn("CPU: automatic", S96(286), () => { app.Settings.CpuTempSensor = new SensorPick(); app.SettingsChanged(); });
            AddBtn("GPU: automatic", S96(422), () => { app.Settings.GpuTempSensor = new SensorPick(); app.SettingsChanged(); });

            if (IsHandleCreated) Ui.DarkTitleBar(this); // the DPI check above already created the window
            app.CatalogWanted = true;
            timer.Tick += (s, e) => Reload();
            timer.Start();
            FormClosed += (s, e) => { app.CatalogWanted = false; timer.Dispose(); };
            Reload();
        }

        void AddBtn(string text, int x, Action a)
        {
            var b = new RoundButton { Text = text, ForeColor = Theme.Text, BackColor = Theme.Card, Bounds = new Rectangle(x, S96(420), S96(128), S96(30)) };
            b.Click += (s, e) => { try { a(); Reload(); } catch (Exception ex) { Log.Error("Picker", ex); } };
            Controls.Add(b);
        }

        void Pick(bool cpu)
        {
            var x = list.SelectedItem as SensorInfo;
            if (x == null) return;
            var p = new SensorPick { Identifier = x.Identifier, Hardware = x.Hardware, Name = x.Name };
            if (cpu) app.Settings.CpuTempSensor = p; else app.Settings.GpuTempSensor = p;
            app.SettingsChanged();
        }

        void Reload()
        {
            var cat = app.Sensors.LastCatalog;
            string selId = list.SelectedItem is SensorInfo ? ((SensorInfo)list.SelectedItem).Identifier : null;
            list.BeginUpdate();
            list.Items.Clear();
            if (cat != null) foreach (var x in cat) list.Items.Add(x);
            if (selId != null) for (int i = 0; i < list.Items.Count; i++) if (((SensorInfo)list.Items[i]).Identifier == selId) list.SelectedIndex = i;
            list.EndUpdate();
            if (cat == null) current.Text = "Reading sensors…";
            else
                current.Text = "CPU temp: " + (app.Settings.CpuTempSensor.IsAuto ? "automatic (" + app.Sensors.CpuTempSource + ")" : app.Settings.CpuTempSensor.Hardware + " · " + app.Settings.CpuTempSensor.Name) +
                               "\nGPU temp: " + (app.Settings.GpuTempSensor.IsAuto ? "automatic (" + app.Sensors.GpuTempSource + ")" : app.Settings.GpuTempSensor.Hardware + " · " + app.Settings.GpuTempSensor.Name);
        }
    }
}
