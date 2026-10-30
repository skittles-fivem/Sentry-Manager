using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// The server's live updates: the same /api/events stream (server-sent
    /// events) the web pages open with EventSource. The server sends
    /// <c>hello</c> ({}) when connected and <c>changed</c> with
    /// <c>{ "topics": [...] }</c> (vehicles, inventory, bin, jobs,
    /// notifications, interface) at most every 0.4 s, and a comment every
    /// 20 s. A dropped stream is opened again with a growing pause (1 s up to
    /// 30 s), as EventSource does; a 401 stops it (signed out: the Api's
    /// SignedOut event fires) until <see cref="Start"/> is called again.
    ///
    /// Events are raised on the synchronization context that called Start
    /// (the WPF dispatcher when started from the window), else on a pool thread.
    /// </summary>
    public class EventStream : IDisposable
    {
        public const string Path = "/api/events";

        /// <summary>No byte for this long (the server pings every 20 s): the connection is dead, open a new one.</summary>
        public static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(65);

        readonly Api api;
        readonly object gate = new object();
        CancellationTokenSource running;
        SynchronizationContext context;
        bool connected;
        bool disposed;

        public EventStream(Api api)
        {
            if (api == null) throw new ArgumentNullException("api");
            this.api = api;
            MinDelay = TimeSpan.FromSeconds(1);
            MaxDelay = TimeSpan.FromSeconds(30);
        }

        /// <summary>event name ("hello", "changed"), its data (parsed JSON; a string when it is not JSON).</summary>
        public event Action<string, JsonValue> Message;

        /// <summary>true once the stream is open, false when it drops (and while it waits to try again).</summary>
        public event Action<bool> ConnectedChanged;

        public bool IsConnected { get { return connected; } }

        public TimeSpan MinDelay { get; set; }
        public TimeSpan MaxDelay { get; set; }

        /// <summary>Opens the stream (again); does nothing while it runs.</summary>
        public void Start()
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("EventStream");
                if (running != null) return;
                context = SynchronizationContext.Current;
                running = new CancellationTokenSource();
                CancellationTokenSource mine = running;
                Task.Run(delegate { return Loop(mine); });
            }
        }

        /// <summary>Closes the stream; Start opens it again.</summary>
        public void Stop()
        {
            lock (gate)
            {
                if (running == null) return;
                running.Cancel();
                running = null;
            }
        }

        async Task Loop(CancellationTokenSource mine)
        {
            CancellationToken ct = mine.Token;
            TimeSpan delay = MinDelay;
            Random jitter = new Random();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    bool signedOut = false;
                    try
                    {
                        using (HttpResponseMessage res = await api.OpenStream(Path, ct).ConfigureAwait(false))
                        {
                            delay = MinDelay;
                            SetConnected(true);
                            TimeSpan? retry = await Read(res, ct).ConfigureAwait(false);
                            if (retry.HasValue) delay = retry.Value;
                        }
                    }
                    catch (ApiError e)
                    {
                        if (e.Status == 401) signedOut = true;
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested) break;
                    }
                    catch (Exception)
                    {
                        // dropped: try again below
                    }
                    SetConnected(false);
                    if (signedOut || ct.IsCancellationRequested) break;
                    TimeSpan wait = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * (0.8 + 0.4 * jitter.NextDouble()));
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(Math.Min(MaxDelay.TotalMilliseconds, Math.Max(MinDelay.TotalMilliseconds, delay.TotalMilliseconds * 2)));
                }
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
            finally
            {
                SetConnected(false);
                lock (gate)
                {
                    if (running == mine) running = null;
                }
                mine.Dispose();
            }
        }

        /// <summary>Reads events until the stream ends; returns the server's retry: time, if it sent one.</summary>
        async Task<TimeSpan?> Read(HttpResponseMessage res, CancellationToken ct)
        {
            TimeSpan? retry = null;
            using (CancellationTokenSource idle = new CancellationTokenSource())
            using (CancellationTokenSource either = CancellationTokenSource.CreateLinkedTokenSource(ct, idle.Token))
            using (either.Token.Register(delegate { res.Dispose(); }))
            {
                idle.CancelAfter(IdleLimit);
                Stream stream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    string eventName = null;
                    StringBuilder data = null;
                    while (true)
                    {
                        string line;
                        try
                        {
                            line = await reader.ReadLineAsync().ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                            return retry; // closed, or idle too long
                        }
                        if (line == null) return retry;
                        idle.CancelAfter(IdleLimit);
                        if (line.Length == 0)
                        {
                            if (data != null) Dispatch(eventName ?? "message", data.ToString());
                            eventName = null;
                            data = null;
                            continue;
                        }
                        if (line[0] == ':') continue; // a comment: the server's keep-alive ping
                        int colon = line.IndexOf(':');
                        string field = colon < 0 ? line : line.Substring(0, colon);
                        string value = colon < 0 ? "" : line.Substring(colon + 1);
                        if (value.Length > 0 && value[0] == ' ') value = value.Substring(1);
                        if (field == "event") eventName = value;
                        else if (field == "data")
                        {
                            if (data == null) data = new StringBuilder();
                            else data.Append('\n');
                            data.Append(value);
                        }
                        else if (field == "retry")
                        {
                            int ms;
                            if (int.TryParse(value, out ms) && ms >= 0) retry = TimeSpan.FromMilliseconds(ms);
                        }
                    }
                }
            }
        }

        void Dispatch(string name, string text)
        {
            JsonValue data = JsonValue.TryParse(text) ?? JsonValue.String(text);
            Raise(delegate
            {
                Action<string, JsonValue> h = Message;
                if (h != null) h(name, data);
            });
        }

        void SetConnected(bool now)
        {
            lock (gate)
            {
                if (connected == now) return;
                connected = now;
            }
            Raise(delegate
            {
                Action<bool> h = ConnectedChanged;
                if (h != null) h(now);
            });
        }

        void Raise(Action a)
        {
            SynchronizationContext ctx = context;
            if (ctx != null) ctx.Post(delegate { a(); }, null);
            else a();
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
            }
            Stop();
        }
    }
}
