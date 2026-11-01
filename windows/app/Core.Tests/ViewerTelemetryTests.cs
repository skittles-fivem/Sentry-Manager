using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's TelemetryFeed and TelemetryCache (how the Viewer gets
    /// the playing segment's telemetry and looks it up as it plays) against
    /// Studio's own seiExtractor.js extractSeiFromEntry and script.js's
    /// telemetryEntryFor, loadNativeSegment's telemetry reset, the
    /// extraction's stale check, applyDashcamProfile's clear,
    /// telemetryAnimationLoop and onMasterTimeUpdate on the same steps
    /// (Fixtures/expected-viewer-telemetry.json, written by
    /// test/windowsNativeViewerTelemetry.test.js).
    /// </summary>
    static class ViewerTelemetryTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-telemetry.json")));

        /// <summary>GET /api/telemetry from the fixture's bodies: the one named now, held while <see cref="Hold"/> is on.</summary>
        sealed class Answers : IJsonApi
        {
            readonly JsonValue bodies;
            public string Name;
            public bool Hold;
            public readonly List<string> Asked = new List<string>();
            public readonly List<Action> Held = new List<Action>();

            public Answers(JsonValue bodies) { this.bodies = bodies; }

            public Task<JsonValue> Get(string path, CancellationToken ct)
            {
                Asked.Add(Uri.UnescapeDataString(path.Substring(path.IndexOf("path=", StringComparison.Ordinal) + 5)));
                JsonValue body = bodies[Name];
                TaskCompletionSource<JsonValue> t = new TaskCompletionSource<JsonValue>();
                Action answer = () =>
                {
                    if (body.Has("$status")) t.SetException(new ApiError((int)body["$status"].Num, null, "Failed"));
                    else t.SetResult(JsonValue.Parse(body.ToJson()));
                };
                if (Hold) Held.Add(answer);
                else answer();
                return t.Task;
            }

            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<byte[]> GetBytes(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) { throw new NotSupportedException(); }
        }

        static void Entries(JsonValue x)
        {
            foreach (JsonValue c in x["entries"].Items)
            {
                PlaybackSegment g = new PlaybackSegment();
                foreach (JsonValue cam in c["cams"].Items) g.FilesByCamera[cam[0].Str] = cam[1].Str;
                string want = c["entry"].IsNull ? null : c["entry"].Str;
                Assert.Equal(want, TelemetryFeed.EntryFor(g, c["master"].Str), "telemetryEntryFor " + c.ToJson());
            }
        }

        static void Cache(JsonValue x)
        {
            Answers api = new Answers(x["bodies"]);
            TelemetryCache cache = new TelemetryCache(api);
            int i = 0;
            foreach (JsonValue s in x["cache"].Items)
            {
                string what = "step " + (i++) + " " + s["op"].Str + " " + s["path"].Str + " (" + s["answer"].Str + ")";
                int before = api.Asked.Count;
                api.Name = s["answer"].Str;
                List<int> counts = new List<int>();
                if (s["op"].Str == "pair")
                {
                    api.Hold = true;
                    Task<ClipTelemetry> one = cache.Get(s["path"].Str, CancellationToken.None);
                    Task<ClipTelemetry> two = cache.Get(s["path"].Str, CancellationToken.None);
                    api.Hold = false;
                    foreach (Action a in api.Held.ToArray()) a();
                    api.Held.Clear();
                    counts.Add(one.Result.Samples.Count);
                    counts.Add(two.Result.Samples.Count);
                }
                else counts.Add(cache.Get(s["path"].Str, CancellationToken.None).Result.Samples.Count);
                Assert.Equal((int)s["requests"].Num, api.Asked.Count - before, what + ": requests");
                Assert.Equal(s["samples"].Count, counts.Count, what + ": answers");
                for (int k = 0; k < counts.Count; k++) Assert.Equal((int)s["samples"][k].Num, counts[k], what + ": samples");
            }
            Assert.True(cache.Kept.Count <= TelemetryCache.MaxClips, "at most eight clips kept");
        }

        static void Feeds(JsonValue x)
        {
            JsonValue bodies = x["bodies"];
            foreach (JsonValue f in x["feeds"].Items)
            {
                TelemetryFeed feed = new TelemetryFeed();
                List<KeyValuePair<int, int>> loads = new List<KeyValuePair<int, int>>();
                int i = 0;
                foreach (JsonValue s in f["steps"].Items)
                {
                    string op = s["op"].Str;
                    string what = f["name"].Str + ", step " + (i++) + " " + op + (s.Has("sec") ? " " + Format.JsNum(s["sec"].Num) : "");
                    List<string> calls = new List<string>();
                    TelemetryStep step = null;
                    if (op == "load")
                    {
                        int seg = (int)s["seg"].Num;
                        loads.Add(new KeyValuePair<int, int>(feed.BeginSegment(seg), seg));
                    }
                    else if (op == "answer")
                    {
                        KeyValuePair<int, int> l = loads[(int)s["load"].Num];
                        ClipTelemetry data = ClipTelemetry.FromServer(bodies[s["clip"].Str]);
                        Assert.Equal(s["kept"].Bool, feed.Deliver(l.Key, l.Value, data), what + ": kept");
                    }
                    else if (op == "profile")
                    {
                        feed.Clear();
                        feed.Enabled = Dashcam.Get(s["profile"].Str == "gm" ? Dashcam.GmcId : Dashcam.TeslaId).Capabilities.Telemetry;
                    }
                    else
                    {
                        step = feed.Tick(s["sec"].Num * 1000, s["dashboard"].Bool, s["transitioning"].Bool);
                        if (step.Parked.HasValue) calls.Add("parked");
                        if (step.Sei != null) calls.Add("sei");
                        if (step.Reset) calls.Add("reset");
                    }
                    JsonValue wantCalls = s["calls"];
                    Assert.Equal(wantCalls.Count, calls.Count, what + ": calls " + wantCalls.ToJson());
                    for (int k = 0; k < calls.Count; k++) Assert.Equal(wantCalls[k].Str, calls[k], what + ": call " + k);
                    if (step != null)
                    {
                        Assert.Equal(s["seq"].IsNull, step.Sei == null, what + ": a record");
                        if (step.Sei != null)
                        {
                            Assert.Equal(Format.JsNum(s["seq"].Num), Format.JsNum(step.Sei.FrameSeqNo), what + ": the record");
                            Assert.Equal((int)s["sei"].Num, feed.Data.IndexAt(s["sec"].Num * 1000), what + ": its index");
                        }
                        Assert.Equal(s["parked"].IsNull ? "null" : s["parked"].Bool ? "true" : "false", step.Parked.HasValue ? (step.Parked.Value ? "true" : "false") : "null", what + ": parked");
                        Assert.Equal(s["reset"].Bool, step.Reset, what + ": reset");
                    }
                    Assert.Equal((int)s["samples"].Num, feed.Data.Samples.Count, what + ": seiData");
                    Assert.Equal((int)s["mapPath"].Num, feed.MapPath.Count, what + ": mapPath");
                    string last = s["lastSeiMs"].IsString ? s["lastSeiMs"].Str : Format.JsNum(s["lastSeiMs"].Num);
                    Assert.Equal(last, double.IsNegativeInfinity(feed.LastSeiMs) ? "-Infinity" : Format.JsNum(feed.LastSeiMs), what + ": lastSeiTimeMs");
                    Assert.Equal(s["dashboardReset"].Bool, feed.DashboardReset, what + ": dashboardReset");
                }
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer telemetry: the master camera's clip, as telemetryEntryFor picks it", () => Entries(Expected()));
            test("viewer telemetry: the 8-clip cache, one request at a time, failures not kept", () => Cache(Expected()));
            test("viewer telemetry: segment loads, stale answers and each tick's record, PARKED and reset", () => Feeds(Expected()));
        }
    }
}
