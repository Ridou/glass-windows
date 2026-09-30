// Mouseover keys: number keys pressed while the pointer is over the overlay act on the frame
// being hovered, not on the character you are playing.
//
// WH_KEYBOARD_LL is the Windows counterpart of the macOS build's CGEventTap, with a harsher
// failure mode: a callback that overruns LowLevelHooksTimeout gets the hook *silently*
// removed, after which every keypress falls through to the wrong character and nothing says
// so. Microsoft's own advice is to run the hook on a dedicated thread that hands work off and
// returns at once, and that is what this is -- the thread does nothing else, so a busy UI can
// never make it late.
//
// And because removal is silent, it is watched for: the same thread registers for Raw Input,
// which sees every keystroke independently of hooks. A keystroke that raw input saw and the
// hook did not means the hook is gone, and it is reinstalled on the spot. (AltSnap tried the
// same with GetLastInputInfo first, and moved to Raw Input because that counts mouse input
// too.)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Glass
{
    /// Everything the hook needs to decide, published by the UI thread as one immutable object.
    /// The hook reads a snapshot and never touches a window, a lock or a file.
    public sealed class HookState
    {
        public Rectangle Overlay;       // screen pixels
        public Rectangle Source;        // the region being mirrored
        public bool Visible, Locked, ForwardKeys, KeysViaPost, Picking;
        public Shortcut[] Shortcuts = new Shortcut[0];

        /// Screen point over the overlay -> the matching point in the captured region.
        public Point? SourcePoint(Point p)
        {
            if (Overlay.Width <= 0 || Overlay.Height <= 0 || !Overlay.Contains(p)) return null;
            double fx = (p.X - Overlay.X + 0.5) / Overlay.Width;
            double fy = (p.Y - Overlay.Y + 0.5) / Overlay.Height;
            int x = Source.X + (int)Math.Floor(fx * Source.Width);
            int y = Source.Y + (int)Math.Floor(fy * Source.Height);
            return new Point(Math.Min(Math.Max(x, Source.Left), Source.Right - 1),
                             Math.Min(Math.Max(y, Source.Top), Source.Bottom - 1));
        }
    }

    public static class Hooks
    {
        public static volatile HookState State = new HookState();

        /// 1 2 3 4 5 6 7 8 9 0 - =  -- the number row, which MMO mice send from their side grid.
        static readonly HashSet<int> Forwarded = new HashSet<int>
        {
            '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 0xBD /* - */, 0xBB /* = */,
        };

        /// Keys swallowed on key-down, so their key-up is swallowed too and the character you
        /// are playing never sees half a keypress. Only the hook thread touches this.
        static readonly HashSet<int> swallowed = new HashSet<int>();

        const int WM_APP_PING = Native.WM_APP + 1;
        const int WM_APP_REHOOK = Native.WM_APP + 2;

        static Thread thread;
        static uint threadId;
        static IntPtr hook = IntPtr.Zero;
        static Native.HookProc proc;         // must outlive the hook, or the GC collects it
        static RawWatch watch;
        static long lastHookTick;            // Environment.TickCount64 of the last hooked event
        static long lastReinstall;
        static int misses;
        static double slowest;
        static System.Threading.Timer ping;
        static readonly ManualResetEventSlim ready = new ManualResetEventSlim();

        public static bool Installed => hook != IntPtr.Zero;

        public static void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "glass.hooks" };
            thread.Start();
            ready.Wait(2000);

            // A watchdog for this thread, the one whose lateness matters most.
            ping = new System.Threading.Timer(_ =>
            {
                if (threadId != 0)
                    Native.PostThreadMessage(threadId, WM_APP_PING, new IntPtr(Environment.TickCount), IntPtr.Zero);
            }, null, 1000, 100);
        }

        /// Ask the hook thread to drop and reinstall its hook. Offered in the tray menu, and done
        /// on display changes and session unlock, which is when things usually come loose.
        public static void Reinstall()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, WM_APP_REHOOK, IntPtr.Zero, IntPtr.Zero);
        }

        public static void Stop()
        {
            ping?.Dispose();
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        static void Run()
        {
            threadId = Native.GetCurrentThreadId();
            Wnd.EnsureMessageQueue();
            Install();
            try { watch = new RawWatch(); }
            catch (Exception e) { Log.Write("raw input watchdog unavailable: " + e.Message); }
            ready.Set();

            while (Native.GetMessage(out MSG m, IntPtr.Zero, 0, 0) > 0)
            {
                if (m.hwnd == IntPtr.Zero && m.message == WM_APP_PING)
                {
                    int lag = Environment.TickCount - m.wParam.ToInt32();
                    if (lag > 250) Log.Write("hook thread stalled " + lag + "ms");
                    continue;
                }
                if (m.hwnd == IntPtr.Zero && m.message == WM_APP_REHOOK)
                {
                    Remove();
                    Install();
                    continue;
                }
                Native.TranslateMessage(ref m);
                Native.DispatchMessage(ref m);
            }
            Remove();
        }

        static void Install()
        {
            if (hook != IntPtr.Zero) return;
            proc = Callback;
            hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            lastHookTick = Environment.TickCount64;
            if (hook == IntPtr.Zero)
                Log.Write("mouseover keys unavailable -- SetWindowsHookEx failed, error " + Marshal.GetLastWin32Error());
            else
                Log.Write("keyboard hook installed");
        }

        static void Remove()
        {
            if (hook == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            swallowed.Clear();
        }

        static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return Native.CallNextHookEx(hook, nCode, wParam, lParam);

            var started = Stopwatch.GetTimestamp();
            lastHookTick = Environment.TickCount64;
            bool claimed = false;
            try
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                // Our own synthesized keys, coming back around. Never re-forward them.
                if (k.dwExtraInfo != Forward.GlassTag)
                {
                    int msg = wParam.ToInt32();
                    bool isUp = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                    claimed = Claim((int)k.vkCode, isUp);
                }
            }
            catch (Exception e) { Log.Write("hook: " + e.Message); }

            var took = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            if (took > 20 && took > slowest)
            {
                slowest = took;
                Log.Write(string.Format("hook callback took {0:F1}ms -- Windows drops hooks that overrun", took));
            }
            return claimed ? new IntPtr(1) : Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        static string lastRefusal;

        /// Decide whether a key belongs to the hovered frame. Only ever true while the pointer is
        /// over a visible, locked overlay -- everywhere else the number row is yours.
        static bool Claim(int vk, bool isUp)
        {
            if (isUp) return swallowed.Remove(vk);
            if (!Forwarded.Contains(vk)) return false;

            var s = State;
            if (s == null || s.Picking || s.Overlay.Width <= 0) return false;

            var pointer = Forward.RealPointer();
            if (!s.Overlay.Contains(pointer)) return false;          // not hovering: your key
            if (!s.ForwardKeys || !s.Locked || !s.Visible)
            {
                var why = "key over overlay not forwarded: forwardKeys=" + s.ForwardKeys
                          + " locked=" + s.Locked + " visible=" + s.Visible;
                if (why != lastRefusal) { lastRefusal = why; Log.Write(why); }
                return false;
            }
            lastRefusal = null;

            // Glass's own shortcuts (Ctrl+Alt+1 and friends) win over forwarding.
            uint mods = Shortcut.LiveMods();
            foreach (var sc in s.Shortcuts)
                if (sc.KeyCode == vk && (sc.Mods & 0xF) == mods) return false;

            // A held key would otherwise repeat the whole warp many times a second. Swallow the
            // repeats and forward the first press only.
            if (!swallowed.Add(vk)) return true;

            var source = s.SourcePoint(pointer);
            if (!source.HasValue) return true;
            Forward.Key(vk, source.Value, mods, s.KeysViaPost);
            return true;
        }

        /// A message-only window on the hook thread that receives Raw Input for every keystroke,
        /// hooked or not.
        sealed class RawWatch : NativeWindow
        {
            readonly IntPtr buffer = Marshal.AllocHGlobal(256);
            readonly uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

            public RawWatch()
            {
                CreateHandle(new CreateParams { Caption = "Glass.RawWatch", Parent = Native.HWND_MESSAGE });
                var dev = new[]
                {
                    new RAWINPUTDEVICE
                    {
                        usUsagePage = 0x01, usUsage = 0x06,       // generic desktop, keyboard
                        dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = Handle,
                    },
                };
                if (!Native.RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                    Log.Write("raw input registration failed, error " + Marshal.GetLastWin32Error());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_INPUT)
                {
                    try { Check(m.LParam); } catch { }
                }
                base.WndProc(ref m);
            }

            void Check(IntPtr raw)
            {
                uint size = 256;
                if (Native.GetRawInputData(raw, Native.RID_INPUT, buffer, ref size, headerSize) == unchecked((uint)-1))
                    return;
                var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
                if (header.dwType != Native.RIM_TYPEKEYBOARD) return;
                var kb = Marshal.PtrToStructure<RAWKEYBOARD>(buffer + (int)headerSize);
                if ((kb.Flags & Native.RI_KEY_BREAK) != 0) return;            // key-ups prove nothing

                // The hook runs before raw input is delivered, and on this same thread, so for a
                // key the hook saw, lastHookTick is only microseconds old by now.
                long now = Environment.TickCount64;
                if (hook != IntPtr.Zero && now - lastHookTick < 500) { misses = 0; return; }

                // A window running as administrator is invisible to a normal hook but may not be
                // to raw input, and reinstalling cannot fix that. Back off rather than churn:
                // 5s, 10s, 20s ... up to five minutes between attempts.
                long wait = Math.Min(5000L << Math.Min(misses, 6), 300000L);
                if (now - lastReinstall < wait) return;
                lastReinstall = now;
                misses++;
                Log.Write("keyboard hook missed a keystroke -- reinstalling (attempt " + misses
                          + "). Repeated attempts usually mean an administrator window has focus.");
                Remove();
                Install();
            }
        }
    }
}
