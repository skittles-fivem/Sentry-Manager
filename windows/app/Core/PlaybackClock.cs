using System;
using System.Diagnostics;

namespace SentrySix.Core
{
    /// <summary>Where the clock reads the time (seconds, only differences matter); a test gives its own.</summary>
    public interface ITimeSource
    {
        double Now { get; }
    }

    /// <summary>The PC's Stopwatch.</summary>
    public sealed class StopwatchTime : ITimeSource
    {
        public static readonly StopwatchTime Instance = new StopwatchTime();
        readonly Stopwatch sw = Stopwatch.StartNew();
        public double Now { get { return sw.Elapsed.TotalSeconds; } }
    }

    /// <summary>
    /// The one clock every camera, the telemetry, the time display, the
    /// progress bar, the recording time and the map read (the web's master
    /// video element, whose currentTime the rAF loop reads). Position is in
    /// collection seconds: base + (now - baseAt) x rate while playing; play,
    /// pause, seek and a new rate rebase it.
    ///
    /// The master camera's decoder holds it back: the clock never runs past
    /// the newest frame the master has decoded (<see cref="MasterReady"/>),
    /// so a stalled master is "buffering" and every camera waits, as the
    /// web's followers wait for the master video. While held, the position
    /// stays at the master's frame and runs on from there once it comes.
    /// Not thread safe: the UI thread owns it.
    /// </summary>
    public sealed class PlaybackClock
    {
        public const double MinRate = 0.5;
        public const double MaxRate = 4;

        readonly ITimeSource time;
        double basePos;
        double baseAt;
        double rate = 1;
        bool playing;
        double masterReady = double.PositiveInfinity;
        double end = double.PositiveInfinity;
        bool held;

        public PlaybackClock() : this(StopwatchTime.Instance) { }

        public PlaybackClock(ITimeSource time)
        {
            if (time == null) throw new ArgumentNullException("time");
            this.time = time;
        }

        public bool Playing { get { return playing; } }
        public double Rate { get { return rate; } }

        /// <summary>True while playing but held at the master's newest frame (the page shows its loading state).</summary>
        public bool Buffering { get { Read(); return held; } }

        /// <summary>Where playback stops (the collection's length); the clock pauses there by itself.</summary>
        public double End
        {
            get { return end; }
            set { Rebase(); end = value > 0 ? value : double.PositiveInfinity; if (basePos > end) basePos = end; }
        }

        /// <summary>
        /// The newest collection time the master has a frame for
        /// (+infinity: no limit, e.g. no master yet or the master's clip
        /// ended). The decoders set it as frames come.
        /// </summary>
        public double MasterReady
        {
            get { return masterReady; }
            set { Rebase(); masterReady = value; }
        }

        /// <summary>The position now, in collection seconds.</summary>
        public double Position { get { return Read(); } }

        double Free()
        {
            return playing ? basePos + (time.Now - baseAt) * rate : basePos;
        }

        double Read()
        {
            double p = Free();
            held = false;
            if (playing && p > masterReady && masterReady >= basePos)
            {
                p = masterReady;
                held = true;
            }
            else if (playing && p > masterReady)
            {
                // the master is behind where the clock was rebased (a seek it
                // has not caught up with): wait at the rebased position
                p = basePos;
                held = true;
            }
            if (p >= end)
            {
                p = end;
                if (playing) { playing = false; basePos = end; baseAt = time.Now; held = false; }
            }
            return p;
        }

        // the position as read now becomes the base (so a hold is not lost: playback goes on from the held frame)
        void Rebase()
        {
            double p = Read();
            basePos = p;
            baseAt = time.Now;
        }

        public void Play()
        {
            if (playing) return;
            if (basePos >= end) return;
            basePos = Read();
            baseAt = time.Now;
            playing = true;
        }

        public void Pause()
        {
            if (!playing) return;
            Rebase();
            playing = false;
            held = false;
        }

        /// <summary>Jumps to a collection time (clamped to 0 .. End); playing goes on from there.</summary>
        public void Seek(double position)
        {
            if (double.IsNaN(position)) position = 0;
            basePos = Math.Max(0, Math.Min(end, position));
            baseAt = time.Now;
        }

        /// <summary>The speed (0.5 to 4, as the web's speed list); the position so far is kept.</summary>
        public void SetRate(double r)
        {
            if (double.IsNaN(r) || r <= 0) r = 1;
            r = Math.Max(MinRate, Math.Min(MaxRate, r));
            Rebase();
            rate = r;
        }
    }

    /// <summary>What a camera that is not the master does with the frames it has (the web's syncMultiVideos).</summary>
    public enum FollowerAction
    {
        /// <summary>Show the newest frame at or before the clock.</summary>
        Show,
        /// <summary>More than 0.15 s behind: drop frames to catch up.</summary>
        Drop,
        /// <summary>More than 1.5 s behind or ahead: start its decoder again at the clock (the web's re-seek).</summary>
        Restart
    }

    /// <summary>The web's drift rules for the followers of the master.</summary>
    public static class PlaybackSync
    {
        /// <summary>multiCamFocus.js syncMultiVideos: a follower further off than this is put right.</summary>
        public const double DriftSeconds = 0.15;
        /// <summary>Further than this, dropping frames would take too long: the decoder starts again at the clock.</summary>
        public const double RestartSeconds = 1.5;

        /// <summary>
        /// newest: the newest decoded frame's time this camera has (NaN:
        /// none yet). A camera ahead of the clock waits (its ring is full and
        /// it blocks), unless it is so far ahead it was decoding the wrong
        /// place.
        /// </summary>
        public static FollowerAction Decide(double clock, double newest, bool failed)
        {
            return Decide(clock, newest, failed, 1);
        }

        /// <summary>
        /// As above at a playback rate: the restart limit grows with the rate
        /// (at 4x a decoder's start-up time alone is seconds of clip), so a
        /// camera restarted at 2x-4x is not restarted again for its own start.
        /// </summary>
        public static FollowerAction Decide(double clock, double newest, bool failed, double rate)
        {
            if (failed) return FollowerAction.Restart;
            if (double.IsNaN(newest)) return FollowerAction.Show;
            double late = clock - newest;
            double limit = RestartSeconds * Math.Max(1, double.IsNaN(rate) ? 1 : rate);
            if (late > limit || -late > limit) return FollowerAction.Restart;
            if (late > DriftSeconds) return FollowerAction.Drop;
            return FollowerAction.Show;
        }
    }
}
