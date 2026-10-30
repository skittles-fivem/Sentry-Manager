'use strict';

// The Docker image and the native Windows build come from one tree but must
// never clash (docs/plans/windows.md, "The builds never clash"): a change to
// windows/ does not rebuild or republish the image the owner runs, nothing
// under windows/ goes into the image, and the Windows workflow does not run
// for Docker-only files or touch the image registry.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const read = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');
const WINDOWS_YML = path.join(REPO, '.github', 'workflows', 'windows.yml');

// Every `paths-ignore:` list in a workflow, in either YAML form:
//   paths-ignore: ['a', "b/**"]
//   paths-ignore:
//     - a
//     - 'b/**'
function pathsIgnoreLists(yml) {
  const lines = yml.split(/\r?\n/);
  const lists = [];
  const unquote = (s) => s.trim().replace(/^(['"])(.*)\1$/, '$2');
  for (let i = 0; i < lines.length; i++) {
    const m = lines[i].match(/^(\s*)paths-ignore:\s*(.*?)\s*(#.*)?$/);
    if (!m) continue;
    if (m[2].startsWith('[')) {
      lists.push(m[2].replace(/^\[|\]$/g, '').split(',').map(unquote).filter(Boolean));
      continue;
    }
    const items = [];
    for (let j = i + 1; j < lines.length; j++) {
      const item = lines[j].match(/^\s*-\s+(.*?)\s*(#.*)?$/);
      if (item) { items.push(unquote(item[1])); continue; }
      if (lines[j].trim() === '' || /^\s*#/.test(lines[j])) continue;
      break;
    }
    lists.push(items);
  }
  return lists;
}

test('image.yml does not run for Windows-only changes (and keeps its old exceptions)', () => {
  const lists = pathsIgnoreLists(read('.github/workflows/image.yml'));
  assert.ok(lists.length >= 1, 'image.yml has a paths-ignore list');
  for (const list of lists) {
    for (const p of ['windows/**', '.github/workflows/windows.yml', 'README.md', 'docs/**', 'truenas/**']) {
      assert.ok(list.includes(p), `image.yml paths-ignore has ${p}: ${JSON.stringify(list)}`);
    }
  }
});

test('.dockerignore leaves windows/ out of the build context', () => {
  const entries = read('.dockerignore').split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith('#'));
  assert.ok(entries.some((e) => /^\/?windows\/?(\*\*)?$/.test(e)), entries.join('\n'));
  // and nothing re-includes it
  assert.equal(entries.some((e) => e.startsWith('!') && /windows/.test(e)), false);
});

test('the Dockerfile copies nothing from windows/', () => {
  const docker = read('Dockerfile');
  const copies = docker.split(/\r?\n/).filter((l) => /^\s*(COPY|ADD)\s/i.test(l) && !/--from=/.test(l));
  assert.ok(copies.length > 0);
  for (const line of copies) {
    const args = line.trim().split(/\s+/).slice(1).filter((a) => !a.startsWith('--'));
    const sources = args.slice(0, -1);
    for (const src of sources) {
      assert.doesNotMatch(src.replace(/^\.\//, ''), /^windows(\/|$)/, line);
      assert.notEqual(src, '.', `COPY . would rely on .dockerignore alone: ${line}`);
    }
  }
  assert.doesNotMatch(docker, /\bwindows\//, 'no windows/ path anywhere in the Dockerfile');
});

test('windows.yml does not run for Docker-only changes and never touches the image registry', {
  skip: fs.existsSync(WINDOWS_YML) ? false : '.github/workflows/windows.yml does not exist yet'
}, () => {
  const yml = fs.readFileSync(WINDOWS_YML, 'utf8');
  const lists = pathsIgnoreLists(yml);
  assert.ok(lists.length >= 1, 'windows.yml has a paths-ignore list');
  for (const list of lists) {
    for (const p of ['Dockerfile', 'docker-compose.yml', 'truenas/**']) {
      assert.ok(list.includes(p), `windows.yml paths-ignore has ${p}: ${JSON.stringify(list)}`);
    }
  }
  assert.doesNotMatch(yml, /ghcr\.io|docker\/build-push-action|docker\/login-action/);
});

test('the parser reads both YAML list forms', () => {
  assert.deepEqual(pathsIgnoreLists("on:\n  push:\n    paths-ignore: ['a', \"b/**\" ,c]\n"), [['a', 'b/**', 'c']]);
  assert.deepEqual(pathsIgnoreLists("on:\n  push:\n    paths-ignore:\n      - Dockerfile\n      # note\n      - 'truenas/**'\n  workflow_dispatch:\n"),
    [['Dockerfile', 'truenas/**']]);
});
