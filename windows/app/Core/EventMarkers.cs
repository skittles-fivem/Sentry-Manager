using System;
using System.Collections.Generic;

namespace SentrySix.Core
{
    /// <summary>
    /// One .event-timeline-marker on #timelineMarkers (eventMarkers.js
    /// updateEventTimelineMarker): a SentryClips or SavedClips collection's
    /// event, where event.json's timestamp falls on the timeline.
    /// </summary>
    public sealed class EventMarker
    {
        /// <summary>"sentry" or "saved" (the marker's class).</summary>
        public string Kind;
        /// <summary>style.left in percent (0..100).</summary>
        public double Pct;
        /// <summary>The marker's title (its tooltip).</summary>
        public string Title;

        public bool IsSentry { get { return Kind == "sentry"; } }
    }

    /// <summary>
    /// Studio's event timeline marker (src/renderer/scripts/ui/eventMarkers.js
    /// updateEventTimelineMarker and its click): which collections have one,
    /// where it sits, its words, and where a click seeks to.
    /// </summary>
    public static class EventMarkers
    {
        /// <summary>The Sentry marker's warning triangle (24 x 24, drawn at 10 px in white).</summary>
        public const string SentrySvg = "M12 2L1 21h22L12 2zm0 3.5L19.5 19h-15L12 5.5zM11 10v4h2v-4h-2zm0 6v2h2v-2h-2z";
        /// <summary>The Saved marker's Material Symbols Outlined 'bookmark' (viewBox 0 -960 960 960).</summary>
        public const string Bookmark = "M200-120v-640q0-33 23.5-56.5T280-840h400q33 0 56.5 23.5T760-760v640L480-240 200-120Zm80-122 200-86 200 86v-518H280v518Zm0-518h400-400Z";

        /// <summary>The collection's event.json: the first group's that has one, else eventMetaByKey's "tag/eventId" (null: none).</summary>
        public static JsonValue Meta(DayCollection c, Func<string, JsonValue> byKey)
        {
            if (c == null) return null;
            JsonValue meta = null;
            if (c.Groups != null)
                foreach (ClipGroup g in c.Groups) if (JsJson.Truthy(g.EventMeta)) { meta = g.EventMeta; break; }
            if (meta == null && !string.IsNullOrEmpty(c.Tag) && !string.IsNullOrEmpty(c.EventId) && byKey != null)
                meta = byKey(c.Tag + "/" + c.EventId);
            return meta;
        }

        /// <summary>
        /// updateEventTimelineMarker: the marker for the active collection, or
        /// null when it has none (no event.json time, not a Sentry or Saved
        /// collection, a time Date.parse cannot read). The position runs from
        /// the first segment's start to the last one's start plus 60 s.
        /// </summary>
        /// <param name="sentryWord">t('ui.clipBrowser.sentry').</param>
        /// <param name="savedWord">t('ui.clipBrowser.saved').</param>
        public static EventMarker Of(DayCollection c, Func<string, JsonValue> byKey, TimeZoneInfo zone, string sentryWord, string savedWord)
        {
            if (c == null) return null;
            List<ClipGroup> groups = c.Groups ?? new List<ClipGroup>();
            JsonValue meta = Meta(c, byKey);
            JsonValue ts = JsJson.Get(meta, "timestamp");
            if (!JsJson.Truthy(ts)) return null;
            string tag = JsText.Lower(c.Tag ?? "");
            if (tag != "sentryclips" && tag != "savedclips") return null;
            bool sentry = tag == "sentryclips";
            string stamp = StudioFrames.JsString(ts);
            double ev = JsDate.Parse(stamp, zone);
            if (double.IsNaN(ev) || double.IsInfinity(ev)) return null;
            double start = LibraryScan.KeyToEpochMs(groups.Count > 0 ? groups[0].TimestampKey : null, zone) ?? 0;
            double lastStart = LibraryScan.KeyToEpochMs(groups.Count > 0 ? groups[groups.Count - 1].TimestampKey : null, zone) ?? start;
            double end = lastStart + 60000;
            double duration = Math.Max(1, end - start);
            double pct = Math.Max(0, Math.Min(100, ((ev - start) / duration) * 100));
            JsonValue reason = JsJson.Get(meta, "reason");
            string why = JsJson.Truthy(reason) ? StudioFrames.JsString(reason) : sentry ? "Unknown" : "User saved";
            EventMarker m = new EventMarker();
            m.Kind = sentry ? "sentry" : "saved";
            m.Pct = pct;
            m.Title = (sentry ? sentryWord : savedWord) + " Event: " + why + "\n" + stamp;
            return m;
        }

        /// <summary>Of(...) with the English words (ClipBrowser.Sentry and Saved).</summary>
        public static EventMarker Of(DayCollection c, Func<string, JsonValue> byKey, TimeZoneInfo zone)
        {
            return Of(c, byKey, zone, ClipBrowser.Sentry, ClipBrowser.Saved);
        }

        /// <summary>
        /// The marker's click: pct of the collection's length
        /// (nativeVideo.cumulativeStarts' last, or 60 s when that is 0 or
        /// not there).
        /// </summary>
        public static double SeekSeconds(double pct, double totalSec)
        {
            double total = double.IsNaN(totalSec) || totalSec == 0 ? 60 : totalSec;
            return (pct / 100) * total;
        }
    }
}
