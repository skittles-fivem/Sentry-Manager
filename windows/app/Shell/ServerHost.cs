using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Shell
{
    /// <summary>
    /// App mode: the server runs as a child of this app when the Windows
    /// service is not running. Started hidden as
    /// "&lt;installdir&gt;\bin\SentryService.exe run --data %ProgramData%\SentrySixManager",
    /// in a kill-on-close Job Object (an app crash never leaves it orphaned);
    /// stopped gracefully with "stop" on its stdin (killed only after 60 s).
    /// </summary>
    public sealed class ServerHost : IDisposable
    {
        public const string ServiceName = "SentrySixManager";
        Process child;
        IntPtr job = IntPtr.Zero;

        /// <summary>The install folder: the folder above the app's own (app-ui\SentrySix.exe).</summary>
        public static string InstallDir
        {
            get
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                DirectoryInfo up = Directory.GetParent(exeDir);
                return up != null ? up.FullName : exeDir;
            }
        }

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SentrySixManager"); }
        }

        /// <summary>The SentrySixManager service exists (service mode installed).</summary>
        public static bool ServiceInstalled()
        {
            try
            {
                foreach (ServiceController s in ServiceController.GetServices())
                    using (s) if (string.Equals(s.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }
            return false;
        }

        public static bool ServiceRunning()
        {
            try
            {
                using (ServiceController s = new ServiceController(ServiceName)) return s.Status == ServiceControllerStatus.Running;
            }
            catch (Exception)
            {
                return false; // not installed, or not allowed to ask
            }
        }

        /// <summary>The server this app started is running.</summary>
        public bool Running
        {
            get { try { return child != null && !child.HasExited; } catch (Exception) { return false; } }
        }

        public event Action Exited;

        /// <summary>Starts the server as a hidden child. Throws when SentryService.exe is missing or does not start.</summary>
        public void Start()
        {
            if (Running) return;
            string exe = Path.Combine(Path.Combine(InstallDir, "bin"), "SentryService.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("The server program is missing: " + exe, exe);
            ProcessStartInfo psi = new ProcessStartInfo(exe, "run --data \"" + DataDir + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardInput = true;
            psi.WorkingDirectory = InstallDir;
            Process p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            p.Exited += delegate { Action h = Exited; if (h != null) h(); };
            p.Start();
            child = p;
            AddToJob(p);
        }

        void AddToJob(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    if (job == IntPtr.Zero) return;
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(info, ptr, false);
                        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    finally { Marshal.FreeHGlobal(ptr); }
                }
                if (!AssignProcessToJobObject(job, p.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Job object: " + ex.Message);
            }
        }

        /// <summary>
        /// Waits for https://127.0.0.1:port/healthz to answer (any HTTP answer:
        /// the server is up), up to the limit. False when it did not, or when the
        /// child exited first.
        /// </summary>
        public async Task<bool> WaitHealthy(int port, TimeSpan limit, CancellationToken ct)
        {
            DateTime until = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                if (child != null && !Running) return false;
                if (await Healthy(port).ConfigureAwait(false)) return true;
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (TaskCanceledException) { return false; }
            }
            return false;
        }

        /// <summary>One look at /healthz on this PC. Its certificate is not checked here: only "is something answering"; the API client pins it.</summary>
        public static async Task<bool> Healthy(int port)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create("https://127.0.0.1:" + port + "/healthz");
                r.Timeout = 3000;
                r.ServerCertificateValidationCallback = delegate { return true; };
                using (WebResponse resp = await r.GetResponseAsync().ConfigureAwait(false)) return true;
            }
            catch (WebException e)
            {
                // an HTTP error is still the server answering
                return e.Response != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Stops the child gracefully: "stop" on its stdin, then up to 60 s; killed only when it does not stop.</summary>
        public Task Stop()
        {
            Process p = child;
            if (p == null) return Task.FromResult(0);
            return Task.Run(() =>
            {
                try
                {
                    if (p.HasExited) return;
                    try
                    {
                        p.StandardInput.Write("stop\n");
                        p.StandardInput.Flush();
                        p.StandardInput.Close();
                    }
                    catch (Exception) { }
                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        p.WaitForExit(5000);
                    }
                }
                catch (Exception) { }
                finally
                {
                    if (child == p) child = null;
                }
            });
        }

        public void Dispose()
        {
            // closing the job's last handle ends whatever is still in it
            if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
        }

        // ---------- Job Object P/Invoke ----------
        const int JobObjectExtendedLimitInformation = 9;
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
    }
}
