using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentrySix.Ui.Builtin
{
    /// <summary>
    /// A checkbox drawn as Chrome draws one with accent-color (17 px by default,
    /// the theme's accent when on), with its label 10 px to the right (.check).
    /// Set <see cref="Kept"/> for a switch the page keeps itself (the web's
    /// data-kept): a live redraw does not wait for it.
    /// </summary>
    public sealed class CheckBoxLike
    {
        public readonly FrameworkElement Root;
        readonly Border box;
        readonly Path tick;
        bool on;
        bool enabled = true;
        readonly Action<bool> changed;

        public CheckBoxLike(string label, bool value, Action<bool> changed) : this(label, value, changed, 17, 14, "text-2")
        {
        }

        public CheckBoxLike(string label, bool value, Action<bool> changed, double size, double fontSize, string labelKey)
        {
            this.changed = changed;
            box = new Border();
            box.Width = size;
            box.Height = size;
            box.CornerRadius = new CornerRadius(3);
            box.BorderThickness = new Thickness(1);
            tick = new Path();
            tick.Data = Geometry.Parse("M3.5 8.5 7 12 13.5 4.5");
            tick.StrokeThickness = 2;
            tick.SetResourceReference(Shape.StrokeProperty, "accent-ink");
            tick.StrokeStartLineCap = PenLineCap.Round;
            tick.StrokeEndLineCap = PenLineCap.Round;
            tick.StrokeLineJoin = PenLineJoin.Round;
            if (size != 17)
            {
                tick.Stretch = Stretch.None;
                tick.RenderTransform = new ScaleTransform(size / 17, size / 17);
            }
            box.Child = tick;
            box.VerticalAlignment = VerticalAlignment.Center;
            Flex f;
            if (!string.IsNullOrEmpty(label))
            {
                TextBlock t = U.Text(label, fontSize, FontWeights.Normal, labelKey);
                t.TextWrapping = TextWrapping.NoWrap;
                t.VerticalAlignment = VerticalAlignment.Center;
                f = Flex.Row(10, box, t);
            }
            else f = Flex.Row(0, box);
            f.Background = Brushes.Transparent;
            Root = U.Click(f, () => { if (!enabled) return; Set(!on); if (this.changed != null) this.changed(on); });
            Root.FocusVisualStyle = null;
            Set(value);
        }

        public bool Value { get { return on; } }

        /// <summary>A switch the page redraws itself (data-kept).</summary>
        public bool Kept;

        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; Root.Opacity = value ? 1 : 0.45; Root.Cursor = value ? Cursors.Hand : Cursors.Arrow; }
        }

        public void Set(bool v)
        {
            on = v;
            if (on)
            {
                box.SetResourceReference(Border.BackgroundProperty, "accent");
                box.SetResourceReference(Border.BorderBrushProperty, "accent");
                tick.Visibility = Visibility.Visible;
            }
            else
            {
                box.SetResourceReference(Border.BackgroundProperty, "check-off");
                box.SetResourceReference(Border.BorderBrushProperty, "check-off-border");
                tick.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// A radio button drawn as Chrome draws one with accent-color (17 px:
    /// the accent ring and dot when chosen, the dark scheme's grey circle
    /// when not; faded when disabled), its words <see cref="Gap"/> px to the
    /// right. Picking it calls back; the page unticks the others of its name.
    /// Disabled, the button is greyed and its words are not (the label is
    /// not disabled, only its input).
    /// </summary>
    public sealed class RadioLike
    {
        public readonly FrameworkElement Root;
        readonly Ellipse ring, dot;
        readonly Grid circle;
        readonly TextBlock text;
        bool on, enabled = true;
        readonly Action picked;

        public RadioLike(string label, bool value, Action picked, double gap)
        {
            this.picked = picked;
            ring = new Ellipse();
            ring.Width = 17;
            ring.Height = 17;
            ring.StrokeThickness = 1;
            dot = new Ellipse();
            dot.Width = 9;
            dot.Height = 9;
            dot.HorizontalAlignment = HorizontalAlignment.Center;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.SetResourceReference(Shape.FillProperty, "accent");
            Grid g = new Grid();
            g.Width = 17;
            g.Height = 17;
            g.Children.Add(ring);
            g.Children.Add(dot);
            g.VerticalAlignment = VerticalAlignment.Center;
            circle = g;
            text = U.Text(label ?? "", 14, FontWeights.Normal, "text");
            text.TextWrapping = TextWrapping.NoWrap;
            text.VerticalAlignment = VerticalAlignment.Center;
            Flex f = Flex.Row(gap, g, text);
            f.Background = Brushes.Transparent;
            Root = U.Click(f, () => { if (!enabled || on) return; Set(true); if (this.picked != null) this.picked(); });
            Root.FocusVisualStyle = null;
            Set(value);
        }

        public bool Value { get { return on; } }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                // (only the button is greyed: its label's words stay as they are)
                circle.Opacity = value ? 1 : 0.5;
                Root.Cursor = value ? Cursors.Hand : Cursors.Arrow;
                Root.Focusable = value;
            }
        }

        public void Set(bool v)
        {
            on = v;
            if (on)
            {
                ring.SetResourceReference(Shape.StrokeProperty, "accent");
                ring.StrokeThickness = 2;
                ring.SetResourceReference(Shape.FillProperty, "check-off");
                dot.Visibility = Visibility.Visible;
            }
            else
            {
                ring.SetResourceReference(Shape.StrokeProperty, "check-off-border");
                ring.StrokeThickness = 1;
                ring.SetResourceReference(Shape.FillProperty, "check-off");
                dot.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// The web's select.input: the box as the other fields (44 px, radius 10,
    /// 40 px on the right for the chevron at right 15), its list opening as a
    /// menu under it; Up and Down change the choice from the keyboard as a
    /// select does.
    /// </summary>
    public sealed class SelectBox
    {
        public readonly Border Root;
        readonly TextBlock label;
        readonly List<KeyValuePair<string, string>> options = new List<KeyValuePair<string, string>>();
        string value;
        readonly Action<string> changed;
        bool enabled = true;

        public SelectBox(IEnumerable<KeyValuePair<string, string>> items, string selected, Action<string> changed)
        {
            this.changed = changed;
            if (items != null) options.AddRange(items);
            value = selected;
            InputLook k = Look.InputK();
            label = new TextBlock();
            label.FontSize = k.FontSize;
            label.FontFamily = U.Inter;
            label.SetResourceReference(TextBlock.ForegroundProperty, k.Fg);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            Path chevron = new Path();
            chevron.Data = Geometry.Parse("M1 1.5 L6 6.5 L11 1.5");
            chevron.Stroke = Css.Brush("#9aa3b2");
            chevron.StrokeThickness = 1.6;
            chevron.StrokeStartLineCap = PenLineCap.Round;
            chevron.StrokeEndLineCap = PenLineCap.Round;
            chevron.StrokeLineJoin = PenLineJoin.Round;
            chevron.Width = 12;
            chevron.Height = 8;
            chevron.HorizontalAlignment = HorizontalAlignment.Right;
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevron.Margin = new Thickness(0, 0, -25, 0); // right 15 px inside the 40 px padding
            chevron.IsHitTestVisible = false;
            Grid g = new Grid();
            g.Children.Add(label);
            g.Children.Add(chevron);
            Root = U.Box(g, k.Bg, k.Border, 1, new CornerRadius(k.Radius), new Thickness(14, 0, 40, 0));
            Root.MinHeight = k.MinHeight;
            Root.MinWidth = 190;
            U.Click(Root, Open);
            Root.FocusVisualStyle = null;
            Root.GotKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "accent"); };
            Root.LostKeyboardFocus += delegate { Root.SetResourceReference(Border.BorderBrushProperty, "border-strong"); };
            Root.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!enabled) return;
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    int i = Index();
                    int j = Math.Max(0, Math.Min(options.Count - 1, i + (e.Key == Key.Down ? 1 : -1)));
                    if (j != i && options.Count > 0) Pick(options[j].Key);
                    e.Handled = true;
                }
            };
            Paint();
        }

        public string Value { get { return value; } }

        /// <summary>The chosen option's text size (a select takes its field's font: 13 px in a .field, 16 px on a touch screen).</summary>
        public double FontSize { get { return label.FontSize; } set { label.FontSize = value; } }

        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; Root.Opacity = value ? 1 : 0.65; Root.Cursor = value ? Cursors.Hand : Cursors.Arrow; }
        }

        int Index()
        {
            for (int i = 0; i < options.Count; i++) if (options[i].Key == value) return i;
            return -1;
        }

        void Paint()
        {
            int i = Index();
            label.Text = i >= 0 ? options[i].Value : (options.Count > 0 && value == null ? options[0].Value : "");
        }

        void Pick(string v)
        {
            if (v == value) return;
            value = v;
            Paint();
            if (changed != null) changed(v);
        }

        void Open()
        {
            if (!enabled) return;
            ContextMenu menu = new ContextMenu();
            foreach (KeyValuePair<string, string> o in options)
            {
                MenuItem item = new MenuItem();
                item.Header = o.Value;
                item.IsCheckable = true;
                item.IsChecked = o.Key == value;
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

    /// <summary>.seg: buttons side by side in one bordered box; the chosen one .on (accent).</summary>
    public sealed class Seg
    {
        public readonly Border Root;
        readonly Dictionary<string, Border> buttons = new Dictionary<string, Border>();
        string value;
        readonly Action<string> changed;

        /// <param name="padding">.seg button: 10px 16px (the Overview's range: 6px 11px, 12.5px).</param>
        public Seg(IEnumerable<KeyValuePair<string, string>> items, string selected, Action<string> changed, Thickness padding, double fontSize)
        {
            this.changed = changed;
            value = selected;
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            foreach (KeyValuePair<string, string> o in items)
            {
                TextBlock t = U.Text(o.Value, fontSize, FontWeights.SemiBold, null);
                t.TextWrapping = TextWrapping.NoWrap;
                Border b = new Border();
                b.Child = t;
                b.Padding = padding;
                string key = o.Key;
                b.Uid = "seg-" + key;
                U.Click(b, () => Pick(key));
                b.FocusVisualStyle = null;
                buttons[key] = b;
                row.Children.Add(b);
            }
            Root = U.Box(row, null, "border-strong", 1, new CornerRadius(10), new Thickness(0));
            Root.HorizontalAlignment = HorizontalAlignment.Left;
            Root.ClipToBounds = true;
            Paint();
        }

        public Seg(IEnumerable<KeyValuePair<string, string>> items, string selected, Action<string> changed)
            : this(items, selected, changed, Css.T(10, 16), 14)
        {
        }

        public string Value { get { return value; } }

        void Pick(string v)
        {
            value = v;
            Paint();
            if (changed != null) changed(v);
        }

        void Paint()
        {
            foreach (KeyValuePair<string, Border> kv in buttons)
            {
                bool on = kv.Key == value;
                kv.Value.SetResourceReference(Border.BackgroundProperty, on ? "accent-bg" : "panel");
                kv.Value.SetResourceReference(TextElement.ForegroundProperty, on ? "accent" : "text-2");
            }
        }
    }

    /// <summary>details &gt; summary: a clickable line (text-2) that shows or hides what follows.</summary>
    public sealed class Details
    {
        public readonly StackPanel Root = new StackPanel();
        readonly FrameworkElement body;
        bool open;
        public event Action<bool> Toggled;

        public Details(string summary, FrameworkElement content, bool startOpen, double fontSize)
        {
            body = content;
            // the disclosure triangle a browser draws before the summary
            TextBlock s = U.Text("", fontSize, FontWeights.Normal, "text-2");
            Run arrow = new Run("▸ ");
            Run words = new Run(summary ?? "");
            s.Inlines.Add(arrow);
            s.Inlines.Add(words);
            s.Padding = new Thickness(0, 6, 0, 6);
            Border hit = new Border();
            hit.Background = Brushes.Transparent;
            hit.Child = s;
            U.Click(hit, () => Set(!open));
            hit.FocusVisualStyle = null;
            Root.Children.Add(hit);
            Root.Children.Add(body);
            Set(startOpen);
            Action<bool> paint = o => arrow.Text = o ? "▾ " : "▸ ";
            Toggled += paint;
            paint(startOpen);
        }

        public bool IsOpen { get { return open; } }

        public void Set(bool o)
        {
            open = o;
            body.Visibility = o ? Visibility.Visible : Visibility.Collapsed;
            Action<bool> h = Toggled;
            if (h != null) h(o);
        }
    }
}
