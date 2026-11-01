using System;
using System.Globalization;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>
    /// The Flux pack's formatting (examples/ui-packs/flux/web/flux/core.js:
    /// bytes, rate, pct, num, plural, dur, clock, ago, tod, wall, wallDay,
    /// when, isoish, watts, energy, money, humanize, dayTitle), checked against
    /// the pack's own functions by test/windowsNativeFlux.test.js
    /// (Core.Tests/Fixtures/expected-flux-format.json). Where Flux's code is
    /// the same as Horizon's it is Horizon's (Core/Horizon/Hz.cs).
    /// </summary>
    public static partial class Fx
    {
        public const string Dash = "—";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] MonShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        static readonly string[] DayShort = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        /// <summary>fin(n): a finite number (not a numeric string).</summary>
        public static bool Fin(JsonValue v) { return Hz.Ok(v); }

        /// <summary>bytes(n, digits): "118 MB"; "—" for null or not a number.</summary>
        public static string Bytes(JsonValue n, int digits = 1)
        {
            if (n == null || n.IsNull) return Dash;
            return Hz.Size(Hz.Number(n), digits);
        }

        public static string Bytes(double n, int digits = 1) { return Hz.Size(n, digits); }

        /// <summary>rate(bps): "12 MB/s".</summary>
        public static string Rate(JsonValue bps) { return Hz.PerSec(bps); }

        /// <summary>pct(n, d): "42%".</summary>
        public static string Pct(JsonValue n, int d = 0) { return Hz.Pc(n, d); }

        /// <summary>num(n, d): toLocaleString with d decimals.</summary>
        public static string Num(JsonValue n, int d = 0) { return Hz.Fixed(n, d); }

        /// <summary>plural(n, w, ws): "1 job" only when n is the number 1 ("1" is "1 jobs").</summary>
        public static string Plural(JsonValue n, string w, string ws = null)
        {
            double x = n != null && n.Bool ? Hz.Number(n) : 0;
            bool one = n != null && n.IsNumber && n.Num == 1;
            return Format.Locale(x) + " " + (one ? w : (ws ?? w + "s"));
        }

        public static string Plural(double n, string w, string ws = null)
        {
            double x = Format.Fin(n) || double.IsInfinity(n) ? n : 0;
            return Format.Locale(x) + " " + (n == 1 ? w : (ws ?? w + "s"));
        }

        /// <summary>dur(ms): "42s", "12m 05s", "1h 02m", "3d 4h".</summary>
        public static string Dur(double ms) { return Hz.Span(ms); }

        /// <summary>clock(sec): "1:05.3" (tenths of a second).</summary>
        public static string Clock(double sec)
        {
            if (!Format.Fin(sec) || sec < 0) return "0:00";
            double m = Math.Floor(sec / 60);
            string s = Format.ToFixed(sec - m * 60, 1);
            while (s.Length < 4) s = "0" + s;
            return Format.JsNum(m) + ":" + s;
        }

        /// <summary>ago(ts): "never", "just now", "42s ago", "5m ago", "3h ago", "2d ago".</summary>
        public static string Ago(JsonValue ts, long now)
        {
            if (ts == null || !ts.Bool) return "never";
            double s = Format.Round((now - Hz.Number(ts)) / 1000);
            if (double.IsNaN(s)) return "NaNd ago";
            if (s < 5) return "just now";
            if (s < 60) return Format.JsNum(s) + "s ago";
            if (s < 3600) return Format.JsNum(Format.Round(s / 60)) + "m ago";
            if (s < 86400) return Format.JsNum(Format.Round(s / 3600)) + "h ago";
            return Format.JsNum(Format.Round(s / 86400)) + "d ago";
        }

        static DateTime Local(double ms)
        {
            DateTime utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, Format.Zone ?? TimeZoneInfo.Local);
        }

        static string Clock12(DateTime t, bool h24, bool secs)
        {
            string tail = ":" + t.ToString("mm", Inv) + (secs ? ":" + t.ToString("ss", Inv) : "");
            if (h24) return t.ToString("HH", Inv) + tail;
            int h = t.Hour % 12;
            if (h == 0) h = 12;
            return h.ToString(Inv) + tail + " " + (t.Hour < 12 ? "AM" : "PM");
        }

        /// <summary>tod(t, secs): "3:10 PM", "3:10:02 PM", "15:10".</summary>
        public static string Tod(double ms, bool h24, bool secs = false)
        {
            if (!Format.Fin(ms)) return "Invalid Date";
            return Clock12(Local(ms), h24, secs);
        }

        /// <summary>wall(s, secs): as Horizon's.</summary>
        public static string Wall(string s, bool h24, bool secs = true) { return Hz.Wall(s, h24, secs); }

        public static string WallDay(string s, bool h24) { return Hz.WallDay(s, h24); }

        /// <summary>when(ts): "Oct 01, 2026, 3:10:02 PM" (the day two digits); "—" without one.</summary>
        public static string When(JsonValue ts, bool h24)
        {
            if (ts == null || !ts.Bool) return Dash;
            double ms = Hz.Number(ts);
            if (!Format.Fin(ms)) return "Invalid Date";
            DateTime t = Local(ms);
            return MonShort[t.Month - 1] + " " + t.Day.ToString("00", Inv) + ", " + t.Year.ToString(Inv) + ", " + Clock12(t, h24, true);
        }

        /// <summary>isoish(ts): "2026-10-31 3:10:02 PM" (wallDay of the local time); "—" without one.</summary>
        public static string Isoish(JsonValue ts, bool h24)
        {
            if (ts == null || !ts.Bool) return Dash;
            double ms = Hz.Number(ts);
            if (!Format.Fin(ms)) return WallDay("NaN-aN-aN aN:aN:aN", h24);
            DateTime t = Local(ms);
            return WallDay(t.ToString("yyyy-MM-dd HH:mm:ss", Inv), h24);
        }

        public static string Watts(JsonValue w) { return Hz.Watt(w); }

        public static string Watts(double w) { return Hz.Watt(w); }

        public static string Energy(JsonValue wh) { return Hz.Wh(wh); }

        public static string Energy(double wh) { return Hz.Wh(wh); }

        public static string Money(JsonValue v, string cur) { return Hz.Cash(v, cur); }

        public static string Money(double v, string cur) { return Hz.Cash(v, cur); }

        public static string Humanize(string s) { return Hz.Words(s); }

        /// <summary>dayTitle("2026-10-31"): "Sat, Oct 31, 2026"; not a day: as it is.</summary>
        public static string DayTitle(string day)
        {
            DateTime d;
            if (day == null || !DateTime.TryParseExact(day, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d)) return day;
            return DayShort[(int)d.DayOfWeek] + ", " + MonShort[d.Month - 1] + " " + d.Day.ToString(Inv) + ", " + d.Year.ToString(Inv);
        }

        // ------------------------------------------------------------ cameras and categories

        public static readonly string[] Cameras = { "front", "back", "left_repeater", "right_repeater", "left_pillar", "right_pillar" };

        public static string CategoryColor(string c)
        {
            switch (c)
            {
                case "Recent": return "cyan";
                case "Sentry": return "amber";
                case "Saved": return "green";
                case "Other": return "muted";
                default: return null;
            }
        }
    }
}
