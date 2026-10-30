// SentryService.exe: the Windows service host for Sentry Six Manager.
//
// On Docker, tini and server/supervisor.js start the app, restart it when it
// crashes, stop it gracefully and make sure nothing it started outlives it.
// This program does the same on Windows:
//
//   SentryService.exe                         run by the Service Control Manager
//                                             (service name SentrySixManager)
//   SentryService.exe run [--data <dir>] [--env <file>]
//                                             the same in the foreground, no admin
//                                             needed; Ctrl+C or closing stdin stops
//
// It finds node\node.exe, app\server\index.js, ffmpeg\ and bin\SentryHelper.exe
// relative to its own folder (<install>\bin), reads manager.env (the keys of
// Docker's .env), and runs node in a kill-on-close Job Object, so node and every
// ffmpeg it starts die with the job (tini's job on Docker). Node's stdout and
// stderr go to <data>\data\logs\service.log (the `docker logs` analogue).
//
// C# 7.3 and .NET Framework 4.8 only (no NuGet), so it compiles with the
// csc.exe that ships with Windows and with Mono's mcs on Linux:
//   csc /target:exe /r:System.ServiceProcess.dll SentryService.cs
//   mcs -langversion:7.3 -r:System.ServiceProcess.dll SentryService.cs
//
// Exit codes: 0 stopped cleanly; 1 bad arguments or install layout;
// 2 (run mode) node crashed 5 times within 60 s and was given up on.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SentrySixManager
{
    // manager.env with the rules of Docker's .env (env_file), so a file that
    // works there works here:
    //   KEY=VALUE                one per line; spaces around the key and before
    //                            the value are ignored
    //   # comment, blank lines   ignored; so is " # comment" after an unquoted value
    //   KEY='value'              single quotes: taken literally, # included
    //   KEY="value"              double quotes: \n \r \t \\ \" \$ are escapes;
    //                            any other backslash is kept (C:\footage stays as is)
    //   export KEY=VALUE         the export word is accepted and dropped, as Compose does
    //   KEY=                     an empty value
    // No ${VAR} expansion (a $ is just a $), no multi-line values. A later line
    // for the same key wins. Lines that break the rules are skipped with a
    // warning that names the line number only: values may be passwords.
    // Kept free of any service code so test/sentryService.test.js can check it
    // with mono on Linux.
    public static class ManagerEnv
    {
        public static List<KeyValuePair<string, string>> Parse(string text, List<string> warnings)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (text == null) return result;
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1); // BOM from Notepad
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1); // CRLF files
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;
                if (trimmed.StartsWith("export ") || trimmed.StartsWith("export\t"))
                    trimmed = trimmed.Substring(7).TrimStart();

                int eq = trimmed.IndexOf('=');
                if (eq < 0)
                {
                    Warn(warnings, i, "no '=' (a key on its own is not supported)");
                    continue;
                }
                string key = trimmed.Substring(0, eq).Trim();
                if (!ValidKey(key))
                {
                    Warn(warnings, i, "the name before '=' is not a valid variable name");
                    continue;
                }
                string rest = trimmed.Substring(eq + 1).TrimStart(' ', '\t');
                string value;
                string error;
                if (!ParseValue(rest, out value, out error))
                {
                    Warn(warnings, i, error);
                    continue;
                }
                result.Add(new KeyValuePair<string, string>(key, value));
            }
            return result;
        }

        // Letters, digits, '_', '.' and '-', not starting with a digit, '.' or '-'
        // (what Compose accepts as an env_file key).
        public static bool ValidKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            char first = key[0];
            if (!(first == '_' || (first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z'))) return false;
            foreach (char c in key)
            {
                bool ok = c == '_' || c == '.' || c == '-' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (!ok) return false;
            }
            return true;
        }

        static bool ParseValue(string rest, out string value, out string error)
        {
            value = "";
            error = null;
            if (rest.Length == 0) return true;

            char q = rest[0];
            if (q == '\'' || q == '"')
            {
                var sb = new StringBuilder();
                int j = 1;
                bool closed = false;
                for (; j < rest.Length; j++)
                {
                    char c = rest[j];
                    if (c == q) { closed = true; j++; break; }
                    if (q == '"' && c == '\\' && j + 1 < rest.Length)
                    {
                        char n = rest[j + 1];
                        switch (n)
                        {
                            case 'n': sb.Append('\n'); j++; continue;
                            case 'r': sb.Append('\r'); j++; continue;
                            case 't': sb.Append('\t'); j++; continue;
                            case '\\': sb.Append('\\'); j++; continue;
                            case '"': sb.Append('"'); j++; continue;
                            case '$': sb.Append('$'); j++; continue;
                        }
                    }
                    sb.Append(c);
                }
                if (!closed)
                {
                    error = "the quote is not closed on the same line";
                    return false;
                }
                // after the closing quote only spaces and a # comment may follow
                string after = rest.Substring(j).Trim();
                if (after.Length > 0 && after[0] != '#')
                {
                    error = "text after the closing quote";
                    return false;
                }
                value = sb.ToString();
                return true;
            }

            // unquoted: a # starts a comment only after a space or tab, so
            // abc#def keeps its #
            int cut = rest.Length;
            for (int k = 1; k < rest.Length; k++)
            {
                if (rest[k] == '#' && (rest[k - 1] == ' ' || rest[k - 1] == '\t')) { cut = k; break; }
            }
            value = rest.Substring(0, cut).TrimEnd(' ', '\t');
            return true;
        }

        static void Warn(List<string> warnings, int index, string why)
        {
            if (warnings != null) warnings.Add("manager.env line " + (index + 1) + " ignored: " + why);
        }
    }

    // Where everything is: the install folder (from this exe's own location)
    // and the data folder (%ProgramData%\SentrySixManager or --data).
    public sealed class Layout
    {
        public string BinDir, InstallRoot, Node, AppRoot, IndexJs, Ffmpeg, Ffprobe, Helper;
        public string DataRoot, EnvFile, DataPath, RunDir, FootageRoot, LogDir, ServiceLog;

        public static Layout Resolve(string binDir, string dataRoot, string envFile)
        {
            var l = new Layout();
            l.BinDir = Path.GetFullPath(binDir);
            l.InstallRoot = Path.GetFullPath(Path.Combine(l.BinDir, ".."));
            l.Node = Path.Combine(l.InstallRoot, "node", "node.exe");
            l.AppRoot = Path.Combine(l.InstallRoot, "app");
            l.IndexJs = Path.Combine(l.AppRoot, "server", "index.js");
            l.Ffmpeg = Path.Combine(l.InstallRoot, "ffmpeg", "ffmpeg.exe");
            l.Ffprobe = Path.Combine(l.InstallRoot, "ffmpeg", "ffprobe.exe");
            l.Helper = Path.Combine(l.BinDir, "SentryHelper.exe");
            l.DataRoot = Path.GetFullPath(dataRoot ?? DefaultDataRoot());
            l.EnvFile = envFile != null ? Path.GetFullPath(envFile) : Path.Combine(l.DataRoot, "manager.env");
            l.DataPath = Path.Combine(l.DataRoot, "data");
            l.RunDir = Path.Combine(l.DataRoot, "run");
            l.FootageRoot = Path.Combine(l.DataRoot, "footage");
            l.LogDir = Path.Combine(l.DataPath, "logs");
            l.ServiceLog = Path.Combine(l.LogDir, "service.log");
            return l;
        }

        public static string DefaultDataRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SentrySixManager");
        }

        // null when the install is complete, else what is missing
        public string Missing()
        {
            if (!File.Exists(Node)) return Node;
            if (!File.Exists(IndexJs)) return IndexJs;
            return null;
        }
    }

    // The environment node gets: this process's own (PATH, SystemRoot, ...),
    // then defaults that manager.env may change (as the Dockerfile's ENV lines
    // may be changed by Docker's .env), then manager.env, then the values tied
    // to this install and data folder, which nothing overrides.
    public static class NodeEnvironment
    {
        public static Dictionary<string, string> Build(IDictionary<string, string> inherited, Layout l,
            List<KeyValuePair<string, string>> managerEnv, bool ffmpegBundled)
        {
            // Windows variable names are case-insensitive; Set drops the old
            // spelling (data_path) so node sees the one written here
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in inherited) Set(env, kv.Key, kv.Value);

            Set(env, "NODE_ENV", "production");
            Set(env, "FOOTAGE_ROOT", l.FootageRoot);
            Set(env, "UV_THREADPOOL_SIZE", "32");
            if (ffmpegBundled)
            {
                Set(env, "FFMPEG_PATH", l.Ffmpeg);
                Set(env, "FFPROBE_PATH", l.Ffprobe);
            }

            foreach (var kv in managerEnv) Set(env, kv.Key, kv.Value);

            Set(env, "APP_ROOT", l.AppRoot);
            Set(env, "DATA_PATH", l.DataPath);
            Set(env, "RUN_DIR", l.RunDir);
            Set(env, "SENTRY_HELPER", l.Helper);
            Set(env, "SENTRY_STOP_STDIN", "1");
            // only the Linux root supervisor may say this; node has no IPC here
            env.Remove("SENTRY_SUPERVISED");
            return env;
        }

        static void Set(Dictionary<string, string> env, string key, string value)
        {
            env.Remove(key);
            env[key] = value;
        }
    }

    // Appends to service.log; at 10 MB the file becomes service.log.1 (the
    // one older copy, replacing the previous one) and a new file starts.
    // Others may read the file while it is written.
    public sealed class LogFile : IDisposable
    {
        readonly string path;
        readonly long limit;
        readonly object sync = new object();
        FileStream stream;

        public LogFile(string path, long limit)
        {
            this.path = path;
            this.limit = limit;
        }

        public void Write(byte[] data, int offset, int count)
        {
            lock (sync)
            {
                try
                {
                    if (stream == null) Open();
                    if (stream.Length > 0 && stream.Length + count > limit) Rotate();
                    stream.Write(data, offset, count);
                    stream.Flush();
                }
                catch (IOException) { CloseStream(); } // disk full or file locked: drop this piece, try again next time
                catch (UnauthorizedAccessException) { CloseStream(); }
            }
        }

        public void Line(string text)
        {
            byte[] b = new UTF8Encoding(false).GetBytes(text + "\n");
            Write(b, 0, b.Length);
        }

        void Open()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }

        void Rotate()
        {
            CloseStream();
            string old = path + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
            Open();
        }

        void CloseStream()
        {
            if (stream == null) return;
            try { stream.Dispose(); } catch (IOException) { }
            stream = null;
        }

        public void Dispose()
        {
            lock (sync) CloseStream();
        }
    }

    // `run [--data <dir>] [--env <file>]`; anything else is a usage error.
    public sealed class RunOptions
    {
        public string Data, Env;

        public static RunOptions Parse(string[] args, int start, out string error)
        {
            error = null;
            var o = new RunOptions();
            for (int i = start; i < args.Length; i++)
            {
                string a = args[i];
                if ((a == "--data" || a == "--env") && i + 1 < args.Length && args[i + 1].Length > 0)
                {
                    if (a == "--data") o.Data = args[++i]; else o.Env = args[++i];
                    continue;
                }
                error = a == "--data" || a == "--env" ? a + " needs a value" : "unknown argument: " + a;
                return null;
            }
            return o;
        }
    }

    static class Native
    {
        public const uint CREATE_SUSPENDED = 0x00000004;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint CREATE_NO_WINDOW = 0x08000000;
        public const int STARTF_USESTDHANDLES = 0x00000100;
        public const uint HANDLE_FLAG_INHERIT = 0x00000001;
        public const int JobObjectExtendedLimitInformation = 9;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        public const uint STILL_ACTIVE = 259;

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SECURITY_ATTRIBUTES attributes, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string currentDirectory,
            ref STARTUPINFO startup, out PROCESS_INFORMATION info);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    }

    // One run of node.exe, in its own kill-on-close Job Object. The job is
    // made first and node is created suspended, put in the job and only then
    // resumed, so every process node starts (ffmpeg, the helper) is in the job
    // from its first instruction. Disposing closes the job, which ends anything
    // still in it.
    sealed class NodeRun : IDisposable
    {
        IntPtr job, process;
        FileStream stdin;
        Thread reader;
        ProcessExit exited;
        public int Pid;

        // a WaitHandle over node's process handle (signalled when it exits);
        // it does not own the handle, NodeRun closes it
        sealed class ProcessExit : WaitHandle
        {
            public ProcessExit(IntPtr handle) { SafeWaitHandle = new SafeWaitHandle(handle, false); }
        }

        public static NodeRun Start(Layout l, Dictionary<string, string> env, Action<byte[], int> output)
        {
            var run = new NodeRun();
            try
            {
                run.StartCore(l, env, output);
                return run;
            }
            catch
            {
                run.Dispose();
                throw;
            }
        }

        void StartCore(Layout l, Dictionary<string, string> env, Action<byte[], int> output)
        {
            job = Native.CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject");
            var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject");

            // node's stdin, and one pipe for both stdout and stderr (as docker
            // logs shows them, in order). Only the child's ends are inheritable.
            var sa = new Native.SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(Native.SECURITY_ATTRIBUTES)), bInheritHandle = 1 };
            SafeFileHandle inRead, inWrite, outRead, outWrite;
            if (!Native.CreatePipe(out inRead, out inWrite, ref sa, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe");
            if (!Native.CreatePipe(out outRead, out outWrite, ref sa, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe");
            Native.SetHandleInformation(inWrite, Native.HANDLE_FLAG_INHERIT, 0);
            Native.SetHandleInformation(outRead, Native.HANDLE_FLAG_INHERIT, 0);

            var si = new Native.STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(Native.STARTUPINFO));
            si.dwFlags = Native.STARTF_USESTDHANDLES;
            si.hStdInput = inRead.DangerousGetHandle();
            si.hStdOutput = outWrite.DangerousGetHandle();
            si.hStdError = outWrite.DangerousGetHandle();

            var cmd = new StringBuilder();
            cmd.Append(Quote(l.Node)).Append(" --disable-warning=ExperimentalWarning ").Append(Quote(l.IndexJs));

            IntPtr block = EnvironmentBlock(env);
            Native.PROCESS_INFORMATION pi;
            bool ok;
            try
            {
                // no console: Ctrl+C in run mode reaches only this host (which
                // then asks node to stop), never node or ffmpeg directly
                ok = Native.CreateProcess(l.Node, cmd, IntPtr.Zero, IntPtr.Zero, true,
                    Native.CREATE_SUSPENDED | Native.CREATE_UNICODE_ENVIRONMENT | Native.CREATE_NO_WINDOW,
                    block, l.InstallRoot, ref si, out pi);
            }
            finally
            {
                Marshal.FreeHGlobal(block);
            }
            int err = Marshal.GetLastWin32Error();
            // the child has its own copies now; ours must close or EOF never comes
            inRead.Dispose();
            outWrite.Dispose();
            if (!ok)
            {
                inWrite.Dispose();
                outRead.Dispose();
                throw new Win32Exception(err, "CreateProcess " + l.Node);
            }
            process = pi.hProcess;
            Pid = pi.dwProcessId;
            if (!Native.AssignProcessToJobObject(job, process))
            {
                err = Marshal.GetLastWin32Error();
                Native.TerminateJobObject(job, 1);
                Native.TerminateProcess(process, 1);
                Native.CloseHandle(pi.hThread);
                inWrite.Dispose();
                outRead.Dispose();
                throw new Win32Exception(err, "AssignProcessToJobObject");
            }
            Native.ResumeThread(pi.hThread);
            Native.CloseHandle(pi.hThread);

            stdin = new FileStream(inWrite, FileAccess.Write, 1);
            exited = new ProcessExit(process);

            var outStream = new FileStream(outRead, FileAccess.Read, 1);
            reader = new Thread(() =>
            {
                var buf = new byte[65536];
                try
                {
                    int n;
                    while ((n = outStream.Read(buf, 0, buf.Length)) > 0) output(buf, n);
                }
                catch (IOException) { } // broken pipe: everyone holding it has exited
                finally { outStream.Dispose(); }
            });
            reader.IsBackground = true;
            reader.Name = "node output";
            reader.Start();
        }

        public WaitHandle Exited { get { return exited; } }

        public int ExitCode
        {
            get
            {
                uint code;
                return Native.GetExitCodeProcess(process, out code) ? unchecked((int)code) : -1;
            }
        }

        // what SIGTERM is on Docker: index.js reads `stop` on stdin
        // (SENTRY_STOP_STDIN=1) and shuts down cleanly
        public void RequestStop()
        {
            try
            {
                byte[] b = Encoding.ASCII.GetBytes("stop\n");
                stdin.Write(b, 0, b.Length);
                stdin.Flush();
            }
            catch (IOException) { } // node is already gone
            catch (ObjectDisposedException) { }
        }

        // the end of the grace period: everything in the job, at once
        public void Kill()
        {
            if (job != IntPtr.Zero) Native.TerminateJobObject(job, 1);
        }

        public void Dispose()
        {
            if (stdin != null) { try { stdin.Dispose(); } catch (IOException) { } stdin = null; }
            if (job != IntPtr.Zero) { Native.CloseHandle(job); job = IntPtr.Zero; } // kill on close: leftovers end here
            if (reader != null) { reader.Join(5000); reader = null; }
            if (exited != null) { exited.Dispose(); exited = null; }
            if (process != IntPtr.Zero) { Native.CloseHandle(process); process = IntPtr.Zero; }
        }

        // CreateProcess quoting for a path: paths cannot contain quotes, so
        // wrapping in quotes is enough (a trailing backslash would need
        // doubling, but these are file paths)
        static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        // KEY=VALUE\0 ... \0\0 in UTF-16, sorted as Windows expects
        static IntPtr EnvironmentBlock(Dictionary<string, string> env)
        {
            var keys = new List<string>(env.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            foreach (string k in keys)
            {
                if (k.Length == 0 || k.IndexOf('=') > 0 || k.IndexOf('\0') >= 0) continue;
                string v = env[k] ?? "";
                if (v.IndexOf('\0') >= 0) continue;
                sb.Append(k).Append('=').Append(v).Append('\0');
            }
            sb.Append('\0');
            return Marshal.StringToHGlobalUni(sb.ToString());
        }
    }

    // The supervisor loop shared by the service and run mode: start node,
    // restart it 5 s after it exits on its own, give up after 5 such exits
    // within 60 s, and on a stop ask node to stop and give it 60 s (Docker's
    // stop_grace_period) before closing the job.
    sealed class Host
    {
        public const int GraceSeconds = 60;
        const int RestartDelayMs = 5000;
        const int CrashLimit = 5;
        static readonly TimeSpan CrashWindow = TimeSpan.FromSeconds(60);

        readonly Layout layout;
        readonly LogFile log;
        readonly bool echo;
        readonly ManualResetEvent stopRequested = new ManualResetEvent(false);
        readonly ManualResetEvent killNow = new ManualResetEvent(false);
        readonly List<DateTime> crashes = new List<DateTime>();
        Stream console;

        public Host(Layout layout, LogFile log, bool echo)
        {
            this.layout = layout;
            this.log = log;
            this.echo = echo;
        }

        public void RequestStop() { stopRequested.Set(); }
        public void KillNow() { stopRequested.Set(); killNow.Set(); }

        public void Say(string message)
        {
            log.Line("[service] " + message);
            if (echo) Console.Error.WriteLine("[service] " + message);
        }

        void Output(byte[] data, int count)
        {
            log.Write(data, 0, count);
            if (echo)
            {
                try
                {
                    if (console == null) console = Console.OpenStandardOutput();
                    console.Write(data, 0, count);
                    console.Flush();
                }
                catch (IOException) { }
            }
        }

        // Returns 0 after a requested stop, 2 after giving up on crashes.
        public int Run()
        {
            while (!stopRequested.WaitOne(0))
            {
                Dictionary<string, string> env = BuildEnv();
                NodeRun node;
                try
                {
                    node = NodeRun.Start(layout, env, Output);
                }
                catch (Exception e)
                {
                    Say("could not start node: " + e.Message);
                    if (CountCrash()) return 2;
                    if (stopRequested.WaitOne(RestartDelayMs)) break;
                    continue;
                }
                using (node)
                {
                    Say("started the app (node pid " + node.Pid + ")");
                    int which = WaitHandle.WaitAny(new WaitHandle[] { node.Exited, stopRequested });
                    if (which == 1)
                    {
                        StopGracefully(node);
                        Say("stopped");
                        return 0;
                    }
                    Say("server exited (" + node.ExitCode + "); restarting in 5 s");
                }
                if (CountCrash()) return 2;
                if (stopRequested.WaitOne(RestartDelayMs)) break;
            }
            Say("stopped");
            return 0;
        }

        void StopGracefully(NodeRun node)
        {
            Say("stopping the app (up to " + GraceSeconds + " s)");
            node.RequestStop();
            int which = WaitHandle.WaitAny(new WaitHandle[] { node.Exited, killNow }, TimeSpan.FromSeconds(GraceSeconds));
            if (which != 0)
            {
                Say(which == 1 ? "stopping now, as asked" : "the app did not stop within " + GraceSeconds + " s; ending it");
                node.Kill();
                node.Exited.WaitOne(5000);
            }
        }

        // true when it is time to give up
        bool CountCrash()
        {
            DateTime now = DateTime.UtcNow;
            crashes.Add(now);
            crashes.RemoveAll(t => now - t > CrashWindow);
            if (crashes.Count < CrashLimit) return false;
            Say("the app exited " + CrashLimit + " times within " + (int)CrashWindow.TotalSeconds + " s; giving up (see the lines above)");
            return true;
        }

        Dictionary<string, string> BuildEnv()
        {
            // read on every start, so a changed manager.env applies after a
            // crash restart just as `docker compose up -d` would apply it
            var parsed = new List<KeyValuePair<string, string>>();
            if (File.Exists(layout.EnvFile))
            {
                var warnings = new List<string>();
                try
                {
                    parsed = ManagerEnv.Parse(File.ReadAllText(layout.EnvFile, Encoding.UTF8), warnings);
                }
                catch (Exception e)
                {
                    Say("could not read " + layout.EnvFile + ": " + e.Message);
                }
                foreach (string w in warnings) Say(w);
            }
            var inherited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
                inherited[(string)e.Key] = (string)e.Value;
            return NodeEnvironment.Build(inherited, layout, parsed, File.Exists(layout.Ffmpeg));
        }
    }

    public sealed class SentryService : ServiceBase
    {
        public const string Name = "SentrySixManager";
        readonly Layout layout;
        Host host;
        LogFile log;
        Thread loop;

        public SentryService(Layout layout)
        {
            this.layout = layout;
            ServiceName = Name;
            CanStop = true;
            CanShutdown = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            string missing = layout.Missing();
            if (missing != null)
            {
                ExitCode = 1;
                throw new InvalidOperationException("Sentry Six Manager is not installed completely: " + missing + " is missing. Run Install.cmd again.");
            }
            Directory.CreateDirectory(layout.LogDir);
            log = new LogFile(layout.ServiceLog, Program.LogLimit);
            host = new Host(layout, log, false);
            host.Say("service starting; data in " + layout.DataRoot);
            loop = new Thread(() =>
            {
                int code = host.Run();
                if (code != 0)
                {
                    // gave up on crashes: stop the service with an error code,
                    // so the recovery actions set by install.ps1 can take over
                    ExitCode = code;
                    try { Stop(); } catch (InvalidOperationException) { }
                }
            });
            loop.Name = "supervisor";
            loop.Start();
        }

        protected override void OnStop()
        {
            StopAndWait();
        }

        protected override void OnShutdown()
        {
            // Windows is shutting down: the same graceful stop, as far as the
            // time Windows gives allows (the job ends the rest)
            StopAndWait();
        }

        void StopAndWait()
        {
            if (host == null || loop == null) return;
            host.RequestStop();
            // Stop() called by the loop itself after giving up: it has
            // already finished its work, there is nothing to wait for
            if (Thread.CurrentThread == loop) return;
            // the loop enforces the 60 s itself; keep telling the SCM we are
            // still working so it does not give up on us first
            DateTime deadline = DateTime.UtcNow.AddSeconds(Host.GraceSeconds + 15);
            while (!loop.Join(5000))
            {
                if (DateTime.UtcNow > deadline) { host.KillNow(); loop.Join(10000); break; }
                try { RequestAdditionalTime(15000); } catch (InvalidOperationException) { }
            }
            if (log != null) log.Dispose();
        }
    }

    public static class Program
    {
        public const long LogLimit = 10L * 1024 * 1024;

        public static int Main(string[] args)
        {
            string binDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (args.Length == 0)
            {
                // double-clicked: Windows starts the service, not a person
                if (Environment.UserInteractive)
                {
                    Usage(null);
                    return 1;
                }
                ServiceBase.Run(new SentryService(Layout.Resolve(binDir, null, null)));
                return 0;
            }
            if (args[0] != "run")
            {
                Usage(args[0] == "--help" || args[0] == "-h" || args[0] == "/?" ? null : "unknown command: " + args[0]);
                return 1;
            }
            string error;
            RunOptions o = RunOptions.Parse(args, 1, out error);
            if (o == null)
            {
                Usage(error);
                return 1;
            }
            return RunForeground(Layout.Resolve(binDir, o.Data, o.Env));
        }

        static void Usage(string error)
        {
            if (error != null) Console.Error.WriteLine("SentryService: " + error);
            Console.Error.WriteLine("Usage: SentryService.exe run [--data <folder>] [--env <manager.env>]");
            Console.Error.WriteLine("  Runs Sentry Six Manager in this window (Ctrl+C stops it).");
            Console.Error.WriteLine("  Without arguments it is the Windows service (started by Windows, not by hand).");
        }

        static int RunForeground(Layout layout)
        {
            string missing = layout.Missing();
            if (missing != null)
            {
                Console.Error.WriteLine("SentryService: not a complete install, missing " + missing);
                return 1;
            }
            try
            {
                Directory.CreateDirectory(layout.LogDir);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("SentryService: cannot create " + layout.LogDir + ": " + e.Message);
                return 1;
            }
            using (var log = new LogFile(layout.ServiceLog, LogLimit))
            {
                var host = new Host(layout, log, true);
                int presses = 0;
                // Ctrl+C (or Ctrl+Break): a graceful stop; a second one ends it now
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    if (Interlocked.Increment(ref presses) == 1) host.RequestStop(); else host.KillNow();
                };
                // closing our stdin (or a `stop` line on it) is a graceful stop too
                var stdinWatch = new Thread(() =>
                {
                    try
                    {
                        string line;
                        while ((line = Console.In.ReadLine()) != null)
                            if (line.Trim() == "stop") break;
                    }
                    catch (IOException) { }
                    host.RequestStop();
                });
                stdinWatch.IsBackground = true;
                stdinWatch.Start();

                host.Say("running in the foreground; data in " + layout.DataRoot);
                return host.Run();
            }
        }
    }
}
