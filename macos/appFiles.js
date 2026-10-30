'use strict';

// The app files of the macOS package: the ones the Dockerfile copies into
// /app, read from the Dockerfile and .dockerignore themselves, so the two
// builds ship the same app. The same rules as windows/build.ps1.
//
//   node macos/appFiles.js list <repo>          prints "from<TAB>to" per file
//   node macos/appFiles.js copy <repo> <dest>   copies them into <dest>
//
// The final stage's COPY lines after `WORKDIR /app` that copy from the build
// context (not --from another stage) are the app; .dockerignore then drops
// files from them as `docker build` does. Anything that does not fit this
// (another WORKDIR, a wildcard, a destination outside /app) stops the build
// rather than being guessed at, so a Dockerfile change cannot quietly leave
// the macOS package behind.

const fs = require('fs');
const path = require('path');

function fail(msg) {
  throw new Error(`appFiles: ${msg}`);
}

// Lines without comments, with continuation lines joined.
function dockerLines(text) {
  const lines = [];
  let cur = '';
  for (const l of String(text).split(/\r?\n/)) {
    if (/^\s*#/.test(l)) continue;
    if (/\\\s*$/.test(l)) {
      cur += l.replace(/\\\s*$/, ' ');
      continue;
    }
    lines.push((cur + l).trim());
    cur = '';
  }
  if (cur) lines.push(cur.trim());
  return lines.filter(Boolean);
}

// [{ src, dest, intoDir }] for the final stage's COPY lines into /app.
function dockerCopies(text) {
  const lines = dockerLines(text);
  let last = -1;
  lines.forEach((l, i) => {
    if (/^FROM\s/.test(l)) last = i;
  });
  if (last < 0) fail('Dockerfile has no FROM');
  let workdir = null;
  const copies = [];
  for (const l of lines.slice(last + 1)) {
    const wd = /^WORKDIR\s+(\S+)$/.exec(l);
    if (wd) {
      workdir = wd[1];
      continue;
    }
    const m = /^COPY\s+(.*)$/.exec(l);
    if (!m) continue;
    let words = m[1].split(/\s+/).filter(Boolean);
    // from another stage: node, native
    if (words.some((w) => w.startsWith('--from='))) continue;
    // --chown, --chmod: install.sh sets the owners
    words = words.filter((w) => !w.startsWith('--'));
    // system files (jellyfin.gpg)
    if (workdir === null) continue;
    if (workdir !== '/app') fail(`Dockerfile WORKDIR is ${workdir}, appFiles.js knows /app only`);
    if (words.length < 2) fail(`Dockerfile line not understood: ${l}`);
    let dest = words[words.length - 1];
    if (dest === '/app' || dest.startsWith('/app/')) dest = `.${dest.slice(4)}`;
    if (dest.startsWith('/')) fail(`Dockerfile copies to ${dest}, outside /app: ${l}`);
    for (const s of words.slice(0, -1)) {
      if (/[*?[]/.test(s)) fail(`Dockerfile COPY with a wildcard is not supported here: ${l}`);
      copies.push({ src: s.replace(/\/+$/, ''), dest, intoDir: words.length > 2 || dest.endsWith('/') });
    }
  }
  if (!copies.length) fail('found no COPY into /app in the Dockerfile');
  return copies;
}

function escapeRx(c) {
  return c.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&');
}

// .dockerignore patterns as Docker reads them: paths from the context root,
// * and ? within one name, ** across names, ! re-includes, the last match wins,
// and a pattern that matches a folder takes everything in it.
function ignoreRules(text) {
  const rules = [];
  for (const l of String(text || '').split(/\r?\n/)) {
    let p = l.trim();
    if (!p || p.startsWith('#')) continue;
    const negate = p.startsWith('!');
    if (negate) p = p.slice(1).trim();
    p = p.replace(/\\/g, '/').replace(/^\/+|\/+$/g, '').replace(/^\.\//, '');
    let rx = '';
    for (let i = 0; i < p.length; i++) {
      const c = p[i];
      if (c === '*' && p[i + 1] === '*') {
        i++;
        if (p[i + 1] === '/') {
          i++;
          rx += '(?:.*/)?';
        } else {
          rx += '.*';
        }
      } else if (c === '*') {
        rx += '[^/]*';
      } else if (c === '?') {
        rx += '[^/]';
      } else {
        rx += escapeRx(c);
      }
    }
    rules.push({ rx: new RegExp(`^${rx}$`), negate });
  }
  return rules;
}

function isIgnored(rules, rel) {
  const parts = rel.split('/');
  let ignored = false;
  for (const r of rules) {
    for (let n = 1; n <= parts.length; n++) {
      if (r.rx.test(parts.slice(0, n).join('/'))) {
        ignored = !r.negate;
        break;
      }
    }
  }
  return ignored;
}

// Every file below dir, as paths relative to it with '/', sorted.
function walk(dir, fsi) {
  const out = [];
  const visit = (rel) => {
    const entries = fsi.readdirSync(path.join(dir, rel), { withFileTypes: true });
    for (const e of entries) {
      const r = rel ? `${rel}/${e.name}` : e.name;
      if (e.isDirectory()) visit(r);
      else if (e.isFile()) out.push(r);
      else fail(`${path.join(dir, r)} is neither a file nor a folder`);
    }
  };
  visit('');
  return out.sort();
}

// [{ from, to }]: repo-relative source, app-relative destination.
function appFiles(repo, fsi = fs) {
  const copies = dockerCopies(fsi.readFileSync(path.join(repo, 'Dockerfile'), 'utf8'));
  const ignoreFile = path.join(repo, '.dockerignore');
  const rules = ignoreRules(fsi.existsSync(ignoreFile) ? fsi.readFileSync(ignoreFile, 'utf8') : '');
  const files = [];
  for (const { src, dest, intoDir } of copies) {
    const srcPath = path.join(repo, src);
    const destRel = dest.replace(/^\.\/?/, '').replace(/\/+$/, '');
    if (!fsi.existsSync(srcPath)) fail(`the Dockerfile copies ${src}, which does not exist`);
    if (fsi.statSync(srcPath).isDirectory()) {
      // COPY dir dest: the folder's contents go into dest
      for (const inside of walk(srcPath, fsi)) {
        if (isIgnored(rules, `${src}/${inside}`)) continue;
        files.push({ from: `${src}/${inside}`, to: [destRel, inside].filter(Boolean).join('/') });
      }
    } else {
      if (isIgnored(rules, src)) continue;
      const to = intoDir || !destRel ? [destRel, path.basename(src)].filter(Boolean).join('/') : destRel;
      files.push({ from: src, to });
    }
  }
  return files;
}

function copyApp(repo, dest, fsi = fs) {
  const files = appFiles(repo, fsi);
  for (const { from, to } of files) {
    const target = path.join(dest, to);
    fsi.mkdirSync(path.dirname(target), { recursive: true });
    fsi.copyFileSync(path.join(repo, from), target);
  }
  return files.length;
}

module.exports = { dockerLines, dockerCopies, ignoreRules, isIgnored, appFiles, copyApp };

if (require.main === module) {
  const [cmd, repo, dest] = process.argv.slice(2);
  try {
    if (cmd === 'list' && repo) {
      process.stdout.write(appFiles(repo).map((f) => `${f.from}\t${f.to}\n`).join(''));
    } else if (cmd === 'copy' && repo && dest) {
      process.stdout.write(`App: ${copyApp(repo, dest)} files, as the Dockerfile copies them\n`);
    } else {
      process.stderr.write('usage: node appFiles.js list <repo> | copy <repo> <dest>\n');
      process.exitCode = 2;
    }
  } catch (err) {
    process.stderr.write(`${err.message}\n`);
    process.exitCode = 1;
  }
}
