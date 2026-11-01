using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// The Viewer's words in Studio's languages (Core/StudioI18n.cs) against
    /// test/windowsNativeViewerI18n.test.js's run of lib/i18n.js t() on
    /// data/translations.js (Fixtures/expected-viewer-i18n.json).
    /// </summary>
    static class ViewerI18nTests
    {
        static JsonValue Expected()
        {
            return JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-i18n.json")));
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer i18n: Studio's languages and t() with its English fallback and parameters", () =>
            {
                JsonValue x = Expected();
                Assert.Equal(string.Join(",", x["languages"].Items.Select(l => l.Str)), string.Join(",", StudioI18n.Languages), "languages");
                Assert.Equal(string.Join(",", x["languages"].Items.Select(l => l.Str)), string.Join(",", SettingsLayout.Languages), "the Language list's values");
                foreach (JsonValue c in x["calls"].Items)
                {
                    Dictionary<string, string> p = null;
                    if (c["params"].IsObject)
                    {
                        p = new Dictionary<string, string>();
                        foreach (KeyValuePair<string, JsonValue> kv in c["params"].Props) p[kv.Key] = kv.Value.IsString ? kv.Value.Str : Format.JsNum(kv.Value.Num);
                    }
                    Assert.Equal(c["result"].Str, StudioI18n.T(c["lang"].Str, c["key"].Str, p), c["lang"].Str + " " + c["key"].Str);
                }
            });

            test("viewer i18n: every page word of the Viewer in every language, found by its English words", () =>
            {
                JsonValue x = Expected();
                foreach (KeyValuePair<string, JsonValue> lang in x["pageWords"].Props)
                    foreach (JsonValue w in lang.Value.Items)
                        Assert.Equal(w[1].Str, StudioI18n.Word(lang.Key, w[0].Str), lang.Key + " \"" + w[0].Str + "\"");
                Assert.Equal("Keine Daten", StudioI18n.T("de", "ui.dashboard.noData"), "de noData");
                Assert.Equal("en", StudioI18n.Detect(new[] { "xx-YY", "en-GB" }), "detect: en-GB");
                Assert.Equal("pt", StudioI18n.Detect(new[] { "pt-BR" }), "detect: pt-BR");
                Assert.Equal("en", StudioI18n.Detect(new string[0]), "detect: none");
            });

            test("viewer i18n: the dashboard's words in the language, and onLanguageChange's 'Manual' / 'No Data' only", () =>
            {
                DashboardView d = new DashboardView();
                d.Language = "de";
                d.Reset(false);
                Assert.Equal("Keine Daten", d.Ap, "reset: noData");
                Assert.Equal("Manuell", d.ApCompact, "reset compact: manual");
                Assert.Equal("Autonomes Fahren", DashboardView.ApText(1, "de"), "selfDriving");
                Assert.Equal("Self Driving", DashboardView.ApText(1), "English");
                d.LanguageChanged("ja", false);
                Assert.Equal("Keine Daten", d.Ap, "a word not in English stays (script.js compares with 'No Data')");
                d.Language = "en";
                d.Reset(false);
                d.LanguageChanged("fr", false);
                Assert.Equal(StudioI18n.T("fr", "ui.dashboard.noData"), d.Ap, "No Data in French");
                Assert.Equal(StudioI18n.T("fr", "ui.dashboard.manual"), d.ApCompact, "Manual in French");
            });
        }
    }
}
