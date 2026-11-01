using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Flux
{
    /// <summary>One vehicle's card of Flux's Policies page: its form (form.pol) or, when its policy could not be read, the message.</summary>
    public sealed class FxPoliciesCard
    {
        public HzCar Car;
        /// <summary>The policy could not be read: the card is the name and this (p.bad-text); nothing else.</summary>
        public string Error;
        /// <summary>The vehicle shown (form.pol.current: a violet edge).</summary>
        public bool Current;
        /// <summary>The chip: "runs daily" (green) or "off".</summary>
        public bool On;
        /// <summary>The fields' value attributes as the page writes them (keep-Recent ... ; runAt).</summary>
        public string[] KeepAttr = new string[4];
        public string RunAtAttr;
        /// <summary>The note is amber (the next run would move something).</summary>
        public bool Amber;
        /// <summary>The note's first line, with its bold parts.</summary>
        public Rich Note;
        /// <summary>"Last run Oct 30, 2026, 3:10:02 PM: 12 moved, 3 skipped."; null before the first run.</summary>
        public string LastRun;
        /// <summary>What the reader has typed and ticked (the form; FormData reads it as Horizon's page does).</summary>
        public HzPolicyForm Form;
    }

    /// <summary>The page as flux/pages/policies.js draw() writes it.</summary>
    public sealed class FxPoliciesView
    {
        /// <summary>No vehicle: the title alone and "Add a vehicle first." with its button.</summary>
        public bool NoVehicles;
        public List<FxPoliciesCard> Cards = new List<FxPoliciesCard>();
    }

    /// <summary>
    /// Flux's Policies page (examples/ui-packs/flux/web/flux/pages/policies.js):
    /// one card per vehicle (Run this policy every day, Keep … (days) for
    /// each kind, Run at (server time), what the next run would move and the
    /// last run), Run now… and Save policy. The same GET/PUT
    /// /api/vehicles/:id/policy and POST …/policy/run as Horizon's page
    /// (HzPolicies: its paths, the form's body and FormData rules, and Core's
    /// Policies field rules), for every vehicle at once; the words and
    /// layout are Flux's. Checked by test/windowsNativeFluxPolicies.test.js
    /// and Core.Tests FluxPoliciesTests.
    /// </summary>
    public sealed class FxPolicies
    {
        public const string Title = "Policies";
        public const string Lead = "Recordings older than the days you set move to the Recycle Bin once a day. Nothing is ever deleted by a policy, and protected recordings are always skipped. Empty means keep forever.";
        public const string NoVehicle = "Add a vehicle first.";
        public const string AddVehicle = "Add a vehicle";
        public const string AddHref = "#/settings/vehicles?add=1";
        public const string RunsDaily = "runs daily";
        public const string Off = "off";
        public const string EnabledLabel = "Run this policy every day";
        public const string KeepWord = "Keep ";
        public const string DaysWord = " (days)";
        public const string Forever = "forever";
        public const string RunAtLabel = "Run at (server time)";
        public const string DefaultRunAt = "03:30";
        public const string NextMoves = "With the saved settings the next run moves ";
        public const string ToBin = " to the Recycle Bin.";
        public const string NothingNow = "With the saved settings nothing would move right now.";
        public const string LastRunWord = "Last run ";
        public const string RunLabel = "Run now…";
        public const string SaveLabel = "Save policy";
        public const string Saved = "Policy saved.";
        public const string NothingToMove = "Nothing to move right now.";
        public const string AskTitle = "Run the policy now?";
        public const string AskRest = " move to the Recycle Bin. You can restore them from there.";
        public const string AskYes = "Run now";
        public const string Moved = " moved to the Recycle Bin.";

        /// <summary>CATS.</summary>
        public static readonly string[] Cats = { "Recent", "Sentry", "Saved", "Other" };

        /// <summary>The field's label: "Keep Recent (days)".</summary>
        public static string KeepLabel(string cat)
        {
            return KeepWord + cat + DaysWord;
        }

        // the page's state: the vehicles and what each policy answered (or its failure)
        public readonly List<HzCar> Cars = new List<HzCar>();
        readonly List<JsonValue> answers = new List<JsonValue>();
        readonly List<string> errors = new List<string>();
        readonly Dictionary<int, HzPolicyForm> forms = new Dictionary<int, HzPolicyForm>();

        /// <summary>draw()'s requests: every vehicle's policy at once (a failure is that card's message). The forms start again from the answers.</summary>
        public async Task Load(IJsonApi api, IList<HzCar> cars, CancellationToken ct)
        {
            List<HzCar> list = new List<HzCar>(cars ?? new HzCar[0]);
            List<Task<JsonValue>> asks = new List<Task<JsonValue>>();
            foreach (HzCar v in list) asks.Add(api.Get(HzPolicies.Path(v.Id), ct));
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

        /// <summary>The form of the vehicle with this id (null when it has none).</summary>
        public HzPolicyForm FormOf(string vehicleId, HzCar current, bool h24)
        {
            foreach (FxPoliciesCard c in View(current, h24).Cards) if (c.Car.Id == vehicleId) return c.Form;
            return null;
        }

        static string Value(JsonValue owner, string key, string fallback)
        {
            if (Hz.Missing(owner, key) || owner[key].IsNull) return fallback;
            return Hz.Js(owner[key]);
        }

        /// <summary>draw(): the cards. A policy that is not there (undefined or null) throws, as p.enabled does.</summary>
        public FxPoliciesView View(HzCar current, bool h24)
        {
            FxPoliciesView view = new FxPoliciesView();
            if (Cars.Count == 0) { view.NoVehicles = true; return view; }
            for (int i = 0; i < Cars.Count; i++)
            {
                HzCar v = Cars[i];
                FxPoliciesCard c = new FxPoliciesCard();
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
                c.Current = current != null && v.Id == current.Id;
                c.On = p["enabled"].Bool;
                for (int k = 0; k < Cats.Length; k++) c.KeepAttr[k] = Value(p["keepDays"], Cats[k], "");
                c.RunAtAttr = p["runAt"].Bool ? Hz.Js(p["runAt"]) : DefaultRunAt;
                c.Amber = pv["recordings"].Bool;
                c.Note = c.Amber
                    ? new Rich().Text(NextMoves).Bold(Fx.Plural(pv["recordings"], "recording")).Text(" (").Bold(Fx.Bytes(pv["bytes"])).Text(")" + ToBin)
                    : new Rich().Text(NothingNow);
                if (p["lastRunAt"].Bool)
                {
                    JsonValue r = p["lastResult"];
                    string what = r["error"].Bool ? Hz.Js(r["error"]) : Value(r, "moved", "0") + " moved, " + Value(r, "skipped", "0") + " skipped";
                    c.LastRun = LastRunWord + Fx.When(p["lastRunAt"], h24) + ": " + what + ".";
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
            return view;
        }

        /// <summary>
        /// The page as test/windowsNativeFluxPolicies.test.js reads the web's:
        /// [head] Policies and the lead; per vehicle [card] name [bad]
        /// message, or [pol( current)] name [chip( green)] [check enabled( on)]
        /// Run this policy every day, the four Keep … (days) [input
        /// keep-Kind=value], Run at [input runAt=value], [note( amber)] its
        /// lines, [btn] Run now… [btn primary] Save policy; or [head] Policies
        /// [empty] Add a vehicle first. [link] Add a vehicle.
        /// </summary>
        public static List<string> Outline(FxPoliciesView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head]"); t(Title);
            if (v.NoVehicles)
            {
                o.Add("[empty]"); t(NoVehicle);
                o.Add("[link " + AddHref + "]"); t(AddVehicle);
                return o;
            }
            t(Lead);
            foreach (FxPoliciesCard c in v.Cards)
            {
                if (c.Error != null)
                {
                    o.Add("[card]"); t(c.Car.Name); o.Add("[bad]"); t(c.Error);
                    continue;
                }
                o.Add(c.Current ? "[pol current]" : "[pol]");
                t(c.Car.Name);
                o.Add(c.On ? "[chip green]" : "[chip]"); t(c.On ? RunsDaily : Off);
                o.Add(c.Form.Enabled ? "[check enabled on]" : "[check enabled]"); t(EnabledLabel);
                for (int k = 0; k < Cats.Length; k++)
                {
                    t(KeepLabel(Cats[k]));
                    o.Add("[input keep-" + Cats[k] + "=" + c.Form.Keep[k] + "]");
                }
                t(RunAtLabel);
                o.Add("[input runAt=" + c.Form.RunAt + "]");
                o.Add(c.Amber ? "[note amber]" : "[note]");
                foreach (RichRun r in c.Note.Runs) t(r.Text);
                if (c.LastRun != null) t(c.LastRun);
                o.Add("[btn]"); t(RunLabel);
                o.Add("[btn primary]"); t(SaveLabel);
            }
            return o;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>Save policy (the form's submit, after the browser's own checks): PUT the form, "Policy saved.", the page again. A failure is a red toast (busy()).</summary>
        public static Task Save(IJsonApi api, HzUi ui, string vehicleId, HzPolicyForm form, Func<Task> draw)
        {
            JsonValue body = form.Body();
            return ui.Act(async () =>
            {
                await api.Put(HzPolicies.Path(vehicleId), body, CancellationToken.None);
                ui.Toast(Saved, "ok");
                await draw();
            });
        }

        public static HzAsk RunAsk(JsonValue preview)
        {
            HzAsk a = new HzAsk();
            a.Title = AskTitle;
            a.Para("", new Rich().Text(Fx.Plural(preview["recordings"], "recording") + " (").Bold(Fx.Bytes(preview["bytes"])).Text(")" + AskRest));
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
                await api.Put(HzPolicies.Path(vehicleId), body, CancellationToken.None);
                JsonValue d = await api.Get(HzPolicies.Path(vehicleId), CancellationToken.None);
                if (!d["preview"]["recordings"].Bool)
                {
                    ui.Toast(NothingToMove, "ok");
                    await draw();
                    return;
                }
                if (!await ui.Ask(RunAsk(d["preview"]))) return;
                JsonValue o = await api.Post(HzPolicies.Path(vehicleId) + "/run", JsonValue.Object(), CancellationToken.None);
                ui.Toast(Fx.Plural(MovedCount(o), "recording") + Moved, "ok");
                await draw();
            });
        }

        /// <summary>onTopics: inventory or vehicles redraw the page (unless the reader is in it).</summary>
        public static bool Redraws(ICollection<string> topics)
        {
            return HzPolicies.Redraws(topics);
        }
    }
}
