using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's settings modal (ViewerSettings.cs: SettingsLayout,
    /// ViewerSettingsModal) against Studio's own index.html #settingsModal,
    /// settingsModal.js, keybinds.js, i18n.js, exportVideo.js's NEW badges
    /// and the shim's settings and update calls, on the same sessions of
    /// steps (Fixtures/expected-viewer-settings.json, written by
    /// test/windowsNativeViewerSettings.test.js).
    /// </summary>
    static class ViewerSettingsTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-settings.json")));

        static void Same(JsonValue want, JsonValue got, string what)
        {
            string a = want.ToJson(), c = got.ToJson();
            if (a == c) return;
            int i = 0;
            while (i < a.Length && i < c.Length && a[i] == c[i]) i++;
            int from = Math.Max(0, i - 160);
            throw new AssertFailed(what + ": first difference at " + i + "\n  web:    …" + a.Substring(from, Math.Min(a.Length - from, 400)) + "\n  native: …" + c.Substring(from, Math.Min(c.Length - from, 400)));
        }

        static string Str(JsonValue v) => v == null || v.IsNull ? null : v.Str;

        /// <summary>script.js's and mapTiles.js's parts the test's stand-ins play (the same as the web test's).</summary>
        sealed class Host : IViewerSettingsHost
        {
            public readonly List<JsonValue> Log = new List<JsonValue>();
            public bool Answer;
            public bool DashboardEnabled { get; set; }
            public bool MapEnabled { get; set; }
            public bool UseMetric { get; set; }
            public string LayoutStyle { get; set; }
            public string MapProvider { get; set; }
            public bool MapLabels { get; set; }

            public Host(JsonValue all)
            {
                JsonValue d = JsJson.Get(all, "dashboardEnabled"), m = JsJson.Get(all, "mapEnabled"), u = JsJson.Get(all, "useMetric");
                DashboardEnabled = d == null || (d.IsBool && d.Bool);
                MapEnabled = m == null || (m.IsBool && m.Bool);
                UseMetric = u != null && u.IsBool && u.Bool;
                JsonValue l = JsJson.Get(all, "layoutStyle");
                LayoutStyle = JsJson.Truthy(l) ? StudioFrames.JsString(l) : "modern";
                JsonValue p = JsJson.Get(all, "mapTileProvider");
                MapProvider = MapProviders.Get(p != null && p.IsString ? p.Str : null).Id;
                JsonValue lab = JsJson.Get(all, "mapLabels");
                MapLabels = lab == null || lab.IsNull ? MapProviders.DefaultLabelsEnabled : lab.Bool;
            }

            void Save(string key, JsonValue v) => Log.Add(JsonValue.Array(new[] { JsonValue.String("setSetting"), JsonValue.String(key), v }));

            public void Call(JsonValue call)
            {
                Log.Add(call);
                string name = call[0].Str;
                switch (name)
                {
                    case "dashboardToggle": DashboardEnabled = call[1].Bool; Save("dashboardEnabled", JsonValue.From(DashboardEnabled)); break;
                    case "mapToggle": MapEnabled = call[1].Bool; Save("mapEnabled", JsonValue.From(MapEnabled)); break;
                    case "metricToggle": UseMetric = call[1].Bool; Save("useMetric", JsonValue.From(UseMetric)); break;
                    case "setLayoutStyle": LayoutStyle = call[1].Str; Save("layoutStyle", call[1]); break;
                    case "setMapTileProvider": MapProvider = MapProviders.Get(call[1].Str).Id; Save("mapTileProvider", JsonValue.String(MapProvider)); break;
                    case "setMapLabels": MapLabels = call[1].Bool; Save("mapLabels", JsonValue.From(MapLabels)); break;
                }
            }

            public bool Confirm(string text)
            {
                Log.Add(JsonValue.Array(new[] { JsonValue.String("confirm"), JsonValue.String(text) }));
                return Answer;
            }
        }

        /// <summary>The web test's diff: each second-level value that changed.</summary>
        static JsonValue Diff(JsonValue a, JsonValue b)
        {
            JsonValue o = JsonValue.Object();
            foreach (KeyValuePair<string, JsonValue> kv in b.Props)
            {
                if (kv.Value.IsObject)
                {
                    JsonValue d = JsonValue.Object();
                    foreach (KeyValuePair<string, JsonValue> j in kv.Value.Props)
                        if (a[kv.Key][j.Key].ToJson() != j.Value.ToJson()) d = d.With(j.Key, j.Value);
                    if (d.Count > 0) o = o.With(kv.Key, d);
                }
                else if (a[kv.Key].ToJson() != kv.Value.ToJson()) o = o.With(kv.Key, kv.Value);
            }
            return o;
        }

        static JsonValue WithoutTexts(JsonValue v) => v.Has("texts") ? JsJson.Without(v, "texts") : v;

        static ViewerSettingsModal Start(JsonValue session, Host host)
        {
            ViewerSettingsModal m = new ViewerSettingsModal(host);
            m.Load(session["settings"], session["context"], "/srv/footage/TeslaCam", session["languages"].Items.Select(x => x.Str).ToList());
            return m;
        }

        static JsonValue opts(SettingsOption[] o) => JsonValue.Array(o.Select(x => JsonValue.Object("value", x.Value, "text", x.Text)));

        static JsonValue Item(ViewerSettingsModal m, SettingsItem i)
        {
            switch (i.Kind)
            {
                case SettingsItem.Toggle:
                    return JsonValue.Object("kind", i.Kind, "key", i.Key, "id", i.Id, "hidden", i.Hidden, "label", i.Label, "desc", i.Desc, "checked", i.Checked, "search", m.SearchText(i));
                case SettingsItem.Select:
                    return JsonValue.Object("kind", i.Kind, "key", i.Key, "label", i.Label, "options", opts(i.Options), "value", i.Value, "search", m.SearchText(i));
                case SettingsItem.Slider:
                    return JsonValue.Object("kind", i.Kind, "key", i.Key, "label", i.Label, "min", i.Min, "max", i.Max, "step", i.Step, "value", i.Value, "valueId", i.ValueId, "search", m.SearchText(i));
                case SettingsItem.ActionRow:
                    return JsonValue.Object("kind", i.Kind, "key", i.Key, "label", i.Label, "button", i.Button, "search", m.SearchText(i));
                case SettingsItem.Keybind:
                    return JsonValue.Object("kind", i.Kind, "key", i.Key, "label", i.LabelId != null ? m.Text(i.LabelId) : i.Label, "labelId", i.LabelId, "dot", i.Dot, "action", i.Action,
                        "options", i.Options == null ? JsonValue.Null : opts(i.Options), "value", i.Options == null ? null : i.Value, "placeholder", i.Placeholder, "clear", i.Clear, "search", m.SearchText(i));
                case SettingsItem.Input:
                    return JsonValue.Object("kind", i.Kind, "key", i.Key, "placeholder", i.Placeholder, "buttons", JsonValue.Array(i.Options.Select(x => JsonValue.Object("id", x.Value, "text", x.Text))), "search", m.SearchText(i));
                case SettingsItem.SubLabel: return JsonValue.Object("kind", i.Kind, "text", i.Text);
                case SettingsItem.Status: return JsonValue.Object("kind", i.Kind, "id", i.Id);
                case SettingsItem.Subtabs: return JsonValue.Object("kind", i.Kind, "tabs", JsonValue.Array(i.Options.Select(x => JsonValue.Object("id", x.Value, "text", x.Text))));
                case SettingsItem.Panel: return JsonValue.Object("kind", i.Kind, "subtab", i.Subtab, "items", JsonValue.Array(i.Items.Select(x => Item(m, x))));
                case SettingsItem.Security:
                    return JsonValue.Object("kind", i.Kind,
                        "details", JsonValue.Array(i.Details.Select(d => JsonValue.Object("label", d.Label, "id", d.Id, "value", m.Text(d.Id)))),
                        "link", m.Text("securityLearnMoreLink"), "head", JsonValue.Array(i.Head.Select(JsonValue.String)),
                        "rows", JsonValue.Array(i.Rows.Select(r => JsonValue.Object("name", r.Name, "badge", r.Badge, "status", r.Status, "desc", r.Desc))));
                case SettingsItem.Buttons: return JsonValue.Object("kind", i.Kind, "buttons", JsonValue.Array(i.Options.Select(x => JsonValue.Object("id", x.Value, "text", x.Text))));
                case SettingsItem.Legal: return JsonValue.Object("kind", i.Kind, "links", JsonValue.Array(i.Options.Select(x => JsonValue.Object("id", x.Value, "text", x.Text))), "separator", i.Separator);
            }
            throw new AssertFailed("item kind " + i.Kind);
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer settings: the modal's sections, rows, options and words as Studio's page has them", () =>
            {
                JsonValue x = Expected();
                JsonValue want = x["layout"];
                JsonValue session = JsonValue.Object("settings", JsonValue.Object("timeFormat", "12h"), "context", JsonValue.Object("version", "2026.26.35 (web)"), "languages", JsonValue.Array(new[] { JsonValue.String("en-US") }));
                ViewerSettingsModal m = Start(session, new Host(session["settings"]));
                Assert.Equal(want["title"].Str, SettingsLayout.Title, "title");
                Assert.Equal(want["search"].Str, SettingsLayout.SearchPlaceholder, "search placeholder");
                Assert.Equal(want["clear"].Str, SettingsLayout.Clear, "clear");
                Assert.Equal(want["noResults"].Str, SettingsLayout.NoResults, "no results");
                Assert.Equal(want["done"].Str, SettingsLayout.Done, "done");
                Assert.Equal(want["prerelease"]["title"].Str, SettingsLayout.PrereleaseTitle, "prerelease title");
                Assert.Equal(want["prerelease"]["body"].Str, SettingsLayout.PrereleaseBody, "prerelease body");
                Assert.Equal(want["prerelease"]["confirm"].Str, SettingsLayout.PrereleaseConfirm, "prerelease confirm");
                Assert.Equal(want["prerelease"]["cancel"].Str, SettingsLayout.PrereleaseCancel, "prerelease cancel");
                Assert.Equal(want["changelog"]["title"].Str, SettingsLayout.ChangelogTitle, "changelog title");
                Assert.Equal(want["changelog"]["loading"].Str, SettingsLayout.ChangelogLoading, "changelog loading");
                Same(want["nav"], JsonValue.Array(SettingsLayout.Nav.Select(n => JsonValue.Object("target", n.Target, "text", n.Text, "badge", n.Badge, "badgeText", n.BadgeText))), "nav");
                JsonValue sections = JsonValue.Array(SettingsLayout.Sections.Select(s => JsonValue.Object("section", s.Id, "title", s.Title, "titleSearch", s.Title, "embedHidden", s.EmbedHidden,
                    "items", JsonValue.Array(s.Items.Select(i => Item(m, i))))));
                Same(want["sections"], sections, "sections");
                JsonValue words = x["words"];
                Assert.Equal(words["skipForward"].Str, SettingsLayout.SkipForward, "skipForward");
                Assert.Equal(words["skipBackward"].Str, SettingsLayout.SkipBackward, "skipBackward");
                Assert.Equal(words["seconds"].Str, SettingsLayout.Seconds, "seconds");
                Assert.Equal(words["checking"].Str, SettingsLayout.Checking, "checking");
                Assert.Equal(words["updateFound"].Str, SettingsLayout.UpdateFound, "updateFound");
                Assert.Equal(words["checkFailed"].Str, SettingsLayout.CheckFailed, "checkFailed");
                Assert.Equal(words["upToDate"].Str, SettingsLayout.UpToDate, "upToDate");
                Assert.Equal(words["checkNow"].Str, SettingsLayout.CheckNow, "checkNow");
                Assert.Equal(words["hideDetails"].Str, SettingsLayout.HideDetails, "hideDetails");
                Assert.Equal(words["learnMore"].Str, SettingsLayout.LearnMore, "learnMore");
            });

            test("viewer settings: each session's calls, classes, values and words as settingsModal.js makes them", () =>
            {
                foreach (JsonValue s in Expected()["sessions"].Items)
                {
                    string name = s["name"].Str;
                    bool foreign = s["foreign"].Bool;
                    Host host = new Host(s["settings"]);
                    host.Answer = s["confirm"].Bool;
                    ViewerSettingsModal m = Start(s, host);
                    JsonValue prev = null;
                    int n = 0;
                    foreach (JsonValue st in s["steps"].Items)
                    {
                        JsonValue step = st["step"];
                        string kind = step[0].Str;
                        string what = name + " step " + n + " " + step.ToJson();
                        if (kind != "start")
                        {
                            if (kind == "clock") m.Step("clock", null, step[1]);
                            else m.Step(kind, step[1].Str, step.Count > 2 ? step[2] : null);
                        }
                        Same(st["log"], JsonValue.Array(host.Log), what + " calls");
                        host.Log.Clear();
                        JsonValue view = m.Snapshot();
                        JsonValue got = prev == null ? view : Diff(prev, view);
                        JsonValue wantView = st["view"];
                        if (foreign) { got = WithoutTexts(got); wantView = WithoutTexts(wantView); }
                        Same(wantView, got, what + " view");
                        prev = view;
                        n++;
                    }
                }
            });

            test("viewer settings: the glass blur slider's value as the browser sanitises it", () =>
            {
                Assert.Equal("7", ViewerSettingsModal.SliderValue("7"), "7");
                Assert.Equal("8", ViewerSettingsModal.SliderValue("7.5"), "7.5");
                Assert.Equal("7", ViewerSettingsModal.SliderValue("7.49"), "7.49");
                Assert.Equal("15", ViewerSettingsModal.SliderValue(""), "empty");
                Assert.Equal("15", ViewerSettingsModal.SliderValue("abc"), "abc");
                Assert.Equal("15", ViewerSettingsModal.SliderValue("+5"), "+5");
                Assert.Equal("15", ViewerSettingsModal.SliderValue(" 5"), "space");
                Assert.Equal("30", ViewerSettingsModal.SliderValue("1e3"), "1e3");
                Assert.Equal("0", ViewerSettingsModal.SliderValue("-4"), "-4");
                Assert.Equal("1", ViewerSettingsModal.SliderValue(".5"), ".5");
                Assert.Equal("15", ViewerSettingsModal.SliderValue("5."), "5.");
            });
        }
    }
}
