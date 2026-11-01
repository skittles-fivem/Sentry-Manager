using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Aurora
{
    /// <summary>One stop of Aurora's jobs timeline (aurora/pages/jobs.js: an li of the ol.timeline).</summary>
    public sealed class AuJobRow
    {
        /// <summary>The li's class (the job's status as written: its dot's colour).</summary>
        public string Status = "";
        public string Title = "";
        /// <summary>The chip's tone: amber, green, red or "".</summary>
        public string Tone = "";
        /// <summary>The chip's word ("Running", "Done", …, or the status as written).</summary>
        public string Word = "";
        /// <summary>The p.small line under the title ("" for none).</summary>
        public string Detail = "";
        /// <summary>The p.muted.tiny line: type · vehicle · started · took · energy.</summary>
        public string Meta = "";
    }

    /// <summary>The Jobs window as aurora/pages/jobs.js load() works it out.</summary>
    public sealed class AuJobsView
    {
        /// <summary>ctx.sub(): "6 jobs · 1 running", "" without jobs.</summary>
        public string Sub = "";
        public bool CanClear;
        /// <summary>emit('jobs-running'): how many run, and whether one is a conversion (the dock's badges).</summary>
        public int Running;
        public bool Converting;
        public List<AuJobRow> Rows = new List<AuJobRow>();
        /// <summary>No jobs: the empty orb with "No jobs yet".</summary>
        public bool Empty;
    }

    /// <summary>
    /// Aurora's Jobs window (examples/ui-packs/aurora/web/aurora/pages/jobs.js):
    /// the kicker "Timeline", the sub line, a vehicle select in the title bar
    /// (two vehicles or more: "All vehicles" or one, kept as aurora:jobs-scope),
    /// Clear finished, the timeline of jobs (title and status chip, the detail,
    /// the type · vehicle · start · duration · energy line) or the empty orb;
    /// Clear finished with the server's preview, "Clear finished jobs?", the
    /// clear and its toasts. The same GET /api/jobs and POST /api/jobs/clear
    /// as the built-in and the other packs' pages; the words are Aurora's.
    /// Checked by test/windowsNativeAuroraJobs.test.js and Core.Tests AuroraJobsTests.
    /// </summary>
    public sealed class AuJobs
    {
        public const string Title = "Jobs";
        public const string Kicker = "Timeline";
        public const string Icon = "jobs";
        public const string ClearLabel = "Clear finished";
        public const string AllVehicles = "All vehicles";
        public const string PickLabel = "Vehicle";
        public const string Running = "running";
        public const string RunningTook = "running…";
        public const string EmptyTitle = "No jobs yet";
        public const string EmptyText = "Scans, conversions, Recycle Bin moves and policy runs show up here.";
        public const string NothingToClear = "Nothing to clear: running, waiting and paused jobs stay.";
        public const string ConfirmTitle = "Clear finished jobs?";
        public const string ConfirmWhich = " (done, failed, cancelled or interrupted) leave";
        public const string ConfirmStay = " the list. Running, waiting and paused jobs stay.";
        public const string ConfirmUntouched = "Only the list changes: footage, converted files and what a paused conversion needs to resume are untouched.";
        public const string ConfirmYes = "Clear jobs";
        public const string ClearedOne = "finished job";
        public const string ScopeKey = "jobs-scope";
        public const string ClearPath = "/api/jobs/clear";

        /// <summary>The kept choice: localStorage "aurora:jobs-scope" on the web.</summary>
        public static string StoreKey { get { return "aurora:" + ScopeKey; } }

        /// <summary>"all" or a vehicle's id.</summary>
        public string Scope = "all";
        public JsonValue List = JsonValue.Array(new JsonValue[0]);

        /// <summary>STATUS[status]: [chip tone, word].</summary>
        public static string[] StatusOf(JsonValue status)
        {
            switch (status != null && status.IsString ? status.Str : null)
            {
                case "running": return new[] { "amber", "Running" };
                case "done": return new[] { "green", "Done" };
                case "failed": return new[] { "red", "Failed" };
                case "cancelled": return new[] { "", "Cancelled" };
                case "interrupted": return new[] { "", "Interrupted" };
                default: return new[] { "", Hz.Js(status) };
            }
        }

        /// <summary>mount(): the kept scope, "all" when it names no vehicle there is.</summary>
        public static string ScopeFrom(string stored, IList<HzCar> cars)
        {
            if (stored == null || stored == "all") return "all";
            if (cars != null) foreach (HzCar c in cars) if (c.Id == stored) return stored;
            return "all";
        }

        public static string Path(string scope)
        {
            return "/api/jobs" + (scope == "all" ? "" : "?vehicle=" + Hz.EncId(scope));
        }

        /// <summary>{ vehicle: scope === 'all' ? null : scope, [flag]: true }.</summary>
        public static JsonValue ClearBody(string scope, string flag)
        {
            return JsonValue.Object("vehicle", scope == "all" ? null : scope, flag, true);
        }

        /// <summary>The vehicle select is shown: two vehicles or more.</summary>
        public static bool Picks(IList<HzCar> cars) { return cars != null && cars.Count > 1; }

        public async Task Load(IJsonApi api, CancellationToken ct)
        {
            JsonValue r = await api.Get(Path(Scope), ct);
            List = r["jobs"];
        }

        static bool Is(JsonValue v, string s) { return v != null && v.IsString && v.Str == s; }

        static double Number(JsonValue owner, string key) { return Hz.Missing(owner, key) ? double.NaN : Hz.Number(owner[key]); }

        public AuJobsView View(IList<HzCar> cars, bool h24, string currency)
        {
            if (!List.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            AuJobsView view = new AuJobsView();
            foreach (JsonValue j in List.Items)
            {
                if (Is(j["status"], Running))
                {
                    view.Running++;
                    if (Is(j["type"], "conversion")) view.Converting = true;
                }
                else view.CanClear = true;
            }
            int n = List.Count;
            view.Sub = n > 0 ? Au.Plural(n, "job") + (view.Running > 0 ? " · " + view.Running + " running" : "") : "";
            string cur = string.IsNullOrEmpty(currency) ? "$" : currency;
            foreach (JsonValue j in List.Items)
            {
                JsonValue st = j["status"];
                string[] s = StatusOf(st);
                AuJobRow r = new AuJobRow();
                r.Status = Hz.Js(st);
                r.Title = Hz.Js(j["title"]);
                r.Tone = s[0];
                r.Word = s[1];
                r.Detail = Hz.Or(j["detail"]);
                string took = Is(st, "interrupted") ? "" : j["finishedAt"].Bool ? Au.Dur(Hz.Number(j["finishedAt"]) - Number(j, "startedAt")) : RunningTook;
                string name = "";
                JsonValue vid = j["vehicleId"];
                if (vid.Bool && vid.IsString && cars != null)
                    foreach (HzCar c in cars) if (c.Id == vid.Str) { name = c.Name ?? ""; break; }
                JsonValue e = j["energy"];
                string energy = "";
                if (e.Bool && Au.Fin(e["wh"]))
                    energy = " · " + Au.Energy(e["wh"]) + (Au.Fin(e["avgW"]) ? ", avg " + Au.Watts(e["avgW"]) : "") + (Au.Fin(e["cost"]) ? ", " + Au.Money(e["cost"], cur) : "");
                r.Meta = Hz.Js(j["type"]) + (name.Length > 0 ? " · " + name : "") + " · " + Au.WhenFull(j["startedAt"], h24) + (took.Length > 0 ? " · " + took : "") + energy;
                view.Rows.Add(r);
            }
            view.Empty = n == 0;
            return view;
        }

        /// <summary>
        /// The window as test/windowsNativeAuroraJobs.test.js reads the web's:
        /// [head] Timeline Jobs [sub] the sub line, [actions] ([select scope]
        /// and its [option( on) name]s with two vehicles or more), [btn( disabled)]
        /// Clear finished, [body] then [timeline] with per job [li status]
        /// title [chip tone] word ([detail] text) [meta] text; or [empty] and
        /// its words; or (error) main.js's [empty] Something went wrong, the
        /// message, [retry] Try again.
        /// </summary>
        public List<string> Outline(AuJobsView v, IList<HzCar> cars, string error)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            Func<string, string> cls = s => string.Join(" ", Regex.Split((s ?? "").Trim(), @"\s+")).Trim();
            o.Add("[head]"); t(Kicker); t(Title);
            o.Add("[sub]"); if (v != null) t(v.Sub);
            o.Add("[actions]");
            if (Picks(cars))
            {
                o.Add("[select " + Scope + "]");
                o.Add("[option" + (Scope == "all" ? " on" : "") + " " + AllVehicles + "]");
                foreach (HzCar c in cars) o.Add("[option" + (c.Id == Scope ? " on" : "") + " " + c.Name + "]");
            }
            o.Add(v != null && v.CanClear ? "[btn]" : error != null ? "[btn]" : "[btn disabled]");
            t(ClearLabel);
            o.Add("[body]");
            if (error != null)
            {
                o.Add("[empty]"); t(Au.LoadFailedTitle); t(error); o.Add("[retry]"); t(Au.TryAgain);
                return o;
            }
            if (v.Empty) { o.Add("[empty]"); t(EmptyTitle); t(EmptyText); return o; }
            o.Add("[timeline]");
            foreach (AuJobRow r in v.Rows)
            {
                string c = cls(r.Status);
                o.Add("[li" + (c.Length > 0 ? " " + c : "") + "]");
                t(r.Title);
                o.Add("[chip" + (r.Tone.Length > 0 ? " " + r.Tone : "") + "]");
                t(r.Word);
                if (r.Detail.Length > 0) { o.Add("[detail]"); t(r.Detail); }
                o.Add("[meta]"); t(r.Meta);
            }
            return o;
        }

        /// <summary>confirmDialog({ title, body, ok: 'Clear jobs' }): how many leave the list, and that only the list changes.</summary>
        public static HzAsk ClearAsk(JsonValue n)
        {
            HzAsk a = new HzAsk();
            a.Title = ConfirmTitle;
            bool one = n != null && n.IsNumber && n.Num == 1;
            a.Para("", new Rich().Text(Au.Plural(n, ClearedOne) + ConfirmWhich + (one ? "s" : "") + ConfirmStay));
            a.Para("muted", new Rich().Text(ConfirmUntouched));
            a.Yes = ConfirmYes;
            a.No = "Cancel";
            return a;
        }

        /// <summary>
        /// Clear finished: the preview (its failure a red toast), "Nothing to
        /// clear…" when none would go, the question, the clear ("Cleared 3
        /// finished jobs." or its failure in red), then the list again.
        /// </summary>
        public async Task Clear(IJsonApi api, HzUi ui, Func<Task> reload)
        {
            JsonValue pv;
            try { pv = await api.Post(ClearPath, ClearBody(Scope, "preview"), CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, "bad"); return; }
            if (!pv["jobs"].Bool) { ui.Toast(NothingToClear, "ok"); return; }
            if (!await ui.Ask(ClearAsk(pv["jobs"]))) return;
            Exception failed = null;
            try
            {
                JsonValue o = await api.Post(ClearPath, ClearBody(Scope, "confirm"), CancellationToken.None);
                ui.Toast("Cleared " + Au.Plural(o["cleared"], ClearedOne) + ".", "ok");
            }
            catch (Exception ex) { failed = ex; }
            if (failed != null) ui.Toast(failed.Message, "bad");
            await reload();
        }
    }
}
