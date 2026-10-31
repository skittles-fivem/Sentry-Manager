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
    /// Flux's Jobs page (Core/Flux/FxJobs.cs) against the pack's own
    /// flux/pages/jobs.js on the same answers, clicks and typing
    /// (Fixtures/expected-flux-jobs.json, written by
    /// test/windowsNativeFluxJobs.test.js).
    /// </summary>
    static class FluxJobsTests
    {
        public static void Register(Action<string, Func<Task>> test)
        {
            test("flux jobs: every session in expected-flux-jobs.json, from C# (requests, outline, status after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-jobs.json");
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    HzCar car = PackScript.Car(s["vehicle"]);
                    long now = e["now"].Long;
                    bool h24 = s["h24"].Bool;
                    string cur = s["currency"].Str;
                    FxJobs m = new FxJobs();
                    string error = null;
                    string status = null;
                    try { await m.Load(api, car, CancellationToken.None); }
                    catch (Exception ex) { error = ex.Message; }
                    Assert.Equal(s["error"].Str, error, name + ": error");
                    int from = 0;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        if (!st.IsNull)
                        {
                            from = api.Asked.Count;
                            if (st.Has("click"))
                            {
                                string sel = st["click"].Str;
                                string val = sel.Substring(sel.LastIndexOf("data-v=\"") + 8).TrimEnd(new[] { (char)93, (char)34 });
                                if (sel.Contains("\"jscope\"")) { m.Scope = val; await m.Load(api, car, CancellationToken.None); }
                                else m.Filter = val;
                            }
                            if (st.Has("type")) m.Type(st["type"].Str);
                            if (st.Has("wait")) now += st["wait"].Long;
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        if (error != null) continue;
                        FxJobsView v = m.View(car, now, h24, cur);
                        PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(v), what + ": outline");
                    }
                }
            }));

            test("flux jobs: Clear finished… in expected-flux-jobs.json, from C# (requests, dialog, toasts, the page after)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-jobs.json");
                foreach (JsonValue a in e["actions"].Items)
                {
                    string name = a["name"].Str;
                    var api = new PackScript.Api(a["answers"]);
                    var win = new PackScript.HzWindow(a["confirm"]) { Dialect = new[] { "cancel", "ok", "primary" } };
                    HzCar car = PackScript.Car(a["vehicle"]);
                    FxJobs m = new FxJobs();
                    await m.Load(api, car, CancellationToken.None);
                    if (a["scope"].Str != "vehicle") { m.Scope = a["scope"].Str; await m.Load(api, car, CancellationToken.None); }
                    int from = api.Asked.Count;
                    if (a["params"].Str.Length > 0) Assert.True(HzJobs.ClearAsked("?" + a["params"].Str), name + ": ?clear=1 clears");
                    bool busy = false;
                    await m.Clear(api, win.Ui, car, b => busy = b, () => m.Load(api, car, CancellationToken.None));
                    Assert.True(!busy, name + ": the button is enabled again");
                    PackScript.Same(PackScript.Strings(a["asked"]), PackScript.Asked(api.Asked, from), name + ": requests");
                    PackScript.Same(PackScript.HzWindow.WebDialogs(a["dialogs"]), win.Dialogs, name + ": dialogs");
                    PackScript.Same(PackScript.HzWindow.WebToasts(a["toasts"]), win.Toasts, name + ": toasts");
                    FxJobsView v = m.View(car, e["now"].Long, false, "$");
                    Assert.Equal(a["disabled"].Bool, !v.CanClear, name + ": Clear finished… disabled after");
                    PackScript.Same(PackScript.Strings(a["outline"]), m.Outline(v), name + ": outline after");
                }
            }));

            test("flux jobs: tones, paths", () =>
            {
                Assert.Equal("amber", FxJobs.ToneOf(JsonValue.String("interrupted")), "interrupted");
                Assert.Equal("/api/jobs?vehicle=a%20b%26c", FxJobs.Path("vehicle", new HzCar("a b&c", "x")), "path");
                Assert.Equal("/api/jobs", FxJobs.Path("all", new HzCar("a", "x")), "every vehicle");
                return Task.CompletedTask;
            });
        }
    }
}
