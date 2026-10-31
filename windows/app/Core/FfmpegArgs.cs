using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SentrySix.Core
{
    /// <summary>How a decoder gets its pictures: the fastest that works on this PC is picked once per codec.</summary>
    public enum DecodePath
    {
        /// <summary>The CPU (libdav1d for AV1, the native H.264 and HEVC decoders).</summary>
        Cpu = 0,
        /// <summary>D3D11VA, frames copied back to memory, scaled on the CPU.</summary>
        D3D11Copy = 1,
        /// <summary>D3D11VA, frames scaled on the GPU (scale_d3d11), then downloaded.</summary>
        D3D11Gpu = 2
    }

    /// <summary>What one decoder is asked for.</summary>
    public sealed class DecodeRequest
    {
        /// <summary>http://127.0.0.1:port/token/n of the media relay.</summary>
        public string Url;
        /// <summary>Seconds into the clip (input -ss: frame accurate when transcoding).</summary>
        public double Offset;
        /// <summary>Output size (even numbers).</summary>
        public int Width;
        public int Height;
        /// <summary>The constant output rate (the clip's r_frame_rate): frame k shows at Offset + k / Fps.</summary>
        public double Fps;
        public DecodePath Path;
        /// <summary>-threads for the CPU path.</summary>
        public int Threads;
        /// <summary>-skip_frame nonref (at 2x and faster, cameras that are neither focused nor the master).</summary>
        public bool SkipNonRef;
        /// <summary>Only one frame (the scrub preview).</summary>
        public bool OneFrame;
    }

    /// <summary>A clip as ffprobe describes it.</summary>
    public sealed class ProbeResult
    {
        public double Duration = double.NaN;
        public double Fps = double.NaN;
        public string Codec;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// The ffmpeg and ffprobe command lines of playback, built as strings
    /// (unit-tested one by one) and the parsing of what the tools answer.
    /// </summary>
    public static class FfmpegArgs
    {
        /// <summary>The rate used when the probe has none (Tesla records about 36 frames a second).</summary>
        public const double DefaultFps = 36;

        static string Num(double d)
        {
            return d.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// ffmpeg -hide_banner -nostdin -loglevel error [HW] -ss offset -i url
        /// -an -sn -dn -map 0:v:0 -vf "scale,fps,format=bgr0" -f rawvideo
        /// -pix_fmt bgr0 pipe:1.
        /// </summary>
        public static List<string> Decode(DecodeRequest r)
        {
            List<string> a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
            switch (r.Path)
            {
                case DecodePath.D3D11Gpu:
                    a.AddRange(new[] { "-hwaccel", "d3d11va", "-hwaccel_output_format", "d3d11" });
                    break;
                case DecodePath.D3D11Copy:
                    a.AddRange(new[] { "-hwaccel", "d3d11va" });
                    break;
                default:
                    a.AddRange(new[] { "-threads", Math.Max(1, r.Threads).ToString(CultureInfo.InvariantCulture) });
                    break;
            }
            if (r.SkipNonRef) a.AddRange(new[] { "-skip_frame", "nonref" });
            if (r.Offset > 0) a.AddRange(new[] { "-ss", Num(r.Offset) });
            a.AddRange(new[] { "-i", r.Url, "-an", "-sn", "-dn", "-map", "0:v:0", "-vf", Filter(r) });
            if (r.OneFrame) a.AddRange(new[] { "-frames:v", "1" });
            a.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "bgr0", "pipe:1" });
            return a;
        }

        /// <summary>The -vf chain: scale where the frames are, a constant rate, bgr0 for WriteableBitmap's Bgr32.</summary>
        public static string Filter(DecodeRequest r)
        {
            string size = r.Width.ToString(CultureInfo.InvariantCulture) + ":" + r.Height.ToString(CultureInfo.InvariantCulture);
            string fps = "fps=" + Num(r.Fps > 0 ? r.Fps : DefaultFps);
            // the scrub preview needs no rate (one frame)
            string tail = r.OneFrame ? "format=bgr0" : fps + ",format=bgr0";
            switch (r.Path)
            {
                case DecodePath.D3D11Gpu:
                    return "scale_d3d11=" + size + ",hwdownload,format=nv12,scale=" + size + ":flags=fast_bilinear," + tail;
                default:
                    return "scale=" + size + ":flags=fast_bilinear," + tail;
            }
        }

        /// <summary>ffprobe -v error -show_entries format=duration:stream=r_frame_rate,codec_name,width,height -select_streams v:0 -of json url.</summary>
        public static List<string> Probe(string url)
        {
            return new List<string> { "-v", "error", "-select_streams", "v:0", "-show_entries", "format=duration:stream=r_frame_rate,codec_name,width,height", "-of", "json", url };
        }

        /// <summary>ffmpeg -hide_banner -hwaccels.</summary>
        public static List<string> HwAccels() { return new List<string> { "-hide_banner", "-hwaccels" }; }

        /// <summary>ffmpeg -hide_banner -filters.</summary>
        public static List<string> Filters() { return new List<string> { "-hide_banner", "-filters" }; }

        /// <summary>
        /// The probe's sample clip: one second of testsrc2 at 640x360, 36 fps,
        /// made by the pinned ffmpeg itself (libx264, libx265, libsvtav1: no
        /// sample files ship with the app).
        /// </summary>
        public static List<string> MakeSample(string codec, string outFile)
        {
            List<string> a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=duration=1:size=640x360:rate=36", "-pix_fmt", "yuv420p" };
            switch (codec)
            {
                case "hevc": a.AddRange(new[] { "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "log-level=error" }); break;
                case "av1": a.AddRange(new[] { "-c:v", "libsvtav1", "-preset", "12" }); break;
                default: a.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast" }); break;
            }
            a.AddRange(new[] { "-an", "-f", "mp4", outFile });
            return a;
        }

        /// <summary>A test decode of a sample on a path: every frame to null, timed by the caller.</summary>
        public static List<string> TestDecode(string file, DecodePath path)
        {
            DecodeRequest r = new DecodeRequest { Url = file, Width = 640, Height = 360, Fps = 36, Path = path, Threads = 2 };
            List<string> a = Decode(r);
            // to nowhere instead of a pipe
            a.RemoveRange(a.Count - 5, 5);
            a.AddRange(new[] { "-f", "null", "-" });
            return a;
        }

        /// <summary>-threads for the CPU path: the cores shared by six cameras, at least 2.</summary>
        public static int CpuThreads(int cores)
        {
            return Math.Max(2, cores / 6);
        }

        /// <summary>
        /// A tile's output size: its size in device pixels rounded to even
        /// numbers, fitted to the source's shape (Stretch Uniform shows no
        /// more), never above the source; at least 2x2.
        /// </summary>
        public static void TileSize(double tileW, double tileH, int srcW, int srcH, out int w, out int h)
        {
            if (srcW <= 0 || srcH <= 0) { srcW = 1280; srcH = 960; }
            double scale = Math.Min(1, Math.Min(tileW / srcW, tileH / srcH));
            if (double.IsNaN(scale)) scale = 1;
            scale = Math.Max(0, scale);
            w = Even(srcW * scale);
            h = Even(srcH * scale);
        }

        static int Even(double d)
        {
            int v = (int)Math.Round(d / 2) * 2;
            return Math.Max(2, v);
        }

        /// <summary>One argument quoted for a Windows command line (CommandLineToArgvW's rules).</summary>
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"', '\n' }) < 0) return arg;
            StringBuilder sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); slashes = 0; continue; }
                sb.Append('\\', slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>The arguments as one command line (ProcessStartInfo.Arguments on .NET Framework).</summary>
        public static string CommandLine(IEnumerable<string> args)
        {
            return string.Join(" ", args.Select(Quote));
        }

        // ------------------------------------------------------------ answers

        /// <summary>ffprobe's JSON: format.duration, and the video stream's codec_name, size and r_frame_rate ("36/1", "30000/1001").</summary>
        public static ProbeResult ParseProbe(string json)
        {
            ProbeResult p = new ProbeResult();
            JsonValue v = JsonValue.TryParse(json ?? "");
            if (v == null) return p;
            double d;
            if (double.TryParse(v["format"]["duration"].Str ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) p.Duration = d;
            JsonValue s = v["streams"][0];
            p.Codec = s["codec_name"].Str;
            p.Width = (int)s["width"].Num;
            p.Height = (int)s["height"].Num;
            p.Fps = ParseRate(s["r_frame_rate"].Str);
            return p;
        }

        /// <summary>"num/den" or a number; NaN when there is none (0/0).</summary>
        public static double ParseRate(string s)
        {
            if (string.IsNullOrEmpty(s)) return double.NaN;
            int slash = s.IndexOf('/');
            double n, dd = 1;
            if (slash < 0) return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && n > 0 ? n : double.NaN;
            if (!double.TryParse(s.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return double.NaN;
            if (!double.TryParse(s.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out dd)) return double.NaN;
            if (dd <= 0 || n <= 0) return double.NaN;
            return n / dd;
        }

        /// <summary>ffmpeg -hwaccels: the names under "Hardware acceleration methods:".</summary>
        public static HashSet<string> ParseHwAccels(string output)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool inList = false;
            foreach (string raw in (output ?? "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0) continue;
                if (l.EndsWith(":", StringComparison.Ordinal)) { inList = true; continue; }
                if (inList && l.IndexOf(' ') < 0) set.Add(l);
            }
            return set;
        }

        /// <summary>ffmpeg -filters: whether a filter is in the list (" T.. name  V->V  ...").</summary>
        public static bool HasFilter(string output, string name)
        {
            foreach (string raw in (output ?? "").Split('\n'))
            {
                string[] parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == name) return true;
            }
            return false;
        }

        /// <summary>The codec family a probe's codec_name decides the path by (h264, hevc, av1, other).</summary>
        public static string Family(string codecName)
        {
            switch ((codecName ?? "").ToLowerInvariant())
            {
                case "h264": return "h264";
                case "hevc":
                case "h265": return "hevc";
                case "av1": return "av1";
                default: return "other";
            }
        }
    }
}
