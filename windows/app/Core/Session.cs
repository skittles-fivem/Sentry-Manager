using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// Sign-in as the sign-in page does it (server/auth.js): a local account
    /// and a cookie session. The first account comes from UI_PASSWORD or is
    /// created with the one-time setup code the server printed.
    /// </summary>
    public class Session
    {
        readonly Api api;

        public Session(Api api)
        {
            if (api == null) throw new ArgumentNullException("api");
            this.api = api;
            User = JsonValue.Null;
            Me = JsonValue.Null;
            api.SignedOut += delegate { User = JsonValue.Null; };
        }

        public Api Api { get { return api; } }

        /// <summary>The signed-in user's name (a string value), or JSON null.</summary>
        public JsonValue User { get; private set; }

        /// <summary>The last /api/auth/me reply: user, needsSetup, secure, fingerprint, deployment.</summary>
        public JsonValue Me { get; private set; }

        /// <summary>The public /api/info: { deployment: "docker" | "linux" | "windows" }.</summary>
        public Task<JsonValue> Info()
        {
            return api.Get("/api/info");
        }

        /// <summary>Asks the server who this session is (public /api/auth/me) and keeps the answer in Me and User.</summary>
        public async Task<JsonValue> Refresh()
        {
            JsonValue me = await api.Get("/api/auth/me").ConfigureAwait(false);
            Me = me;
            User = me["user"];
            return me;
        }

        public async Task<bool> IsSignedIn()
        {
            JsonValue me = await Refresh().ConfigureAwait(false);
            return !me["user"].IsNull;
        }

        /// <summary>No account exists yet: CreateFirstAccount with the setup code.</summary>
        public async Task<bool> NeedsSetup()
        {
            JsonValue me = await Refresh().ConfigureAwait(false);
            return me["needsSetup"].Bool;
        }

        /// <summary>
        /// Signs in; throws ApiError 401 "Wrong user name or password." or 429
        /// "Too many attempts. Try again in N s." with the server's wording.
        /// </summary>
        public async Task SignIn(string user, string password)
        {
            await api.Post("/api/auth/login", JsonValue.Object("username", user, "password", password)).ConfigureAwait(false);
            await Refresh().ConfigureAwait(false);
        }

        public async Task SignOut()
        {
            try
            {
                await api.Post("/api/auth/logout").ConfigureAwait(false);
            }
            catch (ApiError e)
            {
                // already signed out there: fine
                if (e.Status != 401) throw;
            }
            finally
            {
                api.ForgetCookies();
                User = JsonValue.Null;
            }
        }

        /// <summary>
        /// The first account, on a server that has none (/api/auth/me
        /// needsSetup), with the setup code from the server's log or
        /// setup-code.txt (dashes and case do not matter). Signs in as it.
        /// Throws ApiError 403 (wrong code), 409 (an account exists), 400
        /// (user name or password not allowed).
        /// </summary>
        public async Task CreateFirstAccount(string setupCode, string user, string password)
        {
            await api.Post("/api/auth/setup", JsonValue.Object("username", user, "password", password, "code", setupCode)).ConfigureAwait(false);
            await Refresh().ConfigureAwait(false);
        }

        /// <summary>Confirms the password now (opens the server's 10-minute window for sensitive changes).</summary>
        public Task ConfirmPassword(string password)
        {
            return api.Post("/api/auth/confirm", JsonValue.Object("password", password));
        }

        /// <summary>Changes the password; the server signs out every other session.</summary>
        public Task ChangePassword(string current, string next)
        {
            return api.Post("/api/auth/password", JsonValue.Object("current", current, "next", next));
        }
    }

    /// <summary>Where the service on this PC listens.</summary>
    public static class LocalServer
    {
        public const int DefaultPort = 9000;

        /// <summary>
        /// Windows: PORT from %ProgramData%\SentrySixManager\manager.env (the
        /// service's settings), else 9000. Elsewhere (tests): the SENTRY_PORT
        /// environment variable, else 9000.
        /// </summary>
        public static int Port()
        {
            if (IsWindows)
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return PortFromEnvFile(Path.Combine(Path.Combine(dir, "SentrySixManager"), "manager.env"));
            }
            int port;
            return ParsePort(Environment.GetEnvironmentVariable("SENTRY_PORT"), out port) ? port : DefaultPort;
        }

        public static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        /// <summary>PORT from a .env file (KEY=value lines, # comments, optional quotes), else 9000.</summary>
        public static int PortFromEnvFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return DefaultPort;
                return PortFromEnvText(File.ReadAllText(path));
            }
            catch (IOException)
            {
                return DefaultPort;
            }
            catch (UnauthorizedAccessException)
            {
                return DefaultPort;
            }
        }

        public static int PortFromEnvText(string text)
        {
            int found = DefaultPort;
            foreach (KeyValuePair<string, string> kv in ParseEnv(text))
            {
                int port;
                if (kv.Key == "PORT" && ParsePort(kv.Value, out port)) found = port; // the last one wins
            }
            return found;
        }

        static IEnumerable<KeyValuePair<string, string>> ParseEnv(string text)
        {
            if (text == null) yield break;
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("export ", StringComparison.Ordinal)) line = line.Substring(7).TrimStart();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0]) value = value.Substring(1, value.Length - 2);
                else
                {
                    int hash = value.IndexOf(" #", StringComparison.Ordinal);
                    if (hash >= 0) value = value.Substring(0, hash).Trim();
                }
                yield return new KeyValuePair<string, string>(key, value);
            }
        }

        static bool ParsePort(string s, out int port)
        {
            port = 0;
            if (string.IsNullOrEmpty(s)) return false;
            return int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536;
        }
    }
}
