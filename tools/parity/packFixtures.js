'use strict';

// The UI packs' pages for the parity check (docs/plans/windows-native-ui.md,
// "Proving it"): every tools/parity/packs/<pack>-<page>.js names its pages
// (RENDER-PAGES.md's "<pack> <name>"), how capture.js takes the web side
// ({ path, variant, clicks, storage, width }) and its fixture variants.
// This writes the native side's: windows/app/Shell/Fixtures/<pack>-<page>.json
// with { variants: { "<pack>:<name>": { route, sub, api, prefs, state } } }
// from the main fixture (windows/app/Shell/RenderFixture.json: its api,
// vehicle and now), which SentrySix.exe --render merges over that file.
//
//   node tools/parity/packFixtures.js [--check]
//
// --check writes nothing and fails when a file is out of date
// (test/windowsNativePackFixtures.test.js runs it).

const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..', '..');
const DIR = path.join(__dirname, 'packs');
const OUT = path.join(REPO, 'windows', 'app', 'Shell', 'Fixtures');
const MAIN = path.join(REPO, 'windows', 'app', 'Shell', 'RenderFixture.json');

/** Every pack page module: { file, name, pack, pages, variants }. */
function modules() {
  if (!fs.existsSync(DIR)) return [];
  return fs.readdirSync(DIR).filter((f) => f.endsWith('.js')).sort().map((f) => {
    // eslint-disable-next-line global-require, import/no-dynamic-require
    const m = require(path.join(DIR, f));
    return { file: f, name: f.replace(/\.js$/, ''), ...m };
  });
}

/** The fixture's clock (its "now", written as UTC without a zone). */
const nowOf = (fixture) => Date.parse(`${fixture.now}Z`);

/** One module's variants, keyed "<pack>:<name>", with {vehicle} filled in. */
function variantsOf(m, fixture) {
  const fill = (x) => JSON.parse(JSON.stringify(x).replace(/\{vehicle\}/g, fixture.vehicle));
  const vs = m.variants ? m.variants(fixture.api, fixture.vehicle, nowOf(fixture)) : {};
  const out = {};
  for (const name of Object.keys(m.pages || {})) {
    if (!vs[name]) throw new Error(`${m.file}: page ${name} has no variant (give it at least { route })`);
    out[`${m.pack}:${name}`] = fill(vs[name]);
  }
  return out;
}

function fileText(m, fixture) {
  const data = {
    about: `Written by tools/parity/packFixtures.js from tools/parity/packs/${m.file} and Shell/RenderFixture.json: the ${m.pack} pages' states for SentrySix.exe --render (RENDER-PAGES.md).`,
    variants: variantsOf(m, fixture)
  };
  return `${JSON.stringify(data, null, 1)}\n`;
}

function run(check) {
  const fixture = JSON.parse(fs.readFileSync(MAIN, 'utf8'));
  const stale = [];
  if (!check) fs.mkdirSync(OUT, { recursive: true });
  for (const m of modules()) {
    const file = path.join(OUT, `${m.name}.json`);
    const text = fileText(m, fixture);
    if (check) { if (!fs.existsSync(file) || fs.readFileSync(file, 'utf8') !== text) stale.push(path.relative(REPO, file)); } else fs.writeFileSync(file, text);
  }
  return stale;
}

module.exports = { modules, variantsOf, run, nowOf };

if (require.main === module) {
  const check = process.argv.includes('--check');
  const stale = run(check);
  if (check && stale.length) { console.error(`out of date (run node tools/parity/packFixtures.js): ${stale.join(', ')}`); process.exit(1); }
  if (!check) console.log(`wrote ${modules().length} file(s) in ${path.relative(REPO, OUT)}`);
}
