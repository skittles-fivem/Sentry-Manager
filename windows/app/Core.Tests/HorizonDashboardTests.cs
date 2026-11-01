using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Horizon's Dashboard (Core/Horizon/HzDash.cs) against the pack's own
    /// hz/pages/dashboard.js on the same answers, timers, clicks and typing
    /// (Fixtures/expected-horizon-dashboard.json, written by
    /// test/windowsNativeHorizonDashboard.test.js): the requests, the outline,
    /// the instruments (needles, arcs, odometers, sparklines), the status
    /// line, the dialogs, toasts and what is stored, after every step.
    /// </summary>
    static class HorizonDashboardTests
    {
        /// <summary>The web's timers as the harness runs them: in time order, the older one first on a tie.</summary>
        sealed class Clock
        {
            sealed class Timer { public long At; public int Id; public long Every; public Func<Task> Run; }
            readonly List<Timer> timers = new List<Timer>();
            int ids;
            public long Now;

            public int After(long ms, Func<Task> run, long every = 0)
            {
                Timer t = new Timer { At = Now + ms, Id = ++ids, Every = every, Run = run };
                timers.Add(t);
                return t.Id;
            }

            public async Task Flush(long ms)
            {
                long until = Now + ms;
                for (int n = 0; n < 200; n++)
                {
                    Timer next = timers.Where(t => t.At <= until).OrderBy(t => t.At).ThenBy(t => t.Id).FirstOrDefault();
                    if (next == null) break;
                    Now = Math.Max(Now, next.At);
                    if (next.Every > 0) next.At = Now + next.Every; else timers.Remove(next);
                    await next.Run();
                }
                Now = Math.Max(Now, until);
            }
        }

        public static void Register(Action<string, Func<Task>> test)
        {
            test("horizon dashboard: every session in expected-horizon-dashboard.json, from C# (requests, outline, instruments, status, dialogs, toasts after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-horizon-dashboard.json");
                int count = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    count++;
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new PackScript.HzWindow(s["confirm"]);
                    Clock clock = new Clock { Now = e["now"].Long };
                    HzDashEnv env = new HzDashEnv();
                    env.Vehicles = s["vehicles"];
                    env.VehicleId = s["vehicleId"].IsNull ? (s["vehicles"].Count > 0 ? s["vehicles"][0]["id"].Str : null) : s["vehicleId"].Str;
                    env.Notes = s["notes"];
                    env.Converter = s["converter"].IsNull ? null : s["converter"].Str;
                    env.Currency = s["currency"].Str;
                    env.H24 = s["h24"].Bool;
                    env.Windows = s["deployment"].Str == "windows";
                    env.Now = clock.Now;
                    Dictionary<string, string> stored = new Dictionary<string, string>();
                    foreach (var kv in s["storage"].Props) stored[kv.Key] = kv.Value.Str;
                    Func<string, string> Stored = k => stored.ContainsKey("horizon." + k) ? stored["horizon." + k] : null;
                    HzDash m = new HzDash();
                    m.FromStorage(Stored("log.level"), Stored("log.follow"));
                    bool visible = true;
                    string focus = null;
                    Func<Task> Poll = null;
                    Poll = async () =>
                    {
                        env.Now = clock.Now;
                        await m.Poll(api, env, visible);
                        clock.After(2000, Poll);
                    };
                    // the log timer starts before anything loads
                    clock.After(5000, async () => { env.Now = clock.Now; if (m.LogDue(visible)) await m.LoadLog(api, env); }, 5000);
                    await m.Mount(api, env, visible);
                    clock.After(2000, Poll);
                    Assert.True(HzDash.Keys.Length == 1 && HzDash.Keys[0][0] == s["keys"][0][0].Str && HzDash.Keys[0][1] == s["keys"][0][1].Str, name + ": keys");
                    int from = 0;
                    List<string> outline = null, instruments = null;
                    foreach (JsonValue step in s["steps"].Items)
                    {
                        JsonValue st = step["step"];
                        string what = name + " after " + (st.IsNull ? "mount" : st.ToJson());
                        if (!st.IsNull)
                        {
                            from = api.Asked.Count;
                            env.Now = clock.Now;
                            if (st.Has("click"))
                            {
                                switch (st["click"].Str)
                                {
                                    case "#d-arch summary": await m.ToggleArch(api, env); break;
                                    case "#d-unlock summary": m.UnlockOpen = !m.UnlockOpen; break;
                                    case "#d-logclear": await m.Clear(api, win.Ui, env, null); break;
                                    case "#d-follow": m.SetFollow(!m.Follow, v => stored["horizon.log.follow"] = v); break;
                                    case "#d-logref": await m.LoadLog(api, env); break;
                                    default: throw new AssertFailed(what + ": no such click");
                                }
                            }
                            if (st.Has("scan")) await m.Scan(api, win.Ui, st["scan"].Str);
                            if (st.Has("recon")) await m.ReconnectCar(api, win.Ui, st["recon"].Str);
                            if (st.Has("level")) await m.PickLevel(api, env, st["level"].Str, v => stored["horizon.log.level"] = v);
                            if (st.Has("type"))
                            {
                                string typed = st["type"].Str;
                                clock.After(120, () => { env.Now = clock.Now; m.Filter(typed, env); return Task.CompletedTask; });
                                await clock.Flush(120);
                            }
                            if (st.Has("scope")) m.PickScope(st["scope"].Str, env);
                            if (st.Has("key")) focus = "d-logq";
                            if (st.Has("hidden")) visible = !st["hidden"].Bool;
                            if (st.Has("topics"))
                            {
                                if (st.Has("notes")) env.Notes = st["notes"];
                                if (st.Has("vehicles")) env.Vehicles = st["vehicles"];
                                await m.OnTopics(api, env, st["topics"].Items.Select(x => x.Str).ToList());
                            }
                            if (st.Has("car")) { env.VehicleId = st["car"].Str; await m.OnCar(api, env); }
                            if (st.Has("wait")) await clock.Flush(st["wait"].Long);
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        Assert.Equal(step["status"].Str, m.Status, what + ": status");
                        Assert.Equal(step["focus"].IsNull ? null : step["focus"].Str, focus, what + ": focus");
                        if (!step["outline"].IsNull) outline = PackScript.Strings(step["outline"]);
                        if (!step["instruments"].IsNull) instruments = PackScript.Strings(step["instruments"]);
                        PackScript.Same(outline, m.Outline(), what + ": outline");
                        PackScript.Same(instruments, m.Instruments(), what + ": instruments");
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs, what + ": dialogs");
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts, what + ": toasts");
                        Assert.Equal(step["stored"]["level"].IsNull ? null : step["stored"]["level"].Str, Stored("log.level"), what + ": stored level");
                        Assert.Equal(step["stored"]["follow"].IsNull ? null : step["stored"]["follow"].Str, Stored("log.follow"), what + ": stored follow");
                    }
                }
                Assert.True(count >= 20, "the sessions are read (" + count + ")");
            }));

            test("horizon dashboard: the gauges' geometry, the request paths, the clear dialog", () =>
            {
                Assert.Equal("M39.19 160.81A86 86 0 1 1 160.81 160.81", HzGauge.Arc(100, 100, 86, 135, 405), "the dial's track");
                HzDial d = new HzDial { Max = 100 };
                d.SetTo(50, null, null);
                Assert.Equal("rotate(270.00deg)", d.Needle, "half way");
                Assert.Equal("50", d.Read, "the read-out");
                Assert.True(!d.Hot, "not hot");
                d.SetTo(double.NaN, null, "x");
                Assert.Equal("—", d.Read, "no value");
                Assert.Equal("x", d.Unit, "the unit");
                Assert.Equal(null, HzGauge.Spark(new List<double> { 1, double.NaN }, 240, 30, double.NaN, null), "one number: the base line");
                Assert.Equal("M0.0 28.0L240.0 2.0", HzGauge.Spark(new List<double> { 0, 100 }, 240, 30, 100, null), "two numbers");
                Assert.Equal("/api/vehicles/a%20b/days?limit=100", HzDash.DaysPath("a b"), "days");
                Assert.Equal("/api/logs?level=problems&limit=500", HzDash.LogPath("x"), "log");
                Assert.Equal("Clear the log? | The log starts empty. Nothing is lost: the current lines move to the archives below and stay downloadable. | [btn no] | Cancel | [btn yes go] | Clear log", string.Join(" | ", HzDash.ClearAsk().Outline()), "dialog");
                return Task.CompletedTask;
            });
        }
    }
}
