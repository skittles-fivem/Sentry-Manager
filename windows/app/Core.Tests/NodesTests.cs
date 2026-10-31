using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Nodes (the Nodes page): the requests, the page, and
    /// what each button does (requests, dialogs, toasts, the redraw, the
    /// Encoder's converterLanes), against what web/app.js does with the same
    /// answers (Fixtures/expected-nodes.json, written by
    /// test/windowsNativeNodes.test.js).
    /// </summary>
    static class NodesTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-nodes.json")));

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
        static List<string> Toasts(JsonValue arr) => arr.Items.Select(t => t["bad"].Bool + " " + t["text"].Str).ToList();
        static string Dialog(JsonValue d) => d["title"].Str + " | " + string.Join(" / ", Strings(d["body"])) + " | " + d["ok"].Str + " | " + d["danger"].Bool;
        static string Dialog(NodesConfirm c) => c.Title + " | " + string.Join(" / ", c.Body) + " | " + c.Ok + " | " + c.Danger;

        static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        public static void Register(Action<string, Action> test)
        {
            test("nodes: every page in expected-nodes.json, from C# (requests, outline, poll)", () => WithUtc(() =>
            {
                var file = Expected();
                long now = file["now"].Long;
                var bad = new List<string>();
                int n = 0;
                foreach (JsonValue c in file["pages"].Items)
                {
                    n++;
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    string error = null;
                    NodesData data = null;
                    try { data = Nodes.Load(api, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (ApiError e) { error = e.Message; }
                    if ((c["error"].Str ?? "") != (error ?? "")) bad.Add(name + ": error " + error);
                    if (!Strings(c["asked"]).SequenceEqual(api.Asked)) bad.Add(name + ": asked " + string.Join(", ", api.Asked));
                    if (error != null) continue;
                    NodesView v = Nodes.View(data, c["host"].Str, c["port"].Str, c["h24"].Bool, now);
                    var want = Strings(c["outline"]);
                    var got = Nodes.Outline(v);
                    if (!want.SequenceEqual(got)) bad.Add(name + ": " + Diff(want, got));
                    if (c["poll"].Long != v.PollMs) bad.Add(name + ": poll " + v.PollMs);
                }
                Assert.True(n >= 12, "the pages are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("nodes: every button in expected-nodes.json, from C# (requests, dialogs, toasts, redraw, lanes)", () => WithUtc(() =>
            {
                var file = Expected();
                long now = file["now"].Long;
                var bad = new List<string>();
                foreach (JsonValue c in file["actions"].Items)
                {
                    string name = c["name"].Str;
                    var api = new ScriptApi(c["answers"]);
                    NodesView v = Nodes.View(Nodes.Load(api, CancellationToken.None).GetAwaiter().GetResult(), "192.168.1.20", "9000", false, now);
                    int before = api.Asked.Count;
                    var toasts = new List<string>();
                    var dialogs = new List<string>();
                    var systems = new List<string>();
                    int renders = 0;
                    string lanes = c["lanes"].Str;
                    var ui = new NodesUi
                    {
                        Toast = (t, b) => toasts.Add(b + " " + t),
                        Confirm = d => { dialogs.Add(Dialog(d)); return Task.FromResult(c["confirm"].Bool); },
                        SystemConfirm = t => { systems.Add(t); return c["system"].Bool; },
                        Prompt = cur =>
                        {
                            dialogs.Add(Nodes.RenameTitle + " | " + Nodes.RenameLabel + " / " + Nodes.RenameHint + " | Save | False");
                            return Task.FromResult(c["confirm"].Bool ? c["prompt"].Str : null);
                        },
                        GetLanes = () => lanes,
                        SetLanes = s => lanes = s,
                        Render = () => renders++
                    };
                    JsonValue target = c["target"];
                    string id = target["id"].Str;
                    NodeRow row = v.Nodes.FirstOrDefault(x => x.Id == id);
                    bool? buttonDisabled = null;
                    bool checkedAfter = false;
                    switch (target["sel"].Str ?? target["el"].Str ?? (target["submit"].Bool ? "submit" : ""))
                    {
                        case "submit":
                            bool ok = Nodes.Add(api, ui, c["form"]["address"].Str, c["form"]["code"].Str).GetAwaiter().GetResult();
                            buttonDisabled = ok;
                            break;
                        case "benchStart":
                            Nodes.StartBench(api, ui, id, target["codec"].Str ?? row.Bench.Codec).GetAwaiter().GetResult();
                            break;
                        case "benchCancel":
                            Nodes.CancelBench(api, ui, id).GetAwaiter().GetResult();
                            break;
                        case "benchUse":
                            Nodes.UseBench(api, ui, row, row.Bench.Suggested).GetAwaiter().GetResult();
                            break;
                        case "nodeTrust":
                            checkedAfter = Nodes.SetTrust(api, ui, id, target["check"].Bool).GetAwaiter().GetResult() && target["check"].Bool;
                            break;
                        case "carAdd":
                            Nodes.AddCar(api, ui, row, target["value"].Str).GetAwaiter().GetResult();
                            break;
                        case "carRemove":
                            Nodes.RemoveCar(api, ui, row, target["car"].Str).GetAwaiter().GetResult();
                            break;
                        case "carUp":
                            Nodes.MoveCarUp(api, ui, row, target["car"].Str).GetAwaiter().GetResult();
                            break;
                        case "renameNode":
                            Nodes.Rename(api, ui, row).GetAwaiter().GetResult();
                            break;
                        case "removeNode":
                            Nodes.Remove(api, ui, row).GetAwaiter().GetResult();
                            break;
                        case "revokeMain":
                            Nodes.Revoke(api, ui, v.Mains.First(m => m.Id == id)).GetAwaiter().GetResult();
                            break;
                        case "worker-on":
                            Nodes.SetWorker(api, ui, target["check"].Bool).GetAwaiter().GetResult();
                            checkedAfter = target["check"].Bool;
                            break;
                        case "pair-start":
                            Nodes.PairStart(api, ui).GetAwaiter().GetResult();
                            break;
                        case "pair-cancel":
                            Nodes.PairCancel(api, ui).GetAwaiter().GetResult();
                            break;
                        case "nodes-open":
                            Nodes.AllowOtherDevices(api, ui).GetAwaiter().GetResult();
                            break;
                        default:
                            bad.Add(name + ": no such control");
                            continue;
                    }
                    var asked = api.Asked.Skip(before).ToList();
                    // (here the new certificate is read instead of reloading the page)
                    if (name == "open") asked.Remove("GET /api/security");
                    if (!Strings(c["asked"]).SequenceEqual(asked)) bad.Add(name + ": asked " + string.Join(", ", asked));
                    if (!Toasts(c["toasts"]).SequenceEqual(toasts)) bad.Add(name + ": toasts " + string.Join(" || ", toasts));
                    if (!c["dialogs"].Items.Select(Dialog).SequenceEqual(dialogs)) bad.Add(name + ": dialogs " + string.Join(" || ", dialogs));
                    if (!Strings(c["systems"]).SequenceEqual(systems)) bad.Add(name + ": confirm " + string.Join(" || ", systems));
                    if (c["renders"].Long != renders) bad.Add(name + ": renders " + renders);
                    if ((c["lanesAfter"].Str ?? "") != (lanes ?? "")) bad.Add(name + ": lanes " + lanes);
                    if (!c["buttonDisabled"].IsNull && c["buttonDisabled"].Bool != buttonDisabled) bad.Add(name + ": button disabled " + buttonDisabled);
                    if (target.Has("check") && c["checked"].Bool != checkedAfter) bad.Add(name + ": checked " + checkedAfter);
                }
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            }));

            test("nodes: the address a main server types, the lanes, one change at a time", () =>
            {
                string h, p;
                Nodes.HostParts("localhost:9000", out h, out p);
                Assert.Equal("localhost", Nodes.PairAddress(h, p));
                Nodes.HostParts("nas.local", out h, out p);
                Assert.Equal("nas.local", Nodes.PairAddress(h, p), "default port: location.port is empty");
                Nodes.HostParts("[fe80::1]:9443", out h, out p);
                Assert.Equal("[fe80::1]:9443", Nodes.PairAddress(h, p));
                Nodes.HostParts("192.168.1.20:8443", out h, out p);
                Assert.Equal("192.168.1.20:8443", Nodes.PairAddress(h, p));
                Assert.Equal(null, Nodes.LanesAfterUse("{not json", "n1", null, 2), "a broken preference is left alone");
                Assert.Equal("{\"node:n1\":2}", Nodes.LanesAfterUse("{\"node:n1\":5}", "n1", null, 2));
                // a second change while one saves is ignored (app.js once())
                var gate = new TaskCompletionSource<string>();
                var api = new ScriptApi(JsonValue.Object());
                var row = new NodeRow { Id = "n1", Name = "A" };
                var ui = new NodesUi { Prompt = cur => gate.Task };
                Task first = Nodes.Rename(api, ui, row);
                Assert.True(Nodes.Saving, "saving while the dialog is open");
                Nodes.Remove(api, ui, row).GetAwaiter().GetResult();
                gate.SetResult("B");
                first.GetAwaiter().GetResult();
                Assert.Equal("PUT /api/nodes/n1 {\"name\":\"B\"}", string.Join(", ", api.Asked), "only the first change ran");
                Assert.True(!Nodes.Saving, "done");
            });
        }
    }
}
