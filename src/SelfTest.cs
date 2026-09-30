// Glass.exe --selftest DIR
//
// Builds every window Glass has and draws each to a PNG in DIR without putting anything on
// screen, and writes out the WoW command text, so a build can be checked on a machine that is
// not set up to play. It registers no hotkeys and captures nothing.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Glass
{
    public static class SelfTest
    {
        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            var report = new List<string>();
            int failures = 0;

            void Check(string name, Action a)
            {
                try { a(); report.Add("ok    " + name); }
                catch (Exception e) { failures++; report.Add("FAIL  " + name + ": " + e); }
            }

            Check("displays", () =>
            {
                var all = Screens.All();
                if (all.Count == 0) throw new Exception("no displays");
                foreach (var d in all) report.Add("        " + d);
            });

            Check("windows", () => report.Add("        " + Wnd.AllOrdinary().Count + " ordinary windows"));

            Check("wow commands", () =>
            {
                foreach (var (name, list) in new[] { ("Everyone", Wow.Shared) }
                         .Concat(WoWRoles.All.Select(r => (WoWRoles.Title(r), Wow.For(r)))))
                {
                    var lines = Wow.ConsoleCommands(list);
                    File.WriteAllLines(Path.Combine(dir, "wow-" + name + ".txt"), lines);
                    File.WriteAllText(Path.Combine(dir, "wow-" + name + "-macros.txt"),
                                      string.Join("\n\n---\n\n", Wow.MacroChunks(lines)));
                    if (Wow.MacroChunks(lines).Any(c => c.Length > 255)) throw new Exception("macro over 255");
                }
            });

            Check("addon config", () => File.WriteAllText(Path.Combine(dir, "Config.lua"), Wow.AddonConfigLua()));

            Check("shortcuts", () =>
            {
                foreach (var c in Commands.All) report.Add("        " + Commands.Label(c) + "  " + Saved.GetShortcut(c).Display);
            });

            Check("settings window", () =>
            {
                using (var f = new SettingsForm())
                {
                    var h = f.Handle;
                    foreach (var (name, image) in f.Snapshots())
                    {
                        using (image) image.Save(Path.Combine(dir, "settings-" + name + ".png"), ImageFormat.Png);
                        File.WriteAllLines(Path.Combine(dir, "settings-" + name + ".txt"), Layout(f));
                    }
                }
            });

            Check("header", () =>
            {
                using (var bar = new HeaderBar())
                {
                    var h = bar.Handle;
                    bar.Place(new Rectangle(200, 300, 300, 200));
                    bar.SetCompact(true);
                    SaveSurface(bar, Path.Combine(dir, "header-compact.png"));
                    bar.SetCompact(false);
                    SaveSurface(bar, Path.Combine(dir, "header-full.png"));
                }
            });

            Check("picker", () =>
            {
                var d = new Display { Bounds = new Rectangle(0, 0, 1280, 720), Dpi = 96 };
                using (var p = new PickerForm(d, "Drag to choose what to mirror  ·  Esc to cancel"))
                {
                    var h = p.Handle;
                    p.Render(null, null, true);
                    using (var a = p.Snapshot()) a.Save(Path.Combine(dir, "picker-hint.png"), ImageFormat.Png);
                    p.Render(null, new Rectangle(400, 200, 320, 180), false);
                    using (var b = p.Snapshot()) b.Save(Path.Combine(dir, "picker-selection.png"), ImageFormat.Png);
                }
            });

            Check("overlay", () =>
            {
                using (var o = new OverlayForm(new Rectangle(0, 0, 240, 160), 1.5, 0.9, new Point(100, 100)))
                {
                    var h = o.Handle;
                    if (o.Width != 360 || o.Height != 240) throw new Exception("scaled size " + o.Size);
                    var state = new HookState { Overlay = o.Bounds, Source = o.SourceRect };
                    var mid = state.SourcePoint(new Point(o.Left + o.Width / 2, o.Top + o.Height / 2));
                    if (mid != new Point(120, 80)) throw new Exception("centre maps to " + mid);
                    var corner = state.SourcePoint(new Point(o.Left, o.Top));
                    if (corner != new Point(0, 0)) throw new Exception("corner maps to " + corner);
                    var far = state.SourcePoint(new Point(o.Right - 1, o.Bottom - 1));
                    if (far != new Point(239, 159)) throw new Exception("far corner maps to " + far);
                }
            });

            Check("hook thread", () =>
            {
                Hooks.Start();
                report.Add("        keyboard hook " + (Hooks.Installed ? "installed" : "NOT installed"));
                Hooks.Stop();
            });

            Check("settings round trip", () =>
            {
                var before = Saved.Scale;
                Saved.Scale = 1.25;
                if (Math.Abs(Saved.Scale - 1.25) > 1e-9) throw new Exception("scale did not stick");
                Saved.Scale = before;
                Saved.FlushNow();
                if (!File.Exists(Saved.Path_)) throw new Exception("no settings file at " + Saved.Path_);
            });

            report.Add(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            File.WriteAllLines(Path.Combine(dir, "selftest.txt"), report);
            foreach (var line in report) Console.WriteLine(line);
            Saved.FlushNow();
            return failures == 0 ? 0 : 1;
        }

        /// The client area of a window that was never shown, with everything in it. Wine's WM_PRINT
        /// ignores PRF_CHILDREN, so each child window is printed on its own and laid over its
        /// parent, clipped to what the parent shows. On Windows the result is the same.
        public static Bitmap Render(Control root)
        {
            CreateHandles(root);
            var bmp = new Bitmap(root.ClientSize.Width, root.ClientSize.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(root.BackColor);
                var all = new Rectangle(Point.Empty, root.ClientSize);
                foreach (var c in root.Controls.Cast<Control>().Reverse()) Draw(g, root, c, all);
            }
            return bmp;
        }

        /// Every shown control with its place in the window, for finding what a picture cannot
        /// say: which control covers which, and what was cut short.
        static List<string> Layout(Control root)
        {
            var lines = new List<string> { "dpi " + root.DeviceDpi + "  client " + root.ClientSize.Width + "x" + root.ClientSize.Height };
            void Walk(Control c, int depth)
            {
                if ((Native.GetWindowLong(c.Handle, Native.GWL_STYLE) & Native.WS_VISIBLE) == 0) return;
                var r = root.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
                var text = c.Text.Length > 40 ? c.Text.Substring(0, 40) + "…" : c.Text;
                var want = c is Label l && !l.AutoSize ? "  needs " + TextRenderer.MeasureText(c.Text, c.Font).Width : "";
                lines.Add(string.Format("{0}{1} {2},{3} {4}x{5}  \"{6}\"{7}", new string(' ', depth * 2), c.GetType().Name,
                                        r.X, r.Y, r.Width, r.Height, text.Replace("\n", " "), want));
                foreach (Control child in c.Controls) Walk(child, depth + 1);
            }
            foreach (Control c in root.Controls) Walk(c, 0);
            return lines;
        }

        /// Handles are made as a form is shown, and this one never is.
        static void CreateHandles(Control c)
        {
            var _ = c.Handle;
            foreach (Control child in c.Controls) CreateHandles(child);
        }

        static void Draw(Graphics g, Control root, Control c, Rectangle clip)
        {
            // Visible reports false for everything on a hidden form; the window's own style says
            // whether it would be drawn.
            if ((Native.GetWindowLong(c.Handle, Native.GWL_STYLE) & Native.WS_VISIBLE) == 0) return;
            if (c.Width <= 0 || c.Height <= 0) return;
            var bounds = root.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
            var shown = Rectangle.Intersect(bounds, clip);
            if (shown.IsEmpty) return;
            using (var image = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(image, new Rectangle(Point.Empty, c.Size));
                g.SetClip(shown);
                g.DrawImage(image, bounds.Location);
                g.ResetClip();
            }
            var inside = Rectangle.Intersect(shown, root.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)));
            foreach (var child in c.Controls.Cast<Control>().Reverse()) Draw(g, root, child, inside);
        }

        static void SaveSurface(HeaderBar bar, string path)
        {
            // The header draws into a layered surface; grab it through a render at full alpha.
            var field = typeof(HeaderBar).GetField("surface",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var surface = (LayeredSurface)field.GetValue(bar);
            using (var copy = new Bitmap(surface.Width * 3, surface.Height * 3))
            using (var g = Graphics.FromImage(copy))
            {
                g.Clear(Color.FromArgb(70, 90, 110));          // a stand-in for a game behind it
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.DrawImage(surface.Bitmap, 0, 0, copy.Width, copy.Height);
                copy.Save(path, ImageFormat.Png);
            }
        }
    }
}
