// The header that floats above the overlay: settings, lock, hide, and the four presets.
//
// It sits *outside* the overlay's bounds rather than on top of it. Overlapping would put
// buttons exactly where a missed raid-frame click lands, which is the failure this whole
// design is avoiding.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Glass
{
    public sealed class HeaderBar : LayeredForm
    {
        enum Hit { None, Gear, Lock, Hide, Dots, Preset0, Preset1, Preset2, Preset3 }

        // Layout in 96-dpi units, the same numbers as the Mac header's points.
        const int BarHeight = 30, CompactWidth = 34;

        readonly List<(Hit hit, Rectangle rect)> buttons = new List<(Hit, Rectangle)>();
        readonly ToolTip tip = new ToolTip { ShowAlways = true };
        readonly Timer fade = new Timer { Interval = 15 };
        readonly Timer tipDelay = new Timer { Interval = 600 };

        LayeredSurface surface;
        float s = 1;
        bool compact = true;
        bool shown;
        byte alpha;
        byte fadeFrom, fadeTo;
        DateTime fadeStart;
        double fadeMs;
        Hit hover = Hit.None, pressed = Hit.None;

        /// In auto mode the pointer only ever summons the dots; the buttons appear when you
        /// deliberately click them. Nothing actionable is ever a stray hover away.
        public bool Expanded => !compact;

        public HeaderBar()
        {
            Size = new Size(CompactWidth, BarHeight);
            fade.Tick += (o, e) => StepFade();
            tipDelay.Tick += (o, e) => { tipDelay.Stop(); ShowTip(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { surface?.Dispose(); fade.Dispose(); tipDelay.Dispose(); tip.Dispose(); }
            base.Dispose(disposing);
        }

        int Px(float v) => (int)Math.Round(v * s);
        int FullWidth => 255;

        // MARK: - Layout

        void Layout_()
        {
            buttons.Clear();
            if (compact)
            {
                buttons.Add((Hit.Dots, new Rectangle(Px(8), Px(6), Px(18), Px(18))));
                return;
            }
            buttons.Add((Hit.Gear, new Rectangle(Px(8), Px(6), Px(18), Px(18))));
            buttons.Add((Hit.Lock, new Rectangle(Px(32), Px(6), Px(18), Px(18))));
            buttons.Add((Hit.Hide, new Rectangle(Px(56), Px(6), Px(18), Px(18))));
            for (int i = 0; i < 4; i++)
                buttons.Add((Hit.Preset0 + i, new Rectangle(Px(91 + 40 * i), Px(6), Px(38), Px(18))));
        }

        Size WantedSize => new Size(Px(compact ? CompactWidth : FullWidth), Px(BarHeight));

        void EnsureSurface()
        {
            var want = WantedSize;
            if (surface != null && surface.Width == want.Width && surface.Height == want.Height) return;
            surface?.Dispose();
            surface = new LayeredSurface(want.Width, want.Height);
            Size = want;
        }

        // MARK: - Drawing

        Font iconFont, textFont, boldFont, chevronFont;

        void Fonts()
        {
            iconFont?.Dispose(); textFont?.Dispose(); boldFont?.Dispose(); chevronFont?.Dispose();
            iconFont = new Font(Look.IconFamily, 12.5f * s, GraphicsUnit.Pixel);
            textFont = new Font("Segoe UI Semibold", 11 * s, GraphicsUnit.Pixel);
            boldFont = new Font("Segoe UI", 11 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            chevronFont = new Font("Segoe UI Semibold", 16 * s, GraphicsUnit.Pixel);
        }

        static string Glyph(Hit h)
        {
            bool mdl2 = Look.HasIconFont;
            switch (h)
            {
                case Hit.Gear: return mdl2 ? "" : "⚙";
                case Hit.Lock: return Saved.Locked ? (mdl2 ? "" : "L") : (mdl2 ? "" : "U");
                case Hit.Dots: return mdl2 ? "" : "…";
                default: return "";
            }
        }

        void Render()
        {
            if (IsDisposed) return;
            if (iconFont == null) Fonts();
            EnsureSurface();
            Layout_();

            using (var g = surface.Draw())
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;

                var pill = new RectangleF(0.5f, 0.5f, surface.Width - 1, surface.Height - 1);
                using (var path = Look.RoundRect(pill, pill.Height / 2))
                {
                    using (var bg = new SolidBrush(Color.FromArgb(184, 0, 0, 0)))       // 72% black
                        g.FillPath(bg, path);
                    using (var edge = new Pen(Color.FromArgb(31, 255, 255, 255), 1))    // 12% white
                        g.DrawPath(edge, path);
                }

                var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                foreach (var (hit, rect) in buttons)
                {
                    if (hit == hover)
                    {
                        using (var hl = Look.RoundRect(Rectangle.Inflate(rect, Px(2), Px(1)), Px(4)))
                        using (var b = new SolidBrush(Color.FromArgb(36, 255, 255, 255)))
                            g.FillPath(b, hl);
                    }

                    if (hit >= Hit.Preset0)
                    {
                        string name = Saved.PresetNames[hit - Hit.Preset0];
                        bool isSet = Saved.Preset(name).HasValue;
                        bool active = Saved.ActivePreset == name;
                        var colour = active ? Look.Green : (isSet ? Color.White : Color.FromArgb(77, 255, 255, 255));
                        using (var brush = new SolidBrush(colour))
                            g.DrawString(name, active ? boldFont : textFont, brush, rect, center);
                    }
                    else if (hit == Hit.Hide)
                    {
                        g.DrawString("«", chevronFont, Brushes.White, new RectangleF(rect.X, rect.Y - Px(2), rect.Width, rect.Height), center);
                    }
                    else
                    {
                        var colour = hit == Hit.Lock && !Saved.Locked ? Look.Orange : Color.White;
                        using (var brush = new SolidBrush(colour))
                            g.DrawString(Glyph(hit), iconFont, brush, rect, center);
                    }
                }

                if (!compact)
                {
                    // The rule between the controls and the presets.
                    using (var rule = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
                        g.FillRectangle(rule, Px(82), Px(8), Math.Max(1, Px(1)), Px(14));
                }
            }
            if (IsHandleCreated) surface.Push(Handle, Location, alpha);
        }

        // MARK: - Public

        /// Swap between the full header and the dots that summon it.
        public void SetCompact(bool isCompact)
        {
            if (compact == isCompact && surface != null) return;
            compact = isCompact;
            hover = Hit.None;
            Render();
        }

        /// Update the lock icon and preset colours.
        public void RefreshBar() => Render();

        /// Sit just above the overlay, left-aligned with it. If that would be off the top of the
        /// monitor, sit just below it instead, so the header is never out of reach.
        public void Place(Rectangle overlay)
        {
            var d = Screens.For(overlay) ?? Screens.Primary();
            float scale = d?.Scale ?? 1f;
            if (Math.Abs(scale - s) > 0.001f) { s = scale; Fonts(); surface?.Dispose(); surface = null; }
            EnsureSurface();
            int y = overlay.Top - Px(6) - Height;
            if (d != null && y < d.Bounds.Top) y = overlay.Bottom + Px(6);
            Location = new Point(overlay.Left, y);
            Render();
        }

        public void SetVisible(bool visible)
        {
            if (visible == shown) return;
            shown = visible;
            if (visible && !Visible) { Show(); KeepOnTop(); }
            fadeFrom = alpha;
            fadeTo = visible ? (byte)255 : (byte)0;
            fadeMs = visible ? 120 : 250;           // appear briskly, linger on the way out
            fadeStart = DateTime.UtcNow;
            fade.Start();
            if (!visible) { tip.Hide(this); tipDelay.Stop(); }
        }

        void StepFade()
        {
            double t = Math.Min(1, (DateTime.UtcNow - fadeStart).TotalMilliseconds / fadeMs);
            double eased = 1 - (1 - t) * (1 - t);                  // ease-out
            alpha = (byte)Math.Round(fadeFrom + (fadeTo - fadeFrom) * eased);
            if (IsHandleCreated && surface != null) surface.Push(Handle, Location, alpha);
            if (t >= 1)
            {
                fade.Stop();
                if (!shown && Visible) Hide();
            }
        }

        // MARK: - Mouse

        Hit HitAt(Point p)
        {
            foreach (var (hit, rect) in buttons)
                if (Rectangle.Inflate(rect, Px(2), Px(3)).Contains(p)) return hit;
            return Hit.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitAt(e.Location);
            if (h == hover) return;
            hover = h;
            Render();
            tip.Hide(this);
            tipDelay.Stop();
            if (h != Hit.None) tipDelay.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover == Hit.None) return;
            hover = Hit.None;
            Render();
            tip.Hide(this);
            tipDelay.Stop();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) pressed = HitAt(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var h = HitAt(e.Location);
            var p = pressed;
            pressed = Hit.None;
            if (h == Hit.None || h != p) return;
            tip.Hide(this);
            // Defer: some of these rebuild or hide this very window.
            App.Defer(() => Act(h));
        }

        static void Act(Hit h)
        {
            switch (h)
            {
                case Hit.Gear: App.ShowSettings(null); break;
                case Hit.Lock: App.ToggleLock(); break;
                case Hit.Hide: App.Overlay?.CollapseHeader(); break;
                case Hit.Dots: App.Overlay?.ExpandHeader(); break;
                default:
                    string name = Saved.PresetNames[h - Hit.Preset0];
                    // Alt-click re-records; a plain click on an empty preset records it too. Alt
                    // is read from the physical keyboard: it was pressed in the game's window,
                    // so this thread's own keyboard state never saw it.
                    bool alt = (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0;
                    if (alt || !Saved.Preset(name).HasValue) App.RecordPreset(name); else App.UsePreset(name);
                    break;
            }
        }

        string TipFor(Hit h)
        {
            switch (h)
            {
                case Hit.Gear: return "Settings";
                case Hit.Lock: return "Lock or unlock the overlay";
                case Hit.Hide: return "Collapse back to dots";
                case Hit.Dots: return "Show Glass controls";
                case Hit.None: return null;
                default:
                    string name = Saved.PresetNames[h - Hit.Preset0];
                    return Saved.Preset(name).HasValue
                        ? "Switch to " + name + " · Alt-click to re-record"
                        : "Record a region for " + name;
            }
        }

        void ShowTip()
        {
            var text = TipFor(hover);
            if (text == null || !Visible) return;
            var r = buttons.Find(b => b.hit == hover).rect;
            tip.Show(text, this, r.Left, Height + Px(4), 4000);
        }
    }
}
