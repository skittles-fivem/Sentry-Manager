using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    // web/vehicleEncoding.js, written again in C#: each vehicle's saved
    // encoding settings in plain words, and the "Encoding per vehicle" view
    // with its editor (what the browser shows, what the form holds, what
    // Save and Forget send). One copy for every interface: the built-in page
    // and the packs' pages draw what this works out. Values are read as the
    // web reads them (JavaScript's String(), Number(), ===, ||), so an odd
    // answer gives the same words. test/windowsNativeVehicleEncoding.test.js
    // runs the web's own file on the same inputs and writes
    // Core.Tests/Fixtures/expected-vehicle-encoding.json; VehicleEncodingTests
    // compares.

    /// <summary>One radio button of the editor: its name, value, words and state (the DOM's checked and disabled).</summary>
    public sealed class VeRadio
    {
        public string Name;
        public string Value;
        public string Text;
        public bool Checked;
        public bool Disabled;
    }

    /// <summary>A select: its options (value, words) and the chosen one (the first when none is marked selected, as a browser shows it).</summary>
    public sealed class VeSelect
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Options = new List<KeyValuePair<string, string>>();
        /// <summary>The options marked selected in the web's HTML (the outline shows them).</summary>
        public readonly List<bool> Marked = new List<bool>();
        public int Index;

        public string Value { get { return Index >= 0 && Index < Options.Count ? Options[Index].Key : null; } }

        public void Pick(string value)
        {
            for (int i = 0; i < Options.Count; i++) if (Options[i].Key == value) { Index = i; return; }
        }

        internal void Add(string value, string text, bool selected)
        {
            Options.Add(new KeyValuePair<string, string>(value, text));
            Marked.Add(selected);
        }

        internal void Settle()
        {
            Index = 0;
            for (int i = 0; i < Marked.Count; i++) if (Marked[i]) Index = i; // (the last selected one wins, as in a browser)
        }
    }

    /// <summary>One row of "Clip groups and order": the group, its words, ticked or not.</summary>
    public sealed class VeGroup
    {
        public string Id;
        public string Text;
        public bool Checked;
    }

    /// <summary>
    /// The editor of one vehicle (vehicleEncoding.js editor()), as the
    /// browser holds it: every control's value and state, which parts are
    /// shown (sync()), the error line. The interface draws its controls from
    /// this and writes back what the reader changes; <see cref="Sync"/> runs
    /// after every change, as the form's change event does.
    /// </summary>
    public sealed class VeForm
    {
        public string CarId;
        public string Name;
        public readonly List<VeRadio> Codec = new List<VeRadio>();
        public readonly VeSelect Profile = new VeSelect { Name = "profileId" };
        public readonly VeSelect Quality = new VeSelect { Name = "qualityLevel" };
        /// <summary>Bitrate (Mbps): the number field's value ("" when it is not a number).</summary>
        public string Bitrate;
        public bool KeepOffTarget;
        public readonly List<VeRadio> Output = new List<VeRadio>();
        /// <summary>"This server can’t replace originals on this share." is shown.</summary>
        public bool ReplaceUnavailable;
        public string Destination;
        public string DestinationPlaceholder;
        public bool ReplacementConfirmed;
        public readonly List<VeRadio> Verify = new List<VeRadio>();
        public readonly List<VeGroup> Groups = new List<VeGroup>();
        /// <summary>The saved config has devices (lanes): their words instead of "Clips at once".</summary>
        public bool Lanes;
        public string DevicesText;
        /// <summary>Clips at once (no lanes): the number field's value.</summary>
        public string Concurrency;
        public bool Automatic;
        public string AutomaticLabel;
        public readonly VeSelect Interval = new VeSelect { Name = "intervalMinutes" };
        public readonly VeSelect Window = new VeSelect { Name = "window" };
        public readonly VeSelect StartHour = new VeSelect { Name = "startHour" };
        public readonly VeSelect EndHour = new VeSelect { Name = "endHour" };
        /// <summary>"Forget these settings" (saved, automatic off).</summary>
        public bool CanForget;

        // sync(): what is shown
        public bool ShowQuality, ShowCustom, ShowTarget, ShowReplace, ShowSeparate, ShowAutomatic, ShowHours;
        /// <summary>The error line under the form (null: hidden).</summary>
        public string Error;
        /// <summary>Save is disabled while the settings are sent.</summary>
        public bool Saving;

        static string Picked(List<VeRadio> list)
        {
            foreach (VeRadio r in list) if (r.Checked && !r.Disabled) return r.Value;
            return null;
        }

        /// <summary>vehicleEncoding.js sync(): the parts for the chosen size, output, automatic and window; Off is not offered while replacing (Quick instead).</summary>
        public void Sync()
        {
            string profile = Profile.Value;
            ShowQuality = profile == "quality";
            ShowCustom = profile == "custom";
            ShowTarget = profile != "custom" && profile != "quality";
            bool replace = Picked(Output) == "replace";
            ShowReplace = replace;
            ShowSeparate = !replace;
            VeRadio off = null, quick = null;
            foreach (VeRadio r in Verify) { if (r.Value == "off") off = r; if (r.Value == "quick") quick = r; }
            if (off != null)
            {
                off.Disabled = replace;
                if (replace && off.Checked)
                {
                    foreach (VeRadio r in Verify) r.Checked = false;
                    if (quick != null) quick.Checked = true;
                }
            }
            ShowAutomatic = Automatic;
            ShowHours = Window.Value == "hours";
        }

        /// <summary>The reader picks a radio button (name, value): the others of that name are unticked.</summary>
        public void Check(string name, string value)
        {
            List<VeRadio> list = name == "codec" ? Codec : name == "outputMode" ? Output : name == "verifyMode" ? Verify : null;
            if (list == null) return;
            VeRadio hit = null;
            foreach (VeRadio r in list) if (r.Value == value) hit = r;
            if (hit == null || hit.Disabled) return;
            foreach (VeRadio r in list) r.Checked = r == hit;
        }

        /// <summary>The reader ticks or unticks a checkbox (group: its value is the group).</summary>
        public void Tick(string name, string value, bool on)
        {
            switch (name)
            {
                case "keepOffTarget": KeepOffTarget = on; break;
                case "replacementConfirmed": ReplacementConfirmed = on; break;
                case "automatic": Automatic = on; break;
                case "group":
                    foreach (VeGroup g in Groups) if (g.Id == value) g.Checked = on;
                    break;
            }
        }

        /// <summary>The reader types in a field or picks in a select.</summary>
        public void Set(string name, string value)
        {
            switch (name)
            {
                case "bitrate": Bitrate = Policies.NumberValue(value ?? ""); break;
                case "destinationRoot": Destination = value ?? ""; break;
                case "concurrency": if (!Lanes) Concurrency = Policies.NumberValue(value ?? ""); break;
                case "profileId": Profile.Pick(value); break;
                case "qualityLevel": Quality.Pick(value); break;
                case "intervalMinutes": Interval.Pick(value); break;
                case "window": Window.Pick(value); break;
                case "startHour": StartHour.Pick(value); break;
                case "endHour": EndHour.Pick(value); break;
            }
        }

        /// <summary>new FormData(form): what the form sends, as (name, value) in the form's order (disabled and unticked controls left out).</summary>
        public List<KeyValuePair<string, string>> Data()
        {
            List<KeyValuePair<string, string>> d = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, v) => d.Add(new KeyValuePair<string, string>(k, v));
            foreach (VeRadio r in Codec) if (r.Checked && !r.Disabled) add(r.Name, r.Value);
            add(Profile.Name, Profile.Value);
            add(Quality.Name, Quality.Value);
            add("bitrate", Bitrate ?? "");
            if (KeepOffTarget) add("keepOffTarget", "on");
            foreach (VeRadio r in Output) if (r.Checked && !r.Disabled) add(r.Name, r.Value);
            add("destinationRoot", Destination ?? "");
            if (ReplacementConfirmed) add("replacementConfirmed", "on");
            foreach (VeRadio r in Verify) if (r.Checked && !r.Disabled) add(r.Name, r.Value);
            foreach (VeGroup g in Groups) if (g.Checked) add("group", g.Id);
            if (!Lanes) add("concurrency", Concurrency ?? "");
            if (Automatic) add("automatic", "on");
            add(Interval.Name, Interval.Value);
            add(Window.Name, Window.Value);
            add(StartHour.Name, StartHour.Value);
            add(EndHour.Name, EndHour.Value);
            return d;
        }
    }

    /// <summary>One vehicle's card: its name and tag, its settings in words (or "Not set up yet") and its button, or the editor.</summary>
    public sealed class VeCard
    {
        public string Id;
        /// <summary>esc(v.name): the name as drawn.</summary>
        public string Name;
        /// <summary>"on" (Automatic on, green), "saved", "none" or "editing".</summary>
        public string Tag;
        public string TagText;
        /// <summary>The settings in words (describe()); null when not set up.</summary>
        public List<KeyValuePair<string, string>> Rows;
        /// <summary>"Not set up yet — …" when there are no rows.</summary>
        public string Empty;
        /// <summary>"Edit" or "Set up".</summary>
        public string Button;
        /// <summary>The editor, when this vehicle is being edited (then there are no rows and no button).</summary>
        public VeForm Editor;
    }

    /// <summary>What Save and Forget need from the window.</summary>
    public sealed class VeUi
    {
        /// <summary>opts.confirm(text): the built-in interface asks "Replace originals?" (for Forget too, as the web's does).</summary>
        public Func<string, Task<bool>> Confirm = t => Task.FromResult(false);
        /// <summary>toast(text, bad).</summary>
        public Action<string, bool> Toast = delegate { };
        /// <summary>draw(): the cards again from the view's state (no reload).</summary>
        public Action Draw = delegate { };
        /// <summary>The editor's error line or the Save button changed (fail(), button.disabled): show the form's state.</summary>
        public Action Changed = delegate { };
    }

    /// <summary>
    /// web/vehicleEncoding.js: the constants, the plain words (describe,
    /// summary), where a vehicle's settings are (savedFor, folderOf), the
    /// defaults, the payloads (shared, configOf, save) and the view
    /// (<see cref="VehicleEncodingView"/>).
    /// </summary>
    public static class VehicleEncoding
    {
        public static readonly string[,] Codecs = { { "h265", "H.265" }, { "av1", "AV1" } };
        /// <summary>[id, name, percent smaller]</summary>
        public static readonly string[,] SizeProfiles = { { "conservative", "High quality", "5" }, { "balanced", "Balanced", "20" }, { "aggressive", "Smaller", "50" } };
        public static readonly string[,] QualityLevels = { { "high", "High — keeps the most detail" }, { "balanced", "Balanced" }, { "small", "Small — smallest files" } };
        /// <summary>[id, name, what it does]</summary>
        public static readonly string[,] VerifyModes = { { "quick", "Quick", "checks streams, length, frames and telemetry" }, { "full", "Full", "also decodes every frame (slowest)" }, { "off", "Off", "no checks (fastest)" } };
        public static readonly string[,] GroupNames = { { "RecentClips", "Recent drives" }, { "SavedClips", "Saved clips" }, { "SentryClips", "Sentry events" } };
        public static readonly string[] DefaultOrder = { "SavedClips", "SentryClips", "RecentClips" };
        public static readonly int[] IntervalMinutes = { 15, 30, 60, 180, 360, 720, 1440 };
        public static readonly string[] IntervalWords = { "every 15 minutes", "every 30 minutes", "every hour", "every 3 hours", "every 6 hours", "every 12 hours", "once a day" };

        // the page's words (vehicleEncoding.js, character for character)
        public const string Eyebrow = "Encoder";
        public const string PageTitle = "Encoding per vehicle";
        public const string EncoderTab = "Encoder";
        public const string Intro = "What each vehicle converts with. A vehicle with automatic conversion on uses these for its automatic runs; in the Encoder, “Load these” fills the form with them for a run you start.";
        public const string OpenEncoder = "Open the Encoder";
        public const string NoVehicles = "Add a vehicle in Settings first.";
        public const string TagOn = "Automatic on";
        public const string TagSaved = "Saved · automatic off";
        public const string TagNone = "Not set up";
        public const string TagEditing = "Editing";
        public const string NotSetUp = "Not set up yet — uses the Encoder’s settings when you start a run.";
        public const string EditLabel = "Edit";
        public const string SetUpLabel = "Set up";
        public const string LegendCodec = "Codec";
        public const string LegendSize = "Size";
        public const string SizeLabel = "Size or quality";
        public const string QualityOption = "Quality level — size varies by scene";
        public const string CustomOption = "Custom bitrate";
        public const string QualityLabel = "Quality level";
        public const string BitrateLabel = "Bitrate (Mbps)";
        public const string KeepLabel = "Keep clips that miss the size target (otherwise the original stays)";
        public const string LegendOutput = "Output";
        public const string ReplaceLabel = "Replace originals";
        public const string SeparateLabel = "Separate folder";
        public const string ReplaceHint = "This server can’t replace originals on this share.";
        public const string FolderLabel = "Separate folder (on a vehicle share or an output folder)";
        public const string ConfirmLabel = "I understand the originals are replaced (each is backed up until its converted copy passes the checks)";
        public const string LegendVerify = "Verify";
        public const string VerifyHint = "Replacing originals always uses at least Quick.";
        public const string LegendGroups = "Clip groups and order";
        public const string Earlier = "Earlier";
        public const string Later = "Later";
        public const string OrderHint = "Converted in this order.";
        public const string LegendDevices = "Devices";
        public const string LanesHint = "Which GPUs, CPU and nodes convert, and how many clips each converts at once, are set in the Encoder under Parallel conversions. Saving here keeps them.";
        public const string ConcurrencyLabel = "Clips at once";
        public const string ConcurrencyHint = "On this server’s default device. To use several GPUs or nodes, set them in the Encoder under Parallel conversions.";
        public const string LegendAutomatic = "Automatic conversion";
        public const string HowOften = "How often";
        public const string When = "When";
        public const string AnyTime = "Any time";
        public const string OffPeak = "Only when electricity is off-peak";
        public const string SetHours = "Only between set hours";
        public const string From = "From";
        public const string To = "To";
        public const string SharedHint = "How often and when are shared by every vehicle that converts automatically.";
        public const string SaveLabel = "Save";
        public const string CancelLabel = "Cancel";
        public const string ForgetLabel = "Forget these settings";
        public const string NeedFolder = "Enter the separate folder, or choose Replace originals.";
        public const string VehiclesFailed = "Can’t load the vehicles.";
        /// <summary>The built-in interface's confirm dialog (app.js: confirmDialog({ title, body, ok, danger })).</summary>
        public const string ConfirmTitle = "Replace originals?";
        public const string ConfirmOk = "Replace originals";

        // ---------------------------------------------------------------- JavaScript's values

        /// <summary>String(x): "undefined" for a missing value (pass null), "null", numbers as JavaScript prints them, arrays joined with ",".</summary>
        public static string JsString(JsonValue v)
        {
            if (v == null) return "undefined";
            switch (v.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return v.Bool ? "true" : "false";
                case JsonKind.Number: return Format.JsNum(v.Num);
                case JsonKind.String: return v.Str;
                case JsonKind.Array:
                    {
                        List<string> parts = new List<string>();
                        foreach (JsonValue x in v.Items) parts.Add(x.IsNull ? "" : JsString(x));
                        return string.Join(",", parts);
                    }
                default: return "[object Object]";
            }
        }

        /// <summary>owner[key] as a template literal writes it ("undefined" when missing).</summary>
        public static string Tpl(JsonValue owner, string key)
        {
            return JsString(Get(owner, key));
        }

        /// <summary>owner[key], null when there is no such key (undefined).</summary>
        public static JsonValue Get(JsonValue owner, string key)
        {
            return owner != null && owner.Has(key) ? owner[key] : null;
        }

        static bool Truthy(JsonValue v)
        {
            return v != null && v.Bool;
        }

        /// <summary>esc(x): String(x ?? '') (undefined and null are "").</summary>
        public static string EscText(JsonValue v)
        {
            return v == null || v.IsNull ? "" : JsString(v);
        }

        /// <summary>Number(x) as JavaScript reads it (NaN for undefined, 0 for null and "").</summary>
        public static double JsNumber(JsonValue v)
        {
            if (v == null) return double.NaN;
            switch (v.Kind)
            {
                case JsonKind.Null: return 0;
                case JsonKind.Bool: return v.Bool ? 1 : 0;
                case JsonKind.Number: return v.Num;
                case JsonKind.String: return StringNumber(v.Str);
                case JsonKind.Array:
                    if (v.Count == 0) return 0;
                    if (v.Count == 1) return StringNumber(JsString(v));
                    return double.NaN;
                default: return double.NaN;
            }
        }

        /// <summary>The characters String.prototype.trim() and Number() take off (white space and line ends).</summary>
        public static readonly char[] JsBlanks = { ' ', '\t', '\n', '\r', '\v', '\f', '\u00a0', '\ufeff', '\u2028', '\u2029', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200a', '\u202f', '\u205f', '\u3000' };

        /// <summary>Number("…"): blanks around, "" is 0, decimal, Infinity, 0x/0o/0b; anything else NaN.</summary>
        public static double StringNumber(string s)
        {
            if (s == null) return double.NaN;
            string t = s.Trim(JsBlanks);
            if (t.Length == 0) return 0;
            if (t.Length > 2 && t[0] == '0' && (t[1] == 'x' || t[1] == 'X' || t[1] == 'o' || t[1] == 'O' || t[1] == 'b' || t[1] == 'B'))
            {
                int radix = t[1] == 'x' || t[1] == 'X' ? 16 : t[1] == 'o' || t[1] == 'O' ? 8 : 2;
                double n = 0;
                for (int i = 2; i < t.Length; i++)
                {
                    int d = HexDigit(t[i]);
                    if (d < 0 || d >= radix) return double.NaN;
                    n = n * radix + d;
                }
                return n;
            }
            string body = t;
            if (body[0] == '+' || body[0] == '-') body = body.Substring(1);
            if (body == "Infinity") return t[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            if (!JsDecimal(body)) return double.NaN;
            double r;
            if (!double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out r)) return double.NaN;
            return r;
        }

        static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        // digits [. digits] [e[+-]digits], or . digits [e…]
        static bool JsDecimal(string s)
        {
            int i = 0, n = s.Length, digits = 0;
            while (i < n && char.IsDigit(s[i]) && s[i] < 128) { i++; digits++; }
            if (i < n && s[i] == '.') { i++; while (i < n && s[i] >= '0' && s[i] <= '9') { i++; digits++; } }
            if (digits == 0) return false;
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < n && (s[i] == '+' || s[i] == '-')) i++;
                int e = 0;
                while (i < n && s[i] >= '0' && s[i] <= '9') { i++; e++; }
                if (e == 0) return false;
            }
            return i == n;
        }

        /// <summary>a === b for two values from JSON (null for undefined).</summary>
        static bool Same(JsonValue a, JsonValue b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case JsonKind.Null: return true;
                case JsonKind.Bool: return a.Bool == b.Bool;
                case JsonKind.Number: return a.Num == b.Num;
                case JsonKind.String: return a.Str == b.Str;
                default: return ReferenceEquals(a, b);
            }
        }

        static bool IsStr(JsonValue v, string s)
        {
            return v != null && v.IsString && v.Str == s;
        }

        static bool IsTrue(JsonValue v)
        {
            return v != null && v.IsBool && v.Bool;
        }

        /// <summary>label(list, id): the words of the entry whose id === id (null when none).</summary>
        static string Label(string[,] list, JsonValue id)
        {
            if (id == null || !id.IsString) return null;
            for (int i = 0; i < list.GetLength(0); i++) if (list[i, 0] == id.Str) return list[i, 1];
            return null;
        }

        static string Label(string[,] list, string id)
        {
            return Label(list, id == null ? null : JsonValue.String(id));
        }

        static string IntervalLabel(JsonValue m)
        {
            if (m == null || !m.IsNumber) return null;
            for (int i = 0; i < IntervalMinutes.Length; i++) if (m.Num == IntervalMinutes[i]) return IntervalWords[i];
            return null;
        }

        /// <summary>hour(h): String(h).padStart(2, '0') + ":00".</summary>
        public static string Hour(string h)
        {
            string s = h ?? "undefined";
            return (s.Length < 2 ? new string('0', 2 - s.Length) + s : s) + ":00";
        }

        // ---------------------------------------------------------------- plain words

        public static string CodecWords(JsonValue config)
        {
            JsonValue codec = Get(config, "codec");
            string l = Label(Codecs, codec);
            if (l != null) return l;
            return Truthy(codec) ? JsString(codec) : "—";
        }

        public static string SizeWords(JsonValue config, bool shortForm)
        {
            JsonValue profile = Get(config, "profileId");
            if (IsStr(profile, "custom"))
            {
                string mbps = Format.JsNum(JsNumber(Get(config, "bitrateKbps")) / 1000);
                return shortForm ? mbps + " Mbps" : "Custom bitrate: " + mbps + " Mbps";
            }
            if (IsStr(profile, "quality"))
            {
                string level = Label(QualityLevels, Get(config, "qualityLevel")) ?? "Balanced";
                int cut = level.IndexOf(" — ", StringComparison.Ordinal);
                if (cut >= 0) level = level.Substring(0, cut);
                return shortForm ? "Quality " + level : "Quality level: " + level + " (size varies by scene)";
            }
            if (profile != null && profile.IsString)
            {
                for (int i = 0; i < SizeProfiles.GetLength(0); i++)
                    if (SizeProfiles[i, 0] == profile.Str) return shortForm ? SizeProfiles[i, 1] : SizeProfiles[i, 1] + " — about " + SizeProfiles[i, 2] + "% smaller";
            }
            return Truthy(profile) ? JsString(profile) : "—";
        }

        /// <summary>hasTarget(config): not custom and not quality.</summary>
        public static bool HasTarget(JsonValue config)
        {
            JsonValue p = Get(config, "profileId");
            return !IsStr(p, "custom") && !IsStr(p, "quality");
        }

        public static string OutputWords(JsonValue config, bool shortForm)
        {
            if (IsStr(Get(config, "outputMode"), "replace")) return shortForm ? "Replace" : "Replace originals (each original is backed up until its copy is checked)";
            JsonValue d = Get(config, "destinationRoot");
            return shortForm ? "Separate" : "Separate folder: " + (Truthy(d) ? JsString(d) : "—");
        }

        public static string VerifyWords(JsonValue config, bool shortForm)
        {
            JsonValue vm = Get(config, "verifyMode");
            string mode = IsStr(Get(config, "outputMode"), "replace") && IsStr(vm, "off") ? "quick" : (Truthy(vm) ? null : "full");
            int hit = 1;
            for (int i = 0; i < VerifyModes.GetLength(0); i++)
            {
                if (mode != null ? VerifyModes[i, 0] == mode : (vm.IsString && VerifyModes[i, 0] == vm.Str)) { hit = i; break; }
            }
            return shortForm ? VerifyModes[hit, 1] : VerifyModes[hit, 1] + " — " + VerifyModes[hit, 2];
        }

        /// <summary>The kinds of clip the value lists (new Set(x || [])): an array's strings, a string's characters.</summary>
        static HashSet<string> Included(JsonValue groups)
        {
            HashSet<string> set = new HashSet<string>();
            if (groups == null || !groups.Bool) return set;
            if (groups.IsArray) { foreach (JsonValue g in groups.Items) if (g.IsString) set.Add(g.Str); }
            else if (groups.IsString) foreach (char ch in groups.Str) set.Add(ch.ToString());
            return set;
        }

        /// <summary>The order the config keeps (exactly three entries), else DEFAULT_ORDER.</summary>
        static List<JsonValue> OrderOf(JsonValue config)
        {
            JsonValue o = Get(config, "groupOrder");
            List<JsonValue> list = new List<JsonValue>();
            if (o != null && o.IsArray && o.Count == 3) list.AddRange(o.Items);
            else foreach (string g in DefaultOrder) list.Add(JsonValue.String(g));
            return list;
        }

        public static string GroupsWords(JsonValue config)
        {
            HashSet<string> included = Included(Get(config, "includedGroups"));
            List<string> names = new List<string>();
            foreach (JsonValue g in OrderOf(config))
                if (g.IsString && included.Contains(g.Str)) names.Add(Label(GroupNames, g) ?? "");
            return names.Count > 0 ? string.Join(" → ", names) : "—";
        }

        public static string DeviceName(JsonValue id, JsonValue devices)
        {
            if (devices != null && devices.IsArray)
            {
                foreach (JsonValue d in devices.Items)
                {
                    if (!Truthy(d) || !Same(Get(d, "id"), id)) continue;
                    JsonValue name = Get(d, "name");
                    if (Truthy(name)) return JsString(name);
                    break;
                }
            }
            if (IsStr(id, "cpu")) return "CPU";
            string s = JsString(id);
            if (s.StartsWith("node:", StringComparison.Ordinal))
            {
                string rest = s.Substring(5);
                int at = rest.IndexOf('@');
                return "Node " + (at >= 0 ? rest.Substring(0, at) : rest);
            }
            return s;
        }

        public static string DevicesWords(JsonValue config, JsonValue devices)
        {
            JsonValue lanes = Get(config, "lanes");
            if (lanes != null && lanes.IsArray && lanes.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (JsonValue l in lanes.Items) parts.Add(DeviceName(Get(l, "device"), devices) + ": " + Tpl(l, "concurrency") + " at once");
                return string.Join(" · ", parts);
            }
            double n = JsNumber(Get(config, "concurrency"));
            if (double.IsNaN(n) || n == 0) n = 1;
            return "This server’s default device, " + Format.JsNum(n) + " clip" + (n == 1 ? "" : "s") + " at once";
        }

        public static string WindowWords(JsonValue settings)
        {
            JsonValue w = Get(settings, "window");
            if (!Truthy(w)) return "any time";
            if (IsStr(Get(w, "follow"), "off-peak")) return "only when electricity is off-peak";
            return "only from " + Hour(Tpl(w, "startHour")) + " to " + Hour(Tpl(w, "endHour"));
        }

        /// <summary>autoWords(entry, settings); entry null when there is none.</summary>
        public static string AutoWords(bool automatic, JsonValue settings)
        {
            if (!automatic) return "Off — converts only when you start a run in the Encoder";
            return "On — checks for new clips " + (IntervalLabel(Get(settings, "intervalMinutes")) ?? "every hour") + ", " + WindowWords(settings);
        }

        /// <summary>describe(entry, settings, devices): [label, words] rows for one vehicle's saved settings.</summary>
        public static List<KeyValuePair<string, string>> Describe(JsonValue config, bool automatic, JsonValue settings, JsonValue devices)
        {
            List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (a, b) => rows.Add(new KeyValuePair<string, string>(a, b));
            add("Codec", CodecWords(config));
            add("Size", SizeWords(config, false));
            if (HasTarget(config)) add("Keep clips that miss the target", IsTrue(Get(config, "keepOffTarget")) ? "On — kept converted even when not smaller enough" : "Off — the original stays when its copy misses the target");
            add("Output", OutputWords(config, false));
            add("Verify", VerifyWords(config, false));
            add("Clip groups and order", GroupsWords(config));
            add("Devices", DevicesWords(config, devices));
            add("Automatic conversion", AutoWords(automatic, settings));
            return rows;
        }

        /// <summary>summary(config): "H.265 · Balanced · Replace · Quick".</summary>
        public static string Summary(JsonValue config)
        {
            return CodecWords(config) + " · " + SizeWords(config, true) + " · " + OutputWords(config, true) + " · " + VerifyWords(config, true);
        }

        /// <summary>folderOf(v): v.studioRoot || v.root (null for undefined).</summary>
        public static JsonValue FolderOf(JsonValue v)
        {
            JsonValue s = Get(v, "studioRoot");
            return Truthy(s) ? s : Get(v, "root");
        }

        /// <summary>savedFor(state, folder): the automatic config for the folder, else the saved one; null when neither. automatic says which.</summary>
        public static JsonValue SavedFor(JsonValue state, JsonValue folder, out bool automatic)
        {
            automatic = false;
            JsonValue settings = Get(state, "settings");
            JsonValue configs = Truthy(settings) ? Get(settings, "configs") : null;
            if (Truthy(configs) && configs.IsArray)
                foreach (JsonValue c in configs.Items) if (Same(Get(c, "sourceRoot"), folder)) { automatic = true; return c; }
            JsonValue saved = Get(state, "saved");
            if (Truthy(saved) && saved.IsArray)
                foreach (JsonValue c in saved.Items) if (Same(Get(c, "sourceRoot"), folder)) return c;
            return null;
        }

        /// <summary>shared(settings, over): what setAuto needs besides one vehicle's settings (the shared schedule), in the web's key order.</summary>
        public static List<KeyValuePair<string, JsonValue>> Shared(JsonValue settings, JsonValue over)
        {
            JsonValue s = Truthy(settings) ? settings : JsonValue.Object();
            JsonValue ow = Get(over, "window");
            JsonValue sw = Get(s, "window");
            JsonValue window = over != null && over.Has("window") ? ow : (Truthy(sw) ? sw : JsonValue.Null);
            JsonValue oi = Get(over, "intervalMinutes"), si = Get(s, "intervalMinutes");
            JsonValue interval = Truthy(oi) ? oi : Truthy(si) ? si : JsonValue.Number(60L);
            List<KeyValuePair<string, JsonValue>> o = new List<KeyValuePair<string, JsonValue>>();
            o.Add(new KeyValuePair<string, JsonValue>("intervalMinutes", interval));
            o.Add(new KeyValuePair<string, JsonValue>("window", window));
            o.Add(new KeyValuePair<string, JsonValue>("holdManual", (window.Bool && IsTrue(Get(s, "holdManual"))) ? JsonValue.True : JsonValue.False));
            o.Add(new KeyValuePair<string, JsonValue>("keepRunning", IsTrue(Get(s, "keepRunning")) ? JsonValue.True : JsonValue.False));
            o.Add(new KeyValuePair<string, JsonValue>("openAtLogin", IsTrue(Get(s, "openAtLogin")) ? JsonValue.True : JsonValue.False));
            return o;
        }

        static JsonValue Obj(List<KeyValuePair<string, JsonValue>> pairs)
        {
            return JsonValue.From(pairs);
        }

        static JsonValue Strings(IEnumerable<string> items)
        {
            List<JsonValue> list = new List<JsonValue>();
            foreach (string s in items) list.Add(JsonValue.String(s));
            return JsonValue.Array(list);
        }

        static JsonValue FolderValue(JsonValue folder)
        {
            return folder ?? JsonValue.Null;
        }

        /// <summary>defaults(folder, replaceOk): a vehicle's settings before it is set up.</summary>
        public static JsonValue Defaults(JsonValue folder, bool replaceOk)
        {
            List<KeyValuePair<string, JsonValue>> o = new List<KeyValuePair<string, JsonValue>>();
            Action<string, JsonValue> add = (k, v) => o.Add(new KeyValuePair<string, JsonValue>(k, v));
            if (folder != null) add("sourceRoot", folder); // (undefined is left out, as JSON.stringify does)
            add("destinationRoot", JsonValue.Null);
            add("codec", JsonValue.String("h265"));
            add("profileId", JsonValue.String("balanced"));
            add("networkMode", JsonValue.String("auto"));
            add("outputMode", JsonValue.String(replaceOk ? "replace" : "separate"));
            List<string> all = new List<string>();
            for (int i = 0; i < GroupNames.GetLength(0); i++) all.Add(GroupNames[i, 0]);
            add("includedGroups", Strings(all));
            add("groupOrder", Strings(DefaultOrder));
            add("replacementConfirmed", JsonValue.False);
            add("concurrency", JsonValue.Number(5L));
            add("verifyMode", JsonValue.String("quick"));
            return Obj(o);
        }

        /// <summary>
        /// save({ state, folder, config, automatic, schedule }): automatic on,
        /// setAuto with the shared schedule and this config; off, saveVehicle
        /// (after setAuto removes the vehicle when it was automatic). The
        /// answer is the Encoder's new state.
        /// </summary>
        public static async Task<JsonValue> Save(IJsonApi api, JsonValue state, JsonValue folder, JsonValue config, bool automatic, JsonValue schedule, CancellationToken ct)
        {
            bool wasAuto;
            JsonValue was = SavedFor(state, folder, out wasAuto);
            JsonValue settings = Get(state, "settings");
            if (automatic)
            {
                List<KeyValuePair<string, JsonValue>> p = new List<KeyValuePair<string, JsonValue>>();
                p.Add(new KeyValuePair<string, JsonValue>("enabled", JsonValue.True));
                p.AddRange(Shared(settings, schedule));
                p.Add(new KeyValuePair<string, JsonValue>("config", config));
                return await Converter.Invoke(api, "setAuto", Obj(p), ct).ConfigureAwait(false);
            }
            if (was != null && wasAuto)
            {
                int others = 0;
                JsonValue configs = Get(settings, "configs");
                if (Truthy(configs) && configs.IsArray)
                    foreach (JsonValue c in configs.Items) if (!Same(Get(c, "sourceRoot"), folder)) others++;
                List<KeyValuePair<string, JsonValue>> p = new List<KeyValuePair<string, JsonValue>>();
                p.Add(new KeyValuePair<string, JsonValue>("enabled", others > 0 ? JsonValue.True : JsonValue.False));
                p.AddRange(Shared(settings, schedule));
                if (folder != null) p.Add(new KeyValuePair<string, JsonValue>("removeSource", folder));
                await Converter.Invoke(api, "setAuto", Obj(p), ct).ConfigureAwait(false);
            }
            return await Converter.Invoke(api, "saveVehicle", JsonValue.Object("config", config), ct).ConfigureAwait(false);
        }

        /// <summary>errorText(err): the Encoder's code in the page's words, else the message, else "That didn’t work.".</summary>
        public static string ErrorText(Exception err)
        {
            if (err == null) return Converter.DidNotWork;
            ConverterError ce = err as ConverterError;
            string text;
            if (ce != null && Converter.Errors.TryGetValue(ce.Code, out text)) return text;
            return string.IsNullOrEmpty(err.Message) ? Converter.DidNotWork : err.Message;
        }

        /// <summary>?edit=&lt;id&gt; of a sub page ("vehicles?edit=1"): URLSearchParams(...).get('edit'), null when absent.</summary>
        public static string EditParam(string sub)
        {
            if (string.IsNullOrEmpty(sub)) return null;
            string[] parts = sub.Split('?');
            if (parts.Length < 2) return null;
            foreach (string pair in parts[1].Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string k = Decode(eq < 0 ? pair : pair.Substring(0, eq));
                if (k != "edit") continue;
                return eq < 0 ? "" : Decode(pair.Substring(eq + 1));
            }
            return null;
        }

        static string Decode(string s)
        {
            string t = s.Replace('+', ' ');
            try { return Uri.UnescapeDataString(t); }
            catch (Exception) { return t; }
        }
    }

    /// <summary>
    /// vehicleEncoding.js mount(): the view's state (the vehicles, the
    /// Encoder's state, the devices, whether originals can be replaced, the
    /// vehicle being edited and its draft) and what its buttons do. Made
    /// afresh for each draw of the page (the web mounts it again on every
    /// render()); Edit, Cancel, the order buttons, Save and Forget draw the
    /// cards again from this state without reading anything again, as the
    /// web's draw() does.
    /// </summary>
    public sealed class VehicleEncodingView
    {
        public JsonValue State;
        public readonly List<JsonValue> Cars = new List<JsonValue>();
        public JsonValue Devices = JsonValue.Array(null);
        public bool ReplaceOk;
        /// <summary>The vehicle being edited (a string, as the web's data attribute; compared with === to the vehicle's id).</summary>
        public string Editing;
        /// <summary>The editor's values between draws (the web's draft, with its _automatic, _bitrate and _schedule).</summary>
        public JsonValue Draft;

        public VehicleEncodingView(string edit)
        {
            Editing = edit;
        }

        /// <summary>
        /// load(): GET /api/vehicles (any failure: "Can’t load the
        /// vehicles."), getAuto (its failure is the page's), getCapabilities
        /// and devices (their failures are ignored), all at once.
        /// </summary>
        public async Task Load(IJsonApi api, CancellationToken ct)
        {
            Task<JsonValue> vs = Vehicles(api, ct);
            Task<JsonValue> st = Converter.Invoke(api, "getAuto", null, ct);
            Task<JsonValue> caps = Quiet(Converter.Invoke(api, "getCapabilities", null, ct));
            Task<JsonValue> devs = Quiet(Converter.Invoke(api, "devices", null, ct));
            // (Promise.all: the first failure; the vehicles are asked first)
            JsonValue cars = await vs.ConfigureAwait(false);
            JsonValue state = await st.ConfigureAwait(false);
            JsonValue c = await caps.ConfigureAwait(false);
            JsonValue d = await devs.ConfigureAwait(false);
            Cars.Clear();
            Cars.AddRange(cars.Items);
            State = state;
            JsonValue dl = VehicleEncoding.Get(d, "devices");
            Devices = dl != null && dl.Bool ? dl : JsonValue.Array(null);
            ReplaceOk = IsAvailable(c);
        }

        static bool IsAvailable(JsonValue caps)
        {
            JsonValue r = VehicleEncoding.Get(caps, "replacement");
            return r != null && r.IsString && r.Str == "available";
        }

        static async Task<JsonValue> Vehicles(IJsonApi api, CancellationToken ct)
        {
            JsonValue answer;
            try { answer = await api.Get("/api/vehicles", ct).ConfigureAwait(false); }
            catch (ApiError e)
            {
                // (the browser's fetch reaches the server or throws its own error; a refusal is the web's words)
                if (e.Status > 0) throw new InvalidOperationException(VehicleEncoding.VehiclesFailed, e);
                throw;
            }
            JsonValue list = VehicleEncoding.Get(answer, "vehicles");
            return list != null && list.IsArray ? list : JsonValue.Array(null);
        }

        static async Task<JsonValue> Quiet(Task<JsonValue> t)
        {
            try { return await t.ConfigureAwait(false); }
            catch (Exception) { return JsonValue.Object(); }
        }

        static bool IsId(JsonValue car, string id)
        {
            JsonValue v = VehicleEncoding.Get(car, "id");
            return id != null && v != null && v.IsString && v.Str == id;
        }

        /// <summary>cars.find((x) => x.id === id).</summary>
        public JsonValue Car(string id)
        {
            foreach (JsonValue v in Cars) if (IsId(v, id)) return v;
            return null;
        }

        /// <summary>The id a card's buttons carry (esc(v.id), read back from the attribute).</summary>
        public static string IdOf(JsonValue car)
        {
            return VehicleEncoding.EscText(VehicleEncoding.Get(car, "id"));
        }

        /// <summary>
        /// The cards as draw() writes them: one per vehicle, the one being
        /// edited as its editor (which keeps its draft from here on, as the
        /// web's editor() does). Throws as the web's would when the Encoder's
        /// state is missing for the editor.
        /// </summary>
        public List<VeCard> Cards()
        {
            List<VeCard> cards = new List<VeCard>();
            foreach (JsonValue v in Cars) cards.Add(Card(v));
            return cards;
        }

        VeCard Card(JsonValue v)
        {
            JsonValue folder = VehicleEncoding.FolderOf(v);
            bool automatic;
            JsonValue entry = VehicleEncoding.SavedFor(State, folder, out automatic);
            VeCard c = new VeCard();
            c.Id = IdOf(v);
            c.Name = VehicleEncoding.EscText(VehicleEncoding.Get(v, "name"));
            if (Editing != null && IsId(v, Editing))
            {
                c.Tag = "editing";
                c.TagText = VehicleEncoding.TagEditing;
                c.Editor = Editor(v, entry, automatic, folder);
                return c;
            }
            if (entry != null)
            {
                c.Tag = automatic ? "on" : "saved";
                c.TagText = automatic ? VehicleEncoding.TagOn : VehicleEncoding.TagSaved;
                c.Rows = VehicleEncoding.Describe(entry, automatic, VehicleEncoding.Get(State, "settings"), Devices);
                c.Button = VehicleEncoding.EditLabel;
            }
            else
            {
                c.Tag = "none";
                c.TagText = VehicleEncoding.TagNone;
                c.Empty = VehicleEncoding.NotSetUp;
                c.Button = VehicleEncoding.SetUpLabel;
            }
            return c;
        }

        static JsonValue G(JsonValue o, string k)
        {
            return VehicleEncoding.Get(o, k);
        }

        static bool StrIs(JsonValue v, string s)
        {
            return v != null && v.IsString && v.Str == s;
        }

        VeForm Editor(JsonValue v, JsonValue entry, bool automatic, JsonValue folder)
        {
            // (entry here is the config itself: savedFor returns the config)
            JsonValue c = Draft ?? (entry ?? VehicleEncoding.Defaults(folder, ReplaceOk));
            if (Draft == null) { Draft = c.With("_automatic", entry != null && automatic); c = Draft; }
            if (State == null || State.IsNull) throw new InvalidOperationException("Cannot read properties of null (reading 'settings')");
            JsonValue s = G(State, "settings");
            if (s == null || !s.Bool) s = JsonValue.Object();
            VeForm f = new VeForm();
            f.CarId = IdOf(v);
            f.Name = VehicleEncoding.EscText(G(v, "name"));
            JsonValue profile = G(c, "profileId");
            List<JsonValue> order = OrderList(c);
            HashSet<string> included = IncludedSet(G(c, "includedGroups"));
            JsonValue lanes = G(c, "lanes");
            bool hasLanes = lanes != null && lanes.IsArray && lanes.Count > 0;
            JsonValue w = G(s, "window");
            string winMode = w == null || !w.Bool ? "any" : StrIs(G(w, "follow"), "off-peak") ? "off-peak" : "hours";

            for (int i = 0; i < VehicleEncoding.Codecs.GetLength(0); i++)
                f.Codec.Add(new VeRadio { Name = "codec", Value = VehicleEncoding.Codecs[i, 0], Text = VehicleEncoding.Codecs[i, 1], Checked = StrIs(G(c, "codec"), VehicleEncoding.Codecs[i, 0]) });

            for (int i = 0; i < VehicleEncoding.SizeProfiles.GetLength(0); i++)
                f.Profile.Add(VehicleEncoding.SizeProfiles[i, 0], VehicleEncoding.SizeProfiles[i, 1] + " — about " + VehicleEncoding.SizeProfiles[i, 2] + "% smaller", StrIs(profile, VehicleEncoding.SizeProfiles[i, 0]));
            f.Profile.Add("quality", VehicleEncoding.QualityOption, StrIs(profile, "quality"));
            f.Profile.Add("custom", VehicleEncoding.CustomOption, StrIs(profile, "custom"));
            f.Profile.Settle();

            JsonValue ql = G(c, "qualityLevel");
            string level = ql != null && ql.Bool ? null : "balanced";
            for (int i = 0; i < VehicleEncoding.QualityLevels.GetLength(0); i++)
                f.Quality.Add(VehicleEncoding.QualityLevels[i, 0], VehicleEncoding.QualityLevels[i, 1], level != null ? VehicleEncoding.QualityLevels[i, 0] == level : StrIs(ql, VehicleEncoding.QualityLevels[i, 0]));
            f.Quality.Settle();

            // value="${c.bitrateKbps ? c.bitrateKbps / 1000 : 8}", as the number field takes it
            JsonValue kbps = G(c, "bitrateKbps");
            string bitrate = kbps != null && kbps.Bool ? Format.JsNum(VehicleEncoding.JsNumber(kbps) / 1000) : "8";
            f.Bitrate = Policies.NumberValue(bitrate);
            f.KeepOffTarget = IsTrue(G(c, "keepOffTarget"));

            bool replace = StrIs(G(c, "outputMode"), "replace");
            f.Output.Add(new VeRadio { Name = "outputMode", Value = "replace", Text = VehicleEncoding.ReplaceLabel, Checked = replace, Disabled = !ReplaceOk });
            f.Output.Add(new VeRadio { Name = "outputMode", Value = "separate", Text = VehicleEncoding.SeparateLabel, Checked = !replace });
            f.ReplaceUnavailable = !ReplaceOk;
            JsonValue dest = G(c, "destinationRoot");
            f.Destination = dest != null && dest.Bool ? VehicleEncoding.EscText(dest) : "";
            f.DestinationPlaceholder = "/output/" + f.Name;
            f.ReplacementConfirmed = replace && IsTrue(G(c, "replacementConfirmed"));

            JsonValue vm = G(c, "verifyMode");
            for (int i = 0; i < VehicleEncoding.VerifyModes.GetLength(0); i++)
            {
                string id = VehicleEncoding.VerifyModes[i, 0];
                f.Verify.Add(new VeRadio { Name = "verifyMode", Value = id, Text = VehicleEncoding.VerifyModes[i, 1], Checked = vm != null && vm.Bool ? StrIs(vm, id) : id == "quick" });
            }

            foreach (JsonValue g in order)
            {
                string id = VehicleEncoding.EscText(g);
                f.Groups.Add(new VeGroup { Id = id, Text = VehicleEncoding.EscText(LabelOf(g)), Checked = g.IsString && included.Contains(g.Str) });
            }

            f.Lanes = hasLanes;
            if (hasLanes) f.DevicesText = VehicleEncoding.DevicesWords(c, Devices);
            else
            {
                double n = VehicleEncoding.JsNumber(G(c, "concurrency"));
                f.Concurrency = Policies.NumberValue(double.IsNaN(n) || n == 0 ? "1" : Format.JsNum(n));
            }

            JsonValue auto = G(Draft, "_automatic");
            f.Automatic = auto != null && auto.Bool;
            f.AutomaticLabel = "Convert new clips of " + f.Name + " automatically";
            JsonValue im = G(s, "intervalMinutes");
            double every = im != null && im.Bool ? (im.IsNumber ? im.Num : double.NaN) : 60;
            for (int i = 0; i < VehicleEncoding.IntervalMinutes.Length; i++)
            {
                string t = VehicleEncoding.IntervalWords[i];
                f.Interval.Add(VehicleEncoding.IntervalMinutes[i].ToString(CultureInfo.InvariantCulture), char.ToUpperInvariant(t[0]) + t.Substring(1), every == VehicleEncoding.IntervalMinutes[i]);
            }
            f.Interval.Settle();
            f.Window.Add("any", VehicleEncoding.AnyTime, winMode == "any");
            f.Window.Add("off-peak", VehicleEncoding.OffPeak, winMode == "off-peak");
            f.Window.Add("hours", VehicleEncoding.SetHours, winMode == "hours");
            f.Window.Settle();
            double start = HourOr(G(w, "startHour"), 1), end = HourOr(G(w, "endHour"), 6);
            for (int h = 0; h < 24; h++)
            {
                string text = VehicleEncoding.Hour(h.ToString(CultureInfo.InvariantCulture));
                f.StartHour.Add(h.ToString(CultureInfo.InvariantCulture), text, start == h);
                f.EndHour.Add(h.ToString(CultureInfo.InvariantCulture), text, end == h);
            }
            f.StartHour.Settle();
            f.EndHour.Settle();
            f.CanForget = entry != null && !automatic;
            f.Sync();
            return f;
        }

        // w?.startHour ?? fallback, compared with === to a number
        static double HourOr(JsonValue v, double fallback)
        {
            if (v == null || v.IsNull) return fallback;
            return v.IsNumber ? v.Num : double.NaN;
        }

        static JsonValue LabelOf(JsonValue g)
        {
            if (g == null || !g.IsString) return null;
            for (int i = 0; i < VehicleEncoding.GroupNames.GetLength(0); i++) if (VehicleEncoding.GroupNames[i, 0] == g.Str) return JsonValue.String(VehicleEncoding.GroupNames[i, 1]);
            return null;
        }

        static bool IsTrue(JsonValue v)
        {
            return v != null && v.IsBool && v.Bool;
        }

        static List<JsonValue> OrderList(JsonValue c)
        {
            JsonValue o = G(c, "groupOrder");
            List<JsonValue> list = new List<JsonValue>();
            if (o != null && o.IsArray && o.Count == 3) list.AddRange(o.Items);
            else foreach (string g in VehicleEncoding.DefaultOrder) list.Add(JsonValue.String(g));
            return list;
        }

        static HashSet<string> IncludedSet(JsonValue groups)
        {
            HashSet<string> set = new HashSet<string>();
            if (groups == null || !groups.Bool) return set;
            if (groups.IsArray) { foreach (JsonValue g in groups.Items) if (g.IsString) set.Add(g.Str); }
            else if (groups.IsString) foreach (char ch in groups.Str) set.Add(ch.ToString());
            return set;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>Edit / Set up: that vehicle's editor (a fresh draft).</summary>
        public void Edit(string id)
        {
            Editing = id;
            Draft = null;
        }

        /// <summary>Cancel: the cards again, the draft dropped.</summary>
        public void Cancel()
        {
            Editing = null;
            Draft = null;
        }

        static string First(List<KeyValuePair<string, string>> data, string name)
        {
            foreach (KeyValuePair<string, string> kv in data) if (kv.Key == name) return kv.Value;
            return null;
        }

        static bool HasKey(List<KeyValuePair<string, string>> data, string name)
        {
            foreach (KeyValuePair<string, string> kv in data) if (kv.Key == name) return true;
            return false;
        }

        static JsonValue StrOrNull(string s)
        {
            return s == null ? JsonValue.Null : JsonValue.String(s);
        }

        /// <summary>readForm(form): the form's values into the draft (kept while the order is redrawn, and sent by Save).</summary>
        public JsonValue ReadForm(VeForm form)
        {
            List<KeyValuePair<string, string>> fd = form.Data();
            JsonValue c = Draft ?? JsonValue.Object();
            c = c.With("codec", First(fd, "codec") ?? "h265");
            c = c.With("profileId", First(fd, "profileId") ?? "balanced");
            c = c.With("qualityLevel", First(fd, "qualityLevel") ?? "balanced");
            c = c.With("_bitrate", StrOrNull(First(fd, "bitrate")));
            c = c.With("keepOffTarget", First(fd, "keepOffTarget") == "on");
            c = c.With("outputMode", First(fd, "outputMode") ?? "separate");
            // String(fd.get('destinationRoot') ?? c.destinationRoot ?? '').trim() || null
            string typed = First(fd, "destinationRoot");
            string raw = typed ?? (G(c, "destinationRoot") == null || G(c, "destinationRoot").IsNull ? "" : VehicleEncoding.JsString(G(c, "destinationRoot")));
            string trimmed = JsTrim(raw);
            c = c.With("destinationRoot", trimmed.Length > 0 ? JsonValue.String(trimmed) : JsonValue.Null);
            c = c.With("replacementConfirmed", First(fd, "replacementConfirmed") == "on");
            c = c.With("verifyMode", First(fd, "verifyMode") ?? "quick");
            List<JsonValue> groups = new List<JsonValue>();
            foreach (KeyValuePair<string, string> kv in fd) if (kv.Key == "group") groups.Add(JsonValue.String(kv.Value));
            c = c.With("includedGroups", JsonValue.Array(groups));
            if (HasKey(fd, "concurrency")) c = c.With("concurrency", NumberValue(VehicleEncoding.StringNumber(First(fd, "concurrency"))));
            c = c.With("_automatic", First(fd, "automatic") == "on");
            double interval = VehicleEncoding.StringNumber(First(fd, "intervalMinutes"));
            List<KeyValuePair<string, JsonValue>> sch = new List<KeyValuePair<string, JsonValue>>();
            sch.Add(new KeyValuePair<string, JsonValue>("intervalMinutes", NumberValue(double.IsNaN(interval) || interval == 0 ? 60 : interval)));
            sch.Add(new KeyValuePair<string, JsonValue>("mode", JsonValue.String(First(fd, "window") ?? "any")));
            sch.Add(new KeyValuePair<string, JsonValue>("startHour", NumberValue(VehicleEncoding.StringNumber(First(fd, "startHour")))));
            sch.Add(new KeyValuePair<string, JsonValue>("endHour", NumberValue(VehicleEncoding.StringNumber(First(fd, "endHour")))));
            c = c.With("_schedule", JsonValue.From(sch));
            Draft = c;
            return c;
        }

        /// <summary>A number as JSON keeps it (NaN and Infinity become null, as JSON.stringify writes them).</summary>
        static JsonValue NumberValue(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return JsonValue.Null;
            if (d == Math.Floor(d) && Math.Abs(d) < 9e15) return JsonValue.Number((long)d);
            return JsonValue.Number(d);
        }

        /// <summary>A number that may have been NaN (JSON null): back to a double for the checks.</summary>
        static double Num(JsonValue v)
        {
            if (v == null) return double.NaN;
            if (v.IsNull) return double.NaN;
            return VehicleEncoding.JsNumber(v);
        }

        static string JsTrim(string s)
        {
            return s.Trim(VehicleEncoding.JsBlanks);
        }

        /// <summary>
        /// ↑ / ↓ of a group: the form's values kept (readForm), the group
        /// swapped with its neighbour, the bitrate kept as kbps, the editor
        /// drawn again. False when it cannot move. The page then focuses the
        /// same button of the moved row while it is enabled.
        /// </summary>
        public bool Move(VeForm form, string group, bool up)
        {
            if (form == null || Draft == null) return false;
            ReadForm(form);
            List<JsonValue> order = new List<JsonValue>();
            JsonValue go = G(Draft, "groupOrder");
            if (go != null && go.IsArray && go.Count == 3) order.AddRange(go.Items);
            else foreach (string g in VehicleEncoding.DefaultOrder) order.Add(JsonValue.String(g));
            int i = -1;
            for (int k = 0; k < order.Count; k++) if (order[k].IsString && order[k].Str == group) { i = k; break; }
            int j = up ? i - 1 : i + 1;
            if (j < 0 || j > 2) return false;
            if (i < 0) return false; // (order[-1] is undefined: the web's swap writes a key "-1" no one reads; nothing moves)
            JsonValue t = order[i];
            order[i] = order[j];
            order[j] = t;
            Draft = Draft.With("groupOrder", JsonValue.Array(order));
            JsonValue br = G(Draft, "_bitrate");
            double mbps = br != null && br.Bool ? VehicleEncoding.JsNumber(br) : 8;
            Draft = Draft.With("bitrateKbps", NumberValue(Format.Round(mbps * 1000)));
            return true;
        }

        /// <summary>configOf(c, folder): exactly the keys the Encoder validates, in the web's order.</summary>
        public static JsonValue ConfigOf(JsonValue c, JsonValue folder)
        {
            List<KeyValuePair<string, JsonValue>> o = new List<KeyValuePair<string, JsonValue>>();
            Action<string, JsonValue> add = (k, v) => { if (v != null) o.Add(new KeyValuePair<string, JsonValue>(k, v)); };
            bool replace = StrIs(G(c, "outputMode"), "replace");
            add("sourceRoot", folder);
            add("destinationRoot", replace ? JsonValue.Null : G(c, "destinationRoot"));
            add("codec", G(c, "codec"));
            add("profileId", G(c, "profileId"));
            JsonValue nm = G(c, "networkMode");
            add("networkMode", nm != null && nm.Bool ? nm : JsonValue.String("auto"));
            add("outputMode", G(c, "outputMode"));
            HashSet<string> inc = new HashSet<string>();
            JsonValue ig = G(c, "includedGroups");
            if (ig != null && ig.IsArray) foreach (JsonValue g in ig.Items) if (g.IsString) inc.Add(g.Str);
            List<string> groups = new List<string>();
            for (int i = 0; i < VehicleEncoding.GroupNames.GetLength(0); i++) if (inc.Contains(VehicleEncoding.GroupNames[i, 0])) groups.Add(VehicleEncoding.GroupNames[i, 0]);
            List<JsonValue> gl = new List<JsonValue>();
            foreach (string g in groups) gl.Add(JsonValue.String(g));
            add("includedGroups", JsonValue.Array(gl));
            JsonValue go = G(c, "groupOrder");
            List<JsonValue> order = new List<JsonValue>();
            if (go != null && go.Bool && go.IsArray) order.AddRange(go.Items);
            else foreach (string g in VehicleEncoding.DefaultOrder) order.Add(JsonValue.String(g));
            add("groupOrder", JsonValue.Array(order));
            add("replacementConfirmed", replace && IsTrue(G(c, "replacementConfirmed")) ? JsonValue.True : JsonValue.False);
            add("verifyMode", G(c, "verifyMode"));
            JsonValue profile = G(c, "profileId");
            if (StrIs(profile, "custom"))
            {
                JsonValue b = G(c, "_bitrate");
                double mbps = b != null && !b.IsNull ? VehicleEncoding.JsNumber(b) : VehicleEncoding.JsNumber(G(c, "bitrateKbps")) / 1000;
                add("bitrateKbps", NumberValue(Format.Round(mbps * 1000)));
            }
            if (StrIs(profile, "quality")) add("qualityLevel", G(c, "qualityLevel"));
            if (VehicleEncoding.HasTarget(c)) add("keepOffTarget", IsTrue(G(c, "keepOffTarget")) ? JsonValue.True : JsonValue.False);
            JsonValue lanes = G(c, "lanes");
            if (lanes != null && lanes.IsArray && lanes.Count > 0)
            {
                List<JsonValue> ls = new List<JsonValue>();
                foreach (JsonValue l in lanes.Items)
                {
                    List<KeyValuePair<string, JsonValue>> lo = new List<KeyValuePair<string, JsonValue>>();
                    if (G(l, "device") != null) lo.Add(new KeyValuePair<string, JsonValue>("device", G(l, "device")));
                    if (G(l, "concurrency") != null) lo.Add(new KeyValuePair<string, JsonValue>("concurrency", G(l, "concurrency")));
                    ls.Add(lo.Count > 0 ? JsonValue.From(lo) : JsonValue.Object());
                }
                add("lanes", JsonValue.Array(ls));
            }
            else add("concurrency", G(c, "concurrency"));
            return JsonValue.From(o);
        }

        static bool SafeInteger(JsonValue v)
        {
            if (v == null || !v.IsNumber) return false;
            double d = v.Num;
            return !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) && Math.Abs(d) <= 9007199254740991;
        }

        static void Fail(VeForm form, VeUi ui, string text)
        {
            form.Error = text;
            ui.Changed();
            ui.Toast(text, true);
        }

        /// <summary>
        /// Save (the form's submit): the Encoder's own checks first, in the
        /// web's order (a clip kind, the separate folder, the bitrate, clips
        /// at once, replacing: possible, ticked, confirmed; the time window),
        /// each failure in the error line and as a toast; then save(). Saved:
        /// the toast, the editor closed, the cards again. A refusal: its words
        /// in the error line.
        /// </summary>
        public async Task Submit(IJsonApi api, VeUi ui, VeForm form, CancellationToken ct)
        {
            JsonValue v = Car(form.CarId);
            if (v == null) return;
            JsonValue folder = VehicleEncoding.FolderOf(v);
            JsonValue c = ReadForm(form);
            JsonValue groups = G(c, "includedGroups");
            if (groups.Count == 0) { Fail(form, ui, Converter.Errors["INVALID_GROUP_SELECTION"]); return; }
            JsonValue mode = G(c, "outputMode");
            JsonValue dest = G(c, "destinationRoot");
            if (StrIs(mode, "separate") && (dest == null || !dest.Bool)) { Fail(form, ui, VehicleEncoding.NeedFolder); return; }
            if (StrIs(G(c, "profileId"), "custom"))
            {
                double b = VehicleEncoding.JsNumber(G(c, "_bitrate"));
                if (!(b >= 0.25 && b <= 100)) { Fail(form, ui, Converter.Errors["INVALID_BITRATE"]); return; }
            }
            JsonValue lanes = G(c, "lanes");
            bool hasLanes = lanes != null && lanes.IsArray && lanes.Count > 0;
            if (!hasLanes)
            {
                JsonValue cc = G(c, "concurrency");
                if (!(SafeInteger(cc) && cc.Num >= 1 && cc.Num <= 64)) { Fail(form, ui, Converter.Errors["INVALID_CONCURRENCY"]); return; }
            }
            string name = VehicleEncoding.Tpl(v, "name");
            if (StrIs(mode, "replace"))
            {
                if (!ReplaceOk) { Fail(form, ui, Converter.Errors["REPLACEMENT_UNAVAILABLE"]); return; }
                if (!IsTrue(G(c, "replacementConfirmed"))) { Fail(form, ui, Converter.Errors["REPLACEMENT_CONFIRMATION_REQUIRED"]); return; }
                if (!await ui.Confirm(ReplaceQuestion(name))) { Fail(form, ui, Converter.Errors["REPLACEMENT_CONFIRMATION_REQUIRED"]); return; }
            }
            JsonValue sch = G(c, "_schedule");
            string wm = G(sch, "mode").Str;
            JsonValue window;
            if (wm == "off-peak") window = JsonValue.Object("follow", "off-peak");
            else if (wm == "hours") window = JsonValue.From(new List<KeyValuePair<string, JsonValue>> { new KeyValuePair<string, JsonValue>("startHour", G(sch, "startHour")), new KeyValuePair<string, JsonValue>("endHour", G(sch, "endHour")) });
            else window = JsonValue.Null;
            JsonValue schedule = JsonValue.From(new List<KeyValuePair<string, JsonValue>> { new KeyValuePair<string, JsonValue>("intervalMinutes", G(sch, "intervalMinutes")), new KeyValuePair<string, JsonValue>("window", window) });
            bool automatic = IsTrue(G(c, "_automatic"));
            if (automatic && wm == "hours" && Num(G(sch, "startHour")) == Num(G(sch, "endHour"))) { Fail(form, ui, Converter.Errors["INVALID_AUTO_WINDOW"]); return; }
            form.Saving = true;
            ui.Changed();
            try
            {
                State = await VehicleEncoding.Save(api, State, folder, ConfigOf(c, folder), automatic, schedule, ct);
            }
            catch (Exception err)
            {
                form.Saving = false;
                Fail(form, ui, VehicleEncoding.ErrorText(err));
                return;
            }
            Editing = null;
            Draft = null;
            ui.Toast(name + ": encoding settings saved.", false);
            ui.Draw();
        }

        /// <summary>The question before replacing originals.</summary>
        public static string ReplaceQuestion(string name)
        {
            return "Replace originals in " + name + "’s footage? Each original is backed up first, and a failed replacement restores that backup.";
        }

        /// <summary>The question before forgetting a vehicle's saved settings.</summary>
        public static string ForgetQuestion(string name)
        {
            return "Forget " + name + "’s saved encoding settings? The Encoder’s own settings are not changed.";
        }

        /// <summary>Forget these settings: asked first; forgetVehicle; the cards again (a refusal is a toast).</summary>
        public async Task Forget(IJsonApi api, VeUi ui, string id, CancellationToken ct)
        {
            JsonValue v = Car(id);
            if (v == null) return;
            if (!await ui.Confirm(ForgetQuestion(VehicleEncoding.Tpl(v, "name")))) return;
            try
            {
                JsonValue folder = VehicleEncoding.FolderOf(v);
                List<KeyValuePair<string, JsonValue>> p = new List<KeyValuePair<string, JsonValue>>();
                if (folder != null) p.Add(new KeyValuePair<string, JsonValue>("forgetSource", folder));
                State = await Converter.Invoke(api, "forgetVehicle", p.Count > 0 ? JsonValue.From(p) : JsonValue.Object(), ct);
                Editing = null;
                Draft = null;
                ui.Draw();
            }
            catch (Exception err)
            {
                ui.Toast(VehicleEncoding.ErrorText(err), true);
            }
        }

        // ---------------------------------------------------------------- the outline (tests)

        /// <summary>
        /// The view as a reader sees it, in the markers
        /// test/windowsNativeVehicleEncoding.test.js writes for the web's DOM
        /// after each step (see there).
        /// </summary>
        public static List<string> Outline(List<VeCard> cards, string loadError)
        {
            List<string> o = new List<string>();
            Action<string> t = s => { string x = (s ?? "").Trim(); if (x.Length > 0) o.Add(x); };
            if (loadError != null)
            {
                o.Add("[error]"); t(loadError);
                return o;
            }
            o.Add("[intro]"); t(VehicleEncoding.Intro); o.Add("[a]"); t(VehicleEncoding.OpenEncoder);
            if (cards.Count == 0) { o.Add("[empty]"); t(VehicleEncoding.NoVehicles); return o; }
            o.Add("[cards]");
            foreach (VeCard c in cards)
            {
                o.Add("[card]");
                o.Add("[h2]"); t(c.Name);
                o.Add(c.Tag == "on" ? "[tag on]" : "[tag]"); t(c.TagText);
                if (c.Editor != null) { Form(o, t, c.Editor); continue; }
                if (c.Rows != null)
                {
                    o.Add("[rows]");
                    foreach (KeyValuePair<string, string> r in c.Rows) { o.Add("[dt]"); t(r.Key); o.Add("[dd]"); t(r.Value); }
                }
                else { o.Add("[empty]"); t(c.Empty); }
                o.Add("[actions]");
                o.Add("[btn solid]"); t(c.Button);
            }
            return o;
        }

        static string Show(string key, bool shown)
        {
            return "[show " + key + (shown ? "" : " hidden") + "]";
        }

        static void Radio(List<string> o, Action<string> t, VeRadio r)
        {
            o.Add("[radio " + r.Name + "=" + r.Value + (r.Checked ? " checked" : "") + (r.Disabled ? " disabled" : "") + "]");
            t(r.Text);
        }

        static void Select(List<string> o, Action<string> t, VeSelect s)
        {
            o.Add("[select " + s.Name + "=" + s.Value + "]");
            for (int i = 0; i < s.Options.Count; i++)
            {
                o.Add("[option " + s.Options[i].Key + (s.Marked[i] ? " selected" : "") + "]");
                t(s.Options[i].Value);
            }
        }

        static void Form(List<string> o, Action<string> t, VeForm f)
        {
            o.Add("[form]");
            o.Add("[fieldset]"); o.Add("[legend]"); t(VehicleEncoding.LegendCodec);
            o.Add("[choices]");
            foreach (VeRadio r in f.Codec) Radio(o, t, r);
            o.Add("[fieldset]"); o.Add("[legend]"); t(VehicleEncoding.LegendSize);
            o.Add("[field]"); t(VehicleEncoding.SizeLabel); Select(o, t, f.Profile);
            o.Add(Show("quality", f.ShowQuality)); o.Add("[field]"); t(VehicleEncoding.QualityLabel); Select(o, t, f.Quality);
            o.Add(Show("custom", f.ShowCustom)); o.Add("[field]"); t(VehicleEncoding.BitrateLabel); o.Add("[input bitrate=" + f.Bitrate + "]");
            o.Add(Show("target", f.ShowTarget)); o.Add("[check keepOffTarget" + (f.KeepOffTarget ? " checked" : "") + "]"); t(VehicleEncoding.KeepLabel);
            o.Add("[fieldset]"); o.Add("[legend]"); t(VehicleEncoding.LegendOutput);
            o.Add("[choices]");
            foreach (VeRadio r in f.Output) Radio(o, t, r);
            if (f.ReplaceUnavailable) { o.Add("[hint]"); t(VehicleEncoding.ReplaceHint); }
            o.Add(Show("separate", f.ShowSeparate)); o.Add("[field]"); t(VehicleEncoding.FolderLabel); o.Add("[input destinationRoot=" + f.Destination + " placeholder=" + f.DestinationPlaceholder + "]");
            o.Add(Show("replace", f.ShowReplace)); o.Add("[check replacementConfirmed" + (f.ReplacementConfirmed ? " checked" : "") + "]"); t(VehicleEncoding.ConfirmLabel);
            o.Add("[fieldset]"); o.Add("[legend]"); t(VehicleEncoding.LegendVerify);
            o.Add("[choices]");
            foreach (VeRadio r in f.Verify) Radio(o, t, r);
            o.Add("[hint]"); t(VehicleEncoding.VerifyHint);
            o.Add("[fieldset]"); o.Add("[legend]"); t(VehicleEncoding.LegendGroups);
            o.Add("[ol]");
            for (int i = 0; i < f.Groups.Count; i++)
            {
                VeGroup g = f.Groups[i];
                o.Add("[li " + g.Id + "]");
                o.Add("[check group=" + g.Id + (g.Checked ? " checked" : "") + "]"); t(g.Text);
                o.Add("[btn" + (i == 0 ? " disabled" : "") + " aria=" + VehicleEncoding.Earlier + "]"); t("↑");
                o.Add("[btn" + (i == f.Groups.Count - 1 ? " disabled" : "") + " aria=" + VehicleEncoding.Later + "]"); t("↓");
            }
            o.Add("[hint]"); t(VehicleEncoding.OrderHint);
            o.Add("[fieldset]"); o.Add("[legend]"); t(VehicleEncoding.LegendDevices);
            if (f.Lanes) { o.Add("[p]"); t(f.DevicesText); o.Add("[hint]"); t(VehicleEncoding.LanesHint); }
            else { o.Add("[field]"); t(VehicleEncoding.ConcurrencyLabel); o.Add("[input concurrency=" + f.Concurrency + "]"); o.Add("[hint]"); t(VehicleEncoding.ConcurrencyHint); }
            o.Add("[fieldset]"); o.Add("[legend]"); t(VehicleEncoding.LegendAutomatic);
            o.Add("[check automatic" + (f.Automatic ? " checked" : "") + "]"); t(f.AutomaticLabel);
            o.Add(Show("automatic", f.ShowAutomatic));
            o.Add("[field]"); t(VehicleEncoding.HowOften); Select(o, t, f.Interval);
            o.Add("[field]"); t(VehicleEncoding.When); Select(o, t, f.Window);
            o.Add(Show("hours", f.ShowHours)); o.Add("[choices]");
            o.Add("[field]"); t(VehicleEncoding.From); Select(o, t, f.StartHour);
            o.Add("[field]"); t(VehicleEncoding.To); Select(o, t, f.EndHour);
            o.Add("[hint]"); t(VehicleEncoding.SharedHint);
            o.Add(f.Error == null ? "[error hidden]" : "[error]"); t(f.Error);
            o.Add("[actions]");
            o.Add("[btn solid" + (f.Saving ? " disabled" : "") + "]"); t(VehicleEncoding.SaveLabel);
            o.Add("[btn]"); t(VehicleEncoding.CancelLabel);
            if (f.CanForget) { o.Add("[btn]"); t(VehicleEncoding.ForgetLabel); }
        }
    }
}
