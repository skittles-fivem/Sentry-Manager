using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>A tiny test runner: every test, its result, exit code 1 on any failure.</summary>
    public static class Program
    {
        static readonly List<(string Name, Func<Task> Run)> Tests = new List<(string, Func<Task>)>();

        static void Test(string name, Func<Task> run) => Tests.Add((name, run));
        static void Test(string name, Action run) => Tests.Add((name, () => { run(); return Task.CompletedTask; }));

        public static async Task<int> Main(string[] args)
        {
            string only = args.Length > 0 ? args[0] : null;
            JsonTests.Register(Test);
            UnitTests.Register(Test);
            ServerTests.Register(Test);
            int failed = 0, passed = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                foreach (var (name, run) in Tests)
                {
                    if (only != null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
                    var t = Stopwatch.StartNew();
                    try
                    {
                        var task = run();
                        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(90))) != task) throw new TimeoutException("test took over 90 s");
                        await task;
                        passed++;
                        Console.WriteLine($"  ok    {name} ({t.ElapsedMilliseconds} ms)");
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Console.WriteLine($"  FAIL  {name}: {e.GetType().Name}: {e.Message}");
                        Console.WriteLine(Indent(e.StackTrace));
                    }
                }
            }
            finally
            {
                ServerTests.StopAll();
            }
            Console.WriteLine($"{passed} passed, {failed} failed ({sw.Elapsed.TotalSeconds:0.0} s)");
            return failed == 0 ? 0 : 1;
        }

        static string Indent(string s) => s == null ? "" : string.Join("\n", s.Split('\n').Take(6).Select(l => "        " + l.Trim()));
    }

    public class AssertFailed : Exception
    {
        public AssertFailed(string m) : base(m) { }
    }

    public static class Assert
    {
        public static void True(bool cond, string what)
        {
            if (!cond) throw new AssertFailed(what);
        }

        public static void Equal<T>(T want, T got, string what = null)
        {
            if (!EqualityComparer<T>.Default.Equals(want, got)) throw new AssertFailed($"{what ?? "value"}: expected <{want}>, got <{got}>");
        }

        public static T Throws<T>(Action a, string what = null) where T : Exception
        {
            try
            {
                a();
            }
            catch (T e)
            {
                return e;
            }
            catch (Exception e)
            {
                throw new AssertFailed($"{what ?? "call"}: expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}");
            }
            throw new AssertFailed($"{what ?? "call"}: expected {typeof(T).Name}, nothing thrown");
        }

        public static async Task<T> ThrowsAsync<T>(Func<Task> a, string what = null) where T : Exception
        {
            try
            {
                await a();
            }
            catch (T e)
            {
                return e;
            }
            catch (Exception e)
            {
                throw new AssertFailed($"{what ?? "call"}: expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}");
            }
            throw new AssertFailed($"{what ?? "call"}: expected {typeof(T).Name}, nothing thrown");
        }

        public static async Task<ApiError> ApiFails(Func<Task> a, int status, string code, string what)
        {
            var e = await ThrowsAsync<ApiError>(a, what);
            if (e.Status != status || e.Code != code) throw new AssertFailed($"{what}: expected {status} {code ?? "(no code)"}, got {e.Status} {e.Code ?? "(no code)"}: {e.Message}");
            return e;
        }
    }

    // ================================================================== JSON

    static class JsonTests
    {
        public static void Register(Action<string, Action> test)
        {
            test("json: round trip keeps order, types and text", () =>
            {
                const string src = "{\"b\":1,\"a\":[true,false,null,\"x\",{\"z\":[]},{}],\"n\":-12.5e-3,\"big\":12345678901234567890,\"s\":\"q\\\"b\\\\s/\"}";
                var v = JsonValue.Parse(src);
                Assert.Equal(src.Replace("/\"}", "/\"}"), v.ToJson(), "round trip");
                Assert.Equal(JsonKind.Object, v.Kind);
                Assert.Equal(new[] { "b", "a", "n", "big", "s" }.Aggregate((x, y) => x + "," + y), string.Join(",", v.Props.Select(p => p.Key)), "order");
                Assert.Equal(1L, v["b"].Long);
                Assert.Equal(6, v["a"].Count);
                Assert.True(v["a"][0].Bool && !v["a"][1].Bool && v["a"][2].IsNull, "bools and null");
                Assert.Equal(-0.0125, v["n"].Num);
                Assert.Equal("q\"b\\s/", v["s"].Str);
                Assert.Equal(src, JsonValue.Parse(v.ToJson()).ToJson(), "second round trip");
            });

            test("json: whitespace everywhere", () =>
            {
                var v = JsonValue.Parse(" \r\n\t{ \"a\" : [ 1 , 2 ] , \"b\" :\n{ } }\n ");
                Assert.Equal("{\"a\":[1,2],\"b\":{}}", v.ToJson());
            });

            test("json: unicode escapes, surrogate pairs and control characters", () =>
            {
                var v = JsonValue.Parse("\"\\u00e9\\u00C9 \\ud83d\\ude00 \\u0000\\u001f\\b\\f\\n\\r\\t\\/ \\u2028 caf\u00e9 \"");
                string s = v.Str;
                Assert.Equal("\u00e9\u00c9 \U0001F600 \u0000\u001f\b\f\n\r\t/ \u2028 caf\u00e9 ", s, "decoded");
                string json = v.ToJson();
                Assert.Equal("\"\u00e9\u00c9 \U0001F600 \\u0000\\u001f\\b\\f\\n\\r\\t/ \\u2028 caf\u00e9 \"", json, "encoded");
                Assert.Equal(s, JsonValue.Parse(json).Str, "back");
                // a lone surrogate survives (JavaScript strings allow it)
                Assert.Equal("\ud800x", JsonValue.Parse("\"\\ud800x\"").Str);
                // what the server writes as raw UTF-8 text
                Assert.Equal("\u65e5\u672c \U0001F697", JsonValue.Parse("[\"\u65e5\u672c \U0001F697\"]")[0].Str);
            });

            test("json: deep nesting parses to the limit and is refused past it", () =>
            {
                int depth = 900;
                string deep = new string('[', depth) + "7" + new string(']', depth);
                var v = JsonValue.Parse(deep);
                var x = v;
                for (int i = 0; i < depth; i++) x = x[0];
                Assert.Equal(7L, x.Long, "innermost");
                Assert.Equal(deep, v.ToJson(), "writes back");
                string objs = string.Concat(Enumerable.Repeat("{\"k\":", depth)) + "1" + new string('}', depth);
                Assert.Equal(objs, JsonValue.Parse(objs).ToJson(), "objects");
                string tooDeep = new string('[', 5000) + new string(']', 5000);
                Assert.Throws<FormatException>(() => JsonValue.Parse(tooDeep), "too deep");
            });

            test("json: numbers", () =>
            {
                Assert.Equal(0.0, JsonValue.Parse("0").Num);
                Assert.Equal("-0", JsonValue.Parse("-0").ToJson());
                Assert.Equal(1000.0, JsonValue.Parse("1e3").Num);
                Assert.Equal(1000.0, JsonValue.Parse("1E+3").Num);
                Assert.Equal(1.5e-7, JsonValue.Parse("1.5E-7").Num);
                Assert.Equal(9007199254740993L, JsonValue.Parse("9007199254740993").Long, "beyond 2^53 exact");
                Assert.Equal(-42L, JsonValue.Parse("-42").Long);
                Assert.Equal(3L, JsonValue.Parse("3.99").Long, "truncated");
                Assert.Equal(long.MaxValue, JsonValue.Parse("1e300").Long, "clamped");
                Assert.True(double.IsPositiveInfinity(JsonValue.Parse("1e400").Num), "overflow is Infinity as in JS");
                Assert.Equal(12.0, JsonValue.Parse("\"12\"").Num, "numeric string");
                Assert.Equal(0.0, JsonValue.Parse("\"x\"").Num, "not a number");
                foreach (var bad in new[] { "01", "1.", ".5", "+1", "-", "1e", "1e+", "--1", "0x10", "NaN", "Infinity", "1 2" })
                    Assert.Throws<FormatException>(() => JsonValue.Parse(bad), bad);
                // building: whole numbers stay whole, others as JavaScript writes them
                // (each as node prints JSON.stringify of it)
                Assert.Equal("[3,-2,0.1,1.5e-7,1e+21,null,1000000000000000000,-0.000001,123.456,1.7976931348623157e+308,5e-324,-2.5e-7,1234567.5]",
                    JsonValue.From(new object[] { 3, -2L, 0.1, 1.5e-7, 1e21, double.NaN, 1e18, -0.000001, 123.456, double.MaxValue, double.Epsilon, -2.5e-7, 1234567.5 }).ToJson());
                Assert.Equal("18446744073709551615", JsonValue.From(ulong.MaxValue).ToJson());
                Assert.Equal("0.30000000000000004", JsonValue.From(0.1 + 0.2).ToJson());
            });

            test("json: missing keys and indexes give null, never throw", () =>
            {
                var v = JsonValue.Parse("{\"recordings\":[{\"day\":\"2026-10-30\"}],\"s\":\"x\",\"n\":5}");
                Assert.Equal("2026-10-30", v["recordings"][0]["day"].Str);
                Assert.True(v["recordings"][1]["day"].IsNull, "index past the end");
                Assert.True(v["recordings"][-1].IsNull, "negative index");
                Assert.True(v["nope"]["deeper"][3]["x"].IsNull, "missing chain");
                Assert.True(v["s"]["x"].IsNull && v["s"][0].IsNull, "string indexed");
                Assert.True(v["n"]["x"].IsNull, "number indexed");
                Assert.True(v[null].IsNull, "null key");
                Assert.True(ReferenceEquals(JsonValue.Null, v["nope"]), "shared null");
                Assert.Equal(null, v["nope"].Str);
                Assert.Equal(0, v["nope"].Items.Count);
                Assert.Equal(0, v["nope"].Props.Count());
                Assert.Equal(0.0, v["nope"].Num);
                Assert.Equal(0L, v["nope"].Long);
                Assert.True(!v["nope"].Bool, "null is falsy");
                Assert.True(v.Has("s") && !v.Has("zzz"), "Has");
                Assert.Equal("5", v["n"].Str, "number as text");
            });

            test("json: invalid input is refused with FormatException", () =>
            {
                foreach (var bad in new[] { "", " ", "{", "}", "[1,]", "{\"a\":1,}", "{a:1}", "{'a':1}", "[1 2]", "\"abc", "\"a\nb\"", "\"\\x\"", "\"\\u12\"",
                    "\"\\u12G4\"", "tru", "nul", "[1]x", "{\"a\" 1}", "{\"a\":}", "[,1]", "\u00a0[]" })
                    Assert.Throws<FormatException>(() => JsonValue.Parse(bad), "parse " + bad);
                Assert.True(JsonValue.TryParse("<html>") == null, "TryParse html");
                Assert.True(JsonValue.TryParse("") == null, "TryParse empty");
            });

            test("json: building with Object and From", () =>
            {
                var v = JsonValue.Object("username", "admin", "n", 3, "ok", true, "none", null, "list", new[] { "a", "b" },
                    "nested", JsonValue.Object("x", 1.5), "dict", new Dictionary<string, object> { ["k"] = 2 });
                Assert.Equal("{\"username\":\"admin\",\"n\":3,\"ok\":true,\"none\":null,\"list\":[\"a\",\"b\"],\"nested\":{\"x\":1.5},\"dict\":{\"k\":2}}", v.ToJson());
                Assert.Equal("{}", JsonValue.Object().ToJson());
                Assert.Equal("{\"a\":2,\"b\":1}", JsonValue.Parse("{\"a\":1,\"b\":1,\"a\":2}").ToJson(), "repeated key: last wins, first place");
                Assert.Equal("{\"a\":1,\"b\":\"x\"}", JsonValue.Object("a", 1).With("b", "x").ToJson(), "With");
                Assert.Throws<ArgumentException>(() => JsonValue.Object("odd"), "odd count");
                Assert.Throws<ArgumentException>(() => JsonValue.From(new object()), "unknown type");
                Assert.Equal("\"2026-10-30T12:00:00.000Z\"", JsonValue.From(new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)).ToJson());
                Assert.Equal("\"a\\\"b\"", JsonValue.Quote("a\"b"));
            });
        }
    }

    // ================================================================== small units

    static class UnitTests
    {
        public static void Register(Action<string, Action> test)
        {
            test("local server: PORT from manager.env", () =>
            {
                Assert.Equal(9000, LocalServer.PortFromEnvText(""));
                Assert.Equal(9443, LocalServer.PortFromEnvText("# settings\r\nTLS_MODE=auto\r\nPORT=9443\r\n"));
                Assert.Equal(8080, LocalServer.PortFromEnvText("\uFEFFPORT = \"8080\"\n"));
                Assert.Equal(7000, LocalServer.PortFromEnvText("PORT=7000 # the web port\n"));
                Assert.Equal(9000, LocalServer.PortFromEnvText("PORT=abc\nPORTS=1\n#PORT=1\n"));
                Assert.Equal(9000, LocalServer.PortFromEnvText("PORT=70000\n"));
                Assert.Equal(9001, LocalServer.PortFromEnvText("PORT=1\nexport PORT=9001\n"), "last wins");
                string dir = Temp.Dir("env");
                string file = Path.Combine(dir, "manager.env");
                Assert.Equal(9000, LocalServer.PortFromEnvFile(file), "no file");
                File.WriteAllText(file, "PORT=9123\n");
                Assert.Equal(9123, LocalServer.PortFromEnvFile(file));
            });

            test("local server: SENTRY_PORT off Windows", () =>
            {
                string before = Environment.GetEnvironmentVariable("SENTRY_PORT");
                try
                {
                    Environment.SetEnvironmentVariable("SENTRY_PORT", "12345");
                    Assert.Equal(12345, LocalServer.Port());
                    Environment.SetEnvironmentVariable("SENTRY_PORT", null);
                    Assert.Equal(9000, LocalServer.Port());
                    Assert.Equal("https://127.0.0.1:12345/", ServerAddress.Local(12345).BaseUri.ToString());
                }
                finally
                {
                    Environment.SetEnvironmentVariable("SENTRY_PORT", before);
                }
            });

            test("trust store: kept in a JSON file", () =>
            {
                string file = Path.Combine(Temp.Dir("trust"), "sub", "trust.json");
                var a = new FileTrustStore(file);
                Assert.Equal(null, a.Get("127.0.0.1:9000"));
                string sha = string.Concat(Enumerable.Repeat("ab", 32));
                a.Set("127.0.0.1:9000", sha);
                a.Set("nas:9000", "AB:" + string.Join(":", Enumerable.Repeat("CD", 31)));
                var b = new FileTrustStore(file);
                Assert.Equal(sha.ToUpperInvariant(), b.Get("127.0.0.1:9000"), "persisted, upper case");
                Assert.Equal("AB" + string.Concat(Enumerable.Repeat("CD", 31)), b.Get("NAS:9000"), "colons dropped, host case ignored");
                Assert.True(JsonValue.Parse(File.ReadAllText(file)).IsObject, "a JSON object");
                b.Set("nas:9000", null);
                Assert.Equal(null, new FileTrustStore(file).Get("nas:9000"), "removed");
                Assert.Equal(sha.ToUpperInvariant(), new FileTrustStore(file).Get("127.0.0.1:9000"), "others kept");
                File.WriteAllText(file, "not json");
                Assert.Equal(null, new FileTrustStore(file).Get("127.0.0.1:9000"), "a broken file reads as empty");
            });

            test("server address: normalized to the root, pin normalized", () =>
            {
                var s = new ServerAddress(new Uri("https://Example.lan:9000/some/page?x=1"), "ab:cd");
                Assert.Equal("https://example.lan:9000/", s.BaseUri.ToString());
                Assert.Equal(null, s.PinnedSha256, "a short pin is no pin");
                Assert.Equal("example.lan:9000", s.TrustKey);
            });
        }
    }

    // ================================================================== against the real server

    sealed class Prompt : ICertificatePrompt
    {
        public bool Answer = true;
        public int Calls;
        public string LastSha;
        public string LastSubject;

        public Task<bool> TrustAsync(string sha256Colons, string subject)
        {
            Interlocked.Increment(ref Calls);
            LastSha = sha256Colons;
            LastSubject = subject;
            return Task.FromResult(Answer);
        }
    }

    sealed class NodeServer
    {
        public int Port;
        public string Data;
        public string Password;
        Process proc;
        readonly StringBuilder output = new StringBuilder();

        public static string RepoRoot()
        {
            string env = Environment.GetEnvironmentVariable("SENTRY_REPO");
            if (!string.IsNullOrEmpty(env)) return env;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "server", "index.js"))) return d.FullName;
            throw new InvalidOperationException("server/index.js not found above " + AppContext.BaseDirectory + " (set SENTRY_REPO)");
        }

        public static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        public async Task Start()
        {
            var psi = new ProcessStartInfo("node", "server/index.js")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.Environment["DATA_PATH"] = Data;
            psi.Environment["PORT"] = Port.ToString();
            psi.Environment["HOST"] = "127.0.0.1";
            if (Password != null) psi.Environment["UI_PASSWORD"] = Password;
            else psi.Environment.Remove("UI_PASSWORD");
            proc = Process.Start(psi);
            proc.OutputDataReceived += (s, e) => { lock (output) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { lock (output) output.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            // ready when /healthz answers (a check that does not trust anything: test code only)
            using var h = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
            for (int i = 0; i < 150; i++)
            {
                if (proc.HasExited) throw new Exception("server exited: " + Output);
                try
                {
                    if ((await h.GetAsync($"https://127.0.0.1:{Port}/healthz")).IsSuccessStatusCode) return;
                }
                catch (HttpRequestException)
                {
                }
                await Task.Delay(100);
            }
            throw new TimeoutException("server did not start: " + Output);
        }

        public string Output { get { lock (output) return output.ToString(); } }

        public void Stop()
        {
            if (proc == null) return;
            try
            {
                if (!proc.HasExited) proc.Kill(true);
                proc.WaitForExit(10000);
            }
            catch (InvalidOperationException)
            {
            }
            proc = null;
        }

        public string SetupCode() => File.ReadAllText(Path.Combine(Data, "setup-code.txt")).Trim();
    }

    static class Temp
    {
        static readonly string Root = Path.Combine(Path.GetTempPath(), "sentry-core-tests-" + Environment.ProcessId);

        public static string Dir(string name)
        {
            string d = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void Clean()
        {
            try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static class ServerTests
    {
        const string Password = "correcthorse";
        static readonly List<NodeServer> Started = new List<NodeServer>();
        static NodeServer a;
        static string trustFile;
        static Api main;          // signed in to server A
        static Prompt mainPrompt;

        static async Task<NodeServer> Run(NodeServer s)
        {
            Started.Add(s);
            await s.Start();
            return s;
        }

        public static void StopAll()
        {
            main?.Dispose();
            foreach (var s in Started) s.Stop();
            Temp.Clean();
        }

        static Api NewApi(Prompt prompt, bool withStore = true) =>
            new Api(ServerAddress.Local(a.Port), prompt, withStore ? new FileTrustStore(trustFile) : null);

        static async Task Until(Func<bool> cond, string what, int ms = 15000)
        {
            var sw = Stopwatch.StartNew();
            while (!cond())
            {
                if (sw.ElapsedMilliseconds > ms) throw new AssertFailed("timed out waiting for " + what);
                await Task.Delay(50);
            }
        }

        public static void Register(Action<string, Func<Task>> test)
        {
            test("server: starts (HTTPS, self-signed)", async () =>
            {
                string dir = Temp.Dir("a");
                trustFile = Path.Combine(Temp.Dir("trust"), "trusted-servers.json");
                a = await Run(new NodeServer { Port = NodeServer.FreePort(), Data = dir, Password = Password });
            });

            test("certificate: refused when the person says no; nothing remembered", async () =>
            {
                var p = new Prompt { Answer = false };
                using var api = NewApi(p);
                var e = await Assert.ApiFails(() => api.Get("/api/info"), 0, "CERT_UNTRUSTED", "untrusted");
                Assert.Equal(1, p.Calls, "asked once");
                Assert.Equal(null, api.Server.PinnedSha256, "not pinned");
                Assert.Equal(null, new FileTrustStore(trustFile).Get(api.Server.TrustKey), "not stored");
                Assert.True(e.PresentedSha256 != null && e.PresentedSha256.Length == 95, "fingerprint given");
            });

            test("certificate: trusted once, the fingerprint the server shows, pinned and stored", async () =>
            {
                mainPrompt = new Prompt();
                main = NewApi(mainPrompt);
                var session = new Session(main);
                var info = await session.Info();
                Assert.Equal("linux", info["deployment"].Str, "/api/info");
                Assert.Equal(1, mainPrompt.Calls, "asked once");
                var me = await main.Get("/api/auth/me");
                Assert.Equal(me["fingerprint"].Str, mainPrompt.LastSha, "same fingerprint as the sign-in page");
                Assert.True(a.Output.Contains(mainPrompt.LastSha), "and as the log");
                Assert.True(mainPrompt.LastSubject.Contains("Sentry Six Manager"), "subject " + mainPrompt.LastSubject);
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => main.Get("/api/info")));
                Assert.Equal(1, mainPrompt.Calls, "not asked again");
                Assert.Equal(mainPrompt.LastSha.Replace(":", ""), new FileTrustStore(trustFile).Get(main.Server.TrustKey), "stored");
            });

            test("certificate: a new client with the trust store does not ask", async () =>
            {
                var p = new Prompt { Answer = false };
                using var api = NewApi(p);
                Assert.Equal(mainPrompt.LastSha.Replace(":", ""), api.Server.PinnedSha256, "loaded");
                await api.Get("/api/info");
                Assert.Equal(0, p.Calls, "no prompt");
            });

            test("certificate: parallel first requests ask once", async () =>
            {
                var p = new Prompt();
                using var api = NewApi(p, withStore: false);
                await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.Get("/api/info")));
                Assert.Equal(1, p.Calls, "one prompt");
            });

            test("sign-in: wrong password is 401 with the server's wording, not 'signed out'", async () =>
            {
                using var api = NewApi(new Prompt());
                int signedOut = 0;
                api.SignedOut += () => signedOut++;
                var s = new Session(api);
                var e = await Assert.ApiFails(() => s.SignIn("admin", "wrong-password"), 401, null, "wrong password");
                Assert.Equal("Wrong user name or password.", e.Message);
                Assert.Equal(0, signedOut, "SignedOut not raised for /api/auth/");
                Assert.True(!await s.IsSignedIn(), "not signed in");
                Assert.True(s.User.IsNull, "no user");
            });

            test("sign-in: right password; the session cookie works", async () =>
            {
                var s = new Session(main);
                Assert.True(!await s.IsSignedIn(), "not yet");
                Assert.True(!await s.NeedsSetup(), "account exists");
                await s.SignIn("admin", Password);
                Assert.Equal("admin", s.User.Str, "user");
                Assert.True(await s.IsSignedIn(), "signed in");
                Assert.True(s.Me["secure"].Bool, "over HTTPS");
                var packs = await main.Get("/api/ui-packs");
                Assert.True(packs["active"].IsNull, "built-in interface active");
                Assert.True(packs["packs"].Items.Any(x => x["id"].Str == "horizon"), "packs listed");
                var notes = await main.Get("/api/notifications");
                Assert.True(notes["notifications"].IsArray, "notifications");
                var vehicles = await main.Get("/api/vehicles");
                Assert.True(vehicles["vehicles"].IsArray, "vehicles");
            });

            test("api: GetBytes returns files as they are", async () =>
            {
                var crt = Encoding.ASCII.GetString(await main.GetBytes("/api/security/ca.crt"));
                Assert.True(crt.StartsWith("-----BEGIN CERTIFICATE-----"), "CA certificate");
                var zip = await main.GetBytes("/api/ui-packs/original.zip");
                Assert.True(zip.Length > 1000 && zip[0] == (byte)'P' && zip[1] == (byte)'K', "zip");
                await Assert.ApiFails(() => main.GetBytes("/api/logs/archive/nope.log"), 404, null, "missing file");
            });

            test("api: errors carry status and the server's message", async () =>
            {
                var e = await Assert.ApiFails(() => main.Get("/api/no-such-route"), 404, null, "404");
                Assert.Equal("Not found", e.Message);
                e = await Assert.ApiFails(() => main.Post("/api/jobs/clear", JsonValue.Object("vehicle", 5)), 400, null, "400");
                Assert.True(e.Body["error"].Str.Length > 0, "body kept");
                await Assert.ThrowsAsync<ArgumentException>(() => main.Get("../etc/passwd"), "a path not starting with /");
            });

            test("api: a request without a session raises SignedOut (401)", async () =>
            {
                using var api = NewApi(new Prompt());
                int signedOut = 0;
                api.SignedOut += () => signedOut++;
                await Assert.ApiFails(() => api.Get("/api/vehicles"), 401, "SIGNED_OUT", "no cookie");
                Assert.Equal(1, signedOut, "raised once");
                // and after signing out of a real session
                var s = new Session(api);
                await s.SignIn("admin", Password);
                await api.Get("/api/vehicles");
                await s.SignOut();
                Assert.True(s.User.IsNull, "user cleared");
                await Assert.ApiFails(() => api.Put("/api/settings/appearance", JsonValue.Object("clock24", true)), 401, "SIGNED_OUT", "after sign-out");
                Assert.Equal(2, signedOut, "raised again");
            });

            test("events: hello on connect, 'changed' with topic 'interface' after switching the interface", async () =>
            {
                var got = new ConcurrentQueue<(string, JsonValue)>();
                var states = new ConcurrentQueue<bool>();
                using var es = new EventStream(main);
                es.Message += (n, d) => got.Enqueue((n, d));
                es.ConnectedChanged += c => states.Enqueue(c);
                es.Start();
                await Until(() => got.Any(x => x.Item1 == "hello"), "hello");
                Assert.True(es.IsConnected && states.Contains(true), "connected");
                // the session just signed in, so the password was confirmed with it
                var r = await main.Put("/api/ui-packs/active", JsonValue.Object("id", "horizon"));
                Assert.Equal("horizon", r["active"].Str);
                await Until(() => got.Any(x => x.Item1 == "changed" && x.Item2["topics"].Items.Any(t => t.Str == "interface")), "changed/interface");
                Assert.Equal("horizon", (await main.Get("/api/ui-packs"))["active"].Str, "active now");
                await main.Put("/api/ui-packs/active", JsonValue.Object("id", (string)null));
                es.Stop();
                await Until(() => !es.IsConnected, "stopped");
            });

            test("events: reconnect after the server restarts; CONFIRM_PASSWORD after restart", async () =>
            {
                var hellos = 0;
                var states = new ConcurrentQueue<bool>();
                using var es = new EventStream(main) { MinDelay = TimeSpan.FromMilliseconds(200), MaxDelay = TimeSpan.FromSeconds(1) };
                es.Message += (n, d) => { if (n == "hello") Interlocked.Increment(ref hellos); };
                es.ConnectedChanged += c => states.Enqueue(c);
                es.Start();
                await Until(() => hellos == 1, "first hello");
                a.Stop();
                await Until(() => !es.IsConnected, "dropped");
                await Run(a);
                await Until(() => hellos == 2, "hello after restart", 30000);
                Assert.True(es.IsConnected, "connected again");
                Assert.Equal("true,false,true", string.Join(",", states.Select(s => s ? "true" : "false")), "ConnectedChanged");

                // the session survived the restart (it is in the database);
                // the "recently confirmed" window did not (memory only)
                Assert.Equal("admin", (await main.Get("/api/auth/me"))["user"].Str, "still signed in");

                // no AskPassword: the request fails with CONFIRM_PASSWORD
                main.AskPassword = null;
                var e = await Assert.ApiFails(() => main.Put("/api/ui-packs/active", JsonValue.Object("id", "flux")), 403, "CONFIRM_PASSWORD", "no prompt");
                Assert.Equal("Enter your password to confirm this change.", e.Message);

                // cancelled
                var asked = new List<string>();
                main.AskPassword = () => { asked.Add(main.AskPasswordMessage); return Task.FromResult<string>(null); };
                await Assert.ApiFails(() => main.Put("/api/ui-packs/active", JsonValue.Object("id", "flux")), 403, "CONFIRM_CANCELLED", "cancelled");
                Assert.Equal(1, asked.Count, "asked once");

                // a wrong password, then the right one: confirmed, and the request repeated
                asked.Clear();
                var answers = new Queue<string>(new[] { "not-the-password", Password });
                main.AskPassword = () => { asked.Add(main.AskPasswordMessage); return Task.FromResult(answers.Dequeue()); };
                var got = await main.Put("/api/ui-packs/active", JsonValue.Object("id", "flux"));
                Assert.Equal("flux", got["active"].Str, "done after confirming");
                Assert.Equal(2, asked.Count, "asked twice");
                Assert.Equal(Api.ConfirmFirstMessage, asked[0]);
                Assert.Equal(Api.ConfirmAgainMessage, asked[1]);
                // confirmed for a while now: not asked again
                await main.Put("/api/ui-packs/active", JsonValue.Object("id", (string)null));
                Assert.Equal(2, asked.Count, "window open");
                main.AskPassword = null;
            });

            test("events: a 401 stops the stream and raises SignedOut", async () =>
            {
                using var api = NewApi(new Prompt());
                int signedOut = 0;
                api.SignedOut += () => Interlocked.Increment(ref signedOut);
                using var es = new EventStream(api);
                es.Start();
                await Until(() => signedOut == 1, "SignedOut");
                await Task.Delay(300);
                Assert.True(!es.IsConnected && signedOut == 1, "stopped, not retrying");
            });

            test("certificate: another server on the same address is refused (CERT_CHANGED); first account with the setup code", async () =>
            {
                a.Stop();
                // the same host:port, another installation: its own certificate authority and certificate
                var b = await Run(new NodeServer { Port = a.Port, Data = Temp.Dir("b"), Password = null });
                var e = await Assert.ApiFails(() => main.Get("/api/info"), 0, "CERT_CHANGED", "pinned client");
                Assert.True(e.PresentedSha256 != null && e.PresentedSha256 != mainPrompt.LastSha, "the new fingerprint");
                Assert.Equal(1, mainPrompt.Calls, "never asked about the changed one");

                var p = new Prompt();
                using var fresh = NewApi(p); // pin from the trust store
                await Assert.ApiFails(() => fresh.Get("/api/info"), 0, "CERT_CHANGED", "stored pin");
                Assert.Equal(0, p.Calls, "not asked");

                // the person compared the new fingerprint and trusts it
                fresh.ForgetCertificate();
                var s = new Session(fresh);
                await s.Info();
                Assert.Equal(1, p.Calls, "asked for the new one");
                Assert.Equal(e.PresentedSha256.Replace(":", ""), new FileTrustStore(trustFile).Get(fresh.Server.TrustKey), "stored");

                // no account yet: the setup code from the server
                Assert.True(await s.NeedsSetup(), "needs setup");
                await Assert.ApiFails(() => s.SignIn("admin", Password), 401, null, "no account");
                var bad = await Assert.ApiFails(() => s.CreateFirstAccount("AAAAA-AAAAA-AAAAA", "owner", "longenough1"), 403, null, "wrong code");
                Assert.True(bad.Message.StartsWith("That setup code is not right."), bad.Message);
                await s.CreateFirstAccount(b.SetupCode().ToLowerInvariant(), "owner", "longenough1");
                Assert.Equal("owner", s.User.Str, "signed in as the new account");
                Assert.True(!await s.NeedsSetup(), "set up");
                await Assert.ApiFails(() => s.CreateFirstAccount("AAAAA-AAAAA-AAAAA", "x", "longenough2"), 409, null, "only once");
            });
        }
    }
}
