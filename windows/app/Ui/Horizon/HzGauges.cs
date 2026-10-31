using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SentrySix.Core;
using SentrySix.Core.Horizon;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Keeps an element square (or at Ratio = height / width), as wide as its
    /// room up to MaxSide: CSS width 100%, max-width, aspect-ratio, margin 0 auto.
    /// </summary>
    public sealed class HzSquare : Decorator
    {
        public double MaxSide = 300;
        public double Ratio = 1;

        public HzSquare()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
        }

        protected override Size MeasureOverride(Size available)
        {
            double w = double.IsInfinity(available.Width) ? MaxSide : Math.Min(available.Width, MaxSide);
            Size s = new Size(Math.Max(0, w), Math.Max(0, w * Ratio));
            if (Child != null) Child.Measure(s);
            return s;
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (Child != null) Child.Arrange(new Rect(0, 0, final.Width, final.Height));
            return final;
        }
    }

    /// <summary>
    /// A CSS grid of equal-height rows: fixed fractions (grid-template-columns:
    /// minmax(0, 1.25fr) minmax(0, 1fr) …), a fixed number of equal columns, or
    /// repeat(auto-fit | auto-fill, minmax(Min, 1fr)); ColGap / RowGap; items
    /// stretched to their row's height (align-items: stretch) or at its top (start).
    /// </summary>
    public sealed class HzGrid : Panel
    {
        public double[] Fr;
        public int Cols;
        public double Min = 200;
        /// <summary>auto-fill keeps the empty columns; auto-fit (false) folds them away.</summary>
        public bool Fill;
        public double ColGap, RowGap;
        public bool Stretch = true;

        int cols;
        double[] widths;
        List<double> heights = new List<double>();

        List<UIElement> Shown()
        {
            List<UIElement> l = new List<UIElement>();
            foreach (UIElement c in InternalChildren) if (c.Visibility != Visibility.Collapsed) l.Add(c);
            return l;
        }

        void Place(double width, int count)
        {
            if (Fr != null)
            {
                cols = Fr.Length;
                double sum = 0;
                foreach (double f in Fr) sum += f;
                double room = Math.Max(0, width - ColGap * (cols - 1));
                widths = new double[cols];
                for (int i = 0; i < cols; i++) widths[i] = room * Fr[i] / sum;
                return;
            }
            if (Cols > 0) cols = Cols;
            else
            {
                double min = Math.Min(Min, width);
                cols = Math.Max(1, (int)Math.Floor((width + ColGap) / (min + ColGap)));
                if (!Fill) cols = Math.Max(1, Math.Min(cols, count));
            }
            double w = Math.Max(0, (width - ColGap * (cols - 1)) / cols);
            widths = new double[cols];
            for (int i = 0; i < cols; i++) widths[i] = w;
        }

        void MeasureAll(double width)
        {
            List<UIElement> shown = Shown();
            Place(width, shown.Count);
            heights = new List<double>();
            for (int i = 0; i < shown.Count; i++)
            {
                int r = i / cols, c = i % cols;
                shown[i].Measure(new Size(widths[c], double.PositiveInfinity));
                while (heights.Count <= r) heights.Add(0);
                heights[r] = Math.Max(heights[r], shown[i].DesiredSize.Height);
            }
        }

        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? 800 : available.Width;
            MeasureAll(width);
            foreach (UIElement c in InternalChildren) if (c.Visibility == Visibility.Collapsed) c.Measure(new Size(0, 0));
            double h = 0;
            for (int i = 0; i < heights.Count; i++) h += heights[i] + (i > 0 ? RowGap : 0);
            return new Size(width, h);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double used = 0;
            if (widths != null) foreach (double w in widths) used += w;
            if (widths == null || Math.Abs(final.Width - (used + ColGap * (cols - 1))) > 0.5) MeasureAll(final.Width);
            List<UIElement> shown = Shown();
            double y = 0;
            for (int r = 0; r < heights.Count; r++)
            {
                double x = 0;
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= shown.Count) break;
                    double h = Stretch ? heights[r] : shown[i].DesiredSize.Height;
                    shown[i].Arrange(new Rect(x, y, widths[c], h));
                    x += widths[c] + ColGap;
                }
                y += heights[r] + RowGap;
            }
            return final;
        }
    }

    /// <summary>
    /// hz/gauges.js spark(): Core's path (<see cref="HzSpark"/>) in a 240 x 30
    /// box stretched to the width (preserveAspectRatio none), a 1.6 px line that
    /// does not stretch; fewer than two numbers: the dashed base line.
    /// </summary>
    public sealed class HzSparkView : FrameworkElement
    {
        HzSpark spark;

        public HzSparkView()
        {
            Height = 30;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            IsHitTestVisible = false;
        }

        public void Set(HzSpark s)
        {
            spark = s;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size available)
        {
            return new Size(0, Height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (spark == null) return;
            double sx = ActualWidth / spark.W, sy = ActualHeight / spark.H;
            if (spark.Path == null)
            {
                Pen dash = new Pen(Css.Brush("rgba(255,255,255,.12)"), 1);
                dash.DashStyle = new DashStyle(new double[] { 3, 4 }, 0);
                double y = (spark.H - 1) * sy;
                dc.DrawLine(dash, new Point(0, y), new Point(ActualWidth, y));
                return;
            }
            Pen pen = new Pen(Css.Brush(spark.Tone == "org" ? "#ff8a3d" : "#3df0ff"), 1.6);
            pen.LineJoin = PenLineJoin.Miter;
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                foreach (double[] seg in spark.Segments)
                {
                    if (seg.Length < 2) continue;
                    c.BeginFigure(new Point(seg[0] * sx, seg[1] * sy), false, false);
                    for (int i = 2; i + 1 < seg.Length; i += 2) c.LineTo(new Point(seg[i] * sx, seg[i + 1] * sy), true, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    /// <summary>
    /// hz/gauges.js dial(): the 200 x 200 SVG drawn with WPF shapes on the same
    /// coordinates (the bezel, the track, the red zone, the value arc in its
    /// cyan-magenta-orange gradient, the ticks and their labels, the needle)
    /// scaled by a Viewbox, with the read-out and the label over it as CSS puts
    /// them (top 61 % and 84 %, in page pixels, not scaled).
    /// </summary>
    public sealed class HzDialView
    {
        public readonly HzSquare Root = new HzSquare();
        readonly Path val = new Path();
        readonly Canvas needle = new Canvas();
        readonly RotateTransform turn = new RotateTransform(135, 100, 100);
        readonly TextBlock read = new TextBlock();
        readonly SpacedText unit;
        readonly bool mini;
        double shown = 135;

        public HzDialView(HzDial d)
        {
            mini = d.Cls == "mini";
            Root.MaxSide = mini ? 160 : 300;
            Canvas svg = new Canvas();
            svg.Width = 200;
            svg.Height = 200;
            Ellipse bezel = new Ellipse();
            bezel.Width = 184;
            bezel.Height = 184;
            Canvas.SetLeft(bezel, 8);
            Canvas.SetTop(bezel, 8);
            bezel.Fill = Css.Brush("rgba(5,2,18,.88)");
            bezel.Stroke = Css.Brush("rgba(207,195,238,.28)");
            bezel.StrokeThickness = 2;
            svg.Children.Add(bezel);
            svg.Children.Add(Arc(HzGauge.Arc(100, 100, 86, 135, 405), "rgba(255,255,255,.1)", false));
            svg.Children.Add(Arc(HzGauge.Arc(100, 100, 86, 135 + 270 * d.Red, 405), "rgba(255,90,120,.7)", false));
            val.StrokeThickness = 8;
            val.StrokeStartLineCap = PenLineCap.Round;
            val.StrokeEndLineCap = PenLineCap.Round;
            LinearGradientBrush grad = new LinearGradientBrush();
            grad.StartPoint = new Point(0, 1);
            grad.EndPoint = new Point(1, 0);
            grad.GradientStops.Add(new GradientStop(Css.C("#3df0ff"), 0));
            grad.GradientStops.Add(new GradientStop(Css.C("#ff3fb4"), 0.6));
            grad.GradientStops.Add(new GradientStop(Css.C("#ff8a3d"), 1));
            val.Stroke = grad;
            svg.Children.Add(val);
            int n = d.Majors * 5;
            for (int i = 0; i <= n; i++)
            {
                double f = (double)i / n;
                double a = 135 + 270 * f;
                bool major = i % 5 == 0;
                bool hot = f >= d.Red;
                double[] p0 = HzGauge.P(100, 100, major ? 70 : 74, a);
                double[] p1 = HzGauge.P(100, 100, 80, a);
                Line l = new Line();
                l.X1 = Round1(p0[0]); l.Y1 = Round1(p0[1]); l.X2 = Round1(p1[0]); l.Y2 = Round1(p1[1]);
                l.Stroke = Css.Brush(hot ? "#ff5a78" : major ? "#ece6ff" : "rgba(236,230,255,.45)");
                l.StrokeThickness = major ? 2 : 1;
                svg.Children.Add(l);
                // .dial.mini .tk-lab { display: none }
                if (major && !mini)
                {
                    double[] t = HzGauge.P(100, 100, 58, a);
                    string s = d.Cls == "speedo" ? Format.JsNum(Format.Round(d.Max * f / 10)) : Format.JsNum(Format.Round(d.Max * f));
                    svg.Children.Add(Label(s, Round1(t[0]), Round1(t[1] + 4), 12, hot ? "#ffb3c2" : "#ddd5f7"));
                }
            }
            Path hand = new Path();
            hand.Data = Geometry.Parse("M100 96.5 L176 100 L100 103.5 Z");
            hand.Fill = Css.Brush("#ff8a3d");
            needle.Children.Add(hand);
            Ellipse hub = new Ellipse();
            hub.Width = 18;
            hub.Height = 18;
            Canvas.SetLeft(hub, 91);
            Canvas.SetTop(hub, 91);
            hub.Fill = Css.Brush("#1a0f3d");
            hub.Stroke = Css.Brush("#ff3fb4");
            hub.StrokeThickness = 2;
            needle.Children.Add(hub);
            needle.RenderTransform = turn;
            svg.Children.Add(needle);
            Viewbox vb = new Viewbox();
            vb.Stretch = Stretch.Uniform;
            vb.Child = svg;

            // .dial-read (top 61 %) and .dial-label (top 84 %)
            read.FontFamily = U.Consolas;
            read.FontWeight = FontWeights.ExtraBold;
            read.Foreground = Brushes.White;
            read.HorizontalAlignment = HorizontalAlignment.Center;
            read.FontSize = mini ? 24 : 42;
            read.LineHeight = read.FontSize * 1.05;
            unit = new SpacedText("", mini ? 11 : 12, FontWeights.Normal, 0.14);
            unit.SetResourceReference(SpacedText.ForegroundProperty, "dim");
            unit.HorizontalAlignment = HorizontalAlignment.Center;
            StackPanel ro = new StackPanel();
            ro.Children.Add(read);
            ro.Children.Add(unit);
            ro.HorizontalAlignment = HorizontalAlignment.Stretch;
            ro.VerticalAlignment = VerticalAlignment.Top;
            SpacedText label = new SpacedText((d.Label ?? "").ToUpperInvariant(), 12, U.W(800), 0.2);
            label.Foreground = Css.Brush("#ffd2ef");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Top;
            Grid over = new Grid();
            over.RowDefinitions.Add(new RowDefinition { Height = new GridLength(61, GridUnitType.Star) });
            over.RowDefinitions.Add(new RowDefinition { Height = new GridLength(23, GridUnitType.Star) });
            over.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16, GridUnitType.Star) });
            Grid.SetRow(ro, 1);
            Grid.SetRowSpan(ro, 2);
            Grid.SetRow(label, 2);
            over.Children.Add(ro);
            over.Children.Add(label);
            over.ClipToBounds = false;
            Grid g = new Grid();
            g.Children.Add(vb);
            g.Children.Add(over);
            Root.Child = g;
            HzKit.AutomationName(Root, d.Label);
            U.OnWindowWidth(Root, w =>
            {
                // .dial.speedo: 300 px (240 at 640 and under); the read-out clamp(28px, 3.2vw, 42px), a mini dial's 24 px
                if (!mini) Root.MaxSide = w <= 640 ? 240 : 300;
                read.FontSize = mini ? 24 : Math.Max(28, Math.Min(0.032 * w, 42));
                read.LineHeight = read.FontSize * 1.05;
            });
            Update(d, true);
        }

        static double Round1(double v)
        {
            return double.Parse(Format.ToFixed(v, 1), CultureInfo.InvariantCulture);
        }

        static Path Arc(string d, string color, bool round)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(d);
            p.Stroke = Css.Brush(color);
            p.StrokeThickness = 8;
            return p;
        }

        /// <summary>An SVG text with text-anchor middle, its baseline at y.</summary>
        internal static FrameworkElement Label(string s, double x, double y, double size, string color)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = Css.Brush(color);
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double baseline = t.FontFamily.Baseline * size;
            Canvas.SetLeft(t, x - t.DesiredSize.Width / 2);
            Canvas.SetTop(t, y - baseline);
            return t;
        }

        /// <summary>setDial(): the needle (turning 0.8 s, still in pictures and with effects off), the arc, the read-out, the hot colours.</summary>
        public void Update(HzDial d, bool still)
        {
            val.Data = Geometry.Parse(d.Val);
            if (still || HzKit.Still || Math.Abs(shown - d.Deg) < 0.01)
            {
                turn.BeginAnimation(RotateTransform.AngleProperty, null);
                turn.Angle = d.Deg;
            }
            else
            {
                DoubleAnimation a = new DoubleAnimation(shown, d.Deg, new Duration(TimeSpan.FromSeconds(0.8)));
                a.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
                turn.BeginAnimation(RotateTransform.AngleProperty, a);
            }
            shown = d.Deg;
            read.Text = d.Read;
            unit.Text = (d.Unit ?? "").ToUpperInvariant();
            // .dial.hot .dial-read b
            read.Foreground = Css.Brush(d.Hot ? "#ffd0d9" : "#ffffff");
            read.Effect = Css.Glow(14, d.Hot ? "rgba(255,90,120,.9)" : "rgba(61,240,255,.75)");
        }
    }

    /// <summary>hz/gauges.js fuel(): free space from E to F (the SVG's viewBox 0 30 200 110), its needle, the read-out under it.</summary>
    public sealed class HzFuelView
    {
        public readonly StackPanel Root = new StackPanel();
        readonly Canvas needle = new Canvas();
        readonly RotateTransform turn = new RotateTransform(210, 100, 110);
        readonly TextBlock read = new TextBlock();
        readonly TextBlock sub;
        double shown = 210;

        public HzFuelView()
        {
            Canvas svg = new Canvas();
            svg.Width = 200;
            svg.Height = 110;
            Canvas inner = new Canvas();
            inner.RenderTransform = new TranslateTransform(0, -30);
            svg.Children.Add(inner);
            inner.Children.Add(Stroke(HzGauge.Arc(100, 110, 86, 210, 330), "rgba(255,255,255,.1)"));
            inner.Children.Add(Stroke(HzGauge.Arc(100, 110, 86, 210, 222), "rgba(255,90,120,.7)"));
            for (int i = 0; i <= 4; i++)
            {
                double a = 210 + (120.0 * i) / 4;
                double[] p0 = HzGauge.P(100, 110, 70, a);
                double[] p1 = HzGauge.P(100, 110, 82, a);
                Line l = new Line();
                l.X1 = p0[0]; l.Y1 = p0[1]; l.X2 = p1[0]; l.Y2 = p1[1];
                l.Stroke = Css.Brush(i == 0 ? "#ff5a78" : "#ece6ff");
                l.StrokeThickness = 2;
                inner.Children.Add(l);
            }
            double[] e = HzGauge.P(100, 110, 56, 214);
            double[] f = HzGauge.P(100, 110, 56, 326);
            FrameworkElement el = HzDialView.Label("E", e[0], e[1] + 5, 17, "#ffb3c2");
            ((TextBlock)el).FontWeight = FontWeights.ExtraBold;
            inner.Children.Add(el);
            FrameworkElement fl = HzDialView.Label("F", f[0], f[1] + 5, 17, "#ece6ff");
            ((TextBlock)fl).FontWeight = FontWeights.ExtraBold;
            inner.Children.Add(fl);
            Path hand = new Path();
            hand.Data = Geometry.Parse("M100 107 L178 110 L100 113 Z");
            hand.Fill = Css.Brush("#3df0ff");
            needle.Children.Add(hand);
            Ellipse hub = new Ellipse();
            hub.Width = 16;
            hub.Height = 16;
            Canvas.SetLeft(hub, 92);
            Canvas.SetTop(hub, 102);
            hub.Fill = Css.Brush("#1a0f3d");
            hub.Stroke = Css.Brush("#ff3fb4");
            hub.StrokeThickness = 2;
            needle.Children.Add(hub);
            needle.RenderTransform = turn;
            inner.Children.Add(needle);
            Viewbox vb = new Viewbox();
            vb.Stretch = Stretch.Uniform;
            vb.Child = svg;
            HzSquare box = new HzSquare();
            box.MaxSide = 280;
            box.Ratio = 110.0 / 200;
            box.Child = vb;
            HzKit.AutomationName(box, HzDash.FreeSpace);
            Root.Children.Add(box);
            // .fuel-read: margin-top -4px; b 800 24px/1.1 mono, span 13px dim
            read.FontFamily = U.Consolas;
            read.FontWeight = FontWeights.ExtraBold;
            read.FontSize = 24;
            read.LineHeight = 24 * 1.1;
            read.HorizontalAlignment = HorizontalAlignment.Center;
            read.Margin = new Thickness(0, -4, 0, 0);
            sub = HzKit.Text("", 13, "dim");
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            Root.Children.Add(read);
            Root.Children.Add(sub);
            Root.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        static Path Stroke(string d, string color)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(d);
            p.Stroke = Css.Brush(color);
            p.StrokeThickness = 8;
            return p;
        }

        public void Update(HzFuel f, bool still)
        {
            if (still || HzKit.Still || Math.Abs(shown - f.Deg) < 0.01)
            {
                turn.BeginAnimation(RotateTransform.AngleProperty, null);
                turn.Angle = f.Deg;
            }
            else
            {
                DoubleAnimation a = new DoubleAnimation(shown, f.Deg, new Duration(TimeSpan.FromSeconds(0.8)));
                a.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
                turn.BeginAnimation(RotateTransform.AngleProperty, a);
            }
            shown = f.Deg;
            read.Text = f.Read;
            read.Foreground = Css.Brush(f.Low ? "#ffc2a1" : "#ffffff");
            sub.Text = f.Sub;
        }
    }

    public static partial class HzKit
    {
        /// <summary>Letter-spaced words that wrap (a WrapPanel of SpacedText words), for labels with letter-spacing that may not fit one line.</summary>
        public static WrapPanel SpacedWrap(string text, double size, FontWeight weight, double spacing, string color)
        {
            WrapPanel w = new WrapPanel();
            string[] words = (text ?? "").Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                SpacedText t = new SpacedText(words[i] + (i < words.Length - 1 ? " " : ""), size, weight, spacing);
                if (color != null && (color.StartsWith("#") || color.StartsWith("rgb"))) t.Foreground = Css.Brush(color);
                else if (color != null) t.SetResourceReference(SpacedText.ForegroundProperty, color);
                w.Children.Add(t);
            }
            return w;
        }

        /// <summary>
        /// select.in: the field's box (44 px, radius 4, 16 px) with the cyan
        /// chevron, its options opening as a menu under it; Up and Down change
        /// the choice as a select does.
        /// </summary>
        public sealed class Select
        {
            public readonly Border Root = new Border();
            readonly TextBlock label = new TextBlock();
            List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            public string Value = "";
            public event Action<string> Picked;

            public Select(string name)
            {
                InputLook k = InK();
                label.FontSize = k.FontSize;
                label.SetResourceReference(TextBlock.ForegroundProperty, "ink");
                label.VerticalAlignment = VerticalAlignment.Center;
                Path chev = new Path();
                chev.Data = Geometry.Parse("M0 0 L5 5 L10 0");
                chev.SetResourceReference(Shape.StrokeProperty, "cyan");
                chev.StrokeThickness = 2;
                chev.Width = 10;
                chev.Height = 6;
                chev.HorizontalAlignment = HorizontalAlignment.Right;
                chev.VerticalAlignment = VerticalAlignment.Center;
                chev.Margin = new Thickness(0, 0, -24, 0);
                chev.IsHitTestVisible = false;
                Grid g = new Grid();
                g.Children.Add(label);
                g.Children.Add(chev);
                Root.Child = g;
                Root.Padding = new Thickness(12, 0, 36, 0);
                Root.MinHeight = 44;
                Root.CornerRadius = new CornerRadius(4);
                Root.BorderThickness = new Thickness(1);
                Root.BorderBrush = Css.Brush("rgba(207,195,238,.38)");
                Root.Background = Css.Brush("rgba(6,3,22,.78)");
                Root.HorizontalAlignment = HorizontalAlignment.Left;
                Root.VerticalAlignment = VerticalAlignment.Center;
                Root.Focusable = true;
                Root.Cursor = System.Windows.Input.Cursors.Hand;
                AutomationName(Root, name);
                U.Click(Root, Open);
                Root.GotKeyboardFocus += delegate { Root.BorderBrush = Css.Brush("#3df0ff"); };
                Root.LostKeyboardFocus += delegate { Root.BorderBrush = Css.Brush("rgba(207,195,238,.38)"); };
                Root.PreviewKeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key != System.Windows.Input.Key.Down && e.Key != System.Windows.Input.Key.Up) return;
                    int i = Index();
                    int j = Math.Max(0, Math.Min(options.Count - 1, i + (e.Key == System.Windows.Input.Key.Down ? 1 : -1)));
                    if (j != i && options.Count > 0) Pick(options[j].Key);
                    e.Handled = true;
                };
            }

            int Index()
            {
                for (int i = 0; i < options.Count; i++) if (options[i].Key == Value) return i;
                return -1;
            }

            /// <summary>The options (value, text) and the one shown (as selected; none: the first).</summary>
            public void Set(List<KeyValuePair<string, string>> items, string selected)
            {
                options = items ?? new List<KeyValuePair<string, string>>();
                Value = selected ?? "";
                int i = Index();
                label.Text = i >= 0 ? options[i].Value : options.Count > 0 ? options[0].Value : "";
                // as wide as its longest option (width: auto)
                double w = 0;
                foreach (KeyValuePair<string, string> o in options)
                {
                    TextBlock t = new TextBlock();
                    t.Text = o.Value;
                    t.FontSize = label.FontSize;
                    t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    w = Math.Max(w, t.DesiredSize.Width);
                }
                label.MinWidth = Math.Ceiling(w);
            }

            void Pick(string v)
            {
                Value = v;
                int i = Index();
                label.Text = i >= 0 ? options[i].Value : "";
                Action<string> h = Picked;
                if (h != null) h(v);
            }

            void Open()
            {
                ContextMenu menu = new ContextMenu();
                foreach (KeyValuePair<string, string> o in options)
                {
                    MenuItem item = new MenuItem();
                    item.Header = o.Value;
                    item.IsCheckable = true;
                    item.IsChecked = o.Key == Value;
                    string key = o.Key;
                    item.Click += delegate { Pick(key); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = Root;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.MinWidth = Root.ActualWidth;
                menu.IsOpen = true;
            }
        }

        /// <summary>label.chk: a 20 px box (accent magenta when on) and its words 10 px to the right, 44 px tall.</summary>
        public sealed class Check
        {
            public readonly Flex Root;
            readonly Border box = new Border();
            readonly Path tick = new Path();
            public bool Value;
            public event Action<bool> Changed;

            public Check(string label, bool value)
            {
                box.Width = 20;
                box.Height = 20;
                box.CornerRadius = new CornerRadius(3);
                box.BorderThickness = new Thickness(1);
                tick.Data = Geometry.Parse("M4.5 10 8.5 14 15.5 6");
                tick.Stroke = Brushes.White;
                tick.StrokeThickness = 2.2;
                tick.StrokeStartLineCap = PenLineCap.Round;
                tick.StrokeEndLineCap = PenLineCap.Round;
                tick.StrokeLineJoin = PenLineJoin.Round;
                box.Child = tick;
                box.VerticalAlignment = VerticalAlignment.Center;
                TextBlock t = Text(label, 15, "ink");
                t.VerticalAlignment = VerticalAlignment.Center;
                Root = Flex.Row(10, box, t);
                Root.MinHeight = 44;
                Root.Background = Brushes.Transparent;
                Root.Cursor = System.Windows.Input.Cursors.Hand;
                Root.VerticalAlignment = VerticalAlignment.Center;
                AutomationName(Root, label);
                U.Click(Root, () => { Set(!Value); Action<bool> h = Changed; if (h != null) h(Value); });
                Set(value);
            }

            public void Set(bool on)
            {
                Value = on;
                box.Background = on ? Css.Brush("#ff3fb4") : Css.Brush("rgba(6,3,22,.78)");
                box.BorderBrush = on ? Css.Brush("#ff3fb4") : Css.Brush("rgba(207,195,238,.6)");
                tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>details.arch: the summary (cyan 600, 44 px) that opens and closes what is under it.</summary>
        public sealed class Details
        {
            public readonly StackPanel Root = new StackPanel();
            public readonly StackPanel Body = new StackPanel();
            public bool Open;
            public event Action<bool> Toggled;

            public Details(string summary, bool open)
            {
                TextBlock t = Text(summary, 15, "cyan", 600);
                t.VerticalAlignment = VerticalAlignment.Center;
                t.TextWrapping = TextWrapping.Wrap;
                // summary { display: flex }: no disclosure marker
                Flex head = Flex.Row(8, t);
                head.MinHeight = 44;
                head.Background = Brushes.Transparent;
                head.Cursor = System.Windows.Input.Cursors.Hand;
                head.Focusable = true;
                AutomationName(head, summary);
                U.Click(head, () => { Set(!Open); Action<bool> h = Toggled; if (h != null) h(Open); });
                head.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key != System.Windows.Input.Key.Enter && e.Key != System.Windows.Input.Key.Space) return;
                    e.Handled = true;
                    Set(!Open);
                    Action<bool> h = Toggled;
                    if (h != null) h(Open);
                };
                Root.Children.Add(head);
                Root.Children.Add(Body);
                Root.Margin = new Thickness(0, 10, 0, 0);
                Set(open);
            }

            public void Set(bool open)
            {
                Open = open;
                Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
