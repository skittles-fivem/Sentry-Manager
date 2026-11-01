using System;
using System.Globalization;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Aurora
{
    /// <summary>The three parts dayParts(day) gives: the weekday ("Sat"), the day ("31"), the month ("Oct").</summary>
    public sealed class AuDayParts
    {
        public string Wd = "", Dd = "", Mon = "";
    }

    /// <summary>
    /// The Aurora pack's formatting (examples/ui-packs/aurora/web/aurora/core.js:
    /// bytes, rate, pct, num, plural, dur, clock, ago, when, whenFull, wall,
    /// hourName, watts, energy, money, humanize, dayTitle, dayParts, timeOf),
    /// checked against the pack's own functions by
    /// test/windowsNativeAurora.test.js (Core.Tests/Fixtures/expected-aurora-format.json).
    /// Where Aurora's code is the same as Horizon's it is Horizon's (Core/Horizon/Hz.cs).
    /// </summary>
    public static partial class Au
    {
        public const string Dash = "—";
        /// <summary>A page's body while it loads (p.loading).</summary>
        public const string Loading = "Loading…";
        /// <summary>main.js render()'s catch: the heading, then the message and Try again.</summary>
        public const string LoadFailedTitle = "Something went wrong";
        public const string TryAgain = "Try again";
        public const string KickerDefault = "Sentry Six";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] MonShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        static readonly string[] DayShort = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        /// <summary>fin(n): a finite number (not a numeric string).</summary>
        public static bool Fin(JsonValue v) { return Hz.Ok(v); }

        /// <summary>bytes(n, digits): "118 MB"; "—" for null or not a number.</summary>
        public static string Bytes(JsonValue n, int digits = 1) { return Hz.Size(n, digits); }

        public static string Bytes(double n, int digits = 1) { return Hz.Size(n, digits); }

        /// <summary>rate(bps): "12 MB/s".</summary>
        public static string Rate(JsonValue bps) { return Hz.PerSec(bps); }

        /// <summary>pct(n, d): "42%".</summary>
        public static string Pct(JsonValue n, int d = 0) { return Hz.Pc(n, d); }

        /// <summary>num(n, d): toLocaleString with d decimals.</summary>
        public static string Num(JsonValue n, int d = 0) { return Hz.Fixed(n, d); }

        /// <summary>plural(n, w, ws): "1 job" when Number(n) is 1 ("1" too), "1,204 jobs".</summary>
        public static string Plural(JsonValue n, string w, string ws = null) { return Hz.Count(n, w, ws); }

        public static string Plural(double n, string w, string ws = null) { return Hz.Count(n, w, ws); }

        static string Pad2(double n)
        {
            string s = Format.JsNum(n);
            return s.Length < 2 ? "0" + s : s;
        }

        /// <summary>dur(ms): "42 s", "12 min 05 s", "1 h 02 min", "3 d 4 h"; "—" for not a number or negative.</summary>
        public static string Dur(double ms)
        {
            if (!Format.Fin(ms) || ms < 0) return Dash;
            double s = Format.Round(ms / 1000);
            if (s < 60) return Format.JsNum(s) + " s";
            if (s < 3600) return Format.JsNum(Math.Floor(s / 60)) + " min " + Pad2(s % 60) + " s";
            if (s < 86400) return Format.JsNum(Math.Floor(s / 3600)) + " h " + Pad2(Math.Floor((s % 3600) / 60)) + " min";
            return Format.JsNum(Math.Floor(s / 86400)) + " d " + Format.JsNum(Math.Floor((s % 86400) / 3600)) + " h";
        }

        /// <summary>clock(sec): "3:07" (as Horizon's mmss).</summary>
        public static string Clock(double sec) { return Hz.Mmss(sec); }

        /// <summary>ago(ts): "never", "just now" (under 10 s), "42 s ago", "5 min ago", "3 h ago", "2 d ago".</summary>
        public static string Ago(JsonValue ts, long now)
        {
            if (ts == null || !ts.Bool) return "never";
            double s = Format.Round((now - Hz.Number(ts)) / 1000);
            if (double.IsNaN(s)) return "NaN d ago";
            if (s < 10) return "just now";
            if (s < 60) return Format.JsNum(s) + " s ago";
            if (s < 3600) return Format.JsNum(Format.Round(s / 60)) + " min ago";
            if (s < 86400) return Format.JsNum(Format.Round(s / 3600)) + " h ago";
            return Format.JsNum(Format.Round(s / 86400)) + " d ago";
        }

        /// <summary>when(ts): "Oct 31, 3:10 PM" (or "Oct 31, 15:10"); "—" without one.</summary>
        public static string When(JsonValue ts, bool h24) { return Hz.Stamp(ts, h24); }

        public static string When(double ms, bool h24) { return Hz.Stamp(ms, h24); }

        /// <summary>whenFull(ts): "Oct 31, 2026, 3:10:02 PM" (or "…, 15:10:02"); "—" without one.</summary>
        public static string WhenFull(JsonValue ts, bool h24) { return Hz.StampFull(ts, h24); }

        public static string WhenFull(double ms, bool h24) { return Hz.StampFull(ms, h24); }

        /// <summary>wall(s, secs): the car's wall-clock text as "6:07:45 AM" or "06:07:45"; anything else as it is.</summary>
        public static string Wall(string s, bool h24, bool secs = true) { return Hz.Wall(s, h24, secs); }

        /// <summary>timeOf(stamp): wall(stamp, false), "" without one.</summary>
        public static string TimeOf(string stamp, bool h24) { return string.IsNullOrEmpty(stamp) ? "" : Hz.Wall(stamp, h24, false); }

        /// <summary>hourName(hr): "3 PM" or "15:00".</summary>
        public static string HourName(int hr, bool h24)
        {
            if (h24) return (hr < 10 && hr >= 0 ? "0" : "") + hr.ToString(Inv) + ":00";
            int h = hr % 12;
            return (h == 0 ? 12 : h).ToString(Inv) + " " + (hr < 12 ? "AM" : "PM");
        }

        public static string Watts(JsonValue w) { return Hz.Watt(w); }

        public static string Watts(double w) { return Hz.Watt(w); }

        public static string Energy(JsonValue wh) { return Hz.Wh(wh); }

        public static string Energy(double wh) { return Hz.Wh(wh); }

        /// <summary>money(v, cur = '$'): "$0.14", "154 kr", "$1,205".</summary>
        public static string Money(JsonValue v, string cur = "$") { return Hz.Cash(v, cur ?? "$"); }

        public static string Money(double v, string cur = "$") { return Hz.Cash(v, cur ?? "$"); }

        public static string Humanize(string s) { return Hz.Words(s); }

        /// <summary>dayTitle("2026-10-31"): "Saturday, October 31, 2026"; not a day: as it is.</summary>
        public static string DayTitle(string day) { return Hz.DayName(day, true); }

        /// <summary>dayParts("2026-10-31"): Sat, 31, Oct; not a day: the text as the day, the others "".</summary>
        public static AuDayParts DayParts(string day)
        {
            AuDayParts p = new AuDayParts();
            DateTime d;
            if (day == null || !DateTime.TryParseExact(day, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d))
            {
                p.Dd = day ?? "null";
                return p;
            }
            p.Wd = DayShort[(int)d.DayOfWeek];
            p.Dd = d.Day.ToString(Inv);
            p.Mon = MonShort[d.Month - 1];
            return p;
        }
    }
}
