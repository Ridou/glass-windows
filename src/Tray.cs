// The tray icon: the Windows counterpart of the macOS menu bar item. Same menu, same order.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Glass
{
    public sealed class Tray : IDisposable
    {
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu = new ContextMenuStrip();

        public Tray()
        {
            // Built fresh every time it opens, so it can never show stale state -- and nothing
            // ever rebuilds a menu out from under its own click handler.
            menu.Opening += (o, e) => { Populate(); e.Cancel = false; };
            menu.Items.Add(new ToolStripMenuItem("Glass"));          // non-empty, or Opening never fires

            icon = new NotifyIcon
            {
                Icon = App.AppIcon,
                Text = "Glass",
                ContextMenuStrip = menu,
                Visible = true,
            };
            // Double-clicking the icon opens Settings: what a Dock icon does on the Mac.
            icon.MouseDoubleClick += (o, e) => { if (e.Button == MouseButtons.Left) App.ShowSettings(null); };
        }

        public void Balloon(string title, string text, ToolTipIcon kind = ToolTipIcon.Info) =>
            icon.ShowBalloonTip(8000, title, text, kind);

        static string Keys(Command c) => Saved.GetShortcut(c).Display;

        static ToolStripMenuItem Item(string title, string shortcut, Action run, bool enabled = true, bool check = false)
        {
            var mi = new ToolStripMenuItem(title) { Enabled = enabled, Checked = check };
            if (!string.IsNullOrEmpty(shortcut)) mi.ShortcutKeyDisplayString = shortcut;
            if (run != null) mi.Click += (o, e) => App.Defer(run);
            return mi;
        }

        void Populate()
        {
            foreach (var old in menu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();
            menu.Items.Clear();
            var items = menu.Items;

            items.Add(Item("Pick Region…", Keys(Command.Pick), App.PickRegion));
            items.Add(new ToolStripSeparator());

            // One submenu per preset, so "use", "re-record" and "clear" are all explicit and
            // none of them is a stray click away from the others.
            for (int i = 0; i < Saved.PresetNames.Length; i++)
            {
                string name = Saved.PresetNames[i];
                var region = Saved.Preset(name);
                var parent = new ToolStripMenuItem(region.HasValue ? name : name + "  (unset)")
                {
                    Checked = Saved.ActivePreset == name,
                };
                if (!region.HasValue) parent.ForeColor = SystemColors.GrayText;

                parent.DropDownItems.Add(Item("Use", Keys(Commands.All[i]), () => App.UsePreset(name), region.HasValue));
                parent.DropDownItems.Add(Item(region.HasValue ? "Re-record Region…" : "Set Region…", null,
                                              () => App.RecordPreset(name)));
                parent.DropDownItems.Add(Item("Clear", null, () => App.ClearPreset(name), region.HasValue));
                if (region.HasValue)
                {
                    var r = region.Value;
                    parent.DropDownItems.Add(new ToolStripSeparator());
                    parent.DropDownItems.Add(Item(r.Width + "×" + r.Height + " at " + r.X + "," + r.Y, null, null, false));
                }
                items.Add(parent);
            }

            items.Add(new ToolStripSeparator());
            items.Add(Item(Saved.Locked ? "Unlock Overlay (drag to move)" : "Lock Overlay", Keys(Command.Lock), App.ToggleLock));
            items.Add(Item(App.Overlay != null && !App.Overlay.Visible ? "Show Overlay" : "Hide Overlay",
                           Keys(Command.Overlay), App.ToggleOverlay));

            var header = new ToolStripMenuItem("Header");
            foreach (var mode in new[] { HeaderMode.Auto, HeaderMode.Pinned, HeaderMode.Hidden })
            {
                var m = mode;
                header.DropDownItems.Add(Item(m.Label(), null, () => App.SetHeaderMode(m), true, Saved.HeaderMode == m));
            }
            items.Add(header);
            items.Add(new ToolStripSeparator());
            items.Add(Item("Settings…", null, () => App.ShowSettings(null)));

            var opacity = new ToolStripMenuItem("Opacity");
            double current = App.Overlay?.Opacity_ ?? (Saved.Opacity ?? 1.0);
            foreach (var v in new[] { 0.4, 0.6, 0.8, 1.0 })
            {
                var value = v;
                opacity.DropDownItems.Add(Item((int)(v * 100) + "%", null, () => App.SetOpacity(value), true,
                                               Math.Abs(current - v) < 0.01));
            }
            items.Add(opacity);

            items.Add(new ToolStripSeparator());
            items.Add(Item("Copy Report for Help", null, () =>
            {
                var said = Report.CopyAndSave();
                Balloon("Glass report", said);
            }));
            items.Add(Item("Open Log", null, OpenLog));
            items.Add(Item("Reinstall Keyboard Hook", null, () => { Hooks.Reinstall(); Log.Write("keyboard hook reinstall requested"); }));
            items.Add(Item("Reset Everything…", null, App.ResetEverything));
            items.Add(Item("Quit Glass", null, App.Quit));
        }

        static void OpenLog()
        {
            try { Process.Start(new ProcessStartInfo(Log.File_) { UseShellExecute = true }); }
            catch (Exception e) { Log.Write("could not open the log: " + e.Message); }
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
        }
    }
}
