using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SentrySix.Ui.Aurora
{
    /// <summary>
    /// Aurora's night sky (web/aurora/sky.js + app.css): the CSS sky
    /// gradient, a seeded starfield, northern-light curtains drawn on a
    /// low-resolution bitmap stretched to the window (the same five ribbons,
    /// waves and colours), twinkling stars and the odd shooting star.
    /// At most 30 frames a second (calm: 20, three ribbons); one still frame
    /// (t = 37) when effects are reduced or the mode is "off".
    /// </summary>
    public sealed class AuroraSky : Grid
    {
        const double TAU = Math.PI * 2;
        static readonly int[][] Colors3 =
        {
            new[] { 70, 240, 168 },   // green
            new[] { 47, 214, 216 },   // teal
            new[] { 155, 125, 255 },  // violet
            new[] { 232, 110, 224 }   // magenta
        };
        const int Green = 0, Teal = 1, Violet = 2, Magenta = 3;
        // [color, top tint, base, amplitude, wave 1, wave 2, speed, curtain height, brightness, depth]
        static readonly double[][] Ribbons =
        {
            new double[] { Teal, Violet, 0.30, 0.07, 1.7, 4.1, 0.050, 0.34, 0.66, 0.6 },
            new double[] { Green, Violet, 0.42, 0.09, 1.2, 3.3, 0.070, 0.44, 1.00, 1.0 },
            new double[] { Violet, Magenta, 0.22, 0.06, 2.2, 5.0, 0.040, 0.26, 0.62, 0.4 },
            new double[] { Green, Teal, 0.56, 0.07, 1.5, 3.8, 0.085, 0.30, 0.58, 1.3 },
            new double[] { Magenta, Violet, 0.17, 0.05, 2.7, 6.2, 0.030, 0.20, 0.42, 0.3 }
        };

        readonly Border sky = new Border();
        readonly StarField stars = new StarField();
        readonly Image curtain = new Image();
        readonly Canvas twinkleHost = new Canvas();
        readonly Rectangle shoot = new Rectangle();
        readonly Border veil = new Border();
        readonly float[][] strips;
        WriteableBitmap bmp;
        float[] acc;
        int W, H;
        double scale = 0.2;
        double t = 37;
        string mode = "calm";
        bool still, hooked;
        TimeSpan last = TimeSpan.Zero;
        DispatcherTimer shootTimer;
        readonly Random rnd = new Random();

        public AuroraSky()
        {
            IsHitTestVisible = false;
            strips = new float[Ribbons.Length][];
            for (int k = 0; k < Ribbons.Length; k++) strips[k] = Strip((int)Ribbons[k][0], (int)Ribbons[k][1]);
            Children.Add(sky);
            Children.Add(stars);
            curtain.Stretch = Stretch.Fill;
            RenderOptions.SetBitmapScalingMode(curtain, BitmapScalingMode.Linear);
            Children.Add(curtain);
            Children.Add(twinkleHost);
            shoot.Width = 180;
            shoot.Height = 2;
            shoot.RadiusX = 1; shoot.RadiusY = 1;
            shoot.Fill = Css.Linear(90, "rgba(255,255,255,0)", 0, "rgba(220,240,255,.95)", 1);
            shoot.Opacity = 0;
            shoot.Effect = Css.Glow(10, "rgba(180,220,255,.8)");
            shoot.RenderTransformOrigin = new Point(0, 0.5);
            twinkleHost.Children.Add(shoot);
            // body::after: a dim veil over the lower 30 %
            veil.VerticalAlignment = VerticalAlignment.Bottom;
            veil.Background = Css.Linear(180, "rgba(2,5,14,0)", 0, "rgba(2,5,14,.35)", 1);
            Children.Add(veil);
            SizeChanged += delegate { Size(); };
            Loaded += delegate { Update(); };
            Unloaded += delegate { Unhook(); if (shootTimer != null) shootTimer.Stop(); };
        }

        /// <summary>The page background (shell.css body): the sky gradient with two horizon glows.</summary>
        public static Brush SkyBrush(double w, double h)
        {
            DrawingGroup g = new DrawingGroup();
            Rect r = new Rect(0, 0, Math.Max(1, w), Math.Max(1, h));
            g.Children.Add(new GeometryDrawing(Css.Linear(180, "#02040b", 0, "#040a1c", .38, "#081631", .74, "#0f2146", 1), null, new RectangleGeometry(r)));
            g.Children.Add(new GeometryDrawing(Css.RadialPx(r.Width, r.Height, r.Width * 1.2, r.Height * .6, .5, 1.15, "rgba(47,214,216,.16)", 0, "rgba(47,214,216,0)", .6), null, new RectangleGeometry(r)));
            g.Children.Add(new GeometryDrawing(Css.RadialPx(r.Width, r.Height, r.Width * .9, r.Height * .5, .15, 1.05, "rgba(155,125,255,.14)", 0, "rgba(155,125,255,0)", .6), null, new RectangleGeometry(r)));
            DrawingBrush b = new DrawingBrush(g);
            b.Stretch = Stretch.Fill;
            return b;
        }

        /// <summary>full | calm | off (this PC's "aurora:effect", calm until chosen) and reduced effects.</summary>
        public void SetMode(string m, bool reduce)
        {
            mode = m == "full" || m == "off" ? m : "calm";
            still = reduce;
            Size();
            Update();
        }

        bool Animated { get { return mode != "off" && !still; } }

        static float[] Strip(int bottom, int top)
        {
            // one vertical strip, 256 px: the bright lower edge of a curtain fading up into a tint (premultiplied r, g, b, a)
            int[] c = Colors3[bottom], tc = Colors3[top];
            double[][] stops =
            {
                new double[] { 0, tc[0], tc[1], tc[2], 0 },
                new double[] { 0.35, tc[0], tc[1], tc[2], 0.16 },
                new double[] { 0.72, c[0], c[1], c[2], 0.42 },
                new double[] { 0.9, Math.Min(255, c[0] + 60), Math.Min(255, c[1] + 15), Math.Min(255, c[2] + 40), 0.95 },
                new double[] { 0.95, c[0], c[1], c[2], 0.35 },
                new double[] { 1, c[0], c[1], c[2], 0 }
            };
            float[] s = new float[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                double p = (i + 0.5) / 256;
                int j = 0;
                while (j < stops.Length - 2 && p > stops[j + 1][0]) j++;
                double[] a = stops[j], b = stops[j + 1];
                double f = Math.Max(0, Math.Min(1, (p - a[0]) / (b[0] - a[0])));
                // canvas gradients interpolate premultiplied colours
                double al = a[4] + (b[4] - a[4]) * f;
                for (int ch = 0; ch < 3; ch++)
                {
                    double pa = a[ch + 1] * a[4], pb = b[ch + 1] * b[4];
                    s[i * 4 + ch] = (float)((pa + (pb - pa) * f) / 255.0);
                }
                s[i * 4 + 3] = (float)al;
            }
            return s;
        }

        void Size()
        {
            double cw = Math.Max(1, ActualWidth), ch = Math.Max(1, ActualHeight);
            sky.Background = SkyBrush(cw, ch);
            veil.Height = ch * 0.3;
            scale = mode == "calm" ? 0.2 : 0.28;
            W = Math.Max(64, (int)Math.Round(cw * scale));
            H = Math.Max(64, (int)Math.Round(ch * scale));
            bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Pbgra32, null);
            acc = new float[W * H * 4];
            curtain.Source = bmp;
            stars.InvalidateVisual();
            PlaceTwinkles(cw, ch);
            Draw(0);
        }

        static Func<double> Rng(uint seed)
        {
            uint s = seed;
            return () =>
            {
                unchecked
                {
                    s = s + 0x6D2B79F5u;
                    uint x = (s ^ (s >> 15)) * (1u | s);
                    x ^= x + (x ^ (x >> 7)) * (61u | x);
                    return (x ^ (x >> 14)) / 4294967296.0;
                }
            };
        }

        sealed class StarField : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc)
            {
                double cw = ActualWidth, ch = ActualHeight;
                if (cw <= 0 || ch <= 0) return;
                Func<double> rand = Rng(1977);
                int n = (int)Math.Round((cw * ch) / 2300);
                for (int i = 0; i < n; i++)
                {
                    double x = rand() * cw;
                    double y = Math.Pow(rand(), 1.35) * ch;
                    double m = rand();
                    double r = m > 0.985 ? 1.5 : m > 0.92 ? 1.05 : 0.65;
                    double a = (0.35 + rand() * 0.6) * (1 - (y / ch) * 0.55);
                    double tint = rand();
                    Color c = tint > 0.8 ? Color.FromRgb(200, 220, 255) : tint > 0.65 ? Color.FromRgb(255, 240, 220) : Color.FromRgb(255, 255, 255);
                    dc.DrawEllipse(new SolidColorBrush(Css.Alpha(c, a)), null, new Point(x, y), r, r);
                    if (r > 1.4)
                    {
                        RadialGradientBrush g = new RadialGradientBrush(Css.Alpha(Color.FromRgb(210, 230, 255), a * 0.35), Color.FromArgb(0, 210, 230, 255));
                        dc.DrawEllipse(g, null, new Point(x, y), 6, 6);
                    }
                }
            }
        }

        void PlaceTwinkles(double cw, double ch)
        {
            for (int i = twinkleHost.Children.Count - 1; i >= 0; i--) if (twinkleHost.Children[i] != shoot) twinkleHost.Children.RemoveAt(i);
            Func<double> rand = Rng(4242);
            int n = cw < 600 ? 10 : 22;
            for (int i = 0; i < n; i++)
            {
                double left = rand() * cw;
                double top = Math.Pow(rand(), 1.4) * ch * 0.85;
                double delay = -rand() * 7;
                double dur = 3.5 + rand() * 5;
                bool big = rand() > 0.6;
                double size = big ? 3 : 2;
                Ellipse e = new Ellipse();
                e.Width = e.Height = size;
                e.Fill = Brushes.White;
                e.Effect = Css.Glow(7, "rgba(210,228,255,.7)");
                e.RenderTransformOrigin = new Point(0.5, 0.5);
                ScaleTransform st = new ScaleTransform(1, 1);
                e.RenderTransform = st;
                Canvas.SetLeft(e, left);
                Canvas.SetTop(e, top);
                twinkleHost.Children.Insert(0, e);
                // @keyframes twinkle: opacity .15 -> 1 -> .15, scale .7 -> 1.15 -> .7, ease-in-out
                if (mode == "off") { e.Opacity = 0.55; continue; }
                double phase = ((-delay) % dur) / dur;
                double wave = 0.5 - 0.5 * Math.Cos(phase * TAU);
                if (!Animated)
                {
                    e.Opacity = 0.15 + 0.85 * wave;
                    st.ScaleX = st.ScaleY = 0.7 + 0.45 * wave;
                    continue;
                }
                DoubleAnimation op = new DoubleAnimation(0.15, 1, new Duration(TimeSpan.FromSeconds(dur / 2)));
                op.AutoReverse = true;
                op.RepeatBehavior = RepeatBehavior.Forever;
                op.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                op.BeginTime = TimeSpan.FromSeconds(delay);
                e.BeginAnimation(UIElement.OpacityProperty, op);
                DoubleAnimation sc = new DoubleAnimation(0.7, 1.15, new Duration(TimeSpan.FromSeconds(dur / 2)));
                sc.AutoReverse = true;
                sc.RepeatBehavior = RepeatBehavior.Forever;
                sc.EasingFunction = op.EasingFunction;
                sc.BeginTime = op.BeginTime;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
            }
        }

        void Update()
        {
            bool run = Animated && IsLoaded;
            if (run && !hooked) { CompositionTarget.Rendering += Tick; hooked = true; last = TimeSpan.Zero; }
            if (!run) Unhook();
            if (mode == "full" && !still && IsLoaded) ShootingStar(); else if (shootTimer != null) shootTimer.Stop();
        }

        void Unhook()
        {
            if (hooked) { CompositionTarget.Rendering -= Tick; hooked = false; }
        }

        void Tick(object sender, EventArgs e)
        {
            RenderingEventArgs r = e as RenderingEventArgs;
            if (r == null) return;
            double gap = 1000.0 / (mode == "calm" ? 20 : 30);
            if (last == TimeSpan.Zero) last = r.RenderingTime - TimeSpan.FromMilliseconds(gap);
            double dt = (r.RenderingTime - last).TotalMilliseconds;
            if (dt < gap - 1) return;
            last = r.RenderingTime;
            Draw(Math.Min(dt, 100) / 1000);
        }

        void Draw(double dt)
        {
            if (bmp == null) return;
            t += dt;
            Array.Clear(acc, 0, acc.Length);
            bool calm = mode == "calm";
            int step = calm ? 3 : 2;
            int count = calm ? 3 : Ribbons.Length;
            bool narrow = W < 260;
            for (int k = 0; k < count; k++)
            {
                double[] rb = Ribbons[k];
                double b0 = rb[2], amp = rb[3], f1 = rb[4], f2 = rb[5], speed = rb[6], height = rb[7], bright = rb[8];
                float[] strip = strips[k];
                double hh = H * height * (narrow ? 1.15 : 1);
                double tt = t * speed * (calm ? 0.7 : 1);
                double slow = Math.Sin(t * 0.05 + k) * 0.5 + 0.5;
                for (int x = -step; x < W + step; x += step)
                {
                    double u = (double)x / W;
                    double y = H * (b0 + amp * (0.62 * Math.Sin(u * f1 * TAU * 0.5 + tt * 3 + k * 1.7)
                        + 0.38 * Math.Sin(u * f2 * TAU * 0.5 - tt * 4.3 + k)))
                        + H * 0.02 * Math.Sin(u * 23 + tt * 9);
                    double ray = 0.5 + 0.5 * Math.Sin(u * 61 + tt * 13 + k * 3) * Math.Sin(u * 17 - tt * 5);
                    double fold = 0.55 + 0.45 * Math.Sin(u * 7.3 + tt * 2.1 + k);
                    double edge = Math.Min(1, Math.Sin(Math.Max(0, Math.Min(1, u)) * Math.PI) * 1.6 + 0.12);
                    double a = bright * edge * (0.35 + 0.65 * ray * fold) * (0.75 + 0.25 * slow);
                    if (a < 0.02) continue;
                    float ga = (float)Math.Min(1, a);
                    double top = y - hh * 0.9;
                    int y0 = Math.Max(0, (int)Math.Floor(top)), y1 = Math.Min(H, (int)Math.Ceiling(top + hh));
                    int x0 = Math.Max(0, x), x1 = Math.Min(W, x + step + 1);
                    for (int py = y0; py < y1; py++)
                    {
                        int si = (int)((py + 0.5 - top) / hh * 256);
                        if (si < 0 || si > 255) continue;
                        float sr = strip[si * 4] * ga, sg = strip[si * 4 + 1] * ga, sb = strip[si * 4 + 2] * ga, sa = strip[si * 4 + 3] * ga;
                        int row = py * W;
                        for (int px = x0; px < x1; px++)
                        {
                            int o = (row + px) * 4;
                            acc[o] += sr; acc[o + 1] += sg; acc[o + 2] += sb; acc[o + 3] += sa;
                        }
                    }
                }
            }
            byte[] outp = new byte[W * H * 4];
            for (int i = 0; i < W * H; i++)
            {
                float al = Math.Min(1f, acc[i * 4 + 3]);
                // 'lighter' clamps each channel; premultiplied output (Pbgra32)
                outp[i * 4] = (byte)(Math.Min(al, acc[i * 4 + 2]) * 255);
                outp[i * 4 + 1] = (byte)(Math.Min(al, acc[i * 4 + 1]) * 255);
                outp[i * 4 + 2] = (byte)(Math.Min(al, acc[i * 4]) * 255);
                outp[i * 4 + 3] = (byte)(al * 255);
            }
            bmp.WritePixels(new Int32Rect(0, 0, W, H), outp, W * 4, 0);
        }

        void ShootingStar()
        {
            if (shootTimer == null)
            {
                shootTimer = new DispatcherTimer();
                shootTimer.Tick += delegate
                {
                    shootTimer.Interval = TimeSpan.FromMilliseconds(16000 + rnd.NextDouble() * 26000);
                    if (!hooked) return;
                    double sx = ActualWidth * (0.15 + rnd.NextDouble() * 0.6);
                    double sy = ActualHeight * (0.05 + rnd.NextDouble() * 0.25);
                    double len = Math.Min(420, ActualWidth * 0.4);
                    Canvas.SetLeft(shoot, sx);
                    Canvas.SetTop(shoot, sy);
                    TransformGroup tg = new TransformGroup();
                    ScaleTransform s = new ScaleTransform(0.3, 1);
                    TranslateTransform tr = new TranslateTransform();
                    tg.Children.Add(s);
                    tg.Children.Add(tr);
                    tg.Children.Add(new RotateTransform(-28));
                    shoot.RenderTransform = tg;
                    Duration d = new Duration(TimeSpan.FromMilliseconds(1100));
                    DoubleAnimationUsingKeyFrames o = new DoubleAnimationUsingKeyFrames();
                    o.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                    o.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
                    o.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                    o.Duration = d;
                    shoot.BeginAnimation(UIElement.OpacityProperty, o);
                    DoubleAnimationUsingKeyFrames x = new DoubleAnimationUsingKeyFrames();
                    x.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                    x.KeyFrames.Add(new LinearDoubleKeyFrame(len * 0.3, KeyTime.FromPercent(0.25)));
                    x.KeyFrames.Add(new EasingDoubleKeyFrame(len, KeyTime.FromPercent(1)));
                    x.Duration = d;
                    tr.BeginAnimation(TranslateTransform.XProperty, x);
                    DoubleAnimationUsingKeyFrames sc = new DoubleAnimationUsingKeyFrames();
                    sc.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromPercent(0)));
                    sc.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
                    sc.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromPercent(1)));
                    sc.Duration = d;
                    s.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                };
            }
            shootTimer.Interval = TimeSpan.FromMilliseconds(16000 + rnd.NextDouble() * 26000);
            shootTimer.Start();
        }
    }
}
