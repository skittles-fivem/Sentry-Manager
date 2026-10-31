using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core
{
    /// <summary>
    /// The shim's folder reads (web/electronApiShim.js `reads` and
    /// electronAPI.readDir): POST /api/studio/readDir counted while it runs;
    /// when it ends, the time it ended; a failure kept as the last error (its
    /// message: the server's words, else the status text) and read as no
    /// entries. The Viewer's status line (<see cref="ViewerStatus"/>) waits
    /// for these to go quiet and then says why nothing is shown.
    /// </summary>
    public sealed class ViewerReads
    {
        public const string Path = "/api/studio/readDir";
        /// <summary>fetch()'s TypeError when the server cannot be reached (the browser's words).</summary>
        public const string FetchFailed = "Failed to fetch";
        /// <summary>The shim's post() on a 401 (it also goes to the sign-in page; the app signs in again).</summary>
        public const string SignedOut = "Signed out";

        readonly object gate = new object();
        int active;
        long at;
        string error;

        /// <summary>Reads running now (reads.active).</summary>
        public int Active { get { lock (gate) return active; } }

        /// <summary>When the last read ended, ms since 1970 (reads.at; 0: none has).</summary>
        public long At { get { lock (gate) return at; } }

        /// <summary>The last failed read's message (reads.error.message; null: none failed).</summary>
        public string Error { get { lock (gate) return error; } }

        /// <summary>The start of a read (reads.active++).</summary>
        public void Begin()
        {
            lock (gate) active++;
        }

        /// <summary>The end of a read (.catch: reads.error = err; .finally: reads.active--, reads.at = Date.now()).</summary>
        public void End(long nowMs, Exception failure)
        {
            lock (gate)
            {
                if (failure != null) error = Message(failure);
                active--;
                at = nowMs;
            }
        }

        /// <summary>electronAPI.readDir(dirPath): the entries, or [] when it failed.</summary>
        public async Task<JsonValue> ReadDir(IJsonApi api, string dirPath, Func<long> nowMs, CancellationToken ct)
        {
            Begin();
            JsonValue result = null;
            Exception failure = null;
            try
            {
                result = await api.Post(Path, dirPath == null ? JsonValue.Object() : JsonValue.Object("path", dirPath), ct);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            // (C# 5: no await in a finally; nothing above can skip this)
            End(nowMs(), failure);
            if (failure != null) return JsonValue.Array(new JsonValue[0]);
            return result;
        }

        /// <summary>
        /// The message the shim's post() rejects with: the server's error text,
        /// else the status text; 401: "Signed out"; no answer at all (fetch
        /// rejects): "Failed to fetch".
        /// </summary>
        public static string Message(Exception ex)
        {
            ApiError e = ex as ApiError;
            if (e != null)
            {
                if (e.Code == "SIGNED_OUT" || e.Status == 401) return SignedOut;
                if (e.Status == 0) return FetchFailed;
                return e.Message;
            }
            if (ex is OperationCanceledException) return "The user aborted a request.";
            return ex.Message;
        }
    }

    /// <summary>
    /// src/renderer/scripts/lib/utils.js describeMediaError (MEDIA_ERROR_TEXT)
    /// and what the native decoders report in its place: a camera's ffmpeg
    /// that fails gives a MediaError code as Chromium would for the same
    /// failure, with ffmpeg's own line as the detail Chromium adds.
    /// </summary>
    public static class MediaErrors
    {
        public const int Aborted = 1, Network = 2, Decode = 3, SrcNotSupported = 4;
        public const string NoDetails = "the video element reported an error with no details";

        static readonly Dictionary<int, string> text = new Dictionary<int, string>
        {
            { 1, "playback aborted" },
            { 2, "network error while loading the file" },
            { 3, "the file could not be decoded (corrupt or unsupported codec)" },
            { 4, "the file is missing or its format is not supported" }
        };

        /// <summary>The words for a MediaError code (MEDIA_ERROR_TEXT[code] || `media error code ${code}`).</summary>
        public static string Reason(double code)
        {
            string t;
            if (code == Math.Floor(code) && code >= int.MinValue && code <= int.MaxValue && text.TryGetValue((int)code, out t)) return t;
            return "media error code " + Format.JsNum(code);
        }

        /// <summary>describeMediaError(el): no error: its fixed words; else the reason, with the message in brackets when there is one.</summary>
        public static string Describe(double? code, string message)
        {
            if (code == null) return NoDetails;
            string reason = Reason(code.Value);
            return string.IsNullOrEmpty(message) ? reason : reason + " (" + message + ")";
        }

        /// <summary>A camera's failure as a MediaError: (code, the line that says why).</summary>
        public sealed class Fault
        {
            public int Code;
            public string Message;

            public string Describe()
            {
                return MediaErrors.Describe(Code, Message);
            }
        }

        /// <summary>
        /// What a failed ffmpeg said, as the MediaError Chromium gives for it:
        /// the file not there, refused, unreadable as a container or without
        /// a stream it can play: 4 (MEDIA_ERR_SRC_NOT_SUPPORTED, Chromium's
        /// DEMUXER_ERROR_COULD_NOT_OPEN / NO_SUPPORTED_STREAMS); the
        /// connection lost after frames came: 2 (MEDIA_ERR_NETWORK); stopped
        /// on purpose: 1; anything else, a decoder's complaint included: 3
        /// (PIPELINE_ERROR_DECODE). The detail is ffmpeg's line that says it
        /// (its "[mov,mp4,... @ 0x…]" prefix taken off), else its last line.
        /// </summary>
        public static Fault FromFfmpeg(string stderr, bool framesSeen, bool stopped)
        {
            Fault f = new Fault();
            List<string> lines = new List<string>();
            foreach (string raw in (stderr ?? "").Split('\n'))
            {
                string l = Strip(raw.Trim());
                if (l.Length > 0) lines.Add(l);
            }
            if (stopped) { f.Code = Aborted; f.Message = null; return f; }
            string[] missing = { "404", "403", "410", "No such file", "Server returned", "HTTP error", "Connection refused", "Failed to resolve", "moov atom not found", "Invalid data found when processing input", "does not contain any stream", "Decoder (codec", "not found", "Unsupported codec", "Unknown decoder" };
            string[] network = { "Connection reset", "Connection timed out", "timed out", "I/O error", "Broken pipe", "End of file", "Stream ends prematurely" };
            string hit;
            if (framesSeen && (hit = First(lines, network)) != null) { f.Code = Network; f.Message = Cut(hit); return f; }
            if (!framesSeen && (hit = First(lines, missing)) != null) { f.Code = SrcNotSupported; f.Message = Cut(hit); return f; }
            if (!framesSeen && (hit = First(lines, network)) != null) { f.Code = SrcNotSupported; f.Message = Cut(hit); return f; }
            f.Code = Decode;
            f.Message = lines.Count > 0 ? Cut(lines[lines.Count - 1]) : null;
            return f;
        }

        static string First(List<string> lines, string[] needles)
        {
            foreach (string l in lines)
                foreach (string n in needles)
                    if (l.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return l;
            return null;
        }

        /// <summary>"[h264 @ 0x55d…] error while decoding MB 1 2" -> "error while decoding MB 1 2".</summary>
        static string Strip(string l)
        {
            while (l.StartsWith("[", StringComparison.Ordinal))
            {
                int end = l.IndexOf("] ", StringComparison.Ordinal);
                if (end < 0 || l.IndexOf(" @ ", StringComparison.Ordinal) < 0 || l.IndexOf(" @ ", StringComparison.Ordinal) > end) break;
                l = l.Substring(end + 2).TrimStart();
            }
            return l;
        }

        static string Cut(string l)
        {
            return l.Length > 200 ? l.Substring(0, 200) : l;
        }
    }

    /// <summary>
    /// The Studio words this part of the Viewer shows (English, as the web's
    /// translations.js `en`), and i18n.js t()'s {param} replacement, which is
    /// String.prototype.replace with a string: "$&amp;", "$`", "$'" and "$$" in
    /// the value are replacement patterns there too.
    /// </summary>
    public static class StudioText
    {
        /// <summary>ui.clipBrowser.subtitle: the clip list with no day.</summary>
        public const string ClipListPlaceholder = "Select a dashcam folder to browse";
        /// <summary>ui.notifications.failedToLoadVideo.</summary>
        public const string FailedToLoadVideo = "Failed to load video: {error}";
        /// <summary>ui.notifications.errorSelectingDay.</summary>
        public const string ErrorSelectingDay = "Error selecting day: {error}";
        /// <summary>ui.notifications.noDashcamClipsFound (traverseDirectoryElectron when no day was found).</summary>
        public const string NoDashcamClipsFound = "No video clips found. Select a folder containing video files (.mp4, .avi, .mov, .mkv) or a Tesla dashcam folder with RecentClips, SentryClips, or SavedClips.";

        /// <summary>loadDefaultFolderOnStartup: showLoading('Loading default folder...', 'Looking for dashcam clips').</summary>
        public const string LoadingDefault = "Loading default folder...";
        public const string LookingForClips = "Looking for dashcam clips";

        /// <summary>t(key, params): each {name} replaced (every one) as JavaScript's replace does with a string.</summary>
        public static string T(string template, IList<KeyValuePair<string, string>> args)
        {
            string result = template ?? "";
            if (args == null) return result;
            foreach (KeyValuePair<string, string> kv in args) result = ReplaceAll(result, "{" + kv.Key + "}", kv.Value ?? "undefined");
            return result;
        }

        public static string T(string template, string name, string value)
        {
            return T(template, new[] { new KeyValuePair<string, string>(name, value) });
        }

        /// <summary>s.replace(/needle/g, value): every match, with the value's $ patterns ($$ $&amp; $` $') expanded (no groups: $1 stays).</summary>
        public static string ReplaceAll(string s, string needle, string value)
        {
            if (needle.Length == 0) return s;
            StringBuilder b = new StringBuilder();
            int from = 0;
            for (;;)
            {
                int i = s.IndexOf(needle, from, StringComparison.Ordinal);
                if (i < 0) break;
                b.Append(s, from, i - from);
                Expand(b, value, s, i, needle.Length);
                from = i + needle.Length;
            }
            b.Append(s, from, s.Length - from);
            return b.ToString();
        }

        static void Expand(StringBuilder b, string value, string s, int at, int len)
        {
            for (int k = 0; k < value.Length; k++)
            {
                char c = value[k];
                if (c != '$' || k + 1 >= value.Length) { b.Append(c); continue; }
                char n = value[k + 1];
                if (n == '$') { b.Append('$'); k++; }
                else if (n == '&') { b.Append(s, at, len); k++; }
                else if (n == '`') { b.Append(s, 0, at); k++; }
                else if (n == '\'') { b.Append(s, at + len, s.Length - at - len); k++; }
                else b.Append(c);
            }
        }
    }

    /// <summary>
    /// ui/notifications.js notify(message, opts): the type (opts.type ||
    /// 'info') and how long it stays (a finite opts.timeoutMs, else 5.5 s for
    /// an error and 3.2 s for the rest; 0 or less: until dismissed).
    /// </summary>
    public sealed class StudioNotice
    {
        public const int ShowMs = 150, RemoveMs = 180;

        public string Message;
        /// <summary>'info' | 'success' | 'warn' | 'error' (any other is drawn as the plain toast).</summary>
        public string Type = "info";
        /// <summary>null: the default for its type.</summary>
        public double? TimeoutMs;
        public bool Dismissible;
        public string ActionLabel;
        public Action Action;

        public static StudioNotice Of(string message, string type)
        {
            StudioNotice n = new StudioNotice();
            n.Message = message;
            n.Type = type;
            return n;
        }

        /// <summary>opts.type || 'info'.</summary>
        public string Kind { get { return string.IsNullOrEmpty(Type) ? "info" : Type; } }

        /// <summary>The time it stays, ms (0 or less: it stays).</summary>
        public double Life
        {
            get
            {
                if (TimeoutMs.HasValue && !double.IsNaN(TimeoutMs.Value) && !double.IsInfinity(TimeoutMs.Value)) return TimeoutMs.Value;
                return Kind == "error" ? 5500 : 3200;
            }
        }

        /// <summary>It goes by itself (timeoutMs &gt; 0).</summary>
        public bool Expires { get { return Life > 0; } }

        /// <summary>The text shown (String(message || '')).</summary>
        public string Text { get { return Message ?? ""; } }
    }

    /// <summary>
    /// ui/loadingOverlay.js: #loadingOverlay shown with its words and the bar
    /// at 0 (showLoading), its words and bar moved (updateLoading: the width
    /// `${Math.min(100, Math.max(0, percent))}%`, which the browser ignores
    /// when it is NaN%), hidden (hideLoading). index.html starts it hidden
    /// with "Scanning folder..." and "0 files found".
    /// </summary>
    public sealed class StudioLoading
    {
        public const string DefaultText = "Scanning folder...";

        public bool Visible { get; private set; }
        public string Text { get; private set; }
        public string Progress { get; private set; }
        /// <summary>The bar's width, 0-100 (%).</summary>
        public double Percent { get; private set; }
        /// <summary>Bumped on every change (the overlay draws again).</summary>
        public int Version { get; private set; }

        public StudioLoading()
        {
            Text = DefaultText;
            Progress = "0 files found";
        }

        public void Show(string text, string progress)
        {
            Visible = true;
            Text = text ?? "";
            Progress = progress ?? "";
            Percent = 0;
            Version++;
        }

        public void Show()
        {
            Show(DefaultText, "");
        }

        public void Update(string text, string progress, double percent)
        {
            Text = text ?? "";
            Progress = progress ?? "";
            double p = Math.Min(100, Math.Max(0, percent));
            if (!double.IsNaN(p)) Percent = p;
            Version++;
        }

        public void Hide()
        {
            Visible = false;
            Version++;
        }

        /// <summary>The style.width the web sets for that percent ("NaN%" for NaN), as the parity test reads it.</summary>
        public static string WidthCss(double percent)
        {
            return Format.JsNum(Math.Min(100, Math.Max(0, percent))) + "%";
        }
    }

    /// <summary>
    /// #playBtn and #progressBar start disabled (index.html) and are enabled
    /// once a collection's first segment has loaded: its master video can
    /// play (canplay), its metadata came (loadedmetadata), or 5 s passed
    /// without either (loadNativeSegment goes on anyway). When that first
    /// load fails, a toast says "Failed to load video: &lt;why&gt;" (an
    /// event's own segment that fails is retried from the start first);
    /// segments that fail later are skipped without a word.
    /// </summary>
    public sealed class ViewerFirstLoad
    {
        public const long WaitMs = 5000;

        bool pending;
        long since;
        int segment;

        /// <summary>The play button and the progress bar can be used.</summary>
        public bool Enabled { get; private set; }

        /// <summary>A collection was picked: its first segment (the event's, or 0) is loading.</summary>
        public void Begin(long nowMs, int firstSegment)
        {
            pending = true;
            since = nowMs;
            segment = firstSegment;
        }

        /// <summary>The master camera of a segment can play (any segment: loadedmetadata enables them too). True when that enabled them.</summary>
        public bool Opened()
        {
            pending = false;
            if (Enabled) return false;
            Enabled = true;
            return true;
        }

        /// <summary>A timer tick: true when the 5 s wait just ran out (the controls enabled).</summary>
        public bool Tick(long nowMs)
        {
            if (!pending || nowMs - since < WaitMs) return false;
            pending = false;
            if (Enabled) return false;
            Enabled = true;
            return true;
        }

        /// <summary>A segment failed: the toast's words when it was the first load, else null.</summary>
        public string Failed(long nowMs, int index, string why)
        {
            if (!pending || index != segment) return null;
            if (segment > 0)
            {
                // (the event's segment: the web loads segment 0 instead)
                segment = 0;
                since = nowMs;
                return null;
            }
            pending = false;
            return StudioText.T(StudioText.FailedToLoadVideo, "error", why);
        }

        /// <summary>Still waiting on the first load.</summary>
        public bool Pending { get { return pending; } }
    }
}
