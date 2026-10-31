using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SentrySix.Core
{
    /// <summary>
    /// URLSearchParams as the browser writes and reads it
    /// (application/x-www-form-urlencoded, WHATWG URL): the pairs in order,
    /// UTF-8, a space as "+", only A-Z a-z 0-9 * - . _ left as they are.
    /// </summary>
    public static class FormQuery
    {
        public static string Encode(IList<KeyValuePair<string, string>> pairs)
        {
            StringBuilder b = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in pairs)
            {
                if (b.Length > 0) b.Append('&');
                Escape(b, kv.Key);
                b.Append('=');
                Escape(b, kv.Value);
            }
            return b.ToString();
        }

        static void Escape(StringBuilder b, string s)
        {
            // (a lone surrogate is U+FFFD, as the USVString conversion makes it)
            byte[] bytes = new UTF8Encoding(false, false).GetBytes(s ?? "");
            foreach (byte c in bytes)
            {
                if (c == 0x20) b.Append('+');
                else if (c == 0x2A || c == 0x2D || c == 0x2E || c == 0x5F || (c >= 0x30 && c <= 0x39) || (c >= 0x41 && c <= 0x5A) || (c >= 0x61 && c <= 0x7A)) b.Append((char)c);
                else b.Append('%').Append(c.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>new URLSearchParams(query): a leading "?" dropped, empty parts skipped, "+" a space, %XX bytes (a broken one kept as it is), UTF-8 with U+FFFD.</summary>
        public static List<KeyValuePair<string, string>> Parse(string query)
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            string q = query ?? "";
            if (q.StartsWith("?")) q = q.Substring(1);
            foreach (string part in q.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string name = eq < 0 ? part : part.Substring(0, eq);
                string value = eq < 0 ? "" : part.Substring(eq + 1);
                list.Add(new KeyValuePair<string, string>(Unescape(name), Unescape(value)));
            }
            return list;
        }

        static string Unescape(string s)
        {
            s = s.Replace('+', ' ');
            // the string's UTF-8 bytes, with each valid %XX as its byte
            byte[] raw = Encoding.UTF8.GetBytes(s);
            List<byte> bytes = new List<byte>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == (byte)'%' && i + 2 < raw.Length && Hex(raw[i + 1]) >= 0 && Hex(raw[i + 2]) >= 0)
                {
                    bytes.Add((byte)(Hex(raw[i + 1]) * 16 + Hex(raw[i + 2])));
                    i += 2;
                }
                else bytes.Add(raw[i]);
            }
            return Utf8(bytes);
        }

        /// <summary>The Encoding standard's UTF-8 decode: each maximal broken part is one U+FFFD.</summary>
        static string Utf8(List<byte> b)
        {
            StringBuilder s = new StringBuilder(b.Count);
            int i = 0;
            while (i < b.Count)
            {
                int c = b[i];
                if (c < 0x80) { s.Append((char)c); i++; continue; }
                int need, cp, lower = 0x80, upper = 0xBF;
                if (c >= 0xC2 && c <= 0xDF) { need = 1; cp = c & 0x1F; }
                else if (c >= 0xE0 && c <= 0xEF) { need = 2; cp = c & 0x0F; if (c == 0xE0) lower = 0xA0; if (c == 0xED) upper = 0x9F; }
                else if (c >= 0xF0 && c <= 0xF4) { need = 3; cp = c & 0x07; if (c == 0xF0) lower = 0x90; if (c == 0xF4) upper = 0x8F; }
                else { s.Append('\uFFFD'); i++; continue; }
                int j = i + 1;
                bool ok = true;
                for (int k = 0; k < need; k++, j++)
                {
                    if (j >= b.Count || b[j] < lower || b[j] > upper) { ok = false; break; }
                    cp = (cp << 6) | (b[j] & 0x3F);
                    lower = 0x80; upper = 0xBF;
                }
                if (!ok) { s.Append('\uFFFD'); i = j; continue; }
                s.Append(char.ConvertFromUtf32(cp));
                i = j;
            }
            return s.ToString();
        }

        static int Hex(byte c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// <summary>params.get(name): the first value, null when there is none.</summary>
        public static string Get(IList<KeyValuePair<string, string>> pairs, string name)
        {
            foreach (KeyValuePair<string, string> kv in pairs) if (kv.Key == name) return kv.Value;
            return null;
        }
    }

    /// <summary>One embedded Studio tab of the shell (web/app.js frames[kind]: its .frame-wrap's data-src and data-vehicle, and whether it is hidden).</summary>
    public sealed class StudioFrame
    {
        public string Kind;
        public string Src;
        public string Vehicle;
        public bool Hidden;
    }

    /// <summary>A message the shell posts into a frame ({ type: 'pause' } or { type: 'shown' }).</summary>
    public sealed class StudioPost
    {
        public string Kind;
        public string Type;
    }

    /// <summary>fitFrames(): Studio's viewer drawn at 900 px and scaled down to fit a narrower frame.</summary>
    public sealed class StudioFit
    {
        /// <summary>False: the frame's own size (no style set).</summary>
        public bool Scaled;
        public double Width;
        public double Height;
        public double Scale = 1;
    }

    /// <summary>What a message from the Viewer asks of the shell.</summary>
    public enum StudioAsk { None, ShowInFolder, SelectVehicle }

    /// <summary>
    /// How web/app.js hosts Sentry Studio's Viewer (and Encoder): studioUrl,
    /// openStudio (pendingFrame), showFrame (a tab opened for another vehicle
    /// starts over; the others are hidden and told 'pause', a hidden one shown
    /// again is told 'shown'), hideFrame (render() hides them on every page
    /// that is not embedded), fitFrames and the message listener
    /// ('showInFolder', 'select-vehicle'). The native Viewer is not a frame;
    /// this is its bookkeeping, so it opens, starts over, pauses and comes
    /// back at the same moments. test/windowsNativeViewerHost.test.js runs the
    /// web's own code on the same steps.
    /// </summary>
    public sealed class StudioFrames
    {
        /// <summary>VIEWER_MIN_WIDTH: Studio's viewer needs about 900 px.</summary>
        public const double MinWidth = 900;

        // frames, in the order they were made (Object.entries)
        readonly List<StudioFrame> frames = new List<StudioFrame>();
        readonly Dictionary<string, string> pending = new Dictionary<string, string>();

        public IReadOnlyList<StudioFrame> All { get { return frames; } }

        public StudioFrame Frame(string kind)
        {
            foreach (StudioFrame f in frames) if (f.Kind == kind) return f;
            return null;
        }

        public string Pending(string kind)
        {
            string u;
            return pending.TryGetValue(kind, out u) ? u : null;
        }

        /// <summary>
        /// studioUrl(kind, extra): embed, then the current vehicle's root
        /// (studioRoot || root) and id, or before the vehicles have loaded the
        /// one picked last time, then extra (a key already there keeps its
        /// place, as an object spread does).
        /// </summary>
        public static string Url(string kind, JsonValue current, string savedVehicleId, IList<KeyValuePair<string, string>> extra)
        {
            List<KeyValuePair<string, string>> q = new List<KeyValuePair<string, string>>();
            Put(q, "embed", kind);
            if (current != null && !current.IsNull)
            {
                JsonValue root = current["studioRoot"].Bool ? Member(current, "studioRoot") : Member(current, "root");
                Put(q, "root", JsString(root));
                Put(q, "vehicle", JsString(Member(current, "id")));
            }
            else if (!string.IsNullOrEmpty(savedVehicleId)) Put(q, "vehicle", savedVehicleId);
            if (extra != null) foreach (KeyValuePair<string, string> kv in extra) Put(q, kv.Key, kv.Value);
            return "/studio/" + (kind == "encoder" ? "encoder" : "index") + ".html?" + FormQuery.Encode(q);
        }

        static void Put(List<KeyValuePair<string, string>> q, string key, string value)
        {
            for (int i = 0; i < q.Count; i++)
            {
                if (q[i].Key != key) continue;
                q[i] = new KeyValuePair<string, string>(key, value);
                return;
            }
            q.Add(new KeyValuePair<string, string>(key, value));
        }

        /// <summary>A member, or null (JavaScript's undefined) when the object has none.</summary>
        static JsonValue Member(JsonValue o, string key)
        {
            return o.Has(key) ? o[key] : null;
        }

        /// <summary>String(x), with undefined for a missing member.</summary>
        public static string JsString(JsonValue v)
        {
            if (v == null) return "undefined";
            switch (v.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return v.Bool ? "true" : "false";
                case JsonKind.Number: return Format.JsNum(v.Num);
                case JsonKind.String: return v.Str;
                case JsonKind.Array:
                    {
                        List<string> parts = new List<string>();
                        foreach (JsonValue x in v.Items) parts.Add(x.IsNull ? "" : JsString(x));
                        return string.Join(",", parts);
                    }
                default: return "[object Object]";
            }
        }

        /// <summary>String(current()?.id || ''): the vehicle a frame was opened for.</summary>
        public static string VehicleOf(JsonValue current)
        {
            if (current == null || current.IsNull) return "";
            JsonValue id = Member(current, "id");
            return id != null && id.Bool ? JsString(id) : "";
        }

        /// <summary>openStudio(kind, extra): the next showFrame(kind) opens this URL. The caller then goes to #/kind (or draws it again when it is there).</summary>
        public void Open(string kind, string url)
        {
            pending[kind] = url;
        }

        /// <summary>
        /// showFrame(kind) with the current vehicle: the URL it shows (the
        /// pending one, else the open frame's own when it is for the same
        /// vehicle, else a new studioUrl); true when the frame is (re)made.
        /// The messages posted (pause to the others shown, shown to this one
        /// when it was hidden) are added to <paramref name="posts"/>.
        /// </summary>
        public bool Show(string kind, JsonValue current, string savedVehicleId, List<StudioPost> posts)
        {
            StudioFrame f = Frame(kind);
            string vehicle = VehicleOf(current);
            bool same = f != null && f.Vehicle == vehicle;
            string url = Pending(kind) ?? (same ? f.Src : Url(kind, current, savedVehicleId, null));
            pending.Remove(kind);
            bool made = false;
            if (f == null || f.Src != url)
            {
                StudioFrame next = new StudioFrame { Kind = kind, Src = url, Vehicle = vehicle, Hidden = false };
                // (frames[kind] = wrap keeps the key's place)
                int at = f != null ? frames.IndexOf(f) : -1;
                if (at >= 0) frames[at] = next; else frames.Add(next);
                f = next;
                made = true;
            }
            foreach (StudioFrame x in frames)
            {
                if (x.Kind != kind) Hide(x, posts);
                else if (x.Hidden)
                {
                    x.Hidden = false;
                    if (posts != null) posts.Add(new StudioPost { Kind = x.Kind, Type = "shown" });
                }
            }
            return made;
        }

        /// <summary>hideFrame(el): a shown frame is told 'pause', then hidden.</summary>
        static void Hide(StudioFrame f, List<StudioPost> posts)
        {
            if (!f.Hidden && posts != null) posts.Add(new StudioPost { Kind = f.Kind, Type = "pause" });
            f.Hidden = true;
        }

        /// <summary>render() on a page that is not embedded (and Encoder > Encoding per vehicle): every frame hidden.</summary>
        public void HideAll(List<StudioPost> posts)
        {
            foreach (StudioFrame f in frames) Hide(f, posts);
        }

        /// <summary>render()'s embedded test: #/viewer, or #/encoder without a sub page.</summary>
        public static bool Embedded(string route, string sub)
        {
            return route == "viewer" || (route == "encoder" && string.IsNullOrEmpty(sub));
        }

        /// <summary>fitFrames() for a frame of that size: under 900 px wide, 900 px scaled down (its height grown to fill after the scale); a width of 0 is left alone.</summary>
        public static StudioFit Fit(double width, double height)
        {
            if (width > 0 && width < MinWidth)
            {
                double s = width / MinWidth;
                return new StudioFit { Scaled = true, Width = MinWidth, Height = height / s, Scale = s };
            }
            return new StudioFit { Scaled = false, Width = width, Height = height, Scale = 1 };
        }

        /// <summary>
        /// The shell's message listener: only from the shell's own origin
        /// with source 'sentry-studio'; 'showInFolder' goes to the Footage
        /// page (category all); 'select-vehicle' picks that vehicle when it
        /// is one of the list (strictly equal ids) and opens the Viewer again.
        /// </summary>
        public static StudioAsk Message(JsonValue data, bool sameOrigin, IEnumerable<JsonValue> vehicleIds)
        {
            if (!sameOrigin || data == null || !data.IsObject) return StudioAsk.None;
            JsonValue source = data["source"];
            if (!(source.IsString && source.Str == "sentry-studio")) return StudioAsk.None;
            JsonValue type = data["type"];
            if (!type.IsString) return StudioAsk.None;
            if (type.Str == "showInFolder") return StudioAsk.ShowInFolder;
            if (type.Str == "select-vehicle" && data.Has("id") && vehicleIds != null)
                foreach (JsonValue id in vehicleIds) if (StrictEquals(id, data["id"])) return StudioAsk.SelectVehicle;
            return StudioAsk.None;
        }

        /// <summary>a === b for JSON values (objects and arrays: never, as two parsed values are two objects).</summary>
        public static bool StrictEquals(JsonValue a, JsonValue b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case JsonKind.Null: return true;
                case JsonKind.Bool: return a.Bool == b.Bool;
                case JsonKind.Number: return a.Num == b.Num;
                case JsonKind.String: return a.Str == b.Str;
                default: return false;
            }
        }
    }

    /// <summary>
    /// What the Viewer reads from its address (web/electronApiShim.js):
    /// embed (encoder.html is always the Encoder), the folder it opens
    /// (folder || root || null: Footage > Play recording opens that one
    /// event's folder), the vehicle, and the day to pick (Play day).
    /// </summary>
    public sealed class ViewerStart
    {
        public string Embed;
        public string StartFolder;
        public string Root;
        public string Folder;
        public string VehicleId;
        public string Date;

        public static ViewerStart Parse(string url)
        {
            string u = url ?? "";
            int hash = u.IndexOf('#');
            if (hash >= 0) u = u.Substring(0, hash);
            int q = u.IndexOf('?');
            string path = q >= 0 ? u.Substring(0, q) : u;
            List<KeyValuePair<string, string>> p = FormQuery.Parse(q >= 0 ? u.Substring(q) : "");
            ViewerStart s = new ViewerStart();
            string embed = FormQuery.Get(p, "embed");
            s.Embed = path.EndsWith("/encoder.html", StringComparison.Ordinal) ? "encoder" : string.IsNullOrEmpty(embed) ? "viewer" : embed;
            s.Root = FormQuery.Get(p, "root");
            s.Folder = FormQuery.Get(p, "folder");
            string start = !string.IsNullOrEmpty(s.Folder) ? s.Folder : s.Root;
            s.StartFolder = string.IsNullOrEmpty(start) ? null : start;
            s.VehicleId = FormQuery.Get(p, "vehicle");
            s.Date = FormQuery.Get(p, "date");
            return s;
        }

        /// <summary>The empty screen without a folder.</summary>
        public const string NoVehicle = "Add a vehicle in Settings to see its footage here.";

        /// <summary>The empty screen while Studio reads the folders: "Opening Model 3's footage…" (the vehicle's name when it is in the list).</summary>
        public static string Opening(string name)
        {
            return "Opening " + (!string.IsNullOrEmpty(name) ? name + "'s" : "the") + " footage…";
        }

        public static string CantOpen(string message)
        {
            return "Can't open the footage: " + message;
        }

        public static string NoClips(string folder)
        {
            return "No dashcam clips found in " + folder + ". Footage appears here once the car has recorded some.";
        }

        /// <summary>The retry button under the two messages that say why nothing is shown.</summary>
        public const string TryAgain = "Try again";

        /// <summary>The vehicle picker (a select labelled "Vehicle"): only with two vehicles or more.</summary>
        public static bool ShowPicker(int vehicles)
        {
            return vehicles >= 2;
        }
    }

    /// <summary>
    /// The shim's status timer (every 500 ms after the page has loaded):
    /// once the empty screen is gone it stops; while folders are being read,
    /// none has finished yet, or the last finished under 2 s ago it waits
    /// (up to 180 s); then it says why nothing is shown.
    /// </summary>
    public sealed class ViewerStatus
    {
        public const int EveryMs = 500;
        public const long QuietMs = 2000;
        public const long GiveUpMs = 180000;

        readonly long started;
        public bool Done { get; private set; }

        public ViewerStatus(long startedMs)
        {
            started = startedMs;
        }

        /// <summary>One tick: null while it waits (or when done), else the words to show (with Try again).</summary>
        public string Tick(long nowMs, bool overlayHidden, int readsActive, long readsAtMs, string readError, string startFolder)
        {
            if (Done) return null;
            if (overlayHidden) { Done = true; return null; }
            if (readsActive != 0 || readsAtMs == 0 || nowMs - readsAtMs < QuietMs)
            {
                if (nowMs - started < GiveUpMs) return null;
            }
            Done = true;
            return readError != null ? ViewerStart.CantOpen(readError) : ViewerStart.NoClips(startFolder);
        }
    }

    /// <summary>
    /// Footage > Play day: every 300 ms, once the day list has the wanted day,
    /// pick it (when it is not picked already) and stop; give up after 180 s.
    /// </summary>
    public sealed class DayWish
    {
        public const int EveryMs = 300;
        public const long GiveUpMs = 180000;

        readonly string want;
        readonly long since;
        public bool Done { get; private set; }

        public DayWish(string wantDay, long sinceMs)
        {
            want = wantDay;
            since = sinceMs;
            Done = string.IsNullOrEmpty(wantDay);
        }

        /// <summary>One tick with the days listed (null: no list yet): the day to pick, or null.</summary>
        public string Tick(long nowMs, IList<string> days, string picked)
        {
            if (Done) return null;
            if (days == null || !days.Contains(want))
            {
                if (nowMs - since > GiveUpMs) Done = true;
                return null;
            }
            Done = true;
            return picked != want ? want : null;
        }
    }

    /// <summary>
    /// The Viewer paused while another page is shown (the shim's 'pause' and
    /// 'shown'): put away, it presses its own pause when something plays and
    /// the button is enabled; playback that starts while away is paused again
    /// right after.
    /// </summary>
    public sealed class ViewerAway
    {
        public bool Away { get; private set; }

        /// <summary>'pause': true when the Viewer should press pause now.</summary>
        public bool Pause(bool playing, bool buttonEnabled)
        {
            Away = true;
            return playing && buttonEnabled;
        }

        public void Shown()
        {
            Away = false;
        }

        /// <summary>A video started: true when it is to be paused again (on the next turn).</summary>
        public bool Played()
        {
            return Away;
        }
    }
}
