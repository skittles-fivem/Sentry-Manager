'use strict';

// SMB shares on Windows (server/platform/smbWin32.js and the parts of
// share.js, smbTest.js and vehicles.js that use it). Runs on any OS: the
// helper is a fake that answers ops from a table, and the platform is
// injected.

const test = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('events');
const { PassThrough } = require('stream');
const fs = require('fs');
const fsp = require('fs/promises');
const os = require('os');
const path = require('path');
const { createWinMounter, securityProblem, scrub } = require('../server/platform/smbWin32');
const { winConnection, dialectAtLeast, dialectParts } = require('../server/mountSpec');
const { ShareManager, SMB_HINTS, explainSmbError, explainMountError } = require('../server/share');
const { createSmbTester } = require('../server/smbTest');
const { VehicleManager, findClipRoot, findClipRootAsync } = require('../server/vehicles');
const { shouldSkipDir } = require('../server/footage');

const PASSWORD = 'pa$$w0rd-Very-Secret';
const SMB = { type: 'smb', share: '//nas/TeslaCam/car', username: 'tesla', password: PASSWORD, domain: '', version: '3.1.1', encrypt: true };
const GOOD = { connected: true, dialect: '3.1.1', encrypted: true, signed: true };

// a minimal helper: request(op, args) answered from `handlers`, every call recorded
function fakeHelper(handlers = {}) {
  const calls = [];
  const connected = new Set();
  const defaults = {
    'smb.connect': (a) => { connected.add(a.unc.toLowerCase()); return {}; },
    'smb.disconnect': (a) => {
      if (!connected.delete(a.unc.toLowerCase())) throw Object.assign(new Error('The network connection could not be found.'), { code: 'ENOENT', winerr: 2250 });
      return {};
    },
    'smb.status': (a) => (connected.has(`\\\\${a.server}\\${a.share}`.toLowerCase()) ? GOOD : { connected: false, dialect: null, encrypted: null, signed: null }),
    'fs.identity': () => ({ fsName: 'NTFS' })
  };
  return {
    calls,
    connected,
    async request(op, args, opts) {
      calls.push({ op, args, opts });
      const h = handlers[op] || defaults[op];
      if (!h) throw Object.assign(new Error(`no handler for ${op}`), { code: 'EIO' });
      return h(args, defaults);
    }
  };
}

function captureLog() {
  const lines = [];
  const at = (level) => (...a) => lines.push(`${level} ${a.join(' ')}`);
  const log = { lines, info: at('info'), warn: at('warn'), error: at('error'), debug: at('debug') };
  log.scope = () => log;
  return log;
}

const ops = (helper) => helper.calls.map((c) => c.op);

test('mountSpec: the UNC, the folder below it and the account for Windows', () => {
  const c = winConnection(SMB);
  assert.equal(c.unc, '\\\\nas\\TeslaCam');
  assert.equal(c.root, '\\\\nas\\TeslaCam\\car');
  assert.equal(c.source, '//nas/TeslaCam');
  assert.equal(c.account, 'tesla');
  assert.equal(winConnection({ ...SMB, domain: 'HOME' }).account, 'HOME\\tesla');
  assert.equal(winConnection({ ...SMB, port: 445 }).unc, '\\\\nas\\TeslaCam', 'port 445 is fine');
  assert.equal(winConnection({ ...SMB, share: '//[fe80::1]/x' }).unc, '\\\\fe80--1.ipv6-literal.net\\x');
  assert.throws(() => winConnection({ ...SMB, port: 1445 }), (err) => err.code === 'INVALID_SMB' && /port 1445 is not supported on Windows/.test(err.message) && /445 only/.test(err.message));
});

test('mountSpec: dialects compare like versions ("3.02" is 3.0.2)', () => {
  assert.deepEqual(dialectParts('3.02'), [3, 0, 2]);
  assert.deepEqual(dialectParts('3.1.1'), [3, 1, 1]);
  assert.ok(dialectAtLeast('3.1.1', '3.02'));
  assert.ok(dialectAtLeast('3.02', '3.0'));
  assert.ok(!dialectAtLeast('3.0', '3.02'));
  assert.ok(!dialectAtLeast('2.1', '3.0'));
  assert.ok(!dialectAtLeast(null, '3.0'));
});

test('mount: disconnects a leftover connection, connects, checks the protection; target and probe show the share', async () => {
  const helper = fakeHelper();
  helper.connected.add('\\\\nas\\teslacam'); // left over from an earlier run
  const m = createWinMounter({ helper, log: captureLog() });
  const res = await m.mount('car', SMB);
  assert.equal(res.code, 0);
  assert.deepEqual(ops(helper), ['smb.disconnect', 'smb.connect', 'smb.status']);
  const connect = helper.calls[1].args;
  assert.deepEqual({ ...connect, password: '-' }, { unc: '\\\\nas\\TeslaCam', user: 'tesla', password: '-' });
  assert.equal(connect.password, PASSWORD, 'the password goes to the helper (its stdin) only');
  assert.deepEqual(helper.calls[2].args, { server: 'nas', share: 'TeslaCam' });
  assert.equal(m.target('car'), '\\\\nas\\TeslaCam\\car');
  assert.equal(m.target('other', { ...SMB, share: '//nas/b' }), '\\\\nas\\b');
  assert.deepEqual(await m.probe('\\\\NAS\\teslacam\\car'), { mountPoint: '\\\\NAS\\teslacam\\car', fsType: 'smb3', source: '//nas/TeslaCam', options: '' });
  // the NAS dropped: no longer mounted, so ShareManager connects again
  helper.connected.clear();
  assert.equal(await m.probe('\\\\nas\\TeslaCam\\car'), null);
  assert.equal(await m.probe('\\\\nas\\elsewhere'), null, 'a share this mounter does not hold is not mounted');
  assert.equal((await m.probe('\\\\nas\\elsewhere', { mode: 'local' })).fsType, 'smb3', 'a folder vehicle on a UNC path is a network share');
  assert.equal((await m.probe('D:\\TeslaCam', { mode: 'local' })).fsType, 'ntfs');
  assert.equal(m.target.length >= 1, true);
});

test('connections are shared per share and account: the last vehicle to let go disconnects', async () => {
  const helper = fakeHelper();
  const m = createWinMounter({ helper, log: captureLog() });
  assert.equal((await m.mount('a', { ...SMB, share: '//nas/TeslaCam/a' })).code, 0);
  assert.equal((await m.mount('b', { ...SMB, share: '//NAS/teslacam/b' })).code, 0);
  assert.equal(ops(helper).filter((o) => o === 'smb.connect').length, 1, 'one connection for both');
  await m.unmount('a');
  assert.ok(helper.connected.has('\\\\nas\\teslacam'), 'b still uses it');
  assert.equal(await m.probe('\\\\nas\\TeslaCam\\a'), null, 'a no longer counts as mounted');
  assert.ok(await m.probe('\\\\nas\\TeslaCam\\b'));
  await m.unmount('b');
  assert.equal(helper.connected.size, 0);
  assert.equal(ops(helper).filter((o) => o === 'smb.disconnect').length, 2, 'the startup clean-up and the last unmount');
  // forget after a failed or no mount is harmless
  await m.forget('a');
  await m.forget('never');
  assert.throws(() => m.target('a'), /No share is known/);
});

test('a second account on the same server is refused with the one-account hint (Windows error 1219)', async () => {
  const helper = fakeHelper();
  const m = createWinMounter({ helper, log: captureLog() });
  assert.equal((await m.mount('a', SMB)).code, 0);
  const res = await m.mount('b', { ...SMB, share: '//nas/Other', username: 'someone' });
  assert.equal(res.code, -1);
  assert.match(res.output, /one account per server/);
  assert.match(res.hint, /same account for every vehicle on this NAS/);
  assert.match(res.hint, /IP address for one vehicle/);
  assert.equal(ops(helper).filter((o) => o === 'smb.connect').length, 1, 'never tried');
  // Windows says so itself when the other connection is not ours (a mapped drive)
  const h2 = fakeHelper({ 'smb.connect': () => { throw Object.assign(new Error('Multiple connections to a server or shared resource by the same user ... are not allowed.'), { code: 'ESESSION', winerr: 1219 }); } });
  const r2 = await createWinMounter({ helper: h2, log: captureLog() }).mount('a', SMB);
  assert.equal(r2.code, -1);
  assert.match(r2.hint, /one account per server/);
});

test('protection weaker than the vehicle asks for is disconnected and refused with Docker\'s hint', async () => {
  const cases = [
    [{ ...GOOD, encrypted: false }, SMB, SMB_HINTS.encryption],
    [{ ...GOOD, dialect: '2.1' }, SMB, SMB_HINTS.noSmb3],
    [{ ...GOOD, dialect: '3.0' }, SMB, /choose SMB 3\.0 for this vehicle/],
    [{ ...GOOD, encrypted: false, signed: false }, { ...SMB, encrypt: false }, /does not sign/],
    [{ ...GOOD, dialect: null }, SMB, null]
  ];
  for (const [status, smb, hint] of cases) {
    const helper = fakeHelper({ 'smb.status': () => status });
    const res = await createWinMounter({ helper, log: captureLog() }).mount('car', smb);
    assert.equal(res.code, -1, JSON.stringify(status));
    if (hint instanceof RegExp) assert.match(res.hint, hint);
    else assert.equal(res.hint, hint);
    assert.equal(helper.connected.size, 0, 'disconnected again');
    assert.equal(ops(helper).at(-1), 'smb.disconnect');
  }
  // what is fine: encryption off but signed; 3.02 for "3"; encrypted without the signed flag
  assert.equal(securityProblem({ version: '3.1.1', encrypt: false }, { ...GOOD, encrypted: false }, 'u'), null);
  assert.equal(securityProblem({ version: '3', encrypt: true }, { ...GOOD, dialect: '3.02' }, 'u'), null);
  assert.equal(securityProblem({ version: '3.02', encrypt: true }, { ...GOOD, signed: false }, 'u'), null);
  // Docker's own hints did not change
  assert.equal(explainMountError('mount error(95): x'), SMB_HINTS.encryption);
  assert.match(explainMountError('mount error(113): x'), /from the Docker host\.$/);
  assert.match(explainMountError('mount error(13): Permission denied'), /AppArmor/);
});

test('ports other than 445 are refused before anything is sent to the helper', async () => {
  const helper = fakeHelper();
  const res = await createWinMounter({ helper, log: captureLog() }).mount('car', { ...SMB, port: 139 });
  assert.equal(res.code, -1);
  assert.match(res.output, /port 139 is not supported on Windows/);
  assert.equal(helper.calls.length, 0);
});

test('helper errors come with Windows hints in Docker\'s words', () => {
  assert.match(explainSmbError({ code: 'EAUTH', winerr: 1326 }), /^Permission denied: the server rejected the user name or password \(check them and the Domain field\)\.$/);
  assert.equal(explainSmbError({ code: 'EHOSTUNREACH', winerr: 67 }), SMB_HINTS.notFound);
  assert.match(explainSmbError({ code: 'EHOSTUNREACH', winerr: 53 }), /port 445 is open from this PC\.$/);
  assert.match(explainSmbError({ code: 'EGUEST', winerr: 1272 }), /guest/);
  assert.match(explainSmbError({ code: 'EHELPER' }), /SentryHelper\.exe/);
  assert.equal(explainSmbError({ code: 'EIO', winerr: 9999 }), null);
});

test('the password never appears in a log line, a result, a hint or a thrown error', async () => {
  const leaky = (args) => { throw Object.assign(new Error(`logon failed for ${args.user} with ${args.password}`), { code: 'EAUTH', winerr: 1326 }); };
  const helper = fakeHelper({ 'smb.connect': leaky, 'smb.test': leaky });
  const log = captureLog();
  const m = createWinMounter({ helper, log });
  const res = await m.mount('car', SMB);
  assert.equal(res.code, -1);
  assert.match(res.output, /\*\*\*/);
  const err = await m.test(SMB).then(() => null, (e) => e);
  assert.ok(err);
  assert.equal(err.code, 'EAUTH');
  assert.equal(err.winerr, 1326);
  const tester = createSmbTester({ mounter: m, vehicles: { get: () => null }, log, platform: 'win32' });
  const t = await tester.test(SMB);
  assert.equal(t.stage, 'connect');
  assert.match(t.hint, /user name or password/);
  await m.mount('car', { ...SMB, port: 1 });
  for (const text of [...log.lines, JSON.stringify(res), err.message, err.stack, JSON.stringify(t)]) {
    assert.ok(!String(text).includes(PASSWORD), `leaked: ${text}`);
  }
  assert.equal(scrub(`a ${PASSWORD} b ${PASSWORD}`, PASSWORD), 'a *** b ***');
  // only the two ops that need it carry it, as request arguments (the helper's stdin)
  for (const c of helper.calls) {
    if (!['smb.connect', 'smb.test'].includes(c.op)) assert.ok(!JSON.stringify(c).includes(PASSWORD), c.op);
    assert.ok(!JSON.stringify(c.opts || {}).includes(PASSWORD));
  }
});

// With the real helper client (server/platform/winHelper.js, from the
// platform layer): spawned with no password on its command line or in its
// environment. Skipped while that file is not in this tree.
test('through the helper client: the password is only ever written to the helper\'s stdin', async (t) => {
  let createWinHelper;
  try {
    ({ createWinHelper } = require('../server/platform/winHelper'));
  } catch (err) {
    if (err.code === 'MODULE_NOT_FOUND') return t.skip('server/platform/winHelper.js is not in this tree yet');
    throw err;
  }
  const spawns = [];
  let stdinText = '';
  const spawn = (file, args, options) => {
    spawns.push({ file, args, env: options?.env || null });
    const child = new EventEmitter();
    child.stdin = new PassThrough();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    child.pid = 4242;
    child.kill = () => true;
    child.stdin.on('data', (chunk) => {
      stdinText += chunk;
      for (const line of String(chunk).split('\n').filter(Boolean)) {
        const req = JSON.parse(line);
        const reply = req.op === 'smb.connect'
          ? { id: req.id, ok: false, code: 'EAUTH', winerr: 1326, message: `bad password ${req.args.password}` }
          : { id: req.id, ok: true, result: {} };
        child.stdout.write(`${JSON.stringify(reply)}\n`);
      }
    });
    setImmediate(() => child.stdout.write(`${JSON.stringify({ id: 0, ok: true, result: { op: 'hello', version: 1, pid: 4242 } })}\n`));
    return child;
  };
  const log = captureLog();
  const helper = createWinHelper({ path: path.join(os.tmpdir(), 'SentryHelper.exe'), spawn, log, timeoutMs: 5000 });
  try {
    const res = await createWinMounter({ helper, log }).mount('car', SMB);
    assert.equal(res.code, -1);
    assert.ok(stdinText.includes(PASSWORD), 'sent over stdin');
    assert.ok(spawns.length >= 1);
    for (const s of spawns) {
      assert.ok(!JSON.stringify(s.args || []).includes(PASSWORD), 'not in argv');
      assert.ok(!JSON.stringify(s.env || process.env).includes(PASSWORD), 'not in the environment');
    }
    for (const text of [...log.lines, res.output]) assert.ok(!String(text).includes(PASSWORD), `leaked: ${text}`);
  } finally {
    helper.close();
  }
});

test('ShareManager takes an injected probe instead of /proc/self/mountinfo, and the mounter\'s own hint', async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'smb-win-share-'));
  try {
    let connected = false;
    let fail = null;
    const calls = [];
    const mounter = {
      async mount(id) { calls.push(['mount', id]); if (fail) return fail; connected = true; return { code: 0, output: '' }; },
      async unmount(id) { calls.push(['unmount', id]); connected = false; return { code: 0, output: '' }; },
      async forget(id) { calls.push(['forget', id]); },
      target: () => dir,
      probe: async (root, { mode }) => (connected && mode === 'smb' && root === dir ? { mountPoint: root, fsType: 'smb3', source: '//nas/TeslaCam', options: '' } : null)
    };
    const share = new ShareManager({ config: { footagePath: dir, smb: SMB }, log: captureLog(), mounter, id: 'car', platform: 'win32', mountinfoPath: path.join(dir, 'no-such-file') });
    const st = await share.ensure({ forceWriteCheck: true });
    assert.deepEqual(calls, [['mount', 'car']]);
    assert.equal(st.mounted, true);
    assert.equal(st.fsType, 'smb3');
    assert.equal(st.source, '//nas/TeslaCam');
    assert.equal(st.network, true);
    assert.equal(st.healthy, true);
    assert.equal(st.writable, true);
    await share.stop();
    assert.deepEqual(calls.slice(1), [['unmount', 'car'], ['forget', 'car']]);

    // a failed connection: the mounter's hint is shown as it is
    connected = false;
    fail = { code: -1, output: 'The connection is not encrypted.', hint: SMB_HINTS.encryption };
    const s2 = new ShareManager({ config: { footagePath: dir, smb: SMB }, log: captureLog(), mounter, id: 'car', platform: 'win32' });
    const st2 = await s2.ensure();
    assert.equal(st2.lastError, 'Mount failed: The connection is not encrypted.');
    assert.equal(st2.hint, SMB_HINTS.encryption);
    assert.equal(st2.healthy, false);
    await s2.stop();
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('a folder vehicle on Windows gets no "container disk" warning and a Windows hint', async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'smb-win-local-'));
  try {
    const share = new ShareManager({ config: { footagePath: dir }, log: captureLog(), platform: 'win32', probe: async () => null });
    const st = await share.ensure();
    assert.equal(st.healthy, true);
    assert.equal(st.warning, null);
    const gone = new ShareManager({ config: { footagePath: path.join(dir, 'missing') }, log: captureLog(), platform: 'win32', probe: async () => null });
    const st2 = await gone.ensure();
    assert.doesNotMatch(st2.hint, /volumes|SMB_SHARE/);
    await share.stop();
    await gone.stop();
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('connection test on Windows: smb.test as the typed account, same answer shape, footage found by listing', async () => {
  const listings = {
    '': ['Archive', 'notes.txt', '#recycle'],
    'car': ['Archive', 'notes.txt', '#recycle'],
    'car\\Archive': ['TeslaCam'],
    'car\\Archive\\TeslaCam': ['RecentClips', 'SavedClips']
  };
  const helper = fakeHelper({
    'smb.test': (a) => {
      const entries = listings[a.folder || ''];
      if (!entries) throw Object.assign(new Error('not a folder'), { code: 'ENOENT', winerr: 3 });
      return { readable: true, writable: true, entries, dialect: '3.1.1', encrypted: true, signed: true };
    }
  });
  const m = createWinMounter({ helper, log: captureLog() });
  const tester = createSmbTester({ mounter: m, vehicles: { get: () => null }, log: captureLog(), platform: 'win32' });
  const r = await tester.test(SMB);
  assert.equal(r.ok, true);
  assert.equal(r.entries, 3);
  assert.equal(r.writable, true);
  assert.equal(r.writeProblem, null);
  assert.equal(r.footage, 'Archive/TeslaCam');
  assert.equal(typeof r.ms, 'number');
  assert.equal(helper.calls[0].args.write, true);
  assert.equal(helper.calls[0].args.folder, 'car');
  assert.ok(helper.calls.slice(1).every((c) => c.args.write === false));
  assert.ok(!ops(helper).includes('smb.connect'), 'nothing is connected for a test');

  // read-only account
  const ro = createWinMounter({ helper: fakeHelper({ 'smb.test': () => ({ readable: true, writable: false, entries: ['RecentClips'] }) }), log: captureLog() });
  const r2 = await createSmbTester({ mounter: ro, vehicles: { get: () => null }, log: captureLog(), platform: 'win32' }).test(SMB);
  assert.equal(r2.ok, true);
  assert.equal(r2.footage, '.');
  assert.match(r2.writeProblem, /^Read-only for this account: writing a file failed/);
  assert.doesNotMatch(r2.writeProblem, /PUID/);

  // a port Windows cannot use: refused as a detail, nothing sent
  const h3 = fakeHelper();
  const r3 = await createSmbTester({ mounter: createWinMounter({ helper: h3, log: captureLog() }), vehicles: { get: () => null }, log: captureLog(), platform: 'win32' }).test({ ...SMB, port: 1445 });
  assert.equal(r3.stage, 'details');
  assert.match(r3.error, /445/);
  assert.equal(h3.calls.length, 0);
});

function memDb(vehicles = []) {
  const data = { vehicles };
  return { get: (k, d) => (k in data ? data[k] : d), set: (k, v) => { data[k] = v; } };
}

test('vehicles on Windows: an SMB vehicle\'s folder is \\\\host\\share\\folder, names follow validName, shares may not overlap', async () => {
  const helper = fakeHelper();
  const mounter = createWinMounter({ helper, log: captureLog() });
  const reserved = (seg) => !/^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?$/i.test(seg) && !/[:<>"|?*\\]|[. ]$/.test(seg);
  const vm = new VehicleManager({ db: memDb(), footageRoot: path.join(os.tmpdir(), 'none'), logger: captureLog(), mounter, platform: 'win32', validName: reserved });
  vm.startShare = () => null; // no background connections in this test
  const a = vm.add({ name: 'Con', source: SMB });
  assert.equal(a.id, 'con-2', 'CON is no folder name on Windows');
  assert.equal(vm.rootOf(a), '\\\\nas\\TeslaCam\\car');
  assert.deepEqual(vm.roots(), [{ path: '\\\\nas\\TeslaCam\\car', within: ['\\\\nas\\TeslaCam\\car'] }]);
  assert.throws(() => vm.add({ name: 'B', source: { ...SMB, share: '//NAS/teslacam/car/sub' } }), /overlaps the footage of Con/);
  assert.throws(() => vm.add({ name: 'B', source: { ...SMB, share: '//nas/teslacam' } }), /overlaps/);
  const b = vm.add({ name: 'B', source: { ...SMB, share: '//nas/TeslaCam/other' } });
  assert.equal(vm.rootOf(b), '\\\\nas\\TeslaCam\\other');
  assert.throws(() => vm.add({ name: 'C', source: { ...SMB, share: '//nas/TeslaCam/aux' } }), /cannot be a share or folder name on Windows/);
  assert.throws(() => vm.add({ name: 'C', source: { ...SMB, share: '//nas/TeslaCam/bad.' } }), /cannot be a share or folder name/);
  // a saved vehicle keeps working; renaming it does not re-check its share
  await vm.update(b.id, { name: 'B2' });
  // on Linux nothing of this applies
  const linux = new VehicleManager({ db: memDb(), footageRoot: '/footage', logger: captureLog(), mounter, platform: 'linux', validName: () => true });
  linux.startShare = () => null;
  const l = linux.add({ name: 'Car', source: { ...SMB, share: '//nas/TeslaCam/aux' } });
  assert.equal(linux.rootOf(l), path.join(path.resolve('/footage'), 'car'));
});

test('studioRoot on Windows never walks the share on the request path', async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'smb-win-studio-'));
  try {
    await fsp.mkdir(path.join(dir, 'TeslaCam', 'RecentClips'), { recursive: true });
    const vm = new VehicleManager({ db: memDb(), footageRoot: dir, folderRoots: [dir], logger: captureLog(), platform: 'win32', validName: () => true });
    vm.startShare = () => null;
    const v = { id: 'car', name: 'Car', source: { type: 'path', path: dir } };
    assert.equal(vm.studioRoot(v), dir, 'the root while the lookup runs');
    assert.equal(await vm.studioRootAsync(v), path.join(dir, 'TeslaCam'));
    assert.equal(vm.studioRoot(v), path.join(dir, 'TeslaCam'), 'then the folder found');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('findClipRootAsync finds what findClipRoot finds, and gives up on a share that does not answer', async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'smb-win-clips-'));
  try {
    for (const p of ['direct/RecentClips', 'deeper/Archive/TeslaCam/SavedClips', 'two/a/SentryClips', 'two/TeslaCam/SentryClips', 'empty/x']) {
      await fsp.mkdir(path.join(dir, p), { recursive: true });
    }
    for (const name of ['direct', 'deeper', 'two', 'empty', 'missing']) {
      assert.equal(await findClipRootAsync(path.join(dir, name)), findClipRoot(path.join(dir, name)), name);
    }
    const hang = () => new Promise(() => {});
    assert.equal(await findClipRootAsync(dir, { readdir: hang, timeoutMs: 20 }), null);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Windows system folders at the top of a drive are skipped there only', () => {
  assert.equal(shouldSkipDir('Recovery', { platform: 'win32' }), true);
  assert.equal(shouldSkipDir('RECYCLER', { platform: 'win32' }), true);
  assert.equal(shouldSkipDir('Recovery', { platform: 'linux' }), false);
  assert.equal(shouldSkipDir('$RECYCLE.BIN', { platform: 'linux' }), true);
  assert.equal(shouldSkipDir('TeslaCam', { platform: 'win32' }), false);
});
