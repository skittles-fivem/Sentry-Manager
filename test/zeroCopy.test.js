'use strict';

// Zero-copy decode for VA-API and QSV (Linux) and AMF and QSV (Windows):
// the decoded frames stay on the GPU that encodes them
// (docs/plans/windows.md, section 8). The arguments are exactly the
// contract's; only 8-bit 4:2:0 sources with even dimensions and no held
// lead-in frames take the tier; a zero-copy failure falls back to GPU
// decoding and then the CPU, and turns zero-copy off for that GPU and codec
// only after the third time; the first zero-copy clip per GPU and codec has
// its pictures checked against the original's.
//
// No GPU is needed: the arguments are asserted, and the converter runs with
// an ffmpeg that is a small node script (or a fake child) failing whichever
// tier the test says.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const childProcess = require('child_process');
const { EventEmitter } = require('events');
const { createJobStore } = require('../src/main/converter/jobStore');
const { createConverterJobService, createZeroCopyState } = require('../src/main/converter/jobService');
const { createEncoderPlanner, buildEncodeArgs, decodeAccelArgs, zeroCopySource } = require('../src/main/converter/encoderPlanner');
const { fakeProbe, sleep, ARC, CPU } = require('./fixtures/lanesHarness');

const AMD_RENDER = Object.freeze({ id: 'pci:0000:c5:00.0', kind: 'dri', vendor: 'amd', name: 'AMD Radeon 780M', node: '/dev/dri/renderD128', index: null, pci: '0000:c5:00.0' });
const WIN_AMD = Object.freeze({ id: 'pci:0000:03:00.0', kind: 'd3d11', vendor: 'amd', name: 'AMD Radeon RX 7600', node: null, index: null, pci: '0000:03:00.0', adapter: 1, luid: '0x13b10' });
const WIN_INTEL = Object.freeze({ id: 'pci:0000:00:02.0', kind: 'd3d11', vendor: 'intel', name: 'Intel UHD Graphics 770', node: null, index: null, pci: '0000:00:02.0', adapter: 2, luid: '0x14c22' });
const TESLA = Object.freeze({ codec: 'h264', durationSeconds: 2, containerReserveBytes: 0, telemetryReserveBytes: 0, pixFmt: 'yuv420p', width: 1280, height: 960 });
const TAIL = ['-tag:v', 'hvc1', '-fps_mode', 'passthrough', '-start_at_zero', '-avoid_negative_ts', 'make_zero', '-use_editlist', '0'];

// The input and encode arguments a zero-copy encode puts around `-i in`.
const zeroCopyLine = (encoder, platform, device) => [
  ...decodeAccelArgs(encoder, { platform, zeroCopy: true, device }),
  '-i', 'in',
  ...buildEncodeArgs({ codec: encoder.startsWith('av1') ? 'av1' : 'h265', encoder, bitrate: 1_000_000, hwFrames: true, device })
];

test('zero-copy arguments are exactly the contract\'s, per encoder and system', () => {
  assert.deepEqual(zeroCopyLine('hevc_vaapi', 'linux', AMD_RENDER), [
    '-hwaccel', 'vaapi', '-hwaccel_device', '/dev/dri/renderD128', '-hwaccel_output_format', 'vaapi',
    '-i', 'in', '-c:v', 'hevc_vaapi', '-b:v', '1000000', '-bf', '0', ...TAIL
  ]);
  assert.deepEqual(zeroCopyLine('hevc_qsv', 'linux', ARC), [
    '-init_hw_device', 'vaapi=va:/dev/dri/renderD129', '-init_hw_device', 'qsv=qs@va', '-filter_hw_device', 'qs',
    '-hwaccel', 'vaapi', '-hwaccel_device', 'va', '-hwaccel_output_format', 'vaapi',
    '-i', 'in', '-vf', 'hwmap=derive_device=qsv,format=qsv', '-c:v', 'hevc_qsv', '-b:v', '1000000', '-bf', '0', ...TAIL
  ]);
  assert.deepEqual(zeroCopyLine('hevc_amf', 'win32', WIN_AMD), [
    '-init_hw_device', 'd3d11va=dx:1', '-hwaccel', 'd3d11va', '-hwaccel_device', 'dx', '-hwaccel_output_format', 'd3d11',
    '-extra_hw_frames', '16',
    '-i', 'in', '-c:v', 'hevc_amf', '-b:v', '1000000', '-rc', 'vbr_peak', '-bf', '0', ...TAIL
  ]);
  assert.deepEqual(zeroCopyLine('hevc_qsv', 'win32', WIN_INTEL), [
    '-init_hw_device', 'd3d11va=dx:2', '-init_hw_device', 'qsv=qs@dx', '-filter_hw_device', 'qs',
    '-hwaccel', 'd3d11va', '-hwaccel_device', 'dx', '-hwaccel_output_format', 'd3d11', '-extra_hw_frames', '8',
    '-i', 'in', '-vf', 'hwmap=derive_device=qsv,format=qsv', '-c:v', 'hevc_qsv', '-b:v', '1000000', '-bf', '0', ...TAIL
  ]);
  // AV1 takes the same input; no VA-API device and no upload on Linux
  const av1 = zeroCopyLine('av1_vaapi', 'linux', AMD_RENDER);
  assert.equal(av1.includes('-vaapi_device'), false);
  assert.equal(av1.some((arg) => arg.includes('hwupload')), false);
  // without a chosen GPU: VA-API and QSV on renderD128 as before, AMF on the
  // default adapter; Windows QSV picks its own GPU, so no zero-copy there
  assert.deepEqual(decodeAccelArgs('hevc_vaapi', { platform: 'linux', zeroCopy: true }),
    ['-hwaccel', 'vaapi', '-hwaccel_device', '/dev/dri/renderD128', '-hwaccel_output_format', 'vaapi']);
  assert.deepEqual(decodeAccelArgs('hevc_amf', { platform: 'win32', zeroCopy: true }).slice(0, 2), ['-init_hw_device', 'd3d11va=dx']);
  assert.equal(decodeAccelArgs('hevc_qsv', { platform: 'win32', zeroCopy: true }), null);
  // never across systems: no AMF on Linux, no VA-API on Windows
  assert.equal(decodeAccelArgs('hevc_amf', { platform: 'linux', zeroCopy: true }), null);
  assert.equal(decodeAccelArgs('hevc_vaapi', { platform: 'win32', zeroCopy: true }), null);
  // nothing Linux-only in a Windows line
  for (const line of [zeroCopyLine('hevc_amf', 'win32', WIN_AMD), zeroCopyLine('av1_qsv', 'win32', WIN_INTEL)]) {
    assert.ok(!line.some((arg) => arg.includes('/dev/')));
  }
});

test('Windows GPU decode and encode without zero-copy name the chosen adapter', () => {
  assert.deepEqual(decodeAccelArgs('hevc_amf', { platform: 'win32', device: WIN_AMD }), ['-hwaccel', 'd3d11va', '-hwaccel_device', '1']);
  assert.deepEqual(decodeAccelArgs('hevc_qsv', { platform: 'win32', device: WIN_INTEL }), ['-hwaccel', 'd3d11va', '-hwaccel_device', '2']);
  assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_qsv', bitrate: 1e6, device: WIN_INTEL }).slice(0, 4),
    ['-init_hw_device', 'qsv=qsvenc:hw_any,child_device_type=d3d11va,child_device=2', '-c:v', 'hevc_qsv']);
  assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_amf', bitrate: 1e6, device: WIN_AMD }).slice(0, 8),
    ['-init_hw_device', 'd3d11va=amfenc:1', '-filter_hw_device', 'amfenc', '-vf', 'format=nv12,hwupload', '-c:v', 'hevc_amf']);
  // a lead-in hold runs before the upload
  assert.equal(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_amf', bitrate: 1e6, device: WIN_AMD, holdFrames: 3 })[5],
    'tpad=start=3:start_mode=clone,format=nv12,hwupload');
  // NVENC on Windows as on Linux
  const rtx = { id: 'pci:0000:01:00.0', kind: 'nvidia', index: 0 };
  assert.deepEqual(decodeAccelArgs('hevc_nvenc', { platform: 'win32', device: rtx, zeroCopy: true }),
    ['-hwaccel', 'cuda', '-hwaccel_output_format', 'cuda', '-hwaccel_device', '0']);
  assert.throws(() => decodeAccelArgs('hevc_amf', { platform: 'win32', device: { ...WIN_AMD, adapter: -1 } }), RangeError);
});

test('only 8-bit 4:2:0 sources with even dimensions may stay on the GPU', () => {
  assert.equal(zeroCopySource(TESLA), true);
  assert.equal(zeroCopySource({ ...TESLA, pixFmt: 'yuvj420p' }), true);
  for (const bad of [null, {}, { ...TESLA, pixFmt: 'yuv420p10le' }, { ...TESLA, pixFmt: 'yuv422p' }, { ...TESLA, pixFmt: null },
    { ...TESLA, width: 1279 }, { ...TESLA, height: 961 }, { ...TESLA, width: null }, { ...TESLA, height: 0 }]) {
    assert.equal(zeroCopySource(bad), false, JSON.stringify(bad));
  }
});

// A converter job service over the real job store and planner, whose ffmpeg
// is `spawnProcess` (a fake child, or a real node script).
// (encodeMs: how long an encode with these arguments takes; none by default)
function createRig({ platform = 'linux', device, works, behave, encodeMs, video = TESLA, timeline = { frames: [] }, comparePicture, spawnProcess, clips = 1, concurrency = 1, zeroCopyState }) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-zerocopy-'));
  const store = createJobStore({ rootDir: path.join(dir, 'jobs'), fs: fs.promises });
  const advertised = ['libx265', 'hevc_vaapi', 'hevc_qsv', 'hevc_amf', 'hevc_nvenc'];
  const probe = fakeProbe(works);
  const planner = createEncoderPlanner({
    runProbe: async (command, args, options) => (args.includes('-encoders')
      ? { status: 0, stdout: advertised.map((name) => ` V..... ${name}  x`).join('\n'), stderr: '' }
      : probe.runProbe(command, args, options)),
    platform,
    ffmpegPath: 'ffmpeg'
  });
  const spawned = [];
  const compared = [];
  let pid = 2000;
  const fakeSpawn = (command, args) => {
    const child = new EventEmitter();
    child.stdout = new EventEmitter();
    child.stderr = new EventEmitter();
    child.pid = (pid += 1);
    let done = false;
    const finish = (code, signal = null) => {
      if (done) return;
      done = true;
      setImmediate(() => child.emit('close', code, signal));
    };
    child.kill = (signal) => finish(null, signal || 'SIGTERM');
    spawned.push({ command, args });
    const code = behave ? behave(args) : 0;
    const ms = encodeMs ? encodeMs(args) : 0;
    const end = () => {
      if (code === 0) child.stdout.emit('data', 'fps=100\nprogress=end\n');
      finish(code);
    };
    if (ms > 0) setTimeout(end, ms);
    else setImmediate(end);
    return child;
  };
  const record = (spawn) => (command, args, options) => {
    if (spawn === fakeSpawn) return spawn(command, args, options);
    spawned.push({ command, args });
    return spawn(command, args, options);
  };
  const owned = { ownedPaths: {}, ownedArtifacts: {} };
  fs.mkdirSync(path.join(dir, 'work'), { recursive: true });
  const outputOf = (itemId) => path.join(dir, 'work', `${itemId}-encoded.mp4`);
  const service = createConverterJobService({
    store,
    scanner: async function* scanner(config) {
      for (let index = 0; index < clips; index += 1) {
        const name = `2026-10-20_07-10-${String(index).padStart(2, '0')}-front.mp4`;
        yield {
          kind: 'video', group: 'RecentClips', eventId: null,
          relativePath: `RecentClips/${name}`, sourcePath: path.join(config.sourceRoot, 'RecentClips', name),
          sourceBytes: 4_000_000, mtimeMs: 1_700_000_000_000 + index, timestampKey: name.slice(0, 19), camera: 'front'
        };
      }
    },
    planner,
    stagingManager: {
      reserveEncodeOutput: async ({ itemId }) => ({ path: outputOf(itemId), ...owned }),
      adoptEncodeOutput: async ({ itemId }) => ({ path: outputOf(itemId), ...owned }),
      copyAsset: async () => { throw Object.assign(new Error('no assets here'), { code: 'UNEXPECTED' }); },
      publishSeparate: async ({ destination }) => ({ finalPath: destination, outputBytes: 1_000_000, ...owned }),
      replaceOriginal: async () => { throw Object.assign(new Error('no replace here'), { code: 'UNEXPECTED' }); },
      cleanupOwnedArtifacts: async () => ({ cleaned: true })
    },
    ensureDestinationParent: async () => ({ revalidate: async () => {} }),
    inspectVideo: async () => ({ ...video }),
    readTimeline: async () => timeline,
    writers: { h265: async () => {}, av1: async () => {} },
    verifier: async () => ({ ok: true, outputBytes: 1_000_000, publicationArtifact: null }),
    spawnProcess: record(spawnProcess || fakeSpawn),
    ffmpegPath: 'ffmpeg',
    decodeAccel: (encoder, accel = {}) => decodeAccelArgs(encoder, {
      platform, zeroCopy: accel.zeroCopy === true, ...(accel.device ? { device: accel.device } : {})
    }),
    resolveDevice: async (id) => [device, CPU].find((entry) => entry.id === id) || null,
    defaultDevice: async () => null,
    encodeStallMs: 60_000,
    ...(zeroCopyState ? { zeroCopyState } : {}),
    ...(comparePicture ? { comparePicture: async (request) => { compared.push(request); return comparePicture(request); } } : {})
  });
  const config = () => ({
    sourceRoot: path.join(dir, 'footage'), destinationRoot: path.join(dir, 'out'),
    codec: 'h265', profileId: 'custom', bitrateKbps: 250, networkMode: 'direct', outputMode: 'separate',
    includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'], groupOrder: ['SavedClips', 'SentryClips', 'RecentClips'],
    replacementConfirmed: false, concurrency: 1, verifyMode: 'quick', lanes: [{ device: device.id, concurrency }]
  });
  async function run() {
    const id = await service.start(config());
    await Promise.race([service.whenIdle(id), sleep(20_000).then(() => { throw new Error('the job did not finish'); })]);
    return service.get(id);
  }
  // (the encodes only: the planner's probes go through runProbe)
  const encodes = () => spawned.filter((entry) => entry.args.includes('pipe:1'));
  const tierOf = (args) => (args.includes('-hwaccel_output_format') ? 'zero-copy' : args.includes('-hwaccel') ? 'gpu' : 'cpu');
  async function close() {
    await service.shutdown().catch(() => {});
    fs.rmSync(dir, { recursive: true, force: true });
  }
  return { dir, service, run, encodes, tierOf, compared, close };
}

const encoderOf = (args) => args[args.indexOf('-c:v') + 1];
const vaapiWorks = (encoder) => encoder === 'hevc_vaapi' || encoder.startsWith('lib');

test('a VA-API lane encodes a Tesla clip zero-copy, with exactly the contract\'s line', async () => {
  const rig = createRig({ device: AMD_RENDER, works: vaapiWorks });
  try {
    assert.equal((await rig.run()).state, 'completed');
    const [encode] = rig.encodes();
    const at = encode.args.indexOf('-i');
    assert.deepEqual(encode.args.slice(5, at), ['-hwaccel', 'vaapi', '-hwaccel_device', '/dev/dri/renderD128', '-hwaccel_output_format', 'vaapi']);
    assert.deepEqual(encode.args.slice(at + 2, at + 6), ['-c:v', 'hevc_vaapi', '-b:v', '250000']);
    assert.equal(encode.args.includes('-vaapi_device'), false);
  } finally {
    await rig.close();
  }
});

test('10-bit, odd-sized, uninspected or lead-in clips decode the way they always did', async () => {
  const cases = [
    { video: { ...TESLA, pixFmt: 'yuv420p10le' } },
    { video: { ...TESLA, width: 1281 } },
    { video: { codec: 'h264', durationSeconds: 2, containerReserveBytes: 0, telemetryReserveBytes: 0 } },
    { timeline: { frames: [], leadIn: { frames: 5, hold: 5, syncSampleIndex: 5, reason: null, timescale: 10_000, durationTicks: Array(40).fill(417) } } }
  ];
  for (const entry of cases) {
    const rig = createRig({ device: AMD_RENDER, works: vaapiWorks, ...entry });
    try {
      assert.equal((await rig.run()).state, 'completed');
      const [encode] = rig.encodes();
      assert.equal(rig.tierOf(encode.args), 'gpu', JSON.stringify(entry));
      assert.deepEqual(encode.args.slice(5, 9), ['-hwaccel', 'vaapi', '-hwaccel_device', '/dev/dri/renderD128']);
      assert.ok(encode.args.includes('-vaapi_device'), 'today\'s upload');
    } finally {
      await rig.close();
    }
  }
});

test('a failing zero-copy tier falls back to GPU decoding; zero-copy goes off for that GPU only after the third time', async () => {
  const rig = createRig({ device: AMD_RENDER, works: vaapiWorks, clips: 5, behave: (args) => (args.includes('-hwaccel_output_format') ? 1 : 0) });
  try {
    const job = await rig.run();
    assert.equal(job.state, 'completed');
    assert.equal(job.items.filter((item) => item.state === 'completed').length, 5);
    const tiers = rig.encodes().map((entry) => rig.tierOf(entry.args));
    assert.deepEqual(tiers, ['zero-copy', 'gpu', 'zero-copy', 'gpu', 'zero-copy', 'gpu', 'gpu', 'gpu']);
    // retrying with a slower decode is not a new attempt
    assert.ok(job.items.every((item) => item.attempts === 1));
  } finally {
    await rig.close();
  }
});

test('zero-copy and GPU decoding both failing: the CPU encoder takes the clip', async () => {
  const rig = createRig({ device: AMD_RENDER, works: vaapiWorks, behave: (args) => (encoderOf(args) === 'hevc_vaapi' ? 1 : 0) });
  try {
    assert.equal((await rig.run()).state, 'completed');
    assert.deepEqual(rig.encodes().map((entry) => `${rig.tierOf(entry.args)}:${encoderOf(entry.args)}`),
      ['zero-copy:hevc_vaapi', 'gpu:hevc_vaapi', 'cpu:hevc_vaapi', 'cpu:libx265']);
  } finally {
    await rig.close();
  }
});

test('the first zero-copy clip per GPU and codec has its pictures checked; a match is trusted from then on', async () => {
  const rig = createRig({ device: AMD_RENDER, works: vaapiWorks, clips: 3, comparePicture: async () => ({ ok: true, mean: 0.98, low: 0.95 }) });
  try {
    assert.equal((await rig.run()).state, 'completed');
    assert.deepEqual(rig.encodes().map((entry) => rig.tierOf(entry.args)), ['zero-copy', 'zero-copy', 'zero-copy']);
    assert.equal(rig.compared.length, 1);
    assert.match(rig.compared[0].candidate, /-encoded\.mp4$/);
    assert.match(rig.compared[0].source, /front\.mp4$/);
  } finally {
    await rig.close();
  }
});

test('a zero-copy clip whose pictures do not match is encoded again with GPU decoding, and zero-copy stays off on that GPU', async () => {
  const rig = createRig({ device: AMD_RENDER, works: vaapiWorks, clips: 3, comparePicture: async () => ({ ok: false, mean: 0.2, low: 0.1 }) });
  try {
    const job = await rig.run();
    assert.equal(job.state, 'completed');
    assert.deepEqual(rig.encodes().map((entry) => rig.tierOf(entry.args)), ['zero-copy', 'gpu', 'gpu', 'gpu']);
    assert.equal(rig.compared.length, 1);
  } finally {
    await rig.close();
  }
  // a check that could not decode counts as one failure, not a verdict
  const undecided = createRig({ device: AMD_RENDER, works: vaapiWorks, clips: 2, comparePicture: async () => ({ ok: false, reason: 'decode-failed' }) });
  try {
    assert.equal((await undecided.run()).state, 'completed');
    assert.deepEqual(undecided.encodes().map((entry) => undecided.tierOf(entry.args)), ['zero-copy', 'gpu', 'zero-copy', 'gpu']);
  } finally {
    await undecided.close();
  }
});

test('NVENC zero-copy is not picture-checked (unchanged); its failures count to three like the others', async () => {
  const RTX = Object.freeze({ id: 'pci:0000:01:00.0', kind: 'nvidia', vendor: 'nvidia', name: 'RTX', node: null, index: 0, pci: '0000:01:00.0' });
  const rig = createRig({
    device: RTX, clips: 4, video: { codec: 'h264', durationSeconds: 2, containerReserveBytes: 0, telemetryReserveBytes: 0 },
    works: (encoder) => encoder === 'hevc_nvenc' || encoder.startsWith('lib'),
    behave: (args) => (args.includes('-hwaccel_output_format') ? 1 : 0),
    comparePicture: async () => ({ ok: true })
  });
  try {
    assert.equal((await rig.run()).state, 'completed');
    assert.deepEqual(rig.encodes().map((entry) => rig.tierOf(entry.args)), ['zero-copy', 'gpu', 'zero-copy', 'gpu', 'zero-copy', 'gpu', 'gpu']);
    assert.equal(rig.compared.length, 0);
  } finally {
    await rig.close();
  }
});

test('Windows: an AMD lane encodes zero-copy on its own adapter, Intel QSV maps the frames', async () => {
  for (const [device, encoder, expected] of [
    [WIN_AMD, 'hevc_amf', ['-init_hw_device', 'd3d11va=dx:1', '-hwaccel', 'd3d11va', '-hwaccel_device', 'dx', '-hwaccel_output_format', 'd3d11', '-extra_hw_frames', '16']],
    [WIN_INTEL, 'hevc_qsv', ['-init_hw_device', 'd3d11va=dx:2', '-init_hw_device', 'qsv=qs@dx', '-filter_hw_device', 'qs',
      '-hwaccel', 'd3d11va', '-hwaccel_device', 'dx', '-hwaccel_output_format', 'd3d11', '-extra_hw_frames', '8']]
  ]) {
    const rig = createRig({ platform: 'win32', device, works: (name) => name === encoder || name.startsWith('lib'), behave: (args) => (args.includes('-hwaccel_output_format') ? 1 : 0) });
    try {
      assert.equal((await rig.run()).state, 'completed');
      const [first, second] = rig.encodes();
      const at = first.args.indexOf('-i');
      assert.deepEqual(first.args.slice(5, at), expected);
      assert.equal(encoderOf(first.args), encoder);
      // then GPU decoding on the same adapter
      assert.deepEqual(second.args.slice(5, 9), ['-hwaccel', 'd3d11va', '-hwaccel_device', String(device.adapter)]);
      for (const entry of rig.encodes()) assert.ok(!entry.args.some((arg) => arg.includes('/dev/')));
    } finally {
      await rig.close();
    }
  }
});

// A real process standing in for ffmpeg: it fails any zero-copy line, any
// line whose encoder is in FAIL_ENCODERS, and otherwise writes a tiny output
// file and reports progress as ffmpeg does.
const FAKE_FFMPEG = `'use strict';
const fs = require('fs');
const args = process.argv.slice(2);
const encoder = args[args.indexOf('-c:v') + 1];
const failing = (process.env.FAIL_ENCODERS || '').split(',');
if (args.includes('-hwaccel_output_format') || failing.includes(encoder)) {
  process.stderr.write('Failed to set up the hardware frames\\n');
  process.exit(1);
}
fs.writeFileSync(args[args.length - 1], Buffer.alloc(64));
process.stdout.write('fps=100\\nprogress=end\\n');
`;

test('with a real (fake) ffmpeg process: zero-copy fails, then GPU decoding, then the CPU encodes', async () => {
  const scripts = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-zerocopy-ffmpeg-'));
  const script = path.join(scripts, 'ffmpeg.js');
  fs.writeFileSync(script, FAKE_FFMPEG);
  const runWith = async (failEncoders) => {
    const spawnProcess = (_command, args, options) => childProcess.spawn(process.execPath, [script, ...args], {
      ...options, env: { ...process.env, FAIL_ENCODERS: failEncoders }
    });
    const rig = createRig({ device: AMD_RENDER, works: vaapiWorks, spawnProcess });
    try {
      assert.equal((await rig.run()).state, 'completed');
      return rig.encodes().map((entry) => `${rig.tierOf(entry.args)}:${encoderOf(entry.args)}`);
    } finally {
      await rig.close();
    }
  };
  try {
    assert.deepEqual(await runWith(''), ['zero-copy:hevc_vaapi', 'gpu:hevc_vaapi']);
    assert.deepEqual(await runWith('hevc_vaapi'), ['zero-copy:hevc_vaapi', 'gpu:hevc_vaapi', 'cpu:hevc_vaapi', 'cpu:libx265']);
  } finally {
    fs.rmSync(scripts, { recursive: true, force: true });
  }
});

test('one zero-copy picture check at a time per GPU and codec; the clips that finish meanwhile wait for its verdict', async () => {
  // three workers finish their first clips together: one check, not three
  let release;
  const gate = new Promise((resolve) => { release = resolve; });
  const rig = createRig({
    device: AMD_RENDER, works: vaapiWorks, clips: 3, concurrency: 3,
    comparePicture: async () => { setTimeout(release, 100); await gate; return { ok: true }; }
  });
  try {
    assert.equal((await rig.run()).state, 'completed');
    assert.deepEqual(rig.encodes().map((entry) => rig.tierOf(entry.args)), ['zero-copy', 'zero-copy', 'zero-copy']);
    assert.equal(rig.compared.length, 1);
  } finally {
    await rig.close();
  }
  // a mismatch found while the others waited sends every one of them back to GPU decoding
  const mismatch = createRig({
    device: AMD_RENDER, works: vaapiWorks, clips: 3, concurrency: 3,
    comparePicture: async () => { await sleep(100); return { ok: false, mean: 0.2, low: 0.1 }; }
  });
  try {
    assert.equal((await mismatch.run()).state, 'completed');
    const tiers = mismatch.encodes().map((entry) => mismatch.tierOf(entry.args));
    assert.equal(tiers.filter((tier) => tier === 'zero-copy').length, 3);
    assert.equal(tiers.filter((tier) => tier === 'gpu').length, 3);
    assert.equal(mismatch.compared.length, 1);
  } finally {
    await mismatch.close();
  }
  // a clip encoded zero-copy that finishes only after the mismatch was found
  // (a slower clip, or a slower system) is not checked again: zero-copy is
  // off for that GPU, so it goes back to GPU decoding like the others
  let zeroCopyEncodes = 0;
  const late = createRig({
    device: AMD_RENDER, works: vaapiWorks, clips: 3, concurrency: 3,
    encodeMs: (args) => {
      if (!args.includes('pipe:1') || !args.includes('-hwaccel_output_format')) return 0;
      zeroCopyEncodes += 1;
      return zeroCopyEncodes === 3 ? 300 : 0;
    },
    comparePicture: async () => {
      // the verdict comes once all three are encoding zero-copy
      while (zeroCopyEncodes < 3) await sleep(5);
      return { ok: false, mean: 0.2, low: 0.1 };
    }
  });
  try {
    assert.equal((await late.run()).state, 'completed');
    const tiers = late.encodes().map((entry) => late.tierOf(entry.args));
    assert.equal(tiers.filter((tier) => tier === 'zero-copy').length, 3);
    assert.equal(tiers.filter((tier) => tier === 'gpu').length, 3);
    assert.equal(late.compared.length, 1, 'one check, however late a clip finishes');
  } finally {
    await late.close();
  }
});

test('the zero-copy record is shared between services (job and benchmark levels): a GPU is checked once per process', async () => {
  const zeroCopyState = createZeroCopyState();
  const first = createRig({ device: AMD_RENDER, works: vaapiWorks, zeroCopyState, comparePicture: async () => ({ ok: true }) });
  try {
    assert.equal((await first.run()).state, 'completed');
    assert.equal(first.compared.length, 1);
  } finally {
    await first.close();
  }
  const second = createRig({ device: AMD_RENDER, works: vaapiWorks, zeroCopyState, comparePicture: async () => ({ ok: true }) });
  try {
    assert.equal((await second.run()).state, 'completed');
    assert.equal(second.tierOf(second.encodes()[0].args), 'zero-copy');
    assert.equal(second.compared.length, 0);
  } finally {
    await second.close();
  }
  // three failures in one service turn zero-copy off in the next as well
  const failing = createZeroCopyState();
  const behave = (args) => (args.includes('-hwaccel_output_format') ? 1 : 0);
  const broke = createRig({ device: AMD_RENDER, works: vaapiWorks, clips: 3, behave, zeroCopyState: failing });
  try {
    assert.equal((await broke.run()).state, 'completed');
  } finally {
    await broke.close();
  }
  const after = createRig({ device: AMD_RENDER, works: vaapiWorks, behave, zeroCopyState: failing });
  try {
    assert.equal((await after.run()).state, 'completed');
    assert.deepEqual(after.encodes().map((entry) => after.tierOf(entry.args)), ['gpu']);
  } finally {
    await after.close();
  }
});

test('a Linux QSV zero-copy encode reports its lane\'s render node as its GPU (System page stats)', async () => {
  let rig = null;
  const seen = [];
  const spawnProcess = (command, args) => {
    const child = new EventEmitter();
    child.stdout = new EventEmitter();
    child.stderr = new EventEmitter();
    child.pid = 4242;
    let done = false;
    const finish = (code, signal = null) => { if (!done) { done = true; setImmediate(() => child.emit('close', code, signal)); } };
    child.kill = (signal) => finish(null, signal || 'SIGTERM');
    setTimeout(() => {
      if (args.includes('pipe:1')) seen.push(...rig.service.activeEncodes().map((entry) => entry.device));
      child.stdout.emit('data', 'fps=100\nprogress=end\n');
      finish(0);
    }, 30);
    return child;
  };
  rig = createRig({ device: ARC, works: (encoder) => encoder === 'hevc_qsv' || encoder.startsWith('lib'), spawnProcess });
  try {
    assert.equal((await rig.run()).state, 'completed');
    const [encode] = rig.encodes();
    assert.equal(rig.tierOf(encode.args), 'zero-copy');
    assert.ok(encode.args.includes('vaapi=va:/dev/dri/renderD129'));
    assert.deepEqual(seen, ['/dev/dri/renderD129']);
  } finally {
    await rig.close();
  }
});

test('a Windows QSV or AMF encode on a chosen adapter reports the GPU\'s id as its GPU (System page stats)', async () => {
  // the arguments name only the adapter index; the Windows stats engine names
  // the GPU by its id (pci:...), so that is what the live encode carries
  const liveDevices = async (device, works) => {
    let rig = null;
    const seen = [];
    const spawnProcess = (command, args) => {
      const child = new EventEmitter();
      child.stdout = new EventEmitter();
      child.stderr = new EventEmitter();
      child.pid = 4242;
      let done = false;
      const finish = (code, signal = null) => { if (!done) { done = true; setImmediate(() => child.emit('close', code, signal)); } };
      child.kill = (signal) => finish(null, signal || 'SIGTERM');
      setTimeout(() => {
        if (args.includes('pipe:1')) seen.push(...rig.service.activeEncodes().map((entry) => [entry.device, entry.encoder]));
        child.stdout.emit('data', 'fps=100\nprogress=end\n');
        finish(0);
      }, 30);
      return child;
    };
    rig = createRig({ platform: 'win32', device, works, spawnProcess });
    try {
      assert.equal((await rig.run()).state, 'completed');
      return { seen, encodes: rig.encodes(), tierOf: rig.tierOf };
    } finally {
      await rig.close();
    }
  };
  for (const [device, encoder] of [[WIN_INTEL, 'hevc_qsv'], [WIN_AMD, 'hevc_amf']]) {
    const { seen, encodes: [encode], tierOf } = await liveDevices(device, (name) => name === encoder || name.startsWith('lib'));
    assert.equal(tierOf(encode.args), 'zero-copy');
    assert.ok(encode.args.includes(`d3d11va=dx:${device.adapter}`));
    assert.deepEqual(seen, [[device.id, encoder]]);
  }
  // a Windows GPU that cannot encode the codec: its clip runs on the CPU, which names no GPU
  const cpu = await liveDevices(WIN_INTEL, (name) => name.startsWith('lib'));
  assert.deepEqual(cpu.seen, [[null, 'libx265']]);
});
