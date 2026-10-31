using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's StudioFrames, ViewerStart, ViewerStatus, DayWish,
    /// ViewerAway and FormQuery (how the native Viewer is hosted and starts)
    /// against what web/app.js and web/electronApiShim.js do on the same
    /// steps (Fixtures/expected-viewer-host.json, written by
    /// test/windowsNativeViewerHost.test.js).
    /// </summary>
    static class ViewerHostTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-host.json")));

        static string Norm(JsonValue v) => JsonValue.Parse(v.ToJson()).ToJson();

        static void Same(JsonValue want, JsonValue got, string what)
        {
            string a = Norm(want), b = Norm(got);
            if (a != b) throw new Exception(what + ":\n  web:    " + a + "\n  native: " + b);
        }

        static List<KeyValuePair<string, string>> Pairs(JsonValue o)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var kv in o.Props) list.Add(new KeyValuePair<string, string>(kv.Key, StudioFrames.JsString(kv.Value)));
            return list;
        }

        /// <summary>The shell's side, as the web test's pretend shell: render(), openStudio, the listener, the pills and a resize.</summary>
        sealed class Shell
        {
            public string Origin;
            public JsonValue Vehicles = JsonValue.Array(new JsonValue[0]);
            public JsonValue VehicleId = JsonValue.Null;
            public JsonValue Stored = JsonValue.Null;
            public string Hash = "#/overview";
            public int Offset = 3, Selected = 1;
            public string Category = "events";
            public double Width = 1352, Height = 900;
            public readonly StudioFrames Frames = new StudioFrames();
            public readonly Dictionary<string, JsonValue> Styles = new Dictionary<string, JsonValue>();
            public readonly List<StudioPost> Posts = new List<StudioPost>();

            static readonly JsonValue NoStyle = JsonValue.Object("width", "", "height", "", "transform", "");

            JsonValue Current()
            {
                foreach (JsonValue v in Vehicles.Items) if (v.Has("id") && StudioFrames.StrictEquals(v["id"], VehicleId)) return v;
                return Vehicles.Count > 0 ? Vehicles[0] : null;
            }

            string Saved() => VehicleId.Bool ? StudioFrames.JsString(VehicleId) : null;

            void ShowFrame(string kind)
            {
                if (Frames.Show(kind, Current(), Saved(), Posts)) Styles[kind] = NoStyle;
                Fit();
            }

            public void Fit()
            {
                StudioFrame f = Frames.Frame("viewer");
                if (f == null || f.Hidden) return;
                StudioFit fit = StudioFrames.Fit(Width, Height);
                Styles["viewer"] = fit.Scaled
                    ? JsonValue.Object("width", Format.JsNum(fit.Width) + "px", "height", Format.JsNum(fit.Height) + "px", "transform", "scale(" + Format.JsNum(fit.Scale) + ")")
                    : NoStyle;
            }

            public void Render()
            {
                string h = Hash.StartsWith("#/") ? Hash.Substring(2) : Hash;
                string[] parts = h.Split('?')[0].Split('/').Where(x => x.Length > 0).ToArray();
                string route = parts.Length > 0 ? parts[0] : null;
                string rest = string.Join("/", parts.Skip(1));
                if (!StudioFrames.Embedded(route, rest)) Frames.HideAll(Posts);
                if (route == "viewer") ShowFrame("viewer");
                if (route == "encoder")
                {
                    if (rest.Length == 0) ShowFrame("encoder");
                    else Frames.HideAll(Posts);
                }
            }

            public void Open(string kind, List<KeyValuePair<string, string>> extra)
            {
                Frames.Open(kind, StudioFrames.Url(kind, Current(), Saved(), extra));
                Hash = "#/" + kind;
                Render();
            }

            public void Message(JsonValue data, string origin)
            {
                var ids = Vehicles.Items.Select(v => v.Has("id") ? v["id"] : null).ToList();
                switch (StudioFrames.Message(data, origin == Origin, ids))
                {
                    case StudioAsk.ShowInFolder:
                        Category = "all";
                        Hash = "#/footage";
                        Render();
                        break;
                    case StudioAsk.SelectVehicle:
                        VehicleId = data["id"];
                        Stored = data["id"];
                        Offset = 0;
                        Selected = 0;
                        Open("viewer", null);
                        break;
                }
            }

            public JsonValue Snapshot()
            {
                var frames = Frames.All.Select(f => JsonValue.Object("kind", f.Kind, "src", f.Src, "vehicle", f.Vehicle, "hidden", f.Hidden, "style", Styles[f.Kind])).ToList();
                var posts = Posts.Select(p => JsonValue.Object("kind", p.Kind, "type", p.Type)).ToList();
                Posts.Clear();
                return JsonValue.Object("frames", JsonValue.Array(frames), "posts", JsonValue.Array(posts), "hash", Hash, "vehicleId", VehicleId, "stored", Stored,
                    "footage", JsonValue.Object("offset", Offset, "category", Category, "selected", Selected));
            }
        }

        static void Frames(JsonValue x)
        {
            string origin = x["origin"].Str;
            foreach (var session in x["frames"].Props)
            {
                var s = new Shell { Origin = origin };
                JsonValue steps = session.Value["steps"], after = session.Value["after"];
                for (int i = 0; i < steps.Count; i++)
                {
                    JsonValue st = steps[i];
                    switch (st["do"].Str)
                    {
                        case "vehicles": s.Vehicles = st["list"]; s.VehicleId = st["id"]; break;
                        case "pick": s.VehicleId = st["id"]; s.Stored = st["id"]; break;
                        case "route": s.Hash = st["hash"].Str; s.Render(); break;
                        case "open": s.Open(st["kind"].Str, Pairs(st["extra"])); break;
                        case "message": s.Message(st["data"], st["origin"].Str ?? origin); break;
                        case "size": s.Width = st["width"].Num; s.Height = st["height"].Num; s.Fit(); break;
                    }
                    Same(after[i], s.Snapshot(), session.Key + " step " + (i + 1) + " (" + st.ToJson() + ")");
                }
            }
        }

        static void Urls(JsonValue x)
        {
            foreach (JsonValue u in x["urls"].Items)
            {
                ViewerStart s = ViewerStart.Parse(u["url"].Str);
                JsonValue got = JsonValue.Object("url", u["url"], "embed", s.Embed, "startFolder", s.StartFolder, "root", s.Root, "folder", s.Folder, "vehicle", s.VehicleId, "date", s.Date);
                Same(u, got, "address " + u["url"].Str);
            }
            foreach (JsonValue e in x["encode"].Items)
            {
                var pairs = e["pairs"].Items.Select(p => new KeyValuePair<string, string>(p[0].Str, p[1].Str)).ToList();
                Assert.Equal(e["query"].Str, FormQuery.Encode(pairs), "URLSearchParams " + e["pairs"].ToJson());
            }
            foreach (JsonValue p in x["parse"].Items)
            {
                var got = JsonValue.Array(FormQuery.Parse(p["query"].Str).Select(kv => JsonValue.Array(new[] { JsonValue.String(kv.Key), JsonValue.String(kv.Value) })));
                Same(p["pairs"], got, "parse " + p["query"].Str);
            }
        }

        sealed class Timer { public int Id; public long Due; public long Every; public Action Fn; }

        /// <summary>The shim's viewer part on the session's steps, with the same fake clock as the web test.</summary>
        static JsonValue Shim(JsonValue sc, string origin)
        {
            long now = 1000;
            var log = new List<JsonValue>();
            var timers = new List<Timer>();
            int seq = 0;
            Action<Action, long, bool> set = (fn, ms, every) => timers.Add(new Timer { Id = ++seq, Fn = fn, Due = now + Math.Max(0, ms), Every = every ? Math.Max(1, ms) : 0 });
            bool playing = sc["playing"].Bool, btnDisabled = sc["btnDisabled"].Bool, overlayHidden = false;
            int active = 0;
            long readsAt = 0;
            string readError = null;
            List<string> days = null;
            string dayValue = "";
            var away = new ViewerAway();

            ViewerStart start = ViewerStart.Parse(sc["url"].Str);
            JsonValue vehicles = sc["vehicles"];
            Action<string> sayText = t => log.Add(JsonValue.Object("at", now, "say", t));
            Action click = () => { log.Add(JsonValue.Object("at", now, "click", "playBtn")); playing = false; };
            if (start.StartFolder == null) sayText(ViewerStart.NoVehicle);
            else
            {
                string name = vehicles.Items.Where(v => v["id"].IsString && v["id"].Str == start.VehicleId).Select(v => v["name"].Str).FirstOrDefault();
                sayText(ViewerStart.Opening(name));
                if (ViewerStart.ShowPicker(vehicles.Count))
                {
                    var opts = vehicles.Items.Select(v => JsonValue.Object("value", v["id"].Str, "text", v["name"].Str, "selected", v["id"].Str == start.VehicleId)).ToList();
                    log.Add(JsonValue.Object("at", now, "picker", JsonValue.Array(opts), "label", "Vehicle"));
                }
                if (!string.IsNullOrEmpty(start.Date))
                {
                    var wish = new DayWish(start.Date, now);
                    Timer t = null;
                    set(() =>
                    {
                        string pick = wish.Tick(now, days, dayValue);
                        if (pick != null) { dayValue = pick; log.Add(JsonValue.Object("at", now, "dayPicked", dayValue, "event", "change")); }
                        if (wish.Done) timers.Remove(t);
                    }, DayWish.EveryMs, true);
                    t = timers[timers.Count - 1];
                }
                var status = new ViewerStatus(now);
                Timer st = null;
                set(() =>
                {
                    string text = status.Tick(now, overlayHidden, active, readsAt, readError, start.StartFolder);
                    if (text != null)
                    {
                        sayText(text);
                        log.Add(JsonValue.Object("at", now, "button", ViewerStart.TryAgain, "cls", "mini-btn sm-retry"));
                    }
                    if (status.Done) timers.Remove(st);
                }, ViewerStatus.EveryMs, true);
                st = timers[timers.Count - 1];
            }
            log.Add(JsonValue.Object("at", now, "embed", start.Embed));
            Action Pause = () => { if (playing && !btnDisabled) click(); };
            var events = sc["events"].Items.OrderBy(e => e["at"].Long).ToList();
            long until = sc.Has("until") ? sc["until"].Long : 6000;
            while (now <= until)
            {
                foreach (JsonValue e in events.Where(e => e["at"].Long == now))
                {
                    if (e["read"].Str == "start") active++;
                    if (e["read"].Str == "end") { if (e.Has("error")) readError = e["error"].Str; active--; readsAt = now; }
                    if (e.Has("overlayHidden")) overlayHidden = e["overlayHidden"].Bool;
                    if (e.Has("days")) { days = e["days"].Items.Select(d => d.Str).ToList(); dayValue = e["value"].Str ?? ""; }
                    if (e.Has("pick")) log.Add(JsonValue.Object("at", now, "toShell", JsonValue.Object("source", "sentry-studio", "type", "select-vehicle", "id", e["pick"].Str), "origin", origin));
                    if (e.Has("msg") && e["source"].Str != "other" && (e["origin"].Str ?? origin) == origin)
                    {
                        if (e["msg"].Str == "pause") { if (away.Pause(playing, !btnDisabled)) click(); }
                        else if (e["msg"].Str == "shown") away.Shown();
                    }
                    if (e.Has("playing")) playing = e["playing"].Bool;
                    if (e.Has("btnDisabled")) btnDisabled = e["btnDisabled"].Bool;
                    if (e["play"].Bool) { playing = true; if (away.Played()) set(Pause, 0, false); }
                }
                for (;;)
                {
                    Timer due = timers.Where(t => t.Due <= now).OrderBy(t => t.Due).ThenBy(t => t.Id).FirstOrDefault();
                    if (due == null) break;
                    if (due.Every > 0) due.Due += due.Every; else timers.Remove(due);
                    due.Fn();
                }
                now += 100;
            }
            return JsonValue.Array(log);
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer host: frames, pending opens, pause and shown, fit and messages as web/app.js", () => Frames(Expected()));
            test("viewer host: the Viewer's address and URLSearchParams as the browser's", () => Urls(Expected()));
            test("viewer host: the empty screen, picker, Play day and pause as electronApiShim.js", () =>
            {
                JsonValue x = Expected();
                foreach (var kv in x["shim"].Props) Same(kv.Value["log"], Shim(kv.Value["session"], x["origin"].Str), "shim " + kv.Key);
            });
        }
    }
}
