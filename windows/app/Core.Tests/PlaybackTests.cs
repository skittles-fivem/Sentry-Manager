using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// The Viewer's playback engine without WPF: the clock and its master
    /// hold, the timeline arithmetic (cumulativeStarts, seeks, skips, event
    /// markers, the next-segment preload), ffmpeg's command lines, the
    /// frame ring's backpressure, the telemetry decoding against the web
    /// viewer's own code (Fixtures/expected-telemetry.json, written by
    /// test/windowsNativeTelemetry.test.js), and, against the real server,
    /// the media relay's Range round trip and clips decoded through it by
    /// ffmpeg (when ffmpeg is on PATH: the CPU path, and D3D11VA where this
    /// PC decodes with it).
    /// </summary>
    static class PlaybackTests
    {
        sealed class FakeTime : ITimeSource
        {
            public double T;
            public double Now { get { return T; } }
        }

        static NodeServer server;

        public static void StopAll()
        {
            if (server != null) server.Stop();
            server = null;
        }

        static void Near(double want, double got, string what, double eps = 1e-9)
        {
            if (!(Math.Abs(want - got) <= eps)) throw new AssertFailed(what + ": expected " + want.ToString("R", CultureInfo.InvariantCulture) + ", got " + got.ToString("R", CultureInfo.InvariantCulture));
        }

        static JsonValue Expected()
        {
            return JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-telemetry.json")));
        }

        /// <summary>A value of the JSON file (non-finite numbers are strings there) against the C# field.</summary>
        static void SameField(JsonValue want, double got, string what)
        {
            if (want.IsBool) { Assert.Equal(want.Bool ? 1.0 : 0.0, got, what); return; }
            if (want.IsString)
            {
                double w = want.Str == "NaN" ? double.NaN : want.Str == "Infinity" ? double.PositiveInfinity : double.NegativeInfinity;
                if (double.IsNaN(w) ? !double.IsNaN(got) : w != got) throw new AssertFailed(what + ": expected " + want.Str + ", got " + got);
                return;
            }
            // the JSON text is JavaScript's shortest round trip: the same double
            if (want.Num != got) throw new AssertFailed(what + ": expected " + want.Str + ", got " + got.ToString("R", CultureInfo.InvariantCulture));
        }

        static void SameSei(JsonValue fields, JsonValue want, Sei got, string what)
        {
            if (want.IsNull) { Assert.True(got == null, what + ": the web decodes nothing"); return; }
            Assert.True(got != null, what + ": the web decodes it");
            for (int i = 0; i < fields.Count; i++) SameField(want[i], got.Field(fields[i].Str), what + " " + fields[i].Str);
        }

        public static void Register(Action<string, Action> test, Action<string, Func<Task>> testAsync)
        {
            // ------------------------------------------------------------ telemetry (the web's own code)

            test("telemetry: each record decodes as protobuf.js decodes it (or fails where it fails)", () =>
            {
                JsonValue e = Expected();
                JsonValue fields = e["fields"];
                Assert.Equal(16, fields.Count, "fields");
                for (int i = 0; i < Sei.FieldNames.Length; i++) Assert.Equal(fields[i].Str, Sei.FieldNames[i], "field " + i);
                int n = 0;
                foreach (JsonValue r in e["records"].Items)
                {
                    byte[] bytes = Convert.FromBase64String(r["b64"].Str);
                    Sei s = Sei.Decode(bytes);
                    SameSei(fields, r["fields"], s, "record " + n + " (" + r["b64"].Str + ")");
                    Assert.Equal(r["gps"].Bool, ClipTelemetry.HasValidGps(s), "hasValidGps " + n);
                    n++;
                }
                Assert.True(n > 200, "records " + n);
            });

            test("telemetry: the server's answers give the web's seiData, map path and findSeiAtTime", () =>
            {
                JsonValue e = Expected();
                JsonValue fields = e["fields"];
                Assert.Equal(ClipTelemetry.MaxStaleMs, e["maxStaleMs"].Num, "maxStaleMs");
                int clips = 0;
                foreach (KeyValuePair<string, JsonValue> kv in e["answers"].Props)
                {
                    JsonValue want = kv.Value;
                    ClipTelemetry got = ClipTelemetry.FromServer(e["bodies"][kv.Key]);
                    if (want["failed"].Bool)
                    {
                        Assert.True(got == null, kv.Key + ": fails as atob() does");
                        continue;
                    }
                    Assert.True(got != null, kv.Key + ": read");
                    Assert.Equal(want["seiData"].Count, got.Samples.Count, kv.Key + " samples");
                    for (int i = 0; i < got.Samples.Count; i++)
                    {
                        Near(want["seiData"][i][0].Num, got.Samples[i].TimestampMs, kv.Key + " sample " + i + " time", 0);
                        SameSei(fields, want["seiData"][i][1], got.Samples[i].Sei, kv.Key + " sample " + i);
                    }
                    Assert.Equal(want["mapPath"].Count, got.MapPath.Count, kv.Key + " path");
                    for (int i = 0; i < got.MapPath.Count; i++)
                    {
                        JsonValue p = want["mapPath"][i];
                        Near(p[0].Num, got.MapPath[i].Lat, kv.Key + " lat " + i, 0);
                        Near(p[1].Num, got.MapPath[i].Lon, kv.Key + " lon " + i, 0);
                        Near(p[2].Num, got.MapPath[i].TimestampMs, kv.Key + " path time " + i, 0);
                        Assert.Equal(p[3].Bool, got.MapPath[i].Autopilot, kv.Key + " autopilot " + i);
                    }
                    foreach (JsonValue l in want["lookups"].Items)
                        Assert.Equal((int)l[1].Num, got.IndexAt(l[0].Num), kv.Key + " findSeiAtTime(" + l[0].Str + ")");
                    clips++;
                }
                Assert.True(clips >= 8, "answers " + clips);
            });

            test("telemetry: atob() as the browser's", () =>
            {
                foreach (JsonValue c in Expected()["atob"].Items)
                {
                    byte[] got = ClipTelemetry.Atob(c[0].Str);
                    string hex = got == null ? null : string.Concat(got.Select(b => b.ToString("x2")));
                    Assert.Equal(c[1].IsNull ? null : c[1].Str, hex, "atob('" + c[0].Str + "')");
                }
            });

            // ------------------------------------------------------------ the clock

            test("clock: plays at the rate, pauses, seeks, keeps its place across a new rate", () =>
            {
                FakeTime t = new FakeTime();
                PlaybackClock c = new PlaybackClock(t);
                c.End = 120;
                Near(0, c.Position, "start");
                t.T = 5;
                Near(0, c.Position, "paused does not move");
                c.Play();
                t.T = 7;
                Near(2, c.Position, "1x");
                c.SetRate(2);
                t.T = 8;
                Near(4, c.Position, "2x from where it was");
                c.Pause();
                t.T = 20;
                Near(4, c.Position, "paused");
                c.Seek(30);
                Near(30, c.Position, "seek while paused");
                c.Play();
                c.SetRate(0.5);
                t.T = 22;
                Near(31, c.Position, "0.5x");
                c.SetRate(9);
                Near(4, c.Rate, "at most 4x");
                c.SetRate(0.1);
                Near(0.5, c.Rate, "at least 0.5x");
                c.Seek(-5);
                Near(0, c.Position, "not before 0");
                c.Seek(500);
                Near(120, c.Position, "not past the end");
                Assert.True(!c.Playing, "stops at the end");
            });

            test("clock: never runs past the master's newest frame (buffering), then goes on from it", () =>
            {
                FakeTime t = new FakeTime();
                PlaybackClock c = new PlaybackClock(t);
                c.End = 60;
                c.MasterReady = double.NegativeInfinity; // a seek: no master frame yet
                c.Seek(10);
                c.Play();
                t.T = 1;
                Near(10, c.Position, "holds at the seek until the master's first frame");
                Assert.True(c.Buffering, "buffering");
                c.MasterReady = 10.5;
                t.T = 1.2;
                Near(10.2, c.Position, "runs");
                Assert.True(!c.Buffering, "not buffering");
                t.T = 3;
                Near(10.5, c.Position, "held at the master's frame");
                Assert.True(c.Buffering, "the master stalled");
                c.MasterReady = 20;
                t.T = 4;
                Near(11.5, c.Position, "goes on from the held frame, not from where it would have been");
                c.Pause();
                Assert.True(!c.Buffering, "paused is not buffering");
                c.MasterReady = double.PositiveInfinity;
                c.Play();
                t.T = 100;
                Near(60, c.Position, "the end");
                Assert.True(!c.Playing, "paused at the end");
            });

            test("clock: followers drop frames past 0.15 s and start again past 1.5 s or after an error", () =>
            {
                Assert.Equal(FollowerAction.Show, PlaybackSync.Decide(10, double.NaN, false), "none yet");
                Assert.Equal(FollowerAction.Show, PlaybackSync.Decide(10, 9.9, false), "0.1 s late");
                Assert.Equal(FollowerAction.Show, PlaybackSync.Decide(10, 10.1, false), "ahead (it waits)");
                Assert.Equal(FollowerAction.Drop, PlaybackSync.Decide(10, 9.8, false), "0.2 s late");
                Assert.Equal(FollowerAction.Drop, PlaybackSync.Decide(10, 8.6, false), "1.4 s late");
                Assert.Equal(FollowerAction.Restart, PlaybackSync.Decide(10, 8.4, false), "1.6 s late");
                Assert.Equal(FollowerAction.Restart, PlaybackSync.Decide(10, 12, false), "far ahead");
                Assert.Equal(FollowerAction.Restart, PlaybackSync.Decide(10, 10, true), "failed");
                Assert.Equal(FollowerAction.Drop, PlaybackSync.Decide(10, 8.4, false, 2), "1.6 s late at 2x");
                Assert.Equal(FollowerAction.Restart, PlaybackSync.Decide(10, 6.9, false, 2), "3.1 s late at 2x");
                Assert.Equal(FollowerAction.Restart, PlaybackSync.Decide(10, 8.4, false, 0.5), "1.6 s late at 0.5x");
            });

            // ------------------------------------------------------------ the timeline

            test("segments: cumulativeStarts, probed and opened durations, seeking into a segment", () =>
            {
                SegmentPlan p = new SegmentPlan(3, 60);
                Near(180, p.Total, "estimates");
                p.SetProbed(new[] { 59.5, double.NaN, 61 });
                Assert.Equal("0 59.5 119.5 180.5", Starts(p), "probed (a failed probe is the estimate)");
                p.SetActual(1, 58);
                Assert.Equal("0 59.5 117.5 178.5", Starts(p), "onMasterLoaded");
                p.SetActual(1, double.NaN);
                Near(117.5, p.Start(2), "a bad duration keeps it");
                int seg; double local;
                p.Locate(0, out seg, out local); Assert.Equal(0, seg, "0"); Near(0, local, "0 local");
                p.Locate(59.5, out seg, out local); Assert.Equal(1, seg, "boundary is the next segment"); Near(0, local, "boundary local");
                p.Locate(100, out seg, out local); Assert.Equal(1, seg, "100"); Near(40.5, local, "100 local");
                p.Locate(178.5, out seg, out local); Assert.Equal(2, seg, "the end is the last segment"); Near(61, local, "end local");
                p.Locate(999, out seg, out local); Assert.Equal(2, seg, "past the end"); Near(61, local, "clamped");
                p.Locate(-3, out seg, out local); Assert.Equal(0, seg, "before 0");
                Near(178.5, p.Skip(175, 10), "skip clamps to the end");
                Near(0, p.Skip(3, -10), "and to 0");
                Near(50, p.Percent(89.25), "percent");
                Near(89.25, p.FromPercent(50), "from percent");
                Near(1, new SegmentPlan(0, 60).Total, "an empty collection's total is 1 (the web divides by it)");
                Near(300, SegmentPlan.FallbackFor("gm_surroundvision"), "GM");
                Near(60, SegmentPlan.FallbackFor("tesla"), "Tesla");
            });

            test("segments: the event's start 15 s before it, the first load by the estimate, the marker", () =>
            {
                Near(105, SegmentPlan.StartOffset(LocalIso(2020, 1, 2, 12, 31, 0), "2020-01-02_12-29-00"), "120 s in, 15 s before");
                Near(0, SegmentPlan.StartOffset(LocalIso(2020, 1, 2, 12, 29, 10), "2020-01-02_12-29-00"), "not before 0");
                Near(0, SegmentPlan.StartOffset(null, "2020-01-02_12-29-00"), "no event");
                SegmentPlan p = new SegmentPlan(3, 60);
                int seg; double local;
                p.FirstLoad(105, out seg, out local);
                Assert.Equal(1, seg, "second minute"); Near(45, local, "45 s in");
                p.FirstLoad(500, out seg, out local);
                Assert.Equal(2, seg, "the last segment at most");
                double pct = SegmentPlan.EventMarkerPercent("SentryClips", LocalIso(2020, 1, 2, 12, 30, 0), "2020-01-02_12-29-00", "2020-01-02_12-30-00");
                Near(50, pct, "a minute into two", 1e-9);
                Assert.True(double.IsNaN(SegmentPlan.EventMarkerPercent("RecentClips", LocalIso(2020, 1, 2, 12, 30, 0), "2020-01-02_12-29-00", "2020-01-02_12-30-00")), "no marker for RecentClips");
                Near(90, p.EventMarkerTarget(50), "seek to its percent of the total");
            });

            test("segments: the next-segment preload starts as segmentPreload.js says", () =>
            {
                Func<double, double, double, bool> web = (pos, dur, rate) => SegmentPlan.ShouldStartPreload(pos, dur, rate, SegmentPlan.WebPreloadLeadSeconds, SegmentPlan.PreloadSettleSeconds);
                Assert.True(!web(10, 60, 1), "not yet");
                Assert.True(web(48, 60, 1), "12 s left");
                Assert.True(!web(47, 60, 1), "13 s left");
                Assert.True(web(36, 60, 2), "24 s left at 2x");
                Assert.True(!web(2, 8, 1), "a short segment settles first");
                Assert.True(web(4, 8, 1), "then preloads");
                Assert.True(!web(50, double.NaN, 1), "no duration");
                Assert.True(!web(50, 0, 1), "zero duration");
                Assert.True(SegmentPlan.ShouldStartPreload(57, 60, 1, SegmentPlan.PreloadLeadSeconds, SegmentPlan.PreloadSettleSeconds), "the app's 3 s");
                Assert.True(!SegmentPlan.ShouldStartPreload(56, 60, 1, SegmentPlan.PreloadLeadSeconds, SegmentPlan.PreloadSettleSeconds), "not 4 s before");
            });

            test("segments: planSegment's targets and master", () =>
            {
                var slots = new List<LayoutSlot> { new LayoutSlot("tl", "left_pillar"), new LayoutSlot("tc", "front"), new LayoutSlot("tr", "right_pillar"), new LayoutSlot("bc", "back") };
                var g = new PlaybackSegment();
                g.FilesByCamera["back"] = "/f/back.mp4";
                g.FilesByCamera["front"] = "/f/front.mp4";
                var v = SegmentPlan.PlanSegment(g, true, slots, "front", "front");
                Assert.Equal("tc", v.MasterSlot, "front is the master");
                Assert.Equal(4, v.Targets.Count, "a target per slot");
                Assert.Equal(null, v.Targets[0].File, "no left pillar");
                Assert.Equal("/f/back.mp4", v.Targets[3].File, "back");
                g.FilesByCamera.Remove("front");
                Assert.Equal("bc", SegmentPlan.PlanSegment(g, true, slots, "front", "front").MasterSlot, "the first slot with a file");
                var single = SegmentPlan.PlanSegment(g, false, slots, "front", "front");
                Assert.Equal("main", single.MasterSlot, "single view");
                Assert.Equal("/f/back.mp4", single.Targets[0].File, "the first file when the selected camera has none");
                Assert.Equal("/f/back.mp4", g.ProbeFile, "probe file without a front");
            });

            // ------------------------------------------------------------ ffmpeg's command lines

            test("ffmpeg: the decoder's command line for each path", () =>
            {
                var r = new DecodeRequest { Url = "http://127.0.0.1:5000/abc/1", Offset = 12.5, Width = 640, Height = 480, Fps = 36, Path = DecodePath.Cpu, Threads = 2 };
                Assert.Equal("-hide_banner -nostdin -loglevel error -threads 2 -ss 12.5 -i http://127.0.0.1:5000/abc/1 -an -sn -dn -map 0:v:0 -vf scale=640:480:flags=fast_bilinear,fps=36,format=bgr0 -f rawvideo -pix_fmt bgr0 pipe:1",
                    FfmpegArgs.CommandLine(FfmpegArgs.Decode(r)), "cpu");
                r.Path = DecodePath.D3D11Copy;
                r.Offset = 0;
                r.Fps = 30000.0 / 1001;
                Assert.Equal("-hide_banner -nostdin -loglevel error -hwaccel d3d11va -i http://127.0.0.1:5000/abc/1 -an -sn -dn -map 0:v:0 -vf scale=640:480:flags=fast_bilinear,fps=29.97003,format=bgr0 -f rawvideo -pix_fmt bgr0 pipe:1",
                    FfmpegArgs.CommandLine(FfmpegArgs.Decode(r)), "d3d11 copy, no -ss at 0");
                r.Path = DecodePath.D3D11Gpu;
                r.Fps = 36;
                r.SkipNonRef = true;
                Assert.Equal("-hide_banner -nostdin -loglevel error -hwaccel d3d11va -hwaccel_output_format d3d11 -skip_frame nonref -i http://127.0.0.1:5000/abc/1 -an -sn -dn -map 0:v:0 -vf scale_d3d11=640:480,hwdownload,format=nv12,scale=640:480:flags=fast_bilinear,fps=36,format=bgr0 -f rawvideo -pix_fmt bgr0 pipe:1",
                    FfmpegArgs.CommandLine(FfmpegArgs.Decode(r)), "d3d11 on the GPU, skipping non-reference frames");
                r.Path = DecodePath.Cpu;
                r.SkipNonRef = false;
                r.OneFrame = true;
                r.Offset = 3;
                Assert.Equal("-hide_banner -nostdin -loglevel error -threads 2 -ss 3 -i http://127.0.0.1:5000/abc/1 -an -sn -dn -map 0:v:0 -vf scale=640:480:flags=fast_bilinear,format=bgr0 -frames:v 1 -f rawvideo -pix_fmt bgr0 pipe:1",
                    FfmpegArgs.CommandLine(FfmpegArgs.Decode(r)), "scrub preview");
                Assert.Equal("-v error -select_streams v:0 -show_entries format=duration:stream=r_frame_rate,codec_name,width,height -of json http://x/1",
                    FfmpegArgs.CommandLine(FfmpegArgs.Probe("http://x/1")), "ffprobe");
                Assert.Equal("-hide_banner -nostdin -loglevel error -hwaccel d3d11va -i s.mp4 -an -sn -dn -map 0:v:0 -vf scale=640:360:flags=fast_bilinear,fps=36,format=bgr0 -f null -",
                    FfmpegArgs.CommandLine(FfmpegArgs.TestDecode("s.mp4", DecodePath.D3D11Copy)), "test decode");
                Assert.True(FfmpegArgs.CommandLine(FfmpegArgs.MakeSample("av1", "a.mp4")).Contains("-c:v libsvtav1"), "AV1 sample");
                Assert.True(FfmpegArgs.CommandLine(FfmpegArgs.MakeSample("hevc", "h.mp4")).Contains("-c:v libx265"), "H.265 sample");
            });

            test("ffmpeg: quoting, sizes, threads, and what the tools answer", () =>
            {
                Assert.Equal("\"C:\\Program Files\\x\\\\\"", FfmpegArgs.Quote("C:\\Program Files\\x\\"), "trailing backslash in quotes");
                Assert.Equal("\"a \\\"b\\\"\"", FfmpegArgs.Quote("a \"b\""), "quotes");
                Assert.Equal("\"\"", FfmpegArgs.Quote(""), "empty");
                Assert.Equal("a\\b", FfmpegArgs.Quote("a\\b"), "plain");
                int w, h;
                FfmpegArgs.TileSize(641, 481, 1448, 938, out w, out h);
                Assert.Equal(640, w, "even width, the source's shape"); Assert.Equal(416, h, "height");
                FfmpegArgs.TileSize(5000, 5000, 2896, 1876, out w, out h);
                Assert.Equal(2896, w, "never above the source"); Assert.Equal(1876, h, "source height");
                FfmpegArgs.TileSize(0, 0, 1280, 960, out w, out h);
                Assert.Equal(2, w, "at least 2");
                Assert.Equal(2, FfmpegArgs.CpuThreads(4), "min 2");
                Assert.Equal(4, FfmpegArgs.CpuThreads(24), "cores / 6");
                ProbeResult p = FfmpegArgs.ParseProbe("{\"programs\":[],\"streams\":[{\"codec_name\":\"hevc\",\"width\":2896,\"height\":1876,\"r_frame_rate\":\"36/1\"}],\"format\":{\"duration\":\"60.027778\"}}");
                Assert.Equal("hevc", p.Codec, "codec"); Assert.Equal(2896, p.Width, "w"); Assert.Equal(1876, p.Height, "h");
                Near(36, p.Fps, "fps"); Near(60.027778, p.Duration, "duration");
                Assert.True(double.IsNaN(FfmpegArgs.ParseProbe("not json").Duration), "bad probe");
                Assert.True(double.IsNaN(FfmpegArgs.ParseRate("0/0")), "0/0");
                Near(30000.0 / 1001, FfmpegArgs.ParseRate("30000/1001"), "ntsc");
                var hw = FfmpegArgs.ParseHwAccels("Hardware acceleration methods:\r\ncuda\r\ndxva2\r\nqsv\r\nd3d11va\r\nopencl\r\nvulkan\r\n\r\n");
                Assert.True(hw.Contains("d3d11va") && hw.Contains("dxva2") && hw.Count == 6, "hwaccels");
                Assert.True(FfmpegArgs.HasFilter("Filters:\n ... scale_d3d11       V->V       Scale the input video size.\n ... scale    V->V  x\n", "scale_d3d11"), "scale_d3d11");
                Assert.True(!FfmpegArgs.HasFilter(" ... scale    V->V  x\n", "scale_d3d11"), "not there");
                Assert.Equal("hevc", FfmpegArgs.Family("HEVC"), "family");
                DecodePaths d = DecodePaths.Load("h264=2;hevc=1;av1=0;junk;x=9");
                Assert.Equal(DecodePath.D3D11Gpu, d.For("h264", true), "remembered");
                Assert.Equal(DecodePath.Cpu, d.For("h264", false), "Hardware Video Acceleration off");
                Assert.Equal(DecodePath.D3D11Copy, d.For("hevc", true), "hevc");
                Assert.Equal(DecodePath.Cpu, d.For("vp9", true), "unknown codec");
                Assert.Equal("h264=2;hevc=1;av1=0", d.Save(), "saved again");
                Assert.True(DecodePaths.Load("") == null, "nothing saved");
            });

            test("frames: the ring holds a few frames, blocks the reader when full, gives the newest due frame", () =>
            {
                FrameRing ring = new FrameRing(4, 2, 2);
                int filled = 0;
                Thread reader = new Thread(() =>
                {
                    for (int k = 0; k < 10; k++)
                    {
                        VideoFrame f = ring.Rent();
                        if (f == null) return;
                        f.Index = k;
                        f.Time = k * 0.1;
                        ring.Add(f);
                        Interlocked.Increment(ref filled);
                    }
                });
                reader.IsBackground = true;
                reader.Start();
                Thread.Sleep(300);
                Assert.Equal(4, Volatile.Read(ref filled), "the reader waits on a full ring");
                Assert.True(ring.Take(-1) == null, "nothing due before the first");
                VideoFrame g = ring.Take(0.25);
                Assert.Equal(2L, g.Index, "the newest at or before the clock; older ones freed");
                ring.Return(g);
                Thread.Sleep(300);
                Assert.Equal(7, Volatile.Read(ref filled), "three more decoded");
                Assert.Equal(3, ring.DropBefore(0.65), "a late follower drops all but the newest due");
                ring.Close();
                Assert.True(ring.Rent() == null, "closed");
                reader.Join(2000);
            });

            // ------------------------------------------------------------ the relay (a stand-in server)

            testAsync("relay: GET and HEAD with Range pass through; unknown tokens are 404; no path on the loopback", async () =>
            {
                byte[] file = Enumerable.Range(0, 300000).Select(i => (byte)(i * 7)).ToArray();
                var asked = new List<string>();
                MediaRelay.Opener open = (p, range, head, ct) =>
                {
                    lock (asked) asked.Add(p + " " + range + (head ? " HEAD" : ""));
                    var res = new HttpResponseMessage();
                    long from = 0, to = file.Length - 1;
                    if (range != null && range.StartsWith("bytes="))
                    {
                        string[] ft = range.Substring(6).Split('-');
                        from = long.Parse(ft[0]);
                        if (ft[1].Length > 0) to = long.Parse(ft[1]);
                        res.StatusCode = HttpStatusCode.PartialContent;
                    }
                    byte[] part = file.Skip((int)from).Take((int)(to - from + 1)).ToArray();
                    res.Content = new ByteArrayContent(head ? new byte[0] : part);
                    res.Content.Headers.ContentLength = part.Length;
                    if (range != null) res.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, file.Length);
                    res.Headers.AcceptRanges.Add("bytes");
                    return Task.FromResult(res);
                };
                using (var relay = new MediaRelay(open))
                using (var http = new HttpClient())
                {
                    string url = relay.UrlFor("/footage/TeslaCam/RecentClips/2020-01-01_08-00-00-front.mp4");
                    Assert.Equal(url, relay.UrlFor("/footage/TeslaCam/RecentClips/2020-01-01_08-00-00-front.mp4"), "same URL for the same path");
                    Assert.True(!url.Contains("footage") && url.StartsWith("http://127.0.0.1:" + relay.Port + "/"), "only a token: " + url);
                    Assert.Equal(32, url.Split('/')[3].Length, "128-bit token");
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(10, 19);
                    var res = await http.SendAsync(req);
                    Assert.Equal(206, (int)res.StatusCode, "206");
                    Assert.Equal("bytes 10-19/300000", res.Content.Headers.ContentRange.ToString(), "Content-Range");
                    Assert.Equal(true, res.Headers.AcceptRanges.Contains("bytes"), "Accept-Ranges");
                    Assert.Equal(string.Join(",", file.Skip(10).Take(10)), string.Join(",", await res.Content.ReadAsByteArrayAsync()), "the bytes");
                    var all = await http.GetByteArrayAsync(url);
                    Assert.True(all.SequenceEqual(file), "the whole file, streamed");
                    var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                    Assert.Equal(200, (int)head.StatusCode, "HEAD");
                    Assert.Equal(300000L, head.Content.Headers.ContentLength, "HEAD length");
                    var missing = await http.GetAsync("http://127.0.0.1:" + relay.Port + "/00000000000000000000000000000000/0");
                    Assert.Equal(404, (int)missing.StatusCode, "unknown token");
                    lock (asked) Assert.True(asked.Contains("/footage/TeslaCam/RecentClips/2020-01-01_08-00-00-front.mp4 bytes=10-19"), "asked with the Range: " + string.Join(" | ", asked));
                    relay.Clear();
                    Assert.Equal(404, (int)(await http.GetAsync(url)).StatusCode, "forgotten");
                }
            });

            // ------------------------------------------------------------ the real server and ffmpeg

            testAsync("playback: the real server's clips through the relay: Range, ffprobe, decoded frames on each path", async () =>
            {
                string ffmpeg = FfmpegTools.Ffmpeg, ffprobe = FfmpegTools.Ffprobe;
                if (ffmpeg == null || ffprobe == null)
                {
                    Console.WriteLine("        (no ffmpeg on PATH: the relay is checked, the decoders are not)");
                }
                string root = Temp.Dir("playback");
                string footage = Path.Combine(root, "footage");
                string clips = Path.Combine(footage, "TeslaCam", "RecentClips");
                Directory.CreateDirectory(clips);
                var made = new List<string>();
                var codecOf = new Dictionary<string, string>();
                if (ffmpeg != null)
                {
                    // a 3-camera set, two minutes' worth of 2 s segments, in each codec this ffmpeg writes
                    string[] cams = { "front", "back", "left_repeater" };
                    var encoders = new[] { new[] { "h264", "libx264", "08-00-00" }, new[] { "hevc", "libx265", "08-01-00" }, new[] { "av1", "libsvtav1", "08-02-00" } };
                    foreach (var enc in encoders)
                    {
                        foreach (string cam in cams)
                        {
                            string f = Path.Combine(clips, "2020-01-01_" + enc[2] + "-" + cam + ".mp4");
                            string size = cam == "front" ? "320x240" : "160x120";
                            string o, e;
                            var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=duration=2:size=" + size + ":rate=36", "-pix_fmt", "yuv420p", "-c:v", enc[1] };
                            if (enc[1] == "libx265") args.AddRange(new[] { "-x265-params", "log-level=error" });
                            if (enc[1] == "libsvtav1") args.AddRange(new[] { "-preset", "12" });
                            if (enc[1] == "libx264") args.AddRange(new[] { "-preset", "ultrafast" });
                            args.AddRange(new[] { "-an", "-f", "mp4", f });
                            int rc = FfmpegTools.Run(ffmpeg, args, 120000, out o, out e);
                            if (rc != 0) { Console.WriteLine("        (" + enc[1] + " not in this ffmpeg: no " + enc[0] + " clips)"); try { File.Delete(f); } catch (IOException) { } break; }
                            made.Add(f);
                            codecOf[f] = enc[0];
                        }
                    }
                }
                else
                {
                    File.WriteAllBytes(Path.Combine(clips, "2020-01-01_08-00-00-front.mp4"), Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
                    made.Add(Path.Combine(clips, "2020-01-01_08-00-00-front.mp4"));
                }
                var s = new NodeServer { Port = NodeServer.FreePort(), Data = Temp.Dir("playback-data"), Password = "correcthorse" };
                s.Env["FOOTAGE_PATH"] = footage;
                s.Env["VEHICLE_NAME"] = "Daily Driver";
                server = s;
                await s.Start();
                using (var api = new Api(ServerAddress.Local(s.Port), new Prompt()))
                {
                    await new Session(api).SignIn("admin", "correcthorse");
                    using (var relay = new MediaRelay(api))
                    using (var http = new HttpClient())
                    {
                        // Range round trip against the file itself
                        string first = made[0];
                        byte[] bytes = File.ReadAllBytes(first);
                        string url = relay.UrlFor(first);
                        var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 1123);
                        var res = await http.SendAsync(req);
                        Assert.Equal(206, (int)res.StatusCode, "206 from the server");
                        Assert.Equal("bytes 100-1123/" + bytes.Length, res.Content.Headers.ContentRange.ToString(), "Content-Range from the server");
                        Assert.True((await res.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes.Skip(100).Take(1024)), "the server's bytes");
                        Assert.True((await http.GetByteArrayAsync(url)).SequenceEqual(bytes), "the whole clip");
                        var outside = await http.GetAsync(relay.UrlFor(Path.Combine(root, "elsewhere.mp4")));
                        Assert.Equal(403, (int)outside.StatusCode, "outside the shares: the server's 403 passed on");
                        if (ffmpeg == null || ffprobe == null) return;

                        DecodePaths paths = DecodePaths.Probe(ffmpeg, Temp.Dir("probe"), line => Console.WriteLine("        probe: " + line));
                        Console.WriteLine("        decode paths: " + paths.Save());
                        foreach (string clip in made)
                        {
                            string curl = relay.UrlFor(clip);
                            string why;
                            ProbeResult p = FfmpegTools.ProbeClip(ffprobe, curl, 30000, out why);
                            Assert.Equal(codecOf[clip], FfmpegArgs.Family(p.Codec), "codec of " + Path.GetFileName(clip) + (why != null ? " (" + why + ")" : ""));
                            Near(2, p.Duration, "duration of " + Path.GetFileName(clip), 0.1);
                            Near(36, p.Fps, "rate of " + Path.GetFileName(clip), 1e-6);
                            var tried = new List<DecodePath> { DecodePath.Cpu };
                            DecodePath hw = paths.For(p.Codec, true);
                            if (hw != DecodePath.Cpu) tried.Add(hw);
                            foreach (DecodePath path in tried)
                            {
                                int w, h;
                                FfmpegArgs.TileSize(p.Width / 2, p.Height / 2, p.Width, p.Height, out w, out h);
                                var r = new DecodeRequest { Url = curl, Offset = 0.5, Width = w, Height = h, Fps = p.Fps, Path = path, Threads = 2 };
                                using (var cs = new CameraStream(ffmpeg, r, 4))
                                {
                                    cs.Start();
                                    var sw = Stopwatch.StartNew();
                                    while (cs.Frames < 4 && !cs.Failed && sw.ElapsedMilliseconds < 30000) await Task.Delay(20);
                                    await Task.Delay(300);
                                    Assert.True(!cs.Failed, path + " " + Path.GetFileName(clip) + ": " + cs.Error);
                                    Assert.Equal(4L, cs.Frames, path + ": the reader holds four frames and ffmpeg waits");
                                    var times = new List<double>();
                                    VideoFrame f0 = cs.Ring.Take(0.5);
                                    Assert.True(f0 != null, "the first frame at the offset");
                                    Near(0.5, f0.Time, "frame 0 is at the offset");
                                    Assert.True(f0.Data.Length == w * h * 4 && f0.Data.Any(b => b != 0), "a picture");
                                    cs.Ring.Return(f0);
                                    times.Add(0.5);
                                    while (!cs.Ended && !cs.Failed && sw.ElapsedMilliseconds < 60000)
                                    {
                                        VideoFrame f = cs.Ring.Take(cs.Ring.OldestTime); // one at a time (a later time frees the older ones)
                                        if (f == null) { await Task.Delay(5); continue; }
                                        times.Add(f.Time);
                                        cs.Ring.Return(f);
                                    }
                                    Assert.True(cs.Ended, path + " " + Path.GetFileName(clip) + " ended: " + cs.Error);
                                    while (true) { VideoFrame f = cs.Ring.Take(cs.Ring.OldestTime); if (f == null) break; times.Add(f.Time); cs.Ring.Return(f); }
                                    Assert.True(Math.Abs(times.Count - 54) <= 2, path + " " + Path.GetFileName(clip) + ": 1.5 s at 36 fps, got " + times.Count);
                                    for (int k = 1; k < times.Count; k++) Near(0.5 + k / 36.0, times[k], "frame " + k + " time", 1e-9);
                                    Console.WriteLine("        " + Path.GetFileName(clip) + " " + path + ": " + times.Count + " frames " + w + "x" + h + " in " + sw.ElapsedMilliseconds + " ms");
                                }
                            }
                        }
                        // one-frame preview
                        var pr = new DecodeRequest { Url = relay.UrlFor(made[0]), Offset = 1, Width = 160, Height = 120, Fps = 36, Path = DecodePath.Cpu, Threads = 2, OneFrame = true };
                        using (var cs = new CameraStream(ffmpeg, pr, 1))
                        {
                            cs.Start();
                            var sw = Stopwatch.StartNew();
                            while (cs.Frames < 1 && !cs.Failed && sw.ElapsedMilliseconds < 30000) await Task.Delay(20);
                            VideoFrame f = cs.Ring.Take(double.PositiveInfinity);
                            Assert.True(f != null && Math.Abs(f.Time - 1) < 1e-9, "the scrub preview's frame: " + cs.Error);
                            cs.Ring.Return(f);
                            while (!cs.Ended && !cs.Failed && sw.ElapsedMilliseconds < 30000) await Task.Delay(20);
                            Assert.True(cs.Ended && cs.Frames == 1, "and only that one: " + cs.Error);
                        }
                        // a clip that is not a video fails (the CPU retry path's signal)
                        string junk = Path.Combine(clips, "2020-01-01_09-00-00-front.mp4");
                        File.WriteAllBytes(junk, new byte[4096]);
                        var jr = new DecodeRequest { Url = relay.UrlFor(junk), Width = 160, Height = 120, Fps = 36, Path = DecodePath.Cpu, Threads = 2 };
                        using (var cs = new CameraStream(ffmpeg, jr, 4))
                        {
                            cs.Start();
                            var sw = Stopwatch.StartNew();
                            while (!cs.Ended && !cs.Failed && sw.ElapsedMilliseconds < 30000) await Task.Delay(20);
                            Assert.True(cs.Failed && cs.Frames == 0 && cs.Error.Length > 0, "a broken clip fails with ffmpeg's words");
                        }
                    }
                }
                s.Stop();
                server = null;
            });
        }

        static string Starts(SegmentPlan p)
        {
            return string.Join(" ", p.CumulativeStarts.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        }

        static string LocalIso(int y, int mo, int d, int h, int mi, int s)
        {
            // as event.json writes it: local time, no zone
            return new DateTime(y, mo, d, h, mi, s, DateTimeKind.Local).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
