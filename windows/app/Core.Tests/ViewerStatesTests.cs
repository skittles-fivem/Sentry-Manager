using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's MediaErrors, StudioText, StudioNotice, StudioLoading,
    /// ViewerReads and ViewerFirstLoad (the Viewer's empty, loading and error
    /// states) against Studio's own code on the same inputs
    /// (Fixtures/expected-viewer-states.json, written by
    /// test/windowsNativeViewerStates.test.js).
    /// </summary>
    static class ViewerStatesTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-states.json")));

        static void Eq(string want, string got, string what)
        {
            if (want != got) throw new Exception(what + ":\n  web:    " + want + "\n  native: " + got);
        }

        static double Number(JsonValue v)
        {
            if (v.IsString && v.Str == "NaN") return double.NaN;
            if (v.IsString && v.Str == "Infinity") return double.PositiveInfinity;
            return v.Num;
        }

        static string Opt(JsonValue v) => v.IsString && v.Str == "undefined" ? null : v.IsNull ? null : v.Str;

        static void Media(JsonValue x)
        {
            foreach (JsonValue m in x["media"].Items)
            {
                double? code = m["code"].IsNull ? (double?)null : m["code"].Num;
                Eq(m["text"].Str, MediaErrors.Describe(code, m["message"].Str), "describeMediaError " + m.ToJson());
            }
        }

        static void Words(JsonValue x)
        {
            JsonValue w = x["words"];
            Eq(w["ui.clipBrowser.subtitle"].Str, StudioText.ClipListPlaceholder, "ui.clipBrowser.subtitle");
            Eq(w["ui.notifications.failedToLoadVideo"].Str, StudioText.FailedToLoadVideo, "failedToLoadVideo");
            Eq(w["ui.notifications.errorSelectingDay"].Str, StudioText.ErrorSelectingDay, "errorSelectingDay");
            Eq(w["ui.notifications.noDashcamClipsFound"].Str, StudioText.NoDashcamClipsFound, "noDashcamClipsFound");
            foreach (JsonValue r in x["replaced"].Items)
            {
                Eq(r["failedToLoadVideo"].Str, StudioText.T(StudioText.FailedToLoadVideo, "error", r["error"].Str), "t(failedToLoadVideo) " + r["error"].Str);
                Eq(r["errorSelectingDay"].Str, StudioText.T(StudioText.ErrorSelectingDay, "error", r["error"].Str), "t(errorSelectingDay) " + r["error"].Str);
            }
        }

        static void Notices(JsonValue x)
        {
            foreach (JsonValue n in x["notices"].Items)
            {
                JsonValue o = n["opts"];
                StudioNotice s = new StudioNotice();
                s.Message = "Hello";
                s.Type = o.Has("type") ? o["type"].Str : null;
                if (o.Has("timeoutMs")) s.TimeoutMs = Number(o["timeoutMs"]);
                s.Dismissible = o["dismissible"].Bool;
                if (o.Has("action")) s.ActionLabel = o["action"]["label"].Str;
                Eq(n["className"].Str, "toast toast--" + s.Kind, "notify class " + o.ToJson());
                Eq(Format.JsNum(n["timeoutMs"].Num), Format.JsNum(s.Expires ? s.Life : 0), "notify timeout " + o.ToJson());
                Eq(n["text"].Str, s.Text, "notify text");
                Eq(n["action"].Bool.ToString(), (!string.IsNullOrEmpty(s.ActionLabel)).ToString(), "notify action " + o.ToJson());
                Eq(n["dismiss"].Bool.ToString(), s.Dismissible.ToString(), "notify dismiss " + o.ToJson());
            }
        }

        static void Loading(JsonValue x)
        {
            foreach (JsonValue l in x["loading"].Items)
            {
                StudioLoading m = new StudioLoading();
                int i = 0;
                foreach (JsonValue step in l["steps"].Items)
                {
                    string op = step[0].Str;
                    if (op == "show")
                    {
                        if (step.Count == 1) m.Show();
                        else m.Show(Opt(step[1]), step.Count > 2 ? Opt(step[2]) : "");
                    }
                    if (op == "update") m.Update(Opt(step[1]), Opt(step[2]), step.Count > 3 ? Number(step[3]) : 0);
                    if (op == "hide") m.Hide();
                    JsonValue after = l["after"][i++];
                    string what = "loading " + l["steps"].ToJson() + " step " + i;
                    Eq(after["visible"].Bool.ToString(), m.Visible.ToString(), what + " visible");
                    Eq(after["text"].Str, m.Text, what + " text");
                    Eq(after["progress"].Str, m.Progress, what + " progress");
                    Eq(after["width"].Str, StudioLoading.WidthCss(m.Percent), what + " width");
                }
            }
        }

        /// <summary>An IJsonApi whose readDir answers as the test's server answer (the errors as Api builds them: the body's error, else the status text).</summary>
        sealed class ReadApi : IJsonApi
        {
            readonly JsonValue answer;
            public ReadApi(JsonValue a) { answer = a; }
            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct)
            {
                var t = new TaskCompletionSource<JsonValue>();
                if (answer["network"].Bool) t.SetException(new ApiError(0, "UNREACHABLE", "Can't reach Sentry Six Manager"));
                else
                {
                    int status = (int)answer["status"].Long;
                    JsonValue data = answer["body"].IsObject || answer["body"].IsArray ? answer["body"] : JsonValue.Object();
                    if (status >= 200 && status < 300) t.SetResult(data);
                    else
                    {
                        string msg = data["error"].Str;
                        t.SetException(new ApiError(status, status == 401 ? "SIGNED_OUT" : data["code"].Str, string.IsNullOrEmpty(msg) ? answer["statusText"].Str : msg));
                    }
                }
                return t.Task;
            }
            public Task<JsonValue> Get(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<byte[]> GetBytes(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) { throw new NotSupportedException(); }
        }

        static void Reads(JsonValue x)
        {
            foreach (JsonValue r in x["reads"].Items)
            {
                ViewerReads reads = new ViewerReads();
                JsonValue entries = reads.ReadDir(new ReadApi(r["answer"]), "/f", () => 1000, CancellationToken.None).Result;
                string what = "readDir " + r["answer"].ToJson();
                Eq(r["entries"].ToJson(), entries.ToJson(), what + " entries");
                Eq(r["error"].IsNull ? null : r["error"].Str, reads.Error, what + " error");
                Eq(r["active"].Long.ToString(), reads.Active.ToString(), what + " active");
                Eq(r["at"].Long.ToString(), reads.At.ToString(), what + " at");
            }
        }

        static void FirstLoad()
        {
            var f = new ViewerFirstLoad();
            Assert.True(!f.Enabled, "disabled at first (index.html)");
            f.Begin(0, 0);
            Assert.True(!f.Tick(4999) && !f.Enabled, "still waiting at 4.999 s");
            Assert.True(f.Tick(5000) && f.Enabled, "enabled after the 5 s wait");
            Assert.True(f.Failed(6000, 0, "x") == null, "a failure after the wait says nothing");

            f = new ViewerFirstLoad();
            f.Begin(0, 0);
            Assert.Equal("Failed to load video: the file could not be decoded (corrupt or unsupported codec) (bad)", f.Failed(100, 0, MediaErrors.Describe(3, "bad")), "the first load fails");
            Assert.True(!f.Enabled && !f.Tick(9000), "stays disabled after the failure");
            Assert.True(f.Opened() && f.Enabled, "a later segment's metadata enables them");
            Assert.True(!f.Opened(), "only once");

            // an event's segment fails: segment 0 is tried, with its own wait
            f = new ViewerFirstLoad();
            f.Begin(0, 3);
            Assert.True(f.Failed(1000, 2, "x") == null, "another segment's failure is not the first load's");
            Assert.True(f.Failed(1000, 3, "x") == null && f.Pending, "the event's segment: segment 0 next");
            Assert.True(!f.Tick(5999), "segment 0's own 5 s");
            Assert.Equal("Failed to load video: y", f.Failed(2000, 0, "y"), "segment 0 fails too");
        }

        static void Ffmpeg()
        {
            var a = MediaErrors.FromFfmpeg("[http @ 0x55] HTTP error 404 Not Found\nError opening input file http://127.0.0.1:1/m/abc.\n", false, false);
            Assert.Equal(4, a.Code, "404 before any frame");
            Assert.Equal("HTTP error 404 Not Found", a.Message, "its line, prefix off");
            var b = MediaErrors.FromFfmpeg("[h264 @ 0x7f] error while decoding MB 3 4, bytestream -5\n[h264 @ 0x7f] concealing 10 DC errors\n", true, false);
            Assert.Equal(3, b.Code, "a decoder's complaint");
            Assert.Equal("concealing 10 DC errors", b.Message, "the last line");
            var c = MediaErrors.FromFfmpeg("[tcp @ 0x1] Connection reset by peer\n", true, false);
            Assert.Equal(2, c.Code, "connection lost after frames");
            var d = MediaErrors.FromFfmpeg("[mov,mp4,m4a,3gp,3g2,mj2 @ 0x2] moov atom not found\n", false, false);
            Assert.Equal(4, d.Code, "not a playable file");
            Assert.Equal("moov atom not found", d.Message, "the bracket with commas taken off");
            Assert.Equal(1, MediaErrors.FromFfmpeg("anything", true, true).Code, "stopped");
            Assert.Equal("the file could not be decoded (corrupt or unsupported codec)", MediaErrors.FromFfmpeg("", true, false).Describe(), "nothing said");
            Assert.Equal("Signed out", ViewerReads.Message(new ApiError(401, "SIGNED_OUT", "Sign in first")), "401");
            Assert.Equal("Failed to fetch", ViewerReads.Message(new ApiError(0, "TIMEOUT", "x")), "no answer");
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer states: describeMediaError as utils.js", () => Media(Expected()));
            test("viewer states: the words and t()'s replacement as i18n.js", () => Words(Expected()));
            test("viewer states: toasts' type and time as notifications.js", () => Notices(Expected()));
            test("viewer states: the loading overlay as loadingOverlay.js", () => Loading(Expected()));
            test("viewer states: folder reads and their error as electronApiShim.js", () => Reads(Expected()));
            test("viewer states: the play button's first load and its toast", FirstLoad);
            test("viewer states: an ffmpeg failure as a MediaError", Ffmpeg);
        }
    }
}
