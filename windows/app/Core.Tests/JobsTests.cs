using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Jobs (the Jobs page): the request, the page, the
    /// Jobs badge, and what Clear jobs does (requests, dialog, toasts, the
    /// redraw), against what web/app.js does with the same answers
    /// (Fixtures/expected-jobs.json, written by test/windowsNativeJobs.test.js).
    /// </summary>
    static class JobsTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-jobs.json")));

        /// <summary>Answers by "METHOD path" as the web test's pretend api(): a GET without an answer fails, the others answer { ok: true }; { $seq: [...] } answers in turn.</summary>
        sealed class ScriptApi : IJsonApi
        {
            readonly JsonValue answers;
            readonly Dictionary<string, int> used = new Dictionary<string, int>();
            public readonly List<string> Asked = new List<string>();

            public ScriptApi(JsonValue answers) { this.answers = answers ?? JsonValue.Object(); }

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
                if (a.IsObject && a.Has("$seq"))
                {
                    int i;
                    used.TryGetValue(key, out i);
                    used[key] = i + 1;
                    a = a["$seq"][Math.Min(i, a["$seq"].Count - 1)];
                }
                if (a.IsObject && a.Has("$error")) return Task.FromException<JsonValue>(new ApiError(500, null, a["$error"].Str));
                return Task.FromResult(JsonValue.Parse(a.ToJson()));
            }

            public Task<JsonValue> Get(string path, CancellationToken ct) => Answer("GET", path, null);
            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) => Answer("POST", path, body);
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => Answer("PUT", path, body);
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => Answer("DELETE", path, body);
            public Task<byte[]> GetBytes(string path, CancellationToken ct) => Task.FromException<byte[]>(new ApiError(404, "NOT_FOUND", "no bytes"));
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => Answer("POST", path, null);
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
        static string Dialog(JobsConfirm c) => c.Title + " | " + string.Join(" / ", c.Body) + " | " + c.Ok + " | False";

        static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        public static void Register(Action<string, Action> test)
        {
            test("jobs: every page in expected-jobs.json, from C# (request, outline, badge)", () => WithUtc(() =>
            {
                var file = Expected();
                var bad = new List<string>();
                int n = 0;
                foreach (JsonValue c in file["pages"].Items)
                {
                    n++;
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    JsonValue car = c["vehicle"];
                    string error = null;
                    JobsView v = null;
                    try { v = Jobs.View(Jobs.Load(api, car.IsNull ? null : car["id"].Str, CancellationToken.None).GetAwaiter().GetResult(), c["h24"].Bool, c["currency"].Str); }
                    catch (ApiError e) { error = e.Message; }
                    if ((c["error"].Str ?? "") != (error ?? "")) bad.Add(name + ": error " + error);
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (error != null) continue;
                    string badge = v.Running > 0 ? v.Running.ToString() : "";
                    if (c["badge"].Str != badge) bad.Add(name + ": badge " + badge);
                    var want = Strings(c["outline"]);
                    var got = Jobs.Outline(v);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                }
                Assert.True(n >= 10, "the pages are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("jobs: Clear jobs in expected-jobs.json, from C# (requests, dialog, toasts, redraw)", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["actions"].Items)
                {
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    var toasts = new List<string>();
                    var dialogs = new List<string>();
                    int renders = 0;
                    var ui = new JobsUi
                    {
                        Toast = (t, b) => toasts.Add(b + " " + t),
                        Confirm = d => { dialogs.Add(Dialog(d)); return Task.FromResult(c["confirm"].Bool); },
                        Render = () => renders++
                    };
                    JsonValue car = c["vehicle"];
                    Jobs.Clear(api, ui, car.IsNull ? null : car["id"].Str, car.IsNull ? null : car["name"].Str, (int)c["vehicles"].Num).GetAwaiter().GetResult();
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (!Toasts(c["toasts"]).SequenceEqual(toasts)) bad.Add(name + ": toasts " + string.Join(" || ", toasts));
                    if (!c["dialogs"].Items.Select(Dialog).SequenceEqual(dialogs)) bad.Add(name + ": dialogs " + string.Join(" || ", dialogs));
                    if (c["renders"].Long != renders) bad.Add(name + ": renders " + renders);
                }
                Assert.True(file["actions"].Count >= 10, "the actions are read");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("jobs: chip colours, the energy line, the request bodies", () =>
            {
                Assert.Equal("green", Jobs.ToneOf(JsonValue.String("done")));
                Assert.Equal("red", Jobs.ToneOf(JsonValue.String("failed")));
                Assert.Equal("amber", Jobs.ToneOf(JsonValue.String("running")));
                Assert.Equal("", Jobs.ToneOf(JsonValue.String("cancelled")), "cancelled is grey");
                Assert.Equal("", Jobs.ToneOf(JsonValue.String("interrupted")), "interrupted is grey");
                Assert.Equal("", Jobs.ToneOf(JsonValue.Null), "no status");
                Assert.Equal("", Jobs.EnergyOf(JsonValue.Null, "$"), "no measurement");
                Assert.Equal("", Jobs.EnergyOf(JsonValue.Parse("{\"wh\":\"3\"}"), "$"), "a string is not a number");
                Assert.Equal("12.4 Wh", Jobs.EnergyOf(JsonValue.Parse("{\"wh\":12.44}"), "$"));
                Assert.Equal("413 Wh · avg 8 W · ≈ $0.14", Jobs.EnergyOf(JsonValue.Parse("{\"wh\":412.6,\"avgW\":8.04,\"cost\":0.1449}"), "$"));
                Assert.Equal("5.2 kWh · ≈ 154 kr", Jobs.EnergyOf(JsonValue.Parse("{\"wh\":5210,\"cost\":154.4}"), "kr"));
                Assert.Equal("/api/jobs", Jobs.Path(null));
                Assert.Equal("/api/jobs?vehicle=a%20b%26c", Jobs.Path("a b&c"));
                Assert.Equal("{\"vehicle\":null,\"preview\":true}", Jobs.ClearBody(null, "preview").ToJson());
                Assert.Equal("{\"vehicle\":\"model-3\",\"confirm\":true}", Jobs.ClearBody("model-3", "confirm").ToJson());
            });
        }
    }
}
