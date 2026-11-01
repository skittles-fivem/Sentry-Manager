using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>One row of Flux's Recycle Bin table (flux/pages/bin.js tr[data-item]).</summary>
    public sealed class FxBinRow
    {
        /// <summary>data-item: the item's id as the page writes it.</summary>
        public string Id;
        public string Label;
        /// <summary>"3 files" (and " · some files could not be restored (name taken)").</summary>
        public string Files;
        /// <summary>The vehicle's name (the Every vehicle table only; null otherwise).</summary>
        public string Vehicle;
        public string Size, Moved;
        /// <summary>The chip: "policy" (violet) or "you".</summary>
        public bool ByPolicy;
        /// <summary>Ticked (tr.sel).</summary>
        public bool Sel;
    }

    /// <summary>The Recycle Bin page as flux/pages/bin.js draw() works it out.</summary>
    public sealed class FxBinView
    {
        /// <summary>#bin-sub: "3 items · 3.7 GB · Model 3".</summary>
        public string Sub;
        /// <summary>Empty… can be pressed (some item is there).</summary>
        public bool CanEmpty;
        /// <summary>Restore selected can be pressed (something is ticked).</summary>
        public bool CanRestore;
        /// <summary>"Select all" is ticked: every row shown is.</summary>
        public bool AllChecked;
        /// <summary>The table has the vehicle column (Every vehicle).</summary>
        public bool WithVehicle;
        public List<FxBinRow> Rows = new List<FxBinRow>();
        /// <summary>The empty box's text when no row is shown; null otherwise.</summary>
        public string Empty;
    }

    /// <summary>
    /// Flux's Recycle Bin page (examples/ui-packs/flux/web/flux/pages/bin.js):
    /// the items of the vehicle shown or of every vehicle in a table, a text
    /// filter (/ focuses it), ticks with the header's Select all and Restore
    /// selected, each row's Restore and Delete forever, and Empty… The same
    /// GET /api/bin and POST /api/bin/:id/restore, /api/bin/:id/delete and
    /// /api/bin/empty as the built-in and Horizon pages (HzBin's paths and
    /// JavaScript sums, Core/Bin.cs's EmptyBody); after a change the shell
    /// reads the vehicles again (loadVehicles()), then the list. The words
    /// and layout are Flux's. Checked by test/windowsNativeFluxBin.test.js
    /// and Core.Tests FluxBinTests.
    /// </summary>
    public sealed class FxBin
    {
        public const string Title = "Recycle Bin";
        public const string EmptyLabel = "Empty…";
        public const string Note = "Nothing here is deleted automatically. Restore puts recordings back exactly where they were; only Delete forever and Empty free the space.";
        public const string FilterPlaceholder = "Filter  ( / )";
        public const string FilterLabel = "Filter the Recycle Bin";
        public const string RestoreSelected = "Restore selected";
        public const string SelectAll = "Select all";
        public const string ConflictLine = " · some files could not be restored (name taken)";
        public const string ByPolicy = "policy";
        public const string ByYou = "you";
        public const string RestoreLabel = "Restore";
        public const string DeleteLabel = "Delete forever";
        public const string NoMatch = "Nothing matches.";
        public const string IsEmpty = "The Recycle Bin is empty.";
        public const string EveryVehicle = " · every vehicle";
        public const string NotRestored = " file(s) could not be (name taken).";
        public const string DeleteTitle = "Delete forever?";
        public const string DeleteRest = " is permanently deleted from the share. This cannot be undone.";
        public const string Deleted = "Deleted.";
        public const string EmptyTitle = "Empty the Recycle Bin?";
        public const string EmptyRest = " are permanently deleted. This cannot be undone.";
        public const string EmptyYes = "Empty Recycle Bin";
        public const string EveryVehicleName = "every vehicle";
        public const string CommandLabel = "Restore every item shown";
        public static readonly string[] Columns = { "recording", "size", "moved", "why", "actions" };
        public const string VehicleColumn = "vehicle";

        /// <summary>The vehicle filter's buttons.</summary>
        public static readonly string[][] Scopes = { new[] { "vehicle", "This vehicle" }, new[] { "all", "Every vehicle" } };

        // mount()'s let scope, items, text and const sel
        public string Scope = "vehicle";
        public string Text = "";
        public string Typed = "";
        public JsonValue Items = JsonValue.Array(new JsonValue[0]);
        /// <summary>The ticked ids, in the order they were ticked (a Set).</summary>
        public readonly List<string> Sel = new List<string>();
        /// <summary>
        /// Restore selected was pressed: busy() enables the button again after
        /// the list was drawn without ticks, until the next draw.
        /// </summary>
        public bool RestoreLeftOn;

        /// <summary>The reader typed in the filter (the page draws again 100 ms later).</summary>
        public void Type(string typed)
        {
            Typed = typed ?? "";
            Text = Typed.ToLowerInvariant();
            RestoreLeftOn = false;
        }

        /// <summary>/api/bin, with ?vehicle= for "This vehicle" and a vehicle shown.</summary>
        public static string Path(string scope, HzCar v)
        {
            return "/api/bin" + (scope == "vehicle" && v != null ? "?vehicle=" + Hz.EncId(v.Id) : "");
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
            if (string.IsNullOrEmpty(Text)) return true;
            return (Hz.Tpl(i, "label") + " " + Hz.Tpl(i, "reason")).ToLowerInvariant().Contains(Text);
        }

        /// <summary>The rows the filter shows.</summary>
        public List<JsonValue> Shown()
        {
            List<JsonValue> l = new List<JsonValue>();
            foreach (JsonValue i in Items.Items) if (Matches(i)) l.Add(i);
            return l;
        }

        /// <summary>items.reduce((n, i) =&gt; n + i.bytes, 0), as JavaScript adds (Horizon's page sums the same).</summary>
        public object Total()
        {
            HzBin h = new HzBin();
            h.Items = Items;
            return h.Total();
        }

        static string SizeOf(object total)
        {
            string s = total as string;
            if (s != null) return Fx.Bytes(JsonValue.String(s));
            return Fx.Bytes((double)total);
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
        /// is state.vehicles (for "Every vehicle": each item's vehicle). A
        /// list that is not a list throws, as items.filter does.
        /// </summary>
        public FxBinView View(HzCar v, IList<HzCar> names, bool h24)
        {
            if (!Items.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            FxBinView view = new FxBinView();
            List<JsonValue> rows = Shown();
            view.Sub = Fx.Plural(Items.Count, "item") + " · " + SizeOf(Total()) + (Scope == "vehicle" && v != null ? " · " + v.Name : EveryVehicle);
            view.CanEmpty = Items.Count > 0;
            view.CanRestore = Sel.Count > 0 || RestoreLeftOn;
            view.WithVehicle = Scope == "all";
            bool all = rows.Count > 0;
            foreach (JsonValue i in rows) if (!Sel.Exists(id => IdIs(i, id))) all = false;
            view.AllChecked = all;
            foreach (JsonValue i in rows)
            {
                FxBinRow r = new FxBinRow();
                r.Id = Hz.Js(i["id"]);
                r.Label = Hz.Js(i["label"]);
                r.Sel = Sel.Exists(id => IdIs(i, id));
                bool clash = false;
                foreach (JsonValue f in i["files"].Items) if (f["conflict"].Bool) clash = true;
                r.Files = Fx.Plural(FileCount(i), "file") + (clash ? ConflictLine : "");
                if (view.WithVehicle) r.Vehicle = NameOf(i, names);
                r.Size = Fx.Bytes(i["bytes"]);
                r.Moved = Fx.Isoish(i["trashedAt"], h24);
                r.ByPolicy = i["reason"].IsString && i["reason"].Str == "policy";
                view.Rows.Add(r);
            }
            if (view.Rows.Count == 0) view.Empty = Items.Count > 0 ? NoMatch : IsEmpty;
            return view;
        }

        /// <summary>state.vehicles.find((x) =&gt; x.id === id)?.name || id.</summary>
        static string NameOf(JsonValue item, IList<HzCar> names)
        {
            JsonValue id = item["vehicleId"];
            if (names != null && id != null && id.IsString)
                foreach (HzCar c in names) if (c.Id == id.Str && !string.IsNullOrEmpty(c.Name)) return c.Name;
            return Hz.Js(id);
        }

        /// <summary>
        /// The page as test/windowsNativeFluxBin.test.js reads the web's:
        /// [head] Recycle Bin, the sub line, [btn danger( disabled)] Empty…,
        /// [note green], [seg bscope] with [opt( on)], [input …], [btn(
        /// disabled)] Restore selected, [table] [check all( on)] and the
        /// columns, then per row [tr( sel)] [check( on)] label, files,
        /// (vehicle,) size, moved, [chip( violet)], [btn sm] Restore [btn sm
        /// danger] Delete forever; or [tr] [empty] and its text.
        /// </summary>
        public List<string> Outline(FxBinView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head]"); t(Title); t(v.Sub);
            o.Add(v.CanEmpty ? "[btn danger]" : "[btn danger disabled]"); t(EmptyLabel);
            o.Add("[note green]"); t(Note);
            o.Add("[seg bscope]");
            foreach (string[] s in Scopes) { o.Add(s[0] == Scope ? "[opt on]" : "[opt]"); t(s[1]); }
            o.Add("[input " + Typed + "]");
            o.Add(v.CanRestore ? "[btn]" : "[btn disabled]"); t(RestoreSelected);
            o.Add("[table]");
            o.Add(v.AllChecked ? "[check all on]" : "[check all]");
            t(Columns[0]);
            if (v.WithVehicle) t(VehicleColumn);
            for (int i = 1; i < Columns.Length; i++) t(Columns[i]);
            if (v.Empty != null) { o.Add("[tr]"); o.Add("[empty]"); t(v.Empty); return o; }
            foreach (FxBinRow r in v.Rows)
            {
                o.Add(r.Sel ? "[tr sel]" : "[tr]");
                o.Add(r.Sel ? "[check on]" : "[check]");
                t(r.Label); t(r.Files);
                if (v.WithVehicle) t(r.Vehicle);
                t(r.Size); t(r.Moved);
                o.Add(r.ByPolicy ? "[chip violet]" : "[chip]"); t(r.ByPolicy ? ByPolicy : ByYou);
                o.Add("[btn sm]"); t(RestoreLabel);
                o.Add("[btn sm danger]"); t(DeleteLabel);
            }
            return o;
        }

        // ---------------------------------------------------------------- ticks

        /// <summary>A row's box ticked or unticked.</summary>
        public void Tick(string id, bool on)
        {
            RestoreLeftOn = false;
            if (on) { if (!Sel.Contains(id)) Sel.Add(id); }
            else Sel.Remove(id);
        }

        /// <summary>The header's Select all: every item (not only the rows the filter shows) ticked or unticked.</summary>
        public void TickAll(bool on)
        {
            foreach (JsonValue i in Items.Items) Tick(Hz.Js(i["id"]), on);
            RestoreLeftOn = false;
        }

        // ---------------------------------------------------------------- the buttons

        static string ItemPath(string id, string what)
        {
            return "/api/bin/" + Hz.EncId(id) + "/" + what;
        }

        /// <summary>loadVehicles().catch(() =&gt; {}): the shell's vehicles again (never throws).</summary>
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
                ui.Toast(bad ? "Restored " + done + "; " + Words(clash) + NotRestored : "Restored " + Fx.Plural(done, "item") + ".", bad ? "bad" : "ok");
            }
            foreach (string id in ids) Sel.Remove(id);
            await Quiet(reloadCars);
            await load();
        }

        /// <summary>A row's Restore: busy(its button, restore([id])) (a failure of the list's reading is a red toast).</summary>
        public Task RestoreOne(IJsonApi api, HzUi ui, string id, Func<Task> reloadCars, Func<Task> load)
        {
            return ui.Act(() => Restore(api, ui, new List<string> { id }, reloadCars, load));
        }

        /// <summary>Restore selected: busy(the button, restore([...sel])); the button is enabled again after.</summary>
        public async Task RestoreTicked(IJsonApi api, HzUi ui, Func<Task> reloadCars, Func<Task> load)
        {
            await ui.Act(() => Restore(api, ui, new List<string>(Sel), reloadCars, load));
            RestoreLeftOn = true;
        }

        /// <summary>The command palette's "Restore every item shown": restore(items.map((i) =&gt; i.id)).</summary>
        public Task RestoreEvery(IJsonApi api, HzUi ui, Func<Task> reloadCars, Func<Task> load)
        {
            List<string> ids = new List<string>();
            foreach (JsonValue i in Items.Items) ids.Add(Hz.Js(i["id"]));
            return Restore(api, ui, ids, reloadCars, load);
        }

        // conflicts += out.conflicts || 0, as JavaScript adds
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

        static string Words(object v)
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
            a.Para("", new Rich().Bold(Hz.Js(item["label"])).Text(" (" + Fx.Bytes(item["bytes"]) + ")" + DeleteRest));
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
            a.Para("", new Rich().Text(Fx.Plural(Items.Count, "item") + " (").Bold(SizeOf(Total())).Text(") of " + (v != null ? v.Name : EveryVehicleName) + EmptyRest));
            a.Yes = EmptyYes;
            a.Danger = true;
            return a;
        }

        /// <summary>
        /// Empty…: the vehicle shown for "This vehicle", none for "Every
        /// vehicle"; "Empty the Recycle Bin?" (no: nothing), POST
        /// /api/bin/empty { confirm, vehicle }, "Deleted N items (SIZE)." (red
        /// when some failed; a failure a red toast), the vehicles and the list
        /// again. busy is busy()'s disabled button.
        /// </summary>
        public async Task Empty(IJsonApi api, HzUi ui, HzCar shown, Action<bool> busy, Func<Task> reloadCars, Func<Task> load)
        {
            HzCar v = Scope == "vehicle" ? shown : null;
            if (!await ui.Ask(EmptyAsk(v))) return;
            busy(true);
            await ui.Act(async () =>
            {
                JsonValue o = await api.Post("/api/bin/empty", Bin.EmptyBody(v != null ? v.Id : null), CancellationToken.None);
                ui.Toast("Deleted " + Fx.Plural(o["deleted"], "item") + " (" + Fx.Bytes(o["bytes"]) + ").", o["failed"].Bool ? "bad" : "ok");
            });
            busy(false);
            await Quiet(reloadCars);
            await load();
        }
    }
}
