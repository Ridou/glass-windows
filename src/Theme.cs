// The look: dark marble in a metal frame, gold lettering with a hard black shadow, red panel
// buttons, square icon tabs and dark blue tooltips. The Windows half of the macOS build's
// `WoW` section in glass.swift; the two are kept deliberately identical, down to the palette
// and the noise that makes the marble, so the same app is recognisable on either machine.
//
// Nothing here is lifted from any game. The textures are generated, the icons are drawn, and
// Marcellus (SIL OFL, fonts/) stands in for the flared serif the look wants.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Glass
{
    public static class Theme
    {
        // MARK: - Palette

        public static readonly Color Gold = Rgb(1, 0.82, 0);          // normal text
        public static readonly Color White = Rgb(1, 1, 1);            // highlight text
        public static readonly Color Body = Rgb(0.92, 0.90, 0.85);
        public static readonly Color Hint = Rgb(0.66, 0.63, 0.57);
        public static readonly Color Grey = Rgb(0.5, 0.5, 0.5);       // disabled
        public static readonly Color Green = Rgb(0.2, 1, 0.2);
        public static readonly Color Yellow = Rgb(1, 1, 0.35);        // system messages
        public static readonly Color Edge = Rgb(0.48, 0.46, 0.40);    // tooltip-style border
        public static readonly Color Lit = Rgb(1, 0.78, 0.25);        // a selected tab's border
        public static readonly Color Bronze = Rgb(0.66, 0.50, 0.23);
        public static readonly Color Row = Rgb(0.30, 0.23, 0.12);     // list row tint

        public static Color Rgb(double r, double g, double b, double a = 1) =>
            Color.FromArgb((int)(a * 255), (int)(r * 255), (int)(g * 255), (int)(b * 255));

        static Color Fade(Color c, double a) => Color.FromArgb((int)(a * 255), c.R, c.G, c.B);

        // MARK: - Type

        static PrivateFontCollection fonts;
        static FontFamily serif;

        /// The bundled face, or the nearest thing Windows ships if it will not load.
        public static FontFamily Serif
        {
            get
            {
                if (serif != null) return serif;
                try
                {
                    var bytes = Resource("Marcellus-Regular.ttf");
                    fonts = new PrivateFontCollection();
                    var p = Marshal.AllocCoTaskMem(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, p, bytes.Length);
                        fonts.AddMemoryFont(p, bytes.Length);
                    }
                    finally { Marshal.FreeCoTaskMem(p); }
                    serif = fonts.Families[0];
                }
                catch (Exception e)
                {
                    Log.Write("bundled font unavailable, falling back: " + e.Message);
                    try { serif = new FontFamily("Palatino Linotype"); }
                    catch { serif = FontFamily.GenericSerif; }
                }
                return serif;
            }
        }

        static byte[] Resource(string name)
        {
            using (var s = typeof(Theme).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) throw new InvalidOperationException("missing embedded resource " + name);
                using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
            }
        }

        static readonly Dictionary<float, Font> fontCache = new Dictionary<float, Font>();
        static readonly Dictionary<float, Font> narrowCache = new Dictionary<float, Font>();
        static readonly Dictionary<float, Font> digitCache = new Dictionary<float, Font>();
        static FontFamily digitFamily;

        /// Marcellus draws 1 and 0 like I and O, so "10" reads as "IO" and a region size is
        /// unreadable. Digits come from a text serif that sits well beside it instead.
        static FontFamily DigitFamily
        {
            get
            {
                if (digitFamily != null) return digitFamily;
                foreach (var name in new[] { "Palatino Linotype", "Georgia", "Constantia", "Times New Roman" })
                {
                    try { digitFamily = new FontFamily(name); return digitFamily; } catch { }
                }
                digitFamily = FontFamily.GenericSerif;
                return digitFamily;
            }
        }

        static Font Digits(Font f)
        {
            lock (digitCache)
            {
                if (!digitCache.TryGetValue(f.Size, out var d))
                    digitCache[f.Size] = d = new Font(DigitFamily, f.Size, f.Style, GraphicsUnit.Point);
                return d;
            }
        }

        static bool IsSerif(Font f) => ReferenceEquals(f.FontFamily, Serif) || f.FontFamily.Name == Serif.Name;

        /// Split into digit and non-digit runs so each is drawn in the face that suits it.
        static List<(string text, Font font)> Runs(string s, Font f)
        {
            var outv = new List<(string, Font)>();
            int i = 0;
            while (i < s.Length)
            {
                bool digit = char.IsDigit(s[i]);
                int j = i;
                while (j < s.Length && char.IsDigit(s[j]) == digit) j++;
                outv.Add((s.Substring(i, j - i), digit ? Digits(f) : f));
                i = j;
            }
            return outv;
        }

        public static Font F(float size)
        {
            lock (fontCache)
            {
                if (!fontCache.TryGetValue(size, out var f))
                    fontCache[size] = f = new Font(Serif, size, FontStyle.Regular, GraphicsUnit.Point);
                return f;
            }
        }

        /// Running prose. Marcellus is a display face: fine for a heading or a button, tiring
        /// over a paragraph, and its 1 and 0 read as I and O, which no run-splitting can fix
        /// once the text wraps. Paragraphs get the text serif instead.
        public static Font Prose(float size)
        {
            lock (proseCache)
            {
                if (!proseCache.TryGetValue(size, out var f))
                    proseCache[size] = f = new Font(DigitFamily, size, FontStyle.Regular, GraphicsUnit.Point);
                return f;
            }
        }

        static readonly Dictionary<float, Font> proseCache = new Dictionary<float, Font>();

        /// Numbers and console text want a narrow face, as the game's own UI does.
        public static Font Narrow(float size)
        {
            lock (narrowCache)
            {
                if (!narrowCache.TryGetValue(size, out var f))
                {
                    try { f = new Font("Arial Narrow", size, FontStyle.Regular, GraphicsUnit.Point); }
                    catch { f = new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Point); }
                    narrowCache[size] = f;
                }
                return f;
            }
        }

        /// Every string carries a hard one-pixel black shadow. It is most of why gold text
        /// reads the way it does rather than as plain yellow.
        public static void DrawText(Graphics g, string s, Font f, Color c, RectangleF r,
                                    StringAlignment align = StringAlignment.Near,
                                    StringAlignment lineAlign = StringAlignment.Center,
                                    bool wrap = false)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (!wrap && IsSerif(f) && HasDigit(s)) { DrawRuns(g, s, f, c, r, align, lineAlign); return; }
            using (var fmt = Format(align, lineAlign, wrap))
            {
                var old = g.TextRenderingHint;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                    g.DrawString(s, f, shadow, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), fmt);
                using (var brush = new SolidBrush(c))
                    g.DrawString(s, f, brush, r, fmt);
                g.TextRenderingHint = old;
            }
        }

        static bool HasDigit(string s)
        {
            foreach (var ch in s) if (char.IsDigit(ch)) return true;
            return false;
        }

        /// One line, drawn run by run so the digits can come from another face. Alignment is
        /// applied to the measured whole, so a centred label still centres.
        static void DrawRuns(Graphics g, string s, Font f, Color c, RectangleF r,
                             StringAlignment align, StringAlignment lineAlign)
        {
            var runs = Runs(s, f);
            float total = 0, height = 0;
            var widths = new float[runs.Count];
            using (var fmt = StringFormat.GenericTypographic)
            {
                var m = new StringFormat(fmt) { FormatFlags = fmt.FormatFlags | StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
                for (int i = 0; i < runs.Count; i++)
                {
                    var size = g.MeasureString(runs[i].text, runs[i].font, int.MaxValue, m);
                    widths[i] = size.Width;
                    total += size.Width;
                    height = Math.Max(height, size.Height);
                }
                float x = align == StringAlignment.Center ? r.X + (r.Width - total) / 2
                        : align == StringAlignment.Far ? r.Right - total : r.X;
                float y = lineAlign == StringAlignment.Center ? r.Y + (r.Height - height) / 2
                        : lineAlign == StringAlignment.Far ? r.Bottom - height : r.Y;
                var old = g.TextRenderingHint;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                using (var brush = new SolidBrush(c))
                    for (int i = 0; i < runs.Count; i++)
                    {
                        g.DrawString(runs[i].text, runs[i].font, shadow, x + 1, y + 1, m);
                        g.DrawString(runs[i].text, runs[i].font, brush, x, y, m);
                        x += widths[i];
                    }
                g.TextRenderingHint = old;
                m.Dispose();
            }
        }

        /// One description of how text is laid out, used for both measuring and drawing.
        /// When the two disagree even slightly, a measured height wraps differently from the
        /// drawn text and the last line is quietly cut off.
        static StringFormat Format(StringAlignment align, StringAlignment lineAlign, bool wrap)
        {
            // Not FitBlackBox: that flag lets glyphs hang outside the layout rectangle, and
            // the control then clips them, so the last word of a wrapped line loses its tail.
            var fmt = new StringFormat
            {
                Alignment = align,
                LineAlignment = lineAlign,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            if (!wrap) fmt.FormatFlags |= StringFormatFlags.NoWrap;
            return fmt;
        }

        public static SizeF Measure(Graphics g, string s, Font f, float maxWidth, bool wrap = true)
        {
            using (var fmt = Format(StringAlignment.Near, StringAlignment.Near, wrap))
                return g.MeasureString(s ?? "", f, (int)maxWidth, fmt);
        }

        // MARK: - Marble

        const int Side = 512;
        static float[] field;

        /// Clouded marble: value noise summed over octaves, a few faint veins, then grain.
        /// The same construction as the macOS build, so both look like one product.
        static float[] Field()
        {
            if (field != null) return field;
            int n = Side;
            ulong seed = 0x9E3779B97F4A7C15UL;
            Func<float> rand = () =>
            {
                seed = seed * 6364136223846793005UL + 1442695040888963407UL;
                return (seed >> 40) / (float)(1 << 24);
            };
            var f = new float[n * n];
            float amp = 1, total = 0;
            foreach (int cells in new[] { 3, 7, 17, 43, 120 })
            {
                var grid = new float[(cells + 1) * (cells + 1)];
                for (int i = 0; i < grid.Length; i++) grid[i] = rand();
                float scale = cells / (float)n;
                for (int y = 0; y < n; y++)
                {
                    float gy = y * scale; int y0 = (int)gy; float ty = gy - y0, sy = ty * ty * (3 - 2 * ty);
                    for (int x = 0; x < n; x++)
                    {
                        float gx = x * scale; int x0 = (int)gx; float tx = gx - x0, sx = tx * tx * (3 - 2 * tx);
                        int i = y0 * (cells + 1) + x0;
                        float top = grid[i] + (grid[i + 1] - grid[i]) * sx;
                        float bot = grid[i + cells + 1] + (grid[i + cells + 2] - grid[i + cells + 1]) * sx;
                        f[y * n + x] += amp * (top + (bot - top) * sy);
                    }
                }
                total += amp;
                amp *= 0.5f;
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = f[y * n + x] / total;
                    float vein = (float)Math.Pow(1 - Math.Abs(Math.Sin(x * 0.009 + y * 0.018 + v * 11)), 10);
                    float c = (v - 0.5f) * 2.4f + 0.5f + vein * 0.16f + (rand() - 0.5f) * 0.06f;
                    f[y * n + x] = Math.Min(1, Math.Max(0, c));
                }
            field = f;
            return field;
        }

        static Bitmap Tint((double, double, double) dark, (double, double, double) light)
        {
            var f = Field();
            var bmp = new Bitmap(Side, Side, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, Side, Side), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var px = new byte[Side * Side * 4];
            for (int i = 0; i < Side * Side; i++)
            {
                double v = f[i];
                px[i * 4 + 2] = (byte)((dark.Item1 + (light.Item1 - dark.Item1) * v) * 255);   // B G R A
                px[i * 4 + 1] = (byte)((dark.Item2 + (light.Item2 - dark.Item2) * v) * 255);
                px[i * 4 + 0] = (byte)((dark.Item3 + (light.Item3 - dark.Item3) * v) * 255);
                px[i * 4 + 3] = 255;
            }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        static Bitmap stone, marble;
        /// Behind everything: nearly black stone.
        public static Bitmap Stone => stone ?? (stone = Tint((0.055, 0.050, 0.045), (0.16, 0.15, 0.135)));
        /// Inset panels: the same marble with a warmer wash over it.
        public static Bitmap Marble => marble ?? (marble = Tint((0.10, 0.085, 0.065), (0.25, 0.21, 0.155)));

        /// Fill with a texture, anchored to the control's place in its window so neighbouring
        /// panels share one continuous stone rather than each restarting it.
        public static void Fill(Graphics g, Bitmap tex, RectangleF r, Point origin)
        {
            using (var b = new TextureBrush(tex, WrapMode.TileFlipXY))
            {
                b.TranslateTransform(-origin.X, -origin.Y);
                g.FillRectangle(b, r);
            }
        }

        // MARK: - Gradients

        static Brush Linear(RectangleF r, Color[] colors, float[] stops, float angle)
        {
            // GDI+ refuses a zero-area gradient rectangle.
            var safe = new RectangleF(r.X, r.Y, Math.Max(r.Width, 1), Math.Max(r.Height, 1));
            var b = new LinearGradientBrush(safe, colors[0], colors[colors.Length - 1], angle);
            var blend = new ColorBlend(colors.Length) { Colors = colors, Positions = stops };
            b.InterpolationColors = blend;
            b.WrapMode = WrapMode.TileFlipXY;
            return b;
        }

        static float[] Even(int n)
        {
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = i / (float)(n - 1);
            return s;
        }

        /// Top-to-bottom, the direction almost everything here is lit from.
        public static void FillDown(Graphics g, GraphicsPath p, params Color[] colors)
        {
            using (var b = Linear(p.GetBounds(), colors, Even(colors.Length), 90f)) g.FillPath(b, p);
        }

        public static void FillDown(Graphics g, RectangleF r, params Color[] colors)
        {
            using (var b = Linear(r, colors, Even(colors.Length), 90f)) g.FillRectangle(b, r);
        }

        public static void FillDown(Graphics g, GraphicsPath p, Color[] colors, float[] stops)
        {
            using (var b = Linear(p.GetBounds(), colors, stops, 90f)) g.FillPath(b, p);
        }

        public static readonly Color[] Metal =
            { Rgb(0.46, 0.44, 0.40), Rgb(0.25, 0.24, 0.22), Rgb(0.13, 0.12, 0.11) };
        public static readonly Color[] GoldMetal =
            { Rgb(1, 0.92, 0.62), Rgb(0.82, 0.62, 0.24), Rgb(0.42, 0.28, 0.08) };

        public static GraphicsPath Round(RectangleF r, float radius) => Look.RoundRect(r, radius);

        // MARK: - Frames

        /// A window's metal frame: black outline, a bevelled gunmetal band with a groove down
        /// it, a bronze inner rim, and riveted gold plates on the corners.
        public static void DrawFrame(Graphics g, RectangleF r, float band = 7)
        {
            using (var ring = new GraphicsPath())
            using (var outer = Round(r, 5))
            using (var inner = Round(RectangleF.Inflate(r, -band, -band), 2))
            {
                ring.AddPath(outer, false);
                ring.AddPath(inner, false);
                ring.FillMode = FillMode.Alternate;
                var state = g.Save();
                g.SetClip(ring);
                FillDown(g, r, Metal);
                using (var pen = new Pen(Fade(Color.White, 0.20)))
                using (var p = Round(RectangleF.Inflate(r, -1.5f, -1.5f), 4)) g.DrawPath(pen, p);
                using (var pen = new Pen(Fade(Color.Black, 0.55)))
                using (var p = Round(RectangleF.Inflate(r, -band / 2, -band / 2), 3)) g.DrawPath(pen, p);
                using (var pen = new Pen(Fade(Color.White, 0.10)))
                using (var p = Round(RectangleF.Inflate(r, -band / 2 - 1, -band / 2 - 1), 3)) g.DrawPath(pen, p);
                g.Restore(state);
            }

            using (var pen = new Pen(Color.Black))
            using (var p = Round(RectangleF.Inflate(r, -0.5f, -0.5f), 5)) g.DrawPath(pen, p);
            using (var pen = new Pen(Bronze))
            using (var p = Round(RectangleF.Inflate(r, -band + 0.5f, -band + 0.5f), 2)) g.DrawPath(pen, p);
            using (var pen = new Pen(Color.Black))
            using (var p = Round(RectangleF.Inflate(r, -band - 0.5f, -band - 0.5f), 2)) g.DrawPath(pen, p);

            const float plate = 20;
            foreach (var pt in new[]
            {
                new PointF(r.Left, r.Top), new PointF(r.Right - plate, r.Top),
                new PointF(r.Left, r.Bottom - plate), new PointF(r.Right - plate, r.Bottom - plate),
            })
                DrawCorner(g, new RectangleF(pt.X, pt.Y, plate, plate));
        }

        static void DrawCorner(Graphics g, RectangleF r)
        {
            using (var p = Round(RectangleF.Inflate(r, -1, -1), 4))
            {
                FillDown(g, p, GoldMetal);
                using (var pen = new Pen(Fade(Color.Black, 0.35)))
                using (var q = Round(RectangleF.Inflate(r, -4, -4), 2)) g.DrawPath(pen, q);
                using (var pen = new Pen(Color.Black)) g.DrawPath(pen, p);
            }
            DrawStud(g, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), 3.2f);
        }

        public static void DrawStud(Graphics g, PointF c, float radius)
        {
            var r = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (var b = new PathGradientBrush(p))
                {
                    b.CenterPoint = new PointF(c.X - radius * 0.4f, c.Y - radius * 0.4f);
                    b.CenterColor = Rgb(1, 0.96, 0.8);
                    b.SurroundColors = new[] { Rgb(0.3, 0.2, 0.05) };
                    g.FillPath(b, p);
                }
                using (var pen = new Pen(Fade(Color.Black, 0.8))) g.DrawPath(pen, p);
            }
        }

        /// A recessed panel: marble behind a thin border, shadowed along the top.
        public static void DrawInset(Graphics g, RectangleF r, Point origin, Bitmap tex = null)
        {
            using (var p = Round(r, 4))
            {
                var state = g.Save();
                g.SetClip(p);
                Fill(g, tex ?? Marble, r, origin);
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, 11),
                                                       Fade(Color.Black, 0.45), Color.FromArgb(0, 0, 0, 0), 90f))
                    g.FillRectangle(b, new RectangleF(r.X, r.Y, r.Width, 10));
                g.Restore(state);
            }
            using (var pen = new Pen(Color.Black))
            using (var p = Round(RectangleF.Inflate(r, 0.5f, 0.5f), 4.5f)) g.DrawPath(pen, p);
            using (var pen = new Pen(Edge))
            using (var p = Round(RectangleF.Inflate(r, -0.5f, -0.5f), 4)) g.DrawPath(pen, p);
        }

        /// A divider that fades out at both ends.
        public static void DrawRule(Graphics g, RectangleF r)
        {
            var mid = r.Y + r.Height / 2;
            using (var b = new LinearGradientBrush(new RectangleF(r.X, mid, r.Width, 1),
                                                   Color.Transparent, Color.Transparent, 0f))
            {
                b.InterpolationColors = new ColorBlend(3)
                {
                    Colors = new[] { Fade(Bronze, 0), Bronze, Fade(Bronze, 0) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                g.FillRectangle(b, new RectangleF(r.X, mid, r.Width, 1));
            }
            using (var b = new LinearGradientBrush(new RectangleF(r.X, mid + 1, r.Width, 1),
                                                   Color.Transparent, Color.Transparent, 0f))
            {
                b.InterpolationColors = new ColorBlend(3)
                {
                    Colors = new[] { Fade(Color.Black, 0), Fade(Color.Black, 0.8), Fade(Color.Black, 0) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                g.FillRectangle(b, new RectangleF(r.X, mid + 1, r.Width, 1));
            }
        }

        /// The tile's raised edge, and the wash over an unselected one.
        public static void Bevel(Graphics g, RectangleF r, bool dim)
        {
            using (var b = new SolidBrush(Fade(Color.White, 0.35)))
            {
                g.FillRectangle(b, new RectangleF(r.X, r.Y, r.Width, 1));
                g.FillRectangle(b, new RectangleF(r.X, r.Y, 1, r.Height));
            }
            using (var b = new SolidBrush(Fade(Color.Black, 0.6)))
            {
                g.FillRectangle(b, new RectangleF(r.X, r.Bottom - 1, r.Width, 1));
                g.FillRectangle(b, new RectangleF(r.Right - 1, r.Y, 1, r.Height));
            }
            if (dim) using (var b = new SolidBrush(Fade(Color.Black, 0.28))) g.FillRectangle(b, r);
        }
    }
}
