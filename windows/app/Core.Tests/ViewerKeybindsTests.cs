using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's shortcuts (ViewerKeybinds.cs: Keybinds, KeybindActions,
    /// ExportMarkers, KeyCodes) against Studio's own lib/keybinds.js, script.js's
    /// keybind actions and features/exportVideo.js's export markers on the same
    /// keys, buttons and steps (Fixtures/expected-viewer-keybinds.json, written by
    /// test/windowsNativeViewerKeybinds.test.js).
    /// </summary>
    static class ViewerKeybindsTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-keybinds.json")));

        static void Same(string want, string got, string what)
        {
            if (want == got) return;
            int i = 0;
            while (i < want.Length && i < got.Length && want[i] == got[i]) i++;
            int from = Math.Max(0, i - 120);
            throw new AssertFailed(what + ": first difference at " + i + "\n  web:    …" + want.Substring(from, Math.Min(want.Length - from, 300)) + "\n  native: …" + got.Substring(from, Math.Min(got.Length - from, 300)));
        }

        static KeyPress Press(JsonValue e)
        {
            KeyPress p = new KeyPress();
            p.Ctrl = e["ctrl"].Bool;
            p.Alt = e["alt"].Bool;
            p.Shift = e["shift"].Bool;
            p.Meta = e["meta"].Bool;
            if (e["kind"].Str == "mouse") p.Button = (int)e["button"].Num;
            else { p.Key = e["key"].Str; p.Code = e["code"].Str; }
            return p;
        }

        sealed class Host : IViewerSettingsHost
        {
            public bool DashboardEnabled => true;
            public bool MapEnabled => true;
            public bool UseMetric => false;
            public string LayoutStyle => "modern";
            public string MapProvider => MapProviders.Get(null).Id;
            public bool MapLabels => MapProviders.DefaultLabelsEnabled;
            public void Call(JsonValue call) { }
            public bool Confirm(string text) => false;
        }

        static void Rows(JsonValue x)
        {
            JsonValue rows = x["rows"];
            List<SettingsItem> mine = SettingsLayout.All().Where(i => i.Kind == SettingsItem.Keybind && i.Action != null).ToList();
            Assert.Equal(rows.Count, mine.Count, "keybind rows");
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.Equal(rows[i]["id"].Str, mine[i].Key, "row " + i + " id");
                Assert.Equal(rows[i]["action"].Str, mine[i].Action, "row " + i + " action");
            }
            Assert.Equal(string.Join(",", rows.Items.Select(r => r["action"].Str).OrderBy(a => a, StringComparer.Ordinal)),
                string.Join(",", KeybindActions.All.OrderBy(a => a, StringComparer.Ordinal)), "the actions script.js gives");
        }

        static string Values(ViewerSettingsModal m, JsonValue rows) =>
            JsonValue.Object(rows.Items.SelectMany(r => new object[] { r["id"].Str, m.Value(r["id"].Str) }).ToArray()).ToJson();

        static string Recording(ViewerSettingsModal m) =>
            JsonValue.Array(m.Recording == null ? new JsonValue[0] : new[] { JsonValue.String(m.Recording) }).ToJson();

        static void Record(JsonValue x)
        {
            JsonValue rows = x["rows"];
            ViewerSettingsModal m = new ViewerSettingsModal(new Host());
            m.Load(JsonValue.Object("keybinds", JsonValue.Object()), JsonValue.Object(), "/srv/footage/TeslaCam", new[] { "en-US" });
            string focused = null;
            int n = 0;
            foreach (JsonValue s in x["record"].Items)
            {
                JsonValue e = s["event"];
                string what = "record step " + n++ + " " + e.ToJson();
                string kind = e["kind"].Str;
                if (kind == "clear") m.Click(".keybind-clear[data-action=\"" + e["action"].Str + "\"]");
                else if (kind == "blur")
                {
                    string id = e["input"].Str;
                    m.Focus(id);
                    m.Blur(id);
                }
                else
                {
                    string id = s["input"].Str;
                    if (focused != id) { m.Focus(id); focused = id; }
                    string action = SettingsLayout.Find(id).Action;
                    string rec = m.Recording == id ? action : null;
                    KeyPress p = Press(e);
                    KeyOutcome o = kind == "key"
                        ? Keybinds.OnKey(p, rec, true, true, m.Keybinds, KeybindActions.All)
                        : Keybinds.OnMouse(p, rec, true, true, m.Keybinds, KeybindActions.All);
                    if (o.Kind == KeyOutcomes.Record) { m.Bind(o.Action, o.Display, o.Identifier); focused = null; }
                    Assert.Equal(s["prevented"].Bool, o.Prevent, what + ": preventDefault");
                }
                Same(s["recording"].ToJson(), Recording(m), what + ": recording");
                Same(s["keybinds"].ToJson(), m.Keybinds.ToJson(), what + ": the saved keybinds");
                Same(s["values"].ToJson(), Values(m, rows), what + ": the inputs");
            }
        }

        static readonly Dictionary<string, string[]> Contexts = new Dictionary<string, string[]>
        {
            { "none", null }, { "text", new[] { "input", "text" } }, { "range", new[] { "input", "range" } },
            { "checkbox", new[] { "input", "checkbox" } }, { "textarea", new[] { "textarea", "" } }, { "button", new[] { "button", "" } },
            { "select", new[] { "select", "" } }, { "editable", new[] { "div", "", "editable" } }, { "modal", null }
        };

        static void Dispatch(JsonValue x, JsonValue bound)
        {
            foreach (JsonValue ses in x["dispatch"].Items)
            {
                string ctx = ses["context"].Str;
                string[] f = Contexts[ctx];
                bool text = f != null && Keybinds.TextEntry(f[0], f[1], f.Length > 2);
                bool modal = ctx == "modal";
                string sv = ses["saved"].Str;
                JsonValue saved = sv == "bound" ? bound : sv == "null" ? JsonValue.Null : null;
                int n = 0;
                foreach (JsonValue st in ses["steps"].Items)
                {
                    JsonValue e = st["event"];
                    KeyPress p = Press(e);
                    KeyOutcome o = e["kind"].Str == "key"
                        ? Keybinds.OnKey(p, null, text, modal, saved, KeybindActions.All)
                        : Keybinds.OnMouse(p, null, false, modal, saved, KeybindActions.All);
                    JsonValue fired = JsonValue.Array(o.Kind == KeyOutcomes.Run && o.Action != null ? new[] { JsonValue.String(o.Action) } : new JsonValue[0]);
                    Same(st["fired"].ToJson(), fired.ToJson(), ctx + " / " + sv + " step " + n++ + " " + e.ToJson());
                }
            }
        }

        static JsonValue L(params object[] items) => JsonValue.Array(items.Select(i => i as JsonValue ?? JsonValue.From(i)));

        static void Actions(JsonValue x)
        {
            Same(JsonValue.Array(KeybindActions.All.Select(JsonValue.String)).ToJson(), x["actions"]["actions"].ToJson(), "the actions");
            int n = 0;
            foreach (JsonValue c in x["actions"]["cases"].Items)
            {
                JsonValue k = c["case"];
                string action = k["action"].Str;
                List<JsonValue> log = new List<JsonValue>();
                JsonValue checks = JsonValue.Object();
                JsonValue rate = JsonValue.Null;
                string select = "";
                switch (action)
                {
                    case KeybindActions.PlayPause:
                        if (k["playBtn"].IsBool && k["playBtn"].Bool) log.Add(L("click", "playBtn"));
                        break;
                    case KeybindActions.SkipForward:
                    case KeybindActions.SkipBackward:
                        {
                            JsonValue skip = k["skip"].IsString && k["skip"].Str == "undefined" ? null : k["skip"];
                            JsonValue d = SkipLength.Of(skip);
                            if (action == KeybindActions.SkipForward) log.Add(L("skip", d));
                            else log.Add(L("skip", -(d.IsNumber ? d.Num : Settings.JsNumber(d.Str))));
                            break;
                        }
                    case KeybindActions.ToggleDash:
                    case KeybindActions.ToggleMap:
                    case KeybindActions.ToggleMetric:
                        {
                            string id = action == KeybindActions.ToggleDash ? "dashboardToggle" : action == KeybindActions.ToggleMap ? "mapToggle" : "metricToggle";
                            string field = action == KeybindActions.ToggleDash ? "dash" : action == KeybindActions.ToggleMap ? "map" : "metric";
                            if (!k[field].IsBool) break;
                            bool on = !k[field].Bool;
                            log.Add(L("change", id, on));
                            checks = JsonValue.Object(id, on, "settings" + char.ToUpperInvariant(id[0]) + id.Substring(1), on);
                            break;
                        }
                    case KeybindActions.ToggleClips:
                        if (k["clipsBtn"].IsBool) log.Add(L("click", "clipsCollapseBtn"));
                        break;
                    case KeybindActions.SetMarkerIn:
                    case KeybindActions.SetMarkerOut:
                        if (k["markerBtns"].IsBool && k["markerBtns"].Bool) log.Add(L("click", action == KeybindActions.SetMarkerIn ? "setStartMarkerBtn" : "setEndMarkerBtn"));
                        break;
                    case KeybindActions.NextClip:
                    case KeybindActions.PrevClip:
                        {
                            List<JsonValue> items = k["items"].Items.ToList();
                            int idx = KeybindActions.ClipIndex(items.Select(i => i["selected"].Bool).ToList(), action == KeybindActions.NextClip ? 1 : -1);
                            if (idx < 0) break;
                            JsonValue id = items[idx]["id"];
                            if (id.IsString && id.Str.Length > 0) log.Add(L("select", id.Str));
                            log.Add(L("scrollIntoView", idx, JsonValue.Object("block", "nearest", "behavior", "smooth")));
                            break;
                        }
                    default:
                        {
                            double r = KeybindActions.Speed(action, k["rate"].Num);
                            rate = k["rate"];
                            if (double.IsNaN(r)) break;
                            log.Add(L("rate", r));
                            log.Add(L("localStorage", PlaybackSpeeds.StorageKey, PlaybackSpeeds.Value(r)));
                            rate = JsonValue.Number(r);
                            select = PlaybackSpeeds.Value(r);
                            break;
                        }
                }
                if (action == KeybindActions.ToggleDash || action == KeybindActions.ToggleMap || action == KeybindActions.ToggleMetric)
                {
                    string field = action == KeybindActions.ToggleDash ? "dash" : action == KeybindActions.ToggleMap ? "map" : "metric";
                    if (!k[field].IsBool) checks = JsonValue.Object();
                }
                string what = "action case " + n++ + " " + k.ToJson();
                Same(c["log"].ToJson(), JsonValue.Array(log).ToJson(), what + ": what it did");
                Same(c["checks"].ToJson(), checks.ToJson(), what + ": the toggles");
                if (action.StartsWith("speed", StringComparison.Ordinal))
                {
                    Assert.Equal(Format.JsNum(c["rate"].Num), Format.JsNum(rate.Num), what + ": the rate");
                    Assert.Equal(c["select"].Str, select, what + ": the speed list");
                }
            }
        }

        static JsonValue Num(double? d) => d == null ? JsonValue.Null : JsonValue.Number(d.Value);

        static string Pct(double d) => Format.JsNum(d) + "%";

        static void Markers(JsonValue x)
        {
            ExportMarkers m = new ExportMarkers();
            TimeZoneInfo zone = TimeZoneInfo.Utc;
            bool active = false;
            List<string> keys = new List<string>();
            List<double> durations = new List<double>();
            List<double> starts = new List<double> { 0 };
            string bar = "0";
            int seg = 0;
            double at = 0;
            int n = 0;
            foreach (JsonValue s in x["markers"].Items)
            {
                string kind = s["step"][0].Str;
                JsonValue v = s["step"][1];
                List<JsonValue> notes = new List<JsonValue>();
                if (kind == "open") { active = true; keys = v.Items.Select(i => i.Str).ToList(); }
                else if (kind == "durations")
                {
                    durations = v.Items.Select(i => i.Num).ToList();
                    starts = new List<double> { 0 };
                    foreach (double d in durations) starts.Add(starts[starts.Count - 1] + d);
                }
                else if (kind == "bar") bar = v.Str;
                else if (kind == "at") { seg = (int)v[0].Num; at = v[1].Num; }
                else if (kind == "set")
                {
                    string type;
                    string note = m.Set(v.Str == "start", active, bar, ExportMarkers.WallMsNow(keys, seg, at, zone), out type);
                    notes.Add(L(note, type));
                }
                else if (kind == "recompute") m.Recompute(keys, durations, starts, zone);
                else if (kind == "remove") { if (m.Elements.Contains(v.Str)) m.Remove(v.Str == "start"); }
                else if (kind == "drag") { if (m.Elements.Contains(v[0].Str)) m.Drag(v[0].Str == "start", v[1].Num, 100, 400); }
                List<JsonValue> els = new List<JsonValue>();
                foreach (string e in m.Elements)
                {
                    if (e == "highlight")
                    {
                        els.Add(JsonValue.Object("className", "export-range-highlight", "left", Pct(m.HighlightLeft), "width", Pct(m.HighlightWidth), "title", JsonValue.Null, "remove", JsonValue.Null));
                        continue;
                    }
                    bool start = e == "start";
                    els.Add(JsonValue.Object("className", "export-marker " + e, "left", Pct((start ? m.StartPct : m.EndPct).Value), "width", JsonValue.Null,
                        "title", ExportMarkers.Title(start),
                        "remove", JsonValue.Object("className", "marker-remove", "title", ExportMarkers.RemoveTitle(start), "html", "<span class=\"material-symbols-outlined mi-sm\">close</span>")));
                }
                JsonValue got = JsonValue.Object("step", s["step"], "startPct", Num(m.StartPct), "endPct", Num(m.EndPct), "startWallMs", Num(m.StartWallMs), "endWallMs", Num(m.EndWallMs),
                    "notes", JsonValue.Array(notes), "markers", JsonValue.Array(els));
                Same(s.ToJson(), got.ToJson(), "markers step " + n++ + " " + s["step"].ToJson());
            }
        }

        /// <summary>Chromium's codes for a few scan codes, and the Playwright names the render replays.</summary>
        static void Codes()
        {
            Assert.Equal("KeyQ", KeyCodes.Code(0x10, false), "0x10");
            Assert.Equal("KeyD", KeyCodes.Code(0x20, false), "0x20");
            Assert.Equal("Space", KeyCodes.Code(0x39, false), "0x39");
            Assert.Equal("Digit1", KeyCodes.Code(0x02, false), "0x02");
            Assert.Equal("Numpad4", KeyCodes.Code(0x4B, false), "0x4B");
            Assert.Equal("ArrowLeft", KeyCodes.Code(0x4B, true), "0xE04B");
            Assert.Equal("NumpadEnter", KeyCodes.Code(0x1C, true), "0xE01C");
            Assert.Equal("ControlRight", KeyCodes.Code(0x1D, true), "0xE01D");
            Assert.Equal("NumLock", KeyCodes.Code(0x45, true), "0xE045");
            Assert.Equal("Pause", KeyCodes.Code(0x45, false), "0x45");
            Assert.Equal("", KeyCodes.Code(0x7F, false), "0x7F");
            Assert.Equal("ArrowUp", KeyCodes.KeyOf(0x26, null, false), "VK_UP");
            Assert.Equal("F5", KeyCodes.KeyOf(0x74, null, false), "VK_F5");
            Assert.Equal("Dead", KeyCodes.KeyOf(0xDB, null, true), "a dead key");
            Assert.Equal("a", KeyCodes.KeyOf(0x41, "a", false), "VK_A");
            Assert.Equal("@", KeyCodes.KeyOf(0x51, "@", false), "VK_Q with AltGr on de-DE (ToUnicodeEx with Ctrl+Alt held)");
            KeyPress altGr = new KeyPress { Key = "@", Code = "KeyQ", Ctrl = true, Alt = true };
            Assert.Equal("ctrl+alt+keyq", Keybinds.Identifier(altGr), "AltGr+Q's identifier");
            Assert.Equal("Unidentified", KeyCodes.KeyOf(0xFF, "", false), "nothing");
            KeyPress p = KeyCodes.Playwright("Shift+Period");
            Assert.Equal("shift+period", Keybinds.Identifier(p), "Shift+Period");
            Assert.Equal("Shift + >", Keybinds.Format(p), "Shift+Period's words");
            p = KeyCodes.Playwright("KeyD");
            Assert.Equal("d", p.Key, "KeyD's key");
            Assert.Equal("D", Keybinds.Format(p), "KeyD's words");
            Assert.Equal("ArrowUp", KeyCodes.Playwright("ArrowUp").Key, "ArrowUp");
            Assert.Equal("ctrl+keyd", Keybinds.Identifier(KeyCodes.Playwright("Control+KeyD")), "Control+KeyD");
        }

        public static void Register(Action<string, Action> test)
        {
            JsonValue x = null;
            Func<JsonValue> get = () => x ?? (x = Expected());
            test("Viewer keybinds: index.html's rows, the actions, the codes", () => { Rows(get()); Codes(); });
            test("Viewer keybinds: recording keys and buttons (keybinds.js)", () => Record(get()));
            test("Viewer keybinds: keys and buttons on the page in each focus (keybinds.js)", () =>
            {
                JsonValue bound = JsonValue.Object(
                    "playPause", JsonValue.Object("display", "Space", "identifier", "space"),
                    "skipForward", JsonValue.Object("display", "→", "identifier", "arrowright"),
                    "skipBackward", JsonValue.Object("display", "←", "identifier", "arrowleft"),
                    "toggleDash", JsonValue.Object("display", "Ctrl + D", "identifier", "ctrl+keyd"),
                    "nextClip", JsonValue.Object("display", "N", "identifier", "keyn"),
                    "speedUp", JsonValue.Object("display", "Shift + >", "identifier", "shift+period"),
                    "setMarkerIn", JsonValue.Object("display", "Mouse Back", "identifier", "mouse3"),
                    "toggleMap", JsonValue.Object("display", "Ctrl + Mouse Middle", "identifier", "ctrl+mouse1"),
                    "prevClip", JsonValue.Object("display", "Escape", "identifier", "escape"));
                Dispatch(get(), bound);
            });
            test("Viewer keybinds: script.js's actions", () => Actions(get()));
            test("Viewer keybinds: the export markers (exportVideo.js)", () => Markers(get()));
        }
    }
}
