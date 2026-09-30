// GPU boost: keep a sliver of a window composited above the game.
//
// On macOS this stops the system parking the GPU while a game is the only thing on screen
// (35 -> 85 fps measured on an M3 Pro). Windows has no such parking, so here it is off by
// default and kept only so both builds offer the same switches. If anything it can cost a
// little: a window over a full-screen game can stop Windows using independent flip for it.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glass
{
    public sealed class GpuBoost
    {
        BoostWindow window;
        Timer timer;
        int tick;

        /// Match the window to the saved setting, whichever way it changed.
        public void Apply() { if (Saved.GpuBoost) Start(); else Stop(); }

        void Start()
        {
            if (window != null) return;
            var d = Screens.Primary();
            if (d == null) return;
            int side = (int)Math.Round(32 * d.Scale);
            int inset = (int)Math.Round(8 * d.Scale);
            window = new BoostWindow { Bounds = new Rectangle(d.Bounds.Right - side - inset, d.Bounds.Bottom - side - inset, side, side) };
            window.Show();

            // A window that never changes can stop counting as compositing work, so nudge its
            // alpha every frame: 3% and 4% white, invisible, and cheaper than a redraw.
            timer = new Timer { Interval = 33 };
            timer.Tick += (o, e) =>
            {
                if (window == null) return;
                tick++;
                window.SetAlpha(tick % 2 == 0 ? (byte)8 : (byte)10);
                // A game going full screen can bury it; once a second is enough to come back.
                if (tick % 30 == 0) window.KeepOnTop();
            };
            timer.Start();
            Log.Write("gpu boost on");
        }

        void Stop()
        {
            if (window == null) return;
            timer?.Stop(); timer?.Dispose(); timer = null;
            window.Close(); window.Dispose(); window = null;
            Log.Write("gpu boost off");
        }

        sealed class BoostWindow : Form
        {
            public BoostWindow()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                TopMost = true;
                BackColor = Color.White;
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style |= Native.WS_POPUP;
                    // Transparent to the mouse as well as nearly to the eye.
                    cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE
                                | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation => true;

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SetAlpha(8);
                Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
                base.WndProc(ref m);
            }

            public void SetAlpha(byte a)
            {
                if (IsHandleCreated) Native.SetLayeredWindowAttributes(Handle, 0, a, Native.LWA_ALPHA);
            }

            public void KeepOnTop()
            {
                if (IsHandleCreated)
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }
    }
}
