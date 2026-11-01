using System;
using System.Collections.Generic;
using System.IO;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// The Horizon pack's formatting (Core/Horizon/Hz.cs) against the pack's own
    /// hz/core.js on the same inputs (Fixtures/expected-horizon-format.json,
    /// written by test/windowsNativeHorizon.test.js).
    /// </summary>
    static class HorizonFormatTests
    {
        static JsonValue Expected()
        {
            return JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-horizon-format.json")));
        }

        static string Run(string fn, JsonValue a, long now)
        {
            JsonValue x = a[0];
            switch (fn)
            {
                case "size": return a.Count > 1 ? Hz.Size(x, (int)a[1].Num) : Hz.Size(x);
                case "perSec": return Hz.PerSec(x);
                case "pc": return a.Count > 1 ? Hz.Pc(x, (int)a[1].Num) : Hz.Pc(x);
                case "fixed": return a.Count > 1 ? Hz.Fixed(x, (int)a[1].Num) : Hz.Fixed(x);
                case "count": return a.Count > 2 ? Hz.Count(x, a[1].Str, a[2].Str) : Hz.Count(x, a[1].Str);
                case "span": return Hz.Span(Hz.Ok(x) ? x.Num : double.NaN);
                case "mmss": return Hz.Mmss(Hz.Ok(x) ? x.Num : double.NaN);
                case "since": return Hz.Since(x, now);
                case "stamp": return Hz.Stamp(x, a[1].Bool);
                case "stampFull": return Hz.StampFull(x, a[1].Bool);
                case "wall": return Hz.Wall(x.Str, a[1].Bool, a.Count > 2 ? a[2].Bool : true);
                case "wallDay": return Hz.WallDay(x.Str, a[1].Bool);
                case "watt": return Hz.Watt(x);
                case "wh": return Hz.Wh(x);
                case "cash": return Hz.Cash(x, a[1].Str);
                case "words": return Hz.Words(x.Str);
                case "dayName": return Hz.DayName(x.Str, a.Count > 1 && a[1].Bool);
                default: throw new AssertFailed("no C# for " + fn);
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("horizon: core.js formatters in expected-horizon-format.json, from C# (Hz)", () =>
            {
                TimeZoneInfo keep = Format.Zone;
                Format.Zone = TimeZoneInfo.Utc;
                try
                {
                    JsonValue e = Expected();
                    long now = e["now"].Long;
                    List<string> bad = new List<string>();
                    foreach (JsonValue c in e["cases"].Items)
                    {
                        string want = c["out"].Str;
                        string got;
                        try { got = Run(c["fn"].Str, c["args"], now); }
                        catch (Exception ex) { got = "threw " + ex.Message; }
                        if (want != got) bad.Add(c["fn"].Str + "(" + c["args"].ToJson() + "): web <" + want + "> C# <" + got + ">");
                    }
                    Assert.True(bad.Count == 0, bad.Count + " of " + e["cases"].Count + " differ:\n      " + string.Join("\n      ", bad));
                }
                finally { Format.Zone = keep; }
            });
        }
    }
}
