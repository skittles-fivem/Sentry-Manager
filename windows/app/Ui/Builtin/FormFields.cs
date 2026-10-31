using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// The browser's message under a field the form will not send (Chrome's
    /// validation bubble, drawn under the field instead of floating).
    /// </summary>
    public static class FieldBubble
    {
        public static Border Make()
        {
            TextBlock t = U.Text("", 13, FontWeights.Normal, "text");
            Border b = U.Box(t, "panel-3", "border-strong", 1, new CornerRadius(6), Css.T(6, 10));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Effect = Css.Shadow(4, 12, "rgba(0,0,0,0.4)");
            b.Visibility = Visibility.Collapsed;
            return b;
        }

        public static void Show(Border b, string text)
        {
            ((TextBlock)b.Child).Text = text ?? "";
            b.Visibility = Visibility.Visible;
        }

        public static void Hide(Border b)
        {
            b.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// The web's &lt;input class="input" type="number" min max&gt; as Chrome
    /// draws it: the field as the others, the typed text kept as typed (the
    /// value is "" when it is not a number), only the characters Chrome takes
    /// (digits, + - . e E), ArrowUp and ArrowDown step by 1 within min..max,
    /// and the small up and down arrows on the right while the mouse is over it.
    /// </summary>
    public sealed class NumberBox
    {
        public readonly Grid Root;
        public readonly TextBox Box;
        readonly double min, max;
        readonly FrameworkElement spin;

        public NumberBox(string value, string placeholder, double min, double max)
        {
            this.min = min;
            this.max = max;
            Box = (TextBox)Look.Input(value, placeholder);
            Box.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
            {
                foreach (char c in e.Text) if (!Policies.NumberChar(c)) { e.Handled = true; return; }
            };
            DataObject.AddPastingHandler(Box, delegate(object s, DataObjectPastingEventArgs e)
            {
                string text = e.DataObject.GetDataPresent(DataFormats.UnicodeText) ? e.DataObject.GetData(DataFormats.UnicodeText) as string : null;
                if (text == null) { e.CancelCommand(); return; }
                foreach (char c in text) if (!Policies.NumberChar(c)) { e.CancelCommand(); return; }
            });
            Box.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Up) { Step(1); e.Handled = true; }
                else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
                else if (e.Key == Key.Space) e.Handled = true;
            };
            spin = Spinner();
            spin.Visibility = Visibility.Collapsed;
            Root = new Grid();
            Root.Children.Add(Box);
            Root.Children.Add(spin);
            Root.MouseEnter += delegate { spin.Visibility = Visibility.Visible; };
            Root.MouseLeave += delegate { spin.Visibility = Visibility.Collapsed; };
        }

        /// <summary>The value FormData reads: the text when it is a number, else "".</summary>
        public string Value { get { return Policies.NumberValue(Box.Text); } }

        /// <summary>What Chrome says when the form is sent (null: fine).</summary>
        public string Message { get { return Policies.NumberMessage(Box.Text, min, max); } }

        void Step(int dir)
        {
            string next = Policies.NumberStep(Box.Text, dir, min, max);
            if (next == Box.Text) return;
            Box.Text = next;
            Box.CaretIndex = next.Length;
        }

        // ::-webkit-inner-spin-button in the dark scheme: two small arrows, 15 px wide, 8 px in from the right
        FrameworkElement Spinner()
        {
            Func<bool, FrameworkElement> arrow = up =>
            {
                Path p = new Path();
                p.Data = Geometry.Parse(up ? "M0 4 L3.5 0 L7 4 Z" : "M0 0 L3.5 4 L7 0 Z");
                p.SetResourceReference(Shape.FillProperty, "text-2");
                p.HorizontalAlignment = HorizontalAlignment.Center;
                p.VerticalAlignment = VerticalAlignment.Center;
                Border b = new Border();
                b.Background = Brushes.Transparent;
                b.Height = 12;
                b.Child = p;
                b.Cursor = Cursors.Arrow;
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    Step(up ? 1 : -1);
                    Box.Focus();
                    e.Handled = true;
                };
                return b;
            };
            StackPanel s2 = new StackPanel();
            s2.Width = 15;
            s2.Children.Add(arrow(true));
            s2.Children.Add(arrow(false));
            s2.HorizontalAlignment = HorizontalAlignment.Right;
            s2.VerticalAlignment = VerticalAlignment.Center;
            s2.Margin = new Thickness(0, 0, 8, 0);
            return s2;
        }
    }

    /// <summary>
    /// The web's &lt;input class="input" type="time"&gt; as Chrome draws it on
    /// Windows: the field as the others, with an hour, a minute and (on a
    /// 12-hour Windows clock) an AM/PM part ("--:-- --" while empty) and the
    /// clock symbol on the right. Click a part or move with Left, Right and
    /// Tab; type digits (a part moves on when it is full), A or P, Up and
    /// Down to step, Backspace or Delete to empty a part. The clock symbol
    /// (or Alt+Down, F4) opens a list of hours, minutes and AM/PM under it.
    /// The value is "HH:MM" (24 h, as the browser gives it) or "" while a
    /// part is empty.
    /// </summary>
    public sealed class TimeBox
    {
        public readonly Border Root;
        readonly bool h12;
        readonly string am, pm;
        // the parts: hour (1..12 on a 12-hour clock, else 0..23), minute, AM (0) / PM (1); -1 empty
        int hour = -1, minute = -1, half = -1;
        int part;
        bool focused;
        string typed = "";
        readonly TextBlock[] parts;
        Popup popup;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public TimeBox(string value)
        {
            DateTimeFormatInfo f = CultureInfo.CurrentCulture.DateTimeFormat;
            h12 = f.ShortTimePattern.IndexOf('H') < 0;
            am = string.IsNullOrEmpty(f.AMDesignator) ? "AM" : f.AMDesignator;
            pm = string.IsNullOrEmpty(f.PMDesignator) ? "PM" : f.PMDesignator;
            int h, m;
            if (Policies.ParseTime(value, out h, out m))
            {
                minute = m;
                if (h12) { hour = h % 12 == 0 ? 12 : h % 12; half = h < 12 ? 0 : 1; }
                else hour = h;
            }
            InputLook k = Look.InputK();
            parts = new TextBlock[h12 ? 3 : 2];
            Flex row = Flex.Row(0);
            row.VerticalAlignment = VerticalAlignment.Center;
            row.HorizontalAlignment = HorizontalAlignment.Left;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 1) row.Children.Add(Plain(":"));
                if (i == 2) row.Children.Add(Plain(" "));
                TextBlock t = Plain("");
                int which = i;
                t.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Go(which); Root.Focus(); e.Handled = true; };
                parts[i] = t;
                row.Children.Add(t);
            }
            // ::-webkit-calendar-picker-indicator of a time field in the dark scheme
            FrameworkElement icon = Svg.Icon("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>", 16, Svg.Defaults.Outline(1.8));
            icon.HorizontalAlignment = HorizontalAlignment.Right;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "text");
            Border iconHit = new Border();
            iconHit.Background = Brushes.Transparent;
            iconHit.Child = icon;
            iconHit.HorizontalAlignment = HorizontalAlignment.Right;
            iconHit.Cursor = Cursors.Arrow;
            iconHit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Root.Focus(); Open(); e.Handled = true; };
            Grid g = new Grid();
            g.Children.Add(row);
            g.Children.Add(iconHit);
            Root = U.Box(g, k.Bg, k.Border, 1, new CornerRadius(k.Radius), k.Padding);
            Root.MinHeight = k.MinHeight;
            Root.Focusable = true;
            // input:focus-visible (a time field matches it however it was focused)
            FocusRing.Set(Root, 2, k.Radius, true);
            Root.Cursor = Cursors.IBeam;
            KeyboardNavigation.SetIsTabStop(Root, true);
            PageHost.SetField(Root, true);
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Root.Focus(); e.Handled = true; };
            Root.GotKeyboardFocus += delegate { focused = true; Root.SetResourceReference(Border.BorderBrushProperty, "accent"); Paint(); };
            Root.LostKeyboardFocus += delegate(object s, KeyboardFocusChangedEventArgs e)
            {
                if (popup != null && popup.IsOpen) return;
                focused = false;
                Leave();
            };
            Root.PreviewKeyDown += OnKey;
            Root.PreviewTextInput += delegate(object s, TextCompositionEventArgs e) { foreach (char c in e.Text) Typed(c); e.Handled = true; };
            Paint();
        }

        /// <summary>Enter in the field: the form is sent.</summary>
        public Action Submit;

        static TextBlock Plain(string text)
        {
            TextBlock t = U.Text(text, 14, FontWeights.Normal, "text");
            t.FontFamily = U.Inter;
            t.TextWrapping = TextWrapping.NoWrap;
            t.Padding = new Thickness(1, 1, 1, 1);
            return t;
        }

        /// <summary>The value FormData reads: "HH:MM", or "" while a part is empty.</summary>
        public string Value
        {
            get
            {
                if (hour < 0 || minute < 0 || (h12 && (half < 0 || hour == 0))) return "";
                int h = h12 ? hour % 12 + (half == 1 ? 12 : 0) : hour;
                return Policies.TimeValue(h, minute);
            }
        }

        /// <summary>Chrome's message when the form is sent: a field filled in only partly.</summary>
        public string Message
        {
            get
            {
                bool any = hour >= 0 || minute >= 0 || (h12 && half >= 0);
                return any && Value.Length == 0 ? Policies.TimeIncomplete : null;
            }
        }

        public void Focus()
        {
            Root.Focus();
        }

        int Max(int p) { return p == 0 ? (h12 ? 12 : 23) : p == 1 ? 59 : 1; }
        int Min(int p) { return p == 0 && h12 ? 1 : 0; }
        int Get(int p) { return p == 0 ? hour : p == 1 ? minute : half; }
        /// <summary>A part changed (the input event).</summary>
        public Action Changed;

        void Set(int p, int v)
        {
            if (Get(p) != v && Changed != null) Changed();
            if (p == 0) hour = v;
            else if (p == 1) minute = v;
            else half = v;
        }

        void Paint()
        {
            for (int i = 0; i < parts.Length; i++)
            {
                int v = Get(i);
                parts[i].Text = i == 2 ? (v < 0 ? "--" : v == 0 ? am : pm) : (v < 0 ? "--" : v.ToString("00", Inv));
                if (focused && i == part)
                {
                    // ::-webkit-datetime-edit-*-field:focus: the system's highlight
                    parts[i].Background = SystemColors.HighlightBrush;
                    parts[i].Foreground = SystemColors.HighlightTextBrush;
                }
                else
                {
                    parts[i].Background = Brushes.Transparent;
                    parts[i].SetResourceReference(TextBlock.ForegroundProperty, "text");
                }
            }
        }

        void Go(int p)
        {
            Settle();
            part = Math.Max(0, Math.Min(parts.Length - 1, p));
            Paint();
        }

        // a 12-hour clock's hour typed as "0" alone is no hour
        void Settle()
        {
            typed = "";
            if (h12 && part == 0 && hour == 0) hour = -1;
        }

        void Leave()
        {
            Settle();
            Root.SetResourceReference(Border.BorderBrushProperty, "border-strong");
            Paint();
        }

        void Step(int dir)
        {
            typed = "";
            int v = Get(part), lo = Min(part), hi = Max(part);
            if (v < 0) v = dir > 0 ? lo : hi;
            else
            {
                v += dir;
                if (v > hi) v = lo;
                if (v < lo) v = hi;
            }
            Set(part, v);
            Paint();
        }

        void Typed(char c)
        {
            if (part == 2)
            {
                char u = char.ToUpperInvariant(c);
                if (am.Length > 0 && u == char.ToUpperInvariant(am[0])) { half = 0; Paint(); }
                else if (pm.Length > 0 && u == char.ToUpperInvariant(pm[0])) { half = 1; Paint(); }
                return;
            }
            if (c < '0' || c > '9') return;
            int d = c - '0';
            int hi = Max(part);
            int v = typed.Length == 1 ? (typed[0] - '0') * 10 + d : d;
            if (typed.Length == 1 && v > hi) v = d;
            Set(part, v);
            bool full = typed.Length == 1 || v * 10 > hi;
            if (full)
            {
                if (h12 && part == 0 && v == 0) { typed = c.ToString(); Paint(); return; }
                typed = "";
                if (part < parts.Length - 1) part++;
            }
            else typed = c.ToString();
            Paint();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            System.Windows.Input.Key key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (popup != null && popup.IsOpen && (key == System.Windows.Input.Key.Escape || key == System.Windows.Input.Key.Enter))
            {
                popup.IsOpen = false;
                e.Handled = true;
                return;
            }
            switch (key)
            {
                case System.Windows.Input.Key.Left: Go(part - 1); e.Handled = true; break;
                case System.Windows.Input.Key.Right: Go(part + 1); e.Handled = true; break;
                case System.Windows.Input.Key.Tab:
                    // Tab moves through the parts, then on to the next control
                    if (!shift && part < parts.Length - 1) { Go(part + 1); e.Handled = true; }
                    else if (shift && part > 0) { Go(part - 1); e.Handled = true; }
                    else Settle();
                    break;
                case System.Windows.Input.Key.Up: Step(1); e.Handled = true; break;
                case System.Windows.Input.Key.Down:
                    if (alt) Open(); else Step(-1);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.F4: Open(); e.Handled = true; break;
                case System.Windows.Input.Key.Back:
                case System.Windows.Input.Key.Delete:
                    typed = "";
                    Set(part, -1);
                    Paint();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Enter:
                    Settle();
                    Paint();
                    e.Handled = true;
                    if (Submit != null) Submit();
                    break;
            }
        }

        // ---------------------------------------------------------------- the list under the field

        FrameworkElement Column(int p, IList<KeyValuePair<int, string>> items)
        {
            StackPanel s = new StackPanel();
            FrameworkElement chosen = null;
            foreach (KeyValuePair<int, string> it in items)
            {
                int v = it.Key;
                TextBlock t = U.Text(it.Value, 14, FontWeights.Normal, "text");
                t.FontFamily = U.Inter;
                t.TextAlignment = TextAlignment.Center;
                Border b = new Border();
                b.Child = t;
                b.Padding = Css.T(5, 12);
                b.CornerRadius = new CornerRadius(4);
                bool on = Get(p) == v;
                if (on)
                {
                    b.Background = SystemColors.HighlightBrush;
                    t.Foreground = SystemColors.HighlightTextBrush;
                    chosen = b;
                }
                else b.Background = Brushes.Transparent;
                U.Hover(b, over => { if (Get(p) != v) b.SetResourceReference(Border.BackgroundProperty, over ? "panel-2" : "panel-3"); });
                b.MouseLeftButtonUp += delegate(object s2, MouseButtonEventArgs e)
                {
                    typed = "";
                    Set(p, v);
                    part = p;
                    e.Handled = true;
                    Paint();
                    // the list stays open for the other parts, as Chrome's does
                    Refill();
                };
                s.Children.Add(b);
            }
            ScrollViewer sv = new ScrollViewer();
            sv.Content = s;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            sv.Height = 220;
            sv.Width = 56;
            if (chosen != null)
            {
                FrameworkElement c = chosen;
                sv.Loaded += delegate { c.BringIntoView(); };
            }
            return sv;
        }

        Border listBox;

        void Refill()
        {
            if (listBox == null) return;
            List<KeyValuePair<int, string>> hours = new List<KeyValuePair<int, string>>();
            for (int h = Min(0); h <= Max(0); h++) hours.Add(new KeyValuePair<int, string>(h, h.ToString("00", Inv)));
            List<KeyValuePair<int, string>> minutes = new List<KeyValuePair<int, string>>();
            for (int m = 0; m < 60; m++) minutes.Add(new KeyValuePair<int, string>(m, m.ToString("00", Inv)));
            Flex cols = Flex.Row(4, Column(0, hours), Column(1, minutes));
            if (h12)
            {
                List<KeyValuePair<int, string>> halves = new List<KeyValuePair<int, string>>();
                halves.Add(new KeyValuePair<int, string>(0, am));
                halves.Add(new KeyValuePair<int, string>(1, pm));
                cols.Children.Add(Column(2, halves));
            }
            listBox.Child = cols;
        }

        void Open()
        {
            if (popup != null && popup.IsOpen) return;
            Settle();
            listBox = U.Box(null, "panel-3", "border-strong", 1, new CornerRadius(10), new Thickness(6));
            listBox.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "text");
            // (a popup is a window of its own: it carries the theme itself)
            listBox.Resources = Interfaces.Builtin.Theme();
            Refill();
            popup = new Popup();
            popup.Child = listBox;
            popup.PlacementTarget = Root;
            popup.Placement = PlacementMode.Bottom;
            popup.VerticalOffset = 4;
            popup.StaysOpen = false;
            popup.AllowsTransparency = true;
            popup.Closed += delegate { listBox = null; Root.Focus(); Paint(); };
            listBox.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Enter) { popup.IsOpen = false; e.Handled = true; }
            };
            popup.IsOpen = true;
        }
    }
}
