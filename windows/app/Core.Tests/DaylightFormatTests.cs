using System;
using System.Collections.Generic;
using SentrySix.Core.Daylight;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// The Daylight pack's formatting (Core/Daylight/Dl.cs) against the pack's
    /// own dl/core.js and dl/state.js on the same inputs
    /// (Fixtures/expected-daylight-format.json, written by test/windowsNativeDaylight.test.js).
    /// </summary>
    static class DaylightFormatTests
    {
        static string S(JsonValue x) => x.IsNull ? null : x.Str;

        static string Run(string fn, JsonValue a, long now)
        {
            JsonValue x = a[0];
            bool last = a.Count > 0 && a[a.Count - 1].Bool;
            switch (fn)
            {
                case "bytes": return Dl.Bytes(x);
                case "plural": return a.Count > 2 ? Dl.Plural(x, a[1].Str, a[2].Str) : Dl.Plural(x, a[1].Str);
                case "pct": return Dl.Pct(x);
                case "ago": return Dl.Ago(x, now);
                case "wall": return Dl.Wall(S(x), last, a[1].Bool);
                case "niceTime": return Dl.NiceTime(S(x), last);
                case "when": return Dl.When(x, last);
                case "duration": return Dl.Duration(x);
                case "clockOf": return Dl.ClockOf(Hz.Number(x));
                case "dayTitle": return Dl.DayTitle(S(x));
                case "dayShort": return Dl.DayShort(S(x));
                case "hhmm": return Dl.Hhmm(S(x));
                case "humanize": return Dl.Humanize(S(x));
                case "toldHow": return Dl.ToldHow(x);
                case "shareHealth":
                    {
                        DlHealth h = Dl.ShareHealth(x);
                        return h.Tone + " | " + h.Word + " | " + (h.Ok ? "true" : "false") + " | " + h.Detail;
                    }
                default: throw new AssertFailed("no C# for " + fn);
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("daylight: core.js formatters in expected-daylight-format.json, from C# (Dl)", () => PackScript.WithUtc(() =>
            {
                JsonValue e = PackScript.Fixture("expected-daylight-format.json");
                long now = e["now"].Long;
                List<string> bad = new List<string>();
                foreach (JsonValue c in e["cases"].Items)
                {
                    string want = c["out"].Str, got;
                    try { got = Run(c["fn"].Str, c["args"], now); } catch (Exception ex) { got = "threw " + ex.Message; }
                    if (want != got) bad.Add(c["fn"].Str + "(" + c["args"].ToJson() + "): web <" + want + "> C# <" + got + ">");
                }
                Assert.True(bad.Count == 0, bad.Count + " of " + e["cases"].Count + " differ:\n      " + string.Join("\n      ", bad));
            }));

            test("daylight: categories and codecs", () =>
            {
                Assert.Equal("violet", Dl.Category("Saved")[2], "Saved's tone");
                Assert.Equal("Sentry events", Dl.Category("Sentry")[1], "Sentry's many");
                Assert.True(Dl.Category("x") == null, "no category");
                Assert.Equal("H.265", Dl.Codec("h265"), "h265");
                Assert.True(Dl.Codec("vp9") == null, "no codec");
            });
        }
    }
}
