using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SentrySix.Ui
{
    /// <summary>
    /// The browser's automatic table layout (CSS table-layout: auto, as
    /// Chrome's table code does it) for <see cref="Table"/>: every column has
    /// a narrowest width (its cells' min-content: the widest letter, word or
    /// unbreakable part) and a widest one (max-content: each cell on one
    /// line); a cell across several columns adds what its own widths need
    /// beyond theirs, in proportion to their widest widths. The table is as
    /// wide as its box (.table { width: 100% }):
    /// <list type="bullet">
    /// <item>narrower than every narrowest width together: each column its narrowest (the table overflows);</item>
    /// <item>between: each column grows from its narrowest towards its widest in proportion to the difference;</item>
    /// <item>wider than every widest width: the rest goes to the columns in proportion to their widest widths.</item>
    /// </list>
    /// </summary>
    public static class TableSizing
    {
        /// <summary>
        /// overflow-wrap: break-word (td.words, span.words, th) on an element and
        /// what is in it: its text is never narrower than its longest word.
        /// Without it, .table td's overflow-wrap: anywhere lets the text break
        /// between any two letters.
        /// </summary>
        public static readonly DependencyProperty WordsProperty = DependencyProperty.RegisterAttached("Words", typeof(bool), typeof(TableSizing),
            new FrameworkPropertyMetadata(false));
        public static bool GetWords(DependencyObject e) { return (bool)e.GetValue(WordsProperty); }
        public static void SetWords(DependencyObject e, bool v) { e.SetValue(WordsProperty, v); }

        static readonly Size Endless = new Size(double.PositiveInfinity, double.PositiveInfinity);

        /// <summary>The element's max-content width (one line each), margins included.</summary>
        public static double MaxContent(UIElement e)
        {
            if (e == null || e.Visibility == Visibility.Collapsed) return 0;
            e.Measure(Endless);
            return e.DesiredSize.Width;
        }

        /// <summary>The element's min-content width, margins included.</summary>
        public static double MinContent(UIElement e)
        {
            return Min(e, false);
        }

        static double Min(UIElement e, bool words)
        {
            if (e == null || e.Visibility == Visibility.Collapsed) return 0;
            if (GetWords(e)) words = true;
            FrameworkElement fe = e as FrameworkElement;
            if (fe == null) return MaxContent(e);
            double margin = fe.Margin.Left + fe.Margin.Right;
            if (!double.IsNaN(fe.Width)) return fe.Width + margin;
            double v;
            TextBlock t = fe as TextBlock;
            Border b = fe as Border;
            Decorator d = fe as Decorator;
            Flex f = fe as Flex;
            StackPanel sp = fe as StackPanel;
            Grid g = fe as Grid;
            Panel p = fe as Panel;
            if (t != null)
            {
                if (t.TextWrapping == TextWrapping.NoWrap) return MaxContent(t);
                v = TextMin(t, words) + t.Padding.Left + t.Padding.Right;
            }
            else if (b != null)
            {
                if (b.Child == null) return MaxContent(b);
                v = Min(b.Child, words) + b.Padding.Left + b.Padding.Right + b.BorderThickness.Left + b.BorderThickness.Right;
            }
            else if (d != null)
            {
                if (d.Child == null) return MaxContent(d);
                v = Min(d.Child, words);
            }
            else if (f != null && f.Orientation == Orientation.Horizontal && !f.Wrap)
            {
                // a row that does not wrap: its items side by side, each at its narrowest
                v = 0;
                int n = 0;
                foreach (UIElement c in f.Children)
                {
                    if (c.Visibility == Visibility.Collapsed) continue;
                    v += Min(c, words);
                    n++;
                }
                if (n > 1) v += f.Gap * (n - 1);
            }
            else if (sp != null && sp.Orientation == Orientation.Horizontal)
            {
                v = 0;
                foreach (UIElement c in sp.Children) v += Min(c, words);
            }
            else if (g != null && g.ColumnDefinitions.Count > 1)
            {
                // one cell per column: the columns' narrowest widths side by side
                double[] w = new double[g.ColumnDefinitions.Count];
                for (int i = 0; i < w.Length; i++)
                {
                    GridLength gl = g.ColumnDefinitions[i].Width;
                    if (gl.IsAbsolute) w[i] = gl.Value;
                }
                foreach (UIElement c in g.Children)
                {
                    int col = Math.Min(Grid.GetColumn(c), w.Length - 1);
                    if (Grid.GetColumnSpan(c) != 1 || g.ColumnDefinitions[col].Width.IsAbsolute) continue;
                    w[col] = Math.Max(w[col], Min(c, words));
                }
                v = 0;
                foreach (double x in w) v += x;
            }
            else if (p != null)
            {
                // a column, a wrapping row, one cell: its widest child
                v = 0;
                foreach (UIElement c in p.Children) v = Math.Max(v, Min(c, words));
            }
            else return MaxContent(fe);
            v = Math.Max(v, fe.MinWidth);
            if (!double.IsInfinity(fe.MaxWidth)) v = Math.Min(v, fe.MaxWidth);
            return v + margin;
        }

        // ---------- text ----------

        sealed class Piece
        {
            public string Text;
            public Typeface Face;
            public double Size;
            public double Atom = -1;
        }

        static void Pieces(TextBlock t, bool words, List<Piece> into)
        {
            if (t.Inlines.Count == 0)
            {
                Piece p = new Piece();
                p.Text = t.Text ?? "";
                p.Face = new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch);
                p.Size = t.FontSize;
                into.Add(p);
                return;
            }
            foreach (Inline i in t.Inlines) Pieces(i, words, into);
        }

        static void Pieces(Inline i, bool words, List<Piece> into)
        {
            Run r = i as Run;
            Span s = i as Span;
            InlineUIContainer ui = i as InlineUIContainer;
            if (r != null)
            {
                Piece p = new Piece();
                p.Text = r.Text ?? "";
                p.Face = new Typeface(r.FontFamily, r.FontStyle, r.FontWeight, r.FontStretch);
                p.Size = r.FontSize;
                into.Add(p);
            }
            else if (s != null)
            {
                foreach (Inline c in s.Inlines) Pieces(c, words, into);
            }
            else if (ui != null)
            {
                Piece p = new Piece();
                p.Text = " ";
                p.Atom = Min(ui.Child, words);
                into.Add(p);
            }
            else if (i is LineBreak)
            {
                Piece p = new Piece();
                p.Text = "\n";
                into.Add(p);
            }
        }

        static double Width(string text, Piece p, double dip)
        {
            if (text.Length == 0) return 0;
            FormattedText ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, p.Face, p.Size, Brushes.Black, dip);
            return ft.WidthIncludingTrailingWhitespace;
        }

        static bool Space(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }

        /// <summary>
        /// The text's narrowest width: its longest word (words), else its
        /// widest letter (overflow-wrap: anywhere); a part inside it that
        /// does not break (an InlineUIContainer) at its own narrowest.
        /// </summary>
        static double TextMin(TextBlock t, bool words)
        {
            List<Piece> list = new List<Piece>();
            Pieces(t, words, list);
            double dip = 1.0;
            try { dip = VisualTreeHelper.GetDpi(t).PixelsPerDip; } catch (Exception) { }
            double best = 0;
            if (!words)
            {
                foreach (Piece p in list)
                {
                    if (p.Atom >= 0) { best = Math.Max(best, p.Atom); continue; }
                    if (p.Face == null) continue;
                    HashSet<char> seen = new HashSet<char>();
                    foreach (char c in p.Text)
                    {
                        if (Space(c) || c == '​' || !seen.Add(c)) continue;
                        best = Math.Max(best, Width(c.ToString(), p, dip));
                    }
                }
                return best;
            }
            // a word may run across pieces (a bold name glued to its text): add its parts up
            double word = 0;
            foreach (Piece p in list)
            {
                if (p.Atom >= 0)
                {
                    best = Math.Max(best, word);
                    best = Math.Max(best, p.Atom);
                    word = 0;
                    continue;
                }
                if (p.Face == null) { best = Math.Max(best, word); word = 0; continue; }
                // (Format.Breakable's zero-width spaces stand for overflow-wrap; a word keeps them together)
                string text = p.Text.Replace("​", "");
                int start = 0;
                for (int k = 0; k <= text.Length; k++)
                {
                    if (k < text.Length && !Space(text[k])) continue;
                    if (k > start) word += Width(text.Substring(start, k - start), p, dip);
                    if (k < text.Length)
                    {
                        best = Math.Max(best, word);
                        word = 0;
                    }
                    start = k + 1;
                }
            }
            return Math.Max(best, word);
        }

        // ---------- the columns ----------

        /// <summary>
        /// The columns' widths for a table <paramref name="width"/> wide, from
        /// the grid's cells (each at its column and span).
        /// </summary>
        public static double[] Columns(Grid g, double width)
        {
            int n = g.ColumnDefinitions.Count;
            double[] min = new double[n], max = new double[n];
            List<UIElement> spanning = new List<UIElement>();
            foreach (UIElement c in g.Children)
            {
                if (c.Visibility == Visibility.Collapsed) continue;
                int col = Math.Min(Grid.GetColumn(c), n - 1);
                int span = Math.Min(Grid.GetColumnSpan(c), n - col);
                // (the lines under the rows: borders with nothing in them)
                Border line = c as Border;
                if (line != null && line.Child == null) continue;
                if (span > 1) { spanning.Add(c); continue; }
                min[col] = Math.Max(min[col], MinContent(c));
                max[col] = Math.Max(max[col], MaxContent(c));
            }
            for (int i = 0; i < n; i++) max[i] = Math.Max(max[i], min[i]);
            foreach (UIElement c in spanning)
            {
                int col = Math.Min(Grid.GetColumn(c), n - 1);
                int span = Math.Min(Grid.GetColumnSpan(c), n - col);
                Spread(min, max, col, span, MinContent(c), true);
                Spread(min, max, col, span, MaxContent(c), false);
                for (int i = col; i < col + span; i++) max[i] = Math.Max(max[i], min[i]);
            }
            return Distribute(min, max, width);
        }

        // a spanning cell's need beyond its columns', in proportion to their widest widths (equally when they have none)
        static void Spread(double[] min, double[] max, int col, int span, double need, bool toMin)
        {
            double[] target = toMin ? min : max;
            double have = 0, weight = 0;
            for (int i = col; i < col + span; i++) { have += target[i]; weight += max[i]; }
            double more = need - have;
            if (more <= 0) return;
            for (int i = col; i < col + span; i++) target[i] += weight > 0 ? more * max[i] / weight : more / span;
        }

        /// <summary>The three cases of the auto layout (see the class).</summary>
        public static double[] Distribute(double[] min, double[] max, double width)
        {
            int n = min.Length;
            double[] w = new double[n];
            double sumMin = 0, sumMax = 0;
            for (int i = 0; i < n; i++) { sumMin += min[i]; sumMax += max[i]; }
            if (double.IsInfinity(width) || double.IsNaN(width)) width = sumMax;
            if (width <= sumMin)
            {
                for (int i = 0; i < n; i++) w[i] = min[i];
            }
            else if (width <= sumMax)
            {
                double room = sumMax - sumMin, give = width - sumMin;
                for (int i = 0; i < n; i++) w[i] = min[i] + (room > 0 ? (max[i] - min[i]) * give / room : 0);
            }
            else
            {
                double extra = width - sumMax;
                for (int i = 0; i < n; i++) w[i] = max[i] + (sumMax > 0 ? extra * max[i] / sumMax : extra / Math.Max(1, n));
            }
            return w;
        }
    }

    /// <summary>The table's grid: its columns sized by <see cref="TableSizing"/> each time it is measured.</summary>
    sealed class AutoGrid : Grid
    {
        protected override Size MeasureOverride(Size available)
        {
            if (ColumnDefinitions.Count > 0)
            {
                double[] w = TableSizing.Columns(this, available.Width);
                for (int i = 0; i < w.Length; i++)
                {
                    GridLength now = ColumnDefinitions[i].Width;
                    if (!now.IsAbsolute || Math.Abs(now.Value - w[i]) > 0.01) ColumnDefinitions[i].Width = new GridLength(Math.Max(0, w[i]));
                }
            }
            return base.MeasureOverride(available);
        }
    }
}
