using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>One row of the Recycle Bin table, as web/app.js pages.bin writes it.</summary>
    public sealed class BinRow
    {
        /// <summary>The item's id (data-item): the restore and delete paths.</summary>
        public string Id;
        /// <summary>Recording: the label (bold).</summary>
        public string Label;
        /// <summary>Under it (muted, small): "N files[ · some files could not be restored (name taken)]".</summary>
        public string Files;
        /// <summary>Size (mono, small): bytes(i.bytes).</summary>
        public string Size;
        /// <summary>Moved (small): when(trashedAt).</summary>
        public string Moved;
        /// <summary>Why (small): "Policy" or "You".</summary>
        public string Why;
    }

    /// <summary>The Recycle Bin page as web/app.js pages.bin works it out from GET /api/bin.</summary>
    public sealed class BinView
    {
        public List<BinRow> Rows = new List<BinRow>();
        /// <summary>The items' bytes added up (items.reduce((n, i) =&gt; n + i.bytes, 0)).</summary>
        public double Total;
        /// <summary>"N items · SIZE" above the table.</summary>
        public string Summary = "";
        /// <summary>Empty Recycle Bin is in the head: some item is there.</summary>
        public bool CanEmpty { get { return Rows.Count > 0; } }
    }

    /// <summary>"Delete forever?" and "Empty the Recycle Bin?": the title, "&lt;b&gt;bold&lt;/b&gt; rest", OK (always danger).</summary>
    public sealed class BinConfirm
    {
        public string Title, Bold, Rest, Ok;
        public bool Danger = true;

        public Rich Text { get { return new Rich().Bold(Bold ?? "").Text(Rest ?? ""); } }

        /// <summary>The body's text as a reader sees it (the bold label, then the rest).</summary>
        public List<string> Body
        {
            get
            {
                List<string> l = new List<string>();
                if (!string.IsNullOrEmpty((Bold ?? "").Trim())) l.Add(Bold.Trim());
                if (!string.IsNullOrEmpty((Rest ?? "").Trim())) l.Add(Rest.Trim());
                return l;
            }
        }
    }

    /// <summary>What the Recycle Bin's buttons need from the window.</summary>
    public sealed class BinUi
    {
        /// <summary>toast(message, bad).</summary>
        public Action<string, bool> Toast = delegate { };
        /// <summary>confirmDialog(): true when the reader confirms.</summary>
        public Func<BinConfirm, Task<bool>> Confirm = c => Task.FromResult(false);
        /// <summary>loadVehicles().catch(() =&gt; {}): the shell reads GET /api/vehicles again (the Recycle Bin counts).</summary>
        public Func<Task> ReloadVehicles = () => Task.FromResult(0);
        /// <summary>render(): the page draws again.</summary>
        public Action Render = delegate { };
    }

    /// <summary>
    /// web/app.js pages.bin: what the vehicle shown (or every vehicle) has in
    /// the Recycle Bin, with Restore (back where it was; a file whose name is
    /// taken stays), Delete forever (one item) and Empty Recycle Bin (all of
    /// them). The server asks for the password again for the last two (the
    /// Api's CONFIRM_PASSWORD). After each, the vehicles are read again (the
    /// Recycle Bin counts) and the page is drawn again. The words, requests
    /// and toasts are the web's.
    /// </summary>
    public static class Bin
    {
        public const string Eyebrow = "Your archive workspace";
        public const string PageTitle = "Recycle Bin";
        public const string EmptyLabel = "Empty Recycle Bin";
        public const string Note = "Nothing in the Recycle Bin is deleted automatically. Restore puts recordings back exactly where they were; only \"Delete forever\" and \"Empty Recycle Bin\" free the space.";
        public const string IsEmpty = "The Recycle Bin is empty.";
        /// <summary>The table's columns; the last (the buttons) has no title.</summary>
        public static readonly string[] Columns = { "Recording", "Size", "Moved", "Why", "" };
        public const string Conflict = " · some files could not be restored (name taken)";
        public const string ByPolicy = "Policy";
        public const string ByYou = "You";
        public const string RestoreLabel = "Restore";
        public const string DeleteLabel = "Delete forever";
        public const string Restored = "Restored.";
        public const string RestoredSome = " could not be (a file with that name exists).";
        public const string DeleteTitle = "Delete forever?";
        public const string DeleteRest = " will be permanently deleted from the share. This cannot be undone.";
        public const string Deleted = "Deleted.";
        public const string EmptyTitle = "Empty the Recycle Bin?";
        public const string EmptyRest = " will be permanently deleted. This cannot be undone.";
        public const string AllVehicles = "all vehicles";
        public const string Item = "item";
        public const string File = "file";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>String(x ?? '') (esc() of a value).</summary>
        static string Js(JsonValue v)
        {
            if (v == null || v.IsNull) return "";
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.IsString) return v.Str;
            if (v.IsBool) return v.Bool ? "true" : "false";
            if (v.IsArray)
            {
                List<string> parts = new List<string>();
                foreach (JsonValue x in v.Items) parts.Add(Js(x));
                return string.Join(",", parts);
            }
            return "[object Object]";
        }

        /// <summary>${x} of owner[key] in a template: "undefined" when missing, "null" for null.</summary>
        static string Tpl(JsonValue owner, string key)
        {
            if (!owner.Has(key)) return "undefined";
            JsonValue v = owner[key];
            return v.IsNull ? "null" : Js(v);
        }

        /// <summary>Number(owner[key]) as + and &gt; see it: undefined NaN, null 0, a string its number (NaN when it is none).</summary>
        static double Number(JsonValue owner, string key)
        {
            if (!owner.Has(key)) return double.NaN;
            JsonValue v = owner[key];
            if (v.IsNull) return 0;
            if (v.IsNumber || v.IsBool) return v.Num;
            if (v.IsString)
            {
                string s = v.Str.Trim();
                if (s.Length == 0) return 0;
                double d;
                return double.TryParse(s, NumberStyles.Float, Inv, out d) ? d : double.NaN;
            }
            return double.NaN;
        }

        /// <summary>plural(owner[key], w): Number(n || 0), "s" unless n === 1.</summary>
        static string PluralOf(JsonValue owner, string key, string word)
        {
            JsonValue v = owner[key];
            double n = v.Bool ? Number(owner, key) : 0;
            bool one = v.IsNumber && v.Num == 1;
            return Format.Locale(Format.Fin(n) ? n : 0) + " " + word + (one ? "" : "s");
        }

        // ---------------------------------------------------------------- the page

        /// <summary>/api/bin, with ?vehicle=&lt;id&gt; (encoded) for the vehicle shown; null for none.</summary>
        public static string Path(string vehicleId)
        {
            return "/api/bin" + (vehicleId != null ? "?vehicle=" + ApiCalls.Enc(vehicleId) : "");
        }

        /// <summary>GET the items; a failure throws (the whole page is the error).</summary>
        public static Task<JsonValue> Load(IJsonApi api, string vehicleId, CancellationToken ct)
        {
            return api.Get(Path(vehicleId), ct);
        }

        /// <summary>
        /// The page from the answer. An items list that is not a list, or an
        /// item without a files list, throws, as the web's items.reduce and
        /// i.files.length do.
        /// </summary>
        public static BinView View(JsonValue answer, bool h24)
        {
            JsonValue items = answer["items"];
            if (!items.IsArray) throw new InvalidOperationException("Cannot read properties of " + (answer.Has("items") && items.IsNull ? "null" : "undefined") + " (reading 'reduce')");
            BinView v = new BinView();
            double total = 0;
            foreach (JsonValue i in items.Items) total += Number(i, "bytes");
            v.Total = total;
            foreach (JsonValue i in items.Items)
            {
                JsonValue files = i["files"];
                if (!files.IsArray) throw new InvalidOperationException("Cannot read properties of " + (i.Has("files") && files.IsNull ? "null" : "undefined") + " (reading 'length')");
                bool conflict = false;
                foreach (JsonValue f in files.Items) if (f["conflict"].Bool) conflict = true;
                BinRow r = new BinRow();
                r.Id = Js(i["id"]);
                r.Label = Js(i["label"]);
                r.Files = Format.Plural(files.Count, File) + (conflict ? Conflict : "");
                r.Size = Format.Bytes(i["bytes"]);
                r.Moved = Format.When(i["trashedAt"], h24);
                r.Why = i["reason"].IsString && i["reason"].Str == "policy" ? ByPolicy : ByYou;
                v.Rows.Add(r);
            }
            v.Summary = Format.Plural(v.Rows.Count, Item) + " · " + Format.Bytes(total);
            return v;
        }

        /// <summary>
        /// The page as a reader sees it, in the markers test/windowsNativeBin.test.js
        /// writes for the web's HTML: [head eyebrow | title] [btn( classes)] [pills]
        /// [note green] [card] [p( classes)] [table] [tr] [th] [td( classes)] [b] [muted small].
        /// </summary>
        public static List<string> Outline(BinView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = (s ?? "").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head " + Eyebrow + " | " + PageTitle + "]");
            if (v.CanEmpty) { o.Add("[btn danger]"); t(EmptyLabel); }
            o.Add("[pills]");
            o.Add("[note green]"); t(Note);
            o.Add("[card]");
            if (v.Rows.Count == 0)
            {
                o.Add("[p]"); t(IsEmpty);
                return o;
            }
            o.Add("[p muted]"); t(v.Summary);
            o.Add("[table]");
            o.Add("[tr]");
            foreach (string c in Columns) { o.Add("[th]"); t(c); }
            foreach (BinRow r in v.Rows)
            {
                o.Add("[tr]");
                o.Add("[td]"); o.Add("[b]"); t(r.Label); o.Add("[muted small]"); t(r.Files);
                o.Add("[td mono small]"); t(r.Size);
                o.Add("[td small]"); t(r.Moved);
                o.Add("[td small]"); t(r.Why);
                o.Add("[td actions]");
                o.Add("[btn sm]"); t(RestoreLabel);
                o.Add("[btn sm danger]"); t(DeleteLabel);
            }
            return o;
        }

        // ---------------------------------------------------------------- the buttons

        static string ItemPath(string id, string what)
        {
            return "/api/bin/" + ApiCalls.Enc(id) + "/" + what;
        }

        /// <summary>The toast after a restore: "Restored." or "Restored R file(s); C could not be …" (bad when conflicts &gt; 0).</summary>
        public static KeyValuePair<string, bool> RestoreToast(JsonValue answer)
        {
            bool bad = Number(answer, "conflicts") > 0;
            if (!answer["conflicts"].Bool) return new KeyValuePair<string, bool>(Restored, bad);
            return new KeyValuePair<string, bool>("Restored " + Tpl(answer, "restored") + " file(s); " + Tpl(answer, "conflicts") + RestoredSome, bad);
        }

        /// <summary>Restore: POST /api/bin/:id/restore {}, the toast, the vehicles again, the page again (also after a failure).</summary>
        public static async Task Restore(IJsonApi api, BinUi ui, string id)
        {
            try
            {
                JsonValue o = await api.Post(ItemPath(id, "restore"), JsonValue.Object(), CancellationToken.None);
                KeyValuePair<string, bool> t = RestoreToast(o);
                ui.Toast(t.Key, t.Value);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
            await Reload(ui);
            ui.Render();
        }

        /// <summary>"Delete forever?": "&lt;b&gt;label&lt;/b&gt; (SIZE) will be permanently deleted from the share. …".</summary>
        public static BinConfirm DeleteConfirm(BinRow row)
        {
            BinConfirm c = new BinConfirm();
            c.Title = DeleteTitle;
            c.Bold = row.Label;
            c.Rest = " (" + row.Size + ")" + DeleteRest;
            c.Ok = DeleteLabel;
            return c;
        }

        /// <summary>Delete forever: the confirm, POST /api/bin/:id/delete {confirm: true} (the password asked again by the Api), "Deleted.", the vehicles and the page again.</summary>
        public static async Task DeleteForever(IJsonApi api, BinUi ui, BinRow row)
        {
            if (!await ui.Confirm(DeleteConfirm(row))) return;
            try
            {
                await api.Post(ItemPath(row.Id, "delete"), JsonValue.Object("confirm", true), CancellationToken.None);
                ui.Toast(Deleted, false);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
            await Reload(ui);
            ui.Render();
        }

        /// <summary>"Empty the Recycle Bin?": "N items (SIZE) of &lt;vehicle | all vehicles&gt; will be permanently deleted. …".</summary>
        public static BinConfirm EmptyConfirm(BinView v, string vehicleName, bool hasVehicle)
        {
            BinConfirm c = new BinConfirm();
            c.Title = EmptyTitle;
            c.Bold = "";
            c.Rest = Format.Plural(v.Rows.Count, Item) + " (" + Format.Bytes(v.Total) + ") of " + (hasVehicle ? (vehicleName ?? "") : AllVehicles) + EmptyRest;
            c.Ok = EmptyLabel;
            return c;
        }

        /// <summary>{ confirm: true, vehicle: id or null }.</summary>
        public static JsonValue EmptyBody(string vehicleId)
        {
            return JsonValue.Object("confirm", true, "vehicle", string.IsNullOrEmpty(vehicleId) ? null : vehicleId);
        }

        /// <summary>The toast after emptying: "Deleted N items (SIZE)." (bad when failed &gt; 0).</summary>
        public static KeyValuePair<string, bool> EmptyToast(JsonValue answer)
        {
            return new KeyValuePair<string, bool>("Deleted " + PluralOf(answer, "deleted", Item) + " (" + Format.Bytes(answer["bytes"]) + ").", Number(answer, "failed") > 0);
        }

        /// <summary>
        /// Empty Recycle Bin: the confirm, POST /api/bin/empty (the password
        /// asked again by the Api), the toast, the vehicles and the page
        /// again. vehicleId is the vehicle shown (null for none).
        /// </summary>
        public static async Task Empty(IJsonApi api, BinUi ui, BinView v, string vehicleId, string vehicleName)
        {
            if (!await ui.Confirm(EmptyConfirm(v, vehicleName, vehicleId != null))) return;
            try
            {
                JsonValue o = await api.Post("/api/bin/empty", EmptyBody(vehicleId), CancellationToken.None);
                KeyValuePair<string, bool> t = EmptyToast(o);
                ui.Toast(t.Key, t.Value);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
            await Reload(ui);
            ui.Render();
        }

        static async Task Reload(BinUi ui)
        {
            try { await ui.ReloadVehicles(); } catch (Exception) { }
        }
    }
}
