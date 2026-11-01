'use strict';

// The native Windows app's Viewer, part 10: the map (windows/app/Core/ViewerMap.cs,
// drawn by Ui/Builtin/ViewerMap.cs). This test runs the web's own code:
//   - src/shared/mapProviders.js (every provider's template, apistyle and tile URL);
//   - src/renderer/scripts/ui/mapTiles.js on a pretend Leaflet (the saved provider and
//     labels, the Google -> OpenStreetMap fallback after 3 tile errors or a failed probe,
//     the provider, labels and dark-mode changes);
//   - src/renderer/scripts/ui/mapVisualization.js on a pretend map and marker (the arrow,
//     the heading-up turn, north-up, the event pin's counter-rotation, clearMapMarker), with
//     script.js's #mapOrientationBtn click and compass cut out of the file;
//   - script.js's route segments, showEventJsonLocation, the route fit, recenterMap and the
//     right-button drag, cut out of the file, and Leaflet's own fitBounds, setZoomAround,
//     scroll-wheel and double-click zoom (src/renderer/vendor/leaflet) on a map without a page;
// and writes the inputs and what came out to
// windows/app/Core.Tests/Fixtures/expected-viewer-map.json; Core.Tests (ViewerMapTests.cs)
// replays the same steps on the C# and compares. Rewrite the file with
// UPDATE_NATIVE_PARITY=1 after a change on the web side, and port it.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-map.json');
const SCRIPT = fs.readFileSync(path.join(REPO, 'src/renderer/script.js'), 'utf8');
const MapProviders = require(path.join(REPO, 'src/shared/mapProviders.js'));

function slice(src, from, to, inclusive = false) {
  const a = src.indexOf(from);
  const b = src.indexOf(to, a + 1);
  assert.ok(a >= 0 && b > a, `still has ${from.trim()} … ${to.trim()}`);
  return src.slice(a, inclusive ? b + to.length : b);
}

const HAS_VALID_GPS = new Function(`${slice(fs.readFileSync(path.join(REPO, 'src/renderer/scripts/core/seiExtractor.js'), 'utf8'), 'export function hasValidGps(sei) {', '\n}\n', true).replace('export ', '')}; return hasValidGps;`)();

let copies = 0;
/** A web module as a fresh copy (its own state), its imports given from globalThis.__imports. */
async function load(dir, rel) {
  let src = fs.readFileSync(path.join(REPO, rel), 'utf8');
  src = src.replace(/^import \{([^}]+)\} from '[^']+';$/gm, (m, names) => `const {${names}} = globalThis.__imports;`);
  const file = path.join(dir, `${path.basename(rel, '.js')}${++copies}.mjs`);
  fs.writeFileSync(file, src);
  return import(pathToFileURL(file).href);
}

const flush = () => new Promise((r) => setImmediate(r));

// ---- Leaflet without a page ------------------------------------------------------------

function leaflet() {
  const style = {};
  const win = { devicePixelRatio: 1, navigator: { userAgent: 'node', platform: '' }, addEventListener() {} };
  const doc = { documentElement: { style }, createElement: () => ({ style: {} }), addEventListener() {} };
  const m = { exports: {} };
  // eslint-disable-next-line no-new-func
  new Function('module', 'exports', 'define', 'window', 'document', 'navigator', fs.readFileSync(path.join(REPO, 'src/renderer/vendor/leaflet/leaflet.js'), 'utf8'))(m, m.exports, undefined, win, doc, win.navigator);
  return m.exports;
}
const L = leaflet();

/** An L.Map with no page: Leaflet's own methods, setView ending as _resetView ends (zoom, centre, pixel origin). */
function fakeMap(maxZoom) {
  const map = Object.create(L.Map.prototype);
  map.options = { ...L.Map.prototype.options, crs: L.CRS.EPSG3857, zoomControl: false, attributionControl: false, dragging: false };
  map._size = L.point(258, 258);
  map._sizeChanged = false;
  map._layersMaxZoom = maxZoom;
  map._mapPane = { _leaflet_pos: L.point(0, 0) };
  map._loaded = true;
  map._handlers = [];
  map.setViews = 0;
  map.listeners = {};
  map.style = {};
  map.setView = function (center, zoom) {
    zoom = zoom === undefined ? this._zoom : this._limitZoom(zoom);
    center = L.latLng(center);
    this._zoom = zoom;
    this._lastCenter = center;
    this._pixelOrigin = this._getNewPixelOrigin(center, zoom);
    this.setViews++;
    return this;
  };
  map.invalidateSize = function () { return this; };
  map.getContainer = function () { return { style: this.style }; };
  map.on = function (ev, fn) { (this.listeners[ev] = this.listeners[ev] || []).push(fn); return this; };
  map.fire = function (ev) { for (const fn of this.listeners[ev] || []) fn(); return this; };
  map.setView([0, 0], 2);
  map.setViews = 0;
  return map;
}

const num = (x) => (typeof x === 'number' ? x : x == null ? null : Number(x));
const viewOf = (map) => ({ lat: map.getCenter().lat, lng: map.getCenter().lng, zoom: map.getZoom(), origin: [map.getPixelOrigin().x, map.getPixelOrigin().y] });

// ---- A: the providers -------------------------------------------------------------------

function providers() {
  const out = [];
  for (const id of ['google', 'google-satellite', 'osm', 'nope', null]) {
    const p = MapProviders.getProvider(id ?? undefined);
    const rec = { id, provider: p.id, label: p.label, maxZoom: p.maxZoom, subdomains: p.subdomains, isGoogle: MapProviders.isGoogleProvider(id ?? undefined), nativeDark: MapProviders.hasNativeDark(id ?? undefined), darkFilter: MapProviders.wantsDarkFilter(id ?? undefined), templates: [], urls: [] };
    for (const labels of [true, false]) for (const dark of [false, true]) rec.templates.push({ labels, dark, url: MapProviders.getUrlTemplate(id ?? undefined, { labels, dark }) });
    for (const labels of [true, false]) for (let i = 0; i < 5; i++) rec.urls.push({ labels, x: 5 + i, y: 7, z: 12, i, url: MapProviders.buildTileUrl(id ?? undefined, 5 + i, 7, 12, i, { labels }) });
    out.push(rec);
  }
  return out;
}

// ---- B: the session's provider (mapTiles.js) --------------------------------------------

const TILE_SCENARIOS = [
  { settings: {}, steps: [['attach'], ['error'], ['error'], ['load'], ['error'], ['error'], ['error'], ['probe', false]] },
  { settings: {}, steps: [['attach'], ['error'], ['error'], ['error'], ['error'], ['provider', 'google'], ['error'], ['probe', false], ['provider', 'osm'], ['error'], ['probe', true]] },
  { settings: { mapTileProvider: 'osm', mapLabels: false }, steps: [['attach'], ['error'], ['error'], ['error'], ['labels', true], ['provider', 'google-satellite'], ['probe', true], ['error'], ['error'], ['error'], ['labels', false]] },
  { settings: { mapDarkMode: true }, steps: [['dark', true], ['attach'], ['probe', true], ['error'], ['error'], ['error'], ['provider', 'osm'], ['dark', false], ['provider', 'google'], ['dark', true]] },
  { settings: { mapTileProvider: 'bogus', mapLabels: 0 }, steps: [['attach'], ['load'], ['error'], ['error'], ['error']] },
  { settings: { mapTileProvider: 'google-satellite', mapLabels: null }, steps: [['attach'], ['labels', false], ['probe', false]] }
];

async function tiles(dir) {
  const out = [];
  // (the probe's 5 s timer never fires: the steps answer it; the fallback's console line kept quiet)
  const realTimeout = globalThis.setTimeout;
  const realWarn = console.warn;
  globalThis.setTimeout = () => 0;
  console.warn = () => {};
  for (const sc of TILE_SCENARIOS) {
    const saved = { ...sc.settings };
    const layers = [];
    let notices = 0;
    let probe = null;
    globalThis.window = {
      MapProviders,
      _mapDarkMode: false,
      electronAPI: { getSetting: async (k) => saved[k], setSetting: async (k, v) => { saved[k] = v; } },
      L: {
        tileLayer(url, opts) {
          const layer = { url, maxZoom: opts.maxZoom, subdomains: opts.subdomains, handlers: {}, on(ev, fn) { (this.handlers[ev] = this.handlers[ev] || []).push(fn); return this; }, addTo() { layers.push(this); return this; } };
          return layer;
        }
      }
    };
    globalThis.Image = class { set src(v) { if (v) probe = this; } };
    globalThis.__imports = { notify: () => { notices++; }, t: (k) => k };
    const mod = await load(dir, 'src/renderer/scripts/ui/mapTiles.js');
    const map = { removeLayer() {} };
    const steps = [];
    for (const step of sc.steps) {
      const [op, v] = step;
      const live = () => layers[layers.length - 1];
      if (op === 'attach') mod.attachTileLayer(map);
      else if (op === 'error') { const l = live(); for (const fn of (l && l.handlers.tileerror) || []) fn(); }
      else if (op === 'load') { const l = live(); for (const fn of (l && l.handlers.tileload) || []) fn(); }
      else if (op === 'probe') { const p = probe; probe = null; if (p) (v ? p.onload : p.onerror)(); }
      else if (op === 'provider') await mod.setMapTileProvider(v);
      else if (op === 'labels') await mod.setMapLabels(v);
      else if (op === 'dark') { window._mapDarkMode = v; mod.refreshTileLayers(); }
      for (let i = 0; i < 4; i++) await flush();
      const l = live();
      steps.push({ op, value: v ?? null, effective: mod.getEffectiveProviderId(), selected: mod.getSelectedProviderId(), labels: mod.getMapLabelsEnabled(), url: l ? l.url : null, maxZoom: l ? l.maxZoom : null, layers: layers.length, notices, probing: probe !== null, saved: { ...saved } });
    }
    out.push({ settings: sc.settings, steps });
  }
  globalThis.setTimeout = realTimeout;
  console.warn = realWarn;
  delete globalThis.window;
  delete globalThis.Image;
  delete globalThis.__imports;
  return out;
}

// ---- C: the marker (mapVisualization.js) --------------------------------------------------

const AUSTIN = { lat: 30.2672, lon: -97.7431 };
const MARKER_STEPS = [
  ['sei', { lat: AUSTIN.lat, lon: AUSTIN.lon, heading: 135.5 }, 1],
  ['sei', { lat: AUSTIN.lat, lon: AUSTIN.lon, heading: 135.5 }, 1],
  ['sei', { lat: 30.26721, lon: -97.74311, heading: 135.54 }, 2],
  ['sei', { lat: 30.26725, lon: -97.74316, heading: 350 }, 0.5],
  ['sei', { lat: 30.2673, lon: -97.7432, heading: 10 }, 4],
  ['sei', { lat: 30.2673, lon: -97.7432, heading: -170 }, 1],
  ['zoom', 14],
  ['sei', { lat: 30.2673, lon: -97.7432, heading: -170 }, 1],
  ['toggle'],
  ['sei', { lat: 30.2674, lon: -97.7433, heading: 200 }, 1],
  ['sei', { lat: 30.2674, lon: -97.7433, heading: 200 }, 1],
  ['zoom', 6],
  ['toggle'],
  ['sei', { lat: 30.2675, lon: -97.7434, heading: 559.5 }, 0],
  ['sei', { lat: 0.0005, lon: -97.7434, heading: 90 }, 1],
  ['sei', { lat: 30.2675, lon: -97.7434, heading: 90 }, 1],
  ['sei', { lat: 0, lon: 0, heading: 45 }, 1],
  ['sei', { lat: 95, lon: 10, heading: 45 }, 1],
  ['sei', { lat: -33.8688197, lon: 151.2092955, heading: -45 }, 1],
  ['zoom', 20],
  ['sei', { lat: -33.8688197, lon: 151.2092955, heading: -45 }, 1],
  ['clear'],
  ['sei', { lat: -33.8688197, lon: 151.2092955, heading: 181 }, 1],
  ['toggle'],
  ['toggle'],
  ['sei', { lat: -33.8688, lon: 151.2093, heading: 359 }, 1]
];

async function marker(dir) {
  const map = fakeMap(20);
  map.setView([AUSTIN.lat, AUSTIN.lon], 16);
  map.setViews = 0;
  const icons = [];
  const makeIcon = (opts) => {
    const html = opts.html;
    const img = { style: {}, isConnected: true };
    const w = /width:(\d+)px/.exec(html);
    const r = /rotate\(([-\d.e]+)deg\)/.exec(html);
    if (w) img.style.width = `${w[1]}px`;
    if (r) img.style.transform = `rotate(${r[1]}deg)`;
    icons.push(img);
    return { querySelector: (sel) => (sel === 'img' ? img : null) };
  };
  let arrow = null;
  globalThis.window = {};
  globalThis.L = {
    divIcon: (opts) => opts,
    marker: (latlng, { icon }) => {
      const mk = {
        latlng, _icon: makeIcon(icon), removed: false,
        addTo() { arrow = this; return this; },
        setLatLng(ll) { this.latlng = ll; return this; },
        setIcon(ic) { const old = this._icon.querySelector('img'); if (old) old.isConnected = false; this._icon = makeIcon(ic); return this; },
        getLatLng() { return L.latLng(this.latlng); },
        remove() { this.removed = true; if (arrow === this) arrow = null; return this; }
      };
      return mk;
    }
  };
  const vis = await load(dir, 'src/renderer/scripts/ui/mapVisualization.js');
  vis.initMapVisualization({ getMap: () => map, getMapVis: () => null, getMapPolyline: () => null, getState: () => state });
  const state = { ui: { playbackRate: 1 } };
  // the event pin (setUprightMarker) and script.js's #mapOrientationBtn and its compass
  const pinSvg = { style: {}, isConnected: true };
  vis.setUprightMarker({ _icon: { querySelector: (s) => (s === 'svg' ? pinSvg : null) } });
  const compassSvg = { style: {} };
  const button = { title: 'Toggle heading-up / north-up', querySelector: () => compassSvg, addEventListener(ev, fn) { this.click = fn; } };
  const btnSrc = slice(SCRIPT, "            const mapOrientationBtn = document.getElementById('mapOrientationBtn');", "    } catch(e) { console.error('Leaflet init failed', e); }");
  new Function('document', 'getMapOrientation', 'setMapOrientation', 'window', `${btnSrc.replace(/\n {8}\}\s*$/, '')}`)({ getElementById: () => button }, vis.getMapOrientation, vis.setMapOrientation, globalThis.window);

  const out = [];
  for (const step of MARKER_STEPS) {
    const [op, a, b] = step;
    const before = map.setViews;
    if (op === 'sei') {
      state.ui.playbackRate = b;
      vis.updateMapMarker({ latitudeDeg: a.lat, longitudeDeg: a.lon, headingDeg: a.heading }, HAS_VALID_GPS);
    } else if (op === 'zoom') { map.setView(map.getCenter(), a); map.fire('zoomend'); } else if (op === 'toggle') button.click({ stopPropagation() {}, preventDefault() {} });
    else if (op === 'clear') vis.clearMapMarker();
    const img = arrow ? arrow._icon.querySelector('img') : null;
    const deg = (s) => (s ? Number(/rotate\(([-\d.e]+)deg\)/.exec(s)[1]) : null);
    const style = map.getContainer().style;
    out.push({
      op, value: a ?? null, rate: b ?? null,
      shown: !!arrow,
      at: arrow ? [num(arrow.latlng[0] ?? arrow.latlng.lat), num(arrow.latlng[1] ?? arrow.latlng.lng)] : null,
      arrowSize: img ? Number(/^(\d+)px$/.exec(img.style.width)[1]) : null,
      arrowRotation: img ? deg(img.style.transform) : null,
      container: style.transform || 'rotate(0deg) scale(1)',
      transition: style.transition || '',
      setView: map.setViews > before,
      view: viewOf(map),
      current: window._mapCurrentMarkerLatLng ? [window._mapCurrentMarkerLatLng[0], window._mapCurrentMarkerLatLng[1]] : null,
      bearing: vis.getMapBearing(),
      orientation: vis.getMapOrientation(),
      upright: deg(pinSvg.style.transform),
      compass: compassSvg.style.transform ? deg(compassSvg.style.transform) : null,
      title: button.title
    });
  }
  delete globalThis.window;
  delete globalThis.L;
  return out;
}

// ---- D: script.js's map -----------------------------------------------------------------

const PATHS = {
  single: [{ lat: AUSTIN.lat, lon: AUSTIN.lon, autopilot: true }],
  mixed: [
    { lat: 30.2672, lon: -97.7431, autopilot: false }, { lat: 30.2675, lon: -97.7428, autopilot: false }, { lat: 30.2681, lon: -97.742, autopilot: true },
    { lat: 30.2688, lon: -97.7411, autopilot: true }, { lat: 30.2694, lon: -97.7402, autopilot: false }, { lat: 30.2699, lon: -97.7399, autopilot: true }
  ],
  drive: Array.from({ length: 40 }, (_, i) => ({ lat: 30.2 + i * 0.004, lon: -97.8 + i * 0.006 + (i % 3) * 0.0005, autopilot: i >= 10 && i < 30 })),
  long: Array.from({ length: 30 }, (_, i) => ({ lat: 29.9 + i * 0.03, lon: -98.1 + i * 0.04, autopilot: i % 7 < 3 })),
  wide: [{ lat: -33.86, lon: 151.2, autopilot: false }, { lat: -33.9, lon: 151.25, autopilot: false }]
};

function segments() {
  const src = slice(SCRIPT, '                // Build segments with consistent autopilot state', '                // Create polylines for each segment');
  const run = new Function('mapPath', `${src}; return segments;`);
  const out = {};
  for (const [name, p] of Object.entries(PATHS)) out[name] = run(p).map((s) => ({ autopilot: s.autopilot, coords: s.coords }));
  return out;
}

const EVENTS = [
  { meta: { est_lat: '30.2685', est_lon: '-97.7419' }, path: 'mixed' },
  { meta: { est_lat: 30.28, est_lon: -97.75 }, path: 'mixed' },
  { meta: { est_lat: '30.2685abc', est_lon: ' -97.7419' }, path: null },
  { meta: { est_lat: '0.0001', est_lon: '0.0002' }, path: 'mixed' },
  { meta: { est_lat: 'x', est_lon: '-97.7' }, path: 'mixed' },
  { meta: { est_lat: '30.2672', est_lon: '-97.7431', street: 'Congress Ave' }, path: 'single', recenter: false },
  { meta: { est_lat: '-33.88', est_lon: '151.22' }, path: 'wide' },
  { meta: null, path: 'mixed' },
  { meta: { est_lat: null, est_lon: '1' }, path: null }
];

function events() {
  const src = slice(SCRIPT, 'function showEventJsonLocation(coll, { recenter = true } = {}) {', '// Format event reason for display');
  const out = [];
  for (const ev of EVENTS) {
    const map = fakeMap(20);
    map.setViews = 0;
    let pin = null;
    const fakeL = { divIcon: (o) => o, latLng: (a, b) => L.latLng(a, b), marker: (ll) => ({ ll, addTo() { pin = this; return this; }, remove() {} }) };
    const nativeVideo = { mapPath: ev.path ? PATHS[ev.path] : [] };
    const state = { collection: {} };
    const fn = new Function('map', 'nativeVideo', 'state', 'L', 'setUprightMarker', 'console', `let eventLocationMarker = null; ${src}; return showEventJsonLocation;`)(map, nativeVideo, state, fakeL, () => {}, { log() {} });
    const coll = { groups: [{ eventMeta: null }, { eventMeta: ev.meta }] };
    fn(coll, ev.recenter === false ? { recenter: false } : undefined);
    out.push({ meta: ev.meta, path: ev.path, recenter: ev.recenter !== false, pin: pin ? [pin.ll.lat, pin.ll.lng] : null, setView: map.setViews > 0, view: viewOf(map) });
  }
  return out;
}

/** The route fit (loadNativeSegment's extraction answer): fitBounds with 20 px, then zoom 15 on the middle when wider than 14. */
function fits() {
  const src = slice(SCRIPT, '                // Fit bounds to all points', '                // Re-invalidate after layout settles');
  const out = [];
  for (const [name, p] of Object.entries(PATHS)) {
    for (const maxZoom of [20, 19]) {
      const map = fakeMap(maxZoom);
      new Function('map', 'mapPath', 'L', 'window', src)(map, p, L, {});
      out.push({ path: name, maxZoom, view: viewOf(map) });
    }
  }
  return out;
}

const RECENTERS = [
  { pin: [30.2685, -97.7419], current: [30.27, -97.74], polyline: 'mixed', zoom: 12 },
  { current: [30.27, -97.74], polyline: 'mixed', zoom: 12 },
  { polyline: 'drive', zoom: 18 },
  { polyline: 'single', zoom: 3 },
  { mapPath: 'long', zoom: 5 },
  { lastPath: 'wide', zoom: 5 },
  { mapPath: [], lastPath: 'mixed', zoom: 5 },
  { lastBounds: 'long', zoom: 18 },
  { zoom: 9 }
];

function recenters() {
  const src = slice(SCRIPT, '            function recenterMap() {', '            // Store original bounds for re-centering');
  const out = [];
  for (const r of RECENTERS) {
    const map = fakeMap(20);
    map.setView([10, 10], r.zoom);
    map.setViews = 0;
    const toPoly = (name) => {
      // (script.js's polylines: one per autopilot stretch, with their bounds)
      const segs = new Function('mapPath', `${slice(SCRIPT, '                // Build segments with consistent autopilot state', '                // Create polylines for each segment')}; return segments;`)(PATHS[name]);
      return segs.map((s) => ({ getBounds: () => L.latLngBounds(s.coords) }));
    };
    const win = { _mapCurrentMarkerLatLng: r.current || null, _lastMapPath: r.lastPath ? PATHS[r.lastPath] : null, _lastMapBounds: r.lastBounds ? L.latLngBounds(PATHS[r.lastBounds].map((p) => [p.lat, p.lon])) : null };
    const nativeVideo = { mapPath: Array.isArray(r.mapPath) ? r.mapPath : r.mapPath ? PATHS[r.mapPath] : [] };
    const eventLocationMarker = r.pin ? { getLatLng: () => L.latLng(r.pin[0], r.pin[1]) } : null;
    const fn = new Function('map', 'mapPolyline', 'eventLocationMarker', 'nativeVideo', 'window', 'L', `${src}; return recenterMap;`)(map, r.polyline ? toPoly(r.polyline) : null, eventLocationMarker, nativeVideo, win, L);
    fn();
    out.push({ ...r, view: viewOf(map) });
  }
  return out;
}

const VIEW_STEPS = [
  ['view', [30.2672, -97.7431], 16],
  ['drag', 0, [40, -25]],
  ['drag', 135.5, [40, -25]],
  ['drag', -90, [-120, 10]],
  ['drag', 359.9, [3, 3]],
  ['wheel', 50, [129, 129]],
  ['wheel', 50, [30, 200]],
  ['wheel', -50, [200, 40]],
  ['wheel', 150, [100, 100]],
  ['wheel', -400, [10, 10]],
  ['dblclick', [60, 70], false],
  ['dblclick', [250, 5], true],
  ['view', [30.2672, -97.7431], 20],
  ['wheel', 50, [200, 200]],
  ['dblclick', [200, 200], false],
  ['maxZoom', 19],
  ['view', [-33.8688197, 151.2092955], 0],
  ['wheel', -50, [129, 129]],
  ['dblclick', [0, 0], true],
  ['view', [85.2, 179.99], 4],
  ['drag', 30, [-500, 300]]
];

function views() {
  const dragSrc = slice(SCRIPT, '                    let dx = e.clientX - mapDragStart.x;', '                    map.setView(newCenter, map.getZoom(), { animate: false });', true);
  const map = fakeMap(20);
  const probes = [[30.2672, -97.7431], [30.2681, -97.742], [-33.8688197, 151.2092955], [0, 0]];
  const out = [];
  for (const step of VIEW_STEPS) {
    const [op, a, b] = step;
    if (op === 'view') map.setView(a, b);
    else if (op === 'drag') {
      const start = map.getCenter();
      new Function('e', 'mapDragStart', 'mapDragStartCenter', 'map', 'L', 'getMapBearing', dragSrc)({ clientX: 500 + b[0], clientY: 400 + b[1] }, { x: 500, y: 400 }, start, map, L, () => a);
    } else if (op === 'wheel') {
      const h = Object.create(L.Map.ScrollWheelZoom.prototype);
      h._map = map;
      h._delta = a;
      h._lastMousePos = L.point(b[0], b[1]);
      h._performZoom();
    } else if (op === 'dblclick') {
      L.Map.DoubleClickZoom.prototype._onDoubleClick.call({ _map: map }, { containerPoint: L.point(a[0], a[1]), originalEvent: { shiftKey: b } });
    } else if (op === 'maxZoom') {
      map._layersMaxZoom = a;
      if (map.getZoom() > a) map.setZoom(a);
    }
    out.push({ op, a, b: b ?? null, view: viewOf(map), points: probes.map((p) => { const c = map.latLngToContainerPoint(p); return [c.x, c.y]; }) });
  }
  return out;
}

test('native Viewer map: the web map\'s providers, fallback, marker, route, event pin and view, for the app to match', { timeout: 60_000 }, async (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'native-viewer-map-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const got = {
    providers: providers(),
    tiles: await tiles(dir),
    marker: await marker(dir),
    paths: PATHS,
    segments: segments(),
    events: events(),
    fits: fits(),
    recenters: recenters(),
    views: views()
  };

  // what the web does, in words, so a change on its side shows here first
  assert.equal(got.providers[0].templates[0].url, 'https://{s}.google.com/vt/lyrs=m&x={x}&y={y}&z={z}&apistyle=s.e%3Al%7Cp.v%3Aoff,s.t%3A1%7Cs.e%3Al%7Cp.v%3Aon,s.t%3A3%7Cs.e%3Al%7Cp.v%3Aon,s.t%3A49%7Cs.e%3Al%7Cp.v%3Aon');
  assert.equal(got.tiles[0].steps.at(-1).effective, 'google', 'a loaded tile keeps Google');
  assert.equal(got.tiles[1].steps[3].effective, 'osm', 'three errors before any load fall back');
  assert.equal(got.tiles[1].steps[3].notices, 1);
  assert.equal(got.marker[0].container, 'rotate(-135.5deg) scale(1.42)');
  assert.equal(got.marker[0].arrowSize, 80);
  assert.equal(got.segments.mixed.length, 4);
  assert.equal(got.fits.find((f) => f.path === 'single' && f.maxZoom === 20).view.zoom, 20);

  const text = `${JSON.stringify(got, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1' || !fs.existsSync(EXPECTED)) {
    fs.writeFileSync(EXPECTED, text);
    t.diagnostic(`wrote ${path.relative(REPO, EXPECTED)}`);
  } else {
    assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'expected-viewer-map.json is what the web does (UPDATE_NATIVE_PARITY=1 rewrites it)');
  }
});
