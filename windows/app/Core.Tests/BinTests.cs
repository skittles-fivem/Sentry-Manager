using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Bin (the Recycle Bin page): the request, the page,
    /// and what Restore, Delete forever and Empty Recycle Bin do (requests,
    /// dialogs, toasts, the vehicles read again, the redraw), against what
    /// web/app.js does with the same answers (Fixtures/expected-bin.json,
    /// written by test/windowsNativeBin.test.js).
    /// </summary>
    static class BinTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-bin.json")));

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
        static string Dialog(BinConfirm c) => c.Title + " | " + string.Join(" / ", c.Body) + " | " + c.Ok + " | " + c.Danger;

        static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        static string VehicleId(JsonValue car) => car.IsNull ? null : car["id"].Str;

        public static void Register(Action<string, Action> test)
        {
            test("bin: every page in expected-bin.json, from C# (request, outline)", () => WithUtc(() =>
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
                    BinView v = null;
                    try { v = Bin.View(Bin.Load(api, VehicleId(c["vehicle"]), CancellationToken.None).GetAwaiter().GetResult(), c["h24"].Bool); }
                    catch (ApiError e) { error = e.Message; }
                    catch (InvalidOperationException e) { error = e.Message; }
                    if ((c["error"].Str ?? "") != (error ?? "")) bad.Add(name + ": error " + error);
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (error != null) continue;
                    var want = Strings(c["outline"]);
                    var got = Bin.Outline(v);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                }
                Assert.True(n >= 10, "the pages are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("bin: Restore, Delete forever, Empty Recycle Bin in expected-bin.json, from C# (requests, dialogs, toasts, redraw)", () => WithUtc(() =>
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
                    var ui = new BinUi
                    {
                        Toast = (t, b) => toasts.Add(b + " " + t),
                        Confirm = d => { dialogs.Add(Dialog(d)); return Task.FromResult(c["confirm"].Bool); },
                        // the web's loadVehicles(): GET /api/vehicles (no answer here: it fails, and is ignored)
                        ReloadVehicles = () => api.Get("/api/vehicles", CancellationToken.None),
                        Render = () => renders++
                    };
                    JsonValue car = c["vehicle"];
                    string vid = VehicleId(car);
                    BinView v = Bin.View(Bin.Load(api, vid, CancellationToken.None).GetAwaiter().GetResult(), false);
                    JsonValue click = c["click"];
                    string kind = click[0].Str;
                    if (kind == "empty") Bin.Empty(api, ui, v, string.IsNullOrEmpty(vid) ? null : vid, car.IsNull ? null : car["name"].Str).GetAwaiter().GetResult();
                    else if (kind == "restore") Bin.Restore(api, ui, v.Rows[(int)click[1].Num].Id).GetAwaiter().GetResult();
                    else Bin.DeleteForever(api, ui, v.Rows[(int)click[1].Num]).GetAwaiter().GetResult();
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (!Toasts(c["toasts"]).SequenceEqual(toasts)) bad.Add(name + ": toasts " + string.Join(" || ", toasts));
                    if (!c["dialogs"].Items.Select(Dialog).SequenceEqual(dialogs)) bad.Add(name + ": dialogs " + string.Join(" || ", dialogs));
                    if (c["renders"].Long != renders) bad.Add(name + ": renders " + renders);
                }
                Assert.True(file["actions"].Count >= 15, "the actions are read");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("bin: the paths, the bodies, the toasts", () =>
            {
                Assert.Equal("/api/bin", Bin.Path(null));
                Assert.Equal("/api/bin?vehicle=a%20b%26c", Bin.Path("a b&c"));
                Assert.Equal("{\"confirm\":true,\"vehicle\":null}", Bin.EmptyBody(null).ToJson());
                Assert.Equal("{\"confirm\":true,\"vehicle\":null}", Bin.EmptyBody("").ToJson(), "v?.id || null");
                Assert.Equal("{\"confirm\":true,\"vehicle\":\"model-3\"}", Bin.EmptyBody("model-3").ToJson());
                var r = Bin.RestoreToast(JsonValue.Parse("{\"restored\":3,\"conflicts\":0}"));
                Assert.Equal("Restored.", r.Key);
                Assert.Equal(false, r.Value);
                r = Bin.RestoreToast(JsonValue.Parse("{\"restored\":2,\"conflicts\":1}"));
                Assert.Equal("Restored 2 file(s); 1 could not be (a file with that name exists).", r.Key);
                Assert.Equal(true, r.Value);
                var e = Bin.EmptyToast(JsonValue.Parse("{\"deleted\":1,\"bytes\":2048,\"failed\":0}"));
                Assert.Equal("Deleted 1 item (2.0 KiB).", e.Key);
                Assert.Equal(false, e.Value);
                e = Bin.EmptyToast(JsonValue.Parse("{\"deleted\":2,\"bytes\":1024,\"failed\":1}"));
                Assert.Equal(true, e.Value, "failed > 0 is bad");
            });
        }
    }
}
