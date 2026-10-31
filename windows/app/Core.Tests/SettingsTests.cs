using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Settings (the Settings page): the requests, the page
    /// in every state, and every button and field (requests, dialogs,
    /// toasts, redraws, the route, the page after), against what web/app.js
    /// does with the same answers (Fixtures/expected-settings.json, written by
    /// test/windowsNativeSettings.test.js).
    /// </summary>
    static class SettingsTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-settings.json")));

        /// <summary>Answers by "METHOD path" as the web test's pretend api(): a GET without an answer fails, the others answer { ok: true }.</summary>
        sealed class ScriptApi : IJsonApi
        {
            readonly JsonValue answers;
            public readonly List<string> Asked = new List<string>();

            public ScriptApi(JsonValue answers) { this.answers = answers ?? JsonValue.Object(); }

            public JsonValue Answers => answers;

            Task<JsonValue> Answer(string method, string path, JsonValue body)
            {
                string key = method + " " + path;
                Asked.Add(body != null ? key + " " + body.ToJson() : key);
                if (!answers.Has(key))
                {
                    if (method == "GET") return Task.FromException<JsonValue>(new ApiError(404, "NOT_FOUND", "no answer for " + key));
                    return Task.FromResult(JsonValue.Object("ok", true));
                }
                JsonValue a = answers[key];
                if (a.IsObject && a.Has("$error")) return Task.FromException<JsonValue>(new ApiError(500, null, a["$error"].Str));
                return Task.FromResult(JsonValue.Parse(a.ToJson()));
            }

            public Task<JsonValue> Get(string path, CancellationToken ct) => Answer("GET", path, null);
            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) => Answer("POST", path, body);
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => Answer("PUT", path, body);
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => Answer("DELETE", path, body);
            public Task<byte[]> GetBytes(string path, CancellationToken ct) => Task.FromException<byte[]>(new ApiError(404, "NOT_FOUND", "no bytes"));
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => Answer("POST", path, null);

            /// <summary>The web test's fetch() of the pack upload.</summary>
            public Task<JsonValue> Upload()
            {
                const string key = "UPLOAD /api/ui-packs/upload";
                Asked.Add(key);
                JsonValue a = answers[key];
                if (a.IsObject && a.Has("$error")) return Task.FromException<JsonValue>(new ApiError(500, null, a["$error"].Str));
                return Task.FromResult(JsonValue.Parse(a.ToJson()));
            }
        }

        static string Diff(List<string> want, List<string> got)
        {
            int i = 0;
            while (i < want.Count && i < got.Count && want[i] == got[i]) i++;
            string w = string.Join(" | ", want.Skip(Math.Max(0, i - 3)).Take(8));
            string g = string.Join(" | ", got.Skip(Math.Max(0, i - 3)).Take(8));
            return "first difference at token " + i + ":\n      web " + w + "\n      C#  " + g;
        }

        static List<string> Strings(JsonValue arr) => arr.Items.Select(x => x.Str).ToList();
        static List<string> Toasts(JsonValue arr) => arr.Items.Select(t => t["bad"].Bool + " " + t["text"].Str).ToList();
        static string Dialog(JsonValue d) => d["title"].Str + " | " + string.Join(" / ", Strings(d["body"])) + " | " + d["ok"].Str + " | " + d["danger"].Bool;
        static string Dialog(SetConfirm c) => c.Title + " | " + string.Join(" / ", Settings.ConfirmBody(c)) + " | " + c.Ok + " | " + c.Danger;

        static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        static SettingsView Page(ScriptApi api, JsonValue c)
        {
            SettingsData d = Settings.Load(api, CancellationToken.None).GetAwaiter().GetResult();
            SettingsView v = Settings.View(d, c["sub"].Str ?? "", c["vehicles"], c["deployment"].Str ?? "docker", "UTC", c["origin"].Str ?? "https://127.0.0.1:45119", c["h24"].Bool);
            if (v.Form != null && v.Form.Mac) Settings.LoadDrives(api, v.Form).GetAwaiter().GetResult();
            return v;
        }

        /// <summary>A field the web test types into (step "set").</summary>
        static void Set(SettingsView v, string what, JsonValue value)
        {
            string[] p = what.Split('.');
            string s = value.Str ?? "";
            bool on = value.Bool;
            SetVehicleForm f = v.Form;
            switch (p[0])
            {
                case "veh":
                    switch (p[1])
                    {
                        case "name": f.Name = s; break;
                        case "timezone": f.Timezone = s; break;
                        case "share": f.Share = s; break;
                        case "username": f.Username = s; break;
                        case "password": f.Password = s; break;
                        case "domain": f.Domain = s; break;
                        case "version": if (f.Versions.Any(o => o.Key == s)) f.Version = s; break;
                        case "port": f.Port = s; break;
                        case "path": f.Path = s; break;
                        case "encrypt": f.Encrypt = on; break;
                        default: throw new AssertFailed("no field " + what);
                    }
                    break;
                case "pw": if (p[1] == "current") v.PwCurrent = s; else v.PwNext = s; break;
                case "sec-hosts": v.Security.NamesValue = s; break;
                case "power-form":
                    if (p[1] == "currency") v.Power.Currency = s;
                    else if (p[1] == "pricePerKwh") v.Power.PricePerKwh = s;
                    else v.Power.DefaultPrice = s;
                    break;
                case "rate":
                    SetRate r = v.Power.Rates[int.Parse(p[1])];
                    if (p[2] == "day") { int d = int.Parse(p[3]); if (on) r.Days.Add(d); else r.Days.Remove(d); }
                    else if (p[2] == "label") r.Label = s;
                    else if (p[2] == "price") r.Price = s;
                    else if (p[2] == "from") r.From = s;
                    else r.To = s;
                    break;
                default: throw new AssertFailed("no field " + what);
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("settings: every page in expected-settings.json, from C# (requests, outline)", () => WithUtc(() =>
            {
                var file = Expected();
                var bad = new List<string>();
                int n = 0;
                foreach (JsonValue c in file["pages"].Items)
                {
                    n++;
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    string error = null;
                    SettingsView v = null;
                    try { v = Page(api, c); }
                    catch (ApiError e) { error = e.Message; }
                    if ((c["error"].Str ?? "") != (error ?? "")) bad.Add(name + ": error " + error);
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (error != null) continue;
                    var want = Strings(c["outline"]);
                    var got = Settings.Outline(v);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                }
                Assert.True(n >= 25, "the pages are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("settings: every step in expected-settings.json, from C# (requests, dialogs, toasts, redraws, routes, page after)", () => WithUtc(() =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["actions"].Items)
                {
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    SettingsView v = Page(api, c);
                    api.Asked.Clear();
                    var toasts = new List<string>();
                    var dialogs = new List<string>();
                    var navs = new List<string>();
                    var stored = new List<string>();
                    int renders = 0, reloads = 0, confirms = 0;
                    JsonValue power = api.Answers["GET /api/settings"]["power"];
                    if (!power.Bool) power = JsonValue.Null;
                    var ui = new SettingsUi
                    {
                        Toast = (t, b) => toasts.Add(b + " " + t),
                        Confirm = d =>
                        {
                            dialogs.Add(Dialog(d));
                            JsonValue yes = c["confirm"].IsArray ? c["confirm"][confirms] : c["confirm"];
                            confirms++;
                            return Task.FromResult(yes.Bool);
                        },
                        Render = () => renders++,
                        Navigate = r => navs.Add("#/" + r),
                        ReloadVehicles = async () => { try { await api.Get("/api/vehicles", CancellationToken.None); } catch (ApiError) { } },
                        PickVehicle = id => stored.Add("vehicle=" + id),
                        InterfaceChanged = () => { reloads++; return Task.FromResult(0); },
                        CertificateChanged = fp => { reloads++; return Task.FromResult(0); },
                        PowerSaved = p => power = p
                    };
                    bool thisPc = Settings.ThisPc(c["hostname"].Str);
                    foreach (JsonValue s in c["steps"].Items)
                    {
                        string kind = s[0].Str, what = s[1].Str;
                        if (kind == "set") { Set(v, what, s[2]); continue; }
                        int colon = what.IndexOf(':');
                        string target = colon < 0 ? what : what.Substring(0, colon);
                        string arg = colon < 0 ? null : what.Substring(colon + 1);
                        Task run = Task.FromResult(0);
                        switch (target)
                        {
                            case "tab": navs.Add("replace #/" + Settings.PickTab(v, arg)); break;
                            case "src": v.Form.Type = arg; break;
                            case "mode": Settings.PickMode(v.Power, arg); break;
                            case "veh-submit": run = Settings.SaveVehicle(api, ui, v.Form, c["vehicles"]); break;
                            case "veh-del": run = Settings.RemoveVehicle(api, ui, v.Form.Editing); break;
                            case "smb-test": run = Settings.TestSmb(api, v.Form, () => { }); break;
                            case "upd-check": run = Settings.CheckUpdates(api, ui, v.Updates); break;
                            case "pw-submit":
                                run = Settings.ChangePassword(api, ui, v.PwCurrent, v.PwNext).ContinueWith(t => { if (t.Result) { v.PwCurrent = ""; v.PwNext = ""; } }, TaskContinuationOptions.ExecuteSynchronously);
                                break;
                            case "ui-pick": run = Settings.UsePack(api, ui, v.Interface, arg); break;
                            case "ui-del": run = Settings.DeletePack(api, ui, v.Interface, arg); break;
                            case "ui-upload": run = Settings.Upload(api, ui, v.Interface, api.Upload); break;
                            case "sec-submit": run = Settings.SaveHostnames(api, ui, Settings.SplitNames(v.Security.NamesValue)); break;
                            case "sec-add-host": run = Settings.SaveHostnames(api, ui, Settings.WithHost(v.Security)); break;
                            case "net-other":
                                v.Security.NetOn = !v.Security.NetOn;
                                run = Settings.SetOtherDevices(api, ui, v.Security.NetOn, thisPc);
                                break;
                            case "rate-del": Settings.RemoveRateAt(v.Power, int.Parse(arg)); break;
                            case "power-add": Settings.AddRate(v.Power); break;
                            case "power-submit": run = Settings.SavePower(api, ui, v.Power); break;
                            default: throw new AssertFailed(name + ": no step " + what);
                        }
                        run.GetAwaiter().GetResult();
                    }
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (!Toasts(c["toasts"]).SequenceEqual(toasts)) bad.Add(name + ": toasts " + string.Join(" || ", toasts));
                    if (!c["dialogs"].Items.Select(Dialog).SequenceEqual(dialogs)) bad.Add(name + ": dialogs " + string.Join(" || ", dialogs));
                    if (c["renders"].Long != renders) bad.Add(name + ": renders " + renders);
                    if (c["reloads"].Long != reloads) bad.Add(name + ": reloads " + reloads);
                    if (!Strings(c["navs"]).SequenceEqual(navs)) bad.Add(name + ": navs " + string.Join(", ", navs));
                    if (!Strings(c["stored"]).SequenceEqual(stored)) bad.Add(name + ": stored " + string.Join(", ", stored));
                    if (c["power"].ToJson() != power.ToJson()) bad.Add(name + ": power " + power.ToJson());
                    var want = Strings(c["outline"]);
                    var got = Settings.Outline(v);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                }
                Assert.True(file["actions"].Count >= 50, "the actions are read");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("settings: numbers, names, zones", () =>
            {
                Assert.Equal(0.15, Settings.Price(" 0,15 ").Value);
                Assert.Equal(16.0, Settings.Price("0x10").Value);
                Assert.True(double.IsNaN(Settings.Price("15 ct").Value), "15 ct is NaN");
                Assert.True(!Settings.Price("   ").HasValue, "empty is null");
                Assert.True(double.IsNaN(Settings.Price("1,0,0").Value), "only the first comma is a point (1.0,0 is NaN)");
                Assert.Equal("a|b|c", string.Join("|", Settings.SplitNames(" a,, b\tc ")));
                Assert.True(Settings.ThisPc("127.0.0.1") && Settings.ThisPc("LOCALHOST") && Settings.ThisPc("[::1]"), "this PC");
                Assert.True(!Settings.ThisPc("192.168.1.20") && !Settings.ThisPc("::1"), "another device");
                Assert.Equal("vehicle-1", Settings.Editing("vehicle/vehicle-1"));
                Assert.Equal("new", Settings.Editing("vehicle-new"));
                Assert.Equal(null, Settings.Editing("security"));
                Assert.Equal("America/Chicago", Settings.IanaZone(TimeZoneInfo.CreateCustomTimeZone("Central Standard Time", TimeSpan.FromHours(-6), "x", "x")));
                Assert.Equal("Europe/Berlin", Settings.IanaZone(TimeZoneInfo.CreateCustomTimeZone("W. Europe Standard Time", TimeSpan.FromHours(1), "x", "x")));
                Assert.Equal("Europe/Paris", Settings.IanaZone(TimeZoneInfo.CreateCustomTimeZone("Europe/Paris", TimeSpan.FromHours(1), "x", "x")));
                Assert.Equal("Please lengthen this text to 8 characters or more (you are currently using 3 characters).", Settings.TooShort("abc", 8));
                Assert.Equal(null, Settings.TooShort("", 8), "empty is the required check's");
                Assert.Equal("3|3.1.1|3.02", string.Join("|", Settings.Entries(JsonValue.Parse("{\"3.1.1\":1,\"3.02\":2,\"3\":3}")).Select(e => e.Key)));
            });
        }
    }
}
