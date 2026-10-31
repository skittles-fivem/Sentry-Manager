using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Jobs page (Core/Horizon/HzJobs.cs) against the pack's own
    /// hz/pages/jobs.js on the same answers, clicks and typing
    /// (Fixtures/expected-horizon-jobs.json, written by
    /// test/windowsNativeHorizonJobs.test.js).
    /// </summary>
    static class HorizonJobsTests
    {
        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon jobs: every session in expected-horizon-jobs.json, from C# (requests, outline, status after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-jobs.json");
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    HzCar car = PackScript.Car(s["vehicle"]);
                    long now = e["now"].Long;
                    bool h24 = s["h24"].Bool;
                    string cur = s["currency"].Str;
                    HzJobs m = new HzJobs();
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
                                if (sel.Contains("\"jsc\"")) { m.Scope = val; await m.Load(api, car, CancellationToken.None); }
                                else m.Show = val;
                            }
                            if (st.Has("type")) m.Filter(st["type"].Str);
                            if (st.Has("wait")) now += st["wait"].Long;
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        if (error != null) continue;
                        HzJobsView v = m.View(car, now, h24, cur);
                        status = v.Status;
                        Assert.Equal(step["status"].Str, status, what + ": status");
                        PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(v), what + ": outline");
                    }
                }
            }));

            test("horizon jobs: Clear finished… in expected-horizon-jobs.json, from C# (requests, dialog, toasts, the page after)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-jobs.json");
                foreach (JsonValue a in e["actions"].Items)
                {
                    string name = a["name"].Str;
                    var api = new PackScript.Api(a["answers"]);
                    var win = new PackScript.HzWindow(a["confirm"]);
                    HzCar car = PackScript.Car(a["vehicle"]);
                    HzJobs m = new HzJobs();
                    await m.Load(api, car, CancellationToken.None);
                    if (a["scope"].Str != "car") { m.Scope = a["scope"].Str; await m.Load(api, car, CancellationToken.None); }
                    int from = api.Asked.Count;
                    if (a["params"].Str.Length > 0) Assert.True(HzJobs.ClearAsked("?" + a["params"].Str), name + ": ?clear=1 clears");
                    bool busy = false;
                    await m.Clear(api, win.Ui, car, b => busy = b, () => m.Load(api, car, CancellationToken.None));
                    Assert.True(!busy, name + ": the button is enabled again");
                    PackScript.Same(PackScript.Strings(a["asked"]), PackScript.Asked(api.Asked, from), name + ": requests");
                    PackScript.Same(PackScript.HzWindow.WebDialogs(a["dialogs"]), win.Dialogs, name + ": dialogs");
                    PackScript.Same(PackScript.HzWindow.WebToasts(a["toasts"]), win.Toasts, name + ": toasts");
                    HzJobsView v = m.View(car, e["now"].Long, false, "$");
                    Assert.Equal(a["disabled"].Bool, !v.CanClear, name + ": Clear finished… disabled after");
                    PackScript.Same(PackScript.Strings(a["outline"]), m.Outline(v), name + ": outline after");
                }
            }));

            test("horizon jobs: lamps, the route's ?clear, the request bodies", () =>
            {
                Assert.Equal("grn", HzJobs.LampOf(JsonValue.String("done")), "done");
                Assert.Equal("org", HzJobs.LampOf(JsonValue.String("interrupted")), "interrupted");
                Assert.Equal("", HzJobs.LampOf(JsonValue.String("cancelled")), "cancelled");
                Assert.Equal("/api/jobs?vehicle=a%20b%26c", HzJobs.Path("car", new HzCar("a b&c", "x")), "path");
                Assert.Equal("/api/jobs", HzJobs.Path("all", new HzCar("a", "x")), "every vehicle");
                Assert.True(HzJobs.ClearAsked("?clear=1") && !HzJobs.ClearAsked("") && !HzJobs.ClearAsked("?clear="), "?clear");
                Assert.Equal("{\"vehicle\":null,\"preview\":true}", HzJobs.ClearBody(null, "preview").ToJson(), "body");
                return Task.CompletedTask;
            });
        }
    }
}
