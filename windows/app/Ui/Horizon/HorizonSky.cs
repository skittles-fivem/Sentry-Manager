using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's background (web/login.js + shell.css .sky): the dusk sky,
    /// stars, the striped sun and its glow, the seeded wireframe mountains,
    /// the haze, the neon grid floor running toward the horizon, scanlines and
    /// grain. The floor moves at most 30 times a second (12 in "calm"); it
    /// stands still when effects are reduced (the server's setting, Windows'
    /// "Show animations", or the render test mode) and is plain in "off".
    /// </summary>
    public sealed class HorizonSky : FrameworkElement
    {
        /// <summary>auto | full | calm | off (the web's horizon.sky, this PC's choice).</summary>
        string choice = "auto";
        bool still;
        /// <summary>What runs: full | calm | still | off.</summary>
        public string Mode { get; private set; }
        double phase, boost, engineTarget;
        TimeSpan last = TimeSpan.Zero, lastDraw = TimeSpan.Zero;
        bool hooked;
        DateTime born = DateTime.UtcNow;

        static ImageBrush noise;
        static DrawingBrush scan;

        public HorizonSky()
        {
            Mode = "full";
            IsHitTestVisible = false;
            Loaded += delegate { Apply(); };
            Unloaded += delegate { Unhook(); };
            IsVisibleChanged += delegate { Apply(); };
        }

        public void SetChoice(string c, bool reduce)
        {
            choice = c == "full" || c == "calm" || c == "off" ? c : "auto";
            still = reduce;
            Apply();
        }

        /// <summary>The "engine running" cue: the floor speeds up while a conversion runs.</summary>
        public void Engine(bool on)
        {
            engineTarget = on ? 1 : 0;
            if (Mode == "still" || Mode == "off") boost = engineTarget;
            InvalidateVisual();
        }

        void Apply()
        {
            if (choice == "off") Mode = "off";
            else if (still) Mode = "still";
            else if (choice == "calm") Mode = "calm";
            else Mode = "full"; // auto: a PC gets "full" (the web gives phones "calm")
            bool run = (Mode == "full" || Mode == "calm") && IsLoaded && IsVisible;
            if (run && !hooked) { CompositionTarget.Rendering += Tick; hooked = true; last = TimeSpan.Zero; }
            if (!run) Unhook();
            InvalidateVisual();
        }

        void Unhook()
        {
            if (hooked) { CompositionTarget.Rendering -= Tick; hooked = false; }
        }

        void Tick(object sender, EventArgs e)
        {
            RenderingEventArgs r = e as RenderingEventArgs;
            TimeSpan t = r != null ? r.RenderingTime : DateTime.UtcNow - born;
            double fps = Mode == "calm" ? 12 : 30;
            if ((t - lastDraw).TotalMilliseconds < 1000 / fps - 2) return;
            double dt = last == TimeSpan.Zero ? 0 : Math.Min(0.1, (t - last).TotalSeconds);
            last = t;
            lastDraw = t;
            boost += (engineTarget - boost) * Math.Min(1, dt * 1.2);
            double baseSpeed = Mode == "calm" ? 0.18 : 0.55;
            phase += dt * baseSpeed * (1 + boost * 1.6);
            InvalidateVisual();
        }

        // ---------- the static mountains (login.js mountains(), same seed, same numbers) ----------
        sealed class Range
        {
            public List<Point> Pts = new List<Point>();
            public string Facets = "";
        }

        static Range[] ranges;

        static Range[] Mountains()
        {
            if (ranges != null) return ranges;
            const double H = 220;
            uint s = 20261026;
            Func<double> r = () => { unchecked { s = s * 1664525u + 1013904223u; } return s / 4294967296.0; };
            Func<double, double, double, Range> range = (x0, x1, peak) =>
            {
                Range g = new Range();
                g.Pts.Add(new Point(x0, H));
                const int n = 9;
                for (int i = 1; i < n; i++)
                {
                    double x = x0 + ((x1 - x0) * i) / n + (r() - 0.5) * 30;
                    double mid = 1 - Math.Abs((double)i / n - 0.5) * 1.7;
                    double y = H - (peak * (0.35 + 0.65 * mid) * (0.55 + r() * 0.45));
                    g.Pts.Add(new Point(x, y));
                }
                g.Pts.Add(new Point(x1, H));
                return g;
            };
            Range left = range(-40, 640, 200);
            Range right = range(960, 1640, 170);
            Range back = range(380, 1220, 90);
            Action<Range> facets = g =>
            {
                System.Text.StringBuilder d = new System.Text.StringBuilder();
                List<Point> p = g.Pts;
                for (int i = 1; i < p.Count - 1; i++)
                {
                    double x = p[i].X, y = p[i].Y;
                    d.Append(" M").Append(R(x)).Append(',').Append(R(y)).Append(" L").Append(R(x + (r() - 0.5) * 60)).Append(',').Append(R(H));
                    if (i < p.Count - 2)
                    {
                        double nx = p[i + 1].X, ny = p[i + 1].Y;
                        double mx = (x + nx) / 2;
                        double my = Math.Max(y, ny) + (H - Math.Max(y, ny)) * 0.45;
                        d.Append(" M").Append(R(x)).Append(',').Append(R(y)).Append(" L").Append(R(mx)).Append(',').Append(R(my)).Append(" L").Append(R(nx)).Append(',').Append(R(ny));
                    }
                }
                g.Facets = d.ToString();
            };
            // drawn (and so seeded) in this order: back, left, right
            facets(back);
            facets(left);
            facets(right);
            for (int k = 0; k < 3; k++)
            {
                Range g = k == 0 ? back : k == 1 ? left : right;
                for (int i = 0; i < g.Pts.Count; i++) g.Pts[i] = new Point(Math.Round(g.Pts[i].X), Math.Round(g.Pts[i].Y));
            }
            ranges = new[] { back, left, right };
            return ranges;
        }

        static string R(double v)
        {
            return Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static Geometry Poly(List<Point> pts, bool close)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(pts[0], close, close);
                for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i], true, false);
            }
            return g;
        }

        static Pen P(string color, double w)
        {
            Pen p = new Pen(Css.Brush(color), w);
            p.LineJoin = PenLineJoin.Round;
            p.Freeze();
            return p;
        }

        // ---------- scanlines and grain (.sky-fx) ----------
        static void Fx()
        {
            if (noise != null) return;
            int n = 160;
            byte[] px = new byte[n * n * 4];
            Random rnd = new Random(7);
            for (int i = 0; i < n * n; i++)
            {
                // feColorMatrix: white, alpha = 0.5 * noise (premultiplied)
                byte a = (byte)(rnd.NextDouble() * 0.5 * 255);
                px[i * 4] = a; px[i * 4 + 1] = a; px[i * 4 + 2] = a; px[i * 4 + 3] = a;
            }
            BitmapSource bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
            bmp.Freeze();
            noise = new ImageBrush(bmp);
            noise.TileMode = TileMode.Tile;
            noise.ViewportUnits = BrushMappingMode.Absolute;
            noise.Viewport = new Rect(0, 0, n, n);
            noise.Freeze();
            // repeating-linear-gradient(180deg, rgba(0,0,0,.16) 0 1px, transparent 1px 3px)
            DrawingGroup dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1, 3))));
            dg.Children.Add(new GeometryDrawing(Css.Brush("rgba(0,0,0,.16)"), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            scan = new DrawingBrush(dg);
            scan.TileMode = TileMode.Tile;
            scan.ViewportUnits = BrushMappingMode.Absolute;
            scan.Viewport = new Rect(0, 0, 1, 3);
            scan.Stretch = Stretch.None;
            scan.Freeze();
        }

        static readonly object[][] Stars =
        {
            new object[] { 1.2, .12, .18, "#fff" }, new object[] { 1.0, .28, .08, "#d8d2ff" }, new object[] { 1.4, .43, .26, "#fff" },
            new object[] { 1.0, .61, .12, "#ffd6f0" }, new object[] { 1.2, .77, .22, "#fff" }, new object[] { 1.0, .89, .06, "#cfe9ff" },
            new object[] { 1.0, .06, .40, "#fff" }, new object[] { 1.3, .94, .36, "#fff" }, new object[] { 1.0, .52, .04, "#fff" },
            new object[] { 1.0, .35, .44, "#ffe0f4" }
        };

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            if (W <= 0 || H <= 0) return;
            // shell.css: --hz 64% (58% at 700 px and below; 62% on short landscape), --sun min(46vmin, 400px)
            bool phone = W <= 700;
            bool shortLand = W > H && H <= 500;
            double hz = phone ? 0.58 : shortLand ? 0.62 : 0.64;
            double sun = phone ? Math.Min(0.62 * W, 300) : shortLand ? 0.42 * H : Math.Min(0.46 * Math.Min(W, H), 400);
            double hzY = H * hz;
            Rect all = new Rect(0, 0, W, H);

            if (Mode == "off")
            {
                dc.DrawRectangle(Css.Linear(180, "#06031a", 0, "#120a33", .7, "#1c0a3e", 1), null, all);
                DrawStars(dc, W, hzY, 0.35);
                return;
            }
            dc.DrawRectangle(Css.Linear(180, "#06031a", 0, "#0f0730", .22, "#2a0d55", .40, "#5a1670", .52, "#a9286f", .59, "#ff6a4a", hz - 0.005, "#ff9a4a", hz, "#2a0b4a", hz), null, all);
            DrawStars(dc, W, hzY, 0.75);

            double t = (DateTime.UtcNow - born).TotalSeconds;
            bool moving = Mode == "full" || Mode == "calm";
            // .sky-glow (breathes 7 s while moving; 1 with the engine on)
            double glowOp = engineTarget > 0 ? 1 : moving ? 0.68 + 0.27 * (0.5 - 0.5 * Math.Cos(t / 7 * 2 * Math.PI)) : 0.8;
            double gw = sun * 3.4, gh = sun * 2.2;
            Rect glow = new Rect(W / 2 - gw / 2, hzY - gh * 0.62, gw, gh);
            dc.PushOpacity(glowOp);
            dc.DrawEllipse(Css.Radial(.5, .5, .5, .5, "rgba(255,120,90,.55)", 0, "rgba(255,63,180,.22)", .55, "rgba(255,63,180,0)", 1), null, new Point(glow.X + gw / 2, glow.Y + gh / 2), gw / 2, gh / 2);
            dc.Pop();

            // .sky-sun with its glow and the stripes cut out (mask-image)
            Rect sr = new Rect(W / 2 - sun / 2, hzY - sun * 0.8, sun, sun);
            Point sc = new Point(sr.X + sun / 2, sr.Y + sun / 2);
            dc.DrawEllipse(Css.Radial(.5, .5, .5, .5, "rgba(255,150,80,.45)", sun / 2 / (sun / 2 + 60), "rgba(255,150,80,0)", 1), null, sc, sun / 2 + 60, sun / 2 + 60);
            LinearGradientBrush mask = new LinearGradientBrush();
            mask.StartPoint = new Point(0, 0);
            mask.EndPoint = new Point(0, 1);
            double[] bands = { .47, .50, .57, .61, .67, .725, .77, .835, .865, .94 };
            mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
            for (int i = 0; i < bands.Length; i++)
            {
                bool intoGap = i % 2 == 0;
                mask.GradientStops.Add(new GradientStop(intoGap ? Colors.Black : Colors.Transparent, bands[i]));
                mask.GradientStops.Add(new GradientStop(intoGap ? Colors.Transparent : Colors.Black, bands[i]));
            }
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1));
            mask.Freeze();
            dc.PushOpacityMask(mask);
            dc.DrawEllipse(Css.Linear(180, "#fff3a0", 0, "#ffd25a", .22, "#ff9a3d", .48, "#ff5a8a", .72, "#ff3fb4", 1), null, sc, sun / 2, sun / 2);
            dc.Pop();

            // .sky-mtn: viewBox 1600 x 220, xMidYMax slice, height clamp(80px, 17vh, 190px)
            double mh = Math.Max(80, Math.Min(0.17 * H, 190));
            double scale = Math.Max(W / 1600, mh / 220);
            Matrix mx = new Matrix(scale, 0, 0, scale, (W - 1600 * scale) / 2, hzY - mh + (mh - 220 * scale));
            MatrixTransform tf = new MatrixTransform(mx);
            tf.Freeze();
            dc.PushClip(new RectangleGeometry(new Rect(0, hzY - mh, W, mh)));
            Range[] rs = Mountains();
            for (int k = 0; k < rs.Length; k++)
            {
                bool far = k == 0;
                Geometry fill = Poly(rs[k].Pts, true); fill.Transform = tf;
                Geometry wire = Geometry.Parse(rs[k].Facets.Trim()); wire = wire.Clone(); wire.Transform = tf;
                Geometry ridge = Poly(rs[k].Pts, false); ridge.Transform = tf;
                if (far) dc.PushOpacity(0.92);
                dc.DrawGeometry(Css.Brush(far ? "#24093f" : "#0c0526"), null, fill);
                if (far) dc.Pop();
                dc.DrawGeometry(null, P(far ? "rgba(255,120,200,.32)" : "rgba(61,240,255,.4)", 1), wire);
                dc.DrawGeometry(null, P(far ? "rgba(255,138,61,.75)" : "#ff3fb4", 1.6), ridge);
            }
            dc.Pop();

            // .sky-haze (11 s while moving)
            double hazeOp = 0.85 * (moving ? 0.7 + 0.3 * (0.5 - 0.5 * Math.Cos(t / 11 * 2 * Math.PI)) : 1);
            dc.PushOpacity(hazeOp);
            dc.DrawRectangle(Css.Linear(180, "rgba(255,110,170,0)", 0, "rgba(255,110,170,.32)", .55, "rgba(255,160,110,.38)", .60, "rgba(255,160,110,0)", 1), null, new Rect(0, hzY - 0.07 * H, W, 0.12 * H));
            dc.Pop();

            // .sky-floor and the grid (the canvas)
            double fh = H - hzY;
            Rect floor = new Rect(0, hzY, W, fh);
            dc.DrawRectangle(Css.Linear(180, "#3a0d55", 0, "#1a0838", .22, "#0b0524", .60, "#06031a", 1), null, floor);
            dc.PushClip(new RectangleGeometry(floor));
            dc.PushTransform(new TranslateTransform(0, hzY));
            DrawGrid(dc, W, fh);
            dc.Pop();
            dc.DrawRectangle(Css.Linear(180, "#3a0d55", 0, "rgba(58,13,85,.85)", .06, "rgba(40,10,70,.35)", .22, "rgba(40,10,70,0)", .45), null, floor);
            dc.DrawRectangle(CssRadial(W, fh, hzY), null, floor);
            dc.Pop();

            // .sky-fx
            Fx();
            dc.PushOpacity(Mode == "calm" ? 0.18 : 0.32);
            dc.DrawRectangle(scan, null, all);
            dc.DrawRectangle(noise, null, all);
            dc.Pop();
        }

        static Brush CssRadial(double w, double h, double top)
        {
            // radial-gradient(70% 60% at 50% 110%, rgba(61,240,255,.22), transparent 70%)
            RadialGradientBrush g = new RadialGradientBrush();
            g.MappingMode = BrushMappingMode.Absolute;
            g.Center = new Point(w / 2, top + h * 1.1);
            g.GradientOrigin = g.Center;
            g.RadiusX = w * 0.7;
            g.RadiusY = h * 0.6;
            g.GradientStops.Add(new GradientStop(Css.C("rgba(61,240,255,.22)"), 0));
            g.GradientStops.Add(new GradientStop(Css.C("rgba(61,240,255,0)"), 0.7));
            g.Freeze();
            return g;
        }

        void DrawStars(DrawingContext dc, double W, double hzY, double opacity)
        {
            // .sky-stars covers the top (hz - 18%) of the sky
            double h = Math.Max(0, hzY - 0.18 * ActualHeight);
            dc.PushOpacity(opacity);
            foreach (object[] s in Stars)
            {
                double size = (double)s[0];
                dc.DrawEllipse(Css.Brush((string)s[3]), null, new Point((double)s[1] * W, (double)s[2] * h), size / 2, size / 2);
            }
            dc.Pop();
        }

        void DrawGrid(DrawingContext dc, double w, double h)
        {
            double cx = w / 2;
            double unit = Math.Max(w, h * 2.2) / 9;
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                double ph = phase % 1;
                for (int k = 1; k < 60; k++)
                {
                    double z = k - ph;
                    if (z < 0.98) continue;
                    double y = h / z;
                    if (y < 1.5) break;
                    c.BeginFigure(new Point(0, y), false, false);
                    c.LineTo(new Point(w, y), true, false);
                }
                int cols = (int)Math.Ceiling((w / 2) / unit) + 8;
                for (int i = -cols; i <= cols; i++)
                {
                    double xb = cx + i * unit * 1.6;
                    c.BeginFigure(new Point(cx + (xb - cx) * 0.02, 0), false, false);
                    c.LineTo(new Point(xb, h), true, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, P("#ff62cc", 1.5), g);
        }
    }
}
