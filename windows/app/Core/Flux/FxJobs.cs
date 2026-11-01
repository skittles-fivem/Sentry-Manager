using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>One row of Flux's jobs table (flux/pages/jobs.js).</summary>
    public sealed class FxJobRow
    {
        public string Title, Meta, Status, Tone, Detail, Started, Took;
        /// <summary>The running chip with its spinner.</summary>
        public bool Spin;
        /// <summary>The energy cell's text ("" for a job without energy; null when the table has no Energy column).</summary>
        public string Energy;
    }

    /// <summary>The Jobs page as flux/pages/jobs.js draw() works it out.</summary>
    public sealed class FxJobsView
    {
        public string Sub;
        public bool WithEnergy, CanClear, Ticking;
        public List<FxJobRow> Rows = new List<FxJobRow>();
        public string Empty;
    }

    /// <summary>
    /// Flux's Jobs page (examples/ui-packs/flux/web/flux/pages/jobs.js): the
    /// status and vehicle filters, the text filter (/ focuses it), the jobs
    /// table (rows as blocks on a phone), Clear finished… with the server's
    /// preview, "Clear finished jobs?", the clear and its toasts. The same
    /// GET /api/jobs and POST /api/jobs/clear as the built-in and Horizon
    /// pages (HzJobs' paths and bodies); the words and layout are Flux's.
    /// Checked by test/windowsNativeFluxJobs.test.js and Core.Tests FluxJobsTests.
    /// </summary>
    public sealed class FxJobs
    {
        public const string Title = "Jobs";
        public const string ClearLabel = "Clear finished…";
        public const string FilterPlaceholder = "Filter jobs  ( / )";
        public const string FilterLabel = "Filter jobs";
        public const string NoMatch = "No job matches.";
        public const string NoJobs = "No jobs yet. Scans, conversions, Recycle Bin moves and policy runs show up here.";
        public const string NothingToClear = "Nothing to clear: running, waiting and paused jobs stay.";
        public const string ConfirmTitle = "Clear finished jobs?";
        public const string ConfirmWhich = " (done, failed, cancelled or interrupted)";
        public const string ConfirmStay = " leave the list. Running, waiting and paused ones stay.";
        public const string ConfirmUntouched = "Only the list changes: footage, converted files and what a paused conversion needs are untouched.";
        public const string ConfirmYes = "Clear jobs";
        public const string OfNoVehicle = " and jobs of no vehicle";
        public const string CommandLabel = "Clear finished jobs";
        public static readonly string[] Columns = { "job", "status", "details", "started", "took" };
        public const string EnergyColumn = "energy";

        public static readonly string[][] Statuses = { new[] { "all", "All" }, new[] { "running", "Running" }, new[] { "done", "Done" }, new[] { "failed", "Failed" }, new[] { "other", "Cancelled / interrupted" } };
        public static readonly string[][] Scopes = { new[] { "vehicle", "This vehicle" }, new[] { "all", "Every vehicle" } };

        public string Filter = "all";
        public string Scope = "vehicle";
        public string Text = "";
        public string Typed = "";
        public JsonValue List = JsonValue.Array(new JsonValue[0]);

        public void Type(string typed)
        {
            Typed = typed ?? "";
            Text = Typed.ToLowerInvariant();
        }

        /// <summary>TONE[status].</summary>
        public static string ToneOf(JsonValue status)
        {
            switch (status != null && status.IsString ? status.Str : null)
            {
                case "done": return "green";
                case "failed": return "red";
                case "running": return "cyan";
                case "interrupted": return "amber";
                default: return "";
            }
        }

        public static string Path(string scope, HzCar v)
        {
            return "/api/jobs" + (scope == "vehicle" && v != null ? "?vehicle=" + Hz.EncId(v.Id) : "");
        }

        public async Task Load(IJsonApi api, HzCar v, CancellationToken ct)
        {
            JsonValue r = await api.Get(Path(Scope, v), ct);
            List = r["jobs"];
        }

        static bool Is(JsonValue v, string s) { return v != null && v.IsString && v.Str == s; }

        static double Number(JsonValue owner, string key) { return Hz.Missing(owner, key) ? double.NaN : Hz.Number(owner[key]); }

        public FxJobsView View(HzCar v, long now, bool h24, string currency)
        {
            if (!List.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            FxJobsView view = new FxJobsView();
            int running = 0;
            foreach (JsonValue j in List.Items)
            {
                if (Is(j["status"], "running")) running++;
                else view.CanClear = true;
                if (Fx.Fin(j["energy"]["wh"])) view.WithEnergy = true;
            }
            view.Ticking = running > 0;
            view.Sub = Fx.Plural(List.Count, "job") + " · " + running + " running" + (Scope == "vehicle" && v != null ? " · " + v.Name + OfNoVehicle : "");
            string cur = string.IsNullOrEmpty(currency) ? "$" : currency;
            foreach (JsonValue j in List.Items)
            {
                JsonValue st = j["status"];
                bool shown = Filter == "all" || (Filter == "other" ? (Is(st, "cancelled") || Is(st, "interrupted")) : Is(st, Filter));
                if (shown && Text.Length > 0)
                    shown = (Hz.Tpl(j, "title") + " " + Hz.Tpl(j, "type") + " " + Hz.Or(j["detail"]) + " " + Hz.Tpl(j, "status")).ToLowerInvariant().Contains(Text);
                if (!shown) continue;
                FxJobRow r = new FxJobRow();
                r.Title = Hz.Js(j["title"]);
                r.Meta = Hz.Js(j["type"]) + " · #" + Hz.Js(j["id"]);
                r.Spin = Is(st, "running");
                r.Status = r.Spin ? "running" : Hz.Js(st);
                r.Tone = r.Spin ? "cyan" : ToneOf(st);
                r.Detail = Hz.Or(j["detail"]);
                r.Started = Fx.Isoish(j["startedAt"], h24);
                if (Is(st, "interrupted")) r.Took = Fx.Dash;
                else if (j["finishedAt"].Bool) r.Took = Fx.Dur(Hz.Number(j["finishedAt"]) - Number(j, "startedAt"));
                else r.Took = Fx.Dur(now - Number(j, "startedAt"));
                if (view.WithEnergy)
                {
                    JsonValue e = j["energy"];
                    r.Energy = Fx.Fin(e["wh"]) ? Fx.Energy(e["wh"]) + (Fx.Fin(e["avgW"]) ? " · " + Fx.Watts(e["avgW"]) : "") + (Fx.Fin(e["cost"]) ? " · " + Fx.Money(e["cost"], cur) : "") : "";
                }
                view.Rows.Add(r);
            }
            if (view.Rows.Count == 0) view.Empty = List.Count > 0 ? NoMatch : NoJobs;
            return view;
        }

        /// <summary>
        /// The page as test/windowsNativeFluxJobs.test.js reads the web's:
        /// [head] Jobs, the sub line, [btn( disabled)], the two [seg name]
        /// with [opt( on)] buttons, [input …], [table] [th( r)] …, per row
        /// [tr] [td] title, meta, [chip tone( spin)] status, [td] detail,
        /// started, took, energy; or [empty] and its text.
        /// </summary>
        public List<string> Outline(FxJobsView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head]"); t(Title); t(v.Sub);
            o.Add(v.CanClear ? "[btn]" : "[btn disabled]"); t(ClearLabel);
            o.Add("[seg jst]");
            foreach (string[] s in Statuses) { o.Add(s[0] == Filter ? "[opt on]" : "[opt]"); t(s[1]); }
            o.Add("[seg jscope]");
            foreach (string[] s in Scopes) { o.Add(s[0] == Scope ? "[opt on]" : "[opt]"); t(s[1]); }
            o.Add("[input " + Typed + "]");
            o.Add("[table]");
            foreach (string c in Columns) { o.Add(c == "took" ? "[th r]" : "[th]"); t(c); }
            if (v.WithEnergy) { o.Add("[th r]"); t(EnergyColumn); }
            if (v.Empty != null) { o.Add("[tr]"); o.Add("[td]"); o.Add("[empty]"); t(v.Empty); return o; }
            foreach (FxJobRow r in v.Rows)
            {
                o.Add("[tr]");
                o.Add("[td]"); t(r.Title); t(r.Meta);
                o.Add("[td]"); o.Add("[chip " + r.Tone + (r.Spin ? " spin" : "") + "]"); t(r.Status);
                o.Add("[td]"); t(r.Detail);
                o.Add("[td]"); t(r.Started);
                o.Add("[td r]"); t(r.Took);
                if (v.WithEnergy) { o.Add("[td r]"); t(r.Energy); }
            }
            return o;
        }

        public static HzAsk ClearAsk(JsonValue n, HzCar v)
        {
            HzAsk a = new HzAsk();
            a.Title = ConfirmTitle;
            a.Para("", new Rich().Bold(Fx.Plural(n, "finished job")).Text(ConfirmWhich + (v != null ? " of " + v.Name : "") + ConfirmStay));
            a.Para("muted", new Rich().Text(ConfirmUntouched));
            a.Yes = ConfirmYes;
            a.No = "Cancel";
            return a;
        }

        /// <summary>clearJobs(): as Horizon's, with Flux's words (busy() is act()).</summary>
        public async Task Clear(IJsonApi api, HzUi ui, HzCar shown, Action<bool> busy, Func<Task> reload)
        {
            HzCar v = Scope == "vehicle" ? shown : null;
            JsonValue pv;
            try { pv = await api.Post(HzJobs.ClearPath, HzJobs.ClearBody(v, "preview"), CancellationToken.None); }
            catch (Exception ex) { ui.Toast(ex.Message, "bad"); return; }
            if (!pv["jobs"].Bool) { ui.Toast(NothingToClear, "ok"); return; }
            if (!await ui.Ask(ClearAsk(pv["jobs"], v))) return;
            busy(true);
            await ui.Act(async () =>
            {
                JsonValue o = await api.Post(HzJobs.ClearPath, HzJobs.ClearBody(v, "confirm"), CancellationToken.None);
                ui.Toast("Cleared " + Fx.Plural(o["cleared"], "job") + ".", "ok");
            });
            busy(false);
            await reload();
        }
    }
}
