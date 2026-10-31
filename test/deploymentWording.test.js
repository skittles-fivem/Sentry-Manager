'use strict';

// The interfaces' wording on Docker and on the Windows service. The server
// says where it runs (deployment in /api/info and /api/system); the built-in
// interface (web/app.js, web/critical.js, web/login.js) and the four packs
// that come with the app pick their words from it. Docker's words must stay
// exactly as they were, and on Windows nothing may point at compose, docker
// logs or the container. A server that says how to update (status().how in
// /api/updates) is shown in its own words.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const harness = require('./deploymentWordingHarness');
const { continueTemplate, fingerprintFallback, DOCKER_LOG } = require('../server/continueWording');

// the drawings below show times: the snapshots were made in UTC
process.env.TZ = 'UTC';

const REPO = path.join(__dirname, '..');
const read = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');
const sample = (name = '') => JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', `statsSample${name ? `-${name}` : ''}.json`), 'utf8'));
const DOCKER_WORDS = /docker|compose|container/i;
const COMMAND_HTML = '<code>docker compose pull &amp;&amp; docker compose up -d</code>';
const WIN_UPDATE = 'Run update.ps1 as administrator, or download the latest release and run Install.cmd.';
const WIN_LOGS = '%ProgramData%\\SentrySixManager\\data\\logs\\service.log';

function appParts(state) {
  const app = read('web/app.js');
  const start = app.indexOf('// ---- server monitor: drawing');
  const end = app.indexOf('// ---- server monitor: end of drawing ----');
  assert.ok(app.indexOf('// ---- deployment wording') > start && app.indexOf('// ---- deployment wording: end ----') < end, 'the wording is inside the drawing block');
  return harness.appParts(app, state);
}

const UPDATES = {
  on: { enabled: true, image: 'ghcr.io/x/y', tag: 'latest', current: { sha: 'abcdef1234567', builtAt: '2026-10-01T00:00:00Z' }, latest: { revision: '1234567abcdef', created: '2026-10-20T00:00:00Z' }, checkedAt: 1, updateAvailable: true },
  off: { enabled: false, image: 'ghcr.io/x/y', current: { sha: 'abcdef1234567' } },
  source: { enabled: false, image: null, current: {} }
};
const SECURITY = {
  own: { https: true, runningAsRoot: false, tls: { certificate: { fingerprint: 'AA:BB:CC', validTo: '2027-01-01' }, hostnames: ['nas.local'], ca: { fingerprint: 'DD:EE', validTo: '2036-01-01' } }, currentHost: 'nas.local' },
  root: { https: false, runningAsRoot: true, tlsMode: 'off', tls: {} },
  windows: { https: true, runningAsRoot: false, account: 'NT SERVICE\\SentrySixManager', elevated: false, tls: { certificate: { fingerprint: 'AA:BB', validTo: '2027-01-01' }, hostnames: ['pc'], ca: { fingerprint: 'CC' } }, currentHost: 'pc' }
};
const VEHICLES = [null, { name: 'Car', source: { type: 'path', path: '/footage/car' } }, { name: 'Nas', source: { type: 'smb', share: '//nas/TeslaCam' } }];
const withUnlock = (s) => ({ ...s, unlock: [{ adds: 'Per-process numbers', compose: 'pid: host' }], power: { ...s.power, cpuUnavailable: 'windows' } });

test('app.js: Docker and "not known yet" draw the same, with Docker\'s words exactly as before', () => {
  const app = appParts();
  for (const name of ['', 'amd', 'nvidia', 'pi', 'idle']) {
    const s = withUnlock(sample(name));
    assert.equal(app.serverBody(s, { deployment: 'docker' }), app.serverBody(s, {}), name);
    assert.equal(app.serverBody(s, { deployment: 'linux' }), app.serverBody(s, {}), name);
  }
  for (const u of Object.values(UPDATES)) {
    assert.equal(app.updatesCard(u, 'docker'), app.updatesCard(u, null));
    assert.equal(app.updatesCard(u, 'linux'), app.updatesCard(u, null));
  }
  for (const sec of Object.values(SECURITY)) assert.equal(app.securityCard(sec, 'docker'), app.securityCard(sec, null));
  for (const v of VEHICLES) assert.equal(app.vehicleForm(v, 'docker'), app.vehicleForm(v, null));

  // the words themselves (as they were before Windows)
  const body = app.serverBody(withUnlock(sample()), { deployment: 'docker' });
  assert.ok(body.includes('<p>Shown only when the server provides it. More with one optional line each in <code>docker-compose.yml</code>:</p>'));
  assert.ok(body.includes('<li>Per-process numbers: <code>pid: host</code></li>'));
  assert.ok(body.includes('CPU watts aren&#39;t available on Docker Desktop for Windows (Windows doesn&#39;t share them with Docker).'));
  assert.ok(app.updatesCard(UPDATES.on, 'docker').includes(`<p>Updates come as a new image. To install one, run ${COMMAND_HTML} where you set up Sentry Six Manager. Settings, footage and the Recycle Bin stay as they are.</p>`));
  assert.ok(app.updatesCard(UPDATES.off, 'docker').includes(`<p>Checking for updates is turned off (UPDATE_CHECK=off). To update, run ${COMMAND_HTML} where you set up Sentry Six Manager.</p>`));
  assert.ok(app.updatesCard(UPDATES.source, 'docker').includes('<p>This copy was built from source. To update it, build the image again from the new code.</p>'));
  assert.ok(app.securityCard(SECURITY.own, 'docker').includes('or the container log (<code>docker logs sentry-six-manager</code>). If a device'));
  assert.ok(app.securityCard(SECURITY.own, 'docker').includes('runs as an unprivileged user; only the supervisor can mount shares'));
  assert.ok(app.securityCard(SECURITY.root, 'docker').includes('runs as root (not supervised; fine for development, not for the Docker image)'));
  assert.ok(app.vehicleForm(VEHICLES[1], 'docker').includes('>Folder in the container</button>'));
  assert.ok(app.vehicleForm(VEHICLES[1], 'docker').includes('placeholder="/footage/weekend-car"></label>\n        <p class="muted small" style="margin-top:8px">A folder you mounted into the container yourself (bind mount or Docker CIFS volume).</p>'));
  assert.ok(app.vehicleForm(VEHICLES[2], 'docker').includes('Files on the share can never run as programs inside the container.'));
  assert.ok(app.logsPart().includes('Warnings and errors from the server, the converter and the container.'));
});

test('app.js: on Windows nothing points at compose, docker logs or the container', () => {
  const app = appParts({ deployment: 'windows', system: null });
  for (const name of ['', 'amd', 'nvidia', 'pi', 'idle']) {
    const html = app.serverBody(withUnlock(sample(name)), { deployment: 'windows' });
    assert.doesNotMatch(html.replace(/this container/g, ''), /docker|compose/i, `${name}: no compose lines`);
    assert.ok(html.includes('<p>Shown only when the server provides it. Every number stays on this server.</p>'));
  }
  const power = app.serverBody(withUnlock(sample('amd')), { deployment: 'windows' });
  if (power.includes('nd-note')) assert.match(power, /CPU watts need the &quot;Measure CPU power&quot; option|CPU watts need the "Measure CPU power" option/);
  for (const u of Object.values(UPDATES)) {
    const html = app.updatesCard(u, 'windows');
    assert.doesNotMatch(html, DOCKER_WORDS);
  }
  assert.ok(app.updatesCard(UPDATES.on, 'windows').includes(`<p>Updates come as a new release. To install one: ${WIN_UPDATE} Settings, footage and the Recycle Bin stay as they are.</p>`));
  assert.ok(app.updatesCard(UPDATES.off, 'windows').includes(`<p>Checking for updates is turned off (UPDATE_CHECK=off). ${WIN_UPDATE}</p>`));
  for (const sec of Object.values(SECURITY)) assert.doesNotMatch(app.securityCard(sec, 'windows'), DOCKER_WORDS);
  const sec = app.securityCard(SECURITY.windows, 'windows');
  assert.ok(sec.includes('runs as NT SERVICE\\SentrySixManager, not an administrator'));
  assert.ok(sec.includes(`or the install window or <code>${WIN_LOGS}</code>. If a device`));
  assert.ok(app.securityCard({ ...SECURITY.windows, elevated: true }, 'windows').includes('runs as an administrator'));
  for (const v of VEHICLES) assert.doesNotMatch(app.vehicleForm(v, 'windows').replace(/\/\/nas\/TeslaCam/, ''), DOCKER_WORDS);
  assert.ok(app.vehicleForm(VEHICLES[1], 'windows').includes('>Folder on this PC</button>'));
  assert.ok(app.vehicleForm(VEHICLES[1], 'windows').includes('placeholder="D:\\TeslaCam"'));
  assert.ok(app.logsPart().includes('Warnings and errors from the server, the converter and the service.'));
  assert.equal(app.printedIn('windows'), `the install window or <code>${WIN_LOGS}</code>`);
});

test('app.js: a server that says how to update is shown in its own words, escaped', () => {
  const app = appParts();
  for (const d of ['docker', 'windows', null]) {
    const html = app.updatesCard({ ...UPDATES.on, how: 'Run <update.ps1> as administrator' }, d);
    assert.ok(html.includes('To install one: Run &lt;update.ps1&gt; as administrator. Settings'), String(d));
    assert.doesNotMatch(html, /docker compose/);
    assert.ok(app.updatesCard({ ...UPDATES.off, how: 'Run update.ps1.' }, d).includes('(UPDATE_CHECK=off). Run update.ps1.</p>'));
  }
  assert.equal(app.toldHow({ how: '  ' }), '');
  assert.equal(app.toldHow({ how: 42 }), '');
  assert.equal(app.toldHow({}), '');
  assert.equal(app.toldHow({ how: 'Run it!' }), 'Run it!');
});

// web/critical.js: the bar's how line, from its marked block
function howToInstall() {
  const src = read('web/critical.js');
  const start = src.indexOf('// ---- how to install');
  const end = src.indexOf('// ---- how to install: end ----');
  assert.ok(start > 0 && end > start, 'the how block is marked in critical.js');
  return new Function(`${src.slice(start, end)}; return howToInstall;`)();
}

test('critical.js: Docker keeps its command and the TrueNAS line; Windows and the server\'s words replace them', () => {
  const how = howToInstall();
  const docker = { run: 'Run where you set it up:', code: 'docker compose pull && docker compose up -d', nas: 'TrueNAS: docker pull the image in System → Shell, then Stop and Start the app.' };
  assert.deepEqual(how({ critical: {} }, 'docker'), docker);
  assert.deepEqual(how({ critical: {} }, null), docker);
  assert.deepEqual(how({ critical: {} }, 'linux'), docker);
  assert.deepEqual(how({ critical: {} }, 'windows'), { run: WIN_UPDATE, code: null, nas: null });
  assert.deepEqual(how({ how: ' Run update.ps1 now. ' }, 'docker'), { run: 'Run update.ps1 now.', code: null, nas: null });
  assert.deepEqual(how({ how: '' }, 'docker'), docker);
  // it is written with textContent only (the server's words are never HTML)
  assert.doesNotMatch(read('web/critical.js'), /innerHTML/);
});

// The four packs: the same deployment block at the end of each core.js
const PACKS = { aurora: 'aurora', daylight: 'dl', flux: 'flux', horizon: 'hz' };
function coreBlock(pack) {
  const src = read(`examples/ui-packs/${pack}/web/${PACKS[pack]}/core.js`);
  const start = src.indexOf('// Where the server runs (public GET /api/info');
  assert.ok(start > 0, `${pack}: core.js asks where the server runs`);
  return src.slice(start);
}

test('packs: one deployment block in every core.js, Docker until the server says Windows', async () => {
  const block = coreBlock('flux');
  for (const pack of Object.keys(PACKS)) assert.equal(coreBlock(pack), block, `${pack} has the same block`);
  const load = (fetch, setTimeout = () => {}) => new Function('fetch', 'setTimeout', `${block.replace(/^export /gm, '')}; return { where, onWindows, whereRunning, toldHow, WIN_UPDATE, WIN_LOGS };`)(fetch, setTimeout);
  const run = async (answer) => {
    const fetch = async () => (answer === null ? { ok: false } : { ok: true, json: async () => answer });
    const m = load(fetch);
    assert.equal(m.onWindows(), false, 'Docker words until the answer is in');
    await m.whereRunning;
    return m;
  };
  assert.equal((await run({ deployment: 'windows' })).onWindows(), true);
  assert.equal((await run({ deployment: 'docker' })).onWindows(), false);
  assert.equal((await run(null)).onWindows(), false, 'no /api/info yet: Docker');
  const m = await run({});
  assert.equal(m.WIN_UPDATE, WIN_UPDATE);
  assert.equal(m.WIN_LOGS, WIN_LOGS);
  assert.equal(m.toldHow({ how: 'Run update.ps1' }), 'Run update.ps1.');

  // a server that never answers holds Settings up for 3 seconds at most,
  // and an answer that comes later still counts
  let late;
  let timer;
  const slow = load(() => new Promise((resolve) => { late = resolve; }), (fn, ms, value) => { timer = { fn, ms, value }; });
  assert.equal(timer.ms, 3000);
  timer.fn(timer.value);
  assert.equal(await slow.whereRunning, null);
  assert.equal(slow.onWindows(), false);
  late({ ok: true, json: async () => ({ deployment: 'windows' }) });
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(slow.onWindows(), true);
});

test('packs: the Docker sentences are unchanged and each has its Windows branch', () => {
  const has = (rel, text) => assert.ok(read(rel).includes(text), `${rel}: ${text.slice(0, 70)}`);
  const P = (pack, rel) => `examples/ui-packs/${pack}/web/${PACKS[pack]}/${rel}`;
  // updates
  has(P('aurora', 'pages/settings.js'), "`Updates come as a new image: run ${how} where you set up Sentry Six Manager. Settings, footage and the Recycle Bin stay as they are.`");
  has(P('daylight', 'pages/settings.js'), "`Checking for updates is turned off (UPDATE_CHECK=off). To update, run ${how}.`");
  has(P('daylight', 'pages/settings.js'), '${release || `Run ${how} where you set up Sentry Six Manager.`} Settings, footage and the Recycle Bin stay as they are.');
  for (const pack of ['flux', 'horizon']) has(P(pack, 'pages/settings.js'), `'Updates come as a new image. To install one, run ${COMMAND_HTML} where you set up Sentry Six Manager. Settings, footage and the Recycle Bin stay.'`);
  for (const pack of Object.keys(PACKS)) {
    const settings = read(P(pack, 'pages/settings.js'));
    assert.match(settings, /toldHow\(u\)/, `${pack}: the server's own words first`);
    assert.match(settings, /told \|\| onWindows\(\) \? (esc|h)\(told \|\| WIN_UPDATE\)/, `${pack}: then the Windows steps`);
    assert.match(settings, /whereRunning/, `${pack}: waits for the answer before drawing settings`);
    assert.match(settings, /'Folder on this PC'|A folder on a drive of this PC/, `${pack}: a folder on Windows is on the PC`);
  }
  // compose lines and Docker Desktop's CPU note
  for (const [pack, rel] of [['aurora', 'pages/sky.js'], ['daylight', 'pages/server.js'], ['flux', 'pages/overview.js'], ['horizon', 'pages/dashboard.js']]) {
    const src = read(P(pack, rel));
    assert.match(src, /CPU watts aren\\'t available on Docker Desktop for Windows/, `${pack}: Docker's note kept`);
    assert.match(src, /CPU_NA_WINDOWS/, `${pack}: Windows' own note`);
    assert.match(src, /onWindows\(\) \? \[\] :|&& !onWindows\(\)/, `${pack}: no compose lines on Windows`);
  }
});

// The sign-in pages: on Windows the code and fingerprint lines say the
// install window and service.log. Each login.js changes its own page's text
// around <code>docker logs sentry-six-manager</code>; run that on the
// sentences of each login.html.
function windowsWordsOf(src) {
  const start = src.indexOf('  function windowsWords() {');
  assert.ok(start > 0, 'login.js has windowsWords()');
  const end = src.indexOf('\n  }\n', start);
  return src.slice(start, end + 5);
}
function textBeforeCodes(html) {
  return [...html.matchAll(/>([^<>]*)<code>docker logs sentry-six-manager<\/code>/g)].map((m) => m[1]);
}

test('pack sign-in pages: the setup code and fingerprint lines read right on Windows, as they were on Docker', () => {
  for (const pack of Object.keys(PACKS)) {
    const js = read(`examples/ui-packs/${pack}/web/login.js`);
    const html = read(`examples/ui-packs/${pack}/web/login.html`);
    const befores = textBeforeCodes(html);
    assert.equal(befores.length, 2, `${pack}: the setup code hint and the fingerprint line`);
    const codes = befores.map((t) => ({ textContent: 'docker logs sentry-six-manager', previousSibling: { nodeType: 3, textContent: t } }));
    const document = { querySelectorAll: () => codes };
    const said = new Function('document', 'Node', `let logWhere = 'the container log';${windowsWordsOf(js)} windowsWords(); return logWhere;`)(document, { TEXT_NODE: 3 });
    assert.equal(said, 'the install window');
    for (const c of codes) {
      assert.equal(c.textContent, WIN_LOGS);
      assert.doesNotMatch(c.previousSibling.textContent, DOCKER_WORDS, `${pack}: ${c.previousSibling.textContent}`);
      assert.match(c.previousSibling.textContent, /install window(,| and) (in )?$/);
    }
    // on Docker the page keeps its words: only an answer of 'windows' changes them
    assert.match(js, /if \(await askWindows\) windowsWords\(\);/);
    assert.match(js, /info\?\.deployment === 'windows'/);
    assert.doesNotMatch(js, /'[^'\n]*from the container log[^'\n]*'/, `${pack}: the setup code lines follow logWhere`);
  }
});

test('built-in sign-in page: Docker\'s words stay in login.html; login.js swaps them on Windows only', () => {
  const html = read('web/login.html');
  assert.ok(html.includes('Printed in the container log: <code>docker logs sentry-six-manager</code>'));
  assert.ok(html.includes('The same as in <code>docker logs sentry-six-manager</code> and in your browser\'s certificate details.'));
  const js = read('web/login.js');
  assert.match(js, /const windows = info\?\.deployment === 'windows';\n  if \(windows\) \{/);
  assert.ok(js.includes("`This is the first start. Enter the setup code from ${windows ? 'the install window' : 'the container log'}, then choose the user name and password that protect this Sentry Six Manager.`"));
});

// Docker's drawing against snapshots made from the code before the Windows
// wording (commit 7d1e6f7) with the same harness: app.js's monitor, update,
// security, vehicle and log parts, and the four packs' Settings sections.
const SNAPSHOT = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', 'deploymentWording-docker.json'), 'utf8'));
const realToldHow = (u) => {
  const how = typeof u?.how === 'string' ? u.how.trim() : '';
  return how ? (/[.!?]$/.test(how) ? how : `${how}.`) : '';
};

test('app.js on Docker draws exactly what it drew before the Windows wording', (t) => {
  const now = harness.drawApp(read('web/app.js'), sample, 'docker');
  assert.deepEqual(Object.keys(now), Object.keys(SNAPSHOT.app));
  // the monitor's numbers are written in this machine's language: the
  // snapshot is in English, so elsewhere only the other parts are compared
  const english = Intl.NumberFormat().resolvedOptions().locale === 'en-US';
  if (!english) t.diagnostic('not en-US: the monitor pages are not compared');
  for (const [k, html] of Object.entries(SNAPSHOT.app)) {
    if (!english && k.startsWith('serverBody/')) continue;
    assert.equal(now[k], html, k);
  }
});

test('packs on Docker draw their Settings sections exactly as before; on Windows without Docker\'s words', async () => {
  const docker = await harness.drawPacks((rel) => read(rel), { onWindows: () => false, toldHow: realToldHow, WIN_UPDATE });
  assert.deepEqual(Object.keys(docker), Object.keys(SNAPSHOT.packs));
  for (const [k, html] of Object.entries(SNAPSHOT.packs)) assert.equal(docker[k], html, k);

  const windows = await harness.drawPacks((rel) => read(rel), { onWindows: () => true, toldHow: realToldHow, WIN_UPDATE });
  for (const [k, html] of Object.entries(windows)) {
    assert.doesNotMatch(html, /docker|compose|container|supervis/i, k);
    if (k.includes('/updates/on')) assert.ok(html.includes(WIN_UPDATE), k);
  }
});

const PACK_SYSTEM_WINDOWS = { deployment: 'windows', converter: { ready: true, capabilities: {} }, gpu: { gpus: [], nvidia: [] }, dataFree: 1, timezone: 'UTC', security: { https: true, tlsMode: 'auto', supervised: false } };
test('flux and horizon: the converted folder of a Windows data folder is joined with a backslash', async () => {
  for (const pack of ['flux', 'horizon']) {
    const src = read(`examples/ui-packs/${harness.SECTIONS[pack].dir}/pages/settings.js`);
    const answers = { '/api/system': PACK_SYSTEM_WINDOWS, '/api/stats': { filesystems: [{ path: 'C:\\ProgramData\\SentrySixManager\\data' }] } };
    const win = await harness.packSections(pack, src, answers, { onWindows: () => true }).system();
    // the stub esc() prints its argument as JSON, so each backslash shows doubled
    assert.ok(win.includes('C:\\\\ProgramData\\\\SentrySixManager\\\\data\\\\converted'), `${pack}: ${win.match(/.{0,40}converted.{0,60}/g)}`);
    const docker = await harness.packSections(pack, src, { ...answers, '/api/stats': { filesystems: [{ path: '/data' }] } }).system();
    assert.ok(docker.includes('/data/converted'), pack);
  }
});

// The plain-HTTP continue page (web/continue.html) cannot ask /api/info, so
// the server fills in where the fingerprint is printed (server/continueWording.js).
test('continue page: Docker\'s page as it is; Windows points at the install window and service.log', () => {
  const html = read('web/continue.html');
  assert.equal(html.split(DOCKER_LOG).length, 2, 'continue.html has the container log line once');
  assert.equal(continueTemplate(html, 'docker'), html);
  assert.equal(continueTemplate(html, 'linux'), html);
  assert.equal(continueTemplate(html, undefined), html);
  const win = continueTemplate(html, 'windows');
  assert.doesNotMatch(win, DOCKER_WORDS);
  assert.ok(win.includes(`which is also in the install window or <code>${WIN_LOGS}</code>:</p>`));
  assert.ok(win.includes('{{FINGERPRINT}}') && win.includes('{{HTTPS_URL}}'), 'the other placeholders are left for the server');
  assert.equal(fingerprintFallback('docker'), 'shown in the container log');
  assert.equal(fingerprintFallback(undefined), 'shown in the container log');
  assert.doesNotMatch(fingerprintFallback('windows'), DOCKER_WORDS);
});
