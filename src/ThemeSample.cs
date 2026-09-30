// --theme-sample DIR: draw every piece of the look to one PNG and exit.
//
// The counterpart of the macOS build's --snapshot. Working on the look otherwise means
// launching the app, and a frame or an icon that is two pixels out is not something you can
// judge from a description. No window opens and nothing is asked of Windows, so it runs
// anywhere, including a headless build machine.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Glass
{
    public static class ThemeSample
    {
        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            const int w = 900, h = 560;
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Black);

                var frame = new RectangleF(8, 8, w - 16, h - 16);
                var state = g.Save();
                using (var clip = Theme.Round(RectangleF.Inflate(frame, -4, -4), 3)) g.SetClip(clip);
                Theme.Fill(g, Theme.Stone, frame, Point.Empty);
                g.Restore(state);

                // Title bar
                var bar = new RectangleF(frame.X + 7, frame.Y + 7, frame.Width - 14, 24);
                Theme.FillDown(g, bar, Theme.Rgb(0.20, 0.19, 0.17), Theme.Rgb(0.09, 0.085, 0.08));
                using (var b = new SolidBrush(Theme.Bronze))
                    g.FillRectangle(b, new RectangleF(bar.X, bar.Bottom - 1, bar.Width, 1));
                Theme.DrawText(g, "Glass", Theme.F(11), Theme.Gold, bar, StringAlignment.Center);

                Theme.DrawText(g, "Regions", Theme.F(16), Theme.Gold,
                               new RectangleF(frame.X + 72, bar.Bottom + 2, 300, 34));

                var inset = new RectangleF(frame.X + 10, bar.Bottom + 38, frame.Width - 20, frame.Height - 90);
                Theme.DrawInset(g, inset, Point.Empty);
                Theme.DrawFrame(g, frame);
                ThemeArt.DrawPortrait(g, new PointF(frame.X + 26, frame.Y + 26), 34);

                // The four side-tab icons, selected and not.
                float x = inset.X + 20, y = inset.Y + 20;
                foreach (MacroIcon k in new[] { MacroIcon.Spyglass, MacroIcon.Orb, MacroIcon.Key, MacroIcon.Scroll })
                {
                    ThemeArt.DrawIcon(g, k, new RectangleF(x, y, 38, 38), dim: false);
                    ThemeArt.DrawIcon(g, k, new RectangleF(x, y + 54, 38, 38), dim: true);
                    x += 54;
                }

                // Type specimens
                float ty = inset.Y + 20;
                Theme.DrawText(g, "Gold heading 1234567890", Theme.F(15), Theme.Gold,
                               new RectangleF(inset.X + 250, ty, 400, 24));
                Theme.DrawText(g, "White body text, the readable one.", Theme.F(12), Theme.White,
                               new RectangleF(inset.X + 250, ty + 26, 400, 22));
                Theme.DrawText(g, "A hint, quieter than the rest.", Theme.F(11), Theme.Hint,
                               new RectangleF(inset.X + 250, ty + 50, 400, 22));
                Theme.DrawText(g, "/console autoLootDefault 1", Theme.Narrow(11), Theme.Body,
                               new RectangleF(inset.X + 250, ty + 74, 400, 22));
                Theme.DrawText(g, "In use", Theme.F(12), Theme.Green,
                               new RectangleF(inset.X + 250, ty + 98, 200, 22));

                Theme.DrawRule(g, new RectangleF(inset.X + 20, inset.Y + 150, inset.Width - 40, 3));

                bmp.Save(Path.Combine(dir, "theme.png"), ImageFormat.Png);
            }
            Say("wrote " + Path.Combine(dir, "theme.png"));
            return 0;
        }

        static void Say(string s) => System.Console.Out.WriteLine(s);
    }
}
