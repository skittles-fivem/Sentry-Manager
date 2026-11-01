using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// Flux's command palette (flux/main.js openPalette(), drawPalette(),
    /// score(), runPalette(); flux.css .palette): a box 620 px wide 12% down
    /// the window, "Type a page, an action or a vehicle…", the page's own
    /// commands then the shell's (grouped while nothing is typed, best first
    /// when something is), ↑ ↓ to move, Enter or a click to run, Esc or a
    /// click outside to close. Ctrl K opens and closes it.
    /// </summary>
    public sealed class FxPalette
    {
        public readonly Grid Root = new Grid();
        readonly Border box = new RoundBorder();
        readonly TextBox input;
        readonly StackPanel list = new StackPanel();
        readonly ScrollViewer scroll = new ScrollViewer();
        readonly Func<List<PackCommand>> source;
        List<PackCommand> items = new List<PackCommand>();
        int index;
        IInputElement before;

        public const string Placeholder = "Type a page, an action or a vehicle…";
        public const string NoMatch = "No match";

        public FxPalette(Func<List<PackCommand>> commands)
        {
            source = commands;
            Root.Visibility = Visibility.Collapsed;
            Border backdrop = new Border();
            backdrop.Background = Css.Brush("rgba(10,11,16,.55)");
            backdrop.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Close(); e.Handled = true; };
            Root.Children.Add(backdrop);

            // .pal-head: the search icon, the field (16px, no frame), Esc
            FrameworkElement search = FxIcons.Icon("search");
            search.SetResourceReference(TextElement.ForegroundProperty, "muted");
            search.VerticalAlignment = VerticalAlignment.Center;
            InputLook k = FxKit.InK(false);
            k.Bg = "transparent-key"; k.Border = "transparent-key"; k.Focus = "transparent-key"; k.FontSize = 16; k.Padding = new Thickness(0);
            input = (TextBox)U.Input(k, false, "", Placeholder);
            input.Background = Brushes.Transparent;
            input.BorderBrush = Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetName(input, "Command");
            FrameworkElement esc = Kbd("Esc");
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(search);
            input.Margin = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(input, 1);
            head.Children.Add(input);
            Grid.SetColumn(esc, 2);
            esc.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(esc);
            Border headBox = new Border();
            headBox.Child = head;
            headBox.Padding = Css.T(12, 14);
            headBox.BorderThickness = new Thickness(0, 0, 0, 1);
            headBox.SetResourceReference(Border.BorderBrushProperty, "line-soft");

            list.Margin = new Thickness(6);
            scroll.Content = list;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.Focusable = false;

            // .pal-foot: ↑ ↓ move · Enter run · ? all shortcuts
            Flex foot = Flex.Row(14,
                Flex.Row(2, Kbd("↑"), Kbd("↓"), FootText(" move")),
                Flex.Row(2, Kbd("Enter"), FootText(" run")),
                Flex.Row(2, Kbd("?"), FootText(" all shortcuts")));
            Border footBox = new Border();
            footBox.Child = foot;
            footBox.Padding = Css.T(8, 14);
            footBox.BorderThickness = new Thickness(0, 1, 0, 0);
            footBox.SetResourceReference(Border.BorderBrushProperty, "line-soft");

            DockPanel d = new DockPanel();
            DockPanel.SetDock(headBox, Dock.Top);
            DockPanel.SetDock(footBox, Dock.Bottom);
            d.Children.Add(headBox);
            d.Children.Add(footBox);
            d.Children.Add(scroll);
            box.Child = d;
            box.Background = Css.Brush("#1a1c24");
            box.SetResourceReference(Border.BorderBrushProperty, "line");
            box.SetResourceReference(TextElement.ForegroundProperty, "text");
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(14);
            box.Effect = Css.Shadow(30, 80, "rgba(0,0,0,.6)");
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Top;
            Root.Children.Add(box);
            U.OnSize(Root, (w, h) =>
            {
                box.Width = Math.Min(620, w - 24);
                box.MaxHeight = Math.Min(560, h - 40);
                box.Margin = new Thickness(0, h * 0.12, 0, 0);
                bool coarse = FxKit.Coarse(w);
                input.MinHeight = coarse ? 44 : 0;
                input.FontSize = 16;
            });

            input.TextChanged += delegate { index = 0; Draw(); };
            input.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Down) { index = Math.Min(items.Count - 1, index + 1); Draw(); e.Handled = true; }
                else if (e.Key == Key.Up) { index = Math.Max(0, index - 1); Draw(); e.Handled = true; }
                else if (e.Key == Key.Enter) { Run(index); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            Root.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        }

        public bool IsOpen { get { return Root.Visibility == Visibility.Visible; } }

        static TextBlock FootText(string t)
        {
            TextBlock x = FxKit.Text(t, 11.5, "faint");
            x.VerticalAlignment = VerticalAlignment.Center;
            return x;
        }

        /// <summary>kbd: mono, the line around it.</summary>
        public static FrameworkElement Kbd(string text)
        {
            TextBlock t = FxKit.Text(text, 11, "text-2");
            t.FontFamily = FxKit.Mono;
            Border b = new RoundBorder();
            b.Child = t;
            b.Padding = Css.T(1, 5);
            b.CornerRadius = new CornerRadius(4);
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Border.BorderBrushProperty, "line");
            b.SetResourceReference(Border.BackgroundProperty, "panel-2");
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        /// <summary>score(label, q): a prefix scores highest, then a substring (earlier is better), then the letters in order.</summary>
        public static double Score(string label, string q)
        {
            if (string.IsNullOrEmpty(q)) return 1;
            string l = (label ?? "").ToLowerInvariant();
            if (l.StartsWith(q, StringComparison.Ordinal)) return 100 - l.Length / 100.0;
            int at = l.IndexOf(q, StringComparison.Ordinal);
            if (at >= 0) return 60 - at / 10.0;
            int i = 0;
            foreach (char ch in l) if (i < q.Length && ch == q[i]) i++;
            return i == q.Length ? 20 - l.Length / 100.0 : 0;
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            before = Keyboard.FocusedElement;
            input.Text = "";
            index = 0;
            Draw();
            Root.Visibility = Visibility.Visible;
            Root.Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(input)), DispatcherPriority.Input);
        }

        public void Close()
        {
            if (!IsOpen) return;
            Root.Visibility = Visibility.Collapsed;
            if (before != null) { try { Keyboard.Focus(before); } catch (Exception) { } }
        }

        void Run(int i)
        {
            if (i < 0 || i >= items.Count) return;
            PackCommand c = items[i];
            Close();
            Root.Dispatcher.BeginInvoke(new Action(() => { if (c.Run != null) c.Run(); }), DispatcherPriority.Background);
        }

        void Draw()
        {
            string q = (input.Text ?? "").Trim().ToLowerInvariant();
            List<KeyValuePair<PackCommand, double>> scored = new List<KeyValuePair<PackCommand, double>>();
            foreach (PackCommand c in source())
            {
                double s = Score(c.Group + " " + c.Label, q);
                if (s <= 0) s = Score(c.Label, q);
                if (s > 0) scored.Add(new KeyValuePair<PackCommand, double>(c, s));
            }
            // (a stable sort, as Array.prototype.sort is)
            if (q.Length > 0) scored = scored.Select((kv, n) => new { kv, n }).OrderByDescending(x => x.kv.Value).ThenBy(x => x.n).Select(x => x.kv).ToList();
            items = scored.Select(kv => kv.Key).ToList();
            index = Math.Min(index, Math.Max(0, items.Count - 1));
            list.Children.Clear();
            if (items.Count == 0)
            {
                TextBlock none = FxKit.Text(NoMatch, 13.5, "muted");
                none.Margin = new Thickness(14);
                list.Children.Add(none);
                return;
            }
            string last = "";
            for (int i = 0; i < items.Count; i++)
            {
                PackCommand c = items[i];
                if (q.Length == 0 && c.Group != last)
                {
                    last = c.Group;
                    TextBlock g = FxKit.Text((c.Group ?? "").ToUpperInvariant(), 10, "faint", 600);
                    g.FontFamily = FxKit.Mono;
                    g.Margin = Css.T(10, 10, 4, 10);
                    list.Children.Add(g);
                }
                bool on = i == index;
                FrameworkElement ico = FxIcons.Icon(c.Icon ?? "bolt");
                ico.SetResourceReference(TextElement.ForegroundProperty, on ? "violet" : "text-2");
                ico.VerticalAlignment = VerticalAlignment.Center;
                TextBlock label = FxKit.Text(c.Label, 13.5, null);
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(ico);
                label.Margin = new Thickness(10, 0, 10, 0);
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                if (!string.IsNullOrEmpty(c.Hint)) { FrameworkElement h = Kbd(c.Hint); Grid.SetColumn(h, 2); row.Children.Add(h); }
                Border b = new RoundBorder();
                b.Child = row;
                b.Padding = Css.T(8, 10);
                b.CornerRadius = new CornerRadius(8);
                if (on) b.SetResourceReference(Border.BackgroundProperty, "violet-soft"); else b.Background = Brushes.Transparent;
                b.SetResourceReference(TextElement.ForegroundProperty, on ? "text" : "text-2");
                b.Cursor = Cursors.Hand;
                int n = i;
                b.MouseLeftButtonUp += delegate { Run(n); };
                System.Windows.Automation.AutomationProperties.SetName(b, c.Label);
                list.Children.Add(b);
                if (on) b.Loaded += delegate { b.BringIntoView(); };
            }
        }
    }
}
