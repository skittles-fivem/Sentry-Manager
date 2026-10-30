using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SentrySix.Ui
{
    /// <summary>
    /// CSS values as WPF ones, so each theme can be written with the very
    /// numbers of its stylesheet: colours (#rgb, #rrggbb, rgba()), linear and
    /// radial gradients, box shadows.
    /// </summary>
    public static class Css
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>A CSS colour: #rgb, #rrggbb, #rrggbbaa, rgb(), rgba(), transparent, white, black.</summary>
        public static Color C(string css)
        {
            string s = (css ?? "").Trim().ToLowerInvariant();
            if (s == "transparent") return Color.FromArgb(0, 0, 0, 0);
            if (s == "white" || s == "#fff") return Colors.White;
            if (s == "black" || s == "#000") return Colors.Black;
            if (s.StartsWith("#"))
            {
                string h = s.Substring(1);
                if (h.Length == 3 || h.Length == 4)
                {
                    string x = "";
                    foreach (char ch in h) x += new string(ch, 2);
                    h = x;
                }
                byte r = byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber, Inv);
                byte g = byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber, Inv);
                byte b = byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber, Inv);
                byte a = h.Length >= 8 ? byte.Parse(h.Substring(6, 2), NumberStyles.HexNumber, Inv) : (byte)255;
                return Color.FromArgb(a, r, g, b);
            }
            if (s.StartsWith("rgb"))
            {
                int open = s.IndexOf('(');
                string[] p = s.Substring(open + 1).TrimEnd(')').Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                double a = p.Length > 3 ? Num(p[3]) : 1;
                return Color.FromArgb(B(a * 255), B(Num(p[0])), B(Num(p[1])), B(Num(p[2])));
            }
            throw new FormatException("Not a colour: " + css);
        }

        static double Num(string s)
        {
            s = s.Trim();
            if (s.EndsWith("%")) return double.Parse(s.TrimEnd('%'), Inv) / 100;
            return double.Parse(s, Inv);
        }

        static byte B(double v)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
        }

        /// <summary>The colour with its alpha multiplied (CSS opacity on a flat colour).</summary>
        public static Color Alpha(Color c, double a)
        {
            return Color.FromArgb(B(c.A * a), c.R, c.G, c.B);
        }

        public static SolidColorBrush Brush(string css)
        {
            SolidColorBrush b = new SolidColorBrush(C(css));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// linear-gradient(angle, stops): stops alternate colour and position
        /// (0..1), e.g. Linear(180, "#06031a", 0, "#0f0730", .22).
        /// </summary>
        public static LinearGradientBrush Linear(double angleDeg, params object[] stops)
        {
            double rad = angleDeg * Math.PI / 180;
            double vx = Math.Sin(rad), vy = -Math.Cos(rad);
            // CSS reaches the corners: stretch the vector to the box's edge
            double k = 0.5 / Math.Max(Math.Abs(vx), Math.Abs(vy));
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0.5 - vx * k, 0.5 - vy * k);
            g.EndPoint = new Point(0.5 + vx * k, 0.5 + vy * k);
            AddStops(g.GradientStops, stops);
            g.Freeze();
            return g;
        }

        /// <summary>radial-gradient(rx% ry% at cx% cy%, stops) relative to the box.</summary>
        public static RadialGradientBrush Radial(double cx, double cy, double rx, double ry, params object[] stops)
        {
            RadialGradientBrush g = new RadialGradientBrush();
            g.Center = new Point(cx, cy);
            g.GradientOrigin = new Point(cx, cy);
            g.RadiusX = rx;
            g.RadiusY = ry;
            AddStops(g.GradientStops, stops);
            g.Freeze();
            return g;
        }

        /// <summary>A radial gradient in pixels (CSS "900px 500px at 15% -10%"), for a box of the given size.</summary>
        public static RadialGradientBrush RadialPx(double w, double h, double rxPx, double ryPx, double cx, double cy, params object[] stops)
        {
            RadialGradientBrush g = new RadialGradientBrush();
            g.MappingMode = BrushMappingMode.Absolute;
            g.Center = new Point(cx * w, cy * h);
            g.GradientOrigin = g.Center;
            g.RadiusX = rxPx;
            g.RadiusY = ryPx;
            AddStops(g.GradientStops, stops);
            g.Freeze();
            return g;
        }

        static void AddStops(GradientStopCollection list, object[] stops)
        {
            int n = stops.Length / 2;
            for (int i = 0; i < stops.Length; i += 2)
            {
                Color c = stops[i] is Color ? (Color)stops[i] : C((string)stops[i]);
                double at = stops[i + 1] == null ? (n <= 1 ? 0 : (double)(i / 2) / (n - 1)) : Convert.ToDouble(stops[i + 1], Inv);
                list.Add(new GradientStop(c, at));
            }
        }

        /// <summary>box-shadow: 0 y blur color (one shadow; WPF draws one per element).</summary>
        public static DropShadowEffect Shadow(double y, double blur, string color)
        {
            Color c = C(color);
            DropShadowEffect e = new DropShadowEffect();
            e.Color = Color.FromRgb(c.R, c.G, c.B);
            e.Opacity = c.A / 255.0;
            e.BlurRadius = blur;
            e.ShadowDepth = Math.Abs(y);
            e.Direction = y >= 0 ? 270 : 90;
            e.RenderingBias = RenderingBias.Performance;
            e.Freeze();
            return e;
        }

        /// <summary>A glow (box-shadow 0 0 blur color).</summary>
        public static DropShadowEffect Glow(double blur, string color)
        {
            Color c = C(color);
            DropShadowEffect e = new DropShadowEffect();
            e.Color = Color.FromRgb(c.R, c.G, c.B);
            e.Opacity = c.A / 255.0;
            e.BlurRadius = blur;
            e.ShadowDepth = 0;
            e.Freeze();
            return e;
        }

        public static Thickness T(double all)
        {
            return new Thickness(all);
        }

        /// <summary>CSS padding order: top, right, bottom, left.</summary>
        public static Thickness T(double top, double right, double bottom, double left)
        {
            return new Thickness(left, top, right, bottom);
        }

        /// <summary>CSS shorthand: vertical, horizontal.</summary>
        public static Thickness T(double v, double h)
        {
            return new Thickness(h, v, h, v);
        }
    }
}
