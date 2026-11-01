using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentrySix.Core
{
    /// <summary>
    /// The Viewer's controls bar as Studio drives it (index.html's .controls,
    /// script.js, ui/timeDisplay.js, features/skipSeconds.js and the skip
    /// length in ui/settingsModal.js): the time display, the speed list, the
    /// skip buttons' length and words, and the timeline's value while
    /// playing, scrubbing and seeking. The player's own clock and segment
    /// arithmetic are <see cref="SegmentPlan"/>'s.
    /// </summary>
    public static class ViewerControlsText
    {
        /// <summary>index.html's titles before the settings are read (ui.playback.skipBack / skipForward / playbackSpeed / settings).</summary>
        public const string SkipBackTitle = "Skip back 15 seconds";
        public const string SkipForwardTitle = "Skip forward 15 seconds";
        public const string SpeedTitle = "Playback speed";
        public const string SettingsTitle = "Settings";
        /// <summary>ui.settings.skipBackward / skipForward / seconds (updateSkipLabels' titles).</summary>
        public const string SkipBackward = "Skip Backward";
        public const string SkipForward = "Skip Forward";
        public const string Seconds = "seconds";
        /// <summary>The skip length with no setting (window._skipDuration || 15).</summary>
        public const double DefaultSkip = 15;
    }

    /// <summary>ui/timeDisplay.js.</summary>
    public static class TimeDisplay
    {
        /// <summary>formatTimeHMS(seconds): "mm:ss", or "h:mm:ss" from an hour.</summary>
        public static string FormatHMS(double seconds)
        {
            double h = Math.Floor(seconds / 3600);
            double m = Math.Floor((seconds % 3600) / 60);
            double s = Math.Floor(seconds % 60);
            if (h > 0) return Format.JsNum(h) + ":" + Pad2(m) + ":" + Pad2(s);
            return Pad2(m) + ":" + Pad2(s);
        }

        static string Pad2(double n)
        {
            string t = Format.JsNum(n);
            return t.Length >= 2 ? t : "0" + t;
        }

        /// <summary>
        /// The collection's #currentTime and #totalTime (onMasterTimeUpdate and
        /// the seek: updateTimeDisplayNew(Math.floor(position), Math.floor(total))).
        /// </summary>
        public static void Collection(double positionSec, double totalSec, out string current, out string total)
        {
            current = FormatHMS(Math.Floor(positionSec));
            total = FormatHMS(Math.Floor(totalSec));
        }
    }

    /// <summary>The speed list (#speedSelect, SPEED_OPTIONS, setPlaybackSpeed, bumpPlaybackSpeed and its restore from localStorage 'playbackRate').</summary>
    public static class PlaybackSpeeds
    {
        /// <summary>SPEED_OPTIONS (the select's option values).</summary>
        public static readonly double[] Options = { 0.5, 1, 2, 3, 4 };
        /// <summary>localStorage key (this PC's prefs natively).</summary>
        public const string StorageKey = "playbackRate";

        /// <summary>An option's words ("0.5x").</summary>
        public static string Label(double option)
        {
            return Format.JsNum(option) + "x";
        }

        /// <summary>String(rate): the select's value and what is stored.</summary>
        public static string Value(double rate)
        {
            return Format.JsNum(rate);
        }

        /// <summary>The option shown for a select value (-1: none, the select blank), as setting select.value does.</summary>
        public static int SelectIndex(string value)
        {
            for (int i = 0; i < Options.Length; i++) if (Value(Options[i]) == value) return i;
            return -1;
        }

        /// <summary>
        /// The page's start: a stored rate (truthy) is parseFloat(saved) || 1,
        /// else the rate stays (state.ui.playbackRate, 1).
        /// </summary>
        public static double Restore(string saved, double current)
        {
            if (string.IsNullOrEmpty(saved)) return current;
            double r = JsParseFloat(saved);
            return double.IsNaN(r) || r == 0 ? 1 : r;
        }

        /// <summary>
        /// bumpPlaybackSpeed(dir): the next option up (+1) or down (-1) from the
        /// rate (state.ui.playbackRate || 1; a rate that is no option snaps to the
        /// nearest, the first of two as near), clamped at the ends. Returns the
        /// new rate, or NaN when it stays (setPlaybackSpeed is not called).
        /// </summary>
        public static double Bump(double rate, int dir)
        {
            double cur = double.IsNaN(rate) || rate == 0 ? 1 : rate;
            int i = Array.IndexOf(Options, cur);
            if (i == -1)
            {
                int best = 0;
                for (int idx = 0; idx < Options.Length; idx++)
                    if (Math.Abs(Options[idx] - cur) < Math.Abs(Options[best] - cur)) best = idx;
                i = best;
            }
            int next = Math.Max(0, Math.Min(Options.Length - 1, i + dir));
            return Options[next] != cur ? Options[next] : double.NaN;
        }

        /// <summary>setPlaybackSpeed(rate) / the select's change: parseFloat(rate) || 1.</summary>
        public static double Apply(string value)
        {
            double r = JsParseFloat(value);
            return double.IsNaN(r) || r == 0 ? 1 : r;
        }

        /// <summary>JavaScript's white space and line ends (StrWhiteSpaceChar).</summary>
        static readonly char[] JsSpace = new int[] { 0x20, 0x09, 0x0a, 0x0d, 0x0b, 0x0c, 0xa0, 0xfeff, 0x2028, 0x2029, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a, 0x202f, 0x205f, 0x3000 }.Select(c => (char)c).ToArray();

        /// <summary>
        /// parseFloat(text) as JavaScript reads it: leading white space skipped,
        /// the longest decimal (or Infinity) at the start, else NaN.
        /// </summary>
        public static double JsParseFloat(string text)
        {
            if (text == null) return double.NaN;
            string s = text.TrimStart(JsSpace);
            Match inf = Regex.Match(s, @"^([+-]?)Infinity", RegexOptions.CultureInvariant);
            if (inf.Success) return inf.Groups[1].Value == "-" ? double.NegativeInfinity : double.PositiveInfinity;
            Match m = Regex.Match(s, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?", RegexOptions.CultureInvariant);
            if (!m.Success) return double.NaN;
            double r;
            return double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : double.NaN;
        }
    }

    /// <summary>
    /// The skip length (Settings > Skip Duration: getSetting('skipDuration') ||
    /// 15 in window._skipDuration) and what the skip buttons show for it
    /// (updateSkipLabels), and skipSeconds' target in a collection.
    /// </summary>
    public static class SkipLength
    {
        /// <summary>savedValue || 15 (the setting as JSON; undefined, null, 0, "" and false are 15).</summary>
        public static JsonValue Of(JsonValue saved)
        {
            return JsJson.Truthy(saved) ? saved : JsonValue.Number(ViewerControlsText.DefaultSkip);
        }

        /// <summary>The .skip-label's words (the value as text).</summary>
        public static string Label(JsonValue duration)
        {
            return JsText(duration);
        }

        /// <summary>The buttons' titles: "Skip Backward 15 seconds" / "Skip Forward 15 seconds".</summary>
        public static string BackTitle(JsonValue duration)
        {
            return ViewerControlsText.SkipBackward + " " + JsText(duration) + " " + ViewerControlsText.Seconds;
        }

        public static string ForwardTitle(JsonValue duration)
        {
            return ViewerControlsText.SkipForward + " " + JsText(duration) + " " + ViewerControlsText.Seconds;
        }

        static string JsText(JsonValue v)
        {
            if (v == null || v.IsNull) return "null";
            if (v.IsString) return v.Str;
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.Kind == JsonKind.Bool) return v.Bool ? "true" : "false";
            return v.ToJson();
        }

        /// <summary>
        /// skipSeconds(+-(window._skipDuration || 15)) in a day collection: the
        /// collection time currentSec + delta within 0 .. (cumulativeStarts' last
        /// || 1). Forward adds the value as JavaScript's + does (a text length
        /// joins the number's digits), back subtracts its number.
        /// </summary>
        public static double Target(double currentSec, double totalSec, JsonValue duration, bool forward)
        {
            JsonValue d = JsJson.Truthy(duration) ? duration : JsonValue.Number(ViewerControlsText.DefaultSkip);
            double total = totalSec != 0 && !double.IsNaN(totalSec) ? totalSec : 1;
            double next;
            if (forward)
                next = d.IsString ? Settings.JsNumber(Format.JsNum(currentSec) + d.Str) : currentSec + Number(d);
            else
                next = currentSec - Number(d);
            // Math.max(0, Math.min(total, NaN)) is NaN
            if (double.IsNaN(next)) return double.NaN;
            return Math.Max(0, Math.Min(total, next));
        }

        static double Number(JsonValue v)
        {
            if (v.IsNumber) return v.Num;
            if (v.IsString) return Settings.JsNumber(v.Str);
            if (v.Kind == JsonKind.Bool) return v.Bool ? 1 : 0;
            return double.NaN;
        }
    }

    /// <summary>
    /// #progressBar (0..100, the percent of the whole collection) and the time
    /// display as script.js keeps them for a day collection: each tick
    /// (onMasterTimeUpdate) both follow the position unless the bar is held
    /// (isScrubbing) or a seek runs; a seek (seekNativeDayCollectionBySec)
    /// sets both to its clamped target; pointerdown remembers whether it
    /// played, input and change seek to the bar's value, pointerup plays
    /// again when it played and nothing is held or seeking
    /// (maybeAutoplayAfterSeek).
    /// </summary>
    public sealed class ViewerTimeline
    {
        public ViewerTimeline()
        {
            CurrentText = "00:00:00";
            TotalText = "00:00:00";
        }

        /// <summary>What script.js last set #progressBar.value to (0..100; the range itself keeps <see cref="RangeValue"/> of it).</summary>
        public double Value { get; private set; }

        /// <summary>The value the range input holds and draws its thumb at.</summary>
        public double Shown { get { return RangeValue(Value); } }

        /// <summary>#currentTime / #totalTime (index.html starts them at 00:00:00).</summary>
        public string CurrentText { get; private set; }
        public string TotalText { get; private set; }

        /// <summary>state.ui.isScrubbing.</summary>
        public bool Scrubbing { get; private set; }

        /// <summary>state.ui.wasPlayingBeforeScrub.</summary>
        public bool WasPlayingBeforeScrub { get; private set; }

        /// <summary>A seek is running (nativeVideo.isSeeking; the player's own seek natively).</summary>
        public bool Seeking { get; set; }

        /// <summary>A collection time for a bar value: (pct / 100) x (total || 1).</summary>
        public static double TargetOf(double pct, double totalSec)
        {
            double total = totalSec != 0 && !double.IsNaN(totalSec) ? totalSec : 1;
            return pct / 100 * total;
        }

        /// <summary>The bar's value for a time: Math.min(100, position / total x 100).</summary>
        public static double PercentOf(double positionSec, double totalSec)
        {
            double total = totalSec != 0 && !double.IsNaN(totalSec) ? totalSec : 1;
            return Math.Min(100, positionSec / total * 100);
        }

        /// <summary>onMasterTimeUpdate for a collection: the time display always, the bar unless held or seeking.</summary>
        public void Tick(double positionSec, double totalSec)
        {
            string c, t;
            TimeDisplay.Collection(positionSec, totalSec, out c, out t);
            CurrentText = c;
            TotalText = t;
            if (!Scrubbing && !Seeking) Value = PercentOf(positionSec, totalSec);
        }

        /// <summary>A seek landed (seekNativeDayCollectionBySec): the clamped target shown at once.</summary>
        public void Seeked(double targetSec, double totalSec)
        {
            double clamped = Math.Max(0, Math.Min(totalSec, targetSec));
            Value = PercentOf(clamped, totalSec);
            string c, t;
            TimeDisplay.Collection(clamped, totalSec, out c, out t);
            CurrentText = c;
            TotalText = t;
        }

        /// <summary>The collection opened (selectDayCollection): its start and total ("00:00 / 00:04"), the bar at the start.</summary>
        public void Opened(double startSec, double totalSec)
        {
            string c, t;
            TimeDisplay.Collection(startSec, totalSec, out c, out t);
            CurrentText = c;
            TotalText = t;
            Value = PercentOf(startSec, totalSec);
        }

        /// <summary>pointerdown on the bar.</summary>
        public void PointerDown(bool playing)
        {
            Scrubbing = true;
            WasPlayingBeforeScrub = playing;
        }

        /// <summary>input / change: the bar at that value; the time to seek to.</summary>
        public double Input(double pct, double totalSec)
        {
            Value = Math.Max(0, Math.Min(100, pct));
            return TargetOf(Value, totalSec);
        }

        /// <summary>change (the release): no longer held; the time to seek to.</summary>
        public double Change(double pct, double totalSec)
        {
            Scrubbing = false;
            return Input(pct, totalSec);
        }

        /// <summary>pointerup: true when playback starts again (it played, and nothing is held or seeking).</summary>
        public bool PointerUp()
        {
            Scrubbing = false;
            return WasPlayingBeforeScrub && !Seeking;
        }

        /// <summary>pointercancel.</summary>
        public void PointerCancel()
        {
            Scrubbing = false;
        }

        /// <summary>
        /// A value as a range input (0..100, step 1) keeps it: clamped, then the
        /// nearest step (a half up); not a number: the middle.
        /// </summary>
        public static double RangeValue(double pct)
        {
            if (double.IsNaN(pct) || double.IsInfinity(pct)) return 50;
            return Math.Max(0, Math.Min(100, Math.Floor(Math.Max(0, Math.Min(100, pct)) + 0.5)));
        }
    }
}
