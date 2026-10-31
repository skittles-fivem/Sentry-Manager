using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Horizon
{
    /// <summary>A Horizon checkbox (hz/app.css .chk input: 20 x 20, accent-color magenta): the box and its label, a 44px tap target.</summary>
    public sealed class HzCheck
    {
        public readonly Flex Root;
        readonly Border box;
        readonly Path tick;
        bool on;

        /// <summary>The box turned (by a click, Space or Enter): its new state.</summary>
        public event Action<bool> Changed;

        public HzCheck(string label, bool value, string name)
        {
            box = new Border();
            box.Width = 20;
            box.Height = 20;
            box.CornerRadius = new CornerRadius(3);
            box.BorderThickness = new Thickness(1);
            box.VerticalAlignment = VerticalAlignment.Center;
            tick = new Path();
            tick.Data = Geometry.Parse("M4.5 10.5 8.5 14.5 15.5 6");
            tick.StrokeThickness = 2.4;
            tick.Stroke = Brushes.White;
            tick.StrokeStartLineCap = PenLineCap.Round;
            tick.StrokeEndLineCap = PenLineCap.Round;
            tick.StrokeLineJoin = PenLineJoin.Round;
            box.Child = tick;
            Root = Flex.Row(10, box);
            if (!string.IsNullOrEmpty(label))
            {
                TextBlock t = HzKit.Text(label, 15, "ink");
                t.VerticalAlignment = VerticalAlignment.Center;
                Root.Children.Add(t);
            }
            Root.MinHeight = 44;
            Root.Background = Brushes.Transparent;
            Root.VerticalAlignment = VerticalAlignment.Center;
            HzKit.AutomationName(Root, name ?? label);
            U.Click(Root, () => { Set(!on); Action<bool> h = Changed; if (h != null) h(on); });
            U.Hover(Root, x => Paint(x));
            Set(value);
        }

        public bool On { get { return on; } }

        /// <summary>Sets the box without calling back.</summary>
        public void Set(bool value)
        {
            on = value;
            Paint(Root != null && Root.IsMouseOver);
            System.Windows.Automation.AutomationProperties.SetItemStatus(Root, on ? "checked" : "");
        }

        void Paint(bool hover)
        {
            // the browser's dark-scheme box: grey with a light edge; ticked: magenta with a white check
            box.Background = on ? Css.Brush(hover ? "#ff5bc0" : "#ff3fb4") : Css.Brush(hover ? "#4a4458" : "#3b3a40");
            box.BorderBrush = on ? Css.Brush("#ff3fb4") : Css.Brush("#8f8a99");
            tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public static partial class HzKit
    {
        /// <summary>
        /// Text with CSS letter-spacing that wraps between words (a .pl-kind line):
        /// each word spaced, the words wrapping as the browser's lines do.
        /// </summary>
        public static Flex SpacedWrap(string text, double size, int weight, double spacing, string color)
        {
            Flex f = new Flex(Orientation.Horizontal, size * (0.28 + spacing));
            f.Wrap = true;
            foreach (string w in (text ?? "").Split(' '))
            {
                if (w.Length == 0) continue;
                SpacedText t = new SpacedText(w, size, U.W(weight), spacing);
                Paint(t, SpacedText.ForegroundProperty, color);
                f.Children.Add(t);
            }
            return f;
        }

        /// <summary>A heading in .chrome lettering that wraps (the gradient over the text, not uppercase).</summary>
        public static TextBlock ChromeText(string text, double size, int weight)
        {
            TextBlock t = U.Text(text, size, U.W(weight), null);
            t.Foreground = Css.Linear(180, "#ffffff", 0, "#eef0ff", .36, "#a3a7d8", .49, "#ffd2ef", .53, "#ffffff", .86);
            t.TextWrapping = TextWrapping.Wrap;
            t.LineHeight = size * 1.2;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            return t;
        }

        /// <summary>A row of text runs (a line with a faint tail: "8:00:00 AM – 8:01:00 AM").</summary>
        public static TextBlock Runs(double size, string color, int weight, params object[] parts)
        {
            TextBlock t = Text("", size, color, weight);
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                Run r = new Run((string)parts[i]);
                string c = parts[i + 1] as string;
                if (c != null)
                {
                    if (c.StartsWith("#") || c.StartsWith("rgb")) r.Foreground = Css.Brush(c);
                    else r.SetResourceReference(TextElement.ForegroundProperty, c);
                }
                t.Inlines.Add(r);
            }
            return t;
        }

        /// <summary>The sub heading of a sheet (.sub-h: 12px, 0.16em, uppercase, #ffd2ef, 14px over and 6px under).</summary>
        public static SpacedText SubHead(string text)
        {
            SpacedText t = new SpacedText((text ?? "").ToUpperInvariant(), 12, U.W(700), 0.16);
            t.Foreground = Css.Brush("#ffd2ef");
            t.Margin = new Thickness(0, 14, 0, 6);
            t.HorizontalAlignment = HorizontalAlignment.Left;
            return t;
        }

        /// <summary>An element that takes no part in Tab order and swallows nothing (decoration).</summary>
        public static T Deco<T>(T e) where T : UIElement
        {
            e.Focusable = false;
            e.IsHitTestVisible = false;
            return e;
        }

        /// <summary>The key a keydown names as the web's e.key does ("j", "ArrowDown", "Enter", "/", …); null for others.</summary>
        public static string WebKey(KeyEventArgs e)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (k)
            {
                case Key.Enter: return "Enter";
                case Key.Escape: return "Escape";
                case Key.Delete: return "Delete";
                case Key.Back: return "Backspace";
                case Key.Up: return "ArrowUp";
                case Key.Down: return "ArrowDown";
                case Key.Left: return "ArrowLeft";
                case Key.Right: return "ArrowRight";
                case Key.Divide: return "/";
                case Key.OemQuestion: return shift ? "?" : "/";
            }
            if (k >= Key.A && k <= Key.Z)
            {
                string c = ((char)('a' + (k - Key.A))).ToString();
                return shift ? c.ToUpperInvariant() : c;
            }
            return null;
        }
    }
}
