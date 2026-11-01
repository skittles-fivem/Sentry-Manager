using System;
using System.Globalization;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Daylight
{
    /// <summary>shareHealth(v): the tone ("good", "warn", "bad", "grey"), the words, ok and the detail.</summary>
    public sealed class DlHealth
    {
        public string Tone, Word, Detail = "";
        public bool Ok;
    }

    /// <summary>
    /// The Daylight pack's formatting (examples/ui-packs/daylight/web/dl/core.js:
    /// bytes, plural, pct, ago, wall, when, duration, clockOf, dayTitle,
    /// dayShort, hhmm, niceTime, humanize, toldHow, CATEGORY, CODEC; and
    /// dl/state.js shareHealth), checked against the pack's own functions by
    /// test/windowsNativeDaylight.test.js (Core.Tests/Fixtures/expected-daylight-format.json).
    /// Where Daylight's code is the same as Horizon's it is Horizon's (Core/Horizon/Hz.cs).
    /// </summary>
    public static partial class Dl
    {
        public const string Dash = "—";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };
        static readonly string[] MonShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        static readonly string[] MonLong = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        static readonly string[] DayShortNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        /// <summary>fin(n): a finite number (not a numeric string).</summary>
        public static bool Fin(JsonValue v) { return Hz.Ok(v); }

        /// <summary>bytes(n): "118 MB", "1.5 GB", "512 B"; "—" for null or not a number (numeric strings count).</summary>
        public static string Bytes(JsonValue n)
        {
            if (n == null || n.IsNull) return Dash;
            return Bytes(Hz.Number(n));
        }

        public static string Bytes(double n)
        {
            if (!Format.Fin(n)) return Dash;
            double v = Math.Abs(n);
            int i = 0;
            while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
            string text = i == 0 || v >= 100 ? Format.JsNum(Format.Round(v)) : Format.ToFixed(v, 1);
            if (text.EndsWith(".0")) text = text.Substring(0, text.Length - 2);
            return (n < 0 ? "-" : "") + text + " " + Units[i];
        }

        /// <summary>plural(n, word, many): "1 job" when Number(n) is 1 ("1" and true too), else "1,204 jobs".</summary>
        public static string Plural(JsonValue n, string word, string many = null)
        {
            double x = n != null && n.Bool ? Hz.Number(n) : 0;
            return Format.Locale(x) + " " + (Hz.Number(n) == 1 ? word : (many ?? word + "s"));
        }

        public static string Plural(double n, string word, string many = null)
        {
            double x = n == 0 || double.IsNaN(n) ? 0 : n;
            return Format.Locale(x) + " " + (n == 1 ? word : (many ?? word + "s"));
        }

        /// <summary>pct(n): Math.round(n) + "%" (null is 0%, not a number NaN%).</summary>
        public static string Pct(JsonValue n)
        {
            return Pct(Hz.Number(n));
        }

        public static string Pct(double n)
        {
            return Format.JsNum(Format.Round(n)) + "%";
        }

        static DateTime Local(double ms)
        {
            DateTime utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, Format.Zone ?? TimeZoneInfo.Local);
        }

        /// <summary>new Date(ts) of a value: a number as it is, true 1, false and null 0; anything else NaN.</summary>
        static double Ms(JsonValue ts)
        {
            if (ts == null) return double.NaN;
            if (ts.IsNumber || ts.IsBool || ts.IsNull) return Hz.Number(ts);
            return double.NaN;
        }

        static bool Valid(double ms) { return Format.Fin(ms) && Math.Abs(ms) <= 8.64e15; }

        /// <summary>toLocaleDateString() in en-US: "10/31/2026".</summary>
        public static string DateText(double ms)
        {
            if (!Valid(ms)) return "Invalid Date";
            DateTime t = Local(ms);
            return t.Month.ToString(Inv) + "/" + t.Day.ToString(Inv) + "/" + t.Year.ToString(Inv);
        }

        /// <summary>ago(ts): "never", "just now" (under 45 s), "5 min ago", "3 h ago", "2 d ago", then the date.</summary>
        public static string Ago(JsonValue ts, long now)
        {
            if (ts == null || !ts.Bool) return "never";
            double ms = Ms(ts);
            double s = Format.Round((now - ms) / 1000);
            if (s < 45) return "just now";
            if (s < 3600) return Format.JsNum(Math.Max(1, Format.Round(s / 60))) + " min ago";
            if (s < 86400) return Format.JsNum(Format.Round(s / 3600)) + " h ago";
            if (s < 7 * 86400) return Format.JsNum(Format.Round(s / 86400)) + " d ago";
            return DateText(ms);
        }

        /// <summary>wall(s, secs): as Horizon's.</summary>
        public static string Wall(string s, bool h24, bool secs = true) { return Hz.Wall(s, h24, secs); }

        /// <summary>when(ts): "Oct 31, 3:10 PM" (24-hour: "Oct 31, 15:10"); "—" without one.</summary>
        public static string When(JsonValue ts, bool h24)
        {
            if (ts == null || !ts.Bool) return Dash;
            double ms = Ms(ts);
            if (!Valid(ms)) return "Invalid Date";
            DateTime t = Local(ms);
            string clock;
            if (h24) clock = t.ToString("HH", Inv) + ":" + t.ToString("mm", Inv);
            else
            {
                int h = t.Hour % 12;
                if (h == 0) h = 12;
                clock = h.ToString(Inv) + ":" + t.ToString("mm", Inv) + " " + (t.Hour < 12 ? "AM" : "PM");
            }
            return MonShort[t.Month - 1] + " " + t.Day.ToString(Inv) + ", " + clock;
        }

        /// <summary>duration(ms): "42 s", "12 min 5 s", "12 min", "3 h 2 min", "2 d 3 h"; "—" for not a number or below 0.</summary>
        public static string Duration(double ms)
        {
            if (!Format.Fin(ms) || ms < 0) return Dash;
            double s = Format.Round(ms / 1000);
            if (s < 60) return Format.JsNum(s) + " s";
            if (s < 3600) return (Format.JsNum(Math.Floor(s / 60)) + " min " + (s % 60 != 0 ? Format.JsNum(s % 60) + " s" : "")).Trim();
            double h = Math.Floor(s / 3600);
            if (h < 48) return Format.JsNum(h) + " h " + Format.JsNum(Math.Floor((s % 3600) / 60)) + " min";
            return Format.JsNum(Math.Floor(h / 24)) + " d " + Format.JsNum(h % 24) + " h";
        }

        /// <summary>duration() of a value: only a number counts (fin()).</summary>
        public static string Duration(JsonValue ms)
        {
            return Fin(ms) ? Duration(ms.Num) : Dash;
        }

        /// <summary>clockOf(ms): "1:05" (minutes and seconds, below 0 is 0:00).</summary>
        public static string ClockOf(double ms)
        {
            if (double.IsNaN(ms)) return "NaN:NaN";
            double s = Math.Max(0, Math.Floor(ms / 1000));
            if (double.IsInfinity(s)) return "Infinity:NaN";
            double r = s % 60;
            return Format.JsNum(Math.Floor(s / 60)) + ":" + (r < 10 ? "0" : "") + Format.JsNum(r);
        }

        static readonly System.Text.RegularExpressions.Regex DayRe = new System.Text.RegularExpressions.Regex(@"\A([0-9]{4})-([0-9]{2})-([0-9]{2})\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>new Date(`${day}T12:00:00`): months 01-12, days 01-31 (a day past the month's end runs into the next, as V8 does).</summary>
        static bool Day(string day, out DateTime d)
        {
            d = DateTime.MinValue;
            System.Text.RegularExpressions.Match m = DayRe.Match(day ?? "");
            if (!m.Success) return false;
            int y = int.Parse(m.Groups[1].Value, Inv), mo = int.Parse(m.Groups[2].Value, Inv), dd = int.Parse(m.Groups[3].Value, Inv);
            if (y < 1 || mo < 1 || mo > 12 || dd < 1 || dd > 31) return false;
            d = new DateTime(y, mo, 1).AddDays(dd - 1);
            return true;
        }

        /// <summary>dayTitle("2026-10-31"): "Saturday, October 31"; not a day: "Invalid Date".</summary>
        public static string DayTitle(string day)
        {
            DateTime d;
            if (!Day(day, out d)) return "Invalid Date";
            return d.DayOfWeek.ToString() + ", " + MonLong[d.Month - 1] + " " + d.Day.ToString(Inv);
        }

        /// <summary>dayShort("2026-10-31"): "Sat, Oct 31".</summary>
        public static string DayShort(string day)
        {
            DateTime d;
            if (!Day(day, out d)) return "Invalid Date";
            return DayShortNames[(int)d.DayOfWeek] + ", " + MonShort[d.Month - 1] + " " + d.Day.ToString(Inv);
        }

        /// <summary>hhmm("2026-10-25 08:10:02"): "08:10".</summary>
        public static string Hhmm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= 11 ? "" : s.Substring(11, Math.Min(5, s.Length - 11));
        }

        /// <summary>niceTime("2026-10-25 08:10:02"): "8:10 AM" (the wall clock, no seconds).</summary>
        public static string NiceTime(string s, bool h24)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Wall(s.Length > 16 ? s.Substring(0, 16) : s, h24, false);
        }

        /// <summary>humanize("low_space"): "Low space".</summary>
        public static string Humanize(string s) { return Format.Humanize(s); }

        /// <summary>toldHow(u): the server's own words on how to update as one sentence (a full stop added), or "".</summary>
        public static string ToldHow(JsonValue u)
        {
            JsonValue how = u != null && u.IsObject ? u["how"] : null;
            string t = how != null && how.IsString ? how.Str.Trim() : "";
            if (t.Length == 0) return "";
            char last = t[t.Length - 1];
            return last == '.' || last == '!' || last == '?' ? t : t + ".";
        }

        // ------------------------------------------------------------ categories and codecs

        /// <summary>CATEGORY: { word, many, tone } by footage category; null for another.</summary>
        public static string[] Category(string c)
        {
            switch (c)
            {
                case "Recent": return new[] { "Drive", "Drives", "teal" };
                case "Sentry": return new[] { "Sentry event", "Sentry events", "amber" };
                case "Saved": return new[] { "Saved clip", "Saved clips", "violet" };
                case "Other": return new[] { "Other", "Other", "grey" };
                default: return null;
            }
        }

        public static readonly string[] Categories = { "Recent", "Sentry", "Saved", "Other" };

        /// <summary>CODEC[c]: "H.264", "H.265", "AV1"; null for another.</summary>
        public static string Codec(string c)
        {
            switch (c)
            {
                case "h264": return "H.264";
                case "h265": return "H.265";
                case "av1": return "AV1";
                default: return null;
            }
        }

        // ------------------------------------------------------------ dialogs

        /// <summary>
        /// confirmDialog() as test/nativePackHarness.js reads core.js modal():
        /// the title, the close button ([btn icon-btn dlg-x]), the body's text
        /// runs, then [btn cancel] Cancel [btn ok btn-primary|btn-danger] OK.
        /// </summary>
        public static System.Collections.Generic.List<string> AskOutline(HzAsk a)
        {
            System.Collections.Generic.List<string> o = new System.Collections.Generic.List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            t(a.Title);
            o.Add("[btn icon-btn dlg-x]");
            foreach (HzPara p in a.Body) foreach (RichRun r in p.Text.Runs) t(r.Text);
            o.Add("[btn cancel]");
            t(a.No);
            o.Add("[btn ok " + (a.Danger ? "btn-danger" : "btn-primary") + "]");
            t(a.Yes);
            return o;
        }

        // ------------------------------------------------------------ state.js

        /// <summary>shareHealth(v): the footage source's health in plain words.</summary>
        public static DlHealth ShareHealth(JsonValue v)
        {
            JsonValue s = v != null && v.IsObject ? v["share"] : null;
            DlHealth h = new DlHealth();
            if (s == null || !s.Bool) { h.Tone = "grey"; h.Word = "Not connected yet"; return h; }
            if (!s["healthy"].Bool)
            {
                h.Tone = "bad"; h.Word = "Can’t reach the footage";
                h.Detail = s["lastError"].Bool ? Hz.Js(s["lastError"]) : s["hint"].Bool ? Hz.Js(s["hint"]) : "";
                return h;
            }
            if (s["writable"].IsBool && !s["writable"].Bool)
            {
                h.Tone = "warn"; h.Word = "Read-only: we can look but not tidy up"; h.Ok = true;
                h.Detail = Hz.Or(s["lastError"]);
                return h;
            }
            h.Tone = "good"; h.Word = "Connected"; h.Ok = true;
            return h;
        }
    }
}
