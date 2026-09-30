// The overlay: the mirrored region, always on top, clicking through to the real thing.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Glass
{
    public sealed class OverlayForm : Form
    {
        /// Global rect this mirror corresponds to.
        public Rectangle SourceRect { get; private set; }
        public HeaderBar Bar { get; private set; }

        double scale;
        byte alpha;
        bool locked;
        int wheelAccum;
        long wheelTick;
        bool pointerNear;
        int ticks;

        readonly Timer hoverPoll = new Timer { Interval = 30 };
        readonly Timer hideTimer = new Timer { Interval = 400 };
        readonly Timer collapseTimer = new Timer { Interval = 300 };

        /// The capture thread invalidates through this, never through the Form object.
        public static volatile IntPtr LiveHandle;

        public OverlayForm(Rectangle region, double scale, double opacity, Point? origin)
        {
            SourceRect = region;
            this.scale = scale;
            alpha = (byte)Math.Round(Math.Min(1, Math.Max(0.1, opacity)) * 255);
            locked = Saved.Locked;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            BackColor = Color.Black;
            Text = "Glass";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);

            var size = Scaled(region);
            var at = origin ?? new Point(100, 100);
            // A saved position from a different monitor layout could be nowhere at all; a
            // mirror you cannot see or reach is worse than one in the default place.
            if (!Screens.Reachable(new Rectangle(at, size)))
            {
                var p = Screens.Primary();
                at = p != null ? new Point(p.Bounds.X + 100, p.Bounds.Y + 100) : new Point(100, 100);
            }
            Bounds = new Rectangle(at, size);
            Cursor = locked ? Cursors.Hand : Cursors.SizeAll;

            Bar = new HeaderBar();
            hoverPoll.Tick += (o, e) => Poll();
            hideTimer.Tick += (o, e) => HideAfterGrace();
            collapseTimer.Tick += (o, e) => CollapseAfterGrace();
        }

        Size Scaled(Rectangle region) =>
            new Size(Math.Max(8, (int)Math.Round(region.Width * scale)), Math.Max(8, (int)Math.Round(region.Height * scale)));

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= Native.WS_POPUP;
                // Layered for opacity; no-activate so a click never takes focus from the game;
                // a tool window so it stays out of the taskbar and Alt+Tab.
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LiveHandle = Handle;
            Native.SetLayeredWindowAttributes(Handle, 0, alpha, Native.LWA_ALPHA);
            // Exclude ourselves, or mirroring a region the overlay covers would feed back.
            if (!Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE))
                Log.Write("could not exclude the overlay from capture; do not mirror a region it covers");
            ApplyShape();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            LiveHandle = IntPtr.Zero;
            base.OnHandleDestroyed(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bar.Place(Bounds);
            Bar.RefreshBar();
            ApplyHeaderMode();
            hoverPoll.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hoverPoll.Dispose(); hideTimer.Dispose(); collapseTimer.Dispose();
                Bar?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// Rounded corners, the same 6px as the Mac overlay.
        void ApplyShape()
        {
            if (!IsHandleCreated) return;
            int r = 12;
            Native.SetWindowRgn(Handle, Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, r, r), true);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyShape();
            Invalidate();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (!IsHandleCreated) return;
            Bar?.Place(Bounds);
            App.Publish();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            App.Publish();
        }

        // MARK: - Drawing

        /// The frame goes straight from the capture DIB to the window DC, scaled by GDI.
        /// Painting stays inside the WM_PAINT cycle WinForms owns, rather than fighting it --
        /// the same lesson as the Mac build's NSImageView.
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var hdc = g.GetHdc();
            bool drew;
            try { drew = App.Capture.DrawTo(hdc, ClientRectangle); }
            finally { g.ReleaseHdc(hdc); }
            if (!drew) g.Clear(Color.Black);

            // Locked: a faint green edge. Unlocked: a thicker orange one, so it is obvious that
            // a drag will move it and a click will not go through.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = locked ? 1 : 2;
            var edge = locked ? Color.FromArgb(115, Look.Green) : Look.Orange;     // 45% green
            using (var pen = new Pen(edge, w))
            using (var path = Look.RoundRect(new RectangleF(w / 2, w / 2, Width - w, Height - w), 6))
                g.DrawPath(pen, path);
        }

        // MARK: - Input

        /// Screen point over the overlay -> the matching point in the captured region.
        Point SourcePoint(Point global)
        {
            var state = new HookState { Overlay = Bounds, Source = SourceRect };
            return state.SourcePoint(global) ?? new Point(SourceRect.X + SourceRect.Width / 2, SourceRect.Y + SourceRect.Height / 2);
        }

        void Route(IntPtr lParam, MouseButtons button)
        {
            var global = new Point(Left + Native.LoWord(lParam), Top + Native.HiWord(lParam));
            var target = SourcePoint(global);
            if (Covers(target)) return;
            Forward.Click(target, button);
        }

        bool coverWarned;

        /// True if the point a click would go to is under Glass itself: the mirror placed over
        /// the region it mirrors. The click would land back on the mirror, so it is refused, and
        /// said once, since nothing else would explain the dead clicks.
        bool Covers(Point target)
        {
            bool covered = Bounds.Contains(target) || (Bar != null && Bar.Visible && Bar.Bounds.Contains(target));
            if (covered && !coverWarned)
            {
                coverWarned = true;
                App.Warn("The mirror covers what it mirrors",
                         "Glass cannot click through to a spot it is covering. Unlock the mirror (Ctrl+Alt+L by default) "
                         + "and drag it off the region it shows.");
            }
            return covered;
        }

        /// Input Glass synthesized itself. It only arrives here if something is wrong -- the
        /// mirror covering its own region -- and forwarding it would click forever.
        static bool Ours() => Native.GetMessageExtraInfo() == Forward.GlassTag;

        void Wheel(Message m)
        {
            // Wheels on Windows arrive in 120ths of a notch; precision wheels and touchpads send
            // small pieces. Gather them into whole notches, which is what a wheel binding expects.
            // One notch per event, however far it went -- the Mac build does the same, because
            // a bound wheel action wants one press, not a burst.
            int delta = Native.HiWord(m.WParam);
            long now = Environment.TickCount64;
            if (now - wheelTick > 400 || Math.Sign(delta) != Math.Sign(wheelAccum)) wheelAccum = 0;
            wheelTick = now;
            wheelAccum += delta;
            if (Math.Abs(wheelAccum) < Native.WHEEL_DELTA) return;
            int notch = Math.Sign(wheelAccum);
            wheelAccum = 0;
            // WM_MOUSEWHEEL carries screen coordinates.
            var global = new Point(Native.LoWord(m.LParam), Native.HiWord(m.LParam));
            var target = SourcePoint(global);
            if (Covers(target)) return;
            Forward.Scroll(target, notch);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg >= Native.WM_MOUSEFIRST && m.Msg <= Native.WM_MOUSELAST && m.Msg != Native.WM_MOUSEMOVE && Ours())
                return;

            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    m.Result = new IntPtr(Native.MA_NOACTIVATE);
                    return;
                case Native.WM_DPICHANGED:
                    m.Result = IntPtr.Zero;      // sized in physical pixels; nothing to rescale
                    return;
                case Native.WM_EXITSIZEMOVE:
                    // A drag just ended: the one time the position is the user's choice. Moves
                    // Windows makes -- a monitor going to sleep -- are not saved over it.
                    Saved.OverlayOrigin = Location;
                    break;

                case Native.WM_LBUTTONDOWN:
                case Native.WM_LBUTTONDBLCLK:
                    if (!locked)
                    {
                        // Unlocked, a plain drag repositions instead of clicking through. There is
                        // deliberately no modifier-drag escape hatch while locked: every modifier
                        // has to stay available to pass through to the game.
                        Native.ReleaseCapture();
                        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTCAPTION), IntPtr.Zero);
                        return;
                    }
                    Route(m.LParam, MouseButtons.Left);
                    return;
                case Native.WM_RBUTTONDOWN:
                case Native.WM_RBUTTONDBLCLK:
                    if (locked) Route(m.LParam, MouseButtons.Right);
                    return;
                case Native.WM_MBUTTONDOWN:
                case Native.WM_MBUTTONDBLCLK:
                    if (locked) Route(m.LParam, MouseButtons.Middle);
                    return;
                case Native.WM_XBUTTONDOWN:
                case Native.WM_XBUTTONDBLCLK:
                    // Mouse 4 and 5, which MMO mice and Clique binds lean on.
                    if (locked)
                        Route(m.LParam, (Native.HiWord(m.WParam) & Native.XBUTTON2) != 0 ? MouseButtons.XButton2 : MouseButtons.XButton1);
                    m.Result = new IntPtr(1);
                    return;
                case Native.WM_LBUTTONUP:
                case Native.WM_RBUTTONUP:
                case Native.WM_MBUTTONUP:
                    return;
                case Native.WM_XBUTTONUP:
                    m.Result = new IntPtr(1);
                    return;
                case Native.WM_MOUSEWHEEL:
                    if (locked) Wheel(m);
                    return;
                case Native.WM_MOUSEHWHEEL:
                    // A tilt sideways. WoW binds no horizontal wheel, and passing it on as a
                    // vertical notch would fire a bind nobody pressed.
                    return;
            }
            base.WndProc(ref m);
        }

        // MARK: - State

        /// Unlocked: drag anywhere to reposition, and nothing passes through -- so you can place
        /// it without firing off heals. Locked: every click goes to the source.
        public void SetLocked(bool value)
        {
            locked = value;
            Cursor = locked ? Cursors.Hand : Cursors.SizeAll;
            Invalidate();
            Bar?.RefreshBar();
        }

        public void SetOpacity(double v)
        {
            alpha = (byte)Math.Round(Math.Min(1, Math.Max(0.1, v)) * 255);
            if (IsHandleCreated) Native.SetLayeredWindowAttributes(Handle, 0, alpha, Native.LWA_ALPHA);
        }

        public double Opacity_ => alpha / 255.0;

        /// Resize in place, keeping the same region and top-left corner.
        public void SetScale(double s)
        {
            scale = s;
            Retarget(SourceRect);
        }

        /// Point the overlay at a different region, keeping its top-left corner put.
        public void Retarget(Rectangle region)
        {
            SourceRect = region;
            Size = Scaled(region);
            Invalidate();
            Bar?.Place(Bounds);
            Bar?.RefreshBar();
            ApplyHeaderMode();
            App.Publish();
        }

        /// Show or hide the overlay and its header together. Capture keeps running while it is
        /// hidden, so it comes back instantly.
        public void SetShown(bool show)
        {
            if (show)
            {
                Show();
                KeepOnTop();
                ApplyHeaderMode();
            }
            else
            {
                Hide();
                Bar?.SetVisible(false);
            }
        }

        /// After the monitors change: back to where the user last put it if that is on screen
        /// again, else anywhere it can be reached.
        public void EnsureReachable()
        {
            var home = Saved.OverlayOrigin;
            if (home.HasValue && home.Value != Location && Screens.Reachable(new Rectangle(home.Value, Size)))
            {
                Location = home.Value;
                return;
            }
            if (Screens.Reachable(Bounds)) return;
            var p = Screens.Primary();
            if (p != null) Location = new Point(p.Bounds.X + 100, p.Bounds.Y + 100);
        }

        void KeepOnTop()
        {
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // MARK: - Header

        public void ExpandHeader()
        {
            Bar.SetCompact(false);
            Bar.Place(Bounds);
            Bar.RefreshBar();
        }

        public void CollapseHeader()
        {
            Bar.SetCompact(true);
            Bar.Place(Bounds);
        }

        public void ApplyHeaderMode()
        {
            if (Bar == null) return;
            Bar.RefreshBar();
            if (!Visible) { Bar.SetVisible(false); return; }
            switch (Saved.HeaderMode)
            {
                case HeaderMode.Pinned:
                    Bar.SetCompact(false);
                    Bar.Place(Bounds);
                    Bar.SetVisible(true);
                    break;
                case HeaderMode.Auto:
                    Bar.SetCompact(!Bar.Expanded);
                    Bar.Place(Bounds);
                    Bar.SetVisible(pointerNear);
                    break;
                case HeaderMode.Hidden:
                    Bar.SetCompact(true);
                    Bar.Place(Bounds);
                    Bar.SetVisible(pointerNear);
                    break;
            }
        }

        /// Overlay and header, each grown enough to bridge the gap between them, so travelling
        /// from one to the other never reads as leaving. Tested against geometry rather than
        /// with mouse-enter events: a fully transparent header receives no mouse events, so it
        /// could never report the pointer arriving on itself.
        bool Near(Point p)
        {
            int pad = (int)Math.Round(12 * (Screens.For(Bounds)?.Scale ?? 1f));
            if (Rectangle.Inflate(Bounds, pad, pad).Contains(p)) return true;
            return Bar != null && Rectangle.Inflate(Bar.Bounds, pad, pad).Contains(p);
        }

        void Poll()
        {
            // Once a second, make sure a game going full screen has not buried us.
            if (++ticks % 33 == 0 && Visible)
            {
                KeepOnTop();
                if (Bar != null && Bar.Visible) Bar.KeepOnTop();
            }
            if (!Visible) return;

            bool near = Near(Forward.RealPointer());
            if (near == pointerNear) return;
            pointerNear = near;
            if (Saved.HeaderMode == HeaderMode.Pinned) return;

            hideTimer.Stop();
            collapseTimer.Stop();
            if (near) Bar.SetVisible(true);
            else hideTimer.Start();          // a grace period, so a hand that overshoots keeps it
        }

        void HideAfterGrace()
        {
            hideTimer.Stop();
            if (pointerNear || Saved.HeaderMode == HeaderMode.Pinned) return;
            Bar.SetVisible(false);
            // Come back as dots next time rather than as a wall of buttons.
            if (Saved.HeaderMode == HeaderMode.Auto) collapseTimer.Start();
        }

        void CollapseAfterGrace()
        {
            collapseTimer.Stop();
            if (pointerNear) return;
            Bar.SetCompact(true);
            Bar.Place(Bounds);
        }
    }
}
