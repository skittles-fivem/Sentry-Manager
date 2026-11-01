using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's ViewerTimeline, TimeDisplay, PlaybackSpeeds, SkipLength
    /// and SegmentPlan (the Viewer's controls bar, timeline and scrubber)
    /// against Studio's own timeDisplay.js, skipSeconds.js,
    /// playbackTiming.mjs, script.js's speed list, probe, onMasterTimeUpdate,
    /// onMasterLoaded, seekNativeDayCollectionBySec and progress bar listeners
    /// and settingsModal.js's skip length on the same steps
    /// (Fixtures/expected-viewer-timeline.json, written by
    /// test/windowsNativeViewerTimeline.test.js).
    /// </summary>
    static class ViewerTimelineTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-timeline.json")));

        /// <summary>A number from the fixture ("NaN", "Infinity" as words).</summary>
        static double Num(JsonValue v)
        {
            if (v.IsString) return v.Str == "NaN" ? double.NaN : v.Str == "Infinity" ? double.PositiveInfinity : v.Str == "-Infinity" ? double.NegativeInfinity : double.Parse(v.Str);
            return v.Num;
        }

        static string N(double d) => Format.JsNum(d);

        static void Html(JsonValue x)
        {
            JsonValue h = x["html"];
            Assert.Equal(h["speedTitle"].Str, ViewerControlsText.SpeedTitle, "#speedSelect title");
            Assert.Equal(h["skipBackTitle"].Str, ViewerControlsText.SkipBackTitle, "#skipBackBtn title");
            Assert.Equal(h["skipForwardTitle"].Str, ViewerControlsText.SkipForwardTitle, "#skipForwardBtn title");
            Assert.Equal(h["settingsTitle"].Str, ViewerControlsText.SettingsTitle, "#settingsBtn title");
            ViewerTimeline fresh = new ViewerTimeline();
            Assert.Equal(h["time"][0].Str, fresh.CurrentText, "#currentTime at first");
            Assert.Equal(h["time"][1].Str, fresh.TotalText, "#totalTime at first");
            JsonValue options = x["speeds"]["options"];
            Assert.Equal(options.Count, PlaybackSpeeds.Options.Length, "speed options");
            for (int i = 0; i < options.Count; i++)
            {
                Assert.Equal(options[i]["value"].Str, PlaybackSpeeds.Value(PlaybackSpeeds.Options[i]), "option " + i + " value");
                Assert.Equal(options[i]["label"].Str, PlaybackSpeeds.Label(PlaybackSpeeds.Options[i]), "option " + i + " words");
                Assert.Equal(options[i]["selected"].Bool, PlaybackSpeeds.Options[i] == 1, "option " + i + " selected");
            }
        }

        static void Times(JsonValue x)
        {
            foreach (JsonValue t in x["times"].Items)
                Assert.Equal(t["text"].Str, TimeDisplay.FormatHMS(Num(t["s"])), "formatTimeHMS(" + t["s"].ToJson() + ")");
        }

        static void Speeds(JsonValue x)
        {
            foreach (JsonValue r in x["speeds"]["restore"].Items)
            {
                string saved = r["saved"].IsNull ? null : r["saved"].Str;
                double rate = PlaybackSpeeds.Restore(saved, 1);
                Assert.Equal(N(Num(r["rate"])), N(rate), "restore " + r["saved"].ToJson());
                Assert.Equal(r["select"].Str, PlaybackSpeeds.Value(rate), "restore " + r["saved"].ToJson() + " select");
            }
            foreach (JsonValue b in x["speeds"]["bump"].Items)
            {
                double rate = b["rate"].Num;
                int dir = (int)b["dir"].Num;
                double next = PlaybackSpeeds.Bump(rate, dir);
                string what = "bump " + N(rate) + " by " + dir;
                if (double.IsNaN(next))
                {
                    Assert.Equal(N(b["after"].Num), N(rate), what + ": stays");
                    Assert.True(b["stored"].IsNull, what + ": nothing stored");
                }
                else
                {
                    Assert.Equal(N(b["after"].Num), N(next), what);
                    Assert.Equal(b["select"].Str, PlaybackSpeeds.Value(next), what + " select");
                    Assert.Equal(b["stored"].Str, PlaybackSpeeds.Value(next), what + " stored");
                    Assert.Equal(N(next), N(PlaybackSpeeds.Apply(PlaybackSpeeds.Value(next))), what + " applied");
                }
            }
        }

        static JsonValue Setting(JsonValue v) => v.IsObject && v.Has("$undefined") ? null : v;

        static void Skips(JsonValue x)
        {
            foreach (JsonValue s in x["skip"].Items)
            {
                JsonValue d = SkipLength.Of(Setting(s["setting"]));
                string what = "skipDuration " + s["setting"].ToJson();
                Assert.Equal(s["duration"].ToJson(), d.ToJson(), what);
                Assert.Equal(s["backLabel"].Str, SkipLength.Label(d), what + " back label");
                Assert.Equal(s["forwardLabel"].Str, SkipLength.Label(d), what + " forward label");
                Assert.Equal(s["backTitle"].Str, SkipLength.BackTitle(d), what + " back title");
                Assert.Equal(s["forwardTitle"].Str, SkipLength.ForwardTitle(d), what + " forward title");
            }
        }

        static void Collection(JsonValue c)
        {
            JsonValue probes = c["probes"];
            SegmentPlan plan = new SegmentPlan(probes.Count, c["fallback"].Num);
            ViewerTimeline tl = new ViewerTimeline();
            double lastTarget = double.NaN;
            bool afterUp = false;
            bool seeking = false;
            JsonValue steps = c["steps"];
            for (int i = 0; i < steps.Count; i++)
            {
                JsonValue s = steps[i];
                string op = s["op"].Str;
                string what = c["name"].Str + " step " + i + " (" + op + ")";
                tl.Seeking = seeking;
                bool texts = false;
                switch (op)
                {
                    case "probe":
                        plan.SetProbed(probes.Items.Select(p => p.IsNull ? double.NaN : p.Num).ToList());
                        for (int k = 0; k < plan.Count; k++) Assert.Equal(N(s["probed"][k].Num), N(plan.Duration(k)), what + " duration " + k);
                        {
                            string cur, tot;
                            TimeDisplay.Collection(plan.Start(0), plan.Total, out cur, out tot);
                            Assert.Equal(s["current"].Str, cur, what + " #currentTime");
                            Assert.Equal(s["total"].Str, tot, what + " #totalTime");
                        }
                        break;
                    case "tick":
                        tl.Tick(plan.Start((int)s["seg"].Num) + s["t"].Num, plan.Total);
                        texts = true;
                        break;
                    case "loaded":
                        plan.SetActual((int)s["seg"].Num, s["duration"].Num);
                        break;
                    case "skip":
                        {
                            JsonValue d = s.Has("duration") ? s["duration"] : null;
                            double target = SkipLength.Target(plan.Start((int)s["seg"].Num) + s["t"].Num, plan.Total, d, s["forward"].Bool);
                            Assert.Equal(N(s["target"].Num), N(target), what + " target");
                        }
                        break;
                    case "down":
                        tl.PointerDown(s["playing"].Bool);
                        break;
                    case "input":
                    case "change":
                        {
                            double pct = s["pct"].Num;
                            double target = op == "input" ? tl.Input(pct, plan.Total) : tl.Change(pct, plan.Total);
                            Assert.Equal(1, s["requested"].Count, what + " one seek asked");
                            Assert.Equal(N(s["requested"][0].Num), N(target), what + " seek target");
                            Assert.Equal(N(s["value"].Num), N(tl.Value), what + " #progressBar");
                            lastTarget = target;
                        }
                        break;
                    case "up":
                        tl.PointerUp();
                        afterUp = true;
                        break;
                    case "wait":
                        if (!double.IsNaN(lastTarget))
                        {
                            // the last asked seek has landed (latest wins)
                            tl.Seeked(lastTarget, plan.Total);
                            int seg;
                            double local;
                            plan.Locate(lastTarget, out seg, out local);
                            Assert.Equal(N(s["at"]["seg"].Num), N(seg), what + " segment");
                            Assert.Equal(N(s["at"]["t"].Num), N(local), what + " time in it");
                            lastTarget = double.NaN;
                            texts = true;
                        }
                        if (afterUp)
                        {
                            Assert.Equal(s["playing"].Bool, tl.WasPlayingBeforeScrub, what + " playing after the scrub");
                            afterUp = false;
                        }
                        break;
                    default:
                        throw new Exception("unknown step " + op);
                }
                for (int k = 0; k <= plan.Count; k++) Assert.Equal(N(s["starts"][k].Num), N(plan.Start(k)), what + " cumulativeStarts[" + k + "]");
                Assert.Equal(s["scrubbing"].Bool, tl.Scrubbing, what + " isScrubbing");
                if (texts)
                {
                    Assert.Equal(N(s["value"].Num), N(tl.Value), what + " #progressBar");
                    Assert.Equal(s["current"].Str, tl.CurrentText, what + " #currentTime");
                    Assert.Equal(s["total"].Str, tl.TotalText, what + " #totalTime");
                }
                seeking = s["seeking"].Bool;
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer timeline: the time display, speed list and skip length as Studio has them", () =>
            {
                JsonValue x = Expected();
                Html(x);
                Times(x);
                Speeds(x);
                Skips(x);
            });
            test("viewer timeline: durations, ticks, skips, seeks and the scrubber as Studio does them", () =>
            {
                foreach (JsonValue c in Expected()["collections"].Items) Collection(c);
            });
            test("viewer timeline: the range keeps whole steps within 0..100", () =>
            {
                Assert.Equal("33", N(ViewerTimeline.RangeValue(33.333)), "33.333");
                Assert.Equal("34", N(ViewerTimeline.RangeValue(33.5)), "a half up");
                Assert.Equal("100", N(ViewerTimeline.RangeValue(140)), "past 100");
                Assert.Equal("0", N(ViewerTimeline.RangeValue(-3)), "below 0");
                Assert.Equal("50", N(ViewerTimeline.RangeValue(double.NaN)), "not a number");
            });
        }
    }
}
