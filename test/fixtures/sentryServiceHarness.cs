// Test harness for windows/service/SentryService.cs (test/sentryService.test.js).
// Compiled together with it (main class SentrySixManager.Tests.Harness) so the
// pure parts can be checked with mono on Linux, or natively on Windows:
//   env <file>                         ManagerEnv.Parse -> JSON
//   options <args...>                  RunOptions.Parse -> JSON
//   layout <binDir> <data|-> <env|->   Layout.Resolve -> JSON
//   nodeenv <binDir> <data> <envfile> <bundled 0|1>
//                                      NodeEnvironment.Build over a fixed
//                                      inherited set -> JSON
//   log <file> <limit> <count> <size>  LogFile: count writes of size bytes

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SentrySixManager.Tests
{
    public static class Harness
    {
        public static int Main(string[] args)
        {
            switch (args[0])
            {
                case "env":
                {
                    var warnings = new List<string>();
                    var pairs = ManagerEnv.Parse(File.ReadAllText(args[1], Encoding.UTF8), warnings);
                    var sb = new StringBuilder("{\"pairs\":[");
                    for (int i = 0; i < pairs.Count; i++)
                        sb.Append(i > 0 ? "," : "").Append('[').Append(Json(pairs[i].Key)).Append(',').Append(Json(pairs[i].Value)).Append(']');
                    sb.Append("],\"warnings\":[");
                    for (int i = 0; i < warnings.Count; i++) sb.Append(i > 0 ? "," : "").Append(Json(warnings[i]));
                    Console.Out.Write(sb.Append("]}").ToString());
                    return 0;
                }
                case "options":
                {
                    string error;
                    var rest = new string[args.Length - 1];
                    Array.Copy(args, 1, rest, 0, rest.Length);
                    RunOptions o = RunOptions.Parse(rest, 1, out error);
                    Console.Out.Write(o == null
                        ? "{\"error\":" + Json(error) + "}"
                        : "{\"data\":" + Json(o.Data) + ",\"env\":" + Json(o.Env) + "}");
                    return 0;
                }
                case "layout":
                {
                    Layout l = Layout.Resolve(args[1], args[2] == "-" ? null : args[2], args[3] == "-" ? null : args[3]);
                    Console.Out.Write("{\"installRoot\":" + Json(l.InstallRoot) + ",\"node\":" + Json(l.Node) +
                        ",\"appRoot\":" + Json(l.AppRoot) + ",\"indexJs\":" + Json(l.IndexJs) + ",\"ffmpeg\":" + Json(l.Ffmpeg) +
                        ",\"ffprobe\":" + Json(l.Ffprobe) + ",\"helper\":" + Json(l.Helper) + ",\"dataRoot\":" + Json(l.DataRoot) +
                        ",\"envFile\":" + Json(l.EnvFile) + ",\"dataPath\":" + Json(l.DataPath) + ",\"runDir\":" + Json(l.RunDir) +
                        ",\"footageRoot\":" + Json(l.FootageRoot) + ",\"serviceLog\":" + Json(l.ServiceLog) +
                        ",\"missing\":" + Json(l.Missing()) + "}");
                    return 0;
                }
                case "nodeenv":
                {
                    Layout l = Layout.Resolve(args[1], args[2], null);
                    var parsed = ManagerEnv.Parse(File.ReadAllText(args[3], Encoding.UTF8), null);
                    var inherited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    inherited["Path"] = "C:\\Windows\\system32";
                    inherited["SystemRoot"] = "C:\\Windows";
                    inherited["SENTRY_SUPERVISED"] = "1";
                    inherited["data_path"] = "inherited";
                    var env = NodeEnvironment.Build(inherited, l, parsed, args[4] == "1");
                    var keys = new List<string>(env.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    var sb = new StringBuilder("{");
                    for (int i = 0; i < keys.Count; i++)
                        sb.Append(i > 0 ? "," : "").Append(Json(keys[i])).Append(':').Append(Json(env[keys[i]]));
                    Console.Out.Write(sb.Append('}').ToString());
                    return 0;
                }
                case "log":
                {
                    using (var log = new LogFile(args[1], long.Parse(args[2])))
                    {
                        int count = int.Parse(args[3]);
                        int size = int.Parse(args[4]);
                        for (int i = 0; i < count; i++)
                        {
                            byte[] b = Encoding.ASCII.GetBytes(new string((char)('a' + i % 26), size));
                            log.Write(b, 0, b.Length);
                        }
                    }
                    return 0;
                }
            }
            return 1;
        }

        static string Json(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
