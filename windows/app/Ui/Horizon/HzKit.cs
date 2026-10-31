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

namespace SentrySix.Ui.Horizon
{
    /// <summary>The kinds of shell.css .btn: plain, .go, .danger, .ghost, .icon, .sm, .wide.</summary>
    [Flags]
    public enum HzBtn { Plain = 0, Go = 1, Danger = 2, Ghost = 4, Icon = 8, Sm = 16, Wide = 32 }

    /// <summary>
    /// Horizon's look for its pages (examples/ui-packs/horizon/web/shell.css and
    /// hz/app.css): the HUD panels, the page title strip, buttons, fields,
    /// segmented controls, pills, bars, the blank and loading boxes, notes,
    /// key-value lists. Every Horizon page is drawn from these, as every pack
    /// page is drawn from the same classes on the web.
    /// </summary>
    public static partial class HzKit
    {
        // ---------------------------------------------------------------- text

        public static TextBlock Text(string text, double size, string color, int weight = 400)
        {
            TextBlock t = U.Text(text, size, U.W(weight), null);
            Paint(t, TextBlock.ForegroundProperty, color);
            t.LineHeight = size * 1.5;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            return t;
        }

        /// <summary>A colour: a theme key ("dim", "cyan") or a CSS colour ("#ffd2ef", "rgba(…)").</summary>
        public static void Paint(DependencyObject e, DependencyProperty dp, string color)
        {
            if (color == null) return;
            if (color.StartsWith("#") || color.StartsWith("rgb")) e.SetValue(dp, Css.Brush(color));
            else
            {
                FrameworkElement fe = e as FrameworkElement;
                if (fe != null) fe.SetResourceReference(dp, color);
                else
                {
                    FrameworkContentElement fc = e as FrameworkContentElement;
                    if (fc != null) fc.SetResourceReference(dp, color);
                }
            }
        }

        /// <summary>.small (13.5px), .tiny (12.5px, 0.04em: drawn without the spacing), .dim, .faint, .mono, .num.</summary>
        public static TextBlock Small(string text, string color = null)
        {
            return Text(text, 13.5, color);
        }

        public static TextBlock Tiny(string text, string color = null)
        {
            return Text(text, 12.5, color);
        }

        public static TextBlock Mono(TextBlock t)
        {
            t.FontFamily = U.Consolas;
            t.FontSize = t.FontSize * 0.92;
            return t;
        }

        /// <summary>.wrap-any: overflow-wrap anywhere (long paths break inside words).</summary>
        public static TextBlock WrapAny(TextBlock t)
        {
            t.Text = Breakable(t.Text);
            return t;
        }

        /// <summary>Text with zero-width spaces after path separators and every 12 letters, so WPF can break it anywhere.</summary>
        public static string Breakable(string s)
        {
            return Format.Breakable(s ?? "");
        }

        /// <summary>A heading in chrome (h1.chrome): the gradient fill, uppercase, 800, 0.06em.</summary>
        public static SpacedText ChromeTitle(string text, double size)
        {
            SpacedText t = new SpacedText((text ?? "").ToUpperInvariant(), size, U.W(800), 0.06);
            t.Fill = Css.Linear(180, "#ffffff", 0, "#eef0ff", .36, "#a3a7d8", .49, "#ffd2ef", .53, "#ffffff", .86);
            return t;
        }

        // ---------------------------------------------------------------- panels

        /// <summary>.hud: glass, the neon outline and corner brackets (magenta, or "cyan" / "orange").</summary>
        public static Grid Hud(UIElement child, Thickness padding, string bracket = null)
        {
            string c = bracket == "cyan" ? "#3df0ff" : bracket == "orange" ? "#ff8a3d" : bracket;
            return SentrySix.Ui.Hud.Panel(child, padding, null, c);
        }

        /// <summary>.panel.hud: 16px 18px (14px under 1024).</summary>
        public static Grid Panel(UIElement child, string bracket = null)
        {
            Grid g = Hud(child, Css.T(16, 18), bracket);
            Border inner = (Border)g.Children[1];
            U.OnWindowWidth(g, w => inner.Padding = w <= 1023 ? Css.T(14) : Css.T(16, 18));
            return g;
        }

        /// <summary>
        /// section.ptitle.hud: the page's h1 in chrome (clamp(22px, 2.4vw, 30px)),
        /// its lead line (dim 14px, 75ch at most) and the actions on the right
        /// (wrapping under it on a narrow window).
        /// </summary>
        public static Grid PTitle(string title, TextBlock lead, params UIElement[] actions)
        {
            SpacedText h1 = ChromeTitle(title, 30);
            StackPanel left = new StackPanel();
            left.Children.Add(h1);
            if (lead != null)
            {
                lead.Margin = new Thickness(0, 4, 0, 0);
                lead.MaxWidth = 75 * 7.2;
                lead.HorizontalAlignment = HorizontalAlignment.Left;
                left.Children.Add(lead);
            }
            left.VerticalAlignment = VerticalAlignment.Bottom;
            Flex row = new Flex(Orientation.Horizontal, 16);
            row.Wrap = true;
            row.LineGap = 10;
            row.Justify = "between";
            row.Children.Add(left);
            if (actions != null && actions.Length > 0)
            {
                Flex acts = Flex.Row(8, actions);
                acts.Wrap = true;
                acts.LineGap = 8;
                acts.VerticalAlignment = VerticalAlignment.Bottom;
                row.Children.Add(acts);
            }
            Grid g = Hud(row, Css.T(14, 18));
            U.OnWindowWidth(g, w => h1.FontSize = Math.Max(22, Math.Min(0.024 * w, 30)));
            return g;
        }

        /// <summary>The lead line under a page title (dim 14px).</summary>
        public static TextBlock Lead(string text)
        {
            return Text(text, 14, "dim");
        }

        /// <summary>header.ph: h2 (13px 800 0.2em uppercase #ffd2ef, a cyan icon) and the extras on the right.</summary>
        public static Flex PanelHead(string title, string icon, params UIElement[] extra)
        {
            Flex h2 = Flex.Row(8);
            if (icon != null)
            {
                FrameworkElement i = HzIcons.Icon(icon, 20);
                i.SetResourceReference(TextElement.ForegroundProperty, "cyan");
                i.VerticalAlignment = VerticalAlignment.Center;
                h2.Children.Add(i);
            }
            SpacedText t = new SpacedText((title ?? "").ToUpperInvariant(), 13, U.W(800), 0.2);
            t.Foreground = Css.Brush("#ffd2ef");
            t.VerticalAlignment = VerticalAlignment.Center;
            h2.Children.Add(t);
            h2.VerticalAlignment = VerticalAlignment.Center;
            Flex ph = new Flex(Orientation.Horizontal, 10);
            ph.Wrap = true;
            ph.Justify = "between";
            ph.MinHeight = 32;
            ph.Margin = new Thickness(0, 0, 0, 12);
            ph.Children.Add(h2);
            if (extra != null && extra.Length > 0)
            {
                Flex x = Flex.Row(8, extra);
                x.Wrap = true;
                x.VerticalAlignment = VerticalAlignment.Center;
                ph.Children.Add(x);
            }
            return ph;
        }

        /// <summary>.row: a wrapping row, gap 8, items centred.</summary>
        public static Flex Row(params UIElement[] children)
        {
            Flex f = Flex.Row(8, children);
            f.Wrap = true;
            f.LineGap = 8;
            foreach (UIElement c in f.Children)
            {
                FrameworkElement fe = c as FrameworkElement;
                if (fe != null && fe.VerticalAlignment == VerticalAlignment.Stretch) fe.VerticalAlignment = VerticalAlignment.Center;
            }
            return f;
        }

        /// <summary>.grow: flex 1 1 200px (a field filling the rest of a row).</summary>
        public static T Grow<T>(T e) where T : FrameworkElement
        {
            Flex.SetGrow(e, 1);
            e.MinWidth = 200;
            return e;
        }

        /// <summary>.stack: a column, gap 12.</summary>
        public static Flex Stack(double gap, params UIElement[] children)
        {
            return Flex.Col(gap, children);
        }

        // ---------------------------------------------------------------- buttons

        public static BtnLook BtnK(HzBtn kind)
        {
            BtnLook b = new BtnLook();
            b.Bg = "btn-bg"; b.Border = Css.Brush("rgba(61,240,255,.5)"); b.Fg = "ink"; b.HoverBg = Css.Brush("rgba(61,240,255,.16)"); b.HoverBorder = "cyan";
            b.Radius = new CornerRadius(4); b.MinHeight = 44; b.MinWidth = 44; b.Padding = Css.T(0, 16); b.FontSize = 15; b.Weight = FontWeights.SemiBold;
            if ((kind & HzBtn.Go) != 0)
            {
                b.Bg = "go"; b.Border = Brushes.Transparent; b.Fg = "go-ink"; b.HoverBg = "go-hover"; b.HoverBorder = null;
                b.Shadow = Css.Glow(18, "rgba(255,63,180,.35)");
            }
            if ((kind & HzBtn.Danger) != 0)
            {
                b.Bg = Css.Brush("rgba(255,90,120,.1)"); b.Border = Css.Brush("rgba(255,90,120,.7)"); b.Fg = Css.Brush("#ffc2cf");
                b.HoverBg = Css.Brush("rgba(255,90,120,.2)"); b.HoverBorder = null;
            }
            if ((kind & HzBtn.Ghost) != 0)
            {
                b.Bg = Brushes.Transparent; b.Border = Brushes.Transparent; b.HoverBg = Css.Brush("rgba(255,255,255,.08)"); b.HoverBorder = null;
            }
            if ((kind & HzBtn.Icon) != 0) b.Padding = new Thickness(0);
            if ((kind & HzBtn.Sm) != 0) { b.FontSize = 14; b.Padding = Css.T(0, 12); }
            return b;
        }

        /// <summary>.btn with an icon (18px) and a label; null label for an icon button (44 x 44, the label as its tooltip).</summary>
        public static Button Btn(string label, string icon, HzBtn kind, Action click)
        {
            FrameworkElement content;
            if ((kind & HzBtn.Icon) != 0) content = HzIcons.Icon(icon ?? "horizon", 18);
            else content = U.IconLabel(icon != null ? HzIcons.Icon(icon, 18) : null, label, 8);
            Button b = U.Button(content, BtnK(kind));
            if ((kind & HzBtn.Icon) != 0) { b.Width = 44; if (label != null) { b.ToolTip = label; AutomationName(b, label); } }
            if ((kind & HzBtn.Wide) != 0) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            else b.HorizontalAlignment = HorizontalAlignment.Left;
            if (click != null) b.Click += delegate { click(); };
            return b;
        }

        public static void AutomationName(FrameworkElement e, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(e, name ?? "");
        }

        /// <summary>The button's label (the text of an icon-and-label button).</summary>
        public static void SetLabel(Button b, string label)
        {
            Flex f = b.Content as Flex;
            if (f == null) return;
            foreach (UIElement c in f.Children) { TextBlock t = c as TextBlock; if (t != null) t.Text = label; }
        }

        // ---------------------------------------------------------------- fields

        public static InputLook InK()
        {
            InputLook k = new InputLook();
            k.Bg = "in-bg"; k.Border = "in-border"; k.Focus = "cyan"; k.Fg = "ink"; k.Placeholder = "placeholder";
            k.Radius = 4; k.FontSize = 16; k.MinHeight = 44; k.Padding = new Thickness(12, 0, 12, 0);
            return k;
        }

        /// <summary>input.in (a text or search field).</summary>
        public static TextBox Input(string value, string placeholder)
        {
            TextBox t = (TextBox)U.Input(InK(), false, value, placeholder);
            if (placeholder != null) AutomationName(t, placeholder);
            return t;
        }

        /// <summary>label.fld: the label (14px 600 dim) over the field, gap 6.</summary>
        public static Flex Field(string label, UIElement field, string hint = null)
        {
            TextBlock l = Text(label, 14, "dim", 600);
            Flex f = Flex.Col(6, l, field);
            if (hint != null) f.Children.Add(Text(hint, 13, "faint"));
            return f;
        }

        // ---------------------------------------------------------------- segs, pills, bars

        /// <summary>
        /// .segs: a segmented control (role radiogroup). Pick(value) moves the
        /// "on" one, as onSegs does before calling back.
        /// </summary>
        public sealed class Segs
        {
            public readonly Border Root = new Border();
            readonly Flex row = new Flex(Orientation.Horizontal, 2);
            readonly List<KeyValuePair<string, Border>> items = new List<KeyValuePair<string, Border>>();
            public string Value;
            public event Action<string> Picked;

            public Segs(string label, string[][] options, string current)
            {
                row.Wrap = true;
                row.LineGap = 2;
                Root.Child = row;
                Root.Padding = new Thickness(2);
                Root.CornerRadius = new CornerRadius(4);
                Root.BorderThickness = new Thickness(1);
                Root.BorderBrush = Css.Brush("rgba(207,195,238,.28)");
                Root.Background = Css.Brush("rgba(6,3,22,.6)");
                Root.HorizontalAlignment = HorizontalAlignment.Left;
                Root.VerticalAlignment = VerticalAlignment.Center;
                AutomationName(Root, label);
                Value = current;
                foreach (string[] o in options)
                {
                    string v = o[0];
                    TextBlock t = new TextBlock();
                    t.Text = o[1];
                    t.FontSize = 14;
                    t.FontWeight = FontWeights.Bold;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.HorizontalAlignment = HorizontalAlignment.Center;
                    Border b = new Border();
                    b.Child = t;
                    b.MinHeight = 44;
                    b.MinWidth = 44;
                    b.Padding = Css.T(0, 12);
                    b.CornerRadius = new CornerRadius(3);
                    b.Uid = "seg-" + label + "-" + v;
                    U.Hover(b, on => PaintOne(b, v, on));
                    U.Click(b, () => Pick(v));
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

            /// <summary>Moves the "on" one without calling back.</summary>
            public void Show(string v)
            {
                Value = v;
                PaintAll();
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
                    b.Background = Css.Linear(100, "rgba(61,240,255,.24)", 0, "rgba(255,63,180,.26)", 1);
                    b.BorderBrush = Css.Brush("rgba(61,240,255,.55)");
                    b.BorderThickness = new Thickness(1);
                    b.Padding = Css.T(0, 11);
                    b.SetValue(TextElement.ForegroundProperty, Brushes.White);
                }
                else
                {
                    b.Background = hover ? Css.Brush("rgba(255,255,255,.06)") : Brushes.Transparent;
                    b.BorderThickness = new Thickness(0);
                    b.Padding = Css.T(0, 12);
                    b.SetResourceReference(TextElement.ForegroundProperty, hover ? "ink" : "dim");
                }
                System.Windows.Automation.AutomationProperties.SetItemStatus(b, on ? "checked" : "");
            }
        }

        /// <summary>.pill (tone cyan, mag, org, grn, red or ""): 12.5px 700, 999px round.</summary>
        public static Border Pill(string text, string tone, bool spin = false)
        {
            string fg = "dim", border = "rgba(207,195,238,.35)", bg = null;
            switch (tone ?? "")
            {
                case "cyan": fg = "#aef8ff"; border = "rgba(61,240,255,.6)"; bg = "rgba(61,240,255,.08)"; break;
                case "mag": fg = "#ffc0e6"; border = "rgba(255,63,180,.65)"; bg = "rgba(255,63,180,.1)"; break;
                case "org": fg = "#ffd0a8"; border = "rgba(255,138,61,.7)"; bg = "rgba(255,138,61,.1)"; break;
                case "grn": fg = "#b5ffd9"; border = "rgba(93,255,176,.6)"; bg = "rgba(93,255,176,.08)"; break;
                case "red": fg = "#ffc2cf"; border = "rgba(255,90,120,.7)"; bg = "rgba(255,90,120,.12)"; break;
            }
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = 12.5;
            t.FontWeight = FontWeights.Bold;
            t.VerticalAlignment = VerticalAlignment.Center;
            Flex row = Flex.Row(5);
            if (spin) row.Children.Add(Spinner(12, 2));
            row.Children.Add(t);
            Border b = new Border();
            b.Child = row;
            b.Padding = Css.T(2, 9);
            b.CornerRadius = new CornerRadius(999);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Css.Brush(border);
            if (bg != null) b.Background = Css.Brush(bg);
            Paint(b, TextElement.ForegroundProperty, fg);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        /// <summary>.spin: a ring with a cyan top, turning (still in pictures and with effects off).</summary>
        public static FrameworkElement Spinner(double size, double stroke)
        {
            Grid g = new Grid();
            g.Width = size;
            g.Height = size;
            Ellipse track = new Ellipse();
            track.Stroke = Css.Brush("rgba(61,240,255,.25)");
            track.StrokeThickness = stroke;
            g.Children.Add(track);
            System.Windows.Shapes.Path top = new System.Windows.Shapes.Path();
            double r = (size - stroke) / 2, c = size / 2;
            // border-top-color: the top quarter of the ring
            top.Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M{0},{1} A{2},{2} 0 0 1 {3},{4}", c - r * 0.7071, c - r * 0.7071, r, c + r * 0.7071, c - r * 0.7071));
            top.SetResourceReference(Shape.StrokeProperty, "cyan");
            top.StrokeThickness = stroke;
            g.Children.Add(top);
            g.RenderTransformOrigin = new Point(0.5, 0.5);
            RotateTransform rot = new RotateTransform();
            g.RenderTransform = rot;
            g.Loaded += delegate
            {
                if (Still) return;
                DoubleAnimation a = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                rot.BeginAnimation(RotateTransform.AngleProperty, a);
            };
            g.Unloaded += delegate { rot.BeginAnimation(RotateTransform.AngleProperty, null); };
            g.VerticalAlignment = VerticalAlignment.Center;
            return g;
        }

        /// <summary>Animations stand still (the render test mode, Windows' setting, reduce effects); set by the interface.</summary>
        public static bool Still;

        /// <summary>.bar (tone org, red, grn or "" for cyan to magenta): 8px, the fill p% wide.</summary>
        public static Border Bar(double p, string tone)
        {
            double pct = Format.Fin(p) ? Math.Max(0, Math.Min(100, p)) : 0;
            Brush fill;
            switch (tone ?? "")
            {
                case "org": fill = Css.Linear(90, "#ffe46b", 0, "#ff8a3d", 1); break;
                case "red": fill = Css.Linear(90, "#ff8a3d", 0, "#ff5a78", 1); break;
                case "grn": fill = Css.Linear(90, "#2fd6a0", 0, "#5dffb0", 1); break;
                default: fill = Css.Linear(90, "#3df0ff", 0, "#ff3fb4", 1); break;
            }
            Border i = new Border();
            i.Background = fill;
            i.CornerRadius = new CornerRadius(4);
            i.HorizontalAlignment = HorizontalAlignment.Left;
            Grid track = new Grid();
            track.Children.Add(i);
            Border b = new Border();
            b.Height = 8;
            b.MinWidth = 40;
            b.CornerRadius = new CornerRadius(4);
            b.Background = Css.Brush("rgba(255,255,255,.08)");
            b.ClipToBounds = true;
            b.Child = track;
            b.SizeChanged += delegate { i.Width = Math.Round(b.ActualWidth * Math.Round(pct * 10) / 1000, 2); };
            return b;
        }

        // ---------------------------------------------------------------- blank, loading, notes

        /// <summary>.blank: the Horizon mark (40px, magenta), the text, and what goes under it.</summary>
        public static Flex Blank(string text, params UIElement[] extra)
        {
            FrameworkElement i = HzIcons.Icon("horizon", 40);
            i.SetResourceReference(TextElement.ForegroundProperty, "mag");
            i.Opacity = 0.9;
            i.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock p = Text(text, 15, "dim");
            p.TextAlignment = TextAlignment.Center;
            p.HorizontalAlignment = HorizontalAlignment.Center;
            Flex f = Flex.Col(12, i, p);
            foreach (UIElement x in extra ?? new UIElement[0])
            {
                FrameworkElement fe = x as FrameworkElement;
                if (fe != null) fe.HorizontalAlignment = HorizontalAlignment.Center;
                f.Children.Add(x);
            }
            f.Margin = Css.T(28, 16);
            return f;
        }

        /// <summary>.loading: three dots (cyan, magenta, orange) pulsing; 60px above and below.</summary>
        public static FrameworkElement Loading()
        {
            Flex f = Flex.Row(8);
            f.HorizontalAlignment = HorizontalAlignment.Center;
            f.Margin = Css.T(60, 0);
            string[] colors = { "#3df0ff", "#ff3fb4", "#ff8a3d" };
            for (int k = 0; k < 3; k++)
            {
                Ellipse e = U.Dot(10, Css.Brush(colors[k]));
                ScaleTransform s = new ScaleTransform(0.6, 0.6);
                e.RenderTransformOrigin = new Point(0.5, 0.5);
                e.RenderTransform = s;
                e.Opacity = 0.5;
                int n = k;
                e.Loaded += delegate
                {
                    if (Still) return;
                    DoubleAnimationUsingKeyFrames a = new DoubleAnimationUsingKeyFrames();
                    a.Duration = TimeSpan.FromSeconds(1);
                    a.BeginTime = TimeSpan.FromSeconds(0.15 * n);
                    a.RepeatBehavior = RepeatBehavior.Forever;
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromPercent(0)));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.5), new SineEase()));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromPercent(1), new SineEase()));
                    s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                };
                f.Children.Add(e);
            }
            AutomationName(f, "Loading");
            return f;
        }

        /// <summary>main.js render()'s catch: div.blank.hud with the message and "Try again".</summary>
        public static FrameworkElement LoadFailed(string message, Action retry)
        {
            TextBlock p = Text(message, 15, "dim");
            p.TextAlignment = TextAlignment.Center;
            p.HorizontalAlignment = HorizontalAlignment.Center;
            Button again = Btn("Try again", "refresh", HzBtn.Plain, retry);
            again.HorizontalAlignment = HorizontalAlignment.Center;
            again.Uid = "retry";
            Flex f = Flex.Col(12, p, again);
            return Hud(f, Css.T(28, 16));
        }

        /// <summary>.note (tone org, red, grn or "" for cyan): a 3px bar on the left, 14px.</summary>
        public static Border Note(UIElement content, string tone)
        {
            string bar = "#3df0ff", bg = "rgba(61,240,255,.07)", fg = "dim";
            switch (tone ?? "")
            {
                case "org": bar = "#ff8a3d"; bg = "rgba(255,138,61,.08)"; fg = "#ffe3c7"; break;
                case "red": bar = "#ff5a78"; bg = "rgba(255,90,120,.1)"; fg = "#ffd6de"; break;
                case "grn": bar = "#5dffb0"; bg = "rgba(93,255,176,.07)"; fg = "#d6ffeb"; break;
            }
            Border b = new Border();
            b.Child = content;
            b.Padding = Css.T(10, 12);
            b.BorderThickness = new Thickness(3, 0, 0, 0);
            b.BorderBrush = Css.Brush(bar);
            b.Background = Css.Brush(bg);
            b.CornerRadius = new CornerRadius(0, 4, 4, 0);
            Paint(b, TextElement.ForegroundProperty, fg);
            TextElement.SetFontSize(b, 14);
            return b;
        }

        /// <summary>A paragraph from rich runs (bold, code), as the web's innerHTML.</summary>
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
                            Run c = new Run(run.Text);
                            c.FontFamily = U.Consolas;
                            c.FontSize = size * 0.92;
                            c.Background = Css.Brush("rgba(61,240,255,.08)");
                            c.Foreground = Css.Brush("#d8fbff");
                            t.Inlines.Add(c);
                            break;
                        }
                    case RichKind.Break: t.Inlines.Add(new LineBreak()); break;
                    case RichKind.Mono: { Run m = new Run(run.Text); m.FontFamily = U.Consolas; t.Inlines.Add(m); break; }
                    default: t.Inlines.Add(new Run(run.Text)); break;
                }
            }
            return t;
        }

        /// <summary>
        /// dl.kv: the terms (faint) in a column as wide as the widest, the
        /// values (overflow-wrap anywhere) beside them; gap 6 x 14, 14px.
        /// </summary>
        public static Grid Kv(IList<KeyValuePair<string, UIElement>> rows)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock dt = Text(rows[i].Key, 14, "faint");
                dt.Margin = new Thickness(0, i == 0 ? 0 : 6, 14, 0);
                Grid.SetRow(dt, i);
                g.Children.Add(dt);
                FrameworkElement dd = rows[i].Value as FrameworkElement ?? new TextBlock();
                dd.Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0);
                Grid.SetRow(dd, i);
                Grid.SetColumn(dd, 1);
                g.Children.Add(dd);
            }
            TextElement.SetFontSize(g, 14);
            return g;
        }

        /// <summary>A row of the pit log, Recycle Bin or alerts list: rgba(3,1,12,.5), the soft line, radius 4.</summary>
        public static Border ListItem(UIElement child, Thickness padding)
        {
            Border b = new Border();
            b.Child = child;
            b.Padding = padding;
            b.CornerRadius = new CornerRadius(4);
            b.Background = Css.Brush("rgba(3,1,12,.5)");
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            b.BorderThickness = new Thickness(1);
            return b;
        }

        /// <summary>A coloured dot with its glow (the pit lamp, the alert dot).</summary>
        public static Ellipse Lamp(double size, string color, double glow)
        {
            Ellipse e = U.Dot(size, Css.Brush(color));
            if (glow > 0) e.Effect = Css.Glow(glow, color);
            e.VerticalAlignment = VerticalAlignment.Center;
            return e;
        }

        /// <summary>Puts children into a grid's cells (column, row, span).</summary>
        public static T Cell<T>(T e, int col, int row, int span = 1) where T : UIElement
        {
            Grid.SetColumn(e, col);
            Grid.SetRow(e, row);
            if (span > 1) Grid.SetColumnSpan(e, span);
            return e;
        }
    }
}
