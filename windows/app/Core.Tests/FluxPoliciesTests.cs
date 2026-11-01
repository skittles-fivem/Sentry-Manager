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
    /// Flux's Policies page (Core/Flux/FxPolicies.cs) against the pack's
    /// own flux/pages/policies.js on the same answers, ticks, typing and clicks
    /// (Fixtures/expected-flux-policies.json, written by
    /// test/windowsNativeFluxPolicies.test.js).
    /// </summary>
    static class FluxPoliciesTests
    {
        /// <summary>core.js current(): the vehicle picked, else the first, else none.</summary>
        static HzCar Current(List<HzCar> cars, JsonValue id)
        {
            HzCar hit = cars.FirstOrDefault(c => !id.IsNull && c.Id == id.Str);
            return hit ?? cars.FirstOrDefault();
        }

        public static void Register(Action<string, Func<Task>> test)
        {
            test("flux policies: every session in expected-flux-policies.json, from C# (requests, toasts, dialogs, outline after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-policies.json");
                int steps = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new PackScript.HzWindow(s["confirm"]) { Dialect = new[] { "cancel", "ok", "primary" } };
                    List<HzCar> cars = s["vehicles"].Items.Select(PackScript.Car).ToList();
                    HzCar cur = Current(cars, s["current"]);
                    bool h24 = s["h24"].Bool;
                    FxPolicies m = new FxPolicies();
                    Func<Task> draw = () => m.Load(api, cars, CancellationToken.None);
                    string error = null;
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
                                else f.Keep[Array.IndexOf(FxPolicies.Cats, field.Substring(5))] = Policies.NumberValue(value);
                            }
                            if (st.Has("save")) await FxPolicies.Save(api, win.Ui, st["save"].Str, m.FormOf(st["save"].Str, cur, h24), draw);
                            if (st.Has("run")) await FxPolicies.RunNow(api, win.Ui, st["run"].Str, m.FormOf(st["run"].Str, cur, h24), draw);
                            if (st.Has("topics") && FxPolicies.Redraws(new HashSet<string>(st["topics"].Items.Select(x => x.Str)))) await draw();
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        from = api.Asked.Count;
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = win.Toasts.Count;
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs.Skip(dialogs).ToList(), what + ": dialogs");
                        dialogs = win.Dialogs.Count;
                        if (error != null) continue;
                        FxPoliciesView v = m.View(cur, h24);
                        PackScript.Same(PackScript.Strings(step["outline"]), FxPolicies.Outline(v), what + ": outline");
                    }
                }
                Assert.True(steps >= 40, "the steps are read (" + steps + ")");
            }));

            test("flux policies: the labels, the dialog, the topics", () =>
            {
                Assert.Equal("Keep Saved (days)", FxPolicies.KeepLabel("Saved"), "label");
                Assert.Equal("Run the policy now? | 1 recording ( | 1.5 KB | ) move to the Recycle Bin. You can restore them from there. | [btn cancel] | Cancel | [btn ok primary] | Run now", string.Join(" | ", FxPolicies.RunAsk(JsonValue.Parse("{\"recordings\":1,\"bytes\":1536}")).Outline("cancel", "ok", "primary")), "dialog");
                Assert.True(FxPolicies.Redraws(new HashSet<string> { "inventory" }) && !FxPolicies.Redraws(new HashSet<string> { "bin" }), "topics");
                return Task.CompletedTask;
            });
        }
    }
}
