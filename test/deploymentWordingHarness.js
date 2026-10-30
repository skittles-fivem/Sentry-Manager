'use strict';

// Draws a pack's Settings sections (updates, security, system) without a
// browser, for test/deploymentWording.test.js. A pack page is a browser
// module that imports helpers from its core.js; here its imports are left
// out and every name it uses that is not its own resolves to a stub that
// prints its name and arguments. The drawing is then the page's own
// template with the stubs' output in place, so two versions of a page drawn
// the same way can be compared character for character.

const crypto = require('crypto');

const SECTIONS = {
  aurora: { dir: 'aurora/web/aurora', fns: { updates: 'updates', security: 'security', system: 'system' } },
  daylight: { dir: 'daylight/web/dl', fns: { updates: 'updates', security: 'security', system: 'system' }, ctx: true },
  flux: { dir: 'flux/web/flux', fns: { updates: 'updates', security: 'security', system: 'system' } },
  horizon: { dir: 'horizon/web/hz', fns: { updates: 'updates', security: 'security', system: 'sys' } }
};

// a stand-in element: keeps what is written to innerHTML, ignores the rest
function element() {
  const el = {
    innerHTML: '',
    querySelector: () => element(),
    querySelectorAll: () => [],
    addEventListener() {},
    append() {},
    replaceChildren() {},
    classList: { add() {}, remove() {}, toggle() {}, contains: () => false },
    dataset: {},
    style: {},
    contains: () => false
  };
  return el;
}

const stub = (name) => (...args) => `«${name}(${args.map((a) => (typeof a === 'function' ? 'fn' : JSON.stringify(a))).join(',')})»`;

/**
 * The page's sections as functions: { updates, security, system }, each
 * drawing with api answers from `answers` (by path) and returning the HTML.
 * `extra` adds or replaces names the page imports (onWindows, toldHow...).
 */
function packSections(pack, source, answers, extra = {}) {
  const spec = SECTIONS[pack];
  const body = source
    .replace(/^import[\s\S]*?from '[^']+';\n/gm, '')
    .replace(/^export default /m, 'const __default = ')
    .replace(/^export (async )?function /gm, (m, a) => `${a || ''}function `);
  const own = {
    api: async (p) => {
      if (!(p in answers)) throw new Error(`no answer for ${p}`);
      return JSON.parse(JSON.stringify(answers[p]));
    },
    state: {},
    store: { get: () => null, set() {} },
    document: { documentElement: { dataset: {}, classList: { toggle() {}, contains: () => false } }, activeElement: null },
    window: {},
    localStorage: { getItem: () => null, setItem() {} },
    $: () => null,
    whereRunning: Promise.resolve(null),
    onWindows: () => false,
    ...extra
  };
  const scope = new Proxy(own, {
    has: (t, k) => typeof k === 'string' && (k in t || !(k in globalThis)),
    get: (t, k) => (typeof k !== 'string' ? undefined : k in t ? t[k] : stub(k)),
    set: (t, k, v) => { t[k] = v; return true; }
  });
  const names = Object.values(spec.fns);
  // a sloppy-mode function, so that `with` can supply the names the page imports
  // eslint-disable-next-line no-new-func
  const fns = new Function('scope', `with (scope) { return (function () {\n${body}\n; return { ${names.join(', ')} }; })(); }`)(scope);
  const out = {};
  for (const [section, fn] of Object.entries(spec.fns)) {
    out[section] = async () => {
      const box = element();
      await fns[fn](spec.ctx ? { el: box, on() {}, alive: () => true } : box);
      return box.innerHTML;
    };
  }
  return out;
}

// The answers each Docker snapshot is drawn with (test/fixtures/deploymentWording-docker.json)
const PACK_CASES = {
  updates: {
    on: { enabled: true, image: 'ghcr.io/x/y', tag: 'latest', current: { sha: 'abcdef1234567', builtAt: '2026-10-01T00:00:00Z' }, latest: { revision: '1234567abcdef', created: '2026-10-20T00:00:00Z' }, checkedAt: 1, updateAvailable: true },
    current: { enabled: true, image: 'ghcr.io/x/y', current: { sha: 'abcdef1234567' }, latest: { revision: 'abcdef1234567', created: '2026-10-01T00:00:00Z' }, checkedAt: 1, updateAvailable: false },
    off: { enabled: false, image: 'ghcr.io/x/y', current: { sha: 'abcdef1234567' } },
    source: { enabled: false, image: null, current: {} }
  },
  security: {
    own: { https: true, runningAsRoot: false, tlsMode: 'own', tls: { mode: 'own', certificate: { fingerprint: 'AA:BB:CC', validTo: '2027-01-01' }, hostnames: ['nas.local'] }, currentHost: 'nas.local' },
    auto: { https: true, runningAsRoot: false, tlsMode: 'auto', tls: { mode: 'auto', certificate: { fingerprint: 'AA:BB', validTo: '2027-01-01' }, hostnames: ['nas.local'], editable: ['nas.local'], ca: { fingerprint: 'DD:EE', validTo: '2036-01-01' } }, currentHost: 'other.local:8443' },
    root: { https: false, runningAsRoot: true, tlsMode: 'off', tls: {} }
  },
  system: {
    full: { version: { sha: 'abcdef1234567', builtAt: '2026-10-01' }, converter: { ready: true, capabilities: { replacement: 'yes', ownershipCleanup: 'yes', separatePublication: 'yes' } }, nativeHelper: true, gpu: { nvidia: [{ name: 'RTX 3060', driver: '550' }], gpus: [{ vendor: 'intel', name: 'UHD 770', node: '/dev/dri/renderD128', passedThrough: true, encodes: { h264: true, hevc: false } }], hints: ['a hint'] }, dataFree: 123456789, timezone: 'UTC', security: { https: true, tlsMode: 'auto', supervised: true, uid: 1000 } },
    bare: { converter: { ready: false }, gpu: { gpus: [{ vendor: 'amd', name: 'RX', node: '/dev/dri/renderD129', passedThrough: false }] }, dataFree: 0, timezone: 'Europe/Oslo', security: { https: false, tlsMode: 'off', supervised: false } }
  }
};
const PACK_STATS = { filesystems: [{ path: '/footage/car', vehicle: 'v1' }, { path: '/data' }] };

/** Every pack section drawn for every case: { 'pack/section/case': html }. */
async function drawPacks(sourceOf, extra = {}) {
  const out = {};
  for (const pack of Object.keys(SECTIONS)) {
    for (const [section, cases] of Object.entries(PACK_CASES)) {
      for (const [name, answer] of Object.entries(cases)) {
        const answers = { '/api/updates': answer, '/api/security': answer, '/api/system': answer, '/api/stats': PACK_STATS, '/api/settings': {} };
        out[`${pack}/${section}/${name}`] = await packSections(pack, sourceOf(`examples/ui-packs/${SECTIONS[pack].dir}/pages/settings.js`), answers, extra)[section]();
      }
    }
  }
  return out;
}

// ---- web/app.js ----

// A function of app.js by name: from "  function name(" to its closing brace
// at the same indent (app.js is one browser script; these parts have no DOM).
function fnSource(src, name) {
  const start = src.indexOf(`\n  function ${name}(`);
  if (start < 0) return '';
  const end = src.indexOf('\n  }\n', start);
  return src.slice(start, end + 5);
}

/**
 * The drawing parts of an app.js source (the server monitor block and the
 * Settings cards), run with a stand-in state. Works on app.js before the
 * Windows wording too, where the cards take no deployment.
 */
function appParts(app, state = { deployment: null, system: null }) {
  const start = app.indexOf('// ---- server monitor: drawing');
  const end = app.indexOf('// ---- server monitor: end of drawing ----');
  if (!(start > 0 && end > start)) throw new Error('the drawing block is not marked in app.js');
  const helpers = ['esc', 'plural'].map((name) => app.match(new RegExp(`\\n  const ${name} = .*;\\n`))[0]).join('');
  const cards = ['updatesCard', 'serviceAccount', 'securityCard', 'vehicleForm', 'logsPart'].map((n) => fnSource(app, n)).join('');
  const optional = ['toldHow', 'printedIn'].map((n) => `${n}: typeof ${n} === 'function' ? ${n} : null`).join(', ');
  // eslint-disable-next-line no-new-func
  return new Function('state', 'when', 'logs', `${helpers}${app.slice(start, end)}${cards}
    return { serverBody, updatesCard, securityCard, vehicleForm, logsPart, ${optional} };`)(state, (t) => `at ${t}`, { all: false, html: '', tallest: 0 });
}

// The cases each Docker snapshot of app.js is drawn with
const APP_UPDATES = PACK_CASES.updates;
const APP_SECURITY = PACK_CASES.security;
const APP_VEHICLES = { none: null, path: { name: 'Car', source: { type: 'path', path: '/footage/car' } }, smb: { name: 'Nas', source: { type: 'smb', share: '//nas/TeslaCam' } } };
const STATS_SAMPLES = ['', 'amd', 'nvidia', 'pi', 'idle'];
const CPU_UNAVAILABLE = [undefined, 'windows', 'vm'];

/** app.js's Docker drawing for every case: { 'part/case': html }. `docker` is the deployment argument to pass (none for the old code). */
function drawApp(app, readSample, docker) {
  const parts = appParts(app);
  const out = {};
  const arg = docker === undefined ? [] : [docker];
  for (const name of STATS_SAMPLES) {
    for (const cpu of CPU_UNAVAILABLE) {
      for (const range of ['15m', '24h']) {
        const s = readSample(name);
        const sample = { ...s, unlock: [{ adds: 'Per-process numbers', compose: 'pid: host' }], power: { ...s.power, cpuUnavailable: cpu } };
        // the monitor's pages are long: kept as a SHA-256 of the HTML
        const html = parts.serverBody(sample, { range, ...(docker === undefined ? {} : { deployment: docker }) });
        out[`serverBody/${name || 'default'}/${cpu}/${range}`] = `sha256:${crypto.createHash('sha256').update(html).digest('hex')}`;
      }
    }
  }
  for (const [k, u] of Object.entries(APP_UPDATES)) out[`updatesCard/${k}`] = parts.updatesCard(u, ...arg);
  for (const [k, sec] of Object.entries(APP_SECURITY)) out[`securityCard/${k}`] = parts.securityCard(sec, ...arg);
  for (const [k, v] of Object.entries(APP_VEHICLES)) out[`vehicleForm/${k}`] = parts.vehicleForm(v, ...arg);
  out.logsPart = parts.logsPart();
  return out;
}

module.exports = { packSections, drawPacks, appParts, drawApp, SECTIONS, PACK_CASES };
