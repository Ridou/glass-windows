// The themed controls. Each one is the Windows half of a class in the macOS build's glass.swift:
// a red panel button, an On/Off toggle, a gold-tick checkbox, a plate-style picker, a slider
// with a gold thumb, the icon tabs down the side, and a dark blue tooltip.
//
// They all paint their own backdrop from the shared marble, anchored to their position in the
// form, so neighbouring controls sit on one continuous stone instead of each starting it again.
// WinForms' "transparent" background would otherwise copy the parent's BackColor, which is flat.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Glass
{
    /// A window that paints its own frame. The self-test asks for it explicitly, since it
    /// composites children by hand rather than letting Windows paint the window.
    public interface IChrome
    {
        void PaintChrome(Graphics g);
    }

    /// Shared painting for every themed control.
    public static class Backdrop
    {
        /// Where this control sits inside its top-level form, so the texture lines up.
        public static Point Origin(Control c)
        {
            int x = 0, y = 0;
            for (var w = c; w != null && !(w is Form); w = w.Parent) { x += w.Left; y += w.Top; }
            return new Point(x, y);
        }

        public static void Paint(Control c, Graphics g, Bitmap tex = null) =>
            Theme.Fill(g, tex ?? Theme.Marble, c.ClientRectangle, Origin(c));
    }

    public class ThemeLabel : Label
    {
        public Font TextFont = Theme.F(10);
        public Color Tint = Theme.Body;
        public bool Wrap;
        public StringAlignment Align = StringAlignment.Near;
        /// Set where an ellipsis is the intended outcome -- the command column, whose full
        /// text is in the tooltip -- so the layout check does not report it as a fault.
        public bool MayTruncate;

        /// What the text actually needs in the box it has, measured with the font it is drawn
        /// in and wrapped the way it is drawn. Empty when it fits.
        public string Overflow()
        {
            if (MayTruncate || string.IsNullOrEmpty(Text) || Width <= 0) return "";
            using (var g = CreateGraphics())
            {
                var f = Wrap ? Theme.Prose(TextFont.Size - 0.5f) : TextFont;
                var need = Theme.Measure(g, Text, f, Wrap ? Width : int.MaxValue, Wrap);
                if (Wrap) return need.Height > Height + 1 ? "CLIPPED needs " + (int)Math.Ceiling(need.Height) + "px tall" : "";
                return need.Width > Width + 1 ? "CLIPPED needs " + (int)Math.Ceiling(need.Width) + "px wide" : "";
            }
        }

        public ThemeLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Backdrop.Paint(this, e.Graphics);
            Theme.DrawText(e.Graphics, Text, Wrap ? Theme.Prose(TextFont.Size - 0.5f) : TextFont,
                           Enabled ? Tint : Theme.Grey, ClientRectangle,
                           Align, Wrap ? StringAlignment.Near : StringAlignment.Center, Wrap);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    }

    /// A red lacquered panel button with a bronze rim and gold lettering; grey for secondary
    /// actions. As a toggle it stays grey and says "On" in green.
    public class ThemeButton : Button
    {
        public enum Kind { Red, Grey }
        public Kind Style = Kind.Red;
        public bool IsToggle;
        public Font TextFont = Theme.F(10);
        public Color? Tint;
        bool hover, down;

        public ThemeButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        /// Toggles show their state by colour; `on` is set by the form, not by the click.
        public bool On;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop.Paint(this, g);

            var r = RectangleF.Inflate(ClientRectangle, -1, -1);
            using (var p = Theme.Round(r, 3))
            {
                Color[] fill;
                if (!Enabled) fill = new[] { Theme.Rgb(0.28, 0.27, 0.26), Theme.Rgb(0.14, 0.135, 0.13) };
                else if (Style == Kind.Red && !IsToggle)
                    fill = down ? new[] { Theme.Rgb(0.35, 0.03, 0.02), Theme.Rgb(0.55, 0.07, 0.04) }
                                : new[] { Theme.Rgb(0.72, 0.12, 0.07), Theme.Rgb(0.45, 0.04, 0.02), Theme.Rgb(0.28, 0.02, 0.01) };
                else
                    fill = down ? new[] { Theme.Rgb(0.10, 0.10, 0.10), Theme.Rgb(0.22, 0.21, 0.20) }
                                : new[] { Theme.Rgb(0.36, 0.35, 0.33), Theme.Rgb(0.20, 0.195, 0.19), Theme.Rgb(0.11, 0.105, 0.10) };
                Theme.FillDown(g, p, fill);

                if (hover && Enabled && !down)
                    using (var b = new SolidBrush(Color.FromArgb(36, 255, 217, 128))) g.FillPath(b, p);

                var state = g.Save();
                g.SetClip(p);
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height / 2 + 1),
                                                       Color.FromArgb(41, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(b, new RectangleF(r.X, r.Y, r.Width, r.Height / 2));
                g.Restore(state);

                using (var pen = new Pen(Color.Black))
                using (var q = Theme.Round(RectangleF.Inflate(ClientRectangle, -0.5f, -0.5f), 3.5f)) g.DrawPath(pen, q);
                using (var pen = new Pen(On ? Theme.Lit : Enabled ? Theme.Bronze : Theme.Rgb(0.36, 0.34, 0.30)))
                using (var q = Theme.Round(RectangleF.Inflate(r, -0.5f, -0.5f), 2.5f)) g.DrawPath(pen, q);
            }

            var colour = !Enabled ? Theme.Grey
                       : IsToggle ? (On ? Theme.Green : Color.FromArgb(191, Theme.Body))
                       : Tint ?? (hover ? Theme.White : Theme.Gold);
            var box = ClientRectangle;
            if (down) box.Offset(1, 1);
            Theme.DrawText(g, Text, TextFont, colour, box, StringAlignment.Center);
        }
    }

    /// The On/Off button that stands in for a checkbox: grey, and green when it is on. A
    /// checkbox that changes something you cannot see does not feel like it did anything.
    public class ThemeToggle : CheckBox
    {
        public Font TextFont = Theme.F(10);
        public bool On;
        bool hover, down;

        public ThemeToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Appearance = Appearance.Button;
            TextAlign = ContentAlignment.MiddleCenter;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnCheckedChanged(EventArgs e) { On = Checked; Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop.Paint(this, g);

            var r = RectangleF.Inflate(ClientRectangle, -1, -1);
            using (var p = Theme.Round(r, 3))
            {
                Theme.FillDown(g, p, !Enabled
                    ? new[] { Theme.Rgb(0.28, 0.27, 0.26), Theme.Rgb(0.14, 0.135, 0.13) }
                    : down ? new[] { Theme.Rgb(0.10, 0.10, 0.10), Theme.Rgb(0.22, 0.21, 0.20) }
                    : new[] { Theme.Rgb(0.36, 0.35, 0.33), Theme.Rgb(0.20, 0.195, 0.19), Theme.Rgb(0.11, 0.105, 0.10) });
                if (hover && Enabled && !down)
                    using (var b = new SolidBrush(Color.FromArgb(36, 255, 217, 128))) g.FillPath(b, p);
                using (var pen = new Pen(Color.Black))
                using (var q = Theme.Round(RectangleF.Inflate(ClientRectangle, -0.5f, -0.5f), 3.5f)) g.DrawPath(pen, q);
                using (var pen = new Pen(On ? Theme.Lit : Enabled ? Theme.Bronze : Theme.Rgb(0.36, 0.34, 0.30)))
                using (var q = Theme.Round(RectangleF.Inflate(r, -0.5f, -0.5f), 2.5f)) g.DrawPath(pen, q);
            }
            var box = ClientRectangle;
            if (down) box.Offset(1, 1);
            Theme.DrawText(g, Text, TextFont,
                           !Enabled ? Theme.Grey : On ? Theme.Green : Color.FromArgb(191, Theme.Body),
                           box, StringAlignment.Center);
        }
    }

    /// A dark square with an oversized gold tick that spills over its edge, and a blue glow
    /// under the pointer.
    public class ThemeCheck : CheckBox
    {
        public Font TextFont = Theme.F(10);
        bool hover;

        public ThemeCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop.Paint(this, g);

            var box = new RectangleF(2, Height / 2f - 8, 16, 16);
            using (var p = Theme.Round(box, 2))
            {
                Theme.FillDown(g, p, Theme.Rgb(0.16, 0.15, 0.14), Theme.Rgb(0.02, 0.02, 0.02));
                using (var pen = new Pen(Color.Black))
                using (var q = Theme.Round(RectangleF.Inflate(box, 1, 1), 3)) g.DrawPath(pen, q);
                using (var pen = new Pen(hover ? Theme.Rgb(0.45, 0.65, 1) : Theme.Edge)) g.DrawPath(pen, p);
            }

            if (Checked)
            {
                var tick = new[]
                {
                    new PointF(box.Left + 2, box.Y + box.Height / 2),
                    new PointF(box.Left + 7, box.Bottom - 2),
                    new PointF(box.Right + 3, box.Top - 3),
                };
                using (var pen = new Pen(Color.Black, 5) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, tick);
                using (var pen = new Pen(Theme.Gold, 3) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, tick);
            }

            Theme.DrawText(g, Text, TextFont, Enabled ? Theme.White : Theme.Grey,
                           new RectangleF(24, 0, Width - 24, Height));
        }
    }

    /// One plate in a row of them: the picker that replaces a segmented control. Set up as
    /// radio buttons so the form's existing grouping logic still works.
    public class ThemePlate : RadioButton
    {
        public Font TextFont = Theme.F(10);
        bool hover;

        public ThemePlate()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Appearance = Appearance.Button;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop.Paint(this, g);

            var r = RectangleF.Inflate(ClientRectangle, -1.5f, -1.5f);
            using (var p = Theme.Round(r, 3))
            {
                Theme.FillDown(g, p, Checked
                    ? new[] { Theme.Rgb(0.36, 0.27, 0.10), Theme.Rgb(0.16, 0.12, 0.05) }
                    : new[] { Theme.Rgb(0.17, 0.16, 0.15), Theme.Rgb(0.07, 0.065, 0.06) });
                if (Checked)
                {
                    var state = g.Save();
                    g.SetClip(p);
                    using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                                                           Color.FromArgb(0, 255, 204, 77), Color.FromArgb(90, 255, 204, 77), 90f))
                        g.FillRectangle(b, r);
                    g.Restore(state);
                }
                else if (hover)
                    using (var b = new SolidBrush(Color.FromArgb(20, 255, 230, 153))) g.FillPath(b, p);

                using (var pen = new Pen(Color.Black))
                using (var q = Theme.Round(RectangleF.Inflate(ClientRectangle, -0.5f, -0.5f), 4)) g.DrawPath(pen, q);
                using (var pen = new Pen(Checked ? Theme.Lit : Theme.Edge)) g.DrawPath(pen, p);
            }
            Theme.DrawText(g, Text, TextFont,
                           Checked ? Theme.Gold : hover ? Theme.White : Color.FromArgb(179, Theme.Body),
                           ClientRectangle, StringAlignment.Center);
        }
    }

    /// A dark groove with a bronze rim and a gold thumb. TrackBar cannot be owner-drawn, so
    /// this is a plain control that reproduces the part of it Glass uses.
    public class ThemeSlider : Control
    {
        public int Minimum = 0, Maximum = 100;
        int val;
        public event EventHandler ValueChanged;
        bool dragging;

        public int Value
        {
            get => val;
            set
            {
                var v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == val) return;
                val = v;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ThemeSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 26;
        }

        float Track => Math.Max(1, Width - 16);
        float ThumbX => 8 + Track * (Maximum == Minimum ? 0 : (val - Minimum) / (float)(Maximum - Minimum));

        void Seek(int x) => Value = Minimum + (int)Math.Round((x - 8) / Track * (Maximum - Minimum));

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { dragging = true; Seek(e.X); Focus(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) Seek(e.X);
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }

        protected override bool IsInputKey(Keys key) =>
            key == Keys.Left || key == Keys.Right || base.IsInputKey(key);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = Math.Max(1, (Maximum - Minimum) / 50);
            if (e.KeyCode == Keys.Left) { Value -= step; e.Handled = true; }
            if (e.KeyCode == Keys.Right) { Value += step; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop.Paint(this, g);

            var bar = new RectangleF(8, Height / 2f - 3.5f, Track, 7);
            using (var p = Theme.Round(bar, 3.5f))
            {
                Theme.FillDown(g, p, Theme.Rgb(0.02, 0.02, 0.02), Theme.Rgb(0.13, 0.12, 0.11));
                // The filled part, so the value reads at a glance.
                var fill = new RectangleF(bar.X + 2, bar.Y + 2, Math.Max(0, ThumbX - bar.X - 2), bar.Height - 4);
                if (fill.Width > 0)
                    using (var b = new SolidBrush(Color.FromArgb(204, 140, 102, 31)))
                    using (var q = Theme.Round(fill, 1.5f)) g.FillPath(b, q);
                using (var pen = new Pen(Color.Black))
                using (var q = Theme.Round(RectangleF.Inflate(bar, 1, 1), 4.5f)) g.DrawPath(pen, q);
                using (var pen = new Pen(Theme.Edge)) g.DrawPath(pen, p);
            }

            var knob = new RectangleF(ThumbX - 5.5f, Height / 2f - 9, 11, 18);
            using (var p = Theme.Round(knob, 2))
            {
                Theme.FillDown(g, p, Theme.GoldMetal);
                using (var pen = new Pen(Color.Black)) g.DrawPath(pen, p);
                using (var pen = new Pen(Color.FromArgb(115, 0, 0, 0)))
                    g.DrawLine(pen, knob.X + knob.Width / 2, knob.Y + 4, knob.X + knob.Width / 2, knob.Bottom - 4);
            }
        }
    }

    /// A square icon tab hanging off the side of the frame, lit gold with a bar down its left
    /// edge when selected.
    public class ThemeSideTab : Control
    {
        public MacroIcon Icon;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }
        bool selected, hover;

        public ThemeSideTab()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(48, 48);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop.Paint(this, g, Theme.Stone);

            var r = RectangleF.Inflate(ClientRectangle, -1, -1);
            using (var p = Theme.Round(r, 4))
            using (var b = new SolidBrush(Theme.Rgb(0.16, 0.15, 0.13))) g.FillPath(b, p);

            ThemeArt.DrawIcon(g, Icon, RectangleF.Inflate(ClientRectangle, -6, -6), !selected && !hover);
            if (hover && !selected)
                using (var b = new SolidBrush(Color.FromArgb(30, 255, 255, 255)))
                    g.FillRectangle(b, RectangleF.Inflate(ClientRectangle, -6, -6));

            using (var pen = new Pen(Color.Black))
            using (var p = Theme.Round(RectangleF.Inflate(ClientRectangle, -0.5f, -0.5f), 4.5f)) g.DrawPath(pen, p);
            using (var pen = new Pen(selected ? Theme.Lit : Theme.Edge, 2))
            using (var p = Theme.Round(RectangleF.Inflate(r, -1, -1), 3)) g.DrawPath(pen, p);

            if (selected)
                using (var b = new SolidBrush(Theme.Lit))
                    g.FillRectangle(b, 2, Height / 2f - 19, 3, 38);
        }
    }

    /// The red close button in the frame's corner.
    public class ThemeClose : Control
    {
        bool hover, down;

        public ThemeClose()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(20, 20);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop.Paint(this, g, Theme.Stone);

            var r = RectangleF.Inflate(ClientRectangle, -1, -1);
            using (var p = Theme.Round(r, 3))
            {
                Theme.FillDown(g, p, down
                    ? new[] { Theme.Rgb(0.35, 0.03, 0.02), Theme.Rgb(0.55, 0.06, 0.03) }
                    : new[] { Theme.Rgb(0.85, 0.16, 0.08), Theme.Rgb(0.50, 0.04, 0.02), Theme.Rgb(0.30, 0.02, 0.01) });
                using (var pen = new Pen(Color.Black))
                using (var q = Theme.Round(RectangleF.Inflate(ClientRectangle, -0.5f, -0.5f), 3.5f)) g.DrawPath(pen, q);
                using (var ring = new GraphicsPath())
                {
                    ring.AddPath(p, false);
                    using (var inner = Theme.Round(RectangleF.Inflate(r, -1.5f, -1.5f), 2)) ring.AddPath(inner, false);
                    ring.FillMode = FillMode.Alternate;
                    Theme.FillDown(g, ring, Theme.GoldMetal);
                }
            }
            var c = RectangleF.Inflate(r, -r.Width * 0.3f, -r.Height * 0.3f);
            using (var pen = new Pen(Color.FromArgb(153, 0, 0, 0), 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, c.Left, c.Top, c.Right, c.Bottom);
                g.DrawLine(pen, c.Left, c.Bottom, c.Right, c.Top);
            }
            using (var pen = new Pen(hover ? Theme.White : Theme.Rgb(1, 0.85, 0.75), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, c.Left, c.Top, c.Right, c.Bottom);
                g.DrawLine(pen, c.Left, c.Bottom, c.Right, c.Top);
            }
        }
    }

    /// A band behind a list row, alternating the way a game's lists do.
    public class ThemeBand : Control
    {
        public ThemeBand()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Enabled = false;                                  // never takes a click
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Backdrop.Paint(this, g);
            var c = Color.FromArgb(128, Theme.Row);
            using (var b = new LinearGradientBrush(new RectangleF(-1, 0, Width + 2, Math.Max(1, Height)),
                                                   Color.Transparent, Color.Transparent, 0f))
            {
                b.InterpolationColors = new ColorBlend(4)
                {
                    Colors = new[] { Color.FromArgb(0, Theme.Row), c, c, Color.FromArgb(0, Theme.Row) },
                    Positions = new[] { 0f, 0.08f, 0.92f, 1f },
                };
                g.FillRectangle(b, ClientRectangle);
            }
        }
    }

    public class ThemeRule : Control
    {
        public ThemeRule()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Enabled = false;
            Height = 3;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Backdrop.Paint(this, e.Graphics);
            Theme.DrawRule(e.Graphics, ClientRectangle);
        }
    }

    /// A panel that paints the marble rather than a flat colour.
    public class ThemePanel : Panel
    {
        public Bitmap Texture;

        public ThemePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            Backdrop.Paint(this, e.Graphics, Texture);
    }

    /// Layout containers that paint the stone themselves. WinForms' transparent background
    /// paints the *parent's* background into the child without shifting the texture with it,
    /// which shows up as a lighter rectangle behind every control; painting it here instead
    /// keeps one continuous surface.
    public class ThemeTable : TableLayoutPanel
    {
        public ThemeTable()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) => Backdrop.Paint(this, e.Graphics);
    }

    public class ThemeFlow : FlowLayoutPanel
    {
        public ThemeFlow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) => Backdrop.Paint(this, e.Graphics);
    }

    /// The dark blue tooltip: a silver border, a white title and gold body. WinForms' own
    /// ToolTip is drawn by the system, so this is a borderless window that follows the pointer.
    public sealed class ThemeTip : IDisposable
    {
        readonly Dictionary<Control, string[]> tips = new Dictionary<Control, string[]>();
        readonly TipWindow window = new TipWindow();
        readonly Timer timer = new Timer { Interval = 350 };
        Control pending;

        public ThemeTip() => timer.Tick += (o, e) => { timer.Stop(); Show(pending); };

        /// The shape WinForms' own ToolTip has, so call sites that only have one string keep
        /// working: the first line becomes the title and the rest the body.
        public void SetToolTip(Control c, string text)
        {
            if (string.IsNullOrEmpty(text)) { tips.Remove(c); return; }
            int nl = text.IndexOf('\n');
            if (nl < 0) Set(c, text);
            else Set(c, text.Substring(0, nl), text.Substring(nl + 1));
        }

        public void Set(Control c, string title, string detail = "")
        {
            tips[c] = new[] { title, detail ?? "" };
            c.MouseEnter -= Enter; c.MouseEnter += Enter;
            c.MouseLeave -= Leave; c.MouseLeave += Leave;
        }

        void Enter(object sender, EventArgs e) { pending = sender as Control; timer.Stop(); timer.Start(); }
        void Leave(object sender, EventArgs e) { timer.Stop(); window.Hide(); }

        void Show(Control c)
        {
            if (c == null || !c.IsHandleCreated || !tips.TryGetValue(c, out var parts)) return;
            if (!c.ClientRectangle.Contains(c.PointToClient(Cursor.Position))) return;
            window.Show(parts[0], parts[1], Cursor.Position);
        }

        public void Hide() { timer.Stop(); window.Hide(); }
        public void Dispose() { timer.Dispose(); window.Dispose(); }

        sealed class TipWindow : Form
        {
            string title = "", detail = "";
            const int MaxWidth = 320;

            public TipWindow()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                         | ControlStyles.OptimizedDoubleBuffer, true);
            }

            // Never takes focus from the game or the settings window.
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var p = base.CreateParams; p.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */; return p; }
            }

            public void Show(string t, string d, Point at)
            {
                title = t; detail = d ?? "";
                using (var g = CreateGraphics())
                {
                    var ts = Theme.Measure(g, title, Theme.F(11), MaxWidth);
                    var ds = detail.Length == 0 ? SizeF.Empty
                           : Theme.Measure(g, detail, detail.StartsWith("/") ? Theme.Narrow(9.5f) : Theme.F(9.5f), MaxWidth);
                    Size = new Size((int)Math.Max(ts.Width, ds.Width) + 22,
                                    (int)(ts.Height + (detail.Length == 0 ? 0 : ds.Height + 5)) + 16);
                }
                var screen = Screen.FromPoint(at).WorkingArea;
                int x = Math.Min(at.X + 16, screen.Right - Width - 4);
                int y = at.Y + 20 + Height > screen.Bottom ? at.Y - Height - 12 : at.Y + 20;
                Location = new Point(x, y);
                if (!Visible) Show();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = RectangleF.Inflate(ClientRectangle, -1.5f, -1.5f);
                using (var p = Theme.Round(r, 4))
                {
                    using (var b = new SolidBrush(Color.FromArgb(243, 5, 5, 18))) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.Rgb(0.62, 0.62, 0.64), 2)) g.DrawPath(pen, p);
                }
                using (var pen = new Pen(Color.Black))
                using (var p = Theme.Round(RectangleF.Inflate(ClientRectangle, -0.5f, -0.5f), 5)) g.DrawPath(pen, p);

                using (var gg = CreateGraphics())
                {
                    var ts = Theme.Measure(gg, title, Theme.F(11), MaxWidth);
                    Theme.DrawText(g, title, Theme.F(11), Theme.White,
                                   new RectangleF(11, 8, MaxWidth, ts.Height), StringAlignment.Near,
                                   StringAlignment.Near, true);
                    if (detail.Length > 0)
                    {
                        var f = detail.StartsWith("/") ? Theme.Narrow(9.5f) : Theme.F(9.5f);
                        var ds = Theme.Measure(gg, detail, f, MaxWidth);
                        Theme.DrawText(g, detail, f, detail.StartsWith("/") ? Theme.Body : Theme.Gold,
                                       new RectangleF(11, 8 + ts.Height + 5, MaxWidth, ds.Height),
                                       StringAlignment.Near, StringAlignment.Near, true);
                    }
                }
            }
        }
    }
}
