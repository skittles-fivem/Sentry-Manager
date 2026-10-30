'use strict';

// Side-by-side report of the web pages (capture.js) and the native app's
// renders (SentrySix.exe --render; the native-renders artifact of
// .github/workflows/windows.yml), paired by file name:
// <interface>-<page>-<width>x<height>.png.
//
//   node tools/parity/report.js <native-renders folder> [--web <dir>] [--out <file>] [--threshold 32]
//
// Writes tools/parity/out/report.html with, per pair, both pictures, a
// difference picture (the web page dimmed, differing pixels red) and the
// share of pixels that differ. A pixel differs when a colour channel is more
// than --threshold (0-255) apart; sizes that do not match count their extra
// area as different. Fonts and anti-aliasing differ between a browser and
// WPF, so the number is a guide for spotting layout drift, not a pass mark.
// No npm packages: PNGs are read with zlib (png.js).

const fs = require('fs');
const path = require('path');
const { decode, encode, compare } = require('./png');

function args(argv) {
  const o = { native: null, web: path.join(__dirname, 'out', 'web'), out: path.join(__dirname, 'out', 'report.html'), threshold: 32 };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--web') o.web = path.resolve(argv[++i]);
    else if (a === '--out') o.out = path.resolve(argv[++i]);
    else if (a === '--threshold') o.threshold = Number(argv[++i]);
    else if (a.startsWith('--')) throw new Error(`unknown argument ${a}`);
    else if (!o.native) o.native = path.resolve(a);
    else throw new Error(`one native folder only (got ${o.native} and ${a})`);
  }
  if (!o.native) throw new Error('usage: node tools/parity/report.js <native-renders folder> [--web <dir>] [--out <file>] [--threshold 32]');
  if (!Number.isFinite(o.threshold) || o.threshold < 0 || o.threshold > 255) throw new Error('--threshold is 0-255');
  for (const d of [o.native, o.web]) if (!fs.existsSync(d) || !fs.statSync(d).isDirectory()) throw new Error(`no folder ${d}`);
  return o;
}

const NAME = /^([a-z]+)-([a-z0-9-]+)-(\d+)x(\d+)\.png$/;
const ORDER = ['builtin', 'horizon', 'flux', 'aurora', 'daylight'];

function pngs(dir) {
  return fs.readdirSync(dir).filter((f) => NAME.test(f));
}

function sortKey(f) {
  const [, ui, page, w] = f.match(NAME);
  const i = ORDER.indexOf(ui);
  return [i < 0 ? 99 : i, ui, page, -Number(w)];
}

function byKey(a, b) {
  const ka = sortKey(a);
  const kb = sortKey(b);
  for (let i = 0; i < ka.length; i++) if (ka[i] !== kb[i]) return ka[i] < kb[i] ? -1 : 1;
  return 0;
}

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
// a path from the report to a picture, as a URL
const rel = (from, to) => path.relative(path.dirname(from), to).split(path.sep).map(encodeURIComponent).join('/');

function main() {
  const o = args(process.argv.slice(2));
  const outDir = path.dirname(o.out);
  const diffDir = path.join(outDir, 'diff');
  fs.mkdirSync(diffDir, { recursive: true });
  const web = pngs(o.web);
  const native = new Set(pngs(o.native));
  const names = [...new Set([...web, ...native])].sort(byKey);
  const rows = [];
  for (const name of names) {
    const [, ui, page, w, h] = name.match(NAME);
    const row = { name, ui, page, size: `${w}x${h}`, web: null, native: null, diff: null, percent: null, note: '' };
    const webFile = path.join(o.web, name);
    const nativeFile = path.join(o.native, name);
    if (web.includes(name)) row.web = webFile;
    if (native.has(name)) row.native = nativeFile;
    if (row.web && row.native) {
      try {
        const a = decode(fs.readFileSync(webFile));
        const b = decode(fs.readFileSync(nativeFile));
        const r = compare(a, b, { threshold: o.threshold });
        row.percent = r.percent;
        if (a.width !== b.width || a.height !== b.height) row.note = `sizes differ: web ${a.width}x${a.height}, native ${b.width}x${b.height}`;
        row.diff = path.join(diffDir, name);
        fs.writeFileSync(row.diff, encode(r.diff));
      } catch (err) {
        row.note = `could not compare: ${err.message}`;
      }
    } else {
      row.note = row.web ? 'no native render' : 'no web capture';
    }
    rows.push(row);
    console.log(`${name.padEnd(36)} ${row.percent === null ? '   -  ' : `${row.percent.toFixed(2).padStart(6)}%`}  ${row.note}`);
  }

  const compared = rows.filter((r) => r.percent !== null);
  const mean = compared.length ? compared.reduce((n, r) => n + r.percent, 0) / compared.length : null;
  const img = (file, alt) => (file ? `<a href="${rel(o.out, file)}"><img loading="lazy" src="${rel(o.out, file)}" alt="${esc(alt)}"></a>` : '<div class="none">none</div>');
  const tone = (p) => (p === null ? 'na' : p < 5 ? 'good' : p < 20 ? 'fair' : 'poor');
  const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Native parity report</title>
<style>
  :root { --bg: #f6f6f4; --fg: #1c1f22; --muted: #5d646b; --card: #ffffff; --line: #dcdcd8; --good: #1d7a3a; --fair: #9a6700; --poor: #b42318; }
  @media (prefers-color-scheme: dark) { :root { --bg: #111315; --fg: #e8eaec; --muted: #9aa2aa; --card: #1a1d20; --line: #2c3035; --good: #5bc27a; --fair: #e3b341; --poor: #f47067; } }
  * { box-sizing: border-box; }
  body { margin: 0; padding: 24px 16px 48px; background: var(--bg); color: var(--fg); font: 14px/1.45 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  p.meta { color: var(--muted); margin: 0 0 20px; }
  table.sum { border-collapse: collapse; margin-bottom: 28px; max-width: 100%; display: block; overflow-x: auto; }
  table.sum td, table.sum th { padding: 4px 12px 4px 0; text-align: left; border-bottom: 1px solid var(--line); white-space: nowrap; }
  .pct { font-variant-numeric: tabular-nums; font-weight: 600; }
  .good { color: var(--good); } .fair { color: var(--fair); } .poor { color: var(--poor); } .na { color: var(--muted); }
  section { background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: 14px; margin-bottom: 18px; }
  section h2 { font-size: 15px; margin: 0 0 10px; display: flex; gap: 12px; flex-wrap: wrap; align-items: baseline; }
  section h2 .note { color: var(--muted); font-weight: 400; font-size: 13px; }
  .trio { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; }
  .trio.narrow { grid-template-columns: repeat(3, minmax(0, 260px)); }
  figure { margin: 0; min-width: 0; }
  figcaption { color: var(--muted); font-size: 12px; margin-bottom: 4px; }
  img { width: 100%; height: auto; display: block; border: 1px solid var(--line); background: #000; }
  .none { border: 1px dashed var(--line); color: var(--muted); padding: 40px 8px; text-align: center; }
  @media (max-width: 760px) { .trio, .trio.narrow { grid-template-columns: minmax(0, 1fr); } }
</style>
</head>
<body>
<h1>Native parity report</h1>
<p class="meta">Web: ${esc(o.web)} &middot; native: ${esc(o.native)} &middot; ${compared.length} of ${rows.length} pairs compared${mean === null ? '' : `, mean difference ${mean.toFixed(2)}%`} &middot; a pixel differs when a channel is more than ${o.threshold}/255 apart &middot; ${esc(new Date().toISOString())}</p>
<table class="sum">
<tr><th>Interface</th><th>Page</th><th>Size</th><th>Different pixels</th><th></th></tr>
${rows.map((r) => `<tr><td>${esc(r.ui)}</td><td>${esc(r.page)}</td><td>${esc(r.size)}</td><td class="pct ${tone(r.percent)}">${r.percent === null ? '&ndash;' : `${r.percent.toFixed(2)}%`}</td><td>${esc(r.note)}</td></tr>`).join('\n')}
</table>
${rows.map((r) => `<section id="${esc(r.name)}">
<h2>${esc(r.ui)} &middot; ${esc(r.page)} &middot; ${esc(r.size)} <span class="pct ${tone(r.percent)}">${r.percent === null ? '' : `${r.percent.toFixed(2)}% different`}</span> <span class="note">${esc(r.note)}</span></h2>
<div class="trio${Number(r.size.split('x')[0]) < 700 ? ' narrow' : ''}">
<figure><figcaption>Web</figcaption>${img(r.web, `web ${r.name}`)}</figure>
<figure><figcaption>Native</figcaption>${img(r.native, `native ${r.name}`)}</figure>
<figure><figcaption>Difference</figcaption>${img(r.diff, `difference ${r.name}`)}</figure>
</div>
</section>`).join('\n')}
</body>
</html>
`;
  fs.writeFileSync(o.out, html);
  console.log(`${o.out}${mean === null ? '' : ` (mean ${mean.toFixed(2)}% over ${compared.length} pairs)`}`);
}

try {
  main();
} catch (err) {
  console.error(err.message);
  process.exit(1);
}
