// Region picker: every monitor dims, you drag out the rectangle to mirror.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Glass
{
    /// One picker window per monitor, torn down together. The selection is held in global
    /// screen coordinates, so a drag that crosses onto another monitor draws on both.
    public static class Picker
    {
        static readonly List<PickerForm> forms = new List<PickerForm>();
        static Action<Rectangle> onDone;
        static Action onCancel;
        static Point? anchor, cursor;

        public static bool IsActive => forms.Count > 0;

        public static void Show(string title, Action<Rectangle> done, Action cancel = null)
        {
            if (forms.Count > 0) return;
            onDone = done;
            onCancel = cancel;
            foreach (var d in Screens.All())
            {
                var f = new PickerForm(d, title);
                forms.Add(f);
                f.Show();
                f.Render(null, null, true);
            }
            App.Hotkeys?.GrabEsc(Cancel);
            App.Publish();

            // The monitor under the pointer takes the keyboard, so Esc also works as a plain key.
            var p = Cursor.Position;
            var under = forms.FirstOrDefault(f => f.Bounds.Contains(p)) ?? forms.FirstOrDefault();
            under?.Activate();
        }

        /// Teardown is deferred by one message: Cancel and End are called from inside the picker
        /// windows' own event handlers, and a form must not be disposed under its own feet.
        static bool finishing;

        public static void Cancel()
        {
            if (forms.Count == 0 || finishing) return;
            finishing = true;
            var c = onCancel;
            App.Defer(() => { Dismiss(); c?.Invoke(); });
        }

        static void Dismiss()
        {
            App.Hotkeys?.ReleaseEsc();
            foreach (var f in forms) { f.Close(); f.Dispose(); }
            forms.Clear();
            anchor = cursor = null;
            onCancel = null;
            onDone = null;
            finishing = false;
            App.Publish();
        }

        static Rectangle? Selection
        {
            get
            {
                if (!anchor.HasValue || !cursor.HasValue) return null;
                var a = anchor.Value; var c = cursor.Value;
                return new Rectangle(Math.Min(a.X, c.X), Math.Min(a.Y, c.Y), Math.Abs(a.X - c.X), Math.Abs(a.Y - c.Y));
            }
        }

        internal static void Begin(Point global)
        {
            if (finishing) return;
            var before = Selection;
            anchor = cursor = global;
            foreach (var f in forms) f.Render(before, Selection, false);
        }

        internal static void Move(Point global)
        {
            if (!anchor.HasValue || finishing) return;
            var before = Selection;
            cursor = global;
            foreach (var f in forms) f.Render(before, Selection, false);
        }

        internal static void End(Point global)
        {
            if (!anchor.HasValue || finishing) return;
            cursor = global;
            var r = Selection;
            var before = r;
            anchor = cursor = null;
            if (!r.HasValue || r.Value.Width < 8 || r.Value.Height < 8)
            {
                // A stray click is not a selection.
                foreach (var f in forms) f.Render(before, null, false);
                return;
            }
            var done = onDone;
            var region = r.Value;
            finishing = true;
            App.Defer(() => { Dismiss(); done?.Invoke(region); });
        }
    }

    /// Full-monitor dimmed overlay; drag out the rect to mirror.
    public sealed class PickerForm : LayeredForm
    {
        readonly Display display;
        readonly string title;
        readonly LayeredSurface surface;
        readonly float s;
        bool hintShown;
        bool dragging;

        // The picker takes focus, for Esc and the crosshair; the overlay and header never do.
        protected override bool NoActivate => false;

        public PickerForm(Display d, string title)
        {
            display = d;
            this.title = title;
            s = d.Scale;
            Bounds = d.Bounds;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            surface = new LayeredSurface(d.Bounds.Width, d.Bounds.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) surface.Dispose();
            base.Dispose(disposing);
        }

        Point Global(MouseEventArgs e) => new Point(display.Bounds.X + e.X, display.Bounds.Y + e.Y);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            // The drag may start on any monitor; that one takes the keyboard for Esc.
            Activate();
            Capture = true;
            dragging = true;
            Picker.Begin(Global(e));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Picker.Move(Global(e));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !dragging) return;
            dragging = false;
            Capture = false;
            Picker.End(Global(e));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Picker.Cancel();
        }

        // MARK: - Drawing

        Font labelFont, hintFont;
        Font LabelFont => labelFont ?? (labelFont = new Font("Segoe UI Semibold", 13 * s, GraphicsUnit.Pixel));
        Font HintFont => hintFont ?? (hintFont = new Font("Segoe UI Semibold", 18 * s, GraphicsUnit.Pixel));

        /// The size label sits centred just under the selection, like the Mac picker's.
        Rectangle LabelBox(Graphics g, Rectangle local)
        {
            var text = local.Width + " × " + local.Height;
            var size = g.MeasureString(text, LabelFont);
            int w = (int)Math.Ceiling(size.Width + 12 * s);
            int h = (int)Math.Ceiling(size.Height + 6 * s);
            int x = local.X + local.Width / 2 - w / 2;
            int y = local.Bottom + (int)(4 * s);
            if (y + h > surface.Height - (int)(4 * s)) y = surface.Height - (int)(4 * s) - h;
            return new Rectangle(x, y, w, h);
        }

        /// Everything a selection draws on, so the area can be repainted when it moves.
        Rectangle Footprint(Graphics g, Rectangle? global)
        {
            if (!global.HasValue) return Rectangle.Empty;
            var local = global.Value;
            local.Offset(-display.Bounds.X, -display.Bounds.Y);
            var r = Rectangle.Inflate(local, (int)(3 * s) + 2, (int)(3 * s) + 2);
            if (local.Width > 0 && local.Height > 0)
                r = Rectangle.Union(r, Rectangle.Inflate(LabelBox(g, local), 2, 2));
            return r;
        }

        public void Render(Rectangle? before, Rectangle? after, bool full)
        {
            if (IsDisposed || !IsHandleCreated) return;
            bool wantHint = after == null;
            if (wantHint != hintShown) full = true;           // the hint appears or goes: redraw all

            using (var g = surface.Draw())
            {
                var all = new Rectangle(0, 0, surface.Width, surface.Height);
                Rectangle dirty = full ? all : Rectangle.Union(Footprint(g, before), Footprint(g, after));
                if (!full && (dirty.Width <= 0 || dirty.Height <= 0)) return;
                dirty.Intersect(all);
                g.SetClip(dirty);

                // Dim everything, then punch the selection clear so you see exactly what you
                // are grabbing.
                g.CompositingMode = CompositingMode.SourceCopy;
                g.SmoothingMode = SmoothingMode.None;
                using (var dim = new SolidBrush(Color.FromArgb(89, 0, 0, 0)))          // 35% black
                    g.FillRectangle(dim, dirty);

                if (after.HasValue && after.Value.Width > 0 && after.Value.Height > 0)
                {
                    var local = after.Value;
                    local.Offset(-display.Bounds.X, -display.Bounds.Y);
                    using (var clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                        g.FillRectangle(clear, local);

                    g.CompositingMode = CompositingMode.SourceOver;
                    using (var pen = new Pen(Look.Green, 2 * s))
                        g.DrawRectangle(pen, local);

                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var box = LabelBox(g, local);
                    using (var path = Look.RoundRect(box, 4 * s))
                    using (var bg = new SolidBrush(Color.FromArgb(191, 0, 0, 0)))       // 75% black
                        g.FillPath(bg, path);
                    var text = local.Width + " × " + local.Height;
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, LabelFont, Brushes.White, box, fmt);
                }

                if (wantHint)
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using (var brush = new SolidBrush(Color.FromArgb(217, 255, 255, 255)))  // 85% white
                        g.DrawString(title, HintFont, brush, all, fmt);
                }
                hintShown = wantHint;

                if (full) surface.Push(Handle, display.Bounds.Location, 255);
                else surface.Push(Handle, display.Bounds.Location, 255, dirty);
            }
        }

        /// For the self-test: the surface as drawn.
        public Bitmap Snapshot() => (Bitmap)surface.Bitmap.Clone();
    }
}
