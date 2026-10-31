using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Nd (the Overview's Server section): the C# models of
    /// web/app.js's serverBody(), vehicleTiles(), hostLine() and alertChip()
    /// against what the web shows of the same samples
    /// (Fixtures/expected-overview.json, written by
    /// test/windowsNativeOverview.test.js), plus steady() and the logs' words.
    /// </summary>
    static class OverviewTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-overview.json")));

        static string Diff(List<string> want, List<string> got)
        {
            int i = 0;
            while (i < want.Count && i < got.Count && want[i] == got[i]) i++;
            string w = string.Join(" | ", want.Skip(Math.Max(0, i - 3)).Take(8));
            string g = string.Join(" | ", got.Skip(Math.Max(0, i - 3)).Take(8));
            return $"first difference at token {i}:\n      web {w}\n      C#  {g}";
        }

        static List<string> Strings(JsonValue arr) => arr.Items.Select(x => x.Str).ToList();

        public static void Register(Action<string, Action> test)
        {
            test("overview: every server section in expected-overview.json, from C#", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                int n = 0;
                foreach (JsonValue c in file["body"].Items)
                {
                    n++;
                    var o = c["opts"];
                    var opts = new NdOptions
                    {
                        Range = o["range"].Str,
                        Hist = o["hist"].Bool ? file["history"] : JsonValue.Null,
                        Currency = o["currency"].Str,
                        MoreDisks = o["moreDisks"].Bool,
                        MoreSensors = o["moreSensors"].Bool,
                        Deployment = o["deployment"].Str,
                        Seen = new NdSeen(),
                        NowMs = file["now"].Long
                    };
                    NdBody body = null;
                    foreach (JsonValue p in c["polls"].Items) body = Nd.ServerBody(file["samples"][p.Str], opts);
                    var want = Strings(c["outline"]);
                    var got = Nd.Outline(body);
                    if (!want.SequenceEqual(got)) bad.Add(c["name"].Str + ": " + Diff(want, got));
                }
                Assert.True(n >= 10, "the cases are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("overview: the vehicle tiles, the host line and the alert chip, from C#", () =>
            {
                var file = Expected();
                var bad = new List<string>();
                foreach (JsonValue c in file["vehicles"].Items)
                {
                    var want = Strings(c["outline"]);
                    var got = Nd.Outline(Nd.VehicleTiles(c["v"], c["at"].Num, c["all"].Items.ToList()));
                    if (!want.SequenceEqual(got)) bad.Add("vehicle " + c["name"].Str + ": " + Diff(want, got));
                }
                foreach (JsonValue c in file["hosts"].Items)
                {
                    var want = Strings(c["parts"]);
                    var got = Nd.HostLine(c["h"]);
                    if (!want.SequenceEqual(got)) bad.Add("host " + c["h"].ToJson() + ": " + Diff(want, got));
                }
                foreach (JsonValue c in file["chips"].Items)
                {
                    var chip = Nd.AlertChip(c["alerts"]);
                    string got = chip.HasValue ? chip.Value.Key : null;
                    if (got != c["text"].Str || (chip.HasValue && chip.Value.Value != c["bad"].Bool)) bad.Add("chip " + c["alerts"].ToJson() + ": " + got);
                }
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("overview: steady() keeps a row's place with its placeholder", () =>
            {
                var seen = new NdSeen();
                Nd.Steady(seen, "t", new List<NdRow> { new NdRow("A", "1"), new NdRow("B", "2"), new NdRow("C", "") });
                var rows = Nd.Steady(seen, "t", new List<NdRow> { new NdRow("A", ""), new NdRow("C", "3"), new NdRow("B", NdText.Of(""), false, "none") });
                Assert.Equal("A=—|C=3|B=none", string.Join("|", rows.Select(r => r.Key + "=" + r.Value.Plain)), "a new row goes after the one before it; placeholders");
                var twice = Nd.Steady(seen, "u", new List<NdRow> { new NdRow("NVMe", "40 °C"), new NdRow("NVMe", "41 °C") });
                Assert.Equal(2, twice.Count, "a label twice is two rows");
                Assert.Equal(1, Nd.Steady(null, "t", new List<NdRow> { new NdRow("A", ""), new NdRow("", "x") }).Count, "without seen: rows with a label");
            });

            test("fixture api: a $pending answer never comes (the Overview's loading state)", () =>
            {
                var api = new FixtureApi(JsonValue.Parse("{\"GET /api/stats\":{\"$pending\":true}}"));
                var t = api.Get("/api/stats", System.Threading.CancellationToken.None);
                Assert.True(!t.IsCompleted, "still waiting");
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    var c = api.Get("/api/stats", cts.Token);
                    cts.Cancel();
                    Assert.True(c.IsCanceled, "a cancelled wait ends");
                }
            });

            test("overview: the logs' words", () =>
            {
                Assert.Equal("No warnings or errors. Tick Everything to see all activity.", Nd.LogsSaid(0, false));
                Assert.Equal("Nothing logged yet.", Nd.LogsSaid(0, true));
                Assert.Equal("1 line of warnings and errors, newest first.", Nd.LogsSaid(1, false));
                Assert.Equal("1,204 lines, newest first.", Nd.LogsSaid(1204, true));
                Assert.Equal("service", Nd.LogsWho("windows"));
                Assert.Equal("container", Nd.LogsWho("docker"));
            });
        }
    }
}
