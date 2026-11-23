using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Fizz
{
    /// <summary>One label with its values, e.g. "GPU  64°C  57%".</summary>
    internal sealed class OverlayGroup
    {
        public string Label = "";
        public readonly List<string> Texts = new List<string>(4);
        public readonly List<string> Templates = new List<string>(4);
        public readonly List<Tone> Tones = new List<Tone>(4);
        public void Clear() { Label = ""; Texts.Clear(); Templates.Clear(); Tones.Clear(); }
    }

    /// <summary>Everything the overlay needs to draw one frame.</summary>
    internal sealed class OverlayContent
    {
        public readonly List<OverlayGroup> Groups = new List<OverlayGroup>();
        public int GroupCount;
        public bool Vertical, Background;
        public float FontPt;
        public int Opacity;
        public Color Accent;
        public string Corner;
        public int Margin;

        public OverlayGroup NextGroup()
        {
            if (GroupCount == Groups.Count) Groups.Add(new OverlayGroup());
            var g = Groups[GroupCount++];
            g.Clear();
            return g;
        }

        public bool SameAs(OverlayContent o)
        {
            if (o == null || o.GroupCount != GroupCount || o.Vertical != Vertical || o.Background != Background || o.FontPt != FontPt ||
                o.Opacity != Opacity || o.Accent != Accent || o.Corner != Corner || o.Margin != Margin) return false;
            for (int i = 0; i < GroupCount; i++)
            {
                OverlayGroup a = Groups[i], b = o.Groups[i];
                if (!ReferenceEquals(a.Label, b.Label) && a.Label != b.Label) return false;
                if (a.Texts.Count != b.Texts.Count) return false;
                for (int k = 0; k < a.Texts.Count; k++)
                    if (a.Tones[k] != b.Tones[k] || (!ReferenceEquals(a.Texts[k], b.Texts[k]) && a.Texts[k] != b.Texts[k])) return false;
            }
            return true;
        }

        public void CopyFrom(OverlayContent o)
        {
            GroupCount = 0;
            for (int i = 0; i < o.GroupCount; i++)
            {
                var g = NextGroup(); var s = o.Groups[i];
                g.Label = s.Label;
                g.Texts.AddRange(s.Texts); g.Templates.AddRange(s.Templates); g.Tones.AddRange(s.Tones);
            }
            Vertical = o.Vertical; Background = o.Background; FontPt = o.FontPt; Opacity = o.Opacity; Accent = o.Accent; Corner = o.Corner; Margin = o.Margin;
        }
    }

    /// <summary>
    /// Transparent, click-through, always-on-top, never-focused window drawn with UpdateLayeredWindow.
    /// GDI+ draws into one reusable DIB; it only redraws when the content actually changes.
    /// </summary>
    internal sealed class OverlayWindow : Form
    {
        IntPtr memDc, dib, oldBmp, bits;
        int dibW, dibH;
        Bitmap surface;
        Graphics g;
        Font valueFont, labelFont;
        float fontScaleKey = -1;
        readonly Dictionary<string, float> templateWidth = new Dictionary<string, float>();
        readonly OverlayContent last = new OverlayContent();
        bool hasLast, visible;
        Native.RECT lastAnchor;
        Size lastSize;
        uint lastDpi;
        DateTime lastTopmostUtc = DateTime.MinValue;
        static readonly StringFormat Typo = CreateTypo();

        static StringFormat CreateTypo()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            return f;
        }

        public OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Fizz overlay";
            Bounds = new Rectangle(-32000, -32000, 1, 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = Native.WS_POPUP;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Never let anything make it visible the WinForms way (that could steal focus from the game).
        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
        }

        public bool IsShown { get { return visible; } }

        public void HideOverlay()
        {
            if (!visible) return;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            visible = false;
        }

        /// <summary>Forget cached content (e.g. after display/DPI changes) so the next Render redraws and repositions.</summary>
        public void ResetCache(bool fonts)
        {
            hasLast = false;
            lastAnchor = new Native.RECT();
            if (fonts) { fontScaleKey = -1; lastDpi = 0; }
        }

        /// <param name="anchor">Area to pin the overlay into: the game's client area (windowed) or the monitor.</param>
        public void Render(OverlayContent c, IntPtr monitor, Native.RECT anchor)
        {
            if (!IsHandleCreated) CreateHandle();
            if (monitor == IntPtr.Zero) monitor = Native.MonitorFromPoint(new Native.POINT(0, 0), Native.MONITOR_DEFAULTTOPRIMARY);

            var mi = new Native.MONITORINFO { cbSize = 40 };
            Native.GetMonitorInfo(monitor, ref mi);
            if (anchor.Width <= 0 || anchor.Height <= 0) anchor = mi.rcMonitor;
            uint dpiX = 96, dpiY;
            try { if (Native.GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) != 0) dpiX = 96; } catch { dpiX = 96; }
            float scale = dpiX / 96f;

            bool dpiChanged = dpiX != lastDpi;
            bool contentChanged = !hasLast || dpiChanged || !c.SameAs(last);
            bool moved = !RectEq(anchor, lastAnchor);

            if (contentChanged)
            {
                EnsureFonts(c.FontPt * scale);
                lastSize = Draw(c, scale);
                last.CopyFrom(c);
                hasLast = true;
                lastDpi = dpiX;
            }
            if (contentChanged || moved)
            {
                Point pos = Place(c, anchor, lastSize, scale);
                Push(pos, lastSize, c.Opacity);
                lastAnchor = anchor;
            }

            if (!visible)
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                visible = true;
                lastTopmostUtc = DateTime.MinValue;
            }
            // Borderless games can end up above us; quietly re-assert topmost every couple of seconds.
            if ((DateTime.UtcNow - lastTopmostUtc).TotalSeconds >= 2)
            {
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
                lastTopmostUtc = DateTime.UtcNow;
            }
        }

        static bool RectEq(Native.RECT a, Native.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        static FontFamily overlayFamily;

        /// <summary>Bahnschrift SemiBold (ships with Windows 10/11): tabular digits, compact HUD look. Falls back to Segoe UI.</summary>
        static FontFamily OverlayFamily
        {
            get
            {
                if (overlayFamily != null) return overlayFamily;
                foreach (var name in new[] { "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold", "Segoe UI" })
                {
                    try { overlayFamily = new FontFamily(name); break; } catch (ArgumentException) { }
                }
                return overlayFamily ?? (overlayFamily = FontFamily.GenericSansSerif);
            }
        }

        float valueAscent, labelAscent;

        void EnsureFonts(float px)
        {
            float key = (float)Math.Round(px, 2);
            if (key == fontScaleKey && valueFont != null) return;
            if (valueFont != null) valueFont.Dispose();
            if (labelFont != null) labelFont.Dispose();
            float valuePx = px * 96f / 72f;
            var fam = OverlayFamily;
            valueFont = new Font(fam, valuePx, FontStyle.Regular, GraphicsUnit.Pixel);
            labelFont = new Font(fam, valuePx * 0.9f, FontStyle.Regular, GraphicsUnit.Pixel);
            float em = fam.GetEmHeight(FontStyle.Regular), asc = fam.GetCellAscent(FontStyle.Regular);
            valueAscent = valueFont.Size * asc / em;
            labelAscent = labelFont.Size * asc / em;
            templateWidth.Clear();
            fontScaleKey = key;
        }

        void EnsureSurface(int w, int h)
        {
            if (w <= dibW && h <= dibH && surface != null) return;
            FreeSurface();
            dibW = (w + 63) & ~63; dibH = (h + 31) & ~31;
            var bmi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = dibW, biHeight = -dibH, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            try
            {
                memDc = Native.CreateCompatibleDC(screen);
                dib = Native.CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0);
                oldBmp = Native.SelectObject(memDc, dib);
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
            surface = new Bitmap(dibW, dibH, dibW * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(surface);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        void FreeSurface()
        {
            if (g != null) { g.Dispose(); g = null; }
            if (surface != null) { surface.Dispose(); surface = null; }
            if (memDc != IntPtr.Zero)
            {
                Native.SelectObject(memDc, oldBmp);
                Native.DeleteDC(memDc); memDc = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
            dibW = dibH = 0;
        }

        float Measure(Font f, string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            using (var tmp = Graphics.FromHwnd(IntPtr.Zero))
            {
                tmp.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                return tmp.MeasureString(s, f, PointF.Empty, Typo).Width;
            }
        }

        float MeasureG(Font f, string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return g != null ? g.MeasureString(s, f, PointF.Empty, Typo).Width : Measure(f, s);
        }

        float ValueWidth(string text, string template)
        {
            float w = MeasureG(valueFont, text);
            if (!string.IsNullOrEmpty(template))
            {
                float tw;
                if (!templateWidth.TryGetValue(template, out tw)) { tw = MeasureG(valueFont, template); templateWidth[template] = tw; }
                if (tw > w) w = tw;
            }
            return w;
        }

        // reusable layout scratch
        readonly List<float> labelW = new List<float>();
        readonly List<float> valW = new List<float>();

        Size Draw(OverlayContent c, float scale)
        {
            if (g == null) EnsureSurface(64, 32);
            float pad = 8 * scale, padV = 4 * scale, gapLV = 6 * scale, gapVV = 9 * scale, gapG = 14 * scale, rowGap = 1 * scale;
            float lineH = valueFont.GetHeight(g) * 0.98f;
            float labelLift = valueAscent - labelAscent; // labels share the values' baseline

            // ---- measure ----
            labelW.Clear(); valW.Clear();
            float maxLabel = 0;
            for (int i = 0; i < c.GroupCount; i++)
            {
                var grp = c.Groups[i];
                float lw = MeasureG(labelFont, grp.Label);
                labelW.Add(lw);
                if (lw > maxLabel) maxLabel = lw;
                for (int k = 0; k < grp.Texts.Count; k++) valW.Add(ValueWidth(grp.Texts[k], grp.Templates[k]));
            }

            float w, h;
            if (c.Vertical)
            {
                float maxRow = 0; int vi = 0;
                for (int i = 0; i < c.GroupCount; i++)
                {
                    float row = 0; var grp = c.Groups[i];
                    for (int k = 0; k < grp.Texts.Count; k++) row += valW[vi++] + (k > 0 ? gapVV : 0);
                    if (row > maxRow) maxRow = row;
                }
                w = pad * 2 + (maxLabel > 0 ? maxLabel + gapLV * 1.5f : 0) + maxRow;
                h = padV * 2 + c.GroupCount * lineH + Math.Max(0, c.GroupCount - 1) * rowGap;
            }
            else
            {
                w = pad * 2; int vi = 0;
                for (int i = 0; i < c.GroupCount; i++)
                {
                    var grp = c.Groups[i];
                    if (i > 0) w += gapG;
                    if (labelW[i] > 0) w += labelW[i] + gapLV;
                    for (int k = 0; k < grp.Texts.Count; k++) w += valW[vi++] + (k > 0 ? gapVV : 0);
                }
                h = padV * 2 + lineH;
            }
            int iw = Math.Max(8, (int)Math.Ceiling(w)), ih = Math.Max(8, (int)Math.Ceiling(h));
            EnsureSurface(iw, ih);

            // ---- draw ----
            g.CompositingMode = CompositingMode.SourceCopy;
            g.FillRectangle(Brushes.Transparent, 0, 0, dibW, dibH);
            g.CompositingMode = CompositingMode.SourceOver;
            if (c.Background)
            {
                float radius = c.Vertical ? 8 * scale : ih / 2f;
                Color edgeColor = Color.FromArgb(60, c.Accent);
                float edgeW = Math.Max(1f, scale);
                if (edgePen == null || edgePen.Color != edgeColor || edgePen.Width != edgeW)
                {
                    if (edgePen != null) edgePen.Dispose();
                    edgePen = new Pen(edgeColor, edgeW);
                }
                using (var path = RoundRect(new RectangleF(0.5f, 0.5f, iw - 1, ih - 1), radius))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(edgePen, path);
                }
            }

            int v = 0;
            float x = pad, y = padV;
            for (int i = 0; i < c.GroupCount; i++)
            {
                var grp = c.Groups[i];
                if (c.Vertical) { x = pad; if (i > 0) y += lineH + rowGap; }
                else if (i > 0) x += gapG;

                if (!string.IsNullOrEmpty(grp.Label))
                {
                    DrawText(grp.Label, labelFont, c.Accent, x, y + labelLift, !c.Background);
                    x += (c.Vertical ? maxLabel + gapLV * 1.5f : labelW[i] + gapLV);
                }
                else if (c.Vertical && maxLabel > 0) x += maxLabel + gapLV * 1.5f;

                for (int k = 0; k < grp.Texts.Count; k++)
                {
                    if (k > 0) x += gapVV;
                    DrawText(grp.Texts[k], valueFont, ToneColor(grp.Tones[k]), x, y, !c.Background);
                    x += valW[v++];
                }
            }
            g.Flush();
            return new Size(iw, ih);
        }

        readonly SolidBrush brush = new SolidBrush(Color.White);
        readonly SolidBrush bgBrush = new SolidBrush(Color.FromArgb(200, 18, 19, 24));
        Pen edgePen;
        readonly SolidBrush shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0));

        void DrawText(string s, Font f, Color color, float x, float y, bool outline)
        {
            if (outline)
            {
                // no background: a soft dark halo keeps text readable over bright scenes
                g.DrawString(s, f, shadow, x + 1, y + 1, Typo);
                g.DrawString(s, f, shadow, x - 1, y, Typo);
                g.DrawString(s, f, shadow, x + 1, y, Typo);
                g.DrawString(s, f, shadow, x, y - 1, Typo);
            }
            brush.Color = color;
            g.DrawString(s, f, brush, x, y, Typo);
        }

        public static Color ToneColor(Tone t)
        {
            switch (t)
            {
                case Tone.Good: return Color.FromArgb(124, 227, 139);
                case Tone.Warn: return Color.FromArgb(255, 184, 0);
                case Tone.Bad: return Color.FromArgb(255, 82, 82);
                case Tone.Dim: return Color.FromArgb(150, 150, 158);
            }
            return Color.FromArgb(242, 242, 245);
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Point Place(OverlayContent c, Native.RECT anchor, Size size, float scale)
        {
            var r = PlacedRect(c.Corner, c.Margin, anchor, size, scale);
            return new Point(r.Left, r.Top);
        }

        /// <summary>Where an overlay of <paramref name="size"/> sits inside <paramref name="anchor"/> (screen pixels).</summary>
        public static Native.RECT PlacedRect(string corner, int margin, Native.RECT anchor, Size size, float scale)
        {
            int m = (int)Math.Round(margin * scale);
            bool right = corner == "TopRight" || corner == "BottomRight";
            bool bottom = corner == "BottomLeft" || corner == "BottomRight";
            int x = right ? anchor.Right - m - size.Width : anchor.Left + m;
            int y = bottom ? anchor.Bottom - m - size.Height : anchor.Top + m;
            return new Native.RECT { Left = x, Top = y, Right = x + size.Width, Bottom = y + size.Height };
        }

        /// <summary>
        /// Where the overlay sits (or would sit) when pinned to <paramref name="gameArea"/>, whatever it's showing right now.
        /// Before the first draw it's the whole game area.
        /// </summary>
        public Native.RECT BoundsPinnedTo(Native.RECT gameArea, string corner, int margin)
        {
            if (lastSize.Width <= 0 || lastSize.Height <= 0) return gameArea;
            return PlacedRect(corner, margin, gameArea, lastSize, lastDpi > 0 ? lastDpi / 96f : 1f);
        }

        void Push(Point pos, Size size, int opacityPct)
        {
            var dst = new Native.POINT(pos.X, pos.Y);
            var sz = new Native.SIZE(size.Width, size.Height);
            var src = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = (byte)Math.Max(20, Math.Min(255, opacityPct * 255 / 100)), AlphaFormat = Native.AC_SRC_ALPHA
            };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            try
            {
                if (!Native.UpdateLayeredWindow(Handle, screen, ref dst, ref sz, memDc, ref src, 0, ref blend, Native.ULW_ALPHA))
                    Log.Throttled("ulw", "UpdateLayeredWindow failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error(), 120);
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FreeSurface();
                if (valueFont != null) valueFont.Dispose();
                if (labelFont != null) labelFont.Dispose();
                brush.Dispose(); shadow.Dispose(); bgBrush.Dispose();
                if (edgePen != null) edgePen.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
