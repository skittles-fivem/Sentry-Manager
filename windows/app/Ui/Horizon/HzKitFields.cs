using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// Horizon's form controls as Chrome draws them in the pack's CSS
    /// (color-scheme: dark): label.chk with its 20 px box or radio in the
    /// magenta accent, select.in with the two cyan chevron halves, and the
    /// .in number and time fields (Chrome's spin arrows and its hour : minute
    /// AM/PM parts), with the browser's own checks when a form is sent (the
    /// rules are Core's Policies, as the built-in page's FormFields.cs uses
    /// them). Used by the Policies and Encoding per vehicle pages.
    /// </summary>
    public static partial class HzKit
    {
        /// <summary>The browser's message under a field the form will not send (Chrome's bubble, drawn under the field).</summary>
        public static Border Bubble()
        {
            TextBlock t = Text("", 13.5, "ink");
            Border b = new Border();
            b.Child = t;
            b.Padding = Css.T(6, 10);
            b.CornerRadius = new CornerRadius(4);
            b.Background = Css.Brush("rgba(14,7,40,.97)");
            b.BorderBrush = Css.Brush("rgba(255,138,61,.7)");
            b.BorderThickness = new Thickness(1);
            b.Effect = Css.Shadow(4, 12, "rgba(0,0,0,.5)");
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Visibility = Visibility.Collapsed;
            return b;
        }

        public static void ShowBubble(Border b, string text)
        {
            ((TextBlock)b.Child).Text = text ?? "";
            b.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>label.chk: the box or radio (20 px) and its words 10 apart, 44 px tall at least, 15 px ink.</summary>
        public sealed class Check
        {
            public readonly FrameworkElement Root;
            readonly Grid mark;
            readonly Border box;
            readonly Ellipse ring, dot;
            readonly Path tick;
            readonly bool radio;
            bool on, enabled = true;
            public event Action<bool> Changed;

            public Check(string label, bool value, bool isRadio) : this(label, value, isRadio, 10)
            {
            }

            /// <param name="gap">The room between the box and its words (10; 6 in web/vehicleEncoding.js's .ve-choices).</param>
            public Check(string label, bool value, bool isRadio, double gap)
            {
                radio = isRadio;
                mark = new Grid();
                mark.Width = 20;
                mark.Height = 20;
                mark.VerticalAlignment = VerticalAlignment.Center;
                if (radio)
                {
                    ring = new Ellipse();
                    dot = new Ellipse();
                    dot.Width = 10;
                    dot.Height = 10;
                    dot.Fill = Css.Brush("#ff3fb4");
                    mark.Children.Add(ring);
                    mark.Children.Add(dot);
                }
                else
                {
                    box = new Border();
                    box.CornerRadius = new CornerRadius(3);
                    box.BorderThickness = new Thickness(1);
                    tick = new Path();
                    tick.Data = Geometry.Parse("M4.5 10.5 8.5 14.5 15.5 6");
                    tick.Stroke = Brushes.White;
                    tick.StrokeThickness = 2.2;
                    tick.StrokeStartLineCap = PenLineCap.Round;
                    tick.StrokeEndLineCap = PenLineCap.Round;
                    tick.StrokeLineJoin = PenLineJoin.Round;
                    box.Child = tick;
                    mark.Children.Add(box);
                }
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(mark);
                if (!string.IsNullOrEmpty(label))
                {
                    TextBlock t = Text(label, 15, "ink");
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.Margin = new Thickness(gap, 0, 0, 0);
                    Grid.SetColumn(t, 1);
                    g.Children.Add(t);
                }
                g.MinHeight = 44;
                g.Background = Brushes.Transparent;
                g.HorizontalAlignment = HorizontalAlignment.Left;
                Root = U.Click(g, () =>
                {
                    if (!enabled || (radio && on)) return;
                    Set(!on);
                    Action<bool> h = Changed;
                    if (h != null) h(on);
                });
                FocusRing.Set(g, 2, radio ? 10 : 3, false, mark);
                SentrySix.Ui.PageHost.SetField(g, true);
                AutomationName(g, label);
                Set(value);
            }

            public bool Value { get { return on; } }

            public bool Enabled
            {
                get { return enabled; }
                set
                {
                    enabled = value;
                    mark.Opacity = value ? 1 : 0.45;
                    Root.Cursor = value ? Cursors.Hand : Cursors.Arrow;
                    Root.Focusable = value;
                }
            }

            /// <summary>Chrome's dark scheme: on, the accent filled (a white tick); off, a dark box with a light edge.</summary>
            public void Set(bool v)
            {
                on = v;
                if (radio)
                {
                    ring.Stroke = Css.Brush(on ? "#ff3fb4" : "rgba(207,195,238,.7)");
                    ring.StrokeThickness = on ? 2 : 1;
                    ring.Fill = Css.Brush("#3b3b3b");
                    dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    box.Background = Css.Brush(on ? "#ff3fb4" : "#3b3b3b");
                    box.BorderBrush = Css.Brush(on ? "#ff3fb4" : "rgba(207,195,238,.7)");
                    tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                }
                System.Windows.Automation.AutomationProperties.SetItemStatus(Root, on ? "checked" : "");
            }
        }

        /// <summary>
        /// select.in: the field's box (44 px, radius 4, 16 px, 36 px on the
        /// right for the chevron), the chosen option's words, its list as a
        /// menu under it (#120a33); Up and Down change the choice.
        /// </summary>
        public sealed class Select
        {
            public readonly Border Root;
            readonly TextBlock label;
            readonly List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
            string value;
            public event Action<string> Changed;

            public Select(IEnumerable<KeyValuePair<string, string>> items, string selected)
            {
                if (items != null) options.AddRange(items);
                value = selected;
                label = Text("", 16, "ink");
                label.LineHeight = 16 * 1.3;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.TextWrapping = TextWrapping.NoWrap;
                label.VerticalAlignment = VerticalAlignment.Center;
                // the two 6 x 6 gradient halves (45deg and 135deg, cyan) side by side: a triangle pointing down, 12 px in from the right
                Path chev = new Path();
                chev.Data = Geometry.Parse("M0,0 L12,0 L6,6 Z");
                chev.SetResourceReference(Shape.FillProperty, "cyan");
                chev.Width = 12;
                chev.Height = 6;
                chev.Stretch = Stretch.None;
                chev.HorizontalAlignment = HorizontalAlignment.Right;
                chev.VerticalAlignment = VerticalAlignment.Center;
                chev.Margin = new Thickness(0, 0, -23, 0);
                chev.IsHitTestVisible = false;
                Grid g = new Grid();
                g.Children.Add(label);
                g.Children.Add(chev);
                Root = new RoundBorder();
                Root.Child = g;
                Root.Padding = new Thickness(12, 9, 36, 9);
                Root.CornerRadius = new CornerRadius(4);
                Root.BorderThickness = new Thickness(1);
                Root.SetResourceReference(Border.BackgroundProperty, "in-bg");
                Root.SetResourceReference(Border.BorderBrushProperty, "in-border");
                Root.MinHeight = 44;
                U.Click(Root, Open);
                FocusRing.Set(Root, 2, 4);
                Root.GotKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "cyan"); };
                Root.LostKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "in-border"); };
                Root.PreviewKeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.Key != Key.Down && e.Key != Key.Up) return;
                    int i = Index();
                    int j = Math.Max(0, Math.Min(options.Count - 1, i + (e.Key == Key.Down ? 1 : -1)));
                    if (j != i && options.Count > 0) Pick(options[j].Key);
                    e.Handled = true;
                };
                SentrySix.Ui.PageHost.SetField(Root, true);
                Paint();
            }

            public string Value { get { return value; } }

            int Index()
            {
                for (int i = 0; i < options.Count; i++) if (options[i].Key == value) return i;
                return -1;
            }

            void Paint()
            {
                int i = Index();
                label.Text = i >= 0 ? options[i].Value : options.Count > 0 ? options[0].Value : "";
            }

            void Pick(string v)
            {
                if (v == value) return;
                value = v;
                Paint();
                Action<string> h = Changed;
                if (h != null) h(v);
            }

            void Open()
            {
                ContextMenu menu = new ContextMenu();
                menu.Background = Css.Brush("#120a33");
                menu.Foreground = Css.Brush("#f6f0ff");
                menu.BorderBrush = Css.Brush("rgba(207,195,238,.38)");
                foreach (KeyValuePair<string, string> o in options)
                {
                    MenuItem item = new MenuItem();
                    item.Header = o.Value;
                    item.IsCheckable = true;
                    item.IsChecked = o.Key == value;
                    item.Foreground = Css.Brush("#f6f0ff");
                    string key = o.Key;
                    item.Click += delegate { Pick(key); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = Root;
                menu.Placement = PlacementMode.Bottom;
                menu.MinWidth = Root.ActualWidth;
                menu.IsOpen = true;
            }
        }

        /// <summary>
        /// input.in type=number as Chrome keeps it: the text as typed (the
        /// value "" when it is not a number), only digits + - . e E, Up and
        /// Down step by 1 within min..max, the spin arrows while the mouse is over it.
        /// </summary>
        public sealed class NumberField
        {
            public readonly Grid Root;
            public readonly TextBox Box;
            readonly double min, max;

            public NumberField(string value, string placeholder, double min, double max)
            {
                this.min = min;
                this.max = max;
                Box = Input(value, placeholder);
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
                FrameworkElement spin = Spinner();
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

            FrameworkElement Spinner()
            {
                Func<bool, FrameworkElement> arrow = up =>
                {
                    Path p = new Path();
                    p.Data = Geometry.Parse(up ? "M0 4 L3.5 0 L7 4 Z" : "M0 0 L3.5 4 L7 0 Z");
                    p.SetResourceReference(Shape.FillProperty, "dim");
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
        /// input.in type=time as Chrome draws it on Windows: the hour, the
        /// minute and (on a 12-hour Windows clock) AM/PM, "--:-- --" while
        /// empty, the clock symbol on the right. Click a part or move with
        /// Left, Right and Tab; digits fill a part, A or P, Up and Down step,
        /// Backspace or Delete empty a part; the clock symbol (Alt+Down, F4)
        /// opens the lists of hours, minutes and AM/PM. The value is "HH:MM"
        /// (24 h) or "" while a part is empty.
        /// </summary>
        public sealed class TimeField
        {
            public readonly Border Root;
            readonly bool h12;
            readonly string am, pm;
            int hour = -1, minute = -1, half = -1;
            int part;
            bool focused;
            string typed = "";
            readonly TextBlock[] parts;
            Popup popup;
            Border listBox;
            static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

            /// <summary>Enter in the field: the form is sent.</summary>
            public Action Submit;
            /// <summary>A part changed (the input event).</summary>
            public Action Changed;

            public TimeField(string value)
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
                FrameworkElement icon = Svg.Icon("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>", 16, Svg.Defaults.Outline(1.8));
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.SetResourceReference(TextElement.ForegroundProperty, "ink");
                Border iconHit = new Border();
                iconHit.Background = Brushes.Transparent;
                iconHit.Child = icon;
                iconHit.HorizontalAlignment = HorizontalAlignment.Right;
                iconHit.Cursor = Cursors.Arrow;
                iconHit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Root.Focus(); Open(); e.Handled = true; };
                Grid g = new Grid();
                g.Children.Add(row);
                g.Children.Add(iconHit);
                Root = new RoundBorder();
                Root.Child = g;
                Root.Padding = new Thickness(12, 9, 12, 9);
                Root.CornerRadius = new CornerRadius(4);
                Root.BorderThickness = new Thickness(1);
                Root.SetResourceReference(Border.BackgroundProperty, "in-bg");
                Root.SetResourceReference(Border.BorderBrushProperty, "in-border");
                Root.MinHeight = 44;
                Root.Focusable = true;
                FocusRing.Set(Root, 2, 4, true);
                Root.Cursor = Cursors.IBeam;
                KeyboardNavigation.SetIsTabStop(Root, true);
                SentrySix.Ui.PageHost.SetField(Root, true);
                Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Root.Focus(); e.Handled = true; };
                Root.GotKeyboardFocus += delegate { focused = true; Root.SetResourceReference(Border.BorderBrushProperty, "cyan"); Paint(); };
                Root.LostKeyboardFocus += delegate
                {
                    if (popup != null && popup.IsOpen) return;
                    focused = false;
                    Settle();
                    Root.SetResourceReference(Border.BorderBrushProperty, "in-border");
                    Paint();
                };
                Root.PreviewKeyDown += OnKey;
                Root.PreviewTextInput += delegate(object s, TextCompositionEventArgs e) { foreach (char c in e.Text) Typed(c); e.Handled = true; };
                Paint();
            }

            static TextBlock Plain(string text)
            {
                TextBlock t = Text(text, 16, "ink");
                t.TextWrapping = TextWrapping.NoWrap;
                t.Padding = new Thickness(1);
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

            void Set(int p, int v)
            {
                bool changed = Get(p) != v;
                if (p == 0) hour = v;
                else if (p == 1) minute = v;
                else half = v;
                if (changed && Changed != null) Changed();
            }

            void Paint()
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    int v = Get(i);
                    parts[i].Text = i == 2 ? (v < 0 ? "--" : v == 0 ? am : pm) : (v < 0 ? "--" : v.ToString("00", Inv));
                    if (focused && i == part)
                    {
                        parts[i].Background = SystemColors.HighlightBrush;
                        parts[i].Foreground = SystemColors.HighlightTextBrush;
                    }
                    else
                    {
                        parts[i].Background = Brushes.Transparent;
                        parts[i].SetResourceReference(TextBlock.ForegroundProperty, "ink");
                    }
                }
            }

            void Go(int p)
            {
                Settle();
                part = Math.Max(0, Math.Min(parts.Length - 1, p));
                Paint();
            }

            void Settle()
            {
                typed = "";
                if (h12 && part == 0 && hour == 0) hour = -1;
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
                    if (am.Length > 0 && u == char.ToUpperInvariant(am[0])) { Set(2, 0); Paint(); }
                    else if (pm.Length > 0 && u == char.ToUpperInvariant(pm[0])) { Set(2, 1); Paint(); }
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
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (popup != null && popup.IsOpen && (key == Key.Escape || key == Key.Enter))
                {
                    popup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
                switch (key)
                {
                    case Key.Left: Go(part - 1); e.Handled = true; break;
                    case Key.Right: Go(part + 1); e.Handled = true; break;
                    case Key.Tab:
                        if (!shift && part < parts.Length - 1) { Go(part + 1); e.Handled = true; }
                        else if (shift && part > 0) { Go(part - 1); e.Handled = true; }
                        else Settle();
                        break;
                    case Key.Up: Step(1); e.Handled = true; break;
                    case Key.Down:
                        if (alt) Open(); else Step(-1);
                        e.Handled = true;
                        break;
                    case Key.F4: Open(); e.Handled = true; break;
                    case Key.Back:
                    case Key.Delete:
                        typed = "";
                        Set(part, -1);
                        Paint();
                        e.Handled = true;
                        break;
                    case Key.Enter:
                        Settle();
                        Paint();
                        e.Handled = true;
                        if (Submit != null) Submit();
                        break;
                }
            }

            FrameworkElement Column(int p, IList<KeyValuePair<int, string>> items)
            {
                StackPanel s = new StackPanel();
                FrameworkElement chosen = null;
                foreach (KeyValuePair<int, string> it in items)
                {
                    int v = it.Key;
                    TextBlock t = Text(it.Value, 15, "ink");
                    t.TextAlignment = TextAlignment.Center;
                    Border b = new Border();
                    b.Child = t;
                    b.Padding = Css.T(4, 12);
                    b.CornerRadius = new CornerRadius(3);
                    if (Get(p) == v)
                    {
                        b.Background = SystemColors.HighlightBrush;
                        t.Foreground = SystemColors.HighlightTextBrush;
                        chosen = b;
                    }
                    else b.Background = Brushes.Transparent;
                    U.Hover(b, over => { if (Get(p) != v) b.Background = over ? Css.Brush("rgba(255,255,255,.08)") : Brushes.Transparent; });
                    b.MouseLeftButtonUp += delegate(object s2, MouseButtonEventArgs e)
                    {
                        typed = "";
                        Set(p, v);
                        part = p;
                        e.Handled = true;
                        Paint();
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
                listBox = new Border();
                listBox.Background = Css.Brush("#120a33");
                listBox.BorderBrush = Css.Brush("rgba(207,195,238,.38)");
                listBox.BorderThickness = new Thickness(1);
                listBox.CornerRadius = new CornerRadius(4);
                listBox.Padding = new Thickness(6);
                // (a popup is a window of its own: it carries Horizon's colours itself)
                listBox.Resources = new HorizonInterface().Theme();
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
                    if (e.Key == Key.Escape || e.Key == Key.Enter) { popup.IsOpen = false; e.Handled = true; }
                };
                popup.IsOpen = true;
            }
        }
    }
}
