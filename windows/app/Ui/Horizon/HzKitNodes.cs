using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using SentrySix.Core;

namespace SentrySix.Ui.Horizon
{
    /// <summary>
    /// The pieces of hz/app.css the Nodes page adds to the kit: the .chk box,
    /// select.in as a list that opens, the .nsub sections, the dashed .pair box
    /// and the .tbl rows.
    /// </summary>
    public static partial class HzKit
    {
        /// <summary>
        /// .chk input: a 20px box (accent-color magenta: filled with a white
        /// tick when on), the label beside it, gap 10, 44px tall; a click or
        /// Space toggles it, then calls back with the new state.
        /// </summary>
        public sealed class NodeCheck
        {
            public readonly Flex Root;
            readonly Border box = new Border();
            readonly System.Windows.Shapes.Path tick = new System.Windows.Shapes.Path();
            bool on;
            public event Action<bool> Changed;

            public NodeCheck(bool on, UIElement label, string name)
            {
                box.Width = 20;
                box.Height = 20;
                box.CornerRadius = new CornerRadius(3);
                box.BorderThickness = new Thickness(1);
                tick.Data = Geometry.Parse("M4.5,10 L8.5,14 L15.5,6");
                tick.Stroke = Brushes.White;
                tick.StrokeThickness = 2.2;
                tick.StrokeStartLineCap = PenLineCap.Round;
                tick.StrokeEndLineCap = PenLineCap.Round;
                tick.StrokeLineJoin = PenLineJoin.Round;
                box.Child = tick;
                box.VerticalAlignment = VerticalAlignment.Top;
                box.Margin = new Thickness(0, 2, 0, 0);
                Root = Flex.Row(10, box);
                if (label != null) { Root.Children.Add(label); Flex.SetGrow(label, 1); }
                Root.MinHeight = 44;
                Root.VerticalAlignment = VerticalAlignment.Center;
                U.Click(Root, () => Set(!this.on, true));
                AutomationName(Root, name);
                Set(on, false);
            }

            public bool On { get { return on; } }

            public void Set(bool v, bool tell)
            {
                on = v;
                box.Background = on ? Css.Brush("#ff3fb4") : Css.Brush("rgba(255,255,255,.12)");
                box.BorderBrush = on ? Css.Brush("#ff3fb4") : Css.Brush("rgba(207,195,238,.6)");
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
        /// select.in: drawn closed as the web's (the field, a cyan chevron on
        /// the right), opening its options as a list under it; picking one
        /// calls back with its value.
        /// </summary>
        public static Border NodeSelect(string label, IList<KeyValuePair<string, string>> options, Action<string> picked)
        {
            TextBlock t = Text(options.Count > 0 ? options[0].Value : "", 16, "ink");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Shapes.Path arrow = new System.Windows.Shapes.Path();
            arrow.Data = Geometry.Parse("M0,0 L12,0 L6,6 Z");
            arrow.SetResourceReference(Shape.FillProperty, "cyan");
            arrow.HorizontalAlignment = HorizontalAlignment.Right;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Margin = new Thickness(0, 0, -24, 0);
            Grid g = new Grid();
            g.Children.Add(t);
            g.Children.Add(arrow);
            Border b = U.Box(g, "in-bg", "in-border", 1, new CornerRadius(4), new Thickness(12, 9, 36, 9));
            b.MinHeight = 44;
            AutomationName(b, label);
            U.Click(b, () =>
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
            });
            return b;
        }

        /// <summary>.nsub: a section under a line (margin-top 12, padding-top 10, the soft line), a column with gap 6.</summary>
        public static Border Nsub(params UIElement[] children)
        {
            Flex f = Flex.Col(6, children);
            Border b = new Border();
            b.Child = f;
            b.Margin = new Thickness(0, 12, 0, 0);
            b.Padding = new Thickness(0, 10, 0, 0);
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            return b;
        }

        /// <summary>A box with a dashed 1px outline (CSS border: 1px dashed), its fill and radius, padding inside.</summary>
        public static Grid Dashed(UIElement child, string stroke, string fill, double radius, Thickness padding)
        {
            Rectangle r = new Rectangle();
            Paint(r, Shape.StrokeProperty, stroke);
            r.StrokeThickness = 1;
            r.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
            r.RadiusX = radius;
            r.RadiusY = radius;
            if (fill != null) r.Fill = Css.Brush(fill);
            Border b = new Border();
            b.Child = child;
            b.Padding = padding;
            Grid g = new Grid();
            g.Children.Add(r);
            g.Children.Add(b);
            return g;
        }

        /// <summary>A .tbl row: cells (padding 9px 10px) in columns of the given star widths, the soft line under it, .hi's green tint.</summary>
        public static Border TblRow(double[] widths, bool hi, params UIElement[] cells)
        {
            Grid g = new Grid();
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = widths[i] > 0 ? new GridLength(widths[i], GridUnitType.Star) : GridLength.Auto });
            for (int i = 0; i < cells.Length; i++)
            {
                FrameworkElement c = cells[i] as FrameworkElement;
                if (c == null) continue;
                c.Margin = Css.T(9, 10);
                c.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(c, i);
                g.Children.Add(c);
            }
            Border b = new Border();
            b.Child = g;
            b.BorderThickness = new Thickness(0, 0, 0, 1);
            b.SetResourceReference(Border.BorderBrushProperty, "line-soft");
            if (hi) b.Background = Css.Brush("rgba(93,255,176,.06)");
            return b;
        }

        /// <summary>A .tbl th: 11.5px 800, 0.14em, uppercase, faint.</summary>
        public static SpacedText Th(string text)
        {
            SpacedText t = new SpacedText((text ?? "").ToUpperInvariant(), 11.5, U.W(800), 0.14);
            t.SetResourceReference(SpacedText.ForegroundProperty, "faint");
            return t;
        }

        /// <summary>A text run in the monospace face (span.mono).</summary>
        public static Run MonoRun(string text, double size)
        {
            Run r = new Run(text ?? "");
            r.FontFamily = U.Consolas;
            r.FontSize = size * 0.92;
            return r;
        }
    }
}
