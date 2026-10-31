using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Nodes page (Core/Horizon/HzNodes.cs) against the pack's own
    /// hz/pages/nodes.js on the same answers, clicks, picks, typing and timers,
    /// and hz/nodelanes.js (Core/Horizon/HzNodeLanes.cs) on the same devices
    /// (Fixtures/expected-horizon-nodes.json, written by
    /// test/windowsNativeHorizonNodes.test.js).
    /// </summary>
    static class HorizonNodesTests
    {
        /// <summary>The answers, each one after a turn of the loop (as fetch's promise is): a second click meanwhile finds the first one still saving.</summary>
        sealed class Later : IJsonApi
        {
            readonly IJsonApi inner;
            public Later(IJsonApi inner) { this.inner = inner; }
            static async Task<T> After<T>(Task<T> t) { await Task.Yield(); return await t; }
            public Task<JsonValue> Get(string path, CancellationToken ct) => After(inner.Get(path, ct));
            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) => After(inner.Post(path, body, ct));
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => After(inner.Put(path, body, ct));
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => After(inner.Delete(path, body, ct));
            public Task<byte[]> GetBytes(string path, CancellationToken ct) => inner.GetBytes(path, ct);
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => inner.PostBytes(path, data, contentType, headers, ct);
        }

        /// <summary>The dialog as the harness reads the web's askText(): title, body, label, [input v=…], the two buttons.</summary>
        static List<string> TextOutline(HzAskText a)
        {
            var o = new List<string>();
            Action<string> Add = s => { string t = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (t.Length > 0) o.Add(t); };
            Add(a.Title);
            foreach (HzPara p in a.Body) foreach (RichRun r in p.Text.Runs) Add(r.Text);
            Add(a.Label);
            o.Add("[input v=" + a.Value + "]");
            o.Add("[btn no]"); Add("Cancel");
            o.Add("[btn yes go]"); Add(a.Yes);
            return o;
        }

        sealed class Window
        {
            public readonly HzUi Ui = new HzUi();
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Dialogs = new List<string>();
            public string Status;
            readonly Queue<string> prompts = new Queue<string>();

            public Window(bool confirm, JsonValue prompts)
            {
                foreach (JsonValue p in prompts.Items) this.prompts.Enqueue(p.IsNull ? null : p.Str);
                Ui.Toast = (t, tone) => { lock (Toasts) Toasts.Add(tone + " " + t); };
                Ui.Status = s => Status = s;
                Ui.Ask = a =>
                {
                    Dialogs.Add(string.Join(" | ", a.Outline()) + " => " + (confirm ? "yes" : "no"));
                    return Task.FromResult(confirm);
                };
                Ui.AskText = a =>
                {
                    string v = this.prompts.Count > 0 ? this.prompts.Dequeue() : null;
                    Dialogs.Add(string.Join(" | ", TextOutline(a)) + " => " + (v == null ? "cancel" : "type " + v));
                    return Task.FromResult(v);
                };
            }
        }

        static async Task Quiet(Task t)
        {
            try { await t; } catch (Exception) { }
        }

        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon nodes: every session in expected-horizon-nodes.json, from C# (requests, outline, status, toasts, dialogs, the timed draws after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-nodes.json");
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var raw = new PackScript.Api(s["answers"]);
                    IJsonApi api = new Later(raw);
                    var win = new Window(s["confirm"].Bool, s["prompts"]);
                    HzUi ui = win.Ui;
                    List<HzCar> cars = s["cars"].Items.Select(c => new HzCar(Hz.Js(c["id"]), Hz.Js(c["name"]))).ToList();
                    var prefs = new Dictionary<string, string>();
                    foreach (var p in s["storage"].Props) prefs[p.Key] = p.Value.Str;
                    long now = e["now"].Long;
                    bool h24 = s["h24"].Bool;
                    string host = s["host"].Str, port = s["port"].Str;
                    HzNodes m = new HzNodes();
                    long? pollAt = null;
                    // what the page shows is as of its last draw (the pairing code's minutes, "seen …")
                    long drawnAt = now;
                    Action<HzNodesDraw> Schedule = r =>
                    {
                        if (r == HzNodesDraw.Gone) return;
                        if (r == HzNodesDraw.Drawn) drawnAt = now;
                        pollAt = m.Later > 0 ? now + m.Later : (long?)null;
                    };
                    Func<Task> Draw = async () => { Schedule(await m.Draw(api, ui, true, null)); };
                    string error = null;
                    try { await Draw(); }
                    catch (Exception ex) { error = ex.Message; }
                    Assert.Equal(s["error"].Str, error, name + ": error");
                    int toasts = 0, dialogs = 0;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        int from = st.IsNull ? 0 : raw.Asked.Count;
                        if (!st.IsNull)
                        {
                            HzNodesView before = m.View(cars, drawnAt, h24, host, port);
                            Func<string, HzNodeCard> Card = id => before.Nodes.First(c => c.Id == id);
                            Func<Task> one = null;
                            if (st.Has("wait"))
                            {
                                long until = now + st["wait"].Long;
                                while (pollAt.HasValue && pollAt.Value <= until)
                                {
                                    now = pollAt.Value;
                                    pollAt = null;
                                    try { Schedule(await m.Draw(api, ui, false, null)); } catch (Exception) { }
                                }
                                now = until;
                            }
                            else if (st.Has("field")) { if (st["field"].Str == "address") m.Address = st["value"].Str; else m.Code = st["value"].Str; }
                            else if (st.Has("submit")) one = () => m.Pair(api, ui, null, Draw);
                            else if (st.Has("add")) one = () => m.AddCar(api, ui, st["add"].Str, st["car"].Str, Draw);
                            else if (st.Has("rm")) one = () => m.RemoveCar(api, ui, st["rm"].Str, st["car"].Str, Draw);
                            else if (st.Has("up"))
                            {
                                HzNodeCar c = Card(st["up"].Str).Cars.First(x => x.Id == st["car"].Str);
                                if (c.CanUp) one = () => m.MoveCarUp(api, ui, st["up"].Str, st["car"].Str, Draw);
                            }
                            else if (st.Has("trust")) { bool want = !Card(st["trust"].Str).Trusted; one = () => m.Trust(api, ui, st["trust"].Str, want, Draw); }
                            else if (st.Has("rename")) one = () => m.RenameNode(api, ui, st["rename"].Str, Draw);
                            else if (st.Has("remove")) one = () => m.RemoveNode(api, ui, st["remove"].Str, Draw);
                            else if (st.Has("bench"))
                            {
                                HzNodeCard c = Card(st["bench"].Str);
                                bool enabled = c.Bench != null ? !c.Bench.Running : c.CanBench && !c.BenchDisabled;
                                if (enabled) one = () => HzNodes.StartBench(api, ui, st["bench"].Str, st["codec"].Str, Draw);
                            }
                            else if (st.Has("bstop")) one = () => HzNodes.StopBench(api, ui, st["bstop"].Str, Draw);
                            else if (st.Has("buse"))
                            {
                                string level = Card(st["buse"].Str).Bench.UseLevel;
                                one = () => m.Use(api, ui, st["buse"].Str, level, () => prefs.TryGetValue(HzNodes.FormKey, out string f) ? f : null, f => prefs[HzNodes.FormKey] = f, Draw);
                            }
                            else if (st.Has("worker")) { bool want = !before.WorkerChecked; one = () => m.SetWorker(api, ui, want, Draw); }
                            else if (st.Has("pair")) one = () => HzNodes.PairStart(api, ui, Draw);
                            else if (st.Has("cancel")) one = () => HzNodes.PairCancel(api, ui, Draw);
                            else if (st.Has("revoke")) one = () => m.RevokeMain(api, ui, st["revoke"].Str, null, Draw);
                            if (one != null)
                            {
                                Task a = Quiet(one());
                                Task b = st["twice"].Bool ? Quiet(one()) : Task.FromResult(0);
                                await a;
                                await b;
                            }
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(raw.Asked, from), what + ": requests");
                        Assert.Equal(step["status"].Str, win.Status, what + ": status");
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = win.Toasts.Count;
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs.Skip(dialogs).ToList(), what + ": dialogs");
                        dialogs = win.Dialogs.Count;
                        prefs.TryGetValue(HzNodes.FormKey, out string form);
                        Assert.Equal(step["encForm"].Str, form, what + ": horizon.enc.form");
                        string polls = string.Join(",", step["polls"].Items.Select(x => x.Long.ToString()));
                        Assert.Equal(polls, pollAt.HasValue ? (pollAt.Value - now).ToString() : "", what + ": the timed draw");
                        if (error != null) continue;
                        PackScript.Same(PackScript.Strings(step["outline"]), HzNodes.Outline(m.View(cars, drawnAt, h24, host, port)), what + ": outline");
                    }
                }
            }));

            test("horizon nodes: hz/nodelanes.js in expected-horizon-nodes.json, from C# (node keys, groups, labels, saved lanes, job lanes, open groups)", () =>
            {
                JsonValue l = PackScript.Fixture("expected-horizon-nodes.json")["lanes"];
                JsonValue devs = l["devices"];
                string[] ids = { "node:n1", "node:n1@gpu:0", "node:n1@", "gpu0", "", null, "node:", "node:a@b@c", "xnode:n1" };
                for (int i = 0; i < ids.Length; i++) Assert.Equal(l["nodeKeyOf"][i].Str, HzNodeLanes.NodeKeyOf(ids[i]), "nodeKeyOf " + ids[i]);
                Assert.Equal(l["nodeKeyOf"][9].Str, HzNodeLanes.NodeKeyOf(JsonValue.Number(7)), "nodeKeyOf 7");
                for (int i = 0; i < devs.Count; i++)
                {
                    Assert.Equal(l["isNodeDevice"][i].Bool, HzNodeLanes.IsNodeDevice(devs[i]), "isNodeDevice " + i);
                    Assert.Equal(l["labels"][i].Str, HzNodeLanes.DeviceLabel(devs[i]), "deviceLabel " + i);
                }
                HzNodeLanes.SplitDevices(devs, out List<JsonValue> own, out List<HzNodeGroup> groups);
                Assert.Equal(l["split"]["own"].ToJson(), JsonValue.Array(own.Select(d => d["id"])).ToJson(), "own devices");
                Assert.Equal(l["split"]["nodes"].ToJson(), JsonValue.Array(groups.Select(g => JsonValue.Object("key", g.Key, "name", g.Name, "online", g.Online, "devices", JsonValue.Array(g.Devices.Select(d => d["id"]))))).ToJson(), "node groups");
                foreach (JsonValue sv in l["saved"].Items)
                {
                    for (int i = 0; i < devs.Count; i++)
                    {
                        double? lane = HzNodeLanes.SavedLane(sv["saved"], devs[i]);
                        Assert.Equal(sv["lane"][i].ToJson(), lane.HasValue ? JsonValue.Number(lane.Value).ToJson() : "null", "savedLane " + sv["saved"].ToJson() + " " + i);
                        Assert.Equal(sv["value"][i].Num, HzNodeLanes.LaneValue(sv["saved"], devs[i], 2), "laneValue " + sv["saved"].ToJson() + " " + i);
                    }
                }
                string[] rows = { "node:n1", "node:n1@cpu", "node:n2", "node:n3", "node:n9", "gpu0", "node:n1@zz" };
                for (int i = 0; i < rows.Length; i++) Assert.Equal(l["rowIdOf"][i].Str, HzNodeLanes.RowIdOf(devs, JsonValue.String(rows[i])).Str, "rowIdOf " + rows[i]);
                JsonValue jl = JsonValue.Parse("[{\"device\":\"node:n1\",\"concurrency\":2},{\"device\":\"node:n1@gpu:0\",\"concurrency\":5},{\"device\":\"gpu0\",\"concurrency\":1},{\"device\":\"node:n9\",\"concurrency\":3}]");
                Assert.Equal(l["jobLanes"].ToJson(), JsonValue.Array(HzNodeLanes.JobLanes(devs, jl).Select(p => JsonValue.Array(new[] { p.Key, p.Value }))).ToJson(), "jobLanes");
                Assert.Equal(0, HzNodeLanes.JobLanes(devs, JsonValue.String("nope")).Count, "jobLanes of no list");
                Assert.Equal(l["bodyIds"][0].Str, HzNodeLanes.GroupBodyId("enc", "node:n1"), "body id");
                Assert.Equal(l["bodyIds"][1].Str, HzNodeLanes.GroupBodyId("vp", "node:a b/c"), "body id");
                Assert.Equal(l["bodyIds"][2].Str, HzNodeLanes.GroupBodyId("x", "7"), "body id");
                var store = new Dictionary<string, string> { { "horizon.groups", "{\"enc|node:n1\":true,\"enc|node:n2\":false}" }, { "horizon.bad", "[1,2]" } };
                Func<string, string> get = k => store.TryGetValue(k, out string v) ? v : null;
                Action<string, string> set = (k, v) => store[k] = v;
                var memory = new HzNodeLanes.Memory(get, set, "groups");
                var bad = new HzNodeLanes.Memory(get, set, "bad");
                JsonValue m0 = l["memory"][0];
                Assert.Equal(m0["open"].ToJson(), JsonValue.Array(new[] { "enc|node:n1", "enc|node:n2", "x" }.Select(k => memory.IsOpen(k) ? JsonValue.True : JsonValue.False)).ToJson(), "open groups");
                Assert.Equal(m0["bad"].Bool, bad.IsOpen("0"), "a stored list is no groups");
                memory.Set("enc|node:n2", true);
                memory.Set("enc|node:n1", false);
                bad.Set("a", true);
                Assert.Equal(l["memory"][1]["groups"].Str, store["horizon.groups"], "groups after");
                Assert.Equal(l["memory"][1]["bad"].Str, store["horizon.bad"], "list after");
                return Task.CompletedTask;
            });

            test("horizon nodes: the Encoder form after Use N at once, the request paths", () =>
            {
                Assert.Equal("{\"lanes\":{\"node:n1\":2}}", HzNodes.FormAfterUse(null, "n1", null, 2), "nothing saved");
                Assert.Equal("{\"lanes\":{\"node:n1:gpu:0\":3}}", HzNodes.FormAfterUse("{\"lanes\":{\"node:n1\":1}}", "n1", "node:n1:gpu:0", 3), "the whole-node lane goes");
                Assert.Equal("{\"lanes\":{\"node:n1\":2}}", HzNodes.FormAfterUse("not json", "n1", "", 2), "unreadable");
                Assert.Equal("{\"0\":\"a\",\"lanes\":{\"node:x\":1}}", HzNodes.FormAfterUse("\"a\"", "x", null, 1), "a string spreads its letters");
                return Task.CompletedTask;
            });
        }
    }
}
