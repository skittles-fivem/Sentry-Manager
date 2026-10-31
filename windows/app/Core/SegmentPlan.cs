using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentrySix.Core
{
    /// <summary>One segment of a collection (a minute of a Tesla drive): each camera's clip on the server.</summary>
    public sealed class PlaybackSegment
    {
        /// <summary>camera id (front, back, left_repeater ...) to the clip's server path.</summary>
        public readonly Dictionary<string, string> FilesByCamera = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The group's timestampKey (YYYY-MM-DD_HH-MM-SS), for the recording time and event markers.</summary>
        public string TimestampKey;

        /// <summary>The clip probed for the duration: the front camera's, else the first (probeSegmentDurations).</summary>
        public string ProbeFile
        {
            get
            {
                string f;
                if (FilesByCamera.TryGetValue("front", out f)) return f;
                foreach (KeyValuePair<string, string> kv in FilesByCamera) return kv.Value;
                return null;
            }
        }
    }

    /// <summary>A slot of the multi-camera grid and the camera it shows (the profile's layoutSlots).</summary>
    public sealed class LayoutSlot
    {
        public string Slot;
        public string Camera;
        public LayoutSlot(string slot, string camera) { Slot = slot; Camera = camera; }
    }

    /// <summary>What one shown place plays in a segment (segmentPreload.js planSegment).</summary>
    public sealed class SegmentTarget
    {
        public string Slot;
        public string Camera;
        /// <summary>The clip's server path, or null when this camera has none in the segment.</summary>
        public string File;
    }

    public sealed class SegmentView
    {
        public bool Multi;
        public List<SegmentTarget> Targets = new List<SegmentTarget>();
        public string MasterSlot;
    }

    /// <summary>
    /// A day collection's timeline as script.js keeps it (nativeVideo
    /// segmentDurations and cumulativeStarts): the segments back to back,
    /// profile estimates first, probed durations later, each segment's real
    /// duration once it opens (onMasterLoaded), and the seek, skip, event
    /// marker and preload arithmetic on it.
    /// </summary>
    public sealed class SegmentPlan
    {
        /// <summary>The Tesla profile's defaultSegmentDurationSeconds (dashcamProfiles.mjs).</summary>
        public const double TeslaSegmentSeconds = 60;
        /// <summary>GM Surround Vision's.</summary>
        public const double GmSegmentSeconds = 300;
        /// <summary>The event start offset: 15 s before the event (selectDayCollection).</summary>
        public const double EventLeadSeconds = 15;

        readonly double fallback;
        readonly double[] durations;
        readonly double[] starts;

        /// <param name="count">How many segments.</param>
        /// <param name="fallbackSeconds">The profile's defaultSegmentDurationSeconds.</param>
        public SegmentPlan(int count, double fallbackSeconds)
        {
            fallback = fallbackSeconds;
            durations = new double[Math.Max(0, count)];
            for (int i = 0; i < durations.Length; i++) durations[i] = fallbackSeconds;
            starts = new double[durations.Length + 1];
            Recompute(0);
        }

        public static double FallbackFor(string profileId)
        {
            return profileId == "gm_surroundvision" ? GmSegmentSeconds : TeslaSegmentSeconds;
        }

        public int Count { get { return durations.Length; } }
        public double Fallback { get { return fallback; } }
        public double Duration(int i) { return durations[i]; }
        /// <summary>cumulativeStarts[i]; [Count] is the total.</summary>
        public double Start(int i) { return starts[i]; }
        public IList<double> CumulativeStarts { get { return starts; } }

        /// <summary>cumulativeStarts[last] || 1 (the web divides by it).</summary>
        public double Total { get { double t = starts[starts.Length - 1]; return t != 0 && !double.IsNaN(t) ? t : 1; } }

        void Recompute(int from)
        {
            double cum = starts[from];
            for (int i = from; i < durations.Length; i++)
            {
                starts[i] = cum;
                cum += durations[i];
            }
            starts[durations.Length] = cum;
        }

        /// <summary>The probe's results (resolveSegmentDurations: a failed or non-positive one is the fallback).</summary>
        public void SetProbed(IList<double> probed)
        {
            for (int i = 0; i < durations.Length && i < probed.Count; i++)
                durations[i] = IsPositive(probed[i]) ? probed[i] : fallback;
            starts[0] = 0;
            Recompute(0);
        }

        /// <summary>onMasterLoaded: the opened segment's real duration (a bad one keeps the estimate); the starts after it move.</summary>
        public void SetActual(int segIdx, double duration)
        {
            if (segIdx < 0 || segIdx >= durations.Length) return;
            double d = IsPositive(duration) ? duration : durations[segIdx];
            if (d == durations[segIdx]) return;
            durations[segIdx] = d;
            Recompute(segIdx);
        }

        static bool IsPositive(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d) && d > 0;
        }

        /// <summary>
        /// seekNativeDayCollectionBySec: the segment holding this collection
        /// time and the time into it (clamped to the collection; the end is in
        /// the last segment; a local time past the segment's duration is
        /// clamped to it).
        /// </summary>
        public void Locate(double targetSec, out int segIdx, out double localSec)
        {
            segIdx = 0;
            localSec = 0;
            if (durations.Length == 0) return;
            double total = starts[starts.Length - 1];
            double clamped = Math.Max(0, Math.Min(total, targetSec));
            for (int i = 0; i < starts.Length - 1; i++)
            {
                if (clamped >= starts[i] && clamped < starts[i + 1]) { segIdx = i; break; }
                if (i == starts.Length - 2) segIdx = i;
            }
            localSec = Math.Min(clamped - starts[segIdx], durations[segIdx]);
        }

        /// <summary>The collection time of a time into a segment.</summary>
        public double ToCollection(int segIdx, double localSec)
        {
            return starts[Math.Max(0, Math.Min(durations.Length, segIdx))] + localSec;
        }

        /// <summary>skipSeconds: the collection time delta seconds on, within 0 .. total.</summary>
        public double Skip(double currentSec, double delta)
        {
            double total = Total;
            return Math.Max(0, Math.Min(total, currentSec + delta));
        }

        /// <summary>The progress bar's value (0-100) for a collection time.</summary>
        public double Percent(double sec)
        {
            return Math.Min(100, sec / Total * 100);
        }

        /// <summary>A progress bar value (0-100) as a collection time.</summary>
        public double FromPercent(double pct)
        {
            return pct / 100 * Total;
        }

        /// <summary>
        /// selectDayCollection's first position: 15 s before the event
        /// (event.json's timestamp against the first group's timestampKey),
        /// else 0; then the segment and offset by the profile's estimate, as
        /// the web picks them before any duration is known.
        /// </summary>
        public static double StartOffset(string eventTimestamp, string firstTimestampKey)
        {
            double anchor = 0;
            double ev, start;
            if (TryParseIso(eventTimestamp, out ev) && TryParseKey(firstTimestampKey, out start) && start > 0)
                anchor = Math.Max(0, ev - start);
            return Math.Max(0, anchor - EventLeadSeconds * 1000) / 1000;
        }

        /// <summary>The segment and offset the web first loads for a start offset (by the fallback duration).</summary>
        public void FirstLoad(double startOffsetSec, out int segIdx, out double localSec)
        {
            if (startOffsetSec <= 0 || durations.Length == 0) { segIdx = 0; localSec = 0; return; }
            segIdx = (int)Math.Min(Math.Floor(startOffsetSec / fallback), durations.Length - 1);
            localSec = startOffsetSec - segIdx * fallback;
        }

        /// <summary>
        /// eventMarkers.js updateEventTimelineMarker: where the Sentry or
        /// Saved event's marker sits (0-100), or NaN when there is none (no
        /// event time, not SentryClips / SavedClips). The span is the first
        /// group's start to the last group's start + 60 s, by the clock.
        /// </summary>
        public static double EventMarkerPercent(string tag, string eventTimestamp, string firstKey, string lastKey)
        {
            string t = (tag ?? "").ToLowerInvariant();
            if (t != "sentryclips" && t != "savedclips") return double.NaN;
            double ev;
            if (!TryParseIso(eventTimestamp, out ev)) return double.NaN;
            double start, last;
            if (!TryParseKey(firstKey, out start)) start = 0;
            if (!TryParseKey(lastKey, out last)) last = start;
            double duration = Math.Max(1, last + 60000 - start);
            return Math.Max(0, Math.Min(100, (ev - start) / duration * 100));
        }

        /// <summary>The marker's seek target: its percent of the collection's length (|| 60).</summary>
        public double EventMarkerTarget(double pct)
        {
            double t = starts[starts.Length - 1];
            if (t == 0 || double.IsNaN(t)) t = 60;
            return pct / 100 * t;
        }

        /// <summary>
        /// A timestampKey (2026-10-01_10-05-00) as local time in ms since
        /// 1970 (parseTimestampKeyToEpochMs reads it with new Date(y, m, d,
        /// h, mi, s): the PC's time zone).
        /// </summary>
        public static bool TryParseKey(string key, out double epochMs)
        {
            epochMs = 0;
            if (string.IsNullOrEmpty(key)) return false;
            DateTime d;
            if (!DateTime.TryParseExact(key.Length >= 19 ? key.Substring(0, 19) : key, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out d)) return false;
            epochMs = (d.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return true;
        }

        /// <summary>Date.parse of event.json's timestamp (ISO 8601; without a zone it is local time, as Date.parse reads a date-time).</summary>
        public static bool TryParseIso(string s, out double epochMs)
        {
            epochMs = 0;
            if (string.IsNullOrEmpty(s)) return false;
            DateTimeOffset o;
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out o)) return false;
            epochMs = (o.UtcDateTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return true;
        }

        // ------------------------------------------------------------ preload (segmentPreload.js)

        /// <summary>segmentPreload.js PRELOAD_LEAD_SECONDS (the web's; Chrome drops a paused player's decoders after 15 s).</summary>
        public const double WebPreloadLeadSeconds = 12;
        /// <summary>The app's lead: its preloaded decoders wait blocked on a full ring and cost nothing, but are six more processes.</summary>
        public const double PreloadLeadSeconds = 3;
        /// <summary>PRELOAD_SETTLE_SECONDS.</summary>
        public const double PreloadSettleSeconds = 3;

        /// <summary>segmentPreload.js shouldStartPreload, with the same arguments.</summary>
        public static bool ShouldStartPreload(double currentTime, double duration, double playbackRate, double leadSeconds, double settleSeconds)
        {
            double total = duration;
            if (double.IsNaN(total) || double.IsInfinity(total) || total <= 0) return false;
            double rate = playbackRate > 0 ? playbackRate : 1;
            double position = Math.Max(0, double.IsNaN(currentTime) ? 0 : currentTime);
            if (position < Math.Min(settleSeconds * rate, total / 2)) return false;
            double remaining = Math.Max(0, total - position);
            return remaining / rate <= leadSeconds;
        }

        /// <summary>
        /// segmentPreload.js planSegment: in the grid one target per slot (the
        /// camera's clip or none), the master the master camera's slot, or the
        /// first slot with a clip when that camera has none; else the selected
        /// camera (or the segment's first clip) in "main".
        /// </summary>
        public static SegmentView PlanSegment(PlaybackSegment group, bool multiEnabled, IList<LayoutSlot> slots, string masterCamera, string selectedCamera)
        {
            if (group == null) return null;
            Dictionary<string, string> files = group.FilesByCamera;
            SegmentView v = new SegmentView();
            masterCamera = masterCamera ?? "front";
            selectedCamera = selectedCamera ?? "front";
            if (multiEnabled)
            {
                v.Multi = true;
                slots = slots ?? new List<LayoutSlot>();
                foreach (LayoutSlot s in slots)
                {
                    string f = null;
                    if (!string.IsNullOrEmpty(s.Camera)) files.TryGetValue(s.Camera, out f);
                    v.Targets.Add(new SegmentTarget { Slot = s.Slot, Camera = s.Camera, File = f });
                }
                LayoutSlot master = slots.FirstOrDefault(s => s.Camera == masterCamera);
                if (master == null || master.Camera == null || !files.ContainsKey(master.Camera))
                    master = slots.FirstOrDefault(s => s.Camera != null && files.ContainsKey(s.Camera)) ?? master;
                v.MasterSlot = master == null ? null : master.Slot;
                return v;
            }
            string entry;
            if (!files.TryGetValue(selectedCamera, out entry))
            {
                entry = null;
                foreach (KeyValuePair<string, string> kv in files) { entry = kv.Value; break; }
            }
            v.Targets.Add(new SegmentTarget { Slot = "main", Camera = selectedCamera, File = entry });
            v.MasterSlot = "main";
            return v;
        }
    }
}
