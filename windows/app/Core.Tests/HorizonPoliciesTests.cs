using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Policies page (Core/Horizon/HzPolicies.cs) against the pack's
    /// own hz/pages/policies.js on the same answers, ticks, typing and clicks
    /// (Fixtures/expected-horizon-policies.json, written by
    /// test/windowsNativeHorizonPolicies.test.js).
    /// </summary>
    static class HorizonPoliciesTests
    {
        /// <summary>core.js car(): the vehicle picked, else the first, else none.</summary>
        static HzCar Current(List<HzCar> cars, JsonValue id)
        {
            HzCar hit = cars.FirstOrDefault(c => !id.IsNull && c.Id == id.Str);
            return hit ?? cars.FirstOrDefault();
        }

        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon policies: every session in expected-horizon-policies.json, from C# (requests, toasts, dialogs, outline, status after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-policies.json");
                int steps = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new PackScript.HzWindow(s["confirm"]);
                    List<HzCar> cars = s["vehicles"].Items.Select(PackScript.Car).ToList();
                    HzCar cur = Current(cars, s["current"]);
                    bool h24 = s["h24"].Bool;
                    HzPolicies m = new HzPolicies();
                    Func<Task> draw = () => m.Load(api, cars, CancellationToken.None);
                    string error = null;
                    string status = null;
                    try { await draw(); m.View(cur, h24); }
                    catch (Exception ex) { error = ex.Message; }
                    Assert.Equal(s["error"].Str, error, name + ": error");
                    int from = 0, toasts = 0, dialogs = 0;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        steps++;
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        if (!st.IsNull)
                        {
                            if (st.Has("tick")) { HzPolicyForm f = m.FormOf(st["tick"].Str, cur, h24); f.Enabled = !f.Enabled; }
                            if (st.Has("set"))
                            {
                                HzPolicyForm f = m.FormOf(st["set"][0].Str, cur, h24);
                                string field = st["set"][1].Str, value = st["set"][2].Str;
                                if (field == "runAt") f.RunAt = value;
                                else f.Keep[Array.IndexOf(HzPolicies.Cats, field.Substring(5))] = Policies.NumberValue(value);
                            }
                            if (st.Has("save")) await HzPolicies.Save(api, win.Ui, st["save"].Str, m.FormOf(st["save"].Str, cur, h24), draw);
                            if (st.Has("run")) await HzPolicies.RunNow(api, win.Ui, st["run"].Str, m.FormOf(st["run"].Str, cur, h24), draw);
                            if (st.Has("topics") && HzPolicies.Redraws(new HashSet<string>(st["topics"].Items.Select(x => x.Str)))) await draw();
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        from = api.Asked.Count;
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = win.Toasts.Count;
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs.Skip(dialogs).ToList(), what + ": dialogs");
                        dialogs = win.Dialogs.Count;
                        if (error != null) continue;
                        HzPoliciesView v = m.View(cur, h24);
                        if (v.Status != null) status = v.Status;
                        Assert.Equal(step["status"].Str, status, what + ": status");
                        PackScript.Same(PackScript.Strings(step["outline"]), HzPolicies.Outline(v), what + ": outline");
                    }
                }
                Assert.True(steps >= 40, "the steps are read (" + steps + ")");
            }));

            test("horizon policies: the paths, the body and the browser's own field rules", () =>
            {
                Assert.Equal("/api/vehicles/a%20b%26c/policy", HzPolicies.Path("a b&c"), "path");
                HzPolicyForm f = new HzPolicyForm();
                f.Keep = new[] { "030", "", "1e2", "abc" };
                Assert.Equal("{\"enabled\":false,\"runAt\":\"03:30\",\"keepDays\":{\"Recent\":30,\"Sentry\":null,\"Saved\":100,\"Other\":null}}", f.Body().ToJson(), "body");
                f.RunAt = "05:45";
                f.Enabled = true;
                Assert.True(f.Body().ToJson().StartsWith("{\"enabled\":true,\"runAt\":\"05:45\""), "runAt");
                Assert.Equal("Value must be greater than or equal to 1.", Policies.NumberMessage("0", 1, 3650), "min");
                Assert.Equal("Please enter a valid value. The two nearest valid values are 2 and 3.", Policies.NumberMessage("2.5", 1, 3650), "step");
                Assert.True(HzPolicies.Redraws(new HashSet<string> { "vehicles" }) && !HzPolicies.Redraws(new HashSet<string> { "jobs" }), "topics");
                return Task.CompletedTask;
            });
        }
    }
}
