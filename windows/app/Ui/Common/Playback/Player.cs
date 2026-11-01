using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SentrySix.Core;

namespace SentrySix.Ui.Playback
{
    /// <summary>
    /// Six cameras of a day collection on one clock, decoded by the
    /// package's ffmpeg (no Windows codecs): H.264, H.265 and AV1 alike. One
    /// ffmpeg per camera per segment reads its clip through the
    /// <see cref="MediaRelay"/>; the next segment's decoders start before
    /// the boundary and wait on full rings; one CompositionTarget.Rendering
    /// handler shows each camera's newest frame at or before the clock.
    /// The master camera holds the clock (buffering), followers that fall
    /// behind drop frames or start again at the clock (the web's 0.15 s
    /// re-seek). Takes no look of its own: the Viewer page (and the packs'
    /// viewers) draw the controls and place the <see cref="VideoSurface"/>s.
    /// Use on the UI thread.
    /// </summary>
    public sealed class Player : IDisposable
    {
        /// <summary>Restart a camera at a new tile size this long after the last resize.</summary>
        public const int ResizeDebounceMs = 300;
        /// <summary>A scrub preview at most this often.</summary>
        public const int ScrubPreviewMs = 80;
        /// <summary>Paused and off screen this long: the decoders are let go (started again on return).</summary>
        public const int HiddenReleaseMs = 30000;
        /// <summary>How many segments' front clips are probed at once (resolveSegmentDurations' concurrency).</summary>
        public const int ProbeConcurrency = 4;
        /// <summary>A follower restarted for drift starts this far (x the rate) ahead of the clock: its decoder's start-up time.</summary>
        public const double StartupLeadSec = 0.5;
        /// <summary>A scrub preview that has shown nothing yet is replaced after this long at the latest.</summary>
        public const int PreviewPatienceMs = 1500;

        sealed class SegmentSet
        {
            public int Index;
            public SegmentView View;
            public double Offset;
            public string MasterSlot;
            /// <summary>Where the clock waits while the master has no frame yet (its start, or where it was restarted).</summary>
            public double HeldAt;
            public readonly Dictionary<string, CameraStream> Streams = new Dictionary<string, CameraStream>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> Restarts = new Dictionary<string, int>(StringComparer.Ordinal);

            public void Dispose()
            {
                foreach (CameraStream s in Streams.Values) s.Dispose();
                Streams.Clear();
            }

            public CameraStream Master
            {
                get { CameraStream s; return MasterSlot != null && Streams.TryGetValue(MasterSlot, out s) ? s : null; }
            }
        }

        readonly MediaRelay relay;
        readonly string ffmpeg;
        readonly string ffprobe;
        readonly DecodePaths paths;
        readonly Dispatcher ui;
        readonly PlaybackClock clock;
        readonly Dictionary<string, VideoSurface> surfaces = new Dictionary<string, VideoSurface>(StringComparer.Ordinal);
        readonly Dictionary<string, Task<ProbeResult>> probes = new Dictionary<string, Task<ProbeResult>>(StringComparer.Ordinal);
        readonly HashSet<string> hwFailedFamily = new HashSet<string>(StringComparer.Ordinal);
        readonly DispatcherTimer resizeTimer;
        readonly DispatcherTimer scrubTimer;
        readonly DispatcherTimer hiddenTimer;
        readonly CancellationTokenSource closing = new CancellationTokenSource();

        List<PlaybackSegment> segments = new List<PlaybackSegment>();
        SegmentPlan plan = new SegmentPlan(0, SegmentPlan.TeslaSegmentSeconds);
        List<LayoutSlot> slots = new List<LayoutSlot>();
        SegmentSet current, next;
        Dictionary<string, CameraStream> preview = new Dictionary<string, CameraStream>(StringComparer.Ordinal);
        int generation;
        int preloading = -1;
        int collectionGeneration;
        bool hooked;
        bool disposed;
        bool scrubbing, wasPlayingBeforeScrub;
        double scrubTarget = double.NaN;
        bool previewPending;
        readonly System.Diagnostics.Stopwatch previewAge = new System.Diagnostics.Stopwatch();
        bool released;
        long shown, dropped, restarts;

        /// <param name="relay">The clips' loopback relay (on the session's Api).</param>
        /// <param name="paths">The decode paths of this PC (null: the CPU for everything).</param>
        public Player(MediaRelay relay, string ffmpegPath, string ffprobePath, DecodePaths paths)
        {
            if (relay == null) throw new ArgumentNullException("relay");
            this.relay = relay;
            ffmpeg = ffmpegPath;
            ffprobe = ffprobePath;
            this.paths = paths;
            ui = Dispatcher.CurrentDispatcher;
            clock = new PlaybackClock();
            PlaybackJob.Install();
            MasterCamera = "front";
            SelectedCamera = "front";
            MultiEnabled = true;
            Hardware = true;
            resizeTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(ResizeDebounceMs) };
            resizeTimer.Tick += delegate { resizeTimer.Stop(); ApplySizes(); };
            scrubTimer = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(ScrubPreviewMs) };
            scrubTimer.Tick += delegate { scrubTimer.Stop(); OnScrubTimer(); };
            hiddenTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(HiddenReleaseMs) };
            hiddenTimer.Tick += delegate { hiddenTimer.Stop(); Release(); };
        }

        // ------------------------------------------------------------ what is shown

        public PlaybackClock Clock { get { return clock; } }
        public SegmentPlan Plan { get { return plan; } }
        public IReadOnlyList<PlaybackSegment> Segments { get { return segments; } }

        /// <summary>The camera that holds the clock (the profile's master, front for Tesla).</summary>
        public string MasterCamera { get; set; }
        /// <summary>The single view's camera (MultiEnabled off).</summary>
        public string SelectedCamera { get; set; }
        public bool MultiEnabled { get; set; }
        /// <summary>Studio's Hardware Video Acceleration (settingsHardwareVideoAccel): off decodes on the CPU only.</summary>
        public bool Hardware { get; set; }

        /// <summary>The slot shown alone and at the source size (multiCamFocus), or null.</summary>
        public string FocusSlot { get; private set; }

        /// <summary>Each render tick, after the frames: the page reads the clock (time, progress, telemetry, map) here.</summary>
        public event Action Tick;

        /// <summary>A segment is now the one playing (index).</summary>
        public event Action<int> SegmentOpened;
        /// <summary>The next segment started loading ahead (maybePreloadNextSegment): its index.</summary>
        public event Action<int> PreloadStarted;

        /// <summary>A segment could not be played and was skipped (index, why).</summary>
        public event Action<int, string> SegmentFailed;

        /// <summary>
        /// The GPU path of a codec family failed on this PC: it decodes on the
        /// CPU for the rest of the session and the decode paths now say so
        /// (the page saves <see cref="DecodePaths.Save"/>).
        /// </summary>
        public event Action<string> DecodePathFailed;

        /// <summary>The grid's slots (the profile's layoutSlots with the custom camera order).</summary>
        public void SetLayout(IEnumerable<LayoutSlot> layout)
        {
            slots = layout == null ? new List<LayoutSlot>() : layout.ToList();
        }

        /// <summary>
        /// The grid's slots changed (the HW4 / HW3 switch, a camera swap, a
        /// reset order): the playing segment loads again with them at the same
        /// place, playing on when it played (setMultiLayout's and the drop's
        /// loadNativeSegment).
        /// </summary>
        public void Relayout(IEnumerable<LayoutSlot> layout)
        {
            SetLayout(layout);
            if (current != null && segments.Count > 0) Seek(clock.Position);
        }

        /// <summary>What the playing segment shows (each slot's clip or none), or null before one has opened.</summary>
        public SegmentView View { get { return current == null ? null : current.View; } }

        /// <summary>The picture for a slot ("main" for the single view).</summary>
        public void Attach(string slot, VideoSurface surface)
        {
            VideoSurface old;
            if (surfaces.TryGetValue(slot, out old)) old.PixelSizeChanged -= OnResize;
            surfaces[slot] = surface;
            surface.Slot = slot;
            surface.PixelSizeChanged += OnResize;
        }

        void OnResize()
        {
            resizeTimer.Stop();
            resizeTimer.Start();
        }

        public bool Playing { get { return clock.Playing; } }
        public bool Buffering { get { return clock.Buffering; } }
        public double Position { get { return clock.Position; } }
        public double Total { get { return plan.Total; } }
        public int SegmentIndex { get { return current == null ? -1 : current.Index; } }

        /// <summary>The clock in the playing segment (the master video's currentTime).</summary>
        public double LocalTime
        {
            get { return current == null ? 0 : Math.Max(0, clock.Position - plan.Start(current.Index)); }
        }

        /// <summary>The playing segment's decoders by slot (diagnostics: the playback check, the timing log).</summary>
        public IList<KeyValuePair<string, CameraStream>> CurrentStreams
        {
            get { return current == null ? new List<KeyValuePair<string, CameraStream>>() : current.Streams.ToList(); }
        }

        /// <summary>Frames shown and dropped, decoders restarted so far.</summary>
        public long FramesShown { get { return shown; } }
        public long FramesDropped { get { return dropped; } }
        public long Restarts { get { return restarts; } }

        /// <summary>For the timing log: frames shown and dropped, decoders restarted, each camera's decode path.</summary>
        public string Describe()
        {
            List<string> parts = new List<string>();
            parts.Add("shown " + shown + ", dropped " + dropped + ", restarts " + restarts);
            if (current != null)
                foreach (KeyValuePair<string, CameraStream> kv in current.Streams)
                    parts.Add(kv.Key + " " + kv.Value.PathUsed + " " + kv.Value.Request.Width + "x" + kv.Value.Request.Height + (kv.Value.Failed ? " failed" : ""));
            return string.Join("; ", parts);
        }

        // ------------------------------------------------------------ opening a collection

        /// <summary>
        /// A day collection: its segments in order, the profile's segment
        /// estimate, and where to start (seconds; the event's 15 s lead, else
        /// 0). Durations are probed in the background (4 at a time) and the
        /// timeline corrected when they are in, as selectDayCollection.
        /// </summary>
        public void Open(IList<PlaybackSegment> collection, double fallbackSeconds, double startOffsetSec)
        {
            Stop();
            collectionGeneration++;
            segments = collection == null ? new List<PlaybackSegment>() : collection.ToList();
            plan = new SegmentPlan(segments.Count, fallbackSeconds);
            clock.Pause();
            clock.End = plan.Total;
            Hook();
            int seg;
            double local;
            plan.FirstLoad(startOffsetSec, out seg, out local);
            // the first picture's probes go first; the background durations queue behind them
            Start(seg, local);
            ProbeDurations(collectionGeneration);
        }

        void ProbeDurations(int gen)
        {
            List<PlaybackSegment> list = segments;
            double[] results = new double[list.Count];
            int nextIndex = -1;
            Task[] workers = new Task[Math.Max(1, Math.Min(list.Count, ProbeConcurrency))];
            for (int w = 0; w < workers.Length; w++)
            {
                workers[w] = Task.Run(async () =>
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref nextIndex);
                        if (i >= list.Count || closing.IsCancellationRequested || gen != collectionGeneration) return;
                        string f = list[i].ProbeFile;
                        results[i] = double.NaN;
                        if (f == null) continue;
                        ProbeResult p = await ProbeOf(f).ConfigureAwait(false);
                        results[i] = p.Duration;
                    }
                });
            }
            Task.WhenAll(workers).ContinueWith(t =>
            {
                ui.BeginInvoke(new Action(() =>
                {
                    if (disposed || gen != collectionGeneration) return;
                    // the clock keeps its place in its segment (the web keeps the video's currentTime):
                    // the earlier segments' durations move the segment's start, not the picture
                    int ci;
                    double keep;
                    Where(clock.Position, out ci, out keep);
                    int si = -1;
                    double skeep = 0;
                    if (!double.IsNaN(scrubTarget)) plan.Locate(scrubTarget, out si, out skeep);
                    plan.SetProbed(results);
                    // the opened segment's real duration stays
                    if (current != null)
                    {
                        Task<ProbeResult> mp;
                        string mf = MasterFile(current);
                        if (mf != null && TryProbe(mf, out mp) && mp.IsCompleted)
                            plan.SetActual(current.Index, mp.Result.Duration);
                    }
                    clock.End = plan.Total;
                    if (ci >= 0)
                    {
                        clock.Seek(plan.Start(ci) + keep);
                        if (current != null && current.Index == ci) UpdateMasterReady(current);
                        else clock.MasterReady = double.NegativeInfinity;
                    }
                    if (si >= 0) scrubTarget = plan.Start(si) + skeep;
                }));
            });
        }

        /// <summary>The clock's segment and its time in it: the playing one when there is one, else the plan's.</summary>
        void Where(double pos, out int seg, out double local)
        {
            if (current != null)
            {
                seg = current.Index;
                local = Math.Max(0, pos - plan.Start(seg));
                return;
            }
            if (segments.Count == 0) { seg = -1; local = 0; return; }
            plan.Locate(pos, out seg, out local);
        }

        Task<ProbeResult> ProbeOf(string serverPath)
        {
            lock (probes)
            {
                Task<ProbeResult> t;
                if (probes.TryGetValue(serverPath, out t)) return t;
                string url = relay.UrlFor(serverPath);
                string probe = ffprobe;
                // ffprobe blocks its thread until it exits: its own thread, not one of the pool's
                t = Task.Factory.StartNew(() => probe == null ? new ProbeResult() : FfmpegTools.ProbeClip(probe, url, 20000),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                probes[serverPath] = t;
                return t;
            }
        }

        /// <summary>The probe of a clip already asked for (the duration workers write the table from other threads).</summary>
        bool TryProbe(string serverPath, out Task<ProbeResult> t)
        {
            lock (probes) return probes.TryGetValue(serverPath, out t);
        }

        // ------------------------------------------------------------ controls

        public void Play()
        {
            if (current == null && segments.Count == 0) return;
            Hook();
            clock.Play();
        }

        public void Pause()
        {
            clock.Pause();
        }

        /// <summary>
        /// seekNativeDayCollectionBySec: kill the decoders and start new ones
        /// at the target's segment and offset; a seek during a seek replaces
        /// it (latest wins). Playing goes on.
        /// </summary>
        public void Seek(double collectionSec)
        {
            if (segments.Count == 0) return;
            int seg;
            double local;
            plan.Locate(collectionSec, out seg, out local);
            Start(seg, local);
        }

        /// <summary>skipSeconds(±N).</summary>
        public void Skip(double delta)
        {
            Seek(plan.Skip(clock.Position, delta));
        }

        /// <summary>The next or previous clip (segment); past the last, nothing.</summary>
        public void StepSegment(int delta)
        {
            int i = (current == null ? 0 : current.Index) + delta;
            if (i < 0 || i >= plan.Count) return;
            Seek(plan.Start(i));
        }

        public void SetRate(double rate)
        {
            bool fastBefore = clock.Rate >= 2;
            clock.SetRate(rate);
            if ((clock.Rate >= 2) != fastBefore)
            {
                // the preloaded segment was started with the old -skip_frame: it starts again at the new rate
                if (next != null) { next.Dispose(); next = null; }
                preloading = -1;
            }
            if ((clock.Rate >= 2) != fastBefore && current != null)
            {
                // -skip_frame nonref on or off for the followers
                foreach (string slot in current.Streams.Keys.ToList())
                    if (slot != current.MasterSlot && slot != FocusSlot) Restart(current, slot);
            }
        }

        /// <summary>multiCamFocus: one camera alone at its source size (null: the grid again), from the clock.</summary>
        public void SetFocus(string slot)
        {
            if (slot == FocusSlot) return;
            FocusSlot = slot;
            if (current != null) Seek(clock.Position);
        }

        /// <summary>The progress bar is held (pointer down): playback pauses, previews follow the slider.</summary>
        public void BeginScrub()
        {
            if (scrubbing) return;
            Hook();
            scrubbing = true;
            wasPlayingBeforeScrub = clock.Playing;
            clock.Pause();
        }

        /// <summary>The slider moved: a one-frame preview per camera there, at most every 80 ms.</summary>
        public void ScrubTo(double collectionSec)
        {
            if (!scrubbing) BeginScrub();
            scrubTarget = collectionSec;
            clock.Seek(collectionSec);
            if (!scrubTimer.IsEnabled) scrubTimer.Start();
        }

        /// <summary>Released: a real seek, playing again when it was playing (maybeAutoplayAfterSeek).</summary>
        public void EndScrub()
        {
            if (!scrubbing) return;
            scrubbing = false;
            scrubTimer.Stop();
            StopPreview();
            previewPending = false;
            if (!double.IsNaN(scrubTarget)) Seek(scrubTarget);
            scrubTarget = double.NaN;
            if (wasPlayingBeforeScrub) clock.Play();
        }

        /// <summary>
        /// The page went out of sight (another page, the window hidden): pause
        /// (the shell's 'pause' message); after 30 s the decoders are let go,
        /// and come back paused at the same place on return.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                clock.Pause();
                // nothing to draw off screen: no render tick until the page is back
                Unhook();
                hiddenTimer.Stop();
                hiddenTimer.Start();
                return;
            }
            hiddenTimer.Stop();
            if (segments.Count > 0) Hook();
            if (released && segments.Count > 0)
            {
                released = false;
                Seek(clock.Position);
            }
        }

        void Release()
        {
            if (clock.Playing) return;
            // a Prepare still waiting on its probes must not start decoders on the hidden page
            generation++;
            released = true;
            DisposeSets();
            StopPreview();
            Unhook();
        }

        // ------------------------------------------------------------ decoders

        void Start(int segIdx, double local)
        {
            int gen = ++generation;
            Hook();
            clock.Seek(plan.Start(segIdx) + local);
            // hold the clock until the master's first frame
            clock.MasterReady = double.NegativeInfinity;
            SegmentSet keep = null;
            if (next != null && next.Index == segIdx && local < 0.05 && next.Offset == 0) keep = next;
            if (current != null && current != keep) current.Dispose();
            if (next != null && next != keep) next.Dispose();
            current = null;
            next = null;
            preloading = -1;
            if (keep != null)
            {
                Activate(keep);
                return;
            }
            Prepare(segIdx, local, gen, false);
        }

        /// <summary>Probes the segment's clips, then starts its decoders (for now, or as the preloaded next segment).</summary>
        void Prepare(int segIdx, double local, int gen, bool asNext)
        {
            if (segIdx < 0 || segIdx >= segments.Count) return;
            SegmentView view = SegmentPlan.PlanSegment(segments[segIdx], MultiEnabled, slots, MasterCamera, SelectedCamera);
            List<Task<ProbeResult>> waits = new List<Task<ProbeResult>>();
            foreach (SegmentTarget t in view.Targets) if (t.File != null) waits.Add(ProbeOf(t.File));
            Task.WhenAll(waits).ContinueWith(_ => ui.BeginInvoke(new Action(() =>
            {
                if (disposed || gen != generation) return;
                SegmentSet set = new SegmentSet { Index = segIdx, View = view, Offset = local, HeldAt = local, MasterSlot = view.MasterSlot };
                if (asNext)
                {
                    if (preloading != segIdx || next != null || current == null || current.Index + 1 != segIdx) return;
                    preloading = -1;
                    next = set;
                }
                foreach (SegmentTarget t in StreamTargets(view)) StartStream(set, t, local);
                if (set.Master == null && set.Streams.Count > 0) set.MasterSlot = set.Streams.Keys.First();
                if (!asNext)
                {
                    if (set.Streams.Count == 0) { Failed(set, "no clip of this segment could be opened"); return; }
                    Activate(set);
                }
            })));
        }

        /// <summary>
        /// The clips a segment decodes: every one in the grid; while a camera
        /// is focused only that one, or, when the focused camera has no clip
        /// in this segment, the master's (else the first there is), so the
        /// clock and the telemetry go on with the focused tile cleared.
        /// </summary>
        List<SegmentTarget> StreamTargets(SegmentView view)
        {
            List<SegmentTarget> all = view.Targets.Where(t => t.File != null).ToList();
            if (FocusSlot == null) return all;
            SegmentTarget pick = all.FirstOrDefault(t => t.Slot == FocusSlot)
                ?? all.FirstOrDefault(t => t.Slot == view.MasterSlot)
                ?? all.FirstOrDefault();
            return pick == null ? new List<SegmentTarget>() : new List<SegmentTarget> { pick };
        }

        void Activate(SegmentSet set)
        {
            current = set;
            Task<ProbeResult> mp;
            string mf = MasterFile(set);
            if (mf != null && TryProbe(mf, out mp) && mp.IsCompleted)
            {
                // onMasterLoaded: the real duration
                plan.SetActual(set.Index, mp.Result.Duration);
                clock.End = plan.Total;
            }
            clock.MasterReady = plan.Start(set.Index) + set.Offset;
            foreach (KeyValuePair<string, VideoSurface> kv in surfaces)
                if (!set.View.Targets.Any(t => t.Slot == kv.Key && t.File != null)) kv.Value.Clear();
            Action<int> h = SegmentOpened;
            if (h != null) h(set.Index);
        }

        static string MasterFile(SegmentSet set)
        {
            SegmentTarget mt = set.View == null ? null : set.View.Targets.FirstOrDefault(x => x.Slot == set.MasterSlot);
            return mt == null ? null : mt.File;
        }

        DecodeRequest RequestFor(SegmentSet set, SegmentTarget t, double local, bool forceCpu)
        {
            Task<ProbeResult> pt;
            ProbeResult p = TryProbe(t.File, out pt) && pt.IsCompleted ? pt.Result : new ProbeResult();
            int w, h;
            bool full = !MultiEnabled || t.Slot == FocusSlot;
            VideoSurface s;
            if (full || !surfaces.TryGetValue(t.Slot, out s) || s.ActualWidth <= 0)
                FfmpegArgs.TileSize(full ? 1e9 : 640, full ? 1e9 : 480, p.Width, p.Height, out w, out h);
            else
            {
                Size px = s.PixelSize;
                FfmpegArgs.TileSize(px.Width, px.Height, p.Width, p.Height, out w, out h);
            }
            DecodePath path = paths == null ? DecodePath.Cpu : paths.For(p.Codec, Hardware);
            string family = FfmpegArgs.Family(p.Codec);
            if (forceCpu || hwFailedFamily.Contains(family)) path = DecodePath.Cpu;
            return new DecodeRequest
            {
                Url = relay.UrlFor(t.File),
                Offset = Math.Max(0, local),
                Width = w,
                Height = h,
                Fps = p.Fps > 0 ? p.Fps : FfmpegArgs.DefaultFps,
                Path = path,
                Threads = FfmpegArgs.CpuThreads(Environment.ProcessorCount),
                SkipNonRef = clock.Rate >= 2 && t.Slot != set.MasterSlot && t.Slot != FocusSlot && (family == "h264" || family == "hevc")
            };
        }

        void StartStream(SegmentSet set, SegmentTarget t, double local)
        {
            if (ffmpeg == null) return;
            DecodeRequest r = RequestFor(set, t, local, false);
            CameraStream s = new CameraStream(ffmpeg, r, t.Slot == FocusSlot ? 3 : 4);
            try
            {
                s.Start();
            }
            catch (Exception)
            {
                s.Dispose();
                return;
            }
            set.Streams[t.Slot] = s;
        }

        void Restart(SegmentSet set, string slot)
        {
            SegmentTarget t = set.View.Targets.FirstOrDefault(x => x.Slot == slot);
            CameraStream old;
            if (t == null || t.File == null || !set.Streams.TryGetValue(slot, out old)) return;
            if (old.Failed && old.PathUsed != DecodePath.Cpu) GpuFailed(t.File);
            old.Dispose();
            set.Streams.Remove(slot);
            restarts++;
            double local = set == current ? Math.Max(0, clock.Position - plan.Start(set.Index)) : set.Offset;
            if (slot == set.MasterSlot) set.HeldAt = local; // the clock waits here for the master's new first frame
            else if (set == current)
            {
                // a follower's first frame comes after ffmpeg's start, the relay and the seek: start it ahead
                double dur = plan.Duration(set.Index);
                double ahead = local + clock.Rate * StartupLeadSec;
                local = dur > 0.1 ? Math.Min(ahead, dur - 0.1) : ahead;
            }
            StartStream(set, t, local);
        }

        /// <summary>
        /// A GPU path that cannot decode: its codec family decodes on the CPU
        /// for the rest of the session (every clip, not just this one) and
        /// the remembered paths say the CPU, so the next run does not try again.
        /// </summary>
        void GpuFailed(string file)
        {
            Task<ProbeResult> pt;
            ProbeResult p = TryProbe(file, out pt) && pt.IsCompleted ? pt.Result : new ProbeResult();
            string family = FfmpegArgs.Family(p.Codec);
            if (string.IsNullOrEmpty(family) || !hwFailedFamily.Add(family)) return;
            if (paths != null) paths.Set(family, DecodePath.Cpu);
            Action<string> h = DecodePathFailed;
            if (h != null) h(family);
        }

        /// <summary>At most three restarts of a camera per segment for drift or errors (a broken clip does not restart each tick).</summary>
        static bool Budget(SegmentSet set, string slot)
        {
            int n;
            set.Restarts.TryGetValue(slot, out n);
            if (n >= 3) return false;
            set.Restarts[slot] = n + 1;
            return true;
        }

        void ApplySizes()
        {
            if (current == null) return;
            foreach (SegmentSet set in new[] { current, next })
            {
                if (set == null) continue;
                foreach (SegmentTarget t in set.View.Targets)
                {
                    CameraStream s;
                    if (t.File == null || !set.Streams.TryGetValue(t.Slot, out s)) continue;
                    DecodeRequest want = RequestFor(set, t, 0, false);
                    if (want.Width != s.Request.Width || want.Height != s.Request.Height) Restart(set, t.Slot);
                }
            }
        }

        void Failed(SegmentSet set, string why)
        {
            set.Dispose();
            if (set == current) current = null;
            Action<int, string> h = SegmentFailed;
            if (h != null) h(set.Index, why);
            // a segment that fails is skipped; the last one pauses
            if (set.Index + 1 < plan.Count)
            {
                Start(set.Index + 1, 0);
            }
            else
            {
                clock.Pause();
                clock.MasterReady = double.PositiveInfinity;
            }
        }

        void StopPreview()
        {
            foreach (CameraStream s in preview.Values) s.Dispose();
            preview.Clear();
        }

        /// <summary>
        /// The scrub timer: a new preview only once the last one showed its
        /// frame or ended (latest target wins); a preview still decoding is
        /// waited for, so a steady drag shows frames instead of killing each
        /// one before it arrives. The frame shown stays until the next comes.
        /// </summary>
        void OnScrubTimer()
        {
            if (!scrubbing) return;
            bool busy = previewPending || preview.Values.Any(s => s.Frames == 0 && !s.Ended && !s.Failed);
            if (busy && previewAge.ElapsedMilliseconds < PreviewPatienceMs)
            {
                scrubTimer.Start();
                return;
            }
            StartPreview();
        }

        void StartPreview()
        {
            if (!scrubbing || double.IsNaN(scrubTarget) || segments.Count == 0 || ffmpeg == null) return;
            StopPreview();
            previewPending = true;
            previewAge.Restart();
            int seg;
            double local;
            plan.Locate(scrubTarget, out seg, out local);
            SegmentView view = SegmentPlan.PlanSegment(segments[seg], MultiEnabled, slots, MasterCamera, SelectedCamera);
            double target = scrubTarget;
            List<Task<ProbeResult>> waits = new List<Task<ProbeResult>>();
            foreach (SegmentTarget t in view.Targets) if (t.File != null) waits.Add(ProbeOf(t.File));
            Task.WhenAll(waits).ContinueWith(_ => ui.BeginInvoke(new Action(() =>
            {
                previewPending = false;
                if (disposed || !scrubbing) return;
                // the slider moved on while the probes ran: the timer starts the newer one at once
                if (target != scrubTarget) { scrubTimer.Stop(); scrubTimer.Start(); return; }
                SegmentSet set = new SegmentSet { Index = seg, View = view, MasterSlot = view.MasterSlot };
                foreach (SegmentTarget t in StreamTargets(view))
                {
                    DecodeRequest r = RequestFor(set, t, local, false);
                    r.OneFrame = true;
                    r.SkipNonRef = false;
                    CameraStream s = new CameraStream(ffmpeg, r, 1);
                    try { s.Start(); } catch (Exception) { s.Dispose(); continue; }
                    preview[t.Slot] = s;
                }
            })));
        }

        // ------------------------------------------------------------ the render tick

        void Hook()
        {
            if (hooked) return;
            CompositionTarget.Rendering += OnRendering;
            hooked = true;
        }

        /// <summary>No render tick (and no page tick) while nothing is on screen: the page hidden, released or stopped.</summary>
        void Unhook()
        {
            if (!hooked) return;
            CompositionTarget.Rendering -= OnRendering;
            hooked = false;
        }

        void OnRendering(object sender, EventArgs e)
        {
            if (disposed) return;
            if (scrubbing)
            {
                foreach (KeyValuePair<string, CameraStream> kv in preview)
                {
                    VideoFrame f = kv.Value.Ring.Take(double.PositiveInfinity);
                    VideoSurface s;
                    if (f != null && surfaces.TryGetValue(kv.Key, out s)) { s.Show(f, kv.Value); shown++; }
                    kv.Value.Ring.Return(f);
                }
                RaiseTick();
                return;
            }
            SegmentSet set = current;
            if (set != null)
            {
                double pos = clock.Position;
                // the boundary: the preloaded segment takes over with no gap
                if (set.Index + 1 < plan.Count && pos >= plan.Start(set.Index + 1) - 1e-6)
                {
                    Advance(set);
                    set = current;
                }
            }
            if (set != null) Render(set);
            RaiseTick();
        }

        void Advance(SegmentSet from)
        {
            int to = from.Index + 1;
            from.Dispose();
            current = null;
            preloading = -1;
            if (next != null && next.Index == to)
            {
                SegmentSet n = next;
                next = null;
                Activate(n);
            }
            else
            {
                if (next != null) { next.Dispose(); next = null; }
                clock.MasterReady = plan.Start(to);
                Prepare(to, 0, ++generation, false);
            }
        }

        void Render(SegmentSet set)
        {
            double start = plan.Start(set.Index);
            double local = clock.Position - start;
            CameraStream master = set.Master;
            foreach (string slot in set.Streams.Keys.ToList())
            {
                CameraStream s = set.Streams[slot];
                bool isMaster = s == master;
                if (s.Failed)
                {
                    // a GPU path that fails is tried again on the CPU; a clip that fails there is let go
                    if (s.PathUsed != DecodePath.Cpu) { Restart(set, slot); continue; }
                    int tried;
                    set.Restarts.TryGetValue(slot, out tried);
                    if (isMaster && s.Frames == 0 && tried == 0) { Failed(set, s.Error); return; }
                    // a master or follower that dies part way (a dropped connection after a long pause)
                    // starts again at the clock, three times; the clock waits for the master meanwhile
                    if (Budget(set, slot)) { Restart(set, slot); if (isMaster) master = set.Master; continue; }
                    continue; // out of tries: show what it has; for the master the clock is let go below
                }
                if (!isMaster && !s.Ended && s.Frames > 0 && PlaybackSync.Decide(local, s.NewestTime, false, clock.Rate) == FollowerAction.Restart && Budget(set, slot))
                {
                    Restart(set, slot);
                    continue;
                }
                int before = s.Ring.Count;
                VideoFrame f = s.Ring.Take(local);
                if (f != null)
                {
                    VideoSurface surface;
                    if (surfaces.TryGetValue(slot, out surface)) { surface.Show(f, s); shown++; }
                    dropped += Math.Max(0, before - s.Ring.Count - 1);
                    s.Ring.Return(f);
                }
            }
            UpdateMasterReady(set);
            // the preloaded segment's decoders that failed before the boundary start again now
            // (a GPU path on the CPU), so the switch stays gapless
            if (next != null)
            {
                foreach (string slot in next.Streams.Keys.ToList())
                {
                    CameraStream ns = next.Streams[slot];
                    if (ns.Failed && (ns.PathUsed != DecodePath.Cpu || Budget(next, slot))) Restart(next, slot);
                }
            }
            // next-segment preload
            if (next == null && preloading < 0 && set.Index + 1 < plan.Count &&
                SegmentPlan.ShouldStartPreload(local, plan.Duration(set.Index), clock.Rate, SegmentPlan.PreloadLeadSeconds, SegmentPlan.PreloadSettleSeconds))
            {
                preloading = set.Index + 1;
                Prepare(preloading, 0, generation, true);
                // (the page loads its telemetry into the cache, where the switch finds it)
                Action<int> pre = PreloadStarted;
                if (pre != null) pre(preloading);
            }
        }

        /// <summary>The master holds the clock to its newest frame (buffering); while it has none, at where it started.</summary>
        void UpdateMasterReady(SegmentSet set)
        {
            double start = plan.Start(set.Index);
            CameraStream master = set.Master;
            if (master == null || master.Ended || master.Failed) clock.MasterReady = double.PositiveInfinity;
            else
            {
                double newest = master.NewestTime;
                double fps = master.Request.Fps > 0 ? master.Request.Fps : FfmpegArgs.DefaultFps;
                clock.MasterReady = double.IsNaN(newest) ? start + set.HeldAt : start + newest + 1 / fps;
            }
        }

        void RaiseTick()
        {
            Action h = Tick;
            if (h != null) h();
        }

        // ------------------------------------------------------------ ending

        void DisposeSets()
        {
            preloading = -1;
            if (current != null) current.Dispose();
            if (next != null) next.Dispose();
            current = null;
            next = null;
        }

        /// <summary>Stops everything of the collection (Open again to play another).</summary>
        public void Stop()
        {
            generation++;
            DisposeSets();
            StopPreview();
            clock.Pause();
            Unhook();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            closing.Cancel();
            Unhook();
            resizeTimer.Stop();
            scrubTimer.Stop();
            hiddenTimer.Stop();
            DisposeSets();
            StopPreview();
            foreach (VideoSurface s in surfaces.Values) s.PixelSizeChanged -= OnResize;
        }
    }
}
