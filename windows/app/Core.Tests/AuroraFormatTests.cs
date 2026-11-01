using System;
using System.Collections.Generic;
using SentrySix.Core.Aurora;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// The Aurora pack's formatting (Core/Aurora/Au.cs) against the pack's own
    /// aurora/core.js on the same inputs (Fixtures/expected-aurora-format.json,
    /// written by test/windowsNativeAurora.test.js).
    /// </summary>
    static class AuroraFormatTests
    {
        static double D(JsonValue x) => x.IsNumber ? x.Num : double.NaN;

        static string Run(string fn, JsonValue a, long now)
        {
            JsonValue x = a[0];
            bool last = a.Count > 0 && a[a.Count - 1].Bool;
            switch (fn)
            {
                case "bytes": return a.Count > 1 ? Au.Bytes(x, (int)a[1].Num) : Au.Bytes(x);
                case "rate": return Au.Rate(x);
                case "pct": return a.Count > 1 ? Au.Pct(x, (int)a[1].Num) : Au.Pct(x);
                case "num": return a.Count > 1 ? Au.Num(x, (int)a[1].Num) : Au.Num(x);
                case "plural": return a.Count > 2 ? Au.Plural(x, a[1].Str, a[2].Str) : Au.Plural(x, a[1].Str);
                case "dur": return Au.Dur(D(x));
                case "clock": return Au.Clock(D(x));
                case "ago": return Au.Ago(x, now);
                case "when": return Au.When(x, last);
                case "whenFull": return Au.WhenFull(x, last);
                case "wall": return Au.Wall(x.IsNull ? null : x.Str, last, a[1].Bool);
                case "timeOf": return Au.TimeOf(x.IsNull ? null : x.Str, last);
                case "hourName": return Au.HourName((int)x.Num, last);
                case "watts": return Au.Watts(x);
                case "energy": return Au.Energy(x);
                case "money": return a.Count > 1 ? Au.Money(x, a[1].Str) : Au.Money(x);
                case "humanize": return Au.Humanize(x.IsNull ? null : x.Str);
                case "dayTitle": return Au.DayTitle(x.Str);
                case "dayParts": { AuDayParts p = Au.DayParts(x.Str); return p.Wd + "|" + p.Dd + "|" + p.Mon; }
                default: throw new AssertFailed("no C# for " + fn);
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("aurora: core.js formatters in expected-aurora-format.json, from C# (Au)", () => PackScript.WithUtc(() =>
            {
                JsonValue e = PackScript.Fixture("expected-aurora-format.json");
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
        }
    }
}
