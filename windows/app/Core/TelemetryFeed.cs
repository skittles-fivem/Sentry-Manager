using System;
using System.Collections.Generic;

namespace SentrySix.Core
{
    /// <summary>What one telemetry tick tells the dashboard (part 9) to do.</summary>
    public sealed class TelemetryStep
    {
        /// <summary>The tick ran (the master plays or was moved, telemetry is on, no segment switch under way).</summary>
        public bool Ran;
        /// <summary>updateVisualization(sei): the record shown now, or null when there is none near this time.</summary>
        public Sei Sei;
        /// <summary>setDashboardParked(true / false); null when it is not called (the dashboard is off and there is a record).</summary>
        public bool? Parked;
        /// <summary>resetDashboardElements(): more than 2 s since the last record, once until the next one.</summary>
        public bool Reset;
    }

    /// <summary>
    /// The web viewer's telemetry state for the playing segment (script.js's
    /// nativeVideo.seiData, mapPath, lastSeiTimeMs, dashboardReset and
    /// telemetryLoadToken): loadNativeSegment clears it and asks for the
    /// master camera's clip (telemetryEntryFor), the answer is kept only if
    /// it is still for that load, and every tick (telemetryAnimationLoop, or
    /// onMasterTimeUpdate's fallback) looks up findSeiAtTime at the master's
    /// time: the record for the dashboard, PARKED when there is none, the
    /// gauges reset once 2 s pass without one.
    /// </summary>
    public sealed class TelemetryFeed
    {
        /// <summary>resetDashboardElements after this long without a record (timeSinceLastSei &gt; 2000).</summary>
        public const double ResetAfterMs = 2000;

        ClipTelemetry data = ClipTelemetry.Empty;
        double lastSeiMs = double.NegativeInfinity;
        bool reset;
        int token;
        int segment = -1;

        /// <summary>The profile's telemetry capability (library.capabilities.telemetry !== false).</summary>
        public bool Enabled = true;

        /// <summary>nativeVideo.seiData / mapPath: the playing segment's telemetry (empty until its answer is in).</summary>
        public ClipTelemetry Data { get { return data; } }
        /// <summary>The map's path (part 10): the segment's points with a GPS fix.</summary>
        public IReadOnlyList<MapPoint> MapPath { get { return data.MapPath; } }
        /// <summary>nativeVideo.lastSeiTimeMs.</summary>
        public double LastSeiMs { get { return lastSeiMs; } }
        /// <summary>nativeVideo.dashboardReset.</summary>
        public bool DashboardReset { get { return reset; } }
        /// <summary>The segment the last load was for (-1: none).</summary>
        public int Segment { get { return segment; } }

        /// <summary>
        /// telemetryEntryFor(group): the master camera's clip (front for
        /// Tesla), else the group's first; null for a group without clips.
        /// </summary>
        public static string EntryFor(PlaybackSegment group, string masterCamera)
        {
            if (group == null) return null;
            string f;
            if (group.FilesByCamera.TryGetValue(string.IsNullOrEmpty(masterCamera) ? "front" : masterCamera, out f) && f != null) return f;
            foreach (KeyValuePair<string, string> kv in group.FilesByCamera) return kv.Value;
            return null;
        }

        /// <summary>
        /// loadNativeSegment: the old segment's telemetry dropped at once (no
        /// stale gauges during the switch), a new load token. Returns the
        /// token its answer must bring back to <see cref="Deliver"/>.
        /// </summary>
        public int BeginSegment(int segIdx)
        {
            segment = segIdx;
            data = ClipTelemetry.Empty;
            lastSeiMs = double.NegativeInfinity;
            reset = false;
            return ++token;
        }

        /// <summary>
        /// The extraction's answer: kept when no other load, profile switch or
        /// close came since (telemetryLoadToken) and the segment is still the
        /// one playing; false when it is discarded as stale.
        /// </summary>
        public bool Deliver(int loadToken, int segIdx, ClipTelemetry answer)
        {
            if (loadToken != token || segIdx != segment) return false;
            data = answer ?? ClipTelemetry.Empty;
            return true;
        }

        /// <summary>
        /// A profile switch or the collection closed (applyDashcamProfile,
        /// clearing the dashboard and map): everything dropped, any answer
        /// still on its way discarded.
        /// </summary>
        public void Clear()
        {
            token++;
            segment = -1;
            data = ClipTelemetry.Empty;
            lastSeiMs = double.NegativeInfinity;
            reset = false;
        }

        /// <summary>
        /// One tick at the master video's time (currentTime x 1000), as
        /// telemetryAnimationLoop and onMasterTimeUpdate do it.
        /// </summary>
        public TelemetryStep Tick(double videoMs, bool dashboardEnabled, bool transitioning)
        {
            TelemetryStep s = new TelemetryStep();
            if (!Enabled || transitioning) return s;
            s.Ran = true;
            Sei sei = data.At(videoMs);
            if (sei != null)
            {
                if (dashboardEnabled) s.Parked = false;
                s.Sei = sei;
                lastSeiMs = videoMs;
                reset = false;
            }
            else if (dashboardEnabled)
            {
                // no telemetry near this time (parked): PARKED, gauges hidden
                s.Parked = true;
                double since = videoMs - lastSeiMs;
                if (since > ResetAfterMs && !reset)
                {
                    s.Reset = true;
                    reset = true;
                }
            }
            return s;
        }
    }
}
