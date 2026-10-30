using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SentrySix.Ui
{
    /// <summary>
    /// The shells' vehicle &lt;select&gt;: drawn closed exactly like the web's
    /// styled select (the interface gives the box), and opening the list of
    /// vehicles as a menu (a browser opens its own list there too).
    /// </summary>
    public sealed class Picker
    {
        public readonly Border Root;
        readonly TextBlock label;
        readonly ShellModel m;

        public Picker(ShellModel model, InputLook k, FrameworkElement arrow, double minWidth, double maxWidth)
        {
            m = model;
            label = new TextBlock();
            label.FontSize = k.FontSize;
            if (k.Font != null) label.FontFamily = k.Font;
            label.SetResourceReference(TextBlock.ForegroundProperty, k.Fg);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid g = new Grid();
            g.Children.Add(label);
            if (arrow != null)
            {
                arrow.HorizontalAlignment = HorizontalAlignment.Right;
                arrow.VerticalAlignment = VerticalAlignment.Center;
                arrow.IsHitTestVisible = false;
                g.Children.Add(arrow);
            }
            Root = U.Box(g, k.Bg, k.Border, 1, new CornerRadius(k.Radius), k.Padding);
            Root.MinHeight = k.MinHeight;
            Root.MinWidth = minWidth;
            if (maxWidth > 0) Root.MaxWidth = maxWidth;
            Root.ToolTip = "Vehicle";
            U.Click(Root, Open);
            Refresh();
        }

        public void Refresh()
        {
            VehicleInfo v = m.Current;
            label.Text = v != null ? v.Name : "No vehicle";
        }

        void Open()
        {
            ContextMenu menu = new ContextMenu();
            foreach (VehicleInfo v in m.Vehicles)
            {
                MenuItem item = new MenuItem();
                item.Header = v.Name;
                item.IsCheckable = true;
                item.IsChecked = m.Current != null && v.Id == m.Current.Id;
                string id = v.Id;
                item.Click += delegate { m.PickVehicle(id); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = Root;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.MinWidth = Root.ActualWidth;
            menu.IsOpen = true;
        }
    }

    /// <summary>Horizon's .hud: dark glass, a thin neon outline, corner brackets.</summary>
    public static class Hud
    {
        public static Grid Panel(UIElement child, Thickness padding, object bg, string bracket)
        {
            // the glass with its shadow behind, the outline and content on top
            // (an effect on the content's own box would shadow its text too)
            Border glass = new Border();
            U.Paint(glass, Border.BackgroundProperty, bg ?? "glass");
            glass.CornerRadius = new CornerRadius(4);
            glass.Effect = Css.Shadow(12, 40, "rgba(4,1,18,.45)");
            Border b = new Border();
            b.SetResourceReference(Border.BorderBrushProperty, "line");
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(4);
            b.Padding = padding;
            b.Child = child;
            Grid g = new Grid();
            g.Children.Add(glass);
            g.Children.Add(b);
            Brush c = Css.Brush(bracket ?? "#ff3fb4");
            // inset -1px: 16 x 2 and 2 x 16 at each corner
            foreach (HorizontalAlignment h in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
                foreach (VerticalAlignment v in new[] { VerticalAlignment.Top, VerticalAlignment.Bottom })
                {
                    g.Children.Add(Bar(c, 16, 2, h, v));
                    g.Children.Add(Bar(c, 2, 16, h, v));
                }
            return g;
        }

        static FrameworkElement Bar(Brush c, double w, double h, HorizontalAlignment ha, VerticalAlignment va)
        {
            Border r = new Border();
            r.Background = c;
            r.Width = w;
            r.Height = h;
            r.HorizontalAlignment = ha;
            r.VerticalAlignment = va;
            r.Margin = new Thickness(ha == HorizontalAlignment.Left ? -1 : 0, va == VerticalAlignment.Top ? -1 : 0, ha == HorizontalAlignment.Right ? -1 : 0, va == VerticalAlignment.Bottom ? -1 : 0);
            r.IsHitTestVisible = false;
            return r;
        }
    }
}
