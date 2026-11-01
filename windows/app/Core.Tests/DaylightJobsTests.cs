using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;
using SentrySix.Core.Daylight;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Daylight's Activity page (Core/Daylight/DlJobs.cs) against the pack's own
    /// dl/pages/jobs.js on the same answers, picks and live topics
    /// (Fixtures/expected-daylight-jobs.json, written by
    /// test/windowsNativeDaylightJobs.test.js).
    /// </summary>
    static class DaylightJobsTests
    {
        /// <summary>A Daylight window that records toasts ("toast-good …") and dialogs as the harness reads core.js modal().</summary>
        sealed class Window
        {
            public readonly HzUi Ui = new HzUi();
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Dialogs = new List<string>();

            public Window(bool confirm)
            {
                Ui.Toast = (t, kind) => Toasts.Add("toast-" + kind + " " + t);
                Ui.Ask = a =>
                {
                    Dialogs.Add(string.Join(" | ", Dl.AskOutline(a)) + " => " + (confirm ? "yes" : "no"));
                    return Task.FromResult(confirm);
                };
            }
        }

        static List<HzCar> Cars(JsonValue arr) => arr.Items.Select(PackScript.Car).ToList();

        static string Currency(JsonValue c) => c.IsNull ? null : c.Str;

        public static void Register(Action<string, Func<Task>> test)
        {
            test("daylight jobs: every session in expected-daylight-jobs.json, from C# (requests, outline after each step, toasts)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-daylight-jobs.json");
                long now = e["now"].Long;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new Window(false);
                    bool h24 = s["h24"].Bool;
                    string cur = Currency(s["currency"]);
                    DlJobs m = new DlJobs(Cars(s["vehicles"]), null);
                    string error = null;
                    List<string> shown = null;
                    try { await m.Load(api, CancellationToken.None); shown = m.Outline(m.View(h24, cur)); }
                    catch (Exception ex) { error = ex.Message; }
                    Assert.Equal(s["error"].Str, error, name + ": error");
                    bool picking = false;
                    int from = 0;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        if (!st.IsNull)
                        {
                            from = api.Asked.Count;
                            bool refresh = false;
                            if (st.Has("pick")) { m.Pick(st["pick"].Str); refresh = true; }
                            if (st.Has("topics")) refresh = m.Topics(PackScript.Strings(st["topics"]), false, picking) == "refresh";
                            if (st.Has("focus")) picking = true;
                            if (st.Has("blur")) { picking = false; refresh = m.Leave(false); }
                            if (refresh && await m.Refresh(api, win.Ui)) shown = m.Outline(m.View(h24, cur));
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        if (error != null) continue;
                        PackScript.Same(PackScript.Strings(step["outline"]), shown, what + ": outline");
                    }
                    PackScript.Same(PackScript.HzWindow.WebToasts(s["toasts"]), win.Toasts, name + ": toasts");
                }
            }));

            test("daylight jobs: Clear finished in expected-daylight-jobs.json, from C# (requests, dialog, toasts, the page after)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-daylight-jobs.json");
                foreach (JsonValue a in e["actions"].Items)
                {
                    string name = a["name"].Str;
                    var api = new PackScript.Api(a["answers"]);
                    var win = new Window(a["confirm"].Bool);
                    DlJobs m = new DlJobs(Cars(a["vehicles"]), null);
                    await m.Load(api, CancellationToken.None);
                    if (a["pick"].Str.Length > 0) { m.Pick(a["pick"].Str); await m.Refresh(api, win.Ui); }
                    int from = api.Asked.Count;
                    bool drawn = false;
                    await m.Clear(api, win.Ui, () => { DlJobs.LoadRunning(api); }, () => drawn = true);
                    PackScript.Same(PackScript.Strings(a["asked"]), PackScript.Asked(api.Asked, from), name + ": requests");
                    PackScript.Same(PackScript.HzWindow.WebDialogs(a["dialogs"]), win.Dialogs, name + ": dialogs");
                    PackScript.Same(PackScript.HzWindow.WebToasts(a["toasts"]), win.Toasts, name + ": toasts");
                    DlJobsView v = m.View(false, "$");
                    Assert.Equal(a["disabled"].Bool, !v.CanClear, name + ": Clear finished disabled after");
                    PackScript.Same(PackScript.Strings(a["outline"]), m.Outline(v), name + ": outline after");
                }
            }));

            test("daylight jobs: words, paths, a vehicle gone, the running count", async () =>
            {
                Assert.Equal("Stopped", DlJobs.Status("cancelled")[0], "cancelled");
                Assert.True(DlJobs.Status("waiting") == null, "waiting has no word of its own");
                Assert.Equal("Moving to Recycle Bin", DlJobs.Type("trash"), "trash");
                var cars = new List<HzCar> { new HzCar("a b&c", "x"), new HzCar("y", "Y") };
                Assert.Equal("/api/jobs?vehicle=a%20b%26c", new DlJobs(cars, "a b&c").Path(), "picked");
                Assert.Equal("/api/jobs", new DlJobs(cars, "gone").Path(), "a vehicle gone is every vehicle");
                Assert.Equal("", DlJobs.EnergyText(JsonValue.Parse("{\"wh\":\"3\"}"), "$"), "watt-hours as text");
                var api = new PackScript.Api(JsonValue.Parse("{\"GET /api/jobs?limit=50\":{\"jobs\":[{\"status\":\"running\",\"type\":\"scan\"},{\"status\":\"running\",\"type\":\"conversion\"},{\"status\":\"done\",\"type\":\"conversion\"}]}}"));
                int[] r = await DlJobs.LoadRunning(api);
                Assert.Equal(2, r[0], "two running");
                Assert.Equal(1, r[1], "one converts");
                Assert.True(await DlJobs.LoadRunning(new PackScript.Api(JsonValue.Object())) == null, "no answer");
            });
        }
    }
}
