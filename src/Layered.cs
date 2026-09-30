// Per-pixel-alpha windows: the header and the region picker.
//
// WinForms cannot draw a window that is partly see-through (the picker's dimmed screen with a
// clear hole, the header's translucent pill) except through UpdateLayeredWindow. The surface
// here is one DIB section that GDI+ draws into directly and UpdateLayeredWindow reads from, so
// nothing is copied per frame -- which matters for a picker covering a 4K monitor.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Glass
{
    public sealed class LayeredSurface : IDisposable
    {
        public readonly int Width, Height;
        public readonly Bitmap Bitmap;
        readonly IntPtr dib, memDC, oldBitmap;

        public LayeredSurface(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = 40, biWidth = Width, biHeight = -Height,   // negative: top-down rows
                biPlanes = 1, biBitCount = 32, biCompression = Native.BI_RGB,
            };
            var screen = Native.GetDC(IntPtr.Zero);
            dib = Native.CreateDIBSection(screen, ref bmi, Native.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            Native.ReleaseDC(IntPtr.Zero, screen);
            if (dib == IntPtr.Zero) throw new OutOfMemoryException("CreateDIBSection " + Width + "x" + Height);
            memDC = Native.CreateCompatibleDC(IntPtr.Zero);
            oldBitmap = Native.SelectObject(memDC, dib);
            // Premultiplied ARGB over the DIB's own memory: exactly what UpdateLayeredWindow
            // expects to read, with no conversion in between.
            Bitmap = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, bits);
        }

        public Graphics Draw()
        {
            var g = Graphics.FromImage(Bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;   // ClearType needs an opaque background
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            return g;
        }

        static BLENDFUNCTION Blend(byte alpha) => new BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = Native.AC_SRC_ALPHA,
        };

        /// Show the whole surface at `at` (screen pixels), faded by `alpha`.
        public bool Push(IntPtr hWnd, Point at, byte alpha)
        {
            Native.GdiFlush();
            var dst = new POINT(at.X, at.Y);
            var size = new SIZE(Width, Height);
            var src = new POINT(0, 0);
            var blend = Blend(alpha);
            return Native.UpdateLayeredWindow(hWnd, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0,
                                              ref blend, Native.ULW_ALPHA);
        }

        /// Push only `dirty` (surface coordinates). The window must already be this size.
        public unsafe bool Push(IntPtr hWnd, Point at, byte alpha, Rectangle dirty)
        {
            dirty.Intersect(new Rectangle(0, 0, Width, Height));
            if (dirty.Width <= 0 || dirty.Height <= 0) return true;
            Native.GdiFlush();
            var dst = new POINT(at.X, at.Y);
            var size = new SIZE(Width, Height);
            var src = new POINT(0, 0);
            var blend = Blend(alpha);
            var rect = new RECT(dirty);
            var info = new UPDATELAYEREDWINDOWINFO
            {
                cbSize = (uint)sizeof(UPDATELAYEREDWINDOWINFO),
                pptDst = (IntPtr)(&dst),
                psize = (IntPtr)(&size),
                hdcSrc = memDC,
                pptSrc = (IntPtr)(&src),
                pblend = (IntPtr)(&blend),
                dwFlags = Native.ULW_ALPHA,
                prcDirty = (IntPtr)(&rect),
            };
            if (Native.UpdateLayeredWindowIndirect(hWnd, ref info)) return true;
            return Push(hWnd, at, alpha);                     // fall back to a full push
        }

        public void Dispose()
        {
            Bitmap.Dispose();
            if (memDC != IntPtr.Zero)
            {
                Native.SelectObject(memDC, oldBitmap);
                Native.DeleteDC(memDC);
            }
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
        }
    }

    /// A borderless, topmost, per-pixel-alpha window that never paints through WM_PAINT.
    public class LayeredForm : Form
    {
        protected virtual bool NoActivate => true;

        public LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= Native.WS_POPUP;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                if (NoActivate) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => NoActivate;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Glass's own windows must never turn up inside a mirror.
            Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void WndProc(ref Message m)
        {
            // Sized in physical pixels on purpose; moving to a monitor with another scale
            // must not resize anything behind our back.
            if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == Native.WM_MOUSEACTIVATE && NoActivate) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            // A click Glass synthesized, landing on its own header: the mirror placed over the
            // region it mirrors. It must not press a preset nobody chose.
            if (m.Msg > Native.WM_MOUSEMOVE && m.Msg <= Native.WM_MOUSELAST
                && Native.GetMessageExtraInfo() == Forward.GlassTag) return;
            base.WndProc(ref m);
        }

        public void KeepOnTop() =>
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public static class Look
    {
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// The macOS palette, so the two builds look like the same app.
        public static readonly Color Green = Color.FromArgb(52, 199, 89);       // systemGreen
        public static readonly Color Orange = Color.FromArgb(255, 149, 0);      // systemOrange

        static FontFamily icons;

        /// Segoe MDL2 Assets ships with every Windows 10 and 11; the fallback only matters on
        /// something older, where a plain glyph is still readable.
        public static FontFamily IconFamily
        {
            get
            {
                if (icons != null) return icons;
                try { icons = new FontFamily("Segoe MDL2 Assets"); }
                catch { icons = FontFamily.GenericSansSerif; }
                return icons;
            }
        }

        public static bool HasIconFont => IconFamily.Name == "Segoe MDL2 Assets";
    }
}
