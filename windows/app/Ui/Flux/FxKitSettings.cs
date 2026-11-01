using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// The pieces of flux.css and shell.css the Settings page adds to the kit:
    /// the tab list (.settings, .stabs), the card head (.card-h), fields
    /// (.field), the two-column grid (.grid2, .rate), checkboxes (.check),
    /// select.input, the key-value list (.kv), notes (.note), &lt;details&gt;
    /// (.more), code and kbd, the time field, the day picker (.daypick),
    /// .row-actions. Every name starts with Settings (FxKit is shared).
    /// </summary>
    public static partial class FxKit
    {
        // ---------------------------------------------------------------- .settings, .stabs

        /// <summary>
        /// .settings: the tabs (190 px) beside the panel, gap 18; at 1180 px and
        /// less one column, the tabs a wrapping row over the panel.
        /// </summary>
        public static Grid SettingsLayout(SettingsTabList tabs, FrameworkElement panel)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tabs.Root.VerticalAlignment = VerticalAlignment.Top;
            panel.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(tabs.Root);
            g.Children.Add(panel);
            U.OnWindowWidth(g, w =>
            {
                bool narrow = w <= 1180;
                g.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : new GridLength(190);
                System.Windows.Controls.Grid.SetColumnSpan(tabs.Root, narrow ? 2 : 1);
                System.Windows.Controls.Grid.SetRow(panel, narrow ? 1 : 0);
                System.Windows.Controls.Grid.SetColumn(panel, narrow ? 0 : 1);
                System.Windows.Controls.Grid.SetColumnSpan(panel, narrow ? 2 : 1);
                panel.Margin = narrow ? new Thickness(0, 18, 0, 0) : new Thickness(18, 0, 0, 0);
                tabs.Layout(narrow, Coarse(w));
            });
            return g;
        }

        /// <summary>nav.stabs: a column of links (gap 2) or a wrapping row (gap 4); the one shown violet-soft with a 3 px violet bar on its left.</summary>
        public sealed class SettingsTabList
        {
            public readonly Flex Root = new Flex(Orientation.Vertical, 2);
            readonly List<Border> items = new List<Border>();
            readonly List<TextBlock> labels = new List<TextBlock>();

            public SettingsTabList(string[][] tabs, string current, Action<string> go, string label)
            {
                System.Windows.Automation.AutomationProperties.SetName(Root, label);
                foreach (string[] t in tabs)
                {
                    string key = t[0];
                    bool on = key == current;
                    TextBlock text = Text(t[1], 14, on ? "text" : "text-2");
                    text.TextWrapping = TextWrapping.NoWrap;
                    text.VerticalAlignment = VerticalAlignment.Center;
                    text.Margin = Css.T(8, 12);
                    Rectangle bar = new Rectangle();
                    bar.Width = 3;
                    bar.HorizontalAlignment = HorizontalAlignment.Left;
                    bar.SetResourceReference(Shape.FillProperty, "violet");
                    bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                    Grid inner = new Grid();
                    inner.Children.Add(text);
                    inner.Children.Add(bar);
                    Border b = new RoundBorder();
                    b.Child = inner;
                    b.CornerRadius = new CornerRadius(8);
                    b.Uid = "stab-" + key;
                    System.Windows.Automation.AutomationProperties.SetName(b, t[1]);
                    if (on)
                    {
                        b.SetResourceReference(Border.BackgroundProperty, "violet-soft");
                        System.Windows.Automation.AutomationProperties.SetItemStatus(b, "current");
                    }
                    else
                    {
                        b.Background = Brushes.Transparent;
                        U.Hover(b, h => { if (h) b.SetResourceReference(Border.BackgroundProperty, "panel"); else b.Background = Brushes.Transparent; });
                    }
                    U.Click(b, () => go(key));
                    items.Add(b);
                    labels.Add(text);
                    Root.Children.Add(b);
                }
            }

            /// <summary>The column (wide) or the wrapping row; coarse: 44 px line height (60 px tall links).</summary>
            public void Layout(bool narrow, bool coarse)
            {
                Root.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
                Root.Wrap = narrow;
                Root.Gap = narrow ? 4 : 2;
                Root.LineGap = 4;
                Root.Margin = narrow ? new Thickness(0, 0, 0, 4) : new Thickness(0);
                foreach (Border b in items)
                {
                    b.MinHeight = coarse ? 60 : 0;
                    b.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                }
                Root.InvalidateMeasure();
            }
        }

        // ---------------------------------------------------------------- cards, fields

        /// <summary>.card-h: the h2 (14px 650) and what goes on the right, wrapping.</summary>
        public static Flex SettingsCardHead(string title, UIElement right)
        {
            TextBlock h2 = Text(title, 14, "text", 650);
            h2.VerticalAlignment = VerticalAlignment.Center;
            Flex f = Flex.Row(10, h2);
            f.Wrap = true;
            f.LineGap = 10;
            f.Justify = "between";
            if (right != null)
            {
                FrameworkElement fe = right as FrameworkElement;
                if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
                f.Children.Add(right);
            }
            return f;
        }

        /// <summary>section.card (gap 10) or form.card.form-card (gap 12).</summary>
        public static Border SettingsCard(double gap, params UIElement[] children)
        {
            Flex col = Flex.Col(gap);
            foreach (UIElement c in children) if (c != null) col.Children.Add(c);
            return Card(col, false);
        }

        /// <summary>A small line (.small): 12.5px in a colour (muted, faint, text...).</summary>
        public static TextBlock SettingsSmall(string text, string color)
        {
            return Text(text, 12.5, color);
        }

        /// <summary>.field: the label (12px muted 500) over its control, gap 5.</summary>
        public static Flex SettingsField(string label, UIElement control)
        {
            TextBlock l = Text(label, 12, "muted", 500);
            Flex f = Flex.Col(5, l, control);
            FrameworkElement fe = control as FrameworkElement;
            if (fe != null) fe.HorizontalAlignment = HorizontalAlignment.Stretch;
            return f;
        }

        /// <summary>.row-actions: a wrapping row of buttons, gap 8, margin-top 4.</summary>
        public static Flex SettingsActions(params UIElement[] buttons)
        {
            Flex f = Flex.Row(8);
            foreach (UIElement b in buttons) if (b != null) f.Children.Add(b);
            f.Wrap = true;
            f.LineGap = 8;
            f.Margin = new Thickness(0, 4, 0, 0);
            return f;
        }

        /// <summary>input.input (.mono: the monospace font at 12.5px; 16px on a touch screen).</summary>
        public static TextBox SettingsInput(string value, string placeholder, string label, bool mono)
        {
            TextBox t = Input(value, placeholder, label);
            if (mono)
            {
                t.FontFamily = Mono;
                t.FontSize = 12.5;
                U.OnWindowWidth(t, w => t.FontSize = Coarse(w) ? 16 : 12.5);
            }
            return t;
        }

        /// <summary>input.input[type=password].</summary>
        public static PasswordBox SettingsPassword(string placeholder, string label)
        {
            PasswordBox p = (PasswordBox)U.Input(InK(false), true, "", placeholder);
            System.Windows.Automation.AutomationProperties.SetName(p, label ?? "");
            U.OnWindowWidth(p, w => { bool c = Coarse(w); p.FontSize = c ? 16 : 13.5; p.MinHeight = c ? 44 : 34; });
            return p;
        }

        // ---------------------------------------------------------------- .grid2, .rate

        /// <summary>
        /// A CSS grid of fixed tracks (fr shares, gap between): the children in
        /// order, one per cell; one marked SettingsSpan (grid-column: 1 / -1)
        /// takes a row of its own. AlignEnd: align-items end (.rate), else the
        /// row's height. The owner swaps <see cref="Fr"/> at its breakpoints.
        /// </summary>
        public sealed class SettingsGrid : Panel
        {
            public static readonly DependencyProperty SettingsSpanProperty = DependencyProperty.RegisterAttached("SettingsSpan", typeof(bool), typeof(SettingsGrid),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
            public static void SetSettingsSpan(UIElement e, bool v) { e.SetValue(SettingsSpanProperty, v); }
            public static bool GetSettingsSpan(UIElement e) { return (bool)e.GetValue(SettingsSpanProperty); }

            public double[] Fr = { 1, 1 };
            public double ColGap = 12, RowGap = 12;
            public bool AlignEnd;
            readonly List<Rect> slots = new List<Rect>();

            protected override Size MeasureOverride(Size avail)
            {
                double w = avail.Width;
                int cols = Math.Max(1, Fr.Length);
                double sum = 0;
                foreach (double f in Fr) sum += f;
                double room = double.IsInfinity(w) ? double.PositiveInfinity : Math.Max(0, w - (cols - 1) * ColGap);
                double[] cw = new double[cols];
                double[] cx = new double[cols];
                double x0 = 0;
                for (int i = 0; i < cols; i++)
                {
                    cw[i] = double.IsInfinity(room) ? double.PositiveInfinity : room * Fr[i] / sum;
                    cx[i] = x0;
                    if (!double.IsInfinity(cw[i])) x0 += cw[i] + ColGap;
                }
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
                foreach (UIElement c in InternalChildren)
                {
                    if (c.Visibility == Visibility.Collapsed) { slots.Add(Rect.Empty); continue; }
                    bool span = GetSettingsSpan(c);
                    if (span || col >= cols) close();
                    double width = span ? w : cw[col];
                    c.Measure(new Size(width, double.PositiveInfinity));
                    double x = span ? 0 : cx[col];
                    slots.Add(new Rect(x, y, double.IsInfinity(width) ? c.DesiredSize.Width : width, c.DesiredSize.Height));
                    rowH = Math.Max(rowH, c.DesiredSize.Height);
                    widest = Math.Max(widest, c.DesiredSize.Width);
                    row.Add(slots.Count - 1);
                    col = span ? cols : col + 1;
                }
                close();
                return new Size(double.IsInfinity(w) ? widest : w, Math.Max(0, y - RowGap));
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

        public static T SettingsSpan<T>(T e) where T : UIElement
        {
            SettingsGrid.SetSettingsSpan(e, true);
            return e;
        }

        /// <summary>.grid2: two equal columns, gap 12; one column at 860 px and less.</summary>
        public static SettingsGrid SettingsGrid2(params UIElement[] children)
        {
            SettingsGrid g = new SettingsGrid();
            foreach (UIElement c in children) if (c != null) g.Children.Add(c);
            U.OnWindowWidth(g, w => { g.Fr = w <= 860 ? new double[] { 1 } : new double[] { 1, 1 }; g.InvalidateMeasure(); });
            return g;
        }

        // ---------------------------------------------------------------- .check

        /// <summary>
        /// label.check: the box (15 px, 22 on a touch screen; ticked: violet
        /// with a white tick) and the text (13px text-2), gap 8; a click
        /// anywhere toggles it. Set() changes it without calling back.
        /// </summary>
        public sealed class SettingsCheck
        {
            public readonly Flex Root;
            public readonly Border Box = new Border();
            readonly Path tick = new Path();
            public bool On;
            public event Action<bool> Changed;

            public SettingsCheck(bool on, UIElement label)
            {
                Box.Width = 15;
                Box.Height = 15;
                Box.CornerRadius = new CornerRadius(3);
                Box.BorderThickness = new Thickness(1);
                tick.Data = Geometry.Parse("M3.5 7.8 6.3 10.5 11.5 4.5");
                tick.Stroke = Brushes.White;
                tick.StrokeThickness = 2;
                tick.StrokeStartLineCap = PenLineCap.Round;
                tick.StrokeEndLineCap = PenLineCap.Round;
                tick.StrokeLineJoin = PenLineJoin.Round;
                tick.Stretch = Stretch.None;
                tick.Margin = new Thickness(-1);
                Viewbox vb = new Viewbox();
                vb.Child = new Canvas { Width = 15, Height = 15, Children = { tick } };
                Box.Child = vb;
                Box.VerticalAlignment = VerticalAlignment.Center;
                Root = Flex.Row(8, Box, label);
                FrameworkElement fe = label as FrameworkElement;
                if (fe != null) { fe.VerticalAlignment = VerticalAlignment.Center; Flex.SetGrow(fe, 1); }
                Root.HorizontalAlignment = HorizontalAlignment.Left;
                Root.Background = Brushes.Transparent;
                U.Click(Root, Toggle);
                U.OnWindowWidth(Root, w =>
                {
                    bool c = Coarse(w);
                    Box.Width = Box.Height = c ? 22 : 15;
                    Root.MinHeight = c ? 44 : 0;
                });
                Set(on);
            }

            public void Set(bool on)
            {
                On = on;
                if (on)
                {
                    Box.SetResourceReference(Border.BackgroundProperty, "violet");
                    Box.SetResourceReference(Border.BorderBrushProperty, "violet");
                }
                else
                {
                    // (unticked: the browser's own dark box under color-scheme: dark)
                    Box.Background = Css.Brush("rgba(255,255,255,.08)");
                    Box.BorderBrush = Css.Brush("rgba(255,255,255,.45)");
                }
                tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                System.Windows.Automation.AutomationProperties.SetItemStatus(Root, on ? "checked" : "");
            }

            public void Toggle()
            {
                Set(!On);
                Action<bool> h = Changed;
                if (h != null) h(On);
            }
        }

        /// <summary>label.check with its text.</summary>
        public static SettingsCheck SettingsCheckBox(bool on, string label)
        {
            TextBlock t = Text(label, 13, "text-2");
            SettingsCheck c = new SettingsCheck(on, t);
            System.Windows.Automation.AutomationProperties.SetName(c.Root, label);
            return c;
        }

        /// <summary>.daypick label: a day's chip (panel, radius 6, 12px text-2) with its box; 44 px tall on a touch screen.</summary>
        public static Border SettingsDay(string name, bool on, Action<bool> changed)
        {
            TextBlock t = Text(name, 12, "text-2");
            SettingsCheck c = new SettingsCheck(on, t);
            c.Root.Gap = 4;
            c.Changed += changed;
            Border b = new RoundBorder();
            b.Child = c.Root;
            b.CornerRadius = new CornerRadius(6);
            b.SetResourceReference(Border.BackgroundProperty, "panel");
            b.Padding = Css.T(3, 6);
            System.Windows.Automation.AutomationProperties.SetName(c.Root, name);
            U.OnWindowWidth(b, w => { bool k = Coarse(w); b.Padding = k ? Css.T(0, 10) : Css.T(3, 6); b.MinHeight = k ? 44 : 0; });
            return b;
        }

        // ---------------------------------------------------------------- select.input

        /// <summary>select.input: the chosen option and the chevron; a click opens the options (the browser's own list).</summary>
        public sealed class SettingsSelect
        {
            public readonly Border Root = new RoundBorder();
            readonly TextBlock label = new TextBlock();
            List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            public string Value = "";
            public event Action<string> Picked;

            public SettingsSelect(string name)
            {
                label.FontSize = 13.5;
                label.SetResourceReference(TextBlock.ForegroundProperty, "text");
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.VerticalAlignment = VerticalAlignment.Center;
                Path chevron = new Path();
                chevron.Data = Geometry.Parse("M0,0 L4.5,4.5 9,0");
                chevron.StrokeThickness = 1.6;
                chevron.SetResourceReference(Shape.StrokeProperty, "text");
                chevron.HorizontalAlignment = HorizontalAlignment.Right;
                chevron.VerticalAlignment = VerticalAlignment.Center;
                chevron.Margin = new Thickness(0, 0, -18, 0);
                chevron.IsHitTestVisible = false;
                Grid g = new Grid();
                g.Children.Add(label);
                g.Children.Add(chevron);
                Root.Child = g;
                Root.MinHeight = 34;
                Root.Padding = new Thickness(10, 6, 26, 6);
                Root.CornerRadius = new CornerRadius(6);
                Root.BorderThickness = new Thickness(1);
                Root.SetResourceReference(Border.BorderBrushProperty, "line");
                Root.SetResourceReference(Border.BackgroundProperty, "bg-2");
                System.Windows.Automation.AutomationProperties.SetName(Root, name);
                U.Click(Root, Open);
                Root.GotKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "violet"); };
                Root.LostKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "line"); };
                U.OnWindowWidth(Root, w => { bool c = Coarse(w); label.FontSize = c ? 16 : 13.5; Root.MinHeight = c ? 44 : 34; });
            }

            public void Options(List<KeyValuePair<string, string>> list, string value)
            {
                options = list;
                Show(value);
            }

            /// <summary>The option shown (an unknown value shows none, as the browser's).</summary>
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
                menu.Placement = PlacementMode.Bottom;
                menu.MinWidth = Root.ActualWidth;
                menu.IsOpen = true;
            }
        }

        // ---------------------------------------------------------------- .kv, .note, details.more

        /// <summary>dl.kv: the keys (muted, as wide as the widest) beside their values, gap 6 14, 13px; margin 8 0.</summary>
        public static Grid SettingsKv(List<KeyValuePair<string, UIElement>> rows)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock dt = Text(rows[i].Key, 13, "muted");
                dt.Margin = new Thickness(0, i > 0 ? 6 : 0, 14, 0);
                System.Windows.Controls.Grid.SetRow(dt, i);
                g.Children.Add(dt);
                FrameworkElement dd = rows[i].Value as FrameworkElement;
                if (dd == null) continue;
                dd.Margin = new Thickness(0, i > 0 ? 6 : 0, 0, 0);
                System.Windows.Controls.Grid.SetRow(dd, i);
                System.Windows.Controls.Grid.SetColumn(dd, 1);
                g.Children.Add(dd);
            }
            g.Margin = Css.T(8, 0);
            return g;
        }

        /// <summary>A dd's text (text colour, wrapping anywhere) at its size (13px; .mono 0.92em; .small 12.5px).</summary>
        public static TextBlock SettingsDd(string text, bool mono, double size)
        {
            TextBlock t = Text(Format.Breakable(text ?? ""), size, "text");
            if (mono) t.FontFamily = Mono;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        /// <summary>.note (.amber): padding 10 12, radius 8, panel-2 (amber-soft) with its border; margin 4 0 12.</summary>
        public static Border SettingsNote(UIElement child, bool amber)
        {
            Border b = new RoundBorder();
            b.Child = child;
            b.Padding = Css.T(10, 12);
            b.CornerRadius = new CornerRadius(8);
            b.BorderThickness = new Thickness(1);
            if (amber)
            {
                b.SetResourceReference(Border.BackgroundProperty, "amber-soft");
                b.BorderBrush = Css.Brush("rgba(240,183,47,.35)");
            }
            else
            {
                b.SetResourceReference(Border.BackgroundProperty, "panel-2");
                b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            }
            b.Margin = new Thickness(0, 4, 0, 12);
            return b;
        }

        /// <summary>
        /// details.more: the summary (muted, the disclosure triangle; on a touch
        /// screen 44 px tall and no triangle, display: flex) opens and closes a
        /// bulleted list (ul.small: gap 6, padding-left 18), each item from the
        /// page's HTML.
        /// </summary>
        public static Flex SettingsDetails(string summary, params string[] html)
        {
            TextBlock marker = Text("▶", 9, "muted");
            marker.VerticalAlignment = VerticalAlignment.Center;
            marker.Margin = new Thickness(0, 0, 6, 0);
            TextBlock t = Text(summary, 14, "muted");
            t.VerticalAlignment = VerticalAlignment.Center;
            Flex head = Flex.Row(0, marker, t);
            head.Background = Brushes.Transparent;
            head.HorizontalAlignment = HorizontalAlignment.Left;
            head.Margin = Css.T(8, 0);
            Flex list = Flex.Col(6);
            foreach (string h in html)
            {
                TextBlock dot = Text("•", 12.5, "text");
                dot.Width = 18;
                dot.TextAlignment = TextAlignment.Center;
                TextBlock line = SettingsRich(SentrySix.Core.Rich.FromHtml(h), 12.5, "text");
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(dot);
                System.Windows.Controls.Grid.SetColumn(line, 1);
                g.Children.Add(line);
                list.Children.Add(g);
            }
            list.Margin = new Thickness(0, 0, 0, 12);
            list.Visibility = Visibility.Collapsed;
            U.Click(head, () =>
            {
                bool open = list.Visibility != Visibility.Visible;
                list.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                marker.Text = open ? "▼" : "▶";
            });
            U.OnWindowWidth(head, w =>
            {
                bool c = Coarse(w);
                head.MinHeight = c ? 44 : 0;
                marker.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
            });
            System.Windows.Automation.AutomationProperties.SetName(head, summary);
            return Flex.Col(0, head, list);
        }

        // ---------------------------------------------------------------- inline pieces

        /// <summary>A paragraph from rich runs: bold, &lt;code&gt; as a small box (panel-2, line-soft border, radius 4, mono 0.92em), links cyan.</summary>
        public static TextBlock SettingsRich(Rich r, double size, string color)
        {
            TextBlock t = Text("", size, color);
            foreach (RichRun run in r.Runs)
            {
                switch (run.Kind)
                {
                    case RichKind.Bold: t.Inlines.Add(new Bold(new Run(run.Text))); break;
                    case RichKind.Code: t.Inlines.Add(SettingsCode(run.Text, size)); break;
                    case RichKind.Break: t.Inlines.Add(new LineBreak()); break;
                    default: t.Inlines.Add(new Run(run.Text)); break;
                }
            }
            return t;
        }

        /// <summary>code: mono 0.92em, panel-2, a line-soft border, padding 1 5, radius 4.</summary>
        public static InlineUIContainer SettingsCode(string text, double size)
        {
            TextBlock c = Text(text, size * 0.92, "text");
            c.FontFamily = Mono;
            Border b = new Border();
            b.Child = c;
            b.Padding = new Thickness(5, 1, 5, 1);
            b.CornerRadius = new CornerRadius(4);
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Border.BackgroundProperty, "panel-2");
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            InlineUIContainer i = new InlineUIContainer(b);
            i.BaselineAlignment = BaselineAlignment.Center;
            return i;
        }

        /// <summary>kbd: 11.5px text-2, panel-2, a line border (2 px under), radius 5, padding 1 6, at least 1.6em wide.</summary>
        public static InlineUIContainer SettingsKbd(string key)
        {
            TextBlock t = Text(key, 11.5, "text-2");
            t.FontFamily = Mono;
            t.TextAlignment = TextAlignment.Center;
            Border b = new Border();
            b.Child = t;
            b.MinWidth = 11.5 * 1.6;
            b.Padding = new Thickness(6, 1, 6, 1);
            b.CornerRadius = new CornerRadius(5);
            b.BorderThickness = new Thickness(1, 1, 1, 2);
            b.SetResourceReference(Border.BorderBrushProperty, "line");
            b.SetResourceReference(Border.BackgroundProperty, "panel-2");
            InlineUIContainer c = new InlineUIContainer(b);
            c.BaselineAlignment = BaselineAlignment.Center;
            return c;
        }

        static readonly Regex SettingsKbdRe = new Regex("<kbd>(.*?)</kbd>", RegexOptions.CultureInvariant);

        /// <summary>A line of the page's HTML with its &lt;kbd&gt; keys drawn as keys.</summary>
        public static TextBlock SettingsKeys(string html, double size, string color)
        {
            TextBlock t = Text("", size, color);
            int at = 0;
            foreach (Match m in SettingsKbdRe.Matches(html))
            {
                if (m.Index > at) t.Inlines.Add(new Run(System.Net.WebUtility.HtmlDecode(html.Substring(at, m.Index - at))));
                t.Inlines.Add(SettingsKbd(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)));
                at = m.Index + m.Length;
            }
            if (at < html.Length) t.Inlines.Add(new Run(System.Net.WebUtility.HtmlDecode(html.Substring(at))));
            return t;
        }

        /// <summary>a: cyan, underlined on hover.</summary>
        public static Hyperlink SettingsLink(string text, Action click, bool mono)
        {
            Hyperlink l = new Hyperlink(new Run(text));
            l.SetResourceReference(TextElement.ForegroundProperty, "cyan");
            if (mono) { l.FontFamily = Mono; }
            l.TextDecorations = null;
            l.MouseEnter += delegate { l.TextDecorations = TextDecorations.Underline; };
            l.MouseLeave += delegate { l.TextDecorations = null; };
            l.Click += delegate { click(); };
            return l;
        }

        // ---------------------------------------------------------------- input[type=time]

        /// <summary>
        /// input[type=time]: the value ("23:00") shown as the browser shows it
        /// on this PC (12-hour "11:00 PM" or 24-hour "23:00"), the clock glyph
        /// on the right; typed as either, read back as HH:mm.
        /// </summary>
        public sealed class SettingsTime
        {
            public readonly Grid Root = new Grid();
            readonly TextBox box;
            public string Value;
            public event Action<string> Changed;

            public SettingsTime(string value, string label)
            {
                Value = value ?? "";
                box = Input(Shown(Value), null, label);
                box.FontFamily = Mono;
                box.Padding = new Thickness(10, 0, 30, 0);
                Path clock = new Path();
                clock.Data = Geometry.Parse("M7,1 A6,6 0 1 1 6.99,1 Z M7,3.8 L7,7 9.2,8.4");
                clock.StrokeThickness = 1.4;
                clock.SetResourceReference(Shape.StrokeProperty, "text");
                clock.HorizontalAlignment = HorizontalAlignment.Right;
                clock.VerticalAlignment = VerticalAlignment.Center;
                clock.Margin = new Thickness(0, 0, 10, 0);
                clock.IsHitTestVisible = false;
                Root.Children.Add(box);
                Root.Children.Add(clock);
                box.GotKeyboardFocus += delegate { box.Text = Value; box.SelectAll(); };
                box.LostKeyboardFocus += delegate { Commit(); box.Text = Shown(Value); };
                // (Enter sends the form: the time typed counts first)
                box.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Commit(); };
            }

            void Commit()
            {
                string v = Parse(box.Text);
                if (v == null || v == Value) return;
                Value = v;
                Action<string> h = Changed;
                if (h != null) h(v);
            }

            static bool TwelveHour { get { return CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("h"); } }

            static string Shown(string v)
            {
                Match m = Regex.Match(v ?? "", @"^(\d{1,2}):(\d{2})$");
                if (!m.Success) return v ?? "";
                int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (!TwelveHour) return h.ToString("00", CultureInfo.InvariantCulture) + ":" + m.Groups[2].Value;
                return (h % 12 == 0 ? 12 : h % 12).ToString("00", CultureInfo.InvariantCulture) + ":" + m.Groups[2].Value + (h < 12 ? " AM" : " PM");
            }

            /// <summary>"11:00 PM", "11 pm", "23:00", "2300" as HH:mm; null when it is not a time.</summary>
            public static string Parse(string text)
            {
                Match m = Regex.Match((text ?? "").Trim(), @"^(\d{1,2})(?::?(\d{2}))?\s*([aApP])?\.?\s*[mM]?\.?$");
                if (!m.Success) return null;
                int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                if (m.Groups[3].Success)
                {
                    if (h < 1 || h > 12) return null;
                    bool pm = char.ToLowerInvariant(m.Groups[3].Value[0]) == 'p';
                    h = h % 12 + (pm ? 12 : 0);
                }
                if (h > 23 || min > 59) return null;
                return h.ToString("00", CultureInfo.InvariantCulture) + ":" + min.ToString("00", CultureInfo.InvariantCulture);
            }
        }
    }
}
