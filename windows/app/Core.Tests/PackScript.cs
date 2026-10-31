using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentrySix.Core.Horizon;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// What the UI pack tests share: the answers as test/nativePackHarness.js
    /// gives them (by "METHOD path"; a GET without one fails "no answer for …",
    /// the others answer { ok: true }; { $seq } in turn, { $error } fails,
    /// { $pending } never answers), the requests as it records them, the
    /// fixtures, and a Horizon window that records toasts and dialogs.
    /// </summary>
    static class PackScript
    {
        public static JsonValue Fixture(string file) =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", file)));

        public sealed class Api : IJsonApi
        {
            readonly JsonValue answers;
            readonly Dictionary<string, int> used = new Dictionary<string, int>();
            public readonly List<string> Asked = new List<string>();

            public Api(JsonValue answers) { this.answers = answers ?? JsonValue.Object(); }

            Task<JsonValue> Answer(string method, string path, JsonValue body)
            {
                string key = method + " " + path;
                Asked.Add(body != null ? key + " " + body.ToJson() : key);
                if (!answers.Has(key))
                {
                    if (method == "GET") return Task.FromException<JsonValue>(new ApiError(404, "NOT_FOUND", "no answer for " + key));
                    return Task.FromResult(JsonValue.Object("ok", true));
                }
                JsonValue a = answers[key];
                if (a.IsObject && a.Has("$seq"))
                {
                    used.TryGetValue(key, out int i);
                    used[key] = i + 1;
                    a = a["$seq"][Math.Min(i, a["$seq"].Count - 1)];
                }
                if (a.IsObject && a.Has("$pending")) return new TaskCompletionSource<JsonValue>().Task;
                if (a.IsObject && a.Has("$error")) return Task.FromException<JsonValue>(new ApiError(a["$status"].IsNumber ? (int)a["$status"].Num : 500, a["$code"].Str, a["$error"].Str));
                return Task.FromResult(JsonValue.Parse(a.ToJson()));
            }

            public Task<JsonValue> Get(string path, CancellationToken ct) => Answer("GET", path, null);
            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct) => Answer("POST", path, body);
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => Answer("PUT", path, body);
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => Answer("DELETE", path, body);
            public Task<byte[]> GetBytes(string path, CancellationToken ct) => Task.FromException<byte[]>(new ApiError(404, "NOT_FOUND", "no bytes"));
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => Answer("POST", path, null);
        }

        /// <summary>A Horizon window that records: toasts as { text, tone }, dialogs as their outline and the answer given.</summary>
        public sealed class HzWindow
        {
            public readonly HzUi Ui = new HzUi();
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Dialogs = new List<string>();
            public readonly List<string> Status = new List<string>();
            readonly Queue<bool> confirms = new Queue<bool>();
            readonly bool confirmAll;
            readonly Queue<string> prompts = new Queue<string>();

            public HzWindow(JsonValue confirm, JsonValue prompts = null)
            {
                if (confirm != null && confirm.IsArray) foreach (JsonValue c in confirm.Items) confirms.Enqueue(c.Bool);
                else confirmAll = confirm != null && confirm.Bool;
                bool listed = confirm != null && confirm.IsArray;
                if (prompts != null) foreach (JsonValue p in prompts.Items) this.prompts.Enqueue(p.IsNull ? null : p.Str);
                Ui.Toast = (t, tone) => Toasts.Add(tone + " " + t);
                Ui.Status = s => Status.Add(s);
                Ui.Ask = a =>
                {
                    bool yes = listed ? (confirms.Count > 0 && confirms.Dequeue()) : confirmAll;
                    Dialogs.Add(string.Join(" | ", a.Outline()) + " => " + (yes ? "yes" : "no"));
                    return Task.FromResult(yes);
                };
                Ui.AskText = a =>
                {
                    string v = this.prompts.Count > 0 ? this.prompts.Dequeue() : null;
                    Dialogs.Add(a.Title + " => " + (v == null ? "cancel" : "type " + v));
                    return Task.FromResult(v);
                };
            }

            public static List<string> WebToasts(JsonValue arr) => arr.Items.Select(t => t["tone"].Str + " " + t["text"].Str).ToList();
            public static List<string> WebDialogs(JsonValue arr) => arr.Items.Select(d => d.Has("text") ? string.Join(" | ", d["text"].Items.Select(x => x.Str)) + " => " + d["answer"].Str : d.ToJson()).ToList();
        }

        public static HzCar Car(JsonValue v) => v == null || v.IsNull ? null : new HzCar(Hz.Js(v["id"]), Hz.Js(v["name"]));

        public static List<string> Strings(JsonValue arr) => arr.Items.Select(x => x.Str).ToList();

        /// <summary>The web's requests, without the pack's own GET /api/info at start.</summary>
        public static List<string> Asked(List<string> asked, int from) => asked.Skip(from).Where(a => a != "GET /api/info").ToList();

        public static void Same(List<string> want, List<string> got, string what)
        {
            if (want.SequenceEqual(got)) return;
            int i = 0;
            while (i < want.Count && i < got.Count && want[i] == got[i]) i++;
            string w = string.Join(" | ", want.Skip(Math.Max(0, i - 3)).Take(8));
            string g = string.Join(" | ", got.Skip(Math.Max(0, i - 3)).Take(8));
            throw new AssertFailed(what + ": first difference at token " + i + " (web " + want.Count + ", C# " + got.Count + "):\n      web " + w + "\n      C#  " + g);
        }

        public static void WithUtc(Action run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { run(); } finally { Format.Zone = keep; }
        }

        public static async Task WithUtc(Func<Task> run)
        {
            TimeZoneInfo keep = Format.Zone;
            Format.Zone = TimeZoneInfo.Utc;
            try { await run(); } finally { Format.Zone = keep; }
        }
    }
}
