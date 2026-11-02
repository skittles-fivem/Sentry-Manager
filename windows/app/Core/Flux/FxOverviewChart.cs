using System;
using System.Collections.Generic;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>One series of a TimeChart (flux/charts.js): key, label, colour, fill (false for a bare line), dash, width.</summary>
    public sealed class FxOverviewSeries
    {
        public string Key, Label, Color;
        public bool Fill = true;
        public double[] Dash;
        public double Width = 1.6;

        public FxOverviewSeries(string key, string label, string color)
        {
            Key = key;
            Label = label;
            Color = color;
        }
    }

    /// <summary>new TimeChart(host, opts)'s options: height, max (NaN for none), min, floor, format, series, legend.</summary>
    public sealed class FxOverviewChartOpts
    {
        public double Height = 120;
        public double Max = double.NaN;
        public double Min;
        public double Floor;
        public Func<double, string> Format = v => Core.Format.JsNum(Core.Format.Round(v));
        public List<FxOverviewSeries> Series = new List<FxOverviewSeries>();
        public bool Legend;

        /// <summary>drawLegend(): the keys are shown for two series or more, or with legend: true.</summary>
        public bool HasLegend { get { return Series.Count >= 2 || Legend; } }
    }

    /// <summary>
    /// One thing a chart draws on its canvas, with the canvas state it is
    /// drawn in: begin, M (moveTo), L (lineTo), arc, close, stroke, fill,
    /// text, rect (fillRect), srect (strokeRect).
    /// </summary>
    public sealed class FxOverviewOp
    {
        public string Kind;
        public double X, Y, W, H, R;
        public string Text, Align, Baseline;
        /// <summary>fill, text, rect: the fill style (a colour or "grad …"); stroke, srect: the stroke style.</summary>
        public string Paint;
        public double LineWidth;
        public double[] Dash;
        /// <summary>A gradient fill: from y0 to y1, its two colours.</summary>
        public double GradY0, GradY1;
        public string GradFrom, GradTo;
        /// <summary>A heat cell's hsla(): hue, lightness (%), alpha.</summary>
        public double Hue, Light, Alpha;

        static string F2(double n) { return Core.Format.ToFixed(n, 2); }

        /// <summary>The op as test/windowsNativeFluxOverview.test.js's recording canvas writes it.</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case "M": case "L": return Kind + " " + F2(X) + " " + F2(Y);
                case "arc": return "arc " + F2(X) + " " + F2(Y) + " " + F2(R);
                case "stroke":
                    {
                        List<string> d = new List<string>();
                        foreach (double x in Dash ?? new double[0]) d.Add(Core.Format.JsNum(x));
                        return "stroke " + Paint + " " + F2(LineWidth) + " [" + string.Join(",", d) + "]";
                    }
                case "fill": return "fill " + Paint;
                case "text": return "text " + Align + " " + Baseline + " " + F2(X) + " " + F2(Y) + " " + Paint + " " + Text;
                case "rect": return "rect " + F2(X) + " " + F2(Y) + " " + F2(W) + " " + F2(H) + " " + Paint;
                case "srect": return "srect " + F2(X) + " " + F2(Y) + " " + F2(W) + " " + F2(H) + " " + Paint;
                default: return Kind;
            }
        }
    }

    /// <summary>What a chart drew: its ops, and the hover read-out (null when hidden) with its left edge.</summary>
    public sealed class FxOverviewDrawn
    {
        public List<FxOverviewOp> Ops = new List<FxOverviewOp>();
        /// <summary>The tip's lines: the time, then a label and value per series (null: no tip).</summary>
        public List<string[]> Tip;
        public string TipTime;
        public double TipLeft;
        /// <summary>Per tip line, the series colour (TimeChart).</summary>
        public List<string> TipColors = new List<string>();
    }

    /// <summary>
    /// flux/charts.js drawn here: TimeChart.draw() (the y scale with
    /// niceMax(), the grid and labels, the series split at gaps of more than
    /// three samples, the filled areas and lines, the hover line and dots
    /// with its read-out), HeatStrip.draw() and coreBars(), as the same
    /// canvas calls, so the native charts have the web's geometry.
    /// </summary>
    public static class FxOverviewChart
    {
        public const string Collecting = "collecting samples…";
        public const string AllHidden = "all series hidden";
        public const int MaxBridge = 3;
        public const string Grid = "rgba(160,160,200,0.10)";
        public const string Axis = "rgba(200,200,230,0.55)";
        public const string HoverLine = "rgba(230,230,255,0.45)";
        public const string HeatHover = "rgba(255,255,255,0.8)";

        /// <summary>charts.js C: the palette.</summary>
        public static readonly Dictionary<string, string> C = new Dictionary<string, string>
        {
            { "violet", "#a57cff" }, { "cyan", "#5fd6ec" }, { "green", "#5fdc9a" }, { "amber", "#f2c25b" }, { "red", "#ff7a6b" }, { "pink", "#ff86c4" },
            { "blue", "#6ea8ff" }, { "lime", "#b9e66b" }, { "orange", "#ff9f5a" }, { "teal", "#3fc7b5" }, { "grid", Grid }, { "axis", Axis }
        };

        /// <summary>niceMax(v): 1, 2, 2.5, 5 or 10 times a power of ten.</summary>
        public static double NiceMax(double v)
        {
            if (!(v > 0)) return 1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double n = v / p;
            return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * p;
        }

        /// <summary>data[key]?.[i] as fin() reads it: a finite number, else NaN.</summary>
        public static double At(JsonValue values, int i)
        {
            if (values == null || !values.IsArray) return double.NaN;
            JsonValue v = values[i];
            return Hz.Ok(v) ? v.Num : double.NaN;
        }

        static JsonValue Of(IList<KeyValuePair<string, JsonValue>> data, string key)
        {
            foreach (KeyValuePair<string, JsonValue> kv in data) if (kv.Key == key) return kv.Value;
            return null;
        }

        /// <summary>
        /// TimeChart.draw() at width w: times, data (by series key), the hidden
        /// keys, the pointer (null for none); measure gives a label's width in
        /// 10px monospace, tod writes a time (secs for the tip), tipWidth the
        /// tip's width (for which side it goes). Nothing for a zero width.
        /// </summary>
        public static FxOverviewDrawn Draw(FxOverviewChartOpts opts, double w, IList<double> times, IList<KeyValuePair<string, JsonValue>> data, ICollection<string> hidden, double[] hover, Func<string, double> measure, Func<double, bool, string> tod, double tipWidth)
        {
            FxOverviewDrawn d = new FxOverviewDrawn();
            if (!(w > 0)) return d;
            List<FxOverviewOp> ops = d.Ops;
            double h = opts.Height;
            List<FxOverviewSeries> series = new List<FxOverviewSeries>();
            foreach (FxOverviewSeries s in opts.Series) if (hidden == null || !hidden.Contains(s.Key)) series.Add(s);
            int n = times.Count;
            double top = Hz.Ok(opts.Max) ? opts.Max : 0;
            if (!Hz.Ok(opts.Max))
            {
                double m = 0;
                foreach (FxOverviewSeries s in series)
                {
                    JsonValue vals = Of(data, s.Key);
                    if (vals == null || !vals.IsArray) continue;
                    foreach (JsonValue v in vals.Items) if (Hz.Ok(v) && v.Num > m) m = v.Num;
                }
                top = NiceMax(Math.Max(m * 1.1, opts.Floor));
            }
            double bottom = opts.Min;
            double padL = 44;
            foreach (int g in new[] { 0, 2, 4 }) padL = Math.Max(padL, Math.Ceiling(measure(opts.Format(bottom + ((top - bottom) * g) / 4))) + 10);
            const double padR = 8, padT = 8, padB = 18;
            double pw = Math.Max(10, w - padL - padR);
            double ph = h - padT - padB;
            double range = top - bottom;
            if (range == 0 || double.IsNaN(range)) range = 1;
            Func<double, double> y = v => padT + ph - ((v - bottom) / range) * ph;
            Func<int, double> x = i => padL + (n <= 1 ? pw : ((double)i / (n - 1)) * pw);
            string fill = "", stroke = "", align = "start", baseline = "alphabetic";
            double lineWidth = 1;
            double[] dash = new double[0];
            Action begin = () => ops.Add(new FxOverviewOp { Kind = "begin" });
            Action<double, double> move = (a, b) => ops.Add(new FxOverviewOp { Kind = "M", X = a, Y = b });
            Action<double, double> line = (a, b) => ops.Add(new FxOverviewOp { Kind = "L", X = a, Y = b });
            Action doStroke = () => ops.Add(new FxOverviewOp { Kind = "stroke", Paint = stroke, LineWidth = lineWidth, Dash = dash });
            Action<string, double, double> text = (t, a, b) => ops.Add(new FxOverviewOp { Kind = "text", Text = t, X = a, Y = b, Paint = fill, Align = align, Baseline = baseline });
            // grid
            fill = Axis;
            stroke = Grid;
            lineWidth = 1;
            align = "right";
            baseline = "middle";
            for (int g = 0; g <= 4; g++)
            {
                double v = bottom + ((top - bottom) * g) / 4;
                double yy = Core.Format.Round(y(v)) + 0.5;
                begin();
                move(padL, yy);
                line(padL + pw, yy);
                doStroke();
                if (g % 2 == 0) text(opts.Format(v), padL - 6, yy);
            }
            // time labels
            if (n > 1)
            {
                align = "left";
                baseline = "alphabetic";
                text(tod(times[0], false), padL, h - 4);
                align = "right";
                text(tod(times[n - 1], false), padL + pw, h - 4);
            }
            if (n < 2 || series.Count == 0)
            {
                align = "center";
                fill = Axis;
                text(n < 2 ? Collecting : AllHidden, padL + pw / 2, padT + ph / 2);
                return d;
            }
            // series
            foreach (FxOverviewSeries s in series)
            {
                JsonValue vals = Of(data, s.Key);
                List<double[]> pts = new List<double[]>();
                for (int i = 0; i < n; i++)
                {
                    double v = At(vals, i);
                    if (!Hz.Ok(v)) { pts.Add(null); continue; }
                    pts.Add(new[] { x(i), y(Math.Min(top, v)), y(Math.Max(bottom, bottom)) });
                }
                List<List<double[]>> segs = new List<List<double[]>>();
                List<double[]> cur = new List<double[]>();
                int miss = 0;
                foreach (double[] p in pts)
                {
                    if (p == null) { miss++; continue; }
                    if (miss > MaxBridge && cur.Count > 0) { segs.Add(cur); cur = new List<double[]>(); }
                    miss = 0;
                    cur.Add(p);
                }
                if (cur.Count > 0) segs.Add(cur);
                foreach (List<double[]> seg in segs)
                {
                    if (s.Fill)
                    {
                        FxOverviewOp grad = new FxOverviewOp { GradY0 = padT, GradY1 = padT + ph, GradFrom = s.Color + "55", GradTo = s.Color + "05" };
                        fill = "grad " + Core.Format.ToFixed(0, 2) + " " + Core.Format.ToFixed(padT, 2) + " " + Core.Format.ToFixed(0, 2) + " " + Core.Format.ToFixed(padT + ph, 2) + " 0:" + grad.GradFrom + " 1:" + grad.GradTo;
                        begin();
                        for (int i = 0; i < seg.Count; i++) { if (i > 0) line(seg[i][0], seg[i][1]); else move(seg[i][0], seg[i][1]); }
                        for (int i = seg.Count - 1; i >= 0; i--) line(seg[i][0], seg[i][2]);
                        ops.Add(new FxOverviewOp { Kind = "close" });
                        grad.Kind = "fill";
                        grad.Paint = fill;
                        ops.Add(grad);
                    }
                    stroke = s.Color;
                    lineWidth = s.Width > 0 ? s.Width : 1.6;
                    dash = s.Dash ?? new double[0];
                    begin();
                    for (int i = 0; i < seg.Count; i++) { if (i > 0) line(seg[i][0], seg[i][1]); else move(seg[i][0], seg[i][1]); }
                    if (seg.Count == 1) ops.Add(new FxOverviewOp { Kind = "arc", X = seg[0][0], Y = seg[0][1], R = 1.5 });
                    doStroke();
                    dash = new double[0];
                }
            }
            // hover
            if (hover != null && hover[0] >= padL - 4)
            {
                int i = (int)Math.Max(0, Math.Min(n - 1, Core.Format.Round(((hover[0] - padL) / pw) * (n - 1))));
                double xx = Core.Format.Round(x(i)) + 0.5;
                stroke = HoverLine;
                dash = new double[] { 3, 3 };
                begin();
                move(xx, padT);
                line(xx, padT + ph);
                doStroke();
                dash = new double[0];
                foreach (FxOverviewSeries s in series)
                {
                    double v = At(Of(data, s.Key), i);
                    if (!Hz.Ok(v)) continue;
                    fill = s.Color;
                    begin();
                    ops.Add(new FxOverviewOp { Kind = "arc", X = xx, Y = y(Math.Min(top, v)), R = 3 });
                    ops.Add(new FxOverviewOp { Kind = "fill", Paint = fill });
                }
                d.TipTime = tod(times[i], true);
                d.Tip = new List<string[]>();
                foreach (FxOverviewSeries s in series)
                {
                    double v = At(Of(data, s.Key), i);
                    d.Tip.Add(new[] { s.Label, Hz.Ok(v) ? opts.Format(v) : Hz.Dash });
                    d.TipColors.Add(s.Color);
                }
                double left = xx + 12 + tipWidth > w ? xx - 12 - tipWidth : xx + 12;
                d.TipLeft = Math.Max(0, left);
            }
            return d;
        }

        /// <summary>String(n): the shortest digits that read back as the same number.</summary>
        public static string Js(double n)
        {
            if (!Core.Format.Fin(n) || n == Math.Floor(n)) return Core.Format.JsNum(n);
            for (int p = 15; p <= 17; p++)
            {
                string s = n.ToString("G" + p, System.Globalization.CultureInfo.InvariantCulture);
                double back;
                if (s.IndexOf('E') < 0 && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out back) && back == n) return s;
            }
            return Core.Format.JsNum(n);
        }

        /// <summary>The fill of a heat cell: hsla(hue, 85%, light%, alpha) as the web writes it.</summary>
        public static string HeatColor(double v, out double hue, out double light, out double alpha)
        {
            double t = Math.Max(0, Math.Min(1, v / 100));
            hue = 280 - t * 260;
            light = 28 + t * 32;
            alpha = 0.25 + t * 0.75;
            return "hsla(" + Js(hue) + ", 85%, " + Js(light) + "%, " + Js(alpha) + ")";
        }

        /// <summary>HeatStrip.draw() at width w and height h: a cell per core and sample, the hovered one outlined, its read-out.</summary>
        public static FxOverviewDrawn Heat(double w, double h, IList<double> times, IList<List<JsonValue>> rows, double[] hover, Func<double, bool, string> tod, double tipWidth)
        {
            FxOverviewDrawn d = new FxOverviewDrawn();
            if (!(w > 0)) return d;
            int R = rows.Count, N = times.Count;
            if (R == 0 || N == 0) return d;
            double cw = w / N, rh = h / R;
            for (int r = 0; r < R; r++)
            {
                for (int i = 0; i < N; i++)
                {
                    JsonValue v = i < rows[r].Count ? rows[r][i] : null;
                    if (!Hz.Ok(v)) continue;
                    double hue, light, alpha;
                    string c = HeatColor(v.Num, out hue, out light, out alpha);
                    d.Ops.Add(new FxOverviewOp { Kind = "rect", X = i * cw, Y = r * rh, W = Math.Ceiling(cw) + 0.5, H = Math.Max(1, rh - 1), Paint = c, Hue = hue, Light = light, Alpha = alpha });
                }
            }
            if (hover != null)
            {
                int i = (int)Math.Min(N - 1, Math.Max(0, Math.Floor(hover[0] / cw)));
                int r = (int)Math.Min(R - 1, Math.Max(0, Math.Floor(hover[1] / rh)));
                JsonValue v = i < rows[r].Count ? rows[r][i] : null;
                d.Ops.Add(new FxOverviewOp { Kind = "srect", X = i * cw + 0.5, Y = r * rh + 0.5, W = Math.Max(2, cw - 1), H = Math.Max(2, rh - 2), Paint = HeatHover });
                d.TipTime = tod(times[i], true);
                d.Tip = new List<string[]> { new[] { "core " + r, Hz.Ok(v) ? Core.Format.JsNum(Core.Format.Round(v.Num)) + "%" : Hz.Dash } };
                double left = hover[0] + 14 + tipWidth > w ? hover[0] - 14 - tipWidth : hover[0] + 14;
                d.TipLeft = Math.Max(0, left);
            }
            return d;
        }
    }

    /// <summary>One of coreBars()'s bars: its title, height (2..100 %) and hue.</summary>
    public sealed class FxOverviewCoreBar
    {
        public string Title;
        public double V;
        public double Hue;

        /// <summary>The style's --v as the web writes it ("41%").</summary>
        public string VText { get { return Format.JsNum(V) + "%"; } }

        /// <summary>coreBars(perCore): the bars and the grid's column count (32 at most).</summary>
        public static List<FxOverviewCoreBar> Of(JsonValue perCore, out int columns)
        {
            List<FxOverviewCoreBar> o = new List<FxOverviewCoreBar>();
            columns = Math.Min(perCore.Count, 32);
            int i = 0;
            foreach (JsonValue x in perCore.Items)
            {
                double v = Hz.Number(x);
                double capped = Math.Min(100, v);
                FxOverviewCoreBar b = new FxOverviewCoreBar();
                b.Title = "core " + i + ": " + Format.JsNum(Format.Round(v)) + "%";
                b.V = double.IsNaN(capped) ? double.NaN : Math.Max(2, capped);
                b.Hue = Format.Round(260 - capped * 2.4);
                o.Add(b);
                i++;
            }
            return o;
        }
    }
}
