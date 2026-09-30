// End-to-end check of the real Glass.exe, run inside the headless Wine prefix (tools/e2e/run.sh).
//
//   E2E.exe drive GLASS.EXE OUTDIR     the test itself
//   E2E.exe target NAME X Y W H LOG    a stand-in game window that logs every input it receives
//
// Two stand-ins play the two WoW clients. "Priest" holds the region Glass mirrors; "Warrior" is
// the client being played, with the overlay floating on top of it. Every check asks the same
// question Glass exists to answer: did this input reach exactly one character, the right one,
// at the right place?

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class E2E
{
    [STAThread]
    static int Main(string[] a)
    {
        if (a.Length >= 7 && a[0] == "target")
        {
            Application.EnableVisualStyles();
            Application.Run(new Target(a[1], int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]), int.Parse(a[5]), a[6]));
            return 0;
        }
        if (a.Length >= 2 && a[0] == "noact") { Application.Run(new NoActivate(a[1])); return 0; }
        if (a.Length >= 3 && a[0] == "drive") return new Driver(a[1], a[2], a.Length > 3 ? string.Join(" ", a.Skip(3)) : "").Run();
        if (a.Length >= 2 && a[0] == "probe-transparent") return Driver.ProbeTransparent(a[1]);
        if (a.Length >= 3 && a[0] == "drive-window") return new Driver(a[1], a[2], "").RunWindow(front: false);
        if (a.Length >= 3 && a[0] == "drive-window-front") return new Driver(a[1], a[2], "").RunWindow(front: true);
        Console.WriteLine("usage: E2E drive GLASS.EXE OUTDIR");
        return 2;
    }
}

/// A borderless window that writes every mouse button, wheel and key message it gets.
sealed class Target : Form
{
    readonly StreamWriter log;
    readonly bool activate;

    public Target(string name, int x, int y, int w, int h, string logPath)
    {
        Text = name;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(x, y, w, h);
        BackColor = name == "Priest" ? Color.SteelBlue : Color.DarkRed;
        log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        activate = name == "Warrior";
    }

    protected override bool ShowWithoutActivation => !activate;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (activate) Activate();
        log.WriteLine("ready");
    }

    static readonly Dictionary<int, string> Names = new Dictionary<int, string>
    {
        [0x0201] = "LDOWN", [0x0202] = "LUP", [0x0204] = "RDOWN", [0x0205] = "RUP",
        [0x0207] = "MDOWN", [0x0208] = "MUP", [0x020B] = "XDOWN", [0x020C] = "XUP",
        [0x020A] = "WHEEL", [0x0100] = "KEYDOWN", [0x0101] = "KEYUP",
        [0x0104] = "SYSKEYDOWN", [0x0105] = "SYSKEYUP", [0x0200] = "MOVE", [0x0006] = "ACTIVATE",
    };

    protected override void WndProc(ref Message m)
    {
        if (Names.TryGetValue(m.Msg, out var name))
        {
            long w = m.WParam.ToInt64(), l = m.LParam.ToInt64();
            short lo = (short)(l & 0xFFFF), hi = (short)((l >> 16) & 0xFFFF);
            string line = m.Msg >= 0x0100 && m.Msg <= 0x0105
                ? name + " vk=" + w
                : m.Msg == 0x0006 ? name + " " + (w & 0xFFFF)
                : m.Msg == 0x020A ? name + " delta=" + (short)((w >> 16) & 0xFFFF) + " at=" + lo + "," + hi
                : name + " at=" + lo + "," + hi + " mk=" + (w & 0xFFFF);
            log.WriteLine(line);
        }
        base.WndProc(ref m);
    }
}

/// The control for focus checks: the textbook never-activate window (WS_EX_NOACTIVATE, shown
/// without activation, MA_NOACTIVATE on click) with no Glass code in it. If clicking this steals
/// focus too, the platform is ignoring the style, and Glass's focus results mean nothing there.
sealed class NoActivate : Form
{
    readonly StreamWriter log;
    public NoActivate(string logPath)
    {
        Text = "NoActivate"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(440, 220, 100, 60); TopMost = true; ShowInTaskbar = false;
        log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    }
    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } }
    protected override bool ShowWithoutActivation => true;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }     // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        if (m.Msg == 0x0201) log.WriteLine("LDOWN");
        base.WndProc(ref m);
    }
}

sealed class Driver
{
    readonly string glass, outDir;
    readonly List<string> report = new List<string>();
    int failures;
    bool focusMeaningful = true;
    Process priest, warrior, app;
    IntPtr priestWnd, warriorWnd;

    // Layout on Wine's 1024x768 screen. The overlay sits on the Warrior, the mirrored region on
    // the Priest, exactly as on two monitors.
    static readonly Rectangle PriestRect = new Rectangle(0, 0, 400, 300);
    static readonly Rectangle WarriorRect = new Rectangle(420, 200, 600, 560);
    static readonly Rectangle Region = new Rectangle(50, 50, 200, 100);
    static readonly Point OverlayAt = new Point(600, 400);
    static readonly Point OverlayMid = new Point(700, 450);    // maps to 150,100 in the Priest
    static readonly Point Away = new Point(900, 700);           // on the Warrior, off the overlay

    readonly string extra;
    public Driver(string glass, string outDir, string extra) { this.glass = glass; this.outDir = outDir; this.extra = extra; }

    string LogOf(string who) => Path.Combine(outDir, who + ".log");
    // The stand-in is still writing, so share the file both ways or the read fails outright.
    string[] Lines(string who)
    {
        try
        {
            using (var fs = new FileStream(LogOf(who), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return new StreamReader(fs).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch (Exception e) { Console.WriteLine("read " + who + ": " + e.Message); return new string[0]; }
    }

    static string GlassLog => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glass", "Glass.log");
    long glassLogStart;
    string GlassLogSince()
    {
        try
        {
            using (var fs = new FileStream(GlassLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(Math.Min(glassLogStart, fs.Length), SeekOrigin.Begin);
                return new StreamReader(fs).ReadToEnd();
            }
        }
        catch { return ""; }
    }

    /// A focus result, which only counts where no-activate windows behave as Windows documents.
    void FocusCheck(string name, bool ok, string detail = "")
    {
        if (focusMeaningful) { Check(name, ok, detail); return; }
        report.Add("skip  " + name + " (platform ignores no-activate)   [" + detail + "]");
        Console.WriteLine(report[report.Count - 1]);
    }

    /// What a player does between tests: click into the game they are playing.
    void IntoGame() { Move(Away); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(400); }

    void Check(string name, bool ok, string detail = "")
    {
        if (!ok) failures++;
        report.Add((ok ? "ok    " : "FAIL  ") + name + (detail.Length > 0 ? "   [" + detail + "]" : ""));
        Console.WriteLine(report[report.Count - 1]);
    }

    public int Run()
    {
        Directory.CreateDirectory(outDir);
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glass", "settings.json");
        try { File.Delete(settings); } catch { }                         // a first run, like the friend's
        try { glassLogStart = new FileInfo(GlassLog).Length; } catch { glassLogStart = 0; }

        try
        {
            priest = StartTarget("Priest", PriestRect);
            priestWnd = WaitWindow("Priest");
            warrior = StartTarget("Warrior", WarriorRect);
            warriorWnd = WaitWindow("Warrior");
            Thread.Sleep(500);
            Check("stand-ins up, Warrior in front", priestWnd != IntPtr.Zero && warriorWnd != IntPtr.Zero
                  && N.GetForegroundWindow() == warriorWnd, "fg=" + Title(N.GetForegroundWindow()));

            // Control: does this platform honour no-activate windows at all?
            var ctl = Process.Start(new ProcessStartInfo(Environment.ProcessPath, $"noact \"{LogOf("NoActivate")}\"") { UseShellExecute = false });
            var ctlWnd = WaitWindow("NoActivate");
            Thread.Sleep(400);
            var afterShow = Title(N.GetForegroundWindow());
            Move(new Point(490, 250)); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(400);
            var afterClick = Title(N.GetForegroundWindow());
            bool honours = afterShow == "Warrior" && afterClick == "Warrior";
            focusMeaningful = honours;
            report.Add("info  control no-activate window: foreground after show = " + afterShow + ", after click = " + afterClick
                       + (honours ? "  (platform honours no-activate)" : "  (PLATFORM IGNORES no-activate: focus checks below are not meaningful here)"));
            try { ctl.Kill(); } catch { }
            Thread.Sleep(300);
            Move(Away); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(400);

            app = Process.Start(new ProcessStartInfo(glass,
                $"--region {Region.X},{Region.Y},{Region.Width},{Region.Height} --at {OverlayAt.X},{OverlayAt.Y} --mirror screen {extra}")
                { UseShellExecute = false });
            var overlay = WaitWindow("Glass", 15000);
            Thread.Sleep(1500);
            N.GetWindowRect(overlay, out var or);
            Check("overlay shown where asked", overlay != IntPtr.Zero && or.L == 600 && or.T == 400 && or.R == 800 && or.B == 500,
                  $"{or.L},{or.T},{or.R},{or.B}");
            var fg = N.GetForegroundWindow();
            N.GetWindowThreadProcessId(fg, out uint fgPid);
            report.Add("info  foreground after launch: " + Title(fg) + (fgPid == app.Id ? " (Glass's own window)" : ""));
            // Whatever launching did, the player clicks back into their game before playing.
            Move(Away); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(400);
            Check("Warrior in front after clicking into it", N.GetForegroundWindow() == warriorWnd, "fg=" + Title(N.GetForegroundWindow()));

            ClickTests();
            KeyTests();
            WheelTest();
            HotkeyTests();
            HeldKeyEdgeTests();
            ClipTest();
            CoverTest();
            SecondLaunchTest();

            Thread.Sleep(500);
            var log = GlassLogSince();
            File.WriteAllText(Path.Combine(outDir, "glass-run.log"), log);
            Check("log: mirroring started", log.Contains("mirroring 200x100 at 50,50"));
            Check("log: capture started", log.Contains("capture started 200x100"));
            Check("log: every click says where focus went", log.Split('\n').Where(l => l.Contains("via warp,")).All(l => l.Contains("focus") || l.Contains("Glass had")),
                  string.Join(" | ", log.Split('\n').Where(l => l.Contains("via warp,")).Take(2)));
            Check("log: no fatal or unhandled errors", !log.Contains("fatal") && !log.Contains("unhandled") && !log.Contains("action failed"));
            foreach (var l in log.Split('\n').Where(l => l.Contains("stalled"))) report.Add("info  " + l.Trim());
        }
        catch (Exception e) { Check("driver", false, e.ToString()); }
        finally
        {
            foreach (var p in new[] { app, priest, warrior }) { try { if (p != null && !p.HasExited) p.Kill(); } catch { } }
        }

        report.Add(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        File.WriteAllLines(Path.Combine(outDir, "e2e.txt"), report);
        Console.WriteLine(report[report.Count - 1]);
        return failures == 0 ? 0 : 1;
    }

    // MARK: - One monitor: the mirrored client covered by the played one

    static readonly Rectangle FullRect = new Rectangle(0, 0, 1024, 768);

    /// Both clients fill the one screen; the Warrior is played on top. The mirror must show and
    /// drive the covered Priest. `front`: clicks bring the Priest forward instead of posting.
    public int RunWindow(bool front)
    {
        Directory.CreateDirectory(outDir);
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glass", "settings.json");
        try { File.Delete(settingsPath); } catch { }
        if (front) { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); File.WriteAllText(settingsPath, "{ \"hiddenClicks\": \"front\" }"); }
        try { glassLogStart = new FileInfo(GlassLog).Length; } catch { glassLogStart = 0; }
        // On one monitor the mirror usually sits over the Priest's region. Posting never
        // touches the screen there, and a brought-forward click must pass through the mirror.
        var at = new Point(120, 60);
        var mid = new Point(at.X + 100, at.Y + 50);                  // maps to 150,100
        try
        {
            priest = StartTarget("Priest", FullRect);
            priestWnd = WaitWindow("Priest");
            Thread.Sleep(500);
            app = Process.Start(new ProcessStartInfo(glass, $"--region {Region.X},{Region.Y},{Region.Width},{Region.Height} --at {at.X},{at.Y} --mirror window")
                                { UseShellExecute = false });
            var overlay = WaitWindow("Glass", 15000);
            Thread.Sleep(1500);
            warrior = StartTarget("Warrior", FullRect);                  // the played client, on top
            warriorWnd = WaitWindow("Warrior");
            Thread.Sleep(800);
            IntoGame();
            var log = GlassLogSince();
            Check("window mode binds to the Priest, picked while it was in front",
                  log.Contains("window mode: mirroring E2E") && log.Contains("\"Priest\""), Line(log, "window mode"));
            report.Add("info  capture: " + (log.Contains("from window") ? "from the window" : Line(log, "cannot capture the window")));
            // Somewhere neither the mirror nor Wine's corner balloon sits.
            var probe = new Point(900, 700);
            Check("Warrior covers the Priest", N.GetAncestor(N.WindowFromPoint(new N.POINT { X = probe.X, Y = probe.Y }), 2) == warriorWnd, At(probe));
            WaitNoBalloon(new Point(150, 100));

            int p0 = Lines("Priest").Length, w0 = Lines("Warrior").Length;
            Move(mid); Mouse(N.LEFTDOWN); Thread.Sleep(40); Mouse(N.LEFTUP); Thread.Sleep(1000);
            var p = Lines("Priest").Skip(p0).ToList(); var w = Lines("Warrior").Skip(w0).ToList();
            Check((front ? "brought forward" : "posted") + ": click lands on the covered Priest at 150,100",
                  p.Any(l => l.StartsWith("LDOWN at=150,100")) && p.Any(l => l.StartsWith("LUP at=150,100")), string.Join("; ", p));
            Check("...and never on the Warrior", !w.Any(l => l.StartsWith("LDOWN") || l.StartsWith("LUP")), string.Join("; ", w));
            report.Add("info  " + Line(GlassLogSince(), "click Left"));

            if (!front)
            {
                IntoGame();
                p0 = Lines("Priest").Length; w0 = Lines("Warrior").Length;
                Move(new Point(at.X + 10, at.Y + 90));
                Key(N.VK_SHIFT, false); Mouse(N.RIGHTDOWN); Thread.Sleep(40); Mouse(N.RIGHTUP); Thread.Sleep(60); Key(N.VK_SHIFT, true);
                Thread.Sleep(1000);
                p = Lines("Priest").Skip(p0).ToList(); w = Lines("Warrior").Skip(w0).ToList();
                Check("posted: shift+right click reaches the Priest at 60,140 with shift",
                      p.Any(l => l.StartsWith("RDOWN at=60,140") && (int.Parse(l.Split("mk=")[1]) & 4) != 0), string.Join("; ", p));
                Check("...and not the Warrior", !w.Any(l => l.StartsWith("RDOWN")), string.Join("; ", w));

                p0 = Lines("Priest").Length; w0 = Lines("Warrior").Length;
                Move(mid); Thread.Sleep(100);
                Key('1', false); Thread.Sleep(40); Key('1', true); Thread.Sleep(900);
                p = Lines("Priest").Skip(p0).ToList(); w = Lines("Warrior").Skip(w0).ToList();
                Check("key 1 over the mirror reaches the covered Priest", p.Contains("KEYDOWN vk=49"), string.Join("; ", p));
                Check("...and not the Warrior", !w.Any(l => l.EndsWith("vk=49")), string.Join("; ", w));

                p0 = Lines("Priest").Length; w0 = Lines("Warrior").Length;
                Mouse(N.WHEEL, 120); Thread.Sleep(900);
                p = Lines("Priest").Skip(p0).ToList(); w = Lines("Warrior").Skip(w0).ToList();
                Check("wheel reaches the covered Priest", p.Count(l => l.StartsWith("WHEEL delta=120")) == 1, string.Join("; ", p));
                Check("...and not the Warrior", !w.Any(l => l.StartsWith("WHEEL")), string.Join("; ", w));

                // The way out: unlock, and a close button appears in the mirror's corner.
                Chord('L'); Thread.Sleep(600);
                Move(new Point(at.X + 184, at.Y + 16)); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP);
                bool quit = app.WaitForExit(8000);
                Check("unlocked, the mirror's X quits Glass", quit, quit ? "exit " + app.ExitCode : "still running");
                Check("...and says so in the log", GlassLogSince().Contains("quit from the mirror's close button"));
            }

            log = GlassLogSince();
            File.WriteAllText(Path.Combine(outDir, "glass-run.log"), log);
            Check("log: no fatal or unhandled errors", !log.Contains("fatal") && !log.Contains("unhandled") && !log.Contains("action failed"));
        }
        catch (Exception e) { Check("driver", false, e.ToString()); }
        finally
        {
            foreach (var pr in new[] { app, priest, warrior }) { try { if (pr != null && !pr.HasExited) pr.Kill(); } catch { } }
        }
        report.Add(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        File.WriteAllLines(Path.Combine(outDir, "e2e.txt"), report);
        Console.WriteLine(report[report.Count - 1]);
        return failures == 0 ? 0 : 1;
    }

    static string Line(string log, string what) =>
        log.Split('\n').FirstOrDefault(l => l.Contains(what))?.Trim() ?? "(no line with \"" + what + "\")";

    // MARK: - Checks

    void ClickTests()
    {
        int p0 = Lines("Priest").Length, w0 = Lines("Warrior").Length;
        Move(OverlayMid);
        WaitUncovered(new Point(150, 100), priestWnd, "first click");
        report.Add("info  before first click: under pointer " + At(OverlayMid) + "; under target " + At(new Point(150, 100)) + " (glass pid " + app.Id + ")");
        Mouse(N.LEFTDOWN); Thread.Sleep(40);
        report.Add("info  first click, button down: " + Where());
        Mouse(N.LEFTUP);
        Thread.Sleep(800);
        report.Add("info  after first click: " + Where());
        var p = Lines("Priest").Skip(p0).ToList(); var w = Lines("Warrior").Skip(w0).ToList();
        Check("left click on overlay lands on the Priest at 150,100",
              p.Contains("LDOWN at=150,100 mk=1") && p.Any(l => l.StartsWith("LUP at=150,100")), string.Join("; ", p));
        Check("left click never reaches the Warrior", !w.Any(l => l.StartsWith("LDOWN") || l.StartsWith("LUP")), string.Join("; ", w));
        N.GetCursorPos(out var c);
        Check("cursor back where it was", c.X == OverlayMid.X && c.Y == OverlayMid.Y, c.X + "," + c.Y);
        FocusCheck("focus handed back to the Warrior", N.GetForegroundWindow() == warriorWnd, "fg=" + Title(N.GetForegroundWindow()));

        // Shift + right click, as a Clique bind would use it.
        p0 = Lines("Priest").Length;
        Move(new Point(OverlayAt.X + 10, OverlayAt.Y + 90));             // maps to 60,140
        Key(N.VK_SHIFT, false); Mouse(N.RIGHTDOWN); Thread.Sleep(40); Mouse(N.RIGHTUP); Thread.Sleep(60); Key(N.VK_SHIFT, true);
        Thread.Sleep(800);
        p = Lines("Priest").Skip(p0).ToList();
        Check("shift+right click lands at 60,140 with shift held",
              p.Any(l => l.StartsWith("RDOWN at=60,140") && (int.Parse(l.Split("mk=")[1]) & 4) != 0), string.Join("; ", p));
        FocusCheck("focus handed back after right click", N.GetForegroundWindow() == warriorWnd, "fg=" + Title(N.GetForegroundWindow()));

        // Near the far corner (the last few pixels are rounded off), to prove the mapping edges.
        p0 = Lines("Priest").Length;
        Move(new Point(OverlayAt.X + 196, OverlayAt.Y + 96));
        Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(800);
        p = Lines("Priest").Skip(p0).ToList();
        Check("near bottom-right corner maps to 246,146", p.Any(l => l.StartsWith("LDOWN at=246,146")), string.Join("; ", p));
        FocusCheck("focus handed back after corner click", N.GetForegroundWindow() == warriorWnd, "fg=" + Title(N.GetForegroundWindow()));
    }

    void KeyTests()
    {
        // 1 over the overlay: the Priest gets it, the Warrior does not, focus never moves.
        int p0 = Lines("Priest").Length, w0 = Lines("Warrior").Length;
        Move(OverlayMid);
        Thread.Sleep(100);
        Key('1', false); Thread.Sleep(40); Key('1', true);
        Thread.Sleep(800);
        var p = Lines("Priest").Skip(p0).ToList(); var w = Lines("Warrior").Skip(w0).ToList();
        int move = p.FindIndex(l => l.StartsWith("MOVE at=150,100")), down = p.IndexOf("KEYDOWN vk=49");
        Check("1 over overlay reaches the Priest", down >= 0 && p.Contains("KEYUP vk=49"), string.Join("; ", p));
        Check("...after a posted move to the hovered frame", move >= 0 && move < down, string.Join("; ", p));
        Check("...and never the Warrior", !w.Any(l => l.EndsWith("vk=49")), string.Join("; ", w));
        FocusCheck("Warrior kept focus throughout", N.GetForegroundWindow() == warriorWnd, "fg=" + Title(N.GetForegroundWindow()));

        // Shift+2 over the overlay: modifier travels with the key.
        p0 = Lines("Priest").Length; w0 = Lines("Warrior").Length;
        Key(N.VK_SHIFT, false); Key('2', false); Thread.Sleep(40); Key('2', true); Key(N.VK_SHIFT, true);
        Thread.Sleep(800);
        p = Lines("Priest").Skip(p0).ToList(); w = Lines("Warrior").Skip(w0).ToList();
        int sh = p.IndexOf("KEYDOWN vk=16"), two = p.IndexOf("KEYDOWN vk=50"), shUp = p.IndexOf("KEYUP vk=16");
        Check("shift+2 reaches the Priest as shift, 2, 2 up, shift up", sh >= 0 && two > sh && shUp > two, string.Join("; ", p));
        Check("...and the 2 never reaches the Warrior", !w.Any(l => l.EndsWith("vk=50")), string.Join("; ", w));

        // Held key: autorepeat must not fire the forward again.
        p0 = Lines("Priest").Length;
        Key('3', false); Thread.Sleep(30); Key('3', false); Thread.Sleep(30); Key('3', false); Thread.Sleep(30); Key('3', true);
        Thread.Sleep(900);
        p = Lines("Priest").Skip(p0).ToList();
        Check("held 3 forwards once", p.Count(l => l == "KEYDOWN vk=51") == 1, string.Join("; ", p));

        // A non-number key over the overlay is yours.
        IntoGame(); Move(OverlayMid); Thread.Sleep(100);
        w0 = Lines("Warrior").Length; p0 = Lines("Priest").Length;
        Key('Q', false); Thread.Sleep(30); Key('Q', true); Thread.Sleep(500);
        Check("Q over overlay goes to the Warrior, not the Priest",
              Lines("Warrior").Skip(w0).Contains("KEYDOWN vk=81") && !Lines("Priest").Skip(p0).Any(l => l.EndsWith("vk=81")));

        // 1 away from the overlay is yours too.
        w0 = Lines("Warrior").Length; p0 = Lines("Priest").Length;
        Move(Away); Thread.Sleep(100);
        Key('1', false); Thread.Sleep(30); Key('1', true); Thread.Sleep(600);
        Check("1 away from overlay goes to the Warrior only",
              Lines("Warrior").Skip(w0).Contains("KEYDOWN vk=49") && !Lines("Priest").Skip(p0).Any(l => l.EndsWith("vk=49")),
              string.Join("; ", Lines("Warrior").Skip(w0)));
    }

    void WheelTest()
    {
        IntoGame();
        int p0 = Lines("Priest").Length, w0 = Lines("Warrior").Length;
        Move(OverlayMid); Thread.Sleep(100);
        report.Add("info  before wheel: " + Where());
        Mouse(N.WHEEL, 120); Thread.Sleep(800);
        report.Add("info  after wheel: " + Where());
        var p = Lines("Priest").Skip(p0).ToList(); var w = Lines("Warrior").Skip(w0).ToList();
        Check("wheel up over overlay reaches the Priest, one notch", p.Count(l => l.StartsWith("WHEEL delta=120")) == 1, string.Join("; ", p));
        Check("...and not the Warrior", !w.Any(l => l.StartsWith("WHEEL")), string.Join("; ", w));
    }

    void HotkeyTests()
    {
        // Ctrl+Alt+L unlocks: a drag then moves the overlay and nothing goes through.
        Chord('L'); Thread.Sleep(500);
        int p0 = Lines("Priest").Length;
        var overlayWnd = N.FindWindow(null, "Glass");
        Move(OverlayMid); Mouse(N.LEFTDOWN); Thread.Sleep(100);
        for (int i = 1; i <= 10; i++) { N.SetCursorPos(OverlayMid.X - 4 * i, OverlayMid.Y - 3 * i); Mouse(N.MOVE); Thread.Sleep(30); }
        Thread.Sleep(100); Mouse(N.LEFTUP); Thread.Sleep(700);
        N.GetWindowRect(overlayWnd, out var moved);
        Check("unlocked (Ctrl+Alt+L): a drag moves the overlay", moved.L == 560 && moved.T == 370, moved.L + "," + moved.T);
        Check("...and nothing goes through", !Lines("Priest").Skip(p0).Any(l => l.StartsWith("LDOWN")),
              string.Join("; ", Lines("Priest").Skip(p0)));
        // Drag it home again.
        var from = new Point(OverlayMid.X - 40, OverlayMid.Y - 30);
        Move(from); Mouse(N.LEFTDOWN); Thread.Sleep(100);
        for (int i = 1; i <= 10; i++) { N.SetCursorPos(from.X + 4 * i, from.Y + 3 * i); Mouse(N.MOVE); Thread.Sleep(30); }
        Thread.Sleep(100); Mouse(N.LEFTUP); Thread.Sleep(700);
        N.GetWindowRect(overlayWnd, out moved);
        Check("dragged back to 600,400", moved.L == 600 && moved.T == 400, moved.L + "," + moved.T);
        Chord('L'); Thread.Sleep(500);
        Move(Away); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(400);   // back into the game
        p0 = Lines("Priest").Length;
        Move(OverlayMid); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(800);
        Check("locked again: click goes through", Lines("Priest").Skip(p0).Any(l => l.StartsWith("LDOWN at=150,100")),
              string.Join("; ", Lines("Priest").Skip(p0)));
        N.GetCursorPos(out var c);

        // Ctrl+Alt+H hides it: then the number row is the Warrior's again.
        Chord('H'); Thread.Sleep(600);
        var overlay = N.FindWindow(null, "Glass");
        Check("Ctrl+Alt+H hides the overlay", overlay != IntPtr.Zero && !N.IsWindowVisible(overlay));
        int w0 = Lines("Warrior").Length; p0 = Lines("Priest").Length;
        Key('1', false); Thread.Sleep(30); Key('1', true); Thread.Sleep(600);
        Check("hidden: 1 goes to the Warrior", Lines("Warrior").Skip(w0).Contains("KEYDOWN vk=49")
              && !Lines("Priest").Skip(p0).Any(l => l.EndsWith("vk=49")));
        Chord('H'); Thread.Sleep(600);
        Check("Ctrl+Alt+H shows it again", N.IsWindowVisible(overlay));
        Check("hotkeys never leak to the Warrior as L or H", !Lines("Warrior").Any(l => l == "KEYDOWN vk=76" || l == "KEYDOWN vk=72"));
    }

    void HeldKeyEdgeTests()
    {
        // Pressed over the overlay, held while the pointer leaves: every repeat and the release
        // stay with the Priest's forwarded press. The Warrior must see nothing of it.
        IntoGame(); Move(OverlayMid); Thread.Sleep(100);
        int p0 = Lines("Priest").Length, w0 = Lines("Warrior").Length;
        Key('4', false); Thread.Sleep(60);
        Move(Away); Key('4', false); Thread.Sleep(40); Key('4', false); Thread.Sleep(40); Key('4', true);
        Thread.Sleep(800);
        var p = Lines("Priest").Skip(p0).ToList(); var w = Lines("Warrior").Skip(w0).ToList();
        Check("held 4, pointer leaves the overlay: Priest got one press", p.Count(l => l == "KEYDOWN vk=52") == 1, string.Join("; ", p));
        Check("...and the Warrior got no repeat and no stray release", !w.Any(l => l.EndsWith("vk=52")), string.Join("; ", w));

        // Pressed off the overlay, held while the pointer arrives: it stays the Warrior's.
        p0 = Lines("Priest").Length; w0 = Lines("Warrior").Length;
        Move(Away); Thread.Sleep(100);
        Key('5', false); Thread.Sleep(60);
        Move(OverlayMid); Key('5', false); Thread.Sleep(40); Key('5', false); Thread.Sleep(40); Key('5', true);
        Thread.Sleep(800);
        p = Lines("Priest").Skip(p0).ToList(); w = Lines("Warrior").Skip(w0).ToList();
        Check("held 5, pointer arrives on the overlay: nothing forwarded", !p.Any(l => l.EndsWith("vk=53")), string.Join("; ", p));
        Check("...and the Warrior got its presses and its release", w.Count(l => l == "KEYDOWN vk=53") == 3 && w.Contains("KEYUP vk=53"),
              string.Join("; ", w));
    }

    void ClipTest()
    {
        // The game you are playing locks the cursor to its window, as WoW's "Lock Cursor to
        // Window" does. Glass cannot reach the other client and must refuse, not click here.
        IntoGame();
        var clip = new N.RECT { L = WarriorRect.Left, T = WarriorRect.Top, R = WarriorRect.Right, B = WarriorRect.Bottom };
        N.ClipCursor(ref clip);
        int p0 = Lines("Priest").Length, w0 = Lines("Warrior").Length;
        Move(OverlayMid); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(800);
        Key('6', false); Thread.Sleep(30); Key('6', true); Thread.Sleep(800);
        N.ClipCursor(IntPtr.Zero);
        var p = Lines("Priest").Skip(p0).ToList(); var w = Lines("Warrior").Skip(w0).ToList();
        var log = GlassLogSince();
        Check("cursor locked to the Warrior: click and key refused, logged", log.Contains("could not reach"), "");
        Check("...nothing reached the Priest", !p.Any(l => l.StartsWith("LDOWN") || l.EndsWith("vk=54")), string.Join("; ", p));
        Check("...and nothing landed on the Warrior", !w.Any(l => l.StartsWith("LDOWN") || l.EndsWith("vk=54")), string.Join("; ", w));
    }

    void CoverTest()
    {
        // Drag the overlay on top of the region it mirrors, then click it. The click would land
        // back on the overlay; it must be refused once, not loop.
        IntoGame();
        var overlay = N.FindWindow(null, "Glass");
        Chord('L'); Thread.Sleep(500);
        Drag(OverlayMid, new Point(140, 90));
        N.GetWindowRect(overlay, out var r);
        Check("unlocked drag onto the Priest's region", r.L == 40 && r.T == 40, r.L + "," + r.T);
        Thread.Sleep(600);
        Check("the dragged position is saved", SettingsText().Replace(" ", "").Replace("\n", "").Replace("\r", "").Contains("\"overlay\":[40,40]"),
              SettingsText().Replace("\n", " "));
        Chord('L'); Thread.Sleep(500);
        int clicks0 = Count(GlassLogSince(), "click Left");
        int p0 = Lines("Priest").Length;
        Move(new Point(140, 90)); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(1500);
        var log = GlassLogSince();
        Check("mirror over its own region: click refused with a warning", log.Contains("warning: The mirror covers what it mirrors"));
        Check("...and no click loop", Count(log, "click Left") == clicks0, (Count(log, "click Left") - clicks0) + " extra clicks");
        // Put it back where the rest of the run expects it.
        Chord('L'); Thread.Sleep(500);
        WaitUncovered(new Point(140, 90), overlay, "dragging home");
        report.Add("info  before dragging home: under pointer " + At(new Point(140, 90)) + " (glass pid " + app.Id + ")");
        Drag(new Point(140, 90), OverlayMid);
        Chord('L'); Thread.Sleep(500);
        N.GetWindowRect(overlay, out r);
        Check("dragged home again", r.L == 600 && r.T == 400, r.L + "," + r.T);
    }

    /// Control for StepAside: does this platform let a click through a window made layered
    /// and transparent after it was created? Warrior below, a plain window on top.
    public static int ProbeTransparent(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var d = new Driver("", outDir, "");
        var w = Process.Start(new ProcessStartInfo(Environment.ProcessPath,
            $"target Warrior 0 0 1024 768 \"{d.LogOf("Warrior")}\"") { UseShellExecute = false });
        var ww = WaitWindow("Warrior");
        var c = Process.Start(new ProcessStartInfo(Environment.ProcessPath, $"noact \"{d.LogOf("NoActivate")}\"") { UseShellExecute = false });
        var cw = WaitWindow("NoActivate");
        Thread.Sleep(15000);                              // Wine's first balloon, if any, clears
        var p = new Point(490, 250);
        Console.WriteLine("before: " + At(p));
        int ex = N.GetWindowLong(cw, -20);
        N.SetWindowLong(cw, -20, ex | 0x20 | 0x80000);
        Thread.Sleep(200);
        Console.WriteLine("after:  " + At(p));
        Move(p); Mouse(N.LEFTDOWN); Thread.Sleep(30); Mouse(N.LEFTUP); Thread.Sleep(500);
        Console.WriteLine("warrior got: " + string.Join("; ", d.Lines("Warrior").Where(l => l.StartsWith("LDOWN"))));
        Console.WriteLine("control got: " + string.Join("; ", d.Lines("NoActivate")));
        try { w.Kill(); c.Kill(); } catch { }
        return 0;
    }

    static int Count(string s, string what) { int n = 0, i = 0; while ((i = s.IndexOf(what, i)) >= 0) { n++; i += what.Length; } return n; }

    static string SettingsText()
    {
        try { return File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glass", "settings.json")); }
        catch { return ""; }
    }

    static void Drag(Point from, Point to)
    {
        Move(from); Mouse(N.LEFTDOWN); Thread.Sleep(100);
        for (int i = 1; i <= 10; i++)
        {
            N.SetCursorPos(from.X + (to.X - from.X) * i / 10, from.Y + (to.Y - from.Y) * i / 10);
            Mouse(N.MOVE); Thread.Sleep(30);
        }
        Thread.Sleep(100); Mouse(N.LEFTUP); Thread.Sleep(700);
    }

    void SecondLaunchTest()
    {
        var second = Process.Start(new ProcessStartInfo(glass, "--settings WoW") { UseShellExecute = false });
        bool exited = second.WaitForExit(20000);
        Check("second launch hands over and exits", exited && second.ExitCode == 0, exited ? "exit " + second.ExitCode : "still running");
        var settings = WaitWindow("Glass Settings", 8000);
        Check("second launch opens Settings in the running copy", settings != IntPtr.Zero && N.IsWindowVisible(settings));
        Check("still one Glass running", !app.HasExited);

        var desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Glass-report.txt");
        try { File.Delete(desk); } catch { }
        var rep = Process.Start(new ProcessStartInfo(glass, "--report") { UseShellExecute = false });
        bool repDone = rep.WaitForExit(30000);
        var text = File.Exists(desk) ? File.ReadAllText(desk) : "";
        Check("--report saves a report to the Desktop", repDone && rep.ExitCode == 0 && text.StartsWith("GLASS REPORT"), desk);
        Check("...with a summary, displays, settings and the log", text.Contains("== What looks wrong ==") && text.Contains("== Displays ==")
              && text.Contains("\"region\"") && text.Contains("click Left at 150,100"));
        File.Copy(desk, Path.Combine(outDir, "Glass-report.txt"), true);

        var reset = Process.Start(new ProcessStartInfo(glass, "--reset") { UseShellExecute = false });
        bool done = reset.WaitForExit(20000);
        Check("--reset while running is refused", done && reset.ExitCode == 1, done ? "exit " + reset.ExitCode : "still running");
        Check("...and the settings keep the region", SettingsText().Contains("\"region\""));
    }

    // MARK: - Plumbing

    Process StartTarget(string name, Rectangle r) =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath,
            $"target {name} {r.X} {r.Y} {r.Width} {r.Height} \"{LogOf(name)}\"") { UseShellExecute = false });

    static IntPtr WaitWindow(string title, int ms = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            var h = N.FindWindow(null, title);
            if (h != IntPtr.Zero && N.IsWindowVisible(h)) return h;
            Thread.Sleep(100);
        }
        return IntPtr.Zero;
    }

    /// Who holds capture, activation and focus, system-wide and on each stand-in's thread.
    string Where()
    {
        var parts = new List<string>();
        foreach (var (label, h) in new[] { ("fg", N.GetForegroundWindow()), ("overlay", N.FindWindow(null, "Glass")), ("priest", priestWnd), ("warrior", warriorWnd) })
        {
            if (h == IntPtr.Zero) continue;
            uint tid = label == "fg" ? 0 : N.GetWindowThreadProcessId(h, out _);
            var gi = new N.GUITHREADINFO { cbSize = Marshal.SizeOf<N.GUITHREADINFO>() };
            if (N.GetGUIThreadInfo(tid, ref gi))
                parts.Add(label + ": active=" + Title(gi.active) + " focus=" + Title(gi.focus) + " capture=" + (gi.capture == IntPtr.Zero ? "-" : Title(gi.capture) + "/" + gi.capture) + " flags=" + gi.flags);
        }
        return string.Join(" | ", parts);
    }

    /// Wine has no taskbar, so it draws tray balloons in the top-left corner -- on top of the
    /// Priest's frames. Windows shows them bottom-right. Wait until `p` shows `want` again.
    void WaitUncovered(Point p, IntPtr want, string why)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 30000)
        {
            var h = N.WindowFromPoint(new N.POINT { X = p.X, Y = p.Y });
            if (h == want || N.GetAncestor(h, 2) == want) break;
            Thread.Sleep(250);
        }
        report.Add("info  " + why + ": waited " + sw.ElapsedMilliseconds + "ms for Wine's tray balloon to clear " + p.X + "," + p.Y);
    }

    /// Wine's tray balloon sits top-left, over the Priest's region; a real click there would land in it.
    void WaitNoBalloon(Point p)
    {
        // Any balloon over the spot, not just the top window: Glass's mirror may sit above it.
        bool Balloon()
        {
            bool found = false;
            N.EnumWindows((h, _) =>
            {
                var cls = new StringBuilder(64); N.GetClassName(h, cls, 64);
                if (cls.ToString() == "tooltips_class32" && N.IsWindowVisible(h) && N.GetWindowRect(h, out var r)
                    && p.X >= r.L && p.X < r.R && p.Y >= r.T && p.Y < r.B) { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 30000 && Balloon()) Thread.Sleep(250);
        report.Add("info  waited " + sw.ElapsedMilliseconds + "ms for Wine's tray balloon to clear " + p.X + "," + p.Y);
    }

    static string At(Point p)
    {
        var h = N.WindowFromPoint(new N.POINT { X = p.X, Y = p.Y });
        var cls = new StringBuilder(256); N.GetClassName(h, cls, 256);
        N.GetWindowThreadProcessId(h, out uint pid);
        return "\"" + Title(h) + "\" class=" + cls + " pid=" + pid;
    }

    static string Title(IntPtr h) { var sb = new StringBuilder(256); N.GetWindowText(h, sb, 256); return sb.ToString(); }

    static void Move(Point p) { N.SetCursorPos(p.X, p.Y); Thread.Sleep(80); }

    static void Mouse(uint flags, int data = 0) =>
        N.SendInput(1, new[] { new N.INPUT { type = 0, u = new N.U { mi = new N.MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } } } },
                    Marshal.SizeOf<N.INPUT>());

    static void Key(int vk, bool up) =>
        N.SendInput(1, new[] { new N.INPUT { type = 1, u = new N.U { ki = new N.KEYBDINPUT { wVk = (ushort)vk,
                    wScan = (ushort)N.MapVirtualKey((uint)vk, 0), dwFlags = up ? 2u : 0u } } } }, Marshal.SizeOf<N.INPUT>());

    static void Chord(char k)
    {
        Key(N.VK_CONTROL, false); Key(N.VK_MENU, false); Key(k, false);
        Thread.Sleep(30);
        Key(k, true); Key(N.VK_MENU, true); Key(N.VK_CONTROL, true);
    }
}

static class N
{
    public const uint MOVE = 0x1, LEFTDOWN = 0x2, LEFTUP = 0x4, RIGHTDOWN = 0x8, RIGHTUP = 0x10, WHEEL = 0x800;
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] public struct U { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public U u; }

    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool ClipCursor(ref RECT r);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool ClipCursor(IntPtr r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO
    { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public RECT rc; }
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO gi);
}
