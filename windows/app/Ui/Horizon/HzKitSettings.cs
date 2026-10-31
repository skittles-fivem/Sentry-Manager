using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// The pieces of hz/app.css the Settings page adds to the kit: the
    /// auto-fit grids (.grid2, .garage, .rate), checkboxes (.chk), the styled
    /// select (select.in), the day chips (.day), bullet lists (.legend-l),
    /// &lt;details&gt; (.arch), kbd, links in a line.
    /// </summary>
    public static partial class HzKit
    {
        /// <summary>
        /// grid-template-columns: repeat(auto-fit, minmax(min(100%, Min), 1fr)):
        /// as many equal columns of at least Min as fit, gaps between; a child
        /// marked SpanAll (grid-column: 1 / -1) takes a row of its own.
        /// AlignEnd: align-items end (the rate rows), else the row's height.
        /// </summary>
        public sealed class FitGrid : Panel
        {
            public static readonly DependencyProperty SpanAllProperty = DependencyProperty.RegisterAttached("SpanAll", typeof(bool), typeof(FitGrid),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
            public static void SetSpanAll(UIElement e, bool v) { e.SetValue(SpanAllProperty, v); }
            public static bool GetSpanAll(UIElement e) { return (bool)e.GetValue(SpanAllProperty); }

            public double Min = 220, ColGap = 14, RowGap = 12;
            public bool AlignEnd;
            readonly List<Rect> slots = new List<Rect>();

            int Cols(double w)
            {
                if (double.IsInfinity(w) || w <= 0) return 1;
                return Math.Max(1, (int)Math.Floor((w + ColGap) / (Math.Min(Min, w) + ColGap)));
            }

            protected override Size MeasureOverride(Size avail)
            {
                double w = avail.Width;
                int cols = Cols(w);
                double cw = double.IsInfinity(w) ? double.PositiveInfinity : Math.Max(0, (w - (cols - 1) * ColGap) / cols);
                slots.Clear();
                double y = 0, rowH = 0, widest = 0;
                int col = 0;
                List<int> row = new List<int>();
                Action close = () =>
                {
                    if (row.Count == 0) return;
                    foreach (int i in row) { Rect r = slots[i]; r.Height = rowH; slots[i] = r; }
                    y += rowH + RowGap;
                    rowH = 0;
                    col = 0;
                    row.Clear();
                };
                for (int i = 0; i < InternalChildren.Count; i++)
                {
                    UIElement c = InternalChildren[i];
                    bool span = GetSpanAll(c);
                    if (c.Visibility == Visibility.Collapsed) { slots.Add(Rect.Empty); continue; }
                    if (span || col >= cols) close();
                    double cwid = span ? (double.IsInfinity(w) ? w : w) : cw;
                    c.Measure(new Size(cwid, double.PositiveInfinity));
                    double x = span ? 0 : col * (cw + ColGap);
                    slots.Add(new Rect(x, y, double.IsInfinity(cwid) ? c.DesiredSize.Width : cwid, c.DesiredSize.Height));
                    rowH = Math.Max(rowH, c.DesiredSize.Height);
                    widest = Math.Max(widest, c.DesiredSize.Width);
                    row.Add(slots.Count - 1);
                    col = span ? cols : col + 1;
                }
                close();
                double h = Math.Max(0, y - RowGap);
                return new Size(double.IsInfinity(w) ? widest : w, h);
            }

            protected override Size ArrangeOverride(Size final)
            {
                for (int i = 0; i < InternalChildren.Count && i < slots.Count; i++)
                {
                    Rect r = slots[i];
                    if (r.IsEmpty) continue;
                    UIElement c = InternalChildren[i];
                    if (AlignEnd) c.Arrange(new Rect(r.X, r.Y + r.Height - c.DesiredSize.Height, r.Width, c.DesiredSize.Height));
                    else c.Arrange(r);
                }
                return final;
            }
        }

        public static FitGrid Fit(double min, double colGap, double rowGap, params UIElement[] children)
        {
            FitGrid g = new FitGrid();
            g.Min = min;
            g.ColGap = colGap;
            g.RowGap = rowGap;
            foreach (UIElement c in children) if (c != null) g.Children.Add(c);
            return g;
        }

        public static T SpanAll<T>(T e) where T : UIElement
        {
            FitGrid.SetSpanAll(e, true);
            return e;
        }

        // ---------------------------------------------------------------- .chk

        /// <summary>A checkbox (20px, accent magenta) as a box with its tick; Set() changes it without calling back.</summary>
        public sealed class CheckBox
        {
            public readonly Border Box = new Border();
            readonly Path tick = new Path();
            public bool On;
            public event Action<bool> Changed;

            public CheckBox(bool on)
            {
                Box.Width = 20;
                Box.Height = 20;
                Box.CornerRadius = new CornerRadius(3);
                Box.BorderThickness = new Thickness(1);
                tick.Data = Geometry.Parse("M4.5 10.5 8.5 14.5 15.5 6");
                tick.Stroke = Brushes.White;
                tick.StrokeThickness = 2.2;
                tick.StrokeStartLineCap = PenLineCap.Round;
                tick.StrokeEndLineCap = PenLineCap.Round;
                tick.StrokeLineJoin = PenLineJoin.Round;
                tick.Margin = new Thickness(-1);
                Box.Child = tick;
                Box.VerticalAlignment = VerticalAlignment.Center;
                Set(on);
            }

            public void Set(bool on)
            {
                On = on;
                // (unticked: the browser's own dark box under color-scheme: dark)
                Box.Background = on ? Css.Brush("#ff3fb4") : Css.Brush("rgba(255,255,255,.14)");
                Box.BorderBrush = on ? Css.Brush("#ff3fb4") : Css.Brush("rgba(255,255,255,.4)");
                tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }

            public void Toggle()
            {
                Set(!On);
                Action<bool> h = Changed;
                if (h != null) h(On);
            }
        }

        /// <summary>label.chk: the box and the label (15px ink), gap 10, 44px tall; a click anywhere toggles it.</summary>
        public static Flex Check(CheckBox box, string label)
        {
            TextBlock t = Text(label, 15, "ink");
            t.VerticalAlignment = VerticalAlignment.Center;
            Flex f = Flex.Row(10, box.Box, t);
            Flex.SetGrow(t, 1);
            f.MinHeight = 44;
            f.HorizontalAlignment = HorizontalAlignment.Left;
            f.Background = Brushes.Transparent;
            U.Click(f, box.Toggle);
            AutomationName(f, label);
            return f;
        }

        // ---------------------------------------------------------------- select.in

        /// <summary>select.in: the field with the chosen option and the cyan caret; a click opens the options (the browser's own list).</summary>
        public sealed class Select
        {
            public readonly Border Root;
            readonly TextBlock label = new TextBlock();
            List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            public string Value = "";
            public event Action<string> Picked;

            public Select(string name)
            {
                label.FontSize = 16;
                label.SetResourceReference(TextBlock.ForegroundProperty, "ink");
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.VerticalAlignment = VerticalAlignment.Center;
                Path caret = new Path();
                caret.Data = Geometry.Parse("M0,0 L12,0 6,6 Z");
                caret.SetResourceReference(Shape.FillProperty, "cyan");
                caret.HorizontalAlignment = HorizontalAlignment.Right;
                caret.VerticalAlignment = VerticalAlignment.Center;
                caret.Margin = new Thickness(0, 0, -24, 0);
                caret.IsHitTestVisible = false;
                Grid g = new Grid();
                g.Children.Add(label);
                g.Children.Add(caret);
                g.Margin = new Thickness(0, 0, 24, 0);
                Root = new Border();
                Root.Child = g;
                Root.MinHeight = 44;
                Root.Padding = new Thickness(12, 9, 12, 9);
                Root.CornerRadius = new CornerRadius(4);
                Root.BorderThickness = new Thickness(1);
                Root.BorderBrush = Css.Brush("rgba(207,195,238,.38)");
                Root.Background = Css.Brush("rgba(6,3,22,.78)");
                AutomationName(Root, name);
                U.Click(Root, Open);
                Root.GotKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "cyan"); };
                Root.LostKeyboardFocus += delegate { Root.BorderBrush = Css.Brush("rgba(207,195,238,.38)"); };
            }

            public void Options(List<KeyValuePair<string, string>> list, string value)
            {
                options = list;
                Show(value);
            }

            public void Show(string value)
            {
                Value = value ?? "";
                label.Text = "";
                foreach (KeyValuePair<string, string> o in options) if (o.Key == Value) label.Text = o.Value;
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
                    string v = o.Key;
                    item.Click += delegate
                    {
                        Show(v);
                        Action<string> h = Picked;
                        if (h != null) h(v);
                    };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = Root;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.MinWidth = Root.ActualWidth;
                menu.IsOpen = true;
            }
        }

        // ---------------------------------------------------------------- .day

        /// <summary>.day: a day's chip (48 x 44, 700 14px); ticked: magenta.</summary>
        public static Border DayChip(string name, bool on, Action<bool> changed)
        {
            TextBlock t = new TextBlock();
            t.Text = name;
            t.FontSize = 14;
            t.FontWeight = FontWeights.Bold;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Border b = new Border();
            b.Child = t;
            b.MinWidth = 48;
            b.MinHeight = 44;
            b.CornerRadius = new CornerRadius(4);
            b.BorderThickness = new Thickness(1);
            bool state = on;
            Action paint = () =>
            {
                b.Background = state ? Css.Brush("rgba(255,63,180,.25)") : Brushes.Transparent;
                if (state) b.SetResourceReference(Border.BorderBrushProperty, "mag"); else b.BorderBrush = Css.Brush("rgba(207,195,238,.3)");
                if (state) t.Foreground = Brushes.White; else t.SetResourceReference(TextBlock.ForegroundProperty, "dim");
                System.Windows.Automation.AutomationProperties.SetItemStatus(b, state ? "checked" : "");
            };
            paint();
            AutomationName(b, name);
            U.Click(b, () => { state = !state; paint(); changed(state); });
            return b;
        }

        // ---------------------------------------------------------------- lists, details, inline pieces

        /// <summary>ul.legend-l: bullets, padding-left 18, gap 4; each item from the page's HTML.</summary>
        public static Flex Bullets(double size, string color, params string[] html)
        {
            Flex f = Flex.Col(4);
            foreach (string h in html)
            {
                TextBlock dot = Text("•", size, color);
                dot.Width = 18;
                dot.TextAlignment = TextAlignment.Center;
                TextBlock line = Rich(SentrySix.Core.Rich.FromHtml(h), size, color);
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(dot);
                Grid.SetColumn(line, 1);
                g.Children.Add(line);
                f.Children.Add(g);
            }
            f.Margin = new Thickness(0, 4, 0, 0);
            return f;
        }

        /// <summary>details.arch: the summary (cyan 600, 44px; display: flex, so no disclosure marker) opens and closes what follows.</summary>
        public static Flex Details(string summary, UIElement body)
        {
            TextBlock t = Text(summary, 15, "cyan", 600);
            t.VerticalAlignment = VerticalAlignment.Center;
            Flex head = Flex.Row(0, t);
            head.MinHeight = 44;
            head.Background = Brushes.Transparent;
            head.HorizontalAlignment = HorizontalAlignment.Left;
            body.Visibility = Visibility.Collapsed;
            U.Click(head, () => body.Visibility = body.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
            AutomationName(head, summary);
            Flex f = Flex.Col(0, head, body);
            f.Margin = new Thickness(0, 10, 0, 0);
            return f;
        }

        /// <summary>A link inside a line of text (cyan, underlined on hover).</summary>
        public static Hyperlink Link(string text, Action click, bool mono = false)
        {
            Hyperlink l = new Hyperlink(new Run(text));
            l.SetResourceReference(TextElement.ForegroundProperty, "cyan");
            if (mono) l.FontFamily = U.Consolas;
            l.TextDecorations = null;
            l.MouseEnter += delegate { l.TextDecorations = TextDecorations.Underline; };
            l.MouseLeave += delegate { l.TextDecorations = null; };
            l.Click += delegate { click(); };
            return l;
        }

        /// <summary>kbd: 700 12px mono, a 1px border, radius 3.</summary>
        public static InlineUIContainer Kbd(string key)
        {
            TextBlock t = new TextBlock();
            t.Text = key;
            t.FontFamily = U.Consolas;
            t.FontSize = 12;
            t.FontWeight = FontWeights.Bold;
            t.SetResourceReference(TextBlock.ForegroundProperty, "ink");
            Border b = new Border();
            b.Child = t;
            b.Padding = new Thickness(6, 2, 6, 2);
            b.CornerRadius = new CornerRadius(3);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Css.Brush("rgba(207,195,238,.4)");
            b.Background = Css.Brush("rgba(255,255,255,.06)");
            InlineUIContainer c = new InlineUIContainer(b);
            c.BaselineAlignment = BaselineAlignment.Center;
            return c;
        }

        static readonly Regex KbdRe = new Regex("<kbd>(.*?)</kbd>", RegexOptions.CultureInvariant);

        /// <summary>A line of the page's HTML with &lt;kbd&gt; keys drawn as keys (the rest as rich text).</summary>
        public static TextBlock WithKeys(string html, double size, string color)
        {
            TextBlock t = Text("", size, color);
            int at = 0;
            foreach (Match m in KbdRe.Matches(html))
            {
                if (m.Index > at) foreach (Inline i in Rich(SentrySix.Core.Rich.FromHtml(html.Substring(at, m.Index - at)), size, color).Inlines.ToArrayCopy()) t.Inlines.Add(i);
                t.Inlines.Add(Kbd(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)));
                at = m.Index + m.Length;
            }
            if (at < html.Length) foreach (Inline i in Rich(SentrySix.Core.Rich.FromHtml(html.Substring(at)), size, color).Inlines.ToArrayCopy()) t.Inlines.Add(i);
            return t;
        }

        static List<Inline> ToArrayCopy(this InlineCollection c)
        {
            List<Inline> l = new List<Inline>(c);
            c.Clear();
            return l;
        }

        /// <summary>.in.mono: a field in the monospace font.</summary>
        public static TextBox MonoInput(string value, string placeholder)
        {
            TextBox t = Input(value, placeholder);
            t.FontFamily = U.Consolas;
            return t;
        }
    }
}
