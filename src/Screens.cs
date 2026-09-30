// Monitors, and finding and focusing the window under a point.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Glass
{
    public class Display
    {
        public IntPtr Handle;
        public Rectangle Bounds;
        public bool Primary;
        public uint Dpi = 96;

        public float Scale => Dpi / 96f;

        public override string ToString() =>
            string.Format("{0,6},{1,-6} {2}x{3}  {4}% scaling{5}", Bounds.X, Bounds.Y, Bounds.Width,
                          Bounds.Height, Dpi * 100 / 96, Primary ? "  (primary)" : "");
    }

    public static class Screens
    {
        public static List<Display> All()
        {
            var list = new List<Display>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref RECT r, IntPtr d) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!Native.GetMonitorInfo(m, ref info)) return true;
                uint dpi = 96;
                try { if (Native.GetDpiForMonitor(m, 0, out uint x, out _) == 0 && x > 0) dpi = x; }
                catch { }                            // shcore is Windows 8.1+; 96 is a fine guess
                list.Add(new Display
                {
                    Handle = m,
                    Bounds = info.rcMonitor.ToRectangle(),
                    Primary = (info.dwFlags & 1) != 0,
                    Dpi = dpi,
                });
                return true;
            }, IntPtr.Zero);
            return list.OrderBy(x => x.Bounds.X).ThenBy(x => x.Bounds.Y).ToList();
        }

        public static Display Primary() => All().FirstOrDefault(d => d.Primary) ?? All().FirstOrDefault();

        /// Display holding the most of `r`, so a region near an edge still resolves.
        public static Display For(Rectangle r)
        {
            Display best = null;
            long bestArea = 0;
            foreach (var d in All())
            {
                var i = Rectangle.Intersect(d.Bounds, r);
                long area = (long)Math.Max(0, i.Width) * Math.Max(0, i.Height);
                if (area > bestArea) { bestArea = area; best = d; }
            }
            return best;
        }

        /// The display under a point, or the nearest one to it.
        public static Display At(Point p)
        {
            var all = All();
            return all.FirstOrDefault(d => d.Bounds.Contains(p))
                ?? all.OrderBy(d => Distance(d.Bounds, p)).FirstOrDefault();
        }

        static double Distance(Rectangle r, Point p)
        {
            int dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - (r.Right - 1));
            int dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - (r.Bottom - 1));
            return Math.Sqrt((double)dx * dx + (double)dy * dy);
        }

        /// True if enough of `r` is on some display to grab and drag it back.
        public static bool Reachable(Rectangle r) =>
            All().Any(d => { var i = Rectangle.Intersect(d.Bounds, r); return i.Width >= 40 && i.Height >= 20; });
    }

    public static class Wnd
    {
        static readonly uint ourPid = Native.GetCurrentProcessId();

        public static bool IsOurs(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            return pid == ourPid;
        }

        static bool Cloaked(IntPtr hWnd)
        {
            // A cloaked window is on another virtual desktop, or a suspended UWP shell: it is
            // in the z-order and contains the point, but nothing of it is on screen.
            try { return Native.DwmGetWindowAttribute(hWnd, Native.DWMWA_CLOAKED, out int v, 4) == 0 && v != 0; }
            catch { return false; }
        }

        /// GetWindowText, never GetWindowTextLength: for another process's window it reads the
        /// caption the system already holds and cannot block on that process's message pump.
        public static string Title(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            Native.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// The executable's name without ".exe", or "pid N". Cheap enough for every log line:
        /// Process.ProcessName would snapshot every process on the system to answer.
        public static string ProcessName(IntPtr hWnd)
        {
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            var path = ProcessPath(pid);
            return path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : "pid " + pid;
        }

        /// Full path of a process's executable. Limited-information access is granted even for a
        /// process running as administrator, so this works where Process.MainModule does not.
        public static string ProcessPath(uint pid)
        {
            var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, (int)size) : null;
            }
            finally { Native.CloseHandle(h); }
        }

        /// True if the process runs elevated. A process Glass may not even ask counts as
        /// elevated: that refusal is exactly what an administrator process gives a normal one.
        public static bool IsElevated(uint pid)
        {
            var h = pid == ourPid ? Native.GetCurrentProcess()
                                  : Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return Marshal.GetLastWin32Error() == Native.ERROR_ACCESS_DENIED;
            try
            {
                if (!Native.OpenProcessToken(h, Native.TOKEN_QUERY, out IntPtr token))
                    return Marshal.GetLastWin32Error() == Native.ERROR_ACCESS_DENIED;
                try { return Native.GetTokenInformation(token, Native.TokenElevation, out int e, 4, out _) && e != 0; }
                finally { Native.CloseHandle(token); }
            }
            finally { if (pid != ourPid) Native.CloseHandle(h); }
        }

        public static bool WeAreElevated => IsElevated(ourPid);

        /// An ordinary top-level window: visible, not minimised, not cloaked, not a tool window,
        /// titled, and not one of ours. The Windows reading of the macOS build's "layer 0" test.
        public static bool IsOrdinary(IntPtr hWnd)
        {
            if (!Native.IsWindowVisible(hWnd) || Native.IsIconic(hWnd)) return false;
            if (IsOurs(hWnd) || Cloaked(hWnd)) return false;
            int ex = Native.GetWindowLong(hWnd, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
            // Layered and transparent means click-through: a GPU or chat overlay spread over the
            // game. A real click there lands on the game, so input must too.
            const int clickThrough = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
            if ((ex & clickThrough) == clickThrough) return false;
            return Title(hWnd).Length > 0;
        }

        /// Frontmost ordinary window under a screen point, skipping our own. EnumWindows walks
        /// front to back, so the first hit is the one a real click there would land on.
        public static IntPtr At(Point p)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hWnd, _) =>
            {
                if (!IsOrdinary(hWnd)) return true;
                if (!Native.GetWindowRect(hWnd, out RECT r) || !r.ToRectangle().Contains(p)) return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);

            // WindowFromPoint usually agrees and is cheaper; rooted to the top-level window, it
            // covers the case where the enumeration found nothing ordinary-looking.
            if (found == IntPtr.Zero)
            {
                var h = Native.WindowFromPoint(new POINT(p.X, p.Y));
                if (h != IntPtr.Zero) h = Native.GetAncestor(h, Native.GA_ROOT);
                if (h != IntPtr.Zero && !IsOurs(h)) found = h;
            }
            return found;
        }

        /// The largest ordinary window whose title or process name contains `needle`.
        public static IntPtr LargestMatching(string needle)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            foreach (var hWnd in AllOrdinary())
            {
                var hay = Title(hWnd) + " " + ProcessName(hWnd);
                if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!Native.GetWindowRect(hWnd, out RECT r)) continue;
                var b = r.ToRectangle();
                long area = (long)b.Width * b.Height;
                if (area > bestArea) { bestArea = area; best = hWnd; }
            }
            return best;
        }

        public static List<IntPtr> AllOrdinary()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((hWnd, _) => { if (IsOrdinary(hWnd)) list.Add(hWnd); return true; }, IntPtr.Zero);
            return list;
        }

        /// A thread needs a message queue before AttachThreadInput will accept it. Worker threads
        /// do not have one until they ask a message question; this asks one.
        public static void EnsureMessageQueue() => Native.PeekMessage(out _, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);

        /// Bring a window forward. Windows refuses SetForegroundWindow to a process that did
        /// not receive the last input -- which is exactly Glass's position after a forwarded
        /// click has activated the other client. The standard remedy is to attach *this* thread
        /// to the current foreground thread's input for the moment of the call. It never
        /// waits on the game's message pump, and it detaches straight after.
        ///
        /// Call from the forwarding worker, not the UI thread: attaching briefly shares input
        /// state with the foreground thread, and that must not be the thread the user is
        /// waiting on for everything else.
        /// Which way the last successful Focus got there, for the log: Windows grants the
        /// foreground by rules that differ with what the user was pressing, and the log is how
        /// a failure in the field gets understood.
        [ThreadStatic] public static string LastFocusMethod;

        public static bool Focus(IntPtr hWnd)
        {
            LastFocusMethod = null;
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return false;
            if (Native.GetForegroundWindow() == hWnd) { LastFocusMethod = "already"; return true; }
            // The Async forms: the plain ones wait on the window's own thread, and a client in a
            // loading screen would hold up every click and key queued behind this.
            if (Native.IsIconic(hWnd)) Native.ShowWindowAsync(hWnd, Native.SW_RESTORE);

            // Windows lets a process take the foreground only if it received the last input.
            // After a click Glass usually did; after a shift-click it did not -- releasing Shift
            // went to the clicked client -- and plain SetForegroundWindow is refused. Measured
            // on real Windows (the CI end-to-end run). So: the plain call, then attaching to the
            // foreground thread, then SwitchToThisWindow (what Alt+Tab uses), then the
            // documented unlock, a tap of Alt.
            if (Native.SetForegroundWindow(hWnd) && Settled(hWnd)) { LastFocusMethod = "SetForegroundWindow"; return true; }

            uint self = Native.GetCurrentThreadId();
            uint front = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
            bool attached = false;
            try
            {
                if (front != 0 && front != self) attached = Native.AttachThreadInput(self, front, true);
                Native.SetWindowPos(hWnd, Native.HWND_TOP, 0, 0, 0, 0,
                                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_ASYNCWINDOWPOS);
                Native.SetForegroundWindow(hWnd);
            }
            catch (Exception e) { Log.Write("focus failed: " + e.Message); }
            finally { if (attached) Native.AttachThreadInput(self, front, false); }
            if (Settled(hWnd)) { LastFocusMethod = "AttachThreadInput"; return true; }

            Native.SwitchToThisWindow(hWnd, true);
            if (Settled(hWnd)) { LastFocusMethod = "SwitchToThisWindow"; return true; }

            // "The system automatically enables calls to SetForegroundWindow if the user presses
            // the ALT key." This is what worked on real Windows after a shift-click. Not while
            // Shift is held -- Left Alt + Shift switches keyboard layout -- so wait up to a second
            // for a shift-clicking hand to let go first.
            var held = System.Diagnostics.Stopwatch.StartNew();
            while ((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0 && held.ElapsedMilliseconds < 1000) Thread.Sleep(5);
            if ((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) == 0)
            {
                Forward.TapAlt();
                Native.SetForegroundWindow(hWnd);
                if (Settled(hWnd)) { LastFocusMethod = "Alt tap" + (held.ElapsedMilliseconds > 10 ? " after Shift let go" : ""); return true; }
            }
            return false;
        }

        /// Foreground changes can land a moment after the call; give it that moment before
        /// escalating, or a stronger method starts while the first is still arriving.
        static bool Settled(IntPtr hWnd)
        {
            for (int i = 0; i < 30; i++)
            {
                if (Native.GetForegroundWindow() == hWnd) return true;
                Thread.Sleep(2);
            }
            return false;
        }

        /// Focus a window and wait until Windows agrees.
        public static bool FocusAndWait(IntPtr hWnd, int ms, int tries)
        {
            for (int t = 0; t < Math.Max(1, tries); t++)
            {
                if (Focus(hWnd)) return true;
                for (int i = 0; i < Math.Max(1, ms / 5); i++)
                {
                    if (Native.GetForegroundWindow() == hWnd) { LastFocusMethod ??= "late"; return true; }
                    Thread.Sleep(5);
                }
            }
            if (Native.GetForegroundWindow() != hWnd) return false;
            LastFocusMethod ??= "late";
            return true;
        }
    }
}
