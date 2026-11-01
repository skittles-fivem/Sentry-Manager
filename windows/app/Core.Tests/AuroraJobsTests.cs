using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;
using SentrySix.Core.Aurora;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Aurora's Jobs window (Core/Aurora/AuJobs.cs) against the pack's own
    /// aurora/pages/jobs.js on the same answers, picks and refreshes
    /// (Fixtures/expected-aurora-jobs.json, written by
    /// test/windowsNativeAuroraJobs.test.js).
    /// </summary>
    static class AuroraJobsTests
    {
        static List<HzCar> Cars(JsonValue arr) => arr.Items.Select(PackScript.Car).ToList();

        static string Stored(JsonValue s) => s.IsNull ? null : s.Str;

        public static void Register(Action<string, Func<Task>> test)
        {
            test("aurora jobs: every session in expected-aurora-jobs.json, from C# (requests, the window after each step, the kept scope)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-aurora-jobs.json");
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    List<HzCar> cars = Cars(s["vehicles"]);
                    bool h24 = s["h24"].Bool;
                    string cur = s["currency"].IsNull ? null : s["currency"].Str;
                    AuJobs m = new AuJobs();
                    m.Scope = AuJobs.ScopeFrom(Stored(s["storage"]), cars);
                    string stored = Stored(s["storage"]);
                    string error = null;
                    try { await m.Load(api, CancellationToken.None); }
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
                            if (st.Has("pick")) { m.Scope = st["pick"].Str; stored = m.Scope; }
                            await m.Load(api, CancellationToken.None);
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        AuJobsView v = error != null ? null : m.View(cars, h24, cur);
                        PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(v, cars, error), what + ": outline");
                    }
                    string want = s["stored"].IsNull ? null : (string)Hz.Js(JsonValue.Parse(s["stored"].Str));
                    Assert.Equal(want, stored, name + ": aurora:jobs-scope kept");
                }
            }));

            test("aurora jobs: Clear finished in expected-aurora-jobs.json, from C# (requests, dialog, toasts, the window after)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-aurora-jobs.json");
                foreach (JsonValue a in e["actions"].Items)
                {
                    string name = a["name"].Str;
                    var api = new PackScript.Api(a["answers"]);
                    var win = new PackScript.HzWindow(a["confirm"]) { Dialect = new[] { "cancel", "ok", "primary" } };
                    List<HzCar> cars = Cars(a["vehicles"]);
                    AuJobs m = new AuJobs();
                    m.Scope = AuJobs.ScopeFrom(a["scope"].Str, cars);
                    await m.Load(api, CancellationToken.None);
                    int from = api.Asked.Count;
                    await m.Clear(api, win.Ui, () => m.Load(api, CancellationToken.None));
                    PackScript.Same(PackScript.Strings(a["asked"]), PackScript.Asked(api.Asked, from), name + ": requests");
                    PackScript.Same(PackScript.HzWindow.WebDialogs(a["dialogs"]), win.Dialogs, name + ": dialogs");
                    PackScript.Same(PackScript.HzWindow.WebToasts(a["toasts"]), win.Toasts, name + ": toasts");
                    AuJobsView v = m.View(cars, false, "$");
                    Assert.Equal(a["disabled"].Bool, !v.CanClear, name + ": Clear finished disabled after");
                    PackScript.Same(PackScript.Strings(a["outline"]), m.Outline(v, cars, null), name + ": outline after");
                }
            }));

            test("aurora jobs: statuses, scopes, paths, bodies", () =>
            {
                Assert.Equal("amber", AuJobs.StatusOf(JsonValue.String("running"))[0], "running");
                Assert.Equal("waiting", AuJobs.StatusOf(JsonValue.String("waiting"))[1], "a status as written");
                Assert.Equal("/api/jobs?vehicle=a%20b%26c", AuJobs.Path("a b&c"), "path");
                Assert.Equal("/api/jobs", AuJobs.Path("all"), "every vehicle");
                var cars = new List<HzCar> { new HzCar("a", "A"), new HzCar("b", "B") };
                Assert.Equal("b", AuJobs.ScopeFrom("b", cars), "kept");
                Assert.Equal("all", AuJobs.ScopeFrom("gone", cars), "gone");
                Assert.Equal("all", AuJobs.ScopeFrom(null, cars), "none");
                Assert.Equal("{\"vehicle\":null,\"preview\":true}", AuJobs.ClearBody("all", "preview").ToJson(), "all");
                Assert.Equal("aurora:jobs-scope", AuJobs.StoreKey, "key");
                return Task.CompletedTask;
            });
        }
    }
}
