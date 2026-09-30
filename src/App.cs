// The actions everything else calls: the menu, the header, the hotkeys and Settings all go
// through here, so each action behaves the same wherever it was triggered from.

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Glass
{
    public static class App
    {
        public static Hotkeys Hotkeys;
        public static Tray Tray;
        public static OverlayForm Overlay;
        public static SettingsForm Settings;
        public static GpuBoost Boost;
        public static readonly Capture Capture = new Capture();

        /// A handle on the UI thread, for work that has to happen there.
        public static Form Ui;

        /// From the command line; the rest of the state lives in Saved.
        public static double Fps = 15;
        public static double StartOpacity = 1.0;
        public static Point? StartAt;

        static Icon icon;

        public static Icon AppIcon
        {
            get
            {
                if (icon != null) return icon;
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Glass.ico"))
                        icon = s != null ? new Icon(s, SystemInformation.SmallIconSize) : SystemIcons.Application;
                }
                catch { icon = SystemIcons.Application; }
                return icon;
            }
        }

        /// Run on the UI thread, after the current message. From the UI thread itself this is
        /// how work escapes an event handler that is still on the stack.
        public static void Defer(Action a)
        {
            var ui = Ui;
            if (ui == null || ui.IsDisposed || !ui.IsHandleCreated) { a(); return; }
            ui.BeginInvoke((Action)(() =>
            {
                try { a(); }
                catch (Exception e) { Log.Write("action failed: " + e); }
            }));
        }

        /// Tell the hook thread the current state of everything it decides on.
        public static void Publish()
        {
            var o = Overlay;
            bool live = o != null && !o.IsDisposed && o.IsHandleCreated;
            Hooks.State = new HookState
            {
                Overlay = live ? o.Bounds : Rectangle.Empty,
                Source = live ? o.SourceRect : Rectangle.Empty,
                Visible = live && o.Visible,
                Locked = Saved.Locked,
                ForwardKeys = Saved.ForwardKeys,
                KeysViaPost = Saved.KeysViaPid,
                Picking = Picker.IsActive,
                Shortcuts = Commands.All.Select(Saved.GetShortcut).ToArray(),
            };
        }

        // MARK: - Actions

        public static void UsePreset(string name)
        {
            var r = Saved.Preset(name);
            if (!r.HasValue) { RecordPreset(name); return; }
            Begin(r.Value, name);
        }

        public static void RecordPreset(string name)
        {
            Picker.Show("Drag the region for “" + name + "”  ·  Esc to cancel", r =>
            {
                Saved.SetPreset(name, r);
                Begin(r, name);
            });
        }

        public static void PickRegion()
        {
            Picker.Show("Drag to choose what to mirror  ·  Esc to cancel", r => Begin(r, null));
        }

        public static void ToggleOverlay()
        {
            var o = Overlay;
            if (o == null || o.IsDisposed) return;
            o.SetShown(!o.Visible);
            Publish();
            Settings?.RefreshAll();
        }

        public static void SetOpacity(double v)
        {
            v = Math.Min(1.0, Math.Max(0.1, v));
            Overlay?.SetOpacity(v);
            Saved.Opacity = v;
            Settings?.RefreshAll();
        }

        public static void ShowSettings(string tab)
        {
            if (Settings == null || Settings.IsDisposed) Settings = new SettingsForm();
            Settings.ShowTab(tab);
        }

        public static void ToggleLock()
        {
            Saved.Locked = !Saved.Locked;
            Overlay?.SetLocked(Saved.Locked);
            Publish();
            Settings?.RefreshAll();
            Log.Write(Saved.Locked ? "locked -- clicks pass through" : "unlocked -- drag to reposition");
        }

        public static void ClearPreset(string name)
        {
            Saved.SetPreset(name, null);
            if (Saved.ActivePreset == name) Saved.ActivePreset = null;
            Overlay?.Bar?.RefreshBar();
            Settings?.RefreshAll();
            Log.Write("cleared preset '" + name + "'");
        }

        /// Cycle hover-reveal -> always shown -> hidden. The hotkey cycles; the menu and Settings
        /// set a mode directly.
        public static void ToggleBar() => SetHeaderMode(Saved.HeaderMode.Next());

        public static void SetHeaderMode(HeaderMode mode)
        {
            Saved.HeaderMode = mode;
            Overlay?.ApplyHeaderMode();
            Settings?.RefreshAll();
            Log.Write("header: " + mode.Label());
        }

        public static void ResetEverything()
        {
            if (!Confirm("Reset Glass?",
                         "Clears all four preset regions, the saved overlay position, and the current selection."))
                return;
            Saved.Reset();
            Overlay?.Bar?.RefreshBar();
            Settings?.RefreshAll();
            Log.Write("reset: all presets and saved position cleared");
            PickRegion();
        }

        /// A question asked from a tray app has no window to sit on and can land behind the
        /// game. Give it a topmost owner, placed on the monitor you are looking at.
        public static bool Confirm(string title, string text)
        {
            using (var owner = new Form
            {
                TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Size = new Size(1, 1), Opacity = 0,
                Location = Cursor.Position,
            })
            {
                owner.Show();
                owner.Activate();
                return MessageBox.Show(owner, text, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                                       MessageBoxDefaultButton.Button2) == DialogResult.OK;
            }
        }

        /// A problem worth interrupting for, from any thread: a tray balloon, since a tray app
        /// has no window of its own to put it in.
        public static void Warn(string title, string text)
        {
            Log.Write("warning: " + title + " -- " + text);
            Defer(() => Tray?.Balloon(title, text, ToolTipIcon.Warning));
        }

        public static void Alert(string title, string text)
        {
            using (var owner = new Form
            {
                TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Size = new Size(1, 1), Opacity = 0,
                Location = Cursor.Position,
            })
            {
                owner.Show();
                owner.Activate();
                MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // MARK: - Mirroring

        /// Start, or switch to, mirroring a global rect.
        public static void Begin(Rectangle region, string preset)
        {
            if (region.Width < 8 || region.Height < 8) { Log.Write("region too small, ignoring"); return; }
            if (Screens.For(region) == null) { Log.Write("no display contains that region"); return; }

            Capture.Stop();
            Saved.Region = region;
            Saved.ActivePreset = preset;

            if (Overlay != null && !Overlay.IsDisposed)
            {
                Overlay.Retarget(region);
            }
            else
            {
                Overlay = new OverlayForm(region, Saved.Scale, StartOpacity, StartAt ?? Saved.OverlayOrigin);
                Overlay.Show();
            }
            Overlay.Bar?.RefreshBar();
            Settings?.RefreshAll();
            Publish();

            Capture.Start(region, Fps);
            var tag = preset != null ? " [" + preset + "]" : "";
            Log.Write("mirroring " + region.Width + "x" + region.Height + " at " + region.X + "," + region.Y + tag);
        }

        static Timer restart;
        static int restartTries;

        /// The source went away -- a display reconfigured, usually. Re-resolve against the saved
        /// rect once a second for up to 30 seconds rather than freezing on the last frame.
        public static void RestartCapture()
        {
            if (restart == null)
            {
                restart = new Timer { Interval = 1000 };
                restart.Tick += (o, e) =>
                {
                    restartTries++;
                    var r = Saved.Region;
                    if (r.HasValue && Screens.For(r.Value) != null)
                    {
                        Begin(r.Value, Saved.ActivePreset);
                        if (Capture.Running) { restart.Stop(); return; }
                    }
                    if (restartTries >= 30) { restart.Stop(); Log.Write("capture: gave up restarting"); }
                };
            }
            restartTries = 0;
            restart.Start();
        }

        public static void DisplaysChanged()
        {
            Log.Write("displays changed: " + string.Join(" | ", Screens.All().Select(d => d.ToString().Trim())));
            Hooks.Reinstall();
            if (Overlay == null || Overlay.IsDisposed) return;
            Overlay.EnsureReachable();
            var r = Saved.Region;
            if (r.HasValue) Begin(r.Value, Saved.ActivePreset);
        }

        public static void Quit()
        {
            Log.Write("quit");
            Capture.Stop();
            Hooks.Stop();
            try { Settings?.Quitting(); Settings?.Close(); } catch { }
            try { Overlay?.Close(); } catch { }
            Tray?.Dispose();
            Saved.FlushNow();
            Application.ExitThread();
        }
    }
}
