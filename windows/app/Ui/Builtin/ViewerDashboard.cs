using System;
using System.Collections.Generic;
using File = System.IO.File;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The pictures and colours both dashboards share: Studio's assets
    /// (autosteer-icon.png, pedal_brake.png, pedal_acc.png, copied to
    /// Ui\Builtin\Images) and the CSS filters style.css puts on them, worked
    /// out per pixel as Chromium does (sRGB, each step clamped): inactive
    /// grayscale(100%) brightness(0.6) at opacity 0.5, the brake's red
    /// (brightness(0) ... hue-rotate(349deg): #F70427), the accelerator's
    /// blue (brightness(0) ... hue-rotate(190deg): #1C91FF) and the compact
    /// accelerator's brightness(0) invert(100%) brightness(85%) (#D9D9D9).
    /// </summary>
    static class DashArt
    {
        // style.css / flightDeck.css tokens
        public static readonly Color Accent = Css.C("#0048ff");
        public static readonly Color AccentGlow = Css.C("rgba(0, 72, 255, 0.5)");
        public static readonly Brush AccentBrush = Css.Brush("#0048ff");
        public static readonly Brush Success = Css.Brush("#34d399");
        public static readonly Brush Warning = Css.Brush("#fbbf24");
        public static readonly Brush Error = Css.Brush("#f87171");
        public static readonly Brush White = Css.Brush("#fff");
        public static readonly Brush Text = Css.Brush("#E6EAF0");
        public static readonly Brush TextSecondary = Css.Brush("#C3CAD5");
        public static readonly Brush Muted = Css.Brush("#8A94A6");
        public static readonly Brush Faint = Css.Brush("#7D8798");
        public static readonly Brush Gray66 = Css.Brush("#666");
        public static readonly Brush Gray88 = Css.Brush("#888");
        public static readonly Brush DataAccent = Css.Brush("#C8F25A");
        public static readonly Brush SignalOff = Css.Brush("rgba(255, 255, 255, 0.15)");
        public static readonly Brush PanelBg = Css.Brush("#12161D");
        public static readonly Brush PanelBorder = Css.Brush("#1F2530");
        public static readonly Brush BrakeRed = Css.Brush("#F70427");
        public static readonly Brush AccelBlue = Css.Brush("#1C91FF");
        public static readonly Brush AccelWhite = Css.Brush("#D9D9D9");
        public static readonly Brush DotGreen = Css.Brush("#5ee9a0");

        /// <summary>CSS's normal line height for Inter (hhea ascender + descender) and JetBrains Mono.</summary>
        public const double InterLine = 1.21;
        public const double MonoLine = 1.32;
        /// <summary>Inter's ascender (where an SVG text's baseline sits under its top).</summary>
        public const double InterAscent = 0.96875;

        // index.html's turn signal arrows (viewBox -100 -90 200 175)
        public const string LeftArrow = "M-13.11,-33.71 C-15.61,-33.7 -16.06,-36.09 -15.15,-38.05 C-9.81,-49.52 -5.3,-59.32 -1.63,-67.45 C1.42,-74.21 -0.76,-78.22 -5.55,-83.46 C-5.8,-83.72 -6.07,-83.94 -6.38,-84.12 C-14.17,-88.49 -20.03,-84.98 -25.77,-79.01 C-36.67,-67.69 -45.88,-58.89 -57.03,-47.37 C-62.85,-41.36 -73.29,-30.89 -88.34,-15.96 C-91.84,-12.49 -95.74,-8.94 -97.13,-4.62 C-99.07,1.36 -96.56,6.15 -92.31,10.41 C-59.34,43.5 -38.6,64.39 -30.11,73.08 C-27.75,75.49 -26.47,76.81 -26.28,77.03 C-23.64,80 -20.08,83.56 -16.05,83.93 C-13.88,84.12 -11.77,84.13 -9.72,83.94 C-9.38,83.91 -9.05,83.83 -8.73,83.7 C-1.64,80.77 1.19,75.32 -0.25,67.36 C-0.32,66.99 -0.43,66.63 -0.58,66.29 C-5.37,55.61 -9.91,45.68 -14.2,36.51 C-15.9,32.89 -13.31,31.3 -10.28,31.29 C5.65,31.24 34.48,31.23 76.21,31.24 C83.28,31.24 87.73,30.78 89.56,29.85 C93.33,27.94 96.02,24.45 97.65,19.38 C97.74,19.08 97.79,18.76 97.79,18.44 L97.73,-20.92 C97.73,-21.05 97.71,-21.18 97.68,-21.31 C96.46,-26.68 93.59,-30.44 89.07,-32.6 C87.14,-33.53 83.84,-33.99 79.19,-33.98 C43.76,-33.92 13,-33.83 -13.11,-33.71Z";
        public const string RightArrow = "M13.11,-33.71 C15.61,-33.7 16.06,-36.09 15.15,-38.05 C9.81,-49.52 5.3,-59.32 1.63,-67.45 C-1.42,-74.21 0.76,-78.22 5.55,-83.46 C5.8,-83.72 6.07,-83.94 6.38,-84.12 C14.17,-88.49 20.03,-84.98 25.77,-79.01 C36.67,-67.69 45.88,-58.89 57.03,-47.37 C62.85,-41.36 73.29,-30.89 88.34,-15.96 C91.84,-12.49 95.74,-8.94 97.13,-4.62 C99.07,1.36 96.56,6.15 92.31,10.41 C59.34,43.5 38.6,64.39 30.11,73.08 C27.75,75.49 26.47,76.81 26.28,77.03 C23.64,80 20.08,83.56 16.05,83.93 C13.88,84.12 11.77,84.13 9.72,83.94 C9.38,83.91 9.05,83.83 8.73,83.7 C1.64,80.77 -1.19,75.32 0.25,67.36 C0.32,66.99 0.43,66.63 0.58,66.29 C5.37,55.61 9.91,45.68 14.2,36.51 C15.9,32.89 13.31,31.3 10.28,31.29 C-5.65,31.24 -34.48,31.23 -76.21,31.24 C-83.28,31.24 -87.73,30.78 -89.56,29.85 C-93.33,27.94 -96.02,24.45 -97.65,19.38 C-97.74,19.08 -97.79,18.76 -97.79,18.44 L-97.73,-20.92 C-97.73,-21.05 -97.71,-21.18 -97.68,-21.31 C-96.46,-26.68 -93.59,-30.44 -89.07,-32.6 C-87.14,-33.53 -83.84,-33.99 -79.19,-33.98 C-43.76,-33.92 -13,-33.83 13.11,-33.71Z";
        // Material Symbols Outlined expand_more (viewBox 0 -960 960 960)
        public const string ExpandMore = "M480-345 240-585l56-56 184 184 184-184 56 56-240 240Z";

        static BitmapSource autosteer, autosteerGray, brake, brakeGray, accel, accelGray;
        static bool loaded;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            autosteer = Png("autosteer-icon.png");
            brake = Png("pedal_brake.png");
            accel = Png("pedal_acc.png");
            autosteerGray = Gray(autosteer);
            brakeGray = Gray(brake);
            accelGray = Gray(accel);
        }

        public static BitmapSource Autosteer { get { Load(); return autosteer; } }
        public static BitmapSource AutosteerGray { get { Load(); return autosteerGray; } }
        public static BitmapSource Brake { get { Load(); return brake; } }
        public static BitmapSource BrakeGray { get { Load(); return brakeGray; } }
        public static BitmapSource Accel { get { Load(); return accel; } }
        public static BitmapSource AccelGray { get { Load(); return accelGray; } }

        /// <summary>Ui\Builtin\Images beside the exe (or in a checkout), else the repository's assets\.</summary>
        internal static BitmapSource Png(string name)
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] tries =
            {
                System.IO.Path.Combine(exe, "Ui", "Builtin", "Images", name),
                System.IO.Path.Combine(exe, "Images", name),
                System.IO.Path.Combine(exe, "..", "Ui", "Builtin", "Images", name),
                System.IO.Path.Combine(exe, "..", "..", "..", "assets", name),
                System.IO.Path.Combine(exe, "..", "..", "..", "..", "assets", name)
            };
            foreach (string f in tries)
            {
                if (!File.Exists(f)) continue;
                try
                {
                    BitmapImage b = new BitmapImage();
                    b.BeginInit();
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.UriSource = new Uri(System.IO.Path.GetFullPath(f));
                    b.EndInit();
                    b.Freeze();
                    return b;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>grayscale(100%) brightness(0.6), per pixel (the element's opacity 0.5 is set where it is drawn).</summary>
        static BitmapSource Gray(BitmapSource src)
        {
            if (src == null) return null;
            FormatConvertedBitmap c = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = c.PixelWidth, h = c.PixelHeight, stride = w * 4;
            byte[] px = new byte[h * stride];
            c.CopyPixels(px, stride, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0;
                double y = Math.Min(1, 0.2126 * r + 0.7152 * g + 0.0722 * b);
                byte v = (byte)Math.Round(Math.Min(1, y * 0.6) * 255);
                px[i] = px[i + 1] = px[i + 2] = v;
            }
            BitmapSource o = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, px, stride);
            o.Freeze();
            return o;
        }

        /// <summary>An icon (object-fit: contain) in a box.</summary>
        public static Image Picture(BitmapSource src, double opacity)
        {
            Image i = new Image();
            i.Source = src;
            i.Stretch = Stretch.Uniform;
            i.Opacity = opacity;
            i.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality);
            return i;
        }

        /// <summary>The icon's shape in one colour (a CSS filter that starts with brightness(0)).</summary>
        public static Rectangle Tinted(BitmapSource src, Brush color)
        {
            Rectangle r = new Rectangle();
            r.Fill = color;
            if (src != null)
            {
                ImageBrush m = new ImageBrush(src);
                m.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode(m, BitmapScalingMode.HighQuality);
                m.Freeze();
                r.OpacityMask = m;
            }
            else r.Visibility = Visibility.Collapsed;
            r.IsHitTestVisible = false;
            return r;
        }

        /// <summary>A turn signal arrow (its SVG drawn at size, centred in its box).</summary>
        public static Path Arrow(bool left)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(left ? LeftArrow : RightArrow);
            p.Stretch = Stretch.Uniform;
            p.Fill = SignalOff;
            p.IsHitTestVisible = false;
            return p;
        }

        /// <summary>A glow (filter: drop-shadow(0 0 blur color)).</summary>
        public static DropShadowEffect Glow(Color c, double blur)
        {
            DropShadowEffect e = new DropShadowEffect();
            e.Color = Color.FromRgb(c.R, c.G, c.B);
            e.Opacity = c.A / 255.0;
            e.BlurRadius = blur;
            e.ShadowDepth = 0;
            e.Freeze();
            return e;
        }

        public static TextBlock Label(double size, FontWeight weight, Brush color, FontFamily font)
        {
            TextBlock t = new TextBlock();
            t.FontFamily = font;
            t.FontSize = size;
            t.FontWeight = weight;
            t.Foreground = color;
            t.IsHitTestVisible = false;
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            return t;
        }

        public static SpacedText Spaced(double size, FontWeight weight, double spacingPx, Brush color, FontFamily font)
        {
            SpacedText t = new SpacedText("", size, weight, spacingPx / size);
            t.FontFamily = font;
            t.Foreground = color;
            t.IsHitTestVisible = false;
            return t;
        }

        public static double Clamp(double min, double v, double max)
        {
            return Math.Max(min, Math.Min(v, max));
        }
    }

    /// <summary>
    /// The accelerator icon in its display mode (Settings > Accelerator
    /// Display, .accel-pedal-container.mode-*): Icon Bar (the coloured icon
    /// revealed from the bottom, clip-path inset(top% 0 0 0)), Solid Color
    /// (the whole icon coloured while pressed) or Side Bar (a 3 px bar
    /// beside the icon filling from the bottom, the icon coloured while
    /// pressed).
    /// </summary>
    sealed class AccelIcon : Panel
    {
        readonly Image gray = DashArt.Picture(DashArt.AccelGray, 0.5);
        readonly Rectangle lit;
        readonly Rectangle fill;
        readonly Border bar = new Border();
        string mode = "iconbar";
        bool active;
        double top = 100;

        /// <summary>The icon's size and the side bar's width and gap.</summary>
        public double Icon = 18, BarWidth = 3, BarGap = 2;

        public AccelIcon(Brush color)
        {
            lit = DashArt.Tinted(DashArt.Accel, color);
            fill = DashArt.Tinted(DashArt.Accel, color);
            // .accel-fill in mode-sidebar: linear-gradient(to top, rgba(200,200,200,1), rgba(200,200,200,0.8)), radius 2
            bar.Background = new LinearGradientBrush(Css.C("rgba(200, 200, 200, 1)"), Css.C("rgba(200, 200, 200, 0.8)"), new Point(0, 1), new Point(0, 0));
            bar.CornerRadius = new CornerRadius(2);
            Children.Add(gray);
            Children.Add(lit);
            Children.Add(fill);
            Children.Add(bar);
            IsHitTestVisible = false;
            Sync();
        }

        public void Set(string newMode, bool pressed, double? topInset)
        {
            string m = newMode == "solid" || newMode == "sidebar" ? newMode : "iconbar";
            double t = topInset.HasValue ? topInset.Value : 100;
            if (m == mode && pressed == active && t == top) return;
            mode = m;
            active = pressed;
            top = t;
            Sync();
        }

        void Sync()
        {
            bool sidebar = mode == "sidebar", solid = mode == "solid";
            // (.accel-icon: gray at 0.5; coloured at 1 while pressed in Solid Color and Side Bar)
            bool coloured = active && (solid || sidebar);
            gray.Visibility = coloured ? Visibility.Collapsed : Visibility.Visible;
            lit.Visibility = coloured ? Visibility.Visible : Visibility.Collapsed;
            fill.Visibility = !solid && !sidebar ? Visibility.Visible : Visibility.Collapsed;
            bar.Visibility = sidebar ? Visibility.Visible : Visibility.Collapsed;
            InvalidateMeasure();
            InvalidateArrange();
        }

        public double BoxWidth { get { return mode == "sidebar" ? Icon + BarGap + BarWidth : Icon; } }

        protected override Size MeasureOverride(Size available)
        {
            foreach (UIElement c in Children) c.Measure(new Size(Icon, Icon));
            return new Size(BoxWidth, Icon);
        }

        protected override Size ArrangeOverride(Size size)
        {
            Rect icon = new Rect(0, 0, Icon, Icon);
            gray.Arrange(icon);
            lit.Arrange(icon);
            fill.Arrange(icon);
            double shown = Math.Max(0, Math.Min(1, 1 - top / 100));
            fill.Clip = new RectangleGeometry(new Rect(0, Icon * (1 - shown), Icon, Icon * shown));
            bar.Arrange(new Rect(Icon + BarGap, 0, BarWidth, Icon));
            bar.Clip = new RectangleGeometry(new Rect(0, Icon * (1 - shown), BarWidth, Icon * shown));
            return size;
        }
    }

    /// <summary>A pedal or the steering wheel: the gray icon at 0.5, or its active look.</summary>
    sealed class DashIcon : Grid
    {
        readonly UIElement off, on;

        public DashIcon(UIElement off, UIElement on)
        {
            this.off = off;
            this.on = on;
            Children.Add(off);
            Children.Add(on);
            IsHitTestVisible = false;
            Active = false;
        }

        public bool Active
        {
            set
            {
                off.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                on.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>gforceMeter.js's #gforceMeter: the 60 x 60 SVG drawn at the meter's size.</summary>
    sealed class GForceMeter : Viewbox
    {
        readonly Ellipse dot = Circle(30, 30, 4, DashArt.DotGreen, null);
        readonly Ellipse[] trail =
        {
            Circle(30, 30, 2.5, Css.Brush("rgba(94, 233, 160, 0.4)"), null),
            Circle(30, 30, 2, Css.Brush("rgba(94, 233, 160, 0.2)"), null),
            Circle(30, 30, 1.5, Css.Brush("rgba(94, 233, 160, 0.1)"), null)
        };

        public GForceMeter(double size)
        {
            Width = size;
            Height = size;
            Stretch = Stretch.Uniform;
            Canvas c = new Canvas { Width = 60, Height = 60 };
            c.Children.Add(Circle(30, 30, 28, Css.Brush("rgba(0, 0, 0, 0.3)"), Css.Brush("rgba(255, 255, 255, 0.1)")));
            c.Children.Add(Circle(30, 30, 18, null, Css.Brush("rgba(255, 255, 255, 0.06)")));
            c.Children.Add(Circle(30, 30, 9, null, Css.Brush("rgba(255, 255, 255, 0.06)")));
            c.Children.Add(Line(30, 2, 30, 58, Css.Brush("rgba(255, 255, 255, 0.08)")));
            c.Children.Add(Line(2, 30, 58, 30, Css.Brush("rgba(255, 255, 255, 0.08)")));
            c.Children.Add(dot);
            foreach (Ellipse e in trail) c.Children.Add(e);
            Child = c;
            IsHitTestVisible = false;
        }

        internal static Ellipse Circle(double cx, double cy, double r, Brush fill, Brush stroke)
        {
            Ellipse e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = stroke != null ? 1 : 0 };
            Canvas.SetLeft(e, cx - r);
            Canvas.SetTop(e, cy - r);
            return e;
        }

        internal static Line Line(double x1, double y1, double x2, double y2, Brush stroke)
        {
            return new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = 1 };
        }

        static void Move(Ellipse e, double cx, double cy)
        {
            double r = e.Width / 2;
            // (an attribute that is not a number leaves the circle where SVG puts it: at 0)
            Canvas.SetLeft(e, (double.IsNaN(cx) ? 0 : cx) - r);
            Canvas.SetTop(e, (double.IsNaN(cy) ? 0 : cy) - r);
        }

        public void Show(DashboardView v)
        {
            Move(dot, v.DotX, v.DotY);
            for (int i = 0; i < 3; i++) Move(trail[i], v.TrailX[i], v.TrailY[i]);
            // #gforceDot.braking / .accelerating / .cornering-hard
            dot.Fill = v.GForceDotClass == "braking" ? DashArt.Error : v.GForceDotClass == "accelerating" ? DashArt.Success : v.GForceDotClass == "cornering-hard" ? DashArt.Warning : DashArt.DotGreen;
        }
    }

    /// <summary>compass.js's #compassMeter: the ring, the letters, the ticks and the needle turned to the heading.</summary>
    sealed class CompassMeter : Viewbox
    {
        readonly RotateTransform turn = new RotateTransform(0, 30, 30);

        public CompassMeter(double size)
        {
            Width = size;
            Height = size;
            Stretch = Stretch.Uniform;
            Canvas c = new Canvas { Width = 60, Height = 60, ClipToBounds = false };
            c.Children.Add(GForceMeter.Circle(30, 30, 28, Css.Brush("rgba(0, 0, 0, 0.3)"), Css.Brush("rgba(255, 255, 255, 0.1)")));
            c.Children.Add(Letter("N", 30, 10, 8, FontWeights.Bold, Css.Brush("rgba(255, 255, 255, 0.6)")));
            c.Children.Add(Letter("S", 30, 56, 7, FontWeights.Normal, Css.Brush("rgba(255, 255, 255, 0.3)")));
            c.Children.Add(Letter("W", 7, 33, 7, FontWeights.Normal, Css.Brush("rgba(255, 255, 255, 0.3)")));
            c.Children.Add(Letter("E", 53, 33, 7, FontWeights.Normal, Css.Brush("rgba(255, 255, 255, 0.3)")));
            Brush tick = Css.Brush("rgba(255, 255, 255, 0.15)");
            c.Children.Add(GForceMeter.Line(30, 12, 30, 16, tick));
            c.Children.Add(GForceMeter.Line(30, 44, 30, 48, tick));
            c.Children.Add(GForceMeter.Line(12, 30, 16, 30, tick));
            c.Children.Add(GForceMeter.Line(44, 30, 48, 30, tick));
            Canvas needle = new Canvas { Width = 60, Height = 60 };
            needle.Children.Add(new Polygon { Points = PointCollection.Parse("30,14 26,30 30,28 34,30"), Fill = Css.Brush("#e74c3c") });
            needle.Children.Add(new Polygon { Points = PointCollection.Parse("30,46 26,30 30,32 34,30"), Fill = Css.Brush("rgba(255, 255, 255, 0.4)") });
            needle.Children.Add(GForceMeter.Circle(30, 30, 3, Css.Brush("#222"), Css.Brush("rgba(255, 255, 255, 0.2)")));
            needle.RenderTransform = turn;
            c.Children.Add(needle);
            Child = c;
            IsHitTestVisible = false;
        }

        /// <summary>An SVG text (text-anchor middle, its baseline at y).</summary>
        static TextBlock Letter(string text, double x, double y, double size, FontWeight weight, Brush fill)
        {
            TextBlock t = DashArt.Label(size, weight, fill, U.Inter);
            t.Text = text;
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, x - t.DesiredSize.Width / 2);
            Canvas.SetTop(t, y - size * DashArt.InterAscent);
            return t;
        }

        public void Show(double heading)
        {
            turn.Angle = double.IsNaN(heading) ? 0 : heading;
        }
    }

    /// <summary>
    /// #dashboardVis, Studio's default dashboard (style.css with
    /// flightDeck.css): 480 x 200 (260 with its details open, 0.3 s), 20 px
    /// from the frame's top right corner, #12161D with a 1 px #1F2530 border,
    /// radius 24 and its shadow, over the video and the clip list (z-index
    /// 101), dragged anywhere in the frame above the controls bar. In it, as
    /// the CSS's negative margins place them (every part centred on the
    /// row's line 52.5 px down): the steering wheel (60 px icon, turning),
    /// the left blinker, the gear over the speed over the unit over the
    /// brake / Autopilot label / accelerator, the right blinker and the
    /// recording time; under them the instruments row (G-force meter with
    /// its two values, the compass with its heading) and the details toggle
    /// with Lat, Lon, Head and Seq.
    /// </summary>
    public sealed class StudioFloatingDashboard : Grid, IStudioTouchDrag
    {
        public const double PanelWidth = 480;
        public const double Collapsed = 200;
        public const double Expanded = 260;

        readonly Border shadow = new Border();
        readonly Border panel = new Border();
        readonly FloatingLayout layout;
        readonly TranslateTransform offset = new TranslateTransform();
        readonly bool still;
        double expand;
        bool dragging;
        Point dragStart;

        /// <summary>The details toggle (#toggleExtra) was pressed.</summary>
        public event Action ToggleExtra;

        public StudioFloatingDashboard(bool still)
        {
            this.still = still;
            Uid = "dashboardVis";
            Width = PanelWidth;
            Height = Collapsed;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(0, 20, 20, 0);
            RenderTransform = offset;
            // box-shadow 0 12px 34px rgba(0,0,0,0.45) (the inset highlights at 0.05 are left out)
            shadow.CornerRadius = new CornerRadius(24);
            shadow.Background = DashArt.PanelBg;
            shadow.Effect = Css.Shadow(12, 34, "rgba(0, 0, 0, 0.45)");
            shadow.IsHitTestVisible = false;
            Children.Add(shadow);
            panel.CornerRadius = new CornerRadius(24);
            panel.Background = DashArt.PanelBg;
            panel.BorderBrush = DashArt.PanelBorder;
            panel.BorderThickness = new Thickness(1);
            layout = new FloatingLayout(this);
            panel.Child = layout;
            Children.Add(panel);
            Cursor = Cursors.Hand;
            Panel.SetZIndex(this, 101);
            // draggablePanels.js: a press anywhere but the toggle drags it; the video under it never sees the press
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            LostMouseCapture += delegate { dragging = false; Cursor = Cursors.Hand; };
            PreviewMouseRightButtonDown += (s, e) => e.Handled = true;
            // studioTouch.js: a finger drags it as the mouse does
            StudioTouchLayer.SetDrag(this, this);
        }

        internal FloatingLayout Layout { get { return layout; } }

        /// <summary>The panel's drag offset (translate3d).</summary>
        public Vector Offset
        {
            get { return new Vector(offset.X, offset.Y); }
            set { offset.X = value.X; offset.Y = value.Y; }
        }

        internal void RaiseToggle()
        {
            Action h = ToggleExtra;
            if (h != null) h();
        }

        /// <summary>Draws the view (its details open or closed, 0.3 s).</summary>
        public void Show(DashboardView v, DashboardSettings s, double blinkLeft, double blinkRight)
        {
            double want = v.ExtraExpanded ? 1 : 0;
            if (want != expand)
            {
                expand = want;
                if (still) Height = want > 0 ? Expanded : Collapsed;
                else
                {
                    DoubleAnimation a = new DoubleAnimation(want > 0 ? Expanded : Collapsed, TimeSpan.FromMilliseconds(300));
                    a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
                    BeginAnimation(HeightProperty, a);
                }
            }
            layout.Show(v, s, blinkLeft, blinkRight);
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (Down(el => e.GetPosition(el), e.OriginalSource as DependencyObject)) CaptureMouse();
        }

        bool Down(PointAt at, DependencyObject source)
        {
            if (layout.OnToggle(source)) return false;
            dragging = true;
            dragStart = at(Parent as IInputElement) - Offset;
            Cursor = Cursors.SizeAll;
            return true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            Move(el => e.GetPosition(el));
        }

        void Move(PointAt at)
        {
            if (!dragging) return;
            FrameworkElement frame = Parent as FrameworkElement;
            if (frame == null) return;
            Point p = at(frame);
            Vector next = new Vector(p.X - dragStart.X, p.Y - dragStart.Y);
            Offset = DashDrag.Clamp(this, frame, next);
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Up();
        }

        void Up()
        {
            if (!dragging) return;
            dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Cursor = Cursors.Hand;
        }

        // ---- a finger (studioTouch.js) ------------------------------------------------------

        bool IStudioTouchDrag.IsControl(DependencyObject source) { return layout.IsToggle(source); }

        void IStudioTouchDrag.DragDown(PointAt at, DependencyObject source) { Down(at, source); }

        void IStudioTouchDrag.DragMove(PointAt at) { Move(at); }

        void IStudioTouchDrag.DragUp(PointAt at) { Up(); }

        /// <summary>The frame was resized: a panel with less than 50 px left in view goes to the nearest corner.</summary>
        public void KeepInView()
        {
            FrameworkElement frame = Parent as FrameworkElement;
            if (frame != null && Visibility == Visibility.Visible) Offset = DashDrag.Corner(this, frame, Offset);
        }

        /// <summary>resetPanelPosition.</summary>
        public void ResetPosition()
        {
            Offset = new Vector(0, 0);
        }
    }

    /// <summary>draggablePanels.js's sums: a panel's drag kept in the frame above the 60 px controls bar, and the resize's corner rule.</summary>
    static class DashDrag
    {
        public const double ControlsBar = 60;

        static Rect Base(FrameworkElement panel, FrameworkElement frame, Vector offset)
        {
            Point at = panel.TranslatePoint(new Point(0, 0), frame);
            return new Rect(at.X - offset.X, at.Y - offset.Y, panel.ActualWidth, panel.ActualHeight);
        }

        public static Vector Clamp(FrameworkElement panel, FrameworkElement frame, Vector want)
        {
            Vector cur = new Vector(panel.RenderTransform is TranslateTransform ? ((TranslateTransform)panel.RenderTransform).X : 0, panel.RenderTransform is TranslateTransform ? ((TranslateTransform)panel.RenderTransform).Y : 0);
            Rect b = Base(panel, frame, cur);
            double minX = -b.Left, maxX = frame.ActualWidth - b.Width - b.Left;
            double minY = -b.Top, maxY = frame.ActualHeight - b.Height - ControlsBar - b.Top;
            return new Vector(Math.Max(minX, Math.Min(want.X, maxX)), Math.Max(minY, Math.Min(want.Y, maxY)));
        }

        public static Vector Corner(FrameworkElement panel, FrameworkElement frame, Vector offset)
        {
            Rect b = Base(panel, frame, offset);
            Rect r = new Rect(b.Left + offset.X, b.Top + offset.Y, b.Width, b.Height);
            double vw = frame.ActualWidth, vh = frame.ActualHeight;
            const double minVisible = 50, margin = 20;
            bool outside = r.Right < minVisible || r.Left > vw - minVisible || r.Bottom < minVisible || r.Top > vh - minVisible;
            if (!outside) return offset;
            double cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            double left = cx < vw / 2 ? margin : vw - r.Width - margin;
            double top = cy < vh / 2 ? margin : vh - r.Height - 80;
            return new Vector(left - b.Left, top - b.Top);
        }
    }

    /// <summary>The floating dashboard's parts, placed as style.css's flex rows and negative margins put them.</summary>
    sealed class FloatingLayout : Panel
    {
        // the row's line: every part of .vis-content is centred 52.5 px down
        const double Line = 52.5;
        const double Inner = StudioFloatingDashboard.PanelWidth - 2;

        readonly StudioFloatingDashboard owner;
        readonly Grid wheel = new Grid();
        readonly RotateTransform wheelTurn = new RotateTransform();
        readonly DashIcon autosteer;
        readonly Path blinkL = DashArt.Arrow(true), blinkR = DashArt.Arrow(false);
        readonly SpacedText gear = DashArt.Spaced(14, FontWeights.SemiBold, 1, DashArt.White, U.Inter);
        readonly TextBlock speed = DashArt.Label(36, FontWeights.Light, DashArt.White, U.Inter);
        readonly SpacedText unit = DashArt.Spaced(11, FontWeights.SemiBold, 0.5, DashArt.Muted, U.Inter);
        readonly DashIcon brake;
        readonly TextBlock ap = DashArt.Label(14, FontWeights.Medium, DashArt.Muted, U.Inter);
        readonly AccelIcon accel = new AccelIcon(DashArt.AccelBlue);
        readonly SpacedText time = DashArt.Spaced(14, FontWeights.SemiBold, 0.5, DashArt.Text, U.Inter);
        readonly Rectangle instrumentsLine = new Rectangle { Fill = Css.Brush("rgba(255, 255, 255, 0.08)"), IsHitTestVisible = false };
        readonly GForceMeter gforce = new GForceMeter(50);
        readonly TextBlock gLabel = DashArt.Label(7, FontWeights.Bold, DashArt.Faint, U.Inter);
        readonly TextBlock gX = DashArt.Label(11, FontWeights.SemiBold, DashArt.Gray66, U.JetBrains);
        readonly TextBlock gY = DashArt.Label(11, FontWeights.SemiBold, DashArt.Gray66, U.JetBrains);
        readonly CompassMeter compass = new CompassMeter(45);
        readonly TextBlock heading = DashArt.Label(11, FontWeights.SemiBold, DashArt.Gray88, U.JetBrains);
        readonly Rectangle extraLine = new Rectangle { Fill = Css.Brush("rgba(255, 255, 255, 0.1)"), IsHitTestVisible = false };
        readonly Border toggle = new Border();
        readonly SpacedText[] labels = new SpacedText[3];
        readonly TextBlock[] values = new TextBlock[3];
        readonly SpacedText seqLabel = DashArt.Spaced(8, FontWeights.Normal, 0, DashArt.Gray66, U.Inter);
        readonly TextBlock seq = DashArt.Label(8, FontWeights.Normal, DashArt.Gray88, U.JetBrains);
        readonly DropShadowEffect apGlow = DashArt.Glow(DashArt.AccentGlow, 8);
        readonly DropShadowEffect signalGlow = DashArt.Glow(Css.C("rgba(52, 211, 153, 0.7)"), 8);
        double extraHeight;

        public FloatingLayout(StudioFloatingDashboard owner)
        {
            this.owner = owner;
            ClipToBounds = false;
            // .autosteer-wrapper (turned as one) with the 60 px icon: gray at 0.5, or coloured with its two glows
            Image lit = DashArt.Picture(DashArt.Autosteer, 1);
            Border glow2 = new Border { Child = lit, Effect = DashArt.Glow(DashArt.AccentGlow, 15) };
            Border glow1 = new Border { Child = glow2, Effect = DashArt.Glow(DashArt.AccentGlow, 25) };
            autosteer = new DashIcon(DashArt.Picture(DashArt.AutosteerGray, 0.5), glow1);
            wheel.Children.Add(autosteer);
            wheel.RenderTransform = wheelTurn;
            wheel.RenderTransformOrigin = new Point(0.5, 0.5);
            wheel.IsHitTestVisible = false;
            brake = new DashIcon(DashArt.Picture(DashArt.BrakeGray, 0.5), DashArt.Tinted(DashArt.Brake, DashArt.BrakeRed));
            gLabel.Text = "G";
            heading.TextAlignment = TextAlignment.Left;
            // #toggleExtra: full width, 20 px, the expand_more symbol (16 px, #666); hover rgba(255,255,255,0.05)
            Path chevron = new Path { Data = Geometry.Parse("F1 " + DashArt.ExpandMore), Fill = DashArt.Gray66, Stretch = Stretch.Uniform, Width = 16 * 480.0 / 960, Height = 16 * 240.0 / 960, IsHitTestVisible = false };
            Grid chevronBox = new Grid { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            chevron.HorizontalAlignment = HorizontalAlignment.Center;
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevronBox.Children.Add(chevron);
            toggle.Child = chevronBox;
            toggle.Background = Brushes.Transparent;
            toggle.Cursor = Cursors.Hand;
            toggle.ToolTip = "Toggle Details";
            toggle.MouseEnter += delegate { toggle.Background = Css.Brush("rgba(255, 255, 255, 0.05)"); };
            toggle.MouseLeave += delegate { toggle.Background = Brushes.Transparent; };
            string[] names = { "LAT", "LON", "HEAD" };
            for (int i = 0; i < 3; i++)
            {
                labels[i] = DashArt.Spaced(8, FontWeights.Normal, 0.3, DashArt.Gray66, U.Inter);
                labels[i].Text = names[i];
                values[i] = DashArt.Label(10, FontWeights.SemiBold, DashArt.DataAccent, U.JetBrains);
            }
            seqLabel.Text = "SEQ";
            gX.ToolTip = "Lateral G";
            gY.ToolTip = "Longitudinal G";
            foreach (UIElement e in new UIElement[] { wheel, blinkL, blinkR, gear, speed, unit, brake, ap, accel, time, instrumentsLine, gforce, gLabel, gX, gY, compass, heading, extraLine, toggle, seqLabel, seq })
                Children.Add(e);
            foreach (SpacedText l in labels) Children.Add(l);
            foreach (TextBlock v in values) Children.Add(v);
        }

        /// <summary>Whether a press landed on the details toggle (then it toggles, not drags).</summary>
        /// <summary>The press is on #toggleExtra (a button: a finger taps it, studioTouch.js's SKIP).</summary>
        public bool IsToggle(DependencyObject source)
        {
            for (DependencyObject d = source; d != null && d != owner; d = VisualTreeHelper.GetParent(d))
                if (d == toggle) return true;
            return false;
        }

        public bool OnToggle(DependencyObject source)
        {
            for (DependencyObject d = source; d != null && d != this; d = VisualTreeHelper.GetParent(d))
            {
                if (d != toggle) continue;
                owner.RaiseToggle();
                return true;
            }
            return false;
        }

        public void Show(DashboardView v, DashboardSettings s, double blinkLeft, double blinkRight)
        {
            wheelTurn.Angle = v.Wheel.Shown;
            autosteer.Active = v.AutosteerActive;
            Signal(blinkL, v.BlinkLeft, blinkLeft);
            Signal(blinkR, v.BlinkRight, blinkRight);
            gear.Text = (v.Gear ?? "").ToUpperInvariant();
            gear.Foreground = v.GearActive ? DashArt.AccentBrush : DashArt.White;
            speed.Text = v.Speed;
            unit.Text = (v.Unit ?? "").ToUpperInvariant();
            brake.Active = v.BrakeActive;
            ap.Text = v.Ap;
            ap.Foreground = v.ApActive ? DashArt.AccentBrush : DashArt.Muted;
            ap.Effect = v.ApActive ? apGlow : null;
            accel.Set(s.AccelMode, v.AccelActive, v.AccelTopInset);
            time.Text = v.RecordingTime;
            gforce.Show(v);
            gX.Text = v.GForceX;
            gY.Text = v.GForceY;
            gX.Foreground = ValueBrush(v.GForceXClass);
            gY.Foreground = ValueBrush(v.GForceYClass);
            compass.Show(v.Needle);
            heading.Text = v.Compass;
            values[0].Text = v.Lat;
            values[1].Text = v.Lon;
            values[2].Text = v.Heading;
            seq.Text = v.Seq;
            // (a text that changes size lays the panel out again; colours and turns need no layout)
        }

        void Signal(Path p, bool on, double opacity)
        {
            // .turn-signal.active: --success-color with its glow, blinking (0.15 off); else rgba(255,255,255,0.15)
            p.Fill = on ? DashArt.Success : DashArt.SignalOff;
            p.Effect = on ? signalGlow : null;
            p.Opacity = on ? opacity : 1;
        }

        static Brush ValueBrush(string c)
        {
            if (c == "high") return DashArt.Error;
            if (c == "positive") return DashArt.Success;
            if (c == "negative") return DashArt.Warning;
            return DashArt.Gray66;
        }

        protected override Size MeasureOverride(Size available)
        {
            Size inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            foreach (UIElement c in Children) c.Measure(inf);
            return new Size(0, 0);
        }

        static void At(UIElement e, double left, double top, double w, double h)
        {
            e.Arrange(new Rect(left, top, Math.Max(0, w), Math.Max(0, h)));
        }

        /// <summary>A text's own box centred on (cx, cy): CSS centres the glyphs in their line box.</summary>
        static Rect Centre(UIElement e, double cx, double cy)
        {
            Size d = e.DesiredSize;
            At(e, cx - d.Width / 2, cy - d.Height / 2, d.Width, d.Height);
            return new Rect(cx - d.Width / 2, cy - d.Height / 2, d.Width, d.Height);
        }

        /// <summary>A part of the details cut at the bottom of .extra-data (its height opening 0 -> 70).</summary>
        static void Cut(FrameworkElement e, Rect at, double bottom)
        {
            e.Clip = new RectangleGeometry(new Rect(-50, -50, at.Width + 100, Math.Max(0, bottom - at.Top + 50)));
        }

        protected override Size ArrangeOverride(Size size)
        {
            // the panel's details: extra-data's height 0 -> 70 with the panel's 200 -> 260
            extraHeight = 70 * Math.Max(0, Math.Min(1, (size.Height + 2 - StudioFloatingDashboard.Collapsed) / (StudioFloatingDashboard.Expanded - StudioFloatingDashboard.Collapsed)));
            Clip = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height), 23, 23);

            // .autosteer-section: 150 wide at -15, the wrapper 145 x 145 lifted 40 px; the icon 60 x 60 at its centre
            At(wheel, 30, Line - 30, 60, 60);

            // .right-cluster: min-width 90 (with its 5 px right padding) at the right end, 20 px to the left
            double timeW = time.DesiredSize.Width;
            double rightW = Math.Max(90, timeW + 5);
            double timeCx = Inner - rightW + (rightW - 5) / 2 - 20;
            Centre(time, timeCx, Line);

            // .center-cluster: from 114 (after the autosteer section, 24 gap, -45) to the right cluster less 24; its row centred
            double left = 114, right = Inner - rightW - 24;
            double accelW = accel.BoxWidth;
            double apRow = 18 + 6 + ap.DesiredSize.Width + 6 + accelW;
            double sdW = Math.Max(Math.Max(gear.DesiredSize.Width, speed.DesiredSize.Width), Math.Max(unit.DesiredSize.Width, apRow));
            double rowW = 32 + 16 + sdW + 16 + 32;
            double rowLeft = left + ((right - left) - rowW) / 2;
            double sdCx = rowLeft + 32 + 16 + sdW / 2;
            // .turn-signal: 32 x 32 boxes, the arrow 28 x 28 in them
            At(blinkL, rowLeft + 2, Line - 14, 28, 28);
            At(blinkR, rowLeft + rowW - 30, Line - 14, 28, 28);
            // .speed-display's column (gap 2): gear 14px, speed 36px (line-height 1), unit 11px (+1), the brake / label / pedal row 18
            double gearH = 14 * DashArt.InterLine, unitH = 11 * DashArt.InterLine + 1, rowH = Math.Max(18, 14 * DashArt.InterLine);
            double sdH = gearH + 2 + 36 + 2 + unitH + 2 + rowH;
            double y = Line - sdH / 2;
            Centre(gear, sdCx, y + gearH / 2);
            y += gearH + 2;
            Centre(speed, sdCx, y + 18);
            y += 36 + 2;
            Centre(unit, sdCx, y + 1 + (unitH - 1) / 2);
            y += unitH + 2;
            double rowCy = y + rowH / 2;
            double x = sdCx - apRow / 2;
            At(brake, x, rowCy - 9, 18, 18);
            x += 18 + 6;
            Centre(ap, x + ap.DesiredSize.Width / 2, rowCy);
            x += ap.DesiredSize.Width + 6;
            At(accel, x, rowCy - 9, accelW, 18);

            // .instruments-row: 65 px up into the 167 px row (102), 25 px in, a 1 px line on top, padding 2 10, centred
            double instTop = 102;
            At(instrumentsLine, 25, instTop, Inner - 25, 1);
            double cy = instTop + 1 + 2 + 25;
            double gvW = Math.Max(36, Math.Max(gX.DesiredSize.Width, gY.DesiredSize.Width));
            double cvW = Math.Max(36, heading.DesiredSize.Width);
            double instW = 50 + 6 + gvW + 12 + 45 + 6 + cvW;
            double instLeft = 25 + 10 + ((Inner - 25 - 20) - instW) / 2;
            At(gforce, instLeft, cy - 25, 50, 50);
            // .gforce-label: 7px, 2 px from the meter's bottom right
            Centre(gLabel, instLeft + 50 - 2 - gLabel.DesiredSize.Width / 2, cy + 25 - 2 - 7 * DashArt.InterLine / 2);
            double valH = 11 * DashArt.MonoLine;
            double valuesTop = cy - (valH * 2 + 2) / 2;
            double valRight = instLeft + 56 + gvW;
            At(gX, valRight - gX.DesiredSize.Width, valuesTop + valH / 2 - gX.DesiredSize.Height / 2, gX.DesiredSize.Width, gX.DesiredSize.Height);
            At(gY, valRight - gY.DesiredSize.Width, valuesTop + valH + 2 + valH / 2 - gY.DesiredSize.Height / 2, gY.DesiredSize.Width, gY.DesiredSize.Height);
            double compassLeft = valRight + 12;
            At(compass, compassLeft, cy - 22.5, 45, 45);
            At(heading, compassLeft + 51, cy - heading.DesiredSize.Height / 2, heading.DesiredSize.Width, heading.DesiredSize.Height);

            // .extra-data-container: a 1 px line at 157, the 20 px toggle, then the 70 px details as they open
            double extraTop = instTop + 55;
            At(extraLine, 0, extraTop, Inner, 1);
            At(toggle, 0, extraTop + 1, Inner, 20);
            double detailsTop = extraTop + 21;
            double colW = (Inner - 20 - 16) / 3;
            double labelH = 8 * DashArt.InterLine, valueH = 10 * DashArt.MonoLine;
            double bottom = detailsTop + extraHeight;
            Visibility shown = extraHeight > 0.5 ? Visibility.Visible : Visibility.Hidden;
            for (int i = 0; i < 3; i++)
            {
                double ccx = 10 + i * (colW + 8) + colW / 2;
                labels[i].Visibility = values[i].Visibility = shown;
                Cut(labels[i], Centre(labels[i], ccx, detailsTop + 6 + labelH / 2), bottom);
                Cut(values[i], Centre(values[i], ccx, detailsTop + 6 + labelH + valueH / 2), bottom);
            }
            double seqCy = detailsTop + 6 + labelH + valueH + 6 + 2 + 8 * DashArt.MonoLine / 2;
            double seqW = seqLabel.DesiredSize.Width + 6 + seq.DesiredSize.Width;
            seqLabel.Visibility = seq.Visibility = shown;
            Cut(seqLabel, Centre(seqLabel, Inner / 2 - seqW / 2 + seqLabel.DesiredSize.Width / 2, seqCy), bottom);
            Cut(seq, Centre(seq, Inner / 2 + seqW / 2 - seq.DesiredSize.Width / 2, seqCy), bottom);
            return size;
        }
    }

    /// <summary>
    /// #dashboardVisCompact, Studio's compact dashboard: fixed to the front
    /// camera (the tile's bottom: 98 % of its width, clamp(50px, 8%, 90px)
    /// tall, 0.8 % up; no pointer) or movable (480 x 56 over the frame,
    /// dragged), its font clamp(10px, 1.2vh, 16px) and its row zoomed down
    /// on narrow tiles (0.85 at 450 px, 0.75 at 380, 0.65 at 320, 0.55 at
    /// 260). The row: brake, gear, left blinker, speed over unit, Autopilot
    /// label over the recording time, right blinker, steering wheel,
    /// accelerator, spaced evenly; PARKED and the time alone in a gap.
    /// </summary>
    public sealed class StudioCompactDashboard : Grid, IStudioTouchDrag
    {
        readonly Border shadow = new Border();
        readonly Border panel = new Border();
        readonly CompactLayout layout = new CompactLayout();
        readonly TranslateTransform offset = new TranslateTransform();
        bool fixedToCamera = true;
        bool dragging;
        Point dragStart;

        public StudioCompactDashboard()
        {
            Uid = "dashboardVisCompact";
            shadow.CornerRadius = new CornerRadius(24);
            shadow.Background = DashArt.PanelBg;
            shadow.Effect = Css.Shadow(12, 34, "rgba(0, 0, 0, 0.45)");
            shadow.IsHitTestVisible = false;
            Children.Add(shadow);
            panel.CornerRadius = new CornerRadius(24);
            panel.Background = DashArt.PanelBg;
            panel.BorderBrush = DashArt.PanelBorder;
            panel.BorderThickness = new Thickness(1);
            panel.Child = layout;
            panel.ClipToBounds = true;
            Children.Add(panel);
            Panel.SetZIndex(this, 10);
            RenderTransform = offset;
            MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (Down(el => e.GetPosition(el))) CaptureMouse();
            };
            MouseMove += (s, e) => Move(el => e.GetPosition(el));
            MouseLeftButtonUp += (s, e) => { e.Handled = true; dragging = false; if (IsMouseCaptured) ReleaseMouseCapture(); };
            LostMouseCapture += delegate { dragging = false; };
            // studioTouch.js: a finger moves it as the mouse does
            StudioTouchLayer.SetDrag(this, this);
            Fixed = true;
        }

        bool Down(PointAt at)
        {
            if (fixedToCamera) return false;
            dragging = true;
            dragStart = at(Parent as IInputElement) - new Vector(offset.X, offset.Y);
            return true;
        }

        void Move(PointAt at)
        {
            FrameworkElement frame = Parent as FrameworkElement;
            if (!dragging || frame == null) return;
            Point p = at(frame);
            Vector v = DashDrag.Clamp(this, frame, new Vector(p.X - dragStart.X, p.Y - dragStart.Y));
            offset.X = v.X;
            offset.Y = v.Y;
        }

        bool IStudioTouchDrag.IsControl(DependencyObject source) { return false; }

        void IStudioTouchDrag.DragDown(PointAt at, DependencyObject source) { Down(at); }

        void IStudioTouchDrag.DragMove(PointAt at) { Move(at); }

        void IStudioTouchDrag.DragUp(PointAt at) { dragging = false; }

        internal CompactLayout Layout { get { return layout; } }

        /// <summary>.fixed-to-camera (in the front tile, no pointer) or .draggable (over the frame, grabbed).</summary>
        public bool Fixed
        {
            get { return fixedToCamera; }
            set
            {
                fixedToCamera = value;
                IsHitTestVisible = !value;
                Cursor = value ? null : Cursors.Hand;
                offset.X = offset.Y = 0;
            }
        }

        /// <summary>The fixed dashboard in a tile of this size, the frame this tall (1.2vh).</summary>
        public void FitTile(double tileW, double tileH, double frameH)
        {
            double w = tileW * 0.98;
            double h = DashArt.Clamp(50, tileH * 0.08, 90);
            Width = Math.Max(0, w);
            Height = h;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Bottom;
            Margin = new Thickness(0, 0, 0, tileH * 0.008);
            panel.Padding = new Thickness(DashArt.Clamp(10, tileW * 0.015, 16), DashArt.Clamp(5, tileW * 0.007, 8), DashArt.Clamp(10, tileW * 0.015, 16), DashArt.Clamp(5, tileW * 0.007, 8));
            // @container tile (max-width: 450 / 380 / 320 / 260px): zoom
            double zoom = tileW <= 260 ? 0.55 : tileW <= 320 ? 0.65 : tileW <= 380 ? 0.75 : tileW <= 450 ? 0.85 : 1;
            layout.Fit(DashArt.Clamp(10, frameH * 0.012, 16), zoom);
        }

        /// <summary>The movable dashboard: 480 x 56, padding 6px 10px, at the front tile's bottom (in the frame) to start with.</summary>
        public void FitFree(Point at, double frameH)
        {
            Width = 480;
            Height = 56;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(at.X, at.Y, 0, 0);
            panel.Padding = new Thickness(10, 6, 10, 6);
            layout.Fit(DashArt.Clamp(10, frameH * 0.012, 16), 1);
        }

        public void Show(DashboardView v, DashboardSettings s, double blinkLeft, double blinkRight)
        {
            layout.Show(v, s, blinkLeft, blinkRight);
        }
    }

    /// <summary>.vis-content-compact's flex row (space-evenly, align-items center) with its em sizes worked out.</summary>
    sealed class CompactLayout : Panel
    {
        readonly DashIcon brake;
        readonly SpacedText gear = DashArt.Spaced(14, FontWeights.SemiBold, 0.5, DashArt.White, U.Inter);
        readonly Path blinkL = DashArt.Arrow(true), blinkR = DashArt.Arrow(false);
        readonly TextBlock speed = DashArt.Label(14, FontWeights.Bold, DashArt.White, U.Inter);
        readonly SpacedText unit = DashArt.Spaced(8, FontWeights.Medium, 0.5, DashArt.Muted, U.Inter);
        readonly TextBlock ap = DashArt.Label(14, FontWeights.Normal, DashArt.Muted, U.Inter);
        readonly TextBlock time = DashArt.Label(8, FontWeights.Medium, DashArt.TextSecondary, U.Inter);
        readonly Grid wheel = new Grid();
        readonly RotateTransform wheelTurn = new RotateTransform();
        readonly DashIcon autosteer;
        readonly AccelIcon accel = new AccelIcon(DashArt.AccelWhite);
        readonly SpacedText parked = DashArt.Spaced(14, FontWeights.Bold, 0, DashArt.White, U.Inter);
        readonly DropShadowEffect apGlow = DashArt.Glow(DashArt.AccentGlow, 8);
        readonly DropShadowEffect signalGlow = DashArt.Glow(Css.C("rgba(52, 211, 153, 0.7)"), 8);
        double font = 10.8, zoom = 1;
        bool isParked;

        public CompactLayout()
        {
            brake = new DashIcon(DashArt.Picture(DashArt.BrakeGray, 0.5), DashArt.Tinted(DashArt.Brake, DashArt.BrakeRed));
            Image lit = DashArt.Picture(DashArt.Autosteer, 1);
            autosteer = new DashIcon(DashArt.Picture(DashArt.AutosteerGray, 0.5), new Border { Child = lit, Effect = DashArt.Glow(DashArt.AccentGlow, 10) });
            wheel.Children.Add(autosteer);
            wheel.RenderTransform = wheelTurn;
            wheel.RenderTransformOrigin = new Point(0.5, 0.5);
            parked.Text = "PARKED";
            foreach (UIElement e in new UIElement[] { brake, gear, blinkL, speed, unit, ap, time, blinkR, wheel, accel, parked }) Children.Add(e);
            IsHitTestVisible = false;
            Fit(font, zoom);
        }

        /// <summary>The dashboard's font size (clamp(10px, 1.2vh, 16px)) and the row's zoom.</summary>
        public void Fit(double fontSize, double z)
        {
            font = fontSize;
            zoom = z;
            double F = font;
            gear.FontSize = DashArt.Clamp(14, 1.6 * F, 22) * z;
            gear.Spacing = 0.5 / DashArt.Clamp(14, 1.6 * F, 22);
            speed.FontSize = DashArt.Clamp(12, 1.5 * F, 21) * z;
            unit.FontSize = DashArt.Clamp(8, 0.9 * F, 14) * z;
            unit.Spacing = 0.5 / DashArt.Clamp(8, 0.9 * F, 14);
            ap.FontSize = DashArt.Clamp(12, 1.5 * F, 21) * z;
            time.FontSize = DashArt.Clamp(8, 0.9 * F, 14) * z;
            parked.FontSize = DashArt.Clamp(13, 1.7 * F, 24) * z;
            parked.Spacing = 0.08;
            double icon = DashArt.Clamp(18, 2.5 * F, 28) * z;
            accel.Icon = icon;
            accel.BarWidth = DashArt.Clamp(2, 0.3 * F, 3) * z;
            accel.BarGap = DashArt.Clamp(1, 0.2 * F, 2) * z;
            InvalidateMeasure();
            InvalidateArrange();
        }

        public void Show(DashboardView v, DashboardSettings s, double blinkLeft, double blinkRight)
        {
            if (isParked != v.Parked) InvalidateArrange();
            isParked = v.Parked;
            brake.Active = v.BrakeActive;
            gear.Text = (v.GearCompact ?? "").ToUpperInvariant();
            gear.Foreground = v.GearActive ? DashArt.AccentBrush : DashArt.White;
            Signal(blinkL, v.BlinkLeft, blinkLeft);
            Signal(blinkR, v.BlinkRight, blinkRight);
            speed.Text = v.SpeedCompact;
            unit.Text = (v.UnitCompact ?? "").ToUpperInvariant();
            ap.Text = v.ApCompact;
            ap.Foreground = v.ApActive ? DashArt.AccentBrush : DashArt.Muted;
            ap.Effect = v.ApActive ? apGlow : null;
            time.Text = v.RecordingTime;
            wheelTurn.Angle = v.Wheel.Shown;
            autosteer.Active = v.AutosteerActive;
            accel.Set(s.AccelMode, v.AccelActive, v.AccelTopInset);
        }

        void Signal(Path p, bool on, double opacity)
        {
            p.Fill = on ? DashArt.Success : DashArt.SignalOff;
            p.Effect = on ? signalGlow : null;
            p.Opacity = on ? opacity : 1;
        }

        protected override Size MeasureOverride(Size available)
        {
            Size inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            foreach (UIElement c in Children) c.Measure(inf);
            return new Size(0, 0);
        }

        static void Place(UIElement e, double left, double cy, double w, double h)
        {
            e.Arrange(new Rect(left, cy - h / 2, Math.Max(0, w), Math.Max(0, h)));
        }

        protected override Size ArrangeOverride(Size size)
        {
            double F = font, z = zoom;
            double icon = DashArt.Clamp(18, 2.5 * F, 28) * z;
            double signalBox = DashArt.Clamp(20, 2.6 * F, 32) * z, signalSvg = DashArt.Clamp(16, 2.1 * F, 28) * z;
            double gearMin = DashArt.Clamp(35, 4.5 * F, 55) * z;
            double colMin = DashArt.Clamp(40, 5 * F, 65) * z;
            double colGap = DashArt.Clamp(1, 0.15 * F, 3) * z;
            double cy = size.Height / 2;
            // (each item: its width, then how to place it at x)
            List<KeyValuePair<double, Action<double>>> items = new List<KeyValuePair<double, Action<double>>>();
            UIElement[] gauges = { brake, gear, blinkL, speed, unit, ap, blinkR, wheel, accel };
            foreach (UIElement g in gauges) g.Visibility = isParked ? Visibility.Collapsed : Visibility.Visible;
            parked.Visibility = isParked ? Visibility.Visible : Visibility.Collapsed;
            // line-height 1 texts: their glyphs centred on the em box
            double speedLine = speed.FontSize, unitLine = unit.FontSize, apLine = ap.FontSize, timeLine = time.FontSize;
            Action<double> timeColumn;
            double timeColW;
            if (isParked)
            {
                // .telemetry-parked: PARKED (order -1) left of the autopilot section, which shows only the time; centred, gap clamp(10px, 3%, 30px)
                double colW = Math.Max(colMin, time.DesiredSize.Width);
                items.Add(new KeyValuePair<double, Action<double>>(parked.DesiredSize.Width, x => Place(parked, x, cy, parked.DesiredSize.Width, parked.DesiredSize.Height)));
                items.Add(new KeyValuePair<double, Action<double>>(colW, x => Place(time, x + (colW - time.DesiredSize.Width) / 2, cy, time.DesiredSize.Width, time.DesiredSize.Height)));
                double gap = DashArt.Clamp(10 * z, size.Width * 0.03, 30 * z);
                double total = items[0].Key + gap + items[1].Key;
                double x0 = (size.Width - total) / 2;
                items[0].Value(x0);
                items[1].Value(x0 + items[0].Key + gap);
                return size;
            }
            items.Add(new KeyValuePair<double, Action<double>>(icon, x => Place(brake, x, cy, icon, icon)));
            double gearW = Math.Max(gearMin, gear.DesiredSize.Width);
            items.Add(new KeyValuePair<double, Action<double>>(gearW, x => Place(gear, x + (gearW - gear.DesiredSize.Width) / 2, cy, gear.DesiredSize.Width, gear.DesiredSize.Height)));
            items.Add(new KeyValuePair<double, Action<double>>(signalBox, x => Place(blinkL, x + (signalBox - signalSvg) / 2, cy, signalSvg, signalSvg)));
            double speedColW = Math.Max(colMin, Math.Max(speed.DesiredSize.Width, unit.DesiredSize.Width));
            items.Add(new KeyValuePair<double, Action<double>>(speedColW, x =>
            {
                double colH = speedLine + colGap + unitLine;
                double top = cy - colH / 2;
                Place(speed, x + (speedColW - speed.DesiredSize.Width) / 2, top + speedLine / 2, speed.DesiredSize.Width, speed.DesiredSize.Height);
                Place(unit, x + (speedColW - unit.DesiredSize.Width) / 2, top + speedLine + colGap + unitLine / 2, unit.DesiredSize.Width, unit.DesiredSize.Height);
            }));
            timeColW = Math.Max(colMin, Math.Max(ap.DesiredSize.Width, time.DesiredSize.Width));
            timeColumn = x =>
            {
                double colH = apLine + colGap + timeLine;
                double top = cy - colH / 2;
                Place(ap, x + (timeColW - ap.DesiredSize.Width) / 2, top + apLine / 2, ap.DesiredSize.Width, ap.DesiredSize.Height);
                Place(time, x + (timeColW - time.DesiredSize.Width) / 2, top + apLine + colGap + timeLine / 2, time.DesiredSize.Width, time.DesiredSize.Height);
            };
            items.Add(new KeyValuePair<double, Action<double>>(timeColW, timeColumn));
            items.Add(new KeyValuePair<double, Action<double>>(signalBox, x => Place(blinkR, x + (signalBox - signalSvg) / 2, cy, signalSvg, signalSvg)));
            items.Add(new KeyValuePair<double, Action<double>>(icon, x => Place(wheel, x, cy, icon, icon)));
            double accelW = accel.BoxWidth;
            items.Add(new KeyValuePair<double, Action<double>>(accelW, x => Place(accel, x, cy, accelW, icon)));
            // justify-content: space-evenly with gap clamp(4px, 0.8%, 12px)
            double gapEven = DashArt.Clamp(4 * z, size.Width * 0.008, 12 * z);
            double sum = 0;
            foreach (KeyValuePair<double, Action<double>> it in items) sum += it.Key;
            double free = size.Width - sum - gapEven * (items.Count - 1);
            double space = free > 0 ? free / (items.Count + 1) : 0;
            double xAt = free > 0 ? space : free / 2;
            foreach (KeyValuePair<double, Action<double>> it in items)
            {
                it.Value(xAt);
                xAt += it.Key + gapEven + space;
            }
            return size;
        }
    }

    /// <summary>
    /// The Viewer's telemetry dashboard (script.js's updateVisualization and
    /// the parts around it): the floating and the compact dashboard drawn
    /// from one <see cref="DashboardView"/>, the dashboard settings, each
    /// telemetry tick, the recording time, the steering wheel's and the
    /// blinkers' animation each frame. The Viewer page (<see cref="StudioViewer"/>)
    /// owns it; the settings modal and the keys (parts 11, 12) change it
    /// through <see cref="SetEnabled"/>, <see cref="SetMetric"/>,
    /// <see cref="SetLayout"/>, <see cref="SetCompactFixed"/> and
    /// <see cref="SetAccelMode"/>.
    /// </summary>
    public sealed class StudioDashboard
    {
        readonly DashboardView view = new DashboardView();
        readonly StudioFloatingDashboard floating;
        readonly StudioCompactDashboard compact = new StudioCompactDashboard();
        readonly BlinkClock blinkL = new BlinkClock(), blinkR = new BlinkClock();
        DashboardSettings settings = new DashboardSettings();
        bool loaded;
        bool wasPlaying;
        double lastFrameMs = double.NaN;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>The profile allows a dashboard (library.capabilities.dashboard) and telemetry.</summary>
        public bool Capability = true;
        public bool TelemetryCapability = true;

        /// <summary>A setting changed (key, value) to save with setSetting.</summary>
        public Action<string, JsonValue> Save { get; set; }

        /// <summary>The compact dashboard moved between the front tile and the frame.</summary>
        public event Action PlacementChanged;

        public StudioDashboard(bool still)
        {
            floating = new StudioFloatingDashboard(still);
            floating.ToggleExtra += ToggleExtra;
            Draw();
        }

        public DashboardView View { get { return view; } }
        public DashboardSettings Settings { get { return settings; } }
        public StudioFloatingDashboard Floating { get { return floating; } }
        public StudioCompactDashboard Compact { get { return compact; } }

        /// <summary>The settings as read (GET /api/studio/settings): units, layout, accelerator mode.</summary>
        public void ApplySettings(DashboardSettings s)
        {
            bool wasFixed = settings.CompactFixed, wasCompact = settings.Compact;
            settings = s ?? new DashboardSettings();
            view.SetMetric(settings.Metric);
            compact.Fixed = settings.CompactFixed;
            if (wasFixed != settings.CompactFixed || wasCompact != settings.Compact) RaisePlacement();
            Draw();
        }

        void RaisePlacement()
        {
            Action h = PlacementChanged;
            if (h != null) h();
        }

        /// <summary>Settings > Dashboard Overlay (dashboardToggle): on or off, saved.</summary>
        public void SetEnabled(bool on)
        {
            settings.Enabled = on;
            view.ResetDirty();
            Store("dashboardEnabled", JsonValue.From(on));
            Draw();
        }

        /// <summary>Settings > Metric Units (metricToggle): KM/H or MPH at once, saved.</summary>
        public void SetMetric(bool on)
        {
            settings.Metric = on;
            view.SetMetric(on);
            Store("useMetric", JsonValue.From(on));
            Draw();
        }

        /// <summary>Settings > Dashboard Style (updateDashboardLayout): 'default' or 'compact', saved.</summary>
        public void SetLayout(string layout)
        {
            settings.Layout = string.IsNullOrEmpty(layout) ? DashboardSettings.DefaultLayout : layout;
            view.ResetDirty();
            Store("dashboardLayout", JsonValue.String(settings.Layout));
            RaisePlacement();
            Draw();
        }

        /// <summary>Settings > Fixed to Front Camera (updateCompactDashboardPositioning), saved.</summary>
        public void SetCompactFixed(bool on)
        {
            settings.CompactFixed = on;
            compact.Fixed = on;
            Store("compactDashboardFixed", JsonValue.From(on));
            RaisePlacement();
            Draw();
        }

        /// <summary>Settings > Accelerator Display (updateAccelPedMode): 'solid', 'iconbar' or 'sidebar', saved.</summary>
        public void SetAccelMode(string mode)
        {
            settings.AccelMode = string.IsNullOrEmpty(mode) ? "iconbar" : mode;
            Store("accelPedMode", JsonValue.String(settings.AccelMode));
            Draw();
        }

        void Store(string key, JsonValue value)
        {
            Action<string, JsonValue> s = Save;
            if (s != null) s(key, value);
        }

        /// <summary>#toggleExtra: the floating dashboard's details open or close.</summary>
        public void ToggleExtra()
        {
            view.ToggleExtra();
            Draw();
        }

        /// <summary>loadNativeSegment's .visible: the floating dashboard shows once a segment has loaded.</summary>
        public bool Loaded
        {
            get { return loaded; }
            set { loaded = value; Draw(); }
        }

        /// <summary>resetDashboardAndMap (a collection opens, a profile changes).</summary>
        public void ResetAll()
        {
            view.ResetAll(settings.Metric);
            Draw();
        }

        /// <summary>A telemetry tick (the record, PARKED, the reset).</summary>
        public void Apply(TelemetryStep step, bool playing)
        {
            view.Apply(step, settings, playing, TelemetryCapability);
            Draw();
        }

        /// <summary>onMasterTimeUpdate's updateRecordingTime: the playing segment's first clip and the master's time.</summary>
        public void RecordingTime(PlaybackSegment group, double videoSeconds)
        {
            if (group == null) return;
            string first = null;
            foreach (KeyValuePair<string, string> kv in group.FilesByCamera) { first = kv.Value; break; }
            if (first == null) return;
            string name = first;
            int cut = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (cut >= 0) name = name.Substring(cut + 1);
            string before = view.RecordingTime;
            view.UpdateRecordingTime(name, videoSeconds, settings.TimeFormat);
            if (before != view.RecordingTime) Draw();
        }

        /// <summary>
        /// Each frame: the steering wheel's animation (at the playback rate),
        /// stopped when the video pauses; the blinkers' cycle (held while
        /// paused, as their .paused class holds it).
        /// </summary>
        public void Frame(bool playing, double rate)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double dt = double.IsNaN(lastFrameMs) ? 0 : now - lastFrameMs;
            lastFrameMs = now;
            if (wasPlaying && !playing) view.Wheel.Stop();
            wasPlaying = playing;
            double before = view.Wheel.Shown;
            view.Wheel.Frame(rate);
            double l = blinkL.Opacity, r = blinkR.Opacity;
            blinkL.Frame(dt, view.BlinkLeftPaused, view.BlinkLeftRestarts);
            blinkR.Frame(dt, view.BlinkRightPaused, view.BlinkRightRestarts);
            if (before != view.Wheel.Shown || l != blinkL.Opacity || r != blinkR.Opacity) Draw();
        }

        /// <summary>The render test mode: the wheel where its animation ends.</summary>
        public void Settle()
        {
            view.Wheel.Settle();
            Draw();
        }

        /// <summary>The compact dashboard is shown fixed on the front camera (else over the frame, or not at all).</summary>
        public bool CompactInTile { get { return settings.CompactFixed; } }

        void Draw()
        {
            bool floatShown = loaded && !settings.FloatingHidden(Capability);
            bool compactShown = loaded && settings.CompactShown(Capability);
            floating.Visibility = floatShown ? Visibility.Visible : Visibility.Collapsed;
            compact.Visibility = compactShown ? Visibility.Visible : Visibility.Collapsed;
            if (floatShown) floating.Show(view, settings, blinkL.Opacity, blinkR.Opacity);
            if (compactShown) compact.Show(view, settings, blinkL.Opacity, blinkR.Opacity);
        }
    }
}
