using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// What a page asks the server: the JSON API web/app.js's api() calls,
    /// and the raw files it links to. <see cref="Api"/> is the real one; the
    /// render test mode and the tests answer from fixtures (<see cref="FixtureApi"/>).
    /// Errors are <see cref="ApiError"/>, with the server's own words as the message.
    /// </summary>
    public interface IJsonApi
    {
        Task<JsonValue> Get(string path, CancellationToken ct);
        Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct);
        Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct);
        Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct);
        Task<byte[]> GetBytes(string path, CancellationToken ct);
        Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct);
    }

    /// <summary>The calls web/app.js makes with a timeout (api(path, { timeoutMs })), for any <see cref="IJsonApi"/>.</summary>
    public static class ApiCalls
    {
        /// <summary>GET /api/stats: 10 s (it repeats every 2 s, so a stuck one gives up instead of piling up).</summary>
        public const int StatsTimeoutMs = 10000;
        /// <summary>GET /api/stats/history, /api/logs and the log archive: 15 s.</summary>
        public const int HistoryTimeoutMs = 15000;

        public static Task<JsonValue> Get(IJsonApi api, string path)
        {
            return api.Get(path, CancellationToken.None);
        }

        public static Task<JsonValue> Post(IJsonApi api, string path, JsonValue body)
        {
            return api.Post(path, body, CancellationToken.None);
        }

        public static Task<JsonValue> Put(IJsonApi api, string path, JsonValue body)
        {
            return api.Put(path, body, CancellationToken.None);
        }

        public static Task<JsonValue> Delete(IJsonApi api, string path, JsonValue body)
        {
            return api.Delete(path, body, CancellationToken.None);
        }

        /// <summary>
        /// A GET that gives up after <paramref name="timeoutMs"/>: ApiError(0,
        /// "TIMEOUT", <see cref="Api.TimeoutMessage"/>). Cancelling
        /// <paramref name="ct"/> still throws OperationCanceledException.
        /// </summary>
        public static async Task<JsonValue> GetWithin(IJsonApi api, string path, int timeoutMs, CancellationToken ct)
        {
            using (CancellationTokenSource timer = new CancellationTokenSource(timeoutMs))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token))
            {
                try
                {
                    return await api.Get(path, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    if (ct.IsCancellationRequested) throw;
                    throw new ApiError(0, "TIMEOUT", Api.TimeoutMessage, ex);
                }
            }
        }

        /// <summary>A path part as encodeURIComponent writes it.</summary>
        public static string Enc(string s)
        {
            return Uri.EscapeDataString(s ?? "");
        }
    }
}
