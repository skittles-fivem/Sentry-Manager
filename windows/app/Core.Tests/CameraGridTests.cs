using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's MultiLayouts, TeslaHardware, GridLayoutState,
    /// CameraOrders, CameraMirror and EventCameraGlow (the Viewer's camera
    /// grid) against Studio's own multiLayouts.js, playbackLayout.js,
    /// autoTeslaHardware, cameraRearrange.js, applyMirrorTransforms and
    /// updateEventCameraHighlight on the same steps
    /// (Fixtures/expected-camera-grid.json, written by
    /// test/windowsNativeCameraGrid.test.js).
    /// </summary>
    static class CameraGridTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-camera-grid.json")));

        static void Same(JsonValue want, JsonValue got, string what)
        {
            string a = want.ToJson(), c = got.ToJson();
            if (a == c) return;
            int i = 0;
            while (i < a.Length && i < c.Length && a[i] == c[i]) i++;
            int from = Math.Max(0, i - 120);
            throw new Exception(what + ": first difference at " + i + "\n  web:    …" + a.Substring(from, Math.Min(a.Length - from, 300)) + "\n  native: …" + c.Substring(from, Math.Min(c.Length - from, 300)));
        }

        static string Str(JsonValue v) => v == null || v.IsNull ? null : v.Str;

        static JsonValue Nums(IEnumerable<double> d) => JsonValue.Array(d.Select(Num));

        /// <summary>A number as JavaScript prints it (JsonValue.Number's "R" is not the shortest on every runtime).</summary>
        static JsonValue Num(double x) => JsonValue.Parse(Format.JsNum(x));

        static JsonValue Text(string s) => s == null ? JsonValue.Null : JsonValue.String(s);

        static void Layouts(JsonValue x)
        {
            JsonValue got = JsonValue.Object();
            foreach (MultiLayout l in MultiLayouts.All)
                got = got.With(l.Id, JsonValue.Object("name", l.Name, "columns", l.Columns,
                    "slots", JsonValue.Array(l.Slots.Select(s => JsonValue.Object("slot", s.Slot, "camera", Text(s.Camera), "label", s.Label)))));
            Same(x["layouts"], got, "MULTI_LAYOUTS");
            foreach (JsonValue h in x["hardware"].Items)
            {
                string stored = Str(h["stored"]);
                string mode = TeslaHardware.Read(stored);
                Assert.Equal(h["mode"].Str, mode, "getTeslaHardware " + stored);
                Assert.Equal(h["layoutId"].Str, TeslaHardware.LayoutId(mode), "teslaLayoutId " + stored);
                Assert.Equal(h["set"].Str, TeslaHardware.Normalize(stored), "setTeslaHardware " + stored);
            }
            foreach (JsonValue a in x["auto"].Items)
            {
                List<ICollection<string>> groups = a["groups"].Items.Select(g => (ICollection<string>)g.Items.Select(c => c.Str).ToList()).ToList();
                string mode = TeslaHardware.Auto(Str(a["profile"]), groups, Str(a["stored"]), a["layoutId"].Str);
                string what = "autoTeslaHardware " + a.ToJson();
                if (a["calls"].Count == 0)
                {
                    Assert.Equal<string>(null, mode, what);
                    continue;
                }
                Assert.True(mode != null, what + ": changes the layout");
                Assert.Equal(a["calls"][1][1].Str, TeslaHardware.LayoutId(mode), what + " layout");
                Assert.Equal(a["after"].Str, TeslaHardware.Normalize(mode), what + " stored");
            }
        }

        static void Grid(JsonValue g)
        {
            double w = g["width"].Num, h = g["height"].Num;
            foreach (JsonValue c in g["cases"].Items)
            {
                Dictionary<string, string> stored = new Dictionary<string, string>();
                string sizes = Str(c["sizes"]), free = Str(c["free"]);
                if (sizes != null) stored[GridSizes.Key] = sizes;
                if (free != null) stored[FreeLayouts.Key] = free;
                GridLayoutState st = new GridLayoutState(sizes, free, (k, v) => stored[k] = v);
                st.Apply(c["layout"][0].Str, (int)c["layout"][1].Long);
                string name = c["name"].Str;
                Shot(c["shots"][0], st, stored, name + " at start");
                int i = 1;
                foreach (JsonValue s in c["steps"].Items)
                {
                    string op = s["op"].Str;
                    if (op == "divider")
                    {
                        bool cols = s["axis"].Str == "columns";
                        st.BeginDivider(cols, (int)s["index"].Long, s["from"].Num, cols ? w : h);
                        foreach (JsonValue m in s["moves"].Items) st.MoveDivider(m.Num);
                        st.EndDivider();
                    }
                    else if (op == "reset") st.ResetSizes();
                    else if (op == "layout") st.Apply(s["id"].Str, (int)s["columns"].Long);
                    else if (op == "free")
                    {
                        if (st.BeginFree(s["slot"].Str, s["mode"].Str == "resize", s["from"][0].Num, s["from"][1].Num, w, h))
                        {
                            foreach (JsonValue m in s["moves"].Items) st.MoveFree(m[0].Num, m[1].Num);
                            st.EndFree();
                        }
                    }
                    else if (op == "resetFree") st.ResetFree();
                    Shot(c["shots"][i], st, stored, name + " after step " + i + " " + s.ToJson());
                    i++;
                }
            }
        }

        static void Shot(JsonValue want, GridLayoutState st, Dictionary<string, string> stored, string what)
        {
            JsonValue tiles = JsonValue.Object();
            if (st.Painted != null)
                foreach (string slot in GridGeometry.Slots)
                {
                    FreeBox b = st.Painted.Boxes.Has(slot) ? st.Painted.Boxes.Get(slot) : null;
                    tiles = tiles.With(slot, b == null ? JsonValue.Null : JsonValue.Object("x", Num(b.X), "y", Num(b.Y), "w", Num(b.W), "h", Num(b.H), "z", st.ZIndex(slot)));
                }
            string s, f;
            stored.TryGetValue(GridSizes.Key, out s);
            stored.TryGetValue(FreeLayouts.Key, out f);
            JsonValue got = JsonValue.Object("columns", Nums(st.Tracks.Columns), "rows", Nums(st.Tracks.Rows), "free", st.Painted != null, "tiles", tiles, "sizes", Text(s), "freeStored", Text(f));
            Same(want, got, what);
        }

        static JsonValue Undef(JsonValue v) => v != null && v.IsString && v.Str == "$undefined" ? null : v;

        static CameraOrders Load(JsonValue settings, List<string> saves)
        {
            bool move;
            CameraOrders o = CameraOrders.Load(settings.Has(CameraOrders.Setting) ? settings[CameraOrders.Setting] : null, settings.Has(CameraOrders.LegacySetting) ? settings[CameraOrders.LegacySetting] : null, out move);
            if (move) saves.Add(o.ToSetting().ToJson());
            return o;
        }

        static void Orders(JsonValue x)
        {
            List<JsonValue> mirrorValues = x["mirrorValues"].Items.ToList();
            foreach (JsonValue c in x["orders"].Items)
            {
                List<string> saves = new List<string>();
                List<string> notices = new List<string>();
                CameraOrders o = Load(c["settings"], saves);
                string profile = c["profile"].Str, layout = c["layoutId"].Str;
                Snap(c["shots"][0], o, profile, layout, saves, notices, mirrorValues, c["name"].Str + " at start");
                int i = 1;
                foreach (JsonValue s in c["steps"].Items)
                {
                    if (s.Has("drop")) { if (o.Drop(profile, layout, s["drop"][0].Str, s["drop"][1].Str)) saves.Add(o.ToSetting().ToJson()); }
                    else if (s.Has("reset")) { o.Reset(profile, layout); saves.Add(o.ToSetting().ToJson()); notices.Add(CameraOrders.ResetNotice); }
                    else if (s.Has("layout")) layout = s["layout"].Str;
                    Snap(c["shots"][i], o, profile, layout, saves, notices, mirrorValues, c["name"].Str + " after " + s.ToJson());
                    i++;
                }
            }
        }

        static void Snap(JsonValue want, CameraOrders o, string profile, string layout, List<string> saves, List<string> notices, List<JsonValue> mirrorValues, string what)
        {
            List<MultiSlot> eff = o.Effective(profile, layout);
            Assert.Equal(want["layoutId"].Str, layout, what + " layout");
            JsonValue effective = JsonValue.Array(eff.Select(s => JsonValue.Object("slot", s.Slot,
                "camera", s.Raw == null ? Text(s.Camera) : ReferenceEquals(s.Raw, JsJson.Undefined) ? JsonValue.String("$undefined") : s.Raw)));
            Same(want["effective"], effective, what + " getEffectiveSlots");
            JsonValue labels = JsonValue.Object();
            foreach (MultiSlot s in eff) labels = labels.With(s.Slot, CameraOrders.TileLabel(profile, s));
            Same(want["labels"], labels, what + " tile labels");
            JsonValue mirrors = JsonValue.Array(mirrorValues.Select(m =>
            {
                bool on = CameraMirror.On(Undef(m));
                return JsonValue.Array(eff.Where(s => CameraMirror.Mirrored(on, profile, s.Camera)).Select(s => JsonValue.String(s.Slot)));
            }));
            Same(want["mirrors"], mirrors, what + " mirrored");
            Same(want["saved"], JsonValue.Array(saves.Select(v => JsonValue.Array(new[] { JsonValue.String(CameraOrders.Setting), JsonValue.String(v) }))), what + " saved settings");
            Same(want["notices"], JsonValue.Array(notices.Select(n => JsonValue.Object("message", n, "type", "info"))), what + " notices");
        }

        static void Glow(JsonValue x)
        {
            foreach (JsonValue c in x["glow"].Items)
            {
                List<string> saves = new List<string>();
                JsonValue settings = c.Has("orders") ? JsonValue.Object(CameraOrders.Setting, c["orders"]) : JsonValue.Object();
                CameraOrders o = Load(settings, saves);
                string profile = c.Has("profile") ? c["profile"].Str : "tesla";
                string layout = c.Has("layoutId") ? c["layoutId"].Str : "six_default";
                JsonValue byKey = c.Has("byKey") ? c["byKey"] : JsonValue.Object();
                KeyValuePair<string, string>? lit = EventCameraGlow.Of(Str(c["tag"]), Str(c["eventId"]), c["metas"].Items,
                    k => byKey.Has(k) ? byKey[k] : null,
                    CameraMirror.On(Undef(c["sentry"])), CameraMirror.On(Undef(c["saved"])), o.Effective(profile, layout));
                JsonValue got = lit == null ? JsonValue.Array(new JsonValue[0]) : JsonValue.Array(new[] { JsonValue.Object("slot", lit.Value.Key, "cls", lit.Value.Value) });
                Same(c["lit"], got, "updateEventCameraHighlight " + c.ToJson());
            }
        }

        public static void Register(Action<string, Action> test)
        {
            test("camera grid: MULTI_LAYOUTS, the HW4 / HW3 switch and autoTeslaHardware", () => Layouts(Expected()));
            test("camera grid: the dividers' sizes and the free layout per layout, as playbackLayout.js", () => Grid(Expected()["grid"]));
            test("camera grid: swapped cameras, their labels, Reset Camera Order and Mirror Cameras", () => Orders(Expected()));
            test("camera grid: the Sentry / Saved event camera's glow", () => Glow(Expected()));
        }
    }
}
