using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Recycle Bin page (Core/Horizon/HzBin.cs) against the pack's
    /// own hz/pages/bin.js on the same answers, ticks, typing and clicks
    /// (Fixtures/expected-horizon-bin.json, written by
    /// test/windowsNativeHorizonBin.test.js).
    /// </summary>
    static class HorizonBinTests
    {
        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon bin: every session in expected-horizon-bin.json, from C# (requests, toasts, dialogs, outline, status after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-bin.json");
                int steps = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new PackScript.HzWindow(s["confirm"]);
                    HzCar car = PackScript.Car(s["vehicle"]);
                    // app.vehicles: the shown one (and Model Y beside Model 3)
                    List<HzCar> names = car == null ? new List<HzCar>() : car.Id == "model-3" ? new List<HzCar> { car, new HzCar("model-y", "Model Y") } : new List<HzCar> { car };
                    bool h24 = s["h24"].Bool;
                    HzBin m = new HzBin();
                    Func<Task> load = () => m.Load(api, car, CancellationToken.None);
                    Func<Task> cars = async () => { await api.Get("/api/vehicles", CancellationToken.None); };
                    string error = null;
                    string status = null;
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
                            if (st.Has("type")) m.Filter(st["type"].Str);
                            if (st.Has("tick")) m.Tick(st["tick"].Str, !m.Sel.Contains(st["tick"].Str));
                            if (st.Has("all")) m.SelectAllShown(!m.View(car, names, h24).AllChecked);
                            if (st.Has("restore")) await m.RestoreOne(api, win.Ui, st["restore"].Str, cars, load);
                            if (st.Has("restoreSel")) await m.RestoreTicked(api, win.Ui, cars, load);
                            if (st.Has("del")) { try { await m.Delete(api, win.Ui, st["del"].Str, cars, load); } catch (Exception) { } }
                            if (st.Has("empty")) { try { await m.Empty(api, win.Ui, car, b => { }, cars, load); } catch (Exception) { } }
                            if (st.Has("topics") && st["topics"].Items.Any(x => x.Str == "bin")) { try { await load(); } catch (Exception) { } }
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        from = api.Asked.Count;
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts.Skip(toasts).ToList(), what + ": toasts");
                        toasts = win.Toasts.Count;
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs.Skip(dialogs).ToList(), what + ": dialogs");
                        dialogs = win.Dialogs.Count;
                        if (error != null) continue;
                        HzBinView v = m.View(car, names, h24);
                        status = v.Status;
                        Assert.Equal(step["status"].Str, status, what + ": status");
                        PackScript.Same(PackScript.Strings(step["outline"]), m.Outline(v), what + ": outline");
                    }
                }
                Assert.True(steps >= 50, "the steps are read (" + steps + ")");
            }));

            test("horizon bin: the paths, JavaScript's sums and the Empty body", () =>
            {
                Assert.Equal("/api/bin?vehicle=a%20b%26c", HzBin.Path("car", new HzCar("a b&c", "x")), "path");
                Assert.Equal("/api/bin", HzBin.Path("all", new HzCar("a", "x")), "every vehicle");
                HzBin m = new HzBin();
                m.Items = JsonValue.Parse("[{\"bytes\":1},{\"bytes\":null},{\"bytes\":2}]");
                Assert.Equal(3.0, (double)m.Total(), "numbers and null");
                m.Items = JsonValue.Parse("[{\"bytes\":1},{\"bytes\":\"5\"},{\"bytes\":2}]");
                Assert.Equal("152", (string)m.Total(), "a string joins");
                m.Items = JsonValue.Parse("[{\"bytes\":1},{}]");
                Assert.True(double.IsNaN((double)m.Total()), "undefined is NaN");
                Assert.Equal("{\"confirm\":true,\"vehicle\":null}", Bin.EmptyBody(null).ToJson(), "body");
                return Task.CompletedTask;
            });
        }
    }
}
