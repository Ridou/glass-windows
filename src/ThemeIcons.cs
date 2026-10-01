// The side-tab icons and the window portrait, drawn rather than borrowed.
//
// Each icon is one lit object on a dark vignetted ground with crisp black outlines, in a
// bevelled tile -- the shape a game's macro icon takes. A spyglass for choosing where to
// look, a scrying orb for what it shows, a key for keys, a scroll for commands to paste.
//
// The shapes are given in a 0..1 box with y pointing *up*, exactly as the macOS build states
// them, and a transform turns that into GDI+'s y-down device space. Keeping one set of
// numbers for both builds is the only way they stay the same picture.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Glass
{
    public enum MacroIcon { Spyglass, Orb, Key, Scroll, Tome }

    /// The glyphs that sit on a choice plate. The macOS build takes these from SF Symbols
    /// ("display" and "macwindow"); Windows ships nothing equivalent, so they are drawn to
    /// the same reading: a monitor on a stand, and a window with its title bar filled in.
    public enum PlateGlyph { Display, Window }

    public static partial class ThemeArt
    {
        static Color Ground(MacroIcon k)
        {
            switch (k)
            {
                case MacroIcon.Spyglass: return Theme.Rgb(0.20, 0.17, 0.11);
                case MacroIcon.Orb: return Theme.Rgb(0.13, 0.15, 0.30);
                case MacroIcon.Key: return Theme.Rgb(0.19, 0.16, 0.10);
                case MacroIcon.Tome: return Theme.Rgb(0.14, 0.11, 0.16);
                default: return Theme.Rgb(0.17, 0.14, 0.11);
            }
        }

        public static void DrawIcon(Graphics g, MacroIcon kind, RectangleF r, bool dim = false)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(r);

            // The ground: lit from the upper left, falling away to black at the corners.
            var hue = Ground(kind);
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(RectangleF.Inflate(r, r.Width * 0.35f, r.Height * 0.35f));
                using (var b = new PathGradientBrush(p))
                {
                    b.CenterPoint = new PointF(r.X + r.Width * 0.3f, r.Y + r.Height * 0.3f);
                    b.CenterColor = Blend(hue, Color.White, 0.45);
                    b.SurroundColors = new[] { Color.Black };
                    var blend = new ColorBlend(4)
                    {
                        Colors = new[] { Color.Black, Blend(hue, Color.Black, 0.75), hue, Blend(hue, Color.White, 0.45) },
                        Positions = new[] { 0f, 0.22f, 0.7f, 1f },
                    };
                    b.InterpolationColors = blend;
                    g.FillRectangle(b, r);
                }
            }

            InBox(g, r, Turn(kind), () =>
            {
                switch (kind)
                {
                    case MacroIcon.Spyglass: Spyglass(g); break;
                    case MacroIcon.Orb: Orb(g); break;
                    case MacroIcon.Key: Key(g); break;
                    case MacroIcon.Tome: Tome(g); break;
                    default: Scroll(g); break;
                }
            });

            g.ResetClip();
            Theme.Bevel(g, r, dim);
            g.Restore(state);
            using (var pen = new Pen(Color.Black))
                g.DrawRectangle(pen, r.X - 0.5f, r.Y - 0.5f, r.Width, r.Height);
        }

        /// A small tinted tile with a gold glyph on it: the counterpart of the macOS build's
        /// `WoW.drawIcon(_:hue:in:dim:)`. Same ground, same bevel and the same `dim` wash as a
        /// macro icon, but carrying a plain symbol rather than a painted object.
        public static void DrawTile(Graphics g, PlateGlyph glyph, Color hue, RectangleF r, bool dim)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(r);

            var bright = Blend(hue, Color.White, 0.35);
            var deep = Blend(hue, Color.Black, 0.72);
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(RectangleF.Inflate(r, r.Width * 0.30f, r.Height * 0.30f));
                using (var b = new PathGradientBrush(p))
                {
                    b.CenterPoint = new PointF(r.X + r.Width * 0.35f, r.Y + r.Height * 0.30f);
                    b.CenterColor = bright;
                    b.SurroundColors = new[] { Color.Black };
                    b.InterpolationColors = new ColorBlend(4)
                    {
                        Colors = new[] { Color.Black, deep, hue, bright },
                        Positions = new[] { 0f, 0.22f, 0.7f, 1f },
                    };
                    g.FillRectangle(b, r);
                }
            }

            using (var path = GlyphPath(glyph, r))
            {
                // The drop shadow is most of why a gold glyph reads as gold and not as yellow.
                var t = g.Save();
                g.TranslateTransform(0, Math.Max(1f, r.Height * 0.05f));
                using (var sh = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.FillPath(sh, path);
                g.Restore(t);
                Theme.FillDown(g, path, Theme.GoldMetal);
                using (var pen = new Pen(Color.FromArgb(180, 0, 0, 0), Math.Max(0.6f, r.Width * 0.022f)))
                    g.DrawPath(pen, path);
            }

            g.ResetClip();
            Theme.Bevel(g, r, dim);
            g.Restore(state);
            using (var pen = new Pen(Color.Black))
                g.DrawRectangle(pen, r.X - 0.5f, r.Y - 0.5f, r.Width, r.Height);
        }

        /// Nested figures on the default alternate fill: the outer shape is solid, the one
        /// inside it is a hole, and anything inside *that* is solid again. So a window comes
        /// out as a frame with its title bar filled, in one path the gradient can cross.
        static GraphicsPath GlyphPath(PlateGlyph glyph, RectangleF r)
        {
            var p = new GraphicsPath();
            RectangleF Box(float fx, float fy, float fw, float fh) =>
                new RectangleF(r.X + r.Width * fx, r.Y + r.Height * fy, r.Width * fw, r.Height * fh);

            if (glyph == PlateGlyph.Display)
            {
                using (var outer = Theme.Round(Box(0.12f, 0.20f, 0.76f, 0.50f), r.Width * 0.08f))
                using (var inner = Theme.Round(Box(0.20f, 0.28f, 0.60f, 0.34f), r.Width * 0.04f))
                {
                    p.AddPath(outer, false);
                    p.AddPath(inner, false);
                }
                p.AddRectangle(Box(0.44f, 0.70f, 0.12f, 0.08f));          // neck
                using (var foot = Theme.Round(Box(0.28f, 0.78f, 0.44f, 0.07f), r.Width * 0.03f))
                    p.AddPath(foot, false);
            }
            else
            {
                using (var outer = Theme.Round(Box(0.12f, 0.18f, 0.76f, 0.64f), r.Width * 0.08f))
                using (var inner = Theme.Round(Box(0.19f, 0.25f, 0.62f, 0.50f), r.Width * 0.04f))
                using (var bar = new GraphicsPath())
                {
                    p.AddPath(outer, false);
                    p.AddPath(inner, false);
                    bar.AddRectangle(Box(0.19f, 0.25f, 0.62f, 0.13f));    // title bar, solid again
                    p.AddPath(bar, false);
                }
            }
            return p;
        }

        static float Turn(MacroIcon k) =>
            k == MacroIcon.Spyglass ? 28f : k == MacroIcon.Key ? 38f : 0f;

        static Color Blend(Color a, Color b, double f) =>
            Color.FromArgb(255,
                (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));

        /// Draw in a 0..1 box over `r`, y up, turned by `turn` degrees about its centre.
        static void InBox(Graphics g, RectangleF r, float turn, Action body)
        {
            var state = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.ScaleTransform(r.Width, -r.Height);
            if (turn != 0) g.RotateTransform(turn);
            g.TranslateTransform(-0.5f, -0.5f);
            body();
            g.Restore(state);
        }

        static void Outline(Graphics g, GraphicsPath p, float width = 0.028f)
        {
            using (var pen = new Pen(Color.Black, width) { LineJoin = LineJoin.Round }) g.DrawPath(pen, p);
        }

        static GraphicsPath Poly(params float[] xy)
        {
            var p = new GraphicsPath();
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            p.AddPolygon(pts);
            return p;
        }

        /// Lit along the top, dark underneath: what makes a shape read as a metal cylinder.
        static readonly Color[] Brass =
        {
            Theme.Rgb(0.20, 0.13, 0.04), Theme.Rgb(0.62, 0.45, 0.16),
            Theme.Rgb(1, 0.90, 0.60), Theme.Rgb(0.70, 0.52, 0.20),
        };
        static readonly float[] BrassStops = { 0f, 0.34f, 0.66f, 1f };

        /// In the unit box y runs up, so a "downward" GDI+ gradient paints bottom-to-top here,
        /// which is what these shapes want: dark underside, lit top.
        static void FillUp(Graphics g, GraphicsPath p, Color[] colors, float[] stops = null)
        {
            var b = p.GetBounds();
            if (b.Width <= 0 || b.Height <= 0) return;
            using (var brush = new LinearGradientBrush(
                       new RectangleF(b.X, b.Y - 0.002f, b.Width, b.Height + 0.004f),
                       colors[0], colors[colors.Length - 1], 90f))
            {
                var st = stops;
                if (st == null)
                {
                    st = new float[colors.Length];
                    for (int i = 0; i < colors.Length; i++) st[i] = i / (float)(colors.Length - 1);
                }
                brush.InterpolationColors = new ColorBlend(colors.Length) { Colors = colors, Positions = st };
                g.FillPath(brush, p);
            }
        }

        static void Spyglass(Graphics g)
        {
            using (var tube = Poly(0.11f, 0.425f, 0.11f, 0.575f, 0.87f, 0.645f, 0.87f, 0.355f))
            {
                FillUp(g, tube, Brass, BrassStops);
                Outline(g, tube);
            }
            foreach (var x in new[] { 0.33f, 0.585f })
            {
                float w = 0.075f, t = 0.15f + (x - 0.11f) / 0.76f * 0.14f;
                using (var band = Poly(x, 0.5f - t / 2 - 0.012f, x, 0.5f + t / 2 + 0.012f,
                                       x + w, 0.5f + t / 2 + 0.018f, x + w, 0.5f - t / 2 - 0.018f))
                {
                    FillUp(g, band, new[] { Theme.Rgb(0.16, 0.10, 0.03), Theme.Rgb(0.78, 0.60, 0.24), Theme.Rgb(0.42, 0.29, 0.09) });
                    Outline(g, band, 0.022f);
                }
            }
            using (var cap = new GraphicsPath())
            {
                cap.AddEllipse(0.075f, 0.415f, 0.07f, 0.17f);
                FillUp(g, cap, new[] { Theme.Rgb(0.22, 0.15, 0.05), Theme.Rgb(0.55, 0.40, 0.14) });
                Outline(g, cap, 0.022f);
            }
            using (var lens = new GraphicsPath())
            {
                lens.AddEllipse(0.825f, 0.345f, 0.09f, 0.31f);
                using (var b = new PathGradientBrush(lens))
                {
                    b.CenterPoint = new PointF(0.85f, 0.56f);
                    b.CenterColor = Theme.Rgb(0.88, 0.97, 1);
                    b.SurroundColors = new[] { Theme.Rgb(0.06, 0.16, 0.34) };
                    g.FillPath(b, lens);
                }
                Outline(g, lens, 0.022f);
            }
            using (var b = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                g.FillEllipse(b, 0.845f, 0.53f, 0.032f, 0.075f);
            using (var pen = new Pen(Color.FromArgb(128, 255, 250, 217), 0.022f))
                g.DrawLine(pen, 0.20f, 0.555f, 0.80f, 0.615f);
        }

        static void Orb(Graphics g)
        {
            using (var stand = Poly(0.27f, 0.13f, 0.73f, 0.13f, 0.63f, 0.33f, 0.37f, 0.33f))
            {
                FillUp(g, stand, Theme.GoldMetal);
                Outline(g, stand);
            }
            using (var foot = Theme.Round(new RectangleF(0.22f, 0.08f, 0.56f, 0.08f), 0.035f))
            {
                FillUp(g, foot, Theme.GoldMetal);
                Outline(g, foot, 0.024f);
            }
            var c = new PointF(0.5f, 0.565f);
            const float rad = 0.315f;
            using (var ball = new GraphicsPath())
            {
                ball.AddEllipse(c.X - rad, c.Y - rad, rad * 2, rad * 2);
                using (var b = new PathGradientBrush(ball))
                {
                    b.CenterPoint = new PointF(c.X - rad * 0.38f, c.Y + rad * 0.38f);
                    b.CenterColor = Theme.Rgb(0.93, 0.99, 1);
                    b.SurroundColors = new[] { Theme.Rgb(0.03, 0.09, 0.26) };
                    b.InterpolationColors = new ColorBlend(4)
                    {
                        Colors = new[] { Theme.Rgb(0.03, 0.09, 0.26), Theme.Rgb(0.10, 0.34, 0.74),
                                         Theme.Rgb(0.42, 0.80, 0.98), Theme.Rgb(0.93, 0.99, 1) },
                        Positions = new[] { 0f, 0.32f, 0.78f, 1f },
                    };
                    g.FillPath(b, ball);
                }
                Outline(g, ball);
                // Light bouncing up off the stand, along the underside.
                var state = g.Save();
                g.SetClip(ball);
                using (var pen = new Pen(Color.FromArgb(140, 140, 217, 255), 0.05f))
                    g.DrawArc(pen, c.X - rad + 0.035f, c.Y - rad + 0.035f,
                              (rad - 0.035f) * 2, (rad - 0.035f) * 2, 20, 140);
                g.Restore(state);
            }
            using (var b = new SolidBrush(Color.FromArgb(217, 255, 255, 255)))
                g.FillEllipse(b, c.X - 0.20f, c.Y + 0.07f, 0.15f, 0.11f);
            using (var b = new SolidBrush(Color.FromArgb(102, 255, 255, 255)))
                g.FillEllipse(b, c.X - 0.06f, c.Y + 0.17f, 0.07f, 0.05f);
        }

        static void Key(Graphics g)
        {
            using (var shaft = new GraphicsPath())
            {
                shaft.AddRectangle(new RectangleF(0.30f, 0.445f, 0.60f, 0.11f));
                shaft.AddRectangle(new RectangleF(0.66f, 0.29f, 0.085f, 0.16f));
                shaft.AddRectangle(new RectangleF(0.815f, 0.29f, 0.075f, 0.16f));
                FillUp(g, shaft, Theme.GoldMetal);
                Outline(g, shaft, 0.026f);
            }
            using (var ring = new GraphicsPath())
            {
                ring.AddEllipse(0.075f, 0.315f, 0.37f, 0.37f);
                ring.AddEllipse(0.175f, 0.415f, 0.17f, 0.17f);
                ring.FillMode = FillMode.Alternate;
                FillUp(g, ring, Theme.GoldMetal);
            }
            using (var o = new GraphicsPath())
            {
                o.AddEllipse(0.075f, 0.315f, 0.37f, 0.37f);
                Outline(g, o, 0.026f);
            }
            using (var i = new GraphicsPath())
            {
                i.AddEllipse(0.175f, 0.415f, 0.17f, 0.17f);
                Outline(g, i, 0.026f);
            }
            using (var pen = new Pen(Color.FromArgb(153, 255, 247, 204), 0.02f))
                g.DrawLine(pen, 0.40f, 0.525f, 0.86f, 0.525f);
        }

        static void Scroll(Graphics g)
        {
            var parchment = new[] { Theme.Rgb(0.68, 0.56, 0.36), Theme.Rgb(0.85, 0.75, 0.53), Theme.Rgb(0.96, 0.90, 0.72) };
            using (var sheet = new GraphicsPath())
            {
                sheet.AddRectangle(new RectangleF(0.175f, 0.20f, 0.65f, 0.60f));
                FillUp(g, sheet, parchment);
                Outline(g, sheet, 0.024f);
            }
            using (var ink = new SolidBrush(Color.FromArgb(217, 77, 56, 33)))
                foreach (var line in new[] { (0.685f, 0.44f), (0.60f, 0.50f), (0.515f, 0.38f), (0.43f, 0.47f), (0.345f, 0.30f) })
                    g.FillRectangle(ink, 0.25f, line.Item1, line.Item2, 0.035f);

            var roll = new[] { Theme.Rgb(0.30, 0.22, 0.12), Theme.Rgb(0.58, 0.46, 0.28),
                               Theme.Rgb(0.93, 0.85, 0.65), Theme.Rgb(0.42, 0.32, 0.18) };
            var rollStops = new[] { 0f, 0.22f, 0.6f, 1f };
            foreach (var y in new[] { 0.735f, 0.115f })
            {
                using (var r = Theme.Round(new RectangleF(0.10f, y, 0.80f, 0.15f), 0.075f))
                {
                    FillUp(g, r, roll, rollStops);
                    Outline(g, r, 0.024f);
                }
                using (var cap = new GraphicsPath())
                {
                    cap.AddEllipse(0.10f, y + 0.012f, 0.085f, 0.126f);
                    using (var b = new PathGradientBrush(cap))
                    {
                        b.CenterPoint = new PointF(0.13f, y + 0.09f);
                        b.CenterColor = Theme.Rgb(0.52, 0.41, 0.24);
                        b.SurroundColors = new[] { Theme.Rgb(0.26, 0.19, 0.10) };
                        g.FillPath(b, cap);
                    }
                    Outline(g, cap, 0.02f);
                }
            }
        }

        static void Tome(Graphics g)
        {
            // Cover, seen slightly from the side, with the block of pages beside its spine.
            using (var pages = Theme.Round(new RectangleF(0.22f, 0.20f, 0.62f, 0.58f), 0.02f))
            {
                FillUp(g, pages, new[] { Theme.Rgb(0.55, 0.50, 0.40), Theme.Rgb(0.93, 0.90, 0.80), Theme.Rgb(0.75, 0.71, 0.60) });
                Outline(g, pages, 0.022f);
            }
            using (var pen = new Pen(Theme.Rgb(0.45, 0.40, 0.31), 0.012f))
                for (float y = 0.28f; y < 0.75f; y += 0.075f)
                    g.DrawLine(pen, 0.26f, y, 0.80f, y);
            using (var cover = Theme.Round(new RectangleF(0.16f, 0.16f, 0.60f, 0.66f), 0.035f))
            {
                FillUp(g, cover, new[] { Theme.Rgb(0.22, 0.07, 0.09), Theme.Rgb(0.58, 0.16, 0.18), Theme.Rgb(0.38, 0.10, 0.12) });
                Outline(g, cover);
            }
            // Spine and its bands.
            using (var spine = Theme.Round(new RectangleF(0.16f, 0.16f, 0.13f, 0.66f), 0.035f))
            {
                FillUp(g, spine, new[] { Theme.Rgb(0.16, 0.05, 0.06), Theme.Rgb(0.42, 0.12, 0.14), Theme.Rgb(0.26, 0.07, 0.08) });
                Outline(g, spine, 0.022f);
            }
            using (var pen = new Pen(Theme.Rgb(0.82, 0.62, 0.24), 0.022f))
            {
                g.DrawLine(pen, 0.17f, 0.36f, 0.28f, 0.36f);
                g.DrawLine(pen, 0.17f, 0.62f, 0.28f, 0.62f);
            }
            // A gold boss on the cover.
            using (var boss = new GraphicsPath())
            {
                boss.AddEllipse(0.44f, 0.42f, 0.16f, 0.16f);
                FillUp(g, boss, Theme.GoldMetal);
                Outline(g, boss, 0.022f);
            }
        }

        /// The round portrait in a window's top-left corner, in a gold ring.
        public static void DrawPortrait(Graphics g, PointF c, float radius)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF Circle(float rr) => new RectangleF(c.X - rr, c.Y - rr, rr * 2, rr * 2);

            using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillEllipse(b, Circle(radius + 2));
            using (var ring = new GraphicsPath())
            {
                ring.AddEllipse(Circle(radius));
                Theme.FillDown(g, ring, Theme.GoldMetal);
            }
            using (var pen = new Pen(Color.Black)) g.DrawEllipse(pen, Circle(radius - 5));

            using (var face = new GraphicsPath())
            {
                face.AddEllipse(Circle(radius - 5.5f));
                using (var b = new PathGradientBrush(face))
                {
                    b.CenterPoint = new PointF(c.X - radius * 0.2f, c.Y - radius * 0.3f);
                    b.CenterColor = Theme.Rgb(0.30, 0.45, 0.58);
                    b.SurroundColors = new[] { Theme.Rgb(0.03, 0.05, 0.08) };
                    g.FillPath(b, face);
                }
                var state = g.Save();
                g.SetClip(face);
                // The two panes of the app icon, in glass blue.
                float pw = radius * 0.34f, ph = radius * 0.78f, gap = radius * 0.16f;
                for (int i = 0; i < 2; i++)
                {
                    var pane = new RectangleF(c.X - (pw * 2 + gap) / 2 + i * (pw + gap), c.Y - ph / 2, pw, ph);
                    using (var p = Theme.Round(pane, radius * 0.07f))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(70, 210, 235, 255))) g.FillPath(b, p);
                        using (var pen = new Pen(Theme.Rgb(0.80, 0.92, 1), Math.Max(1f, radius * 0.055f)))
                            g.DrawPath(pen, p);
                    }
                }
                using (var b = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                    g.FillEllipse(b, c.X - radius * 0.7f, c.Y - radius * 0.8f, radius * 1.4f, radius * 0.8f);
                g.Restore(state);
            }

            using (var pen = new Pen(Color.FromArgb(128, 255, 255, 255)))
                g.DrawArc(pen, Circle(radius - 1.5f), 190, 70);
            using (var pen = new Pen(Color.Black)) g.DrawEllipse(pen, Circle(radius + 0.5f));
            g.SmoothingMode = old;
        }
    }
}
