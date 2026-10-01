// Everything in one place: presets, overlay size and opacity, shortcuts and game settings. The
// tray menu stays for quick switching mid-fight; this is where you set things up.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Glass
{
    public sealed class SettingsForm : Form, IChrome
    {
        public static readonly string[] TabNames = { "Regions", "Overlay", "Shortcuts", "Game", "Help" };

        // Pages live inside the frame's inset and are shown one at a time; the icon tabs down
        // the right edge choose between them, in place of a tab strip.
        readonly ThemePanel regionsPage = new ThemePanel { Name = "Regions" };
        readonly ThemePanel overlayPage = new ThemePanel { Name = "Overlay", AutoScroll = true };
        readonly ThemePanel shortcutsPage = new ThemePanel { Name = "Shortcuts" };
        readonly ThemePanel gamePage = new ThemePanel { Name = "Game" };
        readonly ThemePanel helpPage = new ThemePanel { Name = "Help" };
        ThemePanel[] pages;
        readonly Dictionary<string, ThemeSideTab> sideTabs = new Dictionary<string, ThemeSideTab>();
        ThemePanel current;
        readonly ThemeTip tips = new ThemeTip();

        static readonly (string page, MacroIcon icon, string tip)[] TabIcons =
        {
            ("Regions",   MacroIcon.Spyglass, "Where the mirror looks, one region per group size"),
            ("Overlay",   MacroIcon.Orb,      "Size, opacity, the header, and keys"),
            ("Shortcuts", MacroIcon.Key,      "Hotkeys that work anywhere, in-game too"),
            ("Game",      MacroIcon.Scroll,   "Console commands to paste into the game"),
            ("Help",      MacroIcon.Tome,     "Reports, the log, and the version you have"),
        };

        // Frame geometry. The page is the usable area inside the inset.
        const int Overhang = 8;            // room for the portrait to overhang the frame
        const int TabW = 48;
        const int TitleH = 24;
        const int HeaderH = 38;
        const int PageW = W;
        const int PageH = 600;
        Rectangle FrameRect => new Rectangle(Overhang, 0, PageW + 20, PageH + 18 + TitleH + HeaderH);
        Rectangle InsetRect => new Rectangle(FrameRect.X + 10, FrameRect.Y + TitleH + HeaderH + 8, PageW, PageH);
        Rectangle TitleBar => new Rectangle(FrameRect.X + 7, FrameRect.Y + 7, FrameRect.Width - 14, TitleH);

        readonly Dictionary<string, ThemeLabel> presetLabels = new Dictionary<string, ThemeLabel>();
        readonly Dictionary<string, ThemeButton> presetUse = new Dictionary<string, ThemeButton>();
        readonly Dictionary<Command, ThemeButton> shortcutButtons = new Dictionary<Command, ThemeButton>();
        ThemeSlider scaleBar, opacityBar;
        ThemeLabel scaleLabel, opacityLabel, overlayStatus;
        ThemeToggle lockBox, showBox, keysBox, noFlipBox, postClicksBox, boostBox;
        ThemePlate[] headerModes, mirrorModes, hiddenClickModes;
        ThemeLabel mirrorStatus;
        Command? recording;
        bool refreshing;
        bool quitting;

        const int W = 760;         // usable page width at 96 dpi


        public SettingsForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Glass Settings";
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Overhang + PageW + 20 + TabW + 2, FrameRect.Height + 2);
            KeyPreview = true;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);

            pages = new[] { regionsPage, overlayPage, shortcutsPage, gamePage, helpPage };
            foreach (var p in pages)
            {
                p.Bounds = InsetRect;
                p.Visible = false;
                Controls.Add(p);
            }

            var close = new ThemeClose { Location = new Point(TitleBar.Right - 34, TitleBar.Y + 2) };
            close.Click += (o, e) => Close();
            Controls.Add(close);

            for (int i = 0; i < TabIcons.Length; i++)
            {
                var t = TabIcons[i];
                var tab = new ThemeSideTab
                {
                    Icon = t.icon,
                    Location = new Point(FrameRect.Right, InsetRect.Y + 2 + i * 54),
                };
                var name = t.page;
                tab.Click += (o, e) => Select(name);
                tips.Set(tab, name, t.tip);
                Controls.Add(tab);
                sideTabs[name] = tab;
            }

            BuildRegions();
            BuildOverlay();
            BuildShortcuts();
            BuildGameFrame();
            BuildHelp();
            Select("Regions");
            ResumeLayout(false);
            PerformLayout();
        }

        /// Show one page and light its tab.
        void Select(string name)
        {
            var page = pages.FirstOrDefault(p => p.Name == name);
            if (page == null) return;
            if (current != null) current.Visible = false;
            current = page;
            page.Visible = true;
            page.BringToFront();
            foreach (var kv in sideTabs) kv.Value.Selected = kv.Key == name;
            if (page == gamePage) EnsureGame();
            if (page == helpPage) ShowReport();
            tips.Hide();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) => PaintChrome(e.Graphics);

        /// The window's own chrome: stone, the title bar, the frame and the portrait.
        public void PaintChrome(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var frame = FrameRect;
            // The whole window is stone, not only the frame. The side tabs reach part way
            // down and the margin the portrait overhangs into is empty; left black, those
            // read as a slab beside the frame on any desktop that is not already black.
            Theme.Fill(g, Theme.Stone, ClientRectangle, Point.Empty);
            using (var shade = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
                g.FillRectangle(shade, ClientRectangle);

            var state = g.Save();
            using (var clip = Theme.Round(RectangleF.Inflate(frame, -4, -4), 3)) g.SetClip(clip);
            Theme.Fill(g, Theme.Stone, frame, Point.Empty);
            g.Restore(state);

            var bar = TitleBar;
            Theme.FillDown(g, bar, Theme.Rgb(0.20, 0.19, 0.17), Theme.Rgb(0.09, 0.085, 0.08));
            using (var b = new SolidBrush(Theme.Bronze))
                g.FillRectangle(b, new RectangleF(bar.X, bar.Bottom - 1, bar.Width, 1));
            using (var b = new SolidBrush(Color.Black))
                g.FillRectangle(b, new RectangleF(bar.X, bar.Bottom, bar.Width, 1));
            Theme.DrawText(g, "Glass", Theme.F(11), Theme.Gold, bar, StringAlignment.Center);

            Theme.DrawInset(g, InsetRect, Point.Empty);
            Theme.DrawFrame(g, frame);

            var title = current == null ? "" : current.Name == "Game" ? "Game settings" : current.Name;
            Theme.DrawText(g, title, Theme.F(16), Theme.Gold,
                           new RectangleF(frame.X + 72, bar.Bottom + 1, frame.Width - 90, HeaderH));
            ThemeArt.DrawPortrait(g, new PointF(frame.X + 26, frame.Y + 26), 34);
        }

        // Borderless, so dragging the chrome moves the window the way a title bar would.
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        public void Quitting() => quitting = true;

        // MARK: - Showing

        public void ShowTab(string tab)
        {
            if (!string.IsNullOrEmpty(tab))
            {
                var page = pages.FirstOrDefault(p => string.Equals(p.Name, tab, StringComparison.OrdinalIgnoreCase));
                if (page != null) Select(page.Name);
            }
            RefreshAll();
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            // A tray app's window can open behind a full-screen game. Flick it topmost and back,
            // which brings it forward even when Windows declines to give it focus.
            TopMost = true;
            TopMost = false;
            Activate();
            if (current == gamePage) EnsureGame();
            if (current == helpPage) ShowReport();
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

        ThemeLabel Text_(Control parent, string text, int x, int y, int w = 300, int h = 20, bool bold = false,
                    float size = 9f, Color? color = null)
        {
            // `size` and `bold` are the old Segoe scale; the serif needs a little more body
            // and says "important" with colour rather than weight.
            var l = new ThemeLabel
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                TextFont = Theme.F(size + 1.5f),
                Tint = color ?? (bold ? Theme.Gold : Theme.White),
                Wrap = h > 24,
            };
            parent.Controls.Add(l);
            return l;
        }

        ThemeLabel Hint(Control parent, string text, int x, int y, int w, int h = 32) =>
            Text_(parent, text, x, y, w, h, false, 8f, Theme.Hint);

        /// Grey unless asked otherwise: red is for the one action a page is really about,
        /// and a page of red buttons says nothing about which of them matters.
        ThemeButton Btn(Control parent, string text, int x, int y, int w, Action click, int h = 28,
                        bool primary = false)
        {
            var b = new ThemeButton { Text = text, Location = new Point(x, y), Size = new Size(w, h), Style = ThemeButton.Kind.Grey };
            if (primary) b.Style = ThemeButton.Kind.Red;
            b.Click += (o, e) => click();
            parent.Controls.Add(b);
            return b;
        }

        /// An On/Off button with its description beside it, in place of a checkbox. A checkbox
        /// that changes something you cannot see does not feel like it did anything; a button
        /// that reads "On" and a line saying what happened does.
        ThemeToggle Toggle(Control parent, string title, int x, int y, Action<bool> changed)
        {
            var b = new ThemeToggle { Location = new Point(x, y), Size = new Size(56, 26), Text = "Off" };
            b.CheckedChanged += (o, e) =>
            {
                b.Text = b.Checked ? "On" : "Off";
                b.On = b.Checked;
                if (!refreshing) changed(b.Checked);
            };
            parent.Controls.Add(b);
            Text_(parent, title, x + 66, y + 4, W - x - 70);
            return b;
        }

        void SetToggle(ThemeToggle b, bool on)
        {
            if (b == null) return;
            b.Checked = on;
            b.On = on;
            b.Text = on ? "On" : "Off";
        }

        /// The foot of the Overlay tab: what the last toggle actually did.
        void Say(string text)
        {
            if (overlayStatus != null) overlayStatus.Text = text;
            Log.Write(text);
        }

        /// Copied with bare \n line breaks, exactly as the Mac build copies them: that is what the game
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
                  16, 14, W - 32, 20, false, 9f, Theme.Body);

            int y = 48, index = 0;
            foreach (var name in Saved.PresetNames)
            {
                var n = name;
                // Every other row stands on a tinted band, as the game's own lists do. The
                // band is a container, so the row's controls are placed inside it and their
                // x and y come back by the band's own offset.
                Control row = v;
                int ox = 0, oy = y;
                if (index++ % 2 == 0)
                {
                    var band = new ThemeBand { Location = new Point(8, y - 6), Size = new Size(W - 16, 52) };
                    v.Controls.Add(band);
                    row = band; ox = 8; oy = 6;
                }
                Text_(row, n, 16 - ox, oy, 60, 22, true, 11f);
                presetLabels[n] = Text_(row, "", 16 - ox, oy + 24, 360, 18, false, 8.5f, Theme.Hint);
                presetLabels[n].TextFont = Theme.Narrow(10f);
                presetUse[n] = Btn(row, "Use", W - 250 - ox, oy + 6, 64, () => App.UsePreset(n));
                Btn(row, "Record…", W - 178 - ox, oy + 6, 96, () => App.RecordPreset(n), primary: true);
                Btn(row, "Clear", W - 74 - ox, oy + 6, 72, () => { App.ClearPreset(n); RefreshAll(); });
                y += 58;
            }

            // One monitor: mirror a game window, which keeps showing while another covers it.
            int m = 300;
            Text_(v, "What the mirror shows", 16, m, 300, 22, true, 9.5f);
            mirrorModes = Choice(v, 16, m + 26, 186, new[] { "The game window", "The screen" }, i =>
            {
                Saved.MirrorMode = new[] { "window", "screen" }[i];
                var r = Saved.Region;
                if (r.HasValue && App.Overlay != null) App.Begin(r.Value, Saved.ActivePreset);
                RefreshAll();
            },
            new (PlateGlyph, Color)?[]
            {
                (PlateGlyph.Window, Theme.Rgb(0.20, 0.46, 0.30)),
                (PlateGlyph.Display, Theme.Rgb(0.16, 0.36, 0.58)),
            });
            Hint(v, "The game window keeps showing even while another window covers it: two clients on one monitor, "
                    + "switching with Alt+Tab. Pick the region while that client is in front. The screen shows "
                    + "whatever is at that spot, such as a client on another monitor.", 16, m + 60, W - 32, 32);
            mirrorStatus = Text_(v, "", 16, m + 96, W - 32, 18, false, 8.5f, Theme.Yellow);

            int c = m + 128;
            Text_(v, "Clicks on a covered window", 16, c, 300, 22, true, 9.5f);
            hiddenClickModes = Choice(v, 16, c + 26, 160, new[] { "Send directly", "Bring it forward" }, i =>
            {
                Saved.HiddenClicks = i == 0 ? "post" : "front";
                RefreshAll();
            });
            Hint(v, "Send directly never changes which window is in front. If clicks on the mirror do nothing in the "
                    + "game, try Bring it forward: it always works, but that window flashes up for a moment.",
                 16, c + 60, W - 32, 32);

            Btn(v, "Pick a One-Off Region…", 16, 560, 200, App.PickRegion, 30, primary: true);
            Btn(v, "Reset Everything…", W - 172, 560, 170, () => { App.ResetEverything(); RefreshAll(); }, 30);
        }

        /// A row of toggle-style radio buttons; `picked` gets the index clicked.
        /// Each row sits in a panel of its own: radio buttons sharing a container are one group,
        /// and choosing in one row would clear the other.
        ThemePlate[] Choice(Control parent, int x, int y, int w, string[] labels, Action<int> picked,
                            (PlateGlyph glyph, Color hue)?[] icons = null)
        {
            var row = new ThemePanel { Location = new Point(x, y), Size = new Size(labels.Length * (w + 4), 28) };
            parent.Controls.Add(row);
            var buttons = new ThemePlate[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var r = new ThemePlate
                {
                    Text = labels[i], Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(i * (w + 4), 0), Size = new Size(w, 28), UseVisualStyleBackColor = true,
                };
                if (icons != null && i < icons.Length && icons[i] != null)
                {
                    r.Icon = icons[i].Value.glyph;
                    r.IconHue = icons[i].Value.hue;
                }
                r.CheckedChanged += (o, e) => { if (!refreshing && r.Checked) picked(index); };
                row.Controls.Add(r);
                buttons[i] = r;
            }
            return buttons;
        }

        // MARK: - Overlay tab

        void BuildOverlay()
        {
            var v = overlayPage;
            int y = 18;

            Text_(v, "Size", 16, y + 4, 70, 22, true);
            // AutoSize goes first: while it is on, setting Size swaps in the TrackBar's own
            // preferred height, and at 150% that height spills over the rows below.
            scaleBar = new ThemeSlider
            {
                Minimum = 50, Maximum = 250,
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
            opacityBar = new ThemeSlider
            {
                Minimum = 20, Maximum = 100,
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
            headerModes = new ThemePlate[3];
            var labels = new[] { "On hover", "Always", "Hidden" };
            var modes = new[] { HeaderMode.Auto, HeaderMode.Pinned, HeaderMode.Hidden };
            for (int i = 0; i < 3; i++)
            {
                var mode = modes[i];
                var r = new ThemePlate
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
            Hint(v, "For click-casting mouseover binds and MMO-mouse grids. Only while the pointer is over the locked overlay; "
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

            overlayStatus = Text_(v, "", 16, y, W - 32, 36, false, 8.5f, Theme.Yellow);
        }

        // MARK: - Shortcuts tab

        void BuildShortcuts()
        {
            var v = shortcutsPage;
            Text_(v, "These work anywhere, including in-game. Click one, then press new keys.",
                  16, 14, W - 32, 20, false, 9f, Theme.Body);

            int y = 50;
            foreach (var command in Commands.All)
            {
                var c = command;
                Text_(v, Commands.Label(c), 16, y + 5, 400, 22);
                var b = new ThemeButton
                {
                    Location = new Point(W - 190, y), Size = new Size(188, 30),
                    Style = ThemeButton.Kind.Grey, TextFont = Theme.Narrow(11f), Tint = Theme.White,
                };
                b.Click += (o, e) => StartRecording(c);
                v.Controls.Add(b);
                shortcutButtons[c] = b;
                y += 40;
            }

            Btn(v, "Restore Defaults", 16, 560, 150, RestoreDefaults, 30, primary: true);
            var hint = Hint(v, "Esc cancels · a shortcut must include a modifier", W - 330, 566, 328, 20);
            hint.TextAlign = ContentAlignment.MiddleRight;
        }

        static uint Pid(IntPtr h) { Native.GetWindowThreadProcessId(h, out uint pid); return pid; }

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

        // MARK: - Game tab

        ThemePanel gameList;
        Label gameBanner;
        ThemePlate[] gameRoleButtons;
        /// null = the Everyone tab; otherwise the role being edited.
        GameRole? gameTab;
        bool gameBuilt;
        readonly Dictionary<string, CheckBox> gameBoxes = new Dictionary<string, CheckBox>();
        readonly Dictionary<string, ThemeLabel> gameCommands = new Dictionary<string, ThemeLabel>();

        /// Which settings the selected role shows.
        List<GameSetting> GameVisible => gameTab.HasValue ? Game.For(gameTab.Value) : Game.Shared;

        void BuildGameFrame()
        {
            var v = gamePage;
            var names = new[] { "Everyone" }.Concat(GameRoles.All.Select(GameRoles.Title)).ToArray();
            gameRoleButtons = new ThemePlate[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var r = new ThemePlate
                {
                    Text = names[i], Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(16 + i * 124, 12), Size = new Size(120, 28), UseVisualStyleBackColor = true,
                    Checked = i == 0,
                };
                r.CheckedChanged += (o, e) =>
                {
                    if (!r.Checked) return;
                    gameTab = index == 0 ? (GameRole?)null : GameRoles.All[index - 1];
                    if (gameBuilt) BuildGameList();
                };
                v.Controls.Add(r);
                gameRoleButtons[i] = r;
            }

            gameBanner = Text_(v, "Tick what you want, then copy. Glass does not touch the game — paste these into chat "
                                 + "yourself, one line at a time, or into a macro (macros take several lines).",
                              16, 48, W - 32, 36, false, 8.5f, Theme.Body);

            // Not anchored: the page is still at its default 200×100 when this is added, so an
            // anchor would grow the list by the whole difference and push it over the buttons.
            // The window is a fixed size anyway.
            gameList = new ThemePanel { Location = new Point(0, 90), Size = new Size(W + 24, 460), AutoScroll = true };
            v.Controls.Add(gameList);

            var all = Btn(v, "Copy Ticked", 16, 560, 130, GameCopyTab, 30, primary: true);
            tips.SetToolTip(all, "Every ticked command on this tab, newline separated.");
            var macro = Btn(v, "Copy as Macro", 156, 560, 150, GameCopyMacro, 30, primary: true);
            tips.SetToolTip(macro, "The same commands split into 255-character macro bodies.");
        }

        /// Built on first view rather than in the constructor: the rows are sized in real pixels,
        /// so they wait until the window knows which monitor, and which scale, it is on.
        public void EnsureGame()
        {
            if (gameBuilt) return;
            gameBuilt = true;
            BuildGameList();
        }

        public void SelectGameRole(GameRole? role)
        {
            int i = role.HasValue ? Array.IndexOf(GameRoles.All, role.Value) + 1 : 0;
            gameRoleButtons[i].Checked = true;
            gameTab = role;
            if (gameBuilt) BuildGameList();
        }

        void BuildGameList()
        {
            gameList.SuspendLayout();
            foreach (var c in gameList.Controls.Cast<Control>().ToList()) c.Dispose();
            gameList.Controls.Clear();
            gameBoxes.Clear();
            gameCommands.Clear();

            var table = new ThemeTable
            {
                ColumnCount = 3, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(12), 0, S(12), S(8)),
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.SuspendLayout();

            // The third column is auto-sized around the Copy button and is taken out first;
            // the two percent columns then split what is left. Working the first column out
            // without allowing for the button overstates it, and the detail text then runs
            // under the command column and is painted over.
            int copyColumn = S(60) + S(8);
            int usable = gameList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(24) - copyColumn;
            int column = (int)(usable * 0.62);
            int detailWidth = Math.Max(S(180), column - S(30));
            var mono = new Font("Consolas", 8f);
            var small = new Font("Segoe UI", 8f);
            var bold = new Font("Segoe UI", 8.5f, FontStyle.Bold);

            string lastGroup = null;
            int row = 0;
            foreach (var s in GameVisible)
            {
                if (s.Group != lastGroup)
                {
                    lastGroup = s.Group;
                    var h = new ThemeLabel
                    {
                        Text = s.Group, AutoSize = false, Size = new Size(S(400), S(22)),
                        TextFont = Theme.F(11), Tint = Theme.Gold,
                        Margin = new Padding(0, row == 0 ? S(4) : S(14), 0, S(2)),
                    };
                    table.Controls.Add(h, 0, row);
                    table.SetColumnSpan(h, 3);
                    row++;
                }

                var setting = s;
                var commands = Game.CommandsFor(s);
                var cell = new ThemeFlow
                {
                    FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, S(2), 0, S(2)),
                };
                var box = new ThemeCheck { Text = s.Title, AutoSize = false, Size = new Size(column, S(22)), Checked = Saved.GameSetting(s), Margin = new Padding(0) };
                tips.SetToolTip(box, s.Detail);
                box.CheckedChanged += (o, e) =>
                {
                    if (refreshing) return;
                    Saved.SetGameSetting(setting.Key, box.Checked);
                    UpdateGameRow(setting);
                };
                cell.Controls.Add(box);
                if (!string.IsNullOrEmpty(s.Detail))
                {
                    var detail = new ThemeLabel
                    {
                        Text = s.Detail, AutoSize = false, TextFont = Theme.F(9), Tint = Theme.Hint, Wrap = true,
                        Margin = new Padding(S(24), 0, 0, S(2)),
                    };
                    // Measured, not guessed: a two-line detail given one line's height loses
                    // its second line, and one given three leaves a hole.
                    using (var g = CreateGraphics())
                        detail.Size = new Size(detailWidth,
                            (int)Math.Ceiling(Theme.Measure(g, s.Detail, detail.TextFont, detailWidth).Height) + 2);
                    cell.Controls.Add(detail);
                }
                table.Controls.Add(cell, 0, row);

                // The commands themselves, since Glass never asks the client anything.
                var cmd = new ThemeLabel
                {
                    Text = commands.FirstOrDefault() ?? "", AutoSize = false, TextFont = Theme.Narrow(9.5f),
                    Tint = Theme.Hint, Dock = DockStyle.Fill, Margin = new Padding(S(8), S(5), S(4), 0),
                    MayTruncate = true,          // the whole command is in the tooltip
                };
                tips.SetToolTip(cmd, string.Join("\n", commands));
                table.Controls.Add(cmd, 1, row);

                var copy = new ThemeButton
                {
                    Text = "Copy", Size = new Size(S(60), S(26)), Style = ThemeButton.Kind.Grey,
                    TextFont = Theme.F(9.5f), Margin = new Padding(0, S(1), 0, 0),
                };
                tips.SetToolTip(copy, string.Join("\n", commands));
                copy.Click += (o, e) => GameCopyOne(setting);
                table.Controls.Add(copy, 2, row);

                gameBoxes[s.Key] = box;
                gameCommands[s.Key] = cmd;
                row++;
            }
            table.RowCount = row;
            table.ResumeLayout(false);
            gameList.Controls.Add(table);
            gameList.ResumeLayout(true);
            gameList.AutoScrollPosition = new Point(0, 0);
        }

        void UpdateGameRow(GameSetting s)
        {
            if (!gameCommands.TryGetValue(s.Key, out var label)) return;
            var lines = Game.CommandsFor(s);
            label.Text = lines.FirstOrDefault() ?? "";
            tips.SetToolTip(label, string.Join("\n", lines));
        }

        void GameSay(string text) { if (gameBanner != null) gameBanner.Text = text; }

        void GameCopyOne(GameSetting s)
        {
            var lines = Game.CommandsFor(s);
            Copy(string.Join("\n", lines));
            GameSay(lines.Count == 1 ? "Copied: " + lines[0]
                                    : "Copied " + lines.Count + " lines — paste them one at a time.");
        }

        void GameCopyTab()
        {
            var lines = Game.ConsoleCommands(GameVisible);
            Copy(string.Join("\n", lines));
            GameSay("Copied " + lines.Count + " commands. Chat takes one line at a time; a macro takes several.");
        }

        void GameCopyMacro()
        {
            var chunks = Game.MacroChunks(Game.ConsoleCommands(GameVisible));
            Copy(string.Join("\n\n---\n\n", chunks));
            GameSay("Copied " + chunks.Count + " macro" + (chunks.Count == 1 ? "" : "s") + ", separated by ---. "
                   + "Each fits the 255-character macro limit.");
        }

        // MARK: - Help tab

        TextBox reportBox;
        Label helpStatus, versionLabel;

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
                  16, 36, W - 32, 72, false, 9f, Theme.Hint);

            Btn(v, "Copy Report", 16, 112, 150, () =>
            {
                helpStatus.Text = Report.CopyAndSave();
                ShowReport();
            }, 32, primary: true);
            Btn(v, "Show Report File", 176, 112, 150, Report.ShowFile, 32);
            Btn(v, "Open Log Folder", 336, 112, 150, () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.Dir) { UseShellExecute = true }); }
                catch (Exception e) { Log.Write("open log folder: " + e.Message); }
            }, 32);
            // Closing this window only hides it, as on the Mac. This is the way out.
            Btn(v, "Quit Glass", W - 150, 112, 150, App.Quit, 32);
            helpStatus = Text_(v, "The report holds Glass's settings, your screen layout, the game windows it can see and the "
                                  + "recent log. Nothing you type is in it, bar the 1-0 keys Glass forwarded.",
                               16, 148, W - 32, 34, false, 8.5f, Theme.Hint);

            versionLabel = Text_(v, "", 16, 192, 420, 20, false, 9f);
            // The Store build updates itself through the Store, so it offers neither button:
            // one would be pointless and the other downloads from outside the Store.
            if (Updates.Available)
            {
                Btn(v, "Check for Updates", W - 330, 186, 150, () =>
                {
                    versionLabel.Text = "Checking…";
                    Updates.Check(quiet: true, done: ShowVersion);
                }, 30);
                Btn(v, "Download the Latest", W - 170, 186, 170, Updates.Download, 30);
            }

            reportBox = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = Theme.Narrow(9.5f), Location = new Point(16, 226), Size = new Size(W - 16, 380),
                BackColor = Theme.Rgb(0.06, 0.055, 0.05), ForeColor = Theme.Body, BorderStyle = BorderStyle.FixedSingle,
            };
            v.Controls.Add(reportBox);
        }

        void ShowVersion()
        {
            if (versionLabel == null) return;
            versionLabel.Text = "You have Glass " + Updates.Current + ". "
                + (!Updates.Available ? "The Microsoft Store keeps it up to date."
                   : Updates.Latest == null ? "Newest: not checked yet."
                   : Updates.Newer ? "Glass " + Updates.Latest + " is available -- download it on the right."
                   : "That's the newest.");
            versionLabel.ForeColor = Updates.Newer ? Color.FromArgb(200, 90, 0) : SystemColors.ControlText;
        }

        /// What Copy Report would send right now, readable before it is sent.
        void ShowReport()
        {
            ShowVersion();
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

                mirrorModes[0].Checked = Saved.MirrorMode == "window";
                mirrorModes[1].Checked = Saved.MirrorMode == "screen";
                hiddenClickModes[0].Checked = Saved.HiddenClicks == "post";
                hiddenClickModes[1].Checked = Saved.HiddenClicks == "front";
                var t = App.Target;
                mirrorStatus.Text = App.Overlay == null ? "Not mirroring anything yet."
                    : t != IntPtr.Zero
                        ? "Now mirroring a game window: " + Wnd.ProcessName(t) + " " + Pid(t) + ", \"" + Wnd.Title(t) + "\""
                          + (App.Capture.WindowProblem != null ? " -- but window capture failed, so the screen is shown" : "")
                        : "Now mirroring the screen" + (App.WindowMode ? " (no game window under the region)" : "") + ".";

                foreach (var s in GameVisible)
                {
                    if (gameBoxes.TryGetValue(s.Key, out var box)) box.Checked = Saved.GameSetting(s);
                    UpdateGameRow(s);
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
            foreach (var page in pages)
            {
                Select(page.Name);
                var roles = page == gamePage ? new GameRole?[] { null, GameRole.Tank, GameRole.Healer, GameRole.DPS } : new GameRole?[] { null };
                foreach (var role in roles)
                {
                    if (page == gamePage) SelectGameRole(role);
                    yield return (page.Name + (role.HasValue ? "-" + GameRoles.Title(role.Value) : ""), SelfTest.Render(this));
                }
            }
        }
    }
}
