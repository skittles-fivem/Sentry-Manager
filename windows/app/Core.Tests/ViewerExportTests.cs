using System;
using System.IO;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's StudioExport (the Viewer has no export, as the web
    /// Viewer) against web/studioEmbed.css's hidden buttons and
    /// web/electronApiShim.js's answers to Studio's export calls
    /// (Fixtures/expected-viewer-export.json, written by
    /// test/windowsNativeViewerExport.test.js).
    /// </summary>
    static class ViewerExportTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-export.json")));

        static void Eq(string want, string got, string what)
        {
            if (want != got) throw new Exception(what + ":\n  web:    " + want + "\n  native: " + got);
        }

        static void Hidden()
        {
            JsonValue x = Expected()["hidden"];
            Eq(x.Count.ToString(), StudioExport.HiddenButtons.Length.ToString(), "hidden buttons");
            for (int i = 0; i < x.Count; i++)
            {
                Eq(x[i].Str, StudioExport.HiddenButtons[i], "hidden button " + i);
                if (StudioExport.Shows(x[i].Str)) throw new Exception(x[i].Str + " is shown");
            }
            if (!StudioExport.Shows("settingsBtn") || !StudioExport.Shows("playBtn")) throw new Exception("the gear and play button are shown");
        }

        static void Answers()
        {
            JsonValue x = Expected();
            JsonValue calls = x["calls"];
            Eq(calls.Count.ToString(), StudioExport.Calls.Length.ToString(), "export calls");
            for (int i = 0; i < calls.Count; i++)
            {
                string call = calls[i]["call"].Str;
                Eq(call, StudioExport.Calls[i], "export call " + i);
                Eq(calls[i]["answer"].ToJson(), StudioExport.Answer(call).ToJson(), call + "()");
            }
            Eq(x["serverPaths"].Count.ToString(), StudioExport.ServerPaths.Length.ToString(), "server paths");
            bool threw = false;
            try { StudioExport.Answer("getSetting"); } catch (ArgumentException) { threw = true; }
            if (!threw) throw new Exception("Answer(getSetting) did not refuse");
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer export: the hidden buttons as studioEmbed.css", Hidden);
            test("viewer export: the export and sharing calls' answers as electronApiShim.js", Answers);
        }
    }
}
