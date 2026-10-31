using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Flux
{
    /// <summary>The kinds of shell.css .btn: plain, .primary, .cyan, .danger, .ghost, .sm, .icon.</summary>
    [Flags]
    public enum FxBtn { Plain = 0, Primary = 1, Cyan = 2, Danger = 4, Ghost = 8, Sm = 16, Icon = 32 }

    /// <summary>
    /// Flux's look for its pages (examples/ui-packs/flux/web/shell.css and
    /// flux.css): the page head, cards, the toolbar, buttons, fields, the
    /// segmented control, chips, meters, the empty and loading boxes, data
    /// tables (rows as labelled blocks at 860 px and less). The phone pictures
    /// are taken with a touch screen (pointer: coarse: 44 px controls, 16 px
    /// fields); here that is a window 600 px wide or less (<see cref="Coarse"/>).
    /// </summary>
    public static partial class FxKit
    {
        /// <summary>pointer: coarse, as the phone captures have it.</summary>
        public static bool Coarse(double width) { return width <= 600; }

        /// <summary>Animations stand still (render mode, Windows' setting, reduce effects).</summary>
        public static bool Still;

        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        public static TextBlock Text(string text, double size, string color, int weight = 400)
        {
            TextBlock t = U.Text(text, size, U.W(weight), null);
            Paint(t, TextBlock.ForegroundProperty, color);
            return t;
        }

        public static void Paint(DependencyObject e, DependencyProperty dp, string color)
        {
            if (color == null) return;
            if (color.StartsWith("#") || color.StartsWith("rgb")) e.SetValue(dp, Css.Brush(color));
            else
            {
                FrameworkElement fe = e as FrameworkElement;
                if (fe != null) fe.SetResourceReference(dp, color);
            }
        }

        /// <summary>.phead: h1 (22px 650) and its sub line (muted small), the actions on the right; margin-bottom 16.</summary>
        public static Flex PHead(string title, TextBlock sub, params UIElement[] actions)
        {
            StackPanel left = new StackPanel();
            TextBlock h1 = Text(title, 22, "text", 650);
            left.Children.Add(h1);
            if (sub != null) { sub.Margin = new Thickness(0, 4, 0, 0); left.Children.Add(sub); }
            Flex row = new Flex(Orientation.Horizontal, 16);
            row.Wrap = true;
            row.LineGap = 16;
            row.Justify = "between";
            row.Margin = new Thickness(0, 0, 0, 16);
            left.VerticalAlignment = VerticalAlignment.Bottom;
            row.Children.Add(left);
            // flux.css at 860 px: h1 19px
            U.OnWindowWidth(h1, w => h1.FontSize = w <= 860 ? 19 : 22);
            if (actions != null && actions.Length > 0)
            {
                Flex a = Flex.Row(8, actions);
                a.Wrap = true;
                a.LineGap = 8;
                a.VerticalAlignment = VerticalAlignment.Bottom;
                row.Children.Add(a);
            }
            return row;
        }

        /// <summary>A muted small line (p.muted.small): 12.5px.</summary>
        public static TextBlock Sub(string text) { return Text(text, 12.5, "muted"); }

        /// <summary>.toolbar: a wrapping row, gap 8, margin-bottom 12; .grow children fill the line.</summary>
        public static Flex Toolbar(params UIElement[] children)
        {
            Flex f = Flex.Row(8, children);
            f.Wrap = true;
            f.LineGap = 8;
            f.GrowOnLines = true;
            f.Margin = new Thickness(0, 0, 0, 12);
            return f;
        }

        /// <summary>.grow in a toolbar: flex 1 1 200px.</summary>
        public static T Grow<T>(T e) where T : FrameworkElement
        {
            Flex.SetGrow(e, 1);
            e.MinWidth = 200;
            return e;
        }

        /// <summary>.card (.flush: no padding): panel, the soft line, radius 10, padding 14.</summary>
        public static Border Card(UIElement child, bool flush)
        {
            Border b = new RoundBorder();
            b.Child = child;
            b.SetResourceReference(Border.BackgroundProperty, "panel");
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(10);
            b.Padding = flush ? new Thickness(0) : new Thickness(14);
            b.ClipToBounds = flush;
            return b;
        }

        // ---------------------------------------------------------------- buttons

        public static BtnLook BtnK(FxBtn kind, bool coarse)
        {
            BtnLook b = new BtnLook();
            b.Bg = "panel-2"; b.Border = "line"; b.Fg = "text"; b.HoverBg = "panel-3"; b.HoverBorder = Css.Brush("#4a4d5e");
            b.Radius = new CornerRadius(6); b.MinHeight = 32; b.Padding = Css.T(0, 12); b.FontSize = 13; b.Weight = U.W(500);
            if ((kind & FxBtn.Primary) != 0) { b.Bg = "primary"; b.Border = Brushes.Transparent; b.Fg = "white"; b.HoverBg = null; b.HoverBorder = null; b.Shadow = Css.Shadow(4, 18, "rgba(145,93,255,.5)"); }
            if ((kind & FxBtn.Cyan) != 0) { b.Bg = "cyan-soft"; b.Border = Css.Brush("rgba(68,225,249,.45)"); b.Fg = "cyan"; }
            if ((kind & FxBtn.Danger) != 0) { b.Bg = "red-soft"; b.Border = Css.Brush("rgba(255,105,109,.5)"); b.Fg = Css.Brush("#ffbcb8"); }
            if ((kind & FxBtn.Ghost) != 0) { b.Bg = Brushes.Transparent; b.Border = Brushes.Transparent; b.HoverBg = "panel-2"; b.HoverBorder = null; }
            if ((kind & FxBtn.Sm) != 0) { b.MinHeight = 26; b.Padding = Css.T(0, 9); b.FontSize = 12; }
            if ((kind & FxBtn.Icon) != 0) b.Padding = new Thickness(0);
            if (coarse) { b.MinHeight = 44; b.MinWidth = 44; }
            return b;
        }

        /// <summary>.btn with an optional 16px icon; an .icon button has the label as its tooltip.</summary>
        public static Button Btn(string label, string icon, FxBtn kind, Action click)
        {
            bool isIcon = (kind & FxBtn.Icon) != 0;
            FrameworkElement content = isIcon ? FxIcons.Icon(icon ?? "x") : U.IconLabel(icon != null ? FxIcons.Icon(icon) : null, label, 7);
            Button b = U.Button(content, BtnK(kind, false));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            if (isIcon) { b.Width = (kind & FxBtn.Sm) != 0 ? 26 : 32; if (label != null) { b.ToolTip = label; System.Windows.Automation.AutomationProperties.SetName(b, label); } }
            if (click != null) b.Click += delegate { click(); };
            FxBtn k = kind;
            U.OnWindowWidth(b, w =>
            {
                bool c = Coarse(w);
                b.MinHeight = c ? 44 : (k & FxBtn.Sm) != 0 ? 26 : 32;
                b.MinWidth = c ? 44 : 0;
                if (isIcon) b.Width = c ? 44 : (k & FxBtn.Sm) != 0 ? 26 : 32;
            });
            return b;
        }

        // ---------------------------------------------------------------- fields

        public static InputLook InK(bool coarse)
        {
            InputLook k = new InputLook();
            k.Bg = "bg-2"; k.Border = "line"; k.Focus = "violet"; k.Fg = "text"; k.Placeholder = "faint";
            k.Radius = 6; k.FontSize = coarse ? 16 : 13.5; k.MinHeight = coarse ? 44 : 34; k.Padding = new Thickness(10, 0, 10, 0);
            return k;
        }

        /// <summary>input.input.</summary>
        public static TextBox Input(string value, string placeholder, string label)
        {
            TextBox t = (TextBox)U.Input(InK(false), false, value, placeholder);
            System.Windows.Automation.AutomationProperties.SetName(t, label ?? placeholder ?? "");
            U.OnWindowWidth(t, w => { bool c = Coarse(w); t.FontSize = c ? 16 : 13.5; t.MinHeight = c ? 44 : 34; });
            return t;
        }

        // ---------------------------------------------------------------- the segmented control

        /// <summary>.seg: buttons in a pill row (ui.js seg(), wireSeg(): click, ← and →).</summary>
        public sealed class Seg
        {
            public readonly Border Root = new RoundBorder();
            readonly Flex row = new Flex(Orientation.Horizontal, 2);
            readonly List<KeyValuePair<string, Border>> items = new List<KeyValuePair<string, Border>>();
            public string Value;
            public event Action<string> Picked;

            public Seg(string name, string[][] options, string value, string label)
            {
                row.Wrap = true;
                row.LineGap = 2;
                Root.Child = row;
                Root.Padding = new Thickness(2);
                Root.CornerRadius = new CornerRadius(8);
                Root.BorderThickness = new Thickness(1);
                Root.SetResourceReference(Border.BorderBrushProperty, "line");
                Root.SetResourceReference(Border.BackgroundProperty, "bg-2");
                Root.HorizontalAlignment = HorizontalAlignment.Left;
                Root.VerticalAlignment = VerticalAlignment.Center;
                System.Windows.Automation.AutomationProperties.SetName(Root, label ?? name);
                Value = value;
                foreach (string[] o in options)
                {
                    string v = o[0];
                    TextBlock t = new TextBlock();
                    t.Text = o[1];
                    t.FontSize = 12.5;
                    t.FontWeight = U.W(500);
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.HorizontalAlignment = HorizontalAlignment.Center;
                    Border b = new RoundBorder();
                    b.Child = t;
                    b.Height = 26;
                    b.Padding = Css.T(0, 10);
                    b.CornerRadius = new CornerRadius(6);
                    b.Uid = "seg-" + name + "-" + v;
                    U.Hover(b, on => PaintOne(b, v, on));
                    U.Click(b, () => Pick(v));
                    b.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
                    {
                        if (e.Key != System.Windows.Input.Key.Left && e.Key != System.Windows.Input.Key.Right) return;
                        int i = 0;
                        for (int k = 0; k < items.Count; k++) if (items[k].Key == Value) i = k;
                        KeyValuePair<string, Border> n = items[(i + (e.Key == System.Windows.Input.Key.Right ? 1 : -1) + items.Count) % items.Count];
                        n.Value.Focus();
                        Pick(n.Key);
                        e.Handled = true;
                    };
                    items.Add(new KeyValuePair<string, Border>(v, b));
                    row.Children.Add(b);
                }
                U.OnWindowWidth(Root, w =>
                {
                    bool c = Coarse(w);
                    foreach (KeyValuePair<string, Border> kv in items)
                    {
                        kv.Value.Height = c ? double.NaN : 26;
                        kv.Value.MinHeight = c ? 44 : 0;
                        kv.Value.MinWidth = c ? 44 : 0;
                        kv.Value.Padding = Css.T(0, c ? 12 : 10);
                    }
                });
                PaintAll();
            }

            public void Pick(string v)
            {
                Value = v;
                PaintAll();
                Action<string> h = Picked;
                if (h != null) h(v);
            }

            void PaintAll()
            {
                foreach (KeyValuePair<string, Border> kv in items) PaintOne(kv.Value, kv.Key, kv.Value.IsMouseOver);
            }

            void PaintOne(Border b, string v, bool hover)
            {
                bool on = v == Value;
                if (on)
                {
                    b.SetResourceReference(Border.BackgroundProperty, "panel-3");
                    b.SetResourceReference(Border.BorderBrushProperty, "line");
                    b.BorderThickness = new Thickness(1);
                    b.Effect = Css.Shadow(1, 6, "rgba(145,93,255,.35)");
                }
                else
                {
                    b.Background = Brushes.Transparent;
                    b.BorderThickness = new Thickness(0);
                    b.Effect = null;
                }
                b.SetResourceReference(TextElement.ForegroundProperty, on || hover ? "text" : "muted");
                System.Windows.Automation.AutomationProperties.SetItemStatus(b, on ? "checked" : "");
            }
        }

        // ---------------------------------------------------------------- chips, spinner, meter

        /// <summary>.chip (green, red, amber, cyan, violet or ""): 21px, 11.5px 500, 99px round.</summary>
        public static Border Chip(string text, string tone, bool spin = false)
        {
            string bg = "panel-3", fg = "text-2";
            switch (tone ?? "")
            {
                case "green": bg = "green-soft"; fg = "green"; break;
                case "red": bg = "red-soft"; fg = "red"; break;
                case "amber": bg = "amber-soft"; fg = "amber"; break;
                case "cyan": bg = "cyan-soft"; fg = "cyan"; break;
                case "violet": bg = "violet-soft"; fg = "violet"; break;
            }
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = 11.5;
            t.FontWeight = U.W(500);
            t.VerticalAlignment = VerticalAlignment.Center;
            Flex row = Flex.Row(5);
            if (spin) row.Children.Add(Spinner());
            row.Children.Add(t);
            row.VerticalAlignment = VerticalAlignment.Center;
            Border b = new RoundBorder();
            b.Child = row;
            b.Height = 21;
            b.Padding = Css.T(0, 8);
            b.CornerRadius = new CornerRadius(99);
            b.SetResourceReference(Border.BackgroundProperty, bg);
            b.SetResourceReference(TextElement.ForegroundProperty, fg);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        /// <summary>.spin: a 9px ring in the current colour, its right quarter open, turning.</summary>
        public static FrameworkElement Spinner()
        {
            System.Windows.Shapes.Path p = new System.Windows.Shapes.Path();
            p.Data = Geometry.Parse("M4.5,1 A3.5,3.5 0 1 0 8,4.5");
            p.StrokeThickness = 2;
            p.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("(TextElement.Foreground)") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.Self) });
            Canvas c = new Canvas();
            c.Width = 9;
            c.Height = 9;
            c.Children.Add(p);
            c.RenderTransformOrigin = new Point(0.5, 0.5);
            RotateTransform rot = new RotateTransform();
            c.RenderTransform = rot;
            c.Loaded += delegate
            {
                if (Still) return;
                DoubleAnimation a = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.8)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                rot.BeginAnimation(RotateTransform.AngleProperty, a);
            };
            c.VerticalAlignment = VerticalAlignment.Center;
            return c;
        }

        // ---------------------------------------------------------------- empty, loading

        /// <summary>.empty: the ∅ glyph (32px mono, violet, .6), the text (muted), what goes under it; padding 40 16.</summary>
        public static Flex Empty(string text, params UIElement[] extra)
        {
            TextBlock g = Text("∅", 32, "violet");
            g.FontFamily = Mono;
            g.Opacity = 0.6;
            g.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock p = Text(text, 13.5, "muted");
            p.TextAlignment = TextAlignment.Center;
            p.HorizontalAlignment = HorizontalAlignment.Center;
            Flex f = Flex.Col(8, g, p);
            foreach (UIElement x in extra ?? new UIElement[0])
            {
                FrameworkElement fe = x as FrameworkElement;
                if (fe != null) fe.HorizontalAlignment = HorizontalAlignment.Center;
                f.Children.Add(x);
            }
            f.Margin = Css.T(40, 16);
            return f;
        }

        /// <summary>.loading: three 7px dots (violet, cyan, violet) bouncing; padding 48.</summary>
        public static FrameworkElement Loading()
        {
            Flex f = Flex.Row(6);
            f.HorizontalAlignment = HorizontalAlignment.Center;
            f.Margin = new Thickness(48);
            string[] colors = { "violet", "cyan", "violet" };
            for (int k = 0; k < 3; k++)
            {
                Ellipse e = U.Dot(7, colors[k]);
                TranslateTransform tr = new TranslateTransform();
                e.RenderTransform = tr;
                int n = k;
                e.Loaded += delegate
                {
                    if (Still) return;
                    DoubleAnimationUsingKeyFrames a = new DoubleAnimationUsingKeyFrames();
                    a.Duration = TimeSpan.FromSeconds(1);
                    a.BeginTime = TimeSpan.FromSeconds(0.12 * n);
                    a.RepeatBehavior = RepeatBehavior.Forever;
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(-5, KeyTime.FromPercent(0.4), new SineEase()));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.8), new SineEase()));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                    tr.BeginAnimation(TranslateTransform.YProperty, a);
                };
                f.Children.Add(e);
            }
            System.Windows.Automation.AutomationProperties.SetName(f, "Loading");
            return f;
        }

        /// <summary>main.js render()'s catch: .empty with "!" and the message, and Retry.</summary>
        public static FrameworkElement LoadFailed(string message, Action retry)
        {
            Flex f = Empty(message, Btn("Retry", null, FxBtn.Plain, retry));
            TextBlock glyph = (TextBlock)f.Children[0];
            glyph.Text = "!";
            ((FrameworkElement)f.Children[2]).Uid = "retry";
            return f;
        }

        /// <summary>A paragraph from rich runs (bold, code).</summary>
        public static TextBlock Rich(Rich r, double size, string color)
        {
            TextBlock t = Text("", size, color);
            foreach (RichRun run in r.Runs)
            {
                switch (run.Kind)
                {
                    case RichKind.Bold: t.Inlines.Add(new Bold(new Run(run.Text))); break;
                    case RichKind.Code: { Run c = new Run(run.Text); c.FontFamily = Mono; c.FontSize = size * 0.92; t.Inlines.Add(c); break; }
                    case RichKind.Break: t.Inlines.Add(new LineBreak()); break;
                    default: t.Inlines.Add(new Run(run.Text)); break;
                }
            }
            return t;
        }

        // ---------------------------------------------------------------- tables

        /// <summary>A column of a .dtable: its head (lower case as written; drawn upper case) and whether it is .r.</summary>
        public sealed class Col
        {
            public string Head;
            public bool Right;
            /// <summary>Star width (share of the room left), or 0 for as wide as its widest cell.</summary>
            public double Star;

            public Col(string head, bool right, double star)
            {
                Head = head;
                Right = right;
                Star = star;
            }
        }

        /// <summary>
        /// table.dtable.stack: the header (mono 10.5px 600 uppercase, faint),
        /// rows with a soft line under each; at 860 px and less each row is a
        /// block of "LABEL … value" lines (the column's name before each cell).
        /// rows[i][c] is the cell's content (null for an empty cell).
        /// </summary>
        public static FrameworkElement Table(IList<Col> cols, IList<UIElement[]> rows, UIElement emptyRow)
        {
            ContentControl host = new ContentControl();
            host.Focusable = false;
            host.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            double last = -1;
            Action<double> draw = w =>
            {
                bool stack = w <= 860;
                bool coarse = Coarse(w);
                if (last >= 0 && (last <= 860) == stack && Coarse(last) == coarse) return;
                last = w;
                foreach (UIElement[] r in rows) foreach (UIElement c in r) Detach(c);
                Detach(emptyRow);
                host.Content = stack ? Stacked(cols, rows, emptyRow, coarse) : Grid(cols, rows, emptyRow, coarse);
            };
            U.OnWindowWidth(host, draw);
            host.Content = Grid(cols, rows, emptyRow, false);
            return host;
        }

        static void Detach(UIElement e)
        {
            if (e == null) return;
            FrameworkElement fe = e as FrameworkElement;
            if (fe == null) return;
            Panel p = fe.Parent as Panel;
            if (p != null) p.Children.Remove(fe);
            Border b = fe.Parent as Border;
            if (b != null) b.Child = null;
        }

        static TextBlock Th(string text)
        {
            TextBlock t = Text(text.ToUpperInvariant(), 10.5, "faint", 600);
            t.FontFamily = Mono;
            return t;
        }

        static FrameworkElement Grid(IList<Col> cols, IList<UIElement[]> rows, UIElement emptyRow, bool coarse)
        {
            Grid g = new Grid();
            foreach (Col c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Star > 0 ? new GridLength(c.Star, GridUnitType.Star) : GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < cols.Count; i++)
            {
                Border th = new Border();
                th.Child = Th(cols[i].Head);
                th.Padding = new Thickness(8);
                th.BorderThickness = new Thickness(0, 0, 0, 1);
                th.SetResourceReference(Border.BorderBrushProperty, "line");
                th.SetResourceReference(Border.BackgroundProperty, "panel");
                ((TextBlock)th.Child).TextAlignment = cols[i].Right ? TextAlignment.Right : TextAlignment.Left;
                System.Windows.Controls.Grid.SetColumn(th, i);
                g.Children.Add(th);
            }
            int row = 1;
            if (rows.Count == 0 && emptyRow != null)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Border td = Cell(emptyRow, false, coarse);
                System.Windows.Controls.Grid.SetRow(td, 1);
                System.Windows.Controls.Grid.SetColumnSpan(td, cols.Count);
                g.Children.Add(td);
            }
            foreach (UIElement[] r in rows)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int i = 0; i < cols.Count; i++)
                {
                    Border td = Cell(i < r.Length ? r[i] : null, cols[i].Right, coarse);
                    System.Windows.Controls.Grid.SetRow(td, row);
                    System.Windows.Controls.Grid.SetColumn(td, i);
                    g.Children.Add(td);
                }
                row++;
            }
            TextElement.SetFontSize(g, 12.5);
            return g;
        }

        static Border Cell(UIElement content, bool right, bool coarse)
        {
            Border td = new Border();
            td.Child = content;
            td.Padding = coarse ? new Thickness(8) : Css.T(6, 8);
            td.BorderThickness = new Thickness(0, 0, 0, 1);
            td.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            FrameworkElement fe = content as FrameworkElement;
            if (fe != null)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                if (right) fe.HorizontalAlignment = HorizontalAlignment.Right;
            }
            return td;
        }

        static FrameworkElement Stacked(IList<Col> cols, IList<UIElement[]> rows, UIElement emptyRow, bool coarse)
        {
            StackPanel s = new StackPanel();
            if (rows.Count == 0 && emptyRow != null)
            {
                Border only = new Border();
                only.Child = emptyRow;
                only.Padding = Css.T(8, 0);
                only.BorderThickness = new Thickness(0, 0, 0, 1);
                only.SetResourceReference(Border.BorderBrushProperty, "line");
                s.Children.Add(only);
            }
            foreach (UIElement[] r in rows)
            {
                StackPanel tr = new StackPanel();
                for (int i = 0; i < cols.Count; i++)
                {
                    UIElement content = i < r.Length ? r[i] : null;
                    Grid line = new Grid();
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    TextBlock label = Text(cols[i].Head.ToUpperInvariant(), 10, "faint", 600);
                    label.FontFamily = Mono;
                    label.VerticalAlignment = VerticalAlignment.Center;
                    line.Children.Add(label);
                    if (content != null)
                    {
                        FrameworkElement fe = content as FrameworkElement;
                        if (fe != null) { fe.HorizontalAlignment = HorizontalAlignment.Right; fe.Margin = new Thickness(12, 0, 0, 0); }
                        TextBlock tb = content as TextBlock;
                        if (tb != null) tb.TextAlignment = TextAlignment.Right;
                        System.Windows.Controls.Grid.SetColumn(content, 1);
                        line.Children.Add(content);
                    }
                    Border td = new Border();
                    td.Child = line;
                    td.Padding = coarse ? new Thickness(8) : Css.T(4, 8);
                    tr.Children.Add(td);
                }
                Border b = new Border();
                b.Child = tr;
                b.Padding = Css.T(8, 0);
                b.BorderThickness = new Thickness(0, 0, 0, 1);
                b.SetResourceReference(Border.BorderBrushProperty, "line");
                s.Children.Add(b);
            }
            TextElement.SetFontSize(s, 12.5);
            return s;
        }
    }
}
