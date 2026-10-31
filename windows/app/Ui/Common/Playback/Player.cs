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

        sealed class SegmentSet
        {
            public int Index;
            public SegmentView View;
            public double Offset;
            public string MasterSlot;
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
        readonly TelemetryCache telemetry;
        readonly Dispatcher ui;
        readonly PlaybackClock clock;
        readonly Dictionary<string, VideoSurface> surfaces = new Dictionary<string, VideoSurface>(StringComparer.Ordinal);
        readonly Dictionary<string, Task<ProbeResult>> probes = new Dictionary<string, Task<ProbeResult>>(StringComparer.Ordinal);
        readonly HashSet<string> hwFailed = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<int, ClipTelemetry> telemetryBySegment = new Dictionary<int, ClipTelemetry>();
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
        bool released;
        long shown, dropped, restarts;

        /// <param name="relay">The clips' loopback relay (on the session's Api).</param>
        /// <param name="paths">The decode paths of this PC (null: the CPU for everything).</param>
        /// <param name="telemetry">Telemetry for the master's clips (null: none).</param>
        public Player(MediaRelay relay, string ffmpegPath, string ffprobePath, DecodePaths paths, TelemetryCache telemetry)
        {
            if (relay == null) throw new ArgumentNullException("relay");
            this.relay = relay;
            ffmpeg = ffmpegPath;
            ffprobe = ffprobePath;
            this.paths = paths;
            this.telemetry = telemetry;
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
            scrubTimer.Tick += delegate { scrubTimer.Stop(); StartPreview(); };
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

        /// <summary>A segment could not be played and was skipped (index, why).</summary>
        public event Action<int, string> SegmentFailed;

        /// <summary>The grid's slots (the profile's layoutSlots with the custom camera order).</summary>
        public void SetLayout(IEnumerable<LayoutSlot> layout)
        {
            slots = layout == null ? new List<LayoutSlot>() : layout.ToList();
        }

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

        /// <summary>The playing segment's telemetry (empty until it is in).</summary>
        public ClipTelemetry Telemetry
        {
            get
            {
                ClipTelemetry t;
                return current != null && telemetryBySegment.TryGetValue(current.Index, out t) ? t : ClipTelemetry.Empty;
            }
        }

        /// <summary>findSeiAtTime(seiData, currentTime x 1000): the dashboard's record now, or null (parked).</summary>
        public Sei CurrentSei { get { return Telemetry.At(LocalTime * 1000); } }

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
            telemetryBySegment.Clear();
            clock.Pause();
            clock.End = plan.Total;
            Hook();
            ProbeDurations(collectionGeneration);
            int seg;
            double local;
            plan.FirstLoad(startOffsetSec, out seg, out local);
            Start(seg, local);
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
                    plan.SetProbed(results);
                    // the opened segment's real duration stays
                    if (current != null)
                    {
                        Task<ProbeResult> mp;
                        string mf = MasterFile(current);
                        if (mf != null && probes.TryGetValue(mf, out mp) && mp.IsCompleted)
                            plan.SetActual(current.Index, mp.Result.Duration);
                    }
                    clock.End = plan.Total;
                }));
            });
        }

        Task<ProbeResult> ProbeOf(string serverPath)
        {
            lock (probes)
            {
                Task<ProbeResult> t;
                if (probes.TryGetValue(serverPath, out t)) return t;
                string url = relay.UrlFor(serverPath);
                string probe = ffprobe;
                t = Task.Run(() => probe == null ? new ProbeResult() : FfmpegTools.ProbeClip(probe, url, 20000));
                probes[serverPath] = t;
                return t;
            }
        }

        // ------------------------------------------------------------ controls

        public void Play()
        {
            if (current == null && segments.Count == 0) return;
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
                hiddenTimer.Stop();
                hiddenTimer.Start();
                return;
            }
            hiddenTimer.Stop();
            if (released && segments.Count > 0)
            {
                released = false;
                Seek(clock.Position);
            }
        }

        void Release()
        {
            if (clock.Playing) return;
            released = true;
            DisposeSets();
            StopPreview();
        }

        // ------------------------------------------------------------ decoders

        void Start(int segIdx, double local)
        {
            int gen = ++generation;
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
                SegmentSet set = new SegmentSet { Index = segIdx, View = view, Offset = local, MasterSlot = view.MasterSlot };
                if (asNext)
                {
                    if (preloading != segIdx || next != null || current == null || current.Index + 1 != segIdx) return;
                    preloading = -1;
                    next = set;
                }
                foreach (SegmentTarget t in view.Targets)
                {
                    if (t.File == null) continue;
                    if (FocusSlot != null && t.Slot != FocusSlot) continue; // the others wait while one is focused
                    StartStream(set, t, local);
                }
                if (set.Master == null && set.Streams.Count > 0) set.MasterSlot = set.Streams.Keys.First();
                if (!asNext)
                {
                    if (set.Streams.Count == 0) { Failed(set, "no clip of this segment could be opened"); return; }
                    Activate(set);
                }
            })));
        }

        void Activate(SegmentSet set)
        {
            current = set;
            Task<ProbeResult> mp;
            string mf = MasterFile(set);
            if (mf != null && probes.TryGetValue(mf, out mp) && mp.IsCompleted)
            {
                // onMasterLoaded: the real duration
                plan.SetActual(set.Index, mp.Result.Duration);
                clock.End = plan.Total;
            }
            clock.MasterReady = plan.Start(set.Index) + set.Offset;
            foreach (KeyValuePair<string, VideoSurface> kv in surfaces)
                if (!set.View.Targets.Any(t => t.Slot == kv.Key && t.File != null)) kv.Value.Clear();
            LoadTelemetry(set);
            Action<int> h = SegmentOpened;
            if (h != null) h(set.Index);
        }

        static string MasterFile(SegmentSet set)
        {
            SegmentTarget mt = set.View == null ? null : set.View.Targets.FirstOrDefault(x => x.Slot == set.MasterSlot);
            return mt == null ? null : mt.File;
        }

        void LoadTelemetry(SegmentSet set)
        {
            if (telemetry == null || telemetryBySegment.ContainsKey(set.Index)) return;
            SegmentTarget mt = set.View.Targets.FirstOrDefault(x => x.Slot == set.MasterSlot);
            string f = mt != null && mt.File != null ? mt.File : segments[set.Index].ProbeFile;
            if (f == null) return;
            int gen = collectionGeneration;
            int idx = set.Index;
            telemetry.Get(f, closing.Token).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion) return;
                ui.BeginInvoke(new Action(() => { if (!disposed && gen == collectionGeneration) telemetryBySegment[idx] = t.Result; }));
            });
        }

        DecodeRequest RequestFor(SegmentSet set, SegmentTarget t, double local, bool forceCpu)
        {
            Task<ProbeResult> pt;
            ProbeResult p = probes.TryGetValue(t.File, out pt) && pt.IsCompleted ? pt.Result : new ProbeResult();
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
            if (forceCpu || hwFailed.Contains(t.File)) path = DecodePath.Cpu;
            string family = FfmpegArgs.Family(p.Codec);
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
            bool cpu = old.Failed && old.PathUsed != DecodePath.Cpu;
            if (cpu) hwFailed.Add(t.File); // a clip the GPU path cannot decode: the CPU from now on
            old.Dispose();
            set.Streams.Remove(slot);
            restarts++;
            double local = set == current ? Math.Max(0, clock.Position - plan.Start(set.Index)) : 0;
            StartStream(set, t, local);
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
            foreach (SegmentTarget t in current.View.Targets)
            {
                CameraStream s;
                if (t.File == null || !current.Streams.TryGetValue(t.Slot, out s)) continue;
                DecodeRequest want = RequestFor(current, t, 0, false);
                if (want.Width != s.Request.Width || want.Height != s.Request.Height) Restart(current, t.Slot);
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

        void StartPreview()
        {
            if (!scrubbing || double.IsNaN(scrubTarget) || segments.Count == 0 || ffmpeg == null) return;
            StopPreview();
            int seg;
            double local;
            plan.Locate(scrubTarget, out seg, out local);
            SegmentView view = SegmentPlan.PlanSegment(segments[seg], MultiEnabled, slots, MasterCamera, SelectedCamera);
            double target = scrubTarget;
            List<Task<ProbeResult>> waits = new List<Task<ProbeResult>>();
            foreach (SegmentTarget t in view.Targets) if (t.File != null) waits.Add(ProbeOf(t.File));
            Task.WhenAll(waits).ContinueWith(_ => ui.BeginInvoke(new Action(() =>
            {
                if (disposed || !scrubbing || target != scrubTarget) return;
                SegmentSet set = new SegmentSet { Index = seg, View = view, MasterSlot = view.MasterSlot };
                foreach (SegmentTarget t in view.Targets)
                {
                    if (t.File == null || (FocusSlot != null && t.Slot != FocusSlot)) continue;
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
                    if (isMaster && s.Frames == 0) { Failed(set, s.Error); return; }
                    if (!isMaster && Budget(set, slot)) { Restart(set, slot); continue; }
                    continue; // the master died part way: show what it has; the clock is let go below
                }
                if (!isMaster && !s.Ended && PlaybackSync.Decide(local, s.NewestTime, false) == FollowerAction.Restart && Budget(set, slot))
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
            // the master holds the clock to its newest frame (buffering)
            if (master == null || master.Ended || master.Failed) clock.MasterReady = double.PositiveInfinity;
            else
            {
                double newest = master.NewestTime;
                double fps = master.Request.Fps > 0 ? master.Request.Fps : FfmpegArgs.DefaultFps;
                clock.MasterReady = double.IsNaN(newest) ? start + set.Offset : start + newest + 1 / fps;
            }
            // next-segment preload
            if (next == null && preloading < 0 && set.Index + 1 < plan.Count &&
                SegmentPlan.ShouldStartPreload(local, plan.Duration(set.Index), clock.Rate, SegmentPlan.PreloadLeadSeconds, SegmentPlan.PreloadSettleSeconds))
            {
                preloading = set.Index + 1;
                Prepare(preloading, 0, generation, true);
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
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            closing.Cancel();
            if (hooked) CompositionTarget.Rendering -= OnRendering;
            hooked = false;
            resizeTimer.Stop();
            scrubTimer.Stop();
            hiddenTimer.Stop();
            DisposeSets();
            StopPreview();
            foreach (VideoSurface s in surfaces.Values) s.PixelSizeChanged -= OnResize;
        }
    }
}
