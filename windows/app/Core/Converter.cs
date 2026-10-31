using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>The Encoder's answer {ok:false, error:{code}} (web/vehicleEncoding.js invoke() throws Error(code) with .code).</summary>
    public class ConverterError : Exception
    {
        public ConverterError(string code) : base(string.IsNullOrEmpty(code) ? "FAILED" : code)
        {
            Code = string.IsNullOrEmpty(code) ? "FAILED" : code;
        }

        public string Code { get; private set; }
    }

    /// <summary>
    /// The Encoder's calls through the server (POST /api/converter/invoke/&lt;name&gt;),
    /// as web/vehicleEncoding.js makes them: HTTP 200 with { ok: true, value }
    /// or { ok: false, error: { code } }.
    /// </summary>
    public static class Converter
    {
        /// <summary>web/vehicleEncoding.js ERRORS: what a code means, in the page's words.</summary>
        public static readonly Dictionary<string, string> Errors = new Dictionary<string, string>
        {
            { "REPLACEMENT_CONFIRMATION_REQUIRED", "Tick the box to confirm that the originals may be replaced." },
            { "REPLACEMENT_UNAVAILABLE", "This share can’t replace originals (it is read-only or does not allow it). Choose a separate folder." },
            { "PATH_NOT_ALLOWED", "That folder is outside the vehicle shares and output folders." },
            { "PATH_OVERLAP", "The separate folder can’t be inside the footage (or the footage inside it)." },
            { "INVALID_PATH", "That folder can’t be used." },
            { "INVALID_BITRATE", "Enter a bitrate from 0.25 to 100 Mbps." },
            { "INVALID_GROUP_SELECTION", "Pick at least one kind of clip." },
            { "INVALID_CONCURRENCY", "Clips at once: a number from 1 to 64." },
            { "INVALID_LANES", "The saved devices are not valid any more: set them again in the Encoder." },
            { "INVALID_AUTO_WINDOW", "The time window needs a start and an end that differ." },
            { "SOURCE_UNAVAILABLE", "The vehicle’s footage can’t be reached right now (is the share connected?)." },
            { "AUTO_UNAVAILABLE", "The Encoder is still starting. Try again in a moment." },
            { "CONVERTER_STARTING", "The Encoder is still starting. Try again in a moment." },
            { "TOO_MANY_AUTO_SOURCES", "Too many vehicles have saved settings." }
        };

        public const string DidNotWork = "That didn’t work.";

        /// <summary>web/vehicleEncoding.js errorText(err): the code's words, else the message, else "That didn’t work.".</summary>
        public static string ErrorText(Exception err)
        {
            if (err == null) return DidNotWork;
            string code = null;
            ConverterError ce = err as ConverterError;
            if (ce != null) code = ce.Code;
            ApiError ae = err as ApiError;
            if (ae != null) code = ae.Code;
            string text;
            if (code != null && Errors.TryGetValue(code, out text)) return text;
            return string.IsNullOrEmpty(err.Message) ? DidNotWork : err.Message;
        }

        public static Task<JsonValue> Invoke(IJsonApi api, string name, JsonValue payload)
        {
            return Invoke(api, name, payload, CancellationToken.None);
        }

        /// <summary>The call's value; ConverterError when the Encoder says no, ApiError when the server does.</summary>
        public static async Task<JsonValue> Invoke(IJsonApi api, string name, JsonValue payload, CancellationToken ct)
        {
            if (api == null) throw new ArgumentNullException("api");
            JsonValue data = await api.Post("/api/converter/invoke/" + Uri.EscapeDataString(name ?? ""), payload, ct).ConfigureAwait(false);
            if (data == null || !data["ok"].Bool) throw new ConverterError(data == null ? null : data["error"]["code"].Str);
            return data["value"];
        }
    }
}
