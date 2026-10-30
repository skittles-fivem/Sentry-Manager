using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml;

namespace SentrySix.Ui
{
    /// <summary>
    /// The web's inline SVG icons drawn as WPF shapes, from the very markup of
    /// each interface's symbol sprite (path, rect, circle, ellipse, line,
    /// polyline, polygon; fill, stroke, stroke-width, opacity, rotate()).
    /// "currentColor" follows the inherited TextElement.Foreground, as in CSS.
    /// </summary>
    public static class Svg
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The CSS defaults a sprite's icons get from their class (the web's .i / .ico / .ic rules).</summary>
        public sealed class Defaults
        {
            public string Fill = "black";
            public string Stroke = "none";
            public double StrokeWidth = 1;
            public bool Round;

            /// <summary>fill: none; stroke: currentColor; stroke-width: w; round caps and joins.</summary>
            public static Defaults Outline(double width)
            {
                Defaults d = new Defaults();
                d.Fill = "none";
                d.Stroke = "currentColor";
                d.StrokeWidth = width;
                d.Round = true;
                return d;
            }

            /// <summary>Plain SVG defaults (each element says its own fill and stroke).</summary>
            public static Defaults Plain()
            {
                return new Defaults();
            }
        }

        /// <summary>An icon of the given size from symbol markup with a 0 0 vw vh viewBox.</summary>
        public static FrameworkElement Icon(string markup, double size, Defaults d, double viewBox = 24)
        {
            Canvas canvas = new Canvas();
            canvas.Width = viewBox;
            canvas.Height = viewBox;
            canvas.IsHitTestVisible = false;
            try
            {
                foreach (Shape s in Shapes(markup, d)) canvas.Children.Add(s);
            }
            catch (Exception)
            {
                // a malformed icon draws nothing rather than breaking the page
            }
            Viewbox box = new Viewbox();
            box.Width = size;
            box.Height = size;
            box.Stretch = Stretch.Uniform;
            box.Child = canvas;
            box.IsHitTestVisible = false;
            box.SnapsToDevicePixels = false;
            return box;
        }

        public static List<Shape> Shapes(string markup, Defaults d)
        {
            List<Shape> list = new List<Shape>();
            XmlReaderSettings rs = new XmlReaderSettings();
            rs.DtdProcessing = DtdProcessing.Prohibit;
            rs.XmlResolver = null;
            using (XmlReader r = XmlReader.Create(new StringReader("<g>" + markup + "</g>"), rs))
            {
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element || r.Name == "g") continue;
                    Shape s = Make(r, d);
                    if (s != null) list.Add(s);
                }
            }
            return list;
        }

        static double A(XmlReader r, string name, double fallback)
        {
            string v = r.GetAttribute(name);
            double x;
            if (v != null && double.TryParse(v, NumberStyles.Float, Inv, out x)) return x;
            return fallback;
        }

        static Shape Make(XmlReader r, Defaults d)
        {
            Geometry g = null;
            bool open = false;
            switch (r.Name)
            {
                case "path":
                    {
                        string data = r.GetAttribute("d");
                        if (string.IsNullOrEmpty(data)) return null;
                        // SVG fills nonzero; the WPF mini-language defaults to even-odd unless told F1
                        g = Geometry.Parse("F1 " + data);
                        break;
                    }
                case "rect":
                    {
                        double rx = A(r, "rx", A(r, "ry", 0));
                        double ry = A(r, "ry", rx);
                        g = new RectangleGeometry(new Rect(A(r, "x", 0), A(r, "y", 0), A(r, "width", 0), A(r, "height", 0)), rx, ry);
                        break;
                    }
                case "circle":
                    {
                        double rr = A(r, "r", 0);
                        g = new EllipseGeometry(new Point(A(r, "cx", 0), A(r, "cy", 0)), rr, rr);
                        break;
                    }
                case "ellipse":
                    g = new EllipseGeometry(new Point(A(r, "cx", 0), A(r, "cy", 0)), A(r, "rx", 0), A(r, "ry", 0));
                    break;
                case "line":
                    g = new LineGeometry(new Point(A(r, "x1", 0), A(r, "y1", 0)), new Point(A(r, "x2", 0), A(r, "y2", 0)));
                    open = true;
                    break;
                case "polyline":
                case "polygon":
                    {
                        string[] n = (r.GetAttribute("points") ?? "").Split(new[] { ' ', ',', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (n.Length < 4) return null;
                        System.Text.StringBuilder sb = new System.Text.StringBuilder("F1 M");
                        for (int i = 0; i + 1 < n.Length; i += 2) sb.Append(i == 0 ? "" : " L").Append(n[i]).Append(',').Append(n[i + 1]);
                        if (r.Name == "polygon") sb.Append(" Z");
                        g = Geometry.Parse(sb.ToString());
                        open = r.Name == "polyline";
                        break;
                    }
                default:
                    return null;
            }
            string transform = r.GetAttribute("transform");
            if (transform != null && transform.StartsWith("rotate("))
            {
                string[] p = transform.Substring(7).TrimEnd(')').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                double a = double.Parse(p[0], Inv);
                double cx = p.Length > 2 ? double.Parse(p[1], Inv) : 0;
                double cy = p.Length > 2 ? double.Parse(p[2], Inv) : 0;
                g = g.Clone();
                g.Transform = new RotateTransform(a, cx, cy);
            }
            System.Windows.Shapes.Path s = new System.Windows.Shapes.Path();
            s.Data = g;
            string fill = r.GetAttribute("fill") ?? (open ? "none" : d.Fill);
            string stroke = r.GetAttribute("stroke") ?? d.Stroke;
            Paint(s, Shape.FillProperty, fill);
            Paint(s, Shape.StrokeProperty, stroke);
            s.StrokeThickness = A(r, "stroke-width", d.StrokeWidth);
            string cap = r.GetAttribute("stroke-linecap");
            string join = r.GetAttribute("stroke-linejoin");
            PenLineCap c = cap == "round" || (cap == null && d.Round) ? PenLineCap.Round : cap == "square" ? PenLineCap.Square : PenLineCap.Flat;
            s.StrokeStartLineCap = c;
            s.StrokeEndLineCap = c;
            s.StrokeLineJoin = join == "round" || (join == null && d.Round) ? PenLineJoin.Round : join == "bevel" ? PenLineJoin.Bevel : PenLineJoin.Miter;
            s.Opacity = A(r, "opacity", 1);
            return s;
        }

        static void Paint(Shape s, DependencyProperty dp, string value)
        {
            if (value == null || value == "none") return;
            if (value == "currentColor")
            {
                Binding b = new Binding();
                b.RelativeSource = RelativeSource.Self;
                b.Path = new PropertyPath(TextElement.ForegroundProperty);
                s.SetBinding(dp, b);
                return;
            }
            s.SetValue(dp, Css.Brush(value));
        }
    }
}
