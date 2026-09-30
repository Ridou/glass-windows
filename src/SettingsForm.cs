// Everything in one place: presets, overlay size and opacity, shortcuts and WoW settings. The
// tray menu stays for quick switching mid-fight; this is where you set things up.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Glass
{
    public sealed class SettingsForm : Form
    {
        public static readonly string[] TabNames = { "Regions", "Overlay", "Shortcuts", "WoW", "Help" };

        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly TabPage regionsPage = new TabPage("Regions");
        readonly TabPage overlayPage = new TabPage("Overlay") { AutoScroll = true };
        readonly TabPage shortcutsPage = new TabPage("Shortcuts");
        readonly TabPage wowPage = new TabPage("WoW");
        readonly TabPage helpPage = new TabPage("Help");
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000 };

        readonly Dictionary<string, Label> presetLabels = new Dictionary<string, Label>();
        readonly Dictionary<string, Button> presetUse = new Dictionary<string, Button>();
        readonly Dictionary<Command, Button> shortcutButtons = new Dictionary<Command, Button>();
        TrackBar scaleBar, opacityBar;
        Label scaleLabel, opacityLabel, overlayStatus;
        CheckBox lockBox, showBox, keysBox, noFlipBox, postClicksBox, boostBox;
        RadioButton[] headerModes;
        Command? recording;
        bool refreshing;
        bool quitting;

        const int W = 760;         // usable page width at 96 dpi

        static readonly Color Secondary = Color.FromArgb(96, 96, 96);
        static readonly Color Tertiary = Color.FromArgb(128, 128, 128);

        public SettingsForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Glass Settings";
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(800, 660);
            KeyPreview = true;
            Font = new Font("Segoe UI", 9F);

            tabs.TabPages.AddRange(new[] { regionsPage, overlayPage, shortcutsPage, wowPage, helpPage });
            tabs.Selected += (o, e) =>
            {
                if (e.TabPage == wowPage) EnsureWow();
                if (e.TabPage == helpPage) ShowReport();
            };
            Controls.Add(tabs);

            BuildRegions();
            BuildOverlay();
            BuildShortcuts();
            BuildWoWFrame();
            BuildHelp();
            ResumeLayout(false);
            PerformLayout();
        }

        public void Quitting() => quitting = true;

        // MARK: - Showing

        public void ShowTab(string tab)
        {
            if (!string.IsNullOrEmpty(tab))
            {
                var page = tabs.TabPages.Cast<TabPage>()
                    .FirstOrDefault(p => string.Equals(p.Text, tab, StringComparison.OrdinalIgnoreCase));
                if (page != null) tabs.SelectedTab = page;
            }
            RefreshAll();
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            // A tray app's window can open behind a full-screen game. Flick it topmost and back,
            // which brings it forward even when Windows declines to give it focus.
            TopMost = true;
            TopMost = false;
            Activate();
            if (tabs.SelectedTab == wowPage) EnsureWow();
            if (tabs.SelectedTab == helpPage) ShowReport();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopRecording();
            // Closing hides it, like the Mac window; it keeps its tab and scroll position.
            if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            StopRecording();
        }

        // MARK: - Helpers

        int S(int v) => (int)Math.Round(v * DeviceDpi / 96f);

        Label Text_(Control parent, string text, int x, int y, int w = 300, int h = 20, bool bold = false,
                    float size = 9f, Color? color = null)
        {
            var l = new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), AutoSize = false,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color ?? SystemColors.ControlText,
            };
            parent.Controls.Add(l);
            return l;
        }

        Label Hint(Control parent, string text, int x, int y, int w, int h = 32) =>
            Text_(parent, text, x, y, w, h, false, 8f, Tertiary);

        Button Btn(Control parent, string text, int x, int y, int w, Action click, int h = 28)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), UseVisualStyleBackColor = true };
            b.Click += (o, e) => click();
            parent.Controls.Add(b);
            return b;
        }

        /// An On/Off button with its description beside it, in place of a checkbox. A checkbox
        /// that changes something you cannot see does not feel like it did anything; a button
        /// that reads "On" and a line saying what happened does.
        CheckBox Toggle(Control parent, string title, int x, int y, Action<bool> changed)
        {
            var b = new CheckBox
            {
                Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, y), Size = new Size(56, 26), Text = "Off", UseVisualStyleBackColor = true,
            };
            b.CheckedChanged += (o, e) =>
            {
                b.Text = b.Checked ? "On" : "Off";
                if (!refreshing) changed(b.Checked);
            };
            parent.Controls.Add(b);
            Text_(parent, title, x + 66, y + 4, W - x - 70);
            return b;
        }

        void SetToggle(CheckBox b, bool on)
        {
            if (b == null) return;
            b.Checked = on;
            b.Text = on ? "On" : "Off";
        }

        /// The foot of the Overlay tab: what the last toggle actually did.
        void Say(string text)
        {
            if (overlayStatus != null) overlayStatus.Text = text;
            Log.Write(text);
        }

        /// Copied with bare \n line breaks, exactly as the Mac build copies them: that is what WoW
        /// keeps inside a macro, and a \r pasted there could count against its 255 characters.
        static void Copy(string text)
        {
            try { Clipboard.SetDataObject(text, true, 10, 50); }
            catch (Exception e) { Log.Write("clipboard: " + e.Message); SystemSounds.Beep.Play(); }
        }

        // MARK: - Regions tab

        void BuildRegions()
        {
            var v = regionsPage;
            Text_(v, "Record a region for each group size, then switch between them in one click.",
                  16, 14, W - 32, 20, false, 9f, Secondary);

            int y = 48;
            foreach (var name in Saved.PresetNames)
            {
                var n = name;
                Text_(v, n, 16, y, 60, 22, true, 11f);
                presetLabels[n] = Text_(v, "", 16, y + 24, 360, 18, false, 8.5f, Secondary);
                presetLabels[n].Font = new Font("Consolas", 8.5f);
                presetUse[n] = Btn(v, "Use", W - 250, y + 6, 64, () => App.UsePreset(n));
                Btn(v, "Record…", W - 178, y + 6, 96, () => App.RecordPreset(n));
                Btn(v, "Clear", W - 74, y + 6, 72, () => { App.ClearPreset(n); RefreshAll(); });
                y += 58;
            }

            Btn(v, "Pick a One-Off Region…", 16, 560, 200, App.PickRegion, 30);
            Btn(v, "Reset Everything…", W - 172, 560, 170, () => { App.ResetEverything(); RefreshAll(); }, 30);
        }

        // MARK: - Overlay tab

        void BuildOverlay()
        {
            var v = overlayPage;
            int y = 18;

            Text_(v, "Size", 16, y + 4, 70, 22, true);
            // AutoSize goes first: while it is on, setting Size swaps in the TrackBar's own
            // preferred height, and at 150% that height spills over the rows below.
            scaleBar = new TrackBar
            {
                AutoSize = false, Minimum = 50, Maximum = 250, TickStyle = TickStyle.None, SmallChange = 5, LargeChange = 25,
                Location = new Point(90, y), Size = new Size(260, 30),
            };
            scaleBar.ValueChanged += (o, e) =>
            {
                if (refreshing) return;
                double s = scaleBar.Value / 100.0;
                Saved.Scale = s;
                App.Overlay?.SetScale(s);
                scaleLabel.Text = s.ToString("0.00") + "×";
            };
            v.Controls.Add(scaleBar);
            scaleLabel = Text_(v, "", 360, y + 4, 80);
            scaleLabel.Font = new Font("Consolas", 9.5f);
            y += 32;
            Hint(v, "How large the mirror is drawn, relative to the region it captures.", 90, y, W - 110, 20);
            y += 30;

            Text_(v, "Opacity", 16, y + 4, 70, 22, true);
            opacityBar = new TrackBar
            {
                AutoSize = false, Minimum = 20, Maximum = 100, TickStyle = TickStyle.None, SmallChange = 5, LargeChange = 10,
                Location = new Point(90, y), Size = new Size(260, 30),
            };
            opacityBar.ValueChanged += (o, e) =>
            {
                if (refreshing) return;
                App.SetOpacity(opacityBar.Value / 100.0);
                opacityLabel.Text = opacityBar.Value + "%";
            };
            v.Controls.Add(opacityBar);
            opacityLabel = Text_(v, "", 360, y + 4, 80);
            opacityLabel.Font = new Font("Consolas", 9.5f);
            y += 44;

            lockBox = Toggle(v, "Locked — clicks pass through to the source", 16, y, on =>
            {
                if (Saved.Locked != on) App.ToggleLock();
                Say(Saved.Locked
                    ? "Locked — a click on the overlay lands on the real frame underneath it."
                    : "Unlocked — the overlay turned orange and a drag moves it. Nothing clicks through.");
            });
            y += 30;
            Hint(v, "Unlocked, the overlay turns orange and a plain drag moves it, with nothing clicking through.",
                 82, y, W - 100, 20);
            y += 30;

            showBox = Toggle(v, "Show overlay", 16, y, on =>
            {
                var o = App.Overlay;
                if (o != null && o.Visible != on) App.ToggleOverlay();
                o = App.Overlay;
                if (o != null && o.Visible)
                    Say("Overlay shown — " + o.Width + "×" + o.Height + " at " + o.Left + "," + o.Top + ".");
                else
                    Say("Overlay hidden. Capture is still running, so it comes back instantly.");
            });
            y += 40;

            Text_(v, "Header", 16, y + 4, 70, 22, true);
            headerModes = new RadioButton[3];
            var labels = new[] { "On hover", "Always", "Hidden" };
            var modes = new[] { HeaderMode.Auto, HeaderMode.Pinned, HeaderMode.Hidden };
            for (int i = 0; i < 3; i++)
            {
                var mode = modes[i];
                var r = new RadioButton
                {
                    Text = labels[i], Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(90 + i * 96, y), Size = new Size(94, 28), UseVisualStyleBackColor = true,
                };
                r.CheckedChanged += (o, e) => { if (!refreshing && r.Checked) App.SetHeaderMode(mode); };
                v.Controls.Add(r);
                headerModes[i] = r;
            }
            y += 32;
            Hint(v, "The header floats above the overlay, never on it — buttons never sit where a missed click would land.",
                 90, y, W - 110, 20);
            y += 34;

            keysBox = Toggle(v, "Number keys act on the hovered frame (1–0, -, =)", 16, y, on =>
            {
                Saved.ForwardKeys = on;
                App.Publish();
                Say(on ? "Number keys pressed over the overlay now act on the frame you are hovering."
                       : "Number keys always reach the game directly now, even over the overlay.");
            });
            y += 30;
            Hint(v, "For Clique mouseover binds and MMO-mouse grids. Only while the pointer is over the locked overlay; "
                    + "everywhere else they reach the game.", 82, y, W - 100);
            y += 40;

            noFlipBox = Toggle(v, "Send keys without switching focus", 48, y, on =>
            {
                Saved.KeysViaPid = on;
                App.Publish();
                Say(on ? "Keys go straight to the hovered client's window — the one you are playing keeps focus the whole time."
                       : "Glass will focus the hovered client for about 150 ms to deliver a key, then hand focus back.");
            });
            y += 30;
            Hint(v, "The character you are playing keeps focus throughout. Turn off only if a mouseover bind stops landing "
                    + "on the frame you hover.", 114, y, W - 132);
            y += 40;

            postClicksBox = Toggle(v, "Send clicks without moving the cursor", 48, y, on =>
            {
                Saved.ClicksViaPid = on;
                Say(on ? "Clicks are posted straight to the game window; the pointer stays where it is."
                       : "Clicks warp the pointer to the frame and straight back — the path known to work.");
            });
            y += 30;
            Hint(v, "Windows only. Posts each click to the window instead of warping the pointer there and back. Try it if "
                    + "the warp nudges your camera; turn it off again if clicks stop landing.", 114, y, W - 132);
            y += 40;

            boostBox = Toggle(v, "GPU boost — keep a sliver of window above the game", 16, y, on =>
            {
                Saved.GpuBoost = on;
                App.Boost?.Apply();
                Say(on ? "GPU boost on — a 32-pixel window now sits above the game and is nudged 30 times a second."
                       : "GPU boost off — that window is gone.");
            });
            y += 30;
            Hint(v, "A macOS fix: macOS parks the GPU while a game is alone on screen, and a window above it clocks it back "
                    + "up. Windows does not do this, so leave it off unless you are comparing.", 82, y, W - 100);
            y += 44;

            overlayStatus = Text_(v, "", 16, y, W - 32, 36, false, 8.5f, Secondary);
        }

        // MARK: - Shortcuts tab

        void BuildShortcuts()
        {
            var v = shortcutsPage;
            Text_(v, "These work anywhere, including in-game. Click one, then press new keys.",
                  16, 14, W - 32, 20, false, 9f, Secondary);

            int y = 50;
            foreach (var command in Commands.All)
            {
                var c = command;
                Text_(v, Commands.Label(c), 16, y + 5, 400, 22);
                var b = new Button
                {
                    Location = new Point(W - 190, y), Size = new Size(188, 30),
                    Font = new Font("Consolas", 9.5f), UseVisualStyleBackColor = true,
                };
                b.Click += (o, e) => StartRecording(c);
                v.Controls.Add(b);
                shortcutButtons[c] = b;
                y += 40;
            }

            Btn(v, "Restore Defaults", 16, 560, 150, RestoreDefaults, 30);
            var hint = Hint(v, "Esc cancels · a shortcut must include a modifier", W - 330, 566, 328, 20);
            hint.TextAlign = ContentAlignment.MiddleRight;
        }

        void RestoreDefaults()
        {
            StopRecording();
            foreach (var c in Commands.All) Saved.ResetShortcut(c);
            App.Hotkeys.Reload();
            App.Publish();
            RefreshAll();
        }

        void StartRecording(Command c)
        {
            StopRecording();
            recording = c;
            App.Hotkeys.Suspend();
            shortcutButtons[c].Text = "Press keys…";
        }

        void StopRecording()
        {
            if (!recording.HasValue) return;
            recording = null;
            App.Hotkeys?.Resume();
            RefreshAll();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!recording.HasValue) return base.ProcessCmdKey(ref msg, keyData);

            var key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { StopRecording(); return true; }          // Esc cancels
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin
                || key == Keys.RWin || key == Keys.LShiftKey || key == Keys.RShiftKey || key == Keys.LControlKey
                || key == Keys.RControlKey || key == Keys.LMenu || key == Keys.RMenu)
                return true;                                                     // wait for the real key

            uint mods = Shortcut.ModsFrom(keyData & Keys.Modifiers);
            // A global hotkey with no modifier would swallow that key everywhere, including
            // in-game. Refuse rather than break the keyboard.
            if (mods == 0) { SystemSounds.Beep.Play(); return true; }

            var command = recording.Value;
            var shortcut = new Shortcut((int)key, mods);
            foreach (var other in Commands.All)
            {
                if (other != command && Saved.GetShortcut(other).Equals(shortcut))
                {
                    SystemSounds.Beep.Play();
                    Log.Write(shortcut.Display + " is already used by: " + Commands.Label(other));
                    StopRecording();
                    return true;
                }
            }
            Saved.SetShortcut(command, shortcut);
            StopRecording();                 // resumes the hotkeys, now with the new binding
            App.Publish();
            return true;
        }

        // MARK: - WoW tab

        Panel wowList;
        Label wowBanner;
        RadioButton[] wowRoleButtons;
        /// null = the Everyone tab; otherwise the role being edited.
        WoWRole? wowTab;
        bool wowBuilt;
        readonly Dictionary<string, CheckBox> wowBoxes = new Dictionary<string, CheckBox>();
        readonly Dictionary<string, Label> wowCommands = new Dictionary<string, Label>();

        /// Which settings the selected role shows.
        List<WoWSetting> WowVisible => wowTab.HasValue ? Wow.For(wowTab.Value) : Wow.Shared;

        void BuildWoWFrame()
        {
            var v = wowPage;
            var names = new[] { "Everyone" }.Concat(WoWRoles.All.Select(WoWRoles.Title)).ToArray();
            wowRoleButtons = new RadioButton[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var r = new RadioButton
                {
                    Text = names[i], Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(16 + i * 124, 12), Size = new Size(120, 28), UseVisualStyleBackColor = true,
                    Checked = i == 0,
                };
                r.CheckedChanged += (o, e) =>
                {
                    if (!r.Checked) return;
                    wowTab = index == 0 ? (WoWRole?)null : WoWRoles.All[index - 1];
                    if (wowBuilt) BuildWowList();
                };
                v.Controls.Add(r);
                wowRoleButtons[i] = r;
            }

            wowBanner = Text_(v, "Tick what you want, then copy. Glass does not touch the game — paste these into chat "
                                 + "yourself, one line at a time, or into a macro (macros take several lines).",
                              16, 48, W - 32, 36, false, 8.5f, Secondary);

            // Not anchored: the page is still at its default 200×100 when this is added, so an
            // anchor would grow the list by the whole difference and push it over the buttons.
            // The window is a fixed size anyway.
            wowList = new Panel { Location = new Point(0, 90), Size = new Size(W + 24, 460), AutoScroll = true };
            v.Controls.Add(wowList);

            var all = Btn(v, "Copy Ticked", 16, 560, 130, WowCopyTab, 30);
            tips.SetToolTip(all, "Every ticked command on this tab, newline separated.");
            var macro = Btn(v, "Copy as Macro", 156, 560, 150, WowCopyMacro, 30);
            tips.SetToolTip(macro, "The same commands split into 255-character macro bodies.");
        }

        /// Built on first view rather than in the constructor: the rows are sized in real pixels,
        /// so they wait until the window knows which monitor, and which scale, it is on.
        public void EnsureWow()
        {
            if (wowBuilt) return;
            wowBuilt = true;
            BuildWowList();
        }

        public void SelectWowRole(WoWRole? role)
        {
            int i = role.HasValue ? Array.IndexOf(WoWRoles.All, role.Value) + 1 : 0;
            wowRoleButtons[i].Checked = true;
            wowTab = role;
            if (wowBuilt) BuildWowList();
        }

        void BuildWowList()
        {
            wowList.SuspendLayout();
            foreach (var c in wowList.Controls.Cast<Control>().ToList()) c.Dispose();
            wowList.Controls.Clear();
            wowBoxes.Clear();
            wowCommands.Clear();

            var table = new TableLayoutPanel
            {
                ColumnCount = 3, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(12), 0, S(12), S(8)),
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.SuspendLayout();

            int detailWidth = Math.Max(S(200), (int)((wowList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth) * 0.58) - S(30));
            var mono = new Font("Consolas", 8f);
            var small = new Font("Segoe UI", 8f);
            var bold = new Font("Segoe UI", 8.5f, FontStyle.Bold);

            string lastGroup = null;
            int row = 0;
            foreach (var s in WowVisible)
            {
                if (s.Group != lastGroup)
                {
                    lastGroup = s.Group;
                    var h = new Label
                    {
                        Text = s.Group, AutoSize = true, Font = bold, ForeColor = Secondary,
                        Margin = new Padding(0, row == 0 ? S(4) : S(14), 0, S(2)),
                    };
                    table.Controls.Add(h, 0, row);
                    table.SetColumnSpan(h, 3);
                    row++;
                }

                var setting = s;
                var commands = Wow.CommandsFor(s);
                var cell = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, S(2), 0, S(2)),
                };
                var box = new CheckBox { Text = s.Title, AutoSize = true, Checked = Saved.WowSetting(s), Margin = new Padding(0) };
                tips.SetToolTip(box, s.Detail);
                box.CheckedChanged += (o, e) =>
                {
                    if (refreshing) return;
                    Saved.SetWowSetting(setting.Key, box.Checked);
                    UpdateWowRow(setting);
                    Task.Run(() => Wow.SyncAddonConfig());
                };
                cell.Controls.Add(box);
                if (!string.IsNullOrEmpty(s.Detail))
                {
                    cell.Controls.Add(new Label
                    {
                        Text = s.Detail, AutoSize = true, Font = small, ForeColor = Tertiary,
                        MaximumSize = new Size(detailWidth, 0), Margin = new Padding(S(18), 0, 0, S(2)),
                    });
                }
                table.Controls.Add(cell, 0, row);

                // The commands themselves, since Glass never asks the client anything.
                var cmd = new OneLineLabel
                {
                    Text = commands.FirstOrDefault() ?? "", AutoSize = false, Font = mono,
                    ForeColor = Tertiary, Dock = DockStyle.Fill, Margin = new Padding(S(8), S(5), S(4), 0),
                };
                tips.SetToolTip(cmd, string.Join("\n", commands));
                table.Controls.Add(cmd, 1, row);

                var copy = new Button
                {
                    Text = "Copy", Size = new Size(S(60), S(26)), Font = small, UseVisualStyleBackColor = true,
                    Margin = new Padding(0, S(1), 0, 0),
                };
                tips.SetToolTip(copy, string.Join("\n", commands));
                copy.Click += (o, e) => WowCopyOne(setting);
                table.Controls.Add(copy, 2, row);

                wowBoxes[s.Key] = box;
                wowCommands[s.Key] = cmd;
                row++;
            }
            table.RowCount = row;
            table.ResumeLayout(false);
            wowList.Controls.Add(table);
            wowList.ResumeLayout(true);
            wowList.AutoScrollPosition = new Point(0, 0);
        }

        void UpdateWowRow(WoWSetting s)
        {
            if (!wowCommands.TryGetValue(s.Key, out var label)) return;
            var lines = Wow.CommandsFor(s);
            label.Text = lines.FirstOrDefault() ?? "";
            tips.SetToolTip(label, string.Join("\n", lines));
        }

        void WowSay(string text) { if (wowBanner != null) wowBanner.Text = text; }

        void WowCopyOne(WoWSetting s)
        {
            var lines = Wow.CommandsFor(s);
            Copy(string.Join("\n", lines));
            WowSay(lines.Count == 1 ? "Copied: " + lines[0]
                                    : "Copied " + lines.Count + " lines — paste them one at a time.");
        }

        void WowCopyTab()
        {
            var lines = Wow.ConsoleCommands(WowVisible);
            Copy(string.Join("\n", lines));
            WowSay("Copied " + lines.Count + " commands. Chat takes one line at a time; a macro takes several.");
        }

        void WowCopyMacro()
        {
            var chunks = Wow.MacroChunks(Wow.ConsoleCommands(WowVisible));
            Copy(string.Join("\n\n---\n\n", chunks));
            WowSay("Copied " + chunks.Count + " macro" + (chunks.Count == 1 ? "" : "s") + ", separated by ---. "
                   + "Each fits WoW's 255-character macro limit.");
        }

        // MARK: - Help tab

        TextBox reportBox;
        Label helpStatus;

        /// One button that gathers everything needed to diagnose a problem from afar, shown here
        /// in full so the person sending it can read what they are sending.
        void BuildHelp()
        {
            var v = helpPage;
            Text_(v, "Something not working? Send a report.", 16, 12, W - 32, 22, true, 10.5f);
            Text_(v, "1. Make the problem happen (or right after it did).\n"
                     + "2. Click Copy Report.\n"
                     + "3. Paste it (Ctrl+V) into Discord or an email to whoever is helping you. Discord turns a long "
                     + "paste into a file called message.txt -- that's fine, just send it.",
                  16, 38, W - 32, 64, false, 9f, Secondary);

            Btn(v, "Copy Report", 16, 108, 150, () =>
            {
                helpStatus.Text = Report.CopyAndSave();
                ShowReport();
            }, 32);
            Btn(v, "Show Report File", 176, 108, 150, Report.ShowFile, 32);
            Btn(v, "Open Log Folder", 336, 108, 150, () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.Dir) { UseShellExecute = true }); }
                catch (Exception e) { Log.Write("open log folder: " + e.Message); }
            }, 32);
            helpStatus = Text_(v, "The report holds Glass's settings, your screen layout, the WoW windows it can see and the "
                                  + "recent log. Nothing you type in the game is ever recorded.",
                               16, 148, W - 32, 34, false, 8.5f, Tertiary);

            reportBox = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 8.5f), Location = new Point(16, 186), Size = new Size(W - 16, 420),
                BackColor = SystemColors.Window,
            };
            v.Controls.Add(reportBox);
        }

        /// What Copy Report would send right now, readable before it is sent.
        void ShowReport()
        {
            if (reportBox == null) return;
            try { reportBox.Text = Report.Build(); }
            catch (Exception e) { reportBox.Text = "Could not build the report: " + e.Message; }
            reportBox.Select(0, 0);
        }

        // MARK: - Refresh

        public void RefreshAll()
        {
            if (IsDisposed) return;
            refreshing = true;
            try
            {
                foreach (var name in Saved.PresetNames)
                {
                    var r = Saved.Preset(name);
                    if (r.HasValue)
                    {
                        var active = Saved.ActivePreset == name ? "  ← in use" : "";
                        presetLabels[name].Text = r.Value.Width + "×" + r.Value.Height + " at " + r.Value.X + "," + r.Value.Y + active;
                        presetUse[name].Enabled = true;
                    }
                    else
                    {
                        presetLabels[name].Text = "not recorded";
                        presetUse[name].Enabled = false;
                    }
                }
                foreach (var kv in shortcutButtons)
                    if (recording != kv.Key) kv.Value.Text = Saved.GetShortcut(kv.Key).Display;

                int scale = (int)Math.Round(Math.Min(2.5, Math.Max(0.5, Saved.Scale)) * 100);
                scaleBar.Value = scale;
                scaleLabel.Text = (scale / 100.0).ToString("0.00") + "×";
                int opacity = (int)Math.Round(Math.Min(1.0, Math.Max(0.2, App.Overlay?.Opacity_ ?? Saved.Opacity ?? 1.0)) * 100);
                opacityBar.Value = opacity;
                opacityLabel.Text = opacity + "%";

                SetToggle(lockBox, Saved.Locked);
                SetToggle(showBox, App.Overlay != null && App.Overlay.Visible);
                SetToggle(keysBox, Saved.ForwardKeys);
                SetToggle(noFlipBox, Saved.KeysViaPid);
                SetToggle(postClicksBox, Saved.ClicksViaPid);
                SetToggle(boostBox, Saved.GpuBoost);
                var modes = new[] { HeaderMode.Auto, HeaderMode.Pinned, HeaderMode.Hidden };
                for (int i = 0; i < 3; i++) headerModes[i].Checked = modes[i] == Saved.HeaderMode;

                foreach (var s in WowVisible)
                {
                    if (wowBoxes.TryGetValue(s.Key, out var box)) box.Checked = Saved.WowSetting(s);
                    UpdateWowRow(s);
                }
            }
            finally { refreshing = false; }
        }

        /// One line cut short with an ellipsis, as the Mac truncates it. A plain Label breaks at a
        /// space first, which leaves a lone "/run" with the command itself out of sight.
        sealed class OneLineLabel : Label
        {
            protected override void OnPaint(PaintEventArgs e) =>
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                                      TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// For the self-test: every tab as it would appear.
        public IEnumerable<(string name, Bitmap image)> Snapshots()
        {
            RefreshAll();
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                if (page == wowPage) EnsureWow();
                if (page == helpPage) ShowReport();
                var roles = page == wowPage ? new WoWRole?[] { null, WoWRole.Tank, WoWRole.Healer, WoWRole.DPS } : new WoWRole?[] { null };
                foreach (var role in roles)
                {
                    if (page == wowPage) SelectWowRole(role);
                    yield return (page.Text + (role.HasValue ? "-" + WoWRoles.Title(role.Value) : ""), SelfTest.Render(this));
                }
            }
        }
    }
}
