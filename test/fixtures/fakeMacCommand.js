'use strict';

// Stand-ins for the macOS commands the macos/ scripts run, so they can be
// run on Linux (test/macosInstall.test.js). Each stub on the fake PATH is
//   #!<node>
//   require('<this file>')('<command>')
// Every call is appended to $FAKE_DIR/calls.log as one JSON line, so a test
// can check what ran and in which order. State lives in $FAKE_DIR as JSON:
// dscl.json (users and groups), launchd.json (the job).
//
// What the stubs read from the environment:
//   FAKE_DIR          the folder with calls.log and the state files
//   FAKE_UID          what `id -u` prints (default 0)
//   FAKE_MACOS        what `sw_vers -productVersion` prints (default 15.1)
//   FAKE_ARM64        hw.optional.arm64: 1, 0, or 'missing' (an Intel Mac)
//   FAKE_URLS         JSON {url: file} that curl downloads from
//   FAKE_CODESIGN_FAIL  a path part whose `codesign --verify` fails
//   FAKE_SETUP_CODE   written to data/setup-code.txt when the job starts
//   SENTRY_ROOT       the sandbox root, as the scripts use it

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const LABEL = 'io.github.skittles-fivem.sentry-six-manager';
const dir = () => process.env.FAKE_DIR;
const root = () => (process.env.SENTRY_ROOT || '').replace(/\/+$/, '');

function load(name, fallback) {
  try {
    return JSON.parse(fs.readFileSync(path.join(dir(), name), 'utf8'));
  } catch {
    return fallback;
  }
}

function save(name, value) {
  fs.writeFileSync(path.join(dir(), name), JSON.stringify(value, null, 2));
}

function out(text) {
  if (text) process.stdout.write(text.endsWith('\n') ? text : `${text}\n`);
}

function exit(code, err) {
  if (err) process.stderr.write(`${err}\n`);
  process.exit(code);
}

// the real command, found on PATH after the fake folder
function real(name, args) {
  const fakeBin = path.dirname(process.argv[1]);
  for (const d of String(process.env.PATH || '').split(':')) {
    if (!d || path.resolve(d) === path.resolve(fakeBin)) continue;
    const p = path.join(d, name);
    if (fs.existsSync(p)) {
      const r = spawnSync(p, args, { stdio: 'inherit' });
      return r.status === null ? 1 : r.status;
    }
  }
  return 127;
}

const commands = {
  id(args) {
    if (args[0] === '-u') return out(process.env.FAKE_UID || '0');
    exit(1, 'id: only -u here');
  },

  sw_vers(args) {
    if (args[0] === '-productVersion') return out(process.env.FAKE_MACOS || '15.1');
    out('ProductName:\tmacOS');
  },

  sysctl(args) {
    const key = args[args.length - 1];
    if (key === 'hw.optional.arm64') {
      const v = process.env.FAKE_ARM64 ?? '1';
      if (v === 'missing') exit(1, "sysctl: unknown oid 'hw.optional.arm64'");
      return out(v);
    }
    exit(1, `sysctl: unknown oid '${key}'`);
  },

  scutil(args) {
    if (args.join(' ') === '--get LocalHostName') return out('Test-Mac');
    exit(1);
  },

  ipconfig(args) {
    if (args.join(' ') === 'getifaddr en0') return out('192.168.1.20');
    exit(1);
  },

  xattr() {},

  chown() {},

  mount() {},

  // numeric and symbolic modes are real; ACL changes (+a) are only logged
  chmod(args) {
    if (args.some((a) => a === '+a' || a.startsWith('+a'))) return;
    process.exit(real('chmod', args));
  },

  codesign(args) {
    const bad = process.env.FAKE_CODESIGN_FAIL;
    if (bad && args.some((a) => a.includes(bad))) exit(1, `${args[args.length - 1]}: invalid signature`);
  },

  // shasum -a 256 [-c] <file>, enough for the scripts
  shasum(args) {
    const files = args.filter((a, i) => !a.startsWith('-') && args[i - 1] !== '-a');
    const hash = (f) => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
    if (args.includes('-c')) {
      let bad = 0;
      for (const line of fs.readFileSync(files[0], 'utf8').split('\n').filter(Boolean)) {
        const m = /^([0-9a-f]{64}) [ *](.+)$/.exec(line);
        if (!m) exit(1, `shasum: bad line ${line}`);
        const ok = fs.existsSync(m[2]) && hash(m[2]) === m[1];
        out(`${m[2]}: ${ok ? 'OK' : 'FAILED'}`);
        if (!ok) bad++;
      }
      if (bad) exit(1, `shasum: WARNING: ${bad} computed checksum did NOT match`);
      return;
    }
    for (const f of files) out(`${hash(f)}  ${f}`);
  },

  // runs the command after -u <user>
  sudo(args) {
    if (args[0] !== '-u') exit(1, 'sudo: only -u here');
    const r = spawnSync(args[2], args.slice(3), { stdio: 'inherit' });
    process.exit(r.status === null ? 1 : r.status);
  },

  curl(args) {
    let outFile = null;
    let url = null;
    const withValue = new Set(['-o', '-H', '--max-time', '--proto', '--proto-redir', '--retry']);
    for (let i = 0; i < args.length; i++) {
      if (args[i] === '-o') outFile = args[++i];
      else if (withValue.has(args[i])) i++;
      else if (!args[i].startsWith('-')) url = args[i];
    }
    if (/^http:\/\/127\.0\.0\.1:\d+\/healthz$/.test(url)) {
      const job = load('launchd.json', {});
      const health = path.join(root(), 'Library/SentrySixManager/app/HEALTH');
      const ok = job.running && fs.existsSync(health) && fs.readFileSync(health, 'utf8').trim() === 'ok';
      if (!ok) exit(7, 'curl: (7) Failed to connect');
      return process.stdout.write('ok');
    }
    const urls = JSON.parse(process.env.FAKE_URLS || '{}');
    if (!url || !urls[url]) exit(22, `curl: (22) The requested URL returned error: 404 (${url})`);
    if (outFile) fs.copyFileSync(urls[url], outFile);
    else process.stdout.write(fs.readFileSync(urls[url]));
  },

  // dscl . -read|-list|-create|-delete, on dscl.json
  dscl(args) {
    if (args[0] !== '.') exit(1, 'dscl: only the local node here');
    const db = load('dscl.json', { Users: {}, Groups: {} });
    const [op, recPath, attr, ...values] = args.slice(1);
    const [, kind, name] = /^\/(Users|Groups)(?:\/(.+))?$/.exec(recPath) || [];
    if (!kind) exit(1, `dscl: bad path ${recPath}`);
    const notFound = () => exit(56, '<dscl_cmd> DS Error: -14136 (eDSRecordNotFound)');
    if (op === '-read') {
      const rec = db[kind][name];
      if (!rec) notFound();
      const keys = attr ? [attr] : Object.keys(rec);
      for (const k of keys) {
        if (!(k in rec)) exit(181, `No such key: ${k}`);
        out(`${k}: ${rec[k]}`);
      }
      return;
    }
    if (op === '-list') {
      for (const [n, rec] of Object.entries(db[kind])) {
        if (attr && !(attr in rec)) continue;
        out(attr ? `${n.padEnd(24)}${rec[attr]}` : n);
      }
      return;
    }
    if (op === '-create') {
      db[kind][name] = db[kind][name] || {};
      if (attr) db[kind][name][attr] = values.join(' ');
      return save('dscl.json', db);
    }
    if (op === '-delete') {
      if (!db[kind][name]) notFound();
      delete db[kind][name];
      return save('dscl.json', db);
    }
    exit(1, `dscl: ${op} not here`);
  },

  // launchctl on launchd.json { loaded, running, disabled }
  launchctl(args) {
    const job = load('launchd.json', { loaded: false, running: false, disabled: false });
    const target = `system/${LABEL}`;
    const [op, a, b] = args;
    const mine = (t) => t === target;
    if (op === 'print') {
      if (!mine(a) || !job.loaded) exit(113, `Could not find service "${LABEL}" in domain for system`);
      return out(`${target} = {\n\tstate = ${job.running ? 'running' : 'not running'}\n${job.running ? '\tpid = 4242\n' : ''}\tlast exit code = 0\n}`);
    }
    if (op === 'enable' && mine(a)) return save('launchd.json', { ...job, disabled: false });
    if (op === 'disable' && mine(a)) return save('launchd.json', { ...job, disabled: true });
    if (op === 'bootstrap' && a === 'system') {
      if (job.disabled) exit(5, 'Bootstrap failed: 5: Input/output error (service is disabled)');
      if (job.loaded) exit(5, 'Bootstrap failed: 5: Input/output error (already loaded)');
      if (!fs.existsSync(b)) exit(2, `${b}: No such file or directory`);
      const code = process.env.FAKE_SETUP_CODE;
      if (code) {
        const data = path.join(root(), 'Library/Application Support/SentrySixManager/data');
        fs.writeFileSync(path.join(data, 'setup-code.txt'), `${code}\n`);
      }
      return save('launchd.json', { ...job, loaded: true, running: true });
    }
    if (op === 'kill' && mine(b)) {
      if (!job.loaded) exit(113);
      return save('launchd.json', { ...job, running: false });
    }
    if (op === 'kickstart' && mine(a)) {
      if (!job.loaded) exit(113);
      return save('launchd.json', { ...job, running: true });
    }
    if (op === 'bootout' && mine(a)) {
      if (!job.loaded) exit(3, 'Boot-out failed: 3: No such process');
      return save('launchd.json', { ...job, loaded: false, running: false });
    }
    exit(64, `launchctl: ${args.join(' ')} not here`);
  },
};

module.exports = function run(name) {
  const args = process.argv.slice(2);
  fs.appendFileSync(path.join(dir(), 'calls.log'), `${JSON.stringify({ cmd: name, args })}\n`);
  const fn = commands[name];
  if (!fn) exit(127, `${name}: no fake`);
  fn(args);
};

module.exports.commands = Object.keys(commands);
