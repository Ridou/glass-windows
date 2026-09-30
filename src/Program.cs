// Glass for Windows -- mirror a live region of your screen as an always-on-top overlay that
// clicks through to the real thing.
//
// Pick any rectangle on any monitor and Glass floats it above your game. Clicking the mirror
// clicks the real thing underneath, then hands focus straight back. Four preset regions
// (duo / 5 / 10 / 20) sit in a header above the overlay, so switching raid-frame layouts is
// one click.
//
// A port of the macOS Glass, feature for feature. Run Glass.exe --help for the options.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Glass
{
    public static class Program
    {
        const string Usage = @"Glass -- mirror a live region of your screen as an always-on-top overlay.

  Glass.exe [options]

    --pick              drag out the region to mirror
    --preset NAME       start on a saved preset (duo, 5, 10, 20)
    --set NAME          drag out a region and save it as NAME, then mirror it
    --region X,Y,W,H    mirror this screen rectangle directly (pixels)
    --window NAME       mirror the largest window whose title or process matches NAME
    --mirror MODE       window or screen for this run (default: Settings, which starts on window)
    --at X,Y            overlay top-left, screen pixels (default: last position)
    --scale F           overlay size multiplier (default 1.0)
    --fps N             capture rate (default 15)
    --opacity F         overlay opacity 0.1-1.0 (default 1.0)
    --bar / --no-bar    show or hide the preset header (default: dots on hover)
    --step MS           delay between synthesized events (default 20)
    --settle MS         delay before warping back (default 40)
    --hover MS          pointer rest before a forwarded key (default 35)
    --settings [TAB]    open Settings on launch, optionally on a tab (e.g. WoW)
    --install-addon     install/update the GlassSetup WoW addon, then exit
    --role NAME-REALM=tank|healer|dps   assign a character's role, then exit
    --list              list monitors, windows and presets, then exit
    --report            copy a help report and save it to the Desktop, then exit
    --reset             forget all presets and saved position
    --pid               route clicks with PostMessage instead of warping the cursor

  Click a preset to switch - Alt-click a preset to re-record its region.
  Ctrl+Alt+L unlocks the overlay so a plain drag moves it.

  Settings: %APPDATA%\Glass\settings.json    Log: %LOCALAPPDATA%\Glass\Glass.log
";

        static string[] args;
        static bool haveConsole;

        static string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool Has(string name) => args.Contains(name);

        static double[] Numbers(string s, int n, string label)
        {
            if (s == null) return null;
            var v = s.Split(',').Select(x => double.TryParse(x.Trim(), System.Globalization.NumberStyles.Float,
                                                             System.Globalization.CultureInfo.InvariantCulture, out var d)
                                                 ? (double?)d : null)
                             .Where(x => x.HasValue).Select(x => x.Value).ToArray();
            if (v.Length != n) Die(label + " needs " + n + " comma-separated numbers");
            return v;
        }

        static double? Number(string s) =>
            s != null && double.TryParse(s, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (double?)null;

        static void Say(string text) { try { Console.WriteLine(text); } catch { } }

        /// Launched from Explorer there is no console to print to, so a fatal problem has to be
        /// said in a window or it is said to nobody.
        static void Die(string message)
        {
            Log.Write("stopped: " + message);
            try { Console.Error.WriteLine(message); } catch { }
            if (!haveConsole) MessageBox.Show(message, "Glass", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Saved.FlushNow();
            Log.Flush();
            Environment.Exit(1);
        }

        /// A GUI program gets no console. When started from one, borrow it, so --list and
        /// --help print where you typed them. Redirected output (> file, | more) already works.
        static void AttachConsole()
        {
            var h = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
            if (h != IntPtr.Zero && h != new IntPtr(-1)) { haveConsole = true; return; }
            if (!Native.AttachConsole(Native.ATTACH_PARENT_PROCESS)) return;
            var handle = Native.CreateFile("CONOUT$", Native.GENERIC_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                                           IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
            var w = new StreamWriter(new FileStream(new SafeFileHandle(handle, true), FileAccess.Write)) { AutoFlush = true };
            Console.SetOut(w);
            Console.SetError(w);
            haveConsole = true;
            Console.WriteLine();                    // the shell has already printed its prompt
        }

        [STAThread]
        static int Main(string[] argv)
        {
            try { return Run(argv); }
            finally { Log.Flush(); }
        }

        static int Run(string[] argv)
        {
            args = argv;
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (o, e) => Log.Write("unhandled: " + e.Exception);
            // The process ends as soon as this returns, so the line has to reach the disk first.
            AppDomain.CurrentDomain.UnhandledException += (o, e) => { Log.Write("fatal: " + e.ExceptionObject); Log.Flush(); };

            AttachConsole();
            Log.Start();
            Saved.Load();

            if (Has("--help") || Has("-h") || Has("/?")) { Say(Usage); return 0; }

            var selftest = Arg("--selftest");
            if (selftest != null) return SelfTest.Run(selftest);

            // MARK: Options

            // These change saved settings. A running copy holds its own and would write over
            // them at its next save, so say so rather than appear to work.
            var persistent = new[] { "--reset", "--role", "--scale", "--bar", "--no-bar" }.Where(Has).ToList();
            if (persistent.Count > 0 && Native.FindWindow(null, Hotkeys.Caption) != IntPtr.Zero)
                Die("Glass is already running, so " + string.Join(", ", persistent)
                    + " would be overwritten. Quit it from its tray icon first, then run this again.");

            var scaleArg = Number(Arg("--scale"));
            if (scaleArg.HasValue) Saved.Scale = scaleArg.Value;
            App.Fps = Math.Max(1, Number(Arg("--fps")) ?? 15);
            App.StartOpacity = Math.Min(1.0, Math.Max(0.1, Number(Arg("--opacity")) ?? Saved.Opacity ?? 1.0));
            var at = Numbers(Arg("--at"), 2, "--at");
            if (at != null) App.StartAt = new System.Drawing.Point((int)at[0], (int)at[1]);
            var region = Numbers(Arg("--region"), 4, "--region");
            var windowName = Arg("--window");
            var presetName = Arg("--preset");
            var setName = Arg("--set");
            // Local latency per click. These are the whole cost this project exists to remove,
            // so they are tunable rather than baked in.
            if (int.TryParse(Arg("--step"), out var step)) Forward.StepMs = Math.Max(0, step);
            if (int.TryParse(Arg("--settle"), out var settle)) Forward.SettleMs = Math.Max(0, settle);
            if (int.TryParse(Arg("--hover"), out var hover)) Forward.HoverMs = Math.Max(0, hover);
            Forward.ForcePost = Has("--pid");
            var mirror = Arg("--mirror");
            if (mirror != null)
            {
                if (mirror != "screen" && mirror != "window") Die("--mirror takes window or screen");
                App.MirrorOverride = mirror;
            }
            if (Has("--bar")) Saved.HeaderMode = HeaderMode.Pinned;
            if (Has("--no-bar")) Saved.HeaderMode = HeaderMode.Hidden;

            foreach (var n in new[] { presetName, setName }.Where(x => x != null))
                if (!Saved.PresetNames.Contains(n))
                    Die("unknown preset '" + n + "' -- choose from " + string.Join(", ", Saved.PresetNames));

            if (Has("--reset")) { Saved.Reset(); Say("forgot all presets and saved position"); }

            // MARK: Commands that exit

            var role = Arg("--role");
            if (role != null && role.Contains("="))
            {
                var roles = Saved.WowRoles;
                int eq = role.IndexOf('=');
                roles[role.Substring(0, eq)] = role.Substring(eq + 1);
                Saved.WowRoles = roles;
                Saved.FlushNow();
                Say("roles: " + string.Join(", ", roles.Select(kv => kv.Key + "=" + kv.Value)));
                return 0;
            }

            if (Has("--install-addon"))
            {
                var (installed, failed) = Wow.InstallAddon();
                if (installed.Count > 0) Say("installed " + Wow.AddonName + " in: " + string.Join(", ", installed));
                if (installed.Count == 0 && failed.Count == 0) Say("no World of Warcraft install found");
                foreach (var f in failed) Say("failed: " + f);
                return failed.Count == 0 ? 0 : 1;
            }

            // For when Glass will not run properly: the same report the Help tab makes.
            if (Has("--report")) { Say(Report.CopyAndSave()); Say(Report.FilePath); return 0; }

            if (Has("--list")) { List(); return 0; }

            // MARK: One Glass at a time

            // Launching Glass again while it runs opens Settings in the running copy -- what
            // clicking a running app's Dock icon does on the Mac.
            using (var mutex = new Mutex(true, @"Local\Glass.7c1e0b52", out bool first))
            {
                if (!first)
                {
                    bool mirroring = setName != null || region != null || presetName != null || windowName != null || Has("--pick");
                    var other = Native.FindWindow(null, Hotkeys.Caption);
                    if (other != IntPtr.Zero && !mirroring)
                    {
                        Native.GetWindowThreadProcessId(other, out uint pid);
                        Native.AllowSetForegroundWindow((int)pid);
                        // --settings TAB carries the tab across as its position, 1-based.
                        var tab = Has("--settings") ? Arg("--settings") : null;
                        int index = tab == null ? 0 : Array.FindIndex(SettingsForm.TabNames,
                            t => string.Equals(t, tab, StringComparison.OrdinalIgnoreCase)) + 1;
                        Native.PostMessage(other, Hotkeys.WM_SHOW_SETTINGS, new IntPtr(index), IntPtr.Zero);
                        return 0;
                    }
                    Die("Glass is already running. Use its tray icon, or quit it from there first.");
                }
                return RunApp(setName, region, presetName, windowName);
            }
        }

        static void List()
        {
            Say("DISPLAYS");
            foreach (var d in Screens.All()) Say("  " + d);
            Say("");
            Say("WINDOWS  (use --window NAME)");
            var windows = Wnd.AllOrdinary()
                .Select(h => { Native.GetWindowRect(h, out RECT r); return (h, r: r.ToRectangle()); })
                .Where(w => w.r.Width > 120)
                .OrderByDescending(w => (long)w.r.Width * w.r.Height);
            foreach (var (h, r) in windows)
                Say(string.Format("  {0,-24} {1,6},{2,-6} {3,5}x{4,-5} {5}", Wnd.ProcessName(h), r.X, r.Y, r.Width, r.Height, Wnd.Title(h)));
            Say("");
            Say("PRESETS");
            foreach (var n in Saved.PresetNames)
            {
                var r = Saved.Preset(n);
                Say(r.HasValue ? string.Format("  {0,-5} {1,6},{2,-6} {3}x{4}", n, r.Value.X, r.Value.Y, r.Value.Width, r.Value.Height)
                               : string.Format("  {0,-5} (unset)", n));
            }
        }

        static int RunApp(string setName, double[] region, string presetName, string windowName)
        {
            // A 1ms timer resolution for the process, so the step and settle delays are what
            // they say. The default 15.6ms tick would roughly double every one of them.
            Native.timeBeginPeriod(1);
            Log.Echo = haveConsole;

            App.Ui = new Form { ShowInTaskbar = false, Text = "Glass.Ui" };
            var _ = App.Ui.Handle;
            Log.StartWatchdog(App.Ui);

            App.Hotkeys = new Hotkeys();
            App.Hotkeys.Reload();
            Forward.Start();
            Hooks.Start();
            App.Tray = new Tray();
            App.Boost = new GpuBoost();
            App.Boost.Apply();

            App.Capture.OnFrame = () =>
            {
                var h = OverlayForm.LiveHandle;
                if (h != IntPtr.Zero) Native.InvalidateRect(h, IntPtr.Zero, false);
            };
            App.Capture.OnDrop = () => App.Defer(App.RestartCapture);
            SystemEvents.DisplaySettingsChanged += (o, e) => App.Defer(App.DisplaysChanged);
            SystemEvents.SessionSwitch += (o, e) =>
            {
                if (e.Reason == SessionSwitchReason.SessionUnlock) Hooks.Reinstall();
            };
            // Logging off or shutting down can end the process without Application.Run returning.
            SystemEvents.SessionEnding += (o, e) => { Log.Write("session ending"); Saved.FlushNow(); Log.Flush(); };
            App.Publish();

            Log.Write("displays: " + string.Join(" | ", Screens.All().Select(d => d.ToString().Trim())));

            // Double-clicked inside the zip, Glass runs from a temporary folder the zip tool may
            // delete while it runs, and "extract first" is the step people skip.
            var exe = Environment.ProcessPath ?? "";
            if (exe.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0
                && (exe.IndexOf("Rar$", StringComparison.Ordinal) >= 0 || exe.IndexOf(".zip", StringComparison.OrdinalIgnoreCase) >= 0
                    || exe.IndexOf("7z", StringComparison.OrdinalIgnoreCase) >= 0))
                App.Warn("Extract Glass first", "Glass is running from inside the zip. Quit it, right-click the zip > Extract All, "
                         + "and run Glass.exe from the extracted folder.");

            if (!Saved.GetBool("welcomed", false))
            {
                Saved.SetBool("welcomed", true);
                App.Tray.Balloon("Glass is running",
                    "It lives in the tray, by the clock. Right-click it for presets and settings; Ctrl+Alt+P picks a region.");
            }

            App.Defer(() =>
            {
                // --settings [Regions|Overlay|Shortcuts|WoW]
                if (Has("--settings"))
                {
                    var tab = Arg("--settings");
                    App.ShowSettings(tab != null && !tab.StartsWith("--") ? tab : null);
                }
                ResolveStart(setName, region, presetName, windowName);
            });

            Application.Run(new ApplicationContext());
            Native.timeEndPeriod(1);
            Saved.FlushNow();
            return 0;
        }

        /// The starting region: explicit flag, preset, named window, saved value, or ask.
        static void ResolveStart(string setName, double[] region, string presetName, string windowName)
        {
            if (setName != null)
            {
                Picker.Show("Drag the region for “" + setName + "”  ·  Esc to cancel", r =>
                {
                    Saved.SetPreset(setName, r);
                    App.Begin(r, setName, picked: true);
                }, App.Quit);
                return;
            }
            if (region != null)
            {
                App.Begin(new System.Drawing.Rectangle((int)region[0], (int)region[1], (int)region[2], (int)region[3]), null, picked: true);
                return;
            }
            if (presetName != null)
            {
                var r = Saved.Preset(presetName);
                if (!r.HasValue) Die("preset '" + presetName + "' has no region yet -- run with --set " + presetName);
                App.Begin(r.Value, presetName);
                return;
            }
            if (windowName != null)
            {
                var h = Wnd.LargestMatching(windowName);
                if (h == IntPtr.Zero) Die("no on-screen window matching '" + windowName + "' -- try --list");
                App.Begin(App.FrameBounds(h), null, window: h);
                return;
            }
            var saved = Saved.Region;
            if (!Has("--pick") && saved.HasValue && Screens.For(saved.Value) != null)
            {
                App.Begin(saved.Value, Saved.ActivePreset);
                return;
            }
            Picker.Show("Drag to choose what to mirror  ·  Esc to cancel", r => App.Begin(r, null, picked: true), App.Quit);
        }
    }
}
