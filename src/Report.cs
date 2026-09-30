// The help report: everything someone helping from afar needs, in one paste.
//
// Settings > Help > Copy Report, the tray's "Copy Report for Help", or Glass.exe --report. It
// goes on the clipboard and into Glass-report.txt on the Desktop, so it can be pasted into a
// message or attached as a file. It holds Glass's settings, the screen layout, the WoW clients
// it can see and the recent log -- never anything typed in the game, which the log never has.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glass
{
    public static class Report
    {
        const int LogLines = 400;

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Glass-report.txt");

        /// Build it, put it on the clipboard and the Desktop. Returns what happened, in words
        /// for the person clicking. UI thread only: the clipboard needs it.
        public static string CopyAndSave()
        {
            string text;
            try { text = Build(); }
            catch (Exception e) { text = "Glass report failed to build: " + e + "\r\n"; }

            bool copied = false, saved = false;
            try { Clipboard.SetDataObject(text, true, 10, 50); copied = true; }
            catch (Exception e) { Log.Write("report: clipboard: " + e.Message); }
            try { File.WriteAllText(FilePath, text, new UTF8Encoding(false)); saved = true; }
            catch (Exception e) { Log.Write("report: file: " + e.Message); }
            Log.Write("report made (" + text.Length + " characters)" + (copied ? ", copied" : "") + (saved ? ", saved" : ""));

            if (copied && saved)
                return "Copied. Paste it into a message, or send Glass-report.txt from your Desktop.";
            if (saved) return "Couldn't use the clipboard, but it's saved as Glass-report.txt on your Desktop. Send that file.";
            if (copied) return "Copied. Paste it into a message. (It couldn't be saved to your Desktop.)";
            return "Couldn't copy or save the report. Send " + Log.File_ + " instead.";
        }

        /// Open Explorer with the report file selected, ready to drag into a chat.
        public static void ShowFile()
        {
            try
            {
                if (!File.Exists(FilePath)) CopyAndSave();
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + FilePath + "\"") { UseShellExecute = true });
            }
            catch (Exception e) { Log.Write("report: show file: " + e.Message); }
        }

        public static string Build()
        {
            Log.Flush();                                   // the newest lines are the useful ones
            var r = new StringBuilder();
            void Line(string s = "") => r.Append(s).Append("\r\n");
            void Head(string s) { Line(); Line("== " + s + " =="); }
            void Try(Action a) { try { a(); } catch (Exception e) { Line("  (failed: " + e.Message + ")"); } }

            List<string> log = new List<string>();
            try
            {
                log = Tail(Log.File_, LogLines);
                if (log.Count < LogLines)
                    log = Tail(Path.Combine(Log.Dir, "Glass.old.log"), LogLines - log.Count).Concat(log).ToList();
            }
            catch { }

            Line("GLASS REPORT -- paste all of this to whoever is helping you.");
            Line("Made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " (UTC" + DateTimeOffset.Now.ToString("zzz") + ")");

            Head("What looks wrong");
            Try(() =>
            {
                var found = Findings(log);
                if (found.Count == 0) Line("  Nothing obvious. The details below will tell more.");
                foreach (var f in found) Line("  * " + f);
            });

            Head("Versions");
            Try(() =>
            {
                var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
                var exe = Environment.ProcessPath ?? "?";
                Line("  Glass " + v + (Updates.Latest != null ? " (newest: " + Updates.Latest + ")" : "") + ", built " + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm") + ", at " + exe);
                Line("  " + WindowsVersion() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"));
                Line("  Glass runs as administrator: " + (Wnd.WeAreElevated ? "yes" : "no"));
                Line("  running since " + Process.GetCurrentProcess().StartTime.ToString("HH:mm:ss"));
            });

            Head("Displays");
            Try(() => { foreach (var d in Screens.All()) Line("  " + d.ToString().Trim()); });

            Head("Windows settings that matter");
            Try(() =>
            {
                string wheel = Native.SystemParametersInfo(Native.SPI_GETMOUSEWHEELROUTING, 0, out uint routing, 0)
                    ? (routing == Native.MOUSEWHEEL_ROUTING_MOUSE_POS ? "under the pointer (Scroll inactive windows: on)"
                                                                     : "to the focused window (Scroll inactive windows: off) [" + routing + "]")
                    : "unknown";
                Line("  wheel goes " + wheel);
                Line("  mouse buttons swapped: " + (Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0 ? "yes" : "no"));
                var lang = InputLanguage.CurrentInputLanguage;
                Line("  keyboard: " + lang.LayoutName + " (" + lang.Culture.Name + ")");
            });

            Head("WoW clients");
            Try(() =>
            {
                int n = 0;
                foreach (var h in Wnd.AllOrdinary())
                {
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    var path = Wnd.ProcessPath(pid) ?? "";
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (!name.StartsWith("Wow", StringComparison.OrdinalIgnoreCase)) continue;
                    n++;
                    Native.GetWindowRect(h, out RECT wr);
                    var b = wr.ToRectangle();
                    var d = Screens.For(b);
                    Line("  " + name + " " + pid + "  \"" + Wnd.Title(h) + "\"  " + b.Width + "x" + b.Height + " at " + b.X + "," + b.Y
                         + (d != null ? "  on the display at " + d.Bounds.X + "," + d.Bounds.Y : "  on no display")
                         + (Wnd.IsElevated(pid) ? "  ADMINISTRATOR" : "")
                         + (Native.GetForegroundWindow() == h ? "  (has focus)" : ""));
                    Line("    " + path);
                }
                if (n == 0) Line("  none running");
            });

            Head("Glass right now");
            Try(() =>
            {
                if (App.Hotkeys == null)
                {
                    // Made by Glass.exe --report: this process is not the one mirroring.
                    bool other = Native.FindWindow(null, Hotkeys.Caption) != IntPtr.Zero;
                    Line("  (made with --report; Glass is " + (other ? "running, but its live state is only in a report made from its Help tab)" : "not running)"));
                    return;
                }
                var o = App.Overlay;
                bool live = o != null && !o.IsDisposed;
                Line("  mirroring: " + (live ? Rect(o.SourceRect) + (Saved.ActivePreset != null ? "  [preset " + Saved.ActivePreset + "]" : "") : "nothing"));
                if (live)
                    Line("  mirror: " + Rect(o.Bounds) + ", " + (o.Visible ? "shown" : "HIDDEN") + ", " + (Saved.Locked ? "locked" : "UNLOCKED")
                         + ", opacity " + (int)Math.Round(o.Opacity_ * 100) + "%");
                Line("  mirror shows: " + (App.WindowMode ? "the game window" : "the screen")
                     + (App.MirrorOverride != null ? " (--mirror for this run)" : "")
                     + ", " + Screens.All().Count + " monitor" + (Screens.All().Count == 1 ? "" : "s"));
                var t = App.Target;
                if (t != IntPtr.Zero)
                {
                    Native.GetWindowThreadProcessId(t, out uint tpid);
                    Line("  mirrored window: " + Wnd.ProcessName(t) + " " + tpid + " \"" + Wnd.Title(t) + "\""
                         + (Native.GetForegroundWindow() == t ? ", in front" : ", behind another window")
                         + "; clicks on it: " + (Saved.HiddenClicks == "front" ? "bring it forward" : "sent directly"));
                }
                Line("  capture: " + (App.Capture.Running ? "running" : "stopped") + " from " + App.Capture.Method + ", "
                     + App.Capture.Frames + " frames so far" + (App.Capture.WindowProblem != null ? "; window capture failed: " + App.Capture.WindowProblem : ""));
                Line("  keyboard hook: " + (Hooks.Installed ? "installed" : "NOT INSTALLED"));
                Line("  number keys over the mirror: " + (Saved.ForwardKeys ? "on" : "off") + ", sent " + (Saved.KeysViaPid ? "without switching focus" : "by switching focus"));
                Line("  clicks: " + (Saved.ClicksViaPid || Forward.ForcePost ? "posted (cursor stays put)" : "cursor warps there and back"));
                Line("  header: " + Saved.HeaderMode.Label() + ", GPU boost " + (Saved.GpuBoost ? "on" : "off"));
                var taken = App.Hotkeys?.Taken ?? "";
                Line("  shortcuts: " + string.Join(", ", Commands.All.Select(c => Saved.GetShortcut(c).Display))
                     + (taken.Length > 0 ? "   TAKEN BY ANOTHER PROGRAM: " + taken : ""));
            });

            Head("Settings file (" + Saved.Path_ + ")");
            Try(() => Line(File.Exists(Saved.Path_) ? File.ReadAllText(Saved.Path_).Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd() : "  (none yet)"));

            Head("Log, last " + log.Count + " lines (" + Log.File_ + ")");
            foreach (var l in log) Line(l);
            Line();
            Line("-- end of Glass report --");
            return r.ToString();
        }

        /// Plain-English problems, from the live state and the recent log, most serious first.
        static List<string> Findings(List<string> log)
        {
            var f = new List<string>();
            // Case matters: "FAILED" is how Glass shouts a failed hand-back, and "thread stalled" must
            // not match "keyboard hook installed".
            int Count(string what) => log.Count(l => l.IndexOf(what, StringComparison.Ordinal) >= 0);
            void From(string what, string say) { int n = Count(what); if (n > 0) f.Add(say + (n > 1 ? " (" + n + " times recently)" : "")); }

            if (Count("fatal:") > 0) f.Add("Glass crashed recently. The log below has the details.");
            if (Updates.Newer) f.Add("This is Glass " + Updates.Current + ", but " + Updates.Latest + " is out. Update first: " + Updates.DownloadUrl);
            var exe = Environment.ProcessPath ?? "";
            if (exe.IndexOf(@"\Temp\", StringComparison.OrdinalIgnoreCase) >= 0 || exe.IndexOf("Rar$", StringComparison.Ordinal) >= 0)
                f.Add("Glass is running from inside the zip (a temporary folder). Extract the zip first (right-click > Extract All) "
                      + "and run Glass.exe from there, or Windows may delete it while it runs.");
            if (App.Target != IntPtr.Zero && App.Capture.WindowProblem != null)
                f.Add("Glass couldn't capture the game window, so the mirror shows the screen instead: " + App.Capture.WindowProblem);
            if (App.Hotkeys != null && App.Overlay != null && App.WindowMode && App.Target == IntPtr.Zero)
                f.Add("The mirror is set to show a game window, but no game window was under the region, so the screen is shown. "
                      + "Alt+Tab to the character whose frames you want, then press Ctrl+Alt+P and drag around them.");
            if (!Wnd.WeAreElevated)
                foreach (var h in Wnd.AllOrdinary())
                {
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    var name = Path.GetFileNameWithoutExtension(Wnd.ProcessPath(pid) ?? "");
                    if (name.StartsWith("Wow", StringComparison.OrdinalIgnoreCase) && Wnd.IsElevated(pid))
                    { f.Add("WoW runs as administrator but Glass doesn't, so Windows blocks Glass's clicks and keys. Run Glass as administrator too."); break; }
                }
            if (App.Hotkeys != null && !Hooks.Installed) f.Add("The keyboard hook isn't installed, so number keys over the mirror can't work.");
            if (App.Overlay != null && !App.Overlay.IsDisposed && App.Capture.Frames == 0)
                f.Add("The mirror has never received a picture. Is WoW in Windowed (Fullscreen) mode?");
            if (App.Overlay != null && !App.Overlay.IsDisposed && !Saved.Locked) f.Add("The mirror is UNLOCKED, so clicks don't go through. Ctrl+Alt+L locks it.");
            if (App.Overlay != null && !App.Overlay.IsDisposed && !App.Overlay.Visible) f.Add("The mirror is hidden. Ctrl+Alt+H shows it.");
            if (Native.SystemParametersInfo(Native.SPI_GETMOUSEWHEELROUTING, 0, out uint routing, 0) && routing != Native.MOUSEWHEEL_ROUTING_MOUSE_POS)
                f.Add("\"Scroll inactive windows\" is off in Windows, so the wheel over the mirror goes to the game you're playing.");
            var taken = App.Hotkeys?.Taken ?? "";
            if (taken.Length > 0) f.Add("Another program owns these shortcuts, so they don't work: " + taken + ".");
            From("could not reach", "The pointer couldn't reach the other game, and Glass refused the input. Is \"Lock Cursor to Window\" on in WoW?");
            From("covers what it mirrors", "The mirror was sitting on top of the area it shows, so clicks were refused.");
            From("FAILED", "Focus didn't come back to the game being played after a click or key.");
            From("never took focus", "A clicked game window was slow to take focus.");
            From("never came forward", "A key was refused because the other game wouldn't come forward.");
            From("runs as administrator", "A game running as administrator blocked Glass's input.");
            From("hook missed a keystroke", "Windows dropped the keyboard hook and Glass had to reinstall it.");
            From("thread stalled", "Glass was briefly slow to respond.");
            From("screen cannot be read", "The screen couldn't be read for a while (locked, or a permission prompt).");
            From("SendInput was refused", "Windows refused Glass's input.");
            From("no window under", "An input was dropped because nothing was under the pointer's target.");
            return f;
        }

        static string Rect(System.Drawing.Rectangle r) => r.Width + "x" + r.Height + " at " + r.X + "," + r.Y;

        static List<string> Tail(string path, int n)
        {
            if (n <= 0 || !File.Exists(path)) return new List<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var rd = new StreamReader(fs))
            {
                var q = new Queue<string>();
                string l;
                while ((l = rd.ReadLine()) != null) { q.Enqueue(l); if (q.Count > n) q.Dequeue(); }
                return q.ToList();
            }
        }

        /// "Windows 11 23H2 (build 22631.4169)". The registry's ProductName still says Windows 10
        /// on 11, so the build number decides.
        static string WindowsVersion()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                var build = k?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString();
                var ubr = k?.GetValue("UBR");
                var display = k?.GetValue("DisplayVersion") as string ?? k?.GetValue("ReleaseId") as string ?? "";
                int.TryParse(build, out int b);
                return (b >= 22000 ? "Windows 11" : "Windows 10") + (display.Length > 0 ? " " + display : "")
                       + " (build " + build + (ubr != null ? "." + ubr : "") + ")";
            }
        }
    }
}
