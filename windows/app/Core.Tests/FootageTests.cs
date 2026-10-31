using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Footage (the Footage page): the requests, the page,
    /// the files, the dialogs and toasts of trash/deleteDays/protect and the
    /// Index speeds button, against what web/app.js does with the same
    /// answers (Fixtures/expected-footage.json, written by
    /// test/windowsNativeFootage.test.js).
    /// </summary>
    static class FootageTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-footage.json")));

        /// <summary>Answers by "METHOD path" (an array answer: one item per call, in turn); records every call as the web test does.</summary>
        sealed class ScriptApi : IJsonApi
        {
            readonly Dictionary<string, Queue<JsonValue>> queues = new Dictionary<string, Queue<JsonValue>>();
            readonly JsonValue answers;
            public readonly List<string> Asked = new List<string>();

            public ScriptApi(JsonValue answers) { this.answers = answers ?? JsonValue.Object(); }

            public void Queue(string key, IEnumerable<JsonValue> items) => queues[key] = new Queue<JsonValue>(items);

            Task<JsonValue> Answer(string method, string path, JsonValue body)
            {
                string key = method + " " + path;
                Asked.Add(body != null ? key + " " + body.ToJson() : key);
                JsonValue a;
                if (queues.TryGetValue(key, out var q)) a = q.Count > 0 ? q.Dequeue() : JsonValue.Null;
                else a = answers[key];
                if (a.IsNull && !answers.Has(key) && !queues.ContainsKey(key)) return Task.FromException<JsonValue>(new ApiError(404, "NOT_FOUND", "no answer for " + key));
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
            return $"first difference at token {i}:\n      web {w}\n      C#  {g}";
        }

        static List<string> Strings(JsonValue arr) => arr.Items.Select(x => x.Str).ToList();

        static string StateJson(FootageState f) =>
            JsonValue.Object("category", f.Category, "date", f.Date, "offset", f.Offset, "vehicleId", f.VehicleId,
                "selected", f.Selected.ToList().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                "expanded", f.Expanded.ToList().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                "open", f.Open.ToList().OrderBy(x => x, StringComparer.Ordinal).ToList()).ToJson();

        static string Canon(JsonValue v) => JsonValue.Parse(v.ToJson()).ToJson();

        static List<string> Toasts(List<KeyValuePair<string, bool>> l) => l.Select(t => t.Value + " " + t.Key).ToList();
        static List<string> Toasts(JsonValue arr) => arr.Items.Select(t => t["bad"].Bool + " " + t["text"].Str).ToList();

        static string Dialog(FootageConfirm c) => c.Title + " | " + string.Join(" / ", c.Body) + " | " + c.Ok + " | " + c.Danger;
        static string Dialog(JsonValue d) => d["title"].Str + " | " + string.Join(" / ", Strings(d["body"])) + " | " + d["ok"].Str + " | " + d["danger"].Bool;

        static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        public static void Register(Action<string, Action> test)
        {
            test("footage: every page in expected-footage.json, from C# (requests, state, outline)", () => WithUtc(() =>
            {
                var file = Expected();
                long now = file["now"].Long;
                var bad = new List<string>();
                int n = 0;
                foreach (JsonValue c in file["pages"].Items)
                {
                    n++;
                    string name = c["name"].Str;
                    var f = FootageState.From(c["state"]);
                    var api = new ScriptApi(c["answers"]);
                    List<string> got;
                    if (c["vehicle"].IsNull) got = Footage.Outline(Footage.NoVehicleView());
                    else
                    {
                        string vid = c["vehicle"]["id"].Str;
                        JsonValue data = null;
                        string failed = null;
                        try { data = Footage.Load(api, vid, f, CancellationToken.None).GetAwaiter().GetResult(); }
                        catch (ApiError e) { failed = e.Message; }
                        got = Footage.Outline(failed != null ? Footage.FailedView(failed) : Footage.View(vid, data, f, c["h24"].Bool, now));
                        // (the page asks the speed index's status after drawing, as followIndex does)
                        if (failed == null) api.Asked.Add("GET /api/speeds/status");
                        if (StateJson(f) != Canon(c["after"])) bad.Add(name + ": state " + StateJson(f) + " != " + Canon(c["after"]));
                    }
                    var want = Strings(c["outline"]);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                }
                Assert.True(n >= 15, "the pages are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("footage: a recording's files, from C#", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["files"].Items)
                {
                    string name = c["name"].Str;
                    var api = new ScriptApi(JsonValue.Object("GET " + Footage.DetailPath("model-3", c["id"].Str), c["detail"]));
                    List<string> all, plain = null;
                    try
                    {
                        var x = Footage.Files(api.Get(Footage.DetailPath("model-3", c["id"].Str), CancellationToken.None).GetAwaiter().GetResult());
                        all = Footage.Outline(x, false);
                        if (x.Head != null) plain = Footage.Outline(x, true);
                    }
                    catch (ApiError e)
                    {
                        all = new List<string> { "[li]", e.Message };
                    }
                    if (!Strings(c["outline"]).SequenceEqual(all)) bad.Add(name + ": " + Diff(Strings(c["outline"]), all));
                    var wantPlain = c["plain"].IsNull ? null : Strings(c["plain"]);
                    if ((wantPlain == null) != (plain == null) || (plain != null && !wantPlain.SequenceEqual(plain))) bad.Add(name + " (not encoded only): " + (plain == null ? "none" : Diff(wantPlain ?? new List<string>(), plain)));
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                }
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("footage: Move to trash, Delete this day / through this date and Protect, from C#", () => WithUtc(() =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["trash"].Items)
                {
                    var f = new FootageState();
                    foreach (JsonValue id in c["ids"].Items) f.Selected.Add(id.Str);
                    f.Selected.Add("other");
                    f.Recordings = c["known"].Items.ToList();
                    var api = new ScriptApi(JsonValue.Object("POST /api/vehicles/model-3/trash", c["answer"]));
                    var dialogs = new List<FootageConfirm>();
                    var toasts = new List<KeyValuePair<string, bool>>();
                    Footage.Trash(api, "model-3", f, Strings(c["ids"]), d => { dialogs.Add(d); return Task.FromResult(true); }, (t, b) => toasts.Add(new KeyValuePair<string, bool>(t, b))).GetAwaiter().GetResult();
                    string name = "trash " + c["name"].Str;
                    if (!c["dialogs"].Items.Select(Dialog).SequenceEqual(dialogs.Select(Dialog))) bad.Add(name + ": dialog " + string.Join(" || ", dialogs.Select(Dialog)));
                    if (!Toasts(c["toasts"]).SequenceEqual(Toasts(toasts))) bad.Add(name + ": toasts " + string.Join(" || ", Toasts(toasts)));
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (!Strings(c["selected"]).SequenceEqual(f.Selected.ToList().OrderBy(x => x, StringComparer.Ordinal))) bad.Add(name + ": selection");
                }
                foreach (JsonValue c in file["deletes"].Items)
                {
                    var f = new FootageState { Category = c["category"].Str };
                    f.Selected.Add("x");
                    f.Selected.Add("y");
                    var api = new ScriptApi(JsonValue.Object());
                    var answers = new List<JsonValue> { c["preview"] };
                    if (!c["answer"].IsNull) answers.Add(c["answer"]);
                    api.Queue("POST /api/vehicles/model-3/trash-days", answers);
                    var dialogs = new List<FootageConfirm>();
                    var toasts = new List<KeyValuePair<string, bool>>();
                    bool ok = !c["cancel"].Bool;
                    Footage.DeleteDays(api, "model-3", f, c["day"].Str, c["through"].Bool, d => { dialogs.Add(d); return Task.FromResult(ok); }, (t, b) => toasts.Add(new KeyValuePair<string, bool>(t, b))).GetAwaiter().GetResult();
                    string name = "delete " + c["name"].Str;
                    if (!c["dialogs"].Items.Select(Dialog).SequenceEqual(dialogs.Select(Dialog))) bad.Add(name + ": dialog " + string.Join(" || ", dialogs.Select(Dialog)));
                    if (!Toasts(c["toasts"]).SequenceEqual(Toasts(toasts))) bad.Add(name + ": toasts " + string.Join(" || ", Toasts(toasts)));
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (c["cleared"].Bool != (f.Selected.Count == 0)) bad.Add(name + ": selection");
                }
                foreach (JsonValue c in file["protect"].Items)
                {
                    var api = new ScriptApi(JsonValue.Object("POST /api/vehicles/model-3/protect", c["answer"]));
                    var toasts = new List<KeyValuePair<string, bool>>();
                    Footage.Protect(api, "model-3", Strings(c["ids"]), c["on"].Bool, (t, b) => toasts.Add(new KeyValuePair<string, bool>(t, b))).GetAwaiter().GetResult();
                    string name = "protect " + c["name"].Str;
                    if (!Toasts(c["toasts"]).SequenceEqual(Toasts(toasts))) bad.Add(name + ": toasts " + string.Join(" || ", Toasts(toasts)));
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                }
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("footage: Index speeds, from C#", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["index"].Items)
                {
                    bool start = c["start"].Bool;
                    var api = new ScriptApi(JsonValue.Object());
                    var statuses = c["statuses"].Items.ToList();
                    api.Queue(start ? "POST /api/speeds/index" : "GET /api/speeds/status", statuses.Take(1));
                    if (start) api.Queue("GET /api/speeds/status", statuses.Skip(1));
                    else api.Queue("GET /api/speeds/status", statuses);
                    var labels = new List<string>();
                    var toasts = new List<KeyValuePair<string, bool>>();
                    Footage.FollowIndex(api, start, () => true, (l, d) => labels.Add(l + " " + d), () => Task.CompletedTask, (t, b) => toasts.Add(new KeyValuePair<string, bool>(t, b))).GetAwaiter().GetResult();
                    string name = "index " + c["name"].Str;
                    var want = c["labels"].Items.Select(l => l["text"].Str + " " + l["disabled"].Bool).ToList();
                    if (!want.SequenceEqual(labels)) bad.Add(name + ": labels " + string.Join(" || ", labels));
                    if (!Toasts(c["toasts"]).SequenceEqual(Toasts(toasts))) bad.Add(name + ": toasts " + string.Join(" || ", Toasts(toasts)));
                    // (the web posts no body: JSON {} is what api() sends for body: {})
                    var asked = api.Asked.Select(a => a.EndsWith(" {}") ? a.Substring(0, a.Length - 3) : a).ToList();
                    if (!Strings(c["asked"]).SequenceEqual(asked)) bad.Add(name + ": asked " + string.Join(", ", asked));
                }
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("footage: the render mode's state, picking a vehicle, encodeURIComponent", () =>
            {
                var f = FootageState.From(JsonValue.Parse("{\"category\":\"Saved\",\"offset\":30,\"open\":[\"2026-10-20\"],\"expanded\":[\"a\"],\"selected\":[\"b\",\"a\",\"b\"]}"));
                Assert.Equal("Saved", f.Category);
                Assert.Equal(30, f.Offset);
                Assert.Equal("b,a", string.Join(",", f.Selected.ToList()), "insertion order, no repeats (a Set)");
                f.VehiclePicked();
                Assert.Equal(0, f.Offset);
                Assert.Equal(0, f.Selected.Count);
                Assert.True(f.Open.Contains("2026-10-20") && f.Expanded.Contains("a"), "open days and shown files stay (the next vehicle resets the days)");
                Assert.Equal("a%20b%2F%C3%A9!'()*~", Footage.EncUri("a b/é!'()*~"));
                Footage.SetDate(f, "2026-01-02");
                Assert.True(f.Open.Contains("2026-01-02") && f.Offset == 0, "a picked date opens");
                Footage.Older(f);
                Footage.Older(f);
                Footage.Newer(f);
                Assert.Equal(30, f.Offset);
            });
        }
    }
}
