using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SentrySix.Core
{
    /// <summary>A decoded picture: bgr0 bytes (Bgr32), and the time into its clip it shows at.</summary>
    public sealed class VideoFrame
    {
        public readonly byte[] Data;
        public readonly int Width;
        public readonly int Height;
        public double Time;
        public long Index;

        public VideoFrame(int w, int h) : this(w, h, new byte[w * h * 4]) { }

        VideoFrame(int w, int h, byte[] data)
        {
            Width = w;
            Height = h;
            Data = data;
        }

        /// <summary>A frame on a buffer from the <see cref="FrameBuffers"/> pool (give it back with FrameBuffers.Return).</summary>
        public static VideoFrame Pooled(int w, int h)
        {
            return new VideoFrame(w, h, FrameBuffers.Rent(w * h * 4));
        }

        public int Stride { get { return Width * 4; } }
    }

    /// <summary>
    /// The frame buffers of decoders that stopped, kept by size for the next
    /// ones: a tile frame is over 85 KB (the Large Object Heap), and every
    /// segment, preload, restart and scrub preview would otherwise allocate
    /// its own and drop the old ones together at the segment boundary, where
    /// a gen2 collection shows as a hitch. Bounded by <see cref="MaxBytes"/>.
    /// Thread safe.
    /// </summary>
    public static class FrameBuffers
    {
        /// <summary>At most this many bytes are kept for reuse (about two sets of six 1080p tiles' rings).</summary>
        public const long MaxBytes = 192L * 1024 * 1024;

        static readonly object gate = new object();
        static readonly Dictionary<int, Stack<byte[]>> bySize = new Dictionary<int, Stack<byte[]>>();
        static long kept;

        public static long KeptBytes { get { lock (gate) return kept; } }

        public static byte[] Rent(int size)
        {
            lock (gate)
            {
                Stack<byte[]> st;
                if (bySize.TryGetValue(size, out st) && st.Count > 0)
                {
                    kept -= size;
                    return st.Pop();
                }
            }
            return new byte[size];
        }

        public static void Return(byte[] buf)
        {
            if (buf == null) return;
            lock (gate)
            {
                if (kept + buf.Length > MaxBytes) return;
                Stack<byte[]> st;
                if (!bySize.TryGetValue(buf.Length, out st)) bySize[buf.Length] = st = new Stack<byte[]>();
                st.Push(buf);
                kept += buf.Length;
            }
        }

        /// <summary>Lets every kept buffer go (tests; the app on low memory).</summary>
        public static void Clear()
        {
            lock (gate) { bySize.Clear(); kept = 0; }
        }
    }

    /// <summary>
    /// The few frames a camera holds: a fixed set of buffers, filled by the
    /// reader thread in order and taken by the render tick. When every buffer
    /// is full the reader waits, so ffmpeg blocks on its pipe: it never runs
    /// far ahead and memory stays bounded. Thread safe.
    /// </summary>
    public sealed class FrameRing
    {
        readonly object gate = new object();
        readonly Stack<VideoFrame> free = new Stack<VideoFrame>();
        readonly LinkedList<VideoFrame> ready = new LinkedList<VideoFrame>();
        readonly List<VideoFrame> all = new List<VideoFrame>();
        readonly int capacity;
        bool closed, recycled;

        public FrameRing(int capacity, int width, int height) : this(capacity, width, height, false) { }

        /// <param name="pooled">The buffers come from <see cref="FrameBuffers"/> and go back on <see cref="Recycle"/>.</param>
        public FrameRing(int capacity, int width, int height, bool pooled)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            this.capacity = capacity;
            Width = width;
            Height = height;
            for (int i = 0; i < capacity; i++)
            {
                VideoFrame f = pooled ? VideoFrame.Pooled(width, height) : new VideoFrame(width, height);
                all.Add(f);
                free.Push(f);
            }
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Capacity { get { return capacity; } }

        /// <summary>Frames decoded and not yet taken.</summary>
        public int Count { get { lock (gate) return ready.Count; } }

        /// <summary>The newest decoded frame's time, NaN when none is waiting.</summary>
        public double NewestTime { get { lock (gate) return ready.Count == 0 ? double.NaN : ready.Last.Value.Time; } }

        /// <summary>The oldest waiting frame's time, NaN when none is waiting.</summary>
        public double OldestTime { get { lock (gate) return ready.Count == 0 ? double.NaN : ready.First.Value.Time; } }

        /// <summary>A free buffer for the reader; waits while all are full; null once closed.</summary>
        public VideoFrame Rent()
        {
            lock (gate)
            {
                while (free.Count == 0 && !closed) Monitor.Wait(gate);
                return closed ? null : free.Pop();
            }
        }

        /// <summary>A filled buffer, in decode order.</summary>
        public void Add(VideoFrame f)
        {
            lock (gate)
            {
                if (closed) { free.Push(f); return; }
                ready.AddLast(f);
                Monitor.PulseAll(gate);
            }
        }

        /// <summary>A buffer handed back (by the reader that could not fill it, or the renderer after its copy).</summary>
        public void Return(VideoFrame f)
        {
            if (f == null) return;
            lock (gate)
            {
                free.Push(f);
                Monitor.PulseAll(gate);
            }
        }

        /// <summary>
        /// The newest frame at or before <paramref name="time"/> (taken: give it
        /// back with Return after copying it); older ones are freed. Null when
        /// none is due yet.
        /// </summary>
        public VideoFrame Take(double time)
        {
            lock (gate)
            {
                VideoFrame pick = null;
                while (ready.Count > 0 && ready.First.Value.Time <= time + 1e-6)
                {
                    if (pick != null) free.Push(pick);
                    pick = ready.First.Value;
                    ready.RemoveFirst();
                }
                if (pick != null) Monitor.PulseAll(gate);
                return pick;
            }
        }

        /// <summary>Frees every waiting frame older than <paramref name="time"/> (a late follower catching up); how many.</summary>
        public int DropBefore(double time)
        {
            lock (gate)
            {
                int n = 0;
                while (ready.Count > 1 && ready.First.Next.Value.Time <= time + 1e-6)
                {
                    free.Push(ready.First.Value);
                    ready.RemoveFirst();
                    n++;
                }
                if (n > 0) Monitor.PulseAll(gate);
                return n;
            }
        }

        /// <summary>Wakes and stops the reader; Rent returns null from now on.</summary>
        public void Close()
        {
            lock (gate)
            {
                closed = true;
                while (ready.Count > 0) { free.Push(ready.First.Value); ready.RemoveFirst(); }
                Monitor.PulseAll(gate);
            }
        }

        public bool Closed { get { lock (gate) return closed; } }

        /// <summary>
        /// Closed, with its reader stopped: the buffers go back to the pool
        /// once, and only those that are free again (a frame the renderer
        /// still holds is left to the collector).
        /// </summary>
        public void Recycle()
        {
            List<VideoFrame> back;
            lock (gate)
            {
                if (!closed || recycled) return;
                recycled = true;
                back = new List<VideoFrame>(free);
                free.Clear();
            }
            foreach (VideoFrame f in back) FrameBuffers.Return(f.Data);
        }
    }

    /// <summary>Where the ffmpeg of the package is: ffmpeg\ next to app-ui\, else on PATH (a developer's PC).</summary>
    public static class FfmpegTools
    {
        public static string Find(string exeName)
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string dir in new[] { Path.Combine(here, "..", "ffmpeg"), Path.Combine(here, "ffmpeg"), here })
            {
                string p = Path.GetFullPath(Path.Combine(dir, exeName));
                if (File.Exists(p)) return p;
            }
            string bare = Path.GetFileNameWithoutExtension(exeName);
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (dir.Length == 0) continue;
                foreach (string name in new[] { exeName, bare })
                {
                    try
                    {
                        string p = Path.Combine(dir.Trim('"'), name);
                        if (File.Exists(p)) return p;
                    }
                    catch (ArgumentException) { }
                }
            }
            return null;
        }

        public static string Ffmpeg { get { return Find("ffmpeg.exe"); } }
        public static string Ffprobe { get { return Find("ffprobe.exe"); } }

        /// <summary>
        /// Called with every process playback starts, before it runs long (the
        /// app puts it in its kill-on-close Job Object, so no ffmpeg outlives
        /// the app).
        /// </summary>
        public static Action<Process> Started;

        /// <summary>A hidden process with its output read: UseShellExecute off, no window, stdin closed.</summary>
        public static Process Start(string exe, IEnumerable<string> args, bool binaryStdout)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, FfmpegArgs.CommandLine(args));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            if (!binaryStdout) psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            Process p = Process.Start(psi);
            try { p.StandardInput.Close(); } catch (Exception) { }
            Action<Process> h = Started;
            if (h != null) { try { h(p); } catch (Exception) { } }
            return p;
        }

        /// <summary>ffprobe of a clip (through the relay): duration, rate, codec, size; a failed probe has NaN duration.</summary>
        public static ProbeResult ProbeClip(string ffprobe, string url, int timeoutMs)
        {
            string error;
            return ProbeClip(ffprobe, url, timeoutMs, out error);
        }

        /// <summary>As above, with what went wrong (ffprobe's exit code and errors, or its output when it could not be read).</summary>
        public static ProbeResult ProbeClip(string ffprobe, string url, int timeoutMs, out string error)
        {
            string o, e;
            error = null;
            try
            {
                int rc = Run(ffprobe, FfmpegArgs.Probe(url), timeoutMs, out o, out e);
                if (rc != 0) { error = "ffprobe exit code " + rc + ": " + (e ?? "").Trim(); return new ProbeResult(); }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new ProbeResult();
            }
            ProbeResult p = FfmpegArgs.ParseProbe(o);
            if (p.Codec == null) error = "ffprobe said: " + (o ?? "").Trim() + " / " + (e ?? "").Trim();
            return p;
        }

        /// <summary>Runs a tool to its end: exit code, standard output and error (killed past the limit: -1).</summary>
        public static int Run(string exe, IEnumerable<string> args, int timeoutMs, out string stdout, out string stderr)
        {
            using (Process p = Start(exe, args, false))
            {
                StringBuilder err = new StringBuilder();
                Thread t = new Thread(() => { try { err.Append(p.StandardError.ReadToEnd()); } catch (Exception) { } });
                t.IsBackground = true;
                t.Start();
                System.Threading.Tasks.Task<string> output = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (Exception) { }
                    p.WaitForExit(5000);
                    stdout = "";
                    t.Join(2000);
                    stderr = err.ToString();
                    return -1;
                }
                output.Wait(5000);
                stdout = output.IsCompleted ? output.Result : "";
                t.Join(5000);
                stderr = err.ToString();
                return p.ExitCode;
            }
        }
    }

    /// <summary>
    /// One camera's decoder for one segment: an ffmpeg process writing raw
    /// bgr0 frames at a constant rate to its stdout, a reader thread reading
    /// exactly width x height x 4 bytes per frame into the camera's
    /// <see cref="FrameRing"/>, and a thread reading stderr (its errors).
    /// Frame k shows at Offset + k / Fps into the clip.
    /// </summary>
    public sealed class CameraStream : IDisposable
    {
        readonly string ffmpeg;
        readonly DecodeRequest request;
        readonly FrameRing ring;
        readonly StringBuilder errors = new StringBuilder();
        Process process;
        Thread reader, errReader;
        volatile bool stopped;
        volatile bool ended;
        volatile bool failed;
        long frames;
        double newestTime = double.NaN;

        public CameraStream(string ffmpegPath, DecodeRequest request, int ringFrames)
        {
            if (ffmpegPath == null) throw new ArgumentNullException("ffmpegPath");
            ffmpeg = ffmpegPath;
            this.request = request;
            ring = new FrameRing(ringFrames, request.Width, request.Height, true);
        }

        public DecodeRequest Request { get { return request; } }
        public FrameRing Ring { get { return ring; } }
        public DecodePath PathUsed { get { return request.Path; } }

        /// <summary>The clip ended (ffmpeg exited cleanly after its last frame).</summary>
        public bool Ended { get { return ended; } }

        /// <summary>ffmpeg failed (exit code not 0, or it wrote a part of a frame): <see cref="Error"/> says why.</summary>
        public bool Failed { get { return failed; } }

        public string Error { get { lock (errors) return errors.ToString().Trim(); } }

        /// <summary>Frames read so far.</summary>
        public long Frames { get { return Interlocked.Read(ref frames); } }

        /// <summary>The newest decoded frame's time (also once taken), NaN before the first.</summary>
        public double NewestTime { get { lock (errors) return newestTime; } }

        /// <summary>Raised on the reader thread once the clip ended or failed.</summary>
        public event Action<CameraStream> Finished;

        public void Start()
        {
            Process p = FfmpegTools.Start(ffmpeg, FfmpegArgs.Decode(request), true);
            process = p;
            // the pipes are taken here, on the caller's thread: a Dispose right after Start
            // (a scrub preview replaced, a quick seek) closes the process before the
            // threads run, and Process.StandardOutput would throw on them then
            Stream outStream = p.StandardOutput.BaseStream;
            StreamReader errStream = p.StandardError;
            errReader = new Thread(() => ReadErrors(errStream)) { IsBackground = true, Name = "ffmpeg stderr" };
            errReader.Start();
            reader = new Thread(() => ReadFrames(p, outStream)) { IsBackground = true, Name = "ffmpeg frames" };
            reader.Start();
        }

        void ReadErrors(StreamReader err)
        {
            try
            {
                string line;
                while ((line = err.ReadLine()) != null)
                {
                    lock (errors)
                    {
                        if (errors.Length < 8000) errors.AppendLine(line);
                    }
                }
            }
            catch (Exception) { }
        }

        // nothing in here may throw out: an exception on a background thread ends the app
        void ReadFrames(Process proc, Stream s)
        {
            try
            {
                ReadLoop(proc, s);
            }
            catch (Exception ex)
            {
                if (!stopped) { failed = true; try { lock (errors) errors.AppendLine(ex.Message); } catch (Exception) { } }
            }
            finally
            {
                if (stopped) ring.Recycle();
            }
            if (stopped) return;
            try
            {
                Action<CameraStream> h = Finished;
                if (h != null) h(this);
            }
            catch (Exception) { }
        }

        void ReadLoop(Process proc, Stream s)
        {
            double fps = request.Fps > 0 ? request.Fps : FfmpegArgs.DefaultFps;
            {
                while (!stopped)
                {
                    VideoFrame f = ring.Rent();
                    if (f == null) return;
                    int got = ReadFully(s, f.Data);
                    if (got < f.Data.Length)
                    {
                        ring.Return(f);
                        if (stopped) return;
                        proc.WaitForExit(5000);
                        bool clean = got == 0 && proc.HasExited && proc.ExitCode == 0;
                        if (clean) ended = true;
                        else
                        {
                            failed = true;
                            if (got > 0) lock (errors) errors.AppendLine("ffmpeg stopped in the middle of a frame.");
                        }
                        break;
                    }
                    long k = Interlocked.Increment(ref frames) - 1;
                    f.Index = k;
                    f.Time = request.Offset + k / fps;
                    lock (errors) newestTime = f.Time;
                    ring.Add(f);
                }
            }
        }

        static int ReadFully(Stream s, byte[] buf)
        {
            int got = 0;
            while (got < buf.Length)
            {
                int n = s.Read(buf, got, buf.Length - got);
                if (n <= 0) break;
                got += n;
            }
            return got;
        }

        /// <summary>Kills the decoder and wakes its reader (a seek, the next segment, the page closed).</summary>
        public void Dispose()
        {
            if (stopped) return;
            stopped = true;
            ring.Close();
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch (Exception) { }
                try { process.Dispose(); } catch (Exception) { }
            }
            // never started, or its reader already gone: the buffers go back now (else the reader returns them)
            if (reader == null || !reader.IsAlive) ring.Recycle();
        }
    }

    /// <summary>
    /// The decode path for each codec on this PC, picked once and remembered
    /// (the app's prefs): ffmpeg -hwaccels and -filters, then a one-second
    /// test decode of a sample of each codec on each path; the fastest that
    /// works wins. The samples are made by the pinned ffmpeg itself.
    /// </summary>
    public sealed class DecodePaths
    {
        readonly Dictionary<string, DecodePath> byCodec = new Dictionary<string, DecodePath>(StringComparer.Ordinal);

        /// <summary>What a probe found, kept as "h264=2;hevc=1;av1=0" (DecodePath numbers).</summary>
        public string Save()
        {
            List<string> parts = new List<string>();
            foreach (string c in new[] { "h264", "hevc", "av1" })
            {
                DecodePath p;
                if (byCodec.TryGetValue(c, out p)) parts.Add(c + "=" + (int)p);
            }
            return string.Join(";", parts);
        }

        public static DecodePaths Load(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return null;
            DecodePaths d = new DecodePaths();
            foreach (string part in saved.Split(';'))
            {
                int eq = part.IndexOf('=');
                int n;
                if (eq <= 0 || !int.TryParse(part.Substring(eq + 1), out n) || n < 0 || n > 2) continue;
                d.byCodec[part.Substring(0, eq)] = (DecodePath)n;
            }
            return d.byCodec.Count == 0 ? null : d;
        }

        /// <summary>
        /// The path for a clip's codec (from its probe). hardware false (the
        /// Studio setting 'Hardware Video Acceleration' off) is always the CPU.
        /// </summary>
        public DecodePath For(string codecName, bool hardware)
        {
            if (!hardware) return DecodePath.Cpu;
            DecodePath p;
            return byCodec.TryGetValue(FfmpegArgs.Family(codecName), out p) ? p : DecodePath.Cpu;
        }

        public void Set(string codec, DecodePath p) { byCodec[codec] = p; }

        /// <summary>
        /// Runs the probe (seconds; on a background thread). log gets one line
        /// per test. A codec whose sample cannot be made, or that decodes on no
        /// hardware path, is the CPU.
        /// </summary>
        public static DecodePaths Probe(string ffmpeg, string tempDir, Action<string> log)
        {
            DecodePaths d = new DecodePaths();
            string o, e;
            HashSet<string> accels = FfmpegTools.Run(ffmpeg, FfmpegArgs.HwAccels(), 15000, out o, out e) == 0 ? FfmpegArgs.ParseHwAccels(o) : new HashSet<string>();
            bool d3d = accels.Contains("d3d11va");
            bool gpuScale = d3d && FfmpegTools.Run(ffmpeg, FfmpegArgs.Filters(), 15000, out o, out e) == 0 && FfmpegArgs.HasFilter(o, "scale_d3d11");
            if (log != null) log("hwaccels: " + string.Join(",", accels) + (gpuScale ? "; scale_d3d11" : ""));
            Directory.CreateDirectory(tempDir);
            foreach (string codec in new[] { "h264", "hevc", "av1" })
            {
                d.byCodec[codec] = DecodePath.Cpu;
                if (!d3d) continue;
                string sample = Path.Combine(tempDir, "probe-" + codec + ".mp4");
                if (FfmpegTools.Run(ffmpeg, FfmpegArgs.MakeSample(codec, sample), 60000, out o, out e) != 0)
                {
                    if (log != null) log(codec + ": no sample (" + e.Trim() + ")");
                    continue;
                }
                double best = Time(ffmpeg, sample, DecodePath.Cpu);
                if (log != null) log(codec + " cpu: " + best.ToString("0.000") + " s");
                foreach (DecodePath p in gpuScale ? new[] { DecodePath.D3D11Gpu, DecodePath.D3D11Copy } : new[] { DecodePath.D3D11Copy })
                {
                    double t = Time(ffmpeg, sample, p);
                    if (log != null) log(codec + " " + p + ": " + (double.IsNaN(t) ? "failed" : t.ToString("0.000") + " s"));
                    if (!double.IsNaN(t) && (double.IsNaN(best) || t < best)) { best = t; d.byCodec[codec] = p; }
                }
                try { File.Delete(sample); } catch (Exception) { }
            }
            return d;
        }

        static double Time(string ffmpeg, string sample, DecodePath p)
        {
            string o, e;
            Stopwatch sw = Stopwatch.StartNew();
            int rc = FfmpegTools.Run(ffmpeg, FfmpegArgs.TestDecode(sample, p), 30000, out o, out e);
            // ffmpeg -loglevel error that still prints is a decoder complaining: not a path to trust
            return rc == 0 && e.Trim().Length == 0 ? sw.Elapsed.TotalSeconds : double.NaN;
        }
    }
}
