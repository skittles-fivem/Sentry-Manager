using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Daylight
{
    /// <summary>The kinds of shell.css .btn: plain, .btn-primary, .btn-soft, .btn-danger, .btn-ghost, .btn-sm, .btn-block.</summary>
    [Flags]
    public enum DlBtn { Plain = 0, Primary = 1, Soft = 2, Danger = 4, Ghost = 8, Sm = 16, Block = 32 }

    /// <summary>
    /// Daylight's look for its pages (examples/ui-packs/daylight/web/shell.css
    /// and dl/app.css): the page head, cards, buttons, the icon button,
    /// fields, the select, the segmented control, chips, notes, meters,
    /// lists, the empty and loading boxes, the router's error box. Every
    /// colour (and shadow) is a key of the theme (DaylightInterface's palette,
    /// light or dark), so a page follows a change of look as it is drawn.
    /// The phone rules are dl/app.css's (max-width: 959px), <see cref="Phone"/>;
    /// Daylight has no pointer: coarse rules.
    /// </summary>
    public static partial class DlKit
    {
        /// <summary>dl/app.css @media (max-width: 959px): the phone layout.</summary>
        public static bool Phone(double width) { return width <= 959; }

        /// <summary>Animations stand still (render mode, Windows' setting, reduce effects: html.calm).</summary>
        public static bool Still;

        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        /// <summary>body's line-height (1.55); headings 1.2.</summary>
        public const double Body = 1.55, Heading = 1.2;

        /// <summary>A text in a theme colour (or "#rrggbb"), at a CSS weight, its lines <paramref name="lh"/> × the size apart.</summary>
        public static TextBlock Text(string text, double size, string color, int weight = 400, double lh = Body)
        {
            TextBlock t = U.Text(text, size, U.W(weight), null);
            Paint(t, TextBlock.ForegroundProperty, color);
            LineHeight(t, size, lh);
            return t;
        }

        public static void LineHeight(TextBlock t, double size, double lh)
        {
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.LineHeight = Math.Round(size * lh, 2);
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

        /// <summary>icon(name) at .ic's 22 px (or the given size), in the current colour.</summary>
        public static FrameworkElement Icon(string name, double size = 22)
        {
            return DlIcons.Icon(name, size);
        }

        /// <summary>The theme is dark (DaylightInterface's palette "dark").</summary>
        public static bool Dark(FrameworkElement e)
        {
            object d = e != null ? e.TryFindResource("dark") : null;
            return d is bool && (bool)d;
        }

        // ---------------------------------------------------------------- the page

        /// <summary>
        /// .page-head: the h1 (clamp(28px, 4vw, 38px) 700; 28 on a phone) and
        /// .lede (muted 18px, 16.5 on a phone, 62ch, 8 under the h1) on the
        /// left, .head-actions (gap 10, wrapping) at the bottom right; the two
        /// wrap onto lines 16 apart; margin-bottom 26 (18 on a phone).
        /// </summary>
        public static Flex PageHead(string title, string lede, params UIElement[] actions)
        {
            StackPanel left = new StackPanel();
            TextBlock h1 = Text(title, 38, "text", 700, Heading);
            left.Children.Add(h1);
            TextBlock p = null;
            if (lede != null)
            {
                p = Text(lede, 18, "muted");
                p.Margin = new Thickness(0, 8, 0, 0);
                p.HorizontalAlignment = HorizontalAlignment.Left;
                left.Children.Add(p);
            }
            left.VerticalAlignment = VerticalAlignment.Bottom;
            Flex row = new Flex(Orientation.Horizontal, 16);
            row.Wrap = true;
            row.LineGap = 16;
            row.Justify = "between";
            row.Children.Add(left);
            Flex a = null;
            if (actions != null && actions.Length > 0)
            {
                a = Flex.Row(10, actions);
                a.Wrap = true;
                a.LineGap = 10;
                a.VerticalAlignment = VerticalAlignment.Bottom;
                foreach (UIElement x in actions) { FrameworkElement fe = x as FrameworkElement; if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center; }
                row.Children.Add(a);
            }
            U.OnWindowWidth(row, w =>
            {
                bool phone = Phone(w);
                h1.FontSize = phone ? 28 : Math.Max(28, Math.Min(0.04 * w, 38));
                LineHeight(h1, h1.FontSize, Heading);
                if (p != null)
                {
                    p.FontSize = phone ? 16.5 : 18;
                    LineHeight(p, p.FontSize, Body);
                    p.MaxWidth = 62 * 0.55 * p.FontSize;
                }
                row.Margin = new Thickness(0, 0, 0, phone ? 18 : 26);
            });
            return row;
        }

        /// <summary>h2 (22px 650, line-height 1.2), with the margin under it the page gives.</summary>
        public static TextBlock H2(string text, double below = 0)
        {
            TextBlock t = Text(text, 22, "text", 650, Heading);
            t.Margin = new Thickness(0, 0, 0, below);
            return t;
        }

        /// <summary>h3 (18px 650).</summary>
        public static TextBlock H3(string text)
        {
            return Text(text, 18, "text", 650, Heading);
        }

        /// <summary>.card: surface, the border, radius 18, shadow-1, padding 24 (a phone: radius 16, padding 18).</summary>
        public static Grid Card(UIElement child)
        {
            Border sh = new RoundBorder();
            sh.CornerRadius = new CornerRadius(18);
            sh.SetResourceReference(Border.BackgroundProperty, "surface");
            sh.SetResourceReference(UIElement.EffectProperty, "shadow-1");
            Border b = new RoundBorder();
            b.Child = child;
            b.SetResourceReference(Border.BackgroundProperty, "surface");
            b.SetResourceReference(Border.BorderBrushProperty, "border");
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(18);
            b.Padding = new Thickness(24);
            Grid g = new Grid();
            g.Children.Add(sh);
            g.Children.Add(b);
            U.OnWindowWidth(g, w =>
            {
                bool phone = Phone(w);
                b.Padding = new Thickness(phone ? 18 : 24);
                sh.CornerRadius = b.CornerRadius = new CornerRadius(phone ? 16 : 18);
            });
            return g;
        }

        /// <summary>The card's own box (its padding and child) inside <see cref="Card"/>.</summary>
        public static Border CardBox(Grid card)
        {
            return (Border)card.Children[1];
        }

        // ---------------------------------------------------------------- buttons

        public static BtnLook BtnK(DlBtn kind)
        {
            BtnLook b = new BtnLook();
            b.Bg = "surface"; b.Border = "border-strong"; b.Fg = "text"; b.HoverBg = "surface-2";
            b.Radius = new CornerRadius(999); b.MinHeight = 46; b.Padding = Css.T(0, 18); b.FontSize = 16; b.Weight = U.W(600);
            b.DisabledOpacity = 0.5;
            if ((kind & DlBtn.Primary) != 0) { b.Bg = "accent"; b.Border = "accent"; b.Fg = "accent-ink"; b.HoverBg = "accent-hover"; b.HoverBorder = "accent-hover"; b.Shadow = Css.Shadow(6, 16, "rgba(13,107,107,.45)"); }
            if ((kind & DlBtn.Soft) != 0) { b.Bg = "accent-soft"; b.Border = Brushes.Transparent; b.Fg = "accent-soft-ink"; b.HoverBg = null; }
            if ((kind & DlBtn.Danger) != 0) { b.Bg = "bad"; b.Border = "bad"; b.Fg = "bad-ink"; b.HoverBg = null; }
            if ((kind & DlBtn.Ghost) != 0) { b.Bg = Brushes.Transparent; b.Border = Brushes.Transparent; b.Fg = "accent"; b.HoverBg = "accent-soft"; }
            if ((kind & DlBtn.Sm) != 0) { b.MinHeight = 44; b.Padding = Css.T(0, 14); b.FontSize = 15; }
            return b;
        }

        /// <summary>.btn with an optional 20 px icon (gap 8); one line (white-space: nowrap).</summary>
        public static Button Btn(string label, string icon, DlBtn kind, Action click)
        {
            FrameworkElement content = U.IconLabel(icon != null ? Icon(icon, 20) : null, label, 8);
            Button b = U.Button(content, BtnK(kind));
            b.HorizontalAlignment = (kind & DlBtn.Block) != 0 ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            if (label != null) System.Windows.Automation.AutomationProperties.SetName(b, label);
            if (click != null) b.Click += delegate { click(); };
            return b;
        }

        /// <summary>.icon-btn: 44 × 44, round, transparent, text-2; hover surface-2; the label is its tooltip.</summary>
        public static Button IconBtn(string icon, string label, Action click, double iconSize = 22)
        {
            BtnLook il = new BtnLook();
            il.Bg = Brushes.Transparent; il.Border = Brushes.Transparent; il.Fg = "text-2"; il.HoverBg = "surface-2";
            il.Radius = new CornerRadius(22); il.Padding = new Thickness(0); il.MinHeight = 44; il.MinWidth = 44; il.BorderWidth = 0;
            FrameworkElement ic = Icon(icon, iconSize);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            ic.VerticalAlignment = VerticalAlignment.Center;
            Button b = U.Button(ic, il);
            b.Width = 44; b.Height = 44;
            if (label != null) { b.ToolTip = label; System.Windows.Automation.AutomationProperties.SetName(b, label); }
            if (click != null) b.Click += delegate { click(); };
            return b;
        }

        // ---------------------------------------------------------------- fields

        /// <summary>.input: surface, border-strong, radius 12, 16.5px, min-height 48, padding 11 14; focus: the accent border.</summary>
        public static InputLook InK()
        {
            InputLook k = new InputLook();
            k.Bg = "surface"; k.Border = "border-strong"; k.Focus = "accent"; k.Fg = "text"; k.Placeholder = "placeholder";
            k.Radius = 12; k.FontSize = 16.5; k.MinHeight = 48; k.Padding = new Thickness(14, 0, 14, 0);
            return k;
        }

        public static TextBox Input(string value, string placeholder, string label)
        {
            TextBox t = (TextBox)U.Input(InK(), false, value, placeholder);
            System.Windows.Automation.AutomationProperties.SetName(t, label ?? placeholder ?? "");
            return t;
        }

        /// <summary>.field: the label (15.5px 600, text-2) over the control, 6 apart; an optional hint (14px muted).</summary>
        public static Flex Field(string label, UIElement control, string hint = null)
        {
            Flex f = Flex.Col(6, Text(label, 15.5, "text-2", 600), control);
            if (hint != null) f.Children.Add(Text(hint, 14, "muted"));
            return f;
        }

        /// <summary>
        /// select.input (the vehicle picker's: radius 999, 600, min-height 44,
        /// padding-right 36, at least 180 wide): the chosen option and the
        /// browser's arrow; a click (or Enter, Space, Alt+↓) opens the options,
        /// ↑ and ↓ pick the one before or after.
        /// </summary>
        public sealed class Select
        {
            public readonly Border Root = new RoundBorder();
            readonly TextBlock label;
            readonly List<string[]> options = new List<string[]>();
            ContextMenu menu;
            public string Value;
            public event Action<string> Changed;

            /// <summary>options: { value, label }.</summary>
            public Select(IList<string[]> opts, string value, string name, bool pill)
            {
                foreach (string[] o in opts) options.Add(o);
                Value = value;
                label = Text("", 16.5, "text", pill ? 600 : 400, 1.4);
                label.TextWrapping = TextWrapping.NoWrap;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.VerticalAlignment = VerticalAlignment.Center;
                Path arrow = new Path();
                arrow.Data = Geometry.Parse("M0,0 L5,5 L10,0");
                arrow.StrokeThickness = 1.6;
                arrow.StrokeStartLineCap = arrow.StrokeEndLineCap = PenLineCap.Round;
                arrow.StrokeLineJoin = PenLineJoin.Round;
                arrow.SetResourceReference(Shape.StrokeProperty, "text");
                arrow.HorizontalAlignment = HorizontalAlignment.Right;
                arrow.VerticalAlignment = VerticalAlignment.Center;
                arrow.Margin = new Thickness(0, 0, -22, 0);
                Grid g = new Grid();
                g.Children.Add(label);
                g.Children.Add(arrow);
                Root.Child = g;
                Root.SetResourceReference(Border.BackgroundProperty, "surface");
                Root.SetResourceReference(Border.BorderBrushProperty, "border-strong");
                Root.BorderThickness = new Thickness(1);
                Root.CornerRadius = new CornerRadius(pill ? 999 : 12);
                Root.Padding = new Thickness(14, 0, 36, 0);
                Root.MinHeight = pill ? 44 : 48;
                Root.MinWidth = pill ? 180 : 0;
                Root.Focusable = true;
                Root.FocusVisualStyle = null;
                Root.Cursor = Cursors.Hand;
                Root.HorizontalAlignment = HorizontalAlignment.Left;
                System.Windows.Automation.AutomationProperties.SetName(Root, name ?? "");
                FocusRing.Set(Root, 2, pill ? 22 : 12, true);
                Root.GotKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "accent"); };
                Root.LostKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "border-strong"); };
                U.Click(Root, () => { Root.Focus(); Open(); });
                Root.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                    if (k == Key.Enter || k == Key.Space || (k == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)) { Open(); e.Handled = true; }
                    else if (k == Key.Up || k == Key.Down)
                    {
                        int i = Index();
                        int n = Math.Max(0, Math.Min(options.Count - 1, i + (k == Key.Down ? 1 : -1)));
                        if (n != i && options.Count > 0) Pick(options[n][0]);
                        e.Handled = true;
                    }
                };
                Paint();
            }

            int Index()
            {
                for (int i = 0; i < options.Count; i++) if (options[i][0] == Value) return i;
                return 0;
            }

            /// <summary>The options are open (a picker in use).</summary>
            public bool IsOpen { get { return menu != null && menu.IsOpen; } }

            void Paint()
            {
                label.Text = options.Count > 0 ? options[Index()][1] : "";
            }

            public void Pick(string v)
            {
                if (v == Value) return;
                Value = v;
                Paint();
                Action<string> h = Changed;
                if (h != null) h(v);
            }

            void Open()
            {
                menu = new ContextMenu();
                menu.SetResourceReference(Control.BackgroundProperty, "surface");
                menu.SetResourceReference(Control.ForegroundProperty, "text");
                menu.SetResourceReference(Control.BorderBrushProperty, "border-strong");
                foreach (string[] o in options)
                {
                    MenuItem item = new MenuItem();
                    item.Header = o[1];
                    item.IsCheckable = true;
                    item.IsChecked = o[0] == Value;
                    item.FontSize = 16.5;
                    string v = o[0];
                    item.Click += delegate { Pick(v); Root.Focus(); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = Root;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.MinWidth = Root.ActualWidth;
                menu.IsOpen = true;
            }
        }

        /// <summary>
        /// state.js vehiclePicker({ allowAll, selected }): nothing without
        /// vehicles; one vehicle (and no "All vehicles") is .vehicle-solo, a
        /// chip with its name; else the select (All vehicles first when allowed).
        /// </summary>
        public static UIElement VehiclePicker(IList<KeyValuePair<string, string>> vehicles, bool allowAll, string selected, out Select select)
        {
            select = null;
            if (vehicles == null || vehicles.Count == 0) return null;
            if (vehicles.Count == 1 && !allowAll)
            {
                // .vehicle-solo.chip: the grey chip (text-2), 650
                return Chip(vehicles[0].Value, "grey", null);
            }
            List<string[]> opts = new List<string[]>();
            if (allowAll) opts.Add(new[] { "", "All vehicles" });
            foreach (KeyValuePair<string, string> v in vehicles) opts.Add(new[] { v.Key, v.Value });
            select = new Select(opts, selected ?? "", "Vehicle", true);
            return select.Root;
        }

        // ---------------------------------------------------------------- the segmented control

        /// <summary>.seg: a pill row of buttons (aria-pressed: surface, accent, shadow-1); padding 4, gap 2, 15px 600.</summary>
        public sealed class Seg
        {
            public readonly Border Root = new RoundBorder();
            readonly List<KeyValuePair<string, Border>> items = new List<KeyValuePair<string, Border>>();
            public string Value;
            public event Action<string> Picked;

            public Seg(string[][] options, string value, string label)
            {
                Flex row = new Flex(Orientation.Horizontal, 2);
                row.Wrap = true;
                row.LineGap = 2;
                Root.Child = row;
                Root.Padding = new Thickness(4);
                Root.CornerRadius = new CornerRadius(999);
                Root.SetResourceReference(Border.BackgroundProperty, "surface-2");
                Root.HorizontalAlignment = HorizontalAlignment.Left;
                System.Windows.Automation.AutomationProperties.SetName(Root, label ?? "");
                Value = value;
                foreach (string[] o in options)
                {
                    string v = o[0];
                    TextBlock t = Text(o[1], 15, null, 600, 1.2);
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.HorizontalAlignment = HorizontalAlignment.Center;
                    Border b = new RoundBorder();
                    b.Child = t;
                    b.MinHeight = 44;
                    b.MinWidth = 44;
                    b.Padding = Css.T(8, 16);
                    b.CornerRadius = new CornerRadius(999);
                    b.Focusable = true;
                    b.FocusVisualStyle = null;
                    b.Cursor = Cursors.Hand;
                    FocusRing.Set(b, 2, 22);
                    U.Click(b, () => Pick(v));
                    b.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { Pick(v); e.Handled = true; } };
                    items.Add(new KeyValuePair<string, Border>(v, b));
                    row.Children.Add(b);
                }
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
                foreach (KeyValuePair<string, Border> kv in items)
                {
                    bool on = kv.Key == Value;
                    Border b = kv.Value;
                    if (on) { b.SetResourceReference(Border.BackgroundProperty, "surface"); b.SetResourceReference(UIElement.EffectProperty, "shadow-1"); }
                    else { b.Background = Brushes.Transparent; b.Effect = null; }
                    b.SetResourceReference(TextElement.ForegroundProperty, on ? "accent" : "text-2");
                    System.Windows.Automation.AutomationProperties.SetItemStatus(b, on ? "pressed" : "");
                }
            }
        }

        // ---------------------------------------------------------------- chips, notes, meters, spinner

        /// <summary>chip(text, tone, icon): 13.5px 650, padding 4 11, round; the icon 15 px, 6 apart. Tones: teal, amber/warn, violet, good, bad, grey.</summary>
        public static Border Chip(string text, string tone, string icon)
        {
            string bg = "surface-2", fg = "text-2";
            switch (tone ?? "")
            {
                case "teal": bg = "accent-soft"; fg = "accent-soft-ink"; break;
                case "amber":
                case "warn": bg = "warn-soft"; fg = "warn"; break;
                case "violet": bg = "violet-soft"; fg = "violet"; break;
                case "good": bg = "good-soft"; fg = "good"; break;
                case "bad": bg = "bad-soft"; fg = "bad"; break;
            }
            Flex row = Flex.Row(6);
            if (!string.IsNullOrEmpty(icon)) { FrameworkElement ic = Icon(icon, 15); ic.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(ic); }
            TextBlock t = Text(text ?? "", 13.5, null, 650, Body);
            t.TextWrapping = TextWrapping.NoWrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            row.VerticalAlignment = VerticalAlignment.Center;
            Border b = new RoundBorder();
            b.Child = row;
            b.Padding = Css.T(4, 11);
            b.CornerRadius = new CornerRadius(999);
            b.SetResourceReference(Border.BackgroundProperty, bg);
            b.SetResourceReference(TextElement.ForegroundProperty, fg);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        /// <summary>.note (good, warn, bad, info or ""): radius 12, padding 14 16, the icon and the words 12 apart.</summary>
        public static Border Note(string icon, UIElement body, string tone)
        {
            string bg = "surface-2", fg = "text-2", ic = null;
            switch (tone ?? "")
            {
                case "good": bg = "good-soft"; fg = "text"; ic = "good"; break;
                case "warn": bg = "warn-soft"; fg = "text"; ic = "warn"; break;
                case "bad": bg = "bad-soft"; fg = "text"; ic = "bad"; break;
                case "info": bg = "accent-soft"; fg = "text"; ic = "accent"; break;
            }
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (icon != null)
            {
                FrameworkElement i = Icon(icon);
                i.VerticalAlignment = VerticalAlignment.Top;
                i.Margin = new Thickness(0, 1, 12, 0);
                if (ic != null) i.SetResourceReference(TextElement.ForegroundProperty, ic);
                g.Children.Add(i);
            }
            Grid.SetColumn(body, 1);
            g.Children.Add(body);
            Border b = new RoundBorder();
            b.Child = g;
            b.Padding = Css.T(14, 16);
            b.CornerRadius = new CornerRadius(12);
            b.SetResourceReference(Border.BackgroundProperty, bg);
            b.SetResourceReference(TextElement.ForegroundProperty, fg);
            return b;
        }

        /// <summary>meter(fraction, tone): 10 px high (.big 14), surface-3, the accent part (warn, bad).</summary>
        public static Grid Meter(double fraction, string tone, bool big = false)
        {
            double v = Math.Max(0, Math.Min(1, Format.Fin(fraction) ? fraction : 0));
            Grid g = new Grid();
            g.Height = big ? 14 : 10;
            Border track = new RoundBorder();
            track.CornerRadius = new CornerRadius(999);
            track.SetResourceReference(Border.BackgroundProperty, "surface-3");
            Border fill = new RoundBorder();
            fill.CornerRadius = new CornerRadius(999);
            fill.SetResourceReference(Border.BackgroundProperty, tone == "warn" ? "warn" : tone == "bad" ? "bad" : "accent");
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(track);
            g.Children.Add(fill);
            g.ClipToBounds = true;
            g.SizeChanged += delegate { fill.Width = Math.Round(g.ActualWidth * v * 1000) / 1000; };
            return g;
        }

        /// <summary>.spin: a 22 px ring (3 px, surface-3) with the accent on top, turning every 0.9 s.</summary>
        public static FrameworkElement Spinner(double size = 22)
        {
            Grid c = new Grid();
            c.Width = size;
            c.Height = size;
            Ellipse ring = new Ellipse();
            ring.StrokeThickness = 3;
            ring.SetResourceReference(Shape.StrokeProperty, "surface-3");
            Path top = new Path();
            double r = (size - 3) / 2, m = size / 2;
            // border-top-color: the top quarter of the ring, from 45° left to 45° right
            double d = r * Math.Sqrt(0.5);
            top.Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M{0},{1} A{2},{2} 0 0 1 {3},{1}", m - d, m - d, r, m + d));
            top.StrokeThickness = 3;
            top.SetResourceReference(Shape.StrokeProperty, "accent");
            c.Children.Add(ring);
            c.Children.Add(top);
            c.RenderTransformOrigin = new Point(0.5, 0.5);
            RotateTransform rot = new RotateTransform();
            c.RenderTransform = rot;
            c.Loaded += delegate
            {
                if (Still) return;
                DoubleAnimation a = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                rot.BeginAnimation(RotateTransform.AngleProperty, a);
            };
            c.VerticalAlignment = VerticalAlignment.Center;
            return c;
        }

        // ---------------------------------------------------------------- lists, rows

        /// <summary>.row-between: wrapping, gap 10, the ends apart, centred.</summary>
        public static Flex RowBetween(params UIElement[] children)
        {
            Flex f = Flex.Row(10, children);
            f.Wrap = true;
            f.LineGap = 10;
            f.Justify = "between";
            foreach (UIElement x in children) { FrameworkElement fe = x as FrameworkElement; if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center; }
            return f;
        }

        /// <summary>ul.list of .list-item rows: 16 above and below each, a border line between (none above the first).</summary>
        public static StackPanel List(IList<UIElement> items)
        {
            StackPanel s = new StackPanel();
            for (int i = 0; i < items.Count; i++)
            {
                Border b = new Border();
                b.Child = items[i];
                b.Padding = Css.T(16, 0);
                b.BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0);
                b.SetResourceReference(Border.BorderBrushProperty, "border");
                s.Children.Add(b);
            }
            return s;
        }

        // ---------------------------------------------------------------- empty, loading, failed

        /// <summary>
        /// emptyState({ art, title, text, action }): a dashed 2 px border
        /// (radius 18) on surface at 60 %, padding 44 20; the 76 px art tile
        /// (accent-soft, the accent icon at 38), h3 21px, the muted words (46ch,
        /// 8 under), the action 18 under.
        /// </summary>
        public static Grid Empty(string art, string title, string text, UIElement action = null)
        {
            StackPanel s = new StackPanel();
            Border tile = new RoundBorder();
            tile.Width = 76; tile.Height = 76;
            tile.CornerRadius = new CornerRadius(26);
            tile.SetResourceReference(Border.BackgroundProperty, "accent-soft");
            tile.SetResourceReference(TextElement.ForegroundProperty, "accent");
            FrameworkElement ai = Icon(art ?? "sparkle", 38);
            ai.HorizontalAlignment = HorizontalAlignment.Center;
            ai.VerticalAlignment = VerticalAlignment.Center;
            tile.Child = ai;
            tile.Margin = new Thickness(0, 0, 0, 16);
            tile.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(tile);
            TextBlock h3 = Text(title, 21, "text", 650, Heading);
            h3.TextAlignment = TextAlignment.Center;
            s.Children.Add(h3);
            if (!string.IsNullOrEmpty(text))
            {
                TextBlock p = Text(text, 17, "muted");
                p.TextAlignment = TextAlignment.Center;
                p.MaxWidth = 46 * 0.55 * 17;
                p.Margin = new Thickness(0, 8, 0, 0);
                p.HorizontalAlignment = HorizontalAlignment.Center;
                s.Children.Add(p);
            }
            if (action != null)
            {
                FrameworkElement fe = action as FrameworkElement;
                if (fe != null) { fe.Margin = new Thickness(0, 18, 0, 0); fe.HorizontalAlignment = HorizontalAlignment.Center; }
                s.Children.Add(action);
            }
            s.Margin = Css.T(44, 20);
            Rectangle dash = new Rectangle();
            dash.RadiusX = dash.RadiusY = 18;
            dash.StrokeThickness = 2;
            dash.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
            dash.SetResourceReference(Shape.StrokeProperty, "border");
            Border fill = new RoundBorder();
            fill.CornerRadius = new CornerRadius(18);
            fill.SetResourceReference(Border.BackgroundProperty, "surface");
            fill.Opacity = 0.6;
            Grid box = new Grid();
            box.Children.Add(fill);
            box.Children.Add(dash);
            box.Children.Add(s);
            return box;
        }

        /// <summary>loading(text): the spinner and the words (muted), 12 apart; padding 28 4.</summary>
        public static FrameworkElement Loading(string text)
        {
            TextBlock t = Text(text ?? "Loading…", 17, "muted");
            t.VerticalAlignment = VerticalAlignment.Center;
            Flex f = Flex.Row(12, Spinner(), t);
            f.Margin = Css.T(28, 4);
            System.Windows.Automation.AutomationProperties.SetName(f, text ?? "Loading…");
            return f;
        }

        /// <summary>
        /// While a page loads: the web's router keeps the page before on screen
        /// until the new one has drawn (no loading box of its own), so nothing.
        /// </summary>
        public static FrameworkElement Pending()
        {
            Border b = new Border();
            b.MinHeight = 1;
            System.Windows.Automation.AutomationProperties.SetName(b, "Loading");
            return b;
        }

        /// <summary>app.js render()'s catch: .note.note-bad with the alert sign and the message, then "Try again" 16 under it.</summary>
        public static FrameworkElement LoadFailed(string message, Action retry)
        {
            TextBlock t = Text(message, 17, null);
            StackPanel s = new StackPanel();
            s.Children.Add(Note("alert", t, "bad"));
            Button again = Btn("Try again", null, DlBtn.Plain, retry);
            again.Uid = "retry";
            again.Margin = new Thickness(0, 16, 0, 0);
            s.Children.Add(again);
            return s;
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
                    case RichKind.Code:
                        {
                            Run c = new Run(" " + run.Text + " ");
                            c.FontFamily = Mono;
                            c.FontSize = size * 0.88;
                            c.SetResourceReference(TextElement.BackgroundProperty, "surface-2");
                            t.Inlines.Add(c);
                            break;
                        }
                    case RichKind.Break: t.Inlines.Add(new LineBreak()); break;
                    default: t.Inlines.Add(new Run(run.Text)); break;
                }
            }
            return t;
        }

        /// <summary>A text with an icon in its line (the icon on the baseline, as an inline svg), then a space and the words.</summary>
        public static TextBlock IconText(string icon, string text, double size, string color, double iconSize = 22)
        {
            TextBlock t = Text("", size, color);
            FrameworkElement ic = Icon(icon, iconSize);
            InlineUIContainer box = new InlineUIContainer(ic);
            box.BaselineAlignment = BaselineAlignment.Baseline;
            t.Inlines.Add(box);
            t.Inlines.Add(new Run(" " + (text ?? "")));
            // (the icon on the baseline makes its line taller, as the web's line box grows round it)
            t.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            return t;
        }
    }
}
