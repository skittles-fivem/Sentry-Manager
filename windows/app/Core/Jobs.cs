using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>One row of the Jobs table, as web/app.js pages.jobs writes it.</summary>
    public sealed class JobRow
    {
        /// <summary>Job: the title (bold) and, under it, the type (muted, small).</summary>
        public string Title, Type;
        /// <summary>Status: the raw status word in a chip.</summary>
        public string Status;
        /// <summary>The chip's colour: "green" (done), "red" (failed), "amber" (running), "" (cancelled, interrupted, anything else: grey).</summary>
        public string Tone;
        /// <summary>Details (small); "" when the job has none.</summary>
        public string Detail;
        /// <summary>Started (small): when(startedAt), "—" without one.</summary>
        public string Started;
        /// <summary>Took (small, mono): "—" (interrupted), took(finishedAt - startedAt) (finished), "…" (still going).</summary>
        public string Took;
        /// <summary>Energy (small, mono): "412 Wh · avg 38 W · ≈ $0.14"; "" without a measurement (the cell is not drawn on a phone).</summary>
        public string Energy;
    }

    /// <summary>The Jobs page as web/app.js pages.jobs works it out from GET /api/jobs.</summary>
    public sealed class JobsView
    {
        public List<JobRow> Rows = new List<JobRow>();
        /// <summary>The Energy column is there: some job has energy.wh.</summary>
        public bool WithEnergy;
        /// <summary>Clear jobs can be pressed: some job is not running (else it is disabled, with "No finished jobs to clear").</summary>
        public bool CanClear;
        /// <summary>Running jobs: the Jobs badge in the navigation ("" for 0).</summary>
        public int Running;
    }

    /// <summary>"Clear finished jobs?": its title, two paragraphs and button.</summary>
    public sealed class JobsConfirm
    {
        public string Title, Ok;
        public List<string> Body = new List<string>();
    }

    /// <summary>What Clear jobs needs from the window.</summary>
    public sealed class JobsUi
    {
        /// <summary>toast(message, bad).</summary>
        public Action<string, bool> Toast = delegate { };
        /// <summary>confirmDialog(): true when the reader confirms.</summary>
        public Func<JobsConfirm, Task<bool>> Confirm = c => Task.FromResult(false);
        /// <summary>render(): the page draws again.</summary>
        public Action Render = delegate { };
    }

    /// <summary>
    /// web/app.js pages.jobs and clearJobs: the jobs of the vehicle shown
    /// (scans, conversions, Recycle Bin actions, policy runs) with their
    /// status, details, start, duration and, for conversions the server
    /// measured, energy, average power and cost; the running count sets the
    /// Jobs badge. Clear jobs takes the finished ones off the list after a
    /// confirmation with how many (the server decides which). The words,
    /// requests and toasts are the web's.
    /// </summary>
    public static class Jobs
    {
        public const string Eyebrow = "Your archive workspace";
        public const string PageTitle = "Jobs";
        public const string ClearLabel = "Clear jobs";
        public const string ClearDisabledTitle = "No finished jobs to clear";
        public const string NoJobs = "No jobs yet. Scans, conversions, Recycle Bin actions and policy runs show up here.";
        public static readonly string[] Columns = { "Job", "Status", "Details", "Started", "Took" };
        public const string EnergyColumn = "Energy";
        public const string Interrupted = "—";
        public const string Going = "…";
        public const string NothingToClear = "Nothing to clear: running, waiting and paused jobs stay on the list.";
        public const string ConfirmTitle = "Clear finished jobs?";
        public const string ConfirmWhich = " (done, failed, cancelled or interrupted)";
        public const string ConfirmStay = " the list. Running, waiting and paused jobs stay.";
        public const string ConfirmUntouched = "Only the list is cleared: footage, converted files and what the Encoder needs to resume a conversion are not touched. New jobs show up as before.";
        public const string FinishedJob = "finished job";
        public const string ClearPath = "/api/jobs/clear";

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>String(x) of a value that is there (esc() makes null and undefined "").</summary>
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

        /// <summary>fin(n): a finite number (not a numeric string).</summary>
        static bool Fin(JsonValue v)
        {
            return v != null && v.IsNumber && Format.Fin(v.Num);
        }

        /// <summary>Number(x) as subtraction sees it: undefined NaN, null 0, a string its number (NaN when it is none).</summary>
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
                return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : double.NaN;
            }
            return double.NaN;
        }

        static bool Is(JsonValue v, string s)
        {
            return v != null && v.IsString && v.Str == s;
        }

        // ---------------------------------------------------------------- the page

        /// <summary>/api/jobs, with ?vehicle=&lt;id&gt; (encoded) for the vehicle shown; null for none.</summary>
        public static string Path(string vehicleId)
        {
            return "/api/jobs" + (vehicleId != null ? "?vehicle=" + ApiCalls.Enc(vehicleId) : "");
        }

        /// <summary>GET the list; a failure throws (the whole page is the error).</summary>
        public static Task<JsonValue> Load(IJsonApi api, string vehicleId, CancellationToken ct)
        {
            return api.Get(Path(vehicleId), ct);
        }

        /// <summary>The chip's colour of a status (done green, failed red, running amber, else grey "").</summary>
        public static string ToneOf(JsonValue status)
        {
            return Is(status, "done") ? "green" : Is(status, "failed") ? "red" : Is(status, "running") ? "amber" : "";
        }

        /// <summary>energy(e): "12.4 Wh[ · avg 38 W][ · ≈ $0.14]"; "" without energy.wh.</summary>
        public static string EnergyOf(JsonValue e, string currency)
        {
            if (e == null || !Fin(e["wh"])) return "";
            string s = Format.EnergyText(e["wh"].Num);
            if (Fin(e["avgW"])) s += " · avg " + Format.Watts(e["avgW"].Num);
            if (Fin(e["cost"])) s += " · " + Format.Money(e["cost"].Num, currency);
            return s;
        }

        /// <summary>
        /// The page from the answer. currency is settings.power.currency
        /// (state.power?.currency: null or "" for "$"). A job list that is not
        /// a list throws, as the web's jobs.filter does.
        /// </summary>
        public static JobsView View(JsonValue answer, bool h24, string currency)
        {
            JsonValue jobs = answer["jobs"];
            if (!jobs.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            JobsView v = new JobsView();
            foreach (JsonValue j in jobs.Items)
            {
                JsonValue status = j["status"];
                if (Is(status, "running")) v.Running++;
                else v.CanClear = true;
                if (Fin(j["energy"]["wh"])) v.WithEnergy = true;
            }
            foreach (JsonValue j in jobs.Items)
            {
                JobRow r = new JobRow();
                r.Title = Js(j["title"]);
                r.Type = Js(j["type"]);
                r.Status = Js(j["status"]);
                r.Tone = ToneOf(j["status"]);
                r.Detail = j["detail"].Bool ? Js(j["detail"]) : "";
                r.Started = Format.When(j["startedAt"], h24);
                if (Is(j["status"], "interrupted")) r.Took = Interrupted;
                else if (j["finishedAt"].Bool) r.Took = Format.Took(Number(j, "finishedAt") - Number(j, "startedAt"));
                else r.Took = Going;
                r.Energy = v.WithEnergy ? EnergyOf(j["energy"], currency) : "";
                v.Rows.Add(r);
            }
            return v;
        }

        /// <summary>
        /// The page as a reader sees it, in the markers test/windowsNativeJobs.test.js
        /// writes for the web's HTML: [head eyebrow | title] [btn( disabled)( | title)]
        /// [pills] [card] [p] [table] [tr] [th] [td( classes)] [b] [muted small] [chip( tone)].
        /// </summary>
        public static List<string> Outline(JobsView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = (s ?? "").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head " + Eyebrow + " | " + PageTitle + "]");
            o.Add(v.CanClear ? "[btn]" : "[btn disabled | " + ClearDisabledTitle + "]");
            t(ClearLabel);
            o.Add("[pills]");
            o.Add("[card]");
            if (v.Rows.Count == 0)
            {
                o.Add("[p]"); t(NoJobs);
                return o;
            }
            o.Add("[table]");
            o.Add("[tr]");
            foreach (string c in Columns) { o.Add("[th]"); t(c); }
            if (v.WithEnergy) { o.Add("[th]"); t(EnergyColumn); }
            foreach (JobRow r in v.Rows)
            {
                o.Add("[tr]");
                o.Add("[td]"); o.Add("[b]"); t(r.Title); o.Add("[muted small]"); t(r.Type);
                o.Add("[td]"); o.Add(r.Tone.Length > 0 ? "[chip " + r.Tone + "]" : "[chip]"); t(r.Status);
                o.Add("[td small]"); t(r.Detail);
                o.Add("[td small]"); t(r.Started);
                o.Add("[td small mono]"); t(r.Took);
                if (v.WithEnergy) { o.Add("[td small mono energy]"); t(r.Energy); }
            }
            return o;
        }

        // ---------------------------------------------------------------- Clear jobs

        /// <summary>{ vehicle: id or null } plus preview: true or confirm: true.</summary>
        public static JsonValue ClearBody(string vehicleId, string flag)
        {
            return JsonValue.Object("vehicle", string.IsNullOrEmpty(vehicleId) ? null : vehicleId, flag, true);
        }

        /// <summary>The confirmation for n finished jobs; " of &lt;vehicle&gt;" when there are two or more vehicles.</summary>
        public static JobsConfirm ClearConfirm(JsonValue n, string vehicleName, bool named)
        {
            JobsConfirm c = new JobsConfirm();
            c.Title = ConfirmTitle;
            bool one = n != null && n.IsNumber && n.Num == 1;
            c.Body.Add(Format.Plural(n.Num, FinishedJob) + ConfirmWhich + (named ? " of " + (vehicleName ?? "") : "") + " leave" + (one ? "s" : "") + ConfirmStay);
            c.Body.Add(ConfirmUntouched);
            c.Ok = ClearLabel;
            return c;
        }

        /// <summary>
        /// Clear jobs: asks how many would go (preview), says so when none,
        /// else asks "Clear finished jobs?" and clears them, "Cleared N
        /// finished jobs." and the page again (also after a failed clear). A
        /// failed preview is a red toast and nothing more. vehicleId is the
        /// vehicle shown (null for none); vehicles is how many there are.
        /// </summary>
        public static async Task Clear(IJsonApi api, JobsUi ui, string vehicleId, string vehicleName, int vehicles)
        {
            JsonValue preview;
            try
            {
                preview = await api.Post(ClearPath, ClearBody(vehicleId, "preview"), CancellationToken.None);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
                return;
            }
            if (!preview["jobs"].Bool)
            {
                ui.Toast(NothingToClear, false);
                return;
            }
            bool ok = await ui.Confirm(ClearConfirm(preview["jobs"], vehicleName, vehicleId != null && vehicles > 1));
            if (!ok) return;
            try
            {
                JsonValue o = await api.Post(ClearPath, ClearBody(vehicleId, "confirm"), CancellationToken.None);
                ui.Toast("Cleared " + Format.Plural(o["cleared"].Num, FinishedJob) + ".", false);
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
            ui.Render();
        }
    }
}
