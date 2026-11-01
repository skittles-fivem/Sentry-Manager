using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SentrySix.Core
{
    // The Viewer's map (#mapVis, Viewer part 10): Studio's tile providers
    // (src/shared/mapProviders.js), the session's provider and its Google ->
    // OpenStreetMap fallback (scripts/ui/mapTiles.js), the map's view as
    // Leaflet keeps it (Web Mercator, 256 px tiles, fitBounds, setZoomAround,
    // the scroll wheel's steps), the heading marker (scripts/ui/mapVisualization.js)
    // and script.js's map: the route drawn per autopilot stretch, the start
    // and end dots, the Sentry / Saved event pin snapped to the route, the
    // re-center button, the right-button drag and the settings. Drawn by
    // Ui/Builtin/ViewerMap.cs; the tiles come from the viewer's PC as in the
    // browser (nothing new in the container).

    /// <summary>One tile provider of mapProviders.js's PROVIDERS.</summary>
    public sealed class MapProvider
    {
        public string Id;
        public string Label;
        public string UrlTemplate;
        public string[] Subdomains;
        public int MaxZoom;
        public string Attribution;
        /// <summary>It has an apistyle builder (googleApistyle): curated labels and a night palette in the tile.</summary>
        public bool Styled;
        /// <summary>Real photography (satellite): never inverted for dark mode.</summary>
        public bool Photographic;
    }

    /// <summary>src/shared/mapProviders.js: the single source of truth for every map's tiles.</summary>
    public static class MapProviders
    {
        public const string DefaultProviderId = "google";
        public const string FallbackProviderId = "osm";
        public const string ProviderSettingKey = "mapTileProvider";
        public const string LabelsSettingKey = "mapLabels";
        public const string DarkSettingKey = "mapDarkMode";
        public const bool DefaultLabelsEnabled = true;
        public const string GoogleProbeUrl = "https://mt1.google.com/vt/lyrs=m&x=0&y=0&z=0";

        static readonly MapProvider Google = new MapProvider
        {
            Id = "google", Label = "Google Maps", UrlTemplate = "https://{s}.google.com/vt/lyrs=m&x={x}&y={y}&z={z}",
            Styled = true, Subdomains = new[] { "mt0", "mt1", "mt2", "mt3" }, MaxZoom = 20, Attribution = "© Google"
        };
        static readonly MapProvider Satellite = new MapProvider
        {
            Id = "google-satellite", Label = "Google Satellite", UrlTemplate = "https://{s}.google.com/vt/lyrs=s&x={x}&y={y}&z={z}",
            Photographic = true, Subdomains = new[] { "mt0", "mt1", "mt2", "mt3" }, MaxZoom = 20, Attribution = "© Google"
        };
        static readonly MapProvider Osm = new MapProvider
        {
            Id = "osm", Label = "OpenStreetMap", UrlTemplate = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
            Subdomains = new[] { "a", "b", "c" }, MaxZoom = 19, Attribution = "© OpenStreetMap contributors"
        };

        /// <summary>PROVIDERS in their order.</summary>
        public static readonly MapProvider[] All = { Google, Satellite, Osm };

        // Google's legacy apistyle (as Sentry Drive styles it): every label off, then the
        // administrative and road labels back on; the night palette in dark mode
        const string LabelsOff = "s.e%3Al%7Cp.v%3Aoff";
        static readonly string[] CuratedLabels = { "s.t%3A1%7Cs.e%3Al%7Cp.v%3Aon", "s.t%3A3%7Cs.e%3Al%7Cp.v%3Aon", "s.t%3A49%7Cs.e%3Al%7Cp.v%3Aon" };
        const string Night = "s.e%3Ag%7Cp.c%3A%23242f3e,s.e%3Al.t.f%7Cp.c%3A%23ffffff,s.e%3Al.t.s%7Cp.c%3A%23242f3e,s.t%3A37%7Cs.e%3Ag%7Cp.c%3A%23263c3f,s.t%3A81%7Cs.e%3Ag%7Cp.c%3A%232b3645,s.t%3A6%7Cs.e%3Ag%7Cp.c%3A%2317263c,s.t%3A3%7Cs.e%3Ag%7Cp.c%3A%2338414e,s.t%3A3%7Cs.e%3Ag.s%7Cp.c%3A%23212a37,s.t%3A49%7Cs.e%3Ag%7Cp.c%3A%235f6b7c,s.t%3A49%7Cs.e%3Ag.f%7Cp.c%3A%235f6b7c,s.t%3A49%7Cs.e%3Ag.s%7Cp.c%3A%232a3340,s.t%3A4%7Cs.e%3Ag%7Cp.c%3A%232f3948";

        /// <summary>getProvider(id): the provider, else Google.</summary>
        public static MapProvider Get(string id)
        {
            foreach (MapProvider p in All) if (p.Id == id) return p;
            return Google;
        }

        /// <summary>isGoogleProvider(id).</summary>
        public static bool IsGoogle(string id)
        {
            return Get(id).Id.StartsWith("google", StringComparison.Ordinal);
        }

        /// <summary>hasNativeDark(id): the night palette is baked into the tiles (no invert on top).</summary>
        public static bool HasNativeDark(string id)
        {
            return Get(id).Styled;
        }

        /// <summary>wantsDarkFilter(id): dark mode faked with the CSS invert (drawn maps with no night style: OSM).</summary>
        public static bool WantsDarkFilter(string id)
        {
            MapProvider p = Get(id);
            return !p.Styled && !p.Photographic;
        }

        /// <summary>googleApistyle({ labels, dark }).</summary>
        public static string GoogleApistyle(bool labels, bool dark)
        {
            List<string> rules = new List<string>();
            if (dark) rules.Add(Night);
            rules.Add(LabelsOff);
            if (labels) rules.AddRange(CuratedLabels);
            return string.Join(",", rules);
        }

        /// <summary>getUrlTemplate(id, { labels, dark }): the template with {s}/{x}/{y}/{z} kept.</summary>
        public static string UrlTemplate(string id, bool labels, bool dark)
        {
            MapProvider p = Get(id);
            if (!p.Styled) return p.UrlTemplate;
            string style = GoogleApistyle(labels, dark);
            return style.Length > 0 ? p.UrlTemplate + "&apistyle=" + style : p.UrlTemplate;
        }

        /// <summary>buildTileUrl(id, x, y, zoom, requestIndex, { labels }) (and the dark palette, as Leaflet's layer has it).</summary>
        public static string TileUrl(string id, int x, int y, int zoom, int requestIndex, bool labels, bool dark)
        {
            MapProvider p = Get(id);
            string s = p.Subdomains[((requestIndex % p.Subdomains.Length) + p.Subdomains.Length) % p.Subdomains.Length];
            return ReplaceFirst(ReplaceFirst(ReplaceFirst(ReplaceFirst(UrlTemplate(id, labels, dark), "{s}", s), "{x}", Int(x)), "{y}", Int(y)), "{z}", Int(zoom));
        }

        /// <summary>
        /// The tile Leaflet's layer asks for at (x, y, z): its {s} is
        /// subdomains[|x + y| % n] (TileLayer.getSubdomain) and x wraps
        /// around the world (noWrap is off).
        /// </summary>
        public static string LayerTileUrl(string id, int x, int y, int zoom, bool labels, bool dark)
        {
            MapProvider p = Get(id);
            int n = 1 << zoom;
            int wx = ((x % n) + n) % n;
            int index = Math.Abs(wx + y) % p.Subdomains.Length;
            return ReplaceFirst(ReplaceFirst(ReplaceFirst(ReplaceFirst(UrlTemplate(id, labels, dark), "{s}", p.Subdomains[index]), "{x}", Int(wx)), "{y}", Int(y)), "{z}", Int(zoom));
        }

        static string Int(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        static string ReplaceFirst(string s, string what, string with)
        {
            int i = s.IndexOf(what, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + with + s.Substring(i + what.Length);
        }
    }

    /// <summary>
    /// Settings > Map as Studio reads it: GPS Map (mapEnabled), Map Provider
    /// and Map Style (mapTileProvider, mapDarkMode; satellite is the
    /// google-satellite provider) and Map Labels (mapLabels).
    /// </summary>
    public sealed class MapSettings
    {
        /// <summary>state.ui.mapEnabled: on unless saved as something other than true.</summary>
        public bool Enabled = true;
        /// <summary>The saved provider (getProvider(saved).id).</summary>
        public string Provider = MapProviders.DefaultProviderId;
        /// <summary>window._mapDarkMode: getSetting('mapDarkMode') === true.</summary>
        public bool Dark;
        /// <summary>Map Labels (default on).</summary>
        public bool Labels = MapProviders.DefaultLabelsEnabled;

        public static MapSettings From(JsonValue all)
        {
            MapSettings s = new MapSettings();
            JsonValue v = JsJson.Get(all, "mapEnabled");
            s.Enabled = v == null || (v.IsBool && v.Bool);
            v = JsJson.Get(all, MapProviders.ProviderSettingKey);
            s.Provider = MapProviders.Get(v != null && v.IsString ? v.Str : null).Id;
            v = JsJson.Get(all, MapProviders.DarkSettingKey);
            s.Dark = v != null && v.IsBool && v.Bool;
            v = JsJson.Get(all, MapProviders.LabelsSettingKey);
            s.Labels = v == null || v.IsNull ? MapProviders.DefaultLabelsEnabled : JsJson.Truthy(v);
            return s;
        }

        /// <summary>syncMapSettingsUI: the Map Provider list's value (google for satellite).</summary>
        public static string ProviderChoice(string providerId)
        {
            return providerId == "google-satellite" ? "google" : providerId;
        }

        /// <summary>syncMapSettingsUI: the Map Style list's value.</summary>
        public static string StyleChoice(string providerId, bool dark)
        {
            return providerId == "google-satellite" ? "satellite" : dark ? "dark" : "light";
        }

        /// <summary>The Satellite style is hidden while OpenStreetMap is the provider.</summary>
        public static bool SatelliteOffered(string providerId)
        {
            return ProviderChoice(providerId) != "osm";
        }
    }

    /// <summary>
    /// scripts/ui/mapTiles.js: which provider the map uses this session.
    /// Google is given up for OpenStreetMap (until the provider is picked
    /// again; the saved setting stays) after 3 tile errors before any Google
    /// tile loaded, or when the probe tile fails and none loaded.
    /// </summary>
    public sealed class MapTileState
    {
        /// <summary>ui.settings.mapProviderFallbackNotice (a 6 s warning).</summary>
        public const string FallbackNotice = "Google Maps unavailable — using OpenStreetMap";
        public const int FallbackNoticeMs = 6000;
        public const int ErrorsBeforeFallback = 3;
        public const int ProbeTimeoutMs = 5000;

        string selected;
        bool? labels;
        bool fallback;
        int errors;
        bool tileLoaded;
        bool probeStarted;
        int generation;

        /// <summary>The fallback happened (the toast; the layers are made again).</summary>
        public event Action FellBack;
        /// <summary>The tile layer is made again (provider, labels or dark mode changed).</summary>
        public event Action LayersChanged;

        /// <summary>window._mapDarkMode.</summary>
        public bool Dark;

        /// <summary>The saved setting is in (loadSelectedProvider).</summary>
        public bool Loaded { get { return selected != null; } }
        public bool SessionFallback { get { return fallback; } }
        public int GoogleTileErrors { get { return errors; } }
        public bool GoogleTileLoaded { get { return tileLoaded; } }
        public bool ProbeStarted { get { return probeStarted; } }
        /// <summary>The live layer's number: tile answers for an older one are not counted.</summary>
        public int Generation { get { return generation; } }

        /// <summary>loadSelectedProvider: the saved provider and labels (each failure: the default).</summary>
        public void Load(MapSettings s)
        {
            selected = MapProviders.Get(s != null ? s.Provider : null).Id;
            labels = s != null ? s.Labels : MapProviders.DefaultLabelsEnabled;
            Dark = s != null && s.Dark;
        }

        /// <summary>getMapLabelsEnabled.</summary>
        public bool Labels { get { return labels ?? MapProviders.DefaultLabelsEnabled; } }

        /// <summary>getSelectedProviderId.</summary>
        public string SelectedId { get { return selected ?? MapProviders.DefaultProviderId; } }

        /// <summary>getEffectiveProviderId: the selected provider, or OpenStreetMap for a Google one after the fallback.</summary>
        public string EffectiveId
        {
            get
            {
                string s = SelectedId;
                return fallback && MapProviders.IsGoogle(s) ? MapProviders.FallbackProviderId : s;
            }
        }

        /// <summary>createLayer's URL template for the live layer.</summary>
        public string Template { get { return MapProviders.UrlTemplate(EffectiveId, Labels, Dark); } }

        /// <summary>createLayer's maxZoom (no cap on the Viewer's map).</summary>
        public int MaxZoom { get { return MapProviders.Get(EffectiveId).MaxZoom; } }

        /// <summary>The tile URL the live layer asks for.</summary>
        public string TileUrl(int x, int y, int z)
        {
            return MapProviders.LayerTileUrl(EffectiveId, x, y, z, Labels, Dark);
        }

        /// <summary>wantsDarkFilter for the live provider while dark mode is on (the CSS invert on the tile pane).</summary>
        public bool DarkFilter { get { return Dark && MapProviders.WantsDarkFilter(EffectiveId); } }

        /// <summary>attachTileLayer once the setting is in (a new layer); true when the probe should start (maybeStartProbe).</summary>
        public bool Attach()
        {
            Rebuild();
            return StartProbe();
        }

        /// <summary>A tile of the layer <paramref name="gen"/> loaded ('tileload': only a Google layer listens).</summary>
        public void TileLoaded(int gen)
        {
            if (gen != generation || !MapProviders.IsGoogle(EffectiveId)) return;
            tileLoaded = true;
        }

        /// <summary>A tile of that layer failed ('tileerror'); true when it made the fallback happen.</summary>
        public bool TileFailed(int gen)
        {
            if (gen != generation || !MapProviders.IsGoogle(EffectiveId)) return false;
            if (tileLoaded || fallback) return false;
            errors++;
            if (errors >= ErrorsBeforeFallback) return Fallback();
            return false;
        }

        /// <summary>maybeStartProbe: true when the probe tile should be fetched now (once, for a Google provider).</summary>
        public bool StartProbe()
        {
            if (probeStarted || !MapProviders.IsGoogle(EffectiveId)) return false;
            probeStarted = true;
            return true;
        }

        /// <summary>The probe's answer (false: failed or 5 s passed); true when it made the fallback happen.</summary>
        public bool ProbeAnswered(bool ok)
        {
            if (!ok && !tileLoaded && !fallback) return Fallback();
            return false;
        }

        bool Fallback()
        {
            if (fallback) return false;
            fallback = true;
            Rebuild();
            Action h = FellBack;
            if (h != null) h();
            return true;
        }

        /// <summary>setMapTileProvider(id): saved, the fallback cleared so Google gets a fresh chance; true when the probe should start.</summary>
        public bool SetProvider(string id)
        {
            selected = MapProviders.Get(id).Id;
            fallback = false;
            errors = 0;
            tileLoaded = false;
            probeStarted = false;
            Rebuild();
            return StartProbe();
        }

        /// <summary>setMapLabels(enabled).</summary>
        public void SetLabels(bool on)
        {
            labels = on;
            Rebuild();
        }

        /// <summary>applyMapDarkMode(enabled): the layers made again (Google's night palette).</summary>
        public void SetDark(bool on)
        {
            Dark = on;
            Rebuild();
        }

        void Rebuild()
        {
            generation++;
            Action h = LayersChanged;
            if (h != null) h();
        }
    }

    /// <summary>A place (L.latLng).</summary>
    public struct GeoPoint
    {
        public double Lat, Lng;
        public GeoPoint(double lat, double lng) { Lat = lat; Lng = lng; }
        public override string ToString() { return Format.JsNum(Lat) + "," + Format.JsNum(Lng); }
    }

    /// <summary>A pixel point (L.point).</summary>
    public struct PixelPoint
    {
        public double X, Y;
        public PixelPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>L.latLngBounds.</summary>
    public struct GeoBounds
    {
        public double South, West, North, East;
        public bool Valid;

        public static GeoBounds Of(IEnumerable<GeoPoint> points)
        {
            GeoBounds b = new GeoBounds();
            foreach (GeoPoint p in points) b = b.Extend(p);
            return b;
        }

        public GeoBounds Extend(GeoPoint p)
        {
            if (!Valid) return new GeoBounds { South = p.Lat, North = p.Lat, West = p.Lng, East = p.Lng, Valid = true };
            return new GeoBounds { South = Math.Min(South, p.Lat), North = Math.Max(North, p.Lat), West = Math.Min(West, p.Lng), East = Math.Max(East, p.Lng), Valid = true };
        }

        public GeoBounds Extend(GeoBounds o)
        {
            if (!o.Valid) return this;
            if (!Valid) return o;
            return Extend(new GeoPoint(o.South, o.West)).Extend(new GeoPoint(o.North, o.East));
        }

        /// <summary>getCenter: the corners' middle.</summary>
        public GeoPoint Center { get { return new GeoPoint((South + North) / 2, (West + East) / 2); } }
    }

    /// <summary>
    /// The map's view as Leaflet keeps it for #map (L.map with no zoom
    /// control, zoomSnap 1, the tile layer's maxZoom): the size, zoom and
    /// centre, EPSG:3857 projection at 256 px tiles, the pixel origin the
    /// panes are placed from, setView, fitBounds (getBoundsZoom with its 1 %
    /// snap), setZoomAround and the scroll wheel's steps.
    /// </summary>
    public sealed class LeafletView
    {
        const double R = 6378137;
        /// <summary>Math.LN2.</summary>
        internal const double Ln2 = 0.6931471805599453;
        const double MaxLatitude = 85.0511287798;
        static readonly double TScale = 0.5 / (Math.PI * R);

        /// <summary>The container's size (#map: 258 x 258 inside the panel's border).</summary>
        public double Width = 258, Height = 258;
        /// <summary>The layers' max zoom (the provider's); Infinity before a layer is in.</summary>
        public double MaxZoom = 20;
        public double MinZoom = 0;
        double zoom = 2;
        GeoPoint center = new GeoPoint(0, 0);

        /// <summary>setView([0, 0], 2) as the map starts.</summary>
        public double Zoom { get { return zoom; } }
        public GeoPoint Center { get { return center; } }

        /// <summary>CRS.scale(zoom): 256 x 2^zoom.</summary>
        public static double Scale(double z) { return 256 * Math.Pow(2, z); }

        /// <summary>CRS.zoom(scale).</summary>
        public static double ZoomOf(double scale) { return Math.Log(scale / 256) / Ln2; }

        /// <summary>map.project(latlng, zoom): SphericalMercator then the 3857 transformation.</summary>
        public static PixelPoint Project(GeoPoint p, double z)
        {
            double d = Math.PI / 180;
            double lat = Math.Max(Math.Min(MaxLatitude, p.Lat), -MaxLatitude);
            double sin = Math.Sin(lat * d);
            double x = R * p.Lng * d;
            double y = R * Math.Log((1 + sin) / (1 - sin)) / 2;
            double s = Scale(z);
            return new PixelPoint(s * (TScale * x + 0.5), s * (-TScale * y + 0.5));
        }

        /// <summary>map.unproject(point, zoom).</summary>
        public static GeoPoint Unproject(PixelPoint pt, double z)
        {
            double s = Scale(z);
            double x = (pt.X / s - 0.5) / TScale;
            double y = (pt.Y / s - 0.5) / -TScale;
            double d = 180 / Math.PI;
            return new GeoPoint((2 * Math.Atan(Math.Exp(y / R)) - Math.PI / 2) * d, x * d / R);
        }

        static double JsRound(double v) { return Math.Floor(v + 0.5); }

        /// <summary>_limitZoom: within the min and max, snapped to whole levels.</summary>
        public double LimitZoom(double z)
        {
            if (!double.IsInfinity(z)) z = JsRound(z);
            return Math.Max(MinZoom, Math.Min(MaxZoom, z));
        }

        /// <summary>setView(center, zoom) (no maxBounds; zoom NaN keeps the zoom).</summary>
        public void SetView(GeoPoint c, double z)
        {
            zoom = double.IsNaN(z) ? zoom : LimitZoom(z);
            center = c;
        }

        /// <summary>A new layer's max zoom (_updateZoomLevels): the view zoomed out when it is past it.</summary>
        public void SetMaxZoom(double max)
        {
            MaxZoom = max;
            if (zoom > max) zoom = max;
        }

        /// <summary>getPixelOrigin: the centre's projection less half the size, rounded.</summary>
        public PixelPoint PixelOrigin
        {
            get
            {
                PixelPoint c = Project(center, zoom);
                return new PixelPoint(JsRound(c.X - Width / 2), JsRound(c.Y - Height / 2));
            }
        }

        /// <summary>latLngToContainerPoint: the rounded projection less the pixel origin.</summary>
        public PixelPoint ContainerPoint(GeoPoint p)
        {
            PixelPoint q = Project(p, zoom);
            PixelPoint o = PixelOrigin;
            return new PixelPoint(JsRound(q.X) - o.X, JsRound(q.Y) - o.Y);
        }

        /// <summary>containerPointToLatLng.</summary>
        public GeoPoint ContainerToLatLng(PixelPoint p)
        {
            PixelPoint o = PixelOrigin;
            return Unproject(new PixelPoint(p.X + o.X, p.Y + o.Y), zoom);
        }

        /// <summary>getBoundsZoom(bounds, false, padding): the largest whole zoom the bounds fit in.</summary>
        public double BoundsZoom(GeoBounds b, double padX, double padY)
        {
            double z = zoom;
            PixelPoint nw = Project(new GeoPoint(b.North, b.West), z);
            PixelPoint se = Project(new GeoPoint(b.South, b.East), z);
            double sx = (Width - padX) / Math.Abs(se.X - nw.X);
            double sy = (Height - padY) / Math.Abs(se.Y - nw.Y);
            double scale = Math.Min(sx, sy);
            double nz = ZoomOf(scale * Scale(z));
            if (double.IsNaN(nz)) nz = double.PositiveInfinity;
            if (!double.IsInfinity(nz))
            {
                nz = JsRound(nz / 0.01) * 0.01;
                nz = Math.Floor(nz);
            }
            return Math.Max(MinZoom, Math.Min(MaxZoom, nz));
        }

        /// <summary>fitBounds(bounds, { padding: [pad, pad], maxZoom }) (maxZoom NaN: none).</summary>
        public void FitBounds(GeoBounds b, double pad, double maxZoom)
        {
            if (!b.Valid) return;
            double z = BoundsZoom(b, pad * 2, pad * 2);
            if (!double.IsNaN(maxZoom)) z = Math.Min(maxZoom, z);
            if (double.IsInfinity(z)) { SetView(b.Center, z); return; }
            PixelPoint sw = Project(new GeoPoint(b.South, b.West), z);
            PixelPoint ne = Project(new GeoPoint(b.North, b.East), z);
            SetView(Unproject(new PixelPoint((sw.X + ne.X) / 2, (sw.Y + ne.Y) / 2), z), z);
        }

        /// <summary>setZoomAround(containerPoint, zoom): the point under the pointer stays put.</summary>
        public void SetZoomAround(PixelPoint at, double z)
        {
            // (the scale is the asked zoom's, before setView keeps it within the limits: a double
            // click at the max zoom still moves the map towards the point, as Leaflet does)
            double scale = Scale(z) / Scale(zoom);
            double hx = Width / 2, hy = Height / 2;
            PixelPoint offset = new PixelPoint((at.X - hx) * (1 - 1 / scale), (at.Y - hy) * (1 - 1 / scale));
            GeoPoint c = ContainerToLatLng(new PixelPoint(hx + offset.X, hy + offset.Y));
            SetView(c, z);
        }

        /// <summary>
        /// ScrollWheelZoom._performZoom: the wheel's pixels gathered in its
        /// 40 ms (Chromium on Windows: -deltaY / 2 per event; a notch is
        /// 100 px) to whole zoom levels, kept within the min and max.
        /// </summary>
        public double WheelZoomDelta(double gatheredPx)
        {
            double d2 = gatheredPx / (60 * 4);
            double d3 = 4 * Math.Log(2 / (1 + Math.Exp(-Math.Abs(d2)))) / LeafletView.Ln2;
            double d4 = Math.Ceiling(d3);
            return LimitZoom(zoom + (gatheredPx > 0 ? d4 : -d4)) - zoom;
        }

        /// <summary>The tiles that cover the container at the zoom (_pxBoundsToTileRange), as [minX, minY, maxX, maxY].</summary>
        public int[] TileRange()
        {
            int z = (int)zoom;
            PixelPoint c = Project(center, z);
            double minX = c.X - Width / 2, minY = c.Y - Height / 2, maxX = c.X + Width / 2, maxY = c.Y + Height / 2;
            return new[] { (int)Math.Floor(minX / 256), (int)Math.Floor(minY / 256), (int)Math.Ceiling(maxX / 256) - 1, (int)Math.Ceiling(maxY / 256) - 1 };
        }
    }

    /// <summary>scripts/ui/mapVisualization.js: the heading arrow, the map's rotation and the event pin's counter-rotation.</summary>
    public sealed class MapMarker
    {
        public const string HeadingUp = "heading-up";
        public const string NorthUp = "north-up";
        /// <summary>The container's scale while it turns (covers the panel's corners).</summary>
        public const double HeadingUpScale = 1.42;

        string orientation = HeadingUp;
        double bearing;
        string lastKey;

        /// <summary>A marker is on the map.</summary>
        public bool Shown;
        public GeoPoint At;
        /// <summary>The arrow's size (getArrowSizeForZoom) and its img rotation (deg).</summary>
        public double ArrowSize;
        public double ArrowRotation;
        /// <summary>The container's transform: rotate(ContainerRotation) scale(ContainerScale), with its transition (s; 0: none).</summary>
        public double ContainerRotation;
        public double ContainerScale = 1;
        public double ContainerTransition;
        /// <summary>window._mapCurrentMarkerLatLng (the re-center button's target while a marker plays).</summary>
        public GeoPoint? Current;
        /// <summary>The compass needle's rotation on #mapOrientationBtn (deg; null: none set).</summary>
        public double? Compass;

        public string Orientation { get { return orientation; } }

        /// <summary>getMapBearing: the container's turn in heading-up, else 0.</summary>
        public double MapBearing { get { return orientation == HeadingUp ? bearing : 0; } }

        /// <summary>currentMapBearing (unwrapped).</summary>
        public double Bearing { get { return bearing; } }

        /// <summary>The event pin's counter-rotation (applyUprightMarkerRotation).</summary>
        public double UprightRotation { get { return orientation == HeadingUp ? bearing : 0; } }

        /// <summary>getArrowSizeForZoom: 28 + (zoom - 8) x 6.5, 28 to 120.</summary>
        public static double ArrowSizeFor(double zoom)
        {
            double size = 28 + (zoom - 8) * 6.5;
            return Math.Max(28, Math.Min(120, Math.Floor(size + 0.5)));
        }

        /// <summary>
        /// updateMapMarker(sei): the arrow moved and turned, the map turned in
        /// heading-up. Returns true when it called setView(latlng, zoom).
        /// </summary>
        public bool Update(Sei sei, LeafletView map, double playbackRate)
        {
            if (map == null || sei == null) return false;
            double lat = Or0(sei.LatitudeDeg), lon = Or0(sei.LongitudeDeg), heading = Or0(sei.HeadingDeg);
            if (!ClipTelemetry.HasValidGps(sei))
            {
                if (Shown) Remove();
                return false;
            }
            if (Math.Abs(lat) < 0.001 || Math.Abs(lon) < 0.001)
            {
                if (Shown) Remove();
                return false;
            }
            double target = ((heading % 360) + 360) % 360;
            double rate = playbackRate == 0 || double.IsNaN(playbackRate) ? 1 : playbackRate;
            double transition = Math.Max(0.03, 0.15 / rate);
            double delta = target - (bearing % 360);
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            bearing += delta;
            double size = ArrowSizeFor(map.Zoom);
            string key = lat.ToString("F6", CultureInfo.InvariantCulture) + "," + lon.ToString("F6", CultureInfo.InvariantCulture) + "," + bearing.ToString("F1", CultureInfo.InvariantCulture) + "," + size.ToString(CultureInfo.InvariantCulture) + "," + orientation;
            if (Shown && key == lastKey) return false;
            lastKey = key;
            if (orientation == HeadingUp)
            {
                ContainerTransition = transition;
                ContainerRotation = -bearing;
                ContainerScale = HeadingUpScale;
            }
            else
            {
                ContainerTransition = 0;
                ContainerRotation = 0;
                ContainerScale = 1;
            }
            Shown = true;
            At = new GeoPoint(lat, lon);
            ArrowSize = size;
            ArrowRotation = bearing;
            Current = At;
            if (orientation == HeadingUp) Compass = -bearing;
            map.SetView(At, map.Zoom);
            return true;
        }

        /// <summary>The zoom changed (zoomend: rebuildMarkerIcon at the zoom's size).</summary>
        public void Zoomed(double zoom)
        {
            if (Shown) ArrowSize = ArrowSizeFor(zoom);
        }

        static double Or0(double v) { return double.IsNaN(v) ? 0 : v; }

        void Remove()
        {
            Shown = false;
            lastKey = null;
        }

        /// <summary>setMapOrientation(mode) (#mapOrientationBtn), with the button's compass reset for north-up.</summary>
        public void SetOrientation(string mode)
        {
            orientation = mode == NorthUp ? NorthUp : HeadingUp;
            ContainerTransition = 0.3;
            if (orientation == NorthUp) { ContainerRotation = 0; ContainerScale = 1; Compass = 0; }
            else { ContainerRotation = -bearing; ContainerScale = HeadingUpScale; Compass = null; }
            if (Shown) ArrowRotation = bearing;
        }

        /// <summary>The orientation button's click: the other mode.</summary>
        public void Toggle()
        {
            SetOrientation(orientation == HeadingUp ? NorthUp : HeadingUp);
        }

        /// <summary>#mapOrientationBtn's title once clicked (index.html's own until then).</summary>
        public string ToggleTitle
        {
            get { return orientation == HeadingUp ? "Heading up — tap for north up" : "North up — tap for heading up"; }
        }

        /// <summary>clearMapMarker: no arrow, bearing 0, the container straightened (0.3 s).</summary>
        public void Clear()
        {
            Shown = false;
            lastKey = null;
            bearing = 0;
            Current = null;
            ContainerTransition = 0.3;
            ContainerRotation = 0;
            ContainerScale = 1;
        }
    }

    /// <summary>One stretch of the route drawn in one colour (#22c55e under autopilot, #3b82f6 manual).</summary>
    public sealed class MapRouteSegment
    {
        public bool Autopilot;
        public List<GeoPoint> Points = new List<GeoPoint>();
        public string Color { get { return Autopilot ? MapRoute.AutopilotColor : MapRoute.ManualColor; } }
    }

    /// <summary>script.js's route on the map (as SentryUSB's web UI draws it).</summary>
    public static class MapRoute
    {
        public const string AutopilotColor = "#22c55e";
        public const string ManualColor = "#3b82f6";
        public const string StartColor = "#22c55e";
        public const string EndColor = "#ef4444";
        public const string PinColor = "#e53935";
        public const double Weight = 4;

        /// <summary>The path cut where autopilot starts or stops (each stretch starts at the last one's end).</summary>
        public static List<MapRouteSegment> Segments(IReadOnlyList<MapPoint> path)
        {
            List<MapRouteSegment> segs = new List<MapRouteSegment>();
            if (path == null || path.Count == 0) return segs;
            MapRouteSegment cur = new MapRouteSegment { Autopilot = path[0].Autopilot };
            foreach (MapPoint p in path)
            {
                if (p.Autopilot != cur.Autopilot && cur.Points.Count > 0)
                {
                    segs.Add(cur);
                    MapRouteSegment next = new MapRouteSegment { Autopilot = p.Autopilot };
                    next.Points.Add(cur.Points[cur.Points.Count - 1]);
                    cur = next;
                }
                cur.Points.Add(new GeoPoint(p.Lat, p.Lon));
            }
            if (cur.Points.Count > 0) segs.Add(cur);
            return segs;
        }

        /// <summary>
        /// showEventJsonLocation's pin: event.json's est_lat / est_lon (none
        /// when not numbers or both near 0), snapped to the closest route
        /// sample within 500 m.
        /// </summary>
        public static GeoPoint? EventPin(JsonValue meta, IReadOnlyList<MapPoint> path)
        {
            if (meta == null || !JsJson.Truthy(meta)) return null;
            double rawLat = ParseFloat(JsJson.Get(meta, "est_lat"));
            double rawLon = ParseFloat(JsJson.Get(meta, "est_lon"));
            if (double.IsNaN(rawLat) || double.IsInfinity(rawLat) || double.IsNaN(rawLon) || double.IsInfinity(rawLon)) return null;
            if (Math.Abs(rawLat) < 0.001 && Math.Abs(rawLon) < 0.001) return null;
            double lat = rawLat, lon = rawLon;
            if (path != null && path.Count > 0)
            {
                double cosLat = Math.Cos(rawLat * Math.PI / 180);
                int best = -1;
                double bestSq = double.PositiveInfinity;
                for (int i = 0; i < path.Count; i++)
                {
                    double dLat = (path[i].Lat - rawLat) * 111000;
                    double dLon = (path[i].Lon - rawLon) * 111000 * cosLat;
                    double sq = dLat * dLat + dLon * dLon;
                    if (sq < bestSq) { bestSq = sq; best = i; }
                }
                if (best >= 0 && bestSq < 500 * 500)
                {
                    lat = path[best].Lat;
                    lon = path[best].Lon;
                }
            }
            return new GeoPoint(lat, lon);
        }

        /// <summary>parseFloat(value) (String(value) first, as JavaScript does).</summary>
        public static double ParseFloat(JsonValue v)
        {
            if (v == null || v.IsNull) return double.NaN;
            if (v.IsNumber) return v.Num;
            if (v.IsString) return PlaybackSpeeds.JsParseFloat(v.Str);
            if (v.IsBool) return double.NaN;
            if (v.IsArray) return v.Count > 0 ? ParseFloat(v[0]) : double.NaN;
            return double.NaN;
        }
    }

    /// <summary>
    /// The map as script.js keeps it: the view, the marker, the route and
    /// its dots, the event pin, the cached path and bounds for the
    /// re-center button, the visibility (Settings > GPS Map and the
    /// profile's map capability), and what each of the Viewer's moments
    /// does to it (a collection opening, a segment loading, its telemetry
    /// in, a tick, the re-center button, the right-button drag, a resize).
    /// </summary>
    public sealed class ViewerMap
    {
        /// <summary>map.fitBounds's padding.</summary>
        public const double Padding = 20;
        /// <summary>The re-center button's and the event pin's zoom.</summary>
        public const double CloseZoom = 16;

        public readonly LeafletView View = new LeafletView();
        public readonly MapMarker Marker = new MapMarker();
        public readonly MapTileState Tiles = new MapTileState();

        /// <summary>mapPolyline (null: none drawn).</summary>
        public List<MapRouteSegment> Route;
        /// <summary>The start (green) and end (red) dots (fsdEventMarkers' two).</summary>
        public GeoPoint? Start, End;
        /// <summary>eventLocationMarker.</summary>
        public GeoPoint? EventPin;
        /// <summary>nativeVideo.mapPath (the playing segment's).</summary>
        public IReadOnlyList<MapPoint> Path = new List<MapPoint>();
        /// <summary>window._lastMapPath / _lastMapBounds (the re-center button's fallbacks).</summary>
        public IReadOnlyList<MapPoint> LastPath;
        public GeoBounds? LastBounds;
        /// <summary>.visible (a segment has loaded).</summary>
        public bool Loaded;
        /// <summary>state.ui.mapEnabled.</summary>
        public bool Enabled = true;
        /// <summary>library.capabilities.map !== false.</summary>
        public bool Capability = true;
        /// <summary>The telemetry capability (updateVisualization does nothing without it).</summary>
        public bool TelemetryCapability = true;

        string lastKey;

        /// <summary>The map was moved or zoomed (redraw).</summary>
        public event Action Changed;

        public ViewerMap()
        {
            View.SetView(new GeoPoint(0, 0), 2);
        }

        /// <summary>#mapVis is drawn: a segment loaded and not user-hidden.</summary>
        public bool Shown { get { return Loaded && !UserHidden; } }

        /// <summary>updateMapVisibility's user-hidden.</summary>
        public bool UserHidden { get { return !(Enabled && Capability); } }

        void Raise()
        {
            Action h = Changed;
            if (h != null) h();
        }

        /// <summary>A new tile layer's max zoom (the provider's).</summary>
        public void LayerMaxZoom(double max)
        {
            double before = View.Zoom;
            View.SetMaxZoom(max);
            if (View.Zoom != before) Marker.Zoomed(View.Zoom);
        }

        /// <summary>resetDashboardAndMap's map part.</summary>
        public void Reset()
        {
            Marker.Clear();
            Route = null;
            Path = new List<MapPoint>();
            LastBounds = null;
            LastPath = null;
            Start = null;
            End = null;
            EventPin = null;
            lastKey = null;
            Raise();
        }

        /// <summary>The first group's event.json (showEventJsonLocation reads any group's eventMeta).</summary>
        public static JsonValue EventMetaOf(DayCollection c)
        {
            if (c == null || c.Groups == null) return null;
            foreach (ClipGroup g in c.Groups) if (JsJson.Truthy(g.EventMeta)) return g.EventMeta;
            return null;
        }

        /// <summary>showEventJsonLocation(coll, { recenter }): the pin (snapped to the route) and, when asked, the map on it at zoom 16.</summary>
        public void ShowEvent(JsonValue meta, bool recenter)
        {
            GeoPoint? pin = MapRoute.EventPin(meta, Path);
            if (pin == null) return;
            EventPin = pin;
            if (recenter) View.SetView(pin.Value, CloseZoom);
            Marker.Zoomed(View.Zoom);
            Raise();
        }

        /// <summary>loadNativeSegment: the segment's path dropped (its telemetry is asked for), the panel shown.</summary>
        public void SegmentLoading()
        {
            Path = new List<MapPoint>();
            Loaded = true;
            Raise();
        }

        /// <summary>
        /// updateMapVisibility's settle (150 ms on): the route fitted, else the
        /// marker at zoom 16, while the map is shown.
        /// </summary>
        public void Settle()
        {
            if (UserHidden) return;
            if (Route != null)
            {
                GeoBounds b = RouteBounds();
                if (b.Valid) View.FitBounds(b, Padding, double.NaN);
            }
            else if (Marker.Shown) View.SetView(Marker.At, CloseZoom);
            Marker.Zoomed(View.Zoom);
            Raise();
        }

        GeoBounds RouteBounds()
        {
            GeoBounds b = new GeoBounds();
            if (Route != null) foreach (MapRouteSegment s in Route) b = b.Extend(GeoBounds.Of(s.Points));
            return b;
        }

        static GeoBounds PathBounds(IReadOnlyList<MapPoint> path)
        {
            GeoBounds b = new GeoBounds();
            foreach (MapPoint p in path) b = b.Extend(new GeoPoint(p.Lat, p.Lon));
            return b;
        }

        /// <summary>
        /// The segment's telemetry is in: the route drawn per autopilot
        /// stretch with its start and end dots, the event pin snapped to it
        /// (without moving the map), and the route fitted (zoom 15 on its
        /// middle when that is wider than zoom 14).
        /// </summary>
        public void PathLoaded(IReadOnlyList<MapPoint> path, JsonValue eventMeta)
        {
            Path = path ?? new List<MapPoint>();
            if (Path.Count > 0) LastPath = Path;
            if (Path.Count == 0) { Raise(); return; }
            Route = MapRoute.Segments(Path);
            // (refreshFsdEventMarkers clears the old dots; Studio's drive events are not in this Viewer)
            Start = new GeoPoint(Path[0].Lat, Path[0].Lon);
            End = new GeoPoint(Path[Path.Count - 1].Lat, Path[Path.Count - 1].Lon);
            if (eventMeta != null) ShowEvent(eventMeta, false);
            GeoBounds b = PathBounds(Path);
            LastBounds = b;
            View.FitBounds(b, Padding, double.NaN);
            if (View.Zoom < 14)
            {
                MapPoint mid = Path[Path.Count / 2];
                View.SetView(new GeoPoint(mid.Lat, mid.Lon), 15);
            }
            Marker.Zoomed(View.Zoom);
            Raise();
        }

        /// <summary>updateVisualization's map part: updateMapMarker when the place or heading changed (the map on, telemetry on).</summary>
        public void Tick(Sei sei, double playbackRate)
        {
            if (sei == null || !TelemetryCapability || !Enabled) return;
            string key = Format.JsNum(Or0(sei.LatitudeDeg)) + "|" + Format.JsNum(Or0(sei.LongitudeDeg)) + "|" + Format.JsNum(Or0(sei.HeadingDeg));
            if (key == lastKey) return;
            lastKey = key;
            Marker.Update(sei, View, playbackRate);
            Raise();
        }

        static double Or0(double v) { return double.IsNaN(v) ? 0 : v; }

        /// <summary>telemetryDirty.reset(): the next tick draws again.</summary>
        public void Dirty() { lastKey = null; }

        /// <summary>
        /// recenterMap (#mapRecenterBtn): the event pin at 16, else the
        /// playing arrow at 16, else the route, the path or the last bounds
        /// fitted (at most 16).
        /// </summary>
        public void Recenter()
        {
            if (EventPin != null) View.SetView(EventPin.Value, CloseZoom);
            else if (Marker.Current != null) View.SetView(Marker.Current.Value, CloseZoom);
            else if (Route != null && RouteBounds().Valid) View.FitBounds(RouteBounds(), Padding, CloseZoom);
            else
            {
                IReadOnlyList<MapPoint> p = Path != null && Path.Count > 0 ? Path : LastPath;
                if (p != null && p.Count > 0) View.FitBounds(PathBounds(p), Padding, CloseZoom);
                else if (LastBounds != null) View.FitBounds(LastBounds.Value, Padding, CloseZoom);
            }
            Marker.Zoomed(View.Zoom);
            Raise();
        }

        /// <summary>The window was resized (100 ms on) or went full screen: the playing path fitted again while a route is drawn.</summary>
        public void Resized()
        {
            if (Route == null || Path == null || Path.Count == 0) return;
            View.FitBounds(PathBounds(Path), Padding, double.NaN);
            Marker.Zoomed(View.Zoom);
            Raise();
        }

        /// <summary>
        /// The right-button drag: the map's centre at its start moved by the
        /// pointer (turned back by the map's bearing so the map follows the
        /// pointer while it is rotated).
        /// </summary>
        public static GeoPoint DragCenter(LeafletView view, GeoPoint startCenter, double dx, double dy, double bearing)
        {
            if (bearing != 0)
            {
                double rad = bearing * Math.PI / 180;
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                double rdx = dx * cos + dy * sin;
                double rdy = -dx * sin + dy * cos;
                dx = rdx;
                dy = rdy;
            }
            PixelPoint start = view.ContainerPoint(startCenter);
            return view.ContainerToLatLng(new PixelPoint(start.X - dx, start.Y - dy));
        }

        /// <summary>The drag's move (setView at the zoom, no animation).</summary>
        public void DragTo(GeoPoint startCenter, double dx, double dy)
        {
            View.SetView(DragCenter(View, startCenter, dx, dy, Marker.MapBearing), View.Zoom);
            Raise();
        }

        /// <summary>A scroll wheel's gathered pixels (Chromium's) at a container point: zoomed around it.</summary>
        public void Wheel(double gatheredPx, PixelPoint at)
        {
            double d = View.WheelZoomDelta(gatheredPx);
            if (d == 0) return;
            View.SetZoomAround(at, View.Zoom + d);
            Marker.Zoomed(View.Zoom);
            Raise();
        }

        /// <summary>doubleClickZoom: one level in around the point (out with Shift).</summary>
        public void DoubleClick(PixelPoint at, bool shift)
        {
            View.SetZoomAround(at, View.Zoom + (shift ? -1 : 1));
            Marker.Zoomed(View.Zoom);
            Raise();
        }

        /// <summary>The orientation button.</summary>
        public void ToggleOrientation()
        {
            Marker.Toggle();
            Raise();
        }
    }
}
