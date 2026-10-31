using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>The Policies page's card, as web/app.js pages.policies writes it from GET /api/vehicles/:id/policy.</summary>
    public sealed class PolicyView
    {
        public string VehicleId;
        /// <summary>"Retention for Model 3".</summary>
        public string Title;
        /// <summary>Run this policy every day.</summary>
        public bool Enabled;
        /// <summary>The four number fields' values (Recent, Sentry, Saved, Other): the days, or "" for forever.</summary>
        public string[] Keep = new string[4];
        /// <summary>Run at: the time field's value ("03:30"; "" when the server has none).</summary>
        public string RunAt;
        /// <summary>The note is amber: the next run would move something.</summary>
        public bool Amber;
        /// <summary>The note's first line.</summary>
        public string Preview;
        /// <summary>The note's second line ("Last run …: 3 moved, 0 skipped."); null before the first run.</summary>
        public string LastRun;
    }

    /// <summary>What the form holds when it is read (app.js read()): the values as the fields give them.</summary>
    public sealed class PolicyForm
    {
        public bool Enabled;
        /// <summary>The time field's value: "HH:MM", or "" while it is empty or incomplete.</summary>
        public string RunAt = "";
        /// <summary>The number fields' values (Recent, Sentry, Saved, Other): the text typed when it is a number, else "".</summary>
        public string[] Keep = { "", "", "", "" };

        /// <summary>{ enabled, runAt, keepDays: { Recent: "30" | null, ... } }: the strings as typed, an empty one null.</summary>
        public JsonValue Body()
        {
            JsonValue keep = JsonValue.Object();
            for (int i = 0; i < Policies.Categories.Length; i++)
            {
                string s = Keep != null && i < Keep.Length ? Keep[i] : null;
                keep = keep.With(Policies.Categories[i], string.IsNullOrEmpty(s) ? null : s);
            }
            return JsonValue.Object("enabled", Enabled, "runAt", RunAt ?? "", "keepDays", keep);
        }
    }

    /// <summary>"Run the policy now?": its title, paragraph and button.</summary>
    public sealed class PoliciesConfirm
    {
        public string Title, Text, Ok;
    }

    /// <summary>What the page's buttons need from the window.</summary>
    public sealed class PoliciesUi
    {
        /// <summary>toast(message, bad).</summary>
        public Action<string, bool> Toast = delegate { };
        /// <summary>confirmDialog(): true when the reader confirms.</summary>
        public Func<PoliciesConfirm, Task<bool>> Confirm = c => Task.FromResult(false);
        /// <summary>render(): the page draws again.</summary>
        public Action Render = delegate { };
    }

    /// <summary>
    /// web/app.js pages.policies: the retention policy of the vehicle shown
    /// (days to keep each category, the daily run and its time, what the next
    /// run would move, the last run) and its two buttons, Save policy and
    /// Run now. The words, requests and toasts are the web's; the browser's
    /// own checks of its number and time fields (Chrome's) are here too, so
    /// the native fields behave as the web's.
    /// </summary>
    public static class Policies
    {
        public const string Eyebrow = "Your archive workspace";
        public const string PageTitle = "Policies";
        public const string NoVehicle = "Add a vehicle in Settings first.";
        public const string TitleBefore = "Retention for ";
        public const string Lead = "Recordings older than the number of days below are moved to the Recycle Bin once a day. Nothing is ever deleted by a policy, and protected recordings are always skipped. Leave a field empty to keep that category forever.";
        public const string EnabledLabel = "Run this policy every day";
        public static readonly string[] Categories = { "Recent", "Sentry", "Saved", "Other" };
        public const string KeepPlaceholder = "forever";
        public const int KeepMin = 1;
        public const int KeepMax = 3650;
        public const string RunAtLabel = "Run at (server time)";
        public const string NothingNow = "With these settings, nothing would be moved right now.";
        public const string RunLabel = "Run now";
        public const string SaveLabel = "Save policy";
        public const string Saved = "Policy saved.";
        public const string NothingToMove = "Nothing to move right now.";
        public const string ConfirmTitle = "Run the policy now?";

        /// <summary>"Keep Recent clips (days)".</summary>
        public static string KeepLabel(string category)
        {
            return "Keep " + category + " clips (days)";
        }

        /// <summary>The name of the number field (name="keep-Recent").</summary>
        public static string KeepName(string category)
        {
            return "keep-" + category;
        }

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>`${x}` of a value (null and missing are what ?? replaces, so they never reach here).</summary>
        static string Js(JsonValue v)
        {
            if (v == null || v.IsNull) return "undefined";
            if (v.IsNumber) return Format.JsNum(v.Num);
            if (v.IsString) return v.Str;
            if (v.IsBool) return v.Bool ? "true" : "false";
            if (v.IsArray)
            {
                List<string> parts = new List<string>();
                foreach (JsonValue x in v.Items) parts.Add(x.IsNull ? "" : Js(x));
                return string.Join(",", parts);
            }
            return "[object Object]";
        }

        /// <summary>`${x ?? fallback}`.</summary>
        static string Or(JsonValue v, string fallback)
        {
            return v == null || v.IsNull ? fallback : Js(v);
        }

        /// <summary>esc(x): null is "".</summary>
        static string S(JsonValue v)
        {
            return v == null || v.IsNull ? "" : Js(v);
        }

        /// <summary>The policy's address: /api/vehicles/:id/policy (the id as the web writes it, not encoded).</summary>
        public static string Path(string vehicleId)
        {
            return "/api/vehicles/" + (vehicleId ?? "") + "/policy";
        }

        // ---------------------------------------------------------------- the page

        /// <summary>GET the policy; a failure throws (the whole page is the error).</summary>
        public static Task<JsonValue> Load(IJsonApi api, string vehicleId, CancellationToken ct)
        {
            return api.Get(Path(vehicleId), ct);
        }

        /// <summary>"7 recordings (3.2 GiB)".</summary>
        static string Moves(JsonValue preview)
        {
            return Format.Plural(preview["recordings"].Num, "recording") + " (" + Format.Bytes(preview["bytes"]) + ")";
        }

        public static PolicyView View(JsonValue answer, string vehicleId, string vehicleName, bool h24)
        {
            JsonValue policy = answer["policy"];
            JsonValue preview = answer["preview"];
            PolicyView v = new PolicyView();
            v.VehicleId = vehicleId;
            v.Title = TitleBefore + (vehicleName ?? "");
            v.Enabled = policy["enabled"].Bool;
            for (int i = 0; i < Categories.Length; i++) v.Keep[i] = Or(policy["keepDays"][Categories[i]], "");
            v.RunAt = S(policy["runAt"]);
            v.Amber = preview["recordings"].Bool;
            v.Preview = v.Amber ? "With these settings, the next run moves " + Moves(preview) + " to the Recycle Bin." : NothingNow;
            if (policy["lastRunAt"].Bool)
            {
                JsonValue r = policy["lastResult"];
                string what = r["error"].Bool ? S(r["error"]) : Or(r["moved"], "0") + " moved, " + Or(r["skipped"], "0") + " skipped";
                v.LastRun = "Last run " + Format.When(policy["lastRunAt"], h24) + ": " + what + ".";
            }
            return v;
        }

        /// <summary>
        /// The page as a reader sees it, in the markers test/windowsNativePolicies.test.js
        /// writes for the web's HTML: [head eyebrow | title] [pills] [empty] [card] [h2]
        /// [check( on)] [input name type | placeholder | value] [note( amber)] [btn( solid)].
        /// </summary>
        public static List<string> Outline(PolicyView v)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = (s ?? "").Trim(); if (x.Length > 0) o.Add(x); };
            o.Add("[head " + Eyebrow + " | " + PageTitle + "]");
            o.Add("[pills]");
            if (v == null)
            {
                o.Add("[empty]"); t(NoVehicle);
                return o;
            }
            o.Add("[card]"); o.Add("[h2]"); t(v.Title); t(Lead);
            o.Add(v.Enabled ? "[check on]" : "[check]"); t(EnabledLabel);
            for (int i = 0; i < Categories.Length; i++)
            {
                t(KeepLabel(Categories[i]));
                o.Add("[input " + KeepName(Categories[i]) + " number | " + KeepPlaceholder + " | " + v.Keep[i] + "]");
            }
            t(RunAtLabel);
            o.Add("[input runAt time |  | " + v.RunAt + "]");
            o.Add(v.Amber ? "[note amber]" : "[note]");
            t(v.Preview);
            if (v.LastRun != null) t(v.LastRun);
            o.Add("[btn]"); t(RunLabel);
            o.Add("[btn solid]"); t(SaveLabel);
            return o;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>Save policy (the form's submit): PUT, "Policy saved." and the page again; a failure is a red toast.</summary>
        public static async Task Save(IJsonApi api, PoliciesUi ui, string vehicleId, PolicyForm form)
        {
            try
            {
                await api.Put(Path(vehicleId), form.Body(), CancellationToken.None);
                ui.Toast(Saved, false);
                ui.Render();
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
        }

        public static PoliciesConfirm RunConfirm(JsonValue preview)
        {
            PoliciesConfirm c = new PoliciesConfirm();
            c.Title = ConfirmTitle;
            c.Text = Moves(preview) + " will move to the Recycle Bin. You can restore them from there.";
            c.Ok = RunLabel;
            return c;
        }

        /// <summary>
        /// Run now: saves the form (PUT), asks what the run would move (GET),
        /// says so when nothing, else asks "Run the policy now?" and runs it
        /// (POST …/run), then "N recordings moved to the Recycle Bin." and the
        /// page again. Any failure is a red toast.
        /// </summary>
        public static async Task RunNow(IJsonApi api, PoliciesUi ui, string vehicleId, PolicyForm form)
        {
            try
            {
                await api.Put(Path(vehicleId), form.Body(), CancellationToken.None);
                JsonValue p = await api.Get(Path(vehicleId), CancellationToken.None);
                if (!p["preview"]["recordings"].Bool)
                {
                    ui.Toast(NothingToMove, false);
                    return;
                }
                if (!await ui.Confirm(RunConfirm(p["preview"]))) return;
                JsonValue o = await api.Post(Path(vehicleId) + "/run", JsonValue.Object(), CancellationToken.None);
                ui.Toast(Format.Plural(o["moved"].Count, "recording") + " moved to the Recycle Bin.", false);
                ui.Render();
            }
            catch (Exception ex)
            {
                ui.Toast(ex.Message, true);
            }
        }

        // ---------------------------------------------------------------- the browser's fields

        // HTML's "valid floating-point number" (what <input type=number> keeps as its value)
        static readonly Regex FloatText = new Regex(@"^-?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant);

        static bool TryNumber(string s, out double d)
        {
            d = 0;
            if (string.IsNullOrEmpty(s) || !FloatText.IsMatch(s)) return false;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }

        /// <summary>
        /// A number field's value (input.value, what FormData reads): the text
        /// as typed when it is a number ("30", "030", "1e2"), else "".
        /// </summary>
        public static string NumberValue(string typed)
        {
            double d;
            return TryNumber(typed, out d) ? typed : "";
        }

        /// <summary>What a number field takes from the keyboard (Chrome: digits, + - . e E).</summary>
        public static bool NumberChar(char c)
        {
            return (c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.' || c == 'e' || c == 'E';
        }

        static string N(double d)
        {
            return Format.JsNum(d);
        }

        /// <summary>
        /// Chrome's message for a number field (min 1, max 3650, step 1) when
        /// the form is sent, null when it is fine: "Please enter a number."
        /// for text that is not one, then the range, then the step.
        /// </summary>
        public static string NumberMessage(string typed, double min, double max)
        {
            if (string.IsNullOrEmpty(typed)) return null;
            double d;
            if (!TryNumber(typed, out d)) return "Please enter a number.";
            if (d < min) return "Value must be greater than or equal to " + N(min) + ".";
            if (d > max) return "Value must be less than or equal to " + N(max) + ".";
            // step 1 from min
            double below = Math.Floor(d - min) + min;
            if (below != d)
            {
                double above = below + 1;
                if (above > max) return "Please enter a valid value. The nearest valid value is " + N(below) + ".";
                return "Please enter a valid value. The two nearest valid values are " + N(below) + " and " + N(above) + ".";
            }
            return null;
        }

        /// <summary>
        /// ArrowUp (+1) or ArrowDown (-1) in a number field: an empty or
        /// unreadable one starts at min, the value moves one step and stays in
        /// min..max; a value below min goes to min going up, one above max to
        /// max going down. Returns the new text (unchanged when it cannot move).
        /// </summary>
        public static string NumberStep(string typed, int dir, double min, double max)
        {
            double d;
            if (!TryNumber(typed, out d)) return N(min);
            double next;
            if (dir > 0)
            {
                if (d < min) next = min;
                else
                {
                    double f = Math.Floor(d - min) + min;
                    next = f == d ? d + 1 : f + 1;
                }
                if (next > max) return d >= max ? typed : N(max);
            }
            else
            {
                if (d > max) next = max;
                else
                {
                    double c = Math.Ceiling(d - min) + min;
                    next = c == d ? d - 1 : c - 1;
                }
                if (next < min) return d <= min ? typed : N(min);
            }
            return N(next);
        }

        /// <summary>Chrome's message for a time field that is partly filled in.</summary>
        public const string TimeIncomplete = "Please enter a valid value. The field is incomplete or has an invalid date.";

        static readonly Regex TimeText = new Regex(@"^([01][0-9]|2[0-3]):([0-5][0-9])(?::([0-5][0-9])(?:\.[0-9]{1,3})?)?$", RegexOptions.CultureInvariant);

        /// <summary>
        /// A time field's starting value: the hour and minute of "HH:MM" (or
        /// "HH:MM:SS"), false for anything else (the field starts empty, as
        /// the browser drops a value it cannot read).
        /// </summary>
        public static bool ParseTime(string value, out int hour, out int minute)
        {
            hour = -1;
            minute = -1;
            Match m = TimeText.Match(value ?? "");
            if (!m.Success) return false;
            hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>The time field's value from its parts: "HH:MM", "" while a part is empty (-1).</summary>
        public static string TimeValue(int hour, int minute)
        {
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return "";
            return hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
