'use strict';

// Kills the converter for real (SIGKILL, like docker kill or a power cut) at a
// named point of a conversion, for the crash-recovery tests
// (test/converterCrash*.test.js). Run as a script (env BASE, POINT, MODE,
// NETMODE, CONCURRENCY) it starts a job in BASE and dies at POINT; the test
// then starts the converter again on the same folders and checks the result.
//
// On Windows the converter uses winOwnedFs over the real SentryHelper.exe
// (test/fixtures/ownedFsAdapter.js), and dying is what the service's Job
// Object does to node.exe and its ffmpegs: everything the converter started
// is ended with taskkill /T /F, then the converter itself (TerminateProcess).
// Windows has no SIGUSR2 either, so the encode and verification points are
// taken where the converter starts that ffmpeg or ffprobe.

const fs = require('fs');
const { realTmpSync } = require('./tmp');
const path = require('path');
const crypto = require('crypto');
const assert = require('node:assert/strict');
const { spawn, spawnSync } = require('child_process');
const { writeNodeTool } = require('./nodeTool');
const { createOwnedFs, windowsHelperBuilt } = require('./ownedFsAdapter');
const { killTree, writeTool, toolSpawn, markKilled, exitSignal } = require('./processTree');


const REPO = path.join(__dirname, '..', '..');
const { createConverterRuntime } = require(path.join(REPO, 'src/main/converter/runtime'));
const { createJobStore } = require(path.join(REPO, 'src/main/converter/jobStore'));
const { createPosixOwnedFs, createNoReplaceRename } = require(path.join(REPO, 'server/posixOwnedFs'));
const { RETRYABLE } = require(path.join(REPO, 'server/converterHost'));

const HELPER = path.join(REPO, 'build', 'noreplace-rename');
const EVENT = path.join('TeslaCam', 'SentryClips', '2026-10-20_07-44-14');
const clipName = (index) => `2026-10-20_07-${String(40 + index).padStart(2, '0')}-00-front.mp4`;

const WINDOWS = process.platform === 'win32';
const available = () => spawnSync('ffmpeg', ['-version']).status === 0 && (WINDOWS ? windowsHelperBuilt() : fs.existsSync(HELPER));

// One Tesla-like H.264 clip, made once per test process and copied (each copy
// is a file of its own: its own inode, like the car's clips).
let template = null;
async function clipTemplate() {
  if (template && fs.existsSync(template)) return template;
  const { createTeslaH264Fixture } = require('./ffmpegFixtures');
  const folder = realTmpSync('sm-crash-clip-');
  template = path.join(folder, 'clip.mp4');
  await createTeslaH264Fixture({
    ffmpegPath: 'ffmpeg', outputPath: template, durationSeconds: 2, frameRate: 12, size: '320x240', spatialNoise: 4,
    telemetryPattern: Array.from({ length: 24 }, (_, n) => Buffer.from([0x20, n]))
  });
  process.once('exit', () => fs.rmSync(folder, { recursive: true, force: true }));
  return template;
}

async function makeBase(clips = 1) {
  const base = realTmpSync('sm-crash-');
  const folder = path.join(base, 'footage', EVENT);
  fs.mkdirSync(folder, { recursive: true });
  const source = await clipTemplate();
  for (let index = 0; index < clips; index += 1) fs.copyFileSync(source, path.join(folder, clipName(index)));
  for (const name of ['data', 'temp', 'out']) fs.mkdirSync(path.join(base, name));
  return { base, folder };
}

const sha256 = (file) => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const codecOf = (file) => String(spawnSync('ffprobe', ['-v', 'error', '-select_streams', 'v:0',
  '-show_entries', 'stream=codec_name', '-of', 'csv=p=0', file]).stdout).trim();

// The whole process group, ffmpeg included, as docker kill stops a container.
// Windows: every process the converter started (and what each started), then
// the converter; the mark tells crash() it died here (processTree.js).
const started = new Set();
function die() {
  if (WINDOWS) {
    markKilled(process.env.BASE);
    for (const pid of started) killTree(pid);
    process.kill(process.pid, 'SIGKILL');
  }
  try { process.kill(-process.pid, 'SIGKILL'); } catch (_) { /* not a group leader */ }
  process.kill(process.pid, 'SIGKILL');
}

// The kill points. Each names where the conversion is when the converter dies.
//  fsOpen(file, flags, handle): a file the converter opens (the telemetry
//    writer, copies to the share, the staged copy)
//  beforeWrite / afterWrite(manifest or patch): around a job-file write
//  onPromote / afterPromote(request): around a no-replace move
//  onRename(from, to): the native helper's moves (quarantine before a removal)
//  ffmpeg: a wrapper that kills the converter when the encode starts
const isTemp = (file) => /^TEMP-H265-/.test(path.basename(file));
const isPartial = (file) => /\.partial$/.test(file);
const isStaged = (file) => /-source\.mp4$/.test(file);
const itemsOf = (value) => Array.isArray(value?.items) ? value.items : [];
const copyField = (item) => (item.ownedPaths?.destinationPartial ? 'destinationPartial' : 'replacementCandidate');
const anyItem = (value, predicate) => itemsOf(value).some(predicate);

function halfWrite(handle) {
  const write = handle.write.bind(handle);
  handle.write = async (buffer, offset, length, position) => {
    if (typeof length === 'number' && length > 1) await write(buffer, offset, Math.floor(length / 2), position);
    die();
  };
  return handle;
}

const POINTS = {
  // several clips encoding at once, then fewer at once (one), then the stop
  lowered: { hold: true },
  // before the save slot
  'staging-copy': { fsOpen: (file, flags, handle) => (flags === 'wx' && isStaged(file) ? halfWrite(handle) : handle) },
  encode: { ffmpeg: true },
  writer: {
    fsOpen: (file, flags, handle) => {
      if (flags !== 'r+' || !/-encoded\.mp4$/.test(file)) return handle;
      const truncate = handle.truncate.bind(handle);
      handle.truncate = async (size) => { await truncate(size); die(); };
      return handle;
    }
  },
  verify: { ffprobeKill: /sentry-six-verification-/ },
  'temp-created': { fsOpen: (file, flags, handle) => { if (flags === 'wx' && (isTemp(file) || isPartial(file))) die(); return handle; } },
  'temp-torn': { fsOpen: (file, flags, handle) => (flags === 'wx' && (isTemp(file) || isPartial(file)) ? halfWrite(handle) : handle) },
  'temp-complete': {
    fsOpen: (file, flags, handle) => {
      if (flags !== 'wx' || !(isTemp(file) || isPartial(file))) return handle;
      const close = handle.close.bind(handle);
      handle.close = async () => { await close(); die(); };
      return handle;
    }
  },
  // the verified snapshot is about to be recorded for the save (it exists,
  // nothing records it yet), and just was (the copy not yet created)
  'snapshot-unrecorded': {
    beforeWrite: (patch, current) => anyItem(patch, (item) => item.ownedArtifacts?.[copyField(item)]?.state === 'intent'
      && !current.items.find((entry) => entry.id === item.id)?.ownedArtifacts?.[copyField(item)])
  },
  'save-recorded': {
    afterWrite: (manifest) => anyItem(manifest, (item) => item.ownedArtifacts?.[copyField(item)]?.state === 'intent' && !item.replacementPhase)
  },
  // that copy is being read back in full (it was written; not yet recorded
  // as complete)
  'temp-readback': {
    fsOpen: (file, flags, handle) => {
      if (flags !== 'r' || !(isTemp(file) || isPartial(file))) return handle;
      const read = handle.read.bind(handle);
      let reads = 0;
      handle.read = async (...args) => {
        if (++reads > 1) die();
        return read(...args);
      };
      return handle;
    }
  },
  // the clip's copy beside its original (or destination) is recorded as
  // complete: the clip waits for (or is about to take) the save slot
  'copy-recorded': {
    afterWrite: (manifest) => anyItem(manifest, (item) => ['owned', 'cleanup-pending'].includes(item.ownedArtifacts?.replacementCandidate?.state
      || item.ownedArtifacts?.destinationPartial?.state) && !item.replacementPhase && manifest.state !== 'copying')
  },
  // in the save slot
  'slot-entered': { afterWrite: (manifest) => manifest.replacementPhase === 'candidate-written' || manifest.state === 'copying' },
  'backup-renamed': { afterPromote: (request) => /^BACKUP-/.test(path.basename(request.to)) },
  'backup-recorded': { afterWrite: (manifest) => manifest.replacementPhase === 'original-backed-up' },
  'before-final': { onPromote: (request) => !/^BACKUP-/.test(path.basename(request.to)) && /front\.mp4$/.test(request.to) },
  'after-final': { afterPromote: (request) => !/^BACKUP-/.test(path.basename(request.to)) && /front\.mp4$/.test(request.to) },
  'final-recorded': { afterWrite: (manifest) => manifest.replacementPhase === 'candidate-promoted' },
  // the final rename fails, the swap puts the original back, and the
  // converter dies once that is recorded (before the job's record of the
  // replacement is cleared)
  'rolled-back': {
    failPromote: (request) => isTemp(request.from) && /front\.mp4$/.test(request.to),
    afterWrite: (manifest) => manifest.replacementPhase === 'backup-restored'
  },
  quarantined: { onRename: (from, to) => /^\.sentry-owned-remove-/.test(path.basename(to)) && /^BACKUP-/.test(path.basename(from)) },
  // the backup is gone; the clip's result is about to be written
  'before-result': {
    beforeWrite: (patch, current) => anyItem(patch, (item) => item.state === 'completed'
      && !current.items.find((entry) => entry.id === item.id && entry.state === 'completed'))
  },
  // the clip is recorded as converted; its private files are being removed
  'cleanup-unrecorded': {
    beforeWrite: (patch, current) => anyItem(patch, (item) => item.state === 'completed'
      && current.items.find((entry) => entry.id === item.id && entry.state === 'completed'
        && Object.keys(entry.ownedPaths || {}).length > 0)
      && Object.keys(item.ownedPaths || {}).length === 0)
  },
  'after-result': {
    afterWrite: (manifest, previous) => anyItem(manifest, (item) => item.state === 'completed'
      && !previous?.items.find((entry) => entry.id === item.id && entry.state === 'completed'))
  }
};

function proxiedFs(point) {
  if (!point.fsOpen) return fs;
  const promises = Object.create(fs.promises);
  promises.open = async (file, flags, mode) => point.fsOpen(String(file), flags, await fs.promises.open(file, flags, mode));
  const wrapped = Object.create(fs);
  Object.defineProperty(wrapped, 'promises', { value: promises });
  return wrapped;
}

// The real tool, after `body` (JavaScript, see nodeTool.js) had its say: body
// either ends the script itself or falls through to the real tool.
function toolWrapper(base, name, body) {
  return writeNodeTool(base, `${name}-crash.js`, `${body}\nexec(${JSON.stringify(name)}, args);`);
}

// The runtime as server/converterHost.js composes it, with the kill point's hooks.
async function open(base, { point = null, concurrency = 1, mode = 'replace', networkMode = 'direct', factories } = {}) {
  const hooks = point ? POINTS[point] : {};
  if (point && !hooks) throw new Error(`unknown kill point ${point}`);
  let owned;
  if (WINDOWS) {
    // the helper's no-replace moves, as noReplace.rename's below
    owned = createOwnedFs({ afterMove: (from, to) => { if (hooks.onRename?.(from, to)) die(); } });
  } else {
    process.env.SENTRY_NOREPLACE_RENAME = HELPER;
    const realRename = createNoReplaceRename();
    owned = createPosixOwnedFs({ noReplace: {
      available: realRename.available,
      rename: async (from, to) => {
        await realRename.rename(from, to);
        if (hooks.onRename?.(from, to)) die();
      }
    } });
  }
  const renameExclusive = Object.assign({}, owned.renameExclusive, {
    promote: async (request) => {
      if (hooks.onPromote?.(request)) die();
      if (hooks.failPromote?.(request)) throw Object.assign(new Error('refused'), { code: 'EPERM' });
      const out = await owned.renameExclusive.promote(request);
      if (hooks.afterPromote?.(request)) die();
      return out;
    }
  });
  const network = {
    isRetryable: (error) => RETRYABLE.has(error?.code),
    checkPath: async (target) => fs.promises.access(target).then(() => true, () => false)
  };
  if (!WINDOWS && (hooks.ffmpeg || hooks.ffprobeKill || hooks.hold)) process.on('SIGUSR2', die);
  // an encode (it reports on -progress) or the verification's probe signals
  // the converter (its parent) to die there, and fails itself
  const signalParent = (match) => `if (joined.includes(${JSON.stringify(match)})) { process.kill(parent, 'SIGUSR2'); process.exit(1); }`;
  // an encode waits until the test writes BASE/release, and gives up when the
  // converter (its parent) is gone; anything else runs at once. Gone means it
  // was re-parented or the pid it started under no longer answers, the way
  // sh's kill -0 $PPID saw it (the pid is the one read at start, not now).
  const holdUntilReleased = `until(() => {
  if (!joined.includes('-progress') || fs.existsSync(${JSON.stringify(path.join(base, 'release'))})) return true;
  if (process.ppid !== parent) process.exit(1);
  try { process.kill(parent, 0); } catch (_) { process.exit(1); }
  return false;
}, () => exec('ffmpeg', args));`;
  let ffmpegPath;
  let ffprobePath;
  let spawnTool = spawn;
  if (WINDOWS) {
    ffmpegPath = hooks.hold ? writeTool(base, 'ffmpeg-crash.js', holdUntilReleased) : 'ffmpeg';
    ffprobePath = 'ffprobe';
    // every process the converter starts is ended with it; an encode (it
    // reports on -progress) or the verification's probe is where those
    // points die, once it started
    const runTool = toolSpawn(spawn);
    spawnTool = (command, args = [], options) => {
      const child = runTool(command, args, options);
      if (child.pid) {
        started.add(child.pid);
        child.once('exit', () => started.delete(child.pid));
      }
      const joined = args.join(' ');
      if ((hooks.ffmpeg && command === ffmpegPath && joined.includes('-progress'))
        || (hooks.ffprobeKill && command === ffprobePath && joined.includes('sentry-six-verification-'))) die();
      return child;
    };
  } else {
    ffmpegPath = hooks.ffmpeg
      ? toolWrapper(base, 'ffmpeg', signalParent('-progress'))
      : hooks.hold
        ? writeNodeTool(base, 'ffmpeg-crash.js', holdUntilReleased)
        : 'ffmpeg';
    ffprobePath = hooks.ffprobeKill
      ? toolWrapper(base, 'ffprobe', signalParent('sentry-six-verification-')) : 'ffprobe';
  }
  const storeFactory = (options) => {
    const store = (factories?.createJobStore || createJobStore)(options);
    if (!hooks.beforeWrite && !hooks.afterWrite) return store;
    let last = null;
    return Object.freeze({
      ...store,
      transition: async (id, patch) => {
        if (hooks.beforeWrite && last && hooks.beforeWrite(patch, last)) die();
        const next = await store.transition(id, patch);
        if (hooks.afterWrite?.(next, last)) die();
        last = next;
        return next;
      },
      recover: async (...args) => {
        const next = await store.recover(...args);
        last = next;
        return next;
      }
    });
  };
  const runtime = await createConverterRuntime({
    userDataPath: fs.realpathSync.native(path.join(base, 'data')),
    tempPath: fs.realpathSync.native(path.join(base, 'temp')),
    ffmpegPath, ffprobePath, spawn: spawnTool, fs: proxiedFs(hooks), pathApi: path, platform: process.platform,
    removeOwned: owned.removeOwned, renameExclusive, network, autoConvert: false, gpuDecode: false,
    privateTempIdentityCleanup: true,
    networkBackoff: { initialMs: 50, maximumMs: 200 },
    factories: { ...(factories || {}), createJobStore: storeFactory }
  });
  const config = {
    sourceRoot: fs.realpathSync.native(path.join(base, 'footage', 'TeslaCam')),
    destinationRoot: mode === 'replace' ? null : fs.realpathSync.native(path.join(base, 'out')),
    codec: 'h265', profileId: 'custom', bitrateKbps: 250, networkMode, outputMode: mode,
    includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'], groupOrder: ['SavedClips', 'SentryClips', 'RecentClips'],
    replacementConfirmed: mode === 'replace', concurrency, verifyMode: 'quick'
  };
  return { runtime, config, renameExclusive };
}

// Runs a job in a child process (a process group of its own) that dies at the
// kill point; resolves once it died. `script` is the one to run (this file,
// or winCrashPoints.js).
function crash(base, { point, concurrency = 1, mode = 'replace', networkMode = 'direct', timeout = 120_000, script = __filename }) {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [script], {
      env: { ...process.env, BASE: base, POINT: point, CONCURRENCY: String(concurrency), MODE: mode, NETMODE: networkMode },
      detached: !WINDOWS, windowsHide: true, stdio: ['ignore', 'ignore', 'pipe']
    });
    let stderr = '';
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    const timer = setTimeout(() => {
      if (WINDOWS) killTree(child.pid);
      else try { process.kill(-child.pid, 'SIGKILL'); } catch (_) { /* gone */ }
    }, timeout);
    child.on('exit', (code, signal) => {
      clearTimeout(timer);
      resolve({ code, signal: exitSignal({ code, signal }, base), stderr });
    });
  });
}

// Everything the converter may leave behind that it should not.
function leftovers(base, folder) {
  const found = [];
  for (const name of fs.readdirSync(folder)) {
    if (/^(TEMP-|BACKUP-|\.sentry-owned-remove-)/.test(name) || name.endsWith('.partial')) found.push(name);
  }
  const out = path.join(base, 'out');
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const file = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(file);
      else if (entry.name.endsWith('.partial') || entry.name.startsWith('.sentry-owned-remove-')) found.push(path.relative(base, file));
    }
  };
  if (fs.existsSync(out)) walk(out);
  const temp = path.join(base, 'temp');
  for (const name of fs.readdirSync(temp)) {
    if (name === 'sentry-six-converter') {
      for (const inner of fs.readdirSync(path.join(temp, name))) found.push(`temp/${name}/${inner}`);
    } else if (name.startsWith('sentry-six-verification-')) found.push(`temp/${name}`);
  }
  return found;
}

// Waits for the restarted job to finish. A slow machine (a CI runner with
// every crash suite and their ffmpegs at once) can take minutes for what
// takes seconds here, so the job is waited for while it moves (its record
// or a running encode changes), and given up on once nothing has changed
// for STALL_MS (a converter that waits on something that never comes), or
// after WAIT_MS in all. Resolves why the
// wait ended: 'finished', 'stalled' or 'too long'.
const STALL_MS = 60_000;
const WAIT_MS = 240_000;
async function waitForJob(runtime, id, note = () => {}) {
  let finished = false;
  runtime.service.whenIdle(id).then(() => { finished = true; }, () => { finished = true; });
  const started = Date.now();
  let seenJob = null;
  let seenEncodes = null;
  let changed = started;
  while (!finished) {
    await new Promise((resolve) => setTimeout(resolve, 250));
    if (finished) break;
    const job = await runtime.service.get(id).catch(() => null);
    const jobNow = JSON.stringify([job?.revision, job?.state]);
    const encodesNow = JSON.stringify(typeof runtime.activeEncodes === 'function' ? runtime.activeEncodes() : []);
    if (jobNow !== seenJob && job) {
      note(`job ${job.state} revision ${job.revision} phase ${job.replacementPhase || '-'}: `
        + job.items.map((item) => `${path.basename(item.relativePath).slice(11, 19)} ${item.state}${item.failure?.code ? `/${item.failure.code}` : ''}`).join(', '));
    }
    if (jobNow !== seenJob || encodesNow !== seenEncodes) {
      seenJob = jobNow;
      seenEncodes = encodesNow;
      changed = Date.now();
    } else if (Date.now() - changed > STALL_MS) {
      return 'stalled';
    }
    if (Date.now() - started > WAIT_MS) return 'too long';
  }
  return 'finished';
}

// Dies at the point, starts again on the same folders, waits for the job.
// Returns what a test checks: the job, each clip's codec (and whether a
// separate run kept the original), and anything left behind.
async function crashAndRecover({ point, mode = 'replace', networkMode = 'direct', concurrency = 1, clips = concurrency, beforeStart }) {
  const { base, folder } = await makeBase(clips);
  const names = fs.readdirSync(folder).sort();
  const before = Object.fromEntries(names.map((name) => [name, sha256(path.join(folder, name))]));
  const crashStarted = Date.now();
  const died = await crash(base, { point, concurrency, mode, networkMode });
  let env;
  try {
    env = await open(base, { concurrency, mode, networkMode });
    if (beforeStart) await beforeStart(env);
    // what the restarted converter did, for a test that fails
    const restartStarted = Date.now();
    const timeline = [`the crash took ${restartStarted - crashStarted} ms`];
    const note = (text) => {
      if (timeline.length < 400) timeline.push(`+${((Date.now() - restartStarted) / 1000).toFixed(1)}s ${text}`);
    };
    env.runtime.service.subscribe((event) => {
      if (/snapshot|progress/i.test(event.type)) return;
      note(`event ${event.type} ${event.itemId || ''} ${event.code || event.status || event.reason || ''}`.trim());
    });
    note('restart');
    await env.runtime.start();
    const [listed] = await env.runtime.service.list();
    const outcome = await waitForJob(env.runtime, listed.id, note);
    note(`wait over: ${outcome}`);
    const job = await env.runtime.service.get(listed.id);
    const clipsAfter = names.map((name) => {
      const output = mode === 'replace' ? path.join(folder, name) : path.join(base, 'out', 'SentryClips', path.basename(path.dirname(path.join(folder, name))), name);
      return {
        name,
        codec: fs.existsSync(output) ? codecOf(output) : null,
        originalKept: fs.existsSync(path.join(folder, name)) && sha256(path.join(folder, name)) === before[name]
      };
    });
    return { died, job, timeline, clips: clipsAfter, leftovers: leftovers(base, folder), names: fs.readdirSync(folder).sort(), expectedNames: names };
  } finally {
    await env?.runtime.shutdown().catch(() => {});
    fs.rmSync(base, { recursive: true, force: true });
  }
}

function assertRecovered(result, { mode = 'replace' } = {}) {
  assert.equal(result.died.signal, 'SIGKILL', `the converter died at the point (${result.died.stderr.slice(0, 300)})`);
  // (what each clip was doing, when the job had not finished)
  const clipStates = () => result.job.items.map((item) => `${item.relativePath}: ${item.state} ${item.resultCode || ''}`
    + ` ${item.failure?.code || ''} attempts ${item.attempts || 0}`).join('; ');
  assert.equal(result.job.state, 'completed', `finished by itself: ${JSON.stringify(result.job.recovery || null)} (${clipStates()})`
    + `\n${(result.timeline || []).join('\n')}`);
  assert.equal(result.job.recovery ?? null, null);
  for (const item of result.job.items) {
    assert.equal(item.state, 'completed', `${item.relativePath}: ${item.state} ${item.resultCode || ''}`);
    assert.equal(item.resultCode, 'converted');
  }
  for (const clip of result.clips) {
    assert.equal(clip.codec, 'hevc', `${clip.name} holds its conversion`);
    if (mode === 'separate') assert.ok(clip.originalKept, `${clip.name}: the original is untouched`);
  }
  assert.deepEqual(result.names, result.expectedNames, 'only the clips are in the folder');
  assert.deepEqual(result.leftovers, [], 'nothing left behind');
}

module.exports = { POINTS, open, crash, crashAndRecover, waitForJob, assertRecovered, makeBase, leftovers, available, clipName, sha256, codecOf, HELPER, EVENT };

if (require.main === module) {
  (async () => {
    const env = await open(process.env.BASE, {
      point: process.env.POINT,
      concurrency: Number(process.env.CONCURRENCY || 1),
      mode: process.env.MODE || 'replace',
      networkMode: process.env.NETMODE || 'direct'
    });
    await env.runtime.start();
    const id = await env.runtime.service.start(env.config);
    if (process.env.POINT === 'lowered') {
      while (env.runtime.activeEncodes().length < env.config.concurrency) await new Promise((r) => setTimeout(r, 20));
      await env.runtime.service.setConcurrency(id, 1);
      die();
    }
    // A point the run never reached: report it (the test fails on a clean exit).
    await env.runtime.service.whenIdle(id);
    process.stderr.write('kill point not reached\n');
    process.exit(3);
  })().catch((error) => {
    process.stderr.write(`${error?.stack || error}\n`);
    process.exit(2);
  });
}
