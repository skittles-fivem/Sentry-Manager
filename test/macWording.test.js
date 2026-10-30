'use strict';

// The interfaces' wording on a Mac (deployment 'macos', the launchd
// service). Docker's and Windows' words are pinned by
// test/deploymentWording.test.js; here the Mac's lines say Terminal and the
// sentry-six-manager command, and nothing points at Docker, compose, the
// container, /footage, PUID, /dev/dri or TrueNAS.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const harness = require('./deploymentWordingHarness');

process.env.TZ = 'UTC';

const REPO = path.join(__dirname, '..');
const read = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');
const sample = (name = '') => JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', `statsSample${name ? `-${name}` : ''}.json`), 'utf8'));
const DOCKER_WORDS = /docker|compose|container|PUID|TrueNAS/i;
const HOST_WORDS = /\/footage|\/dev\/dri/;
const MAC_UPDATE_HTML = 'In Terminal on the Mac, run <code>sudo sentry-six-manager update</code>.';
const MAC_UPDATE = 'In Terminal on the Mac, run sudo sentry-six-manager update.';

const UPDATES = {
  on: { enabled: true, image: null, tag: 'v1', current: { sha: 'abcdef1234567' }, latest: { revision: '1234567abcdef', created: '2026-10-20T00:00:00Z' }, checkedAt: 1, updateAvailable: true },
  off: { enabled: false, image: null, current: { sha: 'abcdef1234567' } },
  source: { enabled: false, image: null, current: {} }
};
const SECURITY = {
  service: { https: true, runningAsRoot: false, tls: { certificate: { fingerprint: 'AA:BB', validTo: '2027-01-01' }, hostnames: ['mac.local'], ca: { fingerprint: 'CC' } }, currentHost: 'mac.local' },
  named: { https: true, runningAsRoot: false, account: '_sentrysix', tls: {}, tlsMode: 'off' },
  root: { https: false, runningAsRoot: true, tlsMode: 'off', tls: {} }
};
const VEHICLES = [null, { name: 'Car', source: { type: 'path', path: '/Volumes/TESLADRIVE' } }, { name: 'Nas', source: { type: 'smb', share: '//nas/TeslaCam' } }];

test('app.js on a Mac: Terminal and the sentry-six-manager command, no Docker words', () => {
  const app = harness.appParts(read('web/app.js'), { deployment: 'macos', system: null });
  for (const name of ['', 'amd', 'nvidia', 'pi', 'idle']) {
    const s = sample(name);
    const html = app.serverBody({ ...s, unlock: [{ adds: 'Per-process numbers', compose: 'pid: host' }], power: { ...s.power, cpuUnavailable: 'macos' } }, { deployment: 'macos' });
    assert.doesNotMatch(html.replace(/this container/g, ''), /docker|compose/i, `${name}: no compose lines`);
    if (html.includes('nd-note')) assert.match(html, /CPU and GPU watts are not available on a Mac\./);
  }
  for (const u of Object.values(UPDATES)) {
    const html = app.updatesCard(u, 'macos');
    assert.doesNotMatch(html, DOCKER_WORDS);
    assert.doesNotMatch(html, HOST_WORDS);
  }
  assert.ok(app.updatesCard(UPDATES.on, 'macos').includes(`<p>Updates come as a new release. To install one: ${MAC_UPDATE_HTML} Settings, footage and the Recycle Bin stay as they are.</p>`));
  assert.ok(app.updatesCard(UPDATES.off, 'macos').includes(`<p>Checking for updates is turned off (UPDATE_CHECK=off). ${MAC_UPDATE_HTML}</p>`));
  // the server's own words (status().how) come first, as everywhere
  assert.ok(app.updatesCard({ ...UPDATES.on, how: 'To update, run sudo sentry-six-manager update in Terminal' }, 'macos').includes('To install one: To update, run sudo sentry-six-manager update in Terminal. Settings'));

  for (const sec of Object.values(SECURITY)) {
    const html = app.securityCard(sec, 'macos');
    assert.doesNotMatch(html, DOCKER_WORDS);
    assert.doesNotMatch(html, /supervisor/);
  }
  assert.ok(app.securityCard(SECURITY.service, 'macos').includes('runs as the macOS service account <code>_sentrysix</code>, not as root; it connects shares itself'));
  assert.ok(app.securityCard(SECURITY.service, 'macos').includes('certificate details or the installer\'s output in Terminal. If a device'));
  assert.ok(app.securityCard(SECURITY.root, 'macos').includes('runs as root (fine for trying it out, not for the service)'));
  assert.equal(app.printedIn('macos'), 'the installer\'s output in Terminal');

  for (const v of VEHICLES) {
    const html = app.vehicleForm(v, 'macos');
    assert.doesNotMatch(html.replace(/\/\/nas\/TeslaCam/, ''), DOCKER_WORDS);
    assert.doesNotMatch(html, HOST_WORDS);
  }
  const form = app.vehicleForm(VEHICLES[1], 'macos');
  assert.ok(form.includes('>Folder on this Mac</button>'));
  assert.ok(form.includes('placeholder="/Volumes/TESLADRIVE"'));
  // the "Drives on this Mac" picker is a Mac's only (filled from /api/system/volumes)
  assert.ok(form.includes('id="drive-pick"'));
  assert.ok(!app.vehicleForm(VEHICLES[1], 'docker').includes('drive-pick'));
  assert.ok(!app.vehicleForm(VEHICLES[1], 'windows').includes('drive-pick'));
  assert.ok(form.includes('A drive plugged into this Mac (under /Volumes) or a folder in /Users/Shared. For a NAS, use SMB share.'));
  assert.ok(app.vehicleForm(VEHICLES[2], 'macos').includes('Files on the share are never run as programs.'));
  assert.ok(app.logsPart().includes('Warnings and errors from the server, the converter and the service.'));
});

test('app.js: a Mac node\'s missing CPU watts read as not available, on any main', () => {
  const src = read('web/app.js');
  assert.ok(src.includes("macos: 'CPU and GPU watts are not available on a Mac.'"));
  assert.ok(src.includes("macos: 'CPU n/a (Mac)'"));
});

function howToInstall() {
  const src = read('web/critical.js');
  const start = src.indexOf('// ---- how to install');
  const end = src.indexOf('// ---- how to install: end ----');
  return new Function(`${src.slice(start, end)}; return howToInstall;`)();
}

test('critical.js on a Mac: the Terminal command, no TrueNAS line', () => {
  const how = howToInstall();
  assert.deepEqual(how({ critical: {} }, 'macos'), { run: 'In Terminal on the Mac, run:', code: 'sudo sentry-six-manager update', nas: null });
  assert.deepEqual(how({ how: 'Run sudo sentry-six-manager update.' }, 'macos'), { run: 'Run sudo sentry-six-manager update.', code: null, nas: null });
});

test('built-in sign-in page on a Mac: the setup-code command and the installer\'s output', () => {
  const js = read('web/login.js');
  assert.match(js, /const mac = info\?\.deployment === 'macos';/);
  assert.ok(js.includes("replaceChildren('In Terminal on the Mac, run ', code('sudo sentry-six-manager setup-code'))"));
  assert.ok(js.includes('The same as in the installer\\\'s output in Terminal and in your browser\\\'s certificate details.'));
  const lead = js.match(/if \(mac\) \$\('lead'\)\.textContent = '([^']+)';/);
  assert.ok(lead, 'the first-start line on a Mac');
  assert.doesNotMatch(lead[1], DOCKER_WORDS);
  assert.ok(lead[1].includes('sudo sentry-six-manager setup-code'));
});

const PACKS = { aurora: 'aurora', daylight: 'dl', flux: 'flux', horizon: 'hz' };

test('packs: the deployment block knows a Mac', async () => {
  const src = read('examples/ui-packs/flux/web/flux/core.js');
  const block = src.slice(src.indexOf('// Where the server runs (public GET /api/info'));
  const m = new Function('fetch', 'setTimeout', `${block.replace(/^export /gm, '')}; return { onMac, onWindows, whereRunning, MAC_UPDATE };`)(async () => ({ ok: true, json: async () => ({ deployment: 'macos' }) }), () => {});
  assert.equal(m.onMac(), false, 'Docker words until the answer is in');
  await m.whereRunning;
  assert.equal(m.onMac(), true);
  assert.equal(m.onWindows(), false);
  assert.equal(m.MAC_UPDATE, MAC_UPDATE);
});

test('packs on a Mac: Settings sections without Docker\'s words', async () => {
  const toldHow = (u) => {
    const how = typeof u?.how === 'string' ? u.how.trim() : '';
    return how ? (/[.!?]$/.test(how) ? how : `${how}.`) : '';
  };
  const mac = await harness.drawPacks((rel) => read(rel), { onWindows: () => false, onMac: () => true, toldHow, MAC_UPDATE });
  for (const [k, html] of Object.entries(mac)) {
    assert.doesNotMatch(html, /docker|compose|container|supervis|TrueNAS/i, k);
    if (k.includes('/updates/on')) assert.ok(html.includes(MAC_UPDATE), k);
  }
  for (const pack of Object.keys(PACKS)) {
    const settings = read(`examples/ui-packs/${pack}/web/${PACKS[pack]}/pages/settings.js`);
    assert.ok(settings.includes("'/Volumes/TESLADRIVE'"), `${pack}: a Mac drive as the example folder`);
    assert.match(settings, /onMac\(\) \? (esc|h)\(MAC_UPDATE\)/, `${pack}: the Mac's update steps`);
  }
});

test('pack sign-in pages on a Mac: the setup-code command and the installer\'s output', () => {
  for (const pack of Object.keys(PACKS)) {
    const js = read(`examples/ui-packs/${pack}/web/login.js`);
    const html = read(`examples/ui-packs/${pack}/web/login.html`);
    const start = js.indexOf('  function macWords() {');
    assert.ok(start > 0, `${pack}: login.js has macWords()`);
    const fn = js.slice(start, js.indexOf('\n  }\n', start) + 5);
    // the page's two <code>docker logs</code> lines, as text nodes before stand-in code elements
    const codes = [...html.matchAll(/>([^<>]*)<code>docker logs sentry-six-manager<\/code>([^<>]*)</g)].map((m) => {
      const c = { textContent: 'docker logs sentry-six-manager', previousSibling: { nodeType: 3, textContent: m[1] }, after: m[2] };
      c.replaceWith = (text) => { c.replacedBy = text; };
      return c;
    });
    assert.equal(codes.length, 2, pack);
    const document = { querySelectorAll: () => codes };
    const said = new Function('document', 'Node', `let logWhere = 'the container log';${fn} macWords(); return logWhere;`)(document, { TEXT_NODE: 3 });
    assert.equal(said, 'Terminal (sudo sentry-six-manager setup-code)');
    const [setup, fingerprint] = codes;
    assert.equal(setup.textContent, 'sudo sentry-six-manager setup-code');
    assert.equal(setup.previousSibling.textContent, 'In Terminal on the Mac, run: ');
    assert.equal(fingerprint.replacedBy, 'the installer’s output in Terminal');
    assert.doesNotMatch(`${fingerprint.previousSibling.textContent}${fingerprint.replacedBy}${fingerprint.after}`, DOCKER_WORDS, pack);
    assert.match(js, /if \(await askMac\) macWords\(\);/);
    assert.match(js, /info\?\.deployment === 'macos'/);
  }
});

test('the plain-HTTP continue page on a Mac points at Terminal, not Docker', () => {
  const { continueTemplate, fingerprintFallback, DOCKER_LOG } = require('../server/continueWording');
  const html = read('web/continue.html');
  const mac = continueTemplate(html, 'macos');
  assert.ok(html.includes(DOCKER_LOG));
  assert.doesNotMatch(mac, /docker|compose|container/i);
  assert.ok(mac.includes("which is also in the installer's output in Terminal (or <code>sudo sentry-six-manager logs</code>):</p>"));
  assert.doesNotMatch(fingerprintFallback('macos'), /docker|container/i);
  assert.equal(continueTemplate(html, 'docker'), html);
  assert.equal(fingerprintFallback('docker'), 'shown in the container log');
});
