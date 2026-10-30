using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// The JSON API the web pages use, as web/app.js calls it: the
    /// X-Requested-With header on every request, a 401 outside /api/auth/
    /// means signed out, a 403 CONFIRM_PASSWORD asks for the password, confirms
    /// it (/api/auth/confirm) and repeats the request once. The server's own
    /// certificate is trusted once, when the person says so, and pinned.
    /// </summary>
    public class Api : IDisposable
    {
        /// <summary>The header every state-changing request needs (server/index.js refuses one without it).</summary>
        public const string RequestedWithHeader = "X-Requested-With";
        public const string RequestedWithValue = "sentry-six-manager";

        // web/app.js's wording, so both say the same
        public const string ConfirmFirstMessage = "This change affects where footage comes from or what code runs, so Sentry Six Manager asks for your password again.";
        public const string ConfirmAgainMessage = "That password was not right. Try again.";
        public const string NotConfirmedMessage = "Not changed: the password was not confirmed.";
        public const string TimeoutMessage = "The server took too long to answer.";

        readonly ServerAddress server;
        readonly ICertificatePrompt prompt;
        readonly ITrustStore store;
        readonly CookieContainer cookies = new CookieContainer();
        readonly HttpClientHandler handler;
        readonly HttpClient http;
        readonly SemaphoreSlim trustGate = new SemaphoreSlim(1, 1);
        readonly SemaphoreSlim confirmGate = new SemaphoreSlim(1, 1);
        readonly object refusalsGate = new object();
        readonly Dictionary<HttpRequestMessage, Refusal> refusals = new Dictionary<HttpRequestMessage, Refusal>();
        Refusal lastRefusal;
        bool callbackGetsRequest;
        DateTime lastConfirmedUtc = DateTime.MinValue;
        bool disposed;

        sealed class Refusal
        {
            public string Sha256;
            public string Subject;
            public bool Changed;
        }

        public Api(ServerAddress server, ICertificatePrompt prompt) : this(server, prompt, null)
        {
        }

        /// <param name="store">Where trusted certificates are kept between runs (null: only in <paramref name="server"/>).</param>
        public Api(ServerAddress server, ICertificatePrompt prompt, ITrustStore store)
        {
            if (server == null) throw new ArgumentNullException("server");
            this.server = server;
            this.prompt = prompt;
            this.store = store;
            if (server.PinnedSha256 == null && store != null) server.PinnedSha256 = Certificates.NormalizeSha256(store.Get(server.TrustKey));
            RequestTimeout = TimeSpan.FromSeconds(100);
            GetRetries = 2;
            handler = new HttpClientHandler();
            handler.UseCookies = true;
            handler.CookieContainer = cookies;
            handler.AllowAutoRedirect = false;
            // .NET Framework allows 2 connections per server by default; the
            // event stream holds one for good, and pages load several things at once
            handler.MaxConnectionsPerServer = 16;
            handler.ServerCertificateCustomValidationCallback = CheckCertificate;
            http = new HttpClient(handler, true);
            http.Timeout = Timeout.InfiniteTimeSpan; // per request instead: the event stream stays open for hours
        }

        public ServerAddress Server { get { return server; } }

        /// <summary>A 401 from any API outside /api/auth/: the session is gone (web/app.js goes to the sign-in page).</summary>
        public event Action SignedOut;

        /// <summary>
        /// Asked when the server wants the password again (CONFIRM_PASSWORD);
        /// returns the password, or null when the person cancels. Read
        /// <see cref="AskPasswordMessage"/> for what to tell them. Null here:
        /// such a request fails with ApiError code CONFIRM_PASSWORD.
        /// </summary>
        public Func<Task<string>> AskPassword { get; set; }

        /// <summary>What the password dialog says now (the web page's wording; it changes after a wrong password).</summary>
        public string AskPasswordMessage { get; private set; }

        /// <summary>How long one request may take (default 100 s). The event stream is not limited by it.</summary>
        public TimeSpan RequestTimeout { get; set; }

        /// <summary>How often a GET is tried again when the server cannot be reached (default 2; the service may be restarting).</summary>
        public int GetRetries { get; set; }

        /// <summary>The cookies of this session (the sign-in cookie is HttpOnly and never needed by the app itself).</summary>
        public CookieContainer Cookies { get { return cookies; } }

        // ------------------------------------------------------------ requests

        public Task<JsonValue> Get(string path, CancellationToken ct = default(CancellationToken))
        {
            return SendJson(HttpMethod.Get, path, null, ct);
        }

        public Task<JsonValue> Post(string path, JsonValue body = null, CancellationToken ct = default(CancellationToken))
        {
            return SendJson(HttpMethod.Post, path, body, ct);
        }

        public Task<JsonValue> Put(string path, JsonValue body = null, CancellationToken ct = default(CancellationToken))
        {
            return SendJson(HttpMethod.Put, path, body, ct);
        }

        public Task<JsonValue> Delete(string path, JsonValue body = null, CancellationToken ct = default(CancellationToken))
        {
            return SendJson(HttpMethod.Delete, path, body, ct);
        }

        /// <summary>A file the API serves (a thumbnail, ca.crt, a .zip, a log): its bytes. Errors as for JSON calls.</summary>
        public async Task<byte[]> GetBytes(string path, CancellationToken ct = default(CancellationToken))
        {
            using (HttpResponseMessage res = await Send(HttpMethod.Get, path, null, null, ct, false).ConfigureAwait(false))
            {
                return await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A raw body, e.g. an interface pack .zip to /api/ui-packs/upload
        /// (with headers such as X-File-Name). Returns the JSON reply.
        /// </summary>
        public async Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers = null, CancellationToken ct = default(CancellationToken))
        {
            Func<HttpContent> content = delegate
            {
                ByteArrayContent c = new ByteArrayContent(data ?? new byte[0]);
                c.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/octet-stream");
                return c;
            };
            using (HttpResponseMessage res = await Send(HttpMethod.Post, path, content, headers, ct, false).ConfigureAwait(false))
            {
                return ParseBody(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        async Task<JsonValue> SendJson(HttpMethod method, string path, JsonValue body, CancellationToken ct)
        {
            string json = body == null ? null : body.ToJson();
            Func<HttpContent> content = delegate
            {
                if (json == null) return null;
                StringContent c = new StringContent(json, new UTF8Encoding(false));
                c.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return c;
            };
            using (HttpResponseMessage res = await Send(method, path, content, null, ct, false).ConfigureAwait(false))
            {
                return ParseBody(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        static JsonValue ParseBody(string text)
        {
            // as web/app.js: a reply that is not JSON reads as {}
            JsonValue v = JsonValue.TryParse(text);
            return v ?? JsonValue.Object();
        }

        /// <summary>Sends, handles CONFIRM_PASSWORD and 401, and returns a successful (2xx) response.</summary>
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, Func<HttpContent> content, IDictionary<string, string> headers, CancellationToken ct, bool retried)
        {
            DateTime sentUtc = DateTime.UtcNow;
            HttpResponseMessage res = await SendOnce(method, path, content, headers, ct, false).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return res;
            JsonValue data;
            using (res)
            {
                data = ParseBody(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            int status = (int)res.StatusCode;
            if (status == 401 && !IsAuthPath(path))
            {
                RaiseSignedOut();
                throw Error(status, "SIGNED_OUT", data, res.ReasonPhrase);
            }
            if (status == 403 && data["code"].Str == "CONFIRM_PASSWORD" && !retried)
            {
                // changes that could hand someone the shares or the server need the password again
                await ConfirmPassword(sentUtc, data, ct).ConfigureAwait(false);
                return await Send(method, path, content, headers, ct, true).ConfigureAwait(false);
            }
            throw Error(status, data["code"].Str, data, res.ReasonPhrase);
        }

        static ApiError Error(int status, string code, JsonValue data, string reason)
        {
            string msg = data["error"].Str;
            ApiError e = new ApiError(status, code, string.IsNullOrEmpty(msg) ? (reason ?? ("HTTP " + status)) : msg);
            e.Body = data;
            return e;
        }

        static bool IsAuthPath(string path)
        {
            return path.StartsWith("/api/auth/", StringComparison.Ordinal);
        }

        internal void RaiseSignedOut()
        {
            Action h = SignedOut;
            if (h != null) h();
        }

        /// <summary>Asks for the password and confirms it with the server; throws if cancelled (web/app.js confirmPassword).</summary>
        async Task ConfirmPassword(DateTime requestSentUtc, JsonValue refusal, CancellationToken ct)
        {
            await confirmGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // two requests refused at once: the second one only repeats
                if (lastConfirmedUtc > requestSentUtc) return;
                Func<Task<string>> ask = AskPassword;
                if (ask == null)
                {
                    ApiError e = new ApiError(403, "CONFIRM_PASSWORD", refusal["error"].Str ?? "Enter your password to confirm this change.");
                    e.Body = refusal;
                    throw e;
                }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    AskPasswordMessage = attempt == 0 ? ConfirmFirstMessage : ConfirmAgainMessage;
                    string password = await ask().ConfigureAwait(false);
                    if (password == null || password.Length == 0) throw new ApiError(403, "CONFIRM_CANCELLED", NotConfirmedMessage);
                    try
                    {
                        HttpResponseMessage ok = await Send(HttpMethod.Post, "/api/auth/confirm", JsonContent(JsonValue.Object("password", password)), null, ct, true).ConfigureAwait(false);
                        ok.Dispose();
                        lastConfirmedUtc = DateTime.UtcNow;
                        return;
                    }
                    catch (ApiError err)
                    {
                        // only a wrong password asks again; anything else (too
                        // many tries, the server unreachable) says what it is
                        if (err.Status != 401) throw;
                    }
                }
                throw new ApiError(403, "CONFIRM_CANCELLED", NotConfirmedMessage);
            }
            finally
            {
                AskPasswordMessage = null;
                confirmGate.Release();
            }
        }

        static Func<HttpContent> JsonContent(JsonValue body)
        {
            string json = body.ToJson();
            return delegate
            {
                StringContent c = new StringContent(json, new UTF8Encoding(false));
                c.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return c;
            };
        }

        /// <summary>One request, with the certificate trusted first if need be, and GETs tried again while the server is unreachable.</summary>
        async Task<HttpResponseMessage> SendOnce(HttpMethod method, string path, Func<HttpContent> content, IDictionary<string, string> headers, CancellationToken ct, bool stream)
        {
            if (disposed) throw new ObjectDisposedException("Api");
            if (string.IsNullOrEmpty(path) || path[0] != '/') throw new ArgumentException("API paths start with /.");
            Uri uri = new Uri(server.BaseUri, path);
            int unreachable = 0;
            bool askedTrust = false;
            while (true)
            {
                HttpRequestMessage req = new HttpRequestMessage(method, uri);
                req.Headers.Add(RequestedWithHeader, RequestedWithValue);
                if (headers != null) foreach (KeyValuePair<string, string> h in headers) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
                HttpContent body = content == null ? null : content();
                if (body != null) req.Content = body;
                CancellationTokenSource timeout = stream ? null : new CancellationTokenSource(RequestTimeout);
                CancellationTokenSource linked = timeout == null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                HttpRequestException failed = null;
                try
                {
                    return await http.SendAsync(req, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, linked == null ? ct : linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    TakeRefusal(req);
                    if (ct.IsCancellationRequested) throw;
                    throw new ApiError(0, "TIMEOUT", TimeoutMessage, ex);
                }
                catch (HttpRequestException ex)
                {
                    failed = ex; // (C# 5 cannot await in a catch: handled below)
                }
                finally
                {
                    if (linked != null) linked.Dispose();
                    if (timeout != null) timeout.Dispose();
                }
                Refusal r = TakeRefusal(req);
                if (r == null)
                {
                    if (method == HttpMethod.Get && !stream && unreachable < GetRetries)
                    {
                        await Task.Delay(500 * (1 << unreachable), ct).ConfigureAwait(false);
                        unreachable++;
                        continue;
                    }
                    throw new ApiError(0, "UNREACHABLE", "Can't reach Sentry Six Manager at " + server.BaseUri + " (" + Innermost(failed).Message + ").", failed);
                }
                if (r.Changed)
                {
                    ApiError e = new ApiError(0, "CERT_CHANGED",
                        "The server at " + server.BaseUri.Authority + " shows a different certificate than the one trusted before (" + Certificates.WithColons(r.Sha256) +
                        "). If its certificate was renewed, compare this fingerprint with the one in the server's log, then trust it again.", failed);
                    e.PresentedSha256 = Certificates.WithColons(r.Sha256);
                    throw e;
                }
                if (askedTrust || !await Trust(r).ConfigureAwait(false))
                {
                    ApiError e = new ApiError(0, "CERT_UNTRUSTED", "The server's certificate (" + Certificates.WithColons(r.Sha256) + ") was not trusted.", failed);
                    e.PresentedSha256 = Certificates.WithColons(r.Sha256);
                    throw e;
                }
                askedTrust = true; // and try again, now trusted
            }
        }

        static Exception Innermost(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e;
        }

        /// <summary>For the event stream: the response once its headers are in (2xx), else ApiError (401 also signs out).</summary>
        internal async Task<HttpResponseMessage> OpenStream(string path, CancellationToken ct)
        {
            HttpResponseMessage res = await SendOnce(HttpMethod.Get, path, null, null, ct, true).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return res;
            int status = (int)res.StatusCode;
            JsonValue data;
            using (res)
            {
                data = ParseBody(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            if (status == 401)
            {
                RaiseSignedOut();
                throw Error(status, "SIGNED_OUT", data, res.ReasonPhrase);
            }
            throw Error(status, data["code"].Str, data, res.ReasonPhrase);
        }

        // ------------------------------------------------------------ certificate

        /// <summary>
        /// Every new TLS connection: the pinned certificate passes; so does one
        /// Windows itself trusts (the CA installed, or a public certificate
        /// behind a reverse proxy), as in a browser. Anything else is refused
        /// here and noted, so the request can ask the person (first time) or
        /// report CERT_CHANGED (pinned to another one).
        /// </summary>
        bool CheckCertificate(HttpRequestMessage req, X509Certificate2 cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (cert == null) return false;
            string sha = Certificates.Sha256Hex(cert.RawData);
            string pinned = server.PinnedSha256;
            if (pinned != null && sha == pinned) return true;
            if (errors == SslPolicyErrors.None) return true;
            Refusal r = new Refusal();
            r.Sha256 = sha;
            r.Subject = cert.Subject;
            r.Changed = pinned != null;
            lock (refusalsGate)
            {
                if (req != null)
                {
                    callbackGetsRequest = true;
                    refusals[req] = r;
                }
                else lastRefusal = r;
            }
            return false;
        }

        Refusal TakeRefusal(HttpRequestMessage req)
        {
            lock (refusalsGate)
            {
                Refusal r;
                if (refusals.TryGetValue(req, out r)) refusals.Remove(req);
                else r = null;
                // (a framework that does not hand the request to the callback)
                if (r == null && !callbackGetsRequest)
                {
                    r = lastRefusal;
                    lastRefusal = null;
                }
                return r;
            }
        }

        async Task<bool> Trust(Refusal r)
        {
            await trustGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // another request asked meanwhile
                if (server.PinnedSha256 == r.Sha256) return true;
                if (server.PinnedSha256 != null) return false;
                if (prompt == null) return false;
                bool yes = await prompt.TrustAsync(Certificates.WithColons(r.Sha256), r.Subject).ConfigureAwait(false);
                if (!yes) return false;
                server.PinnedSha256 = r.Sha256;
                if (store != null) store.Set(server.TrustKey, r.Sha256);
                return true;
            }
            finally
            {
                trustGate.Release();
            }
        }

        /// <summary>
        /// Forgets the trusted certificate (here and in the trust store): the
        /// next request asks again. For a renewed certificate, after the person
        /// compared the new fingerprint (ApiError.PresentedSha256).
        /// </summary>
        public void ForgetCertificate()
        {
            server.PinnedSha256 = null;
            if (store != null) store.Set(server.TrustKey, null);
        }

        /// <summary>Drops this session's cookies (after signing out).</summary>
        public void ForgetCookies()
        {
            foreach (Cookie c in cookies.GetCookies(server.BaseUri)) c.Expired = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            http.Dispose();
        }
    }
}
