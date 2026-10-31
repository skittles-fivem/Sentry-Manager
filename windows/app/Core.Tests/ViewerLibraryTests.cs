using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's ViewerLibrary, LibraryScan, Dashcam and JsDate (the
    /// Viewer's clip library: what it reads from the server and how it groups
    /// it) against Studio's own scan on the same folder trees
    /// (Fixtures/expected-viewer-library.json, written by
    /// test/windowsNativeViewerLibrary.test.js).
    /// </summary>
    static class ViewerLibraryTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-library.json")));

        /// <summary>The web test's zone (America/Chicago) by its IANA name, else its Windows name.</summary>
        static TimeZoneInfo Zone(string iana)
        {
            foreach (string id in new[] { iana, iana == "America/Chicago" ? "Central Standard Time" : iana })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            throw new Exception("no time zone " + iana);
        }

        /// <summary>A canonical text of a value: numbers as JavaScript prints them.</summary>
        static string Canon(JsonValue v)
        {
            StringBuilder b = new StringBuilder();
            Canon(b, v);
            return b.ToString();
        }

        static void Canon(StringBuilder b, JsonValue v)
        {
            if (v == null || v.IsNull) { b.Append("null"); return; }
            if (v.IsNumber) { b.Append(Format.JsNum(v.Num)); return; }
            if (v.IsString || v.IsBool) { b.Append(JsonValue.From(v.IsBool ? (object)v.Bool : v.Str).ToJson()); return; }
            if (v.IsArray)
            {
                b.Append('[');
                for (int i = 0; i < v.Items.Count; i++) { if (i > 0) b.Append(','); Canon(b, v.Items[i]); }
                b.Append(']');
                return;
            }
            b.Append('{');
            bool first = true;
            foreach (var kv in v.Props)
            {
                if (!first) b.Append(',');
                first = false;
                b.Append(JsonValue.Quote(kv.Key)).Append(':');
                Canon(b, kv.Value);
            }
            b.Append('}');
        }

        static void Same(JsonValue want, JsonValue got, string what)
        {
            string a = Canon(want), c = Canon(got);
            if (a == c) return;
            int i = 0;
            while (i < a.Length && i < c.Length && a[i] == c[i]) i++;
            int from = Math.Max(0, i - 120);
            throw new Exception(what + ": first difference at " + i + "\n  web:    …" + a.Substring(from, Math.Min(a.Length - from, 300)) + "\n  native: …" + c.Substring(from, Math.Min(c.Length - from, 300)));
        }

        static JsonValue Arr(IEnumerable<JsonValue> items) => JsonValue.Array(items);
        static JsonValue Strs(IEnumerable<string> items) => JsonValue.Array(items.Select(s => s == null ? JsonValue.Null : JsonValue.String(s)));
        static JsonValue Step(params object[] parts) => JsonValue.Array(parts.Select(JsonValue.From));

        sealed class Tree : IStudioFiles
        {
            readonly JsonValue tree;
            public readonly List<string> Reads = new List<string>(), FileReads = new List<string>();
            public Tree(JsonValue tree) { this.tree = tree; }

            public Task<JsonValue> ReadDir(string path)
            {
                Reads.Add(path);
                JsonValue a = tree["dirs"].Has(path) ? JsonValue.Parse(tree["dirs"][path].ToJson()) : JsonValue.Array(new JsonValue[0]);
                return Task.FromResult(a);
            }

            public Task<string> ReadFile(string path)
            {
                FileReads.Add(path);
                var t = new TaskCompletionSource<string>();
                if (tree["files"].Has(path) && tree["files"][path].IsString) t.SetResult(tree["files"][path].Str);
                else t.SetException(new Exception("Not readable"));
                return t.Task;
            }
        }

        sealed class Surface : IStudioSurface
        {
            public List<JsonValue> Ui = new List<JsonValue>();
            public void ShowLoading(string text, string progress) { Ui.Add(Step("showLoading", text, progress)); }
            public void HideLoading() { Ui.Add(Step("hideLoading")); }
            public void Notify(string message, string type) { Ui.Add(Step("notify", message, type)); }
        }

        static JsonValue FileOf(StudioFile f) => f == null ? JsonValue.Null : JsonValue.Object("name", f.Name, "path", f.Path, "rel", f.RelPath);

        static JsonValue Summary(ViewerLibrary lib)
        {
            StudioFolders f = lib.Folders;
            JsonValue folders = JsonValue.Null;
            if (f != null)
            {
                folders = JsonValue.Object(
                    "root", f.RootName,
                    "profileId", f.ProfileId,
                    "sourceKind", f.SourceKind,
                    "custom", f.IsCustomStructure,
                    "dates", Strs(f.Dates.Keys),
                    "handles", Arr(f.DateHandles.Entries.Select(kv => JsonValue.Object(
                        "date", kv.Key,
                        "recent", kv.Value.HasRecent ? JsonValue.Object("path", kv.Value.RecentPath, "flat", kv.Value.RecentFlat) : JsonValue.Null,
                        "sentry", Arr(kv.Value.Sentry.Entries.Select(e => Strs(new[] { e.Key, e.Value.Path }))),
                        "saved", Arr(kv.Value.Saved.Entries.Select(e => Strs(new[] { e.Key, e.Value.Path }))),
                        "loose", kv.Value.HasLoose ? JsonValue.Object("path", kv.Value.LoosePath, "gmc", kv.Value.LooseGmc, "subfolders", Strs(kv.Value.LooseSubfolders ?? new List<string>())) : JsonValue.Null,
                        "custom", kv.Value.IsCustomStructure,
                        "deletion", kv.Value.Deletion == null ? JsonValue.Null : JsonValue.Object("allowed", kv.Value.Deletion.Allowed, "path", kv.Value.Deletion.Path)))));
            }
            return JsonValue.Object(
                "folders", folders,
                "days", Arr(lib.DayOptions.Select(d => JsonValue.Object("value", d, "text", lib.DayOptionText(d)))),
                "dayValue", lib.DayValue,
                "subtitle", lib.Subtitle,
                "folderLabel", lib.FolderLabel,
                "profileId", lib.ProfileId,
                "sourceKind", lib.SourceKind,
                "allDates", lib.AllDates == null ? JsonValue.Null : Strs(lib.AllDates),
                "metaKeys", Strs(lib.EventMetaByKey.Keys),
                "groups", Arr(lib.ClipGroups.Select(g => JsonValue.Object(
                    "id", g.Id, "profileId", g.ProfileId, "tag", g.Tag, "eventId", g.EventId, "timestampKey", g.TimestampKey, "hint", g.BestRelPathHint,
                    "cameras", Arr(g.FilesByCamera.Entries.Select(kv => JsonValue.Object("camera", kv.Key, "name", kv.Value.File.Name, "path", kv.Value.File.Path, "rel", kv.Value.File.RelPath))),
                    "meta", g.EventMeta == null ? JsonValue.Null : JsonValue.String(g.EventMeta.ToJson()),
                    "json", FileOf(g.EventJsonFile), "png", FileOf(g.EventPngFile), "mp4", FileOf(g.EventMp4File)))),
                "collections", Arr(lib.DayCollections.Values.Select(c => JsonValue.Object(
                    "id", c.Id, "day", c.Day, "profileId", c.ProfileId, "clipType", c.ClipType, "tag", c.Tag, "groups", Strs(c.Groups.Select(g => g.Id)),
                    "durationMs", c.DurationMs, "segmentStartsMs", Arr(c.SegmentStartsMs.Select(x => JsonValue.Number(x))), "anchorGroupId", c.AnchorGroupId, "sortEpoch", c.SortEpoch,
                    "deletion", c.Deletion == null ? JsonValue.Null : JsonValue.Object("allowed", c.Deletion.Allowed, "path", c.Deletion.Path),
                    "eventId", c.EventId, "eventTime", c.EventTime, "custom", c.IsCustomStructure))));
        }

        static void Case(JsonValue c, TimeZoneInfo zone)
        {
            string name = c["name"].Str;
            Tree tree = new Tree(c["tree"]);
            Surface surface = new Surface();
            ViewerLibrary lib = new ViewerLibrary(tree, surface);
            lib.Zone = zone;
            if (c["dateFormat"].IsString) lib.DateFormat = c["dateFormat"].Str;
            List<JsonValue> events = new List<JsonValue>();
            lib.ProfileApplied = (p, s) => surface.Ui.Add(Step("profile", p, s));
            lib.DaysListed = () => surface.Ui.Add(Step("overlay", "hidden"));
            lib.ClipsChanged = () => surface.Ui.Add(Step("render"));
            lib.Selected = col => surface.Ui.Add(Step("select", col.Key));
            lib.EventShown = col => events.Add(Step("shown", col.Id));
            lib.EventSeek = s => events.Add(Step("seek", s));
            lib.EventsRead = () => events.Add(Step("render"));
            JsonValue steps = c["steps"];
            for (int i = 0; i < steps.Count; i++)
            {
                JsonValue want = steps[i];
                tree.Reads.Clear(); tree.FileReads.Clear(); surface.Ui.Clear(); events.Clear();
                JsonValue action = want["action"];
                Task run = action[0].Str == "open" ? lib.Open(c["start"].Str) : lib.PickDay(action[1].Str);
                run.Wait();
                lib.EventReads.Wait();
                string what = name + " step " + i + " " + action.ToJson();
                Same(want["reads"], Strs(tree.Reads), what + " reads");
                Same(want["fileReads"], Strs(tree.FileReads), what + " event.json reads");
                Same(want["ui"], Arr(surface.Ui), what + " loading and toasts");
                Same(want["events"], Arr(events), what + " event.json");
                JsonValue got = Summary(lib);
                foreach (var kv in want["after"].Props) Same(kv.Value, got[kv.Key], what + " " + kv.Key);
            }
        }

        static void Names(JsonValue x)
        {
            foreach (JsonValue n in x["names"].Items)
            {
                DashcamName p = Dashcam.Parse(n["name"].Str);
                JsonValue got = p == null ? JsonValue.Null : JsonValue.Object("profileId", p.ProfileId, "timestampKey", p.TimestampKey, "date", p.Date, "camera", p.Camera);
                Same(n["parsed"], got, "parseDashcamFilename " + n["name"].ToJson());
                Same(n["date"], JsonValue.String(LibraryScan.DateFromFilename(n["name"].Str)), "extractDateFromFilename " + n["name"].ToJson());
            }
            foreach (JsonValue c in x["cameras"].Items)
            {
                string cam = c["camera"].Str;
                Same(c["normalized"], JsonValue.String(Dashcam.NormalizeCamera(cam)), "normalizeCamera " + cam);
                Same(c["tesla"], JsonValue.String(Dashcam.StudioCameraLabel(cam, Dashcam.TeslaId)), "cameraLabel tesla " + cam);
                Same(c["gmc"], JsonValue.String(Dashcam.StudioCameraLabel(cam, Dashcam.GmcId)), "cameraLabel gmc " + cam);
                Same(c["label"], JsonValue.String(Dashcam.CameraLabel(Dashcam.GmcId, cam)), "getCameraLabel " + cam);
            }
        }

        static void Times(JsonValue x, TimeZoneInfo zone)
        {
            foreach (JsonValue k in x["keys"].Items)
            {
                double? ms = LibraryScan.KeyToEpochMs(k["key"].Str, zone);
                Same(k["ms"], ms.HasValue ? JsonValue.Number(ms.Value) : JsonValue.Null, "timestampKeyToEpochMs " + k["key"].Str);
            }
            foreach (JsonValue s in x["stamps"].Items)
            {
                double ms = JsDate.Parse(s["text"].Str, zone);
                Same(s["ms"], double.IsNaN(ms) ? JsonValue.String("NaN") : JsonValue.Number(ms), "Date.parse " + s["text"].ToJson());
            }
        }

        static void Profiles(JsonValue x)
        {
            foreach (JsonValue p in x["profiles"].Items)
            {
                DashcamProfile d = Dashcam.Get(p["id"].Str);
                Assert.Equal(p["id"].Str, d.Id, "profile id");
                Assert.Equal(p["displayName"].Str, d.DisplayName, "displayName");
                Assert.Equal(p["collectionLabel"].Str, d.CollectionLabel, "collectionLabel");
                Assert.Equal(p["customCollectionLabel"].Str, d.CustomCollectionLabel, "customCollectionLabel");
                Assert.Equal(p["normalizeCollectionTag"].Bool, d.NormalizeCollectionTag, "normalizeCollectionTag");
                Assert.Equal(p["localizedCameraLabels"].Bool, d.LocalizedCameraLabels, "localizedCameraLabels");
                Assert.Equal(p["defaultLooseSourceKind"].Str, d.DefaultLooseSourceKind, "defaultLooseSourceKind");
                Assert.Equal(p["layoutId"].Str, d.LayoutId, "layoutId");
                Assert.Equal(p["defaultSegmentDurationSeconds"].Num, d.DefaultSegmentDurationSeconds, "defaultSegmentDurationSeconds");
                Assert.Equal(p["exportDefaults"]["fps"].Num, d.ExportFps, "export fps");
                Same(p["exportDefaults"]["cameraOrder"], Strs(d.ExportCameraOrder), d.Id + " cameraOrder");
                JsonValue caps = p["capabilities"];
                Same(caps, JsonValue.Object("telemetry", d.Capabilities.Telemetry, "gps", d.Capabilities.Gps, "dashboard", d.Capabilities.Dashboard, "map", d.Capabilities.Map, "export", d.Capabilities.Export, "driveMatching", d.Capabilities.DriveMatching), d.Id + " capabilities");
                Same(p["cameras"], JsonValue.From(d.Cameras.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)).ToList()), d.Id + " cameras");
                Same(p["layoutSlots"], Arr(d.LayoutSlots.Select(s => JsonValue.Object("slot", s.Slot, "camera", s.Camera))), d.Id + " layoutSlots");
                Same(p["mirroredCameras"], Strs(d.MirroredCameras), d.Id + " mirroredCameras");
                JsonValue rules = p["sourceRules"];
                if (rules.Has("roots")) Same(rules["roots"], Strs(d.Roots), "roots");
                if (rules.Has("rawAndroidPath")) Same(rules["rawAndroidPath"], Strs(d.RawAndroidPath), "rawAndroidPath");
                if (rules.Has("continuousFolder")) Assert.Equal(rules["continuousFolder"].Str, d.ContinuousFolder, "continuousFolder");
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("viewer library: Studio's scan, index and collections on folder trees", () =>
            {
                JsonValue x = Expected();
                TimeZoneInfo zone = Zone(x["zone"].Str);
                foreach (JsonValue c in x["cases"].Items) Case(c, zone);
            });
            test("viewer library: dashcam file names, cameras and profiles as dashcamProfiles.mjs", () =>
            {
                JsonValue x = Expected();
                Names(x);
                Profiles(x);
            });
            test("viewer library: timestamp keys and event times as JavaScript's Date", () =>
            {
                JsonValue x = Expected();
                Times(x, Zone(x["zone"].Str));
            });
        }
    }
}
