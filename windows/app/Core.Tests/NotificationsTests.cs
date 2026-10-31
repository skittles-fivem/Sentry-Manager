using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Notifications (the Notifications page): the request,
    /// the page, and what Dismiss does (the requests, the shell's open
    /// notifications read again, the redraw), against what web/app.js does
    /// with the same answers (Fixtures/expected-notifications.json, written
    /// by test/windowsNativeNotifications.test.js).
    /// </summary>
    static class NotificationsTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-notifications.json")));

        /// <summary>Answers by "METHOD path" as the web test's pretend api(): a GET without an answer fails, the others answer { ok: true }.</summary>
        sealed class ScriptApi : IJsonApi
        {
            readonly JsonValue answers;
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

        static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        public static void Register(Action<string, Action> test)
        {
            test("notifications: every page in expected-notifications.json, from C# (request, outline)", () => WithUtc(() =>
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
                    NotesView v = null;
                    try { v = Notifications.View(Notifications.Load(api, CancellationToken.None).GetAwaiter().GetResult(), c["h24"].Bool); }
                    catch (ApiError e) { error = e.Message; }
                    catch (InvalidOperationException e) { error = e.Message; }
                    if ((c["error"].Str ?? "") != (error ?? "")) bad.Add(name + ": error " + error);
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (error != null) continue;
                    var want = Strings(c["outline"]);
                    var got = Notifications.Outline(v);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                }
                Assert.True(n >= 9, "the pages are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("notifications: Dismiss in expected-notifications.json, from C# (requests, badge, redraw)", () => WithUtc(() =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["actions"].Items)
                {
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    int renders = 0;
                    var ui = new NotesUi
                    {
                        // the web's loadNotes(): GET /api/notifications?open=1, not caught
                        ReloadNotes = () => api.Get("/api/notifications?open=1", CancellationToken.None),
                        Render = () => renders++
                    };
                    NotesView v = Notifications.View(Notifications.Load(api, CancellationToken.None).GetAwaiter().GetResult(), false);
                    // the Dismiss buttons are the open items, in order (data-dismiss)
                    List<NoteRow> open = v.Rows.Where(r => !r.Resolved).ToList();
                    bool failed = false;
                    try { Notifications.Dismiss(api, ui, open[(int)c["index"].Num].Id).GetAwaiter().GetResult(); }
                    catch (Exception) { failed = true; }
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (c["renders"].Long != renders) bad.Add(name + ": renders " + renders);
                    if (c["failed"].Bool != failed) bad.Add(name + ": failed " + failed);
                }
                Assert.True(file["actions"].Count >= 5, "the actions are read");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("notifications: the paths and the dots", () =>
            {
                Assert.Equal("/api/notifications/n1/dismiss", Notifications.DismissPath("n1"));
                var v = Notifications.View(JsonValue.Parse("{\"notifications\":[{\"id\":\"a\",\"level\":\"critical\",\"title\":\"t\",\"createdAt\":1},{\"id\":\"b\",\"level\":\"info\",\"createdAt\":1},{\"id\":\"c\",\"level\":\"warning\",\"createdAt\":1},{\"id\":\"d\",\"level\":\"critical\",\"createdAt\":1,\"resolvedAt\":2}]}"), false);
                Assert.Equal("bad|ok|warn|", string.Join("|", v.Rows.Select(r => r.Dot)));
                Assert.Equal(true, v.Rows[3].Resolved, "resolvedAt is set");
                Assert.Equal(0, Notifications.View(JsonValue.Parse("{\"notifications\":[]}"), false).Rows.Count);
            });
        }
    }
}
