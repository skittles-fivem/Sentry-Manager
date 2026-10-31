using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SentrySix.Core
{
    /// <summary>
    /// web/app.js's number, size, time and date wording, 1:1 (its top helpers
    /// bytes/plural/when/clockText/took/ago/humanize, the footage page's
    /// minutesText/dayTitle and the server monitor's size/rate/pct/watts/...).
    /// The web's toLocale* calls are drawn as Chrome draws them in en-US;
    /// everything else is JavaScript's own number printing (Number#toFixed,
    /// String(n)), done exactly here so .NET Framework's and .NET's own
    /// rounding do not leak in. test/windowsNativeParity.test.js runs the
    /// web's functions on the values Core.Tests runs these on.
    /// </summary>
    public static class Format
    {
        public const string Dash = "—";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The time zone dates are shown in (the PC's own, as the browser's; the tests fix it).</summary>
        public static TimeZoneInfo Zone = TimeZoneInfo.Local;

        // ------------------------------------------------------------ JavaScript's numbers

        public static bool Fin(double n)
        {
            return !double.IsNaN(n) && !double.IsInfinity(n);
        }

        /// <summary>Math.round: halves go up (towards +∞).</summary>
        public static double Round(double x)
        {
            if (!Fin(x)) return x;
            double f = Math.Floor(x);
            return x - f >= 0.5 ? f + 1 : f;
        }

        /// <summary>
        /// Number#toFixed(digits) for 0..3 digits, from the double's exact binary
        /// value (as the spec says: the n closest to x·10^f, the larger on a tie).
        /// </summary>
        public static string ToFixed(double x, int digits)
        {
            if (double.IsNaN(x)) return "NaN";
            if (digits < 0 || digits > 3) throw new ArgumentOutOfRangeException("digits");
            if (Math.Abs(x) >= 1e15) return JsNum(x);
            string sign = "";
            if (x < 0) { sign = "-"; x = -x; }
            ulong n = ScaledRound(x, digits);
            string s = n.ToString(Inv);
            if (digits == 0) return sign + s;
            if (s.Length <= digits) s = new string('0', digits - s.Length + 1) + s;
            return sign + s.Substring(0, s.Length - digits) + "." + s.Substring(s.Length - digits);
        }

        /// <summary>round-half-up(x · 10^digits) for 0 ≤ x &lt; 1e15, exactly.</summary>
        static ulong ScaledRound(double x, int digits)
        {
            if (x == 0) return 0;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            ulong mant = (ulong)(bits & 0xFFFFFFFFFFFFFL);
            if (exp == 0) exp = 1; else mant |= 1UL << 52;
            int e = exp - 1075; // x = mant · 2^e
            ulong scale = 1;
            for (int i = 0; i < digits; i++) scale *= 10;
            ulong m = mant * scale; // < 2^53 · 1000 < 2^63
            if (e >= 0) return m << e; // (x < 1e15: e is small)
            int s = -e;
            if (s >= 64) return 0;
            ulong half = 1UL << (s - 1);
            return (m + half) >> s;
        }

        /// <summary>String(n) for the numbers the pages print (integers plainly; others shortest round-trip).</summary>
        public static string JsNum(double x)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsPositiveInfinity(x)) return "Infinity";
            if (double.IsNegativeInfinity(x)) return "-Infinity";
            if (x == 0) return "0";
            if (x == Math.Floor(x) && Math.Abs(x) < 9e18) return ((long)x).ToString(Inv);
            string r = x.ToString("R", Inv);
            if (r.IndexOf('E') < 0) return r;
            decimal d;
            if (decimal.TryParse(r, NumberStyles.Float, Inv, out d)) return d.ToString(Inv);
            return r;
        }

        /// <summary>Number#toLocaleString() in en-US: grouped, at most 3 decimals, halves away from 0.</summary>
        public static string Locale(double x)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return x > 0 ? "∞" : "-∞";
            decimal d;
            if (!decimal.TryParse(x.ToString("R", Inv), NumberStyles.Float, Inv, out d)) return x.ToString("#,##0.###", Inv);
            d = Math.Round(d, 3, MidpointRounding.AwayFromZero);
            string s = d.ToString("#,##0.###", Inv);
            return s == "-0" ? "-0" : s;
        }

        // ------------------------------------------------------------ app.js top helpers

        /// <summary>bytes(n): B KiB MiB GiB TiB PiB, no decimals for B; "—" for none.</summary>
        public static string Bytes(double n)
        {
            if (!Fin(n)) return Dash;
            string[] u = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
            double v = n;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return ToFixed(v, i == 0 ? 0 : 1) + " " + u[i];
        }

        /// <summary>bytes() of a JSON value: null and non-numbers are "—" (Number(null) is 0 in JS, but null is checked first).</summary>
        public static string Bytes(JsonValue n)
        {
            if (n == null || n.IsNull) return Dash;
            if (n.IsNumber) return Bytes(n.Num);
            if (n.IsBool) return Bytes(n.Bool ? 1 : 0);
            if (n.IsString)
            {
                string t = n.Str.Trim();
                if (t.Length == 0) return Bytes(0);
                double d;
                return double.TryParse(t, NumberStyles.Float, Inv, out d) ? Bytes(d) : Dash;
            }
            return Dash;
        }

        /// <summary>plural(n, w): "1 recording", "1,204 recordings", "0 recordings".</summary>
        public static string Plural(double n, string word)
        {
            double v = Fin(n) ? n : 0;
            return Locale(v) + " " + word + (n == 1 ? "" : "s");
        }

        static DateTime Local(long ms)
        {
            DateTime utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone ?? TimeZoneInfo.Local);
        }

        static string Time(DateTime t, bool h24)
        {
            if (h24) return t.ToString("HH:mm:ss", Inv);
            int h = t.Hour % 12;
            if (h == 0) h = 12;
            return h.ToString(Inv) + ":" + t.ToString("mm:ss", Inv) + " " + (t.Hour < 12 ? "AM" : "PM");
        }

        static string Date(DateTime t)
        {
            return t.Month.ToString(Inv) + "/" + t.Day.ToString(Inv) + "/" + t.Year.ToString(Inv);
        }

        /// <summary>when(ts): "10/31/2026, 3:10:02 PM" (or "10/31/2026, 15:10:02"); "—" for 0.</summary>
        public static string When(long ms, bool h24)
        {
            if (ms == 0) return Dash;
            DateTime t = Local(ms);
            return Date(t) + ", " + Time(t, h24);
        }

        public static string When(JsonValue ts, bool h24)
        {
            if (ts == null || !ts.Bool) return Dash;
            return When((long)ts.Num, h24);
        }

        /// <summary>toLocaleDateString(): "10/20/2026".</summary>
        public static string DateText(long ms)
        {
            return Date(Local(ms));
        }

        static readonly Regex ClockRe = new Regex(@"(\d{1,2}):(\d{2})(:\d{2})?\z", RegexOptions.CultureInvariant);

        /// <summary>clockText(str): "2026-10-29 15:10:02" on the 12-hour clock is "2026-10-29 3:10:02 PM".</summary>
        public static string ClockText(string str, bool h24)
        {
            string s = str ?? "";
            if (h24) return s;
            return ClockRe.Replace(s, delegate(Match m)
            {
                int h = int.Parse(m.Groups[1].Value, Inv);
                return (h % 12 == 0 ? 12 : h % 12).ToString(Inv) + ":" + m.Groups[2].Value + m.Groups[3].Value + " " + (h < 12 ? "AM" : "PM");
            }, 1);
        }

        /// <summary>took(ms): "42 s", "12 m 5 s", "8 h 0 m".</summary>
        public static string Took(double ms)
        {
            double s = Math.Max(0, Round(ms / 1000));
            if (s < 60) return JsNum(s) + " s";
            if (s < 3600) return JsNum(Math.Floor(s / 60)) + " m " + JsNum(s % 60) + " s";
            return JsNum(Math.Floor(s / 3600)) + " h " + JsNum(Math.Floor((s % 3600) / 60)) + " m";
        }

        /// <summary>ago(ts): "never", "just now", "5 min ago", "3 h ago", else the date.</summary>
        public static string Ago(long ms, long nowMs)
        {
            if (ms == 0) return "never";
            double s = Round((nowMs - ms) / 1000.0);
            if (s < 60) return "just now";
            if (s < 3600) return JsNum(Round(s / 60)) + " min ago";
            if (s < 86400) return JsNum(Round(s / 3600)) + " h ago";
            return DateText(ms);
        }

        /// <summary>Now as JavaScript's Date.now().</summary>
        public static long NowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        static readonly Regex FirstWord = new Regex(@"^[A-Za-z0-9_]", RegexOptions.CultureInvariant);

        /// <summary>humanize(s): "low_space" is "Low space".</summary>
        public static string Humanize(string s)
        {
            string t = (s ?? "").Replace('_', ' ');
            return FirstWord.Replace(t, delegate(Match m) { return m.Value.ToUpperInvariant(); }, 1);
        }

        // ------------------------------------------------------------ footage

        /// <summary>minutesText(m): "1 h 5 min", "42 min".</summary>
        public static string MinutesText(double m)
        {
            return m >= 60 ? JsNum(Math.Floor(m / 60)) + " h " + JsNum(m % 60) + " min" : JsNum(m) + " min";
        }

        static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        /// <summary>dayTitle("2026-10-20"): "Tuesday, Oct 20, 2026" (weekday long, month short).</summary>
        public static string DayTitle(string day)
        {
            DateTime d;
            if (day == null || !DateTime.TryParseExact(day, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d)) return "Invalid Date";
            return d.DayOfWeek.ToString() + ", " + Months[d.Month - 1] + " " + d.Day.ToString(Inv) + ", " + d.Year.ToString(Inv);
        }

        // ------------------------------------------------------------ server monitor (app.js "server monitor: drawing")

        /// <summary>clampPct(n): 0..100, one decimal; not a number is 0.</summary>
        public static double ClampPct(double n)
        {
            return Math.Max(0, Math.Min(100, Round((Fin(n) ? n : 0) * 10) / 10));
        }

        static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

        static int SizeUnit(double b)
        {
            int i = 0;
            for (double v = Math.Abs(b); v >= 1024 && i < SizeUnits.Length - 1; v /= 1024) i++;
            return i;
        }

        static string SizeNum(double b, int i)
        {
            double v = b / Math.Pow(1024, i);
            if (v >= 100 || i == 0) return JsNum(Round(v));
            string s = ToFixed(v, 1);
            return s.EndsWith(".0", StringComparison.Ordinal) ? s.Substring(0, s.Length - 2) : s;
        }

        /// <summary>size(b): "18.2 GB", "640 MB"; "—" for none.</summary>
        public static string Size(double b)
        {
            if (!Fin(b)) return Dash;
            int i = SizeUnit(b);
            return SizeNum(b, i) + " " + SizeUnits[i];
        }

        /// <summary>sizeOf(used, total, sep): "112 of 476 GB", "1.1 / 6 GB" (both in the total's unit).</summary>
        public static string SizeOf(double used, double total, string sep)
        {
            int i = SizeUnit(total);
            return SizeNum(used, i) + (sep ?? " of ") + SizeNum(total, i) + " " + SizeUnits[i];
        }

        public static string SizeOf(double used, double total)
        {
            return SizeOf(used, total, " of ");
        }

        /// <summary>rate(bps): "12 KB/s" below 1 MB/s, else "112 MB/s".</summary>
        public static string Rate(double bps)
        {
            if (!Fin(bps)) return Dash;
            if (bps > 0 && bps < 1024.0 * 1024) return JsNum(Math.Max(1, Round(bps / 1024))) + " KB/s";
            int i = Math.Max(2, SizeUnit(bps));
            return SizeNum(bps, i) + " " + SizeUnits[i] + "/s";
        }

        public static string Pct(double n)
        {
            return JsNum(Round(n)) + "%";
        }

        public static string Watts(double w)
        {
            string n;
            if (w >= 10) n = JsNum(Round(w));
            else
            {
                n = ToFixed(w, 1);
                if (n.EndsWith(".0", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 2);
            }
            return n + " W";
        }

        public static string Temp(double c)
        {
            return JsNum(Round(c)) + " °C";
        }

        /// <summary>Number(x.toFixed(d)) printed: the fixed digits without trailing zeros.</summary>
        static string FixedTrim(double x, int digits)
        {
            string s = ToFixed(x, digits);
            if (s.IndexOf('.') < 0) return s == "-0" ? "0" : s;
            s = s.TrimEnd('0').TrimEnd('.');
            return s == "-0" ? "0" : s;
        }

        /// <summary>clock(mhz): "3.45 GHz", "800 MHz".</summary>
        public static string Clock(double mhz)
        {
            return mhz >= 1000 ? FixedTrim(mhz / 1000, 2) + " GHz" : JsNum(Round(mhz)) + " MHz";
        }

        /// <summary>energyText(wh): "12.4 Wh" below 100 Wh, "412 Wh", then "5.2 kWh".</summary>
        public static string EnergyText(double wh)
        {
            if (Round(wh * 10) / 10 < 100) return ToFixed(Round(wh * 10) / 10, 1) + " Wh";
            if (Round(wh) < 1000) return JsNum(Round(wh)) + " Wh";
            double k = wh / 1000;
            return (Round(k * 10) / 10 < 100 ? ToFixed(k, 1) : Locale(Round(k))) + " kWh";
        }

        static readonly Regex Letters = new Regex(@"^[a-z]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>inCurrency(n, cur): "$0.14" but "1.20 kr".</summary>
        public static string InCurrency(string n, string cur)
        {
            string c = string.IsNullOrEmpty(cur) ? "$" : cur;
            return Letters.IsMatch(c) ? n + " " + c : c + n;
        }

        /// <summary>money(v, cur): "≈ $0.14", "≈ $1,204".</summary>
        public static string Money(double v, string cur)
        {
            return "≈ " + InCurrency(v >= 100 ? Locale(Round(v)) : ToFixed(v, 2), cur);
        }

        /// <summary>span(s): "3 d 4 h", "2 h 5 m", "12 m", "40 s".</summary>
        public static string Span(double s)
        {
            double d = Math.Floor(s / 86400);
            double h = Math.Floor((s % 86400) / 3600);
            double m = Math.Floor((s % 3600) / 60);
            if (d != 0) return JsNum(d) + " d " + JsNum(h) + " h";
            if (h != 0) return JsNum(h) + " h " + JsNum(m) + " m";
            if (m != 0) return JsNum(m) + " m";
            return JsNum(Math.Max(0, Round(s))) + " s";
        }

        /// <summary>answer(ms): "under 1 ms", "42 ms", "1.5 s".</summary>
        public static string Answer(double ms)
        {
            if (ms < 1) return "under 1 ms";
            if (ms < 1000) return JsNum(Round(ms)) + " ms";
            string s = ToFixed(ms / 1000, 1);
            if (s.EndsWith(".0", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 2);
            return s + " s";
        }

        /// <summary>linkSpeed(mbps): "2.5 Gbit/s", "100 Mbit/s", "" for none.</summary>
        public static string LinkSpeed(double mbps)
        {
            if (!Fin(mbps) || mbps <= 0) return "";
            return mbps >= 1000 ? FixedTrim(mbps / 1000, 1) + " Gbit/s" : JsNum(Round(mbps)) + " Mbit/s";
        }

        static readonly Regex Trade = new Regex(@"\((R|TM)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex AtClock = new Regex(@"\s+CPU\s+@.*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Radeon = new Regex(@"\s+with\s+Radeon.*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

        /// <summary>cpuName(m): "Intel(R) Core(TM) i5-12500 CPU @ 3.00GHz" is "Intel Core i5-12500".</summary>
        public static string CpuName(string model)
        {
            string s = model ?? "";
            s = Trade.Replace(s, "");
            s = AtClock.Replace(s, "");
            s = Radeon.Replace(s, "");
            s = Spaces.Replace(s, " ");
            return s.Trim();
        }

        /// <summary>A text with the browser's &lt;wbr&gt; spots: a zero-width space after each / \ _ - . (long names wrap there).</summary>
        public static string Breakable(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            StringBuilder b = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                b.Append(c);
                if (c == '/' || c == '\\' || c == '_' || c == '-' || c == '.') b.Append('​');
            }
            return b.ToString();
        }
    }
}
