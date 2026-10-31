using System;
using System.Windows;
using System.Windows.Controls;

namespace SentrySix.Ui
{
    /// <summary>
    /// CSS flexbox's common case: children in a row or column with a gap,
    /// "flex: 1" children sharing the room left (Flex.Grow), justify-content
    /// (start, end, center, space-between) and optional wrapping, so the
    /// stylesheets' gap/flex numbers carry over unchanged.
    /// </summary>
    public sealed class Flex : Panel
    {
        public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached("Grow", typeof(double), typeof(Flex),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
        public static double GetGrow(UIElement e) { return (double)e.GetValue(GrowProperty); }
        public static void SetGrow(UIElement e, double v) { e.SetValue(GrowProperty, v); }

        public Orientation Orientation = Orientation.Horizontal;
        public double Gap;
        /// <summary>Gap between wrapped lines (row-gap).</summary>
        public double LineGap;
        public bool Wrap;
        /// <summary>"start", "end", "center", "between" (on wrapped lines too).</summary>
        public string Justify = "start";
        /// <summary>
        /// CSS flex-shrink: 1 (a row with no Grow children): when the children's
        /// own widths overflow the row, each gives up a share of the overflow in
        /// proportion to its width and is measured again that narrow (text wraps,
        /// a wrapping Flex takes more lines).
        /// </summary>
        public bool Shrink;
        /// <summary>
        /// CSS flex-grow on wrapped lines too: each line shares its spare room
        /// among its Grow children (off by default: the built-in pages' rows
        /// keep their arrangement).
        /// </summary>
        public bool GrowOnLines;

        public Flex() { }

        public Flex(Orientation o, double gap)
        {
            Orientation = o;
            Gap = gap;
        }

        public static Flex Row(double gap, params UIElement[] children)
        {
            Flex f = new Flex(Orientation.Horizontal, gap);
            foreach (UIElement c in children) if (c != null) f.Children.Add(c);
            return f;
        }

        public static Flex Col(double gap, params UIElement[] children)
        {
            Flex f = new Flex(Orientation.Vertical, gap);
            foreach (UIElement c in children) if (c != null) f.Children.Add(c);
            return f;
        }

        public static T Grow<T>(T e, double g) where T : UIElement
        {
            SetGrow(e, g);
            return e;
        }

        bool H { get { return Orientation == Orientation.Horizontal; } }
        static double Main(Size s, bool h) { return h ? s.Width : s.Height; }
        static double Cross(Size s, bool h) { return h ? s.Height : s.Width; }

        protected override Size MeasureOverride(Size available)
        {
            bool h = H;
            double mainAvail = Main(available, h);
            double crossAvail = Cross(available, h);
            if (Wrap && !double.IsInfinity(mainAvail)) return MeasureWrapped(available, h);
            double used = 0, cross = 0, grow = 0;
            int shown = 0;
            foreach (UIElement c in InternalChildren)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                shown++;
                double g = GetGrow(c);
                if (g > 0) { grow += g; continue; }
                c.Measure(h ? new Size(double.PositiveInfinity, crossAvail) : new Size(crossAvail, double.PositiveInfinity));
                used += Main(c.DesiredSize, h);
                cross = Math.Max(cross, Cross(c.DesiredSize, h));
            }
            double gaps = Math.Max(0, shown - 1) * Gap;
            if (Shrink && grow <= 0 && !double.IsInfinity(mainAvail) && used + gaps > mainAvail + 0.5 && used > 0)
            {
                double over = used + gaps - mainAvail, basis = used;
                used = 0;
                cross = 0;
                foreach (UIElement c in InternalChildren)
                {
                    if (c.Visibility == Visibility.Collapsed) continue;
                    double b = Main(c.DesiredSize, h);
                    double m = Math.Max(0, b - over * b / basis);
                    c.Measure(h ? new Size(m, crossAvail) : new Size(crossAvail, m));
                    used += Math.Min(m, Main(c.DesiredSize, h));
                    cross = Math.Max(cross, Cross(c.DesiredSize, h));
                }
            }
            double left = double.IsInfinity(mainAvail) ? double.PositiveInfinity : Math.Max(0, mainAvail - used - gaps);
            foreach (UIElement c in InternalChildren)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                double g = GetGrow(c);
                if (g <= 0) continue;
                double share = double.IsInfinity(left) ? double.PositiveInfinity : left * g / grow;
                c.Measure(h ? new Size(share, crossAvail) : new Size(crossAvail, share));
                used += Main(c.DesiredSize, h);
                cross = Math.Max(cross, Cross(c.DesiredSize, h));
            }
            double main = used + gaps;
            return h ? new Size(main, cross) : new Size(cross, main);
        }

        Size MeasureWrapped(Size available, bool h)
        {
            double mainAvail = Main(available, h);
            double lineMain = 0, lineCross = 0, total = 0, widest = 0;
            bool first = true;
            foreach (UIElement c in InternalChildren)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                c.Measure(h ? new Size(mainAvail, double.PositiveInfinity) : new Size(double.PositiveInfinity, mainAvail));
                double m = Main(c.DesiredSize, h);
                if (!first && lineMain + Gap + m > mainAvail + 0.5)
                {
                    total += lineCross + LineGap;
                    widest = Math.Max(widest, lineMain);
                    lineMain = 0;
                    lineCross = 0;
                    first = true;
                }
                lineMain += (first ? 0 : Gap) + m;
                lineCross = Math.Max(lineCross, Cross(c.DesiredSize, h));
                first = false;
            }
            total += lineCross;
            widest = Math.Max(widest, lineMain);
            return h ? new Size(widest, total) : new Size(total, widest);
        }

        protected override Size ArrangeOverride(Size final)
        {
            bool h = H;
            double mainSize = Main(final, h);
            double crossSize = Cross(final, h);
            if (Wrap) return ArrangeWrapped(final, h);
            double used = 0, grow = 0;
            int shown = 0;
            foreach (UIElement c in InternalChildren)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                shown++;
                if (GetGrow(c) > 0) grow += GetGrow(c);
                else used += Main(c.DesiredSize, h);
            }
            double gaps = Math.Max(0, shown - 1) * Gap;
            double left = Math.Max(0, mainSize - used - gaps);
            double pos = 0, gap = Gap;
            if (grow <= 0)
            {
                if (Justify == "end") pos = left;
                else if (Justify == "center") pos = left / 2;
                else if (Justify == "between" && shown > 1) gap = Gap + left / (shown - 1);
            }
            foreach (UIElement c in InternalChildren)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                double g = GetGrow(c);
                double m = g > 0 ? left * g / grow : Main(c.DesiredSize, h);
                double cs = Cross(c.DesiredSize, h);
                FrameworkElement fe = c as FrameworkElement;
                double off = 0, size = cs;
                if (fe != null)
                {
                    bool stretch = h ? fe.VerticalAlignment == VerticalAlignment.Stretch : fe.HorizontalAlignment == HorizontalAlignment.Stretch;
                    bool center = h ? fe.VerticalAlignment == VerticalAlignment.Center : fe.HorizontalAlignment == HorizontalAlignment.Center;
                    bool end = h ? fe.VerticalAlignment == VerticalAlignment.Bottom : fe.HorizontalAlignment == HorizontalAlignment.Right;
                    if (stretch) size = crossSize;
                    else if (center) off = (crossSize - cs) / 2;
                    else if (end) off = crossSize - cs;
                }
                c.Arrange(h ? new Rect(pos, off, m, size) : new Rect(off, pos, size, m));
                pos += m + gap;
            }
            return final;
        }

        Size ArrangeWrapped(Size final, bool h)
        {
            double mainAvail = Main(final, h);
            System.Collections.Generic.List<UIElement> line = new System.Collections.Generic.List<UIElement>();
            double lineMain = 0, lineCross = 0, crossPos = 0;
            Action flush = delegate
            {
                double grow = 0;
                if (GrowOnLines) foreach (UIElement c in line) grow += Math.Max(0, GetGrow(c));
                double spare = Math.Max(0, mainAvail - lineMain);
                double pos = grow > 0 ? 0 : Justify == "end" ? mainAvail - lineMain : Justify == "center" ? (mainAvail - lineMain) / 2 : 0;
                // space-between: each line spreads its own items (one alone stays at the start)
                double gap = grow <= 0 && Justify == "between" && line.Count > 1 ? Gap + spare / (line.Count - 1) : Gap;
                foreach (UIElement c in line)
                {
                    double m = Main(c.DesiredSize, h) + (grow > 0 ? spare * Math.Max(0, GetGrow(c)) / grow : 0);
                    double cs = Cross(c.DesiredSize, h);
                    double off = (lineCross - cs) / 2;
                    c.Arrange(h ? new Rect(pos, crossPos + off, m, cs) : new Rect(crossPos + off, pos, cs, m));
                    pos += m + gap;
                }
                crossPos += lineCross + LineGap;
                line.Clear();
                lineMain = 0;
                lineCross = 0;
            };
            foreach (UIElement c in InternalChildren)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                double m = Main(c.DesiredSize, h);
                if (line.Count > 0 && lineMain + Gap + m > mainAvail + 0.5) flush();
                lineMain += (line.Count > 0 ? Gap : 0) + m;
                lineCross = Math.Max(lineCross, Cross(c.DesiredSize, h));
                line.Add(c);
            }
            if (line.Count > 0) flush();
            return final;
        }
    }
}
