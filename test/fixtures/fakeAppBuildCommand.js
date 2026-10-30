'use strict';

// Stand-ins for the Xcode and macOS tools macos/app/scripts/*.sh run, so
// build-app.sh runs on Linux (test/macosApp.test.js). Each stub on the
// fake PATH is
//   #!<node>
//   require('<this file>')('<command>')
// and every call is appended to $FAKE_DIR/calls.log as one JSON line.
// codesign appends a line to the file it signs, so a test can tell
// whether something was hashed before or after it was signed.

const fs = require('fs');
const path = require('path');

const log = (cmd, args) => fs.appendFileSync(path.join(process.env.FAKE_DIR, 'calls.log'), `${JSON.stringify({ cmd, args })}\n`);
const out = (s) => process.stdout.write(s.endsWith('\n') ? s : `${s}\n`);
const exit = (code, msg) => { if (msg) process.stderr.write(`${msg}\n`); process.exit(code); };
const last = (args) => args[args.length - 1];

const commands = {
  uname: (a) => out(a[0] === '-s' ? 'Darwin' : 'Darwin'),
  sysctl: (a) => out(a.includes('hw.optional.arm64') ? '1' : '0'),

  // swift build ... --product X: an arm64 "binary"; --show-bin-path: where
  swift(a) {
    const bin = path.join(process.env.FAKE_DIR, 'swift-bin');
    if (a.includes('--show-bin-path')) return out(bin);
    const p = a[a.indexOf('--product') + 1];
    fs.mkdirSync(bin, { recursive: true });
    fs.writeFileSync(path.join(bin, p), `#!/bin/sh\necho ${p}\n`, { mode: 0o755 });
  },

  codesign(a) {
    if (a.includes('--verify')) return;
    if (a.includes('-d')) {
      // the entitlements a signature carries: from the file named at signing
      const f = last(a);
      const ent = fs.existsSync(`${f}.fake-entitlements`) ? fs.readFileSync(`${f}.fake-entitlements`, 'utf8') : '';
      return out(ent);
    }
    const f = last(a);
    const i = a.indexOf('--entitlements');
    const st = fs.statSync(f);
    if (st.isFile()) {
      fs.appendFileSync(f, `\n# signed by ${a[a.indexOf('--sign') + 1]}\n`);
      if (i >= 0) fs.writeFileSync(`${f}.fake-entitlements`, fs.readFileSync(a[i + 1]));
    } else if (i >= 0) {
      // a bundle: its main binary carries them
      const main = path.join(f, 'Contents', 'MacOS', 'Sentry Six Manager');
      if (fs.existsSync(main)) fs.writeFileSync(`${main}.fake-entitlements`, fs.readFileSync(a[i + 1]));
    }
  },

  lipo: () => out('arm64'),
  plutil(a) {
    if (a[0] === '-lint') return out(`${last(a)}: OK`);
    if (a[0] === '-extract') {
      const v = JSON.parse(fs.readFileSync(last(a), 'utf8'))[a[1]];
      if (v === undefined) exit(1, 'no such key');
      return out(String(v));
    }
    exit(1, 'plutil: not here');
  },
  iconutil: () => {},

  // ditto <from> <to>, ditto -c -k --keepParent <from> <zip>
  ditto(a) {
    const files = a.filter((x) => !x.startsWith('-'));
    if (a.includes('-c')) return fs.writeFileSync(files[1], 'zip');
    fs.mkdirSync(files[1], { recursive: true });
    fs.cpSync(files[0], files[1], { recursive: true, verbatimSymlinks: true });
  },

  hdiutil(a) {
    if (a[0] === 'create') {
      const src = a[a.indexOf('-srcfolder') + 1];
      // what went in: the app and the Applications link
      const listing = fs.readdirSync(src).map((n) => `${n}${fs.lstatSync(path.join(src, n)).isSymbolicLink() ? ` -> ${fs.readlinkSync(path.join(src, n))}` : ''}`);
      return fs.writeFileSync(last(a), `dmg\n${listing.join('\n')}\n`);
    }
    if (a[0] === 'verify') return;
    exit(1, 'hdiutil: not here');
  },

  xcrun(a) {
    if (a[0] === 'notarytool' && a[1] === 'submit') {
      const status = process.env.FAKE_NOTARY_STATUS || 'Accepted';
      return out(JSON.stringify({ id: 'fake-1234', status }));
    }
    if (a[0] === 'notarytool' && a[1] === 'log') return out('{"issues":["fake"]}');
    if (a[0] === 'stapler') return;
    exit(1, 'xcrun: not here');
  },

  security(a) {
    if (a[0] === 'find-identity') {
      return out(`  1) ${'A'.repeat(40)} "Developer ID Application: Test Person (${process.env.APPLE_TEAM_ID})"\n     1 valid identities found`);
    }
  },

  // a "binary" is anything here; no WebKit unless the test plants one
  file: () => out('Mach-O 64-bit executable arm64'),
  otool: (a) => out(`${last(a)}:\n\t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n${fs.existsSync(`${last(a)}.links-webkit`) ? '\t/System/Library/Frameworks/WebKit.framework/Versions/A/WebKit (x)\n' : ''}`),
};

module.exports = function run(name) {
  const args = process.argv.slice(2);
  log(name, args);
  const fn = commands[name];
  if (!fn) exit(127, `${name}: no fake`);
  fn(args);
};

module.exports.commands = Object.keys(commands);
