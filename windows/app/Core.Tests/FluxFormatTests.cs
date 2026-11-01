using System;
using System.Collections.Generic;
using SentrySix.Core.Flux;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// The Flux pack's formatting (Core/Flux/Fx.cs) against the pack's own
    /// flux/core.js on the same inputs (Fixtures/expected-flux-format.json,
    /// written by test/windowsNativeFlux.test.js).
    /// </summary>
    static class FluxFormatTests
    {
        static double D(JsonValue x) => x.IsNumber ? x.Num : double.NaN;

        static string Run(string fn, JsonValue a, long now)
        {
            JsonValue x = a[0];
            bool last = a.Count > 0 && a[a.Count - 1].Bool;
            switch (fn)
            {
                case "bytes": return a.Count > 1 ? Fx.Bytes(x, (int)a[1].Num) : Fx.Bytes(x);
                case "rate": return Fx.Rate(x);
                case "pct": return a.Count > 1 ? Fx.Pct(x, (int)a[1].Num) : Fx.Pct(x);
                case "num": return a.Count > 1 ? Fx.Num(x, (int)a[1].Num) : Fx.Num(x);
                case "plural": return a.Count > 2 ? Fx.Plural(x, a[1].Str, a[2].Str) : Fx.Plural(x, a[1].Str);
                case "dur": return Fx.Dur(D(x));
                case "clock": return Fx.Clock(D(x));
                case "ago": return Fx.Ago(x, now);
                case "tod": return Fx.Tod(x.Num, last, a[1].Bool);
                case "when": return Fx.When(x, last);
                case "isoish": return Fx.Isoish(x, last);
                case "wall": return Fx.Wall(x.Str, last, a[1].Bool);
                case "wallDay": return Fx.WallDay(x.Str, last);
                case "watts": return Fx.Watts(x);
                case "energy": return Fx.Energy(x);
                case "money": return Fx.Money(x, a[1].Str);
                case "humanize": return Fx.Humanize(x.Str);
                case "dayTitle": return Fx.DayTitle(x.Str);
                default: throw new AssertFailed("no C# for " + fn);
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("flux: core.js formatters in expected-flux-format.json, from C# (Fx)", () => PackScript.WithUtc(() =>
            {
                JsonValue e = PackScript.Fixture("expected-flux-format.json");
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
