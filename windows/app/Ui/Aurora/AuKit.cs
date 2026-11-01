using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SentrySix.Core;
using SentrySix.Core.Aurora;

namespace SentrySix.Ui.Aurora
{
    /// <summary>The kinds of shell.css .btn: plain, .primary, .danger, .ghost, .sm, .icon.</summary>
    [Flags]
    public enum AuBtn { Plain = 0, Primary = 1, Danger = 2, Ghost = 4, Sm = 8, Icon = 16 }

    /// <summary>
    /// Aurora's look for its pages (examples/ui-packs/aurora/web/shell.css and
    /// aurora/app.css): text at the body's line height (1.5), the glass
    /// window's parts (<see cref="AuWindow"/>), buttons, fields, the vehicle
    /// select of a title bar, chips, panels, the empty orb, the loading ring
    /// and main.js's "Something went wrong" box. Colours are the theme keys of
    /// AuroraInterface.Theme() (ink, ink-2, ink-3, line, line-2, accent, …) or
    /// CSS colours. Phones are 699 px and less (<see cref="Phone"/>). The
    /// pack has no pointer: coarse rules for its pages (its controls are
    /// 44 px everywhere); its (hover: none) rules are the dock's.
    /// Page helpers add their own members in AuKit.&lt;Page&gt;.cs files.
    /// </summary>
    public static partial class AuKit
    {
        /// <summary>app.css @media (max-width: 699px).</summary>
        public static bool Phone(double width) { return width <= 699; }

        /// <summary>Animations stand still (render mode, Windows' setting, reduce effects).</summary>
        public static bool Still;

        public static readonly FontFamily Mono = U.Cascadia;

        /// <summary>A text in a colour (theme key or CSS colour), the body's line-height 1.5, wrapping.</summary>
        public static TextBlock Text(string text, double size, string color, int weight = 400)
        {
            TextBlock t = U.Text(text, size, U.W(weight), null);
            Paint(t, TextBlock.ForegroundProperty, color);
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.LineHeight = size * 1.5;
            return t;
        }

        /// <summary>A heading (h1–h4: line-height 1.15, letter-spacing -0.015em is too fine to draw).</summary>
        public static TextBlock Heading(string text, double size, int weight = 400, string color = "ink")
        {
            TextBlock t = Text(text, size, color, weight);
            t.LineHeight = size * 1.15;
            return t;
        }

        /// <summary>A colour from a theme key ("ink-3") or CSS ("#9ff0e6", "rgba(…)"); null leaves it inherited.</summary>
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

        /// <summary>p.muted (ink-3), 15px unless told.</summary>
        public static TextBlock Muted(string text, double size = 15) { return Text(text, size, "ink-3"); }

        /// <summary>An icon of the sprite in a colour (null: the current one).</summary>
        public static FrameworkElement Icon(string name, double size, string color = null)
        {
            FrameworkElement i = AuIcons.Icon(name, size);
            if (color != null) Paint(i, TextElement.ForegroundProperty, color);
            i.VerticalAlignment = VerticalAlignment.Center;
            return i;
        }

        /// <summary>
        /// .glass: a translucent panel with the hairline border, the lit top
        /// edge (inset 0 1px 0 rgba(255,255,255,.16)) and the float shadow; no
        /// backdrop blur in WPF. bg: a theme key, a CSS colour or a Brush.
        /// The returned grid's last child is the bordered box holding child.
        /// </summary>
        public static Grid Glass(UIElement child, CornerRadius r, Thickness pad, object bg)
        {
            Border shadow = new RoundBorder();
            shadow.CornerRadius = r;
            SetBg(shadow, bg ?? "glass");
            shadow.Effect = Css.Shadow(24, 60, "rgba(0,0,0,.45)");
            Border lit = new RoundBorder();
            lit.CornerRadius = r;
            lit.BorderThickness = new Thickness(0, 1, 0, 0);
            lit.BorderBrush = Css.Brush("rgba(255,255,255,.16)");
            lit.Margin = new Thickness(1, 1, 1, 0);
            lit.IsHitTestVisible = false;
            Border edge = new RoundBorder();
            edge.CornerRadius = r;
            edge.BorderThickness = new Thickness(1);
            edge.SetResourceReference(Border.BorderBrushProperty, "line");
            edge.Padding = pad;
            edge.Child = child;
            Grid g = new Grid();
            g.Children.Add(shadow);
            g.Children.Add(lit);
            g.Children.Add(edge);
            return g;
        }

        static void SetBg(Border b, object bg)
        {
            string s = bg as string;
            if (s != null) Paint(b, Border.BackgroundProperty, s);
            else b.Background = bg as Brush;
        }

        /// <summary>Sets a glass box's radius (all its layers).</summary>
        public static void GlassRadius(Grid glass, double r)
        {
            foreach (UIElement c in glass.Children) { Border b = c as Border; if (b != null) b.CornerRadius = new CornerRadius(r); }
        }

        /// <summary>.panel: rgba(255,255,255,.055), the line, radius 22, padding 22 (phones: 20 and 16), the lit top edge.</summary>
        public static Border Panel(UIElement child)
        {
            Border b = new RoundBorder();
            b.Child = child;
            b.Background = Css.Brush("rgba(255,255,255,.055)");
            b.SetResourceReference(Border.BorderBrushProperty, "line");
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(22);
            b.Padding = new Thickness(22);
            U.OnWindowWidth(b, w =>
            {
                bool p = Phone(w);
                b.CornerRadius = new CornerRadius(p ? 20 : 22);
                b.Padding = new Thickness(p ? 16 : 22);
            });
            return b;
        }

        // ---------------------------------------------------------------- buttons

        public static BtnLook BtnK(AuBtn kind)
        {
            BtnLook b = new BtnLook();
            b.Bg = Css.Brush("rgba(255,255,255,.07)"); b.Border = "line-2"; b.Fg = "ink";
            b.HoverBg = Css.Brush("rgba(255,255,255,.13)"); b.HoverBorder = Css.Brush("rgba(255,255,255,.34)");
            b.Radius = new CornerRadius(999); b.MinHeight = 44; b.MinWidth = 44; b.Padding = Css.T(10, 18); b.FontSize = 15; b.Weight = U.W(500);
            if ((kind & AuBtn.Primary) != 0)
            {
                b.Bg = "primary"; b.Border = Brushes.Transparent; b.Fg = "accent-ink"; b.Weight = U.W(650); b.HoverBg = null; b.HoverBorder = null;
                b.Shadow = Css.Shadow(8, 26, "rgba(70,240,168,.45)");
            }
            if ((kind & AuBtn.Danger) != 0) { b.Bg = Css.Brush("rgba(255,110,140,.1)"); b.Border = Css.Brush("rgba(255,143,163,.5)"); b.Fg = Css.Brush("#ffc2cd"); b.HoverBg = Css.Brush("rgba(255,110,140,.2)"); b.HoverBorder = null; }
            if ((kind & AuBtn.Ghost) != 0) { b.Bg = Brushes.Transparent; b.Border = Brushes.Transparent; b.HoverBg = Css.Brush("rgba(255,255,255,.08)"); b.HoverBorder = null; }
            if ((kind & AuBtn.Sm) != 0) { b.Padding = Css.T(8, 14); b.FontSize = 14; }
            if ((kind & AuBtn.Icon) != 0) b.Padding = new Thickness(0);
            return b;
        }

        /// <summary>.btn: an optional 18px icon, gap 8; an .icon button (44 wide) has the label as its tooltip and name.</summary>
        public static Button Btn(string label, string icon, AuBtn kind, Action click)
        {
            bool isIcon = (kind & AuBtn.Icon) != 0;
            FrameworkElement content = isIcon ? Icon(icon ?? "x", 18) : U.IconLabel(icon != null ? Icon(icon, 18) : null, label, 8);
            Button b = U.Button(content, BtnK(kind));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            if (isIcon) b.Width = 44;
            if (label != null) System.Windows.Automation.AutomationProperties.SetName(b, label);
            if (isIcon && label != null) b.ToolTip = label;
            if (click != null) b.Click += delegate { click(); };
            return b;
        }

        // ---------------------------------------------------------------- fields

        /// <summary>.input: 46 px, radius 14, line-2, rgba(4,8,22,.55), 16px; focus: accent-2.</summary>
        public static InputLook InK()
        {
            InputLook k = new InputLook();
            k.Bg = "in-bg"; k.Border = "line-2"; k.Focus = "accent-2"; k.Fg = "ink"; k.Placeholder = "placeholder";
            k.Radius = 14; k.FontSize = 16; k.MinHeight = 46; k.Padding = new Thickness(14, 0, 14, 0);
            return k;
        }

        /// <summary>input.input (a text, or a password).</summary>
        public static Control Input(string value, string placeholder, string label, bool password = false)
        {
            Control c = U.Input(InK(), password, value, placeholder);
            System.Windows.Automation.AutomationProperties.SetName(c, label ?? placeholder ?? "");
            return c;
        }

        /// <summary>label.field: the label (14px 500 ink-2) over its control, gap 6.</summary>
        public static Flex Field(string label, UIElement control)
        {
            TextBlock l = Text(label, 14, "ink-2", 500);
            return Flex.Col(6, l, control);
        }

        /// <summary>
        /// select.input: the closed box as the web draws it (46 px, 16px, the
        /// ▼ at the right; a title bar's .veh-pick: round, 42 px in for the
        /// car, at least 160 wide, a darker field) and its options as a menu,
        /// as a browser opens its own list. Picked fires with the new value.
        /// </summary>
        public sealed class Select
        {
            public readonly Border Root;
            readonly TextBlock label = new TextBlock();
            readonly string[][] options;
            public string Value;
            public event Action<string> Picked;

            public Select(string[][] options, string value, string name, string icon)
            {
                this.options = options;
                Value = value;
                label.FontSize = 16;
                label.SetResourceReference(TextBlock.ForegroundProperty, "ink");
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.VerticalAlignment = VerticalAlignment.Center;
                Path arrow = new Path();
                arrow.Data = Geometry.Parse("M0,0 L12,0 L6,6 Z");
                arrow.SetResourceReference(Shape.FillProperty, "ink-2");
                arrow.HorizontalAlignment = HorizontalAlignment.Right;
                arrow.VerticalAlignment = VerticalAlignment.Center;
                arrow.Margin = new Thickness(0, 1, -26, 0);
                arrow.IsHitTestVisible = false;
                Grid g = new Grid();
                g.Children.Add(label);
                g.Children.Add(arrow);
                Root = new RoundBorder();
                Root.Child = g;
                Root.BorderThickness = new Thickness(1);
                Root.SetResourceReference(Border.BorderBrushProperty, "line-2");
                Root.MinHeight = 46;
                Root.Cursor = System.Windows.Input.Cursors.Hand;
                Root.Focusable = true;
                System.Windows.Automation.AutomationProperties.SetName(Root, name ?? "");
                Root.ToolTip = name;
                if (icon != null)
                {
                    // .veh-pick: the icon at 14 px in (accent-2, 18 px), the box round
                    FrameworkElement i = Icon(icon, 18, "accent-2");
                    i.HorizontalAlignment = HorizontalAlignment.Left;
                    i.Margin = new Thickness(-28, 0, 0, 0);
                    i.IsHitTestVisible = false;
                    g.Children.Add(i);
                    Root.CornerRadius = new CornerRadius(999);
                    Root.Padding = new Thickness(42, 0, 40, 0);
                    Root.MinWidth = 160;
                    Root.Background = Css.Brush("rgba(4,8,22,.45)");
                }
                else
                {
                    Root.CornerRadius = new CornerRadius(14);
                    Root.Padding = new Thickness(14, 0, 40, 0);
                    Root.SetResourceReference(Border.BackgroundProperty, "in-bg");
                }
                U.Click(Root, Open);
                Root.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.Down) { Open(); e.Handled = true; }
                };
                FocusRing.Set(Root, 2, icon != null ? 999 : 14);
                Show();
            }

            void Show()
            {
                label.Text = "";
                foreach (string[] o in options) if (o[0] == Value) label.Text = o[1];
                if (label.Text.Length == 0 && options.Length > 0) label.Text = options[0][1];
            }

            void Open()
            {
                ContextMenu menu = new ContextMenu();
                foreach (string[] o in options)
                {
                    MenuItem item = new MenuItem();
                    item.Header = o[1];
                    item.IsCheckable = true;
                    item.IsChecked = o[0] == Value;
                    string v = o[0];
                    item.Click += delegate { Pick(v); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = Root;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.MinWidth = Root.ActualWidth;
                menu.IsOpen = true;
            }

            /// <summary>The reader picks a value (change): drawn, then Picked.</summary>
            public void Pick(string v)
            {
                if (v == Value) return;
                Value = v;
                Show();
                Action<string> h = Picked;
                if (h != null) h(v);
            }
        }

        /// <summary>
        /// A title bar's vehicle select (core.js vehiclePicker(), or a page's
        /// own like Jobs' "All vehicles" one): the car icon, the names; with
        /// "All vehicles" first when allLabel is given.
        /// </summary>
        public static Select VehiclePick(IList<VehicleInfo> cars, string value, string allLabel)
        {
            List<string[]> o = new List<string[]>();
            if (allLabel != null) o.Add(new[] { "all", allLabel });
            if (cars != null) foreach (VehicleInfo v in cars) o.Add(new[] { v.Id, v.Name });
            return new Select(o.ToArray(), value, "Vehicle", "car");
        }

        // ---------------------------------------------------------------- chips

        /// <summary>.chip (green, teal, violet, magenta, amber, red or ""): 13px 500, padding 3 10, round, an optional 15px icon.</summary>
        public static Border Chip(string text, string tone, string icon = null)
        {
            string fg = "ink-2", border = null, bg = "rgba(255,255,255,.06)";
            switch (tone ?? "")
            {
                case "green": fg = "#b5ffe0"; border = "rgba(111,243,191,.4)"; bg = "rgba(111,243,191,.1)"; break;
                case "teal": fg = "#b6f7f7"; border = "rgba(63,210,212,.4)"; bg = "rgba(63,210,212,.1)"; break;
                case "violet": fg = "#ddd1ff"; border = "rgba(185,163,255,.45)"; bg = "rgba(155,125,255,.14)"; break;
                case "magenta": fg = "#ffd2fb"; border = "rgba(232,110,224,.45)"; bg = "rgba(232,110,224,.12)"; break;
                case "amber": fg = "#ffe6b3"; border = "rgba(255,210,122,.45)"; bg = "rgba(255,210,122,.1)"; break;
                case "red": fg = "#ffd0d9"; border = "rgba(255,143,163,.5)"; bg = "rgba(255,143,163,.12)"; break;
            }
            TextBlock t = Text(text ?? "", 13, null, 500);
            t.TextWrapping = TextWrapping.NoWrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            Flex row = Flex.Row(6);
            if (icon != null) row.Children.Add(Icon(icon, 15));
            row.Children.Add(t);
            Border b = new RoundBorder();
            b.Child = row;
            b.Padding = Css.T(3, 10);
            b.CornerRadius = new CornerRadius(999);
            b.BorderThickness = new Thickness(1);
            if (border != null) b.BorderBrush = Css.Brush(border); else b.SetResourceReference(Border.BorderBrushProperty, "line-2");
            b.Background = Css.Brush(bg);
            Paint(b, TextElement.ForegroundProperty, fg);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        // ---------------------------------------------------------------- empty, loading, failed

        /// <summary>.empty-orb: 64 px, the accent icon (28 px) on a green-violet glow.</summary>
        public static FrameworkElement Orb(string icon)
        {
            Border orb = new Border();
            orb.Width = 64; orb.Height = 64;
            orb.CornerRadius = new CornerRadius(32);
            orb.Background = Css.Radial(.35, .3, .7, .7, "rgba(94,242,184,.35)", 0, "rgba(155,125,255,.15)", .6, "rgba(155,125,255,0)", .7);
            FrameworkElement i = Icon(icon, 28, "accent");
            i.HorizontalAlignment = HorizontalAlignment.Center;
            orb.Child = i;
            orb.HorizontalAlignment = HorizontalAlignment.Center;
            return orb;
        }

        /// <summary>
        /// core.js empty(title, text, extra): the orb (sparkle), the heading
        /// (22px 300), the words (muted), what goes under them; centred,
        /// padding 46 16, gap 10. orb false: main.js's error box has none.
        /// </summary>
        public static Flex Empty(string title, string text, bool orb, params UIElement[] extra)
        {
            Flex f = Flex.Col(10);
            if (orb) f.Children.Add(Orb("sparkle"));
            TextBlock h3 = Heading(title, 22, 300);
            h3.TextAlignment = TextAlignment.Center;
            h3.HorizontalAlignment = HorizontalAlignment.Center;
            f.Children.Add(h3);
            if (!string.IsNullOrEmpty(text))
            {
                TextBlock p = Muted(text, 16);
                p.TextAlignment = TextAlignment.Center;
                p.HorizontalAlignment = HorizontalAlignment.Center;
                f.Children.Add(p);
            }
            foreach (UIElement x in extra ?? new UIElement[0])
            {
                FrameworkElement fe = x as FrameworkElement;
                if (fe != null) fe.HorizontalAlignment = HorizontalAlignment.Center;
                f.Children.Add(x);
            }
            f.Margin = Css.T(46, 16);
            return f;
        }

        /// <summary>p.loading: a 34 px ring (white .15, its top accent) turning once a second over "Loading…" (ink-3); padding 40.</summary>
        public static FrameworkElement Loading()
        {
            Grid ring = new Grid();
            ring.Width = 34; ring.Height = 34;
            Ellipse track = new Ellipse();
            track.StrokeThickness = 3;
            track.Stroke = Css.Brush("rgba(255,255,255,.15)");
            Path top = new Path();
            // border-top-color: the top quarter of the ring, from 10:30 to 1:30
            top.Data = Geometry.Parse("M5.04,5.04 A15.5,15.5 0 0 1 28.96,5.04");
            top.StrokeThickness = 3;
            top.SetResourceReference(Shape.StrokeProperty, "accent");
            ring.Children.Add(track);
            ring.Children.Add(top);
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Margin = new Thickness(0, 0, 0, 12);
            ring.RenderTransformOrigin = new Point(0.5, 0.5);
            RotateTransform rot = new RotateTransform();
            ring.RenderTransform = rot;
            ring.Loaded += delegate
            {
                if (Still) return;
                DoubleAnimation a = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                rot.BeginAnimation(RotateTransform.AngleProperty, a);
            };
            TextBlock t = Text(Au.Loading, 16, "ink-3");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            StackPanel s = new StackPanel();
            s.Children.Add(ring);
            s.Children.Add(t);
            s.Margin = new Thickness(40);
            s.Uid = "loading";
            System.Windows.Automation.AutomationProperties.SetName(s, Au.Loading);
            return s;
        }

        /// <summary>main.js render()'s catch: "Something went wrong", the message (muted), Try again (mounts again).</summary>
        public static FrameworkElement LoadFailed(string message, Action retry)
        {
            Button again = Btn(Au.TryAgain, null, AuBtn.Plain, retry);
            again.Uid = "retry";
            return Empty(Au.LoadFailedTitle, message, false, again);
        }

        /// <summary>A paragraph from rich runs (bold, code: mono .92em on a light tile).</summary>
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
                            c.FontSize = size * 0.92;
                            c.Background = Css.Brush("rgba(255,255,255,.08)");
                            t.Inlines.Add(c);
                            break;
                        }
                    case RichKind.Break: t.Inlines.Add(new LineBreak()); break;
                    default: t.Inlines.Add(new Run(run.Text)); break;
                }
            }
            return t;
        }
    }

    /// <summary>
    /// One glass window of the stage, as main.js render() builds it: .win.glass
    /// (radius 30, padding 30 32 34; phones 26 and 22 16 24), its head (the
    /// kicker: icon and 13px .14em uppercase #9ff0e6 600; the h1 clamp(32px,
    /// 4.2vw, 48px) 250; the sub line, 15px muted, empty until the page says;
    /// the actions on the right, bottom-aligned, wrapping) and the body.
    /// A page without chrome (Sky) is bare: no glass and no head. The page
    /// writes the sub line through ctx.Status (the shell's PageStatus) and
    /// its title-bar controls through <see cref="SetActions"/>.
    /// </summary>
    public sealed class AuWindow
    {
        public readonly Grid Root = new Grid();
        public readonly ContentControl Body = new ContentControl();
        public readonly TextBlock Sub;
        public readonly Flex Actions = Flex.Row(10);
        public readonly Flex Head;
        readonly Grid glass;
        readonly Border edge;
        readonly bool bare;

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShellModel, AuWindow> current = new System.Runtime.CompilerServices.ConditionalWeakTable<ShellModel, AuWindow>();

        /// <summary>The window the shell shows now (its page's ctx.actions and ctx.sub).</summary>
        public static AuWindow Of(ShellModel m)
        {
            AuWindow w;
            return m != null && current.TryGetValue(m, out w) ? w : null;
        }

        public AuWindow(ShellModel m, string title, string kicker, string icon, bool chrome)
        {
            if (m != null) { current.Remove(m); current.Add(m, this); }
            bare = !chrome;
            Body.Focusable = false;
            Body.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Sub = AuKit.Muted("", 15);
            Sub.Margin = new Thickness(0, 8, 0, 0);
            Sub.Height = 0;
            Sub.Uid = "win-sub";
            StackPanel all = new StackPanel();
            if (chrome)
            {
                FrameworkElement ki = AuKit.Icon(icon ?? "sky", 16);
                SpacedText ks = U.Spaced((kicker ?? Au.KickerDefault).ToUpperInvariant(), 13, FontWeights.SemiBold, 0.14, "kicker");
                ks.VerticalAlignment = VerticalAlignment.Center;
                Flex kick = Flex.Row(8, ki, ks);
                kick.SetResourceReference(TextElement.ForegroundProperty, "kicker");
                kick.Margin = new Thickness(0, 0, 0, 8);
                kick.HorizontalAlignment = HorizontalAlignment.Left;
                TextBlock h1 = AuKit.Heading(title, 48, 250);
                h1.Uid = "win-title";
                StackPanel titles = new StackPanel();
                titles.Children.Add(kick);
                titles.Children.Add(h1);
                titles.Children.Add(Sub);
                titles.VerticalAlignment = VerticalAlignment.Bottom;
                Actions.Wrap = true;
                Actions.LineGap = 10;
                Actions.VerticalAlignment = VerticalAlignment.Bottom;
                Actions.Visibility = Visibility.Collapsed;
                Head = new Flex(Orientation.Horizontal, 20);
                Head.Wrap = true;
                Head.LineGap = 16;
                Head.Justify = "between";
                Head.Margin = new Thickness(0, 0, 0, 26);
                Head.Children.Add(titles);
                Head.Children.Add(Actions);
                all.Children.Add(Head);
                U.OnWindowWidth(h1, w => h1.FontSize = AuKit.Phone(w) ? 32 : Math.Max(32, Math.Min(0.042 * w, 48)));
                U.OnWindowWidth(h1, w => h1.LineHeight = h1.FontSize * 1.15);
            }
            all.Children.Add(Body);
            if (chrome)
            {
                // .win.glass: rgba(12,19,41,.76) under a faint diagonal sheen
                glass = AuKit.Glass(all, new CornerRadius(30), Css.T(30, 32, 34, 32), Css.Brush("rgba(12,19,41,.76)"));
                Border sheen = new RoundBorder();
                sheen.CornerRadius = new CornerRadius(30);
                sheen.Background = Css.Linear(140, "rgba(255,255,255,.07)", 0, "rgba(255,255,255,0)", .38, "rgba(120,140,255,.03)", .7, "rgba(255,255,255,.04)", 1);
                sheen.IsHitTestVisible = false;
                glass.Children.Insert(1, sheen);
                ((Border)glass.Children[0]).Effect = Css.Shadow(24, 60, "rgba(0,0,0,.5)");
                edge = (Border)glass.Children[glass.Children.Count - 1];
                Root.Children.Add(glass);
                U.OnWindowWidth(Root, w =>
                {
                    bool p = AuKit.Phone(w);
                    edge.Padding = p ? Css.T(22, 16, 24, 16) : Css.T(30, 32, 34, 32);
                    AuKit.GlassRadius(glass, p ? 26 : 30);
                    Head.Margin = new Thickness(0, 0, 0, p ? 18 : 26);
                });
            }
            else Root.Children.Add(all);
            // .win.entering: up 16 px and faded in (.32 s / .42 s), not when still
            Root.Loaded += delegate
            {
                if (AuKit.Still) return;
                TranslateTransform tr = new TranslateTransform(0, 16);
                Root.RenderTransform = tr;
                CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                DoubleAnimation y = new DoubleAnimation(16, 0, new Duration(TimeSpan.FromSeconds(0.42))) { EasingFunction = ease };
                DoubleAnimation o = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.32))) { EasingFunction = ease };
                tr.BeginAnimation(TranslateTransform.YProperty, y);
                Root.BeginAnimation(UIElement.OpacityProperty, o);
            };
        }

        public bool Bare { get { return bare; } }

        /// <summary>ctx.sub(text): the line under the title ("" keeps its 8 px and no line, as an empty p).</summary>
        public void SetSub(string text)
        {
            Sub.Text = text ?? "";
            Sub.Height = Sub.Text.Length == 0 ? 0 : double.NaN;
        }

        /// <summary>ctx.actions.innerHTML = …: the title bar's controls (none: .win-actions:empty is not shown).</summary>
        public void SetActions(params UIElement[] items)
        {
            Actions.Children.Clear();
            if (items != null) foreach (UIElement e in items) if (e != null) Actions.Children.Add(e);
            Actions.Visibility = Actions.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The head hidden (the Viewer's window: body[data-route="viewer"] .win-head).</summary>
        public void HideHead()
        {
            if (Head != null) Head.Visibility = Visibility.Collapsed;
        }
    }
}
