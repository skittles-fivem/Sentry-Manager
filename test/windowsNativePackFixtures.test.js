'use strict';

// The UI packs' native pages are rendered (windows.yml "Render the pages",
// SentrySix.exe --render <pack> <name>) from the fixture variants
// tools/parity/packFixtures.js writes into windows/app/Shell/Fixtures/ from
// the page modules in tools/parity/packs/. These checks keep the three in
// step: the files are what the modules give, every pack page in
// RENDER-PAGES.md is a route of its pack or one of its modules' pages (else
// the exe throws "No page ..."), and every module's variant routes to one of
// the pack's pages.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const packFixtures = require('../tools/parity/packFixtures');

const REPO = path.join(__dirname, '..');
const PACKS = ['horizon', 'flux', 'aurora', 'daylight'];
const IFACE = { horizon: 'Horizon/HorizonInterface.cs', flux: 'Flux/FluxInterface.cs', aurora: 'Aurora/AuroraInterface.cs', daylight: 'Daylight/DaylightInterface.cs' };
const routesOf = (pack) => new Set([...fs.readFileSync(path.join(REPO, 'windows/app/Ui', IFACE[pack]), 'utf8').matchAll(/new PageDef\("([a-z0-9-]+)"/g)].map((x) => x[1]));

test('native app: windows/app/Shell/Fixtures is what tools/parity/packs gives (node tools/parity/packFixtures.js)', () => {
  assert.deepEqual(packFixtures.run(true), [], 'rewrite them with node tools/parity/packFixtures.js');
  const have = fs.existsSync(path.join(REPO, 'windows/app/Shell/Fixtures')) ? fs.readdirSync(path.join(REPO, 'windows/app/Shell/Fixtures')).sort() : [];
  assert.deepEqual(have, packFixtures.modules().map((m) => `${m.name}.json`).sort(), 'one fixture file per module, no stray ones');
});

test('native app: every pack page in RENDER-PAGES.md is a page of its pack or a pack fixture variant', () => {
  const list = fs.readFileSync(path.join(REPO, 'windows/app/Shell/RENDER-PAGES.md'), 'utf8');
  const yml = fs.readFileSync(path.join(REPO, '.github/workflows/windows.yml'), 'utf8');
  const rx = new RegExp(/\$rx = '([^']+)'/.exec(yml)[1]);
  const named = {};
  for (const m of packFixtures.modules()) for (const n of Object.keys(m.pages || {})) (named[m.pack] = named[m.pack] || new Set()).add(n);
  const unknown = [];
  for (const line of list.split(/\r?\n/)) {
    const p = rx.exec(line);
    if (!p || !PACKS.includes(p[1])) continue;
    const ids = new Set(['shell', 'login', 'login-setup', 'login-local', ...routesOf(p[1])]);
    if (!ids.has(p[2]) && !(named[p[1]] && named[p[1]].has(p[2]))) unknown.push(`${p[1]} ${p[2]}`);
  }
  assert.deepEqual(unknown, [], 'pack pages in RENDER-PAGES.md that SentrySix.exe --render cannot find');
  // and every module page is listed (so the app job renders it)
  const listed = new Set(list.split(/\r?\n/).map((l) => rx.exec(l)).filter(Boolean).map((p) => `${p[1]} ${p[2]}`));
  const missing = [];
  for (const [pack, names] of Object.entries(named)) for (const n of names) if (!listed.has(`${pack} ${n}`)) missing.push(`${pack} ${n}`);
  assert.deepEqual(missing, [], 'pack pages with a variant that RENDER-PAGES.md does not list');
});

test('native app: every pack fixture variant opens a page of its pack', () => {
  const fixture = JSON.parse(fs.readFileSync(path.join(REPO, 'windows/app/Shell/RenderFixture.json'), 'utf8'));
  for (const m of packFixtures.modules()) {
    assert.ok(PACKS.includes(m.pack), `${m.file}: pack ${m.pack}`);
    assert.ok(m.file.startsWith(`${m.pack}-`), `${m.file} is named <pack>-<page>.js`);
    const routes = routesOf(m.pack);
    for (const [k, v] of Object.entries(packFixtures.variantsOf(m, fixture))) {
      const route = v.route || k.split(':')[1].split('-')[0];
      assert.ok(routes.has(route), `${k}: route ${route} is a page of ${m.pack}`);
    }
    for (const [n, spec] of Object.entries(m.pages || {})) assert.ok(typeof spec.path === 'string' && spec.path.startsWith('/#/'), `${m.file} ${n}: the web page's path`);
  }
});
