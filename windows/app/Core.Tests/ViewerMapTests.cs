using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// SentrySix.Core's map (ViewerMap.cs: MapProviders, MapSettings,
    /// MapTileState, LeafletView, MapMarker, MapRoute, ViewerMap) against
    /// Studio's own mapProviders.js, mapTiles.js, mapVisualization.js,
    /// script.js's route, event pin, route fit, recenterMap, right-button
    /// drag and orientation button, and Leaflet's own view methods, on the
    /// same steps (Fixtures/expected-viewer-map.json, written by
    /// test/windowsNativeViewerMap.test.js).
    /// </summary>
    static class ViewerMapTests
    {
        static JsonValue Expected() =>
            JsonValue.Parse(File.ReadAllText(Path.Combine(NodeServer.RepoRoot(), "windows", "app", "Core.Tests", "Fixtures", "expected-viewer-map.json")));

        static void Near(double want, double got, string what)
        {
            double tol = 1e-9 * Math.Max(1, Math.Abs(want));
            if (!(Math.Abs(want - got) <= tol)) throw new AssertFailed(what + ": expected <" + Format.JsNum(want) + ">, got <" + Format.JsNum(got) + ">");
        }

        static void View(JsonValue want, LeafletView v, string what)
        {
            Near(want["lat"].Num, v.Center.Lat, what + " lat");
            Near(want["lng"].Num, v.Center.Lng, what + " lng");
            Assert.Equal(want["zoom"].Num, v.Zoom, what + " zoom");
            PixelPoint o = v.PixelOrigin;
            Assert.Equal(want["origin"][0].Num, o.X, what + " origin x");
            Assert.Equal(want["origin"][1].Num, o.Y, what + " origin y");
        }

        static List<MapPoint> PathOf(JsonValue p)
        {
            List<MapPoint> l = new List<MapPoint>();
            foreach (JsonValue x in p.Items) l.Add(new MapPoint { Lat = x["lat"].Num, Lon = x["lon"].Num, Autopilot = x["autopilot"].Bool });
            return l;
        }

        static string Deg(double d) { return Format.JsNum(d == 0 ? 0 : d); }

        public static void Register(Action<string, Action> test)
        {
            test("viewer map: a two-finger pinch zooms by log2 of its scale, snapped and kept within the limits, about its centre", () =>
            {
                ViewerMap map = new ViewerMap();
                map.View.SetView(new GeoPoint(37.77, -122.42), 12);
                PixelPoint c = new PixelPoint(129, 129);
                GeoPoint before = map.View.ContainerToLatLng(c);
                map.Pinch(12, 2.2, c);
                Assert.Equal(13.0, map.View.Zoom, "2.2x: one level in");
                map.Pinch(13, 0.2, c);
                Assert.Equal(11.0, map.View.Zoom, "0.2x: log2 -2.3 snaps to two out");
                map.Pinch(11, 1.3, c);
                Assert.Equal(11.0, map.View.Zoom, "1.3x: less than half a level, no change");
                GeoPoint after = map.View.ContainerToLatLng(c);
                Assert.True(Math.Abs(after.Lat - before.Lat) < 1e-3 && Math.Abs(after.Lng - before.Lng) < 1e-3, "the centre stays put");
                map.View.SetMaxZoom(18);
                map.Pinch(11, 1e6, c);
                Assert.Equal(18.0, map.View.Zoom, "at most the layer's max zoom");
            });

            test("viewer map: providers, templates and tile URLs as mapProviders.js builds them", () =>
            {
                foreach (JsonValue r in Expected()["providers"].Items)
                {
                    string id = r["id"].IsNull ? null : r["id"].Str;
                    MapProvider p = MapProviders.Get(id);
                    string w = "provider " + (id ?? "undefined");
                    Assert.Equal(r["provider"].Str, p.Id, w);
                    Assert.Equal(r["label"].Str, p.Label, w + " label");
                    Assert.Equal((int)r["maxZoom"].Num, p.MaxZoom, w + " maxZoom");
                    Assert.Equal(string.Join(",", r["subdomains"].Items.Select(x => x.Str)), string.Join(",", p.Subdomains), w + " subdomains");
                    Assert.Equal(r["isGoogle"].Bool, MapProviders.IsGoogle(id), w + " isGoogle");
                    Assert.Equal(r["nativeDark"].Bool, MapProviders.HasNativeDark(id), w + " nativeDark");
                    Assert.Equal(r["darkFilter"].Bool, MapProviders.WantsDarkFilter(id), w + " darkFilter");
                    foreach (JsonValue t in r["templates"].Items)
                        Assert.Equal(t["url"].Str, MapProviders.UrlTemplate(id, t["labels"].Bool, t["dark"].Bool), w + " template");
                    foreach (JsonValue u in r["urls"].Items)
                        Assert.Equal(u["url"].Str, MapProviders.TileUrl(id, (int)u["x"].Num, (int)u["y"].Num, (int)u["z"].Num, (int)u["i"].Num, u["labels"].Bool, false), w + " url");
                }
            });

            test("viewer map: the session's provider and its OpenStreetMap fallback as mapTiles.js keeps them", () =>
            {
                int n = 0;
                foreach (JsonValue sc in Expected()["tiles"].Items)
                {
                    n++;
                    MapTileState st = new MapTileState();
                    int notices = 0, layers = 0;
                    bool attached = false, probing = false;
                    st.FellBack += () => notices++;
                    st.LayersChanged += () => { if (attached) layers++; };
                    MapSettings ms = MapSettings.From(sc["settings"]);
                    // (the dark step before attach: window._mapDarkMode set, no layer yet; the settings load with attach)
                    bool darkBefore = false, darkSet = false;
                    int i = 0;
                    foreach (JsonValue s in sc["steps"].Items)
                    {
                        i++;
                        string w = "scenario " + n + " step " + i + " " + s["op"].Str;
                        string op = s["op"].Str;
                        if (op == "attach")
                        {
                            st.Load(ms);
                            if (darkSet) st.Dark = darkBefore;
                            attached = true;
                            if (st.Attach()) probing = true;
                        }
                        else if (op == "error") { if (attached) st.TileFailed(st.Generation); }
                        else if (op == "load") { if (attached) st.TileLoaded(st.Generation); }
                        else if (op == "probe") { if (probing) { probing = false; st.ProbeAnswered(s["value"].Bool); } }
                        else if (op == "provider") { if (st.SetProvider(s["value"].Str)) probing = true; }
                        else if (op == "labels") st.SetLabels(s["value"].Bool);
                        else if (op == "dark")
                        {
                            if (attached) st.SetDark(s["value"].Bool);
                            else { darkSet = true; darkBefore = s["value"].Bool; }
                        }
                        Assert.Equal(s["effective"].Str, st.EffectiveId, w + " effective");
                        Assert.Equal(s["selected"].Str, st.SelectedId, w + " selected");
                        Assert.Equal(s["labels"].Bool, st.Labels, w + " labels");
                        Assert.Equal(s["url"].IsNull ? null : s["url"].Str, attached ? st.Template : null, w + " url");
                        Assert.Equal(s["maxZoom"].IsNull ? -1 : (int)s["maxZoom"].Num, attached ? st.MaxZoom : -1, w + " maxZoom");
                        Assert.Equal((int)s["layers"].Num, layers, w + " layers");
                        Assert.Equal((int)s["notices"].Num, notices, w + " notices");
                        Assert.Equal(s["probing"].Bool, probing, w + " probing");
                    }
                }
            });

            test("viewer map: the arrow, the turn and the orientation button as mapVisualization.js and script.js leave them", () =>
            {
                LeafletView v = new LeafletView();
                v.SetView(new GeoPoint(30.2672, -97.7431), 16);
                MapMarker m = new MapMarker();
                bool toggled = false;
                int i = 0;
                foreach (JsonValue s in Expected()["marker"].Items)
                {
                    i++;
                    string op = s["op"].Str;
                    string w = "step " + i + " " + op;
                    bool setView = false;
                    if (op == "sei")
                    {
                        JsonValue a = s["value"];
                        Sei sei = new Sei { LatitudeDeg = a["lat"].Num, LongitudeDeg = a["lon"].Num, HeadingDeg = a["heading"].Num };
                        setView = m.Update(sei, v, s["rate"].Num);
                    }
                    else if (op == "zoom") { v.SetView(v.Center, s["value"].Num); m.Zoomed(v.Zoom); setView = true; }
                    else if (op == "toggle") { m.Toggle(); toggled = true; }
                    else if (op == "clear") m.Clear();
                    Assert.Equal(s["shown"].Bool, m.Shown, w + " shown");
                    if (m.Shown)
                    {
                        Near(s["at"][0].Num, m.At.Lat, w + " at lat");
                        Near(s["at"][1].Num, m.At.Lng, w + " at lng");
                        Assert.Equal(s["arrowSize"].Num, m.ArrowSize, w + " arrow size");
                        Near(s["arrowRotation"].Num, m.ArrowRotation, w + " arrow rotation");
                    }
                    Assert.Equal(s["container"].Str, "rotate(" + Deg(m.ContainerRotation) + "deg) scale(" + Format.JsNum(m.ContainerScale) + ")", w + " container");
                    Assert.Equal(s["transition"].Str, m.ContainerTransition > 0 ? "transform " + Format.JsNum(m.ContainerTransition) + "s ease-out" : "", w + " transition");
                    Assert.Equal(s["setView"].Bool, setView, w + " setView");
                    View(s["view"], v, w + " view");
                    if (s["current"].IsNull) Assert.True(m.Current == null, w + " no current");
                    else
                    {
                        Assert.True(m.Current != null, w + " current");
                        Near(s["current"][0].Num, m.Current.Value.Lat, w + " current lat");
                        Near(s["current"][1].Num, m.Current.Value.Lng, w + " current lng");
                    }
                    Near(s["bearing"].Num, m.MapBearing, w + " bearing");
                    Assert.Equal(s["orientation"].Str, m.Orientation, w + " orientation");
                    Near(s["upright"].Num, m.UprightRotation, w + " upright");
                    if (s["compass"].IsNull) Assert.True(m.Compass == null, w + " no compass");
                    else Near(s["compass"].Num, m.Compass ?? double.NaN, w + " compass");
                    Assert.Equal(s["title"].Str, toggled ? m.ToggleTitle : "Toggle heading-up / north-up", w + " title");
                }
            });

            test("viewer map: route stretches, event pin, route fit and re-center as script.js draws them", () =>
            {
                JsonValue x = Expected();
                JsonValue paths = x["paths"];
                foreach (KeyValuePair<string, JsonValue> kv in x["segments"].Props)
                {
                    List<MapRouteSegment> segs = MapRoute.Segments(PathOf(paths[kv.Key]));
                    Assert.Equal(kv.Value.Count, segs.Count, kv.Key + " segments");
                    for (int i = 0; i < segs.Count; i++)
                    {
                        JsonValue ws = kv.Value[i];
                        Assert.Equal(ws["autopilot"].Bool, segs[i].Autopilot, kv.Key + " segment " + i);
                        Assert.Equal(ws["coords"].Count, segs[i].Points.Count, kv.Key + " segment " + i + " points");
                        for (int j = 0; j < segs[i].Points.Count; j++)
                        {
                            Assert.Equal(ws["coords"][j][0].Num, segs[i].Points[j].Lat, kv.Key + " lat");
                            Assert.Equal(ws["coords"][j][1].Num, segs[i].Points[j].Lng, kv.Key + " lng");
                        }
                    }
                }
                int n = 0;
                foreach (JsonValue e in x["events"].Items)
                {
                    n++;
                    string w = "event " + n;
                    ViewerMap map = new ViewerMap();
                    if (!e["path"].IsNull) map.Path = PathOf(paths[e["path"].Str]);
                    map.ShowEvent(e["meta"].IsNull ? null : e["meta"], e["recenter"].Bool);
                    if (e["pin"].IsNull) Assert.True(map.EventPin == null, w + " no pin");
                    else
                    {
                        Assert.True(map.EventPin != null, w + " pin");
                        Near(e["pin"][0].Num, map.EventPin.Value.Lat, w + " pin lat");
                        Near(e["pin"][1].Num, map.EventPin.Value.Lng, w + " pin lng");
                    }
                    View(e["view"], map.View, w);
                }
                foreach (JsonValue f in x["fits"].Items)
                {
                    ViewerMap map = new ViewerMap();
                    map.LayerMaxZoom(f["maxZoom"].Num);
                    map.PathLoaded(PathOf(paths[f["path"].Str]), null);
                    View(f["view"], map.View, "fit " + f["path"].Str + " " + f["maxZoom"].Num);
                }
                n = 0;
                foreach (JsonValue r in x["recenters"].Items)
                {
                    n++;
                    ViewerMap map = new ViewerMap();
                    map.View.SetView(new GeoPoint(10, 10), r["zoom"].Num);
                    if (r.Has("pin")) map.EventPin = new GeoPoint(r["pin"][0].Num, r["pin"][1].Num);
                    if (r.Has("current")) map.Marker.Current = new GeoPoint(r["current"][0].Num, r["current"][1].Num);
                    if (r.Has("polyline")) map.Route = MapRoute.Segments(PathOf(paths[r["polyline"].Str]));
                    if (r.Has("mapPath")) map.Path = r["mapPath"].IsArray ? PathOf(r["mapPath"]) : PathOf(paths[r["mapPath"].Str]);
                    if (r.Has("lastPath")) map.LastPath = PathOf(paths[r["lastPath"].Str]);
                    if (r.Has("lastBounds"))
                    {
                        GeoBounds b = new GeoBounds();
                        foreach (MapPoint p in PathOf(paths[r["lastBounds"].Str])) b = b.Extend(new GeoPoint(p.Lat, p.Lon));
                        map.LastBounds = b;
                    }
                    map.Recenter();
                    View(r["view"], map.View, "recenter " + n);
                }
            });

            test("viewer map: Leaflet's drag, wheel, double click and max zoom on the view", () =>
            {
                LeafletView v = new LeafletView();
                v.SetView(new GeoPoint(0, 0), 2);
                GeoPoint[] probes = { new GeoPoint(30.2672, -97.7431), new GeoPoint(30.2681, -97.742), new GeoPoint(-33.8688197, 151.2092955), new GeoPoint(0, 0) };
                int i = 0;
                foreach (JsonValue s in Expected()["views"].Items)
                {
                    i++;
                    string op = s["op"].Str;
                    JsonValue a = s["a"], b = s["b"];
                    if (op == "view") v.SetView(new GeoPoint(a[0].Num, a[1].Num), b.Num);
                    else if (op == "drag") v.SetView(ViewerMap.DragCenter(v, v.Center, b[0].Num, b[1].Num, a.Num), v.Zoom);
                    else if (op == "wheel")
                    {
                        double d = v.WheelZoomDelta(a.Num);
                        if (d != 0) v.SetZoomAround(new PixelPoint(b[0].Num, b[1].Num), v.Zoom + d);
                    }
                    else if (op == "dblclick") v.SetZoomAround(new PixelPoint(a[0].Num, a[1].Num), v.Zoom + (b.Bool ? -1 : 1));
                    else if (op == "maxZoom") v.SetMaxZoom(a.Num);
                    string w = "view step " + i + " " + op;
                    View(s["view"], v, w);
                    for (int k = 0; k < probes.Length; k++)
                    {
                        PixelPoint c = v.ContainerPoint(probes[k]);
                        Assert.Equal(s["points"][k][0].Num, c.X, w + " point " + k + " x");
                        Assert.Equal(s["points"][k][1].Num, c.Y, w + " point " + k + " y");
                    }
                }
            });

            test("viewer map: Settings > Map as Studio reads it", () =>
            {
                MapSettings d = MapSettings.From(JsonValue.Object());
                Assert.True(d.Enabled && d.Provider == "google" && !d.Dark && d.Labels, "defaults");
                MapSettings s = MapSettings.From(JsonValue.Parse("{\"mapEnabled\":false,\"mapTileProvider\":\"google-satellite\",\"mapDarkMode\":true,\"mapLabels\":0}"));
                Assert.True(!s.Enabled && s.Provider == "google-satellite" && s.Dark && !s.Labels, "saved");
                Assert.Equal("google", MapSettings.ProviderChoice("google-satellite"), "provider list");
                Assert.Equal("satellite", MapSettings.StyleChoice("google-satellite", true), "style list");
                Assert.Equal("dark", MapSettings.StyleChoice("osm", true), "style list dark");
                Assert.True(!MapSettings.SatelliteOffered("osm") && MapSettings.SatelliteOffered("google"), "satellite offered");
                Assert.Equal(false, MapSettings.From(JsonValue.Parse("{\"mapEnabled\":null}")).Enabled, "null is off");
            });
        }
    }
}
