using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;
using SentrySix.Core.Flux;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// The Overview's own pieces of Flux's look (flux.css: .kpis, .cgrid,
    /// .ctile, .tchart with charts.js's canvas, .heat, .cores, .meter, .kv,
    /// .dtable in .tscroll, .ll, select.input, .check, details), each named
    /// for the page so the other pages' additions do not clash.
    /// </summary>
    public static partial class FxKit
    {
        // ---------------------------------------------------------------- a CSS grid

        /// <summary>The cell a child of an <see cref="OverviewGrid"/> spans: the whole row (grid-column: 1 / -1).</summary>
        public static readonly DependencyProperty OverviewWideProperty = DependencyProperty.RegisterAttached("OverviewWide", typeof(bool), typeof(FxKit), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
        public static bool GetOverviewWide(UIElement e) { return (bool)e.GetValue(OverviewWideProperty); }
        public static void SetOverviewWide(UIElement e, bool v) { e.SetValue(OverviewWideProperty, v); }

        /// <summary>
        /// display: grid with repeat(auto-fill | auto-fit, minmax(min(100%, Min), 1fr))
        /// or a fixed number of columns, gaps, items stretched to their row,
        /// wide items on a row of their own (the auto-placement of a grid
        /// without dense packing).
        /// </summary>
        public sealed class OverviewGrid : Panel
        {
            public double Min = 200;
            public int Cols;
            public bool Fill;
            public double Gap, RowGap;

            int cols;
            double colW;
            readonly List<int[]> spots = new List<int[]>();
            readonly List<double> heights = new List<double>();

            List<UIElement> Shown()
            {
                List<UIElement> l = new List<UIElement>();
                foreach (UIElement c in InternalChildren) if (c.Visibility != Visibility.Collapsed) l.Add(c);
                return l;
            }

            void Lay(double width)
            {
                List<UIElement> shown = Shown();
                if (Cols > 0) cols = Cols;
                else
                {
                    double min = Math.Min(Min, width);
                    cols = Math.Max(1, (int)Math.Floor((width + Gap) / (min + Gap)));
                    if (!Fill) cols = Math.Max(1, Math.Min(cols, shown.Count));
                }
                colW = Math.Max(0, (width - Gap * (cols - 1)) / cols);
                spots.Clear();
                heights.Clear();
                int row = 0, col = 0;
                foreach (UIElement e in shown)
                {
                    bool wide = GetOverviewWide(e);
                    if (wide && col > 0) { row++; col = 0; }
                    int span = wide ? cols : 1;
                    spots.Add(new[] { row, col, span });
                    double w = colW * span + Gap * (span - 1);
                    e.Measure(new Size(w, double.PositiveInfinity));
                    while (heights.Count <= row) heights.Add(0);
                    heights[row] = Math.Max(heights[row], e.DesiredSize.Height);
                    col += span;
                    if (col >= cols) { row++; col = 0; }
                }
            }

            protected override Size MeasureOverride(Size available)
            {
                double width = double.IsInfinity(available.Width) ? 800 : available.Width;
                Lay(width);
                foreach (UIElement c in InternalChildren) if (c.Visibility == Visibility.Collapsed) c.Measure(new Size(0, 0));
                double h = 0;
                for (int i = 0; i < heights.Count; i++) h += heights[i] + (i > 0 ? RowGap : 0);
                return new Size(width, h);
            }

            protected override Size ArrangeOverride(Size final)
            {
                if (Math.Abs(colW * cols + Gap * (cols - 1) - final.Width) > 0.5) Lay(final.Width);
                List<UIElement> shown = Shown();
                double[] top = new double[heights.Count];
                for (int r = 1; r < heights.Count; r++) top[r] = top[r - 1] + heights[r - 1] + RowGap;
                for (int i = 0; i < shown.Count && i < spots.Count; i++)
                {
                    int[] s = spots[i];
                    double x = s[1] * (colW + Gap);
                    double w = colW * s[2] + Gap * (s[2] - 1);
                    shown[i].Arrange(new Rect(x, top[s[0]], w, heights[s[0]]));
                }
                return final;
            }
        }

        // ---------------------------------------------------------------- KPIs, meters, key/value lists

        /// <summary>.kpi: the label (mono 10px 600, spaced, upper case, faint), the value (mono 20px 600; 17 at 860 px), the line under it, the coloured top edge; a link lifts on hover.</summary>
        public static Border OverviewKpi(FxOverviewKpi k, Action go)
        {
            SpacedText label = U.Spaced(k.Label.ToUpperInvariant(), 10, U.W(600), 0.12, "faint");
            label.FontFamily = Mono;
            TextBlock value = Text(k.Value, 20, "text", 600);
            value.FontFamily = Mono;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            TextBlock sub = Text(k.Sub, 11.5, "muted");
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            U.OnWindowWidth(value, w => value.FontSize = w <= 860 ? 17 : 20);
            StackPanel s = new StackPanel();
            s.Children.Add(label);
            value.Margin = new Thickness(0, 3, 0, 3);
            s.Children.Add(value);
            s.Children.Add(sub);
            Border edge = new Border();
            edge.Height = 2;
            edge.VerticalAlignment = VerticalAlignment.Top;
            edge.Opacity = 0.7;
            edge.IsHitTestVisible = false;
            switch (k.Tone)
            {
                case "warn": edge.SetResourceReference(Border.BackgroundProperty, "amber"); edge.Opacity = 1; break;
                case "bad": edge.SetResourceReference(Border.BackgroundProperty, "red"); edge.Opacity = 1; break;
                case "hot": edge.Background = Css.Linear(90, "#44e1f9", 0, "#af87ff", 1); edge.Opacity = 1; break;
                default: edge.Background = Css.Linear(90, "#af87ff", 0, "#00af87ff", 1); break;
            }
            Border inner = new Border();
            inner.Child = s;
            inner.Padding = Css.T(10, 12);
            Grid g = new Grid();
            g.Children.Add(inner);
            g.Children.Add(edge);
            Border b = new RoundBorder();
            b.Child = g;
            b.CornerRadius = new CornerRadius(10);
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Border.BackgroundProperty, "panel");
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            b.ClipToBounds = true;
            b.MinWidth = 0;
            System.Windows.Automation.AutomationProperties.SetName(b, k.Label + " " + k.Value + " " + k.Sub);
            if (k.Href != null && go != null)
            {
                b.Cursor = Cursors.Hand;
                TranslateTransform lift = new TranslateTransform();
                b.RenderTransform = lift;
                U.Hover(b, on =>
                {
                    if (on) b.SetResourceReference(Border.BorderBrushProperty, "violet");
                    else b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                    lift.Y = on ? -1 : 0;
                });
                U.Click(b, go);
            }
            return b;
        }

        static readonly Dictionary<string, Brush> MeterFills = new Dictionary<string, Brush>();

        static Brush MeterFill(string tone)
        {
            Brush b;
            if (MeterFills.TryGetValue(tone ?? "", out b)) return b;
            switch (tone ?? "")
            {
                case "cyan": b = Css.Linear(90, "#00b0c5", 0, "#44e1f9", 1); break;
                case "green": b = Css.Brush("#5ee295"); break;
                case "amber": b = Css.Brush("#ffc249"); break;
                case "red": b = Css.Brush("#ff696d"); break;
                case "pink": b = Css.Brush("#fe7fc0"); break;
                default: b = Css.Linear(90, "#915dff", 0, "#af87ff", 1); break;
            }
            if (b.CanFreeze) b.Freeze();
            MeterFills[tone ?? ""] = b;
            return b;
        }

        /// <summary>ui.js meter(): a 6px bar (radius 3, bg-2) filled to its width in its tone.</summary>
        public static FrameworkElement OverviewMeter(FxOverviewMeter m)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, m.Fill), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - m.Fill), GridUnitType.Star) });
            Border fill = new Border();
            fill.CornerRadius = new CornerRadius(3);
            fill.Background = MeterFill(m.Tone);
            g.Children.Add(fill);
            Border b = new RoundBorder();
            b.Child = g;
            b.Height = 6;
            b.MinWidth = 40;
            b.CornerRadius = new CornerRadius(3);
            b.SetResourceReference(Border.BackgroundProperty, "bg-2");
            b.ClipToBounds = true;
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        /// <summary>
        /// dl.kv (.tight: 3px 12px gaps, 12px, the keys mono 11px faint): the
        /// key and its value (mono, in its tone) per row; the "this server"
        /// line across with a line over it; a note across.
        /// </summary>
        public static Grid OverviewKv(IList<FxOverviewKv> rows, bool tight)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Margin = Css.T(8, 0);
            double rowGap = tight ? 3 : 6, colGap = tight ? 12 : 14, size = tight ? 12 : 13;
            int r = 0;
            foreach (FxOverviewKv kv in rows)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Thickness top = new Thickness(0, r > 0 ? rowGap : 0, 0, 0);
                if (kv.Kind == "sep")
                {
                    TextBlock t = Text(kv.Key.ToUpperInvariant(), 11, tight ? "faint" : "muted");
                    if (tight) t.FontFamily = Mono;
                    Border line = new Border();
                    line.Child = t;
                    line.Padding = new Thickness(0, 6, 0, 0);
                    line.BorderThickness = new Thickness(0, 1, 0, 0);
                    line.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                    line.Margin = top;
                    System.Windows.Controls.Grid.SetRow(line, r);
                    System.Windows.Controls.Grid.SetColumnSpan(line, 2);
                    g.Children.Add(line);
                    r++;
                    continue;
                }
                if (kv.Kind == "note")
                {
                    TextBlock n = Text(kv.Value, size, "muted");
                    n.TextWrapping = TextWrapping.Wrap;
                    n.Margin = top;
                    System.Windows.Controls.Grid.SetRow(n, r);
                    System.Windows.Controls.Grid.SetColumnSpan(n, 2);
                    g.Children.Add(n);
                    r++;
                    continue;
                }
                TextBlock k = Text(kv.Key, tight ? 11 : size, tight ? "faint" : "muted");
                if (tight) k.FontFamily = Mono;
                k.Margin = new Thickness(0, top.Top, colGap, 0);
                k.VerticalAlignment = VerticalAlignment.Top;
                TextBlock v = Text(Format.Breakable(kv.Value), size, kv.Tone == "bad-text" ? "red" : kv.Tone == "faint" ? "faint" : "text");
                v.FontFamily = Mono;
                v.TextWrapping = TextWrapping.Wrap;
                v.Margin = top;
                v.LineHeight = size * 1.45;
                k.LineHeight = size * 1.45;
                System.Windows.Controls.Grid.SetRow(k, r);
                System.Windows.Controls.Grid.SetRow(v, r);
                System.Windows.Controls.Grid.SetColumn(v, 1);
                g.Children.Add(k);
                g.Children.Add(v);
                r++;
            }
            return g;
        }

        // ---------------------------------------------------------------- charts.js on WPF

        static readonly Typeface ChartFace = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        static FormattedText ChartText(string s, Brush b, double size)
        {
            return new FormattedText(s ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ChartFace, size, b, 1.0);
        }

        /// <summary>A label's width in the charts' 10px monospace (ctx.measureText).</summary>
        public static double OverviewMeasure(string s) { return ChartText(s, Brushes.White, 10).WidthIncludingTrailingWhitespace; }

        static Brush PaintOf(string css, double y0, double y1, FxOverviewOp op)
        {
            if (css == null) return Brushes.Transparent;
            if (css.StartsWith("grad ") && op != null && op.GradFrom != null)
            {
                LinearGradientBrush lg = new LinearGradientBrush(Css.C(op.GradFrom), Css.C(op.GradTo), new Point(0, op.GradY0), new Point(0, op.GradY1));
                lg.MappingMode = BrushMappingMode.Absolute;
                lg.Freeze();
                return lg;
            }
            if (css.StartsWith("hsla")) return Css.Brush("#000");
            return Css.Brush(css);
        }

        /// <summary>hsla(h, s%, l%, a) as a colour.</summary>
        public static Color OverviewHsla(double h, double s, double l, double a)
        {
            h = ((h % 360) + 360) % 360 / 360;
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            Func<double, double> f = t =>
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            };
            Func<double, byte> c = v => (byte)Math.Max(0, Math.Min(255, Math.Round(v * 255)));
            return Color.FromArgb(c(a), c(f(h + 1.0 / 3)), c(f(h)), c(f(h - 1.0 / 3)));
        }

        /// <summary>Replays a chart's canvas calls (FxOverviewChart) with WPF's pens and brushes, then the read-out.</summary>
        static void OverviewReplay(DrawingContext dc, FxOverviewDrawn d, double w)
        {
            StreamGeometry geo = null;
            StreamGeometryContext ctx = null;
            bool open = false;
            List<Geometry> extra = new List<Geometry>();
            Action finish = () =>
            {
                if (ctx != null) { ctx.Close(); ctx = null; }
            };
            Func<Geometry> current = () =>
            {
                finish();
                if (geo == null && extra.Count == 0) return null;
                GeometryGroup gg = new GeometryGroup();
                if (geo != null) gg.Children.Add(geo);
                foreach (Geometry x in extra) gg.Children.Add(x);
                return gg;
            };
            foreach (FxOverviewOp op in d.Ops)
            {
                switch (op.Kind)
                {
                    case "begin":
                        finish();
                        geo = new StreamGeometry();
                        ctx = geo.Open();
                        open = false;
                        extra = new List<Geometry>();
                        break;
                    case "M":
                        if (ctx == null) { geo = new StreamGeometry(); ctx = geo.Open(); }
                        ctx.BeginFigure(new Point(op.X, op.Y), true, false);
                        open = true;
                        break;
                    case "L":
                        if (ctx == null) { geo = new StreamGeometry(); ctx = geo.Open(); }
                        if (!open) { ctx.BeginFigure(new Point(op.X, op.Y), true, false); open = true; }
                        else ctx.LineTo(new Point(op.X, op.Y), true, true);
                        break;
                    case "arc":
                        extra.Add(new EllipseGeometry(new Point(op.X, op.Y), op.R, op.R));
                        break;
                    case "close":
                        break;
                    case "stroke":
                        {
                            Geometry g = current();
                            if (g == null) break;
                            Pen pen = new Pen(PaintOf(op.Paint, 0, 0, null), op.LineWidth);
                            if (op.Dash != null && op.Dash.Length > 0)
                            {
                                double[] ds = new double[op.Dash.Length];
                                for (int i = 0; i < ds.Length; i++) ds[i] = op.Dash[i] / Math.Max(0.1, op.LineWidth);
                                pen.DashStyle = new DashStyle(ds, 0);
                            }
                            pen.LineJoin = PenLineJoin.Miter;
                            dc.DrawGeometry(null, pen, g);
                            break;
                        }
                    case "fill":
                        {
                            Geometry g = current();
                            if (g == null) break;
                            dc.DrawGeometry(PaintOf(op.Paint, op.GradY0, op.GradY1, op), null, g);
                            break;
                        }
                    case "text":
                        {
                            FormattedText ft = ChartText(op.Text, PaintOf(op.Paint, 0, 0, null), 10);
                            double x = op.X, y = op.Y;
                            if (op.Align == "right") x -= ft.WidthIncludingTrailingWhitespace;
                            else if (op.Align == "center") x -= ft.WidthIncludingTrailingWhitespace / 2;
                            if (op.Baseline == "middle") y -= ft.Height / 2;
                            else y -= ft.Baseline;
                            dc.DrawText(ft, new Point(x, y));
                            break;
                        }
                    case "rect":
                        dc.DrawRectangle(new SolidColorBrush(OverviewHsla(op.Hue, 0.85, op.Light / 100, op.Alpha)), null, new Rect(op.X, op.Y, Math.Max(0, op.W), Math.Max(0, op.H)));
                        break;
                    case "srect":
                        dc.DrawRectangle(null, new Pen(PaintOf(op.Paint, 0, 0, null), 1), new Rect(op.X, op.Y, Math.Max(0, op.W), Math.Max(0, op.H)));
                        break;
                }
            }
            finish();
            if (d.Tip == null) return;
            // .tc-tip: 6px 8px, radius 6, the line colour, 11px/1.45 mono, min-width 120
            Brush faint = Css.Brush("#777a87"), muted = Css.Brush("#9a9eab"), text = Css.Brush("#edeef4");
            double lh = 11 * 1.45;
            FormattedText time = ChartText(d.TipTime, faint, 11);
            double inner = Math.Max(120 - 16, time.WidthIncludingTrailingWhitespace);
            List<FormattedText[]> rows = new List<FormattedText[]>();
            for (int i = 0; i < d.Tip.Count; i++)
            {
                FormattedText l = ChartText(d.Tip[i][0], muted, 11), v = ChartText(d.Tip[i][1], text, 11);
                v.SetFontWeight(FontWeights.Bold);
                rows.Add(new[] { l, v });
                double rw = (i < d.TipColors.Count ? 14 : 0) + l.WidthIncludingTrailingWhitespace + 6 + v.WidthIncludingTrailingWhitespace;
                inner = Math.Max(inner, rw);
            }
            double tw = inner + 16 + 2;
            double th = lh + 2 + rows.Count * lh + 12 + 2;
            double left = d.TipLeft;
            // (the web places it knowing its width: the left side once it would pass the right edge)
            if (left + tw > w && left > tw) left = Math.Max(0, left - tw - 24);
            Rect box = new Rect(left, 6, tw, th);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(240, 10, 11, 16)), new Pen(Css.Brush("#2d303b"), 1), box, 6, 6);
            double y0 = box.Y + 7;
            dc.DrawText(time, new Point(box.X + 9, y0 + (lh - time.Height) / 2));
            y0 += lh + 2;
            for (int i = 0; i < rows.Count; i++)
            {
                double x = box.X + 9;
                if (i < d.TipColors.Count)
                {
                    dc.DrawRoundedRectangle(Css.Brush(d.TipColors[i]), null, new Rect(x, y0 + (lh - 8) / 2, 8, 8), 2, 2);
                    x += 14;
                }
                dc.DrawText(rows[i][0], new Point(x, y0 + (lh - rows[i][0].Height) / 2));
                dc.DrawText(rows[i][1], new Point(box.Right - 9 - rows[i][1].WidthIncludingTrailingWhitespace, y0 + (lh - rows[i][1].Height) / 2));
                y0 += lh;
            }
        }

        /// <summary>
        /// .tchart: charts.js TimeChart drawn natively (FxOverviewChart.Draw:
        /// the same scale, grid, labels, gaps and fills), its height fixed, as
        /// wide as its tile; the pointer shows the dashed line, the dots and
        /// the read-out.
        /// </summary>
        public sealed class OverviewChartView : FrameworkElement
        {
            readonly FxOverviewChartOpts opts;
            readonly HashSet<string> hidden;
            FxOverviewChartSet set;
            double[] hover;
            public Func<double, bool, string> Tod = (ms, secs) => "";

            public OverviewChartView(FxOverviewChartOpts o, HashSet<string> hiddenKeys)
            {
                opts = o;
                hidden = hiddenKeys;
                Height = o.Height;
                ClipToBounds = false;
                Focusable = false;
                MouseMove += delegate(object s, MouseEventArgs e) { Point p = e.GetPosition(this); hover = new[] { p.X, p.Y }; InvalidateVisual(); };
                MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Point p = e.GetPosition(this); hover = new[] { p.X, p.Y }; InvalidateVisual(); };
                MouseLeave += delegate { hover = null; InvalidateVisual(); };
                Panel.SetZIndex(this, 1);
            }

            public void Set(FxOverviewChartSet s)
            {
                set = s;
                InvalidateVisual();
            }

            protected override Size MeasureOverride(Size available) { return new Size(0, opts.Height); }

            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
                if (set == null) return;
                FxOverviewDrawn d = FxOverviewChart.Draw(opts, ActualWidth, set.Times, set.Data, hidden, hover, OverviewMeasure, Tod, 120);
                OverviewReplay(dc, d, ActualWidth);
            }
        }

        /// <summary>.heat: charts.js HeatStrip drawn natively (a cell per core and sample, 64px tall), the hovered cell outlined with its read-out.</summary>
        public sealed class OverviewHeatView : FrameworkElement
        {
            FxOverviewChartSet set;
            double[] hover;
            public Func<double, bool, string> Tod = (ms, secs) => "";

            public OverviewHeatView()
            {
                Height = 64;
                MouseMove += delegate(object s, MouseEventArgs e) { Point p = e.GetPosition(this); hover = new[] { p.X, p.Y }; InvalidateVisual(); };
                MouseLeave += delegate { hover = null; InvalidateVisual(); };
                Panel.SetZIndex(this, 1);
            }

            public void Set(FxOverviewChartSet s)
            {
                set = s;
                InvalidateVisual();
            }

            protected override Size MeasureOverride(Size available) { return new Size(0, 64); }

            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
                if (set == null || set.Rows == null) return;
                FxOverviewDrawn d = FxOverviewChart.Heat(ActualWidth, 64, set.Times, set.Rows, hover, Tod, 120);
                OverviewReplay(dc, d, ActualWidth);
            }
        }

        /// <summary>.tc-legend: the series' keys (11px mono, a 9px swatch); a click hides the series (crossed out, faded) or shows it again.</summary>
        public static FrameworkElement OverviewLegend(FxOverviewChartOpts o, ICollection<string> hidden, Action<string> toggle)
        {
            WrapPanel w = new WrapPanel();
            w.Margin = new Thickness(0, 4, 0, 0);
            if (!o.HasLegend) return w;
            foreach (FxOverviewSeries s in o.Series)
            {
                string key = s.Key;
                bool off = hidden.Contains(key);
                Border sw = new Border();
                sw.Width = 9;
                sw.Height = 9;
                sw.CornerRadius = new CornerRadius(2);
                sw.Background = Css.Brush(s.Color);
                sw.VerticalAlignment = VerticalAlignment.Center;
                TextBlock t = Text(s.Label, 11, "text-2");
                t.FontFamily = Mono;
                if (off) t.TextDecorations = TextDecorations.Strikethrough;
                Flex row = Flex.Row(5, sw, t);
                Border b = new RoundBorder();
                b.Child = row;
                b.Padding = Css.T(2, 6);
                b.CornerRadius = new CornerRadius(4);
                b.Margin = new Thickness(0, 0, 4, 0);
                b.Background = Brushes.Transparent;
                b.Cursor = Cursors.Hand;
                b.Opacity = off ? 0.4 : 1;
                b.Focusable = true;
                System.Windows.Automation.AutomationProperties.SetName(b, s.Label);
                System.Windows.Automation.AutomationProperties.SetItemStatus(b, off ? "off" : "on");
                U.Click(b, () => toggle(key));
                b.KeyDown += delegate(object x, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; toggle(key); } };
                w.Children.Add(b);
            }
            return w;
        }

        /// <summary>coreBars(): a bar per core (up to 32 a row) in a 28px strip, gap 2, their heights and hues the web's.</summary>
        public static FrameworkElement OverviewCores(IList<FxOverviewCoreBar> bars, int columns)
        {
            Grid g = new Grid();
            g.Height = 28;
            g.Margin = Css.T(6, 0);
            int cols = Math.Max(1, columns);
            int rows = (bars.Count + cols - 1) / cols;
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
            double rowH = (28 - 2 * (rows - 1)) / Math.Max(1, rows);
            for (int i = 0; i < bars.Count; i++)
            {
                FxOverviewCoreBar b = bars[i];
                Border bar = new Border();
                bar.CornerRadius = new CornerRadius(2, 2, 0, 0);
                double v = double.IsNaN(b.V) ? 0 : b.V;
                bar.Height = Math.Max(0, rowH * v / 100);
                bar.VerticalAlignment = VerticalAlignment.Bottom;
                double hue = double.IsNaN(b.Hue) ? 0 : b.Hue;
                bar.Background = new SolidColorBrush(OverviewHsla(hue, 0.85, 0.62, 1));
                bar.ToolTip = b.Title;
                System.Windows.Controls.Grid.SetColumn(bar, (i % cols) * 2);
                System.Windows.Controls.Grid.SetRow(bar, (i / cols) * 2);
                g.Children.Add(bar);
            }
            return g;
        }

        // ---------------------------------------------------------------- the data tables (auto layout, in a sideways scroller)

        /// <summary>
        /// table.dtable in .tscroll (not .stack: it scrolls sideways on a
        /// phone): the columns laid out as a table's auto layout does (each as
        /// wide as its content, the room left shared by width; when short, the
        /// wrappable cells give way down to their longest word).
        /// </summary>
        public sealed class OverviewTable : Panel
        {
            public int Columns;
            /// <summary>The scroller's width (it measures its content without one).</summary>
            public double Room;
            double[] widths = new double[0];
            readonly List<double> heights = new List<double>();

            static double MinOf(UIElement e)
            {
                e.Measure(new Size(0, double.PositiveInfinity));
                return e.DesiredSize.Width;
            }

            protected override Size MeasureOverride(Size available)
            {
                int n = InternalChildren.Count;
                int rows = Columns > 0 ? (n + Columns - 1) / Columns : 0;
                double[] min = new double[Columns], max = new double[Columns];
                for (int i = 0; i < n; i++)
                {
                    UIElement e = InternalChildren[i];
                    int c = i % Columns;
                    e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    max[c] = Math.Max(max[c], e.DesiredSize.Width);
                    min[c] = Math.Max(min[c], MinOf(e));
                }
                double room = double.IsInfinity(available.Width) ? Room : available.Width;
                double sumMax = 0, sumMin = 0;
                for (int c = 0; c < Columns; c++) { sumMax += max[c]; sumMin += min[c]; }
                widths = new double[Columns];
                for (int c = 0; c < Columns; c++)
                {
                    if (sumMax <= room) widths[c] = max[c] + (room - sumMax) * (sumMax > 0 ? max[c] / sumMax : 1.0 / Columns);
                    else if (sumMin <= room) widths[c] = min[c] + (room - sumMin) * (sumMax - sumMin > 0 ? (max[c] - min[c]) / (sumMax - sumMin) : 0);
                    else widths[c] = min[c];
                }
                heights.Clear();
                for (int r = 0; r < rows; r++)
                {
                    double h = 0;
                    for (int c = 0; c < Columns; c++)
                    {
                        int i = r * Columns + c;
                        if (i >= n) break;
                        InternalChildren[i].Measure(new Size(widths[c], double.PositiveInfinity));
                        h = Math.Max(h, InternalChildren[i].DesiredSize.Height);
                    }
                    heights.Add(h);
                }
                double tw = 0, th = 0;
                foreach (double x in widths) tw += x;
                foreach (double x in heights) th += x;
                return new Size(tw, th);
            }

            protected override Size ArrangeOverride(Size final)
            {
                int n = InternalChildren.Count;
                double y = 0;
                for (int r = 0; r < heights.Count; r++)
                {
                    double x = 0;
                    for (int c = 0; c < Columns; c++)
                    {
                        int i = r * Columns + c;
                        if (i >= n) break;
                        InternalChildren[i].Arrange(new Rect(x, y, widths[c], heights[r]));
                        x += widths[c];
                    }
                    y += heights[r];
                }
                return final;
            }
        }

        /// <summary>A .dtable: the head (mono 10.5px 600, spaced, upper case, faint; a line under it) and the rows (12.5px, 6px 8px, a soft line under each).</summary>
        public static FrameworkElement OverviewTableOf(string[] heads, bool[] right, IList<FxOverviewCell[]> rows)
        {
            OverviewTable t = new OverviewTable();
            t.Columns = heads.Length;
            for (int c = 0; c < heads.Length; c++)
            {
                SpacedText h = U.Spaced(heads[c].ToUpperInvariant(), 10.5, U.W(600), 0.08, "faint");
                h.FontFamily = Mono;
                h.HorizontalAlignment = right[c] ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                Border th = new Border();
                th.Child = h;
                th.Padding = new Thickness(8);
                th.BorderThickness = new Thickness(0, 0, 0, 1);
                th.SetResourceReference(Border.BorderBrushProperty, "line");
                th.SetResourceReference(Border.BackgroundProperty, "panel");
                t.Children.Add(th);
            }
            foreach (FxOverviewCell[] r in rows)
            {
                for (int c = 0; c < heads.Length; c++)
                {
                    FxOverviewCell cell = c < r.Length ? r[c] : new FxOverviewCell("", "");
                    string cls = " " + (cell.Cls ?? "") + " ";
                    bool mono = cls.Contains(" mono ") || cls.Contains(" num ");
                    // (.mono is 0.92em; .num only takes the monospace face)
                    TextBlock tx = Text(cell.Text, cls.Contains(" mono ") ? 12.5 * 0.92 : 12.5, cls.Contains(" faint ") ? "faint" : "text");
                    if (mono) tx.FontFamily = Mono;
                    bool nowrap = cls.Contains(" num ") || cls.Contains(" r ");
                    tx.TextWrapping = nowrap ? TextWrapping.NoWrap : TextWrapping.Wrap;
                    FrameworkElement content = tx;
                    if (cell.Under != null)
                    {
                        StackPanel s = new StackPanel();
                        s.Children.Add(tx);
                        TextBlock u = Text(cell.Under, 12.5, "faint");
                        u.FontFamily = Mono;
                        u.TextWrapping = TextWrapping.Wrap;
                        s.Children.Add(u);
                        content = s;
                    }
                    content.HorizontalAlignment = right[c] ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                    content.VerticalAlignment = VerticalAlignment.Center;
                    Border td = new Border();
                    td.Child = content;
                    td.Padding = Css.T(6, 8);
                    td.BorderThickness = new Thickness(0, 0, 0, 1);
                    td.SetResourceReference(Border.BorderBrushProperty, "line-soft");
                    td.Background = Brushes.Transparent;
                    t.Children.Add(td);
                }
            }
            ScrollViewer sv = new ScrollViewer();
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Focusable = false;
            sv.Content = t;
            sv.SizeChanged += delegate { if (Math.Abs(t.Room - sv.ActualWidth) > 0.5) { t.Room = sv.ActualWidth; t.InvalidateMeasure(); } };
            sv.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                // the page scrolls on, as a browser's .tscroll lets it
                if (e.Handled) return;
                e.Handled = true;
                MouseWheelEventArgs up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
                up.RoutedEvent = UIElement.MouseWheelEvent;
                up.Source = s;
                UIElement parent = VisualTreeHelper.GetParent((DependencyObject)s) as UIElement;
                if (parent != null) parent.RaiseEvent(up);
            };
            return sv;
        }

        // ---------------------------------------------------------------- the log tail's controls

        /// <summary>
        /// select.input (width auto): the box (34px, 44 on a phone; radius 6,
        /// bg-2, the line) with the chosen option and a chevron; its options
        /// open as a menu under it; Up and Down change the choice.
        /// </summary>
        public sealed class OverviewSelect
        {
            public readonly Border Root = new RoundBorder();
            readonly TextBlock label = new TextBlock();
            List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            public string Value = "";
            public event Action<string> Picked;

            public OverviewSelect(string name)
            {
                label.FontSize = 13.5;
                label.SetResourceReference(TextBlock.ForegroundProperty, "text");
                label.VerticalAlignment = VerticalAlignment.Center;
                Path chev = new Path();
                chev.Data = Geometry.Parse("M0 0 L4 4 L8 0");
                chev.SetResourceReference(Shape.StrokeProperty, "text-2");
                chev.StrokeThickness = 1.5;
                chev.Width = 8;
                chev.Height = 5;
                chev.HorizontalAlignment = HorizontalAlignment.Right;
                chev.VerticalAlignment = VerticalAlignment.Center;
                chev.Margin = new Thickness(0, 0, -18, 0);
                chev.IsHitTestVisible = false;
                Grid g = new Grid();
                g.Children.Add(label);
                g.Children.Add(chev);
                Root.Child = g;
                Root.Padding = new Thickness(10, 0, 26, 0);
                Root.MinHeight = 34;
                Root.CornerRadius = new CornerRadius(6);
                Root.BorderThickness = new Thickness(1);
                Root.SetResourceReference(Border.BorderBrushProperty, "line");
                Root.SetResourceReference(Border.BackgroundProperty, "bg-2");
                Root.HorizontalAlignment = HorizontalAlignment.Left;
                Root.VerticalAlignment = VerticalAlignment.Center;
                Root.Focusable = true;
                Root.Cursor = Cursors.Hand;
                System.Windows.Automation.AutomationProperties.SetName(Root, name);
                U.Click(Root, Open);
                Root.GotKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "violet"); };
                Root.LostKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "line"); };
                Root.PreviewKeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.Key != Key.Down && e.Key != Key.Up) return;
                    int i = Index();
                    int j = Math.Max(0, Math.Min(options.Count - 1, i + (e.Key == Key.Down ? 1 : -1)));
                    if (j != i && options.Count > 0) Pick(options[j].Key);
                    e.Handled = true;
                };
                U.OnWindowWidth(Root, w =>
                {
                    bool c = Coarse(w);
                    Root.MinHeight = c ? 44 : 34;
                    label.FontSize = c ? 16 : 13.5;
                    Measure();
                });
            }

            int Index()
            {
                for (int i = 0; i < options.Count; i++) if (options[i].Key == Value) return i;
                return -1;
            }

            void Measure()
            {
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

            /// <summary>The options (value, text) and the one chosen (none: the first).</summary>
            public void Set(List<KeyValuePair<string, string>> items, string selected)
            {
                options = items ?? new List<KeyValuePair<string, string>>();
                Value = selected ?? "";
                int i = Index();
                label.Text = i >= 0 ? options[i].Value : options.Count > 0 ? options[0].Value : "";
                Measure();
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
                    item.Header = o.Value.Length > 0 ? o.Value : " ";
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

        /// <summary>label.check: a 15px box (violet with a white tick when on) and the words (text-2, 13px) 8px to the right.</summary>
        public sealed class OverviewCheck
        {
            public readonly Flex Root;
            readonly Border box = new RoundBorder();
            readonly Path tick = new Path();
            public bool Value;
            public event Action<bool> Changed;

            public OverviewCheck(string label, bool value)
            {
                box.Width = 15;
                box.Height = 15;
                box.CornerRadius = new CornerRadius(3);
                box.BorderThickness = new Thickness(1);
                tick.Data = Geometry.Parse("M3.5 7.5 6.2 10.2 11.5 4.5");
                tick.Stroke = Brushes.White;
                tick.StrokeThickness = 2;
                tick.StrokeStartLineCap = PenLineCap.Round;
                tick.StrokeEndLineCap = PenLineCap.Round;
                tick.StrokeLineJoin = PenLineJoin.Round;
                box.Child = tick;
                box.VerticalAlignment = VerticalAlignment.Center;
                TextBlock t = Text(label, 13, "text-2");
                t.VerticalAlignment = VerticalAlignment.Center;
                Root = Flex.Row(8, box, t);
                Root.Background = Brushes.Transparent;
                Root.Cursor = Cursors.Hand;
                Root.VerticalAlignment = VerticalAlignment.Center;
                Root.Focusable = true;
                System.Windows.Automation.AutomationProperties.SetName(Root, label);
                Action flip = () => { Set(!Value); Action<bool> h = Changed; if (h != null) h(Value); };
                U.Click(Root, flip);
                Root.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Space) { e.Handled = true; flip(); } };
                U.OnWindowWidth(Root, w => Root.MinHeight = Coarse(w) ? 44 : 0);
                Set(value);
            }

            public void Set(bool on)
            {
                Value = on;
                if (on) { box.SetResourceReference(Border.BackgroundProperty, "violet"); box.SetResourceReference(Border.BorderBrushProperty, "violet"); }
                else { box.Background = Css.Brush("#3b3b3b"); box.BorderBrush = Css.Brush("#858585"); }
                tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                System.Windows.Automation.AutomationProperties.SetItemStatus(Root, on ? "checked" : "");
            }
        }

        /// <summary>details: the summary (the disclosure triangle, then its words) that opens and closes what is under it.</summary>
        public sealed class OverviewDetails
        {
            public readonly StackPanel Root = new StackPanel();
            public readonly StackPanel Body = new StackPanel();
            readonly Path marker = new Path();
            public bool Open;
            public event Action<bool> Toggled;

            public OverviewDetails(string summary, string color, double size, bool open)
            {
                marker.Data = Geometry.Parse("M0 0 L6 3.5 L0 7 Z");
                marker.SetResourceReference(Shape.FillProperty, color);
                marker.Width = 6;
                marker.Height = 7;
                marker.VerticalAlignment = VerticalAlignment.Center;
                marker.RenderTransformOrigin = new Point(0.5, 0.5);
                marker.Margin = new Thickness(1, 0, 1, 0);
                TextBlock t = Text(summary, size, color);
                t.VerticalAlignment = VerticalAlignment.Center;
                t.TextWrapping = TextWrapping.Wrap;
                Flex head = Flex.Row(6, marker, t);
                head.Background = Brushes.Transparent;
                head.Cursor = Cursors.Hand;
                head.Focusable = true;
                head.Shrink = true;
                System.Windows.Automation.AutomationProperties.SetName(head, summary);
                Action flip = () => { Set(!Open); Action<bool> h = Toggled; if (h != null) h(Open); };
                U.Click(head, flip);
                head.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; flip(); } };
                Root.Children.Add(head);
                Root.Children.Add(Body);
                Set(open);
            }

            public void Set(bool open)
            {
                Open = open;
                Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                marker.RenderTransform = new RotateTransform(open ? 90 : 0);
                System.Windows.Automation.AutomationProperties.SetItemStatus(Root, open ? "open" : "");
            }
        }
    }
}
