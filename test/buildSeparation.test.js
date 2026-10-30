'use strict';

// The Docker image and the native Windows and macOS builds come from one
// tree but must never clash (docs/plans/windows.md and macos.md, "The builds
// never clash"): a change to windows/ or macos/ does not rebuild or
// republish the image the owner runs, nothing under windows/, macos/ or
// native/darwin/ goes into the image, and the Windows and macOS workflows do
// not run for each other's or Docker-only files, or touch the image registry.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const read = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');
const WINDOWS_YML = path.join(REPO, '.github', 'workflows', 'windows.yml');
const MACOS_YML = path.join(REPO, '.github', 'workflows', 'macos.yml');
const MAC_ONLY = ['macos/**', 'native/darwin/**', '.github/workflows/macos.yml'];
// a workflow's lines without its comments (comments may name anything)
const stepsOf = (yml) => yml.split('\n').filter((line) => !/^\s*#/.test(line)).join('\n');

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

test('image.yml and windows.yml do not run for macOS-only changes', () => {
  for (const wf of ['image.yml', 'windows.yml']) {
    const lists = pathsIgnoreLists(read(`.github/workflows/${wf}`));
    assert.ok(lists.length >= 1, `${wf} has a paths-ignore list`);
    for (const list of lists) {
      for (const p of MAC_ONLY) assert.ok(list.includes(p), `${wf} paths-ignore has ${p}: ${JSON.stringify(list)}`);
    }
  }
});

test('.dockerignore leaves macos/ and native/darwin/ out of the build context', () => {
  const entries = read('.dockerignore').split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith('#'));
  assert.ok(entries.some((e) => /^\/?macos\/?(\*\*)?$/.test(e)), entries.join('\n'));
  assert.ok(entries.some((e) => /^\/?native\/darwin\/?(\*\*)?$/.test(e)), entries.join('\n'));
  assert.equal(entries.some((e) => e.startsWith('!') && /macos|darwin/.test(e)), false);
});

test('the Dockerfile copies nothing from macos/ or native/darwin/', () => {
  const docker = read('Dockerfile');
  const copies = docker.split(/\r?\n/).filter((l) => /^\s*(COPY|ADD)\s/i.test(l) && !/--from=/.test(l));
  for (const line of copies) {
    const sources = line.trim().split(/\s+/).slice(1).filter((a) => !a.startsWith('--')).slice(0, -1);
    for (const src of sources.map((x) => x.replace(/^\.\//, ''))) {
      assert.doesNotMatch(src, /^(macos|native\/darwin)(\/|$)/, line);
      // the whole native/ folder would bring native/darwin/ along
      assert.doesNotMatch(src, /^native\/?$/, line);
    }
  }
  assert.doesNotMatch(docker, /\bmacos\/|native\/darwin/, 'no macOS path anywhere in the Dockerfile');
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

test('windows.yml does not run for compose or TrueNAS changes and never touches the image registry', {
  skip: fs.existsSync(WINDOWS_YML) ? false : '.github/workflows/windows.yml does not exist yet'
}, () => {
  const yml = fs.readFileSync(WINDOWS_YML, 'utf8');
  const lists = pathsIgnoreLists(yml);
  assert.ok(lists.length >= 1, 'windows.yml has a paths-ignore list');
  for (const list of lists) {
    // (not the Dockerfile: build.ps1 takes the package's file list from it,
    // so a Dockerfile change can change the Windows package too)
    const compose = list.includes('docker-compose.yml') || list.includes('docker-compose*.yml');
    assert.ok(compose, `windows.yml paths-ignore has docker-compose.yml: ${JSON.stringify(list)}`);
    assert.ok(list.includes('truenas/**'), `windows.yml paths-ignore has truenas/**: ${JSON.stringify(list)}`);
  }
  // (comments may say so; only the steps count)
  const steps = stepsOf(yml);
  assert.doesNotMatch(steps, /ghcr\.io|docker\/build-push-action|docker\/login-action/);
});

test('macos.yml does not run for Docker-only or Windows-only changes, or on pull requests, and never touches the image registry', {
  skip: fs.existsSync(MACOS_YML) ? false : '.github/workflows/macos.yml does not exist yet'
}, () => {
  const yml = fs.readFileSync(MACOS_YML, 'utf8');
  const lists = pathsIgnoreLists(yml);
  assert.ok(lists.length >= 1, 'macos.yml has a paths-ignore list');
  for (const list of lists) {
    for (const p of ['docker-compose*.yml', 'truenas/**', 'windows/**', '.github/workflows/image.yml', '.github/workflows/windows.yml', 'README.md', 'docs/**']) {
      assert.ok(list.includes(p), `macos.yml paths-ignore has ${p}: ${JSON.stringify(list)}`);
    }
    // build.sh takes the package's file list from the Dockerfile
    assert.equal(list.includes('Dockerfile'), false, 'a Dockerfile change can change the macOS package');
    for (const p of MAC_ONLY) assert.equal(list.includes(p), false, `macos.yml must run for ${p}`);
  }
  const steps = stepsOf(yml);
  assert.doesNotMatch(steps, /ghcr\.io|docker\/build-push-action|docker\/login-action/);
  assert.doesNotMatch(steps, /pull_request/);
  assert.match(steps, /branches: \[main, wip\]/);
  assert.match(steps, /workflow_dispatch:/);
  // Apple Silicon runners only, and the release is never Windows' "latest"
  assert.doesNotMatch(steps, /macos-1[0-4]\b|macos-latest-large|macos-\d+-large|x86_64-apple|--arch x86_64/);
  for (const job of ['test', 'package', 'install-check']) assert.match(steps, new RegExp(`\\n  ${job}:[\\s\\S]*?runs-on: macos-15\\n`), job);
  assert.match(steps, /\n  publish:\n[\s\S]*?if: github\.event_name == 'push' && github\.ref == 'refs\/heads\/main'/);
  assert.match(steps, /tag="macos-v\$VERSION-\$SHORT"/);
  assert.match(steps, /--latest=false/);
  assert.doesNotMatch(steps, /--latest\s*(\\|$)/m);
  // the real installer, and the helper built where the tests look for it
  assert.match(steps, /sudo bash macos\/install\.sh --from/);
  assert.match(steps, /-o build\/sentry-fsops native\/darwin\/sentry-fsops\.c/);
  assert.match(steps, /REQUIRE_TOOLS: '1'/);
});

test('windows.yml never runs on pull requests either', { skip: fs.existsSync(WINDOWS_YML) ? false : 'no windows.yml' }, () => {
  assert.doesNotMatch(stepsOf(fs.readFileSync(WINDOWS_YML, 'utf8')), /pull_request/);
});

test('the parser reads both YAML list forms', () => {
  assert.deepEqual(pathsIgnoreLists("on:\n  push:\n    paths-ignore: ['a', \"b/**\" ,c]\n"), [['a', 'b/**', 'c']]);
  assert.deepEqual(pathsIgnoreLists("on:\n  push:\n    paths-ignore:\n      - Dockerfile\n      # note\n      - 'truenas/**'\n  workflow_dispatch:\n"),
    [['Dockerfile', 'truenas/**']]);
});
