using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>Button variants of web/shell.css (.btn plus .sm, .green, .solid, .danger, .ghost).</summary>
    [Flags]
    public enum BtnKind { Plain = 0, Small = 1, Green = 2, Solid = 4, Danger = 8, Ghost = 16 }

    /// <summary>Chip colours (.chip.green/.red/.amber/.blue/.violet).</summary>
    public enum Tone { None, Green, Red, Amber, Blue, Violet }

    /// <summary>
    /// The built-in interface's building blocks, drawn as web/shell.css draws
    /// them (cards, the page head, buttons, chips, dots, notes, key/value
    /// lists, fields, the form grid, the empty box), each following the
    /// stylesheet's 760 px phone rules on its own. Pages are made of these.
    /// </summary>
    public static class Look
    {
        public const double PhoneWidth = 760;

        /// <summary>Calls back with "is it a phone" now and whenever the window crosses 760 px.</summary>
        public static void Phone(FrameworkElement e, Action<bool> apply)
        {
            bool? last = null;
            U.OnWindowWidth(e, w =>
            {
                bool p = w <= PhoneWidth;
                if (last.HasValue && last.Value == p) return;
                last = p;
                apply(p);
            });
        }

        // ---------- inputs and buttons ----------
        public static InputLook InputK()
        {
            InputLook k = new InputLook();
            k.Bg = "panel"; k.Border = "border-strong"; k.Focus = "accent"; k.Fg = "text"; k.Placeholder = "muted";
            k.Radius = 10; k.FontSize = 14; k.MinHeight = 44; k.Padding = new Thickness(14, 0, 14, 0);
            k.Font = U.Inter;
            return k;
        }

        public static BtnLook BtnK(BtnKind kind)
        {
            BtnLook b = new BtnLook();
            b.Bg = "panel-2"; b.Border = "border-strong"; b.Fg = "text"; b.HoverBg = "panel-3"; b.HoverBorder = "hover-border";
            b.Radius = new CornerRadius(10); b.MinHeight = 44; b.Padding = new Thickness(16, 0, 16, 0); b.FontSize = 14; b.Weight = FontWeights.SemiBold;
            b.Font = U.Inter;
            if ((kind & BtnKind.Small) != 0) { b.MinHeight = 34; b.Padding = new Thickness(12, 0, 12, 0); b.FontSize = 13; b.Radius = new CornerRadius(9); }
            if ((kind & BtnKind.Green) != 0) { b.Fg = "accent"; b.Border = "accent-border"; b.Bg = "accent-bg"; }
            if ((kind & BtnKind.Solid) != 0) { b.Bg = "accent"; b.Fg = "accent-ink"; b.Border = Brushes.Transparent; b.HoverBg = null; b.HoverBorder = null; }
            if ((kind & BtnKind.Danger) != 0) { b.Fg = "danger"; b.Border = Css.Brush("rgba(248,113,113,0.5)"); b.Bg = "danger-bg"; }
            if ((kind & BtnKind.Ghost) != 0) { b.Bg = Brushes.Transparent; }
            return b;
        }

        /// <summary>A .btn with a label (and optional icon before it, 8 px apart).</summary>
        public static Button Btn(string label, BtnKind kind, Action click)
        {
            return Btn(label, null, kind, click);
        }

        public static Button Btn(string label, FrameworkElement icon, BtnKind kind, Action click)
        {
            Button b = U.Button(icon != null ? U.IconLabel(icon, label, 8) : (object)label, BtnK(kind));
            if (click != null) b.Click += delegate { click(); };
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Top;
            return b;
        }

        /// <summary>
        /// A button that runs an async action once at a time (app.js once()):
        /// disabled while it runs, its error as a toast.
        /// </summary>
        public static Button Busy(string label, BtnKind kind, Func<System.Threading.Tasks.Task> run, Action<string, bool> toast)
        {
            Button b = Btn(label, kind, null);
            b.Click += async delegate
            {
                if (!b.IsEnabled) return;
                b.IsEnabled = false;
                try { await run(); }
                catch (Exception ex) { if (toast != null) toast(ex.Message, true); }
                finally { b.IsEnabled = true; }
            };
            return b;
        }

        public static Control Input(string value, string placeholder)
        {
            Control c = U.Input(InputK(), false, value, placeholder);
            c.MinWidth = 190;
            return c;
        }

        public static Control Password()
        {
            Control c = U.Input(InputK(), true, null, null);
            c.MinWidth = 190;
            return c;
        }

        // ---------- text ----------
        public static RichLook RichK()
        {
            RichLook r = new RichLook();
            r.Mono = U.JetBrains;
            return r;
        }

        /// <summary>Text with the web's inline markup (code, b, links to #/routes).</summary>
        public static TextBlock RichBlock(Rich r, double size, string colorKey, Action<string> follow)
        {
            return RichText.Block(r, size, FontWeights.Normal, colorKey, RichK(), follow);
        }

        public static TextBlock P(string text)
        {
            return U.Text(text, 14, FontWeights.Normal, "text-2");
        }

        /// <summary>.muted.small.</summary>
        public static TextBlock Small(string text)
        {
            return U.Text(text, 12.5, FontWeights.Normal, "muted");
        }

        public static TextBlock Mono(string text, double size, string colorKey)
        {
            TextBlock t = U.Text(Format.Breakable(text), size, FontWeights.Normal, colorKey);
            t.FontFamily = U.JetBrains;
            return t;
        }

        /// <summary>h2 (20px 700 -0.01em).</summary>
        public static FrameworkElement H2(string text)
        {
            return U.Spaced(text, 20, FontWeights.Bold, -0.01, "text");
        }

        /// <summary>h3 (15px 650).</summary>
        public static TextBlock H3(string text)
        {
            return U.Text(text, 15, U.W(650), "text");
        }

        /// <summary>.hint: muted 13.5px, margin-bottom 18.</summary>
        public static TextBlock Hint(string text)
        {
            TextBlock t = U.Text(text, 13.5, FontWeights.Normal, "muted");
            t.Margin = new Thickness(0, 0, 0, 18);
            return t;
        }

        // ---------- blocks ----------
        /// <summary>
        /// .card: panel, 1px border, radius 16, padding 28 (20 at 760 px),
        /// margin-bottom 20. Its children stack; an h2 followed by a paragraph
        /// gets the lead style (CardLead).
        /// </summary>
        public static Border Card(params UIElement[] children)
        {
            StackPanel s = new StackPanel();
            foreach (UIElement c in children) if (c != null) s.Children.Add(c);
            Border b = U.Box(s, "panel", "border", 1, new CornerRadius(16), new Thickness(28));
            b.Margin = new Thickness(0, 0, 0, 20);
            Phone(b, p => b.Padding = new Thickness(p ? 20 : 28));
            return b;
        }

        /// <summary>The card's stack, to add to after making it.</summary>
        public static StackPanel Body(Border card)
        {
            return (StackPanel)card.Child;
        }

        /// <summary>.card h2 + p: 15.5px, line 1.6, text at 0.88, max 560, margin-top 12.</summary>
        public static TextBlock CardLead(Rich text, Action<string> follow)
        {
            TextBlock t = RichBlock(text, 15.5, "text", follow);
            t.Opacity = 0.88;
            t.MaxWidth = 560;
            t.HorizontalAlignment = HorizontalAlignment.Left;
            t.LineHeight = 15.5 * 1.6;
            t.Margin = new Thickness(0, 12, 0, 0);
            return t;
        }

        public static TextBlock CardLead(string text)
        {
            return CardLead(new Rich().Text(text), null);
        }

        /// <summary>
        /// .page-head: an eyebrow (13px text-2, margin-bottom 4) over the h1 (34px
        /// 700 -0.02em, line 1.15; 28px at 760 px) on the left, the actions (gap 12,
        /// wrapping, right aligned) and "Sign out" last on the right; margin-bottom 26.
        /// At 760 px the actions wrap under the title when they do not fit.
        /// </summary>
        public static FrameworkElement PageHead(string eyebrow, string title, ShellModel shell, params UIElement[] actions)
        {
            StackPanel left = new StackPanel();
            if (!string.IsNullOrEmpty(eyebrow))
            {
                TextBlock e = U.Text(eyebrow, 13, FontWeights.Normal, "text-2");
                e.Margin = new Thickness(0, 0, 0, 4);
                left.Children.Add(e);
            }
            SpacedText h1 = U.Spaced(title ?? "", 34, FontWeights.Bold, -0.02, "text");
            left.Children.Add(h1);
            Flex right = new Flex(Orientation.Horizontal, 12);
            right.Wrap = true;
            right.LineGap = 12;
            right.Justify = "end";
            foreach (UIElement a in actions) if (a != null) right.Children.Add(a);
            Button signOut = Btn("Sign out", BtnKind.Plain, () => { if (shell != null) shell.SignOut(); });
            signOut.Uid = "sign-out";
            right.Children.Add(signOut);
            // flex, space-between, wrapping at 760 px (gap 16)
            Flex head = new Flex(Orientation.Horizontal, 16);
            head.Justify = "between";
            head.LineGap = 16;
            head.Children.Add(Flex.Grow(left, 1));
            head.Children.Add(right);
            head.Margin = new Thickness(0, 0, 0, 26);
            Phone(head, p => { h1.FontSize = p ? 28 : 34; head.Wrap = p; head.InvalidateMeasure(); });
            return head;
        }

        /// <summary>.empty: padding 56x20, centred muted text, a dashed border-strong line, radius 16.</summary>
        public static FrameworkElement Empty(Rich text, Action<string> follow)
        {
            TextBlock t = RichBlock(text, 14, "muted", follow);
            t.TextAlignment = TextAlignment.Center;
            Grid g = new Grid();
            Rectangle dash = new Rectangle();
            dash.RadiusX = 16;
            dash.RadiusY = 16;
            dash.StrokeThickness = 1;
            dash.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
            dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "border-strong");
            dash.SnapsToDevicePixels = true;
            g.Children.Add(dash);
            Border pad = new Border();
            pad.Padding = Css.T(56, 20);
            pad.Child = t;
            g.Children.Add(pad);
            return g;
        }

        public static FrameworkElement Empty(string text)
        {
            return Empty(new Rich().Text(text), null);
        }

        /// <summary>.note (padding 14x16, radius 12, panel-2, border, 13.5px text-2, margin-top 14); green, amber, red.</summary>
        public static Border Note(Rich text, Tone tone, Action<string> follow)
        {
            string fg = tone == Tone.None ? "text-2" : "text";
            TextBlock t = RichBlock(text, 13.5, fg, follow);
            Border b = U.Box(t, "panel-2", "border", 1, new CornerRadius(12), Css.T(14, 16));
            b.Margin = new Thickness(0, 14, 0, 0);
            switch (tone)
            {
                case Tone.Green: b.SetResourceReference(Border.BorderBrushProperty, "accent-border"); b.SetResourceReference(Border.BackgroundProperty, "accent-bg"); break;
                case Tone.Amber: b.BorderBrush = Css.Brush("rgba(255,159,90,0.45)"); b.SetResourceReference(Border.BackgroundProperty, "warn-bg"); break;
                case Tone.Red: b.BorderBrush = Css.Brush("rgba(248,113,113,0.45)"); b.SetResourceReference(Border.BackgroundProperty, "danger-bg"); break;
            }
            return b;
        }

        public static Border Note(string text, Tone tone)
        {
            return Note(new Rich().Text(text), tone, null);
        }

        /// <summary>.chip: 3x10, radius 7, 12.5px 600, panel-2, border-strong; colours; an optional .cdot (7px, its colour).</summary>
        public static Border Chip(string text, Tone tone, bool dot)
        {
            TextBlock t = U.Text(text, 12.5, FontWeights.SemiBold, null);
            t.TextWrapping = TextWrapping.NoWrap;
            Flex f = Flex.Row(6);
            if (dot)
            {
                System.Windows.Shapes.Ellipse d = new System.Windows.Shapes.Ellipse();
                d.Width = 7;
                d.Height = 7;
                d.VerticalAlignment = VerticalAlignment.Center;
                // background: currentColor
                System.Windows.Data.Binding cur = new System.Windows.Data.Binding();
                cur.Path = new PropertyPath(TextElement.ForegroundProperty);
                cur.RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.Self);
                d.SetBinding(Shape.FillProperty, cur);
                f.Children.Add(d);
            }
            t.VerticalAlignment = VerticalAlignment.Center;
            f.Children.Add(t);
            Border b = U.Box(f, "panel-2", "border-strong", 1, new CornerRadius(7), Css.T(3, 10));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.SetResourceReference(TextElement.ForegroundProperty, "text");
            switch (tone)
            {
                case Tone.Green: b.SetResourceReference(TextElement.ForegroundProperty, "accent"); b.SetResourceReference(Border.BorderBrushProperty, "accent-border"); break;
                case Tone.Red: b.SetResourceReference(TextElement.ForegroundProperty, "danger"); b.BorderBrush = Css.Brush("rgba(248,113,113,0.5)"); break;
                case Tone.Amber: b.SetResourceReference(TextElement.ForegroundProperty, "warn"); b.BorderBrush = Css.Brush("rgba(255,159,90,0.5)"); break;
                case Tone.Blue: b.SetResourceReference(TextElement.ForegroundProperty, "info"); b.BorderBrush = Css.Brush("rgba(90,209,242,0.55)"); b.Background = Css.Brush("rgba(90,209,242,0.12)"); break;
                case Tone.Violet: TextElement.SetForeground(b, Css.Brush("#c4b5fd")); b.BorderBrush = Css.Brush("rgba(167,139,250,0.55)"); b.Background = Css.Brush("rgba(167,139,250,0.12)"); break;
            }
            return b;
        }

        /// <summary>.dot: 8px; ok accent with a glow, bad danger, warn warn, else muted.</summary>
        public static FrameworkElement Dot(string kind)
        {
            string key = kind == "ok" ? "accent" : kind == "bad" ? "danger" : kind == "warn" ? "warn" : "muted";
            System.Windows.Shapes.Ellipse e = U.Dot(8, key);
            e.VerticalAlignment = VerticalAlignment.Center;
            if (kind == "ok") e.Effect = Css.Glow(8, "rgba(200,242,90,0.55)");
            return e;
        }

        /// <summary>.kv: a 180 px key column and the values (gap 8 x 18, 13.5px, keys muted); one column at 760 px. Rows without a value are left out.</summary>
        public static FrameworkElement Kv(IList<KeyValuePair<string, FrameworkElement>> rows)
        {
            Grid g = new Grid();
            TextElement.SetFontSize(g, 13.5);
            List<KeyValuePair<string, FrameworkElement>> list = new List<KeyValuePair<string, FrameworkElement>>();
            foreach (KeyValuePair<string, FrameworkElement> r in rows) if (r.Value != null) list.Add(r);
            List<TextBlock> keys = new List<TextBlock>();
            foreach (KeyValuePair<string, FrameworkElement> r in list)
            {
                TextBlock k = U.Text(r.Key, 13.5, FontWeights.Normal, "muted");
                keys.Add(k);
                g.Children.Add(k);
                g.Children.Add(r.Value);
            }
            Action<bool> lay = phone =>
            {
                g.RowDefinitions.Clear();
                g.ColumnDefinitions.Clear();
                if (!phone)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
                for (int i = 0; i < list.Count; i++)
                {
                    double gap = i == 0 ? 0 : 8;
                    if (phone)
                    {
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        Grid.SetRow(keys[i], 2 * i); Grid.SetColumn(keys[i], 0);
                        Grid.SetRow(list[i].Value, 2 * i + 1); Grid.SetColumn(list[i].Value, 0);
                        keys[i].Margin = new Thickness(0, gap, 0, 0);
                        list[i].Value.Margin = new Thickness(0, 8, 0, 0);
                    }
                    else
                    {
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        Grid.SetRow(keys[i], i); Grid.SetColumn(keys[i], 0);
                        Grid.SetRow(list[i].Value, i); Grid.SetColumn(list[i].Value, 1);
                        keys[i].Margin = new Thickness(0, gap, 18, 0);
                        list[i].Value.Margin = new Thickness(0, gap, 0, 0);
                    }
                }
            };
            lay(false);
            Phone(g, lay);
            return g;
        }

        /// <summary>A value for <see cref="Kv"/>: plain text (13.5px, breaks long words), or null to leave the row out.</summary>
        public static FrameworkElement Val(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return U.Text(text, 13.5, FontWeights.Normal, "text");
        }

        public static KeyValuePair<string, FrameworkElement> Row(string key, FrameworkElement value)
        {
            return new KeyValuePair<string, FrameworkElement>(key, value);
        }

        /// <summary>label.field: the label above its control (column, gap 8, text-2 13px).</summary>
        public static FrameworkElement Field(string label, UIElement control)
        {
            return Flex.Col(8, U.Text(label, 13, FontWeights.Normal, "text-2"), control);
        }

        /// <summary>
        /// .form: two equal columns (gap 16 x 20), a "full" item across both; one
        /// column at 760 px. Items are (element, full).
        /// </summary>
        public static FrameworkElement Form(IList<KeyValuePair<FrameworkElement, bool>> items)
        {
            Grid g = new Grid();
            foreach (KeyValuePair<FrameworkElement, bool> it in items) g.Children.Add(it.Key);
            Action<bool> lay = phone =>
            {
                g.RowDefinitions.Clear();
                g.ColumnDefinitions.Clear();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (!phone) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                int row = 0, col = 0;
                foreach (KeyValuePair<FrameworkElement, bool> it in items)
                {
                    bool full = it.Value || phone;
                    if (full && col > 0) { row++; col = 0; }
                    if (g.RowDefinitions.Count <= row) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(it.Key, row);
                    Grid.SetColumn(it.Key, col);
                    Grid.SetColumnSpan(it.Key, full ? (phone ? 1 : 2) : 1);
                    it.Key.Margin = new Thickness(col == 1 ? 10 : 0, row == 0 ? 0 : 16, !full && col == 0 ? 10 : 0, 0);
                    if (full || col == 1) { row++; col = 0; } else col = 1;
                }
            };
            lay(false);
            Phone(g, lay);
            return g;
        }

        public static KeyValuePair<FrameworkElement, bool> Item(FrameworkElement e, bool full)
        {
            return new KeyValuePair<FrameworkElement, bool>(e, full);
        }

        /// <summary>.form-actions: right aligned, gap 12, margin-top 18, wrapping.</summary>
        public static Flex FormActions(params UIElement[] buttons)
        {
            Flex f = Flex.Row(12, buttons);
            f.Wrap = true;
            f.LineGap = 12;
            f.Justify = "end";
            f.Margin = new Thickness(0, 18, 0, 0);
            return f;
        }

        /// <summary>The web's error/empty box in place of a whole page (render()'s catch).</summary>
        public static FrameworkElement PageError(string message)
        {
            return Empty(message ?? "");
        }

        public static TableLook TableK()
        {
            return new TableLook();
        }

        /// <summary>details.more (summary text-2 15px; p margin-top 10, text-2, max 760).</summary>
        public static Details More(string summary, FrameworkElement content)
        {
            content.Margin = new Thickness(0, 10, 0, 0);
            content.MaxWidth = 760;
            content.HorizontalAlignment = HorizontalAlignment.Left;
            Details d = new Details(summary, content, false, 15);
            d.Root.Margin = new Thickness(0, 14, 0, 0);
            return d;
        }

        /// <summary>
        /// The vehicle pills (.pills: gap 10, wrap, margin-bottom 30): each pill
        /// a column (padding 12 20 12 18, radius 12, border-strong, panel, min
        /// 180): a row of the dot and the name (16px 600, gap 12) and the line
        /// under it (12px muted, 20px in); the current one with the accent
        /// border and a fade from accent-bg. "+ Add vehicle" last when asked (dashed).
        /// </summary>
        public static FrameworkElement Pills(ShellModel shell, Action add)
        {
            Flex f = Flex.Row(10);
            f.Wrap = true;
            f.LineGap = 10;
            f.Margin = new Thickness(0, 0, 0, 30);
            VehicleInfo cur = shell.Current;
            foreach (VehicleInfo v in shell.Vehicles)
            {
                bool active = cur != null && cur.Id == v.Id;
                TextBlock name = U.Text(v.Name, 16, FontWeights.SemiBold, "text");
                name.TextWrapping = TextWrapping.NoWrap;
                name.VerticalAlignment = VerticalAlignment.Center;
                Flex row = Flex.Row(12, Dot(v.Dot), name);
                TextBlock sub = U.Text(v.Sub, 12, FontWeights.Normal, "muted");
                sub.Margin = new Thickness(20, 0, 0, 0);
                StackPanel col = new StackPanel();
                col.Children.Add(row);
                sub.Margin = new Thickness(20, 2, 0, 0);
                col.Children.Add(sub);
                Border pill = U.Box(col, "panel", active ? "accent-border" : "border-strong", 1, new CornerRadius(12), Css.T(12, 20, 12, 18));
                pill.MinWidth = 180;
                if (active)
                {
                    // inset 0 0 0 1px accent-border: the border twice as thick; and the fade
                    pill.BorderThickness = new Thickness(2);
                    pill.Padding = Css.T(11, 19, 11, 17);
                    Color a = Css.C("#1AC8F25A");
                    pill.Background = new LinearGradientBrush(a, Color.FromArgb(0, a.R, a.G, a.B), 90);
                }
                string id = v.Id;
                pill.Uid = "pill-" + id;
                U.Click(pill, () => shell.PickVehicle(id));
                pill.FocusVisualStyle = null;
                f.Children.Add(pill);
            }
            if (add != null)
            {
                TextBlock plus = U.Text("+ Add vehicle", 14, FontWeights.Normal, "text-2");
                plus.VerticalAlignment = VerticalAlignment.Center;
                Grid g = new Grid();
                Rectangle dash = new Rectangle();
                dash.RadiusX = 12;
                dash.RadiusY = 12;
                dash.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
                dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "border-strong");
                dash.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "panel");
                g.Children.Add(dash);
                Border pad = new Border();
                pad.Padding = Css.T(12, 20, 12, 18);
                pad.Child = plus;
                g.Children.Add(pad);
                U.Click(g, add);
                g.Uid = "pill-add";
                f.Children.Add(g);
            }
            return f;
        }
    }
}
