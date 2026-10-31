using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// An <see cref="IJsonApi"/> that answers from fixture data, for the render
    /// test mode (SentrySix.exe --render) and the tests: an object keyed by
    /// "GET &lt;path&gt;" (the path with its query, exactly as the page asks it).
    /// A value { "$error": "text", "$status": 500, "$code": "X" } fails with
    /// ApiError; a GET with no entry fails with a 404 ApiError, as an unknown
    /// API would. POST, PUT and DELETE answer their own entry if there is one,
    /// else { ok: true }. Files (GetBytes) answer "BYTES &lt;path&gt;" entries
    /// as base64, else fail, so thumbnails show their fallback text.
    /// Every task is already completed, so a page's awaits finish at once.
    /// </summary>
    public sealed class FixtureApi : IJsonApi
    {
        readonly JsonValue answers;
        readonly List<string> asked = new List<string>();

        public FixtureApi(JsonValue answers)
        {
            this.answers = answers ?? JsonValue.Object();
        }

        /// <summary>
        /// The fixture's "api" answers with a variant's on top: fixture["api"]
        /// merged with fixture["variants"][variant]["api"] (a variant's key wins).
        /// </summary>
        public static FixtureApi For(JsonValue fixture, string variant)
        {
            JsonValue merged = JsonValue.Object();
            foreach (KeyValuePair<string, JsonValue> kv in fixture["api"].Props) merged = merged.With(kv.Key, kv.Value);
            if (!string.IsNullOrEmpty(variant))
                foreach (KeyValuePair<string, JsonValue> kv in fixture["variants"][variant]["api"].Props) merged = merged.With(kv.Key, kv.Value);
            return new FixtureApi(merged);
        }

        /// <summary>Every request so far, as "METHOD path".</summary>
        public IList<string> Asked { get { return asked; } }

        Task<JsonValue> Answer(string method, string path, bool mustExist)
        {
            string key = method + " " + path;
            asked.Add(key);
            TaskCompletionSource<JsonValue> t = new TaskCompletionSource<JsonValue>();
            if (!answers.Has(key))
            {
                if (mustExist) t.SetException(new ApiError(404, "NOT_FOUND", "Not found"));
                else t.SetResult(JsonValue.Object("ok", true));
                return t.Task;
            }
            JsonValue v = answers[key];
            if (v.IsObject && v.Has("$error"))
            {
                int status = v.Has("$status") ? (int)v["$status"].Long : 500;
                ApiError e = new ApiError(status, v["$code"].Str, v["$error"].Str ?? "Failed");
                e.Body = JsonValue.Object("error", v["$error"].Str ?? "Failed");
                t.SetException(e);
                return t.Task;
            }
            // a copy, so a page that changes its answer does not change the next one
            t.SetResult(JsonValue.Parse(v.ToJson()));
            return t.Task;
        }

        public Task<JsonValue> Get(string path, CancellationToken ct) { return Answer("GET", path, true); }
        public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) { return Answer("POST", path, false); }
        public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) { return Answer("PUT", path, false); }
        public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) { return Answer("DELETE", path, false); }

        public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct)
        {
            return Answer("POST", path, false);
        }

        public Task<byte[]> GetBytes(string path, CancellationToken ct)
        {
            string key = "BYTES " + path;
            asked.Add(key);
            TaskCompletionSource<byte[]> t = new TaskCompletionSource<byte[]>();
            string b64 = answers[key].Str;
            if (b64 == null) t.SetException(new ApiError(404, "NOT_FOUND", "Not found"));
            else
            {
                try { t.SetResult(Convert.FromBase64String(b64)); }
                catch (FormatException ex) { t.SetException(ex); }
            }
            return t.Task;
        }
    }
}
