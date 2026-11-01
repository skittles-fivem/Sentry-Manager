using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SentrySix.Core;

namespace SentrySix.Ui.Playback
{
    /// <summary>
    /// SentrySix.exe --playback-check &lt;folder&gt; [--seconds 20] [--out report.json]
    /// [--ffmpeg path] [--ffprobe path]: the Viewer's playback engine on a
    /// folder of Tesla clips with no server and no page. It finds the folder's
    /// event (a folder with event.json, else the one with the most cameras),
    /// probes this PC's decode paths as the Viewer's first run does, serves the
    /// clips to the decoders through the <see cref="MediaRelay"/> from the
    /// disk (read only), plays every camera from 8 s before a segment
    /// boundary for the time asked in a window of six tiles, and reports the
    /// decode path of each camera, the frames each decoded and showed per
    /// second, the largest time apart of the cameras' shown frames, the
    /// buffering, this app's and its decoders' CPU, the GPU's use
    /// (nvidia-smi when there is one) and whether any ffmpeg outlived the
    /// player. Exit code 0 when every camera showed frames, nothing failed
    /// and no ffmpeg was left; 1 otherwise; 2 when there was nothing to play.
    /// </summary>
    public static class PlaybackCheck
    {
        static readonly Regex ClipName = new Regex(@"^(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})-(.+)\.mp4$", RegexOptions.IgnoreCase);

        public static int Run(string folder, double seconds, string outFile, string ffmpegPath, string ffprobePath)
        {
            StringBuilder log = new StringBuilder();
            Action<string> say = s => { Console.WriteLine(s); log.AppendLine(s); };
            Dictionary<string, object> report = new Dictionary<string, object>();
            report["folder"] = folder;
            int code = 2;
            try
            {
                code = Check(folder, seconds, ffmpegPath, ffprobePath, say, report);
            }
            catch (Exception ex)
            {
                say("playback check failed: " + ex);
                report["error"] = ex.Message;
                code = 1;
            }
            report["exitCode"] = code;
            report["log"] = log.ToString();
            if (!string.IsNullOrEmpty(outFile))
            {
                try
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(outFile, ToJson(report).ToJson(), new UTF8Encoding(false));
                }
                catch (Exception ex) { Console.WriteLine("could not write " + outFile + ": " + ex.Message); }
            }
            return code;
        }

        // ------------------------------------------------------------ the clips

        sealed class Found
        {
            public string Dir;
            public bool Event;
            public List<PlaybackSegment> Segments = new List<PlaybackSegment>();
            public int Cameras;
        }

        static Found Pick(string folder)
        {
            Dictionary<string, SortedDictionary<string, PlaybackSegment>> byDir = new Dictionary<string, SortedDictionary<string, PlaybackSegment>>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Directory.EnumerateFiles(folder, "*.mp4", SearchOption.AllDirectories))
            {
                Match m = ClipName.Match(Path.GetFileName(f));
                if (!m.Success) continue;
                // a cloud-only OneDrive file would be downloaded by the read: left out
                try { if (((int)File.GetAttributes(f) & 0x400000) != 0) continue; }
                catch (Exception) { continue; }
                string dir = Path.GetDirectoryName(f);
                SortedDictionary<string, PlaybackSegment> groups;
                if (!byDir.TryGetValue(dir, out groups)) byDir[dir] = groups = new SortedDictionary<string, PlaybackSegment>(StringComparer.Ordinal);
                PlaybackSegment seg;
                string key = m.Groups[1].Value;
                if (!groups.TryGetValue(key, out seg)) groups[key] = seg = new PlaybackSegment { TimestampKey = key };
                seg.FilesByCamera[m.Groups[2].Value.ToLowerInvariant()] = f;
            }
            Found best = null;
            foreach (KeyValuePair<string, SortedDictionary<string, PlaybackSegment>> kv in byDir)
            {
                Found c = new Found { Dir = kv.Key, Event = File.Exists(Path.Combine(kv.Key, "event.json")) };
                c.Segments = kv.Value.Values.ToList();
                c.Cameras = c.Segments.Max(s => s.FilesByCamera.Count);
                if (best == null || (c.Event && !best.Event) || (c.Event == best.Event && (c.Cameras > best.Cameras || (c.Cameras == best.Cameras && c.Segments.Count > best.Segments.Count))))
                    best = c;
            }
            return best;
        }

        /// <summary>/api/media for the relay, from the disk: GET and HEAD with Range, 206 / 200 / 416, read only.</summary>
        static Task<HttpResponseMessage> OpenLocal(string path, string range, bool head, CancellationToken ct)
        {
            HttpResponseMessage res;
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent(new byte[0]) });
            long size = fi.Length, from = 0, to = size - 1;
            bool partial = false;
            if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                string r = range.Substring(6).Split(',')[0].Trim();
                int dash = r.IndexOf('-');
                long a, b;
                if (dash == 0 && long.TryParse(r.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out b)) { from = Math.Max(0, size - b); partial = true; }
                else if (dash > 0 && long.TryParse(r.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out a))
                {
                    from = a;
                    if (dash + 1 < r.Length && long.TryParse(r.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out b)) to = Math.Min(b, size - 1);
                    partial = true;
                }
                if (partial && from >= size)
                {
                    res = new HttpResponseMessage((HttpStatusCode)416) { Content = new ByteArrayContent(new byte[0]) };
                    res.Content.Headers.ContentRange = new ContentRangeHeaderValue(size);
                    return Task.FromResult(res);
                }
            }
            long length = Math.Max(0, to - from + 1);
            HttpContent content;
            if (head) content = new ByteArrayContent(new byte[0]);
            else
            {
                FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
                fs.Seek(from, SeekOrigin.Begin);
                content = new StreamContent(new Limited(fs, length), 64 * 1024);
            }
            res = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content };
            content.Headers.ContentLength = length;
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            if (partial) content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, size);
            res.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(res);
        }

        /// <summary>A stream that ends after a number of bytes (the range's end).</summary>
        sealed class Limited : Stream
        {
            readonly Stream inner;
            long left;
            public Limited(Stream inner, long length) { this.inner = inner; left = length; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (left <= 0) return 0;
                int n = inner.Read(buffer, offset, (int)Math.Min(count, left));
                left -= n;
                return n;
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if (left <= 0) return Task.FromResult(0);
                return inner.ReadAsync(buffer, offset, (int)Math.Min(count, left), ct).ContinueWith(t => { left -= t.Result; return t.Result; }, ct);
            }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }

        // ------------------------------------------------------------ the run

        sealed class Cam
        {
            public long Decoded, Shown;
            public CameraStream Last;
            public long LastFrames, LastShows;
            public string Path = "";
            public string Size = "";
            public int Streams;
            public bool Failed;
        }

        static int Check(string folder, double seconds, string ffmpegPath, string ffprobePath, Action<string> say, Dictionary<string, object> report)
        {
            if (!Directory.Exists(folder)) { say("no such folder: " + folder); return 2; }
            string ff = ffmpegPath ?? FfmpegTools.Ffmpeg, fp = ffprobePath ?? FfmpegTools.Ffprobe;
            if (ff == null || fp == null) { say("no ffmpeg / ffprobe (--ffmpeg, --ffprobe, next to the app or on PATH)"); return 2; }
            Found pick = Pick(folder);
            if (pick == null || pick.Segments.Count == 0) { say("no Tesla clips (YYYY-MM-DD_HH-MM-SS-camera.mp4) under " + folder); return 2; }
            List<string> cameras = pick.Segments.SelectMany(s => s.FilesByCamera.Keys).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
            say("clips: " + pick.Dir + (pick.Event ? " (event.json)" : "") + ", " + pick.Segments.Count + " segments, cameras " + string.Join(", ", cameras));
            report["clips"] = pick.Dir;
            report["event"] = pick.Event;
            report["segments"] = pick.Segments.Count;
            report["cameras"] = cameras;

            // the kill-on-close job first, then every process the check starts is counted
            PlaybackJob.Install();
            List<Process> started = new List<Process>();
            Action<Process> job = FfmpegTools.Started;
            FfmpegTools.Started = p => { if (job != null) job(p); lock (started) started.Add(p); };

            Stopwatch probeTime = Stopwatch.StartNew();
            List<string> probeLog = new List<string>();
            DecodePaths paths = DecodePaths.Probe(ff, Path.Combine(Path.GetTempPath(), "sentry-playback-check"), l => { probeLog.Add(l); say("probe: " + l); });
            say("decode paths: " + paths.Save() + " (" + probeTime.ElapsedMilliseconds + " ms)");
            report["decodePaths"] = paths.Save();
            report["probe"] = probeLog;

            // eight seconds before the first boundary, so the run crosses into the next segment
            double start = 0;
            int startSeg = 0;
            if (pick.Segments.Count > 1)
            {
                ProbeResult first = FfmpegTools.ProbeClip(fp, pick.Segments[0].ProbeFile, 20000);
                double d = double.IsNaN(first.Duration) || first.Duration <= 0 ? 60 : first.Duration;
                start = Math.Max(0, d - 8);
            }
            report["startSec"] = start;
            say("start: " + start.ToString("0.0", CultureInfo.InvariantCulture) + " s into segment " + startSeg);

            Application app = Application.Current ?? new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Window w = new Window();
            w.Title = "Sentry Six playback check";
            w.Width = 1280;
            w.Height = 720;
            w.WindowStyle = WindowStyle.None;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = 0;
            w.Top = 0;
            UniformGrid tiles = new UniformGrid();
            tiles.Columns = cameras.Count > 4 ? 3 : 2;
            w.Content = tiles;
            Dictionary<string, VideoSurface> surfaces = new Dictionary<string, VideoSurface>(StringComparer.Ordinal);
            foreach (string c in cameras)
            {
                VideoSurface s = new VideoSurface();
                surfaces[c] = s;
                tiles.Children.Add(s);
            }
            w.Show();

            MediaRelay relay = new MediaRelay(OpenLocal);
            Player player = new Player(relay, ff, fp, paths);
            player.MasterCamera = "front";
            player.SetLayout(cameras.Select(c => new LayoutSlot(c, c)));
            foreach (KeyValuePair<string, VideoSurface> kv in surfaces) player.Attach(kv.Key, kv.Value);

            Dictionary<string, Cam> cams = cameras.ToDictionary(c => c, c => new Cam(), StringComparer.Ordinal);
            List<string> events = new List<string>();
            List<double> skews = new List<double>();
            double maxSkew = 0;
            string maxSkewAt = "";
            long ticks = 0, bufferingTicks = 0;
            int opened = 0;
            Stopwatch run = new Stopwatch();
            Stopwatch wall = Stopwatch.StartNew();
            double firstFrameMs = double.NaN;
            List<double> gpu = new List<double>(), dec = new List<double>();
            Dictionary<int, TimeSpan> cpuOf = new Dictionary<int, TimeSpan>();
            TimeSpan selfCpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            int segAtEnd = -1;
            double posAtEnd = 0;

            player.SegmentOpened += i =>
            {
                opened++;
                events.Add(wall.ElapsedMilliseconds + " ms: segment " + i + " playing");
                if (!run.IsRunning) run.Start();
            };
            player.SegmentFailed += (i, why) => events.Add(wall.ElapsedMilliseconds + " ms: segment " + i + " failed: " + why);
            player.PreloadStarted += i => events.Add(wall.ElapsedMilliseconds + " ms: segment " + i + " preloading");
            player.DecodePathFailed += f => events.Add(wall.ElapsedMilliseconds + " ms: the GPU path of " + f + " failed: the CPU from now on");
            player.Tick += () =>
            {
                if (!run.IsRunning) return;
                ticks++;
                if (player.Buffering) bufferingTicks++;
                if (double.IsNaN(firstFrameMs) && surfaces.Values.Any(s => s.Shows > 0)) firstFrameMs = wall.ElapsedMilliseconds;
                // the cameras' shown frames, all of this segment's: how far apart in clip time
                List<double> times = new List<double>();
                foreach (KeyValuePair<string, CameraStream> kv in player.CurrentStreams)
                {
                    VideoSurface s;
                    if (!surfaces.TryGetValue(kv.Key, out s) || double.IsNaN(s.ShownTime) || kv.Value.Frames == 0) continue;
                    times.Add(s.ShownTime);
                }
                if (times.Count >= 2 && run.Elapsed.TotalSeconds > 2)
                {
                    double skew = times.Max() - times.Min();
                    skews.Add(skew);
                    if (skew > maxSkew) { maxSkew = skew; maxSkewAt = run.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"; }
                }
            };

            // once a second: frames per camera, CPU of the decoders
            DispatcherTimer sample = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
            sample.Tick += delegate
            {
                foreach (KeyValuePair<string, CameraStream> kv in player.CurrentStreams)
                {
                    Cam c;
                    if (!cams.TryGetValue(kv.Key, out c)) continue;
                    if (c.Last != kv.Value) { c.Last = kv.Value; c.LastFrames = 0; c.Streams++; }
                    long f = kv.Value.Frames;
                    c.Decoded += Math.Max(0, f - c.LastFrames);
                    c.LastFrames = f;
                    c.Path = kv.Value.PathUsed.ToString();
                    c.Size = kv.Value.Request.Width + "x" + kv.Value.Request.Height;
                    if (kv.Value.Failed) c.Failed = true;
                }
                foreach (KeyValuePair<string, VideoSurface> kv in surfaces)
                {
                    Cam c = cams[kv.Key];
                    c.Shown += kv.Value.Shows - c.LastShows;
                    c.LastShows = kv.Value.Shows;
                }
                lock (started)
                {
                    foreach (Process p in started)
                    {
                        try { if (!p.HasExited) cpuOf[p.Id] = p.TotalProcessorTime; }
                        catch (Exception) { }
                    }
                }
            };
            sample.Start();

            // the GPU's use while it plays (nvidia-smi, every 2 s)
            bool gpuDone = false;
            string smi = FindSmi();
            Thread gpuThread = null;
            if (smi != null)
            {
                gpuThread = new Thread(() =>
                {
                    while (!Volatile.Read(ref gpuDone))
                    {
                        try
                        {
                            string o, e;
                            if (FfmpegTools.Run(smi, new[] { "--query-gpu=utilization.gpu,utilization.decoder", "--format=csv,noheader,nounits" }, 5000, out o, out e) == 0)
                            {
                                string[] parts = o.Split('\n')[0].Split(',');
                                double g, d;
                                if (parts.Length >= 2 && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out g) && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                                    lock (gpu) { gpu.Add(g); dec.Add(d); }
                            }
                        }
                        catch (Exception) { }
                        Thread.Sleep(2000);
                    }
                }) { IsBackground = true };
            }

            Stopwatch selfWall = Stopwatch.StartNew();
            DispatcherTimer stop = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(200) };
            DispatcherFrame frame = new DispatcherFrame();
            stop.Tick += delegate
            {
                // the measured time runs from the first segment playing; 60 s at most waiting for it
                bool done = (run.IsRunning && run.Elapsed.TotalSeconds >= seconds) || wall.Elapsed.TotalSeconds > seconds + 60;
                if (!done) return;
                stop.Stop();
                frame.Continue = false;
            };
            player.Open(pick.Segments, 60, start);
            player.Play();
            if (gpuThread != null) { lock (started) { } gpuThread.Start(); }
            stop.Start();
            Dispatcher.PushFrame(frame);
            sample.Stop();
            Volatile.Write(ref gpuDone, true);
            double measured = run.Elapsed.TotalSeconds;
            segAtEnd = player.SegmentIndex;
            posAtEnd = player.Position;
            string describe = player.Describe();
            long shownAll = player.FramesShown, droppedAll = player.FramesDropped, restartsAll = player.Restarts;
            TimeSpan selfCpu = Process.GetCurrentProcess().TotalProcessorTime - selfCpu0;
            double selfSecs = selfWall.Elapsed.TotalSeconds;

            // the player let go: every decoder must be gone
            player.Dispose();
            relay.Dispose();
            Stopwatch gone = Stopwatch.StartNew();
            List<Process> mine;
            lock (started) mine = started.ToList();
            List<string> left = new List<string>();
            while (gone.ElapsedMilliseconds < 5000)
            {
                left.Clear();
                foreach (Process p in mine)
                {
                    try { if (!p.HasExited) left.Add(p.Id.ToString(CultureInfo.InvariantCulture)); }
                    catch (Exception) { }
                }
                if (left.Count == 0) break;
                Thread.Sleep(100);
            }
            int ffmpegRunning = 0;
            try { ffmpegRunning = Process.GetProcessesByName("ffmpeg").Length + Process.GetProcessesByName("ffprobe").Length; }
            catch (Exception) { }
            w.Close();

            // ---- the report
            double decoderCpu = cpuOf.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b).TotalSeconds;
            int cores = Environment.ProcessorCount;
            double appPct = selfSecs > 0 ? 100 * selfCpu.TotalSeconds / (selfSecs * cores) : 0;
            double decPct = selfSecs > 0 ? 100 * decoderCpu / (selfSecs * cores) : 0;
            say("measured: " + measured.ToString("0.0", CultureInfo.InvariantCulture) + " s from the first segment playing (first frame " + (double.IsNaN(firstFrameMs) ? "never" : firstFrameMs.ToString("0", CultureInfo.InvariantCulture) + " ms") + " after Open); now segment " + segAtEnd + " at " + posAtEnd.ToString("0.00", CultureInfo.InvariantCulture) + " s");
            List<object> camReport = new List<object>();
            bool allShown = true, anyFailed = false;
            foreach (string c in cameras)
            {
                Cam k = cams[c];
                double dfps = measured > 0 ? k.Decoded / measured : 0, sfps = measured > 0 ? k.Shown / measured : 0;
                say(string.Format(CultureInfo.InvariantCulture, "  {0,-16} {1,-9} {2,-9} decoded {3,5:0.0} fps, shown {4,5:0.0} fps, decoders {5}{6}", c, k.Path, k.Size, dfps, sfps, k.Streams, k.Failed ? ", FAILED" : ""));
                if (k.Shown == 0) allShown = false;
                if (k.Failed) anyFailed = true;
                camReport.Add(new Dictionary<string, object> { { "camera", c }, { "path", k.Path }, { "size", k.Size }, { "decodedFps", Math.Round(dfps, 2) }, { "shownFps", Math.Round(sfps, 2) }, { "decoders", k.Streams }, { "failed", k.Failed } });
            }
            skews.Sort();
            double p95 = skews.Count == 0 ? double.NaN : skews[Math.Min(skews.Count - 1, (int)(skews.Count * 0.95))];
            double med = skews.Count == 0 ? double.NaN : skews[skews.Count / 2];
            say(string.Format(CultureInfo.InvariantCulture, "skew between cameras (shown frames' clip times, after the first 2 s): max {0:0.000} s at {1}, p95 {2:0.000} s, median {3:0.000} s over {4} ticks", maxSkew, maxSkewAt, p95, med, skews.Count));
            say(string.Format(CultureInfo.InvariantCulture, "render ticks {0}, buffering {1} ({2:0.0}%); shown {3}, dropped {4}, restarts {5}", ticks, bufferingTicks, ticks > 0 ? 100.0 * bufferingTicks / ticks : 0, shownAll, droppedAll, restartsAll));
            say(string.Format(CultureInfo.InvariantCulture, "CPU ({0} logical processors): the app {1:0.0}%, its decoders {2:0.0}% of the whole PC", cores, appPct, decPct));
            if (gpu.Count > 0) say(string.Format(CultureInfo.InvariantCulture, "GPU (nvidia-smi, {0} samples): {1:0} % average, {2:0} % max; decoder {3:0} % average, {4:0} % max", gpu.Count, gpu.Average(), gpu.Max(), dec.Average(), dec.Max()));
            else say("GPU: no nvidia-smi samples");
            say("player: " + describe);
            foreach (string e in events) say("  " + e);
            say("ffmpeg processes started: " + mine.Count + "; still running after the player was disposed: " + (left.Count == 0 ? "none" : string.Join(", ", left)) + "; ffmpeg/ffprobe on the PC now: " + ffmpegRunning);

            report["measuredSec"] = Math.Round(measured, 2);
            report["firstFrameMs"] = double.IsNaN(firstFrameMs) ? (object)null : firstFrameMs;
            report["camerasReport"] = camReport;
            report["skewMaxSec"] = Math.Round(maxSkew, 4);
            report["skewP95Sec"] = double.IsNaN(p95) ? (object)null : Math.Round(p95, 4);
            report["skewMedianSec"] = double.IsNaN(med) ? (object)null : Math.Round(med, 4);
            report["renderTicks"] = ticks;
            report["bufferingTicks"] = bufferingTicks;
            report["framesShown"] = shownAll;
            report["framesDropped"] = droppedAll;
            report["restarts"] = restartsAll;
            report["segmentsOpened"] = opened;
            report["cpuAppPct"] = Math.Round(appPct, 1);
            report["cpuDecodersPct"] = Math.Round(decPct, 1);
            report["gpuAvgPct"] = gpu.Count > 0 ? (object)Math.Round(gpu.Average(), 1) : null;
            report["gpuMaxPct"] = gpu.Count > 0 ? (object)gpu.Max() : null;
            report["gpuDecoderAvgPct"] = dec.Count > 0 ? (object)Math.Round(dec.Average(), 1) : null;
            report["gpuDecoderMaxPct"] = dec.Count > 0 ? (object)dec.Max() : null;
            report["events"] = events;
            report["ffmpegStarted"] = mine.Count;
            report["ffmpegLeft"] = left;
            report["ffmpegRunningAfter"] = ffmpegRunning;
            bool ok = allShown && !anyFailed && left.Count == 0 && opened > 0;
            report["ok"] = ok;
            say(ok ? "playback check: OK" : "playback check: FAILED");
            return ok ? 0 : 1;
        }

        static string FindSmi()
        {
            foreach (string p in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"), @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe" })
                if (File.Exists(p)) return p;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string p = Path.Combine(dir.Trim('"'), "nvidia-smi.exe");
                    if (dir.Length > 0 && File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        static JsonValue ToJson(object o)
        {
            if (o == null) return JsonValue.Null;
            if (o is JsonValue) return (JsonValue)o;
            if (o is string) return JsonValue.String((string)o);
            if (o is bool) return JsonValue.From((bool)o);
            if (o is int) return JsonValue.Number((int)o);
            if (o is long) return JsonValue.Number((long)o);
            if (o is double) return JsonValue.Number((double)o);
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null)
            {
                JsonValue j = JsonValue.Object();
                foreach (KeyValuePair<string, object> kv in d) j = j.With(kv.Key, ToJson(kv.Value));
                return j;
            }
            System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
            if (e != null)
            {
                List<JsonValue> l = new List<JsonValue>();
                foreach (object x in e) l.Add(ToJson(x));
                return JsonValue.Array(l);
            }
            return JsonValue.String(o.ToString());
        }
    }
}
