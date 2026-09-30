// Live capture of a rect on the desktop.
//
// The macOS build uses SCStream. Here it is a BitBlt of the desktop DC on a timer, which is
// the equivalent supported path: it reads what the compositor has already drawn, so it costs
// almost nothing for a few hundred square pixels, and it sees every window on every monitor.
//
// Two things follow from using the composited desktop as the source:
//   * Our own overlay would be captured back into itself. SetWindowDisplayAffinity with
//     WDA_EXCLUDEFROMCAPTURE removes Glass's windows from every capture path, and the
//     overlay is also a layered window, which a plain SRCCOPY skips anyway.
//   * A game in exclusive full screen is not composited, so there is nothing to read.
//     WoW must be in Windowed (Fullscreen). This is the one real behavioural difference
//     from the Mac build and it is called out in the README.
//
// The captured pixels stay in a DIB that the overlay blits straight out of. Nothing is
// copied into managed memory and no Bitmap is allocated per frame.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Glass
{
    public sealed class Capture : IDisposable
    {
        /// Held while the DIB is written, and again while the overlay reads it. The capture
        /// timer and the UI thread are the only two contenders and both are brief.
        public readonly object Gate = new object();

        public IntPtr MemDC { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Running { get; private set; }
        /// Frames captured since launch. A restart counts as working once this moves.
        public long Frames => Interlocked.Read(ref frames);
        long frames;

        /// Raised after each frame lands, on the capture thread.
        public Action OnFrame;
        /// Raised when the display being captured has gone. The owner re-resolves the region
        /// rather than freezing on the last frame.
        public Action OnDrop;

        IntPtr screenDC, dib, oldBitmap, bits;
        Rectangle source;

        /// Set when a window is being captured rather than the screen: the window, and the
        /// mirrored rectangle in the window's own coordinates.
        WindowCapture window;
        Rectangle crop;
        long lastWindowFrame;

        /// "the screen" or "window WowB 1492", for the report.
        public string Method { get; private set; } = "none";
        /// Why window capture was not used, if it was asked for and failed.
        public string WindowProblem { get; private set; }
        /// Set when the captured window has closed; the owner rebinds.
        public Action OnWindowClosed;
        Timer timer;
        int failures;
        int busy;
        bool loggedFormat;

        /// Capture `rect` (screen pixels). With `hWnd`, capture that window's `windowRect` (window
        /// pixels) instead, so it keeps showing while covered; the screen is the fallback.
        public void Start(Rectangle rect, double fps, IntPtr hWnd = default, Rectangle windowRect = default)
        {
            Stop();
            if (rect.Width < 8 || rect.Height < 8) return;
            WindowProblem = null;

            source = rect;
            Width = rect.Width;
            Height = rect.Height;

            screenDC = Native.GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero) { Log.Write("capture: no desktop DC"); return; }

            MemDC = Native.CreateCompatibleDC(screenDC);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = Width,
                // Negative height asks for a top-down DIB, which is the orientation every
                // other coordinate in Glass already uses.
                biHeight = -Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Native.BI_RGB,
            };
            dib = Native.CreateDIBSection(screenDC, ref bmi, Native.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
            {
                Log.Write("capture: could not allocate a " + Width + "x" + Height + " DIB");
                Stop();
                return;
            }
            oldBitmap = Native.SelectObject(MemDC, dib);

            Method = "the screen";
            if (hWnd != IntPtr.Zero)
            {
                window = WindowCapture.TryStart(hWnd, out string why);
                var who = Wnd.ProcessName(hWnd) + " " + WindowPid(hWnd);
                if (window != null)
                {
                    crop = new Rectangle(windowRect.Location, new Size(Width, Height));
                    Method = "window " + who;
                    lastWindowFrame = Environment.TickCount64;
                }
                else
                {
                    WindowProblem = why;
                    Log.Write("capture: cannot capture the window " + who + " (" + why + "); mirroring the screen instead");
                }
            }

            Running = true;
            failures = 0;
            loggedFormat = false;
            int period = Math.Max(4, (int)Math.Round(1000.0 / Math.Max(1, fps)));
            timer = new Timer(_ => Tick(), null, 0, period);
            Log.Write(string.Format("capture started {0}x{1} at {2},{3}, {4} fps, from {5}",
                                    Width, Height, window != null ? crop.X : source.X, window != null ? crop.Y : source.Y,
                                    (int)fps, Method));
        }

        void Tick()
        {
            // A tick that overruns the period would otherwise run alongside the next one.
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try { TickOnce(); }
            finally { Volatile.Write(ref busy, 0); }
        }

        static uint WindowPid(IntPtr h) { Native.GetWindowThreadProcessId(h, out uint pid); return pid; }

        void TickOnce()
        {
            if (!Running) return;
            if (window != null) { WindowTick(); return; }
            bool ok;
            lock (Gate)
            {
                if (!Running || MemDC == IntPtr.Zero) return;
                // No CAPTUREBLT: it would pull in layered windows, which is precisely the
                // set of windows Glass puts on screen itself.
                ok = Native.BitBlt(MemDC, 0, 0, Width, Height, screenDC, source.X, source.Y, Native.SRCCOPY);
            }

            if (ok)
            {
                if (!loggedFormat)
                {
                    loggedFormat = true;
                    Log.Write("capture format BGRA32 top-down, " + Width + "x" + Height);
                }
                if (failures >= 30) Log.Write("capture: working again after " + failures + " failed reads");
                failures = 0;
                Interlocked.Increment(ref frames);
                var f = OnFrame;
                if (f != null) { try { f(); } catch { } }
                return;
            }

            // A failed blit on its own means nothing -- a mode switch or a UAC prompt will do
            // it. The lock screen does it for as long as the PC stays locked, so a run of them
            // is retried quietly, with a fresh desktop DC every couple of seconds in case the old
            // one died with a display change. Only a display that has gone ends the capture.
            if (++failures == 1) Log.Write("capture: BitBlt failed");
            if (failures % 30 != 0) return;
            if (failures == 30) Log.Write("capture: the screen cannot be read (locked, or a UAC prompt?) -- still trying");
            if (Screens.For(source) == null)
            {
                Log.Write("capture: the display it was reading has gone");
                var d = OnDrop;
                Stop();
                if (d != null) { try { d(); } catch { } }
                return;
            }
            lock (Gate)
            {
                if (!Running || screenDC == IntPtr.Zero) return;
                Native.ReleaseDC(IntPtr.Zero, screenDC);
                screenDC = Native.GetDC(IntPtr.Zero);
            }
        }

        /// A window frame, if a new one has arrived. A covered client may draw only a few times a
        /// second, so no new frame is not a failure; an error or a closed window is.
        void WindowTick()
        {
            bool fresh;
            try
            {
                lock (Gate)
                {
                    if (!Running || window == null || bits == IntPtr.Zero) return;
                    Native.GdiFlush();
                    fresh = window.CopyLatest(crop, bits, Width * 4);
                }
            }
            catch (Exception e)
            {
                if (++failures == 1) Log.Write("capture: window frame failed: " + e.Message);
                return;
            }
            if (window.Closed)
            {
                Log.Write("capture: the mirrored window closed");
                var closed = OnWindowClosed;
                Stop();
                if (closed != null) { try { closed(); } catch { } }
                return;
            }
            if (!fresh)
            {
                if (Environment.TickCount64 - lastWindowFrame > 10000 && !loggedFormat)
                {
                    loggedFormat = true;     // reused as "said so once"
                    Log.Write("capture: no picture from the window for 10s (minimised? WoW skips drawing when minimised)");
                }
                return;
            }
            lastWindowFrame = Environment.TickCount64;
            if (failures > 0) { Log.Write("capture: window frames working again"); failures = 0; }
            Interlocked.Increment(ref frames);
            var f = OnFrame;
            if (f != null) { try { f(); } catch { } }
        }

        public void Stop()
        {
            Running = false;
            var t = timer; timer = null;
            if (t != null) { t.Dispose(); }
            lock (Gate)
            {
                if (window != null) { window.Dispose(); window = null; }
                bits = IntPtr.Zero;
                if (MemDC != IntPtr.Zero)
                {
                    if (oldBitmap != IntPtr.Zero) Native.SelectObject(MemDC, oldBitmap);
                    Native.DeleteDC(MemDC);
                    MemDC = IntPtr.Zero;
                }
                if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
                if (screenDC != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, screenDC); screenDC = IntPtr.Zero; }
                oldBitmap = IntPtr.Zero;
            }
        }

        /// Blit the newest frame onto a device context, scaled to `dest`.
        public bool DrawTo(IntPtr hdc, Rectangle dest)
        {
            lock (Gate)
            {
                if (MemDC == IntPtr.Zero || Width <= 0 || Height <= 0) return false;
                // Exact size is a straight copy; anything else asks GDI to interpolate, or
                // party frames turn to aliased mush at 1.4x.
                bool exact = dest.Width == Width && dest.Height == Height;
                Native.SetStretchBltMode(hdc, exact ? Native.COLORONCOLOR : Native.HALFTONE);
                return Native.StretchBlt(hdc, dest.X, dest.Y, dest.Width, dest.Height,
                                         MemDC, 0, 0, Width, Height, Native.SRCCOPY);
            }
        }

        public void Dispose() => Stop();
    }
}
