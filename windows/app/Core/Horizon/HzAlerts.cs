using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>One warning light of the Notifications list (hz/pages/alerts.js li.alert-i).</summary>
    public sealed class HzAlertRow
    {
        /// <summary>data-id: `${n.id}` ("undefined" when missing); its buttons send Number() of it.</summary>
        public string Id;
        /// <summary>The level as written (li's class, the first pill): "critical", "warning", "info", anything.</summary>
        public string Level;
        /// <summary>TONE[level]: red (critical), org (warning), cyan (info), "" otherwise.</summary>
        public string Tone;
        /// <summary>The lamp's sign: "info" for info, "warn" otherwise.</summary>
        public string Icon;
        public string Title, Message;
        /// <summary>The kind (tiny, faint, mono); "" for none.</summary>
        public string Kind;
        /// <summary>Not read yet: the magenta edge and the "new" pill, and Mark read.</summary>
        public bool Unread;
        /// <summary>Resolved: drawn at .75, no Dismiss.</summary>
        public bool Done;
        /// <summary>"Oct 31, 2026, 1:10:02 PM · 2 h ago[ · resolved 3 d ago]".</summary>
        public string When;
    }

    /// <summary>The page as hz/pages/alerts.js draw() works it out.</summary>
    public sealed class HzAlertsView
    {
        /// <summary>#a-sub: "2 open · 1 unread · 4 notifications kept".</summary>
        public string Sub;
        /// <summary>status(): "2 open · 1 unread".</summary>
        public string Status;
        /// <summary>Mark all read can be pressed (some open one is unread).</summary>
        public bool CanReadAll;
        public List<HzAlertRow> Rows = new List<HzAlertRow>();
        /// <summary>The blank's text when no row is shown; null otherwise.</summary>
        public string Empty;
    }

    /// <summary>
    /// Horizon's Notifications page (examples/ui-packs/horizon/web/hz/pages/alerts.js):
    /// the notifications as warning lights, Open / Unread / All with
    /// resolved, each one's Mark read and Dismiss, and Mark all read. The
    /// same GET /api/notifications (all of them, as the pack's loadNotes()
    /// reads them) and POST /api/notifications/:id/dismiss as the built-in
    /// page (Core/Notifications.cs: its Path and DismissPath), plus POST
    /// /api/notifications/read { ids }; after a change the list is read
    /// again (and the shell's own count, ShellModel.ReloadNotes). The words
    /// are the pack's. Checked by test/windowsNativeHorizonAlerts.test.js
    /// and Core.Tests HorizonAlertsTests.
    /// </summary>
    public sealed class HzAlerts
    {
        public const string Title = "Notifications";
        public const string ReadAll = "Mark all read";
        public const string NewPill = "new";
        public const string MarkRead = "Mark read";
        public const string Dismiss = "Dismiss";
        public const string Dismissed = "Dismissed.";
        public const string NoneOpen = "No open notifications. All lights are off.";
        public const string NothingHere = "Nothing here.";
        public const string ResolvedWord = " · resolved ";
        public const string ReadPath = "/api/notifications/read";

        /// <summary>The filter's buttons.</summary>
        public static readonly string[][] Shows = { new[] { "open", "Open" }, new[] { "unread", "Unread" }, new[] { "all", "All, with resolved" } };

        public string Show = "open";
        /// <summary>
        /// Mark all read ran: its act() enables the button again after the
        /// list was drawn with nothing unread, until the next draw.
        /// </summary>
        public bool ReadAllLeftOn;

        /// <summary>The filter picked (the page draws again).</summary>
        public void Pick(string show)
        {
            Show = show;
            ReadAllLeftOn = false;
        }
        /// <summary>app.notes: the last GET /api/notifications answer's list.</summary>
        public JsonValue Notes = JsonValue.Array(new JsonValue[0]);

        /// <summary>loadNotes(): GET /api/notifications (all of them); a failure throws and the list stays.</summary>
        public async Task Load(IJsonApi api, CancellationToken ct)
        {
            JsonValue r = await api.Get(Notifications.Path, ct);
            Notes = r["notifications"];
            ReadAllLeftOn = false;
        }

        /// <summary>TONE[level].</summary>
        public static string ToneOf(JsonValue level)
        {
            string s = level != null && level.IsString ? level.Str : null;
            switch (s)
            {
                case "critical": return "red";
                case "warning": return "org";
                case "info": return "cyan";
                default: return "";
            }
        }

        /// <summary>
        /// draw(): the rows the filter shows and the lines over them. A list
        /// that is not a list throws, as all.filter does.
        /// </summary>
        public HzAlertsView View(long now, bool h24)
        {
            if (!Notes.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            HzAlertsView v = new HzAlertsView();
            int unread = 0, open = 0;
            foreach (JsonValue n in Notes.Items)
            {
                bool done = n["resolvedAt"].Bool;
                if (!done) open++;
                if (!done && !n["readAt"].Bool) unread++;
                bool shown = Show == "all" || (Show == "unread" ? !n["readAt"].Bool && !done : !done);
                if (!shown) continue;
                HzAlertRow r = new HzAlertRow();
                r.Id = Hz.Tpl(n, "id");
                r.Level = Hz.Js(n["level"]);
                r.Tone = ToneOf(n["level"]);
                r.Icon = n["level"].IsString && n["level"].Str == "info" ? "info" : "warn";
                r.Title = Hz.Js(n["title"]);
                r.Message = Hz.Or(n["message"]);
                r.Kind = n["kind"].Bool ? Hz.Js(n["kind"]) : "";
                r.Unread = !n["readAt"].Bool;
                r.Done = done;
                JsonValue upd = n["updatedAt"].Bool ? n["updatedAt"] : n["createdAt"];
                r.When = Hz.StampFull(n["createdAt"], h24) + " · " + Hz.Since(upd, now) + (done ? ResolvedWord + Hz.Since(n["resolvedAt"], now) : "");
                v.Rows.Add(r);
            }
            v.Sub = open + " open · " + unread + " unread · " + Hz.Count(Notes.Count, "notification") + " kept";
            v.Status = open + " open · " + unread + " unread";
            v.CanReadAll = unread > 0 || ReadAllLeftOn;
            if (v.Rows.Count == 0) v.Empty = Show == "open" ? NoneOpen : NothingHere;
            return v;
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonAlerts.test.js reads the web's:
        /// [title] Notifications, the sub line, [btn( disabled)] Mark all read,
        /// [segs as] with [seg( on)], [list], then per light [alert level(
        /// unread)( done)] title [pill tone] level ([pill mag] new) ([kind]
        /// kind) message when, ([btn sm read] Mark read) ([btn sm dismiss]
        /// Dismiss); or [blank] and its text.
        /// </summary>
        public List<string> Outline(HzAlertsView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[title]"); t(Title); t(v.Sub);
            o.Add(v.CanReadAll ? "[btn]" : "[btn disabled]"); t(ReadAll);
            o.Add("[segs as]");
            foreach (string[] s in Shows) { o.Add(s[0] == Show ? "[seg on]" : "[seg]"); t(s[1]); }
            o.Add("[list]");
            if (v.Empty != null) { o.Add("[blank]"); t(v.Empty); return o; }
            foreach (HzAlertRow r in v.Rows)
            {
                string cls = System.Text.RegularExpressions.Regex.Replace((r.Level + (r.Unread ? " unread" : "") + (r.Done ? " done" : "")).Trim(), @"\s+", " ");
                o.Add("[alert" + (cls.Length > 0 ? " " + cls : "") + "]");
                t(r.Title);
                o.Add("[pill" + (r.Tone.Length > 0 ? " " + r.Tone : "") + "]"); t(r.Level);
                if (r.Unread) { o.Add("[pill mag]"); t(NewPill); }
                if (r.Kind.Length > 0) { o.Add("[kind]"); t(r.Kind); }
                t(r.Message);
                t(r.When);
                if (r.Unread) { o.Add("[btn sm read]"); t(MarkRead); }
                if (!r.Done) { o.Add("[btn sm dismiss]"); t(Dismiss); }
            }
            return o;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>Number(li.dataset.id): the id the buttons send (NaN for one that is not a number).</summary>
        public static double IdNumber(string dataId)
        {
            return Hz.Number(JsonValue.String(dataId ?? ""));
        }

        static object IdJson(double id)
        {
            // (JSON.stringify writes NaN as null)
            return Format.Fin(id) ? (object)JsonValue.Number(id) : null;
        }

        /// <summary>{ ids: [...] }.</summary>
        public static JsonValue ReadBody(IEnumerable<double> ids)
        {
            List<JsonValue> l = new List<JsonValue>();
            foreach (double id in ids) l.Add(JsonValue.From(IdJson(id)));
            return JsonValue.Object("ids", JsonValue.Array(l));
        }

        /// <summary>
        /// A light's Mark read: POST /api/notifications/read { ids: [id] }
        /// (a failure a red toast), the list read again (a failure ignored).
        /// </summary>
        public async Task MarkOneRead(IJsonApi api, HzUi ui, string dataId, Func<Task> reload)
        {
            double id = IdNumber(dataId);
            await ui.Act(() => api.Post(ReadPath, ReadBody(new[] { id }), CancellationToken.None));
            await Quiet(reload);
        }

        /// <summary>A light's Dismiss: POST /api/notifications/:id/dismiss {}, "Dismissed." (a failure a red toast), the list again.</summary>
        public async Task DismissOne(IJsonApi api, HzUi ui, string dataId, Func<Task> reload)
        {
            double id = IdNumber(dataId);
            await ui.Act(async () =>
            {
                await api.Post(Notifications.DismissPath(Format.JsNum(id)), JsonValue.Object(), CancellationToken.None);
                ui.Toast(Dismissed, "ok");
            });
            await Quiet(reload);
        }

        /// <summary>
        /// Mark all read: every one not read (resolved ones too) in one POST,
        /// the list again (its failure a red toast, as the POST's), then
        /// "Marked N notifications read.".
        /// </summary>
        public async Task MarkAllRead(IJsonApi api, HzUi ui, Func<Task> reload)
        {
            await ui.Act(async () =>
            {
                // (the ids as the answer has them; a missing one is null in JSON)
                List<JsonValue> ids = new List<JsonValue>();
                foreach (JsonValue n in Notes.Items) if (!n["readAt"].Bool) ids.Add(Hz.Missing(n, "id") ? JsonValue.Null : n["id"]);
                await api.Post(ReadPath, JsonValue.Object("ids", JsonValue.Array(ids)), CancellationToken.None);
                await reload();
                ui.Toast("Marked " + Hz.Count(ids.Count, "notification") + " read.", "ok");
            });
            ReadAllLeftOn = true;
        }

        static async Task Quiet(Func<Task> reload)
        {
            try { await reload(); } catch (Exception) { }
        }
    }
}
