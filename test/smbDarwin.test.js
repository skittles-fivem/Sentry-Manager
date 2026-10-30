'use strict';

// The macOS SMB mounter, on Linux: a fake script(1) child stands in for the
// pseudo-terminal and a fake runCmd for /sbin/mount, /sbin/umount and
// smbutil. The fixtures are /sbin/mount and smbutil statshares output in the
// form macOS prints them (key names: verify on a Mac).

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const fsp = require('fs/promises');
const path = require('path');
const { EventEmitter } = require('events');
const { PassThrough } = require('stream');
const {
  createDarwinMounter, mountPlan, smbUrl, nsmbConf, scrubOutput, checkMountPoint,
  parseMountLine, parseDarwinMount, findDarwinMount, parseStatshares, checkStatshares,
  explainMountErrorDarwin, CHECK, PROMPT_RE
} = require('../server/platform/smbDarwin');
const { realTmp } = require('./fixtures/tmp');

const FIX = path.join(__dirname, 'fixtures', 'darwin');
const fixture = (name) => fs.readFileSync(path.join(FIX, name), 'utf8');
const SECRET = 'pa$$ w0rd;@x';
const SMB = { type: 'smb', share: '//nas/TeslaCam', username: 'tesla', password: SECRET, domain: '', version: '3.1.1', encrypt: true, port: null };
const ME = '_sentrysix';
const OK_OPTS = 'nodev, nosuid, noexec, nobrowse';
// Windows has no process.getuid, and its stat() reports every file as uid 0
const MY_UID = process.getuid?.() ?? 0;

/**
 * A fake /usr/bin/script child. `script` says what it does:
 *   banner   printed before the prompt
 *   prompt   false: never asks; otherwise the prompt text
 *   onPassword(child, text): what happens once the password arrives
 *   exitWithoutPrompt: exit code when it never asks
 */
function fakeSpawn(script = {}) {
  const calls = [];
  const spawn = (cmd, args, opts) => {
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    const call = { cmd, args, opts, child, stdin: '', stdinBeforePrompt: '', promptShown: false, stdinEndedAtExit: null, killed: null };
    calls.push(call);
    let closed = false;
    child.exit = (code) => {
      if (closed) return;
      closed = true;
      call.stdinEndedAtExit = child.stdin.writableEnded;
      setImmediate(() => child.emit('close', code, null));
    };
    child.kill = (sig) => {
      call.killed = sig;
      child.exit(null);
      return true;
    };
    child.stdin.on('data', (d) => {
      call.stdin += d;
      if (!call.promptShown) call.stdinBeforePrompt += d;
      if (call.stdin.endsWith('\n') && script.onPassword) script.onPassword(child, call.stdin, call);
    });
    if (script.spawnError) {
      setImmediate(() => child.emit('error', Object.assign(new Error('spawn ENOENT'), { code: 'ENOENT' })));
      return child;
    }
    setImmediate(() => {
      if (script.banner) child.stdout.write(script.banner);
      if (script.prompt === false) {
        if (script.exitWithoutPrompt !== undefined) {
          setTimeout(() => {
            script.beforeExit?.(call);
            child.exit(script.exitWithoutPrompt);
          }, 5);
        }
        return;
      }
      // readpassphrase flushes input before it prompts: show it a little later
      setTimeout(() => {
        call.promptShown = true;
        child.stdout.write(script.prompt || 'Password for nas: ');
      }, 10);
    });
    return child;
  };
  return { spawn, calls };
}

/** A fake runCmd with a mount table the fake mounts add to. */
function fakeRun({ table = '', statshares = fixture('statshares-encrypted.txt'), statsCode = 0, mountCode = 0 } = {}) {
  const calls = [];
  const state = { table };
  const runCmd = async (cmd, args) => {
    calls.push([cmd, ...args]);
    if (cmd === '/sbin/mount') return mountCode === 0 ? { code: 0, stdout: state.table, stderr: '' } : { code: mountCode, stdout: '', stderr: 'mount: failed' };
    if (cmd === '/sbin/umount') {
      const dir = args[args.length - 1];
      state.table = state.table.split('\n').filter((l) => !l.includes(` on ${dir} (`)).join('\n');
      return { code: 0, stdout: '', stderr: '' };
    }
    if (cmd === '/usr/bin/smbutil') return { code: statsCode, stdout: statshares, stderr: statsCode ? 'smbutil: failed' : '' };
    return { code: 127, stdout: '', stderr: 'unexpected command' };
  };
  const addMount = (dir, opts = OK_OPTS, by = ME, type = 'smbfs') => {
    state.table += `\n//tesla@nas/TeslaCam on ${dir} (${type}, ${opts}, mounted by ${by})`;
  };
  return { runCmd, calls, state, addMount };
}

/** The usual mount_smbfs: asks, takes the password, mounts. */
function mountsOk(fr, mountOpts) {
  return {
    banner: '',
    onPassword(child, text, call) {
      call.answered = text;
      fr.addMount(call.args[call.args.length - 1], ...(mountOpts || []));
      child.stdout.write('\r\n');
      child.exit(0);
    }
  };
}

async function setup(t, { root = 'foot age', ...opts } = {}) {
  // under the temp folder's real path: on Windows os.tmpdir() can be an 8.3
  // short name (RUNNER~1), which the mounter's realpath expands
  const base = await realTmp('sm-smbdarwin-');
  t.after(() => fsp.rm(base, { recursive: true, force: true }));
  const footageRoot = path.join(base, root);
  const runDir = path.join(base, 'run');
  // Windows cannot hold a line break in a name. The mounter refuses such a
  // root from its path alone, so there it is left uncreated
  if (!(process.platform === 'win32' && /[\r\n]/.test(root))) await fsp.mkdir(footageRoot, { recursive: true });
  const fr = fakeRun(opts.run);
  const fsps = fakeSpawn(opts.script ? opts.script(fr) : mountsOk(fr, opts.mountOpts));
  const mounter = createDarwinMounter({
    footageRoot, runDir, user: ME, uid: opts.uid ?? MY_UID, spawn: fsps.spawn, runCmd: fr.runCmd,
    now: opts.now, promptWaitMs: opts.promptWaitMs ?? 2000, timeoutMs: opts.timeoutMs ?? 4000
  });
  return { base, footageRoot, runDir, fr, fsps, mounter };
}

const noSecret = (value) => assert.equal(JSON.stringify(value).includes(SECRET), false, 'the password must not show up');

test('URL: every part is percent-encoded, :port only when set, never a password', () => {
  assert.equal(smbUrl({ ...SMB }), '//tesla@nas/TeslaCam');
  const url = smbUrl({ ...SMB, share: '//nas.local/Tesla Cam/a#b;c@d', username: 'me.x', domain: 'CORP', port: 4445 });
  assert.equal(url, '//CORP;me.x@nas.local:4445/Tesla%20Cam/a%23b%3Bc%40d');
  assert.equal(smbUrl({ ...SMB, share: '//10.0.0.5/100%' }), '//tesla@10.0.0.5/100%25');
  for (const u of [url, smbUrl(SMB)]) {
    assert.equal(u.includes(SECRET), false);
    assert.doesNotMatch(u.slice(2, u.indexOf('@')), /:/, 'nothing like user:password before the @');
  }
});

test('mount plan: script(1) runs mount_smbfs by absolute path; nothing secret in argv or env', () => {
  const plan = mountPlan(SMB, { target: '/Library/Application Support/SentrySixManager/footage/car', home: '/run/smb/car/home' });
  assert.equal(plan.cmd, '/usr/bin/script');
  assert.deepEqual(plan.args, ['-q', '/dev/null', '/sbin/mount_smbfs', '-s', '-o', 'nobrowse,nosuid,nodev,noexec,soft', '-f', '0660', '-d', '0770',
    '//tesla@nas/TeslaCam', '/Library/Application Support/SentrySixManager/footage/car']);
  assert.deepEqual(plan.env, { HOME: '/run/smb/car/home', PATH: '/usr/bin:/bin:/usr/sbin:/sbin', LANG: 'C', LC_ALL: 'C' });
  noSecret({ args: plan.args, env: plan.env, conf: plan.nsmbConf });
  // the shared validation still applies
  assert.throws(() => mountPlan({ ...SMB, version: '2.1' }, { target: '/x/car', home: '/h' }), { code: 'INVALID_SMB' });
  assert.throws(() => mountPlan({ ...SMB, password: 'a\nb' }, { target: '/x/car', home: '/h' }), { code: 'INVALID_SMB' });
  assert.throws(() => mountPlan({ ...SMB, username: '' }, { target: '/x/car', home: '/h' }), { code: 'INVALID_SMB' });
  // the terminal's line editing would act on these instead of passing them on
  for (const c of ['\x03', '\x04', '\x15', '\x16', '\x17', '\x7f', '\x1b']) {
    assert.throws(() => mountPlan({ ...SMB, password: `ab${c}cd` }, { target: '/x/car', home: '/h' }), { code: 'INVALID_SMB' }, JSON.stringify(c));
  }
  assert.equal(mountPlan({ ...SMB, password: 'a\tb ü' }, { target: '/x/car', home: '/h' }).smb.password, 'a\tb ü');
});

test('nsmb.conf: SMB 3 only, signing, NTLMv2; encryption only when the vehicle asks for it', () => {
  const base = ['[default]', 'protocol_vers_map=4', 'signing_required=yes', 'signing_req_vers=4', 'port445=no_netbios', 'minauth=ntlmv2'];
  assert.equal(nsmbConf({ encrypt: true }), `${[...base, 'force_sess_encrypt=yes', 'force_share_encrypt=yes'].join('\n')}\n`);
  assert.equal(nsmbConf({ encrypt: false }), `${base.join('\n')}\n`);
});

test('mount point: %, line breaks and NUL are refused; spaces are fine', () => {
  for (const bad of ['/foot/100%/car', '/foot/a\nb/car', '/foot/a\rb/car', '/foot/a\0b/car', 'relative/car']) {
    assert.throws(() => checkMountPoint(bad), { code: 'BAD_MOUNTPOINT' }, JSON.stringify(bad));
  }
  assert.equal(checkMountPoint('/Library/Application Support/x/car'), '/Library/Application Support/x/car');
});

test('mount: the password goes in once, only after the prompt, and stdin stays open until mount_smbfs exits', async (t) => {
  const { fsps, fr, mounter, footageRoot, runDir } = await setup(t);
  const res = await mounter.mount('car', SMB);
  assert.equal(res.code, 0, res.output);
  assert.deepEqual(res.warnings, []);
  const [call] = fsps.calls;
  assert.equal(fsps.calls.length, 1);
  assert.equal(call.cmd, '/usr/bin/script');
  assert.equal(call.args.at(-1), path.join(fs.realpathSync(footageRoot), 'car'));
  assert.equal(call.stdinBeforePrompt, '', 'nothing written before the prompt (readpassphrase flushes it)');
  assert.equal(call.stdin, `${SECRET}\n`, 'exactly once');
  assert.equal(call.stdinEndedAtExit, false, 'stdin was still open when the child exited');
  assert.equal(call.child.stdin.writableEnded, true, 'and is closed afterwards');
  assert.deepEqual(Object.keys(call.opts.env).sort(), ['HOME', 'LANG', 'LC_ALL', 'PATH']);
  assert.equal(call.opts.env.HOME, path.join(runDir, 'smb', 'car', 'home'));
  noSecret({ args: call.args, env: call.opts.env, res, runCalls: fr.calls });
  // the private nsmb.conf: 0700 folders, a 0600 file, no secret
  const prefs = path.join(runDir, 'smb', 'car', 'home', 'Library', 'Preferences');
  for (const d of [path.join(runDir, 'smb'), path.join(runDir, 'smb', 'car'), path.join(runDir, 'smb', 'car', 'home'), path.dirname(prefs), prefs]) {
    assert.equal(fs.statSync(d).mode & 0o777, 0o700, d);
  }
  const conf = path.join(prefs, 'nsmb.conf');
  assert.equal(fs.statSync(conf).mode & 0o777, 0o600);
  assert.equal(fs.readFileSync(conf, 'utf8'), nsmbConf({ encrypt: true }));
  assert.equal(fs.readFileSync(conf, 'utf8').includes(SECRET), false);
  // checked afterwards with smbutil, by absolute path
  assert.ok(fr.calls.some((c) => c[0] === '/usr/bin/smbutil' && c[1] === 'statshares' && c[2] === '-m' && c[3] === call.args.at(-1)));
  assert.equal(fr.calls.some((c) => c[0] === '/sbin/umount'), false, 'nothing left over, nothing failed: no unmount');
});

test('mount: a stale nsmb.conf home (even a symlink in it) is replaced, never followed', async (t) => {
  const { mounter, runDir, base } = await setup(t);
  const outside = path.join(base, 'outside.txt');
  fs.writeFileSync(outside, 'keep');
  const prefs = path.join(runDir, 'smb', 'car', 'home', 'Library', 'Preferences');
  fs.mkdirSync(prefs, { recursive: true });
  fs.symlinkSync(outside, path.join(prefs, 'nsmb.conf'));
  assert.equal((await mounter.mount('car', SMB)).code, 0);
  assert.equal(fs.readFileSync(outside, 'utf8'), 'keep');
  assert.equal(fs.lstatSync(path.join(prefs, 'nsmb.conf')).isSymbolicLink(), false);
});

test('mount: no prompt within the wait is a timeout; the child is killed and gets no password', async (t) => {
  const { fsps, mounter, fr } = await setup(t, { script: () => ({ prompt: false }), promptWaitMs: 50 });
  const res = await mounter.mount('car', SMB);
  assert.equal(res.timedOut, true);
  assert.notEqual(res.code, 0);
  assert.equal(fsps.calls[0].killed, 'SIGKILL');
  assert.equal(fsps.calls[0].stdin, '');
  assert.equal(fr.calls.some((c) => c[0] === '/usr/bin/smbutil'), false);
});

test('mount: a child that hangs after the password is killed at the overall timeout', async (t) => {
  const { fsps, mounter } = await setup(t, { script: () => ({ onPassword() {} }), promptWaitMs: 50, timeoutMs: 150 });
  const res = await mounter.mount('car', SMB);
  assert.equal(res.timedOut, true);
  assert.equal(fsps.calls[0].killed, 'SIGKILL');
  assert.equal(fsps.calls[0].stdin, `${SECRET}\n`);
});

test('mount: exit 0 without ever asking for the password is a failure, and the share is unmounted again', async (t) => {
  // what macOS would do with a saved credential: mount without asking
  const { mounter, fr, footageRoot } = await setup(t, {
    script: (r) => ({ prompt: false, exitWithoutPrompt: 0, beforeExit: (call) => r.addMount(call.args.at(-1)) })
  });
  const dir = path.join(fs.realpathSync(footageRoot), 'car');
  const res = await mounter.mount('car', SMB);
  assert.equal(res.code, 1);
  assert.equal(res.security, true);
  assert.ok(res.output.includes(CHECK.noPrompt));
  assert.deepEqual(fr.calls.filter((c) => c[0] === '/sbin/umount').at(-1), ['/sbin/umount', '-f', dir]);
  assert.match(explainMountErrorDarwin(res.output, res.code), /password typed here/);
});

test('mount: a second password prompt (wrong password) stops at once instead of waiting', async (t) => {
  const { fsps, mounter } = await setup(t, {
    script: () => ({ onPassword(child) { child.stdout.write('\r\nPassword for nas: '); } })
  });
  const res = await mounter.mount('car', SMB);
  assert.notEqual(res.code, 0);
  assert.equal(fsps.calls[0].killed, 'SIGKILL');
  assert.equal(fsps.calls[0].stdin, `${SECRET}\n`, 'still only once');
  assert.match(explainMountErrorDarwin(res.output, res.code), /user name and password/);
});

test('mount: output loses \\r, the prompt line and any echo of the password', async (t) => {
  const { mounter } = await setup(t, {
    script: () => ({
      banner: 'Connecting\r\n',
      onPassword(child) {
        child.stdout.write(`${SECRET}\r\nmount_smbfs: server rejected the connection: Authentication error\r\n`);
        child.exit(77);
      }
    })
  });
  const res = await mounter.mount('car', SMB);
  assert.equal(res.code, 77);
  // the echo landed on the prompt line, which goes as a whole
  assert.equal(res.output, 'Connecting\nmount_smbfs: server rejected the connection: Authentication error');
  noSecret(res);
  assert.match(explainMountErrorDarwin(res.output, res.code), /rejected the user name or password/);
  assert.equal(scrubOutput(`a\r\nPassword for nas: \r\nb ${SECRET} c`, SECRET), 'a\nb *** c');
  assert.ok(PROMPT_RE.test('Password for nas.local: '));
  assert.ok(PROMPT_RE.test('Password for nas:'));
  assert.equal(PROMPT_RE.test('Password for nas: \nmore'), false);
});

test('mount: a missing script(1) reads as not found', async (t) => {
  const { mounter } = await setup(t, { script: () => ({ spawnError: true }) });
  const res = await mounter.mount('car', SMB);
  assert.equal(res.notFound, true);
  assert.notEqual(res.code, 0);
});

test('mount: refuses a footage root with % or a line break before anything runs', async (t) => {
  for (const root of ['100%', 'a\nb']) {
    const { mounter, fsps, fr } = await setup(t, { root });
    await assert.rejects(mounter.mount('car', SMB), { code: 'BAD_MOUNTPOINT' });
    assert.equal(fsps.calls.length, 0);
    assert.equal(fr.calls.length, 0);
  }
});

test('mount: a symlinked mount point, or one owned by someone else, is refused', async (t) => {
  {
    const { mounter, fsps, footageRoot, base } = await setup(t);
    fs.mkdirSync(path.join(base, 'elsewhere'));
    fs.symlinkSync(path.join(base, 'elsewhere'), path.join(footageRoot, 'car'));
    await assert.rejects(mounter.mount('car', SMB), { code: 'BAD_MOUNTPOINT' });
    assert.equal(fsps.calls.length, 0);
  }
  {
    const { mounter, fsps } = await setup(t, { uid: MY_UID + 1 });
    await assert.rejects(mounter.mount('car', SMB), /not owned by the app's own user/);
    assert.equal(fsps.calls.length, 0);
  }
  {
    const { mounter } = await setup(t);
    await assert.rejects(mounter.mount('../etc', SMB), { code: 'INVALID_ID' });
  }
});

test('mount: a footage root behind a symlink (like /var -> /private/var) mounts at the real path', async (t) => {
  const { base, fr, fsps } = await setup(t);
  const real = path.join(base, 'private', 'footage');
  fs.mkdirSync(real, { recursive: true });
  fs.symlinkSync(path.join(base, 'private'), path.join(base, 'var'));
  const mounter = createDarwinMounter({ footageRoot: path.join(base, 'var', 'footage'), runDir: path.join(base, 'run2'), user: ME, spawn: fsps.spawn, runCmd: fr.runCmd });
  assert.equal((await mounter.mount('car', SMB)).code, 0);
  assert.equal(fsps.calls[0].args.at(-1), path.join(real, 'car'));
  assert.equal((await mounter.probe(mounter.target('car'))).fsType, 'smbfs');
});

test('mount: a leftover mount from before a crash is force-unmounted first', async (t) => {
  const { mounter, fr, footageRoot } = await setup(t);
  const dir = path.join(fs.realpathSync(footageRoot), 'car');
  fs.mkdirSync(dir);
  fr.addMount(dir);
  assert.equal((await mounter.mount('car', SMB)).code, 0);
  const firstUmount = fr.calls.findIndex((c) => c[0] === '/sbin/umount');
  assert.deepEqual(fr.calls[firstUmount], ['/sbin/umount', '-f', dir]);
  assert.ok(firstUmount < fr.calls.findIndex((c) => c[0] === '/usr/bin/smbutil'));
});

test('mount: a leftover mount that will not go away is refused, never checked in place of the new one', async (t) => {
  const { fr, fsps, footageRoot, runDir } = await setup(t);
  const dir = path.join(fs.realpathSync(footageRoot), 'car');
  fs.mkdirSync(dir);
  fr.addMount(dir);
  // an umount that fails, as with a share that is still busy
  const runCmd = async (cmd, args) => (cmd === '/sbin/umount' ? { code: 1, stdout: '', stderr: 'umount: busy' } : fr.runCmd(cmd, args));
  const m = createDarwinMounter({ footageRoot, runDir, user: ME, spawn: fsps.spawn, runCmd });
  const res = await m.mount('car', SMB);
  assert.equal(res.code, 1);
  assert.equal(fsps.calls.length, 0, 'mount_smbfs never ran');
  assert.match(explainMountErrorDarwin(res.output, res.code), /busy/);
});

test('mount fails closed: every check after mounting unmounts with -f and says why', async (t) => {
  const cases = [
    { what: 'no noexec', mountOpts: ['nodev, nosuid, nobrowse'], reason: CHECK.notOurs },
    { what: 'no nobrowse', mountOpts: ['nodev, nosuid, noexec'], reason: CHECK.notOurs },
    { what: 'mounted by someone else', mountOpts: [OK_OPTS, 'alice'], reason: CHECK.notOurs },
    { what: 'not smbfs', mountOpts: [OK_OPTS, ME, 'nfs'], reason: CHECK.notOurs },
    { what: 'SMB 2.1', run: { statshares: fixture('statshares-smb21.txt') }, reason: CHECK.dialect },
    { what: 'not encrypted', run: { statshares: fixture('statshares-signed.txt') }, reason: CHECK.notEncrypted },
    { what: 'guest', run: { statshares: fixture('statshares-guest.txt') }, smb: { encrypt: false }, reason: CHECK.guest },
    { what: '3.02 when 3.1.1 is required', run: { statshares: fixture('statshares-smb759.txt') }, reason: CHECK.dialect },
    { what: 'statshares fails', run: { statsCode: 1 }, reason: CHECK.noStats },
    { what: 'unsigned when not encrypting', run: { statshares: fixture('statshares-smb21.txt').replace('SMB_2.1', 'SMB_3.1.1') }, smb: { encrypt: false }, reason: CHECK.notSigned }
  ];
  for (const c of cases) {
    const { mounter, fr, footageRoot } = await setup(t, { run: c.run, mountOpts: c.mountOpts });
    const res = await mounter.mount('car', { ...SMB, ...c.smb });
    assert.equal(res.code, 1, c.what);
    assert.equal(res.security, true, c.what);
    assert.ok(res.output.includes(c.reason), `${c.what}: ${res.output}`);
    assert.deepEqual(fr.calls.filter((x) => x[0] === '/sbin/umount').at(-1), ['/sbin/umount', '-f', path.join(fs.realpathSync(footageRoot), 'car')], c.what);
    assert.ok(explainMountErrorDarwin(res.output, res.code), c.what);
    noSecret(res);
  }
});

test('mount: a NAS without stable file ids mounts, with a warning', async (t) => {
  const { mounter } = await setup(t, { run: { statshares: fixture('statshares-signed.txt') } });
  const res = await mounter.mount('car', { ...SMB, encrypt: false });
  assert.equal(res.code, 0, res.output);
  assert.equal(res.warnings.length, 1);
  assert.match(res.warnings[0], /file ids/);
});

test('statshares: both smbutil styles parse; the version gate and fail-closed rules', () => {
  const old = parseStatshares(fixture('statshares-smb759.txt'));
  assert.deepEqual(old.SMB_VERSION, ['SMB_3.02']);
  assert.deepEqual(old.ENCRYPTION_REQUIRED, ['TRUE']);
  assert.equal(old.SHARE, undefined, 'the header is not an attribute');
  assert.equal(old.TeslaCam, undefined);
  const enc = parseStatshares(fixture('statshares-encrypted.txt'));
  assert.deepEqual(enc.SMB_NEGOTIATE, ['SMBV_NEG_SMB2_ENABLED', 'SMBV_NEG_SMB3_ENABLED']);
  assert.deepEqual(enc.SMB_CURR_ENCRYPT_ALGORITHM, ['AES_128_GCM']);
  assert.deepEqual(enc.SESSION_RECONNECT_TIME, ['0:0']);
  const signed = parseStatshares(fixture('statshares-signed.txt'));
  const smb21 = parseStatshares(fixture('statshares-smb21.txt'));
  const ok = (stats, smb) => checkStatshares(stats, { ...SMB, ...smb }).ok;
  // encrypted, older smbutil
  assert.equal(ok(old, { version: '3.02' }), true);
  assert.equal(ok(old, { version: '3.0' }), true);
  assert.equal(ok(old, { version: '3' }), true);
  assert.equal(ok(old, { version: '3.1.1' }), false);
  // encrypted, newer smbutil
  for (const version of ['3', '3.0', '3.02', '3.1.1']) assert.equal(ok(enc, { version }), true, version);
  // OFF is not encrypted, but signed is enough when encryption is off
  assert.equal(ok(signed, {}), false);
  assert.equal(ok(signed, { encrypt: false }), true);
  // SMB 2 never passes
  for (const version of ['3', '3.0']) assert.equal(ok(smb21, { version, encrypt: false }), false);
  // nothing known: fail closed
  assert.equal(ok({}, { version: '3' }), false);
  assert.equal(ok({ SMB_VERSION: ['SMB_3.1.1'] }, { encrypt: false }), false);
  assert.equal(ok({ SMB_VERSION: ['SMB_3.1.1'] }, { encrypt: true }), false);
  assert.equal(ok({ SMB_VERSION: ['SMB_3.1.1'], SMB_CURR_ENCRYPT_ALGORITHM: ['OFF'] }, { encrypt: true }), false);
  assert.equal(ok({ SMB_VERSION: ['SMB_4.0'], SMB_CURR_ENCRYPT_ALGORITHM: ['AES_256_GCM'] }, { encrypt: true }), false, 'unknown dialects are refused');
  assert.deepEqual(checkStatshares(enc, SMB).warnings, []);
});

test('/sbin/mount parsing: spaces, " on " and "(" in paths, mounted by, stacked mounts', () => {
  const text = fixture('mount-output.txt');
  const all = parseDarwinMount(text);
  assert.equal(all.length, 12);
  assert.deepEqual(all[0], { source: '/dev/disk3s1s1', mountPoint: '/', fsType: 'apfs', options: ['sealed', 'local', 'read-only', 'journaled'], mountedBy: null });
  assert.deepEqual(parseMountLine('//other@nas/Share on /Volumes/Share (smbfs, nodev, nosuid, mounted by alice)'),
    { source: '//other@nas/Share', mountPoint: '/Volumes/Share', fsType: 'smbfs', options: ['nodev', 'nosuid'], mountedBy: 'alice' });
  assert.equal(parseMountLine('not a mount line'), null);
  const odd = findDarwinMount(text, '/Volumes/My on (Disk)');
  assert.equal(odd.source, '/dev/disk4s1');
  assert.equal(odd.fsType, 'apfs');
  const car = findDarwinMount(text, '/Library/Application Support/SentrySixManager/footage/car');
  assert.equal(car.source, '//tesla@nas2/Other', 'the last of stacked mounts wins');
  assert.equal(car.mountedBy, ME);
  assert.deepEqual(car.options, ['nodev', 'nosuid', 'noexec', 'nobrowse']);
  assert.equal(findDarwinMount(text, '/Library/Application Support/SentrySixManager/footage'), null);
  assert.equal(findDarwinMount(text, '/Library/Application Support/SentrySixManager/footage/ca'), null);
  assert.equal(findDarwinMount(text, '/Volumes/My'), null, 'a path that only starts the same way');
});

test('probe: finds the mount at a vehicle folder, cached for 2 s across vehicles', async (t) => {
  let clock = 1000;
  const { mounter, fr, footageRoot } = await setup(t, { now: () => clock });
  const real = fs.realpathSync(footageRoot);
  fr.addMount(path.join(real, 'car'));
  const p = await mounter.probe(mounter.target('car'));
  assert.deepEqual(p, { mountPoint: mounter.target('car'), fsType: 'smbfs', source: '//tesla@nas/TeslaCam', options: 'nodev,nosuid,noexec,nobrowse', mountedBy: ME });
  assert.equal(await mounter.probe(mounter.target('van')), null);
  assert.equal(fr.calls.filter((c) => c[0] === '/sbin/mount').length, 1, 'one /sbin/mount for both');
  clock += 2500;
  await mounter.probe(mounter.target('car'));
  assert.equal(fr.calls.filter((c) => c[0] === '/sbin/mount').length, 2);
  assert.deepEqual(fr.calls[0], ['/sbin/mount']);
  // a failing /sbin/mount is "nothing known", not a crash
  const broken = await setup(t, { run: { mountCode: 1 } });
  assert.equal(await broken.mounter.probe(broken.mounter.target('car')), null);
});

test('unmount: plain, or -f for lazy (macOS has no lazy detach)', async (t) => {
  const { mounter, fr, footageRoot } = await setup(t);
  const dir = path.join(fs.realpathSync(footageRoot), 'car');
  await mounter.unmount('car');
  await mounter.unmount('car', { lazy: true });
  assert.deepEqual(fr.calls, [['/sbin/umount', dir], ['/sbin/umount', '-f', dir]]);
});

test('forget: removes the nsmb.conf home and the empty mount point', async (t) => {
  const { mounter, runDir, footageRoot } = await setup(t);
  assert.equal((await mounter.mount('car', SMB)).code, 0);
  await mounter.forget('car');
  assert.equal(fs.existsSync(path.join(runDir, 'smb', 'car')), false);
  assert.equal(fs.existsSync(path.join(footageRoot, 'car')), false);
  // a folder with something in it stays
  fs.mkdirSync(path.join(footageRoot, 'van'));
  fs.writeFileSync(path.join(footageRoot, 'van', 'clip.mp4'), 'x');
  await mounter.forget('van');
  assert.equal(fs.existsSync(path.join(footageRoot, 'van', 'clip.mp4')), true);
});

test('sweep: force-unmounts only our smbfs mounts directly under the footage root', async (t) => {
  const { mounter, fr, footageRoot } = await setup(t);
  const real = fs.realpathSync(footageRoot);
  fr.state.table = fixture('mount-output.txt').replaceAll('/Library/Application Support/SentrySixManager/footage', real);
  const done = await mounter.sweep();
  assert.deepEqual(done.map((d) => d.mountPoint).sort(), [path.join(real, 'car'), path.join(real, 'car'), path.join(real, 'van')].sort());
  const umounts = fr.calls.filter((c) => c[0] === '/sbin/umount');
  assert.ok(umounts.every((c) => c[1] === '-f'));
  assert.equal(umounts.some((c) => c[2].endsWith('truck')), false, 'mounted by another user');
  assert.equal(umounts.some((c) => c[2].endsWith('inner')), false, 'not directly under the root');
  assert.equal(umounts.some((c) => c[2].startsWith('/Volumes')), false);
});

test('error texts: keyed on mount_smbfs messages first, sysexits only as a fallback', () => {
  const cases = [
    ['mount_smbfs: server rejected the connection: Authentication error', 77, /user name or password/],
    ['mount_smbfs: server connection failed: Connection refused', 68, /Could not reach/],
    ['mount_smbfs: server connection failed: Operation timed out', 68, /Could not reach/],
    ['mount_smbfs: server connection failed: No route to host', 68, /Could not reach/],
    ['mount_smbfs: server connection failed: Operation not supported', 68, /SMB 3/],
    ['mount_smbfs: share connection failed: No such file or directory', 68, /share was not found/],
    ['mount_smbfs: mount error: /x/car: File exists', 64, /busy/],
    ['mount_smbfs: mount error: /x/car: Resource busy', 64, /busy/],
    ['mount_smbfs: mount error: /x/car: Operation not permitted', 64, /folder the app's own user owns/],
    ['mount_smbfs: URL parsing failed, please correct the URL and try again: Invalid argument', 64, /could not be read/],
    ['mount_smbfs: could not find mount point /x/car: No such file or directory', 71, /mount point/],
    ["mount_smbfs: can't mount on /x/car: Not a directory", 71, /mount point/],
    // script(1) exited 0 although mount_smbfs failed: its own text wins over the check
    [`mount_smbfs: server rejected the connection: Authentication error\n${CHECK.notOurs}`, 1, /rejected the user name or password/],
    ['', 77, /user name or password/],
    ['', 68, /Could not reach/],
    ['', 71, /mount point/],
    [CHECK.dialect, 1, /older SMB version/],
    [CHECK.notEncrypted, 1, /encrypt/],
    [CHECK.notSigned, 1, /sign/],
    [CHECK.notOurs, 1, /safety check/],
    [CHECK.guest, 1, /guest/]
  ];
  for (const [text, code, re] of cases) assert.match(explainMountErrorDarwin(text, code) || '', re, text || String(code));
  assert.equal(explainMountErrorDarwin('', 0), null);
});
