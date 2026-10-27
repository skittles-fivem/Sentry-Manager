'use strict';

// "Keep clips that can't reach the size target" (opt-in): a clip that still
// misses its size profile after the size retry is kept, as long as it is
// smaller than the original and passed every other check. Off by default.

const test = require('node:test');
const assert = require('node:assert/strict');
const { createHarness, CPU } = require('./fixtures/lanesHarness');

// a 4 MB clip, Balanced (20% smaller, 2% tolerance): 3.6 MB is too big
const SOURCE = 4_000_000;
const OFF_BYTES = 3_600_000; // 10% smaller, target 20%

// Stands in for the verifier: a size miss is reported as it really is
// (failure without the option; ok with the marker with it).
function sizeMissVerifier(calls, { other = null, notSmaller = false } = {}) {
  return async (options) => {
    calls.push(options);
    if (other) return { ok: false, reason: other, outputBytes: OFF_BYTES, retryable: false };
    // (no size target for custom bitrate: smaller is enough)
    if (options.profileId !== 'balanced') return { ok: true, outputBytes: OFF_BYTES, publicationArtifact: null };
    if (notSmaller) return { ok: false, reason: 'not-smaller', outputBytes: SOURCE + 1, retryable: false };
    const sizeDetails = { profileId: 'balanced', targetBytes: SOURCE * 0.8, toleranceBytes: SOURCE * 0.02 };
    if (options.keepOffTarget !== true) {
      return { ok: false, reason: 'profile-size', retryable: true, sourceBytes: SOURCE, outputBytes: OFF_BYTES, details: sizeDetails };
    }
    return {
      ok: true, reason: null, outputBytes: OFF_BYTES, publicationArtifact: null,
      details: { ...sizeDetails, offTarget: true, offTargetSavedPercent: 10, offTargetTargetPercent: 20 }
    };
  };
}

function harness(verifier) {
  return createHarness({
    clips: 1,
    devices: [CPU],
    defaultDevice: null,
    gpuDecode: false,
    works: (encoder) => encoder.startsWith('lib'),
    copyAsset: async ({ destination }) => ({ finalPath: destination, outputBytes: 4_000_000, ownedPaths: {}, ownedArtifacts: {} }),
    verifier
  });
}

const balanced = (extra = {}) => ({ profileId: 'balanced', bitrateKbps: undefined, ...extra });

async function run(verifier, extra) {
  const h = harness(verifier);
  try {
    const id = await h.service.start(h.config(balanced(extra)));
    const job = await h.finishJob(id);
    const [item] = job.items.filter((entry) => entry.kind === 'video');
    return { item, encodes: h.encodes().length };
  } finally {
    await h.close();
  }
}

test('option off: a clip off its size target still fails profile-size after its retry', async () => {
  const calls = [];
  const { item, encodes } = await run(sizeMissVerifier(calls));
  assert.equal(item.state, 'failed');
  assert.equal(item.failure?.code, 'profile-size');
  assert.equal(encodes, 2, 'one size retry');
  assert.ok(calls.every((options) => options.keepOffTarget === undefined), 'the verifier is not asked to keep it');
  assert.equal(item.offTarget, undefined);
});

test('option on: the size retry still happens first, then the clip is kept and marked', async () => {
  const calls = [];
  const { item, encodes } = await run(sizeMissVerifier(calls), { keepOffTarget: true });
  assert.equal(encodes, 2, 'retried once at a lower bitrate before keeping it');
  assert.equal(item.state, 'completed');
  assert.equal(item.resultCode, 'converted');
  assert.deepEqual(item.offTarget, { savedPercent: 10, targetPercent: 20 });
  assert.equal(item.outputBytes, OFF_BYTES);
  assert.ok(calls.every((options) => options.keepOffTarget === true));
});

test('option on: a clip that is not smaller than the original still fails', async () => {
  const { item } = await run(sizeMissVerifier([], { notSmaller: true }), { keepOffTarget: true });
  assert.equal(item.state, 'failed');
  assert.equal(item.failure?.code, 'not-smaller');
});

test('option on: a telemetry (or any other) failure still fails', async () => {
  const { item } = await run(sizeMissVerifier([], { other: 'telemetry-mismatch' }), { keepOffTarget: true });
  assert.equal(item.state, 'failed');
  assert.equal(item.failure?.code, 'telemetry-mismatch');
  assert.equal(item.offTarget, undefined);
});

test('the job store keeps the setting only for a size profile and refuses a wrong type', async () => {
  const h = harness(sizeMissVerifier([]));
  try {
    const kept = await h.service.start(h.config(balanced({ keepOffTarget: true })));
    assert.equal((await h.finishJob(kept)).config.keepOffTarget, true);
    const off = await h.service.start(h.config(balanced({ keepOffTarget: false })));
    assert.equal('keepOffTarget' in (await h.finishJob(off)).config, false, 'off is stored as absent');
    await assert.rejects(() => h.service.start(h.config(balanced({ keepOffTarget: 'yes' }))));
    // custom bitrate has no size target: not stored
    const custom = await h.service.start(h.config({ keepOffTarget: true }));
    assert.equal('keepOffTarget' in (await h.finishJob(custom)).config, false);
  } finally {
    await h.close();
  }
});

test('IPC: start and automatic settings validate the setting', async () => {
  const fs = require('node:fs');
  const os = require('node:os');
  const path = require('node:path');
  const { registerConverterIpc } = require('../src/main/converter/ipc');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-offtarget-ipc-'));
  for (const name of ['source', 'out']) fs.mkdirSync(path.join(dir, name));
  const handlers = new Map();
  const started = [];
  const configured = [];
  const registration = registerConverterIpc({
    ipcMain: { handle: (channel, fn) => handlers.set(channel, fn), removeHandler: (channel) => handlers.delete(channel) },
    dialog: { showOpenDialog: async () => ({ canceled: true }) },
    service: { subscribe: () => () => {}, start: async (config) => { started.push(config); return 'job-1'; }, list: async () => [] },
    capabilities: { replacement: 'available' },
    auto: { configure: async (settings) => { configured.push(settings); return { settings }; } }
  });
  const invoke = (name, payload) => handlers.get(`converter:${name}`)({ sender: null }, payload);
  const config = (over = {}) => ({
    sourceRoot: path.join(dir, 'source'), destinationRoot: path.join(dir, 'out'), codec: 'h265', profileId: 'balanced',
    networkMode: 'direct', outputMode: 'separate', includedGroups: ['RecentClips'], replacementConfirmed: false, ...over
  });
  try {
    let out = await invoke('start', config({ keepOffTarget: true }));
    assert.equal(out.ok, true, JSON.stringify(out));
    assert.equal(started.at(-1).keepOffTarget, true);
    out = await invoke('start', config({ keepOffTarget: false }));
    assert.equal(out.ok, true);
    out = await invoke('start', config());
    assert.equal(out.ok, true, 'without the field, as before');
    assert.equal('keepOffTarget' in started.at(-1), false);
    for (const bad of [config({ keepOffTarget: 'true' }), config({ keepOffTarget: 1 }),
      config({ profileId: 'custom', bitrateKbps: 2500, keepOffTarget: true }),
      config({ profileId: 'quality', qualityLevel: 'balanced', keepOffTarget: true })]) {
      assert.equal((await invoke('start', bad)).error?.code, 'INVALID_KEEP_OFF_TARGET', JSON.stringify(bad));
    }
    out = await invoke('setAuto', { enabled: true, intervalMinutes: 60, window: null, keepRunning: false, openAtLogin: false, config: config({ keepOffTarget: true }) });
    assert.equal(out.ok, true, JSON.stringify(out));
    assert.equal(configured.at(-1).config.keepOffTarget, true, 'saved with the vehicle\'s automatic settings');
    out = await invoke('setAuto', { enabled: true, intervalMinutes: 60, window: null, keepRunning: false, openAtLogin: false, config: config({ keepOffTarget: 'x' }) });
    assert.equal(out.error?.code, 'INVALID_KEEP_OFF_TARGET');
  } finally {
    registration.unregister();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('verifier: a size miss fails without the option; with it, it is ok and marked (integrity-only checks)', async () => {
  const fs = require('node:fs');
  const os = require('node:os');
  const path = require('node:path');
  const { verifyCandidate } = require('../src/main/converter/verifier');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-offtarget-verify-'));
  const source = path.join(dir, 'source.mp4');
  const candidate = path.join(dir, 'candidate.mp4');
  const bigger = path.join(dir, 'bigger.mp4');
  fs.writeFileSync(source, Buffer.alloc(1_000_000, 1));
  fs.writeFileSync(candidate, Buffer.alloc(900_000, 2)); // 10% smaller, Balanced wants 20%
  fs.writeFileSync(bigger, Buffer.alloc(1_000_001, 3));
  const never = async () => { throw new Error('not used'); };
  const verify = (file, extra = {}) => verifyCandidate({
    source, candidate: file, codec: 'h265', profileId: 'balanced', runFfprobe: never, runFullDecode: never, verifyMode: 'off', ...extra
  });
  const cleanup = (result) => { if (result?.publicationArtifact?.path) fs.rmSync(path.dirname(result.publicationArtifact.path), { recursive: true, force: true }); };
  try {
    const missed = await verify(candidate);
    cleanup(missed);
    assert.equal(missed.ok, false);
    assert.equal(missed.reason, 'profile-size');
    const kept = await verify(candidate, { keepOffTarget: true });
    cleanup(kept);
    assert.equal(kept.ok, true, JSON.stringify(kept));
    assert.equal(kept.details.offTarget, true);
    assert.equal(kept.details.offTargetSavedPercent, 10);
    assert.equal(kept.details.offTargetTargetPercent, 20);
    const notSmaller = await verify(bigger, { keepOffTarget: true });
    cleanup(notSmaller);
    assert.equal(notSmaller.ok, false);
    assert.equal(notSmaller.reason, 'not-smaller');
    const wrongType = await verify(candidate, { keepOffTarget: 'yes' });
    assert.equal(wrongType.ok, false);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
