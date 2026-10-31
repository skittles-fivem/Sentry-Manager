using System;
using System.Collections.Generic;
using System.Text;

namespace SentrySix.Core
{
    /// <summary>One drawn piece of a sparkline: its points in the 220 x 40 box, and the colour it was given.</summary>
    public sealed class SparkSegment
    {
        public string Color;
        /// <summary>x, y pairs (the web's toFixed(1) values).</summary>
        public List<double[]> Points = new List<double[]>();
    }

    /// <summary>
    /// web/app.js spark(points, color, floor, also): a 220 x 40 sparkline, 0
    /// at the bottom, the top at the larger of floor and 1.15 x the highest
    /// value, missing samples as gaps, extra series on the same scale. Each
    /// segment is drawn as a filled area (8 % opacity) and a 2 px line.
    /// </summary>
    public static class Spark
    {
        public const double W = 220, H = 40;

        static List<double?> Clean(IList<double?> list)
        {
            List<double?> o = new List<double?>();
            if (list == null) return o;
            foreach (double? p in list) o.Add(p.HasValue && Format.Fin(p.Value) ? (double?)Math.Max(0, p.Value) : null);
            return o;
        }

        static int Known(List<double?> vals)
        {
            int n = 0;
            foreach (double? v in vals) if (v.HasValue) n++;
            return n;
        }

        /// <summary>A JSON array of numbers (null for a gap) as spark's input.</summary>
        public static List<double?> Values(JsonValue arr)
        {
            List<double?> o = new List<double?>();
            foreach (JsonValue v in arr.Items) o.Add(v.IsNumber ? (double?)v.Num : null);
            return o;
        }

        /// <summary>The segments; empty when no series has two samples (the web draws nothing then).</summary>
        public static List<SparkSegment> Segments(IList<double?> points, string color, double floor, IList<KeyValuePair<IList<double?>, string>> also)
        {
            List<KeyValuePair<List<double?>, string>> series = new List<KeyValuePair<List<double?>, string>>();
            series.Add(new KeyValuePair<List<double?>, string>(Clean(points), color));
            if (also != null) foreach (KeyValuePair<IList<double?>, string> a in also) series.Add(new KeyValuePair<List<double?>, string>(Clean(a.Key), a.Value));
            series.RemoveAll(s => Known(s.Key) <= 1);
            List<SparkSegment> outList = new List<SparkSegment>();
            if (series.Count == 0) return outList;
            double top = floor;
            foreach (KeyValuePair<List<double?>, string> s in series)
                foreach (double? v in s.Key)
                    if (v.HasValue) top = Math.Max(top, v.Value * 1.15);
            foreach (KeyValuePair<List<double?>, string> s in series)
            {
                List<double?> vals = s.Key;
                SparkSegment seg = null;
                for (int i = 0; i < vals.Count; i++)
                {
                    if (!vals[i].HasValue)
                    {
                        if (seg != null && seg.Points.Count > 1) outList.Add(seg);
                        seg = null;
                        continue;
                    }
                    if (seg == null) { seg = new SparkSegment(); seg.Color = s.Value; }
                    double x = Fixed1((i / (double)(vals.Count - 1)) * W);
                    double y = Fixed1(H - 3 - (vals[i].Value / top) * (H - 6));
                    seg.Points.Add(new[] { x, y });
                }
                if (seg != null && seg.Points.Count > 1) outList.Add(seg);
            }
            return outList;
        }

        static double Fixed1(double v)
        {
            if (!Format.Fin(v)) return v;
            return double.Parse(Format.ToFixed(v, 1), System.Globalization.CultureInfo.InvariantCulture);
        }

        static string P(double v)
        {
            return double.IsNaN(v) ? "NaN" : Format.ToFixed(v, 1);
        }

        /// <summary>The web's SVG text for the same input ('' for none): what the tests compare.</summary>
        public static string Svg(IList<double?> points, string color, double floor, IList<KeyValuePair<IList<double?>, string>> also)
        {
            List<SparkSegment> segs = Segments(points, color, floor, also);
            if (segs.Count == 0)
            {
                // spark() returns '' only when no series has two samples
                return "";
            }
            StringBuilder b = new StringBuilder();
            b.Append("<svg class=\"nd-spark\" viewBox=\"0 0 220 40\" preserveAspectRatio=\"none\" aria-hidden=\"true\">");
            foreach (SparkSegment s in segs)
            {
                List<string> pts = new List<string>();
                foreach (double[] p in s.Points) pts.Add(P(p[0]) + "," + P(p[1]));
                string all = string.Join(" ", pts);
                b.Append("<polyline points=\"").Append(P(s.Points[0][0])).Append(",40 ").Append(all).Append(' ').Append(P(s.Points[s.Points.Count - 1][0])).Append(",40\" fill=\"")
                    .Append(s.Color).Append("\" opacity=\"0.08\" stroke=\"none\"/>");
                b.Append("<polyline points=\"").Append(all).Append("\" fill=\"none\" stroke=\"").Append(s.Color).Append("\" stroke-width=\"2\" vector-effect=\"non-scaling-stroke\"/>");
            }
            b.Append("</svg>");
            return b.ToString();
        }
    }
}
