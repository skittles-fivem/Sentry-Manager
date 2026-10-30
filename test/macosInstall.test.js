'use strict';

// The macOS install, update and uninstall scripts and the CLI, run on Linux
// against a sandbox root (SENTRY_ROOT) with stand-ins for the macOS commands
// (test/fixtures/fakeMacCommand.js) first on PATH.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const fake = require('./fixtures/fakeMacCommand');
const { notOnWindows } = require('./fixtures/platformSkip');
const { footageTmpSync } = require('./fixtures/tmp');

const REPO = path.join(__dirname, '..');
const MACOS = path.join(REPO, 'macos');
const LABEL = 'io.github.skittles-fivem.sentry-six-manager';
const SCRIPTS = ['install.sh', 'update.sh', 'uninstall.sh', 'build.sh', 'sentry-six-manager'];
const hasTar = spawnSync('tar', ['--version']).status === 0;
// The macOS scripts are run by /bin/bash, with the macOS commands stood in
// for by Node scripts that run by their path through #! (fakeMacCommand.js).
// Windows has neither, and nothing of macOS's install to test; the checks
// that only read the scripts run there too.
const runsBash = notOnWindows('the macOS scripts run under /bin/bash, with #! Node stand-ins for the macOS commands');

// a folder under the real temp folder (macOS's /var/folders is a symlink)
const made = [];
function tmp() {
  const d = fs.mkdtempSync(path.join(fs.realpathSync(os.tmpdir()), 'ssm-macos-'));
  made.push(d);
  return d;
}
test.after(() => {
  for (const d of made) fs.rmSync(d, { recursive: true, force: true });
});

const sha256 = (file) => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');

// A sandbox: fake commands, their state, a root, packages.
function sandbox() {
  const base = tmp();
  const sb = {
    base,
    bin: path.join(base, 'bin'),
    fake: path.join(base, 'fake'),
    root: path.join(base, 'root'),
    pkgs: path.join(base, 'pkgs'),
    urls: {},
    env: {},
  };
  for (const d of [sb.bin, sb.fake, sb.root, sb.pkgs]) fs.mkdirSync(d, { recursive: true });
  const dispatcher = path.join(__dirname, 'fixtures', 'fakeMacCommand.js');
  for (const name of fake.commands) {
    fs.writeFileSync(path.join(sb.bin, name), `#!${process.execPath}\nrequire(${JSON.stringify(dispatcher)})(${JSON.stringify(name)});\n`, { mode: 0o755 });
  }
  // a Mac with a few system accounts; 200 is a user's and 201 a group's
  fs.writeFileSync(path.join(sb.fake, 'dscl.json'), JSON.stringify({
    Users: { root: { UniqueID: '0' }, daemon: { UniqueID: '1' }, _www: { UniqueID: '70' }, _other: { UniqueID: '200' } },
    Groups: { wheel: { PrimaryGroupID: '0' }, staff: { PrimaryGroupID: '20' }, admin: { PrimaryGroupID: '80' }, _othergroup: { PrimaryGroupID: '201' } },
  }));
  // left disabled by an earlier `launchctl disable`: bootstrap fails until enabled
  fs.writeFileSync(path.join(sb.fake, 'launchd.json'), JSON.stringify({ loaded: false, running: false, disabled: true }));
  sb.prog = path.join(sb.root, 'Library/SentrySixManager');
  sb.state = path.join(sb.root, 'Library/Application Support/SentrySixManager');
  return sb;
}

// A package as build.sh lays it out, with stand-in binaries.
function makePackage(sb, { version = '2026.1.0', health = 'ok', vt = 0, name } = {}) {
  const stage = path.join(sb.base, `stage-${version}`);
  const w = (rel, text, mode = 0o644) => {
    fs.mkdirSync(path.dirname(path.join(stage, rel)), { recursive: true });
    fs.writeFileSync(path.join(stage, rel), text, { mode });
  };
  w('node/bin/node', `#!/bin/sh\nexec ${JSON.stringify(process.execPath)} "$@"\n`, 0o755);
  w('ffmpeg/ffmpeg', `#!/bin/sh\necho "$@" > ${JSON.stringify(path.join(sb.fake, 'ffmpeg-args'))}\nexit ${vt}\n`, 0o755);
  w('ffmpeg/ffprobe', '#!/bin/sh\nexit 0\n', 0o755);
  w('bin/sentry-fsops', '#!/bin/sh\nexit 0\n', 0o755);
  w('app/server/index.js', '// the app\n');
  w('app/BUILD_INFO.json', `${JSON.stringify({ sha: 'a'.repeat(40), channel: 'macos', version, platform: 'darwin-arm64' }, null, 2)}\n`);
  w('app/HEALTH', `${health}\n`);
  for (const f of ['install.sh', 'update.sh', 'uninstall.sh']) w(f, fs.readFileSync(path.join(MACOS, f)), 0o755);
  w('bin/sentry-six-manager', fs.readFileSync(path.join(MACOS, 'sentry-six-manager')), 0o755);
  for (const f of [`${LABEL}.plist`, 'newsyslog.conf', 'manager.env.example']) w(f, fs.readFileSync(path.join(MACOS, f)));
  const dir = path.join(sb.pkgs, version);
  fs.mkdirSync(dir, { recursive: true });
  const tarName = name || `SentrySixManager-${version}-aaaaaaa-macos-arm64.tar.gz`;
  const tarball = path.join(dir, tarName);
  const r = spawnSync('tar', ['-czf', tarball, '-C', stage, '.']);
  assert.equal(r.status, 0, String(r.stderr));
  fs.writeFileSync(path.join(dir, 'release.json'), '{}\n');
  fs.writeFileSync(path.join(dir, 'SHA256SUMS'), `${sha256(tarball)}  ${tarName}\n${sha256(path.join(dir, 'release.json'))}  release.json\n`);
  return { tarball, sums: path.join(dir, 'SHA256SUMS'), dir, tarName };
}

function run(sb, script, args = [], env = {}) {
  const r = spawnSync('/bin/bash', [script, ...args], {
    encoding: 'utf8',
    timeout: 60000,
    env: {
      PATH: `${sb.bin}:${process.env.PATH}`,
      HOME: sb.base,
      TMPDIR: sb.base,
      FAKE_DIR: sb.fake,
      SENTRY_ROOT: sb.root,
      SENTRY_WAIT_SECONDS: '2',
      // no service answer by default: the Terminal-side test runs at once
      SENTRY_VT_WAIT_SECONDS: '0',
      FAKE_URLS: JSON.stringify(sb.urls),
      ...sb.env,
      ...env,
    },
  });
  return { status: r.status, stdout: r.stdout, stderr: r.stderr, out: `${r.stdout}${r.stderr}` };
}

const install = (sb, pkg, env, extra = []) => run(sb, path.join(MACOS, 'install.sh'), ['--from', pkg.tarball, ...extra], env);

function calls(sb) {
  const f = path.join(sb.fake, 'calls.log');
  if (!fs.existsSync(f)) return [];
  return fs.readFileSync(f, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
}
const line = (c) => `${c.cmd} ${c.args.join(' ')}`;
const indexOf = (list, re) => list.findIndex((c) => re.test(line(c)));
const clearCalls = (sb) => fs.rmSync(path.join(sb.fake, 'calls.log'), { force: true });
const dscl = (sb) => JSON.parse(fs.readFileSync(path.join(sb.fake, 'dscl.json'), 'utf8'));
const launchd = (sb) => JSON.parse(fs.readFileSync(path.join(sb.fake, 'launchd.json'), 'utf8'));
const mode = (p) => (fs.statSync(p).mode & 0o7777).toString(8).padStart(4, '0');
const version = (sb) => JSON.parse(fs.readFileSync(path.join(sb.prog, 'app/BUILD_INFO.json'), 'utf8')).version;

test('every script parses with bash', { skip: runsBash }, () => {
  for (const f of SCRIPTS) {
    const r = spawnSync('/bin/bash', ['-n', path.join(MACOS, f)], { encoding: 'utf8' });
    assert.equal(r.status, 0, `${f}: ${r.stderr}`);
  }
});

test('the scripts stay within macOS\'s bash 3.2', () => {
  // bash 4 and later only: associative arrays, mapfile, case changes,
  // |& and &>> redirections, ;& fall-through, coproc, globstar
  const bash4 = [/declare -A|local -A/, /\bmapfile\b|\breadarray\b/, /\$\{[A-Za-z_]+(,,|\^\^)/, /\|&/, /&>>/, /;;&|;&\s*$/m, /\bcoproc\b/, /shopt -s globstar/, /\$EPOCHSECONDS|\$EPOCHREALTIME/, /\$\{[A-Za-z_]+@[QEPAa]\}/];
  for (const f of SCRIPTS) {
    const text = fs.readFileSync(path.join(MACOS, f), 'utf8');
    assert.match(text, /^#!\/bin\/bash\n/, `${f} runs with macOS's /bin/bash`);
    assert.match(text, /^set -euo pipefail$/m, `${f} stops at the first error`);
    for (const re of bash4) assert.doesNotMatch(text, re, `${f} uses ${re}`);
    // GNU-only tools and flags
    assert.doesNotMatch(text, /\bstat -c\b|\bsed -i\b|\breadlink -f\b|\btimeout \d|\bgrep -P\b/, `${f} uses a GNU-only tool`);
  }
});

test('nothing in macos/ is for Intel Macs', () => {
  // the Mac app's folder too (not its build output)
  const files = (d) => fs.readdirSync(d, { withFileTypes: true }).flatMap((e) => {
    if (e.isDirectory()) return ['.build', '.swiftpm', 'dist'].includes(e.name) ? [] : files(path.join(d, e.name));
    return [path.join(d, e.name)];
  });
  for (const f of files(MACOS)) {
    const text = fs.readFileSync(f, 'utf8');
    assert.doesNotMatch(text, /darwin-x64|mac64-|x86_64|macos-x64|\bx64\b/i, path.relative(MACOS, f));
  }
});

test('an Intel Mac is refused before anything is changed', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  for (const arm of ['missing', '0']) {
    const sb = sandbox();
    const pkg = makePackage(sb);
    const r = install(sb, pkg, { FAKE_ARM64: arm });
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /Intel processor/);
    assert.match(r.stderr, /Apple Silicon/);
    assert.equal(indexOf(calls(sb), /^(dscl|launchctl|shasum|codesign|curl) /), -1, 'nothing ran after the check');
    assert.deepEqual(fs.readdirSync(sb.root), []);
  }
});

test('an installer running under Rosetta is refused before anything is changed', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const r = install(sb, makePackage(sb), { FAKE_ROSETTA: '1' });
  assert.notEqual(r.status, 0);
  assert.match(r.stderr, /running under Rosetta/);
  assert.match(r.stderr, /arch -arm64/);
  assert.equal(indexOf(calls(sb), /^(dscl|launchctl|shasum|codesign|curl) /), -1, 'nothing ran after the check');
  assert.deepEqual(fs.readdirSync(sb.root), []);
});

test('an Intel Mac with an old macOS gets the Intel message', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const r = install(sb, makePackage(sb), { FAKE_ARM64: 'missing', FAKE_MACOS: '12.7.6' });
  assert.notEqual(r.status, 0);
  assert.match(r.stderr, /Intel processor/);
});

test('macOS older than 13.5 is refused, 13.5 is not', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  for (const v of ['13.4.1', '13', '12.7.6', '10.15']) {
    const sb = sandbox();
    const r = install(sb, makePackage(sb), { FAKE_MACOS: v });
    assert.notEqual(r.status, 0, v);
    assert.match(r.stderr, new RegExp(`needs macOS 13\\.5 or newer; this Mac has ${v.replace(/\./g, '\\.')}`));
    assert.deepEqual(fs.readdirSync(sb.root), []);
  }
  const sb = sandbox();
  const r = install(sb, makePackage(sb), { FAKE_MACOS: '13.5' });
  assert.equal(r.status, 0, r.out);
});

test('it has to run as root', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const r = install(sb, makePackage(sb), { FAKE_UID: '501' });
  assert.notEqual(r.status, 0);
  assert.match(r.stderr, /run it as root/);
  assert.deepEqual(fs.readdirSync(sb.root), []);
});

test('a hash mismatch stops before anything is unpacked or changed', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const pkg = makePackage(sb);
  fs.writeFileSync(pkg.sums, `${'0'.repeat(64)}  ${pkg.tarName}\n`);
  const r = install(sb, pkg);
  assert.notEqual(r.status, 0);
  assert.match(r.stderr, /does not match its SHA-256.*Nothing was changed/);
  const c = calls(sb);
  assert.ok(indexOf(c, /^shasum -a 256 -c /) >= 0, 'checked');
  assert.equal(indexOf(c, /^(xattr|codesign|dscl|launchctl|chown) /), -1, 'nothing after the check');
  assert.deepEqual(fs.readdirSync(sb.root), [], 'not even the .new folder');

  // no line for it, or no SHA256SUMS at all
  fs.writeFileSync(pkg.sums, `${sha256(pkg.tarball)}  something-else.tar.gz\n`);
  assert.match(install(sb, pkg).stderr, /no single line/);
  fs.rmSync(pkg.sums);
  assert.match(install(sb, pkg).stderr, /no SHA256SUMS next to/);
  assert.deepEqual(fs.readdirSync(sb.root), []);
});

test('a package whose signature does not verify is refused', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const r = install(sb, makePackage(sb), { FAKE_CODESIGN_FAIL: 'sentry-fsops' });
  assert.notEqual(r.status, 0);
  assert.match(r.stderr, /sentry-fsops's code signature does not verify/);
  assert.equal(fs.existsSync(sb.prog), false);
  assert.equal(fs.existsSync(`${sb.prog}.new`), false, 'the refused package is removed');
  assert.equal(indexOf(calls(sb), /^(dscl|launchctl) /), -1);
});

test('a first install: account, folders, settings, job, health, self-test, welcome', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const pkg = makePackage(sb);
  const r = install(sb, pkg, { FAKE_SETUP_CODE: 'ABCD-1234' });
  assert.equal(r.status, 0, r.out);
  const c = calls(sb);

  // the account: 200 is a user's and 201 a group's, so 202 for both
  const db = dscl(sb);
  assert.deepEqual(db.Users._sentrysix, {
    UniqueID: '202', PrimaryGroupID: '202', UserShell: '/usr/bin/false', NFSHomeDirectory: '/var/empty',
    RealName: 'Sentry Six Manager', Password: '*', IsHidden: '1',
  });
  assert.deepEqual(db.Groups._sentrysix, { PrimaryGroupID: '202', RealName: 'Sentry Six Manager', Password: '*' });
  assert.equal(fs.readFileSync(path.join(sb.state, '.uid'), 'utf8'), '202\n');

  // the folders and their modes
  assert.equal(mode(sb.state), '0750');
  for (const d of ['data', 'run', 'run/home', 'run/tmp']) assert.equal(mode(path.join(sb.state, d)), '0700', d);
  assert.equal(mode(path.join(sb.state, 'footage')), '0755');
  assert.ok(fs.existsSync(path.join(sb.state, 'data/.metadata_never_index')));
  assert.equal(mode(path.join(sb.state, 'manager.env')), '0640');
  assert.equal(fs.readFileSync(path.join(sb.state, 'manager.env'), 'utf8'), fs.readFileSync(path.join(MACOS, 'manager.env.example'), 'utf8'));
  const owners = c.filter((x) => x.cmd === 'chown').map((x) => x.args.join(' '));
  for (const want of [
    `root:202 ${sb.state}`, `202:202 ${sb.state}/data`, `202:202 ${sb.state}/run/home`, `202:202 ${sb.state}/footage`,
    `root:202 ${sb.state}/manager.env`, `202:admin ${sb.root}/Library/Logs/SentrySixManager/service.log`,
    `-R root:wheel ${sb.prog}.new`, `root:wheel ${sb.root}/Library/LaunchDaemons/${LABEL}.plist`,
  ]) assert.ok(owners.includes(want), `chown ${want}`);
  assert.equal(mode(path.join(sb.root, 'Library/Logs/SentrySixManager/service.log')), '0640');

  // the program, the job, the PATH entry instead of a /usr/local/bin link
  assert.equal(version(sb), '2026.1.0');
  assert.equal(fs.existsSync(`${sb.prog}.new`), false);
  assert.equal(fs.existsSync(`${sb.prog}.old`), false, 'nothing to keep on a first install');
  const plist = path.join(sb.root, 'Library/LaunchDaemons', `${LABEL}.plist`);
  assert.equal(fs.readFileSync(plist, 'utf8'), fs.readFileSync(path.join(MACOS, `${LABEL}.plist`), 'utf8'));
  assert.equal(mode(plist), '0644');
  assert.equal(fs.readFileSync(path.join(sb.root, 'etc/paths.d/sentry-six-manager'), 'utf8'), '/Library/SentrySixManager/bin\n');
  assert.equal(fs.readFileSync(path.join(sb.root, 'etc/newsyslog.d/sentry-six-manager.conf'), 'utf8'), fs.readFileSync(path.join(MACOS, 'newsyslog.conf'), 'utf8'));
  assert.equal(fs.existsSync(path.join(sb.root, 'usr/local/bin')), false);

  // the order: hash, then quarantine, then signatures, then the account, then the job
  const hash = indexOf(c, /^shasum -a 256 -c one\.sha256$/);
  const quarantine = indexOf(c, /^xattr -dr com\.apple\.quarantine .*\.tar\.gz$/);
  const enable = indexOf(c, new RegExp(`^launchctl enable system/${LABEL}$`));
  const bootstrap = indexOf(c, new RegExp(`^launchctl bootstrap system ${plist.replace(/[.]/g, '\\.')}$`));
  assert.ok(hash >= 0 && quarantine > hash, 'the quarantine flag goes only after the hash check');
  assert.ok(indexOf(c, /^codesign /) > quarantine);
  assert.ok(indexOf(c, /^dscl \. -create /) > indexOf(c, /^codesign /));
  assert.ok(enable > 0 && bootstrap > enable, 'enable comes before bootstrap');
  assert.deepEqual(c.filter((x) => x.cmd === 'codesign').map((x) => x.args.join(' ')), [
    `--verify ${sb.prog}.new/node/bin/node`, `--verify ${sb.prog}.new/bin/sentry-fsops`,
  ], 'only node and our helper; ffmpeg keeps Jellyfin\'s own signature');
  assert.deepEqual(launchd(sb), { loaded: true, running: true, disabled: false });

  // the VideoToolbox self-test, as the service account
  const vt = c.find((x) => x.cmd === 'sudo');
  assert.deepEqual(vt.args.slice(0, 3), ['-u', '_sentrysix', 'env']);
  assert.equal(vt.args[5], `${sb.prog}/ffmpeg/ffmpeg`);
  assert.equal(fs.readFileSync(path.join(sb.fake, 'ffmpeg-args'), 'utf8').trim(),
    '-hide_banner -nostdin -v error -f lavfi -i testsrc2=size=1280x720:rate=30 -t 1 -c:v hevc_videotoolbox -q:v 55 -allow_sw 0 -f null -');
  assert.match(r.stdout, /GPU: VideoToolbox HEVC works for the service\./);

  // what docker logs shows on Docker
  assert.match(r.stdout, /Open https:\/\/Test-Mac\.local:9000\n/);
  assert.match(r.stdout, /or https:\/\/192\.168\.1\.20:9000\n/);
  assert.match(r.stdout, /Setup code: ABCD-1234\n/);
});

test('the welcome shows the port from manager.env and the certificate fingerprint', { skip: runsBash || ((!hasTar || spawnSync('openssl', ['version']).status !== 0) && 'needs tar and openssl') }, () => {
  const sb = sandbox();
  fs.mkdirSync(path.join(sb.state, 'data/tls'), { recursive: true });
  fs.writeFileSync(path.join(sb.state, 'manager.env'), 'PORT=9443\n');
  const crt = path.join(sb.state, 'data/tls/server.crt');
  const g = spawnSync('openssl', ['req', '-x509', '-newkey', 'ec', '-pkeyopt', 'ec_paramgen_curve:P-256', '-nodes', '-keyout', path.join(sb.base, 'k.pem'), '-out', crt, '-subj', '/CN=test', '-days', '1']);
  assert.equal(g.status, 0, String(g.stderr));
  const r = install(sb, makePackage(sb));
  assert.equal(r.status, 0, r.out);
  assert.match(r.stdout, /Open https:\/\/Test-Mac\.local:9443\n/);
  const fp = new crypto.X509Certificate(fs.readFileSync(crt)).fingerprint256;
  assert.ok(r.stdout.includes(`Certificate fingerprint (SHA-256), as browsers show it: ${fp}\n`), r.stdout);
  assert.match(r.stdout, /No setup code: the account is already set up\./);

  // behind a reverse proxy (TLS_MODE=off) the address is http
  fs.writeFileSync(path.join(sb.state, 'manager.env'), 'PORT=9443\nTLS_MODE="Off"\n');
  const off = install(sb, makePackage(sb, { version: '2.0.0' }));
  assert.equal(off.status, 0, off.out);
  assert.match(off.stdout, /Open http:\/\/Test-Mac\.local:9443\n/);
  assert.match(off.stdout, /or http:\/\/192\.168\.1\.20:9443\n/);
});

test('a failing self-test is reported, not fatal', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const r = install(sb, makePackage(sb, { vt: 1 }));
  assert.equal(r.status, 0, r.out);
  assert.match(r.stdout, /VideoToolbox HEVC did not work for the service/);
});

test('the VideoToolbox answer comes from the running service when it gives one', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  for (const [answer, says] of [['failed', /VideoToolbox HEVC did not work for the service/], ['ok', /GPU: VideoToolbox HEVC works for the service\./]]) {
    const sb = sandbox();
    // the Terminal-side test would pass: only the service's answer counts
    const r = install(sb, makePackage(sb), { FAKE_VT_DAEMON: answer, SENTRY_VT_WAIT_SECONDS: '5' });
    assert.equal(r.status, 0, r.out);
    assert.match(r.stdout, says);
    assert.equal(calls(sb).some((x) => x.cmd === 'sudo'), false, 'no test from the Terminal session');
  }
});

test('installing again keeps the account, the settings and the data, and keeps the old version', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  assert.equal(install(sb, makePackage(sb, { version: '1.0.0' })).status, 0);
  fs.writeFileSync(path.join(sb.state, 'manager.env'), 'PORT=9100\nUI_PASSWORD=secret\n');
  fs.writeFileSync(path.join(sb.state, 'data/db.json'), '{"kept":true}');
  clearCalls(sb);
  const r = install(sb, makePackage(sb, { version: '2.0.0' }));
  assert.equal(r.status, 0, r.out);
  const c = calls(sb);
  assert.equal(fs.readFileSync(path.join(sb.state, 'manager.env'), 'utf8'), 'PORT=9100\nUI_PASSWORD=secret\n', 'manager.env is written once');
  assert.equal(mode(path.join(sb.state, 'manager.env')), '0640');
  assert.equal(fs.readFileSync(path.join(sb.state, 'data/db.json'), 'utf8'), '{"kept":true}');
  assert.equal(indexOf(c, /^dscl \. -create /), -1, 'the account is reused');
  assert.equal(dscl(sb).Users._sentrysix.UniqueID, '202');
  assert.equal(version(sb), '2.0.0');
  assert.equal(JSON.parse(fs.readFileSync(`${sb.prog}.old/app/BUILD_INFO.json`, 'utf8')).version, '1.0.0');
  // a clean stop of the old one, then enable before bootstrap
  const kill = indexOf(c, /^launchctl kill TERM /);
  const bootout = indexOf(c, /^launchctl bootout /);
  const enable = indexOf(c, /^launchctl enable /);
  const bootstrap = indexOf(c, /^launchctl bootstrap /);
  assert.ok(kill >= 0 && bootout > kill && enable > bootout && bootstrap > enable, c.filter((x) => x.cmd === 'launchctl').map(line).join('\n'));
  assert.ok(indexOf(c, /^shasum /) < kill, 'the old version runs until the new one is checked');
  assert.match(r.stdout, /:9100\n/);
});

test('a recorded id is reused when the account is made again', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  fs.mkdirSync(sb.state, { recursive: true });
  fs.writeFileSync(path.join(sb.state, '.uid'), '300\n');
  assert.equal(install(sb, makePackage(sb)).status, 0);
  assert.equal(dscl(sb).Users._sentrysix.UniqueID, '300');
  assert.equal(dscl(sb).Groups._sentrysix.PrimaryGroupID, '300');

  // the account removed by hand, then installed again: the same id
  const db = dscl(sb);
  delete db.Users._sentrysix;
  delete db.Groups._sentrysix;
  fs.writeFileSync(path.join(sb.fake, 'dscl.json'), JSON.stringify(db));
  clearCalls(sb);
  assert.equal(install(sb, makePackage(sb, { version: '2.0.0' })).status, 0);
  assert.equal(dscl(sb).Users._sentrysix.UniqueID, '300');
  assert.equal(indexOf(calls(sb), /^chown -R /), indexOf(calls(sb), /^chown -R root:wheel /), 'no data handed over');
});

test('a recorded id someone else took: a free one, and the data handed to it', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  assert.equal(install(sb, makePackage(sb)).status, 0);
  const db = dscl(sb);
  delete db.Users._sentrysix;
  delete db.Groups._sentrysix;
  db.Users.newcomer = { UniqueID: '202' };
  fs.writeFileSync(path.join(sb.fake, 'dscl.json'), JSON.stringify(db));
  clearCalls(sb);
  const r = install(sb, makePackage(sb, { version: '2.0.0' }));
  assert.equal(r.status, 0, r.out);
  assert.equal(dscl(sb).Users._sentrysix.UniqueID, '203');
  assert.match(r.stdout, /id changed \(202 to 203\)/);
  const handed = calls(sb).find((x) => x.cmd === 'chown' && x.args[0] === '-R' && x.args[1] === '203:203');
  assert.deepEqual(handed.args.slice(2), [`${sb.state}/data`, `${sb.state}/run`], 'never into footage/, where shares are mounted');
  assert.equal(fs.readFileSync(path.join(sb.state, '.uid'), 'utf8'), '203\n');
});

test('a new version that does not answer is rolled back', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  assert.equal(install(sb, makePackage(sb, { version: '1.0.0' })).status, 0);
  clearCalls(sb);
  const r = install(sb, makePackage(sb, { version: '2.0.0', health: 'fail' }));
  assert.notEqual(r.status, 0);
  assert.match(r.stdout, /did not answer within 2 s: going back/);
  assert.match(r.stdout, /The version before runs again\./);
  assert.match(r.stderr, /the update was not installed/);
  assert.equal(version(sb), '1.0.0');
  for (const s of ['.old', '.new', '.failed']) assert.equal(fs.existsSync(sb.prog + s), false, s);
  assert.deepEqual(launchd(sb), { loaded: true, running: true, disabled: false });
  assert.equal(calls(sb).filter((x) => /^launchctl bootstrap /.test(line(x))).length, 2, 'the new one, then the old one again');
});

test('update.sh installs over a running version, and refuses when nothing is installed', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const first = run(sb, path.join(MACOS, 'update.sh'), ['--from', makePackage(sb, { version: '1.0.0' }).tarball]);
  assert.notEqual(first.status, 0);
  assert.match(first.stderr, /not installed here/);
  assert.equal(fs.existsSync(sb.prog), false);
  assert.equal(install(sb, makePackage(sb, { version: '1.0.0' })).status, 0);
  const r = run(sb, path.join(sb.prog, 'update.sh'), ['--from', makePackage(sb, { version: '2.0.0' }).tarball]);
  assert.equal(r.status, 0, r.out);
  assert.equal(version(sb), '2.0.0');
});

test('without --from it takes the newest macos-v release from GitHub', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const pkg = makePackage(sb, { version: '3.0.0' });
  const base = 'https://github.com/skittles-fivem/Sentry-Manager/releases/download';
  const newest = `${base}/macos-v3.0.0-aaaaaaa/${pkg.tarName}`;
  const releases = path.join(sb.base, 'releases.json');
  const pre = `${base}/macos-v4.0.0-ddddddd/SentrySixManager-4.0.0-ddddddd-macos-arm64.tar.gz`;
  // as GitHub lists them: tag_name and prerelease before the assets
  fs.writeFileSync(releases, JSON.stringify([
    { tag_name: 'macos-v4.0.0-ddddddd', prerelease: true, assets: [{ browser_download_url: pre }] },
    { tag_name: 'v3.0.0', prerelease: false, assets: [{ browser_download_url: `${base}/v3.0.0/SentrySixManager-3.0.0-aaaaaaa-win-x64.zip` }] },
    { tag_name: 'macos-v3.0.0-aaaaaaa', prerelease: false, assets: [{ browser_download_url: `${base}/macos-v3.0.0-aaaaaaa/SHA256SUMS` }, { browser_download_url: newest }] },
    { tag_name: 'macos-v2.0.0-bbbbbbb', prerelease: false, assets: [{ browser_download_url: `${base}/macos-v2.0.0-bbbbbbb/SentrySixManager-2.0.0-bbbbbbb-macos-arm64.tar.gz` }] },
  ], null, 2));
  sb.urls = {
    'https://api.github.com/repos/skittles-fivem/Sentry-Manager/releases?per_page=100&page=1': releases,
    [newest]: pkg.tarball,
    [`${base}/macos-v3.0.0-aaaaaaa/SHA256SUMS`]: pkg.sums,
  };
  const r = run(sb, path.join(MACOS, 'install.sh'));
  assert.equal(r.status, 0, r.out);
  assert.equal(version(sb), '3.0.0', 'the newest final release, not the newer pre-release');
  assert.equal(indexOf(calls(sb), new RegExp(`^curl .*${pre.replace(/[.]/g, '\\.')}`)), -1, 'the pre-release is not downloaded');
  for (const x of calls(sb).filter((y) => y.cmd === 'curl' && !y.args.some((a) => a.includes('/healthz')))) {
    assert.ok(x.args.includes('--proto') && x.args[x.args.indexOf('--proto') + 1] === '=https', line(x));
    assert.ok(x.args.includes('--proto-redir') && x.args[x.args.indexOf('--proto-redir') + 1] === '=https', line(x));
  }
  // GitHub's own output has no line breaks to rely on
  fs.writeFileSync(releases, JSON.stringify(JSON.parse(fs.readFileSync(releases, 'utf8'))));
  clearCalls(sb);
  assert.equal(run(sb, path.join(MACOS, 'install.sh')).status, 0);
  assert.equal(indexOf(calls(sb), new RegExp(`^curl .*${newest.replace(/[.]/g, '\\.')}$`)) >= 0, true);

  // a pre-release only when it is named
  const named = run(sb, path.join(MACOS, 'install.sh'), ['--release', 'macos-v4.0.0-ddddddd']);
  assert.notEqual(named.status, 0);
  assert.ok(indexOf(calls(sb), new RegExp(`^curl .*${pre.replace(/[.]/g, '\\.')}$`)) >= 0, 'asked for by name');

  // a tag that is not there
  const missing = run(sb, path.join(MACOS, 'install.sh'), ['--release', 'macos-v9.9.9-ccccccc']);
  assert.notEqual(missing.status, 0);
  assert.match(missing.stderr, /found no macOS release in skittles-fivem\/Sentry-Manager called macos-v9\.9\.9-ccccccc/);
});

test('a macOS release behind a full page of Windows releases is still found', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const pkg = makePackage(sb, { version: '3.0.0' });
  const base = 'https://github.com/skittles-fivem/Sentry-Manager/releases/download';
  const mac = `${base}/macos-v3.0.0-aaaaaaa/${pkg.tarName}`;
  const page1 = path.join(sb.base, 'page1.json');
  const page2 = path.join(sb.base, 'page2.json');
  fs.writeFileSync(page1, JSON.stringify(Array.from({ length: 100 }, (_, i) => (
    { tag_name: `v3.${i}.0`, prerelease: false, assets: [{ browser_download_url: `${base}/v3.${i}.0/SentrySixManager-3.${i}.0-aaaaaaa-win-x64.zip` }] }))));
  fs.writeFileSync(page2, JSON.stringify([
    { tag_name: 'macos-v3.0.0-aaaaaaa', prerelease: false, assets: [{ browser_download_url: `${base}/macos-v3.0.0-aaaaaaa/SHA256SUMS` }, { browser_download_url: mac }] }]));
  const api = 'https://api.github.com/repos/skittles-fivem/Sentry-Manager/releases?per_page=100&page=';
  sb.urls = { [`${api}1`]: page1, [`${api}2`]: page2, [mac]: pkg.tarball, [`${base}/macos-v3.0.0-aaaaaaa/SHA256SUMS`]: pkg.sums };
  const r = run(sb, path.join(MACOS, 'install.sh'));
  assert.equal(r.status, 0, r.out);
  assert.equal(version(sb), '3.0.0');
  // a page that is not full is the last one asked for
  fs.writeFileSync(page1, '[]');
  clearCalls(sb);
  const none = run(sb, path.join(MACOS, 'install.sh'));
  assert.notEqual(none.status, 0);
  assert.match(none.stderr, /found no macOS release in skittles-fivem\/Sentry-Manager/);
  assert.equal(indexOf(calls(sb), /page=2/), -1);
});

test('a package with paths outside its folder is refused', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const pkg = makePackage(sb);
  const evil = path.join(sb.base, 'evil');
  fs.mkdirSync(path.join(evil, 'x'), { recursive: true });
  fs.writeFileSync(path.join(sb.base, 'escape'), 'x');
  const tarball = path.join(pkg.dir, pkg.tarName);
  assert.equal(spawnSync('tar', ['-czPf', tarball, '-C', path.join(evil, 'x'), '../../escape']).status, 0);
  fs.writeFileSync(pkg.sums, `${sha256(tarball)}  ${pkg.tarName}\n`);
  const r = install(sb, pkg);
  assert.notEqual(r.status, 0);
  assert.match(r.stderr, /paths outside its folder/);
  assert.equal(fs.existsSync(`${sb.prog}.new/escape`), false);
});

test('uninstall keeps settings, data and the account; --remove-data takes them too', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  assert.equal(install(sb, makePackage(sb)).status, 0);
  fs.writeFileSync(path.join(sb.state, 'data/db.json'), '{}');
  clearCalls(sb);
  const r = run(sb, path.join(sb.prog, 'uninstall.sh'));
  assert.equal(r.status, 0, r.out);
  assert.equal(fs.existsSync(sb.prog), false);
  for (const f of [`Library/LaunchDaemons/${LABEL}.plist`, 'etc/newsyslog.d/sentry-six-manager.conf', 'etc/paths.d/sentry-six-manager']) {
    assert.equal(fs.existsSync(path.join(sb.root, f)), false, f);
  }
  assert.ok(fs.existsSync(path.join(sb.state, 'data/db.json')));
  assert.ok(fs.existsSync(path.join(sb.state, 'manager.env')));
  assert.ok(dscl(sb).Users._sentrysix, 'the account stays');
  assert.equal(launchd(sb).loaded, false);
  const c = calls(sb);
  assert.ok(indexOf(c, /^launchctl kill TERM /) < indexOf(c, /^launchctl bootout /));
  assert.match(r.stdout, /Kept the settings and data/);

  // and installing again picks it all up
  assert.equal(install(sb, makePackage(sb, { version: '2.0.0' })).status, 0);
  assert.ok(fs.existsSync(path.join(sb.state, 'data/db.json')));

  const all = run(sb, path.join(sb.prog, 'uninstall.sh'), ['--remove-data']);
  assert.equal(all.status, 0, all.out);
  assert.equal(fs.existsSync(sb.state), false);
  assert.equal(fs.existsSync(path.join(sb.root, 'Library/Logs/SentrySixManager')), false);
  assert.equal(dscl(sb).Users._sentrysix, undefined);
  assert.equal(dscl(sb).Groups._sentrysix, undefined);
});

test('the CLI: status, stop, start, setup-code, allow-folder', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  assert.equal(install(sb, makePackage(sb, { version: '4.5.6' }), { FAKE_SETUP_CODE: 'WXYZ-9876' }).status, 0);
  const cli = path.join(sb.prog, 'bin/sentry-six-manager');
  const status = run(sb, cli, ['status'], { FAKE_UID: '501' });
  assert.equal(status.status, 0, status.out);
  assert.match(status.stdout, /Sentry Six Manager 4\.5\.6 \(aaaaaaa\)/);
  assert.match(status.stdout, /Service: running as _sentrysix \(pid 4242\)/);
  assert.match(status.stdout, /Answers on port 9000: https:\/\/Test-Mac\.local:9000/);

  assert.match(run(sb, cli, ['stop'], { FAKE_UID: '501' }).stderr, /run it with sudo: sudo sentry-six-manager stop/);
  clearCalls(sb);
  assert.equal(run(sb, cli, ['restart']).status, 0);
  const c = calls(sb).filter((x) => x.cmd === 'launchctl').map(line);
  assert.ok(c.indexOf(`launchctl kill TERM system/${LABEL}`) < c.indexOf(`launchctl kickstart system/${LABEL}`), c.join('\n'));
  assert.ok(!c.some((l) => /kickstart -k/.test(l)));
  assert.equal(run(sb, cli, ['stop']).status, 0);
  assert.equal(launchd(sb).running, false);
  const stopped = run(sb, cli, ['status'], { FAKE_UID: '501' });
  assert.equal(stopped.status, 1);
  assert.match(stopped.stdout, /Service: stopped \(last exit code 0\)/);
  assert.equal(run(sb, cli, ['start']).status, 0);
  assert.equal(launchd(sb).running, true);

  assert.match(run(sb, cli, ['setup-code']).stdout, /Setup code: WXYZ-9876/);

  // (on a Mac the temp folder is under /private, a system folder the CLI
  // rightly refuses: there the drive stand-in goes in /Users/Shared)
  const footageBase = process.platform === 'darwin' ? footageTmpSync('ssm-macos-') : sb.base;
  if (footageBase !== sb.base) made.push(footageBase);
  const footage = path.join(footageBase, 'Volumes', 'TESLADRIVE');
  fs.mkdirSync(path.join(footage, 'TeslaCam'), { recursive: true });
  clearCalls(sb);
  const allow = run(sb, cli, ['allow-folder', footage, '--write']);
  assert.equal(allow.status, 0, allow.out);
  const acl = calls(sb).find((x) => x.cmd === 'chmod');
  assert.deepEqual(acl.args, ['-R', '+a', 'user:_sentrysix allow list,search,readattr,readextattr,readsecurity,add_file,add_subdirectory,delete_child,write,append,writeattr,writeextattr,delete,file_inherit,directory_inherit', footage],
    'recursive: the folder\'s own entry reaches only files made later');
  clearCalls(sb);
  assert.equal(run(sb, cli, ['allow-folder', footage]).status, 0);
  assert.equal(calls(sb).find((x) => x.cmd === 'chmod').args[2], 'user:_sentrysix allow list,search,readattr,readextattr,readsecurity,file_inherit,directory_inherit');
  // (on a Mac also the temp folder, /private/var/folders/...)
  for (const bad of ['/', '/System', '/Library', '/etc', '/private/var', '/Applications', '/Volumes', '/SYSTEM/Library', ...(process.platform === 'darwin' ? [sb.base] : [])]) {
    if (!fs.existsSync(bad)) continue;
    const r = run(sb, cli, ['allow-folder', bad]);
    assert.notEqual(r.status, 0, bad);
    assert.match(r.stderr, /is a system folder/);
  }
  // on a Mac: never a whole home folder or its Library
  const home = fs.existsSync('/Users') ? fs.readdirSync('/Users').find((n) => n !== 'Shared' && !n.startsWith('.') && fs.existsSync(`/Users/${n}/Library`)) : null;
  if (home) {
    assert.match(run(sb, cli, ['allow-folder', `/Users/${home}`]).stderr, /whole home folder/);
    assert.match(run(sb, cli, ['allow-folder', `/Users/${home}/Library`]).stderr, /in someone's Library/);
  }

  // with TLS_MODE=off the address is http
  fs.writeFileSync(path.join(sb.state, 'manager.env'), 'TLS_MODE=off\nPORT=9100\n');
  assert.equal(launchd(sb).running, true);
  assert.match(run(sb, cli, ['status'], { FAKE_UID: '501' }).stdout, /Answers on port 9100: http:\/\/Test-Mac\.local:9100/);
});

// ---- the Mac app's admin step (docs/plans/macos-app.md 3.4)

// The app's engine as its admin step stages it: the package laid out as a folder.
function stagedDir(sb, opts) {
  const pkg = makePackage(sb, opts);
  const dir = path.join(sb.base, `staged-${opts?.version || 'x'}`);
  fs.mkdirSync(dir, { recursive: true });
  const r = spawnSync('tar', ['-xzf', pkg.tarball, '-C', dir]);
  assert.equal(r.status, 0, String(r.stderr));
  return dir;
}
const fromDir = (sb, dir, extra = [], env = {}) => run(sb, path.join(MACOS, 'install.sh'), ['--from-dir', dir, '--service-mode', 'daemon', ...extra], env);

test('--from-dir: a staged folder installs as a tarball does, with the public and local folders and a SETUP-CODE line', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const users = dscl(sb);
  users.Users.anna = { UniqueID: '501' };
  fs.writeFileSync(path.join(sb.fake, 'dscl.json'), JSON.stringify(users));
  const dir = stagedDir(sb, { version: '3.0.0' });
  // the user's copy may carry a set-id bit or an access list: neither stays
  fs.chmodSync(path.join(dir, 'bin', 'sentry-fsops'), 0o6755);
  const r = fromDir(sb, dir, ['--local-user', 'anna'], { FAKE_SETUP_CODE: 'ABCDE-FGHIJ-KLMNO' });
  assert.equal(r.status, 0, r.out);
  assert.equal(version(sb), '3.0.0');
  assert.equal(fs.statSync(path.join(sb.prog, 'bin', 'sentry-fsops')).mode & 0o7777, 0o755);
  assert.ok(calls(sb).some((c) => c.cmd === 'chmod' && c.args.join(' ') === `-R -N ${sb.prog}.new`), 'access lists cleared');
  assert.match(r.stdout, /Copied the app's engine from /);
  assert.match(r.stdout, /^SETUP-CODE: ABCDE-FGHIJ-KLMNO$/m);
  assert.match(r.stdout, /^ {2}Setup code: ABCDE-FGHIJ-KLMNO$/m);
  // no tarball, no download
  assert.ok(!calls(sb).some((c) => c.cmd === 'curl' && !/healthz/.test(line(c))));
  const pub = path.join(sb.root, 'Library/Application Support/SentrySixManager Public');
  const local = path.join(sb.root, 'Library/Application Support/SentrySixManager Local');
  assert.equal(mode(pub), '0755');
  assert.equal(mode(local), '0700');
  const acl = calls(sb).filter((c) => c.cmd === 'chmod' && c.args[0] === '+a');
  assert.deepEqual(acl.map((c) => c.args), [['+a', 'user:anna allow search', local]]);
  assert.equal(fs.readFileSync(path.join(sb.state, 'local-signin-users'), 'utf8'), 'anna\n');
  // the plist the app's engine brings has both paths
  const plist = fs.readFileSync(path.join(sb.root, `Library/LaunchDaemons/${LABEL}.plist`), 'utf8');
  assert.match(plist, /SentrySixManager Public\/engine\.json/);
  assert.match(plist, /SentrySixManager Local\/signin\.sock/);

  // again (Update the service): the same user is not added twice
  clearCalls(sb);
  const again = fromDir(sb, stagedDir(sb, { version: '3.1.0' }), ['--local-user', 'anna', '--update']);
  assert.equal(again.status, 0, again.out);
  assert.equal(version(sb), '3.1.0');
  assert.equal(calls(sb).filter((c) => c.cmd === 'chmod' && c.args[0] === '+a').length, 0);
  assert.equal(fs.readFileSync(path.join(sb.state, 'local-signin-users'), 'utf8'), 'anna\n');
});

test('the Terminal install: no SETUP-CODE line; the sudo user is allowed when a real user, nobody otherwise', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const users = dscl(sb);
  users.Users.bob = { UniqueID: '502' };
  fs.writeFileSync(path.join(sb.fake, 'dscl.json'), JSON.stringify(users));
  const r = install(sb, makePackage(sb), { FAKE_SETUP_CODE: 'ABCDE-FGHIJ-KLMNO', SUDO_USER: 'bob' });
  assert.equal(r.status, 0, r.out);
  assert.doesNotMatch(r.stdout, /SETUP-CODE:/);
  assert.equal(fs.readFileSync(path.join(sb.state, 'local-signin-users'), 'utf8'), 'bob\n');
  // a system account (uid under 501) or root: nobody
  const sb2 = sandbox();
  assert.equal(install(sb2, makePackage(sb2), { SUDO_USER: '_www' }).status, 0);
  assert.equal(fs.readFileSync(path.join(sb2.state, 'local-signin-users'), 'utf8'), '');
  assert.equal(calls(sb2).filter((c) => c.cmd === 'chmod' && c.args[0] === '+a').length, 0);
});

test('--from-dir refuses a folder with links or missing files, and options that do not go together', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const dir = stagedDir(sb, { version: '3.0.0' });
  fs.symlinkSync('/etc/passwd', path.join(dir, 'app', 'link'));
  const r = fromDir(sb, dir);
  assert.notEqual(r.status, 0);
  assert.match(r.stderr, /has links in it: refusing it/);
  assert.equal(fs.existsSync(sb.prog), false);
  fs.rmSync(path.join(dir, 'app', 'link'));
  fs.rmSync(path.join(dir, 'bin', 'sentry-fsops'));
  const r2 = fromDir(sb, dir);
  assert.match(r2.stderr, /the package has no bin\/sentry-fsops: refusing it/);
  assert.equal(fs.existsSync(sb.prog), false);
  assert.match(fromDir(sb, dir, ['--from', 'x.tar.gz']).stderr, /--from-dir cannot go with --from or --release/);
  assert.match(run(sb, path.join(MACOS, 'install.sh'), ['--from-dir', dir, '--service-mode', 'agent']).stderr, /--service-mode takes daemon/);
  assert.match(fromDir(sb, path.join(sb.base, 'nope')).stderr, /is not a folder/);
  const users = dscl(sb);
  fs.writeFileSync(path.join(sb.fake, 'dscl.json'), JSON.stringify(users));
  assert.match(fromDir(sb, stagedDir(sb, { version: '3.0.1' }), ['--local-user', 'nobody-here']).stderr, /nobody-here is not a user on this Mac/);
  assert.match(fromDir(sb, stagedDir(sb, { version: '3.0.2' }), ['--local-user', "a'b"]).stderr, /is not a user on this Mac/);
});

test('--import-data: copied into an empty data folder and handed to the service; an existing one is kept', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const mine = path.join(sb.base, 'home-data');
  fs.mkdirSync(path.join(mine, 'tls'), { recursive: true });
  fs.writeFileSync(path.join(mine, 'manager.db'), 'mine');
  fs.writeFileSync(path.join(mine, 'tls', 'ca.crt'), 'ca');
  fs.symlinkSync('/etc/passwd', path.join(mine, 'stray-link'));
  const r = fromDir(sb, stagedDir(sb, { version: '3.0.0' }), ['--import-data', mine]);
  assert.equal(r.status, 0, r.out);
  assert.match(r.stdout, /Copied the data from /);
  assert.equal(fs.readFileSync(path.join(sb.state, 'data', 'manager.db'), 'utf8'), 'mine');
  assert.equal(fs.readFileSync(path.join(sb.state, 'data', 'tls', 'ca.crt'), 'utf8'), 'ca');
  assert.equal(fs.existsSync(path.join(sb.state, 'data', 'stray-link')), false);
  assert.equal(mode(path.join(sb.state, 'data', 'manager.db')).slice(2), '00');
  // copied while the service was stopped, before the new version started
  const c = calls(sb);
  assert.ok(indexOf(c, /^ditto /) < indexOf(c, /^launchctl bootstrap /));
  // the user's own copy stays
  assert.ok(fs.existsSync(path.join(mine, 'manager.db')));

  // the service has data now: never overwritten
  fs.writeFileSync(path.join(mine, 'manager.db'), 'newer');
  const again = fromDir(sb, stagedDir(sb, { version: '3.0.1' }), ['--import-data', mine]);
  assert.equal(again.status, 0, again.out);
  assert.match(again.stdout, /already has its own data .*: kept it/);
  assert.equal(fs.readFileSync(path.join(sb.state, 'data', 'manager.db'), 'utf8'), 'mine');
});

test('TRUSTED_HOSTS without 127.0.0.1 gets it added once; a list with it, or none, is left alone', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  fs.mkdirSync(sb.state, { recursive: true });
  fs.writeFileSync(path.join(sb.state, 'manager.env'), 'TRUSTED_HOSTS=nas.local, mac.lan\n');
  assert.equal(install(sb, makePackage(sb)).status, 0);
  const env = fs.readFileSync(path.join(sb.state, 'manager.env'), 'utf8');
  assert.match(env, /\nTRUSTED_HOSTS=nas\.local,mac\.lan,127\.0\.0\.1\n$/);
  assert.equal(install(sb, makePackage(sb, { version: '2.0.0' })).status, 0);
  assert.equal(fs.readFileSync(path.join(sb.state, 'manager.env'), 'utf8'), env);
  const sb2 = sandbox();
  assert.equal(install(sb2, makePackage(sb2)).status, 0);
  assert.doesNotMatch(fs.readFileSync(path.join(sb2.state, 'manager.env'), 'utf8'), /^TRUSTED_HOSTS=/m);
});

test('the CLI: local-signin list, allow and remove; uninstall removes the app\'s folders', { skip: runsBash || (!hasTar && 'needs tar') }, () => {
  const sb = sandbox();
  const users = dscl(sb);
  users.Users.anna = { UniqueID: '501' };
  fs.writeFileSync(path.join(sb.fake, 'dscl.json'), JSON.stringify(users));
  assert.equal(install(sb, makePackage(sb)).status, 0);
  const cli = path.join(sb.prog, 'bin/sentry-six-manager');
  const local = path.join(sb.root, 'Library/Application Support/SentrySixManager Local');
  assert.match(run(sb, cli, ['local-signin', 'list'], { FAKE_UID: '501' }).stderr, /run it with sudo/);
  assert.match(run(sb, cli, ['local-signin', 'list']).stdout, /^Nobody: every Mac user signs in with the password\.$/m);
  clearCalls(sb);
  const allow = run(sb, cli, ['local-signin', 'allow', 'anna']);
  assert.equal(allow.status, 0, allow.out);
  assert.deepEqual(calls(sb).filter((c) => c.cmd === 'chmod').map((c) => c.args), [['+a', 'user:anna allow search', local]]);
  assert.equal(run(sb, cli, ['local-signin', 'list']).stdout, 'anna\n');
  assert.match(run(sb, cli, ['local-signin', 'allow', 'anna']).stdout, /already/);
  assert.match(run(sb, cli, ['local-signin', 'allow', '_www']).stderr, /not a person's account/);
  assert.match(run(sb, cli, ['local-signin', 'allow', 'ghost']).stderr, /not a user on this Mac/);
  assert.match(run(sb, cli, ['local-signin', 'allow', '../x']).stderr, /not a Mac user name/);
  clearCalls(sb);
  assert.equal(run(sb, cli, ['local-signin', 'remove', 'anna']).status, 0);
  assert.deepEqual(calls(sb).filter((c) => c.cmd === 'chmod' && c.args[0] === '-a').map((c) => c.args), [['-a', 'user:anna allow search', local]]);
  assert.match(run(sb, cli, ['local-signin', 'list']).stdout, /^Nobody/m);
  assert.match(run(sb, cli, ['local-signin', 'what']).stderr, /usage: sudo sentry-six-manager local-signin list/);
  assert.equal(run(sb, cli, ['local-signin', 'allow', 'anna']).status, 0);
  const r = run(sb, path.join(sb.prog, 'uninstall.sh'));
  assert.equal(r.status, 0, r.out);
  assert.equal(fs.existsSync(local), false);
  assert.equal(fs.existsSync(path.join(sb.root, 'Library/Application Support/SentrySixManager Public')), false);
  // the list went with the folder's access entries: installed again, anna is let in again
  assert.equal(fs.existsSync(path.join(sb.state, 'local-signin-users')), false);
  clearCalls(sb);
  assert.equal(install(sb, makePackage(sb, { version: '2.0.0' }), { SUDO_USER: 'anna' }).status, 0);
  assert.deepEqual(calls(sb).filter((c) => c.cmd === 'chmod' && c.args[0] === '+a').map((c) => c.args), [['+a', 'user:anna allow search', local]]);
  assert.equal(fs.readFileSync(path.join(sb.state, 'local-signin-users'), 'utf8'), 'anna\n');
});
