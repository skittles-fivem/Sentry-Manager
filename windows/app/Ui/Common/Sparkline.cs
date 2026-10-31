using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui
{
    /// <summary>
    /// web/app.js spark() drawn: Core's <see cref="Spark"/> segments in a
    /// 220 x 40 box stretched to the width (preserveAspectRatio none), a
    /// filled area at 8 % and a 2 px line that does not stretch
    /// (vector-effect: non-scaling-stroke). Colours are theme keys or CSS colours.
    /// </summary>
    public sealed class Sparkline : FrameworkElement
    {
        List<SparkSegment> segs = new List<SparkSegment>();
        readonly Dictionary<string, Brush> brushes = new Dictionary<string, Brush>();

        public Sparkline()
        {
            Height = 40;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            SnapsToDevicePixels = false;
        }

        /// <summary>Theme key or CSS colour for each series colour name the segments use (e.g. "var(--accent)" to "accent").</summary>
        public Func<string, Brush> Paint;

        public void Set(List<SparkSegment> segments)
        {
            segs = segments ?? new List<SparkSegment>();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size available)
        {
            return new Size(0, Height);
        }

        Brush BrushFor(string color)
        {
            Brush b;
            if (color != null && brushes.TryGetValue(color, out b)) return b;
            b = Paint != null ? Paint(color) : null;
            if (b == null)
            {
                string key = color;
                if (key != null && key.StartsWith("var(--", StringComparison.Ordinal)) key = key.Substring(6).TrimEnd(')');
                object r = key != null ? TryFindResource(key) : null;
                b = r as Brush;
                if (b == null)
                {
                    try { b = new SolidColorBrush(Css.C(color)); } catch (Exception) { b = Brushes.Gray; }
                }
            }
            if (color != null) brushes[color] = b;
            return b;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double sx = w / Spark.W, sy = h / Spark.H;
            foreach (SparkSegment s in segs)
            {
                if (s.Points.Count < 2) continue;
                Brush b = BrushFor(s.Color);
                StreamGeometry area = new StreamGeometry();
                using (StreamGeometryContext g = area.Open())
                {
                    g.BeginFigure(new Point(s.Points[0][0] * sx, h), true, true);
                    foreach (double[] p in s.Points) g.LineTo(new Point(p[0] * sx, p[1] * sy), true, false);
                    g.LineTo(new Point(s.Points[s.Points.Count - 1][0] * sx, h), true, false);
                }
                area.Freeze();
                dc.PushOpacity(0.08);
                dc.DrawGeometry(b, null, area);
                dc.Pop();
                StreamGeometry line = new StreamGeometry();
                using (StreamGeometryContext g = line.Open())
                {
                    g.BeginFigure(new Point(s.Points[0][0] * sx, s.Points[0][1] * sy), false, false);
                    for (int i = 1; i < s.Points.Count; i++) g.LineTo(new Point(s.Points[i][0] * sx, s.Points[i][1] * sy), true, true);
                }
                line.Freeze();
                Pen pen = new Pen(b, 2);
                pen.LineJoin = PenLineJoin.Miter;
                pen.Freeze();
                dc.DrawGeometry(null, pen, line);
            }
        }
    }

    /// <summary>.nd-bar: 6 px, round, a faint track; the fill in accent, warn when hot, danger when bad.</summary>
    public static class Bar
    {
        public static FrameworkElement Make(double pct, bool hot, bool bad)
        {
            Grid g = new Grid();
            g.Height = 6;
            Border track = new RoundBorder();
            track.CornerRadius = new CornerRadius(99);
            track.Background = new SolidColorBrush(Css.C("rgba(255,255,255,0.07)"));
            g.Children.Add(track);
            Border fill = new RoundBorder();
            fill.CornerRadius = new CornerRadius(99);
            fill.SetResourceReference(Border.BackgroundProperty, bad ? "danger" : hot ? "warn" : "accent");
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(fill);
            double p = Format.ClampPct(pct);
            SizeChangedEventHandler fit = delegate { fill.Width = g.ActualWidth * p / 100; };
            g.SizeChanged += fit;
            return g;
        }
    }
}
