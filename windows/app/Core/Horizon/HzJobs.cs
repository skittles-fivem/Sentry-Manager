using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>The vehicle shown (core.js car()): its id and name; null for none.</summary>
    public sealed class HzCar
    {
        public string Id, Name;

        public HzCar(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>One entry of the pit log (hz/pages/jobs.js li.pit).</summary>
    public sealed class HzJobRow
    {
        /// <summary>The lamp and pill colour: grn (done), red (failed), cyan (running), org (interrupted), "" (cancelled, anything else).</summary>
        public string Lamp;
        public string Title, Detail;
        /// <summary>"scan · #30".</summary>
        public string Meta;
        public string Status;
        /// <summary>A spinner in the pill (running).</summary>
        public bool Spin;
        /// <summary>stampFull(startedAt).</summary>
        public string Started;
        /// <summary>"took 42s".</summary>
        public string Took;
        /// <summary>"413 Wh · 8.0 W · $0.14"; null without energy.wh.</summary>
        public string Energy;
    }

    /// <summary>The Jobs page as hz/pages/jobs.js draw() works it out.</summary>
    public sealed class HzJobsView
    {
        /// <summary>#j-sub: "4 jobs · 1 running · Model 3 and jobs of no vehicle".</summary>
        public string Sub;
        /// <summary>status(): "1 running · 4 jobs".</summary>
        public string Status;
        public List<HzJobRow> Rows = new List<HzJobRow>();
        /// <summary>The blank's text when no row is shown ("No job matches." / "No jobs yet. …"); null otherwise.</summary>
        public string Empty;
        /// <summary>Clear finished… can be pressed (some job is not running).</summary>
        public bool CanClear;
        /// <summary>Some job is running: the page draws again every second.</summary>
        public bool Ticking;
    }

    /// <summary>
    /// Horizon's Jobs page (examples/ui-packs/horizon/web/hz/pages/jobs.js):
    /// "the pit log" of scans, conversions, Recycle Bin moves and policy runs
    /// with a lamp each, the status and vehicle filters, the text filter, and
    /// Clear finished… (the server's preview, "Clear finished jobs?", the
    /// clear, the toasts). The same GET /api/jobs and POST /api/jobs/clear as
    /// the built-in page (Core/Jobs.cs); the words and the filtering are the
    /// pack's own. Checked by test/windowsNativeHorizonJobs.test.js and
    /// Core.Tests HorizonJobsTests.
    /// </summary>
    public sealed class HzJobs
    {
        public const string Title = "Jobs";
        public const string Lead = "The pit log: scans, conversions, Recycle Bin moves and policy runs.";
        public const string ClearLabel = "Clear finished…";
        public const string FilterPlaceholder = "Filter jobs";
        public const string NoMatch = "No job matches.";
        public const string NoJobs = "No jobs yet. Scans, conversions, Recycle Bin moves and policy runs show up here.";
        public const string NothingToClear = "Nothing to clear: running, waiting and paused jobs stay.";
        public const string ConfirmTitle = "Clear finished jobs?";
        public const string ConfirmWhich = " (done, failed, cancelled or interrupted)";
        public const string ConfirmStay = " leave the list. Running, waiting and paused ones stay.";
        public const string ConfirmUntouched = "Only the list changes: footage, converted files and what a paused conversion needs are untouched.";
        public const string ConfirmYes = "Clear jobs";
        public const string OfNoVehicle = " and jobs of no vehicle";
        public const string EveryVehicle = " · every vehicle";
        public const string ClearPath = "/api/jobs/clear";

        /// <summary>The status filter's buttons: value, label.</summary>
        public static readonly string[][] Shows = { new[] { "all", "All" }, new[] { "running", "Running" }, new[] { "done", "Done" }, new[] { "failed", "Failed" }, new[] { "other", "Stopped" } };
        /// <summary>The vehicle filter's buttons.</summary>
        public static readonly string[][] Scopes = { new[] { "car", "This vehicle" }, new[] { "all", "Every vehicle" } };

        // the page's state (mount()'s let show, scope, q, list)
        public string Show = "all";
        public string Scope = "car";
        /// <summary>The text filter, lower-cased (as the input handler keeps it).</summary>
        public string Q = "";
        /// <summary>The filter field's text, as typed.</summary>
        public string Typed = "";

        /// <summary>The reader typed in "Filter jobs" (the page draws again 120 ms later).</summary>
        public void Filter(string typed)
        {
            Typed = typed ?? "";
            Q = Typed.ToLowerInvariant();
        }
        public JsonValue List = JsonValue.Array(new JsonValue[0]);

        /// <summary>LAMP[status].</summary>
        public static string LampOf(JsonValue status)
        {
            string s = status != null && status.IsString ? status.Str : null;
            switch (s)
            {
                case "done": return "grn";
                case "failed": return "red";
                case "running": return "cyan";
                case "interrupted": return "org";
                default: return "";
            }
        }

        /// <summary>/api/jobs, with ?vehicle= for "This vehicle" and a vehicle shown.</summary>
        public static string Path(string scope, HzCar v)
        {
            return "/api/jobs" + (scope == "car" && v != null ? "?vehicle=" + Hz.EncId(v.Id) : "");
        }

        /// <summary>load(): the list for the scope (a failure throws).</summary>
        public async Task Load(IJsonApi api, HzCar v, CancellationToken ct)
        {
            JsonValue r = await api.Get(Path(Scope, v), ct);
            List = r["jobs"];
        }

        static bool Is(JsonValue v, string s)
        {
            return v != null && v.IsString && v.Str == s;
        }

        bool Shown(JsonValue j)
        {
            JsonValue st = j["status"];
            bool byStatus = Show == "all" || (Show == "other" ? (Is(st, "cancelled") || Is(st, "interrupted")) : Is(st, Show));
            if (!byStatus) return false;
            if (string.IsNullOrEmpty(Q)) return true;
            string text = Hz.Tpl(j, "title") + " " + Hz.Tpl(j, "type") + " " + Hz.Or(j["detail"]) + " " + Hz.Tpl(j, "status");
            return text.ToLowerInvariant().Contains(Q);
        }

        /// <summary>
        /// draw(): the rows shown, the lines over them, Clear's state. now is
        /// Date.now(); currency is app.power?.currency (null or "" for "$").
        /// A list that is not a list throws, as list.filter does.
        /// </summary>
        public HzJobsView View(HzCar v, long now, bool h24, string currency)
        {
            if (!List.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            HzJobsView view = new HzJobsView();
            int running = 0;
            foreach (JsonValue j in List.Items)
            {
                if (Is(j["status"], "running")) running++;
                else view.CanClear = true;
            }
            view.Ticking = running > 0;
            view.Sub = Hz.Count(List.Count, "job") + " · " + running + " running" + (Scope == "car" && v != null ? " · " + v.Name + OfNoVehicle : EveryVehicle);
            view.Status = running + " running · " + Hz.Count(List.Count, "job");
            string cur = string.IsNullOrEmpty(currency) ? "$" : currency;
            foreach (JsonValue j in List.Items)
            {
                if (!Shown(j)) continue;
                HzJobRow r = new HzJobRow();
                r.Lamp = LampOf(j["status"]);
                r.Title = Hz.Js(j["title"]);
                r.Detail = Hz.Or(j["detail"]);
                r.Meta = Hz.Js(j["type"]) + " · #" + Hz.Js(j["id"]);
                r.Status = Hz.Js(j["status"]);
                r.Spin = Is(j["status"], "running");
                r.Started = Hz.StampFull(j["startedAt"], h24);
                string took;
                if (Is(j["status"], "interrupted")) took = Hz.Dash;
                else if (j["finishedAt"].Bool) took = Hz.Span(Hz.Number(j["finishedAt"]) - Number(j, "startedAt"));
                else took = Hz.Span(now - Number(j, "startedAt"));
                r.Took = "took " + took;
                JsonValue e = j["energy"];
                if (Hz.Ok(e["wh"]))
                    r.Energy = Hz.Wh(e["wh"]) + (Hz.Ok(e["avgW"]) ? " · " + Hz.Watt(e["avgW"]) : "") + (Hz.Ok(e["cost"]) ? " · " + Hz.Cash(e["cost"], cur) : "");
                view.Rows.Add(r);
            }
            if (view.Rows.Count == 0) view.Empty = List.Count > 0 ? NoMatch : NoJobs;
            return view;
        }

        /// <summary>A number as subtraction reads it: undefined NaN, null 0.</summary>
        static double Number(JsonValue owner, string key)
        {
            return Hz.Missing(owner, key) ? double.NaN : Hz.Number(owner[key]);
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonJobs.test.js reads the web's:
        /// [title] Jobs, the lead, [btn( disabled)] Clear finished…, the two
        /// segs with [seg( on)] buttons, [input q=…], [list], then per entry
        /// [pit lamp] title, detail, meta, [pill lamp( spin)] status, started,
        /// took, energy; or [blank] and its text.
        /// </summary>
        public List<string> Outline(HzJobsView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[title]"); t(Title); t(v.Sub);
            o.Add(v.CanClear ? "[btn]" : "[btn disabled]"); t(ClearLabel);
            o.Add("[segs js]");
            foreach (string[] s in Shows) { o.Add(s[0] == Show ? "[seg on]" : "[seg]"); t(s[1]); }
            o.Add("[segs jsc]");
            foreach (string[] s in Scopes) { o.Add(s[0] == Scope ? "[seg on]" : "[seg]"); t(s[1]); }
            o.Add("[input q=" + Typed + "]");
            o.Add("[list]");
            if (v.Empty != null) { o.Add("[blank]"); t(v.Empty); return o; }
            foreach (HzJobRow r in v.Rows)
            {
                o.Add("[pit " + r.Lamp + "]");
                t(r.Title); t(r.Detail); t(r.Meta);
                o.Add("[pill " + r.Lamp + (r.Spin ? " spin" : "") + "]"); t(r.Status);
                t(r.Started); t(r.Took);
                if (r.Energy != null) t(r.Energy);
            }
            return o;
        }

        // ---------------------------------------------------------------- Clear finished…

        /// <summary>{ vehicle: id or null } plus preview: true or confirm: true.</summary>
        public static JsonValue ClearBody(HzCar v, string flag)
        {
            return JsonValue.Object("vehicle", v != null && !string.IsNullOrEmpty(v.Id) ? v.Id : null, flag, true);
        }

        public static HzAsk ClearAsk(JsonValue n, HzCar v)
        {
            HzAsk a = new HzAsk();
            a.Title = ConfirmTitle;
            a.Para("", new Rich().Bold(Hz.Count(n, "finished job")).Text(ConfirmWhich + (v != null ? " of " + v.Name : "") + ConfirmStay));
            a.Para("dim", new Rich().Text(ConfirmUntouched));
            a.Yes = ConfirmYes;
            return a;
        }

        /// <summary>
        /// clear(): the vehicle is the shown one for "This vehicle", none for
        /// "Every vehicle". A failed preview is a red toast; none to clear a
        /// green one; a "no" ends it; else the clear (a failure a red toast)
        /// and load() again. busy(true/false) is act()'s disabled button.
        /// </summary>
        public async Task Clear(IJsonApi api, HzUi ui, HzCar shown, Action<bool> busy, Func<Task> reload)
        {
            HzCar v = Scope == "car" ? shown : null;
            JsonValue pv;
            try { pv = await api.Post(ClearPath, ClearBody(v, "preview"), CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, "bad"); return; }
            if (!pv["jobs"].Bool) { ui.Toast(NothingToClear, "ok"); return; }
            if (!await ui.Ask(ClearAsk(pv["jobs"], v))) return;
            busy(true);
            await ui.Act(async () =>
            {
                JsonValue o = await api.Post(ClearPath, ClearBody(v, "confirm"), CancellationToken.None);
                ui.Toast("Cleared " + Hz.Count(o["cleared"], "job") + ".", "ok");
            });
            busy(false);
            await reload();
        }

        /// <summary>?clear=1 on the route: Clear finished… starts by itself (params.get('clear')).</summary>
        public static bool ClearAsked(string sub)
        {
            return HzRoute.Param(sub, "clear") != null && HzRoute.Param(sub, "clear").Length > 0;
        }
    }

    /// <summary>The route's sub path and query (main.js parse(): "#/jobs?clear=1").</summary>
    public static class HzRoute
    {
        /// <summary>The part before "?" ("vehicle/3" of "vehicle/3?x=1").</summary>
        public static string Path(string sub)
        {
            string s = sub ?? "";
            int q = s.IndexOf('?');
            return q < 0 ? s : s.Substring(0, q);
        }

        /// <summary>URLSearchParams(query).get(name): null when it is not there.</summary>
        public static string Param(string sub, string name)
        {
            string s = sub ?? "";
            int q = s.IndexOf('?');
            if (q < 0) return null;
            foreach (string part in s.Substring(q + 1).Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string k = Uri.UnescapeDataString((eq < 0 ? part : part.Substring(0, eq)).Replace('+', ' '));
                if (k == name) return Uri.UnescapeDataString((eq < 0 ? "" : part.Substring(eq + 1)).Replace('+', ' '));
            }
            return null;
        }
    }
}
