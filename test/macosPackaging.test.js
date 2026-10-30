'use strict';

// The macOS package build: the app file list read from the Dockerfile
// (macos/appFiles.js), BUILD_INFO.json and release.json (macos/buildInfo.js)
// and the pinned downloads (macos/versions.json). build.sh itself needs a
// Mac; its parts that decide what goes in are checked here.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { dockerCopies, ignoreRules, isIgnored, appFiles, copyApp } = require('../macos/appFiles');
const { buildInfo, releaseJson } = require('../macos/buildInfo');

const REPO = path.join(__dirname, '..');
const MACOS = path.join(REPO, 'macos');
const pins = JSON.parse(fs.readFileSync(path.join(MACOS, 'versions.json'), 'utf8'));

const made = [];
function tmp() {
  const d = fs.mkdtempSync(path.join(fs.realpathSync(os.tmpdir()), 'ssm-macpkg-'));
  made.push(d);
  return d;
}
test.after(() => {
  for (const d of made) fs.rmSync(d, { recursive: true, force: true });
});

function writeTree(base, files) {
  for (const [rel, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(base, rel)), { recursive: true });
    fs.writeFileSync(path.join(base, rel), text);
  }
}

test('the final stage\'s COPY lines into /app are the app', () => {
  const copies = dockerCopies([
    'FROM node:24 AS node',
    'WORKDIR /app',
    'COPY ignored-stage-file ./',
    'FROM debian',
    'COPY jellyfin.gpg /etc/apt/keyrings/jellyfin.gpg',
    'COPY --from=node /usr/local/bin/node /usr/local/bin/node',
    'WORKDIR /app',
    '# a comment',
    'COPY version.json \\',
    '     CRITICAL_UPDATE.txt ./',
    'COPY --chown=1000:1000 server ./server',
    'COPY examples/ui-packs/ /app/ui-packs',
    'COPY one.txt renamed.txt',
  ].join('\n'));
  assert.deepEqual(copies, [
    { src: 'version.json', dest: './', intoDir: true },
    { src: 'CRITICAL_UPDATE.txt', dest: './', intoDir: true },
    { src: 'server', dest: './server', intoDir: false },
    { src: 'examples/ui-packs', dest: './ui-packs', intoDir: false },
    { src: 'one.txt', dest: 'renamed.txt', intoDir: false },
  ]);
  assert.throws(() => dockerCopies('FROM x\nWORKDIR /srv\nCOPY a b'), /WORKDIR is \/srv/);
  assert.throws(() => dockerCopies('FROM x\nWORKDIR /app\nCOPY *.js ./'), /wildcard/);
  assert.throws(() => dockerCopies('FROM x\nWORKDIR /app\nCOPY a /opt/a'), /outside \/app/);
  assert.throws(() => dockerCopies('FROM x\nRUN true'), /no COPY into \/app/);
});

test('.dockerignore as Docker reads it', () => {
  const rules = ignoreRules('# c\n.git\n**/.DS_Store\nexamples/ui-packs/*.zip\nexamples/ui-packs/*/README.md\ndocs\n!docs/keep.md\n');
  assert.equal(isIgnored(rules, '.git/config'), true, 'a folder takes everything in it');
  assert.equal(isIgnored(rules, 'server/.DS_Store'), true);
  assert.equal(isIgnored(rules, '.DS_Store'), true);
  assert.equal(isIgnored(rules, 'examples/ui-packs/aurora.zip'), true);
  assert.equal(isIgnored(rules, 'examples/ui-packs/aurora/README.md'), true);
  assert.equal(isIgnored(rules, 'examples/ui-packs/aurora/web/README.md'), false, '* is one name');
  assert.equal(isIgnored(rules, 'docs/a.md'), true);
  assert.equal(isIgnored(rules, 'docs/keep.md'), false, '! re-includes, the last match wins');
  assert.equal(isIgnored(rules, 'server/index.js'), false);
});

test('a fixture repository is copied as docker build would', () => {
  const repo = tmp();
  writeTree(repo, {
    Dockerfile: 'FROM debian\nWORKDIR /app\nCOPY version.json ./\nCOPY server ./server\nCOPY examples/ui-packs ./ui-packs\n',
    '.dockerignore': '**/.DS_Store\nexamples/ui-packs/*.zip\nexamples/ui-packs/*/README.md\n',
    'version.json': '{}',
    'server/index.js': 'x',
    'server/lib/a.js': 'a',
    'server/.DS_Store': 'junk',
    'examples/ui-packs/p.zip': 'zip',
    'examples/ui-packs/p/README.md': 'readme',
    'examples/ui-packs/p/ui.json': '{}',
    'notcopied.txt': 'n',
  });
  assert.deepEqual(appFiles(repo), [
    { from: 'version.json', to: 'version.json' },
    { from: 'server/index.js', to: 'server/index.js' },
    { from: 'server/lib/a.js', to: 'server/lib/a.js' },
    { from: 'examples/ui-packs/p/ui.json', to: 'ui-packs/p/ui.json' },
  ]);
  const dest = path.join(repo, 'out');
  assert.equal(copyApp(repo, dest), 4);
  assert.equal(fs.readFileSync(path.join(dest, 'ui-packs/p/ui.json'), 'utf8'), '{}');
  fs.rmSync(path.join(repo, 'server'), { recursive: true });
  assert.throws(() => appFiles(repo), /copies server, which does not exist/);
});

test('the real Dockerfile gives the app the image has', () => {
  const files = appFiles(REPO);
  const to = new Set(files.map((f) => f.to));
  for (const f of ['version.json', 'CRITICAL_UPDATE.txt', 'server/index.js', 'server/supervisor.js', 'web/app.js', 'src/main/converter/encoderPlanner.js']) assert.ok(to.has(f), f);
  assert.ok(files.some((f) => f.from.startsWith('examples/ui-packs/') && f.to.startsWith('ui-packs/')), 'the packs');
  assert.ok(files.some((f) => f.from.startsWith('assets/')), 'the assets');
  for (const f of files) {
    assert.doesNotMatch(f.to, /^(test|docs|macos|windows|native|\.git)\//, f.to);
    assert.doesNotMatch(f.to, /\.zip$|^ui-packs\/[^/]+\/README\.md$|\.DS_Store$/, f.to);
  }
  // no file twice
  assert.equal(to.size, files.length);
});

test('BUILD_INFO.json: channel macos, Apple Silicon, no image or tag', () => {
  const info = buildInfo({ repo: REPO, sha: 'A'.repeat(40), branch: 'main', builtAt: '2026-10-30T12:00:00.123Z', release: 'skittles-fivem/Sentry-Manager', pins });
  const version = JSON.parse(fs.readFileSync(path.join(REPO, 'version.json'), 'utf8')).version;
  assert.deepEqual(Object.keys(info), ['sha', 'branch', 'builtAt', 'channel', 'release', 'version', 'platform', 'node', 'ffmpeg', 'critical']);
  assert.equal(info.sha, 'a'.repeat(40));
  assert.equal(info.builtAt, '2026-10-30T12:00:00Z');
  assert.equal(info.channel, 'macos');
  assert.equal(info.platform, 'darwin-arm64');
  assert.equal(info.version, version);
  assert.equal(info.node, pins.node.version);
  assert.equal(info.ffmpeg, pins.ffmpeg.version);
  assert.ok(!('image' in info) && !('tag' in info), 'so imageUpdates stays idle');
  const { criticalFromFile } = require('../server/imageUpdates');
  assert.deepEqual(info.critical, criticalFromFile(fs.readFileSync(path.join(REPO, 'CRITICAL_UPDATE.txt'), 'utf8')));
  assert.deepEqual(releaseJson(info), { sha: info.sha, builtAt: info.builtAt, version: info.version, critical: info.critical });
  assert.equal(buildInfo({ repo: REPO, sha: 'b'.repeat(40), branch: 'HEAD', release: 'o/r', pins }).branch, '', 'a detached build has no branch');
  assert.throws(() => buildInfo({ repo: REPO, sha: 'abc', release: 'o/r', pins }), /no commit id/);
  assert.throws(() => buildInfo({ repo: REPO, sha: 'a'.repeat(40), release: 'not a repo', pins }), /no repository/);
});

test('the pins: Apple Silicon only, HTTPS, a SHA-256 each', () => {
  assert.equal(pins.arch, 'arm64');
  assert.deepEqual(Object.keys(pins).sort(), ['arch', 'ffmpeg', 'node']);
  for (const name of ['node', 'ffmpeg']) {
    const p = pins[name];
    assert.match(p.url, /^https:\/\//, name);
    assert.match(p.sha256, /^[0-9a-f]{64}$/, `${name}: verified, not a placeholder`);
    assert.ok(p.url.includes(p.version), `${name} url names its version`);
    for (const member of Object.values(p.files)) assert.doesNotMatch(member, /^\/|\.\./);
  }
  assert.equal(pins.node.url, `https://nodejs.org/dist/v${pins.node.version}/node-v${pins.node.version}-darwin-arm64.tar.xz`);
  assert.equal(pins.ffmpeg.url, `https://github.com/jellyfin/jellyfin-ffmpeg/releases/download/v${pins.ffmpeg.version}/jellyfin-ffmpeg_${pins.ffmpeg.version}_portable_macarm64-gpl.tar.xz`);
  assert.match(pins.node.version, /^24\./, 'the Node line the app is tested with');
  // the converter's encoders on a Mac; there is no Apple AV1 encoder
  assert.deepEqual(pins.ffmpeg.requireEncoders, ['hevc_videotoolbox', 'h264_videotoolbox', 'libx265', 'libsvtav1']);
  assert.deepEqual(pins.ffmpeg.requireDecoders, ['libdav1d']);
  // what build.sh takes out of the archives
  assert.deepEqual(Object.keys(pins.node.files), ['bin/node']);
  assert.deepEqual(Object.keys(pins.ffmpeg.files), ['ffmpeg', 'ffprobe']);
});

test('build.sh builds the tarball install.sh expects', () => {
  const build = fs.readFileSync(path.join(MACOS, 'build.sh'), 'utf8');
  const install = fs.readFileSync(path.join(MACOS, 'install.sh'), 'utf8');
  assert.match(build, /TARBALL="SentrySixManager-\$VERSION-\$SHORT-macos-arm64\.tar\.gz"/);
  assert.match(install, /SentrySixManager-\[A-Za-z0-9._\+-\]\+-macos-arm64/);
  // every file install.sh requires is put in by build.sh
  const required = /for f in (node\/bin\/node[\s\S]*?); do/.exec(install)[1].replace(/\\\n/g, ' ').split(/\s+/).filter(Boolean);
  const staged = { 'node/bin/node': /take node "\$NODE_TAR" bin\/node "\$STAGE\/node\/bin\/node"/, 'ffmpeg/ffmpeg': /"\$STAGE\/ffmpeg\/ffmpeg"/, 'ffmpeg/ffprobe': /"\$STAGE\/ffmpeg\/ffprobe"/, 'bin/sentry-fsops': /-o "\$STAGE\/bin\/sentry-fsops"/, 'bin/sentry-six-manager': /"\$STAGE\/bin\/sentry-six-manager"/, 'app/server/index.js': /appFiles\.js" copy/, 'app/BUILD_INFO.json': /buildInfo\.js"/ };
  for (const f of required) {
    if (staged[f]) assert.match(build, staged[f], f);
    else {
      const name = f.replace('"$LABEL.plist"', 'io.github.skittles-fivem.sentry-six-manager.plist');
      assert.ok(build.includes(name), f);
      assert.ok(fs.existsSync(path.join(MACOS, name)), name);
    }
  }
  assert.match(build, /-arch arm64 -mmacosx-version-min=13\.5/);
  assert.match(build, /lipo -archs/);
});
