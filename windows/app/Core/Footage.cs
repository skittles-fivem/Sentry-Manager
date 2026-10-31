using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>A set of ids in the order they were added (a JavaScript Set).</summary>
    public sealed class IdSet
    {
        readonly List<string> order = new List<string>();
        readonly HashSet<string> has = new HashSet<string>();

        public int Count { get { return order.Count; } }
        public bool Contains(string id) { return id != null && has.Contains(id); }
        public void Add(string id) { if (id != null && has.Add(id)) order.Add(id); }
        public void Remove(string id) { if (id != null && has.Remove(id)) order.Remove(id); }
        public void Clear() { order.Clear(); has.Clear(); }
        public List<string> ToList() { return new List<string>(order); }
        public void Toggle(string id) { if (Contains(id)) Remove(id); else Add(id); }
    }

    /// <summary>
    /// What the Footage page keeps between visits (web/app.js state.footage):
    /// the filter, the page of days, the selection, the recordings whose files
    /// are shown, the days opened, and the recordings of the open days.
    /// </summary>
    public sealed class FootageState
    {
        public string Category = "all";
        public string Date = "";
        public int Offset;
        public readonly IdSet Selected = new IdSet();
        public readonly IdSet Expanded = new IdSet();
        public IdSet Open = new IdSet();
        /// <summary>The vehicle the open days belong to (null before the first visit).</summary>
        public string VehicleId;
        /// <summary>The recordings of each open day (dayRecs).</summary>
        public Dictionary<string, List<JsonValue>> DayRecs = new Dictionary<string, List<JsonValue>>();
        /// <summary>Every loaded recording (state.footage.data.recordings).</summary>
        public List<JsonValue> Recordings = new List<JsonValue>();

        /// <summary>A vehicle pill was picked (the web's page click handler): the first page, nothing selected.</summary>
        public void VehiclePicked()
        {
            Offset = 0;
            Selected.Clear();
        }

        /// <summary>
        /// The render test mode's state: { category, date, offset, vehicleId,
        /// selected: [], expanded: [], open: [] } (the fields left out keep their defaults).
        /// </summary>
        public static FootageState From(JsonValue j)
        {
            FootageState f = new FootageState();
            if (j == null || !j.IsObject) return f;
            if (j["category"].IsString) f.Category = j["category"].Str;
            if (j["date"].IsString) f.Date = j["date"].Str;
            if (j["offset"].IsNumber) f.Offset = (int)j["offset"].Long;
            if (j.Has("vehicleId")) f.VehicleId = j["vehicleId"].IsNull ? null : j["vehicleId"].Str;
            foreach (JsonValue x in j["selected"].Items) f.Selected.Add(x.Str);
            foreach (JsonValue x in j["expanded"].Items) f.Expanded.Add(x.Str);
            foreach (JsonValue x in j["open"].Items) f.Open.Add(x.Str);
            return f;
        }
    }

    // ---------------------------------------------------------------- the models

    /// <summary>A .chip: its text, colour ("", "amber", "green", "blue", "violet"), .cdot and tooltip.</summary>
    public sealed class FootageChip
    {
        public string Text;
        public string Tone = "";
        public bool Dot;
        public string Title;
    }

    /// <summary>One item of a recording's meta row: a chip (Chip set) or plain text.</summary>
    public sealed class FootageMeta
    {
        public string Text;
        /// <summary>Null for plain text.</summary>
        public FootageChip Chip;
    }

    public sealed class FootageRec
    {
        public string Id;
        public JsonValue Raw;
        public bool Selected;
        public string SelectLabel;
        /// <summary>The thumbnail's path (null: the box shows <see cref="ThumbFallback"/>).</summary>
        public string ThumbPath;
        public string ThumbFallback;
        public string When;
        public List<FootageMeta> Meta = new List<FootageMeta>();
        public string Orig;
        public bool Expanded;
        public string FilesLabel;
        public bool Protected;
        public string ProtectLabel;
    }

    public sealed class FootageDay
    {
        public string Day;
        public JsonValue Raw;
        public bool Open;
        public bool AllSelected;
        public string SelectLabel;
        public string ThumbPath;
        public string ThumbFallback;
        public string Count;
        public string Title;
        public List<FootageChip> Chips = new List<FootageChip>();
        public string Meta;
        public string Toggle;
        /// <summary>The recordings when open (empty: "No recordings."); null when closed.</summary>
        public List<FootageRec> Recs;
    }

    public sealed class FootagePager
    {
        public string Text;
        public bool CanPrev, CanNext;
    }

    /// <summary>The page as web/app.js pages.footage draws it from one /days answer.</summary>
    public sealed class FootageView
    {
        /// <summary>No vehicle or a failed /days: only this message under the head (an .empty box).</summary>
        public string Failed;
        public string Category;
        public string Date;
        /// <summary>The date field's min and max (null when there are no dates).</summary>
        public string DateMin, DateMax;
        public string Hint;
        public int SelCount;
        public bool CanTrash, CanProtect, CanUnprotect, CanClear;
        /// <summary>The list's .empty text (null when there are days).</summary>
        public string EmptyText;
        public List<FootageDay> Days = new List<FootageDay>();
        public FootagePager Pager;
    }

    /// <summary>A codec tag of a file row: "checking" (muted), an encoded codec (enc) or a plain one (plain, "Not encoded yet").</summary>
    public sealed class FootageTag
    {
        public string Kind;
        public string Text;
        public string Title;
    }

    public sealed class FootageFile
    {
        public string Rel;
        /// <summary>Null for a file that is not a clip (no codec).</summary>
        public FootageTag Tag;
        public string Size;
        /// <summary>The file's path on the server (GET /api/media?path=).</summary>
        public string Path;
        public string Href;
        public bool Plain;
        public bool Clip;
    }

    /// <summary>A recording's files (loadFiles): the "not encoded yet" head, the event row and the rows.</summary>
    public sealed class FootageFiles
    {
        /// <summary>"N clips of M not encoded yet" (null when every checked clip is encoded).</summary>
        public string Head;
        /// <summary>The event row (null without an event).</summary>
        public string Event;
        public List<FootageFile> Files = new List<FootageFile>();

        public List<FootageFile> Shown(bool onlyPlain)
        {
            if (!onlyPlain) return Files;
            List<FootageFile> l = new List<FootageFile>();
            foreach (FootageFile f in Files) if (f.Plain) l.Add(f);
            return l;
        }
    }

    /// <summary>The words of a confirm dialog (confirmDialog's title, paragraphs, OK button, danger).</summary>
    public sealed class FootageConfirm
    {
        public string Title;
        public List<string> Body = new List<string>();
        public string Ok;
        public bool Danger;
    }

    /// <summary>
    /// web/app.js pages.footage and its helpers (dayCard, recCard, codecTag,
    /// loadFiles, trash, deleteDays, protect, followIndex): the requests, what
    /// each answer means and every word, for the native Footage page to draw.
    /// test/windowsNativeFootage.test.js runs the web's own code on the cases
    /// Core.Tests (FootageTests.cs) runs these on.
    /// </summary>
    public static class Footage
    {
        public const int DaysPerPage = 30;
        public static readonly string[] Categories = { "all", "Recent", "Sentry", "Saved", "Other" };
        public const string IndexTitle = "Read every clip's speed now, for the Viewer's speed graph";
        public const string IndexLabel = "Index speeds";
        public const string NoVehicle = "Add a vehicle in Settings first.";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string CategoryLabel(string c)
        {
            return c == "all" ? "All categories" : c;
        }

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>A value as `${x}` writes it.</summary>
        static string Js(JsonValue v)
        {
            if (v == null) return "undefined";
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.IsString) return v.Str;
            if (v.IsBool) return v.Bool ? "true" : "false";
            return "undefined";
        }

        /// <summary>esc(x): the text (null is "").</summary>
        static string S(JsonValue v)
        {
            if (v == null || v.IsNull) return "";
            return v.Str ?? "";
        }

        /// <summary>str.slice(from, to) (to &lt; 0: to the end).</summary>
        static string Slice(string s, int from, int to)
        {
            if (s == null) return "";
            int a = Math.Min(from, s.Length);
            int b = to < 0 ? s.Length : Math.Min(to, s.Length);
            return b > a ? s.Substring(a, b - a) : "";
        }

        static double N(JsonValue v)
        {
            return v == null ? 0 : v.Num;
        }

        /// <summary>encodeURIComponent.</summary>
        public static string EncUri(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            foreach (byte b in bytes)
            {
                char c = (char)b;
                bool keep = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "-_.!~*'()".IndexOf(c) >= 0;
                if (keep && b < 128) sb.Append(c);
                else sb.Append('%').Append(b.ToString("X2", Inv));
            }
            return sb.ToString();
        }

        /// <summary>URLSearchParams' value encoding (spaces as +).</summary>
        static string Q(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                char c = (char)b;
                if (b < 128 && ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "*-._".IndexOf(c) >= 0)) sb.Append(c);
                else if (c == ' ') sb.Append('+');
                else sb.Append('%').Append(b.ToString("X2", Inv));
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- requests

        /// <summary>GET /api/vehicles/:id/days?category=&amp;offset=&amp;limit=30[&amp;date=].</summary>
        public static string DaysPath(string vehicleId, FootageState f)
        {
            string p = "/api/vehicles/" + vehicleId + "/days?category=" + Q(f.Category) + "&offset=" + f.Offset.ToString(Inv) + "&limit=" + DaysPerPage.ToString(Inv);
            if (!string.IsNullOrEmpty(f.Date)) p += "&date=" + Q(f.Date);
            return p;
        }

        /// <summary>GET /api/vehicles/:id/recordings?category=&amp;date=DAY&amp;limit=500.</summary>
        public static string RecordingsPath(string vehicleId, string category, string day)
        {
            return "/api/vehicles/" + vehicleId + "/recordings?category=" + Q(category) + "&date=" + Q(day) + "&limit=500";
        }

        public static string DetailPath(string vehicleId, string id)
        {
            return "/api/vehicles/" + vehicleId + "/recordings/" + id;
        }

        public static string ThumbPath(string vehicleId, string id)
        {
            return "/api/vehicles/" + EncUri(vehicleId) + "/recordings/" + EncUri(id) + "/thumbnail";
        }

        public static string MediaPath(string path)
        {
            return "/api/media?path=" + EncUri(path);
        }

        static JsonValue Ids(IEnumerable<string> ids)
        {
            List<JsonValue> l = new List<JsonValue>();
            foreach (string id in ids) l.Add(JsonValue.String(id));
            return JsonValue.Array(l);
        }

        public static JsonValue TrashBody(IList<string> ids)
        {
            return JsonValue.Object("ids", Ids(ids));
        }

        public static JsonValue ProtectBody(IList<string> ids, bool on)
        {
            return JsonValue.Object("ids", Ids(ids), "protected", on);
        }

        public static JsonValue TrashDaysBody(string day, bool through, string category, bool preview)
        {
            JsonValue b = JsonValue.Object("day", day, "through", through, "category", category);
            return preview ? b.With("preview", true) : b;
        }

        /// <summary>The page's start (pages.footage): another vehicle than last time starts at its first page with no day open.</summary>
        public static void Begin(FootageState f, string vehicleId)
        {
            if (f.VehicleId != vehicleId)
            {
                f.VehicleId = vehicleId;
                f.Open = new IdSet();
                f.Offset = 0;
            }
        }

        /// <summary>
        /// The /days answer and the recordings of the open days (dayRecs, a
        /// failed list being empty). A failed /days throws (the page shows the message).
        /// </summary>
        public static async Task<JsonValue> Load(IJsonApi api, string vehicleId, FootageState f, CancellationToken ct)
        {
            Begin(f, vehicleId);
            JsonValue data = await api.Get(DaysPath(vehicleId, f), ct);
            List<string> open = new List<string>();
            foreach (JsonValue d in data["days"].Items) if (f.Open.Contains(d["day"].Str)) open.Add(d["day"].Str);
            List<Task<List<JsonValue>>> loads = new List<Task<List<JsonValue>>>();
            foreach (string day in open) loads.Add(DayRecordings(api, vehicleId, f.Category, day, ct));
            List<JsonValue>[] lists = await Task.WhenAll(loads);
            f.DayRecs = new Dictionary<string, List<JsonValue>>();
            f.Recordings = new List<JsonValue>();
            for (int i = 0; i < open.Count; i++)
            {
                f.DayRecs[open[i]] = lists[i];
                f.Recordings.AddRange(lists[i]);
            }
            return data;
        }

        /// <summary>A day's recordings; empty when the list cannot be read.</summary>
        public static async Task<List<JsonValue>> DayRecordings(IJsonApi api, string vehicleId, string category, string day, CancellationToken ct)
        {
            try
            {
                JsonValue x = await api.Get(RecordingsPath(vehicleId, category, day), ct);
                return new List<JsonValue>(x["recordings"].Items);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return new List<JsonValue>(); }
        }

        /// <summary>A day's checkbox: every recording of it selected or not (loading the day when it is not open).</summary>
        public static async Task SelectDay(IJsonApi api, string vehicleId, FootageState f, string day, bool on, CancellationToken ct)
        {
            List<JsonValue> list;
            if (!f.DayRecs.TryGetValue(day, out list)) list = await DayRecordings(api, vehicleId, f.Category, day, ct);
            foreach (JsonValue r in list)
            {
                if (on) f.Selected.Add(r["id"].Str);
                else f.Selected.Remove(r["id"].Str);
            }
        }

        /// <summary>The Category select: the first page again.</summary>
        public static void SetCategory(FootageState f, string c)
        {
            f.Category = c;
            f.Offset = 0;
        }

        /// <summary>The Capture date field: the first page, and that day opens.</summary>
        public static void SetDate(FootageState f, string d)
        {
            f.Date = d ?? "";
            f.Offset = 0;
            if (f.Date.Length > 0) f.Open.Add(f.Date);
        }

        public static void Newer(FootageState f) { f.Offset = Math.Max(0, f.Offset - DaysPerPage); }
        public static void Older(FootageState f) { f.Offset += DaysPerPage; }

        // ---------------------------------------------------------------- the page

        public static string Hint(JsonValue data, long nowMs)
        {
            string s = Format.Plural(N(data["total"]), "day") + " · " + Format.Plural(N(data["recordings"]), "recording");
            if (data["scanning"].Bool) s += " · scanning…";
            s += " · capture times use the vehicle timezone";
            if (data["scannedAt"].Bool) s += " · inventory " + Format.Ago(data["scannedAt"].Long, nowMs);
            return s;
        }

        public static FootageView NoVehicleView()
        {
            FootageView v = new FootageView();
            v.Failed = NoVehicle;
            return v;
        }

        public static FootageView FailedView(string message)
        {
            FootageView v = new FootageView();
            v.Failed = message ?? "";
            return v;
        }

        public static FootageView View(string vehicleId, JsonValue data, FootageState f, bool h24, long nowMs)
        {
            FootageView v = new FootageView();
            v.Category = f.Category;
            v.Date = f.Date;
            IReadOnlyList<JsonValue> dates = data["dates"].Items;
            if (dates.Count > 0)
            {
                v.DateMin = S(dates[dates.Count - 1]);
                v.DateMax = S(dates[0]);
            }
            v.Hint = Hint(data, nowMs);
            v.SelCount = f.Selected.Count;
            v.CanTrash = v.CanProtect = v.CanClear = v.SelCount > 0;
            foreach (JsonValue r in f.Recordings) if (f.Selected.Contains(r["id"].Str) && r["protected"].Bool) v.CanUnprotect = true;
            IReadOnlyList<JsonValue> days = data["days"].Items;
            if (days.Count == 0)
            {
                v.EmptyText = data["error"].Bool ? S(data["error"]) : data["scanning"].Bool ? "Scanning the share…" : "No recordings match. Try another category or date, or refresh the inventory.";
                return v;
            }
            foreach (JsonValue d in days) v.Days.Add(Day(vehicleId, d, f, h24));
            double offset = N(data["offset"]), total = N(data["total"]);
            int n = days.Count;
            if (total > offset + n || offset > 0)
            {
                FootagePager p = new FootagePager();
                p.Text = "days " + Format.JsNum(offset + 1) + "–" + Format.JsNum(offset + n) + " of " + Js(data["total"]);
                p.CanPrev = data["offset"].Bool;
                p.CanNext = total > offset + n;
                v.Pager = p;
            }
            return v;
        }

        static FootageChip Chip(string text, string tone, bool dot, string title)
        {
            FootageChip c = new FootageChip();
            c.Text = text;
            c.Tone = tone ?? "";
            c.Dot = dot;
            c.Title = title;
            return c;
        }

        /// <summary>dayEncodedChip(d).</summary>
        public static FootageChip DayEncoded(JsonValue d)
        {
            if (!d["encodedRecordings"].Bool) return null;
            string label = N(d["encodedRecordings"]) == N(d["recordings"]) ? "Encoded" : "Encoded " + Js(d["encodedRecordings"]) + "/" + Js(d["recordings"]);
            return Chip(label, "blue", true, Js(d["encoded"]) + " of " + Js(d["clips"]) + " clips are H.265 or AV1");
        }

        /// <summary>dayIndexedChip(d).</summary>
        public static FootageChip DayIndexed(JsonValue d)
        {
            JsonValue x = d["speedIndex"];
            if (!x["indexed"].Bool) return null;
            string label = N(x["indexed"]) >= N(x["clips"]) ? "Indexed" : "Indexed " + Js(x["indexed"]) + "/" + Js(x["clips"]);
            return Chip(label, "violet", true, "Speed read from " + Js(x["indexed"]) + " of " + Js(x["clips"]) + " front-camera clips for the Viewer's speed graph");
        }

        public static FootageDay Day(string vehicleId, JsonValue d, FootageState f, bool h24)
        {
            FootageDay x = new FootageDay();
            x.Raw = d;
            x.Day = S(d["day"]);
            x.Open = f.Open.Contains(x.Day);
            List<JsonValue> recs;
            if (!f.DayRecs.TryGetValue(x.Day, out recs)) recs = new List<JsonValue>();
            bool all = recs.Count > 0;
            foreach (JsonValue r in recs) if (!f.Selected.Contains(r["id"].Str)) all = false;
            x.AllSelected = all;
            x.SelectLabel = "Select every recording of " + x.Day;
            x.ThumbPath = d["thumbId"].Bool ? ThumbPath(vehicleId, S(d["thumbId"])) : null;
            x.ThumbFallback = x.Day;
            x.Count = Format.Plural(N(d["recordings"]), "recording");
            x.Title = Format.DayTitle(x.Day);
            JsonValue by = d["byCategory"];
            string[,] cats = { { "Recent", "", "drive" }, { "Sentry", "amber", "Sentry event" }, { "Saved", "green", "Saved event" }, { "Other", "", "other" } };
            for (int i = 0; i < cats.GetLength(0); i++)
                if (by[cats[i, 0]].Bool) x.Chips.Add(Chip(Format.Plural(N(by[cats[i, 0]]), cats[i, 2]), cats[i, 1], false, null));
            FootageChip enc = DayEncoded(d);
            if (enc != null) x.Chips.Add(enc);
            FootageChip idx = DayIndexed(d);
            if (idx != null) x.Chips.Add(idx);
            if (d["protected"].Bool) x.Chips.Add(Chip(Js(d["protected"]) + " protected", "green", false, null));
            string meta = Format.MinutesText(N(d["minutes"]));
            if (d["cameras"].Bool) meta += " · " + Format.Plural(N(d["cameras"]), "camera");
            meta += " · " + Format.Bytes(d["bytes"]);
            if (d["unknown"].Bool) meta += " · checking which clips are encoded…";
            x.Meta = meta;
            x.Toggle = x.Open ? "▾ Hide recordings" : "▸ Show " + Format.Plural(N(d["recordings"]), "recording");
            if (x.Open)
            {
                x.Recs = new List<FootageRec>();
                foreach (JsonValue r in recs) x.Recs.Add(Rec(vehicleId, r, f, h24));
            }
            return x;
        }

        static readonly Dictionary<string, string> CodecNames = new Dictionary<string, string> { { "h265", "H.265" }, { "av1", "AV1" }, { "h264", "H.264" } };

        static string CodecName(string c)
        {
            string n;
            return c != null && CodecNames.TryGetValue(c, out n) ? n : c;
        }

        static bool Encoded(string codec)
        {
            return codec == "h265" || codec == "av1";
        }

        /// <summary>recEncodedChip(r): "Encoded H.265" when every clip is, "Encoded 3/6 clips" when some are.</summary>
        public static FootageChip RecEncoded(JsonValue r)
        {
            JsonValue e = r["encoded"];
            if (!e.Bool || !e["clips"].Bool || !e["encoded"].Bool) return null;
            List<string> names = new List<string>();
            foreach (JsonValue c in e["codecs"].Items) names.Add(CodecName(c.Str) ?? "");
            string name = string.Join(" + ", names);
            return Chip(N(e["encoded"]) == N(e["clips"]) ? "Encoded " + name : "Encoded " + Js(e["encoded"]) + "/" + Js(e["clips"]) + " clips", "blue", true, null);
        }

        static FootageMeta Plain(string text)
        {
            FootageMeta m = new FootageMeta();
            m.Text = text;
            return m;
        }

        public static FootageRec Rec(string vehicleId, JsonValue r, FootageState f, bool h24)
        {
            FootageRec x = new FootageRec();
            x.Raw = r;
            x.Id = S(r["id"]);
            x.Selected = f.Selected.Contains(x.Id);
            x.SelectLabel = "Select " + S(r["start"]);
            string cat = S(r["category"]);
            x.ThumbPath = r["thumbRel"].Bool || r["frontRel"].Bool ? ThumbPath(vehicleId, x.Id) : null;
            x.ThumbFallback = cat;
            string start = S(r["start"]), end = S(r["end"]);
            string when = r["start"].Bool ? Format.ClockText(start, h24) : S(r["day"]);
            if (r["start"].Bool && r["end"].Bool && end != start) when += " – " + Format.ClockText(Slice(end, 11, -1), h24);
            x.When = when;
            FootageMeta catChip = new FootageMeta();
            catChip.Chip = Chip(cat, cat == "Saved" ? "green" : cat == "Sentry" ? "amber" : "", false, null);
            catChip.Text = cat;
            x.Meta.Add(catChip);
            FootageChip enc = RecEncoded(r);
            if (enc != null) { FootageMeta m = new FootageMeta(); m.Chip = enc; m.Text = enc.Text; x.Meta.Add(m); }
            x.Meta.Add(Plain(Format.Plural(N(r["fileCount"]), "file")));
            x.Meta.Add(Plain(Format.Bytes(r["bytes"])));
            if (r["eventAt"].Bool) x.Meta.Add(Plain("event " + Slice(S(r["eventAt"]), 11, -1)));
            if (r["cameras"].Count > 0) x.Meta.Add(Plain(Format.Plural(r["cameras"].Count, "camera")));
            x.Protected = r["protected"].Bool;
            if (x.Protected) { FootageMeta m = new FootageMeta(); m.Chip = Chip("Protected", "green", false, null); m.Text = "Protected"; x.Meta.Add(m); }
            string key = S(r["startKey"]);
            string orig = r["startKey"].Bool ? Slice(key, 0, 10) + "T" + Slice(key, 11, -1).Replace('-', ':') : S(r["day"]);
            x.Orig = "Original capture: " + orig + " · " + S(r["folder"]);
            x.Expanded = f.Expanded.Contains(x.Id);
            x.FilesLabel = x.Expanded ? "Hide files" : "Show files";
            x.ProtectLabel = x.Protected ? "Protected · unprotect" : "Protect recording";
            return x;
        }

        // ---------------------------------------------------------------- files

        /// <summary>codecTag(f): null for a file that is not a clip.</summary>
        public static FootageTag CodecTag(JsonValue file)
        {
            if (!file.Has("codec")) return null;
            FootageTag t = new FootageTag();
            JsonValue c = file["codec"];
            if (!c.Bool)
            {
                t.Kind = "muted";
                t.Text = "checking";
                return t;
            }
            string codec = S(c);
            string n;
            string name = CodecNames.TryGetValue(codec, out n) ? n : codec.ToUpperInvariant();
            t.Text = name;
            if (Encoded(codec)) t.Kind = "enc";
            else { t.Kind = "plain"; t.Title = "Not encoded yet"; }
            return t;
        }

        /// <summary>loadFiles' rows from GET /api/vehicles/:id/recordings/:rid.</summary>
        public static FootageFiles Files(JsonValue detail)
        {
            FootageFiles x = new FootageFiles();
            JsonValue ev = detail["event"];
            if (ev.Bool)
            {
                List<string> parts = new List<string>();
                string reason = Format.Humanize(S(ev["reason"]));
                if (reason.Length > 0) parts.Add(reason);
                if (ev["city"].Bool) parts.Add(S(ev["city"]));
                if (ev["street"].Bool) parts.Add(S(ev["street"]));
                x.Event = parts.Count > 0 ? string.Join(" · ", parts) : "Event";
            }
            int clips = 0, plain = 0;
            foreach (JsonValue f in detail["files"].Items)
            {
                FootageFile r = new FootageFile();
                r.Rel = S(f["rel"]);
                r.Tag = CodecTag(f);
                r.Size = Format.Bytes(f["size"]);
                r.Path = S(f["path"]);
                r.Href = MediaPath(r.Path);
                r.Clip = f.Has("codec");
                r.Plain = r.Clip && f["codec"].Bool && !Encoded(S(f["codec"]));
                if (r.Clip) clips++;
                if (r.Plain) plain++;
                x.Files.Add(r);
            }
            if (plain > 0) x.Head = Format.Plural(plain, "clip") + " of " + clips.ToString(Inv) + " not encoded yet";
            return x;
        }

        // ---------------------------------------------------------------- actions

        public static string ProtectToast(bool on)
        {
            return on ? "Protected: policies and Move to trash will skip it." : "Protection removed.";
        }

        /// <summary>protect(v, ids, on): the toast; the page draws again after.</summary>
        public static async Task Protect(IJsonApi api, string vehicleId, IList<string> ids, bool on, Action<string, bool> toast)
        {
            if (ids.Count == 0) return;
            try
            {
                await api.Post("/api/vehicles/" + vehicleId + "/protect", ProtectBody(ids, on), CancellationToken.None);
                toast(ProtectToast(on), false);
            }
            catch (Exception ex)
            {
                toast(ex.Message, true);
            }
        }

        public static FootageConfirm TrashConfirm(IList<string> ids, IEnumerable<JsonValue> loaded)
        {
            double total = 0;
            bool anyProtected = false;
            HashSet<string> want = new HashSet<string>(ids);
            foreach (JsonValue r in loaded)
            {
                if (!want.Contains(r["id"].Str)) continue;
                total += N(r["bytes"]);
                if (r["protected"].Bool) anyProtected = true;
            }
            FootageConfirm c = new FootageConfirm();
            c.Title = "Move " + Format.Plural(ids.Count, "recording") + " to the Recycle Bin?";
            c.Body.Add(Format.Bytes(total) + " moves to the Recycle Bin on the same share. Nothing is deleted: restore it any time, or delete it forever from the Recycle Bin.");
            if (anyProtected) c.Body.Add("Protected recordings are skipped.");
            c.Ok = "Move to trash";
            return c;
        }

        /// <summary>The toast after POST /trash: its text, and whether it is bad (nothing moved, something skipped).</summary>
        public static KeyValuePair<string, bool> TrashToast(JsonValue answer)
        {
            IReadOnlyList<JsonValue> skipped = answer["skipped"].Items;
            int moved = answer["moved"].Count;
            string why = "";
            if (skipped.Count > 0)
            {
                List<string> reasons = new List<string>();
                foreach (JsonValue s in skipped)
                {
                    string reason = S(s["reason"]);
                    if (!reasons.Contains(reason)) reasons.Add(reason);
                }
                why = " " + skipped.Count.ToString(Inv) + " skipped (" + string.Join(", ", reasons) + ").";
            }
            string text = Format.Plural(moved, "recording") + " moved to the Recycle Bin (" + Format.Bytes(answer["bytes"]) + ")." + why;
            return new KeyValuePair<string, bool>(text, skipped.Count > 0 && moved == 0);
        }

        /// <summary>trash(v, ids): the confirm, POST /trash, the toast; the moved ids leave the selection.</summary>
        public static async Task Trash(IJsonApi api, string vehicleId, FootageState f, IList<string> ids, Func<FootageConfirm, Task<bool>> confirm, Action<string, bool> toast)
        {
            if (ids.Count == 0) return;
            List<string> list = new List<string>(ids);
            if (!await confirm(TrashConfirm(list, f.Recordings))) return;
            try
            {
                JsonValue answer = await api.Post("/api/vehicles/" + vehicleId + "/trash", TrashBody(list), CancellationToken.None);
                KeyValuePair<string, bool> t = TrashToast(answer);
                toast(t.Key, t.Value);
                foreach (string id in list) f.Selected.Remove(id);
            }
            catch (Exception ex)
            {
                toast(ex.Message, true);
            }
        }

        /// <summary>The toast when the preview finds nothing to move (null when there is something).</summary>
        public static string DeletePreviewToast(JsonValue preview)
        {
            if (preview["recordings"].Bool) return null;
            return preview["protected"].Bool ? "Everything there is protected: nothing to move." : "Nothing to move.";
        }

        public static FootageConfirm DeleteConfirm(string day, bool through, string category, JsonValue preview)
        {
            string what = through ? Format.DayTitle(day) + " and every older day" : Format.DayTitle(day);
            string scope = category == "all" ? "" : " (" + category + " only, as filtered)";
            FootageConfirm c = new FootageConfirm();
            c.Title = through ? "Delete through this date?" : "Delete this day?";
            string over = through && N(preview["days"]) > 1 ? " over " + Format.Plural(N(preview["days"]), "day") : "";
            c.Body.Add(Format.Plural(N(preview["recordings"]), "recording") + " from " + what + scope + ", " + Format.Bytes(preview["bytes"]) + over + ", move to the Recycle Bin.");
            string stay = "";
            if (preview["protected"].Bool)
            {
                double p = N(preview["protected"]);
                stay = " " + Format.Plural(p, "protected recording") + " stay" + (p == 1 ? "s" : "") + ".";
            }
            c.Body.Add("Nothing is deleted for good until you empty the Recycle Bin; you can restore it until then." + stay);
            c.Ok = through ? "Delete through this date" : "Delete this day";
            c.Danger = true;
            return c;
        }

        public static string DeleteToast(JsonValue answer)
        {
            return "Moving " + Format.Plural(N(answer["recordings"]), "recording") + " (" + Format.Bytes(answer["bytes"]) + ") to the Recycle Bin. Jobs shows the progress.";
        }

        /// <summary>deleteDays(v, day, through): the preview, the confirm, the move (a job), the toast; the selection is cleared.</summary>
        public static async Task DeleteDays(IJsonApi api, string vehicleId, FootageState f, string day, bool through, Func<FootageConfirm, Task<bool>> confirm, Action<string, bool> toast)
        {
            string path = "/api/vehicles/" + vehicleId + "/trash-days";
            string category = f.Category;
            JsonValue preview;
            try
            {
                preview = await api.Post(path, TrashDaysBody(day, through, category, true), CancellationToken.None);
            }
            catch (Exception ex)
            {
                toast(ex.Message, true);
                return;
            }
            string none = DeletePreviewToast(preview);
            if (none != null)
            {
                toast(none, false);
                return;
            }
            if (!await confirm(DeleteConfirm(day, through, category, preview))) return;
            try
            {
                JsonValue answer = await api.Post(path, TrashDaysBody(day, through, category, false), CancellationToken.None);
                toast(DeleteToast(answer), false);
                f.Selected.Clear();
            }
            catch (Exception ex)
            {
                toast(ex.Message, true);
            }
        }

        /// <summary>The Index speeds button while a pass runs: "Indexing done / total" or "Indexing…".</summary>
        public static string IndexRunning(JsonValue s)
        {
            return s["total"].Bool ? "Indexing " + Format.Locale(N(s["done"])) + " / " + Format.Locale(N(s["total"])) : "Indexing…";
        }

        public static string IndexDone(JsonValue s)
        {
            if (!s["last"].Bool) return "Every clip is indexed already.";
            double n = N(s["last"]);
            return "Speeds indexed for " + Format.Locale(n) + " clip" + (n == 1 ? "" : "s") + ".";
        }

        /// <summary>
        /// followIndex(b, start): starts a pass (start) or follows one already
        /// running, showing its progress on the button (show(label, disabled))
        /// every <paramref name="wait"/> while it runs and the button is on
        /// screen (<paramref name="shown"/>); the toast at the end when it was
        /// started or running. The button gets its label back at the end.
        /// </summary>
        public static async Task FollowIndex(IJsonApi api, bool start, Func<bool> shown, Action<string, bool> show, Func<Task> wait, Action<string, bool> toast)
        {
            try
            {
                JsonValue s = start ? await api.Post("/api/speeds/index", JsonValue.Object(), CancellationToken.None) : await api.Get("/api/speeds/status", CancellationToken.None);
                bool was = s["running"].Bool;
                while (s["running"].Bool && shown())
                {
                    show(IndexRunning(s), true);
                    await wait();
                    s = await api.Get("/api/speeds/status", CancellationToken.None);
                }
                if (shown() && (start || was)) toast(IndexDone(s), false);
            }
            catch (Exception ex)
            {
                if (start) toast(ex.Message, true);
            }
            finally
            {
                show(IndexLabel, false);
            }
        }

        // ---------------------------------------------------------------- the outline (the parity test's view of the page)

        static void T(List<string> o, string s)
        {
            string t = (s ?? "").Trim();
            if (t.Length > 0) o.Add(t);
        }

        static void Btn(List<string> o, string kinds, bool disabled, string label)
        {
            o.Add("[btn" + (string.IsNullOrEmpty(kinds) ? "" : " " + kinds) + (disabled ? " disabled" : "") + "]");
            T(o, label);
        }

        static void ChipOut(List<string> o, FootageChip c)
        {
            o.Add("[chip" + (string.IsNullOrEmpty(c.Tone) ? "" : " " + c.Tone) + "]");
            if (!string.IsNullOrEmpty(c.Title)) o.Add("[title " + c.Title + "]");
            if (c.Dot) o.Add("[dot]");
            T(o, c.Text);
        }

        /// <summary>The page as test/windowsNativeFootage.test.js outline() reads the web's HTML (the files as "Loading…").</summary>
        public static List<string> Outline(FootageView v)
        {
            List<string> o = new List<string>();
            if (v.Failed != null)
            {
                o.Add("[empty]");
                T(o, v.Failed);
                return o;
            }
            o.Add("Category");
            o.Add("[select]");
            if (Array.IndexOf(Categories, v.Category) >= 0) o.Add("[selected " + v.Category + "]");
            o.Add("Capture date");
            o.Add("[date value=" + (v.Date ?? "") + " min=" + (v.DateMin ?? "") + " max=" + (v.DateMax ?? "") + "]");
            if (!string.IsNullOrEmpty(v.Date)) Btn(o, "ghost", false, "Clear date");
            o.Add("[hint]");
            T(o, v.Hint);
            o.Add("[selpanel]");
            T(o, v.SelCount.ToString(Inv) + " selected");
            Btn(o, "", !v.CanTrash, "Move to trash");
            Btn(o, "green", !v.CanProtect, "Protect");
            Btn(o, "", !v.CanUnprotect, "Unprotect");
            Btn(o, "", !v.CanClear, "Clear selection");
            Btn(o, "green", false, "Review conversion");
            o.Add("[btn]");
            o.Add("[title " + IndexTitle + "]");
            o.Add(IndexLabel);
            if (v.EmptyText != null)
            {
                o.Add("[empty]");
                T(o, v.EmptyText);
                return o;
            }
            foreach (FootageDay d in v.Days)
            {
                o.Add(d.Open ? "[day open]" : "[day]");
                o.Add("[check" + (d.AllSelected ? " on" : "") + " " + d.SelectLabel + "]");
                if (d.ThumbPath != null) o.Add("[img " + d.ThumbPath + " | " + d.ThumbFallback + "]");
                T(o, d.Count);
                T(o, d.Title);
                foreach (FootageChip c in d.Chips) ChipOut(o, c);
                T(o, d.Meta);
                Btn(o, "solid", false, "Play day");
                Btn(o, "", false, "Delete this day");
                Btn(o, "", false, "Delete through this date");
                o.Add("[toggle]");
                T(o, d.Toggle);
                if (d.Recs == null) continue;
                if (d.Recs.Count == 0) T(o, "No recordings.");
                foreach (FootageRec r in d.Recs)
                {
                    o.Add(r.Selected ? "[rec selected]" : "[rec]");
                    o.Add("[check" + (r.Selected ? " on" : "") + (r.SelectLabel.Length > 0 ? " " + r.SelectLabel : "") + "]");
                    if (r.ThumbPath != null) o.Add("[img " + r.ThumbPath + " | " + r.ThumbFallback + "]");
                    else T(o, r.ThumbFallback);
                    T(o, r.When);
                    foreach (FootageMeta m in r.Meta)
                    {
                        if (m.Chip != null) ChipOut(o, m.Chip);
                        else T(o, m.Text);
                    }
                    T(o, r.Orig);
                    Btn(o, "", false, "Play recording");
                    Btn(o, "", false, r.FilesLabel);
                    Btn(o, r.Protected ? "green" : "", false, r.ProtectLabel);
                    if (!r.Protected) Btn(o, "ghost", false, "Move to trash");
                    if (r.Expanded)
                    {
                        o.Add("[files]");
                        o.Add("[li]");
                        o.Add("Loading…");
                    }
                }
            }
            if (v.Pager != null)
            {
                o.Add("[pager]");
                Btn(o, "", !v.Pager.CanPrev, "Newer days");
                T(o, v.Pager.Text);
                Btn(o, "", !v.Pager.CanNext, "Older days");
            }
            return o;
        }

        /// <summary>A recording's files as outline() reads loadFiles' list.</summary>
        public static List<string> Outline(FootageFiles x, bool onlyPlain)
        {
            List<string> o = new List<string>();
            if (x.Head != null)
            {
                o.Add("[li head]");
                T(o, x.Head);
                o.Add(onlyPlain ? "[check on]" : "[check]");
                o.Add("Not encoded only");
            }
            if (!onlyPlain && x.Event != null)
            {
                o.Add("[li]");
                T(o, x.Event);
            }
            foreach (FootageFile f in x.Shown(onlyPlain))
            {
                o.Add("[li]");
                if (f.Rel.Length > 0) o.Add("[title " + f.Rel + "]");
                T(o, f.Rel);
                if (f.Tag != null)
                {
                    o.Add("[tag " + f.Tag.Kind + "]");
                    if (f.Tag.Title != null) o.Add("[title " + f.Tag.Title + "]");
                    T(o, f.Tag.Text);
                }
                T(o, f.Size);
                o.Add("[link " + f.Href + "]");
                o.Add("Download");
            }
            return o;
        }
    }
}
