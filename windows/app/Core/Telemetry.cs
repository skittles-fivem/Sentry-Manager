using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// One frame's Tesla telemetry record (SeiMetadata in
    /// src/renderer/proto/dashcam.proto), as protobuf.js decodes it for the
    /// web viewer: proto3 defaults (0, false) for a field the record leaves
    /// out, enums as their numbers, uint64 as a number.
    /// </summary>
    public sealed class Sei
    {
        public uint Version;
        /// <summary>Gear: 0 park, 1 drive, 2 reverse, 3 neutral (other numbers kept as sent).</summary>
        public int GearState;
        public double FrameSeqNo;
        public double VehicleSpeedMps;
        public double AcceleratorPedalPosition;
        public double SteeringWheelAngle;
        public bool BlinkerOnLeft;
        public bool BlinkerOnRight;
        public bool BrakeApplied;
        /// <summary>0 none, 1 self driving, 2 autosteer, 3 TACC.</summary>
        public int AutopilotState;
        public double LatitudeDeg;
        public double LongitudeDeg;
        public double HeadingDeg;
        public double LinearAccelerationMps2X;
        public double LinearAccelerationMps2Y;
        public double LinearAccelerationMps2Z;

        /// <summary>The fields by their .proto names, in field order (the parity tests compare these).</summary>
        public static readonly string[] FieldNames =
        {
            "version", "gear_state", "frame_seq_no", "vehicle_speed_mps", "accelerator_pedal_position",
            "steering_wheel_angle", "blinker_on_left", "blinker_on_right", "brake_applied", "autopilot_state",
            "latitude_deg", "longitude_deg", "heading_deg", "linear_acceleration_mps2_x",
            "linear_acceleration_mps2_y", "linear_acceleration_mps2_z"
        };

        /// <summary>A field by its .proto name, as a number (booleans 1 or 0); NaN for an unknown name.</summary>
        public double Field(string name)
        {
            switch (name)
            {
                case "version": return Version;
                case "gear_state": return GearState;
                case "frame_seq_no": return FrameSeqNo;
                case "vehicle_speed_mps": return VehicleSpeedMps;
                case "accelerator_pedal_position": return AcceleratorPedalPosition;
                case "steering_wheel_angle": return SteeringWheelAngle;
                case "blinker_on_left": return BlinkerOnLeft ? 1 : 0;
                case "blinker_on_right": return BlinkerOnRight ? 1 : 0;
                case "brake_applied": return BrakeApplied ? 1 : 0;
                case "autopilot_state": return AutopilotState;
                case "latitude_deg": return LatitudeDeg;
                case "longitude_deg": return LongitudeDeg;
                case "heading_deg": return HeadingDeg;
                case "linear_acceleration_mps2_x": return LinearAccelerationMps2X;
                case "linear_acceleration_mps2_y": return LinearAccelerationMps2Y;
                case "linear_acceleration_mps2_z": return LinearAccelerationMps2Z;
                default: return double.NaN;
            }
        }

        /// <summary>
        /// SeiMetadata.decode(bytes) of protobuf.js 8 (src/renderer/vendor),
        /// step for step: a known field with another wire type is skipped as
        /// unknown, unknown fields and groups are skipped, and whatever
        /// protobuf.js throws on (a varint or field running past the end, field
        /// number 0, wire types 4, 6 and 7, a bad tag or length encoding, a
        /// group that does not end) is null here.
        /// </summary>
        public static Sei Decode(byte[] buf)
        {
            if (buf == null) return null;
            try
            {
                return new PbReader(buf).Message();
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>protobuf.js's Reader, only what SeiMetadata needs.</summary>
        sealed class PbReader
        {
            const int RecursionLimit = 100;
            readonly byte[] buf;
            int pos;
            readonly int len;

            public PbReader(byte[] b) { buf = b; len = b.Length; }

            static FormatException Range() { return new FormatException("index out of range"); }

            public Sei Message()
            {
                Sei m = new Sei();
                while (pos < len)
                {
                    uint t = Tag();
                    uint wire = t & 7;
                    uint field = t >> 3;
                    switch (field)
                    {
                        case 1: if (wire == 0) { m.Version = UInt32(); continue; } break;
                        case 2: if (wire == 0) { m.GearState = (int)UInt32(); continue; } break;
                        case 3: if (wire == 0) { m.FrameSeqNo = UInt64(); continue; } break;
                        case 4: if (wire == 5) { m.VehicleSpeedMps = Float(); continue; } break;
                        case 5: if (wire == 5) { m.AcceleratorPedalPosition = Float(); continue; } break;
                        case 6: if (wire == 5) { m.SteeringWheelAngle = Float(); continue; } break;
                        case 7: if (wire == 0) { m.BlinkerOnLeft = Bool(); continue; } break;
                        case 8: if (wire == 0) { m.BlinkerOnRight = Bool(); continue; } break;
                        case 9: if (wire == 0) { m.BrakeApplied = Bool(); continue; } break;
                        case 10: if (wire == 0) { m.AutopilotState = (int)UInt32(); continue; } break;
                        case 11: if (wire == 1) { m.LatitudeDeg = Double(); continue; } break;
                        case 12: if (wire == 1) { m.LongitudeDeg = Double(); continue; } break;
                        case 13: if (wire == 1) { m.HeadingDeg = Double(); continue; } break;
                        case 14: if (wire == 1) { m.LinearAccelerationMps2X = Double(); continue; } break;
                        case 15: if (wire == 1) { m.LinearAccelerationMps2Y = Double(); continue; } break;
                        case 16: if (wire == 1) { m.LinearAccelerationMps2Z = Double(); continue; } break;
                    }
                    SkipType(wire, 0, field);
                }
                return m;
            }

            // reads up to four bytes when fewer than five are left (protobuf.js's slow path)
            uint Short()
            {
                uint v = 0;
                for (int r = 0; r < 4; r++)
                {
                    if (pos >= len) throw Range();
                    byte n = buf[pos++];
                    v |= (uint)(n & 127) << (7 * r);
                    if (n < 128) return v;
                }
                throw Range();
            }

            uint First4(out bool done)
            {
                uint r = (uint)(buf[pos] & 127);
                done = true;
                if (buf[pos++] < 128) return r;
                r |= (uint)(buf[pos] & 127) << 7;
                if (buf[pos++] < 128) return r;
                r |= (uint)(buf[pos] & 127) << 14;
                if (buf[pos++] < 128) return r;
                r |= (uint)(buf[pos] & 127) << 21;
                if (buf[pos++] < 128) return r;
                done = false;
                return r;
            }

            public uint UInt32()
            {
                if (len - pos < 5)
                {
                    if (pos >= len) throw Range();
                    if (buf[pos] >= 128) return Short();
                }
                bool done;
                uint r = First4(out done);
                if (done) return r;
                r |= (uint)(buf[pos] & 15) << 28;
                if (buf[pos++] < 128) return r;
                for (int n = 0; n < 5; n++)
                {
                    if (pos >= len) throw Range();
                    if (buf[pos++] < 128) return r;
                }
                throw new FormatException("invalid varint encoding");
            }

            uint Tag()
            {
                if (len - pos < 5)
                {
                    if (pos >= len) throw Range();
                    if (buf[pos] >= 128) return Short();
                }
                bool done;
                uint r = First4(out done);
                if (done) return r;
                r |= (uint)(buf[pos] & 15) << 28;
                byte b = buf[pos++];
                if (b < 128 && (b & 112) == 0) return r;
                throw new FormatException("invalid tag encoding");
            }

            uint Size()
            {
                if (len - pos < 5)
                {
                    if (pos >= len) throw Range();
                    if (buf[pos] >= 128) return Short();
                }
                bool done;
                uint r = First4(out done);
                if (done) return r;
                byte b = buf[pos++];
                if (b > 7) throw new FormatException("invalid length encoding");
                return r | ((uint)b << 28);
            }

            // LongBits: a 64-bit varint, then lo + hi * 2^32 (unsigned)
            double UInt64()
            {
                ulong v = 0;
                if (!(len - pos > 4))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (pos >= len) throw Range();
                        byte b = buf[pos++];
                        v |= (ulong)(b & 127) << (7 * i);
                        if (b < 128) return v;
                    }
                    throw Range();
                }
                for (int i = 0; i < 4; i++)
                {
                    byte b = buf[pos++];
                    v |= (ulong)(b & 127) << (7 * i);
                    if (b < 128) return v;
                }
                {
                    byte b = buf[pos++];
                    v |= (ulong)(b & 127) << 28;
                    if (b < 128) return v;
                }
                if (len - pos > 4)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        byte b = buf[pos++];
                        v |= (ulong)(b & 127) << (7 * i + 35);
                        if (b < 128) return v;
                    }
                }
                else
                {
                    for (int i = 0; i < 5; i++)
                    {
                        if (pos >= len) throw Range();
                        byte b = buf[pos++];
                        v |= (ulong)(b & 127) << (7 * i + 35);
                        if (b < 128) return v;
                    }
                }
                throw new FormatException("invalid varint encoding");
            }

            bool Bool()
            {
                bool any = false;
                for (int r = 0; r < 10; r++)
                {
                    if (pos >= len) throw Range();
                    byte t = buf[pos++];
                    if ((t & 127) != 0) any = true;
                    if (t < 128) return any;
                }
                throw new FormatException("invalid varint encoding");
            }

            double Float()
            {
                if (pos + 4 > len) throw Range();
                float f = BitConverter.ToSingle(LittleEndian(pos, 4), 0);
                pos += 4;
                return f;
            }

            double Double()
            {
                if (pos + 8 > len) throw Range();
                double d = BitConverter.ToDouble(LittleEndian(pos, 8), 0);
                pos += 8;
                return d;
            }

            byte[] LittleEndian(int at, int n)
            {
                byte[] b = new byte[n];
                Array.Copy(buf, at, b, 0, n);
                if (!BitConverter.IsLittleEndian) Array.Reverse(b);
                return b;
            }

            void Skip(int n)
            {
                if (pos + (long)n > len) throw Range();
                pos += n;
            }

            void SkipVarint()
            {
                do
                {
                    if (pos >= len) throw Range();
                }
                while ((buf[pos++] & 128) != 0);
            }

            void SkipType(uint wire, int depth, uint field)
            {
                if (depth > RecursionLimit) throw new FormatException("max depth exceeded");
                if (field == 0) throw new FormatException("illegal tag: field number 0");
                switch (wire)
                {
                    case 0: SkipVarint(); break;
                    case 1: Skip(8); break;
                    case 2:
                        {
                            uint n = Size();
                            if (n > int.MaxValue) throw Range();
                            Skip((int)n);
                            break;
                        }
                    case 3:
                        while (true)
                        {
                            uint t = Tag();
                            uint f = t >> 3;
                            if (f == 0) throw new FormatException("illegal tag: field number 0");
                            uint w = t & 7;
                            if (w == 4)
                            {
                                if (f != field) throw new FormatException("invalid end group tag");
                                break;
                            }
                            SkipType(w, depth + 1, f);
                        }
                        break;
                    case 5: Skip(4); break;
                    default: throw new FormatException("invalid wire type " + wire);
                }
            }
        }
    }

    /// <summary>A frame's telemetry and when the frame shows (ms into the clip).</summary>
    public sealed class TelemetrySample
    {
        public double TimestampMs;
        public Sei Sei;
    }

    /// <summary>A point of the map's path: where the car was, and whether Autopilot drove.</summary>
    public sealed class MapPoint
    {
        public double Lat;
        public double Lon;
        public double TimestampMs;
        public bool Autopilot;
    }

    /// <summary>
    /// A clip's telemetry as the web viewer holds it
    /// (src/renderer/scripts/core/seiExtractor.js): the samples and the
    /// map path, both in presentation order, and findSeiAtTime.
    /// </summary>
    public sealed class ClipTelemetry
    {
        public static readonly ClipTelemetry Empty = new ClipTelemetry(new List<TelemetrySample>(), new List<MapPoint>());

        /// <summary>findSeiAtTime's default: past this a sample is too far from the frame (parked footage).</summary>
        public const double MaxStaleMs = 3000;

        readonly List<TelemetrySample> samples;
        readonly List<MapPoint> mapPath;

        ClipTelemetry(List<TelemetrySample> samples, List<MapPoint> mapPath)
        {
            this.samples = samples;
            this.mapPath = mapPath;
        }

        public IReadOnlyList<TelemetrySample> Samples { get { return samples; } }
        public IReadOnlyList<MapPoint> MapPath { get { return mapPath; } }

        /// <summary>
        /// extractSeiFromServer: GET /api/telemetry's { frames: [[timestamp,
        /// base64 record], ...] }, each record decoded (an undecodable one is
        /// a frame without telemetry), then collectSei. A record that is not
        /// base64 fails the whole clip, as atob() throws in the web: null
        /// (the web shows no telemetry for it and does not cache it).
        /// </summary>
        public static ClipTelemetry FromServer(JsonValue reply)
        {
            List<double> times = new List<double>();
            List<Sei> seis = new List<Sei>();
            foreach (JsonValue f in (reply == null ? JsonValue.Null : reply["frames"]).Items)
            {
                byte[] bytes = Atob(JsString(f[1]));
                if (bytes == null) return null;
                times.Add(f[0].Num);
                seis.Add(Sei.Decode(bytes));
            }
            return Collect(times, seis);
        }

        /// <summary>collectSei: frames with telemetry, and the map path from those with a GPS fix, each sorted by time (stably).</summary>
        public static ClipTelemetry Collect(IList<double> timestamps, IList<Sei> seis)
        {
            List<TelemetrySample> data = new List<TelemetrySample>();
            List<MapPoint> path = new List<MapPoint>();
            for (int i = 0; i < seis.Count; i++)
            {
                Sei s = seis[i];
                if (s == null) continue;
                data.Add(new TelemetrySample { TimestampMs = timestamps[i], Sei = s });
                if (HasValidGps(s))
                    path.Add(new MapPoint { Lat = s.LatitudeDeg, Lon = s.LongitudeDeg, TimestampMs = timestamps[i], Autopilot = s.AutopilotState != 0 });
            }
            // MP4 samples are in decode order; lookups need presentation order (Array.sort is stable)
            return new ClipTelemetry(data.OrderBy(d => d.TimestampMs).ToList(), path.OrderBy(p => p.TimestampMs).ToList());
        }

        /// <summary>hasValidGps: finite, not (0, 0), within ±90 / ±180.</summary>
        public static bool HasValidGps(Sei s)
        {
            if (s == null) return false;
            double lat = s.LatitudeDeg, lon = s.LongitudeDeg;
            if (double.IsNaN(lat) || double.IsInfinity(lat) || double.IsNaN(lon) || double.IsInfinity(lon)) return false;
            if (lat == 0 && lon == 0) return false;
            if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return false;
            return true;
        }

        /// <summary>findSeiAtTime: the sample nearest to this time (the later one on a tie loses), null past maxStaleMs.</summary>
        public Sei At(double timestampMs, double maxStaleMs = MaxStaleMs)
        {
            int i = IndexAt(timestampMs, maxStaleMs);
            return i < 0 ? null : samples[i].Sei;
        }

        /// <summary>The index in <see cref="Samples"/> findSeiAtTime picks, or -1.</summary>
        public int IndexAt(double timestampMs, double maxStaleMs = MaxStaleMs)
        {
            if (samples.Count == 0) return -1;
            int lo = 0, hi = samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (samples[mid].TimestampMs <= timestampMs) lo = mid;
                else hi = mid - 1;
            }
            int closest = lo;
            if (lo + 1 < samples.Count)
            {
                double diffLo = Math.Abs(samples[lo].TimestampMs - timestampMs);
                double diffHi = Math.Abs(samples[lo + 1].TimestampMs - timestampMs);
                if (diffHi < diffLo) closest = lo + 1;
            }
            // NaN compares false in both languages: such a sample is kept
            if (Math.Abs(samples[closest].TimestampMs - timestampMs) > maxStaleMs) return -1;
            return closest;
        }

        /// <summary>String(v) for the values a frame's record can be (the server sends strings).</summary>
        static string JsString(JsonValue v)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return v.Bool ? "true" : "false";
                case JsonKind.String:
                case JsonKind.Number: return v.Str;
                default: return null; // arrays and objects: not records (the web's atob of their text would fail or differ)
            }
        }

        /// <summary>
        /// The browser's atob(): ASCII whitespace removed, one or two '=' only
        /// at the end of a length divisible by 4, a length of 1 mod 4 refused,
        /// anything outside the base64 alphabet refused (null). Leftover bits
        /// are dropped.
        /// </summary>
        public static byte[] Atob(string s)
        {
            if (s == null) return null;
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == ' ' || c == '\t' || c == '\n' || c == '\f' || c == '\r') continue;
                sb.Append(c);
            }
            string d = sb.ToString();
            if (d.Length % 4 == 0)
            {
                if (d.EndsWith("==", StringComparison.Ordinal)) d = d.Substring(0, d.Length - 2);
                else if (d.EndsWith("=", StringComparison.Ordinal)) d = d.Substring(0, d.Length - 1);
            }
            if (d.Length % 4 == 1) return null;
            List<byte> outBytes = new List<byte>(d.Length * 3 / 4);
            int acc = 0, bits = 0;
            foreach (char c in d)
            {
                int v;
                if (c >= 'A' && c <= 'Z') v = c - 'A';
                else if (c >= 'a' && c <= 'z') v = c - 'a' + 26;
                else if (c >= '0' && c <= '9') v = c - '0' + 52;
                else if (c == '+') v = 62;
                else if (c == '/') v = 63;
                else return null;
                acc = (acc << 6) | v;
                bits += 6;
                if (bits >= 8)
                {
                    bits -= 8;
                    outBytes.Add((byte)((acc >> bits) & 0xff));
                }
            }
            return outBytes.ToArray();
        }
    }

    /// <summary>
    /// Clips' telemetry from GET /api/telemetry?path=, kept for the last
    /// eight clips as the web viewer keeps them (SEI_CACHE_MAX), and asked
    /// again with the server's ETag so an unchanged clip is not sent again.
    /// One request per clip at a time (seiInFlight).
    /// </summary>
    public sealed class TelemetryCache
    {
        public const int MaxClips = 8;

        sealed class Entry
        {
            public string ETag;
            public ClipTelemetry Data;
        }

        readonly Api api;
        readonly object gate = new object();
        readonly LinkedList<KeyValuePair<string, Entry>> lru = new LinkedList<KeyValuePair<string, Entry>>();
        readonly Dictionary<string, Task<ClipTelemetry>> inFlight = new Dictionary<string, Task<ClipTelemetry>>();

        public TelemetryCache(Api api)
        {
            if (api == null) throw new ArgumentNullException("api");
            this.api = api;
        }

        /// <summary>The clip's telemetry; <see cref="ClipTelemetry.Empty"/> when the server cannot read it (not cached, as the web).</summary>
        public Task<ClipTelemetry> Get(string serverPath, CancellationToken ct)
        {
            Task<ClipTelemetry> running;
            lock (gate)
            {
                if (inFlight.TryGetValue(serverPath, out running)) return running;
                running = Load(serverPath, ct);
                if (!running.IsCompleted) inFlight[serverPath] = running;
            }
            return running;
        }

        async Task<ClipTelemetry> Load(string serverPath, CancellationToken ct)
        {
            await Task.Yield();
            Entry cached = Find(serverPath);
            try
            {
                Api.Conditional reply = await api.GetIfChanged("/api/telemetry?path=" + Uri.EscapeDataString(serverPath), cached == null ? null : cached.ETag, ct).ConfigureAwait(false);
                if (reply.NotModified && cached != null) return cached.Data;
                ClipTelemetry data = ClipTelemetry.FromServer(JsonValue.TryParse(reply.Text) ?? JsonValue.Object());
                if (data == null) return ClipTelemetry.Empty;
                Store(serverPath, new Entry { ETag = reply.ETag, Data = data });
                return data;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // a transient failure: no telemetry for now, asked again next time
                return ClipTelemetry.Empty;
            }
            finally
            {
                lock (gate) inFlight.Remove(serverPath);
            }
        }

        Entry Find(string key)
        {
            lock (gate)
            {
                for (LinkedListNode<KeyValuePair<string, Entry>> n = lru.First; n != null; n = n.Next)
                {
                    if (n.Value.Key != key) continue;
                    lru.Remove(n);
                    lru.AddLast(n);
                    return n.Value.Value;
                }
                return null;
            }
        }

        void Store(string key, Entry e)
        {
            lock (gate)
            {
                for (LinkedListNode<KeyValuePair<string, Entry>> n = lru.First; n != null; n = n.Next)
                    if (n.Value.Key == key) { lru.Remove(n); break; }
                lru.AddLast(new KeyValuePair<string, Entry>(key, e));
                while (lru.Count > MaxClips) lru.RemoveFirst();
            }
        }
    }
}
