using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>A day in the album list (hz/pages/footage.js drawDays(): button.album).</summary>
    public sealed class HzAlbum
    {
        /// <summary>data-day: "" for All days.</summary>
        public string Day;
        public bool On;
        /// <summary>The thumbnail's path (the day's thumbId), null for the film icon.</summary>
        public string Art;
        /// <summary>The category bar: each category and its count (flex), in the server's order.</summary>
        public List<KeyValuePair<string, string>> Bar = new List<KeyValuePair<string, string>>();
        public string Title, Sub;
        /// <summary>The tiny line: "Recent 1 · Sentry 1 · 1 protected · 83% converted" (" ·" when an index part follows).</summary>
        public string Cats;
        /// <summary>The speed graph's index ("indexed", "indexed 3/6"), null for none.</summary>
        public string Ix;
    }

    /// <summary>A recording in the track list (li.track).</summary>
    public sealed class HzTrack
    {
        public JsonValue Raw;
        public string Id;
        /// <summary>"01", "02"…</summary>
        public string N;
        public bool Cur, Sel, Protected;
        /// <summary>The checkbox is ticked (as drawn, or as clicked since).</summary>
        public bool Checked;
        /// <summary>The thumbnail's path, null without thumbRel / frontRel.</summary>
        public string Art;
        /// <summary>r.start as the labels read it ("2026-10-30 22:13:02").</summary>
        public string Start;
        /// <summary>wall(start) and "– wall(end)".</summary>
        public string Time, Till;
        public string Category, CategoryTone;
        /// <summary>The dim part after the pill: "Fri, Oct 30 · SentryClips · event 10:14:03 PM".</summary>
        public string Sub;
        /// <summary>"Cameras: Front, Rear".</summary>
        public string CamsLabel;
        /// <summary>For each of Hz.Cams: the recording has it.</summary>
        public bool[] Cams = new bool[6];
        /// <summary>The codec pill's text and tone ("" or "mag"); null for none.</summary>
        public string Codec, CodecTone;
        /// <summary>"1:00" and "· 2.4 MB".</summary>
        public string Dur, DurSize;
        public string SelectLabel, PlayLabel, ProtectLabel, TrashLabel, InfoLabel;
    }

    /// <summary>The selection bar (div.bulk), shown with a selection.</summary>
    public sealed class HzBulk
    {
        /// <summary>"2 selected · 4.9 MB".</summary>
        public string Text;
        public bool CanUnprotect, CanTrash;
    }

    /// <summary>The page as hz/pages/footage.js draws it.</summary>
    public sealed class HzFootageView
    {
        /// <summary>No vehicle: the page is the blank with Add a vehicle.</summary>
        public bool NoVehicle;
        /// <summary>#ft-sum: "Model 3 · 4 recordings on 2 days · inventory 5 min ago · times are the car's (UTC)".</summary>
        public string Sum;
        public string IndexText;
        public bool IndexBusy;
        /// <summary>#ft-dayn: "2 days".</summary>
        public string DayCount;
        /// <summary>All days' sub line: "4 tracks · 9.7 MB".</summary>
        public string AllSub;
        public bool AllOn;
        /// <summary>The days' failure (p.badtx), null for none.</summary>
        public string DaysError;
        public List<HzAlbum> Albums = new List<HzAlbum>();
        /// <summary>"No days match these filters." when there is no day and no failure.</summary>
        public bool NoDays;
        /// <summary>The playlist head was drawn (the recordings are in).</summary>
        public bool Head;
        public string HeadArt, Kind, HeadTitle, HeadLine, PlayLabel;
        public bool CanPlay, DayButtons;
        /// <summary>The selection bar, null when nothing is selected.</summary>
        public HzBulk Bulk;
        public List<HzTrack> Tracks = new List<HzTrack>();
        /// <summary>The blank row's text when no track is shown, null otherwise.</summary>
        public string Empty;
        /// <summary>#ft-foot: "4 of 4 shown · keyboard: …" ("" for none).</summary>
        public string Foot;
        /// <summary>status(): "4 tracks · Fri, Oct 30"; null before the head was drawn.</summary>
        public string Status;
    }

    /// <summary>One row of the details sheet's Files table.</summary>
    public sealed class HzFootFile
    {
        public string Name;
        /// <summary>The codec cell: a pill (Tone "" or "mag") or "checking" / "asset" (Tone null, faint).</summary>
        public string Codec, Tone;
        public string Size;
        /// <summary>/api/media?path=… (the download link) and the file's own path.</summary>
        public string Href, Path;
    }

    /// <summary>The details sheet (info(): sheet("Track …")).</summary>
    public sealed class HzFootInfo
    {
        public string Title;
        /// <summary>The thumbnail's path, null without one; Alt its alt text.</summary>
        public string Art, Alt;
        /// <summary>dl.kv: term, value (Mono: the value is small mono, Extra: a second small mono line).</summary>
        public List<string[]> Kv = new List<string[]>();
        public string EventCoords;
        public bool Protected;
        public List<HzFootFile> Files = new List<HzFootFile>();

        /// <summary>The sheet as test/nativePackHarness.js reads the web's dialog: the title, the close button, the list, the buttons, the files.</summary>
        public List<string> Outline()
        {
            List<string> o = new List<string>();
            HzFootage.T(o, Title);
            o.Add("[btn close icon ghost]");
            foreach (string[] kv in Kv)
            {
                HzFootage.T(o, kv[0]);
                HzFootage.T(o, kv[1]);
                if (kv[0] == HzFootage.KvEvent && EventCoords != null) HzFootage.T(o, EventCoords);
            }
            o.Add("[btn play go]"); HzFootage.T(o, HzFootage.PlayWord);
            o.Add("[btn]"); HzFootage.T(o, Protected ? HzFootage.UnprotectWord : HzFootage.ProtectWord);
            o.Add("[btn danger]"); HzFootage.T(o, HzFootage.MoveToBin);
            HzFootage.T(o, HzFootage.FilesHead);
            foreach (string h in HzFootage.FileCols) HzFootage.T(o, h);
            foreach (HzFootFile f in Files)
            {
                HzFootage.T(o, f.Name);
                HzFootage.T(o, f.Codec);
                HzFootage.T(o, f.Size);
            }
            return o;
        }
    }

    /// <summary>
    /// Horizon's Footage page (examples/ui-packs/horizon/web/hz/pages/footage.js):
    /// footage as a music library. The filters strip (category, conversion,
    /// Protected only, the search), the days as albums (thumbnail, category
    /// bar, conversion and speed-index state), the playlist of the day picked
    /// (or of every day) with its head, the selection bar and the tracks, and
    /// every action: Refresh inventory, Index speeds (following the pass),
    /// play (the Viewer), protect / unprotect, the Recycle Bin (one, a
    /// selection, a day, a day and older), the details sheet, the keys.
    /// Paths and bodies are Core/Footage.cs's (the same API as the built-in
    /// page); the pack's own wording, filters and order are here. Checked by
    /// test/windowsNativeHorizonFootage.test.js and Core.Tests HorizonFootageTests.
    /// </summary>
    public sealed class HzFootage
    {
        public const string Title = "Footage";
        public const string Loading = " · loading the library…";
        public const string ScanLabel = "Refresh inventory";
        public const string IndexLabel = "Index speeds";
        public const string IndexTip = "Read every clip's speed now, for the Viewer's speed graph";
        public const string ConvertLabel = "Convert";
        public const string ConvertLink = "#/encoder?new=1";
        public const string ProtectedOnly = "Protected only";
        public const string SearchPlaceholder = "Search time, event, place, camera";
        public const string DaysHead = "Days";
        public const string AllDays = "All days";
        public const string NoDaysText = "No days match these filters.";
        public const string Converted = "% converted";
        public const string KindDay = "DAY";
        public const string KindLibrary = "LIBRARY";
        public const string PlayDay = "Play day";
        public const string PlayNewest = "Play newest day";
        public const string DelDay = "Move day to Bin";
        public const string DelThru = "This day and older";
        public const string ProtectedOnlyLine = " · protected only";
        public const string TimesAre = " · times are the car's (";
        public const string ScanningTail = " · scanning…";
        public const string NoMatch = "No track matches the search.";
        public const string NoRecordings = "No recordings here yet.";
        public const string FootKeys = " shown · keyboard: j / k move · Enter play · x select · p protect · Delete to Bin";
        public const string ProtectWord = "Protect";
        public const string UnprotectWord = "Unprotect";
        public const string MoveToBin = "Move to Bin";
        public const string SelectAll = "Select all shown";
        public const string ClearWord = "Clear";
        public const string PlayWord = "Play";
        public const string Selected = " selected · ";
        public const string ToTheBin = " to the Recycle Bin";
        public const string DetailsOf = "Details of ";
        public const string NoVehicleText = "No vehicle yet. Add one to see its footage.";
        public const string AddVehicle = "Add a vehicle";
        public const string AddVehicleLink = "#/settings/vehicles?add=1";
        public const string ScanToast = "Refreshing the inventory. Jobs shows the progress.";
        public const string ProtectionRemoved = "Protection removed.";
        public const string ProtectedSkip = ": policies and the Bin skip ";
        public const string StayWhere = "Protected recordings stay where they are.";
        public const string TrashBody = " moves to the Recycle Bin on the same share. You can restore it any time; nothing is deleted until you empty the Bin.";
        public const string TrashStay = " protected recording(s) stay.";
        public const string MovedToBin = " moved to the Recycle Bin (";
        public const string AllProtected = "Everything there is protected: nothing to move.";
        public const string NothingToMove = "Nothing to move.";
        public const string DayAsk = "Move this day to the Recycle Bin?";
        public const string ThruAsk = "Move this day and every older day to the Bin?";
        public const string AsFiltered = " only, as filtered)";
        public const string NothingDeleted = "Nothing is deleted until you empty the Recycle Bin.";
        public const string DayYes = "Move day";
        public const string ThruYes = "Move day and older";
        public const string MovingTail = " to the Recycle Bin. Jobs shows the progress.";
        public const string KvType = "Type";
        public const string KvStartEnd = "Start → end";
        public const string KvEventAt = "Event at";
        public const string KvEvent = "Event";
        public const string KvCameras = "Cameras";
        public const string KvSize = "Size";
        public const string KvConverted = "Converted";
        public const string KvProtected = "Protected";
        public const string KvFolder = "Folder";
        public const string FilesHead = "Files";
        public const string Checking = "checking";
        public const string Asset = "asset";
        public const string ThumbnailOf = "Thumbnail of ";

        /// <summary>The Files table's heads.</summary>
        public static readonly string[] FileCols = { "File", "Codec", "Size", "Download" };
        /// <summary>The category filter's buttons: value, label.</summary>
        public static readonly string[][] Cats = { new[] { "all", "All" }, new[] { "Recent", "Recent" }, new[] { "Sentry", "Sentry" }, new[] { "Saved", "Saved" }, new[] { "Other", "Other" } };
        /// <summary>The conversion filter's buttons.</summary>
        public static readonly string[][] Convs = { new[] { "all", "Any codec" }, new[] { "no", "Not converted" }, new[] { "yes", "Converted" } };
        /// <summary>The page's keys (the keyboard sheet's section).</summary>
        public static readonly string[][] Keys = { new[] { "j / k", "Next / previous track" }, new[] { "Enter", "Play the track" }, new[] { "x", "Select the track" }, new[] { "p", "Protect / unprotect" }, new[] { "Delete", "Move to the Recycle Bin" }, new[] { "i", "Track details" }, new[] { "/", "Search" } };

        // ---------------------------------------------------------------- the page's state (mount()'s f and lets)

        public readonly IJsonApi Api;
        public readonly HzUi Ui;
        public readonly HzCar Car;
        /// <summary>v.timezone (null for none: "local").</summary>
        public string Timezone;
        /// <summary>Date.now().</summary>
        public Func<long> Now = Format.NowMs;
        /// <summary>local.set(key, JSON): "ft.cat", "ft.conv".</summary>
        public Action<string, string> Store = delegate { };
        /// <summary>sheet(): the details; resolves with the button used ("play", "protect", "trash", "close").</summary>
        public Func<HzFootInfo, Task<string>> Sheet = i => Task.FromResult("close");

        public string Cat = "all", Day = "", Conv = "all";
        public bool Prot;
        /// <summary>f.q: the search as typed.</summary>
        public string Q = "";
        public readonly IdSet Sel = new IdSet();
        public int Cur;
        // the track list as painted: its rows (their .cur, .sel and checkboxes as the reader left them) or the blank's text
        List<HzTrack> domRows = new List<HzTrack>();
        string domEmpty, painted;
        /// <summary>"ft-q" after "/", "play" after a move put the focus on a play button, else null.</summary>
        public string Focus;

        public JsonValue Days = JsonValue.Object("days", JsonValue.Array(new JsonValue[0]));
        public List<JsonValue> Recs = new List<JsonValue>();
        public JsonValue Total = JsonValue.Number(0);
        public JsonValue Meta = JsonValue.Object();
        /// <summary>shown: the tracks of the last drawTracks().</summary>
        public List<JsonValue> Shown = new List<JsonValue>();
        JsonValue headDays;
        long headNow;
        bool headDrawn;

        // Index speeds
        public string IndexText = IndexLabel;
        public bool IndexBusy;
        bool following, followStart, followWas;

        public HzFootage(IJsonApi api, HzUi ui, HzCar car)
        {
            Api = api;
            Ui = ui ?? new HzUi();
            Car = car;
        }

        /// <summary>local.get(key, fallback): the stored JSON text read (a string), else the fallback.</summary>
        public static string Stored(string json, string fallback)
        {
            if (json == null) return fallback;
            JsonValue v = JsonValue.TryParse(json);
            if (v == null) return fallback;
            return v.IsString ? v.Str : Hz.Js(v);
        }

        // ---------------------------------------------------------------- requests

        static string Q1(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s ?? ""))
            {
                char c = (char)b;
                if (b < 128 && ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "*-._".IndexOf(c) >= 0)) sb.Append(c);
                else if (c == ' ') sb.Append('+');
                else sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        string V { get { return "/api/vehicles/" + Hz.EncId(Car.Id); } }

        /// <summary>GET /api/vehicles/:id/days?category=&amp;limit=100.</summary>
        public static string DaysPath(string vehicleId, string cat)
        {
            return "/api/vehicles/" + Hz.EncId(vehicleId) + "/days?category=" + Q1(cat) + "&limit=100";
        }

        /// <summary>GET /api/vehicles/:id/recordings?category=&amp;limit=500[&amp;date=][&amp;protected=1][&amp;offset=].</summary>
        public static string RecsPath(string vehicleId, string cat, string day, bool prot, int offset)
        {
            string p = "/api/vehicles/" + Hz.EncId(vehicleId) + "/recordings?category=" + Q1(cat) + "&limit=500";
            if (!string.IsNullOrEmpty(day)) p += "&date=" + Q1(day);
            if (prot) p += "&protected=1";
            if (offset >= 0) p += "&offset=" + offset.ToString(CultureInfo.InvariantCulture);
            return p;
        }

        /// <summary>thumbUrl(vid, rid).</summary>
        public static string ThumbPath(string vehicleId, JsonValue rid)
        {
            return Footage.ThumbPath(vehicleId, Hz.Js(rid));
        }

        /// <summary>The first page of days, the first recordings, and Index speeds following a pass already running: mount().</summary>
        public async Task Mount()
        {
            // (followIndex(b, false) is not waited for: it asks first, then the lists)
            Task index = IndexBegin(false);
            await Task.WhenAll(LoadDays(), LoadRecs());
            GC.KeepAlive(index);
        }

        /// <summary>loadDays(): a failure is { days: [], total: 0, error }.</summary>
        public async Task LoadDays()
        {
            try { Days = await Api.Get(DaysPath(Car.Id, Cat), CancellationToken.None); }
            catch (Exception ex) { Days = JsonValue.Object("days", JsonValue.Array(new JsonValue[0]), "total", 0, "error", ex.Message); }
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

        /// <summary>A number as JavaScript compares it (undefined NaN).</summary>
        static double Num(JsonValue owner, string key)
        {
            return Hz.Missing(owner, key) ? double.NaN : Hz.Number(owner[key]);
        }

        /// <summary>loadRecs(): every page of the recordings (5000 at most); a failure is none with meta.error; then the head and the tracks.</summary>
        public async Task LoadRecs()
        {
            try
            {
                JsonValue o = await Api.Get(RecsPath(Car.Id, Cat, Day, Prot, -1), CancellationToken.None);
                List<JsonValue> recs = List(o, "recordings");
                Recs = recs;
                Total = o["total"];
                if (Hz.Missing(o, "total")) Total = null;
                Meta = o;
                int off = recs.Count;
                while (off < Num(o, "total") && off < 5000)
                {
                    JsonValue more = await Api.Get(RecsPath(Car.Id, Cat, Day, Prot, off), CancellationToken.None);
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
                Meta = JsonValue.Object("error", ex.Message);
            }
            foreach (string id in Sel.ToList()) if (!Recs.Any(r => IdOf(r) == id)) Sel.Remove(id);
            DrawHead();
            DrawTracks();
        }

        /// <summary>A recording's id as the selection keeps it.</summary>
        public static string IdOf(JsonValue r)
        {
            return Hz.Js(r["id"]);
        }

        static double Sum(IEnumerable<JsonValue> list, string key, bool orZero)
        {
            double n = 0;
            foreach (JsonValue r in list)
            {
                if (orZero) { n += r[key].Bool ? Hz.Number(r[key]) : 0; continue; }
                n += Hz.Missing(r, key) ? double.NaN : Hz.Number(r[key]);
            }
            return n;
        }

        /// <summary>drawHead(): the head reads the days as they are now (a later loadDays leaves it).</summary>
        void DrawHead()
        {
            headDays = Days;
            headNow = Now();
            headDrawn = true;
            Ui.Status(StatusText());
        }

        string StatusText()
        {
            return Hz.Count(Total, "track") + (Day.Length > 0 ? " · " + Hz.DayName(Day) : "");
        }

        /// <summary>
        /// drawTracks(): what the filters and the search show, newest first;
        /// f.cur kept in it. As paint() does, the list is only replaced when
        /// its markup changed: a row's .cur moved by j / k and a checkbox
        /// ticked by hand stay as they are when the new markup is the same.
        /// </summary>
        public void DrawTracks()
        {
            Shown = Filtered();
            Cur = Math.Max(0, Math.Min(Cur, Shown.Count - 1));
            List<HzTrack> rows = BuildRows();
            string empty = Shown.Count == 0 ? (Meta["error"].Bool ? Hz.Js(Meta["error"]) : Recs.Count > 0 ? NoMatch : NoRecordings) : null;
            string sig = Sig(rows, empty);
            if (sig == painted) return;
            painted = sig;
            domRows = rows;
            domEmpty = empty;
        }

        /// <summary>drawTracks()'s rows of the shown tracks (f.cur's row .cur, the selected ones .sel and checked).</summary>
        List<HzTrack> BuildRows()
        {
            List<HzTrack> rows = new List<HzTrack>();
            for (int i = 0; i < Shown.Count; i++)
            {
                JsonValue r = Shown[i];
                HzTrack t = new HzTrack();
                t.Raw = r;
                t.Id = IdOf(r);
                t.N = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');
                t.Sel = Sel.Contains(t.Id);
                t.Cur = i == Cur;
                t.Checked = t.Sel;
                t.Protected = r["protected"].Bool;
                if (r["thumbRel"].Bool || r["frontRel"].Bool) t.Art = ThumbPath(Car.Id, r["id"]);
                t.Start = Hz.Js(r["start"]);
                t.Time = Hz.Wall(Hz.Or(r["start"]), H24);
                t.Till = "– " + Hz.Wall(r["end"].Bool ? Hz.Js(r["end"]) : Hz.Or(r["start"]), H24);
                t.Category = Hz.Js(r["category"]);
                t.CategoryTone = Tone(r["category"].IsString ? r["category"].Str : null);
                string ev = r["eventAt"].Bool ? " · event " + Hz.Wall(Hz.Js(r["eventAt"]), H24) : "";
                t.Sub = (Day.Length > 0 ? "" : Hz.DayName(Hz.Tpl(r, "day")) + " · ") + Hz.Words(Hz.Or(r["type"])) + ev;
                t.CamsLabel = "Cameras: " + CamNames(r["cameras"], ", ");
                HashSet<string> have = new HashSet<string>(r["cameras"].Items.Where(c => c.IsString).Select(c => c.Str));
                for (int k = 0; k < Hz.Cams.Length; k++) t.Cams[k] = have.Contains(Hz.Cams[k]);
                string[] codec = Codec(r);
                if (codec != null) { t.Codec = codec[0]; t.CodecTone = codec[1]; }
                t.Dur = Dur(r);
                t.DurSize = "· " + Hz.Size(r["bytes"]);
                t.SelectLabel = "Select " + t.Start;
                t.PlayLabel = "Play " + t.Start;
                t.ProtectLabel = (t.Protected ? UnprotectWord : ProtectWord) + " " + t.Start;
                t.TrashLabel = "Move " + t.Start + ToTheBin;
                t.InfoLabel = DetailsOf + t.Start;
                rows.Add(t);
            }
            return rows;
        }

        static string Sig(List<HzTrack> rows, string empty)
        {
            List<string> o = new List<string>();
            foreach (HzTrack t in rows) o.Add(t.Id);
            TracksOutline(o, rows, empty);
            return string.Join("\n", o);
        }


        static bool StrictEq(JsonValue a, JsonValue b)
        {
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

        static string CamNames(JsonValue cams, string sep)
        {
            List<string> l = new List<string>();
            foreach (JsonValue c in cams.Items) l.Add(Hz.CamName(Hz.Js(c)) ?? Hz.Js(c));
            return string.Join(sep, l);
        }

        List<JsonValue> Filtered()
        {
            string q = (Q ?? "").Trim().ToLowerInvariant();
            List<JsonValue> l = new List<JsonValue>();
            foreach (JsonValue r in Recs)
            {
                JsonValue e = r["encoded"];
                if (Conv == "yes" && !(Num(e, "encoded") > 0)) continue;
                if (Conv == "no" && e["clips"].Bool && StrictEq(e["encoded"], e["clips"]) && !Hz.Missing(e, "encoded")) continue;
                if (q.Length > 0)
                {
                    string text = Hz.Tpl(r, "start") + " " + Hz.Tpl(r, "end") + " " + Hz.Tpl(r, "folder") + " " + Hz.Tpl(r, "category") + " " + Hz.Or(r["eventId"]) + " " + CamNames(r["cameras"], " ");
                    if (!text.ToLowerInvariant().Contains(q)) continue;
                }
                l.Add(r);
            }
            // (String(b.startKey).localeCompare(String(a.startKey)): the car's keys sort the same ordinally; a stable sort)
            return l.Select((r, i) => new KeyValuePair<int, JsonValue>(i, r)).OrderBy(x => Hz.Tpl(x.Value, "startKey"), Comparer<string>.Create((a, b) => string.CompareOrdinal(b, a))).ThenBy(x => x.Key).Select(x => x.Value).ToList();
        }

        // ---------------------------------------------------------------- what it shows

        static string Tone(string cat)
        {
            switch (cat)
            {
                case "Recent": return "cyan";
                case "Sentry": return "org";
                case "Saved": return "grn";
                default: return "";
            }
        }

        static string Dur(JsonValue r)
        {
            double m = Num(r, "minutes");
            return (Hz.Ok(m) ? Format.JsNum(Math.Floor(m)) : "NaN") + ":00";
        }

        /// <summary>codec(r): the pill's text and tone, or null.</summary>
        static string[] Codec(JsonValue r)
        {
            JsonValue e = r["encoded"];
            if (!e["clips"].Bool) return null;
            if (!e["encoded"].Bool) return new[] { "H.264", "" };
            List<string> names = new List<string>();
            foreach (JsonValue c in e["codecs"].Items) names.Add(Hz.CodecName(Hz.Js(c)) ?? Hz.Js(c));
            string n = string.Join("+", names);
            if (n.Length == 0) n = "converted";
            if (Hz.Number(e["encoded"]) < Num(e, "clips")) n += " " + Hz.Tpl(e, "encoded") + "/" + Hz.Tpl(e, "clips");
            return new[] { n, "mag" };
        }

        /// <summary>The page as drawn now (h24: appearance.clock24).</summary>
        public HzFootageView View(bool h24)
        {
            HzFootageView v = new HzFootageView();
            if (Car == null) { v.NoVehicle = true; return v; }
            v.IndexText = IndexText;
            v.IndexBusy = IndexBusy;
            // the days (albums)
            JsonValue days = Days;
            List<JsonValue> list = days["days"].Bool ? new List<JsonValue>(days["days"].Items) : new List<JsonValue>();
            v.DayCount = Hz.Count(days["total"].Bool ? days["total"] : JsonValue.Number(0), "day");
            v.AllOn = Day.Length == 0;
            v.AllSub = Hz.Count(days["recordings"].Bool ? days["recordings"] : JsonValue.Number(0), "track") + " · " + Hz.Size(Sum(list, "bytes", false));
            if (days["error"].Bool) v.DaysError = Hz.Js(days["error"]);
            foreach (JsonValue d in list)
            {
                HzAlbum a = new HzAlbum();
                a.Day = Hz.Js(d["day"]);
                a.On = d["day"].IsString && Day == d["day"].Str;
                if (d["thumbId"].Bool) a.Art = ThumbPath(Car.Id, d["thumbId"]);
                List<string> cats = new List<string>();
                foreach (KeyValuePair<string, JsonValue> kv in d["byCategory"].Props)
                {
                    a.Bar.Add(new KeyValuePair<string, string>(kv.Key, Hz.Js(kv.Value)));
                    cats.Add(kv.Key + " " + Hz.Js(kv.Value));
                }
                double enc = d["clips"].Bool ? Format.Round(Num(d, "encoded") / Hz.Number(d["clips"]) * 100) : 0;
                JsonValue si = d["speedIndex"];
                string ix = "";
                if (si["indexed"].Bool) ix = Hz.Number(si["indexed"]) >= Num(si, "clips") ? "Indexed" : "Indexed " + Hz.Tpl(si, "indexed") + "/" + Hz.Tpl(si, "clips");
                a.Title = Hz.DayName(Hz.Tpl(d, "day"));
                a.Sub = Hz.Count(d["recordings"], "track") + " · " + Hz.Tpl(d, "minutes") + " min · " + Hz.Size(d["bytes"]);
                a.Cats = string.Join(" · ", cats) + (d["protected"].Bool ? " · " + Hz.Js(d["protected"]) + " protected" : "") + " · " + (Hz.Ok(enc) ? Format.JsNum(enc) : "NaN") + Converted + (ix.Length > 0 ? " · " : "");
                if (ix.Length > 0) a.Ix = ix.ToLowerInvariant();
                v.Albums.Add(a);
            }
            v.NoDays = list.Count == 0 && !days["error"].Bool;
            // the head (as last drawn)
            if (headDrawn)
            {
                v.Head = true;
                JsonValue hd = JsonValue.Null;
                foreach (JsonValue x in headDays["days"].Items) if (x["day"].IsString && x["day"].Str == Day) { hd = x; break; }
                if (hd["thumbId"].Bool) v.HeadArt = ThumbPath(Car.Id, hd["thumbId"]);
                v.Kind = (Day.Length > 0 ? KindDay : KindLibrary) + " · " + Car.Name;
                v.HeadTitle = Day.Length > 0 ? Hz.DayName(Day, true) : AllDays;
                v.HeadLine = Hz.Count(Total, "track") + " · " + Format.JsNum(Sum(Recs, "minutes", true)) + " min · " + Hz.Size(Sum(Recs, "bytes", false)) + (Cat != "all" ? " · " + Cat + " only" : "") + (Prot ? ProtectedOnlyLine : "");
                v.CanPlay = Recs.Count > 0;
                v.PlayLabel = Day.Length > 0 ? PlayDay : PlayNewest;
                v.DayButtons = Day.Length > 0;
                string inv = Meta["scannedAt"].Bool ? "inventory " + Hz.Since(Meta["scannedAt"], headNow) + (Meta["scanning"].Bool ? ScanningTail : "") : "";
                v.Sum = Car.Name + " · " + Hz.Count(headDays["recordings"].Bool ? headDays["recordings"] : JsonValue.Number(0), "recording") + " on " + Hz.Count(headDays["total"].Bool ? headDays["total"] : JsonValue.Number(0), "day") + " · " + inv + TimesAre + (string.IsNullOrEmpty(Timezone) ? "local" : Timezone) + ")";
                v.Status = StatusText();
            }
            else v.Sum = Car.Name + Loading;
            // the tracks, as the list was last painted (and changed in place since)
            foreach (HzTrack t in domRows) v.Tracks.Add(t);
            v.Empty = domEmpty;
            v.Foot = Shown.Count > 0 ? Shown.Count.ToString(CultureInfo.InvariantCulture) + " of " + (Total == null ? "undefined" : Hz.Tpl(JsonValue.Object("t", Total), "t")) + FootKeys : "";
            // the selection bar
            List<JsonValue> sel = Recs.Where(r => Sel.Contains(IdOf(r))).ToList();
            if (sel.Count > 0)
            {
                HzBulk b = new HzBulk();
                b.Text = sel.Count.ToString(CultureInfo.InvariantCulture) + Selected + Hz.Size(Sum(sel, "bytes", false));
                b.CanUnprotect = sel.Any(r => r["protected"].Bool);
                b.CanTrash = sel.Any(r => !r["protected"].Bool);
                v.Bulk = b;
            }
            return v;
        }

        internal static void T(List<string> o, string s)
        {
            string x = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            if (x.Length > 0) o.Add(x);
        }

        static void TracksOutline(List<string> o, List<HzTrack> rows, string empty)
        {
            foreach (HzTrack t in rows)
            {
                o.Add("[track" + (t.Cur ? " cur" : "") + (t.Sel ? " sel" : "") + " " + t.N + (t.Art != null ? " art=" + t.Art : "") + "]");
                o.Add("[chk" + (t.Checked ? " on" : "") + " " + t.SelectLabel + "]");
                o.Add("[play " + t.PlayLabel + "]");
                T(o, t.Time); T(o, t.Till);
                o.Add("[pill" + (t.CategoryTone.Length > 0 ? " " + t.CategoryTone : "") + "]"); T(o, t.Category);
                T(o, t.Sub);
                if (t.Protected) o.Add("[lock]");
                o.Add("[cams " + t.CamsLabel + "]");
                for (int k = 0; k < Hz.Cams.Length; k++) { o.Add(t.Cams[k] ? "[cam on]" : "[cam]"); T(o, Hz.CamTag(Hz.Cams[k])); }
                if (t.Codec != null) { o.Add("[pill" + (t.CodecTone.Length > 0 ? " " + t.CodecTone : "") + "]"); T(o, t.Codec); }
                T(o, t.Dur); T(o, t.DurSize);
                o.Add("[act " + t.ProtectLabel + "]");
                o.Add("[act " + t.TrashLabel + (t.Protected ? " disabled" : "") + "]");
                o.Add("[act " + t.InfoLabel + "]");
            }
            if (empty != null) { o.Add("[blank]"); T(o, empty); }
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonFootage.test.js reads the web's,
        /// marker for marker (see marks() there).
        /// </summary>
        public List<string> Outline(HzFootageView v)
        {
            List<string> o = new List<string>();
            o.Add("[title]"); T(o, Title);
            if (v.NoVehicle)
            {
                o.Add("[blank]"); T(o, NoVehicleText);
                o.Add("[link " + AddVehicleLink + "]"); T(o, AddVehicle);
                return o;
            }
            T(o, v.Sum);
            o.Add("[btn scan]"); T(o, ScanLabel);
            o.Add(v.IndexBusy ? "[btn index disabled]" : "[btn index]"); T(o, v.IndexText);
            o.Add("[link " + ConvertLink + "]"); T(o, ConvertLabel);
            o.Add("[filters]");
            o.Add("[segs cat]");
            foreach (string[] s in Cats) { o.Add(s[0] == Cat ? "[seg on]" : "[seg]"); T(o, s[1]); }
            o.Add("[segs conv]");
            foreach (string[] s in Convs) { o.Add(s[0] == Conv ? "[seg on]" : "[seg]"); T(o, s[1]); }
            o.Add(Prot ? "[chk prot on]" : "[chk prot]"); T(o, ProtectedOnly);
            o.Add("[input q=" + Q + "]");
            o.Add("[albums]"); T(o, DaysHead); T(o, v.DayCount);
            o.Add(v.AllOn ? "[album on]" : "[album]"); T(o, AllDays); T(o, v.AllSub);
            if (v.DaysError != null) { o.Add("[bad]"); T(o, v.DaysError); }
            foreach (HzAlbum a in v.Albums)
            {
                string bar = string.Join(",", a.Bar.Select(kv => kv.Key + ":" + kv.Value));
                o.Add("[album" + (a.On ? " on" : "") + (a.Art != null ? " art=" + a.Art : "") + (bar.Length > 0 ? " bar=" + bar : "") + "]");
                T(o, a.Title); T(o, a.Sub); T(o, a.Cats);
                if (a.Ix != null) { o.Add("[ix]"); T(o, a.Ix); }
            }
            if (v.NoDays) T(o, NoDaysText);
            o.Add("[head" + (v.HeadArt != null ? " art=" + v.HeadArt : "") + "]");
            if (v.Head)
            {
                T(o, v.Kind); T(o, v.HeadTitle); T(o, v.HeadLine);
                o.Add(v.CanPlay ? "[btn playall]" : "[btn playall disabled]"); T(o, v.PlayLabel);
                if (v.DayButtons) { o.Add("[btn delday]"); T(o, DelDay); o.Add("[btn delthru]"); T(o, DelThru); }
            }
            if (v.Bulk != null)
            {
                o.Add("[bulk]"); T(o, v.Bulk.Text);
                o.Add("[bulk protect]"); T(o, ProtectWord);
                o.Add(v.Bulk.CanUnprotect ? "[bulk unprotect]" : "[bulk unprotect disabled]"); T(o, UnprotectWord);
                o.Add(v.Bulk.CanTrash ? "[bulk trash]" : "[bulk trash disabled]"); T(o, MoveToBin);
                o.Add("[bulk all]"); T(o, SelectAll);
                o.Add("[bulk clear]"); T(o, ClearWord);
            }
            o.Add("[tracks]");
            TracksOutline(o, v.Tracks, v.Empty);
            o.Add("[foot]"); T(o, v.Foot);
            return o;
        }

        // ---------------------------------------------------------------- the filters

        /// <summary>A category: kept, then the days and the recordings again.</summary>
        public Task SetCat(string c)
        {
            Cat = c;
            Store("ft.cat", JsonValue.Quote(c));
            return Task.WhenAll(LoadDays(), LoadRecs());
        }

        /// <summary>A conversion filter: kept, the tracks drawn again.</summary>
        public void SetConv(string c)
        {
            Conv = c;
            Store("ft.conv", JsonValue.Quote(c));
            DrawTracks();
        }

        /// <summary>Protected only.</summary>
        public Task SetProt(bool on)
        {
            Prot = on;
            return LoadRecs();
        }

        /// <summary>The search (120 ms after the last key).</summary>
        public void Search(string text)
        {
            Q = text ?? "";
            Cur = 0;
            DrawTracks();
        }

        /// <summary>pickDay(day): "" for All days.</summary>
        public Task PickDay(string day)
        {
            Day = day ?? "";
            Cur = 0;
            return LoadRecs();
        }

        // ---------------------------------------------------------------- the tracks

        /// <summary>A track's checkbox (the row's index): the box turns, the selection follows it.</summary>
        public void Check(int i)
        {
            HzTrack t = domRows[i];
            Cur = i;
            t.Checked = !t.Checked;
            if (t.Checked) Sel.Add(t.Id); else Sel.Remove(t.Id);
            t.Sel = t.Checked;
        }

        /// <summary>The recording of a painted row (recs.find by its data-id).</summary>
        JsonValue RowRec(int i)
        {
            string id = domRows[i].Id;
            return Recs.FirstOrDefault(r => IdOf(r) == id);
        }

        /// <summary>A click on a track (not a button): f.cur only.</summary>
        public void Click(int i)
        {
            Cur = i;
        }

        /// <summary>A track's play, protect, trash or info button.</summary>
        public Task TrackAct(int i, string act)
        {
            Cur = i;
            JsonValue r = RowRec(i);
            switch (act)
            {
                case "play": Play(r); return Task.FromResult(0);
                case "protect": return Protect(new[] { IdOf(r) }, !r["protected"].Bool);
                case "trash": return Trash(new[] { IdOf(r) });
                case "info": return Info(r);
                default: return Task.FromResult(0);
            }
        }

        /// <summary>A double click on a track: play it.</summary>
        public void Dbl(int i)
        {
            Play(RowRec(i));
        }

        /// <summary>The selection bar's buttons.</summary>
        public Task Bulk(string b)
        {
            IList<string> ids = Sel.ToList();
            switch (b)
            {
                case "protect": return Protect(ids, true);
                case "unprotect": return Protect(ids, false);
                case "trash": return Trash(ids);
                case "all": foreach (JsonValue r in Shown) Sel.Add(IdOf(r)); DrawTracks(); break;
                case "clear": Sel.Clear(); DrawTracks(); break;
            }
            return Task.FromResult(0);
        }

        /// <summary>play(r): the Viewer with the recording.</summary>
        public void Play(JsonValue r)
        {
            if (r == null) return;
            Ui.Go("#/viewer?rec=" + Hz.EncId(IdOf(r)));
        }

        /// <summary>Play day / Play newest day: the day picked, else the first recording's, else the newest date.</summary>
        public void PlayAll()
        {
            string day = Day;
            if (day.Length == 0 && Recs.Count > 0 && Recs[0]["day"].Bool) day = Hz.Js(Recs[0]["day"]);
            if (day.Length == 0 && Days["dates"][0].Bool) day = Hz.Js(Days["dates"][0]);
            if (day.Length > 0) Ui.Go("#/viewer?day=" + Hz.EncId(day));
        }

        // ---------------------------------------------------------------- actions

        /// <summary>Refresh inventory.</summary>
        public Task Scan()
        {
            return Ui.Act(async () =>
            {
                await Api.Post(V + "/scan", JsonValue.Object(), CancellationToken.None);
                Ui.Toast(ScanToast, "ok");
            });
        }

        public async Task Protect(IList<string> ids, bool on)
        {
            if (ids.Count == 0) return;
            await Ui.Act(async () =>
            {
                await Api.Post(V + "/protect", Footage.ProtectBody(ids, on), CancellationToken.None);
                Ui.Toast(on ? "Protected " + Hz.Count(ids.Count, "recording") + ProtectedSkip + (ids.Count == 1 ? "it" : "them") + "." : ProtectionRemoved, "ok");
            });
            await LoadRecs();
        }

        public static HzAsk TrashAsk(int movable, double bytes, int stay)
        {
            HzAsk a = new HzAsk();
            a.Title = "Move " + Hz.Count(movable, "recording") + " to the Recycle Bin?";
            a.Para("", new Rich().Bold(Hz.Size(bytes)).Text(TrashBody));
            if (stay > 0) a.Para("dim", new Rich().Text(stay.ToString(CultureInfo.InvariantCulture) + TrashStay));
            a.Yes = MoveToBin;
            a.Danger = true;
            return a;
        }

        /// <summary>The Recycle Bin's answer as its toast: text and tone.</summary>
        public static string[] TrashToast(JsonValue o)
        {
            JsonValue sk = o["skipped"];
            bool skipped = sk.IsArray && sk.Count > 0;
            string why = "";
            if (skipped)
            {
                List<string> reasons = new List<string>();
                foreach (JsonValue s in sk.Items) { string x = Hz.Js(s["reason"]); if (!reasons.Contains(x)) reasons.Add(x); }
                why = " " + sk.Count.ToString(CultureInfo.InvariantCulture) + " skipped (" + string.Join(", ", reasons) + ").";
            }
            JsonValue moved = o["moved"];
            if (Hz.Missing(o, "moved") || moved.IsNull) throw new InvalidOperationException(ReadErr(o, "moved", "length"));
            JsonValue n = moved.IsArray ? JsonValue.Number(moved.Count) : moved.IsString ? JsonValue.Number(moved.Str.Length) : JsonValue.Null;
            string text = Hz.Count(n, "recording") + MovedToBin + Hz.Size(o["bytes"]) + ")." + why;
            return new[] { text, skipped && !n.Bool ? "bad" : "ok" };
        }

        /// <summary>trash(ids): protected ones stay; the dialog; the move; the recordings and days again.</summary>
        public async Task Trash(IList<string> ids)
        {
            List<JsonValue> list = Recs.Where(r => ids.Contains(IdOf(r))).ToList();
            List<JsonValue> movable = list.Where(r => !r["protected"].Bool).ToList();
            if (movable.Count == 0) { Ui.Toast(StayWhere, "bad"); return; }
            double b = Sum(movable, "bytes", false);
            if (!await Ui.Ask(TrashAsk(movable.Count, b, list.Count - movable.Count))) return;
            await Ui.Act(async () =>
            {
                JsonValue o = await Api.Post(V + "/trash", Footage.TrashBody(movable.Select(IdOf).ToList()), CancellationToken.None);
                string[] t = TrashToast(o);
                Ui.Toast(t[0], t[1]);
                foreach (JsonValue r in movable) Sel.Remove(IdOf(r));
            });
            await Task.WhenAll(LoadRecs(), LoadDays());
        }

        public static HzAsk TrashDaysAsk(string day, bool through, string cat, JsonValue pv)
        {
            HzAsk a = new HzAsk();
            a.Title = through ? ThruAsk : DayAsk;
            string tail = " from " + Hz.DayName(day, true);
            if (through)
            {
                tail += " and older";
                if (Hz.Number(pv["days"]) > 1 && !Hz.Missing(pv, "days")) tail += " (" + Hz.Tpl(pv, "days") + " days, the oldest " + Hz.Js(pv["oldest"]) + ")";
            }
            if (cat != "all") tail += " (" + cat + AsFiltered;
            a.Para("", new Rich().Bold(Hz.Count(pv["recordings"], "recording")).Text(" · ").Bold(Hz.Size(pv["bytes"])).Text(tail + "."));
            a.Para("dim", new Rich().Text(NothingDeleted + (pv["protected"].Bool ? " " + Hz.Count(pv["protected"], "protected recording") + " stay." : "")));
            a.Yes = through ? ThruYes : DayYes;
            a.Danger = true;
            return a;
        }

        /// <summary>trashDays(day, through): the server's preview, the dialog, the move (a job).</summary>
        public async Task TrashDays(string day, bool through)
        {
            JsonValue pv;
            try { pv = await Api.Post(V + "/trash-days", Footage.TrashDaysBody(day, through, Cat, true), CancellationToken.None); }
            catch (Exception ex) { Ui.Toast(ex.Message, "bad"); return; }
            if (!pv["recordings"].Bool) { Ui.Toast(pv["protected"].Bool ? AllProtected : NothingToMove, "ok"); return; }
            if (!await Ui.Ask(TrashDaysAsk(day, through, Cat, pv))) return;
            await Ui.Act(async () =>
            {
                JsonValue o = await Api.Post(V + "/trash-days", Footage.TrashDaysBody(day, through, Cat, false), CancellationToken.None);
                Ui.Toast("Moving " + Hz.Count(o["recordings"], "recording") + " (" + Hz.Size(o["bytes"]) + ")" + MovingTail, "ok");
            });
        }

        /// <summary>The details sheet of a recording's detail answer (r: the track, for its thumbnail).</summary>
        public HzFootInfo InfoView(JsonValue r, JsonValue d, bool h24)
        {
            HzFootInfo i = new HzFootInfo();
            string start = Hz.WallDay(Hz.Or(d["start"]), h24);
            i.Title = "Track " + start;
            if (r["thumbRel"].Bool || r["frontRel"].Bool) { i.Art = ThumbPath(Car.Id, r["id"]); i.Alt = ThumbnailOf + start; }
            i.Kv.Add(new[] { KvType, Hz.Words(Hz.Or(d["type"])) + " · " + Hz.Js(d["category"]) });
            i.Kv.Add(new[] { KvStartEnd, start + " → " + Hz.WallDay(Hz.Or(d["end"]), h24) });
            if (d["eventAt"].Bool) i.Kv.Add(new[] { KvEventAt, Hz.WallDay(Hz.Js(d["eventAt"]), h24) });
            JsonValue ev = d["event"];
            if (ev.Bool)
            {
                List<string> parts = new List<string>();
                string reason = Hz.Words(Hz.Or(ev["reason"]));
                if (reason.Length > 0) parts.Add(reason);
                if (ev["city"].Bool) parts.Add(Hz.Js(ev["city"]));
                if (ev["street"].Bool) parts.Add(Hz.Js(ev["street"]));
                i.Kv.Add(new[] { KvEvent, string.Join(" · ", parts) });
                if (ev["est_lat"].Bool) i.EventCoords = Hz.Js(ev["est_lat"]) + ", " + Hz.Js(ev["est_lon"]);
            }
            i.Kv.Add(new[] { KvCameras, CamNames(d["cameras"], ", ") });
            i.Kv.Add(new[] { KvSize, Hz.Size(d["bytes"]) + " · " + Hz.Count(d["fileCount"], "file") + " · " + Hz.Count(d["clipCount"], "clip") });
            JsonValue e = d["encoded"];
            List<string> codecs = new List<string>();
            foreach (JsonValue c in e["codecs"].Items) codecs.Add(Hz.CodecName(Hz.Js(c)) ?? Hz.Js(c));
            i.Kv.Add(new[] { KvConverted, (e["encoded"].IsNull ? "0" : Hz.Js(e["encoded"])) + " of " + (e["clips"].IsNull ? "0" : Hz.Js(e["clips"])) + " clips " + string.Join(", ", codecs) });
            i.Protected = d["protected"].Bool;
            i.Kv.Add(new[] { KvProtected, i.Protected ? "yes" : "no" });
            i.Kv.Add(new[] { KvFolder, Hz.Js(d["folderPath"]) });
            foreach (JsonValue x in d["files"].Items)
            {
                HzFootFile f = new HzFootFile();
                string rel = Hz.Js(x["rel"]);
                f.Name = rel.Substring(rel.LastIndexOf('/') + 1);
                if (x.Has("codec"))
                {
                    if (x["codec"].Bool) { string c = Hz.Js(x["codec"]); f.Codec = Hz.CodecName(c) ?? c; f.Tone = c == "h264" ? "" : "mag"; }
                    else f.Codec = Checking;
                }
                else f.Codec = Asset;
                f.Size = Hz.Size(x["size"]);
                f.Path = Hz.Js(x["path"]);
                f.Href = Footage.MediaPath(f.Path);
                i.Files.Add(f);
            }
            return i;
        }

        /// <summary>info(r): the detail (a failure a red toast), the sheet, then what was pressed in it.</summary>
        public async Task Info(JsonValue r)
        {
            JsonValue d;
            try { d = await Api.Get(V + "/recordings/" + Hz.EncId(IdOf(r)), CancellationToken.None); }
            catch (Exception ex) { Ui.Toast(ex.Message, "bad"); return; }
            string done = await Sheet(InfoView(r, d, H24));
            if (done == "play") Play(r);
            else if (done == "protect") await Protect(new[] { IdOf(r) }, !d["protected"].Bool);
            else if (done == "trash") await Trash(new[] { IdOf(r) });
        }

        /// <summary>appearance.clock24, for the sheet.</summary>
        public bool H24;

        /// <summary>The event stream's topics: inventory or bin load the days and recordings again.</summary>
        public Task Topics(ICollection<string> t)
        {
            if (t.Contains("inventory") || t.Contains("bin")) return Task.WhenAll(LoadDays(), LoadRecs());
            return Task.FromResult(0);
        }

        // ---------------------------------------------------------------- Index speeds

        /// <summary>Index speeds is following a pass (waiting 3 s for the next status).</summary>
        public bool IndexFollowing { get { return following; } }

        /// <summary>followIndex(b, start): starts a pass (start) or looks for one running; while it runs the button shows its progress.</summary>
        public async Task IndexBegin(bool start)
        {
            if (following) return;
            following = true;
            followStart = start;
            followWas = false;
            JsonValue s;
            try
            {
                s = start ? await Api.Post("/api/speeds/index", JsonValue.Object(), CancellationToken.None) : await Api.Get("/api/speeds/status", CancellationToken.None);
            }
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

        // ---------------------------------------------------------------- keys

        /// <summary>The work a key started (protect, trash, details), done or not.</summary>
        public Task KeyWork = Task.FromResult(0);

        /// <summary>
        /// onKey(e): true when the page took the key. onButton: the key went to
        /// a button or link (Enter is then theirs).
        /// </summary>
        public bool Key(string key, bool onButton)
        {
            KeyWork = Task.FromResult(0);
            JsonValue r = Cur >= 0 && Cur < Shown.Count ? Shown[Cur] : null;
            switch (key)
            {
                case "j": case "ArrowDown": Move(Cur + 1); return true;
                case "k": case "ArrowUp": Move(Cur - 1); return true;
                case "Enter": if (r != null && !onButton) { Play(r); return true; } return false;
                case "x":
                    if (r != null) { Sel.Toggle(IdOf(r)); DrawTracks(); }
                    return true;
                case "p":
                    if (r != null) KeyWork = Protect(Sel.Count > 0 ? Sel.ToList() : (IList<string>)new[] { IdOf(r) }, Sel.Count > 0 ? true : !r["protected"].Bool);
                    return true;
                case "Delete": case "Backspace":
                    if (r != null) KeyWork = Trash(Sel.Count > 0 ? Sel.ToList() : (IList<string>)new[] { IdOf(r) });
                    return true;
                case "i": if (r != null) KeyWork = Info(r); return true;
                case "/": Focus = "ft-q"; return true;
                case "Escape":
                    if (Sel.Count > 0) { Sel.Clear(); DrawTracks(); return true; }
                    return false;
                default: return false;
            }
        }

        void Move(int i)
        {
            if (Shown.Count == 0) return;
            if (Cur < domRows.Count) domRows[Cur].Cur = false;
            Cur = Math.Max(0, Math.Min(Shown.Count - 1, i));
            if (Cur < domRows.Count) domRows[Cur].Cur = true;
            Focus = "play";
        }
    }
}
