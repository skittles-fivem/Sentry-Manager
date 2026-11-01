using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Flux
{
    /// <summary>
    /// The pieces of shell.css and flux.css the Nodes page adds to the kit:
    /// the checkbox (.check), select.input as a list that opens, the
    /// .sub-block sections under a dashed line, the .node-cols and .grid2
    /// grids, the .section-h heads and a monospace run.
    /// </summary>
    public static partial class FxKit
    {
        /// <summary>
        /// input[type=checkbox] (accent-color violet: filled with a white tick
        /// when on; 15px, 22px with pointer: coarse) and its label, gap 8; a
        /// click or Space toggles it, then calls back with the new state.
        /// top: label.sub-block's align-items: flex-start (else centred).
        /// </summary>
        public sealed class NodesCheck
        {
            public readonly Flex Root;
            readonly Border box = new Border();
            readonly System.Windows.Shapes.Path tick = new System.Windows.Shapes.Path();
            bool on;
            public event Action<bool> Changed;

            public NodesCheck(bool on, UIElement label, string name, bool top)
            {
                box.Width = 15;
                box.Height = 15;
                box.CornerRadius = new CornerRadius(2.5);
                box.BorderThickness = new Thickness(1);
                tick.Data = Geometry.Parse("M3,7.5 L6,10.5 L12,4");
                tick.Stroke = Brushes.White;
                tick.StrokeThickness = 2;
                tick.StrokeStartLineCap = PenLineCap.Round;
                tick.StrokeEndLineCap = PenLineCap.Round;
                tick.StrokeLineJoin = PenLineJoin.Round;
                tick.Stretch = Stretch.None;
                Viewbox vb = new Viewbox();
                Canvas c = new Canvas();
                c.Width = 15;
                c.Height = 15;
                c.Children.Add(tick);
                vb.Child = c;
                box.Child = vb;
                box.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
                if (top) box.Margin = new Thickness(0, 2, 0, 0);
                Root = Flex.Row(8, box);
                if (label != null) { Root.Children.Add(label); Flex.SetGrow(label, 1); }
                Root.VerticalAlignment = VerticalAlignment.Center;
                Root.Focusable = true;
                Root.Cursor = System.Windows.Input.Cursors.Hand;
                U.Click(Root, () => Set(!this.on, true));
                Root.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key != System.Windows.Input.Key.Space) return;
                    e.Handled = true;
                    Set(!this.on, true);
                };
                System.Windows.Automation.AutomationProperties.SetName(Root, name ?? "");
                U.OnWindowWidth(Root, w =>
                {
                    bool coarse = Coarse(w);
                    box.Width = box.Height = coarse ? 22 : 15;
                    Root.MinHeight = coarse ? 44 : 0;
                });
                Set(on, false);
            }

            public bool On { get { return on; } }

            public void Set(bool v, bool tell)
            {
                on = v;
                if (on)
                {
                    box.SetResourceReference(Border.BackgroundProperty, "violet");
                    box.SetResourceReference(Border.BorderBrushProperty, "violet");
                }
                else
                {
                    box.Background = Css.Brush("#3b3b3b");
                    box.BorderBrush = Css.Brush("#858585");
                }
                tick.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                System.Windows.Automation.AutomationProperties.SetItemStatus(Root, on ? "checked" : "");
                if (tell)
                {
                    Action<bool> h = Changed;
                    if (h != null) h(on);
                }
            }
        }

        /// <summary>
        /// select.input: drawn closed as the web's (bg-2, the line, radius 6,
        /// 34px tall, 13.5px; 44px and 16px with pointer: coarse; a chevron on
        /// the right), opening its options as a list under it; picking one
        /// calls back with its value.
        /// </summary>
        public static Border NodesSelect(string label, IList<KeyValuePair<string, string>> options, Action<string> picked)
        {
            TextBlock t = Text(options.Count > 0 ? options[0].Value : "", 13.5, "text");
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Shapes.Path arrow = new System.Windows.Shapes.Path();
            arrow.Data = Geometry.Parse("M0,0 L4.5,4.5 L9,0");
            arrow.StrokeThickness = 1.6;
            arrow.StrokeStartLineCap = PenLineCap.Round;
            arrow.StrokeEndLineCap = PenLineCap.Round;
            arrow.SetResourceReference(Shape.StrokeProperty, "text");
            arrow.HorizontalAlignment = HorizontalAlignment.Right;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Margin = new Thickness(0, 0, -18, 0);
            Grid g = new Grid();
            g.Children.Add(t);
            g.Children.Add(arrow);
            Border b = new RoundBorder();
            b.Child = g;
            b.SetResourceReference(Border.BackgroundProperty, "bg-2");
            b.SetResourceReference(Border.BorderBrushProperty, "line");
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(6);
            b.Padding = new Thickness(10, 6, 26, 6);
            b.MinHeight = 34;
            b.Focusable = true;
            b.Cursor = System.Windows.Input.Cursors.Hand;
            b.Tag = "field";
            System.Windows.Automation.AutomationProperties.SetName(b, label ?? "");
            U.OnWindowWidth(b, w =>
            {
                bool coarse = Coarse(w);
                b.MinHeight = coarse ? 44 : 34;
                t.FontSize = coarse ? 16 : 13.5;
            });
            Action open = () =>
            {
                ContextMenu menu = new ContextMenu();
                foreach (KeyValuePair<string, string> o in options)
                {
                    MenuItem item = new MenuItem();
                    item.Header = o.Value;
                    string v = o.Key;
                    item.Click += delegate { picked(v); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = b;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.MinWidth = b.ActualWidth;
                menu.IsOpen = true;
            };
            U.Click(b, () => { b.Focus(); open(); });
            b.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Down) { e.Handled = true; open(); }
            };
            return b;
        }

        /// <summary>.sub-block: a section under a 1px dashed line-soft rule (margin-top 6, padding 8 0).</summary>
        public static Grid NodesSubBlock(UIElement child)
        {
            Line dash = new Line();
            dash.X1 = 0; dash.X2 = 4000; dash.Y1 = 0.5; dash.Y2 = 0.5;
            dash.SetResourceReference(Shape.StrokeProperty, "line-soft");
            dash.StrokeThickness = 1;
            dash.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
            dash.VerticalAlignment = VerticalAlignment.Top;
            Border inner = new Border();
            inner.Child = child;
            inner.Padding = new Thickness(0, 8, 0, 8);
            Grid g = new Grid();
            g.ClipToBounds = true;
            g.Children.Add(dash);
            g.Children.Add(inner);
            g.Margin = new Thickness(0, 6, 0, 0);
            return g;
        }

        /// <summary>.section-h: the h2 (15px 650) and what goes on the right, space between, margin-bottom 10.</summary>
        public static Flex NodesSectionHead(string title, UIElement right)
        {
            TextBlock h2 = Text(title, 15, "text", 650);
            h2.VerticalAlignment = VerticalAlignment.Center;
            Flex f = Flex.Row(10, h2);
            if (right != null)
            {
                FrameworkElement fe = right as FrameworkElement;
                if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
                f.Children.Add(right);
            }
            f.Justify = "between";
            f.Shrink = true;
            f.Margin = new Thickness(0, 0, 0, 10);
            return f;
        }

        /// <summary>
        /// .node-cols: minmax(0, 1.4fr) and minmax(0, 1fr), gap 18, tops
        /// aligned; one column under the other at 1180 px (window) and less.
        /// </summary>
        public static Grid NodesCols(FrameworkElement a, FrameworkElement b)
        {
            Grid g = new Grid();
            a.VerticalAlignment = VerticalAlignment.Top;
            b.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(a);
            g.Children.Add(b);
            bool? was = null;
            Action<bool> lay = two =>
            {
                if (was.HasValue && was.Value == two) return;
                was = two;
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                if (two)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    System.Windows.Controls.Grid.SetColumn(a, 0); System.Windows.Controls.Grid.SetRow(a, 0);
                    System.Windows.Controls.Grid.SetColumn(b, 2); System.Windows.Controls.Grid.SetRow(b, 0);
                    b.Margin = new Thickness(0);
                }
                else
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    System.Windows.Controls.Grid.SetColumn(a, 0); System.Windows.Controls.Grid.SetRow(a, 0);
                    System.Windows.Controls.Grid.SetColumn(b, 0); System.Windows.Controls.Grid.SetRow(b, 1);
                    b.Margin = new Thickness(0, 18, 0, 0);
                }
            };
            U.OnWindowWidth(g, w => lay(w > 1180));
            lay(true);
            return g;
        }

        /// <summary>.grid2: two equal columns, gap 12; one column at 860 px (window) and less.</summary>
        public static Grid NodesGrid2(FrameworkElement a, FrameworkElement b)
        {
            Grid g = new Grid();
            g.Children.Add(a);
            g.Children.Add(b);
            bool? was = null;
            Action<bool> lay = two =>
            {
                if (was.HasValue && was.Value == two) return;
                was = two;
                g.ColumnDefinitions.Clear();
                g.RowDefinitions.Clear();
                if (two)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    System.Windows.Controls.Grid.SetColumn(b, 2); System.Windows.Controls.Grid.SetRow(b, 0);
                    b.Margin = new Thickness(0);
                }
                else
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    System.Windows.Controls.Grid.SetColumn(b, 0); System.Windows.Controls.Grid.SetRow(b, 1);
                    b.Margin = new Thickness(0, 12, 0, 0);
                }
            };
            U.OnWindowWidth(g, w => lay(w > 860));
            lay(true);
            return g;
        }

        /// <summary>A text run in the monospace face (span.mono: 0.92em of the text around it).</summary>
        public static Run NodesMono(string text, double size)
        {
            Run r = new Run(text ?? "");
            r.FontFamily = Mono;
            r.FontSize = size * 0.92;
            return r;
        }
    }
}
