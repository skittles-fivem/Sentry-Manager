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
    /// Flux's Recycle Bin page (Core/Flux/FxBin.cs) against the pack's
    /// own flux/pages/bin.js on the same answers, ticks, typing and clicks
    /// (Fixtures/expected-flux-bin.json, written by
    /// test/windowsNativeFluxBin.test.js).
    /// </summary>
    static class FluxBinTests
    {
        public static void Register(Action<string, Func<Task>> test)
        {
            test("flux bin: every session in expected-flux-bin.json, from C# (requests, toasts, dialogs, outline after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-bin.json");
                int steps = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new PackScript.HzWindow(s["confirm"]) { Dialect = new[] { "cancel", "ok", "primary" } };
                    HzCar car = PackScript.Car(s["vehicle"]);
                    // state.vehicles: the shown one (and Model Y beside Model 3)
                    List<HzCar> names = car == null ? new List<HzCar>() : car.Id == "model-3" ? new List<HzCar> { car, new HzCar("model-y", "Model Y") } : new List<HzCar> { car };
                    bool h24 = s["h24"].Bool;
                    FxBin m = new FxBin();
                    Func<Task> load = () => m.Load(api, car, CancellationToken.None);
                    Func<Task> cars = async () => { await api.Get("/api/vehicles", CancellationToken.None); };
                    string error = null;
                    try { await load(); }
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
                            if (st.Has("scope")) { m.Scope = st["scope"].Str; try { await load(); } catch (Exception) { } }
                            if (st.Has("type")) m.Type(st["type"].Str);
                            if (st.Has("tick")) m.Tick(st["tick"].Str, !m.Sel.Contains(st["tick"].Str));
                            if (st.Has("all")) m.TickAll(!m.View(car, names, h24).AllChecked);
                            if (st.Has("restore")) await m.RestoreOne(api, win.Ui, st["restore"].Str, cars, load);
                            if (st.Has("restoreSel")) await m.RestoreTicked(api, win.Ui, cars, load);
                            if (st.Has("del")) { try { await m.Delete(api, win.Ui, st["del"].Str, cars, load); } catch (Exception) { } }
                            if (st.Has("empty")) { try { await m.Empty(api, win.Ui, car, b => { }, cars, load); } catch (Exception) { } }
                            if (st.Has("command")) { try { await m.RestoreEvery(api, win.Ui, cars, load); } catch (Exception) { } }
                            if (st.Has("topics") && st["topics"].Items.Any(x => x.Str == "bin")) { try { await load(); } catch (Exception) { } }
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        from = api.Asked.Count;
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = win.Toasts.Count;
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs.Skip(dialogs).ToList(), what + ": dialogs");
                        dialogs = win.Dialogs.Count;
                        if (error != null) continue;
                        FxBinView v = m.View(car, names, h24);
                        PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(v), what + ": outline");
                    }
                }
                Assert.True(steps >= 50, "the steps are read (" + steps + ")");
            }));

            test("flux bin: the paths, JavaScript's sums, the dialogs", () =>
            {
                Assert.Equal("/api/bin?vehicle=a%20b%26c", FxBin.Path("vehicle", new HzCar("a b&c", "x")), "path");
                Assert.Equal("/api/bin", FxBin.Path("all", new HzCar("a", "x")), "every vehicle");
                FxBin m = new FxBin();
                m.Items = JsonValue.Parse("[{\"bytes\":1},{\"bytes\":\"5\"},{\"bytes\":2}]");
                Assert.Equal("152", (string)m.Total(), "a string joins");
                Assert.Equal("Empty the Recycle Bin? | 3 items ( | 152 B | ) of every vehicle are permanently deleted. This cannot be undone. | [btn cancel] | Cancel | [btn ok danger] | Empty Recycle Bin", string.Join(" | ", m.EmptyAsk(null).Outline("cancel", "ok", "primary")), "empty");
                return Task.CompletedTask;
            });
        }
    }
}
