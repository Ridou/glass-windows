// Sending input to the real thing underneath.
//
// Two paths, the same two the macOS build has:
//
//   Warp  -- move the real cursor to the source point, act, warp back, hand focus back to
//            whoever had it. The default, and the one known to work.
//   Post  -- PostMessage straight at the target window, cursor untouched. On macOS the
//            equivalent (CGEventPostToPid) is dead for mouse events; on Windows it is not, so
//            here it is a real option. It stays off by default for clicks: a game that reads
//            the cursor rather than the message will ignore a posted click, and failing
//            visibly beats failing quietly on a heal. For keys it is the default, exactly as
//            on the Mac -- tools have sent keys to background game windows this way for years.
//
// Every button and modifier is forwarded exactly as pressed -- click-casting addons bind spells to
// combinations like shift-right-click, so anything less than full fidelity would fire the
// wrong spell rather than fail visibly. On the warp path modifiers need no synthesis: your
// real Shift or Alt is physically down while the click is routed, and the target reads it
// from the keyboard state.
//
// Everything runs on one serial worker thread. The UI thread and the hook thread only ever
// enqueue, so neither can be held up by a warp, a sleep, or a slow window.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Glass
{
    public static class Forward
    {
        /// Marks input Glass synthesizes, so its own keyboard hook never re-forwards it.
        public static readonly IntPtr GlassTag = new IntPtr(0x474C5353);   // 'GLSS'

        // Local latency per forwarded action. These are the whole cost this project exists to
        // remove, so they are tunable (--step, --settle, --hover) rather than baked in.
        public static int StepMs = 20;
        public static int SettleMs = 40;
        /// How long the pointer rests over a frame before a forwarded key is sent. Windows
        /// hands a window its posted messages *before* its mouse input, so a key posted too soon
        /// can be read before the client has noticed the pointer arrived -- and cast on whatever
        /// it was hovering before. 35ms covers one frame of a client capped at 30 fps.
        public static int HoverMs = 35;

        /// --pid: route clicks with PostMessage for this run, whatever Settings says.
        public static bool ForcePost;

        static readonly BlockingCollection<Action> jobs = new BlockingCollection<Action>();
        static Thread worker;

        public static void Start()
        {
            worker = new Thread(() =>
            {
                Wnd.EnsureMessageQueue();
                foreach (var job in jobs.GetConsumingEnumerable())
                {
                    try { job(); }
                    catch (Exception e) { Log.Write("forward: " + e.Message); }
                }
            }) { IsBackground = true, Name = "glass.forward" };
            worker.Start();
        }

        static void Pause(int ms) { if (ms > 0) Thread.Sleep(ms); }

        // MARK: - Where the pointer really is

        /// Set while a job has the cursor warped away. The pointer's *real* position is then the
        /// pre-warp one, and that is what hover tests and key claims must use.
        static volatile object warpOrigin;

        public static Point RealPointer()
        {
            var o = warpOrigin;
            if (o != null) return (Point)o;
            Native.GetCursorPos(out POINT p);
            return new Point(p.X, p.Y);
        }

        // MARK: - Buttons

        /// Swapped buttons (left-handed mouse settings) apply to synthesized input and to
        /// GetAsyncKeyState, which both speak physical buttons. Window messages speak logical
        /// ones. Translate once, here.
        static MouseButtons Physical(MouseButtons logical)
        {
            if (Native.GetSystemMetrics(Native.SM_SWAPBUTTON) == 0) return logical;
            if (logical == MouseButtons.Left) return MouseButtons.Right;
            if (logical == MouseButtons.Right) return MouseButtons.Left;
            return logical;
        }

        static int Vk(MouseButtons physical)
        {
            switch (physical)
            {
                case MouseButtons.Right: return Native.VK_RBUTTON;
                case MouseButtons.Middle: return Native.VK_MBUTTON;
                case MouseButtons.XButton1: return Native.VK_XBUTTON1;
                case MouseButtons.XButton2: return Native.VK_XBUTTON2;
                default: return Native.VK_LBUTTON;
            }
        }

        static void ButtonFlags(MouseButtons physical, out uint down, out uint up, out uint data)
        {
            data = 0;
            switch (physical)
            {
                case MouseButtons.Right:
                    down = Native.MOUSEEVENTF_RIGHTDOWN; up = Native.MOUSEEVENTF_RIGHTUP; return;
                case MouseButtons.Middle:
                    down = Native.MOUSEEVENTF_MIDDLEDOWN; up = Native.MOUSEEVENTF_MIDDLEUP; return;
                case MouseButtons.XButton1:
                    down = Native.MOUSEEVENTF_XDOWN; up = Native.MOUSEEVENTF_XUP; data = Native.XBUTTON1; return;
                case MouseButtons.XButton2:
                    down = Native.MOUSEEVENTF_XDOWN; up = Native.MOUSEEVENTF_XUP; data = Native.XBUTTON2; return;
                default:
                    down = Native.MOUSEEVENTF_LEFTDOWN; up = Native.MOUSEEVENTF_LEFTUP; return;
            }
        }

        /// A forwarded click is synthesized only once your real button is back up. While it is
        /// held, Windows still counts that button as down and routes button messages to the
        /// window that took the press -- the overlay -- so a click made then would land on
        /// Glass itself. The cost is the length of your press, typically well under 100ms.
        static bool WaitForRelease(MouseButtons logical)
        {
            int vk = Vk(Physical(logical));
            var sw = Stopwatch.StartNew();
            while ((Native.GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                // A two-second hold is not a click. Refuse rather than click into a drag.
                if (sw.ElapsedMilliseconds > 2000) return false;
                Thread.Sleep(1);
            }
            return true;
        }

        // MARK: - Public entry points (any thread)

        /// `window`: in window mode, the mirrored window, which may be covered by the one being
        /// played. Input then goes to it rather than to whatever is on screen at `target`.
        public static void Click(Point target, MouseButtons button, IntPtr window = default)
        {
            bool post = ForcePost || Saved.ClicksViaPid;
            bool front = Saved.HiddenClicks == "front";
            jobs.Add(() =>
            {
                if (!WaitForRelease(button))
                {
                    Log.Write("click " + button + " held over 2s -- not forwarded");
                    return;
                }
                if (window != IntPtr.Zero) HiddenClick(window, target, button, front);
                else if (post) PostClick(target, button); else WarpClick(target, button);
            });
        }

        public static void Scroll(Point target, int notches, IntPtr window = default)
        {
            if (notches == 0) return;
            bool post = ForcePost || Saved.ClicksViaPid;
            jobs.Add(() =>
            {
                if (window != IntPtr.Zero) HiddenScroll(window, target, notches);
                else if (post) PostScroll(target, notches); else WarpScroll(target, notches);
            });
        }

        static int routingLogged;

        /// With "Scroll inactive windows when I hover over them" turned off, Windows sends the
        /// wheel to the focused window -- the character you are playing -- wherever the pointer
        /// is. A synthesized wheel would then zoom the wrong camera, so it has to be posted.
        static bool WheelFollowsPointer()
        {
            if (!Native.SystemParametersInfo(Native.SPI_GETMOUSEWHEELROUTING, 0, out uint routing, 0))
                return true;                           // older than 1703: the pointer, always
            // Only 2 follows the pointer; 1 (hybrid) sends a desktop program's wheel to focus.
            bool follows = routing == Native.MOUSEWHEEL_ROUTING_MOUSE_POS;
            if (!follows && Interlocked.Exchange(ref routingLogged, 1) == 0)
                Log.Write("\"Scroll inactive windows\" is off, so the wheel is posted to the hovered client instead");
            return follows;
        }

        /// Called from the keyboard hook, so it only enqueues: `viaPost` comes from the hook's
        /// snapshot rather than from Saved, which would mean taking a lock.
        public static void Key(int vk, Point target, uint mods, bool viaPost, IntPtr window = default)
        {
            jobs.Add(() => RunKeyJob(vk, target, mods, viaPost, window));
        }

        // MARK: - Warp there and back

        static INPUT MouseInput(uint flags, uint data = 0) => new INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = GlassTag } },
        };

        static INPUT KeyInput(int vk, bool up) => new INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)Native.MapVirtualKey((uint)vk, 0),
                    dwFlags = up ? Native.KEYEVENTF_KEYUP : 0,
                    dwExtraInfo = GlassTag,
                },
            },
        };

        static int blockedLogged;
        static readonly ConcurrentDictionary<uint, bool> elevated = new ConcurrentDictionary<uint, bool>();
        static int elevationWarned;

        /// Windows silently discards input a normal program sends to one running as
        /// administrator -- SendInput still reports success. Nothing Glass does can reach such a
        /// window, so say so once, where it will be seen, instead of failing quietly on a heal.
        static void CheckReachable(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return;
            bool up = elevated.GetOrAdd(pid, p => Wnd.IsElevated(p) && !Wnd.WeAreElevated);
            if (!up || Interlocked.Exchange(ref elevationWarned, 1) != 0) return;
            var name = Wnd.ProcessName(hWnd);
            Log.Write(name + " runs as administrator; Windows blocks Glass from sending it input");
            App.Warn("Glass cannot reach " + name,
                     name + " is running as administrator, so Windows blocks Glass's clicks and keys. "
                     + "Run Glass as administrator too, or start the game normally.");
        }

        static int clipLogged;

        /// Put the cursor on `p` and confirm it is there. Windows clamps SetCursorPos to any
        /// ClipCursor rectangle, and a game's "lock cursor to window" option sets one around the client
        /// you are playing: the cursor would stop at its edge, and the click meant for the
        /// other character would land on this one. Better refused than delivered there.
        static bool PinTo(Point p)
        {
            Native.SetCursorPos(p.X, p.Y);
            Native.GetCursorPos(out POINT at);
            if (at.X == p.X && at.Y == p.Y) return true;
            if (Interlocked.Exchange(ref clipLogged, 1) == 0)
                Log.Write("the cursor could not reach " + p.X + "," + p.Y + " (it stopped at " + at.X + "," + at.Y
                          + "). Is \"Lock Cursor to Window\" on in the game you are playing? Input refused.");
            System.Media.SystemSounds.Beep.Play();
            return false;
        }

        /// A tap of Alt, tagged as Glass's own so the keyboard hook passes it by. Only for
        /// unlocking SetForegroundWindow; see Wnd.Focus.
        public static void TapAlt()
        {
            Send(KeyInput(Native.VK_MENU, false));
            Send(KeyInput(Native.VK_MENU, true));
        }

        static void Send(INPUT input)
        {
            var sent = Native.SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if (sent == 0 && Interlocked.Exchange(ref blockedLogged, 1) == 0)
                Log.Write("SendInput was refused (error " + Marshal.GetLastWin32Error()
                          + "). If the game runs as administrator, Glass must too.");
        }

        /// Shared warp-there-and-back, so every input kind behaves identically.
        ///
        /// A real click on a background window activates it -- that is Windows, not something
        /// we can post our way around. Remember who had focus and hand it straight back. Unlike
        /// macOS, the activation happens when the clicked window next pumps its messages, which
        /// for a background client at 30 fps can be most of a frame later. So after a click,
        /// wait to see it happen before handing focus back, or it would happen afterwards and
        /// quietly give the other character your keyboard.
        ///
        /// There is no Windows equivalent of CGAssociateMouseAndMouseCursorPosition: a client in
        /// mouse-look sees the round trip as two equal and opposite movements. The camera ends
        /// where it started, but can flick for a frame. Post mode avoids the trip entirely.
        static void WithWarp(Point target, bool expectActivation, Func<bool> body)
        {
            focusNote = "";
            IntPtr wasFront = Native.GetForegroundWindow();
            IntPtr clicked = expectActivation ? Wnd.At(target) : IntPtr.Zero;
            CheckReachable(expectActivation ? clicked : Wnd.At(target));
            Native.GetCursorPos(out POINT origin);
            warpOrigin = new Point(origin.X, origin.Y);
            try
            {
                // SetCursorPos rather than an absolute SendInput move: absolute moves are
                // normalised to 0..65535 across the whole desktop and can land a pixel off, and
                // a pixel is the difference between two party frames.
                if (!PinTo(target)) { clicked = IntPtr.Zero; return; }
                Pause(StepMs);
                if (!body()) clicked = IntPtr.Zero;              // nothing was delivered
            }
            finally
            {
                Pause(SettleMs);                                // must not race delivery
                Native.SetCursorPos(origin.X, origin.Y);
                warpOrigin = null;
                RestoreFocus(wasFront, clicked);
            }
        }

        /// What the last focus hand-back did, for the click's log line. Worker thread only.
        static string focusNote = "";

        /// "Client 4812": which one, told apart by process id, since both are the same program.
        static string Who(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return "nothing";
            Native.GetWindowThreadProcessId(hWnd, out uint pid);
            return Wnd.ProcessName(hWnd) + " " + pid;
        }

        static void RestoreFocus(IntPtr wasFront, IntPtr clicked)
        {
            if (wasFront == IntPtr.Zero) { focusNote = "no window had focus"; return; }
            if (Wnd.IsOurs(wasFront)) { focusNote = "Glass had focus, so it stays with " + Who(Native.GetForegroundWindow()); return; }
            var sw = Stopwatch.StartNew();
            string took = "";
            if (clicked != IntPtr.Zero && clicked != wasFront)
            {
                // Give the clicked client time to take focus, so it cannot do so after we have
                // already handed focus back. It activates when it next pumps its messages: within a
                // frame usually, but a client hitching on a load can take longer. Returning as soon
                // as it happens means the wait only costs anything when it is needed.
                while (Native.GetForegroundWindow() == wasFront && sw.ElapsedMilliseconds < 400) Thread.Sleep(2);
                took = Native.GetForegroundWindow() == wasFront
                    ? Who(clicked) + " never took focus (400ms); "
                    : Who(Native.GetForegroundWindow()) + " took focus in " + sw.ElapsedMilliseconds + "ms; ";
            }
            if (Native.GetForegroundWindow() == wasFront) { focusNote = took + "focus stayed on " + Who(wasFront); return; }
            var back = Stopwatch.StartNew();
            bool ok = Wnd.FocusAndWait(wasFront, 150, 2);
            focusNote = took + "focus back to " + Who(wasFront) + (ok ? " ok in " + back.ElapsedMilliseconds + "ms via " + Wnd.LastFocusMethod
                                                                     : " FAILED, now on " + Who(Native.GetForegroundWindow()));
            if (!ok) System.Media.SystemSounds.Beep.Play();
        }

        static void WarpClick(Point global, MouseButtons button)
        {
            var t0 = Stopwatch.StartNew();
            ButtonFlags(Physical(button), out uint down, out uint up, out uint data);
            bool sent = false;
            WithWarp(global, true, () =>
            {
                // Re-pin right before each press: Windows has no way to hold the pointer still,
                // so a hand still moving the mouse would otherwise carry it off the frame during
                // the pause, and the click would land on whatever it drifted to.
                if (!PinTo(global)) return false;
                Send(MouseInput(down, data));
                Pause(StepMs);
                PinTo(global);                                 // the release goes out regardless
                Send(MouseInput(up, data));
                Pause(StepMs);
                return sent = true;
            });
            if (!sent) { Log.Write("click " + button + " at " + global.X + "," + global.Y + " refused"); return; }
            Log.Write(string.Format("click {0} at {1},{2} via warp, {3:F0}ms -> {4}; {5}", button, global.X, global.Y,
                                    t0.Elapsed.TotalMilliseconds, Who(Wnd.At(global)), focusNote));
        }

        static void WarpScroll(Point global, int notches)
        {
            bool follows = WheelFollowsPointer();
            bool sent = false;
            WithWarp(global, false, () =>
            {
                if (!PinTo(global)) return false;
                if (follows) Send(MouseInput(Native.MOUSEEVENTF_WHEEL, unchecked((uint)(notches * Native.WHEEL_DELTA))));
                else PostScroll(global, notches);       // the pointer is there for mouseover binds
                Pause(StepMs);
                return sent = true;
            });
            if (!sent) return;
            Log.Write("wheel " + (notches > 0 ? "up" : "down") + " at " + global.X + "," + global.Y
                      + (follows ? " via warp" : " via warp and post") + "; " + focusNote);
        }

        // MARK: - Post straight at the window

        static int HeldModifiers()
        {
            int f = 0;
            if ((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0) f |= Native.MK_SHIFT;
            if ((Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0) f |= Native.MK_CONTROL;
            return f;
        }

        static int ButtonMask(MouseButtons b)
        {
            switch (b)
            {
                case MouseButtons.Left: return Native.MK_LBUTTON;
                case MouseButtons.Right: return Native.MK_RBUTTON;
                case MouseButtons.Middle: return Native.MK_MBUTTON;
                case MouseButtons.XButton1: return Native.MK_XBUTTON1;
                case MouseButtons.XButton2: return Native.MK_XBUTTON2;
                default: return 0;
            }
        }

        /// Path A: no cursor movement, no focus change. PostMessage never waits on the game.
        static void PostClick(Point global, MouseButtons button)
        {
            var hWnd = Wnd.At(global);
            if (hWnd == IntPtr.Zero) { Log.Write("post click: no window under " + global); return; }
            CheckReachable(hWnd);

            var p = new POINT(global.X, global.Y);
            Native.ScreenToClient(hWnd, ref p);
            var at = Native.MakeLParam(p.X, p.Y);

            int downMsg, upMsg, xbutton = 0;
            switch (button)
            {
                case MouseButtons.Right: downMsg = Native.WM_RBUTTONDOWN; upMsg = Native.WM_RBUTTONUP; break;
                case MouseButtons.Middle: downMsg = Native.WM_MBUTTONDOWN; upMsg = Native.WM_MBUTTONUP; break;
                case MouseButtons.XButton1:
                    downMsg = Native.WM_XBUTTONDOWN; upMsg = Native.WM_XBUTTONUP; xbutton = Native.XBUTTON1; break;
                case MouseButtons.XButton2:
                    downMsg = Native.WM_XBUTTONDOWN; upMsg = Native.WM_XBUTTONUP; xbutton = Native.XBUTTON2; break;
                default: downMsg = Native.WM_LBUTTONDOWN; upMsg = Native.WM_LBUTTONUP; break;
            }

            int mods = HeldModifiers();
            Native.PostMessage(hWnd, Native.WM_MOUSEMOVE, new IntPtr(mods), at);
            Pause(StepMs);
            Native.PostMessage(hWnd, downMsg, new IntPtr((xbutton << 16) | mods | ButtonMask(button)), at);
            Pause(StepMs);
            Native.PostMessage(hWnd, upMsg, new IntPtr((xbutton << 16) | mods), at);
            Log.Write("click " + button + " -> " + Wnd.ProcessName(hWnd) + " at " + p.X + "," + p.Y + " via post");
        }

        // MARK: - A covered window (window mode)

        /// A click on a window that another window covers -- one monitor, two clients. A real
        /// click at that spot would land on the window in front, the character being played.
        ///
        /// "post" (the default): the cursor still goes to the spot, so a client reading the
        /// cursor sees it on the right frame, but the button messages go straight to the covered
        /// window. Nothing changes focus and nothing flashes. Whether a game acts on posted clicks
        /// is the open question this mode exists to answer; keys posted this way work.
        ///
        /// "front": bring the covered window forward, click it for real, and hand focus back.
        /// Known to work, at the cost of that window flashing up for a moment.
        static void HiddenClick(IntPtr hWnd, Point global, MouseButtons button, bool front)
        {
            var t0 = Stopwatch.StartNew();
            if (!Native.IsWindow(hWnd)) { Log.Write("click: the mirrored window is gone"); return; }
            CheckReachable(hWnd);
            if (front) { FrontClick(hWnd, global, button, t0); return; }

            Native.GetCursorPos(out POINT origin);
            warpOrigin = new Point(origin.X, origin.Y);
            bool sent = false;
            try
            {
                if (!PinTo(global)) return;
                var p = new POINT(global.X, global.Y);
                Native.ScreenToClient(hWnd, ref p);
                var at = Native.MakeLParam(p.X, p.Y);
                int downMsg, upMsg, xbutton;
                Messages(button, out downMsg, out upMsg, out xbutton);
                int mods = HeldModifiers();
                Native.PostMessage(hWnd, Native.WM_MOUSEMOVE, new IntPtr(mods), at);
                Pause(StepMs);
                PinTo(global);
                Native.PostMessage(hWnd, downMsg, new IntPtr((xbutton << 16) | mods | ButtonMask(button)), at);
                Pause(StepMs);
                Native.PostMessage(hWnd, upMsg, new IntPtr((xbutton << 16) | mods), at);
                Pause(StepMs);
                sent = true;
            }
            finally
            {
                Pause(SettleMs);
                Native.SetCursorPos(origin.X, origin.Y);
                warpOrigin = null;
            }
            Log.Write(string.Format("click {0} at {1},{2} posted to covered {3}, {4:F0}ms{5}", button, global.X, global.Y,
                                    Who(hWnd), t0.Elapsed.TotalMilliseconds, sent ? "" : " -- refused"));
        }

        static void FrontClick(IntPtr hWnd, Point global, MouseButtons button, Stopwatch t0)
        {
            IntPtr home = Native.GetForegroundWindow();
            if (!Wnd.FocusAndWait(hWnd, 250, 2))
            {
                Log.Write("click " + button + ": " + Who(hWnd) + " would not come forward -- refused");
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            var took = t0.ElapsedMilliseconds;
            ButtonFlags(Physical(button), out uint down, out uint up, out uint data);
            Native.GetCursorPos(out POINT origin);
            warpOrigin = new Point(origin.X, origin.Y);
            bool sent = false;
            var restore = StepAside();
            try
            {
                if (!PinTo(global)) return;
                Pause(StepMs);
                if (!PinTo(global)) return;
                // The one thing this must never do is click the character being played. If
                // anything but the brought-forward client is at the spot, refuse.
                Func<IntPtr> underSpot = () => Native.GetAncestor(Native.WindowFromPoint(new POINT(global.X, global.Y)), Native.GA_ROOT);
                var under = underSpot();
                // Focused is not always raised yet; nudge it to the top and look again.
                for (int i = 0; i < 10 && under != hWnd; i++)
                {
                    Native.SetWindowPos(hWnd, Native.HWND_TOP, 0, 0, 0, 0,
                                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
                    Thread.Sleep(10);
                    under = underSpot();
                }
                if (under != hWnd)
                {
                    Log.Write("click " + button + ": " + Who(under) + " is over the spot, not " + Who(hWnd) + " -- refused");
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                Send(MouseInput(down, data));
                Pause(StepMs);
                PinTo(global);
                Send(MouseInput(up, data));
                Pause(StepMs);
                sent = true;
            }
            finally
            {
                Pause(SettleMs);
                Native.SetCursorPos(origin.X, origin.Y);
                warpOrigin = null;
                restore();
            }
            string back = "focus stayed on " + Who(hWnd);
            if (home != IntPtr.Zero && home != hWnd && !Wnd.IsOurs(home))
            {
                var sw = Stopwatch.StartNew();
                bool ok = Wnd.FocusAndWait(home, 150, 2);
                back = "focus back to " + Who(home) + (ok ? " ok in " + sw.ElapsedMilliseconds + "ms via " + Wnd.LastFocusMethod : " FAILED, now on " + Who(Native.GetForegroundWindow()));
                if (!ok) System.Media.SystemSounds.Beep.Play();
            }
            Log.Write(string.Format("click {0} at {1},{2} with {3} brought forward in {4}ms, {5:F0}ms{6}; {7}", button, global.X, global.Y,
                                    Who(hWnd), took, t0.Elapsed.TotalMilliseconds, sent ? "" : " -- refused", back));
        }

        /// Make the mirror and its header click-through for the moment of a real click, so a
        /// click on a spot they cover passes to the window brought forward beneath them. The
        /// Mac build does the same with ignoresMouseEvents. Returns the undo.
        static Action StepAside()
        {
            var undo = new List<(IntPtr, int)>();
            foreach (var h in new[] { OverlayForm.LiveHandle, HeaderBar.LiveHandle })
            {
                if (h == IntPtr.Zero) continue;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                // Layered and transparent: Windows hit-tests straight through the window.
                Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED);
                undo.Add((h, ex));
            }
            return () => { foreach (var (h, ex) in undo) Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex); };
        }

        static void HiddenScroll(IntPtr hWnd, Point global, int notches)
        {
            if (!Native.IsWindow(hWnd)) return;
            Native.GetCursorPos(out POINT origin);
            warpOrigin = new Point(origin.X, origin.Y);
            try
            {
                if (!PinTo(global)) return;
                Pause(StepMs);
                var w = new IntPtr(unchecked((notches * Native.WHEEL_DELTA) << 16) | HeldModifiers());
                Native.PostMessage(hWnd, Native.WM_MOUSEWHEEL, w, Native.MakeLParam(global.X, global.Y));
                Pause(StepMs);
            }
            finally
            {
                Pause(SettleMs);
                Native.SetCursorPos(origin.X, origin.Y);
                warpOrigin = null;
            }
            Log.Write("wheel " + (notches > 0 ? "up" : "down") + " at " + global.X + "," + global.Y + " posted to covered " + Who(hWnd));
        }

        static void Messages(MouseButtons button, out int downMsg, out int upMsg, out int xbutton)
        {
            xbutton = 0;
            switch (button)
            {
                case MouseButtons.Right: downMsg = Native.WM_RBUTTONDOWN; upMsg = Native.WM_RBUTTONUP; break;
                case MouseButtons.Middle: downMsg = Native.WM_MBUTTONDOWN; upMsg = Native.WM_MBUTTONUP; break;
                case MouseButtons.XButton1:
                    downMsg = Native.WM_XBUTTONDOWN; upMsg = Native.WM_XBUTTONUP; xbutton = Native.XBUTTON1; break;
                case MouseButtons.XButton2:
                    downMsg = Native.WM_XBUTTONDOWN; upMsg = Native.WM_XBUTTONUP; xbutton = Native.XBUTTON2; break;
                default: downMsg = Native.WM_LBUTTONDOWN; upMsg = Native.WM_LBUTTONUP; break;
            }
        }

        static void PostScroll(Point global, int notches)
        {
            var hWnd = Wnd.At(global);
            if (hWnd == IntPtr.Zero) { Log.Write("post scroll: no window under " + global); return; }
            CheckReachable(hWnd);
            // WM_MOUSEWHEEL carries *screen* coordinates, unlike every other mouse message.
            var w = new IntPtr(unchecked((notches * Native.WHEEL_DELTA) << 16) | HeldModifiers());
            Native.PostMessage(hWnd, Native.WM_MOUSEWHEEL, w, Native.MakeLParam(global.X, global.Y));
        }

        // MARK: - Keys

        static IntPtr KeyLParam(int vk, bool up, bool altHeld)
        {
            uint scan = Native.MapVirtualKey((uint)vk, 0);
            long l = 1 | ((long)scan << 16);
            if (altHeld) l |= 1L << 29;                    // context code: Alt is down
            if (up) l |= (1L << 30) | (1L << 31);          // previous state, transition
            return new IntPtr(l);
        }

        static void PostKey(IntPtr hWnd, int vk, bool up, bool alt)
        {
            int msg = alt ? (up ? Native.WM_SYSKEYUP : Native.WM_SYSKEYDOWN)
                          : (up ? Native.WM_KEYUP : Native.WM_KEYDOWN);
            Native.PostMessage(hWnd, msg, new IntPtr(vk), KeyLParam(vk, up, alt));
        }

        /// A background client's own keyboard state never saw the Shift you are holding -- it
        /// went to the window you are playing. So posted keys carry their modifiers as posted
        /// key messages too, in press order, released in reverse, the way other forwarders do it.
        static void PostKeyWithModifiers(IntPtr hWnd, int vk, uint mods)
        {
            bool alt = (mods & Native.MOD_ALT) != 0;
            bool ctrl = (mods & Native.MOD_CONTROL) != 0;
            bool shift = (mods & Native.MOD_SHIFT) != 0;

            if (ctrl) PostKey(hWnd, Native.VK_CONTROL, false, false);
            if (shift) PostKey(hWnd, Native.VK_SHIFT, false, false);
            if (alt) PostKey(hWnd, Native.VK_MENU, false, true);
            PostKey(hWnd, vk, false, alt);
            Pause(StepMs);
            PostKey(hWnd, vk, true, alt);
            if (alt) PostKey(hWnd, Native.VK_MENU, true, false);
            if (shift) PostKey(hWnd, Native.VK_SHIFT, true, false);
            if (ctrl) PostKey(hWnd, Native.VK_CONTROL, true, false);
        }

        /// Runs on the forwarding worker. Jobs are serial and each restores focus before it
        /// ends, so whoever has focus when a job starts is the right one to hand it back to.
        static void RunKeyJob(int vk, Point at, uint mods, bool viaPost, IntPtr window)
        {
            var t0 = Stopwatch.StartNew();
            Func<string> ms = () => string.Format("{0:F0}ms", t0.Elapsed.TotalMilliseconds);
            string name = Shortcut.Name(vk);

            // In window mode the key goes to the mirrored window, even while it is covered.
            var target = window != IntPtr.Zero && Native.IsWindow(window) ? window : Wnd.At(at);
            if (target == IntPtr.Zero)
            {
                Log.Write("key " + name + ": no window under " + at.X + "," + at.Y + " -- dropped");
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            CheckReachable(target);

            IntPtr home = Native.GetForegroundWindow();
            Native.GetCursorPos(out POINT origin);
            warpOrigin = new Point(origin.X, origin.Y);
            try
            {
                // Warp so the client knows which frame the pointer is over. A mouseover bind is
                // only meaningful with the cursor actually there.
                if (!PinTo(at)) { Log.Write("key " + name + " refused"); return; }
                if (viaPost)
                {
                    Pause(Math.Max(StepMs, HoverMs));
                    if (!PinTo(at)) { Log.Write("key " + name + " refused"); return; }   // re-pin; see WarpClick
                    // Posted messages are read in the order they were posted, but all of them
                    // before any mouse input. A client that has not run since the warp would read
                    // the key before the move and act on whatever it hovered last. A posted move
                    // of our own, just ahead of the key, puts the two back in order -- the order
                    // the Mac build gets for free from one event stream.
                    var client = new POINT(at.X, at.Y);
                    Native.ScreenToClient(target, ref client);
                    Native.PostMessage(target, Native.WM_MOUSEMOVE, IntPtr.Zero, Native.MakeLParam(client.X, client.Y));
                    PostKeyWithModifiers(target, vk, mods);
                    Pause(StepMs);
                }
                else
                {
                    Pause(StepMs);
                    // Never guess: if the target did not come forward, the key would land on the
                    // other character. Refuse loudly instead.
                    if (!Wnd.FocusAndWait(target, 250, 2))
                    {
                        Log.Write("key " + name + ": target never came forward at " + ms() + " -- refused");
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }
                    Native.SetCursorPos(at.X, at.Y);           // now active, it re-reads the hover
                    Pause(Math.Max(StepMs, HoverMs));
                    if (!PinTo(at)) { Log.Write("key " + name + " refused"); return; }
                    Send(KeyInput(vk, false));
                    Pause(StepMs);
                    Send(KeyInput(vk, true));
                    Pause(StepMs);
                }
                Log.Write("key " + name + " -> " + Who(target) + (window != IntPtr.Zero ? " (mirrored window)" : "")
                          + (viaPost ? " via post" : " via focus") + " at " + ms());
            }
            finally
            {
                Pause(SettleMs);                               // must not race delivery
                Native.SetCursorPos(origin.X, origin.Y);
                warpOrigin = null;
                if (!viaPost && home != IntPtr.Zero && home != target && !Wnd.IsOurs(home))
                {
                    bool ok = Wnd.FocusAndWait(home, 300, 3);
                    Log.Write("key " + name + ": focus back " + (ok ? "ok" : "FAILED") + " at " + ms());
                    if (!ok) System.Media.SystemSounds.Beep.Play();
                }
            }
        }
    }
}
