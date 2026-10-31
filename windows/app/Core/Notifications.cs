using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>One item of the Notifications list, as web/app.js pages.notifications writes it.</summary>
    public sealed class NoteRow
    {
        /// <summary>The id as the web puts it in data-dismiss (${n.id}: "undefined" when missing): the dismiss path.</summary>
        public string Id;
        /// <summary>The dot: "bad" (critical), "ok" (info), "warn" (anything else), "" (resolved: muted).</summary>
        public string Dot;
        /// <summary>The title (bold, on its own line).</summary>
        public string Title;
        /// <summary>The message (text-2, 13px); "" without one.</summary>
        public string Message;
        /// <summary>Under it (muted, small): "when(createdAt)[ · resolved when(resolvedAt)]".</summary>
        public string When;
        /// <summary>Resolved: drawn at opacity .6, no Dismiss button.</summary>
        public bool Resolved;
    }

    /// <summary>The Notifications page as web/app.js pages.notifications works it out from GET /api/notifications.</summary>
    public sealed class NotesView
    {
        public List<NoteRow> Rows = new List<NoteRow>();
    }

    /// <summary>What Dismiss needs from the window.</summary>
    public sealed class NotesUi
    {
        /// <summary>loadNotes(): the shell reads GET /api/notifications?open=1 again (its red badge). When it fails the page is not drawn again, as the web's.</summary>
        public Func<Task> ReloadNotes = () => Task.FromResult(0);
        /// <summary>render(): the page draws again.</summary>
        public Action Render = delegate { };
    }

    /// <summary>
    /// web/app.js pages.notifications: every notification the server keeps
    /// (open and resolved), newest as the server orders them, each with its
    /// level's dot, title, message and times, and Dismiss on the open ones.
    /// Dismiss sends POST /api/notifications/:id/dismiss (a failure is
    /// ignored), the shell reads its open notifications again (the badge) and
    /// the page is drawn again. No vehicle pills. The words and requests are
    /// the web's.
    /// </summary>
    public static class Notifications
    {
        public const string Eyebrow = "Your archive workspace";
        public const string PageTitle = "Notifications";
        public const string None = "No notifications.";
        public const string DismissLabel = "Dismiss";
        public const string ResolvedText = " · resolved ";
        public const string Path = "/api/notifications";

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

        // ---------------------------------------------------------------- the page

        /// <summary>GET /api/notifications (all of them); a failure throws (the whole page is the error).</summary>
        public static Task<JsonValue> Load(IJsonApi api, CancellationToken ct)
        {
            return api.Get(Path, ct);
        }

        /// <summary>
        /// The page from the answer. A missing or null list throws as the
        /// web's notifications.length does; a list that is not a list (and has
        /// no length) is "No notifications.", a string with a length throws as
        /// notifications.map does.
        /// </summary>
        public static NotesView View(JsonValue answer, bool h24)
        {
            JsonValue list = answer["notifications"];
            if (!answer.Has("notifications") || list.IsNull)
                throw new InvalidOperationException("Cannot read properties of " + (answer.Has("notifications") ? "null" : "undefined") + " (reading 'length')");
            NotesView v = new NotesView();
            if (list.IsString && list.Str.Length > 0) throw new InvalidOperationException("notifications.map is not a function");
            if (!list.IsArray) return v;
            foreach (JsonValue n in list.Items)
            {
                NoteRow r = new NoteRow();
                r.Resolved = n["resolvedAt"].Bool;
                string level = n["level"].IsString ? n["level"].Str : null;
                r.Dot = r.Resolved ? "" : level == "critical" ? "bad" : level == "info" ? "ok" : "warn";
                r.Id = Tpl(n, "id");
                r.Title = Js(n["title"]);
                r.Message = n["message"].Bool ? Js(n["message"]) : "";
                r.When = Format.When(n["createdAt"], h24) + (r.Resolved ? ResolvedText + Format.When(n["resolvedAt"], h24) : "");
                v.Rows.Add(r);
            }
            return v;
        }

        /// <summary>
        /// The page as a reader sees it, in the markers
        /// test/windowsNativeNotifications.test.js writes for the web's HTML:
        /// [head eyebrow | title] [card] [p] [ul] [li( resolved)] [dot( kind)]
        /// [b] [muted small] [btn sm].
        /// </summary>
        public static List<string> Outline(NotesView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = (s ?? "").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head " + Eyebrow + " | " + PageTitle + "]");
            o.Add("[card]");
            if (v.Rows.Count == 0)
            {
                o.Add("[p]"); t(None);
                return o;
            }
            o.Add("[ul]");
            foreach (NoteRow r in v.Rows)
            {
                o.Add(r.Resolved ? "[li resolved]" : "[li]");
                o.Add(r.Dot.Length > 0 ? "[dot " + r.Dot + "]" : "[dot]");
                o.Add("[b]"); t(r.Title);
                t(r.Message);
                o.Add("[muted small]"); t(r.When);
                if (!r.Resolved) { o.Add("[btn sm]"); t(DismissLabel); }
            }
            return o;
        }

        // ---------------------------------------------------------------- Dismiss

        /// <summary>/api/notifications/&lt;id&gt;/dismiss: the id as the web writes it (not encoded).</summary>
        public static string DismissPath(string id)
        {
            return Path + "/" + id + "/dismiss";
        }

        /// <summary>
        /// Dismiss: POST /api/notifications/:id/dismiss {} (a failure is
        /// ignored), the shell's open notifications again (the badge), the
        /// page again. When reading the open notifications fails, the page is
        /// not drawn again (the web's await loadNotes() throws first).
        /// </summary>
        public static async Task Dismiss(IJsonApi api, NotesUi ui, string id)
        {
            try { await api.Post(DismissPath(id), JsonValue.Object(), CancellationToken.None); }
            catch (Exception) { }
            await ui.ReloadNotes();
            ui.Render();
        }
    }
}
