using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;
using SentrySix.Core.Flux;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Flux's Nodes page (Core/Flux/FxNodes.cs) against the pack's own
    /// flux/pages/nodes.js on the same answers, clicks, picks, typing, focus
    /// and timers (Fixtures/expected-flux-nodes.json, written by
    /// test/windowsNativeFluxNodes.test.js).
    /// </summary>
    static class FluxNodesTests
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

        /// <summary>The dialog as the harness reads the web's promptDialog(): title, body, label, [input v=…], Cancel and the OK button.</summary>
        static List<string> TextOutline(HzAskText a)
        {
            var o = new List<string>();
            Action<string> Add = s => { string t = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); if (t.Length > 0) o.Add(t); };
            Add(a.Title);
            foreach (HzPara p in a.Body) foreach (RichRun r in p.Text.Runs) Add(r.Text);
            Add(a.Label);
            o.Add("[input v=" + a.Value + "]");
            o.Add("[btn cancel]"); Add("Cancel");
            o.Add("[btn ok primary]"); Add(a.Yes);
            return o;
        }

        sealed class Window
        {
            public readonly HzUi Ui = new HzUi();
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Dialogs = new List<string>();
            readonly Queue<string> prompts = new Queue<string>();

            public Window(bool confirm, JsonValue prompts)
            {
                foreach (JsonValue p in prompts.Items) this.prompts.Enqueue(p.IsNull ? null : p.Str);
                Ui.Toast = (t, tone) => { lock (Toasts) Toasts.Add(tone + " " + t); };
                Ui.Ask = a =>
                {
                    Dialogs.Add(string.Join(" | ", a.Outline("cancel", "ok", "primary")) + " => " + (confirm ? "yes" : "no"));
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
            test("flux nodes: every session in expected-flux-nodes.json, from C# (requests, outline, toasts, dialogs, the timed draws after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-nodes.json");
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
                    FxNodes m = new FxNodes();
                    long? pollAt = null;
                    bool focused = false;
                    // what the page shows is as of its last draw (the pairing code's minutes, "seen …")
                    long drawnAt = now;
                    Action<FxNodesDraw> Schedule = r =>
                    {
                        if (r == FxNodesDraw.Gone) return;
                        if (r == FxNodesDraw.Drawn) { drawnAt = now; focused = false; }
                        pollAt = m.Later > 0 ? now + m.Later : (long?)null;
                    };
                    Func<Task> Draw = async () => { Schedule(await m.Draw(api, true, () => focused)); };
                    string error = null;
                    try { await Draw(); }
                    catch (Exception ex) { error = ex.Message; }
                    Assert.Equal(s["error"].Str, error, name + ": error");
                    if (error == null)
                    {
                        JsonValue cmd = s["commands"][0];
                        Assert.Equal(cmd["group"].Str + "|" + cmd["label"].Str + "|" + cmd["icon"].Str, FxNodes.CommandGroup + "|" + FxNodes.CommandLabel + "|" + FxNodes.CommandIcon, name + ": the palette's command");
                    }
                    int toasts = 0, dialogs = 0;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        int from = st.IsNull ? 0 : raw.Asked.Count;
                        if (!st.IsNull)
                        {
                            FxNodesView before = m.View(cars, drawnAt, h24, host, port);
                            Func<string, FxNodesCard> Card = id => before.Nodes.First(c => c.Id == id);
                            Func<Task> one = null;
                            if (st.Has("wait"))
                            {
                                long until = now + st["wait"].Long;
                                while (pollAt.HasValue && pollAt.Value <= until)
                                {
                                    now = pollAt.Value;
                                    pollAt = null;
                                    try { Schedule(await m.Draw(api, false, () => focused)); } catch (Exception) { }
                                }
                                now = until;
                            }
                            else if (st.Has("field")) { if (st["field"].Str == "address") m.Address = st["value"].Str; else m.Code = st["value"].Str; }
                            else if (st.Has("focus")) focused = true;
                            else if (st.Has("blur")) focused = false;
                            else if (st.Has("submit")) one = () => m.Pair(api, ui, null, Draw);
                            else if (st.Has("add")) one = () => m.AddCar(api, ui, st["add"].Str, st["car"].Str, Draw);
                            else if (st.Has("rm")) one = () => m.RemoveCar(api, ui, st["rm"].Str, st["car"].Str, Draw);
                            else if (st.Has("up"))
                            {
                                FxNodesCar c = Card(st["up"].Str).Cars.First(x => x.Id == st["car"].Str);
                                if (c.CanUp) one = () => m.MoveCarUp(api, ui, st["up"].Str, st["car"].Str, Draw);
                            }
                            else if (st.Has("trust")) { bool want = !Card(st["trust"].Str).Trusted; one = () => m.Trust(api, ui, st["trust"].Str, want, Draw); }
                            else if (st.Has("rename")) one = () => m.RenameNode(api, ui, st["rename"].Str, Draw);
                            else if (st.Has("remove")) one = () => m.RemoveNode(api, ui, st["remove"].Str, Draw);
                            else if (st.Has("bench"))
                            {
                                FxNodesCard c = Card(st["bench"].Str);
                                bool enabled = c.Bench != null ? !c.Bench.Running : c.CanBench && !c.BenchDisabled;
                                if (enabled) one = () => FxNodes.StartBench(api, ui, st["bench"].Str, st["codec"].Str, Draw);
                            }
                            else if (st.Has("bstop")) one = () => FxNodes.StopBench(api, ui, st["bstop"].Str, Draw);
                            else if (st.Has("buse"))
                            {
                                string level = Card(st["buse"].Str).Bench.UseLevel;
                                one = () => m.Use(api, ui, st["buse"].Str, level, () => prefs.TryGetValue(FxNodes.FormKey, out string f) ? f : null, f => prefs[FxNodes.FormKey] = f, Draw);
                            }
                            else if (st.Has("worker")) { bool want = !before.WorkerChecked; one = () => m.SetWorker(api, ui, want, Draw); }
                            else if (st.Has("pair")) one = () => FxNodes.PairStart(api, ui, Draw);
                            else if (st.Has("command")) { if (FxNodes.CanShowPairing(before)) one = () => FxNodes.PairStart(api, ui, Draw); }
                            else if (st.Has("cancel")) one = () => FxNodes.PairCancel(api, ui, Draw);
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
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = win.Toasts.Count;
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs.Skip(dialogs).ToList(), what + ": dialogs");
                        dialogs = win.Dialogs.Count;
                        prefs.TryGetValue(FxNodes.FormKey, out string form);
                        Assert.Equal(step["encForm"].Str, form, what + ": flux:enc:form");
                        string polls = string.Join(",", step["polls"].Items.Select(x => x.Long.ToString()));
                        Assert.Equal(polls, pollAt.HasValue ? (pollAt.Value - now).ToString() : "", what + ": the timed draw");
                        if (error != null) continue;
                        PackScript.Same(PackScript.Strings(step["outline"]), FxNodes.Outline(m.View(cars, drawnAt, h24, host, port)), what + ": outline");
                    }
                }
            }));

            test("flux nodes: the store key, the palette's command", () =>
            {
                Assert.Equal("flux:enc:form", FxNodes.FormKey, "store key");
                var v = new FxNodesView { WorkerAvailable = true };
                Assert.True(FxNodes.CanShowPairing(v), "no code: Show pairing code is there");
                v.PairCode = "AB";
                Assert.True(!FxNodes.CanShowPairing(v), "a code shown: no button");
                Assert.True(!FxNodes.CanShowPairing(new FxNodesView()), "TLS_MODE=off: no button");
                return Task.CompletedTask;
            });
        }
    }
}
