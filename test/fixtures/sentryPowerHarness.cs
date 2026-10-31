// Prints what SentryPower.cs's maths make of fixed inputs, one "name value"
// per line, for test/sentryPower.test.js (no driver, no Windows needed).
using System;
using System.Globalization;
using SentrySixManager.Power;

namespace SentrySixManager.Tests
{
    public static class PowerHarness
    {
        static void Say(string name, object v)
        {
            Console.WriteLine(name + " " + Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        public static int Main(string[] args)
        {
            // AMD Zen 4: MSR_PWR_UNIT 0x000A1003 -> ESU 16 (15.2588 uJ per count)
            Say("amdJpc", Rapl.JoulesPerCount("amd", 97, 0x000A1003UL).ToString("R", CultureInfo.InvariantCulture));
            // Intel: 0x000A0E03 -> ESU 14 (61.035 uJ); Silvermont 0x37 counts 2^ESU uJ
            Say("intelJpc", Rapl.JoulesPerCount("intel", 151, 0x000A0E03UL).ToString("R", CultureInfo.InvariantCulture));
            Say("atomJpc", Rapl.JoulesPerCount("intel", 0x37, 0x00000505UL).ToString("R", CultureInfo.InvariantCulture));

            Say("amd19", Rapl.AmdSupported(0x19));
            Say("amd15", Rapl.AmdSupported(0x15));
            Say("intelRaptor", Rapl.IntelSupported(6, 0xB7));
            Say("intelNehalem", Rapl.IntelSupported(6, 0x1A));
            Say("intelNetburst", Rapl.IntelSupported(15, 4));
            // the families after 6: Nova Lake (0x12), Diamond Rapids (0x13)
            Say("intelNovaLake", Rapl.IntelSupported(18, 1));
            Say("intelDiamondRapids", Rapl.IntelSupported(19, 1));

            // a driver without IOCTL_PIO_VERSION (PawnIO 2.0.x) will do
            string d, why;
            why = PawnIo.Check(false, 0, null, out d);
            Say("check20", (why ?? "ok") + " " + d);
            why = PawnIo.Check(false, 0, "2.0.1", out d);
            Say("check20reg", (why ?? "ok") + " " + d);
            why = PawnIo.Check(true, 0x020200, null, out d);
            Say("check22", (why ?? "ok") + " " + d);
            why = PawnIo.Check(true, 0x010900, null, out d);
            Say("check19", why == null ? "ok" : "old " + d);

            CpuInfo c = CpuInfo.Parse("AuthenticAMD", "AMD64 Family 25 Model 97 Stepping 2");
            Say("cpu", c.Vendor + " " + c.Family + " " + c.Model + " " + c.Module());
            c = CpuInfo.Parse("GenuineIntel", "Intel64 Family 6 Model 151 Stepping 2");
            Say("cpuIntel", c.Vendor + " " + c.Family + " " + c.Model + " " + c.Module());
            c = CpuInfo.Parse("GenuineIntel", "Intel64 Family 18 Model 1 Stepping 0");
            Say("cpuIntel18", c.Vendor + " " + c.Family + " " + c.Model + " " + c.Module());
            c = CpuInfo.Parse("AuthenticAMD", "AMD64 Family 21 Model 2 Stepping 0");
            Say("cpuOld", c.Module() == null ? "none" : c.Module());

            // 100 W for 1 s at ESU 16 is 6553600 counts; the counter wraps in between
            const long S = 10000000L;
            var p = new PackageCounter(Math.Pow(0.5, 16));
            p.Add(0xFFF00000u, 0);
            p.Add(unchecked(0xFFF00000u + 6553600u), S); // wraps past 2^32
            Say("wrapCounts", p.Counts);
            Say("wrapUj", p.EnergyMicrojoules);
            // a gap of more than a minute is not counted
            p.Add(5u, S + 61 * S);
            Say("gapCounts", p.Counts);
            p.Add(5u + 655360u, S + 62 * S);
            Say("afterGapCounts", p.Counts);
            return 0;
        }
    }
}
