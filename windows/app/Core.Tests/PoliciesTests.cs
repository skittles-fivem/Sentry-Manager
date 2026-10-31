using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Policies (the Policies page): the request, the page,
    /// and what Save policy and Run now do (requests, dialog, toasts, the
    /// redraw), against what web/app.js does with the same answers
    /// (Fixtures/expected-policies.json, written by
    /// test/windowsNativePolicies.test.js); and Chrome's number and time
    /// field rules the native fields follow.
    /// </summary>
    static class PoliciesTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-policies.json")));

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
        static string Dialog(PoliciesConfirm c) => c.Title + " | " + c.Text + " | " + c.Ok + " | False";

        /// <summary>The form as the web's FormData reads it (a missing checkbox is off).</summary>
        static PolicyForm Form(JsonValue f)
        {
            var form = new PolicyForm { Enabled = f["enabled"].Str == "on", RunAt = f["runAt"].Str ?? "" };
            for (int i = 0; i < Policies.Categories.Length; i++) form.Keep[i] = f[Policies.KeepName(Policies.Categories[i])].Str ?? "";
            return form;
        }

        static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        public static void Register(Action<string, Action> test)
        {
            test("policies: every page in expected-policies.json, from C# (request, outline)", () => WithUtc(() =>
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
                    PolicyView v = null;
                    if (!car.IsNull)
                    {
                        try { v = Policies.View(Policies.Load(api, car["id"].Str, CancellationToken.None).GetAwaiter().GetResult(), car["id"].Str, car["name"].Str, c["h24"].Bool); }
                        catch (ApiError e) { error = e.Message; }
                    }
                    if ((c["error"].Str ?? "") != (error ?? "")) bad.Add(name + ": error " + error);
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (error != null) continue;
                    var want = Strings(c["outline"]);
                    var got = Policies.Outline(v);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                }
                Assert.True(n >= 10, "the pages are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("policies: Save policy and Run now in expected-policies.json, from C# (requests, dialog, toasts, redraw)", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["actions"].Items)
                {
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    Policies.Load(api, "model-3", CancellationToken.None).GetAwaiter().GetResult();
                    int before = api.Asked.Count;
                    var toasts = new List<string>();
                    var dialogs = new List<string>();
                    int renders = 0;
                    var ui = new PoliciesUi
                    {
                        Toast = (t, b) => toasts.Add(b + " " + t),
                        Confirm = d => { dialogs.Add(Dialog(d)); return Task.FromResult(c["confirm"].Bool); },
                        Render = () => renders++
                    };
                    PolicyForm form = Form(c["form"]);
                    if (c["what"].Str == "submit") Policies.Save(api, ui, "model-3", form).GetAwaiter().GetResult();
                    else Policies.RunNow(api, ui, "model-3", form).GetAwaiter().GetResult();
                    var asked = api.Asked.Skip(before).ToList();
                    if (!Strings(c["asked"]).SequenceEqual(asked)) bad.Add(name + ": asked " + string.Join(", ", asked));
                    if (!Toasts(c["toasts"]).SequenceEqual(toasts)) bad.Add(name + ": toasts " + string.Join(" || ", toasts));
                    if (!c["dialogs"].Items.Select(Dialog).SequenceEqual(dialogs)) bad.Add(name + ": dialogs " + string.Join(" || ", dialogs));
                    if (c["renders"].Long != renders) bad.Add(name + ": renders " + renders);
                }
                Assert.True(file["actions"].Count >= 10, "the actions are read");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("policies: Chrome's number and time fields (value, messages, arrow keys)", () =>
            {
                Assert.Equal("30", Policies.NumberValue("30"));
                Assert.Equal("090", Policies.NumberValue("090"), "the text as typed");
                Assert.Equal("1e2", Policies.NumberValue("1e2"));
                Assert.Equal(".5", Policies.NumberValue(".5"));
                Assert.Equal("", Policies.NumberValue("1."), "not a number: the value is empty");
                Assert.Equal("", Policies.NumberValue("-"));
                Assert.Equal("", Policies.NumberValue("+3"));
                Assert.Equal("", Policies.NumberValue("1e999"), "not finite");
                Assert.Equal(null, Policies.NumberMessage("", 1, 3650), "empty is allowed (forever)");
                Assert.Equal(null, Policies.NumberMessage("3650", 1, 3650));
                Assert.Equal("Please enter a number.", Policies.NumberMessage("1e", 1, 3650));
                Assert.Equal("Value must be greater than or equal to 1.", Policies.NumberMessage("0", 1, 3650));
                Assert.Equal("Value must be greater than or equal to 1.", Policies.NumberMessage("-4", 1, 3650));
                Assert.Equal("Value must be less than or equal to 3650.", Policies.NumberMessage("3651", 1, 3650));
                Assert.Equal("Please enter a valid value. The two nearest valid values are 2 and 3.", Policies.NumberMessage("2.5", 1, 3650));
                Assert.Equal("Please enter a valid value. The nearest valid value is 3.", Policies.NumberMessage("3.5", 1, 3.7));
                Assert.Equal("1", Policies.NumberStep("", 1, 1, 3650), "empty starts at min");
                Assert.Equal("1", Policies.NumberStep("", -1, 1, 3650));
                Assert.Equal("31", Policies.NumberStep("30", 1, 1, 3650));
                Assert.Equal("29", Policies.NumberStep("30", -1, 1, 3650));
                Assert.Equal("3", Policies.NumberStep("2.5", 1, 1, 3650), "onto the step");
                Assert.Equal("2", Policies.NumberStep("2.5", -1, 1, 3650));
                Assert.Equal("3650", Policies.NumberStep("3650", 1, 1, 3650), "stays at max");
                Assert.Equal("1", Policies.NumberStep("1", -1, 1, 3650), "stays at min");
                Assert.Equal("1", Policies.NumberStep("-5", 1, 1, 3650));
                Assert.Equal("3650", Policies.NumberStep("9000", -1, 1, 3650));
                int h, m;
                Assert.True(Policies.ParseTime("03:30", out h, out m) && h == 3 && m == 30, "03:30");
                Assert.True(Policies.ParseTime("23:59:10", out h, out m) && h == 23 && m == 59, "with seconds");
                Assert.True(!Policies.ParseTime("3:30", out h, out m) && h == -1, "not HH:MM");
                Assert.True(!Policies.ParseTime("24:00", out h, out m), "24:00");
                Assert.True(!Policies.ParseTime(null, out h, out m), "null");
                Assert.Equal("04:05", Policies.TimeValue(4, 5));
                Assert.Equal("", Policies.TimeValue(-1, 5), "incomplete");
                Assert.Equal("{\"enabled\":false,\"runAt\":\"\",\"keepDays\":{\"Recent\":null,\"Sentry\":null,\"Saved\":null,\"Other\":null}}", new PolicyForm().Body().ToJson());
            });
        }
    }
}
