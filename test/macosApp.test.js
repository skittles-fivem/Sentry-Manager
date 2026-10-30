'use strict';

// The native Mac app's packaging, checked on Linux (docs/plans/macos-app.md,
// sections 3 and 10): the scripts parse with bash 3.2, build-app.sh runs
// end to end against stand-ins for Xcode's tools (signed and unsigned), the
// bundle's plists and entitlements have the right keys, the launcher's
// environment matches the daemon plist, and no WebKit is anywhere.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const fake = require('./fixtures/fakeAppBuildCommand');
const { buildInfo } = require('../macos/buildInfo');

const REPO = path.join(__dirname, '..');
const APP = path.join(REPO, 'macos', 'app');
const SCRIPTS = path.join(APP, 'scripts');
const BUNDLE = path.join(APP, 'bundle');
const SCRIPT_NAMES = ['build-app.sh', 'sign.sh', 'notarize.sh', 'dmg.sh', 'check-no-webkit.sh'];
const LABEL = 'io.github.skittles-fivem.sentry-six-manager';
const SECRETS = { APPLE_ID: 'dev@example.com', APPLE_TEAM_ID: 'TEAM123456', APPLE_APP_PASSWORD: 'abcd-efgh-ijkl-mnop', DEVELOPER_ID_P12: Buffer.from('p12 bytes').toString('base64'), DEVELOPER_ID_P12_PASSWORD: 'p12-secret-pass' };

const made = [];
function tmp() {
  const d = fs.mkdtempSync(path.join(fs.realpathSync(os.tmpdir()), 'ssm-app-'));
  made.push(d);
  return d;
}
test.after(() => {
  for (const d of made) fs.rmSync(d, { recursive: true, force: true });
});
const sha256 = (buf) => crypto.createHash('sha256').update(buf).digest('hex');

// ---- a small XML property list reader (dict, array, string, integer, true, false)

function parsePlist(xml) {
  const tokens = xml.replace(/<\?xml[^>]*\?>/, '').replace(/<!DOCTYPE[^>]*>/, '').replace(/<!--[\s\S]*?-->/g, '')
    .match(/<[^>]+>|[^<]+/g).filter((t) => t.trim());
  let i = 0;
  const text = (tag) => {
    let s = '';
    while (tokens[i] !== `</${tag}>`) {
      if (tokens[i] === undefined || tokens[i].startsWith('<')) throw new Error(`bad <${tag}>`);
      s += tokens[i++];
    }
    i++;
    return s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');
  };
  function value() {
    const t = tokens[i++];
    if (t === '<true/>') return true;
    if (t === '<false/>') return false;
    if (t === '<string>') return text('string');
    if (t === '<integer>') return Number(text('integer'));
    if (t === '<dict/>') return {};
    if (t === '<array>') { const a = []; while (tokens[i] !== '</array>') a.push(value()); i++; return a; }
    if (t === '<dict>') {
      const d = {};
      while (tokens[i] !== '</dict>') {
        if (tokens[i++] !== '<key>') throw new Error('expected <key>');
        const k = text('key');
        if (k in d) throw new Error(`key ${k} twice`);
        d[k] = value();
      }
      i++;
      return d;
    }
    throw new Error(`not understood: ${t}`);
  }
  if (tokens[i++] !== '<plist version="1.0">') throw new Error('no <plist version="1.0">');
  const v = value();
  if (tokens[i++] !== '</plist>' || i !== tokens.length) throw new Error('trailing content');
  return v;
}
const plist = (f) => parsePlist(fs.readFileSync(f, 'utf8'));

// ---- the scripts

test('every app script parses with bash and stays within bash 3.2', () => {
  const bash4 = [/declare -A|local -A/, /\bmapfile\b|\breadarray\b/, /\$\{[A-Za-z_]+(,,|\^\^)/, /\|&/, /&>>/, /\bcoproc\b/, /shopt -s globstar/, /\$\{[A-Za-z_]+@[QEPAa]\}/];
  for (const f of SCRIPT_NAMES) {
    const file = path.join(SCRIPTS, f);
    const r = spawnSync('/bin/bash', ['-n', file], { encoding: 'utf8' });
    assert.equal(r.status, 0, `${f}: ${r.stderr}`);
    const text = fs.readFileSync(file, 'utf8');
    for (const rx of bash4) assert.doesNotMatch(text, rx, `${f}: ${rx}`);
    assert.match(text, /^set -euo pipefail$/m, f);
    // a secret must never reach the log
    assert.doesNotMatch(text, /set -x|set -o xtrace/, f);
  }
});

// A copy of macos/app (build-app.sh writes the manifest file into its sources)
// and an engine folder laid out as macos/build.sh's package, with stand-ins.
function sandbox({ channel = 'macos-app' } = {}) {
  const base = tmp();
  const sb = { base, bin: path.join(base, 'bin'), fake: path.join(base, 'fake'), repo: path.join(base, 'repo'), engine: path.join(base, 'engine'), out: path.join(base, 'out') };
  for (const d of [sb.bin, sb.fake]) fs.mkdirSync(d, { recursive: true });
  const dispatcher = path.join(__dirname, 'fixtures', 'fakeAppBuildCommand.js');
  for (const name of fake.commands) {
    fs.writeFileSync(path.join(sb.bin, name), `#!${process.execPath}\nrequire(${JSON.stringify(dispatcher)})(${JSON.stringify(name)});\n`, { mode: 0o755 });
  }
  fs.mkdirSync(path.join(sb.repo, 'macos'), { recursive: true });
  fs.cpSync(APP, path.join(sb.repo, 'macos', 'app'), { recursive: true, filter: (src) => !/\/\.build(\/|$)|\/dist(\/|$)/.test(src) });
  const w = (rel, text, mode = 0o644) => {
    fs.mkdirSync(path.dirname(path.join(sb.engine, rel)), { recursive: true });
    fs.writeFileSync(path.join(sb.engine, rel), text, { mode });
  };
  w('node/bin/node', `#!/bin/sh\nexec ${JSON.stringify(process.execPath)} "$@"\n`, 0o755);
  w('ffmpeg/ffmpeg', '#!/bin/sh\nexit 0\n', 0o755);
  w('ffmpeg/ffprobe', '#!/bin/sh\nexit 0\n', 0o755);
  w('bin/sentry-fsops', '#!/bin/sh\nexit 0\n', 0o755);
  w('bin/sentry-six-manager', fs.readFileSync(path.join(REPO, 'macos', 'sentry-six-manager')), 0o755);
  w('app/server/index.js', '// the server\n');
  w('app/web/index.html', '<!doctype html>\n');
  w('app/BUILD_INFO.json', JSON.stringify({ sha: 'c'.repeat(40), channel, version: '2026.2.0', platform: 'darwin-arm64' }));
  for (const f of ['install.sh', 'update.sh', 'uninstall.sh']) w(f, fs.readFileSync(path.join(REPO, 'macos', f)), 0o755);
  w(`${LABEL}.plist`, fs.readFileSync(path.join(REPO, 'macos', `${LABEL}.plist`)));
  return sb;
}

function build(sb, args = [], env = {}) {
  const r = spawnSync('/bin/bash', [path.join(sb.repo, 'macos', 'app', 'scripts', 'build-app.sh'), '--engine-dir', sb.engine, '--out', sb.out, '--work', path.join(sb.base, 'work'), ...args], {
    encoding: 'utf8',
    timeout: 120000,
    env: { PATH: `${sb.bin}:${process.env.PATH}`, HOME: sb.base, TMPDIR: sb.base, FAKE_DIR: sb.fake, ...env },
  });
  return { status: r.status, out: `${r.stdout}${r.stderr}`, stdout: r.stdout };
}

function calls(sb) {
  const f = path.join(sb.fake, 'calls.log');
  return fs.existsSync(f) ? fs.readFileSync(f, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l)) : [];
}

const appPath = (sb) => path.join(sb.base, 'work', 'Sentry Six Manager.app');

test('build-app.sh without secrets: ad hoc, signed inside out, the manifest after signing, an -unsigned dmg', () => {
  const sb = sandbox();
  const r = build(sb);
  assert.equal(r.status, 0, r.out);
  assert.match(r.out, /No signing secrets: signing ad hoc/);
  const app = appPath(sb);
  // the bundle's layout
  for (const f of ['Contents/Info.plist', 'Contents/MacOS/Sentry Six Manager', 'Contents/MacOS/sentry-six-engine',
    `Contents/Library/LaunchAgents/${LABEL}.agent.plist`, 'Contents/Resources/engine/MANIFEST.sha256',
    'Contents/Resources/engine/node/bin/node', 'Contents/Resources/engine/install.sh']) {
    assert.ok(fs.existsSync(path.join(app, f)), f);
  }
  const info = plist(path.join(app, 'Contents/Info.plist'));
  assert.equal(info.CFBundleShortVersionString, '2026.2.0');
  assert.equal(info.CFBundleVersion, '2026.2.0');

  // signing, inside out, all ad hoc; node with its entitlements only
  const signs = calls(sb).filter((c) => c.cmd === 'codesign' && !c.args.includes('--verify') && !c.args.includes('-d'));
  const order = signs.map((c) => path.relative(app, c.args[c.args.length - 1]).replace(/^(\.\.\/)+/, ''));
  const engineOf = (p) => p.replace(/^.*?engine\//, 'engine/');
  assert.deepEqual(order.map(engineOf), ['engine/ffmpeg/ffmpeg', 'engine/ffmpeg/ffprobe', 'engine/bin/sentry-fsops', 'engine/node/bin/node', 'Contents/MacOS/sentry-six-engine', '']);
  for (const c of signs) {
    assert.ok(c.args.includes('-'), c.args.join(' '));
    assert.ok(!c.args.includes('--deep'), 'never --deep');
  }
  assert.match(signs[3].args.join(' '), /--entitlements .*node\.entitlements/);
  assert.match(signs[5].args.join(' '), /--entitlements .*SentrySixManager\.entitlements/);
  assert.ok(!signs.slice(0, 3).some((c) => c.args.includes('--entitlements')));

  // the manifest lists every engine file as it ships (signed), and its hash is compiled in
  const engine = path.join(app, 'Contents/Resources/engine');
  const manifest = fs.readFileSync(path.join(engine, 'MANIFEST.sha256'), 'utf8');
  const lines = manifest.trim().split('\n');
  const listed = lines.map((l) => l.slice(66));
  assert.deepEqual(listed, [...listed].sort());
  assert.ok(listed.includes('./node/bin/node') && listed.includes('./app/server/index.js') && !listed.includes('./MANIFEST.sha256'));
  for (const l of lines) {
    const [hash, rel] = [l.slice(0, 64), l.slice(66)];
    assert.equal(hash, sha256(fs.readFileSync(path.join(engine, rel))), rel);
  }
  assert.match(fs.readFileSync(path.join(engine, 'node/bin/node'), 'utf8'), /# signed by -/);
  const gen = fs.readFileSync(path.join(sb.repo, 'macos/app/Sources/SentrySixManager/Platform/EngineManifest.generated.swift'), 'utf8');
  assert.match(gen, new RegExp(`^let generatedEngineManifestSHA256 = "${sha256(manifest)}"$`, 'm'));
  assert.match(gen, /^#if ENGINE_MANIFEST$/m);
  const swift = calls(sb).filter((c) => c.cmd === 'swift' && !c.args.includes('--show-bin-path')).map((c) => c.args.join(' '));
  assert.deepEqual(swift, [
    'build -c release --arch arm64 --product SentrySixManager -Xswiftc -DENGINE_MANIFEST',
    'build -c release --arch arm64 --product sentry-six-engine',
  ]);
  // compiled after the manifest was written, signed after it was assembled
  const all = calls(sb);
  assert.ok(all.findIndex((c) => c.cmd === 'swift') > all.findIndex((c) => c.cmd === 'codesign' && /node\/bin\/node$/.test(c.args[c.args.length - 1])));

  // the dmg: unsigned name, the app and an Applications link, its SHA-256 line
  const dmg = 'SentrySixManager-2026.2.0-ccccccc-macos-arm64-unsigned.dmg';
  assert.deepEqual(fs.readdirSync(sb.out).sort(), [dmg, 'SHA256SUMS.dmg'].sort());
  assert.match(fs.readFileSync(path.join(sb.out, dmg), 'utf8'), /^Applications -> \/Applications$/m);
  assert.match(fs.readFileSync(path.join(sb.out, dmg), 'utf8'), /^Sentry Six Manager\.app$/m);
  assert.equal(fs.readFileSync(path.join(sb.out, 'SHA256SUMS.dmg'), 'utf8'), `${sha256(fs.readFileSync(path.join(sb.out, dmg)))}  ${dmg}\n`);
  const create = all.find((c) => c.cmd === 'hdiutil' && c.args[0] === 'create').args.join(' ');
  assert.match(create, /-volname Sentry Six Manager -srcfolder .* -format UDZO -fs APFS/);
  // nothing for Apple without secrets
  assert.ok(!all.some((c) => c.cmd === 'xcrun' || c.cmd === 'security'));
});

test('build-app.sh with the five secrets: Developer ID with the runtime and a timestamp, two notarizations, never a secret printed', () => {
  const sb = sandbox();
  const r = build(sb, [], SECRETS);
  assert.equal(r.status, 0, r.out);
  const id = `Developer ID Application: Test Person (${SECRETS.APPLE_TEAM_ID})`;
  const all = calls(sb);
  const signs = all.filter((c) => c.cmd === 'codesign' && !c.args.includes('--verify') && !c.args.includes('-d'));
  for (const c of signs) {
    assert.ok(c.args.includes(id), c.args.join(' '));
    assert.ok(c.args.includes('--timestamp'));
    // the dmg itself is signed without the runtime option
    if (!c.args[c.args.length - 1].endsWith('.dmg')) assert.deepEqual(c.args.slice(c.args.indexOf('--options'), c.args.indexOf('--options') + 2), ['--options', 'runtime']);
  }
  assert.ok(signs.some((c) => c.args[c.args.length - 1].endsWith('-macos-arm64.dmg')), 'the dmg is signed');
  const submits = all.filter((c) => c.cmd === 'xcrun' && c.args[1] === 'submit');
  assert.equal(submits.length, 2);
  assert.match(submits[0].args[2], /Sentry Six Manager\.zip$/);
  assert.match(submits[1].args[2], /SentrySixManager-2026\.2\.0-ccccccc-macos-arm64\.dmg$/);
  for (const s of submits) {
    for (const k of ['--wait', '--timeout', '60m']) assert.ok(s.args.includes(k), k);
  }
  const staples = all.filter((c) => c.cmd === 'xcrun' && c.args[0] === 'stapler').map((c) => `${c.args[1]} ${path.basename(c.args[2])}`);
  assert.deepEqual(staples, ['staple Sentry Six Manager.app', 'validate Sentry Six Manager.app', 'staple SentrySixManager-2026.2.0-ccccccc-macos-arm64.dmg', 'validate SentrySixManager-2026.2.0-ccccccc-macos-arm64.dmg']);
  // the keychain: made, used, removed
  const sec = all.filter((c) => c.cmd === 'security').map((c) => c.args[0]);
  assert.deepEqual(sec.slice(0, 2), ['delete-keychain', 'create-keychain']);
  assert.equal(sec[sec.length - 1], 'delete-keychain');
  assert.ok(sec.includes('set-key-partition-list'));
  // the p12 file is gone, and no secret is in the output
  assert.deepEqual(fs.readdirSync(sb.base).filter((n) => n.startsWith('devid.')), []);
  for (const v of [SECRETS.APPLE_APP_PASSWORD, SECRETS.DEVELOPER_ID_P12_PASSWORD, SECRETS.DEVELOPER_ID_P12]) assert.ok(!r.out.includes(v));
  assert.deepEqual(fs.readdirSync(sb.out).sort(), ['SHA256SUMS.dmg', 'SentrySixManager-2026.2.0-ccccccc-macos-arm64.dmg']);
});

test('build-app.sh: one secret missing means ad hoc; a refused notarization fails the build with Apple\'s log', () => {
  const sb = sandbox();
  const r = build(sb, [], { ...SECRETS, APPLE_APP_PASSWORD: '' });
  assert.equal(r.status, 0, r.out);
  assert.ok(fs.existsSync(path.join(sb.out, 'SentrySixManager-2026.2.0-ccccccc-macos-arm64-unsigned.dmg')));
  assert.ok(!calls(sb).some((c) => c.cmd === 'xcrun'));
  const sb2 = sandbox();
  const bad = build(sb2, [], { ...SECRETS, FAKE_NOTARY_STATUS: 'Invalid' });
  assert.notEqual(bad.status, 0);
  assert.match(bad.out, /notarization of Sentry Six Manager\.app ended as 'Invalid'/);
  assert.ok(calls(sb2).some((c) => c.cmd === 'xcrun' && c.args[1] === 'log'));
  assert.equal(calls(sb2).filter((c) => c.cmd === 'security').pop().args[0], 'delete-keychain', 'the keychain goes on a failure too');
});

test('build-app.sh refuses an engine built for another channel, and a web file outside the engine', () => {
  const sb = sandbox({ channel: 'macos' });
  const r = build(sb);
  assert.notEqual(r.status, 0);
  assert.match(r.out, /BUILD_INFO\.json says channel macos, not macos-app/);
  const sb2 = sandbox();
  // the engine's own web files are fine (the server); the checker is run on a bundle with one outside
  const ok = build(sb2, ['--skip-dmg']);
  assert.equal(ok.status, 0, ok.out);
  const bundle = path.join(sb2.out, 'Sentry Six Manager.app');
  fs.writeFileSync(path.join(bundle, 'Contents/Resources/page.html'), '<p>no</p>');
  const chk = spawnSync('/bin/bash', [path.join(SCRIPTS, 'check-no-webkit.sh'), '--app', bundle], { encoding: 'utf8', env: { PATH: `${sb2.bin}:${process.env.PATH}`, FAKE_DIR: sb2.fake } });
  assert.notEqual(chk.status, 0);
  assert.match(chk.stderr, /web files in the bundle outside the engine/);
  fs.rmSync(path.join(bundle, 'Contents/Resources/page.html'));
  fs.writeFileSync(path.join(bundle, 'Contents/MacOS/Sentry Six Manager.links-webkit'), '');
  const linked = spawnSync('/bin/bash', [path.join(SCRIPTS, 'check-no-webkit.sh'), '--app', bundle], { encoding: 'utf8', env: { PATH: `${sb2.bin}:${process.env.PATH}`, FAKE_DIR: sb2.fake } });
  assert.notEqual(linked.status, 0);
  assert.match(linked.stderr, /links \/System\/Library\/Frameworks\/WebKit\.framework/);
});

// ---- the bundle's plists and entitlements

test('Info.plist: the app id, Apple Silicon only, macOS 14, the local network reason, a Dock icon set at run time', () => {
  const info = plist(path.join(BUNDLE, 'Info.plist'));
  assert.equal(info.CFBundleIdentifier, 'io.github.skittles-fivem.sentry-six-manager');
  assert.equal(info.CFBundleName, 'Sentry Six Manager');
  assert.equal(info.CFBundleExecutable, 'Sentry Six Manager');
  assert.equal(info.CFBundlePackageType, 'APPL');
  assert.equal(info.LSMinimumSystemVersion, '14.0');
  assert.deepEqual(info.LSArchitecturePriority, ['arm64']);
  assert.equal(info.LSRequiresNativeExecution, true);
  assert.equal(info.LSApplicationCategoryType, 'public.app-category.video');
  assert.equal(info.LSUIElement, false);
  assert.equal(info.NSLocalNetworkUsageDescription, 'Sentry Six Manager connects to your NAS and to other Sentry Six Manager servers on your network.');
  assert.equal(info.CFBundleShortVersionString, '@VERSION@');
  assert.equal(info.CFBundleVersion, '@BUILD@');
  assert.ok(info.NSHumanReadableCopyright);
  // the app never asks for a web view's permissions
  assert.ok(!Object.keys(info).some((k) => /WebKit|NSAppTransportSecurity/.test(k)));
});

test('entitlements: the app has none (hardened runtime, no sandbox); node has JIT only, nothing notarization refuses', () => {
  assert.deepEqual(plist(path.join(BUNDLE, 'SentrySixManager.entitlements')), {});
  const node = plist(path.join(BUNDLE, 'node.entitlements'));
  assert.deepEqual(node, { 'com.apple.security.cs.allow-jit': true, 'com.apple.security.cs.allow-unsigned-executable-memory': true });
  // what sign.sh checks after signing is the same list
  assert.match(fs.readFileSync(path.join(SCRIPTS, 'sign.sh'), 'utf8'), /check_entitlements "\$app\/Contents\/Resources\/engine\/node\/bin\/node" 'com\.apple\.security\.cs\.allow-jit com\.apple\.security\.cs\.allow-unsigned-executable-memory'/);
});

test('the LaunchAgent: its own label, the launcher in the bundle, and the daemon\'s launchd rules', () => {
  const agent = plist(path.join(BUNDLE, `${LABEL}.agent.plist`));
  const daemon = plist(path.join(REPO, 'macos', `${LABEL}.plist`));
  assert.equal(agent.Label, `${LABEL}.agent`);
  assert.notEqual(agent.Label, daemon.Label);
  assert.equal(agent.BundleProgram, 'Contents/MacOS/sentry-six-engine');
  assert.deepEqual(agent.AssociatedBundleIdentifiers, [plist(path.join(BUNDLE, 'Info.plist')).CFBundleIdentifier]);
  for (const k of ['RunAtLoad', 'KeepAlive', 'ThrottleInterval', 'ExitTimeOut', 'ProcessType', 'Umask', 'SoftResourceLimits', 'HardResourceLimits']) {
    assert.deepEqual(agent[k], daemon[k], k);
  }
  // no user, no paths: the launcher works them out from the home folder
  for (const k of ['UserName', 'GroupName', 'ProgramArguments', 'EnvironmentVariables', 'StandardOutPath']) assert.ok(!(k in agent), k);
  assert.doesNotMatch(fs.readFileSync(path.join(BUNDLE, `${LABEL}.agent.plist`), 'utf8'), /DYLD_/);
});

test('the launcher\'s environment is the daemon plist\'s keys plus SERVICE_MODE', () => {
  const daemon = plist(path.join(REPO, 'macos', `${LABEL}.plist`));
  const src = fs.readFileSync(path.join(APP, 'Sources/SentryKit/Service/AgentEnvironment.swift'), 'utf8');
  const block = /public var environment: \[String: String\] \{\n\s*\[([\s\S]*?)\]\n\s*\}/.exec(src)[1];
  const keys = [...block.matchAll(/^\s*"([A-Z_]+)":/gm)].map((m) => m[1]).sort();
  assert.deepEqual(keys, [...Object.keys(daemon.EnvironmentVariables), 'SERVICE_MODE'].sort());
  // and the same node flags
  assert.match(src, /"--disable-warning=ExperimentalWarning", "--env-file-if-exists=\\\(paths\.managerEnv\)"/);
  assert.equal(daemon.ProgramArguments[1], '--disable-warning=ExperimentalWarning');
});

test('the app\'s fixed paths are install.sh\'s', () => {
  const swift = fs.readFileSync(path.join(APP, 'Sources/SentryKit/Service/ServiceMode.swift'), 'utf8');
  const install = fs.readFileSync(path.join(REPO, 'macos', 'install.sh'), 'utf8');
  for (const p of ['/Library/SentrySixManager', '/Library/Application Support/SentrySixManager', '/Library/Application Support/SentrySixManager Public', '/Library/Application Support/SentrySixManager Local']) {
    assert.ok(swift.includes(`"${p}"`), p);
    assert.ok(install.includes(`$ROOT${p}`), p);
  }
  assert.match(fs.readFileSync(path.join(REPO, 'macos', `${LABEL}.plist`), 'utf8'), /SentrySixManager Public\/engine\.json/);
});

// ---- no WebKit

test('no WebKit: the sources pass, and each forbidden use is caught', () => {
  const ok = spawnSync('/bin/bash', [path.join(SCRIPTS, 'check-no-webkit.sh')], { encoding: 'utf8' });
  assert.equal(ok.status, 0, ok.stderr);
  for (const bad of ['import WebKit', 'import JavaScriptCore', 'let v = WKWebView()', 'let v = WebView(frame: .zero)', 'let c = JSContext()',
    'let o = [NSAttributedString.DocumentReadingOptionKey.documentType: NSAttributedString.DocumentType.html]', 'let s = NSAttributedString(html: d, documentAttributes: nil)',
    'let p = "../../examples/ui-packs/horizon/index.html"']) {
    const dir = tmp();
    fs.mkdirSync(path.join(dir, 'X'));
    fs.writeFileSync(path.join(dir, 'X', 'Bad.swift'), `${bad}\n`);
    const r = spawnSync('/bin/bash', [path.join(SCRIPTS, 'check-no-webkit.sh'), '--sources', dir], { encoding: 'utf8' });
    assert.notEqual(r.status, 0, bad);
    assert.match(r.stderr, /use WebKit, JavaScriptCore or HTML/, bad);
  }
});

test('the app target is macOS only; SentryKit and the launcher use nothing Linux lacks', () => {
  const pkg = fs.readFileSync(path.join(APP, 'Package.swift'), 'utf8');
  assert.match(pkg, /^\/\/ swift-tools-version:6\.0$/m);
  assert.match(pkg, /platforms: \[\.macOS\(\.v14\)\]/);
  assert.match(pkg, /#if os\(macOS\)\n[\s\S]*name: "SentrySixManager"[\s\S]*#endif/);
  assert.doesNotMatch(pkg.split('#if os(macOS)')[0], /"SentrySixManager"/);
  const macOnly = /^import +(AppKit|SwiftUI|AVFoundation|AVKit|MapKit|Charts|Security|ServiceManagement|CryptoKit|IOKit|UserNotifications)\b/m;
  const walk = (d) => fs.readdirSync(d, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]));
  for (const dir of ['Sources/SentryKit', 'Sources/sentry-six-engine', 'Tests/SentryKitTests']) {
    for (const f of walk(path.join(APP, dir)).filter((x) => x.endsWith('.swift'))) assert.doesNotMatch(fs.readFileSync(f, 'utf8'), macOnly, f);
  }
  // the generated manifest file is never committed
  assert.match(fs.readFileSync(path.join(APP, '.gitignore'), 'utf8'), /^\/Sources\/SentrySixManager\/Platform\/EngineManifest\.generated\.swift$/m);
  assert.match(fs.readFileSync(path.join(APP, '.gitignore'), 'utf8'), /^\/\.build\/$/m);
});

test('nothing of the Mac app goes into the Docker image or the macOS tarball\'s app files', () => {
  const r = spawnSync(process.execPath, [path.join(REPO, 'macos', 'appFiles.js'), 'list', REPO], { encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
  assert.ok(r.stdout.length > 0);
  assert.ok(!r.stdout.split('\n').some((l) => l.startsWith('macos/') || l.includes('/macos/app/')));
  assert.doesNotMatch(fs.readFileSync(path.join(REPO, 'Dockerfile'), 'utf8'), /macos\/app/);
});

test('BUILD_INFO for the app\'s engine: channel macos-app; anything else is refused', () => {
  const pins = JSON.parse(fs.readFileSync(path.join(REPO, 'macos', 'versions.json'), 'utf8'));
  const base = { repo: REPO, sha: 'a'.repeat(40), branch: 'main', builtAt: '2026-10-30T12:00:00Z', release: 'skittles-fivem/Sentry-Manager', pins };
  assert.equal(buildInfo(base).channel, 'macos');
  assert.equal(buildInfo({ ...base, channel: 'macos-app' }).channel, 'macos-app');
  assert.throws(() => buildInfo({ ...base, channel: 'docker' }), /channel is macos or macos-app/);
  assert.match(fs.readFileSync(path.join(REPO, 'macos', 'build.sh'), 'utf8'), /buildInfo\.js" .* "\$CHANNEL"$/m);
});

test('macos.yml: SentryKit on Linux with a pinned toolchain, the app on macos-15, secrets only on the signing step', () => {
  const yml = fs.readFileSync(path.join(REPO, '.github', 'workflows', 'macos.yml'), 'utf8');
  const job = (name) => {
    const m = new RegExp(`\\n  ${name}:\\n([\\s\\S]*?)(?=\\n  [a-z][a-z-]*:\\n|$)`).exec(yml);
    assert.ok(m, name);
    return m[1];
  };
  const linux = job('sentrykit-linux');
  assert.match(linux, /runs-on: ubuntu-24\.04/);
  assert.match(linux, /swift-toolchain\.json/);
  assert.match(linux, /swift build\n\s*swift test/);
  const pin = JSON.parse(fs.readFileSync(path.join(APP, 'swift-toolchain.json'), 'utf8'));
  assert.match(pin.url, /^https:\/\/download\.swift\.org\//);
  assert.match(pin.sha256, /^[0-9a-f]{64}$/);
  const app = job('app');
  assert.match(app, /runs-on: macos-15/);
  assert.match(app, /check-no-webkit\.sh/);
  assert.match(app, /build-app\.sh/);
  assert.match(app, /upload-artifact[\s\S]*name: macos-app/);
  assert.match(app, /if: always\(\)\n\s*run: bash macos\/app\/scripts\/sign\.sh cleanup/);
  // each secret appears once, on the build step's env
  for (const s of ['APPLE_ID', 'APPLE_TEAM_ID', 'APPLE_APP_PASSWORD', 'DEVELOPER_ID_P12', 'DEVELOPER_ID_P12_PASSWORD']) {
    assert.equal(yml.split(`secrets.${s} }}`).length - 1, 1, s);
  }
  assert.match(job('app-check'), /--check/);
  assert.match(job('publish'), /needs: \[package, install-check, app, app-check\]/);
  assert.match(job('publish'), /cat dist-app\/SHA256SUMS\.dmg >> dist\/SHA256SUMS/);
});
