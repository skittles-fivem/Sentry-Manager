using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SentrySix.Ui
{
    /// <summary>A toast's look: theme keys and sizes (the built-in one is web/shell.css .toast).</summary>
    public sealed class ToastLook
    {
        public string Bg = "panel-3", Border = "border-strong", Accent = "accent", Bad = "danger", Fg = "text";
        public double Radius = 12, MaxWidth = 380, FontSize = 14, Gap = 10, Right = 20, Bottom = 20, Edge = 3;
        public Thickness Padding = new Thickness(16, 12, 16, 12);
        public FontFamily Font;
        /// <summary>box-shadow 0 14px 40px rgba(0,0,0,.5).</summary>
        public string Shadow = "rgba(0,0,0,0.5)";
        public double ShadowY = 14, ShadowBlur = 40;
        public int LifeMs = 5000, BadLifeMs = 9000;
    }

    /// <summary>
    /// web/app.js toast(msg, bad): a stack bottom right above everything,
    /// each note gone after 5 s (9 s when it reports a failure). The shell
    /// puts the host's <see cref="Root"/> over its page.
    /// </summary>
    public sealed class ToastHost
    {
        readonly ToastLook k;
        readonly StackPanel stack = new StackPanel();
        public readonly Grid Root = new Grid();

        public ToastHost(ToastLook look)
        {
            k = look ?? new ToastLook();
            stack.HorizontalAlignment = HorizontalAlignment.Right;
            stack.VerticalAlignment = VerticalAlignment.Bottom;
            stack.Margin = new Thickness(0, 0, k.Right, k.Bottom);
            Root.Children.Add(stack);
            // only the toasts take clicks; the page under the empty area stays usable
            Root.IsHitTestVisible = true;
            Root.Background = null;
            Panel.SetZIndex(Root, 1000);
        }

        public void Show(string message, bool bad)
        {
            if (string.IsNullOrEmpty(message)) return;
            TextBlock t = U.Text(message, k.FontSize, FontWeights.Normal, k.Fg);
            if (k.Font != null) t.FontFamily = k.Font;
            // the 3 px left edge: an outer box in the edge colour, the toast inset 3 px on its left
            Border inner = U.Box(t, null, null, 0, new CornerRadius(0), k.Padding);
            Border box = U.Box(inner, k.Bg, k.Border, 1, new CornerRadius(k.Radius), new Thickness(0));
            box.BorderThickness = new Thickness(k.Edge, 1, 1, 1);
            box.SetResourceReference(Border.BorderBrushProperty, k.Border);
            Grid g = new Grid();
            Border edge = U.Box(null, bad ? k.Bad : k.Accent, null, 0, new CornerRadius(k.Radius, 0, 0, k.Radius), new Thickness(0));
            edge.Width = k.Edge;
            edge.HorizontalAlignment = HorizontalAlignment.Left;
            // the edge is drawn over the left border, with the box's rounding
            Grid.SetColumn(edge, 0);
            g.Children.Add(box);
            g.Children.Add(edge);
            g.MaxWidth = k.MaxWidth;
            g.HorizontalAlignment = HorizontalAlignment.Right;
            if (k.Shadow != null) box.Effect = Css.Shadow(k.ShadowY, k.ShadowBlur, k.Shadow);
            if (stack.Children.Count > 0) g.Margin = new Thickness(0, k.Gap, 0, 0);
            stack.Children.Add(g);
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(bad ? k.BadLifeMs : k.LifeMs);
            timer.Tick += delegate
            {
                timer.Stop();
                stack.Children.Remove(g);
                if (stack.Children.Count > 0)
                {
                    FrameworkElement first = stack.Children[0] as FrameworkElement;
                    if (first != null) first.Margin = new Thickness(0);
                }
            };
            timer.Start();
        }
    }
}
