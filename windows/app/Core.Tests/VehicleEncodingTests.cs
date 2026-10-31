using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's VehicleEncoding (the "Encoding per vehicle" page and
    /// the words every interface shows for a vehicle's saved settings),
    /// against what web/vehicleEncoding.js does with the same answers
    /// (Fixtures/expected-vehicle-encoding.json, written by
    /// test/windowsNativeVehicleEncoding.test.js): the words, the folders and
    /// defaults, save()'s requests, every page and every session of clicks
    /// and typing (what is shown after each step, the form's values, the
    /// requests, toasts and questions, the focus after an order button).
    /// </summary>
    static class VehicleEncodingTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-vehicle-encoding.json")));

        /// <summary>The web test's pretend server: GET /api/vehicles and the Encoder's calls by name (a list answers in turn).</summary>
        sealed class ScriptApi : IJsonApi
        {
            readonly JsonValue setup;
            public readonly List<string> Calls = new List<string>();
            readonly Dictionary<string, int> count = new Dictionary<string, int>();

            public ScriptApi(JsonValue setup) { this.setup = setup; }

            static Task<JsonValue> Answer(JsonValue a)
            {
                if (a.IsObject && a.Has("$status")) return Task.FromException<JsonValue>(new ApiError((int)a["$status"].Long, null, a["$error"].Str ?? "Internal Server Error"));
                return Task.FromResult(JsonValue.Parse(a.ToJson()));
            }

            public Task<JsonValue> Get(string path, CancellationToken ct)
            {
                Calls.Add(Call("GET " + path, null));
                return Answer(setup["vehicles"]);
            }

            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct)
            {
                string name = path.Replace("/api/converter/invoke/", "");
                Calls.Add(Call(name, body));
                count[name] = count.TryGetValue(name, out int n) ? n + 1 : 1;
                JsonValue script = setup["api"][name];
                if (!setup["api"].Has(name)) return Task.FromResult(JsonValue.Parse("{\"ok\":true,\"value\":null}"));
                if (script.IsArray) script = script[Math.Min(script.Count - 1, count[name] - 1)];
                return Answer(script);
            }

            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => throw new InvalidOperationException("PUT " + path);
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => throw new InvalidOperationException("DELETE " + path);
            public Task<byte[]> GetBytes(string path, CancellationToken ct) => throw new InvalidOperationException("BYTES " + path);
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => throw new InvalidOperationException("POST " + path);
        }

        static string Call(string name, JsonValue body) => name + " " + (body == null ? "null" : body.ToJson());

        static List<string> Calls(JsonValue list) => list.Items.Select(c => Call(c[0].Str, c[1].IsNull ? null : JsonValue.Parse(c[1].ToJson()))).ToList();

        static string Diff(string want, string got)
        {
            var w = want.Split('\n');
            var g = got.Split('\n');
            int i = 0;
            while (i < w.Length && i < g.Length && w[i] == g[i]) i++;
            return "first difference at token " + i + ":\n      web " + string.Join(" | ", w.Skip(Math.Max(0, i - 3)).Take(7)) + "\n      C#  " + string.Join(" | ", g.Skip(Math.Max(0, i - 3)).Take(7));
        }

        static string Rows(List<KeyValuePair<string, string>> rows) => string.Join("\n", rows.Select(r => r.Key + " = " + r.Value));
        static string Rows(JsonValue rows) => string.Join("\n", rows.Items.Select(r => r[0].Str + " = " + r[1].Str));

        static JsonValue Undef(JsonValue owner, string key) => owner.Has(key) ? owner[key] : null;

        /// <summary>A session with the view: the web's mount, draw and handlers, with the C#.</summary>
        sealed class Session
        {
            public readonly VehicleEncodingView View;
            public readonly ScriptApi Api;
            public readonly VeUi Ui;
            public List<VeCard> Cards;
            public VeForm Form;
            public string LoadError;
            public string Thrown;
            public bool Answer = true;
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Confirms = new List<string>();

            public Session(JsonValue setup, string edit)
            {
                View = new VehicleEncodingView(edit);
                Api = new ScriptApi(setup);
                Ui = new VeUi
                {
                    Confirm = t => { Confirms.Add(t); return Task.FromResult(Answer); },
                    Toast = (t, bad) => Toasts.Add(t + (bad ? " (bad)" : "")),
                    Draw = Draw
                };
                try
                {
                    View.Load(Api, CancellationToken.None).GetAwaiter().GetResult();
                    Cards = View.Cards();
                    Form = Cards.Select(c => c.Editor).FirstOrDefault(f => f != null);
                }
                catch (Exception e) { LoadError = VehicleEncoding.ErrorText(e); }
            }

            /// <summary>draw(): an error leaves the page as it was (the click handler's uncaught error).</summary>
            public void Draw()
            {
                try
                {
                    Cards = View.Cards();
                    Form = Cards.Select(c => c.Editor).FirstOrDefault(f => f != null);
                }
                catch (Exception e) { Thrown = e.Message; }
            }

            public string Outline() => string.Join("\n", VehicleEncodingView.Outline(Cards ?? new List<VeCard>(), LoadError));
        }

        static List<string> Toasts(JsonValue list) => list.Items.Select(t => t[0].Str + (t[1].Bool ? " (bad)" : "")).ToList();

        public static void Register(Action<string, Action> test)
        {
            test("vehicle encoding: the words (describe, summary) in expected-vehicle-encoding.json, from C#", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue w in file["words"].Items)
                {
                    JsonValue settings = w["settingsMissing"].Bool ? null : w["settings"];
                    string got = Rows(VehicleEncoding.Describe(w["config"], w["automatic"].Bool, settings, w["devices"]));
                    string want = Rows(w["describe"]);
                    if (got != want) bad.Add(w["config"].ToJson() + " / " + (settings == null ? "undefined" : settings.ToJson()) + ": " + Diff(want, got));
                }
                foreach (JsonValue s in file["summaries"].Items)
                {
                    string got = VehicleEncoding.Summary(s["config"]);
                    if (got != s["summary"].Str) bad.Add("summary " + s["config"].ToJson() + ": " + got);
                }
                Assert.True(file["words"].Count >= 70, "the words are read");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad.Take(20)));
            });

            test("vehicle encoding: folderOf, savedFor and defaults in expected-vehicle-encoding.json, from C#", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue f in file["folders"].Items)
                {
                    JsonValue folder = VehicleEncoding.FolderOf(f["vehicle"]);
                    string want = f["folderMissing"].Bool ? "undefined" : f["folder"].ToJson();
                    string got = folder == null ? "undefined" : folder.ToJson();
                    if (want != got) bad.Add(f["vehicle"].ToJson() + ": folder " + got);
                    JsonValue hit = VehicleEncoding.SavedFor(file["state"], folder, out bool automatic);
                    string savedGot = hit == null ? "null" : JsonValue.Object("config", hit, "automatic", automatic).ToJson();
                    if (savedGot != f["saved"].ToJson()) bad.Add(f["vehicle"].ToJson() + ": saved " + savedGot);
                    string defaults = JsonValue.Array(new[] { VehicleEncoding.Defaults(folder, true), VehicleEncoding.Defaults(folder, false) }).ToJson();
                    if (defaults != f["defaults"].ToJson()) bad.Add(f["vehicle"].ToJson() + ": defaults " + defaults);
                }
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("vehicle encoding: save() in expected-vehicle-encoding.json, from C# (setAuto, saveVehicle)", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["saves"].Items)
                {
                    var api = new ScriptApi(JsonValue.Parse("{\"api\":{\"setAuto\":{\"ok\":true,\"value\":{\"from\":\"setAuto\"}},\"saveVehicle\":{\"ok\":true,\"value\":{\"from\":\"saveVehicle\"}}}}"));
                    JsonValue folder = c["folder"];
                    JsonValue result = VehicleEncoding.Save(api, c["state"], folder, JsonValue.Object("sourceRoot", folder), c["automatic"].Bool, c["schedule"], CancellationToken.None).GetAwaiter().GetResult();
                    if (!Calls(c["calls"]).SequenceEqual(api.Calls)) bad.Add(c["name"].Str + ": calls " + string.Join(" ; ", api.Calls));
                    if (result.ToJson() != c["result"].ToJson()) bad.Add(c["name"].Str + ": result " + result.ToJson());
                }
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("vehicle encoding: every page in expected-vehicle-encoding.json, from C# (requests, outline)", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue p in file["pages"].Items)
                {
                    var s = new Session(p["setup"], p["edit"].Str);
                    string name = p["name"].Str;
                    if (!Calls(p["calls"]).SequenceEqual(s.Api.Calls)) bad.Add(name + ": calls " + string.Join(" ; ", s.Api.Calls));
                    string got = s.Outline();
                    if (got != p["outline"].Str) bad.Add(name + ": " + Diff(p["outline"].Str, got));
                }
                Assert.True(file["pages"].Count >= 20, "the pages are read");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("vehicle encoding: every session in expected-vehicle-encoding.json, from C# (each step's page, form values, requests, toasts, questions, focus)", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                int steps = 0;
                foreach (JsonValue f in file["flows"].Items)
                {
                    string name = f["name"].Str;
                    var s = new Session(f["setup"], f["edit"].Str);
                    if (s.Outline() != f["first"]["outline"].Str) bad.Add(name + " (first): " + Diff(f["first"]["outline"].Str, s.Outline()));
                    int seenCalls = s.Api.Calls.Count, seenToasts = 0, seenConfirms = 0;
                    int i = 0;
                    foreach (JsonValue step in f["steps"].Items)
                    {
                        steps++;
                        i++;
                        string where = name + " step " + i + " " + step["step"].ToJson();
                        JsonValue d = step["step"];
                        s.Thrown = null;
                        s.Answer = !d.Has("answer") || d["answer"].Bool;
                        string focus = null;
                        string click = d["click"].Str;
                        if (click == "edit")
                        {
                            VeCard card = s.Cards.FirstOrDefault(c => c.Editor == null && c.Id == d["id"].Str);
                            if (card != null) { s.View.Edit(card.Id); s.Draw(); }
                        }
                        else if (click == "cancel") { s.View.Cancel(); s.Draw(); }
                        else if (click == "forget") s.View.Forget(s.Api, s.Ui, d["id"].Str, CancellationToken.None).GetAwaiter().GetResult();
                        else if (click == "up" || click == "down")
                        {
                            string g = d["group"].Str;
                            bool up = click == "up";
                            if (s.View.Move(s.Form, g, up))
                            {
                                s.Draw();
                                int at = s.Form.Groups.FindIndex(x => x.Id == g);
                                bool enabled = up ? at > 0 : at < s.Form.Groups.Count - 1;
                                if (enabled) focus = click + ":" + g;
                            }
                        }
                        else if (d.Has("check")) { s.Form.Check(d["check"].Str, d["value"].Str); s.Form.Sync(); }
                        else if (d.Has("tick")) { s.Form.Tick(d["tick"].Str, d["value"].Str, d["on"].Bool); s.Form.Sync(); }
                        else if (d.Has("set")) { s.Form.Set(d["set"].Str, d["value"].Str); s.Form.Sync(); }
                        else if (d.Has("submit")) s.View.Submit(s.Api, s.Ui, s.Form, CancellationToken.None).GetAwaiter().GetResult();

                        string got = s.Outline();
                        if (got != step["outline"].Str) bad.Add(where + ": " + Diff(step["outline"].Str, got));
                        string data = s.Form == null || !s.Cards.Any(c => c.Editor == s.Form) ? "null" : string.Join("&", s.Form.Data().Select(kv => kv.Key + "=" + kv.Value));
                        string wantData = step["data"].IsNull ? "null" : string.Join("&", step["data"].Items.Select(kv => kv[0].Str + "=" + kv[1].Str));
                        if (data != wantData) bad.Add(where + ": form " + data + "\n      web " + wantData);
                        var calls = s.Api.Calls.Skip(seenCalls).ToList();
                        seenCalls = s.Api.Calls.Count;
                        if (!Calls(step["calls"]).SequenceEqual(calls)) bad.Add(where + ": calls " + string.Join(" ; ", calls) + "\n      web " + string.Join(" ; ", Calls(step["calls"])));
                        var toasts = s.Toasts.Skip(seenToasts).ToList();
                        seenToasts = s.Toasts.Count;
                        if (!Toasts(step["toasts"]).SequenceEqual(toasts)) bad.Add(where + ": toasts " + string.Join(" ; ", toasts));
                        var confirms = s.Confirms.Skip(seenConfirms).ToList();
                        seenConfirms = s.Confirms.Count;
                        if (!step["confirms"].Items.Select(x => x.Str).SequenceEqual(confirms)) bad.Add(where + ": confirms " + string.Join(" ; ", confirms));
                        if ((step["focus"].Str ?? "") != (focus ?? "")) bad.Add(where + ": focus " + focus);
                        if ((step["thrown"].Str ?? "") != (s.Thrown ?? "")) bad.Add(where + ": thrown " + s.Thrown);
                    }
                }
                Assert.True(steps >= 60, "the steps are read (" + steps + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad.Take(20)));
            });

            test("vehicle encoding: the ?edit= of a sub page and JavaScript's Number()", () =>
            {
                Assert.Equal("1", VehicleEncoding.EditParam("vehicles?edit=1"));
                Assert.Equal("a b", VehicleEncoding.EditParam("vehicles?x=2&edit=a+b"));
                Assert.Equal("m/3", VehicleEncoding.EditParam("vehicles?edit=m%2F3&edit=2"));
                Assert.Equal(null, VehicleEncoding.EditParam("vehicles"));
                Assert.Equal(0.0, VehicleEncoding.StringNumber(" "));
                Assert.Equal(16.0, VehicleEncoding.StringNumber("0x10"));
                Assert.True(double.IsNaN(VehicleEncoding.StringNumber("1,5")), "1,5 is NaN");
                Assert.Equal(1e2, VehicleEncoding.StringNumber(" 1e2 "));
                Assert.Equal("00:00", VehicleEncoding.Hour("0"));
                Assert.Equal("undefined:00", VehicleEncoding.Hour(null));
            });
        }
    }
}
