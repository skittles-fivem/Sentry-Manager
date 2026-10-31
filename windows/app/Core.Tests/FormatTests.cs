using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's Format, Wording, Rich, Converter and FixtureApi: the
    /// formatting against the web's own outputs (Fixtures/expected-format.json,
    /// written by test/windowsNativeParity.test.js from web/app.js), the rest
    /// on their own.
    /// </summary>
    static class FormatTests
    {
        static string Fixtures() => Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures");

        static double D(JsonValue v) => v.IsNull ? double.NaN : v.Num;

        static string Call(string fn, IReadOnlyList<JsonValue> a, long now)
        {
            switch (fn)
            {
                case "bytes": return Format.Bytes(a[0]);
                case "plural": return Format.Plural(D(a[0]), a[1].Str);
                case "when": return Format.When((long)a[0].Num, a[1].Bool);
                case "clockText": return Format.ClockText(a[0].Str, a[1].Bool);
                case "took": return Format.Took(a[0].Num);
                case "ago": return Format.Ago((long)a[0].Num, now);
                case "humanize": return Format.Humanize(a[0].Str);
                case "minutesText": return Format.MinutesText(a[0].Num);
                case "dayTitle": return Format.DayTitle(a[0].Str);
                case "clampPct": return Format.JsNum(Format.ClampPct(D(a[0])));
                case "size": return Format.Size(D(a[0]));
                case "sizeOf": return Format.SizeOf(a[0].Num, a[1].Num, a[2].Str);
                case "rate": return Format.Rate(D(a[0]));
                case "pct": return Format.Pct(a[0].Num);
                case "watts": return Format.Watts(a[0].Num);
                case "temp": return Format.Temp(a[0].Num);
                case "clock": return Format.Clock(a[0].Num);
                case "energyText": return Format.EnergyText(a[0].Num);
                case "inCurrency": return Format.InCurrency(a[0].Str, a[1].Str);
                case "money": return Format.Money(a[0].Num, a[1].Str);
                case "span": return Format.Span(a[0].Num);
                case "answer": return Format.Answer(a[0].Num);
                case "linkSpeed": return Format.LinkSpeed(D(a[0]));
                case "cpuName": return Format.CpuName(a[0].Str);
                case "toldHow": return Wording.ToldHow(a[0]);
                case "serviceAccount": return Wording.ServiceAccount(a[0]);
                default: throw new AssertFailed("no C# twin for " + fn);
            }
        }

        static string Runs(Rich r) => string.Join("|", r.Runs.Select(x => x.ToString()));

        public static void Register(Action<string, Action> test, Action<string, Func<Task>> testAsync)
        {
            test("format: every web/app.js output in expected-format.json, from C#", () =>
            {
                var file = JsonValue.Parse(File.ReadAllText(Path.Combine(Fixtures(), "expected-format.json")));
                TimeZoneInfo keep = Format.Zone;
                Format.Zone = TimeZoneInfo.Utc;
                var bad = new List<string>();
                int n = 0;
                try
                {
                    foreach (JsonValue c in file["cases"].Items)
                    {
                        string fn = c["fn"].Str;
                        var args = c["args"].Items;
                        n++;
                        if (fn == "printedIn" || fn == "macAccount")
                        {
                            Rich got = fn == "printedIn" ? Wording.PrintedIn(args[0].Str) : Wording.MacAccount(args[0]);
                            string want = Runs(Rich.FromHtml(c["out"].Str));
                            if (want != Runs(got)) bad.Add($"{fn}({c["args"].ToJson()}): web {want}, C# {Runs(got)}");
                            continue;
                        }
                        string wantText = c["out"].IsNumber ? Format.JsNum(c["out"].Num) : c["out"].Str;
                        string gotText;
                        try { gotText = Call(fn, args, c["now"].Long); }
                        catch (AssertFailed) { throw; }
                        catch (Exception e) { gotText = "threw " + e.GetType().Name + ": " + e.Message; }
                        if (wantText != gotText) bad.Add($"{fn}({c["args"].ToJson()}): web \"{wantText}\", C# \"{gotText}\"");
                    }
                }
                finally
                {
                    Format.Zone = keep;
                }
                Assert.True(n > 100, "the cases are read (" + n + ")");
                Assert.True(bad.Count == 0, bad.Count + " differ:\n    " + string.Join("\n    ", bad));
            });

            test("format: toFixed is exact (no 15-digit rounding first)", () =>
            {
                Assert.Equal("0.1", Format.ToFixed(0.15, 1), "0.15 is 0.1499…");
                Assert.Equal("1.3", Format.ToFixed(1.25, 1), "a true tie goes up");
                Assert.Equal("0.14", Format.ToFixed(0.135, 2), "0.135 is 0.13500…01");
                Assert.Equal("1.00", Format.ToFixed(1.005, 2), "1.005 is 1.00499…");
                Assert.Equal("-2.0", Format.ToFixed(-2, 1));
                Assert.Equal("0", Format.ToFixed(4.9e-324, 0));
                Assert.Equal("123456789012345", Format.ToFixed(123456789012345, 0));
            });

            test("rich: the pages' small HTML is runs", () =>
            {
                var r = Rich.FromHtml("Run <code>a &amp; b</code>, then <b>stop</b><wbr> <a href=\"#/settings\">Settings</a><br><span class=\"mono\">x</span>");
                Assert.Equal("Text:Run |Code:a & b|Text:, then |Bold:stop|Text: |Link:Settings->#/settings|Break:|Mono:x", Runs(r));
                Assert.Equal("Run a & b, then stop Settings\nx", r.Plain);
                Assert.Equal("Text:x", Runs(Rich.FromHtml("<em>x</em>")), "an unknown tag keeps its text");
            });

            testAsync("converter: invoke's envelope, and the error words", async () =>
            {
                var api = new FixtureApi(JsonValue.Parse("{\"POST /api/converter/invoke/getAuto\":{\"ok\":true,\"value\":{\"enabled\":true}},\"POST /api/converter/invoke/setAuto\":{\"ok\":false,\"error\":{\"code\":\"INVALID_AUTO_WINDOW\"}}}"));
                Assert.True((await Converter.Invoke(api, "getAuto", null))["enabled"].Bool, "value");
                var e = await Assert.ThrowsAsync<ConverterError>(() => Converter.Invoke(api, "setAuto", JsonValue.Object()), "ok:false");
                Assert.Equal("INVALID_AUTO_WINDOW", e.Code);
                Assert.Equal("The time window needs a start and an end that differ.", Converter.ErrorText(e));
                Assert.Equal("Something else", Converter.ErrorText(new ApiError(500, null, "Something else")));
                Assert.Equal("That didn’t work.", Converter.ErrorText(null));
                // no entry for a POST: { ok: true } without a value, which the Encoder never sends
                Assert.True((await Converter.Invoke(api, "devices", null)).IsNull, "ok without value");
            });

            testAsync("fixture api: exact keys, variants, errors, no bytes", async () =>
            {
                var fx = JsonValue.Parse("{\"api\":{\"GET /api/jobs?limit=50\":{\"jobs\":[1]},\"GET /api/bin\":{\"items\":[1]}},\"variants\":{\"bin-error\":{\"api\":{\"GET /api/bin\":{\"$error\":\"Disk gone\",\"$status\":503}}}}}");
                var plain = FixtureApi.For(fx, null);
                Assert.Equal(1, (await plain.Get("/api/jobs?limit=50", CancellationToken.None))["jobs"].Count);
                await Assert.ApiFails(() => plain.Get("/api/jobs", CancellationToken.None), 404, "NOT_FOUND", "another query is another key");
                var v = FixtureApi.For(fx, "bin-error");
                var err = await Assert.ApiFails(() => v.Get("/api/bin", CancellationToken.None), 503, null, "a variant's error");
                Assert.Equal("Disk gone", err.Message);
                Assert.True((await v.Put("/api/settings", null, CancellationToken.None))["ok"].Bool, "writes say ok");
                await Assert.ThrowsAsync<ApiError>(() => v.GetBytes("/api/vehicles/1/recordings/2/thumbnail", CancellationToken.None), "no thumbnails");
                Assert.True(v.Get("/api/jobs?limit=50", CancellationToken.None).IsCompleted, "answers are already there");
            });

            testAsync("api calls: a GET that outlasts its timeout is TIMEOUT", async () =>
            {
                var e = await Assert.ApiFails(() => ApiCalls.GetWithin(new SlowApi(), "/api/stats", 50, CancellationToken.None), 0, "TIMEOUT", "slow stats");
                Assert.Equal(Api.TimeoutMessage, e.Message);
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    await Assert.ThrowsAsync<OperationCanceledException>(() => ApiCalls.GetWithin(new SlowApi(), "/api/stats", 5000, cts.Token), "cancelled by the page");
                }
            });
        }

        sealed class SlowApi : IJsonApi
        {
            public async Task<JsonValue> Get(string path, CancellationToken ct) { await Task.Delay(10000, ct); return JsonValue.Object(); }
            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) => Get(path, ct);
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => Get(path, ct);
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => Get(path, ct);
            public Task<byte[]> GetBytes(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => Get(path, ct);
        }
    }
}
