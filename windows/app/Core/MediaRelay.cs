using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// Clips for ffmpeg without TLS or cookies on its command line: a
    /// listener on 127.0.0.1 (any free port; a TcpListener needs no URL ACL)
    /// answering GET and HEAD /token/n with just enough HTTP/1.1, each
    /// request passed to the Api's HttpClient as /api/media?path= with the
    /// same Range, and the status, Content-Length, Content-Range,
    /// Accept-Ranges and Content-Type copied back, the body streamed. The
    /// server path stays in the token map: the loopback and the command line
    /// carry only a random 128-bit token (passwords stay on HTTPS).
    /// </summary>
    public sealed class MediaRelay : IDisposable
    {
        /// <summary>How the relay asks the server; the Api's OpenMedia, or a test's own.</summary>
        public delegate Task<HttpResponseMessage> Opener(string serverPath, string range, bool head, CancellationToken ct);

        const int BufferSize = 64 * 1024;
        const int MaxHeaderBytes = 16 * 1024;

        readonly Opener open;
        readonly TcpListener listener;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly object gate = new object();
        readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> tokenOf = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        int next;
        bool disposed;

        public MediaRelay(Api api) : this(api.OpenMedia) { }

        public MediaRelay(Opener open)
        {
            if (open == null) throw new ArgumentNullException("open");
            this.open = open;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(64);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task.Run(new Func<Task>(AcceptLoop));
        }

        public int Port { get; private set; }

        /// <summary>The URL ffmpeg opens for a server path (the same one each time the path is asked).</summary>
        public string UrlFor(string serverPath)
        {
            if (serverPath == null) throw new ArgumentNullException("serverPath");
            lock (gate)
            {
                string key;
                if (!tokenOf.TryGetValue(serverPath, out key))
                {
                    byte[] r = new byte[16];
                    using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(r);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in r) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    // /token/n: n only makes each clip's URL look like a file to ffmpeg
                    key = sb + "/" + (next++).ToString(CultureInfo.InvariantCulture);
                    tokenOf[serverPath] = key;
                    paths[key] = serverPath;
                }
                return "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/" + key;
            }
        }

        /// <summary>Forgets every token (the viewer closed): old URLs answer 404.</summary>
        public void Clear()
        {
            lock (gate) { paths.Clear(); tokenOf.Clear(); }
        }

        async Task AcceptLoop()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient c;
                try
                {
                    c = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    if (stop.IsCancellationRequested) return;
                    continue;
                }
                lock (gate)
                {
                    if (disposed) { c.Close(); return; }
                    clients.Add(c);
                }
                Forget(Task.Run(() => Serve(c)));
            }
        }

        static void Forget(Task t) { }

        /// <summary>Where the relay says what went wrong on a connection (a test, a timing log); null: nowhere.</summary>
        public static Action<string> Trace;

        static void Note(string line)
        {
            Action<string> h = Trace;
            if (h == null) return;
            try { h(line); } catch (Exception) { }
        }

        sealed class Request
        {
            public string Method;
            public string Target;
            public string Range;
            public bool KeepAlive = true;
        }

        async Task Serve(TcpClient c)
        {
            try
            {
                c.NoDelay = true;
                using (NetworkStream ns = c.GetStream())
                {
                    bool ended = await Requests(ns).ConfigureAwait(false);
                    // a connection that ends after its answer ends gently: the send side shut, then
                    // what the client still sends read until it hangs up (2 s at most), so Windows
                    // does not reset it while the client still reads the body (ffprobe's moov at the end)
                    if (ended) await CloseGently(c, ns).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // ffmpeg went away (a seek kills it): nothing to tell anyone but a trace
                Note("connection ended: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                lock (gate) clients.Remove(c);
                c.Close();
            }
        }

        static async Task CloseGently(TcpClient c, NetworkStream ns)
        {
            try
            {
                c.Client.Shutdown(SocketShutdown.Send);
                byte[] sink = new byte[4096];
                Task timeout = Task.Delay(2000);
                while (true)
                {
                    Task<int> r = ns.ReadAsync(sink, 0, sink.Length);
                    if (await Task.WhenAny(r, timeout).ConfigureAwait(false) != r) break;
                    if (r.Result <= 0) break;
                }
            }
            catch (Exception) { }
        }

        /// <summary>The connection's requests answered in turn; true when it ended after an answer (not the client gone).</summary>
        async Task<bool> Requests(NetworkStream ns)
        {
            byte[] buffer = new byte[BufferSize];
            MemoryStream pending = new MemoryStream();
            while (!stop.IsCancellationRequested)
            {
                Request req = await ReadRequest(ns, pending).ConfigureAwait(false);
                if (req == null) return false;
                Note(req.Method + " " + req.Target + " range " + (req.Range ?? "-") + (req.KeepAlive ? "" : " (close)"));
                bool keep;
                // per request: cancelled when the relay stops or the request ends (a write to an
                // ffmpeg that was killed throws), so the server's response and its connection go at once
                using (CancellationTokenSource gone = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    try { keep = await Answer(ns, req, buffer, gone.Token).ConfigureAwait(false); }
                    finally { gone.Cancel(); }
                }
                if (!keep || !req.KeepAlive) return true;
            }
            return false;
        }

        static async Task<Request> ReadRequest(NetworkStream ns, MemoryStream pending)
        {
            byte[] one = new byte[4096];
            while (true)
            {
                byte[] have = pending.ToArray();
                int end = HeaderEnd(have);
                if (end >= 0)
                {
                    string head = Encoding.ASCII.GetString(have, 0, end);
                    pending.SetLength(0);
                    pending.Write(have, end + 4, have.Length - end - 4);
                    return Parse(head);
                }
                if (have.Length > MaxHeaderBytes) return null;
                int n = await ns.ReadAsync(one, 0, one.Length).ConfigureAwait(false);
                if (n <= 0) return null;
                pending.Write(one, 0, n);
            }
        }

        static int HeaderEnd(byte[] b)
        {
            for (int i = 0; i + 3 < b.Length; i++)
                if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
            return -1;
        }

        static Request Parse(string head)
        {
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 3) return new Request { Method = "", Target = "", KeepAlive = false };
            Request r = new Request { Method = first[0], Target = first[1] };
            if (first[2] == "HTTP/1.0") r.KeepAlive = false;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                if (name.Equals("Range", StringComparison.OrdinalIgnoreCase)) r.Range = value;
                else if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Equals("close", StringComparison.OrdinalIgnoreCase)) r.KeepAlive = false;
                    else if (value.Equals("keep-alive", StringComparison.OrdinalIgnoreCase)) r.KeepAlive = true;
                }
            }
            return r;
        }

        /// <summary>The path a request's target names, or null (an unknown token, anything else).</summary>
        string Lookup(string target)
        {
            if (string.IsNullOrEmpty(target) || target[0] != '/') return null;
            string key = target.Substring(1);
            int q = key.IndexOf('?');
            if (q >= 0) key = key.Substring(0, q);
            lock (gate)
            {
                string p;
                return paths.TryGetValue(key, out p) ? p : null;
            }
        }

        static Task WriteHead(NetworkStream ns, int status, string reason, IList<KeyValuePair<string, string>> headers)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(string.IsNullOrEmpty(reason) ? "Status" : reason).Append("\r\n");
            foreach (KeyValuePair<string, string> h in headers) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
            sb.Append("\r\n");
            byte[] b = Encoding.ASCII.GetBytes(sb.ToString());
            return ns.WriteAsync(b, 0, b.Length);
        }

        /// <summary>One answer; false when the connection cannot be used again.</summary>
        async Task<bool> Answer(NetworkStream ns, Request req, byte[] buffer, CancellationToken ct)
        {
            List<KeyValuePair<string, string>> h = new List<KeyValuePair<string, string>>();
            bool head = req.Method == "HEAD";
            if (req.Method != "GET" && !head)
            {
                h.Add(new KeyValuePair<string, string>("Content-Length", "0"));
                h.Add(new KeyValuePair<string, string>("Connection", "close"));
                await WriteHead(ns, 405, "Method Not Allowed", h).ConfigureAwait(false);
                return false;
            }
            string path = Lookup(req.Target);
            if (path == null)
            {
                h.Add(new KeyValuePair<string, string>("Content-Length", "0"));
                await WriteHead(ns, 404, "Not Found", h).ConfigureAwait(false);
                return true;
            }
            HttpResponseMessage res = null;
            try
            {
                res = await open(path, req.Range, head, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Note("the server could not be asked for " + req.Target + " (" + req.Range + "): " + ex.GetType().Name + ": " + ex.Message);
                res = null; // (C# 5 cannot await in a catch: answered below)
            }
            if (res == null)
            {
                h.Add(new KeyValuePair<string, string>("Content-Length", "0"));
                h.Add(new KeyValuePair<string, string>("Connection", "close"));
                await WriteHead(ns, 502, "Bad Gateway", h).ConfigureAwait(false);
                return false;
            }
            using (res)
            {
                long? length = res.Content == null ? null : res.Content.Headers.ContentLength;
                if (length.HasValue) h.Add(new KeyValuePair<string, string>("Content-Length", length.Value.ToString(CultureInfo.InvariantCulture)));
                if (res.Content != null && res.Content.Headers.ContentRange != null) h.Add(new KeyValuePair<string, string>("Content-Range", res.Content.Headers.ContentRange.ToString()));
                if (res.Content != null && res.Content.Headers.ContentType != null) h.Add(new KeyValuePair<string, string>("Content-Type", res.Content.Headers.ContentType.ToString()));
                IEnumerable<string> ar;
                if (res.Headers.TryGetValues("Accept-Ranges", out ar)) h.Add(new KeyValuePair<string, string>("Accept-Ranges", string.Join(", ", ar)));
                else if (res.Headers.AcceptRanges.Count > 0) h.Add(new KeyValuePair<string, string>("Accept-Ranges", string.Join(", ", res.Headers.AcceptRanges)));
                bool chunked = !length.HasValue && !head;
                // a request that asked to close (ffmpeg's Connection: close) is told the connection
                // closes, so it opens a new one to seek instead of asking again on this one
                if (chunked || !req.KeepAlive) h.Add(new KeyValuePair<string, string>("Connection", "close"));
                await WriteHead(ns, (int)res.StatusCode, res.ReasonPhrase, h).ConfigureAwait(false);
                if (head || res.Content == null) return true;
                using (Stream body = await res.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    long sent = 0;
                    while (true)
                    {
                        int n = await body.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                        if (n <= 0) break;
                        // a write to a client that went away throws: the request (and the server's read) ends with it
                        await ns.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                        sent += n;
                    }
                    if (length.HasValue && sent != length.Value) return false;
                }
                return !chunked;
            }
        }

        public void Dispose()
        {
            List<TcpClient> still;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                still = new List<TcpClient>(clients);
                clients.Clear();
            }
            stop.Cancel();
            try { listener.Stop(); } catch (Exception) { }
            foreach (TcpClient c in still) { try { c.Close(); } catch (Exception) { } }
        }
    }
}
