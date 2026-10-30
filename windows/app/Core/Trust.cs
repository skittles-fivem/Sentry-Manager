using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SentrySix.Core
{
    /// <summary>
    /// Which server the app talks to, and the SHA-256 of the HTTPS
    /// certificate it was told to trust there.
    /// </summary>
    public class ServerAddress
    {
        Uri baseUri;

        public ServerAddress(Uri baseUri) : this(baseUri, null)
        {
        }

        public ServerAddress(Uri baseUri, string pinnedSha256)
        {
            if (baseUri == null) throw new ArgumentNullException("baseUri");
            if (!baseUri.IsAbsoluteUri) throw new ArgumentException("The server address must be absolute.");
            // the API paths start with /, so the base is the server's root
            this.baseUri = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/");
            PinnedSha256 = Certificates.NormalizeSha256(pinnedSha256);
        }

        /// <summary>https://host:port/</summary>
        public Uri BaseUri { get { return baseUri; } }

        /// <summary>The trusted certificate's SHA-256 as 64 upper-case hex digits; null until one is trusted.</summary>
        public string PinnedSha256 { get; set; }

        /// <summary>What the trust store files this server under: host:port.</summary>
        public string TrustKey { get { return baseUri.Authority.ToLowerInvariant(); } }

        /// <summary>The service on this PC. HTTPS: it serves HTTPS on its one port (plain HTTP there only redirects).</summary>
        public static ServerAddress Local(int port)
        {
            return new ServerAddress(new Uri("https://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"));
        }

        public override string ToString()
        {
            return baseUri.ToString();
        }
    }

    /// <summary>Asks the person whether to trust a server's certificate (the first time only).</summary>
    public interface ICertificatePrompt
    {
        /// <param name="sha256Colons">AB:CD:... as browsers and the server's log and sign-in page show it.</param>
        /// <param name="subject">The certificate's subject, e.g. "O=Sentry Six Manager, CN=localhost".</param>
        System.Threading.Tasks.Task<bool> TrustAsync(string sha256Colons, string subject);
    }

    /// <summary>Remembers trusted certificates between runs.</summary>
    public interface ITrustStore
    {
        /// <summary>The pinned SHA-256 (hex) for host:port, or null.</summary>
        string Get(string host);
        void Set(string host, string sha256);
    }

    /// <summary>A trust store in one small JSON file: { "host:port": "HEX", ... }.</summary>
    public class FileTrustStore : ITrustStore
    {
        readonly string path;
        readonly object gate = new object();

        public FileTrustStore(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A file path is needed.");
            this.path = path;
        }

        public string Path { get { return path; } }

        public string Get(string host)
        {
            if (host == null) return null;
            lock (gate)
            {
                return Certificates.NormalizeSha256(Load()[host.ToLowerInvariant()].Str);
            }
        }

        public void Set(string host, string sha256)
        {
            if (host == null) throw new ArgumentNullException("host");
            lock (gate)
            {
                List<object> pairs = new List<object>();
                foreach (KeyValuePair<string, JsonValue> p in Load().Props)
                {
                    if (p.Key == host.ToLowerInvariant()) continue;
                    pairs.Add(p.Key);
                    pairs.Add(p.Value);
                }
                string norm = Certificates.NormalizeSha256(sha256);
                if (norm != null)
                {
                    pairs.Add(host.ToLowerInvariant());
                    pairs.Add(norm);
                }
                string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                // written beside, then moved over: a crash never leaves half a file
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonValue.Object(pairs.ToArray()).ToJson(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
        }

        JsonValue Load()
        {
            try
            {
                if (!File.Exists(path)) return JsonValue.Null;
                return JsonValue.TryParse(File.ReadAllText(path, Encoding.UTF8)) ?? JsonValue.Null;
            }
            catch (IOException)
            {
                return JsonValue.Null;
            }
            catch (UnauthorizedAccessException)
            {
                return JsonValue.Null;
            }
        }
    }

    /// <summary>A failed API call: the HTTP status, the server's error code (e.g. CONFIRM_PASSWORD) and its message.</summary>
    public class ApiError : Exception
    {
        public ApiError(int status, string code, string message) : this(status, code, message, null)
        {
        }

        public ApiError(int status, string code, string message, Exception inner) : base(message, inner)
        {
            Status = status;
            Code = code;
        }

        /// <summary>The HTTP status; 0 when the server could not be reached.</summary>
        public int Status { get; private set; }

        /// <summary>
        /// The server's code (CONFIRM_PASSWORD, ...) or one of the app's own:
        /// UNREACHABLE, TIMEOUT, CERT_UNTRUSTED (the person said no),
        /// CERT_CHANGED (not the certificate trusted before),
        /// CONFIRM_CANCELLED (no password given), SIGNED_OUT.
        /// </summary>
        public string Code { get; private set; }

        /// <summary>The body of the error reply, when it was JSON.</summary>
        public JsonValue Body { get; set; }

        /// <summary>For CERT_CHANGED and CERT_UNTRUSTED: the certificate the server showed (AB:CD:...).</summary>
        public string PresentedSha256 { get; set; }
    }

    static class Certificates
    {
        /// <summary>Any of "ab:cd:..", "ABCD..", spaced: 64 upper-case hex digits, or null.</summary>
        public static string NormalizeSha256(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            StringBuilder sb = new StringBuilder(64);
            foreach (char c in s)
            {
                if (c == ':' || c == ' ' || c == '-') continue;
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return null;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.Length == 64 ? sb.ToString() : null;
        }

        public static string WithColons(string hex)
        {
            StringBuilder sb = new StringBuilder(95);
            for (int i = 0; i < hex.Length; i += 2)
            {
                if (i > 0) sb.Append(':');
                sb.Append(hex, i, 2);
            }
            return sb.ToString();
        }

        public static string Sha256Hex(byte[] der)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(der);
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
