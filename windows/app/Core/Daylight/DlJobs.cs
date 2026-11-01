using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Daylight
{
    /// <summary>One job of Daylight's Activity list (dl/pages/jobs.js row()).</summary>
    public sealed class DlJobRow
    {
        public string Title;
        /// <summary>The chip: its word, tone (teal, good, bad, grey, warn) and icon ("bolt", "check" or "").</summary>
        public string Word, Tone, Icon;
        /// <summary>"Converting · Model 3 · started Oct 31, 3:10 PM · took 42 s".</summary>
        public string Meta;
        /// <summary>The detail line ("" for none) and the energy line ("" for none).</summary>
        public string Detail, Energy;
    }

    /// <summary>The Activity page as dl/pages/jobs.js draw() works it out.</summary>
    public sealed class DlJobsView
    {
        /// <summary>The vehicle picker (more than one vehicle): "" is All vehicles.</summary>
        public bool Picker;
        public string Picked = "";
        public bool CanClear;
        public List<DlJobRow> Running = new List<DlJobRow>();
        public List<DlJobRow> Earlier = new List<DlJobRow>();
        /// <summary>No job at all: the empty box.</summary>
        public bool Empty;
    }

    /// <summary>
    /// Daylight's Activity page (examples/ui-packs/daylight/web/dl/pages/jobs.js):
    /// the vehicle picker (with more than one vehicle), Clear finished with the
    /// server's preview, "Clear finished activity?", the clear and its toasts;
    /// "Happening now" and "Earlier" cards of jobs, or the empty box. The same
    /// GET /api/jobs and POST /api/jobs/clear as the other interfaces; the
    /// words and layout are Daylight's. Checked by
    /// test/windowsNativeDaylightJobs.test.js and Core.Tests DaylightJobsTests.
    /// </summary>
    public sealed class DlJobs
    {
        public const string Title = "Activity";
        public const string Lede = "What the server has been doing for you.";
        public const string ClearLabel = "Clear finished";
        public const string RunningTitle = "Happening now";
        public const string EarlierTitle = "Earlier";
        public const string EmptyTitle = "Nothing yet";
        public const string EmptyText = "Footage checks, conversions, Recycle Bin moves and cleanup rules show up here.";
        public const string AllVehicles = "All vehicles";
        public const string NothingToClear = "Nothing to clear. Running, waiting and paused jobs always stay.";
        public const string ConfirmTitle = "Clear finished activity?";
        public const string ConfirmLeave = " will leave this list. Running, waiting and paused ones stay.";
        public const string ConfirmUntouched = "Only the list changes: footage, converted files and anything a paused conversion needs are left alone.";
        public const string ConfirmYes = "Clear";
        public const string Started = " · started ";
        public const string Took = " · took ";
        public const string Average = " · average ";
        public const string About = " · about ";
        public const string RunningPath = "/api/jobs?limit=50";

        /// <summary>STATUS: the chip's word and tone by status.</summary>
        public static string[] Status(string s)
        {
            switch (s)
            {
                case "running": return new[] { "In progress", "teal" };
                case "done": return new[] { "Done", "good" };
                case "failed": return new[] { "Problem", "bad" };
                case "cancelled": return new[] { "Stopped", "grey" };
                case "interrupted": return new[] { "Interrupted", "warn" };
                default: return null;
            }
        }

        /// <summary>TYPE: the job's kind in words; null for another.</summary>
        public static string Type(string t)
        {
            switch (t)
            {
                case "scan": return "Looking for footage";
                case "conversion": return "Converting";
                case "trash": return "Moving to Recycle Bin";
                case "policy": return "Cleanup rules";
                case "restore": return "Restoring";
                case "delete": return "Deleting";
                default: return null;
            }
        }

        /// <summary>The picked vehicle's id (the module's let filter, kept between visits); null is every vehicle.</summary>
        public string Filter;
        public List<HzCar> Vehicles = new List<HzCar>();
        public JsonValue List = JsonValue.Array(new JsonValue[0]);
        /// <summary>A live redraw waits while the picker has the focus (picking()).</summary>
        public bool Held;

        public DlJobs(List<HzCar> vehicles, string filter)
        {
            Vehicles = vehicles ?? new List<HzCar>();
            Filter = filter;
            // render(): if (filter && !vehicleById(filter)) filter = null
            if (!string.IsNullOrEmpty(Filter) && ById(Filter) == null) Filter = null;
            if (Filter == "") Filter = null;
        }

        public HzCar ById(string id)
        {
            foreach (HzCar v in Vehicles) if (v.Id == id) return v;
            return null;
        }

        public string Path()
        {
            return "/api/jobs" + (Filter != null ? "?vehicle=" + Hz.EncId(Filter) : "");
        }

        public async Task Load(IJsonApi api, CancellationToken ct)
        {
            JsonValue r = await api.Get(Path(), ct);
            List = r["jobs"];
        }

        /// <summary>The picker's change: "" is every vehicle.</summary>
        public void Pick(string value)
        {
            Filter = string.IsNullOrEmpty(value) ? null : value;
        }

        static bool Is(JsonValue v, string s) { return v != null && v.IsString && v.Str == s; }

        static double Number(JsonValue owner, string key) { return Hz.Missing(owner, key) ? double.NaN : Hz.Number(owner[key]); }

        /// <summary>energyText(e): "12.4 Wh · average 61 W · about $0.14"; "" without watt-hours.</summary>
        public static string EnergyText(JsonValue e, string currency)
        {
            if (e == null || !Dl.Fin(e["wh"])) return "";
            double x = e["wh"].Num;
            string wh = x < 1000 ? Format.ToFixed(x, x < 100 ? 1 : 0) + " Wh" : Format.ToFixed(x / 1000, 1) + " kWh";
            string cur = string.IsNullOrEmpty(currency) ? "$" : currency;
            return wh + (Dl.Fin(e["avgW"]) ? Average + Format.JsNum(Format.Round(e["avgW"].Num)) + " W" : "") + (Dl.Fin(e["cost"]) ? About + cur + Format.ToFixed(e["cost"].Num, 2) : "");
        }

        DlJobRow Row(JsonValue j, bool h24, string currency)
        {
            JsonValue st = j["status"];
            string status = st != null && st.IsString ? st.Str : null;
            string[] word = Status(status);
            DlJobRow r = new DlJobRow();
            r.Title = Hz.Js(j["title"]);
            r.Word = word != null ? word[0] : Hz.Js(st);
            r.Tone = word != null ? word[1] : "grey";
            r.Icon = status == "running" ? "bolt" : status == "done" ? "check" : "";
            string type = Type(j["type"].IsString ? j["type"].Str : null);
            string vname = "";
            if (Vehicles.Count > 1 && j["vehicleId"].Bool && j["vehicleId"].IsString)
            {
                HzCar v = ById(j["vehicleId"].Str);
                if (v != null) vname = v.Name ?? "";
            }
            string took = "";
            if (j["finishedAt"].Bool && !Is(st, "interrupted")) took = Took + Dl.Duration(Hz.Number(j["finishedAt"]) - Number(j, "startedAt"));
            r.Meta = (type ?? Hz.Js(j["type"])) + (vname.Length > 0 ? " · " + vname : "") + Started + Dl.When(j["startedAt"], h24) + took;
            r.Detail = Hz.Or(j["detail"]);
            r.Energy = EnergyText(j["energy"], currency);
            return r;
        }

        public DlJobsView View(bool h24, string currency)
        {
            if (!List.IsArray) throw new InvalidOperationException("Cannot read properties of undefined (reading 'filter')");
            DlJobsView view = new DlJobsView();
            view.Picker = Vehicles.Count > 1;
            view.Picked = Filter ?? "";
            foreach (JsonValue j in List.Items)
            {
                if (Is(j["status"], "running")) view.Running.Add(Row(j, h24, currency));
                else view.Earlier.Add(Row(j, h24, currency));
            }
            view.CanClear = view.Earlier.Count > 0;
            view.Empty = List.Count == 0;
            return view;
        }

        /// <summary>
        /// The page as test/windowsNativeDaylightJobs.test.js reads the web's:
        /// [head] Activity, the lede, ([select v] [opt …]), [btn( disabled)]
        /// Clear finished, then [card] Happening now / Earlier with per job
        /// [item] title [chip tone( icon)] word [meta] … ([detail] …) ([energy] …);
        /// or [empty] Nothing yet and its text.
        /// </summary>
        public List<string> Outline(DlJobsView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head]"); t(Title); t(Lede);
            if (v.Picker)
            {
                o.Add("[select " + v.Picked + "]");
                o.Add("[opt " + (v.Picked == "" ? "on " : "") + AllVehicles + "]");
                foreach (HzCar c in Vehicles) o.Add(System.Text.RegularExpressions.Regex.Replace("[opt " + c.Id + (c.Id == v.Picked ? " on " : " ") + (c.Name ?? "") + "]", @"\s+", " "));
            }
            o.Add(v.CanClear ? "[btn]" : "[btn disabled]"); t(ClearLabel);
            if (v.Empty) { o.Add("[empty]"); t(EmptyTitle); t(EmptyText); return o; }
            Action<string, List<DlJobRow>> card = (title, rows) =>
            {
                if (rows.Count == 0) return;
                o.Add("[card]"); t(title);
                foreach (DlJobRow r in rows)
                {
                    o.Add("[item]"); t(r.Title);
                    o.Add("[chip " + r.Tone + (r.Icon.Length > 0 ? " " + r.Icon : "") + "]"); t(r.Word);
                    o.Add("[meta]"); t(r.Meta);
                    if (r.Detail.Length > 0) { o.Add("[detail]"); t(r.Detail); }
                    if (r.Energy.Length > 0) { o.Add("[energy]"); t(r.Energy); }
                }
            };
            card(RunningTitle, v.Running);
            card(EarlierTitle, v.Earlier);
            return o;
        }

        public static HzAsk ClearAsk(JsonValue n, HzCar v)
        {
            HzAsk a = new HzAsk();
            a.Title = ConfirmTitle;
            a.Para("", new Rich().Text(Dl.Plural(n, "finished job") + (v != null ? " of " + v.Name : "") + ConfirmLeave));
            a.Para("muted", new Rich().Text(ConfirmUntouched));
            a.Yes = ConfirmYes;
            a.No = "Cancel";
            return a;
        }

        /// <summary>fail(err): a red toast with the message, except when signed out (401).</summary>
        public static void Fail(HzUi ui, Exception ex)
        {
            ApiError ae = ex as ApiError;
            if (ae != null && ae.Status == 401) return;
            ui.Toast(ex.Message, "bad");
        }

        /// <summary>refresh(): the list again (a failure is a red toast and no redraw); true to draw.</summary>
        public async Task<bool> Refresh(IJsonApi api, HzUi ui)
        {
            try { await Load(api, CancellationToken.None); }
            catch (Exception ex) { Fail(ui, ex); return false; }
            return true;
        }

        JsonValue Body(string flag)
        {
            return HzJobs.ClearBody(Filter != null ? new HzCar(Filter, "") : null, flag);
        }

        /// <summary>
        /// clear(): the server's preview, "Clear finished activity?", the clear
        /// and its toast (toast kinds "info", "good", "bad"), the shell's
        /// running jobs again (loadJobs(): <paramref name="loadRunning"/>),
        /// then refresh(); <paramref name="redraw"/> runs when it read the list.
        /// </summary>
        public async Task Clear(IJsonApi api, HzUi ui, Action loadRunning, Action redraw)
        {
            JsonValue pv;
            try { pv = await api.Post(HzJobs.ClearPath, Body("preview"), CancellationToken.None); }
            catch (Exception ex) { Fail(ui, ex); return; }
            if (!pv["jobs"].Bool) { ui.Toast(NothingToClear, "info"); return; }
            HzCar v = Filter != null ? ById(Filter) : null;
            if (!await ui.Ask(ClearAsk(pv["jobs"], v))) return;
            try
            {
                JsonValue o = await api.Post(HzJobs.ClearPath, Body("confirm"), CancellationToken.None);
                ui.Toast("Cleared " + Dl.Plural(o["cleared"], "finished job") + ".", "good");
            }
            catch (Exception ex) { Fail(ui, ex); }
            loadRunning();
            if (await Refresh(api, ui)) redraw();
        }

        /// <summary>loadJobs() (the shell's running jobs): GET /api/jobs?limit=50; the running ones' count and whether one converts, or null.</summary>
        public static async Task<int[]> LoadRunning(IJsonApi api)
        {
            try
            {
                JsonValue r = await api.Get(RunningPath, CancellationToken.None);
                int n = 0, conv = 0;
                foreach (JsonValue j in r["jobs"].Items) if (Is(j["status"], "running")) { n++; if (Is(j["type"], "conversion")) conv = 1; }
                return new[] { n, conv };
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// ctx.on('topics'): "refresh" for the jobs topic, "hold" while the
        /// picker has the focus (then <see cref="Leave"/>), "" otherwise (no
        /// jobs topic, or a dialog is open).
        /// </summary>
        public string Topics(ICollection<string> topics, bool dialogOpen, bool picking)
        {
            if (topics == null || !topics.Contains("jobs") || dialogOpen) return "";
            if (picking) { Held = true; return "hold"; }
            return "refresh";
        }

        /// <summary>focusout: the held refresh, once the picker is left.</summary>
        public bool Leave(bool picking)
        {
            if (!Held || picking) return false;
            Held = false;
            return true;
        }
    }
}
