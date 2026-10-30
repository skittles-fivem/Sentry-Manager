'use strict';

// The launchd job of the macOS build (macos/<label>.plist), read with a
// small XML property list reader and checked key by key.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const LABEL = 'io.github.skittles-fivem.sentry-six-manager';
const FILE = path.join(__dirname, '..', 'macos', `${LABEL}.plist`);
const PROG = '/Library/SentrySixManager';
const STATE = '/Library/Application Support/SentrySixManager';

const ENTITIES = { '&lt;': '<', '&gt;': '>', '&amp;': '&', '&quot;': '"', '&apos;': "'" };
const unescape = (s) => s.replace(/&(lt|gt|amp|quot|apos);/g, (m) => ENTITIES[m]);

// The XML plist format: dict, array, string, integer, real, true, false.
// Anything else is an error, so a typo cannot pass as a missing key.
function parsePlist(xml) {
  const body = xml
    .replace(/<\?xml[^>]*\?>/, '')
    .replace(/<!DOCTYPE[^>]*>/, '')
    .replace(/<!--[\s\S]*?-->/g, '');
  const tokens = body.match(/<[^>]+>|[^<]+/g).filter((t) => t.trim());
  let i = 0;
  const next = () => tokens[i++];
  const text = (tag) => {
    let s = '';
    while (tokens[i] !== `</${tag}>`) {
      if (tokens[i] === undefined || tokens[i].startsWith('<')) throw new Error(`bad <${tag}>`);
      s += tokens[i++];
    }
    i++;
    return unescape(s);
  };
  function value() {
    const t = next();
    if (t === '<true/>') return true;
    if (t === '<false/>') return false;
    if (t === '<string/>') return '';
    if (t === '<string>') return text('string');
    if (t === '<integer>') {
      const s = text('integer').trim();
      if (!/^-?\d+$/.test(s)) throw new Error(`bad integer ${s}`);
      return Number(s);
    }
    if (t === '<real>') return Number(text('real'));
    if (t === '<array>') {
      const a = [];
      while (tokens[i] !== '</array>') a.push(value());
      i++;
      return a;
    }
    if (t === '<dict>') {
      const d = {};
      while (tokens[i] !== '</dict>') {
        if (next() !== '<key>') throw new Error(`expected <key> at ${tokens[i - 1]}`);
        const k = text('key');
        if (k in d) throw new Error(`key ${k} twice`);
        d[k] = value();
      }
      i++;
      return d;
    }
    throw new Error(`not understood: ${t}`);
  }
  if (next() !== '<plist version="1.0">') throw new Error('no <plist version="1.0">');
  const v = value();
  if (next() !== '</plist>' || i !== tokens.length) throw new Error('trailing content');
  return v;
}

const xml = fs.readFileSync(FILE, 'utf8');
const job = parsePlist(xml);

test('the reader reads plists and refuses what is not one', () => {
  assert.deepEqual(parsePlist('<plist version="1.0"><dict><key>a</key><array><string>x &amp; y</string><integer>-3</integer><false/></array></dict></plist>'), { a: ['x & y', -3, false] });
  assert.throws(() => parsePlist('<plist version="1.0"><dict><key>a</key><strin>x</strin></dict></plist>'));
  assert.throws(() => parsePlist('<plist version="1.0"><dict><key>a</key><true/><key>a</key><true/></dict></plist>'));
});

test('exactly these keys', () => {
  assert.deepEqual(Object.keys(job).sort(), [
    'EnvironmentVariables', 'ExitTimeOut', 'GroupName', 'HardResourceLimits', 'InitGroups', 'KeepAlive', 'Label',
    'ProcessType', 'ProgramArguments', 'RunAtLoad', 'SoftResourceLimits', 'StandardErrorPath', 'StandardOutPath',
    'ThrottleInterval', 'Umask', 'UserName', 'WorkingDirectory',
  ]);
});

test('who it runs as, and how launchd treats it', () => {
  assert.equal(job.Label, LABEL);
  assert.equal(job.UserName, '_sentrysix');
  assert.equal(job.GroupName, '_sentrysix');
  assert.equal(job.InitGroups, true);
  assert.equal(job.RunAtLoad, true);
  // a clean stop (exit 0) stays stopped; anything else restarts after 5 s
  assert.deepEqual(job.KeepAlive, { SuccessfulExit: false });
  assert.equal(job.ThrottleInterval, 5);
  // SIGTERM, then SIGKILL a minute later, as Docker's stop_grace_period
  assert.equal(job.ExitTimeOut, 60);
  // not throttled to background CPU and disk
  assert.equal(job.ProcessType, 'Interactive');
  // decimal 23 is 027, the umask index.js sets for itself
  assert.equal(job.Umask, 0o027);
  assert.deepEqual(job.SoftResourceLimits, { NumberOfFiles: 8192 });
  assert.deepEqual(job.HardResourceLimits, { NumberOfFiles: 8192 });
  assert.equal(job.WorkingDirectory, '/');
  assert.equal(job.StandardOutPath, '/Library/Logs/SentrySixManager/service.log');
  assert.equal(job.StandardErrorPath, '/Library/Logs/SentrySixManager/service.log');
});

test('node with manager.env, then the app', () => {
  assert.deepEqual(job.ProgramArguments, [
    `${PROG}/node/bin/node`,
    '--disable-warning=ExperimentalWarning',
    `--env-file-if-exists=${STATE}/manager.env`,
    `${PROG}/app/server/index.js`,
  ]);
});

test('the environment: fixed paths only, nothing manager.env should set, no DYLD_', () => {
  assert.deepEqual(job.EnvironmentVariables, {
    NODE_ENV: 'production',
    APP_ROOT: `${PROG}/app`,
    DATA_PATH: `${STATE}/data`,
    RUN_DIR: `${STATE}/run`,
    FOOTAGE_ROOT: `${STATE}/footage`,
    FFMPEG_PATH: `${PROG}/ffmpeg/ffmpeg`,
    FFPROBE_PATH: `${PROG}/ffmpeg/ffprobe`,
    SENTRY_NOREPLACE_RENAME: `${PROG}/bin/sentry-fsops`,
    // not inside DATA: Apple's frameworks may write ~/Library there
    HOME: `${STATE}/run/home`,
    TMPDIR: `${STATE}/run/tmp`,
    PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
    UV_THREADPOOL_SIZE: '16',
    SENTRY_CONSOLE: 'off',
    // the Mac app (docs/plans/macos-app.md 2.3, 2.6): beside the state root, not in it
    PUBLIC_INFO_PATH: '/Library/Application Support/SentrySixManager Public/engine.json',
    LOCAL_SIGNIN_SOCKET: '/Library/Application Support/SentrySixManager Local/signin.sock',
  });
  // the variables set here win over manager.env: PORT, TZ and the rest stay settable
  for (const k of ['PORT', 'HOST', 'TZ', 'TLS_MODE', 'PUID', 'PGID', 'SENTRY_SUPERVISED']) assert.ok(!(k in job.EnvironmentVariables), k);
  assert.doesNotMatch(xml, /DYLD_/);
  for (const v of Object.values(job.EnvironmentVariables)) assert.equal(typeof v, 'string');
});

test('the Docker image sets the same production values', () => {
  const dockerfile = fs.readFileSync(path.join(__dirname, '..', 'Dockerfile'), 'utf8');
  assert.match(dockerfile, /NODE_ENV=production/);
  assert.match(dockerfile, new RegExp(`UV_THREADPOOL_SIZE=${job.EnvironmentVariables.UV_THREADPOOL_SIZE}\\b`));
});
