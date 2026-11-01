using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>One item of Flux's Notifications list (flux/pages/notifications.js li.note-item).</summary>
    public sealed class FxNotificationsRow
    {
        /// <summary>data-id: `${n.id}` ("undefined" when missing); its buttons send Number() of it.</summary>
        public string Id;
        /// <summary>The level as written (li's class, the chip): "critical", "warning", "info", anything.</summary>
        public string Level;
        /// <summary>TONE[level]: red (critical), amber (warning), cyan (info), "" otherwise.</summary>
        public string Tone;
        public string Title, Message;
        /// <summary>The kind (faint, small, mono); "" for none.</summary>
        public string Kind;
        /// <summary>Not read yet: the violet edge, the dot and Mark read.</summary>
        public bool Unread;
        /// <summary>Resolved: drawn at .55, no Dismiss.</summary>
        public bool Resolved;
        /// <summary>"2026-10-31 1:10:02 PM · 2h ago[ · resolved 3d ago]".</summary>
        public string When;
    }

    /// <summary>The page as flux/pages/notifications.js draw() works it out.</summary>
    public sealed class FxNotificationsView
    {
        /// <summary>#nt-sub: "2 open · 1 unread · 4 kept".</summary>
        public string Sub;
        /// <summary>Mark all read can be pressed (some open one is unread).</summary>
        public bool CanReadAll;
        public List<FxNotificationsRow> Rows = new List<FxNotificationsRow>();
        /// <summary>The empty box's text when no item is shown; null otherwise.</summary>
        public string Empty;
    }

    /// <summary>
    /// Flux's Notifications page (examples/ui-packs/flux/web/flux/pages/notifications.js):
    /// the list with Open / Unread / All, incl. resolved, each one's Mark read
    /// and Dismiss, and Mark all read. Its requests and handlers are
    /// literally Horizon's (hz/pages/alerts.js): GET /api/notifications (the
    /// shell's loadNotes()), POST /api/notifications/read { ids } and POST
    /// /api/notifications/:id/dismiss, so the actions are HzAlerts'; the
    /// words and layout are Flux's. Checked by
    /// test/windowsNativeFluxNotifications.test.js and Core.Tests FluxNotificationsTests.
    /// </summary>
    public sealed class FxNotifications
    {
        public const string Title = "Notifications";
        public const string ReadAll = "Mark all read";
        public const string MarkRead = "Mark read";
        public const string Dismiss = "Dismiss";
        public const string NoneOpen = "No open notifications. All clear.";
        public const string NothingHere = "Nothing here.";
        public const string ResolvedWord = " · resolved ";
        public const string UnreadTitle = "unread";

        /// <summary>The filter's buttons.</summary>
        public static readonly string[][] Shows = { new[] { "open", "Open" }, new[] { "unread", "Unread" }, new[] { "all", "All, incl. resolved" } };

        /// <summary>The list, its loading and the buttons' requests (the same code as Horizon's page).</summary>
        public readonly HzAlerts Data = new HzAlerts();

        public string Show = "open";

        /// <summary>The filter picked (the page draws again).</summary>
        public void Pick(string show)
        {
            Show = show;
            Data.Pick(show);
        }

        /// <summary>state.notes.</summary>
        public JsonValue Notes { get { return Data.Notes; } set { Data.Notes = value; } }

        /// <summary>loadNotes(): GET /api/notifications; a failure throws and the list stays.</summary>
        public Task Load(IJsonApi api, CancellationToken ct)
        {
            return Data.Load(api, ct);
        }

        /// <summary>TONE[level].</summary>
        public static string ToneOf(JsonValue level)
        {
            string s = level != null && level.IsString ? level.Str : null;
            switch (s)
            {
                case "critical": return "red";
                case "warning": return "amber";
                case "info": return "cyan";
                default: return "";
            }
        }

        /// <summary>draw(): the items the filter shows and the line over them. A list that is not a list throws, as all.filter does.</summary>
        public FxNotificationsView View(long now, bool h24)
        {
            JsonValue all = Notes;
            if (!all.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            FxNotificationsView v = new FxNotificationsView();
            int unread = 0, open = 0;
            foreach (JsonValue n in all.Items)
            {
                bool done = n["resolvedAt"].Bool;
                if (!done) open++;
                if (!done && !n["readAt"].Bool) unread++;
                bool shown = Show == "all" || (Show == "unread" ? !n["readAt"].Bool && !done : !done);
                if (!shown) continue;
                FxNotificationsRow r = new FxNotificationsRow();
                r.Id = Hz.Tpl(n, "id");
                r.Level = Hz.Js(n["level"]);
                r.Tone = ToneOf(n["level"]);
                r.Title = Hz.Js(n["title"]);
                r.Message = Hz.Or(n["message"]);
                r.Kind = n["kind"].Bool ? Hz.Js(n["kind"]) : "";
                r.Unread = !n["readAt"].Bool;
                r.Resolved = done;
                JsonValue upd = n["updatedAt"].Bool ? n["updatedAt"] : n["createdAt"];
                r.When = Fx.Isoish(n["createdAt"], h24) + " · " + Fx.Ago(upd, now) + (done ? ResolvedWord + Fx.Ago(n["resolvedAt"], now) : "");
                v.Rows.Add(r);
            }
            v.Sub = open + " open · " + unread + " unread · " + all.Count + " kept";
            v.CanReadAll = unread > 0 || Data.ReadAllLeftOn;
            if (v.Rows.Count == 0) v.Empty = Show == "open" ? NoneOpen : NothingHere;
            return v;
        }

        /// <summary>
        /// The page as test/windowsNativeFluxNotifications.test.js reads the
        /// web's: [head] Notifications, the sub line, [btn( disabled)] Mark
        /// all read, [seg nshow] with [opt( on)], [list], then per item [note
        /// level( unread)( resolved)] title [chip tone] level ([kind] kind)
        /// ([dot]) message when, ([btn sm read] Mark read) ([btn sm dismiss]
        /// Dismiss); or [empty] and its text.
        /// </summary>
        public List<string> Outline(FxNotificationsView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head]"); t(Title); t(v.Sub);
            o.Add(v.CanReadAll ? "[btn]" : "[btn disabled]"); t(ReadAll);
            o.Add("[seg nshow]");
            foreach (string[] s in Shows) { o.Add(s[0] == Show ? "[opt on]" : "[opt]"); t(s[1]); }
            o.Add("[list]");
            if (v.Empty != null) { o.Add("[empty]"); t(v.Empty); return o; }
            foreach (FxNotificationsRow r in v.Rows)
            {
                string cls = System.Text.RegularExpressions.Regex.Replace((r.Level + (r.Unread ? " unread" : "") + (r.Resolved ? " resolved" : "")).Trim(), @"\s+", " ");
                o.Add("[note" + (cls.Length > 0 ? " " + cls : "") + "]");
                t(r.Title);
                o.Add("[chip" + (r.Tone.Length > 0 ? " " + r.Tone : "") + "]"); t(r.Level);
                if (r.Kind.Length > 0) { o.Add("[kind]"); t(r.Kind); }
                if (r.Unread) o.Add("[dot]");
                t(r.Message);
                t(r.When);
                if (r.Unread) { o.Add("[btn sm read]"); t(MarkRead); }
                if (!r.Resolved) { o.Add("[btn sm dismiss]"); t(Dismiss); }
            }
            return o;
        }

        // ---------------------------------------------------------------- the buttons (Horizon's handlers, the same code)

        /// <summary>An item's Mark read: POST /api/notifications/read { ids: [id] } (a failure a red toast), the list again (a failure ignored).</summary>
        public Task MarkOneRead(IJsonApi api, HzUi ui, string dataId, Func<Task> reload)
        {
            return Data.MarkOneRead(api, ui, dataId, reload);
        }

        /// <summary>An item's Dismiss: POST /api/notifications/:id/dismiss {}, "Dismissed." (a failure a red toast), the list again.</summary>
        public Task DismissOne(IJsonApi api, HzUi ui, string dataId, Func<Task> reload)
        {
            return Data.DismissOne(api, ui, dataId, reload);
        }

        /// <summary>Mark all read: every one not read (resolved ones too) in one POST, the list again, then "Marked N notifications read.".</summary>
        public Task MarkAllRead(IJsonApi api, HzUi ui, Func<Task> reload)
        {
            return Data.MarkAllRead(api, ui, reload);
        }
    }
}
