using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentrySix.Ui
{
    /// <summary>Small builders shared by the five interfaces (C# 5, no compiled XAML).</summary>
    public static class U
    {
        /// <summary>Takes a property from the theme by key (DynamicResource, so a theme switch repaints).</summary>
        public static T Ref<T>(T e, DependencyProperty dp, string key) where T : FrameworkElement
        {
            e.SetResourceReference(dp, key);
            return e;
        }

        public static TextBlock Text(string text, double size, FontWeight weight, string colorKey)
        {
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = size;
            t.FontWeight = weight;
            if (colorKey != null) t.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        /// <summary>One line, cut with an ellipsis (CSS white-space: nowrap; text-overflow: ellipsis).</summary>
        public static TextBlock Line(string text, double size, FontWeight weight, string colorKey)
        {
            TextBlock t = Text(text, size, weight, colorKey);
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        public static SpacedText Spaced(string text, double size, FontWeight weight, double spacingEm, string colorKey)
        {
            SpacedText t = new SpacedText(text, size, weight, spacingEm);
            if (colorKey != null) t.SetResourceReference(SpacedText.ForegroundProperty, colorKey);
            return t;
        }

        public static FontWeight W(int css)
        {
            return FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, css)));
        }

        public static Border Box(UIElement child, string bgKey, string borderKey, double borderWidth, CornerRadius radius, Thickness padding)
        {
            Border b = new Border();
            b.Child = child;
            if (bgKey != null) b.SetResourceReference(Border.BackgroundProperty, bgKey);
            if (borderKey != null)
            {
                b.SetResourceReference(Border.BorderBrushProperty, borderKey);
                b.BorderThickness = new Thickness(borderWidth);
            }
            b.CornerRadius = radius;
            b.Padding = padding;
            return b;
        }

        /// <summary>A click target: the mouse, Enter and Space, a hand cursor, keyboard focus.</summary>
        public static T Click<T>(T e, Action act) where T : FrameworkElement
        {
            e.Cursor = Cursors.Hand;
            e.Focusable = true;
            bool down = false;
            e.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs a) { down = true; a.Handled = true; };
            e.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs a)
            {
                if (down && e.IsMouseOver) act();
                down = false;
                a.Handled = true;
            };
            e.MouseLeave += delegate { down = false; };
            e.KeyDown += delegate(object s, KeyEventArgs a)
            {
                if (a.Key == Key.Enter || a.Key == Key.Space) { act(); a.Handled = true; }
            };
            return e;
        }

        /// <summary>CSS :hover.</summary>
        public static T Hover<T>(T e, Action<bool> on) where T : FrameworkElement
        {
            e.MouseEnter += delegate { on(true); };
            e.MouseLeave += delegate { on(false); };
            return e;
        }

        /// <summary>Calls back with the window-relative width and height whenever they change (the CSS media queries).</summary>
        public static void OnSize(FrameworkElement root, Action<double, double> apply)
        {
            double lastW = -1, lastH = -1;
            SizeChangedEventHandler h = delegate
            {
                double w = root.ActualWidth, hh = root.ActualHeight;
                if (w <= 0) return;
                if (Math.Abs(w - lastW) < 0.5 && Math.Abs(hh - lastH) < 0.5) return;
                lastW = w;
                lastH = hh;
                apply(w, hh);
            };
            root.SizeChanged += h;
            root.Loaded += delegate { h(root, null); };
        }

        /// <summary>The width of the whole window (or the render target) the element sits in: the viewport the media queries test.</summary>
        public static void OnWindowWidth(FrameworkElement e, Action<double> apply)
        {
            FrameworkElement top = null;
            SizeChangedEventHandler h = delegate { if (top != null && top.ActualWidth > 0) apply(top.ActualWidth); };
            e.Loaded += delegate
            {
                DependencyObject d = e;
                FrameworkElement last = e;
                while (d != null)
                {
                    if (d is Window) break;
                    FrameworkElement fe = d as FrameworkElement;
                    if (fe != null) last = fe;
                    d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
                }
                if (top != null) top.SizeChanged -= h;
                top = last;
                top.SizeChanged += h;
                h(null, null);
            };
            e.Unloaded += delegate { if (top != null) top.SizeChanged -= h; };
        }

        /// <summary>A loose-XAML fragment (DynamicResource keys, styles, templates) parsed at run time.</summary>
        public static object Xaml(string xaml)
        {
            ParserContext pc = new ParserContext();
            pc.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            pc.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
            return XamlReader.Parse(xaml, pc);
        }

        public static ResourceDictionary Dict(string inner)
        {
            return (ResourceDictionary)Xaml("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\">" + inner + "</ResourceDictionary>");
        }

        /// <summary>
        /// A text field styled like the web's input: background, border, focus
        /// border, radius, padding, size, minimum height (all theme keys or numbers).
        /// Placeholder text is drawn on top while it is empty.
        /// </summary>
        public static Control Input(InputLook k, bool password, string value, string placeholder)
        {
            string tpl = "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Control\">"
                + "<Border x:Name=\"bd\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"1\" CornerRadius=\"" + k.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\" SnapsToDevicePixels=\"True\">"
                + "<Grid><ScrollViewer x:Name=\"PART_ContentHost\" Margin=\"{TemplateBinding Padding}\" VerticalAlignment=\"Center\" Focusable=\"False\" HorizontalScrollBarVisibility=\"Hidden\" VerticalScrollBarVisibility=\"Hidden\"/>"
                + "<TextBlock x:Name=\"ph\" IsHitTestVisible=\"False\" VerticalAlignment=\"Center\" Margin=\"2,0,0,0\" Text=\"{TemplateBinding Tag}\" Foreground=\"{DynamicResource " + k.Placeholder + "}\" Visibility=\"Collapsed\"/></Grid></Border>"
                + "<ControlTemplate.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter TargetName=\"bd\" Property=\"BorderBrush\" Value=\"{DynamicResource " + k.Focus + "}\"/></Trigger></ControlTemplate.Triggers>"
                + "</ControlTemplate>";
            ControlTemplate template = (ControlTemplate)Xaml(tpl);
            Control c;
            if (password)
            {
                PasswordBox p = new PasswordBox();
                p.SetResourceReference(PasswordBox.CaretBrushProperty, k.Fg);
                p.SetResourceReference(PasswordBox.SelectionBrushProperty, k.Focus);
                c = p;
            }
            else
            {
                TextBox t = new TextBox();
                t.Text = value ?? "";
                t.SetResourceReference(TextBox.CaretBrushProperty, k.Fg);
                t.SetResourceReference(TextBox.SelectionBrushProperty, k.Focus);
                c = t;
            }
            c.Template = template;
            c.SetResourceReference(Control.BackgroundProperty, k.Bg);
            c.SetResourceReference(Control.BorderBrushProperty, k.Border);
            c.SetResourceReference(Control.ForegroundProperty, k.Fg);
            c.Padding = k.Padding;
            c.FontSize = k.FontSize;
            if (k.Font != null) c.FontFamily = k.Font;
            c.MinHeight = k.MinHeight;
            c.VerticalContentAlignment = VerticalAlignment.Center;
            c.Tag = placeholder ?? "";
            c.Loaded += delegate { UpdatePlaceholder(c); };
            if (password) ((PasswordBox)c).PasswordChanged += delegate { UpdatePlaceholder(c); };
            else ((TextBox)c).TextChanged += delegate { UpdatePlaceholder(c); };
            c.Padding = new Thickness(k.Padding.Left, 0, k.Padding.Right, 0);
            return c;
        }

        static void UpdatePlaceholder(Control c)
        {
            c.ApplyTemplate();
            TextBlock ph = c.Template.FindName("ph", c) as TextBlock;
            if (ph == null) return;
            ph.Margin = new Thickness(c.Padding.Left + 2, 0, 0, 0);
            string text = c is PasswordBox ? ((PasswordBox)c).Password : ((TextBox)c).Text;
            ph.Visibility = string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(c.Tag as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public static string Value(Control c)
        {
            PasswordBox p = c as PasswordBox;
            if (p != null) return p.Password;
            TextBox t = c as TextBox;
            return t != null ? t.Text : "";
        }

        /// <summary>
        /// A button drawn like the web's .btn: background, border and text from
        /// theme keys (or a Brush), radius, padding, minimum height, font; hover
        /// background and border; disabled at the web's opacity.
        /// </summary>
        public static Button Button(object content, BtnLook k)
        {
            Button b = new Button();
            b.Content = content;
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.CornerRadiusProperty, k.Radius);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(k.BorderWidth));
            bd.SetValue(Border.SnapsToDevicePixelsProperty, true);
            Paint(bd, Border.BackgroundProperty, k.Bg);
            Paint(bd, Border.BorderBrushProperty, k.Border);
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, k.Align);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.MarginProperty, k.Padding);
            bd.AppendChild(cp);
            ControlTemplate t = new ControlTemplate(typeof(Button));
            if (k.Shadow != null)
            {
                // the shadow from a box under the button (an effect on the button itself would shadow its text)
                FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));
                FrameworkElementFactory sh = new FrameworkElementFactory(typeof(Border), "sh");
                sh.SetValue(Border.CornerRadiusProperty, k.Radius);
                sh.SetValue(Border.BackgroundProperty, Brushes.Black);
                sh.SetValue(UIElement.EffectProperty, k.Shadow);
                root.AppendChild(sh);
                root.AppendChild(bd);
                t.VisualTree = root;
            }
            else t.VisualTree = bd;
            if (k.HoverBg != null || k.HoverBorder != null)
            {
                Trigger over = new Trigger();
                over.Property = UIElement.IsMouseOverProperty;
                over.Value = true;
                if (k.HoverBg != null) over.Setters.Add(new Setter(Border.BackgroundProperty, Val(k.HoverBg), "bd"));
                if (k.HoverBorder != null) over.Setters.Add(new Setter(Border.BorderBrushProperty, Val(k.HoverBorder), "bd"));
                t.Triggers.Add(over);
            }
            Trigger off = new Trigger();
            off.Property = UIElement.IsEnabledProperty;
            off.Value = false;
            off.Setters.Add(new Setter(UIElement.OpacityProperty, k.DisabledOpacity));
            t.Triggers.Add(off);
            b.Template = t;
            Paint(b, Control.ForegroundProperty, k.Fg);
            b.FontSize = k.FontSize;
            b.FontWeight = k.Weight;
            if (k.Font != null) b.FontFamily = k.Font;
            b.MinHeight = k.MinHeight;
            if (k.MinWidth > 0) b.MinWidth = k.MinWidth;
            b.Cursor = Cursors.Hand;
            b.FocusVisualStyle = null;
            return b;
        }

        static object Val(object v)
        {
            string key = v as string;
            return key != null ? (object)new DynamicResourceExtension(key) : v;
        }

        static void Paint(FrameworkElementFactory f, DependencyProperty dp, object v)
        {
            if (v == null) return;
            string key = v as string;
            if (key != null) f.SetResourceReference(dp, key);
            else f.SetValue(dp, v);
        }

        public static void Paint(FrameworkElement e, DependencyProperty dp, object v)
        {
            if (v == null) return;
            string key = v as string;
            if (key != null) e.SetResourceReference(dp, key);
            else e.SetValue(dp, v);
        }

        /// <summary>Icon and label side by side (a button's content).</summary>
        public static FrameworkElement IconLabel(FrameworkElement icon, string label, double gap)
        {
            Flex f = Flex.Row(gap);
            if (icon != null) { icon.VerticalAlignment = VerticalAlignment.Center; f.Children.Add(icon); }
            if (label != null)
            {
                TextBlock t = new TextBlock();
                t.Text = label;
                t.VerticalAlignment = VerticalAlignment.Center;
                f.Children.Add(t);
            }
            return f;
        }

        /// <summary>A dot (CSS width = height, border-radius: 50%).</summary>
        public static Ellipse Dot(double size, object fill)
        {
            Ellipse e = new Ellipse();
            e.Width = size;
            e.Height = size;
            Paint(e, Shape.FillProperty, fill);
            return e;
        }

        /// <summary>A pill badge (the nav counts).</summary>
        public static Border Badge(string text, double height, double minWidth, double padX, object bg, object fg, double size, FontWeight weight, FontFamily font)
        {
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = size;
            t.FontWeight = weight;
            if (font != null) t.FontFamily = font;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Paint(t, TextBlock.ForegroundProperty, fg);
            Border b = new Border();
            b.Child = t;
            b.Height = height;
            b.MinWidth = minWidth;
            b.Padding = new Thickness(padX, 0, padX, 0);
            b.CornerRadius = new CornerRadius(height / 2);
            Paint(b, Border.BackgroundProperty, bg);
            b.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            return b;
        }

        public static void SetBadge(Border b, string text, object bg, object fg)
        {
            b.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            TextBlock t = b.Child as TextBlock;
            if (t != null)
            {
                t.Text = text ?? "";
                if (fg != null) Paint(t, TextBlock.ForegroundProperty, fg);
            }
            if (bg != null) Paint(b, Border.BackgroundProperty, bg);
        }

        /// <summary>The words every placeholder page says: which phase brings the page.</summary>
        public static string PhaseLine(PageDef p)
        {
            return "The " + p.Title + " page comes to this app in Phase " + p.Phase + ".";
        }

        public static string PhaseWhat(int phase)
        {
            switch (phase)
            {
                case 1: return "Phase 1 brings the built-in interface's pages.";
                case 2: return "Phase 2 brings the Viewer: six cameras, telemetry, map and export.";
                case 3: return "Phase 3 brings the Encoder.";
                case 4: return "Phase 4 brings every Horizon page.";
                case 5: return "Phase 5 brings every Flux page.";
                case 6: return "Phase 6 brings every Aurora page.";
                case 7: return "Phase 7 brings every Daylight page.";
                default: return "";
            }
        }

        public const string OpenInBrowserText = "Until then it works in the browser, with the same data.";

        // ---------- fonts ----------
        static FontFamily inter, mono;

        /// <summary>The folder with the fonts shipped beside the exe (Fonts\), else Ui\Builtin\Fonts\ in a checkout.</summary>
        static string FontDir()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] tries = { System.IO.Path.Combine(exe, "Fonts"), System.IO.Path.Combine(exe, "Ui", "Builtin", "Fonts"), System.IO.Path.Combine(exe, "..", "Ui", "Builtin", "Fonts") };
            foreach (string d in tries) if (File.Exists(System.IO.Path.Combine(d, "Inter-Regular.ttf"))) return System.IO.Path.GetFullPath(d);
            return null;
        }

        /// <summary>Inter, the built-in interface's font (web/fonts, as static TTF weights), else Segoe UI.</summary>
        public static FontFamily Inter
        {
            get
            {
                if (inter == null)
                {
                    string d = FontDir();
                    inter = d != null ? new FontFamily(new Uri(d.TrimEnd('\\', '/') + "/"), "./#Inter, Segoe UI") : new FontFamily("Segoe UI");
                }
                return inter;
            }
        }

        /// <summary>JetBrains Mono, the built-in interface's monospace, else Cascadia Mono / Consolas.</summary>
        public static FontFamily JetBrains
        {
            get
            {
                if (mono == null)
                {
                    string d = FontDir();
                    mono = d != null ? new FontFamily(new Uri(d.TrimEnd('\\', '/') + "/"), "./#JetBrains Mono, Cascadia Mono, Consolas") : new FontFamily("Cascadia Mono, Consolas");
                }
                return mono;
            }
        }

        /// <summary>CSS system-ui on Windows (what Chrome and Edge draw there).</summary>
        public static readonly FontFamily SystemUi = new FontFamily("Segoe UI");
        /// <summary>ui-monospace, "SF Mono", ..., Consolas: on Windows, Consolas.</summary>
        public static readonly FontFamily Consolas = new FontFamily("Consolas");
        /// <summary>Flux's and Aurora's monospace lists name Cascadia Mono before Consolas.</summary>
        public static readonly FontFamily Cascadia = new FontFamily("Cascadia Mono, Consolas");
    }

    /// <summary>The look of a text field (theme keys for colours).</summary>
    public sealed class InputLook
    {
        public string Bg = "panel", Border = "border-strong", Focus = "accent", Fg = "text", Placeholder = "muted";
        public double Radius = 10, FontSize = 14, MinHeight = 44;
        public Thickness Padding = new Thickness(14, 0, 14, 0);
        public FontFamily Font;
    }

    /// <summary>The look of a button: Brush values or theme keys (strings).</summary>
    public sealed class BtnLook
    {
        public object Bg, Border, Fg, HoverBg, HoverBorder;
        public double BorderWidth = 1, FontSize = 14, MinHeight = 44, MinWidth, DisabledOpacity = 0.45;
        public CornerRadius Radius = new CornerRadius(10);
        public Thickness Padding = new Thickness(16, 0, 16, 0);
        public FontWeight Weight = FontWeights.SemiBold;
        public FontFamily Font;
        public HorizontalAlignment Align = HorizontalAlignment.Center;
        public System.Windows.Media.Effects.Effect Shadow;

        public BtnLook Copy()
        {
            return (BtnLook)MemberwiseClone();
        }
    }
}
