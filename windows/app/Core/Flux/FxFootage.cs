using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>A day in Flux's days pane (flux/pages/footage.js drawDays(): .day), or All days.</summary>
    public sealed class FxFootageDay
    {
        /// <summary>data-day ("" for All days).</summary>
        public string Day;
        public bool All, On;
        public string Title, Size;
        /// <summary>The category bar: each category and its count (flex), in the server's order; its title.</summary>
        public List<KeyValuePair<string, string>> Bar = new List<KeyValuePair<string, string>>();
        public string BarTitle;
        /// <summary>"2 recs · 3 min · 6 cams · 1" (the lock after it when Protected), or All days' "4 recordings".</summary>
        public string Line;
        public bool Protected;
        /// <summary>The encoded track's width ("83.3%") and its text ("15/18 encoded").</summary>
        public string EncWidth, EncText;
        /// <summary>The speed index chip ("Indexed", "Indexed 3/6"), null for none.</summary>
        public string Ix;
        /// <summary>The size line's width under the day ("100.0%").</summary>
        public string SizeWidth;
        public string PlayLabel;
    }

    /// <summary>The days pane as drawDays() last drew it.</summary>
    public sealed class FxFootageDays
    {
        /// <summary>#fo-days-n: "2 days".</summary>
        public string Count;
        public FxFootageDay All;
        public string Error;
        public List<FxFootageDay> Days = new List<FxFootageDay>();
        /// <summary>"No days match." (no day, no failure).</summary>
        public bool NoDays;
    }

    /// <summary>A recording's codec cell: "none" (—), "plain" (H.264, maybe "·checking") or "enc" (names, meter, n/m).</summary>
    public sealed class FxFootageCodec
    {
        public string Kind = "none";
        public string Text;
        public bool Checking;
        /// <summary>The mini meter's width ("50%"), its title ("3/6 clips") and the count ("3/6").</summary>
        public string Meter, MeterTitle, Of;
        public double Fraction;
    }

    /// <summary>A row of the recordings table (as drawn; Cur moves in place with j / k).</summary>
    public sealed class FxFootageRow
    {
        public JsonValue Raw;
        /// <summary>data-id (the id as text) and data-i.</summary>
        public string Id;
        public int I;
        public bool Sel, Cur, Protected;
        /// <summary>The thumbnail's path, null without thumbRel / frontRel.</summary>
        public string Thumb;
        public string Start, Sub, Category, Minutes, Files, Size;
        public string CamsTitle;
        public bool[] Cams = new bool[6];
        public FxFootageCodec Codec;
        public string SelectLabel, PlayLabel, ProtectLabel, TrashLabel, InspectLabel;
    }

    /// <summary>The bulk bar (#fo-bulk) as drawn.</summary>
    public sealed class FxFootageBulk
    {
        public string Count;
        /// <summary>The selection's size, null when nothing is selected.</summary>
        public string Bytes;
        public bool CanProtect, CanUnprotect, CanTrash, CanAll, CanClear;
    }

    /// <summary>The recordings table as drawn: the head, the rows (or the blank row), or a failure's one cell.</summary>
    public sealed class FxFootageTable
    {
        /// <summary>The list could not be read: the table is this message alone.</summary>
        public string Error;
        public bool AllChecked;
        /// <summary>The column sorted (null for none of them), ascending or not.</summary>
        public string SortKey;
        public bool Ascending;
        public List<FxFootageRow> Rows = new List<FxFootageRow>();
        public string Empty;
    }

    /// <summary>A file of the inspector's Files tab.</summary>
    public sealed class FxFootageFile
    {
        public string Name;
        /// <summary>The codec: a tag (Tone "plain" or "enc") or faint "checking" / "asset" (Tone null).</summary>
        public string Codec, Tone;
        public string Size, Href, Path;
    }

    /// <summary>The raw-JSON inspector drawer (inspect(r): drawer({ title, wide })).</summary>
    public sealed class FxFootageDrawer
    {
        public string Title;
        public JsonValue Rec, Detail;
        public bool Loading = true;
        /// <summary>The detail could not be read (the list entry is shown under it).</summary>
        public string Error;
        /// <summary>"sum", "files" or "json".</summary>
        public string Tab = "sum";
        public bool Open = true;
        // the parts of the Summary and Files tabs
        public string Thumb;
        public List<string[]> Kv = new List<string[]>();
        public string EventCoords;
        public bool Protected;
        public List<FxFootageFile> Files = new List<FxFootageFile>();
        /// <summary>JSON.stringify(detail, null, 2) (or of the list entry after a failure).</summary>
        public string Json;
    }

    /// <summary>
    /// Flux's Footage page (examples/ui-packs/flux/web/flux/pages/footage.js):
    /// the head (Refresh inventory, Index speeds following the pass, Review
    /// conversion), the toolbar (category, codec, Protected only, the filter),
    /// the days pane (All days and each day: its size, category bar, encoded
    /// track, speed index, Play, Delete day, …and older), the bulk bar, the
    /// sortable recordings table (thumbnail, start, type, duration, cameras,
    /// files, size, codec, lock, actions), its foot, the keys (j / k, J / K,
    /// Home / End, x, Enter, i, p, Delete, Esc, Ctrl A), the commands, and the
    /// raw-JSON inspector drawer. What the page shows is kept as it was last
    /// drawn (the table's cursor moves in place, as the web's classes do).
    /// Paths and bodies are the same as Horizon's and the built-in page's
    /// (HzFootage, Core/Footage.cs); the words and the order are Flux's.
    /// Checked by test/windowsNativeFluxFootage.test.js and Core.Tests FluxFootageTests.
    /// </summary>
    public sealed class FxFootage
    {
        public const string Title = "Footage";
        public const string LoadingText = "Loading…";
        public const string ScanLabel = "Refresh inventory";
        public const string IndexLabel = "Index speeds";
        public const string IndexTip = "Read every clip's speed now, for the Viewer's speed graph";
        public const string ReviewLabel = "Review conversion";
        public const string ReviewLink = "#/encoder?new=1";
        public const string ProtectedOnly = "Protected only";
        public const string FilterPlaceholder = "Filter: time, folder, event, camera  ( / )";
        public const string FilterLabel = "Filter recordings";
        public const string DaysHead = "Days";
        public const string AllDays = "All days";
        public const string NoDaysText = "No days match.";
        public const string IndexedChip = "Indexed";
        public const string IndexTitle = "Speeds read for the Viewer's speed graph";
        public const string PlayWord = "Play";
        public const string DelDay = "Delete day";
        public const string DelThrough = "…and older";
        public const string EncodedTail = " encoded";
        public const string ScanningTail = " · scanning…";
        public const string InventoryWord = " · inventory ";
        public const string TimesAre = " · times are the vehicle's (";
        public const string SelectEvery = "Select every recording shown";
        public const string ThumbnailWord = "Thumbnail";
        public const string ActionsHead = "Actions";
        public const string NoRecordings = "No recordings match";
        public const string TheFilter = " the filter";
        public const string FootKeys = " · j/k move · x select · Enter play · i inspect";
        public const string ProtectWord = "Protect";
        public const string UnprotectWord = "Unprotect";
        public const string MoveToBin = "Move to Bin";
        public const string SelectAllWord = "Select all";
        public const string ClearWord = "Clear";
        public const string SelectedWord = " selected";
        public const string PlayTip = "Play (Enter)";
        public const string ToTheBin = " to the Recycle Bin";
        public const string TrashTip = "Move to the Recycle Bin (Delete)";
        public const string InspectTip = "Raw JSON (i)";
        public const string ProtectedTitle = "Protected";
        public const string NoVehicleText = "No vehicles yet. Add one in Settings to see its footage.";
        public const string AddVehicle = "Add a vehicle";
        public const string AddVehicleLink = "#/settings/vehicles?add=1";
        public const string ScanToast = "Refreshing the inventory… Jobs shows the progress.";
        public const string ProtectionRemoved = "Protection removed.";
        public const string ProtectedSkip = ": policies and the Recycle Bin skip ";
        public const string StayWhere = "Protected recordings stay where they are.";
        public const string TrashBody = " moves to the Recycle Bin on the same share. Restore it any time; nothing is deleted until you empty the bin.";
        public const string TrashSkipped = " protected recording(s) are skipped.";
        public const string MovedToBin = " moved to the Recycle Bin (";
        public const string AllProtected = "Everything there is protected: nothing to move.";
        public const string NothingToMove = "Nothing to move.";
        public const string DayAsk = "Move this day to the Recycle Bin?";
        public const string ThroughAsk = "Move this day and every older day to the Recycle Bin?";
        public const string AsFiltered = " only, as filtered)";
        public const string NothingDeleted = "Nothing is deleted until you empty the Recycle Bin.";
        public const string DayYes = "Move day";
        public const string ThroughYes = "Move day and older";
        public const string MovingTail = " to the Recycle Bin. Jobs shows the progress.";
        public const string RecordingWord = "Recording ";
        public const string TabSummary = "Summary";
        public const string TabFiles = "Files (";
        public const string TabJson = "Raw JSON";
        public const string ListEntry = "List entry";
        public const string CopyJson = "Copy JSON";
        public const string Copied = "Copied.";
        public const string CopyFailed = "The browser did not allow copying.";
        public const string Checking = "checking";
        public const string Asset = "asset";
        public const string CmdSelect = "Select every recording shown";
        public const string CmdTrash = "Move selection to the Recycle Bin";
        public const string CmdProtect = "Protect selection";
        public const string CmdNotConverted = "Show not-converted only";
        public const string CmdGroup = "Footage";
        public const string CmdDayGroup = "Footage day";

        /// <summary>The category control's buttons: value, label.</summary>
        public static readonly string[][] Cats = { new[] { "all", "All" }, new[] { "Recent", "Recent" }, new[] { "Sentry", "Sentry" }, new[] { "Saved", "Saved" }, new[] { "Other", "Other" } };
        /// <summary>The codec control's buttons.</summary>
        public static readonly string[][] Convs = { new[] { "all", "Any codec" }, new[] { "no", "Not converted" }, new[] { "yes", "Converted" } };
        /// <summary>COLS: the sortable columns (key, label; the lock column has none).</summary>
        public static readonly string[][] Cols = { new[] { "start", "Start" }, new[] { "category", "Type" }, new[] { "minutes", "Dur" }, new[] { "cameras", "Cams" }, new[] { "fileCount", "Files" }, new[] { "bytes", "Size" }, new[] { "enc", "Codec" }, new[] { "protected", "" } };
        /// <summary>The inspector's Files table heads.</summary>
        public static readonly string[] FileCols = { "file", "codec", "size" };
        /// <summary>CAMERA_LABEL and CAMERA_SHORT, in CAMERAS order.</summary>
        public static readonly string[] CamLabels = { "Front", "Back", "Left rep.", "Right rep.", "Left pillar", "Right pillar" };
        public static readonly string[] CamShort = { "F", "B", "LR", "RR", "LP", "RP" };

        // ---------------------------------------------------------------- state (mount()'s f and lets)

        public readonly IJsonApi Api;
        public readonly HzUi Ui;
        public readonly HzCar Car;
        /// <summary>v.timezone (null or "" for none: "local").</summary>
        public string Timezone;
        /// <summary>Date.now().</summary>
        public Func<long> Now = Format.NowMs;
        /// <summary>clockPref.h24.</summary>
        public bool H24;
        /// <summary>store.set(key, value): the key ("fo:cat") and the value as JSON.</summary>
        public Action<string, string> Store = delegate { };
        /// <summary>navigator.clipboard.writeText (throws when it is not allowed).</summary>
        public Action<string> Clip = delegate { };

        public string Cat = "all", Date = "", Conv = "all";
        public bool Prot;
        /// <summary>f.text: the filter (100 ms after the last key).</summary>
        public string Text = "";
        /// <summary>f.sort: its key and dir as stored (C# null: undefined).</summary>
        public JsonValue SortKey = JsonValue.String("start"), SortDir = JsonValue.Number(-1);
        readonly List<JsonValue> sel = new List<JsonValue>();
        public int Cursor;
        /// <summary>f.anchor (null: none).</summary>
        public int? Anchor;

        public JsonValue Days;
        public List<JsonValue> Recs = new List<JsonValue>();
        /// <summary>total (C# null: undefined).</summary>
        public JsonValue Total = JsonValue.Number(0);
        public List<JsonValue> Shown = new List<JsonValue>();
        bool alive = true;

        // what the page shows as last drawn
        public string Sum = LoadingText;
        public FxFootageDays DaysDrawn;
        public FxFootageTable Table;
        public FxFootageBulk Bulk;
        public string Foot = "";
        public FxFootageDrawer Drawer;

        // Index speeds
        public string IndexText = IndexLabel;
        public bool IndexBusy;
        bool following, followStart, followWas;

        public FxFootage(IJsonApi api, HzUi ui, HzCar car)
        {
            Api = api;
            Ui = ui ?? new HzUi();
            Car = car;
        }

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>owner[key], C# null when it is not there (undefined).</summary>
        static JsonValue Get(JsonValue owner, string key)
        {
            return Hz.Missing(owner, key) ? null : owner[key];
        }

        /// <summary>String(x): undefined "undefined", null "null".</summary>
        static string S(JsonValue v)
        {
            if (v == null) return "undefined";
            return v.IsNull ? "null" : Hz.Js(v);
        }

        static bool Truthy(JsonValue v) { return v != null && v.Bool; }

        /// <summary>Number(x) (undefined NaN).</summary>
        static double N(JsonValue v) { return v == null ? double.NaN : Hz.Number(v); }

        /// <summary>a &gt; b, a &lt; b as JavaScript compares (two strings by their code units, else as numbers): 1, -1, 0 (0 also for NaN).</summary>
        static int Cmp(JsonValue a, JsonValue b)
        {
            if (a != null && b != null && a.IsString && b.IsString)
            {
                int c = string.CompareOrdinal(a.Str, b.Str);
                return c > 0 ? 1 : c < 0 ? -1 : 0;
            }
            double x = N(a), y = N(b);
            return x > y ? 1 : x < y ? -1 : 0;
        }

        /// <summary>a === b for the values a page compares (objects by reference).</summary>
        static bool StrictEq(JsonValue a, JsonValue b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case JsonKind.Number: return a.Num == b.Num;
                case JsonKind.String: return a.Str == b.Str;
                case JsonKind.Bool: return a.Bool == b.Bool;
                case JsonKind.Null: return true;
                default: return ReferenceEquals(a, b);
            }
        }

        /// <summary>list.reduce((n, x) =&gt; n + x[key], 0): a number, or text once a string was added.</summary>
        static JsonValue Sum0(IEnumerable<JsonValue> list, string key)
        {
            double n = 0;
            StringBuilder s = null;
            foreach (JsonValue x in list)
            {
                JsonValue v = Get(x, key);
                if (s != null) { s.Append(S(v)); continue; }
                if (v != null && v.IsString) { s = new StringBuilder(Format.JsNum(n)).Append(v.Str); continue; }
                if (v != null && (v.IsArray || v.IsObject)) { s = new StringBuilder(Format.JsNum(n)).Append(Hz.Js(v)); continue; }
                n += N(v);
            }
            return s != null ? JsonValue.String(s.ToString()) : JsonValue.Number(n);
        }

        /// <summary>The key a Set keeps a value by (SameValueZero for the values ids are).</summary>
        static string KeyOf(JsonValue v)
        {
            if (v == null) return "u";
            if (v.IsArray || v.IsObject) return "o" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v).ToString(CultureInfo.InvariantCulture);
            return ((int)v.Kind).ToString(CultureInfo.InvariantCulture) + ":" + v.ToJson();
        }

        /// <summary>store.get(key, fallback): the stored JSON read, else the fallback (text unreadable as JSON too).</summary>
        public static JsonValue Stored(string json, JsonValue fallback)
        {
            if (json == null) return fallback;
            JsonValue v = JsonValue.TryParse(json);
            return v ?? fallback;
        }

        /// <summary>The stored category or codec filter as the page uses it (String(value)).</summary>
        public static string StoredText(string json, string fallback)
        {
            JsonValue v = Stored(json, JsonValue.String(fallback));
            return S(v);
        }

        /// <summary>The stored sort ({ key, dir }).</summary>
        public void StoredSort(string json)
        {
            JsonValue v = Stored(json, null);
            if (v == null) return;
            SortKey = Get(v, "key");
            SortDir = Get(v, "dir");
            if (!v.IsObject) { SortKey = null; SortDir = null; }
        }

        // ---------------------------------------------------------------- the selection (f.selected, a Set)

        public int SelCount { get { return sel.Count; } }

        public bool Has(JsonValue id)
        {
            string k = KeyOf(id);
            return sel.Any(x => KeyOf(x) == k);
        }

        public void Add(JsonValue id) { if (!Has(id)) sel.Add(id); }

        public void Remove(JsonValue id)
        {
            string k = KeyOf(id);
            sel.RemoveAll(x => KeyOf(x) == k);
        }

        public List<JsonValue> Selected() { return new List<JsonValue>(sel); }

        void Toggle(JsonValue id, bool? on)
        {
            if (on.HasValue ? on.Value : !Has(id)) Add(id); else Remove(id);
        }

        // ---------------------------------------------------------------- requests

        string V { get { return "/api/vehicles/" + Hz.EncId(Car.Id); } }

        /// <summary>GET /api/vehicles/:id/days?category=&amp;limit=100 (Horizon's).</summary>
        public static string DaysPath(string vehicleId, string cat) { return HzFootage.DaysPath(vehicleId, cat); }

        /// <summary>GET /api/vehicles/:id/recordings?category=&amp;limit=500[&amp;date=][&amp;protected=1][&amp;offset=] (Horizon's).</summary>
        public static string RecsPath(string vehicleId, string cat, string day, bool prot, int offset) { return HzFootage.RecsPath(vehicleId, cat, day, prot, offset); }

        /// <summary>A recording's thumbnail.</summary>
        public static string ThumbPath(string vehicleId, JsonValue rid) { return Footage.ThumbPath(vehicleId, S(rid)); }

        /// <summary>mount(): Index speeds asks the status first (not waited for), then the days and the recordings.</summary>
        public async Task Mount()
        {
            Task index = IndexBegin(false);
            await Task.WhenAll(LoadDays(), LoadRecs());
            GC.KeepAlive(index);
        }

        /// <summary>unmount(): no more drawing.</summary>
        public void Unmount() { alive = false; }

        /// <summary>loadDays(): a failure is { days: [], total: 0, recordings: 0, error }; then drawDays().</summary>
        public async Task LoadDays()
        {
            try { Days = await Api.Get(DaysPath(Car.Id, Cat), CancellationToken.None); }
            catch (Exception ex) { Days = JsonValue.Object("days", JsonValue.Array(new JsonValue[0]), "total", 0, "recordings", 0, "error", ex.Message); }
            if (alive) DrawDays();
        }

        static string ReadErr(JsonValue owner, string key, string prop)
        {
            return "Cannot read properties of " + (Hz.Missing(owner, key) ? "undefined" : "null") + " (reading '" + prop + "')";
        }

        static List<JsonValue> List(JsonValue owner, string key)
        {
            JsonValue v = owner[key];
            if (v.IsArray) return new List<JsonValue>(v.Items);
            if (Hz.Missing(owner, key) || v.IsNull) throw new InvalidOperationException(ReadErr(owner, key, "length"));
            return new List<JsonValue>();
        }

        /// <summary>loadRecs(): every page (5000 at most); a failure is the table's one cell; then the sum line and the table.</summary>
        public async Task LoadRecs()
        {
            JsonValue o;
            try
            {
                o = await Api.Get(RecsPath(Car.Id, Cat, Date, Prot, -1), CancellationToken.None);
                List<JsonValue> recs = List(o, "recordings");
                Recs = recs;
                Total = Get(o, "total");
                int off = recs.Count;
                while (off < N(Total) && off < 5000)
                {
                    JsonValue more = await Api.Get(RecsPath(Car.Id, Cat, Date, Prot, off), CancellationToken.None);
                    List<JsonValue> page = List(more, "recordings");
                    if (page.Count == 0) break;
                    recs = recs.Concat(page).ToList();
                    Recs = recs;
                    off += page.Count;
                }
            }
            catch (Exception ex)
            {
                Recs = new List<JsonValue>();
                Total = JsonValue.Number(0);
                Table = new FxFootageTable();
                Table.Error = ex.Message;
                return;
            }
            foreach (JsonValue id in sel.ToList()) if (!Recs.Any(r => StrictEq(Get(r, "id"), id))) Remove(id);
            Sum = Fx.Plural(Get(o, "total") ?? JsonValue.Null, "recording") + (Date.Length > 0 ? " on " + Fx.DayTitle(Date) : "") + InventoryWord + Fx.Ago(o["scannedAt"], Now())
                + (o["scanning"].Bool ? ScanningTail : "") + TimesAre + (string.IsNullOrEmpty(Timezone) ? "local" : Timezone) + ")";
            if (alive) DrawTable();
        }

        // ---------------------------------------------------------------- the days pane

        static string DayTitleOf(JsonValue day)
        {
            if (day != null && day.IsString) return Fx.DayTitle(day.Str) ?? "";
            return Hz.Js(day);
        }

        /// <summary>drawDays().</summary>
        public void DrawDays()
        {
            if (Days == null) return;
            FxFootageDays v = new FxFootageDays();
            JsonValue days = Days;
            List<JsonValue> list = days["days"].IsArray ? new List<JsonValue>(days["days"].Items) : new List<JsonValue>();
            JsonValue total = Get(days, "total");
            v.Count = (total == null || total.IsNull ? "0" : Hz.Js(total)) + " days";
            double maxB = 1;
            foreach (JsonValue d in list) { double b = N(Get(d, "bytes")); if (double.IsNaN(b) || double.IsNaN(maxB)) maxB = double.NaN; else maxB = Math.Max(maxB, b); }
            FxFootageDay all = new FxFootageDay();
            all.All = true;
            all.Day = "";
            all.On = Date.Length == 0;
            all.Title = AllDays;
            all.Size = Fx.Bytes(Sum0(list, "bytes"));
            JsonValue recs = Get(days, "recordings");
            all.Line = Fx.Plural(Truthy(recs) ? recs : JsonValue.Number(0), "recording");
            v.All = all;
            if (days["error"].Bool) v.Error = Hz.Js(days["error"]);
            foreach (JsonValue d in list)
            {
                FxFootageDay a = new FxFootageDay();
                JsonValue day = Get(d, "day");
                a.Day = Hz.Js(day);
                a.On = day != null && day.IsString && Date == day.Str;
                a.Title = DayTitleOf(day);
                a.Size = Fx.Bytes(d["bytes"]);
                List<string> titles = new List<string>();
                foreach (KeyValuePair<string, JsonValue> kv in d["byCategory"].Props)
                {
                    a.Bar.Add(new KeyValuePair<string, string>(kv.Key, S(kv.Value)));
                    titles.Add(kv.Key + ": " + S(kv.Value));
                }
                a.BarTitle = string.Join(", ", titles);
                a.Protected = d["protected"].Bool;
                a.Line = Fx.Plural(Get(d, "recordings") ?? JsonValue.Null, "rec") + " · " + S(Get(d, "minutes")) + " min · " + S(Get(d, "cameras")) + " cams" + (a.Protected ? " · " + S(Get(d, "protected")) : "");
                double encP = d["clips"].Bool ? N(Get(d, "encoded")) / N(Get(d, "clips")) * 100 : 0;
                a.EncWidth = Format.ToFixed(encP, 1) + "%";
                a.EncText = S(Get(d, "encoded")) + "/" + S(Get(d, "clips")) + EncodedTail;
                JsonValue si = d["speedIndex"];
                if (si["indexed"].Bool) a.Ix = Cmp(Get(si, "indexed"), Get(si, "clips")) >= 0 && !Unordered(Get(si, "indexed"), Get(si, "clips")) ? IndexedChip : IndexedChip + " " + S(Get(si, "indexed")) + "/" + S(Get(si, "clips"));
                a.SizeWidth = Format.ToFixed(N(Get(d, "bytes")) / maxB * 100, 1) + "%";
                a.PlayLabel = "Play " + Hz.Js(day);
                v.Days.Add(a);
            }
            v.NoDays = list.Count == 0 && !days["error"].Bool;
            DaysDrawn = v;
        }

        /// <summary>a &gt;= b is false for NaN (Cmp gives 0 for it).</summary>
        static bool Unordered(JsonValue a, JsonValue b)
        {
            if (a != null && b != null && a.IsString && b.IsString) return false;
            return double.IsNaN(N(a)) || double.IsNaN(N(b));
        }

        /// <summary>pickDay(day): the day (or "" for every day), the days drawn, the recordings again.</summary>
        public Task PickDay(string day)
        {
            Date = day ?? "";
            Cursor = 0;
            DrawDays();
            return LoadRecs();
        }

        // ---------------------------------------------------------------- the table

        static double EncOf(JsonValue r)
        {
            JsonValue e = r["encoded"];
            return e["clips"].Bool ? N(Get(e, "encoded")) / N(Get(e, "clips")) : 0;
        }

        static JsonValue SortVal(JsonValue r, string key)
        {
            if (key == "enc") return JsonValue.Number(EncOf(r));
            if (key == "cameras") return JsonValue.Number(r["cameras"].Count);
            if (key == "protected") return JsonValue.Number(r["protected"].Bool ? 1 : 0);
            JsonValue v = Get(r, key);
            return v == null || v.IsNull ? JsonValue.String("") : v;
        }

        static string Lower(string s) { return (s ?? "").ToLowerInvariant(); }

        static string Join(JsonValue arr, string sep)
        {
            return string.Join(sep, arr.Items.Select(x => Hz.Js(x)));
        }

        /// <summary>filtered(): the codec filter, the text, then the sort (the newest start first among equals).</summary>
        List<JsonValue> Filtered()
        {
            string q = Lower((Text ?? "").Trim());
            List<JsonValue> list = new List<JsonValue>();
            foreach (JsonValue r in Recs)
            {
                JsonValue e = r["encoded"];
                double enc = N(Get(e, "encoded"));
                if (Conv == "yes" && !(enc > 0)) continue;
                if (Conv == "no" && enc > 0 && StrictEq(Get(e, "encoded"), Get(e, "clips"))) continue;
                if (q.Length > 0)
                {
                    string text = S(Get(r, "start")) + " " + S(Get(r, "end")) + " " + S(Get(r, "folder")) + " " + S(Get(r, "category")) + " " + Hz.Or(r["eventId"]) + " " + Join(r["cameras"], " ") + " " + S(Get(r, "id"));
                    if (!Lower(text).Contains(q)) continue;
                }
                list.Add(r);
            }
            string key = S(SortKey);
            double dir = N(SortDir);
            Comparison<JsonValue> cmp = (a, b) =>
            {
                double p = Cmp(SortVal(a, key), SortVal(b, key)) * dir;
                if (p != 0 && !double.IsNaN(p)) return p > 0 ? 1 : -1;
                int t = string.CompareOrdinal(S(Get(a, "start")), S(Get(b, "start")));
                return t > 0 ? -1 : t < 0 ? 1 : 0;
            };
            return list.Select((r, i) => new KeyValuePair<int, JsonValue>(i, r)).OrderBy(x => x, Comparer<KeyValuePair<int, JsonValue>>.Create((x, y) => { int c = cmp(x.Value, y.Value); return c != 0 ? c : x.Key.CompareTo(y.Key); })).Select(x => x.Value).ToList();
        }

        static string CodecName(JsonValue c)
        {
            string s = c != null && c.IsString ? c.Str : null;
            switch (s)
            {
                case "h264": return "H.264";
                case "h265": return "H.265";
                case "av1": return "AV1";
                case "hevc": return "H.265";
                default: return S(c);
            }
        }

        static string CamLabel(JsonValue c)
        {
            int i = c != null && c.IsString ? Array.IndexOf(Fx.Cameras, c.Str) : -1;
            return i >= 0 ? CamLabels[i] : Hz.Js(c);
        }

        static FxFootageCodec CodecOf(JsonValue r)
        {
            JsonValue e = r["encoded"];
            FxFootageCodec c = new FxFootageCodec();
            if (!e["clips"].Bool) return c;
            if (!e["encoded"].Bool) { c.Kind = "plain"; c.Text = "H.264"; c.Checking = e["unknown"].Bool; return c; }
            c.Kind = "enc";
            string names = string.Join("+", e["codecs"].Items.Select(CodecName));
            c.Text = names.Length > 0 ? names : "enc";
            c.Fraction = N(Get(e, "encoded")) / N(Get(e, "clips"));
            c.Meter = Format.ToFixed(c.Fraction * 100, 0) + "%";
            c.Of = S(Get(e, "encoded")) + "/" + S(Get(e, "clips"));
            c.MeterTitle = c.Of + " clips";
            return c;
        }

        /// <summary>drawTable(): the rows of what the filters show, f.cursor kept in it; the foot and the bulk bar.</summary>
        public void DrawTable()
        {
            Shown = Filtered();
            Cursor = Math.Max(0, Math.Min(Cursor, Shown.Count - 1));
            FxFootageTable t = new FxFootageTable();
            t.AllChecked = Shown.Count > 0 && Shown.All(r => Has(Get(r, "id")));
            t.SortKey = SortKey != null && SortKey.IsString ? SortKey.Str : null;
            t.Ascending = N(SortDir) > 0;
            for (int i = 0; i < Shown.Count; i++)
            {
                JsonValue r = Shown[i];
                FxFootageRow w = new FxFootageRow();
                w.Raw = r;
                w.Id = Hz.Js(r["id"]);
                w.I = i;
                w.Sel = Has(Get(r, "id"));
                w.Cur = i == Cursor;
                w.Protected = r["protected"].Bool;
                if (r["thumbRel"].Bool || r["frontRel"].Bool) w.Thumb = ThumbPath(Car.Id, Get(r, "id"));
                w.Start = r["start"].Bool ? Hz.WallDay(Hz.Js(r["start"]), H24) : Hz.Js(r["day"]);
                bool till = r["end"].Bool && !StrictEq(Get(r, "end"), Get(r, "start"));
                w.Sub = (till ? "→ " + Hz.Wall(Hz.Or(r["end"]), H24) : "") + (r["eventAt"].Bool ? " · event " + Hz.Wall(Hz.Or(r["eventAt"]), H24) : "");
                w.Category = Hz.Js(r["category"]);
                w.Minutes = S(Get(r, "minutes")) + "m";
                HashSet<string> have = new HashSet<string>(r["cameras"].Items.Where(c => c.IsString).Select(c => c.Str));
                for (int k = 0; k < Fx.Cameras.Length; k++) w.Cams[k] = have.Contains(Fx.Cameras[k]);
                w.CamsTitle = string.Join(", ", r["cameras"].Items.Select(CamLabel));
                w.Files = S(Get(r, "fileCount"));
                w.Size = Fx.Bytes(r["bytes"]);
                w.Codec = CodecOf(r);
                string start = Hz.Js(r["start"]);
                w.SelectLabel = "Select " + start;
                w.PlayLabel = "Play " + start;
                w.ProtectLabel = (w.Protected ? UnprotectWord : ProtectWord) + " " + start;
                w.TrashLabel = "Move " + start + ToTheBin;
                w.InspectLabel = "Inspect " + start;
                t.Rows.Add(w);
            }
            if (t.Rows.Count == 0) t.Empty = NoRecordings + (Recs.Count > 0 ? TheFilter : "") + ".";
            Table = t;
            Foot = Shown.Count.ToString(CultureInfo.InvariantCulture) + " shown of " + S(Total) + " · sort: " + S(SortKey) + " " + (N(SortDir) > 0 ? "↑" : "↓") + FootKeys;
            DrawBulk();
        }

        void DrawBulk()
        {
            List<JsonValue> s = Recs.Where(r => Has(Get(r, "id"))).ToList();
            FxFootageBulk b = new FxFootageBulk();
            b.Count = s.Count.ToString(CultureInfo.InvariantCulture);
            if (s.Count > 0) b.Bytes = Fx.Bytes(Sum0(s, "bytes"));
            b.CanProtect = s.Count > 0;
            b.CanUnprotect = s.Any(r => r["protected"].Bool);
            b.CanTrash = s.Any(r => !r["protected"].Bool);
            b.CanAll = Shown.Count > 0;
            b.CanClear = s.Count > 0;
            Bulk = b;
        }

        FxFootageRow RowOf(int i)
        {
            if (Table == null || Table.Error != null) return null;
            return i >= 0 && i < Table.Rows.Count ? Table.Rows[i] : null;
        }

        /// <summary>moveCursor(i, extend): the cursor in place, or the range from the anchor selected and the table drawn.</summary>
        public void MoveCursor(int i, bool extend)
        {
            if (Shown.Count == 0) return;
            int prev = Cursor;
            Cursor = Math.Max(0, Math.Min(Shown.Count - 1, i));
            if (extend)
            {
                if (!Anchor.HasValue) Anchor = prev;
                int a = Math.Min(Anchor.Value, Cursor), b = Math.Max(Anchor.Value, Cursor);
                for (int k = a; k <= b; k++) Add(Get(Shown[k], "id"));
                DrawTable();
            }
            else
            {
                Anchor = null;
                FxFootageRow p = RowOf(prev);
                if (p != null) p.Cur = false;
                FxFootageRow n = RowOf(Cursor);
                if (n != null) n.Cur = true;
            }
        }

        /// <summary>The recording of a drawn row (recs.find by its data-id), null when it is gone.</summary>
        public JsonValue RowRec(int i)
        {
            FxFootageRow w = RowOf(i);
            if (w == null) return null;
            return Recs.FirstOrDefault(x => x["id"].IsString && x["id"].Str == w.Id);
        }

        // ---------------------------------------------------------------- the table's clicks

        /// <summary>A column's sort button: the other way round, or the column newest/largest first.</summary>
        public void SortBy(string k)
        {
            if (SortKey != null && SortKey.IsString && SortKey.Str == k) SortDir = JsonValue.Number(-N(SortDir));
            else SortDir = JsonValue.Number(-1);
            SortKey = JsonValue.String(k);
            Store("fo:sort", JsonValue.Object("key", SortKey, "dir", SortDir).ToJson());
            DrawTable();
        }

        /// <summary>The head's checkbox (its new state): every row shown selected or not.</summary>
        public void CheckAll(bool on)
        {
            foreach (JsonValue r in Shown) Toggle(Get(r, "id"), on);
            DrawTable();
        }

        /// <summary>A row's checkbox, ticked (on) or not; with Shift and an anchor the range is selected.</summary>
        public void Check(int i, bool on, bool shift)
        {
            JsonValue r = RowRec(i);
            Cursor = i;
            if (shift && Anchor.HasValue)
            {
                int a = Math.Min(Anchor.Value, Cursor), b = Math.Max(Anchor.Value, Cursor);
                for (int k = a; k <= b; k++) if (k < Shown.Count) Add(Get(Shown[k], "id"));
            }
            else if (r != null) Toggle(Get(r, "id"), on);
            Anchor = Cursor;
            DrawTable();
        }

        /// <summary>A click on a row (not a control): the cursor; on a touch screen it selects too.</summary>
        public void RowClick(int i, bool coarse)
        {
            JsonValue r = RowRec(i);
            Cursor = i;
            if (coarse && r != null) Toggle(Get(r, "id"), null);
            DrawTable();
        }

        /// <summary>A row's play, protect, trash or inspect button.</summary>
        public Task RowAct(int i, string act)
        {
            JsonValue r = RowRec(i);
            Cursor = i;
            if (r == null) return Task.FromResult(0);
            switch (act)
            {
                case "play": Play(r); break;
                case "protect": return Protect(new List<JsonValue> { Get(r, "id") }, !r["protected"].Bool);
                case "trash": return Trash(new List<JsonValue> { Get(r, "id") });
                case "inspect": return Inspect(r);
            }
            return Task.FromResult(0);
        }

        /// <summary>A double click on a row: play it.</summary>
        public void Dbl(int i)
        {
            JsonValue r = RowRec(i);
            if (r != null) Play(r);
        }

        /// <summary>The bulk bar's buttons.</summary>
        public Task BulkAct(string b)
        {
            List<JsonValue> ids = Selected();
            switch (b)
            {
                case "protect": return Protect(ids, true);
                case "unprotect": return Protect(ids, false);
                case "trash": return Trash(ids);
                case "all": foreach (JsonValue r in Shown) Add(Get(r, "id")); DrawTable(); break;
                case "clear": sel.Clear(); DrawTable(); break;
            }
            return Task.FromResult(0);
        }

        // ---------------------------------------------------------------- the filters

        public Task SetCat(string c)
        {
            Cat = c;
            Store("fo:cat", JsonValue.Quote(c));
            return Task.WhenAll(LoadDays(), LoadRecs());
        }

        public void SetConv(string c)
        {
            Conv = c;
            Store("fo:conv", JsonValue.Quote(c));
            DrawTable();
        }

        public Task SetProt(bool on)
        {
            Prot = on;
            return LoadRecs();
        }

        /// <summary>The filter (100 ms after the last key).</summary>
        public void Filter(string text)
        {
            Text = text ?? "";
            Cursor = 0;
            DrawTable();
        }

        // ---------------------------------------------------------------- actions

        /// <summary>play(r): the Viewer with the recording.</summary>
        public void Play(JsonValue r)
        {
            if (r == null) return;
            Ui.Go("#/viewer?rec=" + Hz.EncId(S(Get(r, "id"))));
        }

        /// <summary>A day's Play: the Viewer with the day.</summary>
        public void PlayDay(string day)
        {
            Ui.Go("#/viewer?day=" + Hz.EncId(day));
        }

        /// <summary>Refresh inventory.</summary>
        public Task Scan()
        {
            return Ui.Act(async () =>
            {
                await Api.Post(V + "/scan", JsonValue.Object(), CancellationToken.None);
                Ui.Toast(ScanToast, "ok");
            });
        }

        public async Task Protect(IList<JsonValue> ids, bool on)
        {
            if (ids.Count == 0) return;
            await Ui.Act(async () =>
            {
                await Api.Post(V + "/protect", JsonValue.Object("ids", JsonValue.Array(ids.Select(x => x ?? JsonValue.Null)), "protected", on), CancellationToken.None);
                Ui.Toast(on ? "Protected " + Fx.Plural(ids.Count, "recording") + ProtectedSkip + (ids.Count == 1 ? "it" : "them") + "." : ProtectionRemoved, "ok");
            });
            await LoadRecs();
        }

        public static HzAsk TrashAsk(int movable, JsonValue bytes, int skipped)
        {
            HzAsk a = new HzAsk();
            a.Title = "Move " + Fx.Plural(movable, "recording") + " to the Recycle Bin?";
            a.Para("", new Rich().Bold(Fx.Bytes(bytes)).Text(TrashBody));
            if (skipped > 0) a.Para("muted", new Rich().Text(skipped.ToString(CultureInfo.InvariantCulture) + TrashSkipped));
            a.Yes = MoveToBin;
            a.Danger = true;
            return a;
        }

        /// <summary>The Recycle Bin's answer as its toast: text and tone (an answer without moved fails as the web's does).</summary>
        public static string[] TrashToast(JsonValue o)
        {
            JsonValue sk = Get(o, "skipped");
            int skipped = sk != null && sk.IsArray ? sk.Count : sk != null && sk.IsString ? sk.Str.Length : 0;
            string why = "";
            if (skipped > 0)
            {
                List<string> reasons = new List<string>();
                foreach (JsonValue s in sk.Items) { string x = Hz.Js(s["reason"]); if (!reasons.Contains(x)) reasons.Add(x); }
                why = " " + skipped.ToString(CultureInfo.InvariantCulture) + " skipped (" + string.Join(", ", reasons) + ").";
            }
            JsonValue moved = Get(o, "moved");
            if (moved == null || moved.IsNull) throw new InvalidOperationException(ReadErr(o, "moved", "length"));
            JsonValue n = moved.IsArray ? JsonValue.Number(moved.Count) : moved.IsString ? JsonValue.Number(moved.Str.Length) : null;
            string text = Fx.Plural(n ?? JsonValue.Null, "recording") + MovedToBin + Fx.Bytes(o["bytes"]) + ")." + why;
            return new[] { text, skipped > 0 && !(n != null && n.Bool) ? "bad" : "ok" };
        }

        /// <summary>trash(ids): protected ones stay; the dialog; the move; the recordings and the days again.</summary>
        public async Task Trash(IList<JsonValue> ids)
        {
            HashSet<string> keys = new HashSet<string>(ids.Select(KeyOf));
            List<JsonValue> list = Recs.Where(r => keys.Contains(KeyOf(Get(r, "id")))).ToList();
            List<JsonValue> movable = list.Where(r => !r["protected"].Bool).ToList();
            if (movable.Count == 0) { Ui.Toast(StayWhere, "bad"); return; }
            if (!await Ui.Ask(TrashAsk(movable.Count, Sum0(movable, "bytes"), list.Count - movable.Count))) return;
            await Ui.Act(async () =>
            {
                JsonValue o = await Api.Post(V + "/trash", JsonValue.Object("ids", JsonValue.Array(movable.Select(r => Get(r, "id") ?? JsonValue.Null))), CancellationToken.None);
                string[] t = TrashToast(o);
                Ui.Toast(t[0], t[1]);
                foreach (JsonValue r in movable) Remove(Get(r, "id"));
            });
            await Task.WhenAll(LoadRecs(), LoadDays());
        }

        public static HzAsk TrashDaysAsk(string day, bool through, string cat, JsonValue pv)
        {
            HzAsk a = new HzAsk();
            a.Title = through ? ThroughAsk : DayAsk;
            string tail = " from " + (Fx.DayTitle(day) ?? "");
            if (through)
            {
                tail += " and older";
                if (Cmp(Get(pv, "days"), JsonValue.Number(1)) > 0) tail += " (" + S(Get(pv, "days")) + " days, oldest " + Hz.Js(pv["oldest"]) + ")";
            }
            if (cat != "all") tail += " (" + cat + AsFiltered;
            a.Para("", new Rich().Bold(Fx.Plural(Get(pv, "recordings") ?? JsonValue.Null, "recording")).Text(" · ").Bold(Fx.Bytes(pv["bytes"])).Text(tail + "."));
            a.Para("muted", new Rich().Text(NothingDeleted + (pv["protected"].Bool ? " " + Fx.Plural(pv["protected"], "protected recording") + " stay." : "")));
            a.Yes = through ? ThroughYes : DayYes;
            a.Danger = true;
            return a;
        }

        /// <summary>deleteDays(day, through): the server's preview, the dialog, the move (a job).</summary>
        public async Task DeleteDays(string day, bool through)
        {
            JsonValue pv;
            try { pv = await Api.Post(V + "/trash-days", Footage.TrashDaysBody(day, through, Cat, true), CancellationToken.None); }
            catch (Exception ex) { Ui.Toast(ex.Message, "bad"); return; }
            if (!pv["recordings"].Bool) { Ui.Toast(pv["protected"].Bool ? AllProtected : NothingToMove, "ok"); return; }
            if (!await Ui.Ask(TrashDaysAsk(day, through, Cat, pv))) return;
            await Ui.Act(async () =>
            {
                JsonValue o = await Api.Post(V + "/trash-days", Footage.TrashDaysBody(day, through, Cat, false), CancellationToken.None);
                Ui.Toast("Moving " + Fx.Plural(Get(o, "recordings") ?? JsonValue.Null, "recording") + " (" + Fx.Bytes(o["bytes"]) + ")" + MovingTail, "ok");
            });
        }

        // ---------------------------------------------------------------- the inspector

        /// <summary>inspect(r): the drawer (loading), the detail, then its tabs (a failure: the message and the list entry).</summary>
        public async Task Inspect(JsonValue r)
        {
            FxFootageDrawer d = new FxFootageDrawer();
            d.Title = RecordingWord + Hz.WallDay(Hz.Or(r["start"]), H24);
            d.Rec = r;
            Drawer = d;
            JsonValue detail;
            try { detail = await Api.Get(V + "/recordings/" + Hz.EncId(S(Get(r, "id"))), CancellationToken.None); }
            catch (Exception ex)
            {
                d.Loading = false;
                d.Error = ex.Message;
                d.Json = Pretty(r);
                return;
            }
            Fill(d, r, detail, H24, Car.Id);
        }

        static void Fill(FxFootageDrawer d, JsonValue r, JsonValue detail, bool h24, string vehicleId)
        {
            d.Loading = false;
            d.Detail = detail;
            if (r["thumbRel"].Bool || r["frontRel"].Bool) d.Thumb = ThumbPath(vehicleId, Get(r, "id"));
            d.Kv.Add(new[] { "id", Hz.Js(detail["id"]) });
            d.Kv.Add(new[] { "type", Hz.Js(detail["type"]) + " · " + Hz.Js(detail["category"]) });
            d.Kv.Add(new[] { "start → end", Hz.WallDay(Hz.Or(detail["start"]), h24) + " → " + Hz.WallDay(Hz.Or(detail["end"]), h24) });
            string at = Hz.WallDay(Hz.Or(detail["eventAt"]), h24);
            d.Kv.Add(new[] { "event at", at.Length > 0 ? at : Fx.Dash });
            d.Kv.Add(new[] { "cameras", string.Join(", ", detail["cameras"].Items.Select(CamLabel)) });
            d.Kv.Add(new[] { "size", Fx.Bytes(detail["bytes"]) + " · " + S(Get(detail, "fileCount")) + " files · " + S(Get(detail, "clipCount")) + " clips" });
            JsonValue e = detail["encoded"];
            JsonValue en = Get(e, "encoded"), cl = Get(e, "clips");
            d.Kv.Add(new[] { "encoded", (en == null || en.IsNull ? "0" : Hz.Js(en)) + "/" + (cl == null || cl.IsNull ? "0" : Hz.Js(cl)) + " clips " + string.Join(", ", e["codecs"].Items.Select(CodecName)) });
            d.Protected = detail["protected"].Bool;
            d.Kv.Add(new[] { "protected", d.Protected ? "yes" : "no" });
            d.Kv.Add(new[] { "folder", Hz.Js(detail["folderPath"]) });
            JsonValue ev = detail["event"];
            if (ev.Bool)
            {
                List<string> parts = new List<string>();
                string reason = Fx.Humanize(Hz.Or(ev["reason"]));
                if (reason.Length > 0) parts.Add(reason);
                if (ev["city"].Bool) parts.Add(Hz.Js(ev["city"]));
                if (ev["street"].Bool) parts.Add(Hz.Js(ev["street"]));
                d.Kv.Add(new[] { "event", string.Join(" · ", parts) });
                if (ev["est_lat"].Bool) d.EventCoords = Hz.Js(ev["est_lat"]) + ", " + Hz.Js(ev["est_lon"]);
            }
            foreach (JsonValue x in detail["files"].Items)
            {
                FxFootageFile f = new FxFootageFile();
                string rel = Hz.Js(x["rel"]);
                f.Name = rel.Substring(rel.LastIndexOf('/') + 1);
                if (x.Has("codec"))
                {
                    if (x["codec"].Bool) { f.Codec = CodecName(x["codec"]); f.Tone = x["codec"].IsString && x["codec"].Str == "h264" ? "plain" : "enc"; }
                    else f.Codec = Checking;
                }
                else f.Codec = Asset;
                f.Size = Fx.Bytes(x["size"]);
                f.Path = S(Get(x, "path"));
                f.Href = Footage.MediaPath(f.Path);
                d.Files.Add(f);
            }
            d.Json = Pretty(detail);
        }

        /// <summary>A drawer tab: "sum", "files" or "json".</summary>
        public void DrawerTab(string tab)
        {
            if (Drawer != null && Drawer.Detail != null) Drawer.Tab = tab;
        }

        /// <summary>The drawer's close button (or Esc).</summary>
        public void DrawerClose()
        {
            if (Drawer != null) Drawer.Open = false;
            Drawer = null;
        }

        /// <summary>The drawer's Play, Protect / Unprotect and Move to Bin: the drawer closes, then the action.</summary>
        public Task DrawerAct(string act)
        {
            FxFootageDrawer d = Drawer;
            if (d == null || d.Detail == null) return Task.FromResult(0);
            JsonValue r = d.Rec;
            DrawerClose();
            switch (act)
            {
                case "play": Play(r); break;
                case "prot": return Protect(new List<JsonValue> { Get(r, "id") }, !d.Detail["protected"].Bool);
                case "trash": if (!d.Protected) return Trash(new List<JsonValue> { Get(r, "id") }); break;
            }
            return Task.FromResult(0);
        }

        /// <summary>Copy JSON: the detail, two spaces indented, on the clipboard.</summary>
        public void DrawerCopy()
        {
            FxFootageDrawer d = Drawer;
            if (d == null || d.Detail == null) return;
            try { Clip(d.Json); }
            catch (Exception) { Ui.Toast(CopyFailed, "bad"); return; }
            Ui.Toast(Copied, "ok");
        }

        /// <summary>JSON.stringify(value, null, 2).</summary>
        public static string Pretty(JsonValue v)
        {
            StringBuilder sb = new StringBuilder();
            Pretty(sb, v, "");
            return sb.ToString();
        }

        static void Pretty(StringBuilder sb, JsonValue v, string ind)
        {
            if (v.IsArray || v.IsObject)
            {
                if (v.Count == 0) { sb.Append(v.IsArray ? "[]" : "{}"); return; }
                string inner = ind + "  ";
                sb.Append(v.IsArray ? "[\n" : "{\n");
                bool first = true;
                if (v.IsArray)
                {
                    foreach (JsonValue x in v.Items)
                    {
                        if (!first) sb.Append(",\n");
                        first = false;
                        sb.Append(inner);
                        Pretty(sb, x, inner);
                    }
                }
                else
                {
                    foreach (KeyValuePair<string, JsonValue> kv in v.Props)
                    {
                        if (!first) sb.Append(",\n");
                        first = false;
                        sb.Append(inner).Append(JsonValue.Quote(kv.Key)).Append(": ");
                        Pretty(sb, kv.Value, inner);
                    }
                }
                sb.Append('\n').Append(ind).Append(v.IsArray ? ']' : '}');
                return;
            }
            sb.Append(v.ToJson());
        }

        // ---------------------------------------------------------------- live

        /// <summary>onTopics(topics): inventory or bin load the days and the recordings again.</summary>
        public Task Topics(ICollection<string> t)
        {
            if (t.Contains("inventory") || t.Contains("bin")) return Task.WhenAll(LoadDays(), LoadRecs());
            return Task.FromResult(0);
        }

        // ---------------------------------------------------------------- Index speeds

        public bool IndexFollowing { get { return following; } }

        /// <summary>followIndex(b, start): starts a pass (start) or looks for one running; while it runs the button shows its progress.</summary>
        public async Task IndexBegin(bool start)
        {
            if (following) return;
            following = true;
            followStart = start;
            followWas = false;
            JsonValue s;
            try { s = start ? await Api.Post("/api/speeds/index", JsonValue.Object(), CancellationToken.None) : await Api.Get("/api/speeds/status", CancellationToken.None); }
            catch (Exception ex) { IndexFailed(ex); return; }
            followWas = s["running"].Bool;
            IndexShow(s, true);
        }

        /// <summary>3 s later: the status again (shown: the button is still on the page).</summary>
        public async Task IndexTick(bool shown)
        {
            if (!following) return;
            JsonValue s;
            try { s = await Api.Get("/api/speeds/status", CancellationToken.None); }
            catch (Exception ex) { IndexFailed(ex); return; }
            IndexShow(s, shown);
        }

        void IndexShow(JsonValue s, bool shown)
        {
            if (s["running"].Bool && shown)
            {
                IndexBusy = true;
                IndexText = Footage.IndexRunning(s);
                return;
            }
            if (shown && (followStart || followWas)) Ui.Toast(Footage.IndexDone(s), "ok");
            IndexEnd();
        }

        void IndexFailed(Exception ex)
        {
            if (followStart) Ui.Toast(ex.Message, "ok");
            IndexEnd();
        }

        void IndexEnd()
        {
            IndexText = IndexLabel;
            IndexBusy = false;
            following = false;
        }

        // ---------------------------------------------------------------- keys and commands

        /// <summary>The work a key or command started (protect, trash, inspect), done or not.</summary>
        public Task KeyWork = Task.FromResult(0);

        /// <summary>onKey(e): true when the page took the key.</summary>
        public bool Key(string key, bool shift, bool ctrl, bool alt)
        {
            KeyWork = Task.FromResult(0);
            JsonValue r = Cursor >= 0 && Cursor < Shown.Count ? Shown[Cursor] : null;
            if (ctrl && (key ?? "").ToLowerInvariant() == "a") { foreach (JsonValue x in Shown) Add(Get(x, "id")); DrawTable(); return true; }
            if (ctrl || alt) return false;
            switch (key)
            {
                case "j": case "ArrowDown": MoveCursor(Cursor + 1, shift); return true;
                case "k": case "ArrowUp": MoveCursor(Cursor - 1, shift); return true;
                case "J": MoveCursor(Cursor + 1, true); return true;
                case "K": MoveCursor(Cursor - 1, true); return true;
                case "Home": MoveCursor(0, false); return true;
                case "End": MoveCursor(Shown.Count - 1, false); return true;
                case "x": case " ":
                    if (r != null) { Toggle(Get(r, "id"), null); Anchor = Cursor; DrawTable(); }
                    return true;
                case "Enter": case "o": if (r != null) Play(r); return true;
                case "i": if (r != null) KeyWork = Inspect(r); return true;
                case "p":
                    if (sel.Count > 0) KeyWork = Protect(Selected(), !Recs.Where(x => Has(Get(x, "id"))).All(x => x["protected"].Bool));
                    else if (r != null) KeyWork = Protect(new List<JsonValue> { Get(r, "id") }, !r["protected"].Bool);
                    return true;
                case "Delete": case "Backspace": case "#":
                    if (sel.Count > 0) KeyWork = Trash(Selected());
                    else if (r != null) KeyWork = Trash(new List<JsonValue> { Get(r, "id") });
                    return true;
                case "Escape":
                    if (sel.Count > 0) { sel.Clear(); DrawTable(); return true; }
                    return false;
                default: return false;
            }
        }

        /// <summary>commands(): the palette's entries (group, label), the first ten days after the page's own.</summary>
        public List<string[]> Commands()
        {
            List<string[]> c = new List<string[]>
            {
                new[] { CmdGroup, CmdSelect }, new[] { CmdGroup, CmdTrash }, new[] { CmdGroup, CmdProtect }, new[] { CmdGroup, CmdNotConverted }
            };
            if (Days != null) foreach (JsonValue d in Days["days"].Items.Take(10)) c.Add(new[] { CmdDayGroup, "Show " + S(Get(d, "day")) });
            return c;
        }

        /// <summary>A command run (Show not-converted only: the codec control's button, which the page presses).</summary>
        public Task Command(string label)
        {
            switch (label)
            {
                case CmdSelect: foreach (JsonValue x in Shown) Add(Get(x, "id")); DrawTable(); return Task.FromResult(0);
                case CmdTrash: return Trash(Selected());
                case CmdProtect: return Protect(Selected(), true);
                case CmdNotConverted: SetConv("no"); return Task.FromResult(0);
            }
            if (Days != null)
                foreach (JsonValue d in Days["days"].Items.Take(10))
                    if ("Show " + S(Get(d, "day")) == label) return PickDay(Hz.Js(d["day"]));
            return Task.FromResult(0);
        }

        // ---------------------------------------------------------------- the outline

        internal static void T(List<string> o, string s)
        {
            string x = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            if (x.Length > 0) o.Add(x);
        }

        /// <summary>The page as test/windowsNativeFluxFootage.test.js reads the web's, marker for marker (see marks() there).</summary>
        public List<string> Outline()
        {
            List<string> o = new List<string>();
            o.Add("[head]"); T(o, Title);
            if (Car == null)
            {
                o.Add("[empty]"); T(o, NoVehicleText);
                o.Add("[link " + AddVehicleLink + "]"); T(o, AddVehicle);
                return o;
            }
            o.Add("[sub]"); T(o, Car.Name);
            o.Add("[sum]"); T(o, Sum);
            o.Add("[btn scan]"); T(o, ScanLabel);
            o.Add(IndexBusy ? "[btn index disabled]" : "[btn index]"); T(o, IndexText);
            o.Add("[link " + ReviewLink + "]"); T(o, ReviewLabel);
            o.Add("[seg cat]");
            foreach (string[] s in Cats) { o.Add(s[0] == Cat ? "[opt on]" : "[opt]"); T(o, s[1]); }
            o.Add("[seg conv]");
            foreach (string[] s in Convs) { o.Add(s[0] == Conv ? "[opt on]" : "[opt]"); T(o, s[1]); }
            o.Add(Prot ? "[chk prot on]" : "[chk prot]"); T(o, ProtectedOnly);
            o.Add("[input " + Text + "]");
            o.Add("[pane]"); T(o, DaysHead);
            FxFootageDays dv = DaysDrawn;
            if (dv != null)
            {
                T(o, dv.Count);
                DayOutline(o, dv.All);
                if (dv.Error != null) { o.Add("[bad]"); T(o, dv.Error); }
                foreach (FxFootageDay d in dv.Days) DayOutline(o, d);
                if (dv.NoDays) T(o, NoDaysText);
            }
            o.Add("[bulk]");
            if (Bulk != null)
            {
                T(o, Bulk.Count); T(o, SelectedWord + (Bulk.Bytes != null ? " ·" : ""));
                if (Bulk.Bytes != null) T(o, Bulk.Bytes);
                o.Add(Bulk.CanProtect ? "[bulk protect]" : "[bulk protect disabled]"); T(o, ProtectWord);
                o.Add(Bulk.CanUnprotect ? "[bulk unprotect]" : "[bulk unprotect disabled]"); T(o, UnprotectWord);
                o.Add(Bulk.CanTrash ? "[bulk trash]" : "[bulk trash disabled]"); T(o, MoveToBin);
                o.Add(Bulk.CanAll ? "[bulk all]" : "[bulk all disabled]"); T(o, SelectAllWord);
                o.Add(Bulk.CanClear ? "[bulk clear]" : "[bulk clear disabled]"); T(o, ClearWord);
            }
            o.Add("[table]");
            TableOutline(o, Table);
            o.Add("[foot]"); T(o, Foot);
            return o;
        }

        static void DayOutline(List<string> o, FxFootageDay d)
        {
            o.Add("[day" + (d.On ? " on" : "") + " " + d.Day + "]");
            T(o, d.Title); T(o, d.Size);
            if (d.All) { T(o, d.Line); return; }
            o.Add("[bar " + string.Join(",", d.Bar.Select(kv => kv.Key + ":" + kv.Value)) + (d.BarTitle.Length > 0 ? " title=" + d.BarTitle : "") + "]");
            T(o, d.Line);
            o.Add("[enc " + d.EncWidth + "]"); T(o, d.EncText);
            if (d.Ix != null) { o.Add("[chip violet]"); T(o, d.Ix); }
            o.Add("[play-day " + d.PlayLabel + "]"); T(o, PlayWord);
            o.Add("[del-day]"); T(o, DelDay);
            o.Add("[del-through]"); T(o, DelThrough);
            o.Add("[size " + d.SizeWidth + "]");
        }

        static void TableOutline(List<string> o, FxFootageTable t)
        {
            if (t == null) return;
            if (t.Error != null) { o.Add("[tr]"); T(o, t.Error); return; }
            o.Add("[th]"); o.Add(t.AllChecked ? "[all on]" : "[all]");
            o.Add("[th]");
            foreach (string[] c in Cols)
            {
                bool r = c[0] == "bytes" || c[0] == "minutes" || c[0] == "fileCount";
                bool on = t.SortKey == c[0];
                o.Add("[th" + (r ? " r" : "") + (on ? (t.Ascending ? " ascending" : " descending") : "") + "]");
                T(o, c[1]);
                if (on) T(o, t.Ascending ? "▲" : "▼");
            }
            o.Add("[th r]"); T(o, ActionsHead);
            foreach (FxFootageRow w in t.Rows)
            {
                o.Add("[tr " + w.I.ToString(CultureInfo.InvariantCulture) + (w.Sel ? " sel" : "") + (w.Cur ? " cur" : "") + "]");
                o.Add("[chk" + (w.Sel ? " on" : "") + " " + w.SelectLabel + "]");
                if (w.Thumb != null) o.Add("[img " + w.Thumb + "]");
                T(o, w.Start); T(o, w.Sub);
                o.Add("[cat " + string.Join(" ", Regex.Split(w.Category, @"\s+").Where(x => x.Length > 0 && x != "cat")) + "]"); T(o, w.Category);
                T(o, w.Minutes);
                o.Add("[cams " + w.CamsTitle + "]");
                for (int k = 0; k < CamShort.Length; k++) { o.Add(w.Cams[k] ? "[cam on]" : "[cam]"); T(o, CamShort[k]); }
                T(o, w.Files); T(o, w.Size);
                FxFootageCodec c = w.Codec;
                if (c.Kind == "none") T(o, Fx.Dash);
                else if (c.Kind == "plain") { o.Add("[tag plain]"); T(o, c.Text); if (c.Checking) T(o, "·checking"); }
                else { o.Add("[tag enc]"); T(o, c.Text); o.Add("[meter " + c.Meter + " " + c.MeterTitle + "]"); T(o, c.Of); }
                if (w.Protected) o.Add("[prot]");
                o.Add("[act play " + w.PlayLabel + "]");
                o.Add("[act protect " + w.ProtectLabel + "]");
                o.Add("[act trash " + w.TrashLabel + (w.Protected ? " disabled" : "") + "]");
                o.Add("[act inspect " + w.InspectLabel + "]");
            }
            if (t.Empty != null) { o.Add("[tr]"); T(o, t.Empty); }
        }

        /// <summary>The drawer as the test reads the web's (null when there is none).</summary>
        public List<string> DrawerOutline()
        {
            FxFootageDrawer d = Drawer;
            if (d == null) return null;
            List<string> o = new List<string>();
            T(o, d.Title);
            o.Add("[close]");
            if (d.Loading) { o.Add("[loading]"); return o; }
            if (d.Error != null)
            {
                o.Add("[bad]"); T(o, d.Error);
                T(o, ListEntry);
                o.Add("[json]"); T(o, d.Json);
                return o;
            }
            o.Add("[tab sum" + (d.Tab == "sum" ? " on" : "") + "]"); T(o, TabSummary);
            o.Add("[tab files" + (d.Tab == "files" ? " on" : "") + "]"); T(o, TabFiles + d.Files.Count.ToString(CultureInfo.InvariantCulture) + ")");
            o.Add("[tab json" + (d.Tab == "json" ? " on" : "") + "]"); T(o, TabJson);
            if (d.Tab == "sum")
            {
                if (d.Thumb != null) o.Add("[img " + d.Thumb + "]");
                o.Add("[kv]");
                foreach (string[] kv in d.Kv)
                {
                    T(o, kv[0]); T(o, kv[1]);
                    if (kv[0] == "event" && d.EventCoords != null) T(o, d.EventCoords);
                }
                o.Add("[i-play]"); T(o, PlayWord);
                o.Add("[i-prot]"); T(o, d.Protected ? UnprotectWord : ProtectWord);
                o.Add(d.Protected ? "[i-trash disabled]" : "[i-trash]"); T(o, MoveToBin);
            }
            else if (d.Tab == "files")
            {
                o.Add("[table]");
                o.Add("[th]"); T(o, FileCols[0]);
                o.Add("[th]"); T(o, FileCols[1]);
                o.Add("[th r]"); T(o, FileCols[2]);
                o.Add("[th]");
                foreach (FxFootageFile f in d.Files)
                {
                    o.Add("[tr]");
                    T(o, f.Name);
                    if (f.Tone != null) o.Add("[tag " + f.Tone + "]");
                    T(o, f.Codec);
                    T(o, f.Size);
                    o.Add("[dl " + f.Href + "]");
                }
            }
            else
            {
                o.Add("[copy]"); T(o, CopyJson);
                o.Add("[json]"); T(o, d.Json);
            }
            return o;
        }
    }
}
