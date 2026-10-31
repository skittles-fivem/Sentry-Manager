using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SentrySix.Core.Horizon
{
    /// <summary>
    /// The Horizon pack's formatting (examples/ui-packs/horizon/web/hz/core.js:
    /// size, perSec, pc, fixed, count, span, mmss, since, stamp, stampFull,
    /// wall, wallDay, watt, wh, cash, words, dayName), checked against the
    /// pack's own functions by test/windowsNativeHorizon.test.js
    /// (Core.Tests/Fixtures/expected-horizon-format.json). Dates are shown in
    /// <see cref="Format.Zone"/>, as the browser's local time.
    /// </summary>
    public static partial class Hz
    {
        public const string Dash = "—";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] Sizes = { "B", "KB", "MB", "GB", "TB", "PB" };
        static readonly string[] MonShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        static readonly string[] MonLong = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        static readonly string[] DayShort = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        // ------------------------------------------------------------ JavaScript's values

        /// <summary>ok(n): a finite number (not a numeric string).</summary>
        public static bool Ok(JsonValue v)
        {
            return v != null && v.IsNumber && Format.Fin(v.Num);
        }

        public static bool Ok(double n)
        {
            return Format.Fin(n);
        }

        /// <summary>Number(x): undefined/objects NaN, null 0, booleans 0/1, strings their number ("" 0).</summary>
        public static double Number(JsonValue v)
        {
            if (v == null) return double.NaN;
            switch (v.Kind)
            {
                case JsonKind.Null: return 0;
                case JsonKind.Bool: return v.Bool ? 1 : 0;
                case JsonKind.Number: return v.Num;
                case JsonKind.String:
                    {
                        string s = v.Str.Trim();
                        if (s.Length == 0) return 0;
                        double d;
                        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            long l;
                            return long.TryParse(s.Substring(2), NumberStyles.HexNumber, Inv, out l) ? l : double.NaN;
                        }
                        if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
                        if (s == "-Infinity") return double.NegativeInfinity;
                        return double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, Inv, out d) ? d : double.NaN;
                    }
                case JsonKind.Array:
                    if (v.Count == 0) return 0;
                    if (v.Count == 1) return Number(v[0].IsArray || v[0].IsObject ? JsonValue.Null : JsonValue.String(Js(v[0])));
                    return double.NaN;
                default: return double.NaN;
            }
        }

        /// <summary>A value that is missing (undefined): the key is not there.</summary>
        public static bool Missing(JsonValue owner, string key)
        {
            return owner == null || !owner.IsObject || !owner.Has(key);
        }

        /// <summary>String(x ?? ''), as h() reads a value (null and undefined are "").</summary>
        public static string Js(JsonValue v)
        {
            if (v == null || v.IsNull) return "";
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.IsString) return v.Str;
            if (v.IsBool) return v.Bool ? "true" : "false";
            if (v.IsArray)
            {
                List<string> parts = new List<string>();
                foreach (JsonValue x in v.Items) parts.Add(Js(x));
                return string.Join(",", parts);
            }
            return "[object Object]";
        }

        /// <summary>`${o.key}` in a template string: undefined "undefined", null "null", else String().</summary>
        public static string Tpl(JsonValue owner, string key)
        {
            if (Missing(owner, key)) return "undefined";
            JsonValue v = owner[key];
            return v.IsNull ? "null" : Js(v);
        }

        /// <summary>v || '' as text: a falsy value is "", else String(v).</summary>
        public static string Or(JsonValue v)
        {
            return v != null && v.Bool ? Js(v) : "";
        }

        /// <summary>encodeURIComponent(id), exactly.</summary>
        public static string EncId(string id)
        {
            return Footage.EncUri(id);
        }

        // ------------------------------------------------------------ numbers

        /// <summary>size(n, digits): "118 MB", "1.5 GB", "512 B"; "—" for null, undefined or not a number.</summary>
        public static string Size(JsonValue n, int digits = 1)
        {
            if (n == null || n.IsNull) return Dash;
            return Size(Number(n), digits);
        }

        public static string Size(double n, int digits = 1)
        {
            if (!Ok(n)) return Dash;
            double v = n;
            int i = 0;
            while (Math.Abs(v) >= 1024 && i < Sizes.Length - 1) { v /= 1024; i++; }
            return (i > 0 ? Format.ToFixed(v, v >= 100 ? 0 : digits) : Format.JsNum(Format.Round(v))) + " " + Sizes[i];
        }

        /// <summary>perSec(n): "12 MB/s"; "—" when n is not a number.</summary>
        public static string PerSec(JsonValue n)
        {
            return Ok(n) ? Size(n.Num) + "/s" : Dash;
        }

        /// <summary>pc(n, d): "42%".</summary>
        public static string Pc(JsonValue n, int d = 0)
        {
            return Ok(n) ? Format.ToFixed(n.Num, d) + "%" : Dash;
        }

        /// <summary>fixed(n, d): toLocaleString with d decimals ("1,204", "3.50").</summary>
        public static string Fixed(JsonValue n, int d = 0)
        {
            return Ok(n) ? Grouped(n.Num, d, d) : Dash;
        }

        /// <summary>Number#toLocaleString('en-US', { minimumFractionDigits, maximumFractionDigits }).</summary>
        public static string Grouped(double x, int minD, int maxD)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return x > 0 ? "∞" : "-∞";
            decimal d;
            if (!decimal.TryParse(x.ToString("R", Inv), NumberStyles.Float, Inv, out d)) return x.ToString("#,##0", Inv);
            d = Math.Round(d, maxD, MidpointRounding.AwayFromZero);
            string fmt = "#,##0" + (maxD > 0 ? "." + new string('0', minD) + new string('#', maxD - minD) : "");
            string s = d.ToString(fmt, Inv);
            if (s.StartsWith("-") && d == 0) s = s.Substring(1).Insert(0, "-");
            return s;
        }

        /// <summary>count(n, one, many): "1 job", "1,204 jobs", "0 jobs" (n || 0).</summary>
        public static string Count(JsonValue n, string one, string many = null)
        {
            double x = n != null && n.Bool ? Number(n) : 0;
            double eq = n == null ? double.NaN : Number(n);
            return Format.Locale(x) + " " + (eq == 1 ? one : (many ?? one + "s"));
        }

        public static string Count(double n, string one, string many = null)
        {
            double x = Format.Fin(n) || double.IsInfinity(n) ? n : 0;
            if (n == 0) x = 0;
            return Format.Locale(x) + " " + (n == 1 ? one : (many ?? one + "s"));
        }

        static string Pad2(double n)
        {
            string s = Format.JsNum(n);
            return s.Length < 2 ? "0" + s : s;
        }

        /// <summary>span(ms): "42s", "12m 05s", "1h 02m", "3d 4h"; "—" for not a number or negative.</summary>
        public static string Span(double ms)
        {
            if (!Ok(ms) || ms < 0) return Dash;
            double s = Format.Round(ms / 1000);
            if (s < 60) return Format.JsNum(s) + "s";
            if (s < 3600) return Format.JsNum(Math.Floor(s / 60)) + "m " + Pad2(s % 60) + "s";
            if (s < 86400) return Format.JsNum(Math.Floor(s / 3600)) + "h " + Pad2(Math.Floor((s % 3600) / 60)) + "m";
            return Format.JsNum(Math.Floor(s / 86400)) + "d " + Format.JsNum(Math.Floor((s % 86400) / 3600)) + "h";
        }

        /// <summary>mmss(sec): "3:07"; "0:00" for not a number or negative.</summary>
        public static string Mmss(double sec)
        {
            if (!Ok(sec) || sec < 0) return "0:00";
            double m = Math.Floor(sec / 60);
            return Format.JsNum(m) + ":" + Pad2(Math.Floor(sec - m * 60));
        }

        /// <summary>since(ts): "never", "just now", "42s ago", "5 min ago", "3 h ago", "2 d ago".</summary>
        public static string Since(JsonValue ts, long now)
        {
            if (ts == null || !ts.Bool) return "never";
            double s = Format.Round((now - Number(ts)) / 1000);
            if (double.IsNaN(s)) return "NaN d ago";
            if (s < 5) return "just now";
            if (s < 60) return Format.JsNum(s) + "s ago";
            if (s < 3600) return Format.JsNum(Format.Round(s / 60)) + " min ago";
            if (s < 86400) return Format.JsNum(Format.Round(s / 3600)) + " h ago";
            return Format.JsNum(Format.Round(s / 86400)) + " d ago";
        }

        // ------------------------------------------------------------ dates

        static DateTime Local(double ms)
        {
            DateTime utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, Format.Zone ?? TimeZoneInfo.Local);
        }

        static string Clock(DateTime t, bool h24, bool secs)
        {
            string tail = ":" + t.ToString("mm", Inv) + (secs ? ":" + t.ToString("ss", Inv) : "");
            if (h24) return t.ToString("HH", Inv) + tail;
            int h = t.Hour % 12;
            if (h == 0) h = 12;
            return h.ToString(Inv) + tail + " " + (t.Hour < 12 ? "AM" : "PM");
        }

        static bool Date(JsonValue ts, out double ms)
        {
            ms = double.NaN;
            if (ts == null || !ts.Bool) return false;
            ms = Number(ts);
            if (ts.IsString)
            {
                // new Date("…") of a date string
                DateTime d;
                if (DateTime.TryParse(ts.Str, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d) && double.IsNaN(ms))
                    ms = (d - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            }
            return true;
        }

        /// <summary>stamp(ts): "Oct 31, 3:10 PM" (or "Oct 31, 15:10"); "—" without one.</summary>
        public static string Stamp(JsonValue ts, bool h24)
        {
            double ms;
            if (!Date(ts, out ms)) return Dash;
            return Stamp(ms, h24);
        }

        public static string Stamp(double ms, bool h24)
        {
            if (ms == 0) return Dash;
            if (!Ok(ms)) return "Invalid Date";
            DateTime t = Local(ms);
            return MonShort[t.Month - 1] + " " + t.Day.ToString(Inv) + ", " + Clock(t, h24, false);
        }

        /// <summary>stampFull(ts): "Oct 31, 2026, 3:10:02 PM" (or "…, 15:10:02"); "—" without one.</summary>
        public static string StampFull(JsonValue ts, bool h24)
        {
            double ms;
            if (!Date(ts, out ms)) return Dash;
            return StampFull(ms, h24);
        }

        public static string StampFull(double ms, bool h24)
        {
            if (ms == 0) return Dash;
            if (!Ok(ms)) return "Invalid Date";
            DateTime t = Local(ms);
            return MonShort[t.Month - 1] + " " + t.Day.ToString(Inv) + ", " + t.Year.ToString(Inv) + ", " + Clock(t, h24, true);
        }

        static readonly Regex WallRe = new Regex(@"(?:^|[ T_])(\d{1,2})[:-](\d{2})(?:[:-](\d{2}))?$", RegexOptions.CultureInvariant);

        /// <summary>wall(s, secs): the car's wall-clock text ("2024-05-01 06:07:45", "…_06-07-45") as "6:07:45 AM" or "06:07:45"; anything else as it is.</summary>
        public static string Wall(string s, bool h24, bool secs = true)
        {
            string str = s ?? "";
            Match m = WallRe.Match(str);
            if (!m.Success) return str;
            string tail = ":" + m.Groups[2].Value + (secs && m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? ":" + m.Groups[3].Value : "");
            int hr = int.Parse(m.Groups[1].Value, Inv);
            if (h24) return (hr < 10 ? "0" : "") + hr.ToString(Inv) + tail;
            return (hr % 12 == 0 ? 12 : hr % 12).ToString(Inv) + tail + " " + (hr < 12 ? "AM" : "PM");
        }

        /// <summary>wallDay(s): "2024-05-01 6:07:45 AM"; "" for none.</summary>
        public static string WallDay(string s, bool h24)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return (s.Length > 10 ? s.Substring(0, 10) : s) + " " + Wall(s, h24);
        }

        /// <summary>watt(w): "38 W", "4.2 W".</summary>
        public static string Watt(JsonValue w)
        {
            return Ok(w) ? Watt(w.Num) : Dash;
        }

        public static string Watt(double w)
        {
            if (!Ok(w)) return Dash;
            return (w >= 10 ? Format.JsNum(Format.Round(w)) : Format.ToFixed(w, 1)) + " W";
        }

        /// <summary>wh(x): "12.4 Wh", "413 Wh", "5.21 kWh".</summary>
        public static string Wh(JsonValue x)
        {
            return Ok(x) ? Wh(x.Num) : Dash;
        }

        public static string Wh(double x)
        {
            if (!Ok(x)) return Dash;
            if (x < 1000) return Format.ToFixed(x, x < 100 ? 1 : 0) + " Wh";
            return Format.ToFixed(x / 1000, 2) + " kWh";
        }

        static readonly Regex Letters = new Regex("^[a-z]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>cash(v, cur): "$0.14", "154 kr", "$1,204".</summary>
        public static string Cash(JsonValue v, string cur)
        {
            return Ok(v) ? Cash(v.Num, cur) : Dash;
        }

        public static string Cash(double v, string cur)
        {
            if (!Ok(v)) return Dash;
            string c = cur ?? "$";
            string n = v >= 100 ? Format.Locale(Format.Round(v)) : Format.ToFixed(v, 2);
            return Letters.IsMatch(c) ? n + " " + c : c + n;
        }

        static readonly Regex Seps = new Regex("[_-]+", RegexOptions.CultureInvariant);
        static readonly Regex First = new Regex(@"^\w", RegexOptions.CultureInvariant | RegexOptions.ECMAScript);

        /// <summary>words(s): "low_space" is "Low space".</summary>
        public static string Words(string s)
        {
            string t = Seps.Replace(s ?? "", " ");
            return First.Replace(t, m => m.Value.ToUpperInvariant(), 1);
        }

        /// <summary>dayName("2026-10-31"): "Sat, Oct 31"; long: "Saturday, October 31, 2026"; not a day: as it is.</summary>
        public static string DayName(string day, bool longForm = false)
        {
            DateTime d;
            if (day == null || !TryDay(day, out d)) return day;
            if (longForm) return d.DayOfWeek.ToString() + ", " + MonLong[d.Month - 1] + " " + d.Day.ToString(Inv) + ", " + d.Year.ToString(Inv);
            return DayShort[(int)d.DayOfWeek] + ", " + MonShort[d.Month - 1] + " " + d.Day.ToString(Inv);
        }

        static bool TryDay(string day, out DateTime d)
        {
            return DateTime.TryParseExact(day, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d);
        }

        // ------------------------------------------------------------ cameras and codecs

        public static readonly string[] Cams = { "front", "back", "left_repeater", "right_repeater", "left_pillar", "right_pillar" };

        public static string CamName(string cam)
        {
            switch (cam)
            {
                case "front": return "Front";
                case "back": return "Rear";
                case "left_repeater": return "Left repeater";
                case "right_repeater": return "Right repeater";
                case "left_pillar": return "Left pillar";
                case "right_pillar": return "Right pillar";
                default: return null;
            }
        }

        public static string CamTag(string cam)
        {
            switch (cam)
            {
                case "front": return "F";
                case "back": return "R";
                case "left_repeater": return "LR";
                case "right_repeater": return "RR";
                case "left_pillar": return "LP";
                case "right_pillar": return "RP";
                default: return null;
            }
        }

        public static string CodecName(string codec)
        {
            switch (codec)
            {
                case "h264": return "H.264";
                case "h265":
                case "hevc": return "H.265";
                case "av1": return "AV1";
                default: return null;
            }
        }
    }
}
