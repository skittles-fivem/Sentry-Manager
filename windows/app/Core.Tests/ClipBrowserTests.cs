using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's ClipBrowser, ClipsPanelMode and JsText (the Viewer's
    /// clip list panel) against Studio's own clipBrowser.js, panelMode.js,
    /// formatEventReason and the shim's deleteFolder on the same input
    /// (Fixtures/expected-clip-browser.json, written by
    /// test/windowsNativeClipBrowser.test.js).
    /// </summary>
    static class ClipBrowserTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-clip-browser.json")));

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

        static void Same(JsonValue want, JsonValue got, string what)
        {
            string a = want.ToJson(), c = got.ToJson();
            if (a == c) return;
            int i = 0;
            while (i < a.Length && i < c.Length && a[i] == c[i]) i++;
            int from = Math.Max(0, i - 120);
            throw new Exception(what + ": first difference at " + i + "\n  web:    …" + a.Substring(from, Math.Min(a.Length - from, 300)) + "\n  native: …" + c.Substring(from, Math.Min(c.Length - from, 300)));
        }

        static JsonValue S(string s) => s == null ? JsonValue.Null : JsonValue.String(s);

        /// <summary>An item as the web test reads it from the element.</summary>
        static JsonValue ItemJson(ClipListItem i, JsonValue exists, JsonValue strips)
        {
            List<JsonValue> badges = new List<JsonValue> { JsonValue.Object("cls", i.Type, "title", JsonValue.Null, "text", i.Badge) };
            if (i.Reason != null) badges.Add(JsonValue.Object("cls", "reason-icon " + i.ReasonClass, "title", i.ReasonTitle, "text", i.Reason));
            JsonValue o = JsonValue.Object(
                "id", i.Id,
                "className", "clip-item event-item " + i.Type + "-item",
                "folderpath", S(i.FolderPath),
                "title", i.Title,
                "badges", JsonValue.Array(badges),
                "sub", i.Sub,
                "del", i.FolderPath == null ? JsonValue.Null : JsonValue.Object("title", ClipBrowser.DeleteClip, "path", i.FolderPath),
                "selected", i.Selected,
                "hover", i.HasPreview,
                "opens", i.Key);
            if (i.HasPreview)
            {
                string thumb = ClipBrowser.ThumbPath(i.ThumbFolder);
                List<JsonValue> asked = new List<JsonValue>(), stripAsked = new List<JsonValue>(), imgs = new List<JsonValue>();
                bool hasThumb = false;
                if (thumb != null)
                {
                    asked.Add(JsonValue.String(thumb));
                    hasThumb = exists.Items.Any(e => e.Str == thumb);
                    if (hasThumb) imgs.Add(JsonValue.Object("src", ClipBrowser.MediaUrl(thumb), "cls", JsonValue.Null));
                }
                if (i.Filmstrip != null)
                {
                    stripAsked.Add(JsonValue.String(i.Filmstrip));
                    string strip = strips[i.Filmstrip].Str;
                    if (strip != null) imgs.Add(JsonValue.Object("src", ClipBrowser.MediaUrl(strip), "cls", "clip-filmstrip"));
                }
                o = o.With("preview", JsonValue.Object("exists", JsonValue.Array(asked), "strips", JsonValue.Array(stripAsked), "imgs", JsonValue.Array(imgs)));
            }
            return o;
        }

        static void Case(JsonValue c, TimeZoneInfo zone)
        {
            string name = c["name"].Str;
            List<StudioFile> files = c["files"].Items.Select(f => new StudioFile { Name = f["name"].Str, Path = f["path"].Str, RelPath = f["rel"].Str }).ToList();
            ClipIndex built = LibraryScan.BuildIndex(files, c["rootName"].Str);
            foreach (var kv in c["metas"].Props)
            {
                string[] parts = kv.Key.Split('/');
                foreach (ClipGroup g in built.Groups) if (g.Tag == parts[0] && g.EventId == parts[1]) g.EventMeta = JsonValue.Parse(kv.Value.ToJson());
            }
            OrderedMap<CollectionDeletion> deletion = new OrderedMap<CollectionDeletion>();
            foreach (var kv in c["deletion"].Props) deletion.Set(kv.Key, new CollectionDeletion { Allowed = kv.Value["allowed"].Kind == JsonKind.Bool && kv.Value["allowed"].Bool, Path = kv.Value["path"].Str });
            DayCollectionSet set = LibraryScan.BuildCollections(built.Groups, c["profileId"].Str, c["sourceKind"].Str, deletion, zone);
            foreach (JsonValue r in c["renders"].Items)
            {
                string what = name + " " + r["day"].Str + " " + r["active"].ToJson();
                ClipListView view = ClipBrowser.Render(set.DayData, set.Collections, r["day"].Str, r["active"].Str);
                Same(r["thrown"], S(view.Thrown), what + " thrown");
                Same(r["placeholder"], view.Placeholder ? JsonValue.String(StudioText.ClipListPlaceholder) : JsonValue.Null, what + " placeholder");
                Same(r["items"], JsonValue.Array(view.Items.Select(i => ItemJson(i, c["exists"], c["strips"]))), what + " items");
            }
        }

        sealed class TrashApi : IJsonApi
        {
            readonly JsonValue spec;
            public readonly List<string> Calls = new List<string>();
            public TrashApi(JsonValue spec) { this.spec = spec; }

            public Task<JsonValue> Post(string path, JsonValue body, CancellationToken ct)
            {
                Calls.Add("POST " + path + " " + body.ToJson());
                var t = new TaskCompletionSource<JsonValue>();
                if (spec["status"].IsNumber)
                {
                    string words = spec["error"].Str;
                    ApiError e = new ApiError((int)spec["status"].Long, null, words);
                    t.SetException(e);
                }
                else t.SetResult(spec["answer"].IsNull ? null : JsonValue.Parse(spec["answer"].ToJson()));
                return t.Task;
            }

            public Task<JsonValue> Get(string path, CancellationToken ct) => throw new NotSupportedException();
            public Task<JsonValue> Put(string path, JsonValue body, CancellationToken ct) => throw new NotSupportedException();
            public Task<JsonValue> Delete(string path, JsonValue body, CancellationToken ct) => throw new NotSupportedException();
            public Task<JsonValue> PostBytes(string path, byte[] data, string contentType, IDictionary<string, string> headers, CancellationToken ct) => throw new NotSupportedException();
            public Task<byte[]> GetBytes(string path, CancellationToken ct) => throw new NotSupportedException();
        }

        static JsonValue DialogJson(ClipDeleteDialog d) => JsonValue.Object(
            "icon", d.Icon, "title", d.Title, "message", d.Message, "pathLabel", S(d.PathLabel), "path", d.Path,
            "warning", S(d.Warning), "confirm", d.Confirm, "cancel", d.Cancel);

        static void Delete(JsonValue d)
        {
            string name = d["name"].Str;
            string path = d["path"].Str;
            Same(d["dialogs"], JsonValue.Array(new[] { DialogJson(ClipBrowser.Dialog(path, false)), DialogJson(ClipBrowser.Dialog(path, true)) }), name + " dialogs");
            Assert.Equal(d["during"]["text"].Str, ClipBrowser.Deleting, name + " while it is sent");
            TrashApi api = new TrashApi(d);
            bool playing = d["playing"].Bool;
            ClipDeleteOutcome o = ClipBrowser.Delete(api, path, playing, CancellationToken.None).Result;
            JsonValue calls = d["calls"];
            string first = "POST " + calls[0][1].Str + " " + calls[0][2].Str;
            Assert.Equal(first, api.Calls.Count > 0 ? api.Calls[0] : null, name + " request");
            Assert.Equal(1, api.Calls.Count, name + " one request");
            if (playing)
            {
                // the web reloads the Viewer (and its script goes on until the reload lands); the native Viewer starts over at once
                Assert.True(calls.Items.Any(x => x[0].Str == "reload"), name + " reloads on the web");
                Assert.True(o.Reload && o.Close, name + " starts over");
                Assert.Equal(d["answer"]["success"].Bool, o.Success, name + " moved");
                return;
            }
            Assert.True(!o.Reload, name + " no reload");
            Assert.Equal(!d["open"].Bool, o.Close, name + " dialog closed");
            Same(d["notes"], o.Toast == null ? JsonValue.Array(new JsonValue[0]) : JsonValue.Array(new[] { JsonValue.Array(new[] { JsonValue.String(o.Toast), JsonValue.String(o.ToastType) }) }), name + " toasts");
            Assert.Equal(d["deleted"].Count > 0, o.Rescan, name + " read again (onClipDeleted)");
            if (d["open"].Bool) Assert.Equal(ClipBrowser.DeleteConfirmBtn, d["after"]["text"].Str, name + " the button's words back");
        }

        static void Panel(JsonValue p)
        {
            string name = p["name"].Str;
            ClipsPanelMode m = new ClipsPanelMode();
            List<string> saves = new List<string>();
            JsonValue steps = p["steps"];
            for (int i = 0; i < steps.Count; i++)
            {
                JsonValue want = steps[i];
                JsonValue step = want["step"];
                string kind = step[0].Str;
                if (kind == "init") m.Init(p["settings"]["layoutStyle"], p["settings"]["sidebarWidth"], p["saved"].Str);
                if (kind == "toggle") m.Toggle();
                if (kind == "layout") { m.SetLayout(step[1].Str); saves.Add("layoutStyle=" + step[1].Str); }
                if (kind == "drag" && m.Handle && m.Classic)
                {
                    double start = m.Width, x0 = step[1].Num, w = start;
                    foreach (JsonValue x in step[2].Items) w = ClipsPanelMode.Dragged(start, x.Num - x0);
                    m.Width = w;
                    double saved = m.DragEnd(w);
                    saves.Add("sidebarWidth=" + Format.JsNum(saved));
                }
                string what = name + " step " + i + " " + step.ToJson();
                Assert.Equal(want["mode"].Str, "clips-mode-" + m.Mode, what + " mode");
                Assert.Equal(want["saved"].Str, m.Saved, what + " remembered");
                Assert.Equal(want["title"].Str, m.ButtonTitle, what + " button");
                Assert.Equal(want["width"].Str, Format.JsNum(m.Width) + "px", what + " width");
                Assert.Equal(want["handle"].Bool, m.Handle, what + " handle");
                Same(JsonValue.Array(want["saves"].Items.Select(s => JsonValue.String(s[0].Str + "=" + (s[1].IsString ? s[1].Str : Format.JsNum(s[1].Num))))), JsonValue.Array(saves.Select(JsonValue.String)), what + " saved settings");
                saves.Clear();
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("clip browser: Studio's clip list (order, words, badges, delete folder, preview, selection)", () =>
            {
                JsonValue x = Expected();
                TimeZoneInfo zone = Zone(x["zone"].Str);
                foreach (JsonValue c in x["cases"].Items) Case(c, zone);
            });
            test("clip browser: the delete dialogs and the folder to the Recycle Bin as the shim sends it", () =>
            {
                foreach (JsonValue d in Expected()["deletes"].Items) Delete(d);
            });
            test("clip browser: panelMode.js's modes, remembered mode and sidebar drag", () =>
            {
                foreach (JsonValue p in Expected()["panels"].Items) Panel(p);
            });
            test("clip browser: formatEventReason, formatEventTime, Chrome's localeCompare and encodeURIComponent", () =>
            {
                JsonValue x = Expected();
                foreach (JsonValue r in x["reasons"].Items) Assert.Equal(r["text"].Str, ClipBrowser.FormatEventReason(r["reason"].Str), "formatEventReason " + r["reason"].Str);
                foreach (JsonValue t in x["times"].Items) Assert.Equal(t["time"].Str, ClipBrowser.FormatEventTime(t["id"].Str), "formatEventTime " + t["id"].Str);
                List<string> strs = x["order"].Items.Select(o => o[0].Str).ToList();
                // (the web's sorted order: each next one not before the last)
                for (int i = 1; i < strs.Count; i++)
                    Assert.True(JsText.LocaleCompare(strs[i - 1], strs[i]) <= 0, "localeCompare order " + JsonValue.String(strs[i - 1]).ToJson() + " before " + JsonValue.String(strs[i]).ToJson());
                foreach (JsonValue o in x["order"].Items)
                    Assert.Equal((int)o[1].Long, strs.Count(s => JsText.LocaleCompare(s, o[0].Str) == 0), "localeCompare equal to " + o[0].ToJson());
                foreach (JsonValue p in x["pairs"].Items)
                    Assert.Equal(Math.Sign(p[2].Num), JsText.LocaleCompare(p[0].Str, p[1].Str), "localeCompare " + p[0].ToJson() + " " + p[1].ToJson());
                foreach (JsonValue u in x["uris"].Items) Assert.Equal(u["uri"].Str, JsText.EncodeUriComponent(u["text"].Str), "encodeURIComponent " + u["text"].ToJson());
            });
        }
    }
}
