// SentryPower.exe: the "Sentry Six Manager CPU power" service
// (SentrySixPower). It reads the CPU package energy counters (RAPL) through
// the signed PawnIO driver (https://pawnio.eu) and hands the readings to
// SentryHelper.exe over a local named pipe, so the Windows build shows the
// CPU package watts the Docker build reads from
// /sys/class/powercap/intel-rapl:N/energy_uj.
//
// Why a service of its own: PawnIO's device (\Device\PawnIO) may be opened
// by SYSTEM and Administrators only (its INF sets D:P(A;;GA;;;SY)(A;;GA;;;BA)).
// The web app runs as NT SERVICE\SentrySixManager or as the person signed
// in, and neither should be trusted with the driver: a module loaded into it
// can also write MSRs. So this small LocalSystem service holds the one
// handle, loads one signed module (AMDFamily17.bin or IntelMSR.bin, embedded
// in this exe, from PawnIO.Modules), and only ever calls that module's
// ioctl_read_msr with a fixed list of two MSR numbers. Nothing a client
// sends reaches the driver: the pipe is write-only towards the client (it
// gets one JSON line per connection and the pipe is closed), and its
// permissions keep out network logons.
//
//   AMD family 17h-1Ah (Zen 1-5): MSR_PWR_UNIT 0xC0010299, MSR_PKG_ENERGY_STAT 0xC001029B
//   Intel (Sandy Bridge and newer): MSR_RAPL_POWER_UNIT 0x606, MSR_PKG_ENERGY_STATUS 0x611
//
// The energy MSRs are 32-bit counters that wrap (AMD at the usual unit of
// 15.3 uJ after about 65 kJ: 11 minutes at 100 W), so the service reads them
// every second, on a CPU of each package (MSRs are per CPU), and keeps a
// 64-bit running total in microjoules per package, as energy_uj is on Linux.
//
// The pipe: \\.\pipe\SentrySixPower. One line, then the server hangs up:
//   {"ok":true,"source":"pawnio-rapl","vendor":"amd","driver":"2.2.0",
//    "packages":[{"socket":0,"energyUj":"123456789","t100ns":"987654321"}]}
//   {"ok":false,"reason":"no-driver"}   (no-driver, old-driver, cpu,
//                                        load-failed, msr-failed, starting)
// energyUj and t100ns are decimal strings; t100ns is a monotonic clock
// (QueryPerformanceCounter, in 100 ns), only for working out the time
// between two readings.
//
// Builds with the csc.exe of .NET Framework 4.8 and with Mono's mcs (the
// tests compile it): C# 7.2, .NET Framework 4.8 APIs only, no NuGet.
//
//   SentryPower.exe          the service (started by Windows)
//   SentryPower.exe --once   as administrator: two readings a second apart,
//                            printed with the watts (to check a PC by hand)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("Sentry Six Manager CPU power")]
[assembly: AssemblyProduct("Sentry Six Manager")]

namespace SentrySixManager.Power
{
    // ------------------------------------------------------------ the maths

    public static class Rapl
    {
        public const uint AmdPowerUnit = 0xC0010299;
        public const uint AmdPackageEnergy = 0xC001029B;
        public const uint IntelPowerUnit = 0x606;
        public const uint IntelPackageEnergy = 0x611;

        // Silvermont and Airmont Atoms count energy in 2^ESU microjoules
        // (Intel SDM, and LibreHardwareMonitor's IntelCpu.cs), every other
        // RAPL CPU in 1/2^ESU joules.
        static readonly int[] IntelMicrojouleModels = { 0x37, 0x4A, 0x4C, 0x4D, 0x5A, 0x5D };

        // family 6 models from before Sandy Bridge (no RAPL MSRs: reading
        // 0x606 there faults, which the driver turns into an error, but
        // these are not even tried)
        static readonly int[] IntelBeforeRapl = { 0x0F, 0x16, 0x17, 0x1A, 0x1C, 0x1D, 0x1E, 0x1F, 0x25, 0x26, 0x27, 0x2C, 0x2E, 0x2F, 0x35, 0x36 };

        /** AMD Zen 1 to Zen 5: the families AMDFamily17.bin accepts. */
        public static bool AmdSupported(int family)
        {
            return family >= 0x17 && family <= 0x1A;
        }

        public static bool IntelSupported(int family, int model)
        {
            return family == 6 && model > 0 && Array.IndexOf(IntelBeforeRapl, model) < 0;
        }

        /** Joules per count of the package energy counter, from the power unit MSR. */
        public static double JoulesPerCount(string vendor, int model, ulong powerUnit)
        {
            int esu = (int)((powerUnit >> 8) & 0x1F);
            if (vendor == "intel" && Array.IndexOf(IntelMicrojouleModels, model) >= 0) return 1e-6 * Math.Pow(2, esu);
            return Math.Pow(0.5, esu);
        }
    }

    /**
     * One package's 32-bit energy counter as a 64-bit running total. Read
     * every second, the counter can only have wrapped once between two
     * readings, which unsigned subtraction takes care of. After a longer gap
     * (the PC slept, the service was held up for a minute) nobody knows how
     * often it wrapped, so that step is not counted: the total stays where it
     * was, and the next readings go on from the new value.
     */
    public sealed class PackageCounter
    {
        public const long MaxGap100ns = 60L * 10000000L;
        readonly double joulesPerCount;
        uint last;
        long lastAt;
        bool started;
        ulong counts;

        public PackageCounter(double joulesPerCount)
        {
            this.joulesPerCount = joulesPerCount;
        }

        public void Add(uint raw, long at100ns)
        {
            if (started && at100ns >= lastAt && at100ns - lastAt <= MaxGap100ns) counts += unchecked(raw - last);
            last = raw;
            lastAt = at100ns;
            started = true;
        }

        public ulong Counts { get { return counts; } }

        public ulong EnergyMicrojoules
        {
            get { return (ulong)Math.Round(counts * joulesPerCount * 1e6); }
        }
    }

    // --------------------------------------------------------------- Windows

    static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        [StructLayout(LayoutKind.Sequential)]
        public struct GROUP_AFFINITY
        {
            public UIntPtr Mask;
            public ushort Group;
            public ushort Reserved0, Reserved1, Reserved2;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetThreadGroupAffinity(IntPtr thread, ref GROUP_AFFINITY affinity, out GROUP_AFFINITY previous);

        public const int RelationProcessorPackage = 3;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetLogicalProcessorInformationEx(int relation, IntPtr buffer, ref int length);

        // named pipe
        public const uint PIPE_ACCESS_DUPLEX = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
        public const uint PIPE_REJECT_REMOTE_CLIENTS = 8;

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances, uint outSize, uint inSize, uint timeout, ref SECURITY_ATTRIBUTES sa);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr sd, IntPtr size);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr mem);
    }

    /** The CPU, as Windows describes it (the same values CPUID gives). */
    public sealed class CpuInfo
    {
        public string Vendor; // "amd", "intel", or what VendorIdentifier says
        public int Family;
        public int Model;

        /** "AMD64 Family 25 Model 97 Stepping 2" with "AuthenticAMD". */
        public static CpuInfo Parse(string vendorIdentifier, string identifier)
        {
            var info = new CpuInfo();
            string v = (vendorIdentifier ?? "").Trim();
            info.Vendor = v == "AuthenticAMD" ? "amd" : v == "GenuineIntel" ? "intel" : v;
            Match m = Regex.Match(identifier ?? "", @"Family (\d+) Model (\d+)");
            if (m.Success)
            {
                info.Family = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                info.Model = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            return info;
        }

        public static CpuInfo Read()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            {
                if (k == null) return Parse(null, null);
                return Parse(k.GetValue("VendorIdentifier") as string, k.GetValue("Identifier") as string);
            }
        }

        /** The module that reads this CPU's MSRs, or null when none does. */
        public string Module()
        {
            if (Vendor == "amd" && Rapl.AmdSupported(Family)) return "AMDFamily17.bin";
            if (Vendor == "intel" && Rapl.IntelSupported(Family, Model)) return "IntelMSR.bin";
            return null;
        }
    }

    /** The first logical CPU of each package (socket), as a group affinity. */
    static class Packages
    {
        public static List<Native.GROUP_AFFINITY> List()
        {
            var result = new List<Native.GROUP_AFFINITY>();
            int len = 0;
            Native.GetLogicalProcessorInformationEx(Native.RelationProcessorPackage, IntPtr.Zero, ref len);
            if (len <= 0) return result;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!Native.GetLogicalProcessorInformationEx(Native.RelationProcessorPackage, buf, ref len)) return result;
                int off = 0;
                while (off + 48 <= len)
                {
                    // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship (4),
                    // Size (4), then PROCESSOR_RELATIONSHIP: Flags, EfficiencyClass,
                    // Reserved[20], GroupCount (2 at 30), GroupMask[] at 32 (each
                    // 16: Mask 8, Group 2, Reserved 6)
                    int rel = Marshal.ReadInt32(buf, off);
                    int size = Marshal.ReadInt32(buf, off + 4);
                    if (size <= 0) break;
                    if (rel == Native.RelationProcessorPackage)
                    {
                        int groups = Marshal.ReadInt16(buf, off + 30);
                        for (int g = 0; g < groups; g++)
                        {
                            long mask = Marshal.ReadInt64(buf, off + 32 + 16 * g);
                            ushort group = (ushort)Marshal.ReadInt16(buf, off + 40 + 16 * g);
                            if (mask == 0) continue;
                            // the lowest CPU of the package
                            ulong lowest = (ulong)mask & (ulong)(-mask);
                            result.Add(new Native.GROUP_AFFINITY { Mask = new UIntPtr(lowest), Group = group });
                            break;
                        }
                    }
                    off += size;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return result;
        }
    }

    /** One handle to \Device\PawnIO with one module loaded. */
    sealed class PawnIo : IDisposable
    {
        const uint IoctlLoadBinary = 0xA1B22084;
        const uint IoctlExecuteFn = 0xA1B22104;
        const uint IoctlVersion = 0xA1B22184;
        public const uint MinVersion = 0x020000; // 2.0.0
        readonly SafeFileHandle h;

        PawnIo(SafeFileHandle h) { this.h = h; }

        /** Opens the device, or returns null (no driver, or not allowed to). */
        public static PawnIo Open(out int error)
        {
            foreach (string path in new[] { @"\\?\GLOBALROOT\Device\PawnIO", @"\\.\PawnIO" })
            {
                SafeFileHandle h = Native.CreateFileW(path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                error = Marshal.GetLastWin32Error();
                if (!h.IsInvalid) return new PawnIo(h);
                h.Dispose();
            }
            error = Marshal.GetLastWin32Error();
            return null;
        }

        public uint Version()
        {
            var outBuf = new byte[4];
            int got;
            if (!Native.DeviceIoControl(h, IoctlVersion, null, 0, outBuf, 4, out got, IntPtr.Zero) || got < 4) return 0;
            return BitConverter.ToUInt32(outBuf, 0);
        }

        public static string VersionText(uint v)
        {
            return ((v >> 16) & 0xFF) + "." + ((v >> 8) & 0xFF) + "." + (v & 0xFF);
        }

        public bool Load(byte[] module)
        {
            int got;
            return Native.DeviceIoControl(h, IoctlLoadBinary, module, module.Length, null, 0, out got, IntPtr.Zero);
        }

        /**
         * The module's ioctl_read_msr for one MSR: a 32-byte function name,
         * then the MSR number; the answer is the 64-bit value. Only the MSRs
         * this service reads may be asked for.
         */
        public bool ReadMsr(uint msr, out ulong value)
        {
            value = 0;
            if (msr != Rapl.AmdPowerUnit && msr != Rapl.AmdPackageEnergy && msr != Rapl.IntelPowerUnit && msr != Rapl.IntelPackageEnergy) return false;
            var inBuf = new byte[32 + 8];
            byte[] name = Encoding.ASCII.GetBytes("ioctl_read_msr");
            Buffer.BlockCopy(name, 0, inBuf, 0, name.Length);
            Buffer.BlockCopy(BitConverter.GetBytes((ulong)msr), 0, inBuf, 32, 8);
            var outBuf = new byte[8];
            int got;
            if (!Native.DeviceIoControl(h, IoctlExecuteFn, inBuf, inBuf.Length, outBuf, outBuf.Length, out got, IntPtr.Zero) || got < 8) return false;
            value = BitConverter.ToUInt64(outBuf, 0);
            return true;
        }

        public void Dispose() { h.Dispose(); }
    }

    // ------------------------------------------------------------- sampling

    sealed class Sampler
    {
        readonly object gate = new object();
        string latest = "{\"ok\":false,\"reason\":\"starting\"}";
        PawnIo dev;
        string vendor;
        string driver;
        uint energyMsr;
        List<Native.GROUP_AFFINITY> packages;
        List<PackageCounter> counters;
        DateTime retryAt = DateTime.MinValue;
        public string LastProblem;

        public string Latest()
        {
            lock (gate) return latest;
        }

        void Set(string json)
        {
            lock (gate) latest = json;
        }

        void Fail(string reason, string why)
        {
            if (dev != null) { dev.Dispose(); dev = null; }
            Set("{\"ok\":false,\"reason\":\"" + reason + "\"}");
            LastProblem = why;
            // the driver may come later (Setup asked for a restart, it was
            // installed afterwards): try again in a minute
            retryAt = DateTime.UtcNow.AddMinutes(1);
        }

        static byte[] Embedded(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static long Now100ns()
        {
            long ts = Stopwatch.GetTimestamp();
            return (long)(ts / (double)Stopwatch.Frequency * 1e7);
        }

        bool Start()
        {
            CpuInfo cpu = CpuInfo.Read();
            string module = cpu.Module();
            if (module == null) { Fail("cpu", "this CPU (" + cpu.Vendor + " family " + cpu.Family + " model " + cpu.Model + ") has no supported package energy counter"); return false; }
            byte[] blob = Embedded(module);
            if (blob == null) { Fail("load-failed", module + " is not in this build"); return false; }
            int err;
            dev = PawnIo.Open(out err);
            if (dev == null) { Fail("no-driver", "PawnIO is not installed or not loaded (error " + err + ")"); return false; }
            uint v = dev.Version();
            driver = PawnIo.VersionText(v);
            if (v < PawnIo.MinVersion) { Fail("old-driver", "PawnIO " + driver + " is older than 2.0.0; update it from https://pawnio.eu"); return false; }
            if (!dev.Load(blob)) { Fail("load-failed", "PawnIO did not load " + module + " (error " + Marshal.GetLastWin32Error() + ")"); return false; }
            vendor = cpu.Vendor;
            uint unitMsr = vendor == "amd" ? Rapl.AmdPowerUnit : Rapl.IntelPowerUnit;
            energyMsr = vendor == "amd" ? Rapl.AmdPackageEnergy : Rapl.IntelPackageEnergy;
            packages = Packages.List();
            if (packages.Count == 0) { Fail("cpu", "Windows lists no processor packages"); return false; }
            counters = new List<PackageCounter>();
            foreach (Native.GROUP_AFFINITY p in packages)
            {
                ulong unit;
                if (!Pin(p) || !dev.ReadMsr(unitMsr, out unit)) { Fail("msr-failed", "the power unit MSR could not be read (a virtual machine?)"); return false; }
                counters.Add(new PackageCounter(Rapl.JoulesPerCount(vendor, cpu.Model, unit)));
            }
            LastProblem = null;
            return true;
        }

        static bool Pin(Native.GROUP_AFFINITY a)
        {
            Native.GROUP_AFFINITY prev;
            return Native.SetThreadGroupAffinity(Native.GetCurrentThread(), ref a, out prev);
        }

        /** One reading of every package; false when the device failed. */
        public bool Poll()
        {
            if (dev == null)
            {
                if (DateTime.UtcNow < retryAt) return false;
                if (!Start()) return false;
            }
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"source\":\"pawnio-rapl\",\"vendor\":\"").Append(vendor).Append("\",\"driver\":\"").Append(driver).Append("\",\"packages\":[");
            for (int i = 0; i < packages.Count; i++)
            {
                ulong raw;
                if (!Pin(packages[i]) || !dev.ReadMsr(energyMsr, out raw)) { Fail("msr-failed", "the package energy MSR could not be read"); return false; }
                long at = Now100ns();
                counters[i].Add((uint)(raw & 0xFFFFFFFF), at);
                if (i > 0) sb.Append(',');
                sb.Append("{\"socket\":").Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"energyUj\":\"").Append(counters[i].EnergyMicrojoules.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"t100ns\":\"").Append(at.ToString(CultureInfo.InvariantCulture)).Append("\"}");
            }
            sb.Append("]}");
            Set(sb.ToString());
            return true;
        }

        /** Every second until stop is set; MSRs are read on this thread only. */
        public void Run(WaitHandle stop)
        {
            Thread.BeginThreadAffinity();
            try
            {
                do
                {
                    try { Poll(); }
                    catch (Exception e) { Fail("msr-failed", e.GetType().Name); }
                } while (!stop.WaitOne(1000));
            }
            finally
            {
                if (dev != null) dev.Dispose();
                Thread.EndThreadAffinity();
            }
        }
    }

    // ----------------------------------------------------------------- pipe

    sealed class PipeServer
    {
        public const string Name = "SentrySixPower";
        // SYSTEM and Administrators full; local users and services (the web
        // app's own NT SERVICE account has the SERVICE group) may read only
        // (FILE_GENERIC_READ | FILE_WRITE_ATTRIBUTES, what a reading client
        // opens with); network logons never. Nobody but this service can
        // write into the pipe, so no client input exists.
        const string Sddl = "D:P(D;;GA;;;NU)(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x120189;;;IU)(A;;0x120189;;;SU)(A;;0x120189;;;AU)";

        readonly Sampler sampler;
        readonly WaitHandle stop;

        public PipeServer(Sampler sampler, WaitHandle stop)
        {
            this.sampler = sampler;
            this.stop = stop;
        }

        static NamedPipeServerStream Create()
        {
            IntPtr sd;
            if (!Native.ConvertStringSecurityDescriptorToSecurityDescriptorW(Sddl, 1, out sd, IntPtr.Zero)) throw new IOException("security descriptor");
            try
            {
                var sa = new Native.SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(Native.SECURITY_ATTRIBUTES)), lpSecurityDescriptor = sd, bInheritHandle = 0 };
                // one instance, and only if nobody holds the name already
                SafePipeHandle h = Native.CreateNamedPipeW(@"\\.\pipe\" + Name,
                    Native.PIPE_ACCESS_DUPLEX | Native.FILE_FLAG_OVERLAPPED | Native.FILE_FLAG_FIRST_PIPE_INSTANCE,
                    Native.PIPE_REJECT_REMOTE_CLIENTS, 1, 4096, 16, 0, ref sa);
                if (h.IsInvalid) throw new IOException("CreateNamedPipe failed (" + Marshal.GetLastWin32Error() + ")");
                return new NamedPipeServerStream(PipeDirection.InOut, true, false, h);
            }
            finally { Native.LocalFree(sd); }
        }

        public void Run()
        {
            while (!stop.WaitOne(0))
            {
                NamedPipeServerStream pipe;
                try { pipe = Create(); }
                catch (Exception)
                {
                    // the name is taken (another copy still closing): try again soon
                    if (stop.WaitOne(5000)) return;
                    continue;
                }
                using (pipe)
                {
                    while (!stop.WaitOne(0))
                    {
                        try
                        {
                            IAsyncResult c = pipe.BeginWaitForConnection(null, null);
                            if (WaitHandle.WaitAny(new[] { c.AsyncWaitHandle, stop }) != 0) return;
                            pipe.EndWaitForConnection(c);
                            byte[] line = new UTF8Encoding(false).GetBytes(sampler.Latest() + "\n");
                            IAsyncResult w = pipe.BeginWrite(line, 0, line.Length, null, null);
                            if (w.AsyncWaitHandle.WaitOne(2000))
                            {
                                pipe.EndWrite(w);
                                // the client hangs up once it has the line (the read
                                // then ends); a client that does not is cut off
                                var one = new byte[1];
                                IAsyncResult r = pipe.BeginRead(one, 0, 1, null, null);
                                if (r.AsyncWaitHandle.WaitOne(2000)) { try { pipe.EndRead(r); } catch (IOException) { } }
                            }
                        }
                        catch (IOException) { }
                        catch (InvalidOperationException) { break; }
                        try { pipe.Disconnect(); }
                        catch (Exception) { break; }
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------- service

    public sealed class PowerService : ServiceBase
    {
        public const string Name = "SentrySixPower";
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        Thread sampling, serving;

        public PowerService()
        {
            ServiceName = Name;
            CanStop = true;
            CanShutdown = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            var sampler = new Sampler();
            sampling = new Thread(() => sampler.Run(stop)) { IsBackground = true, Name = "rapl" };
            serving = new Thread(() => new PipeServer(sampler, stop).Run()) { IsBackground = true, Name = "pipe" };
            sampling.Start();
            serving.Start();
        }

        protected override void OnStop() { Halt(); }

        protected override void OnShutdown() { Halt(); }

        void Halt()
        {
            stop.Set();
            if (sampling != null) sampling.Join(5000);
            if (serving != null) serving.Join(5000);
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                if (Environment.UserInteractive)
                {
                    Console.Error.WriteLine("SentryPower.exe is the Sentry Six Manager CPU power service (started by Windows).");
                    Console.Error.WriteLine("  SentryPower.exe --once   as administrator: read the CPU package energy twice, a second apart");
                    return 1;
                }
                ServiceBase.Run(new PowerService());
                return 0;
            }
            if (args[0] != "--once")
            {
                Console.Error.WriteLine("SentryPower: unknown argument " + args[0]);
                return 1;
            }
            Thread.BeginThreadAffinity();
            var s = new Sampler();
            if (!s.Poll()) { Console.WriteLine(s.Latest()); Console.Error.WriteLine("SentryPower: " + s.LastProblem); return 2; }
            string first = s.Latest();
            Thread.Sleep(1000);
            if (!s.Poll()) { Console.WriteLine(s.Latest()); Console.Error.WriteLine("SentryPower: " + s.LastProblem); return 2; }
            Console.WriteLine(first);
            Console.WriteLine(s.Latest());
            return 0;
        }
    }
}
