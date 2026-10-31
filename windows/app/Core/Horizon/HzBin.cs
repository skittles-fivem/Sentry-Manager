using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>One item of the Recycle Bin list (hz/pages/bin.js li.binit).</summary>
    public sealed class HzBinRow
    {
        /// <summary>data-id: the item's id as the page writes it.</summary>
        public string Id;
        public string Label;
        /// <summary>"3 files · 412 MB · moved Oct 31, 2026, 1:10:02 PM[ · Model 3]".</summary>
        public string Line;
        /// <summary>"Some files could not be restored: the name is taken." is shown.</summary>
        public bool Conflict;
        /// <summary>The pill: "by policy" (mag) or "by you".</summary>
        public bool ByPolicy;
        /// <summary>Ticked (li.binit.sel).</summary>
        public bool Sel;
    }

    /// <summary>The Recycle Bin page as hz/pages/bin.js draw() works it out.</summary>
    public sealed class HzBinView
    {
        /// <summary>#b-sub: "3 items · 3.6 GB · Model 3".</summary>
        public string Sub;
        /// <summary>status(): "3 items · 3.6 GB".</summary>
        public string Status;
        /// <summary>Empty… can be pressed (some item is there).</summary>
        public bool CanEmpty;
        /// <summary>Restore selected can be pressed (something is ticked).</summary>
        public bool CanRestore;
        /// <summary>"Select all" is ticked: every row shown is.</summary>
        public bool AllChecked;
        public List<HzBinRow> Rows = new List<HzBinRow>();
        /// <summary>The blank's text when no row is shown; null otherwise.</summary>
        public string Empty;
    }

    /// <summary>
    /// Horizon's Recycle Bin page (examples/ui-packs/horizon/web/hz/pages/bin.js):
    /// the items of the vehicle shown or of every vehicle, a text filter,
    /// ticks with Select all and Restore selected, each item's Restore and
    /// Delete forever…, and Empty… The same GET /api/bin and POST
    /// /api/bin/:id/restore, /api/bin/:id/delete and /api/bin/empty as the
    /// built-in page (Core/Bin.cs: the Empty body is its EmptyBody); after a
    /// change the shell reads the vehicles again (the bin counts,
    /// ShellModel.ReloadVehicles), then the list. The words are the pack's.
    /// Checked by test/windowsNativeHorizonBin.test.js and Core.Tests HorizonBinTests.
    /// </summary>
    public sealed class HzBin
    {
        public const string Title = "Recycle Bin";
        public const string RestoreSelected = "Restore selected";
        public const string EmptyLabel = "Empty…";
        public const string Note = "Nothing here is deleted by itself. Restore puts recordings back exactly where they were; only Delete forever and Empty free the space.";
        public const string SelectAll = "Select all";
        public const string FilterPlaceholder = "Filter";
        public const string FilterAria = "Filter the Recycle Bin";
        public const string ConflictLine = "Some files could not be restored: the name is taken.";
        public const string ByPolicy = "by policy";
        public const string ByYou = "by you";
        public const string RestoreLabel = "Restore";
        public const string DeleteLabel = "Delete forever";
        public const string NoMatch = "Nothing matches.";
        public const string IsEmpty = "The Recycle Bin is empty.";
        public const string EveryVehicle = " · every vehicle";
        public const string Moved = " · moved ";
        public const string NotRestored = " file(s) could not be (name taken).";
        public const string DeleteTitle = "Delete forever?";
        public const string DeleteRest = " is deleted from the share for good. This cannot be undone.";
        public const string Deleted = "Deleted.";
        public const string EmptyTitle = "Empty the Recycle Bin?";
        public const string EmptyRest = " are deleted for good. This cannot be undone.";
        public const string EmptyYes = "Empty Recycle Bin";
        public const string EveryVehicleName = "every vehicle";

        /// <summary>The vehicle filter's buttons.</summary>
        public static readonly string[][] Scopes = { new[] { "car", "This vehicle" }, new[] { "all", "Every vehicle" } };

        // mount()'s let scope, items, q and const sel
        public string Scope = "car";
        public string Q = "";
        public string Typed = "";
        public JsonValue Items = JsonValue.Array(new JsonValue[0]);
        /// <summary>The ticked ids, in the order they were ticked (a Set).</summary>
        public readonly List<string> Sel = new List<string>();
        /// <summary>
        /// Restore selected was pressed: its act() enables the button again
        /// after the list was drawn without ticks, until the next draw.
        /// </summary>
        public bool RestoreLeftOn;

        /// <summary>The reader typed in "Filter" (the page draws again 120 ms later).</summary>
        public void Filter(string typed)
        {
            Typed = typed ?? "";
            Q = Typed.ToLowerInvariant();
            RestoreLeftOn = false;
        }

        /// <summary>/api/bin, with ?vehicle= for "This vehicle" and a vehicle shown.</summary>
        public static string Path(string scope, HzCar v)
        {
            return "/api/bin" + (scope == "car" && v != null ? "?vehicle=" + Hz.EncId(v.Id) : "");
        }

        static bool IdIs(JsonValue item, string id)
        {
            JsonValue v = item["id"];
            return v != null && v.IsString && v.Str == id;
        }

        bool Has(string id)
        {
            foreach (JsonValue i in Items.Items) if (IdIs(i, id)) return true;
            return false;
        }

        /// <summary>load(): the items for the scope; ticks of items that are gone are dropped (a failure throws).</summary>
        public async Task Load(IJsonApi api, HzCar v, CancellationToken ct)
        {
            JsonValue r = await api.Get(Path(Scope, v), ct);
            Items = r["items"];
            if (Sel.Count > 0 && !Items.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'some')");
            Sel.RemoveAll(id => !Has(id));
            RestoreLeftOn = false;
        }

        bool Matches(JsonValue i)
        {
            if (string.IsNullOrEmpty(Q)) return true;
            return (Hz.Tpl(i, "label") + " " + Hz.Tpl(i, "reason")).ToLowerInvariant().Contains(Q);
        }

        /// <summary>The rows the filter shows.</summary>
        public List<JsonValue> Shown()
        {
            List<JsonValue> l = new List<JsonValue>();
            foreach (JsonValue i in Items.Items) if (Matches(i)) l.Add(i);
            return l;
        }

        /// <summary>
        /// items.reduce((n, i) =&gt; n + i.bytes, 0) as JavaScript adds: numbers
        /// add, null is 0, undefined makes NaN, a string joins as text.
        /// </summary>
        public object Total()
        {
            object acc = 0.0;
            foreach (JsonValue i in Items.Items)
            {
                JsonValue b = i["bytes"];
                bool missing = Hz.Missing(i, "bytes");
                if (acc is string)
                {
                    acc = (string)acc + (missing ? "undefined" : b.IsNull ? "null" : Hz.Js(b));
                    continue;
                }
                double n = (double)acc;
                if (!missing && b.IsString) { acc = Format.JsNum(n) + b.Str; continue; }
                acc = n + (missing ? double.NaN : Hz.Number(b.IsArray || b.IsObject ? JsonValue.String(Hz.Js(b)) : b));
            }
            return acc;
        }

        static string SizeOf(object total)
        {
            string s = total as string;
            if (s != null) return Hz.Size(JsonValue.String(s));
            return Hz.Size((double)total);
        }

        /// <summary>i.files?.length || 0.</summary>
        static double FileCount(JsonValue i)
        {
            JsonValue f = i["files"];
            if (Hz.Missing(i, "files") || f.IsNull) return 0;
            if (f.IsArray) return f.Count;
            if (f.IsString) return f.Str.Length;
            return 0;
        }

        /// <summary>
        /// draw(): the rows the filter shows and the lines over them. names
        /// is app.vehicles (for "Every vehicle": each item's vehicle). A list
        /// that is not a list throws, as items.filter does.
        /// </summary>
        public HzBinView View(HzCar v, IList<HzCar> names, bool h24)
        {
            if (!Items.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            HzBinView view = new HzBinView();
            List<JsonValue> rows = Shown();
            string size = SizeOf(Total());
            view.Sub = Hz.Count(Items.Count, "item") + " · " + size + (Scope == "car" && v != null ? " · " + v.Name : EveryVehicle);
            view.Status = Hz.Count(Items.Count, "item") + " · " + size;
            view.CanEmpty = Items.Count > 0;
            view.CanRestore = Sel.Count > 0 || RestoreLeftOn;
            bool all = rows.Count > 0;
            foreach (JsonValue i in rows) if (!Sel.Exists(id => IdIs(i, id))) all = false;
            view.AllChecked = all;
            foreach (JsonValue i in rows)
            {
                HzBinRow r = new HzBinRow();
                r.Id = Hz.Js(i["id"]);
                r.Label = Hz.Js(i["label"]);
                r.Sel = Sel.Exists(id => IdIs(i, id));
                string line = Hz.Count(FileCount(i), "file") + " · " + Hz.Size(i["bytes"]) + Moved + Hz.StampFull(i["trashedAt"], h24);
                if (Scope == "all") line += " · " + NameOf(i, names);
                r.Line = line;
                foreach (JsonValue f in i["files"].Items) if (f["conflict"].Bool) r.Conflict = true;
                r.ByPolicy = i["reason"].IsString && i["reason"].Str == "policy";
                view.Rows.Add(r);
            }
            if (view.Rows.Count == 0) view.Empty = Items.Count > 0 ? NoMatch : IsEmpty;
            return view;
        }

        /// <summary>app.vehicles.find((x) =&gt; x.id === id)?.name || id.</summary>
        static string NameOf(JsonValue item, IList<HzCar> names)
        {
            JsonValue id = item["vehicleId"];
            if (names != null && id != null && id.IsString)
                foreach (HzCar c in names) if (c.Id == id.Str && !string.IsNullOrEmpty(c.Name)) return c.Name;
            return Hz.Js(id);
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonBin.test.js reads the web's:
        /// [title] Recycle Bin, the sub line, [btn( disabled)] Restore selected,
        /// [btn danger( disabled)] Empty…, [note grn], [segs bs] with [seg( on)],
        /// [check all( on)] Select all, [input q=…], [list], then per item
        /// [item( sel)] [check( on)] label, line, ([warm] …), [pill( mag)],
        /// [btn sm] Restore [btn sm danger] Delete forever; or [blank] and its text.
        /// </summary>
        public List<string> Outline(HzBinView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[title]"); t(Title); t(v.Sub);
            o.Add(v.CanRestore ? "[btn]" : "[btn disabled]"); t(RestoreSelected);
            o.Add(v.CanEmpty ? "[btn danger]" : "[btn danger disabled]"); t(EmptyLabel);
            o.Add("[note grn]"); t(Note);
            o.Add("[segs bs]");
            foreach (string[] s in Scopes) { o.Add(s[0] == Scope ? "[seg on]" : "[seg]"); t(s[1]); }
            o.Add(v.AllChecked ? "[check all on]" : "[check all]"); t(SelectAll);
            o.Add("[input q=" + Typed + "]");
            o.Add("[list]");
            if (v.Empty != null) { o.Add("[blank]"); t(v.Empty); return o; }
            foreach (HzBinRow r in v.Rows)
            {
                o.Add(r.Sel ? "[item sel]" : "[item]");
                o.Add(r.Sel ? "[check on]" : "[check]");
                t(r.Label); t(r.Line);
                if (r.Conflict) { o.Add("[warm]"); t(ConflictLine); }
                o.Add(r.ByPolicy ? "[pill mag]" : "[pill]"); t(r.ByPolicy ? ByPolicy : ByYou);
                o.Add("[btn sm]"); t(RestoreLabel);
                o.Add("[btn sm danger]"); t(DeleteLabel);
            }
            return o;
        }

        // ---------------------------------------------------------------- ticks

        /// <summary>An item's box ticked or unticked.</summary>
        public void Tick(string id, bool on)
        {
            RestoreLeftOn = false;
            if (on) { if (!Sel.Contains(id)) Sel.Add(id); }
            else Sel.Remove(id);
        }

        /// <summary>Select all: every row the filter shows ticked (or unticked).</summary>
        public void SelectAllShown(bool on)
        {
            foreach (JsonValue i in Shown()) Tick(Hz.Js(i["id"]), on);
            RestoreLeftOn = false;
        }

        // ---------------------------------------------------------------- the buttons

        static string ItemPath(string id, string what)
        {
            return "/api/bin/" + Hz.EncId(id) + "/" + what;
        }

        /// <summary>loadCars().catch(() =&gt; {}): the shell's vehicles again (never throws).</summary>
        static async Task Quiet(Func<Task> reloadCars)
        {
            try { await reloadCars(); } catch (Exception) { }
        }

        /// <summary>
        /// restore(ids): each in turn (a failure is a red toast), then
        /// "Restored N items." or "Restored N; C file(s) could not be (name
        /// taken)." (red), the ticks dropped, the vehicles and the list again.
        /// </summary>
        public async Task Restore(IJsonApi api, HzUi ui, List<string> ids, Func<Task> reloadCars, Func<Task> load)
        {
            int done = 0;
            object clash = 0.0;
            foreach (string id in new List<string>(ids))
            {
                try
                {
                    JsonValue o = await api.Post(ItemPath(id, "restore"), JsonValue.Object(), CancellationToken.None);
                    done++;
                    clash = Add(clash, o["conflicts"].Bool ? o["conflicts"] : JsonValue.Number(0));
                }
                catch (Exception ex) { ui.Toast(ex.Message, "bad"); }
            }
            if (done > 0)
            {
                bool bad = Truthy(clash);
                ui.Toast(bad ? "Restored " + done + "; " + Text(clash) + NotRestored : "Restored " + Hz.Count(done, "item") + ".", bad ? "bad" : "ok");
            }
            foreach (string id in ids) Sel.Remove(id);
            await Quiet(reloadCars);
            await load();
        }

        /// <summary>An item's Restore: act(its button, restore([id])) (a failure of the list's reading is a red toast).</summary>
        public Task RestoreOne(IJsonApi api, HzUi ui, string id, Func<Task> reloadCars, Func<Task> load)
        {
            return ui.Act(() => Restore(api, ui, new List<string> { id }, reloadCars, load));
        }

        /// <summary>Restore selected: act(the button, restore([...sel])); the button is enabled again after.</summary>
        public async Task RestoreTicked(IJsonApi api, HzUi ui, Func<Task> reloadCars, Func<Task> load)
        {
            await ui.Act(() => Restore(api, ui, new List<string>(Sel), reloadCars, load));
            RestoreLeftOn = true;
        }

        // clash += out.conflicts || 0, as JavaScript adds
        static object Add(object acc, JsonValue v)
        {
            if (acc is string) return (string)acc + Hz.Js(v);
            if (v.IsString) return Format.JsNum((double)acc) + v.Str;
            return (double)acc + Hz.Number(v.IsArray || v.IsObject ? JsonValue.String(Hz.Js(v)) : v);
        }

        static bool Truthy(object v)
        {
            string s = v as string;
            if (s != null) return s.Length > 0;
            double d = (double)v;
            return d != 0 && !double.IsNaN(d);
        }

        static string Text(object v)
        {
            string s = v as string;
            return s ?? Format.JsNum((double)v);
        }

        JsonValue Item(string id)
        {
            foreach (JsonValue i in Items.Items) if (IdIs(i, id)) return i;
            return null;
        }

        public static HzAsk DeleteAsk(JsonValue item)
        {
            HzAsk a = new HzAsk();
            a.Title = DeleteTitle;
            a.Para("", new Rich().Bold(Hz.Js(item["label"])).Text(" (" + Hz.Size(item["bytes"]) + ")" + DeleteRest));
            a.Yes = DeleteLabel;
            a.Danger = true;
            return a;
        }

        /// <summary>
        /// Delete forever: "Delete forever?" (no: nothing), POST
        /// /api/bin/:id/delete { confirm: true }, "Deleted." (a failure a red
        /// toast), the vehicles and the list again. An item that is not in the
        /// list throws, as item.label does.
        /// </summary>
        public async Task Delete(IJsonApi api, HzUi ui, string id, Func<Task> reloadCars, Func<Task> load)
        {
            JsonValue item = Item(id);
            if (item == null) throw new InvalidOperationException("Cannot read properties of undefined (reading 'label')");
            if (!await ui.Ask(DeleteAsk(item))) return;
            await ui.Act(async () =>
            {
                await api.Post(ItemPath(id, "delete"), JsonValue.Object("confirm", true), CancellationToken.None);
                ui.Toast(Deleted, "ok");
            });
            await Quiet(reloadCars);
            await load();
        }

        public HzAsk EmptyAsk(HzCar v)
        {
            HzAsk a = new HzAsk();
            a.Title = EmptyTitle;
            a.Para("", new Rich().Text(Hz.Count(Items.Count, "item") + " (").Bold(SizeOf(Total())).Text(") of " + (v != null ? v.Name : EveryVehicleName) + EmptyRest));
            a.Yes = EmptyYes;
            a.Danger = true;
            return a;
        }

        /// <summary>
        /// Empty…: the vehicle shown for "This vehicle", none for "Every
        /// vehicle"; "Empty the Recycle Bin?" (no: nothing), POST
        /// /api/bin/empty { confirm, vehicle }, "Deleted N items (SIZE)." (red
        /// when some failed; a failure a red toast), the vehicles and the list
        /// again. busy is act()'s disabled button.
        /// </summary>
        public async Task Empty(IJsonApi api, HzUi ui, HzCar shown, Action<bool> busy, Func<Task> reloadCars, Func<Task> load)
        {
            HzCar v = Scope == "car" ? shown : null;
            if (!await ui.Ask(EmptyAsk(v))) return;
            busy(true);
            await ui.Act(async () =>
            {
                JsonValue o = await api.Post("/api/bin/empty", Bin.EmptyBody(v != null ? v.Id : null), CancellationToken.None);
                ui.Toast("Deleted " + Hz.Count(o["deleted"], "item") + " (" + Hz.Size(o["bytes"]) + ").", o["failed"].Bool ? "bad" : "ok");
            });
            busy(false);
            await Quiet(reloadCars);
            await load();
        }
    }
}
