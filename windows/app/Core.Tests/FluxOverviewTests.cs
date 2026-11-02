using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SentrySix.Core.Flux;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// Flux's Overview (Core/Flux/FxOverview.cs, FxOverviewChart.cs) against
    /// the pack's own flux/pages/overview.js and flux/charts.js on the same
    /// answers, timers, clicks and typing (Fixtures/expected-flux-overview.json,
    /// written by test/windowsNativeFluxOverview.test.js): the requests, the
    /// outline, what every chart was given, the dialogs, toasts and what is
    /// stored after every step; then the charts' strokes, fills and labels as
    /// charts.js draws them on a recording canvas.
    /// </summary>
    static class FluxOverviewTests
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

            public void Cancel(int id) { timers.RemoveAll(t => t.Id == id); }

            public async Task Flush(long ms)
            {
                long until = Now + ms;
                for (int n = 0; n < 200; n++)
                {
                    Timer next = timers.Where(t => t.At <= until).OrderBy(t => t.At).ThenBy(t => t.Id).FirstOrDefault();
                    if (next == null) break;
                    Now = Math.Max(Now, next.At);
                    if (next.Every > 0) next.At = Now + Math.Max(1, next.Every); else timers.Remove(next);
                    await next.Run();
                }
                Now = Math.Max(Now, until);
            }
        }

        // ---------------------------------------------------------------- JSON as the web's JSON.stringify writes it (numbers as JavaScript prints them)

        static void Canon(StringBuilder b, JsonValue v)
        {
            if (v == null || v.IsNull) { b.Append("null"); return; }
            if (v.IsNumber) { b.Append(Format.Fin(v.Num) ? Format.JsNum(v.Num) : "null"); return; }
            if (v.IsString || v.IsBool) { b.Append(v.ToJson()); return; }
            if (v.IsArray)
            {
                b.Append('[');
                for (int i = 0; i < v.Count; i++) { if (i > 0) b.Append(','); Canon(b, v[i]); }
                b.Append(']');
                return;
            }
            b.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, JsonValue> kv in v.Props)
            {
                if (!first) b.Append(',');
                first = false;
                b.Append(JsonValue.Quote(kv.Key)).Append(':');
                Canon(b, kv.Value);
            }
            b.Append('}');
        }

        static string Canon(JsonValue v)
        {
            StringBuilder b = new StringBuilder();
            Canon(b, v);
            return b.ToString();
        }

        static JsonValue Times(List<double> t) => JsonValue.Array(t.Select(x => Format.Fin(x) ? JsonValue.Number(x) : JsonValue.Null));

        /// <summary>m.Charts as the web's watchCharts() writes them.</summary>
        static List<string> Charts(FxOverview m)
        {
            List<string> o = new List<string>();
            foreach (var kv in m.Charts)
            {
                JsonValue c = kv.Value.Rows != null
                    ? JsonValue.Object("t", Times(kv.Value.Times), "rows", JsonValue.Array(kv.Value.Rows.Select(r => JsonValue.Array(r))))
                    : JsonValue.Object("t", Times(kv.Value.Times), "s", JsonValue.Object(kv.Value.Data.SelectMany(d => new object[] { d.Key, d.Value }).ToArray()));
                o.Add(kv.Key + " " + Canon(c));
            }
            return o;
        }

        static List<string> WebCharts(JsonValue charts) => charts.Props.Select(kv => kv.Key + " " + Canon(kv.Value)).ToList();

        public static void Register(Action<string, Func<Task>> test)
        {
            test("flux overview: every session in expected-flux-overview.json, from C# (requests, outline, charts, dialogs, toasts, stored after each step)", () => PackScript.WithUtc(async () =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-overview.json");
                int count = 0;
                foreach (JsonValue s in e["sessions"].Items)
                {
                    count++;
                    string name = s["name"].Str;
                    var api = new PackScript.Api(s["answers"]);
                    var win = new PackScript.HzWindow(s["confirm"]) { Dialect = new[] { "cancel", "ok", "primary" } };
                    Clock clock = new Clock { Now = e["now"].Long };
                    FxOverviewEnv env = new FxOverviewEnv();
                    env.Vehicles = s["vehicles"];
                    env.VehicleId = s["vehicleId"].IsNull ? (s["vehicles"].Count > 0 ? s["vehicles"][0]["id"].Str : null) : s["vehicleId"].Str;
                    env.Notes = s["notes"];
                    env.Currency = s["currency"].Str;
                    env.H24 = s["h24"].Bool;
                    env.Windows = s["deployment"].Str == "windows";
                    env.Now = clock.Now;
                    Dictionary<string, string> stored = new Dictionary<string, string>();
                    foreach (var kv in s["storage"].Props) stored[kv.Key] = kv.Value.Str;
                    Func<string, string> Stored = k => stored.ContainsKey("flux:ov:" + k) ? stored["flux:ov:" + k] : null;
                    FxOverview m = new FxOverview();
                    m.FromStorage(Stored("range"), Stored("logLevel"), Stored("logAuto"));
                    bool visible = true;
                    int pollId = 0;
                    Func<Task> Poll = null;
                    Poll = async () =>
                    {
                        env.Now = clock.Now;
                        await m.Poll(api, env, visible);
                        pollId = clock.After(2000, Poll);
                    };
                    Func<string, Task> Range = async v =>
                    {
                        env.Now = clock.Now;
                        if (v == "24h") clock.Cancel(pollId);
                        await m.PickRange(api, env, v, x => stored["flux:ov:range"] = x, visible);
                        if (v == "24h") pollId = clock.After(2000, Poll);
                    };
                    // the log timer starts before anything loads
                    clock.After(2500, async () => { env.Now = clock.Now; if (m.LogDue(visible)) await m.LoadLogs(api, env); }, 2500);
                    await m.Mount(api, env, visible);
                    pollId = clock.After(2000, Poll);
                    List<string> cmds = FxOverview.Commands.Select(c => string.Join(" | ", c)).ToList();
                    PackScript.Same(s["commands"].Items.Select(c => string.Join(" | ", c.Items.Select(x => x.Str))).ToList(), cmds, name + ": commands");
                    int from = 0;
                    List<string> outline = null, charts = null;
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
                                    case "#log-arch summary": await m.ToggleArch(api, env); break;
                                    case "#ov-alerts details.unlock summary": m.UnlockOpen = !m.UnlockOpen; break;
                                    case "#log-clear": await m.Clear(api, win.Ui, env); break;
                                    case "#log-auto": m.SetAuto(!m.Auto, v => stored["flux:ov:logAuto"] = v); break;
                                    case "#log-refresh": await m.LoadLogs(api, env); break;
                                    default: throw new AssertFailed(what + ": no such click");
                                }
                            }
                            if (st.Has("scan")) await m.Scan(api, win.Ui, st["scan"].Str);
                            if (st.Has("recon")) await m.ReconnectCar(api, win.Ui, st["recon"].Str);
                            if (st.Has("level")) await m.PickLevel(api, env, st["level"].Str, v => stored["flux:ov:logLevel"] = v);
                            if (st.Has("range")) await Range(st["range"].Str);
                            if (st.Has("type"))
                            {
                                string typed = st["type"].Str;
                                m.Typed = typed;
                                clock.After(120, () => { env.Now = clock.Now; m.Type(typed, env); return Task.CompletedTask; });
                                await clock.Flush(120);
                            }
                            if (st.Has("scope")) m.PickScope(st["scope"].Str, env);
                            if (st.Has("key")) await Range(m.OtherRange());
                            if (st.Has("legend")) m.ToggleSeries(st["legend"][0].Str, st["legend"][1].Str);
                            if (st.Has("hidden")) visible = !st["hidden"].Bool;
                            if (st.Has("topics"))
                            {
                                if (st.Has("notes")) env.Notes = st["notes"];
                                if (st.Has("vehicles")) env.Vehicles = st["vehicles"];
                                m.OnTopics(env, st["topics"].Items.Select(x => x.Str).ToList());
                            }
                            if (st.Has("car")) { env.VehicleId = st["car"].Str; m.DrawKpis(env); }
                            if (st.Has("wait")) await clock.Flush(st["wait"].Long);
                        }
                        PackScript.Same(PackScript.Strings(step["asked"]), PackScript.Asked(api.Asked, from), what + ": requests");
                        if (!step["outline"].IsNull) outline = PackScript.Strings(step["outline"]);
                        if (!step["charts"].IsNull) charts = WebCharts(step["charts"]);
                        if (s["error"].IsNull)
                        {
                            PackScript.Same(outline, m.Outline(), what + ": outline");
                            PackScript.Same(charts, Charts(m), what + ": charts");
                        }
                        PackScript.Same(PackScript.HzWindow.WebDialogs(step["dialogs"]), win.Dialogs, what + ": dialogs");
                        PackScript.Same(PackScript.HzWindow.WebToasts(step["toasts"]), win.Toasts, what + ": toasts");
                        foreach (string k in new[] { "range", "level", "auto" })
                        {
                            string key = k == "range" ? "range" : k == "level" ? "logLevel" : "logAuto";
                            Assert.Equal(step["stored"][k].IsNull ? null : step["stored"][k].Str, Stored(key), what + ": stored " + k);
                        }
                    }
                }
                Assert.True(count >= 20, "the sessions are read (" + count + ")");
            }));

            test("flux overview: TimeChart, HeatStrip and coreBars in expected-flux-overview.json, from C# (every stroke, fill, label and the read-out)", () =>
            {
                PackScript.WithUtc(() =>
                {
                    JsonValue g = PackScript.Fixture("expected-flux-overview.json")["geometry"];
                    Func<string, double> measure = t => t.Length * 6;
                    int count = 0;
                    foreach (JsonValue c in g["charts"].Items)
                    {
                        count++;
                        string what = "chart " + c["name"].Str + (c["h24"].Bool ? " (24 h)" : "");
                        bool h24 = c["h24"].Bool;
                        Func<double, bool, string> tod = (ms, secs) => Fx.Tod(ms, h24, secs);
                        List<double> times = c["times"].Items.Select(x => x.IsNumber ? x.Num : double.NaN).ToList();
                        double[] hover = c["hover"].IsNull ? null : new[] { c["hover"]["x"].Num, c["hover"]["y"].Num };
                        FxOverviewDrawn d;
                        if (c["kind"].Str == "heat")
                        {
                            List<List<JsonValue>> rows = c["rows"].Items.Select(r => r.Items.ToList()).ToList();
                            d = FxOverviewChart.Heat(c["width"].Num, 64, times, rows, hover, tod, 0);
                        }
                        else
                        {
                            string id = c["kind"].Str == "diskBusy" ? "ch-diskbusy" : "ch-" + c["kind"].Str;
                            FxOverviewChartOpts o = FxOverview.ChartOpts(id);
                            List<KeyValuePair<string, JsonValue>> data = c["data"].Props.ToList();
                            d = FxOverviewChart.Draw(o, c["width"].Num, times, data, new HashSet<string>(c["hidden"].Items.Select(x => x.Str)), hover, measure, tod, 0);
                        }
                        PackScript.Same(PackScript.Strings(c["ops"]), d.Ops.Select(x => x.ToString()).ToList(), what + ": ops");
                        if (c["tip"].IsNull) Assert.True(d.Tip == null, what + ": no read-out");
                        else
                        {
                            List<string> tip = new List<string> { d.TipTime };
                            foreach (string[] r in d.Tip) { tip.Add(r[0]); tip.Add(r[1]); }
                            PackScript.Same(PackScript.Strings(c["tip"]), tip, what + ": read-out");
                            Assert.Equal(c["tipLeft"].Str, Format.JsNum(d.TipLeft) + "px", what + ": read-out's left");
                        }
                    }
                    Assert.True(count >= 15, "the charts are read (" + count + ")");
                    foreach (JsonValue b in g["bars"].Items)
                    {
                        int cols;
                        List<FxOverviewCoreBar> bars = FxOverviewCoreBar.Of(b["values"], out cols);
                        Assert.Equal(b["n"].Str, cols.ToString(), "coreBars: the columns");
                        PackScript.Same(b["bars"].Items.Select(x => string.Join(" ", x.Items.Select(y => y.Str))).ToList(), bars.Select(x => x.Title + " " + x.VText + " " + Format.JsNum(x.Hue)).ToList(), "coreBars");
                    }
                    foreach (var kv in g["palette"].Props) Assert.Equal(kv.Value.Str, FxOverviewChart.C[kv.Key], "C." + kv.Key);
                });
                return Task.CompletedTask;
            });

            test("flux overview: paths, the clear dialog, niceMax", () =>
            {
                Assert.Equal("/api/logs?level=problems&limit=500", FxOverview.LogPath("x"), "log");
                Assert.Equal("/api/vehicles/a%20b/scan", FxOverview.ScanPath("a b"), "scan");
                Assert.Equal("Clear the logs? | The log starts empty. Nothing is deleted: the current lines move to | logs/archive/ | and stay downloadable under Cleared logs. | [btn cancel] | Cancel | [btn ok primary] | Clear logs", string.Join(" | ", FxOverview.ClearAsk().Outline("cancel", "ok", "primary")), "dialog");
                Assert.Equal(2.5, FxOverviewChart.NiceMax(2.2), "niceMax 2.2");
                Assert.Equal(1.0, FxOverviewChart.NiceMax(0), "niceMax 0");
                Assert.Equal(100.0, FxOverviewChart.NiceMax(64), "niceMax 64");
                return Task.CompletedTask;
            });
        }
    }
}
