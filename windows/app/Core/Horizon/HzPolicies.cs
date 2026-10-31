using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Horizon
{
    /// <summary>What the reader has in one vehicle's form (FormData of form.pol): the box, the four day fields as typed, the time.</summary>
    public sealed class HzPolicyForm
    {
        /// <summary>"Run this policy every day".</summary>
        public bool Enabled;
        /// <summary>keep-Recent, keep-Sentry, keep-Saved, keep-Other: the number fields' values ("" for none).</summary>
        public string[] Keep = { "", "", "", "" };
        /// <summary>runAt: the time field's value ("" while it is empty).</summary>
        public string RunAt = "";

        /// <summary>
        /// read(): { enabled, runAt: runAt || '03:30', keepDays: { Recent:
        /// Number(x) or null (an empty field), ... } }.
        /// </summary>
        public JsonValue Body()
        {
            JsonValue keep = JsonValue.Object();
            for (int i = 0; i < HzPolicies.Cats.Length; i++)
            {
                string s = Keep != null && i < Keep.Length ? Keep[i] : null;
                if (string.IsNullOrEmpty(s)) { keep = keep.With(HzPolicies.Cats[i], null); continue; }
                double d = Hz.Number(JsonValue.String(s));
                // (JSON.stringify writes NaN as null)
                keep = keep.With(HzPolicies.Cats[i], Format.Fin(d) ? (object)JsonValue.Number(d) : null);
            }
            return JsonValue.Object("enabled", Enabled, "runAt", string.IsNullOrEmpty(RunAt) ? HzPolicies.DefaultRunAt : RunAt, "keepDays", keep);
        }
    }

    /// <summary>One vehicle's panel: its form (form.pol) or, when its policy could not be read, the message.</summary>
    public sealed class HzPolicyCard
    {
        public HzCar Car;
        /// <summary>The policy could not be read: the panel is the name and this (p.badtx); nothing else.</summary>
        public string Error;
        /// <summary>The vehicle shown (form.pol.cyan: cyan corner brackets).</summary>
        public bool Current;
        /// <summary>The pill: "runs daily" (grn) or "off".</summary>
        public bool On;
        /// <summary>The fields' value attributes as the page writes them (keep-Recent ... ; runAt).</summary>
        public string[] KeepAttr = new string[4];
        public string RunAtAttr;
        /// <summary>The note is orange (the next run would move something).</summary>
        public bool Org;
        /// <summary>The note's first line, with its bold parts.</summary>
        public Rich Note;
        /// <summary>"Last run Oct 30, 3:30 AM: 12 moved, 3 skipped."; null before the first run.</summary>
        public string LastRun;
        /// <summary>What the reader has typed and ticked (the form).</summary>
        public HzPolicyForm Form;
    }

    /// <summary>The page as hz/pages/policies.js draw() writes it.</summary>
    public sealed class HzPoliciesView
    {
        /// <summary>No vehicle: the title alone and "Add a vehicle first." with its link.</summary>
        public bool NoVehicles;
        public List<HzPolicyCard> Cards = new List<HzPolicyCard>();
        /// <summary>status(): "1 of 2 vehicles on a schedule"; null when it is not called (no vehicle).</summary>
        public string Status;
    }

    /// <summary>
    /// Horizon's Policies page (examples/ui-packs/horizon/web/hz/pages/policies.js):
    /// a service schedule per vehicle, one form each (Run this policy every
    /// day, the days to keep each kind, Run at (server time), what the next
    /// run would move and the last run), Save policy and Run now… The same
    /// GET/PUT /api/vehicles/:id/policy and POST …/policy/run as the built-in
    /// page (Core/Policies.cs, whose number and time field rules the native
    /// fields use), here for every vehicle at once and with the id encoded;
    /// the words are the pack's. Checked by test/windowsNativeHorizonPolicies.test.js
    /// and Core.Tests HorizonPoliciesTests.
    /// </summary>
    public sealed class HzPolicies
    {
        public const string Title = "Policies";
        public const string Lead = "A service schedule per vehicle: recordings older than the days you set move to the Recycle Bin once a day. A policy never deletes anything, and protected recordings are always skipped. Leave a field empty to keep that kind forever.";
        public const string NoVehicle = "Add a vehicle first.";
        public const string AddVehicle = "Add a vehicle";
        public const string AddHref = "#/settings/vehicles?add=1";
        public const string RunsDaily = "runs daily";
        public const string Off = "off";
        public const string EnabledLabel = "Run this policy every day";
        public const string Forever = "forever";
        public const string Days = "days";
        public const string RunAtLabel = "Run at (server time)";
        public const string DefaultRunAt = "03:30";
        public const string NextMoves = "With the saved settings the next run moves ";
        public const string ToBin = " to the Recycle Bin.";
        public const string NothingNow = "With the saved settings nothing would move right now.";
        public const string LastRunWord = "Last run ";
        public const string SaveLabel = "Save policy";
        public const string RunLabel = "Run now…";
        public const string Saved = "Policy saved.";
        public const string NothingToMove = "Nothing to move right now.";
        public const string AskTitle = "Run the policy now?";
        public const string AskRest = " move to the Recycle Bin. You can restore them from there.";
        public const string AskYes = "Run now";
        public const string Moved = " moved to the Recycle Bin.";
        public const string OnSchedule = " on a schedule";

        /// <summary>CATS: the kinds and their colours (the field's left bar).</summary>
        public static readonly string[] Cats = { "Recent", "Sentry", "Saved", "Other" };
        public static readonly string[] Tones = { "cyan", "org", "grn", "" };

        /// <summary>The number field's aria-label: "Keep Recent recordings for this many days".</summary>
        public static string KeepAria(string cat)
        {
            return "Keep " + cat + " recordings for this many days";
        }

        /// <summary>/api/vehicles/:id/policy (encodeURIComponent).</summary>
        public static string Path(string vehicleId)
        {
            return "/api/vehicles/" + Hz.EncId(vehicleId) + "/policy";
        }

        // the page's state: the vehicles and what each policy answered (or its failure)
        public readonly List<HzCar> Cars = new List<HzCar>();
        readonly List<JsonValue> answers = new List<JsonValue>();
        readonly List<string> errors = new List<string>();
        readonly Dictionary<int, HzPolicyForm> forms = new Dictionary<int, HzPolicyForm>();

        /// <summary>
        /// draw()'s requests: every vehicle's policy at once (a failure is that
        /// panel's message). The forms start again from the answers.
        /// </summary>
        public async Task Load(IJsonApi api, IList<HzCar> cars, CancellationToken ct)
        {
            List<HzCar> list = new List<HzCar>(cars ?? new HzCar[0]);
            List<Task<JsonValue>> asks = new List<Task<JsonValue>>();
            foreach (HzCar v in list) asks.Add(api.Get(Path(v.Id), ct));
            List<JsonValue> got = new List<JsonValue>();
            List<string> bad = new List<string>();
            foreach (Task<JsonValue> t in asks)
            {
                try { got.Add(await t); bad.Add(null); }
                catch (Exception ex) { got.Add(null); bad.Add(ex.Message); }
            }
            Cars.Clear();
            Cars.AddRange(list);
            answers.Clear();
            answers.AddRange(got);
            errors.Clear();
            errors.AddRange(bad);
            forms.Clear();
        }

        /// <summary>The form of the vehicle with this id (null when it has none: no such vehicle, or its policy failed).</summary>
        public HzPolicyForm FormOf(string vehicleId, HzCar current, bool h24)
        {
            foreach (HzPolicyCard c in View(current, h24).Cards) if (c.Car.Id == vehicleId) return c.Form;
            return null;
        }

        static string Value(JsonValue owner, string key, string fallback)
        {
            if (Hz.Missing(owner, key) || owner[key].IsNull) return fallback;
            return Hz.Js(owner[key]);
        }

        /// <summary>
        /// draw(): the panels. A policy that is not there (undefined or null)
        /// throws, as p.enabled does.
        /// </summary>
        public HzPoliciesView View(HzCar current, bool h24)
        {
            HzPoliciesView view = new HzPoliciesView();
            if (Cars.Count == 0) { view.NoVehicles = true; return view; }
            int on = 0;
            for (int i = 0; i < Cars.Count; i++)
            {
                HzCar v = Cars[i];
                HzPolicyCard c = new HzPolicyCard();
                c.Car = v;
                if (errors[i] != null)
                {
                    c.Error = errors[i];
                    view.Cards.Add(c);
                    continue;
                }
                JsonValue d = answers[i];
                JsonValue p = d["policy"];
                if (Hz.Missing(d, "policy") || p.IsNull) throw new InvalidOperationException("Cannot read properties of " + (Hz.Missing(d, "policy") ? "undefined" : "null") + " (reading 'enabled')");
                JsonValue pv = d["preview"].Bool ? d["preview"] : JsonValue.Object();
                if (p["enabled"].Bool) on++;
                c.Current = current != null && v.Id == current.Id;
                c.On = p["enabled"].Bool;
                for (int k = 0; k < Cats.Length; k++) c.KeepAttr[k] = Value(p["keepDays"], Cats[k], "");
                c.RunAtAttr = p["runAt"].Bool ? Hz.Js(p["runAt"]) : DefaultRunAt;
                c.Org = pv["recordings"].Bool;
                c.Note = c.Org
                    ? new Rich().Text(NextMoves).Bold(Hz.Count(pv["recordings"], "recording")).Text(" (").Bold(Hz.Size(pv["bytes"])).Text(")" + ToBin)
                    : new Rich().Text(NothingNow);
                if (p["lastRunAt"].Bool)
                {
                    JsonValue r = p["lastResult"];
                    string what = r["error"].Bool ? Hz.Js(r["error"]) : Value(r, "moved", "0") + " moved, " + Value(r, "skipped", "0") + " skipped";
                    c.LastRun = LastRunWord + Hz.Stamp(p["lastRunAt"], h24) + ": " + what + ".";
                }
                HzPolicyForm f;
                if (!forms.TryGetValue(i, out f))
                {
                    f = new HzPolicyForm();
                    f.Enabled = c.On;
                    // (the browser keeps a number field's value only when it is a number, a time field's only when it is a time)
                    for (int k = 0; k < Cats.Length; k++) f.Keep[k] = Policies.NumberValue(c.KeepAttr[k]);
                    int hh, mm;
                    f.RunAt = Policies.ParseTime(c.RunAtAttr, out hh, out mm) ? c.RunAtAttr : "";
                    forms[i] = f;
                }
                c.Form = f;
                view.Cards.Add(c);
            }
            view.Status = on + " of " + Hz.Count(Cars.Count, "vehicle") + OnSchedule;
            return view;
        }

        /// <summary>
        /// The page as test/windowsNativeHorizonPolicies.test.js reads the
        /// web's: [title] and the lead; per vehicle [panel] name [bad] message,
        /// or [pol( cyan)] name [pill tone] [check enabled( on)], the four
        /// [keep tone] kind [input keep-Kind=value] days, Run at [input
        /// runAt=value], [note( org)] its lines, [btn go] Save policy [btn]
        /// Run now…; or [title] [panel] [blank] Add a vehicle first. [link] Add a vehicle.
        /// </summary>
        public static List<string> Outline(HzPoliciesView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[title]"); t(Title);
            if (v.NoVehicles)
            {
                o.Add("[panel]"); o.Add("[blank]"); t(NoVehicle);
                o.Add("[link " + AddHref + "]"); t(AddVehicle);
                return o;
            }
            t(Lead);
            foreach (HzPolicyCard c in v.Cards)
            {
                if (c.Error != null)
                {
                    o.Add("[panel]"); t(c.Car.Name); o.Add("[bad]"); t(c.Error);
                    continue;
                }
                o.Add(c.Current ? "[pol cyan]" : "[pol]");
                t(c.Car.Name);
                o.Add(c.On ? "[pill grn]" : "[pill]"); t(c.On ? RunsDaily : Off);
                o.Add(c.Form.Enabled ? "[check enabled on]" : "[check enabled]"); t(EnabledLabel);
                for (int k = 0; k < Cats.Length; k++)
                {
                    o.Add("[keep k-" + (Tones[k].Length > 0 ? Tones[k] : "none") + "]");
                    t(Cats[k]);
                    o.Add("[input keep-" + Cats[k] + "=" + c.Form.Keep[k] + "]");
                    t(Days);
                }
                t(RunAtLabel);
                o.Add("[input runAt=" + c.Form.RunAt + "]");
                o.Add(c.Org ? "[note org]" : "[note]");
                foreach (RichRun r in c.Note.Runs) t(r.Text);
                if (c.LastRun != null) t(c.LastRun);
                o.Add("[btn go]"); t(SaveLabel);
                o.Add("[btn]"); t(RunLabel);
            }
            return o;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>
        /// Save policy (the form's submit, after the browser's own checks):
        /// PUT the form, "Policy saved.", the page again (draw). A failure is
        /// a red toast (act()).
        /// </summary>
        public static Task Save(IJsonApi api, HzUi ui, string vehicleId, HzPolicyForm form, Func<Task> draw)
        {
            JsonValue body = form.Body();
            return ui.Act(async () =>
            {
                await api.Put(Path(vehicleId), body, CancellationToken.None);
                ui.Toast(Saved, "ok");
                await draw();
            });
        }

        public static HzAsk RunAsk(JsonValue preview)
        {
            HzAsk a = new HzAsk();
            a.Title = AskTitle;
            a.Para("", new Rich().Text(Hz.Count(preview["recordings"], "recording") + " (").Bold(Hz.Size(preview["bytes"])).Text(")" + AskRest));
            a.Yes = AskYes;
            return a;
        }

        /// <summary>out.moved?.length ?? 0: a list's or a string's length, else 0.</summary>
        static double MovedCount(JsonValue o)
        {
            JsonValue m = o["moved"];
            if (Hz.Missing(o, "moved") || m.IsNull) return 0;
            if (m.IsArray) return m.Count;
            if (m.IsString) return m.Str.Length;
            return 0;
        }

        /// <summary>
        /// Run now…: PUT the form, GET what the run would move; none: "Nothing
        /// to move right now." and the page again; else "Run the policy now?"
        /// (no: nothing more), POST …/run {}, "N recordings moved to the
        /// Recycle Bin." and the page again. A failure is a red toast.
        /// </summary>
        public static Task RunNow(IJsonApi api, HzUi ui, string vehicleId, HzPolicyForm form, Func<Task> draw)
        {
            JsonValue body = form.Body();
            return ui.Act(async () =>
            {
                await api.Put(Path(vehicleId), body, CancellationToken.None);
                JsonValue d = await api.Get(Path(vehicleId), CancellationToken.None);
                if (!d["preview"]["recordings"].Bool)
                {
                    ui.Toast(NothingToMove, "ok");
                    await draw();
                    return;
                }
                if (!await ui.Ask(RunAsk(d["preview"]))) return;
                JsonValue o = await api.Post(Path(vehicleId) + "/run", JsonValue.Object(), CancellationToken.None);
                ui.Toast(Hz.Count(MovedCount(o), "recording") + Moved, "ok");
                await draw();
            });
        }

        /// <summary>onTopics: inventory or vehicles redraw the page (unless the reader is in it).</summary>
        public static bool Redraws(ICollection<string> topics)
        {
            return topics != null && (topics.Contains("inventory") || topics.Contains("vehicles"));
        }
    }
}
