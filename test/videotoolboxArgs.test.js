'use strict';

// The converter on a Mac (docs/plans/macos.md, sections 7, 8 and 11): the
// VideoToolbox device is listed only after a passing hevc_videotoolbox test
// encode, its encodes are hardware only (-allow_sw 0, never 1, never
// -require_sw), quality levels use -q:v and bitrate profiles -b:v, the test
// encode itself uses -q:v 55, and the zero-copy tier keeps VideoToolbox
// frames on the GPU with the same guards as everywhere else. Linux and
// Windows arguments do not change (encodeArgsSnapshot.test.js, and the
// golden table below).

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');
const { EventEmitter } = require('events');
const {
  buildEncodeArgs, decodeAccelArgs, deviceCandidates, probeCandidate, createEncoderPlanner
} = require('../src/main/converter/encoderPlanner');
const { createDeviceDiscovery } = require('../src/main/converter/encodeDevices');
const { createConverterRuntime } = require('../src/main/converter/runtime');
const { createJobStore } = require('../src/main/converter/jobStore');
const { createConverterJobService } = require('../src/main/converter/jobService');
const { encodeMatrix, ARC, RADEON, RTX } = require('./fixtures/encodeArgsMatrix');

const VT = Object.freeze({
  id: 'pci:0000:00:00.0', kind: 'videotoolbox', vendor: 'apple', name: 'Apple M2 Max media engine', node: null, index: null, pci: null,
  chip: 'Apple M2 Max', gpuCores: 38, encodeEngines: 2, virtual: false
});
const CPU = Object.freeze({ id: 'cpu', kind: 'cpu', vendor: null, name: 'CPU (software encoder)', node: null, index: null, pci: null });
const TAIL = ['-bf', '0', '-tag:v', 'hvc1', '-fps_mode', 'passthrough', '-start_at_zero', '-avoid_negative_ts', 'make_zero', '-use_editlist', '0'];
const TESLA = Object.freeze({ codec: 'h264', durationSeconds: 2, containerReserveBytes: 0, telemetryReserveBytes: 0, pixFmt: 'yuv420p', width: 1280, height: 960 });
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const encoderOf = (args) => args[args.indexOf('-c:v') + 1];

test('hevc_videotoolbox on a Mac: -allow_sw 0, -q:v per quality level, -b:v for a bitrate, hvc1', () => {
  for (const [level, q] of [['high', '65'], ['balanced', '55'], ['small', '45']]) {
    assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', quality: level, platform: 'darwin' }),
      ['-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-q:v', q, '-pix_fmt', 'yuv420p', ...TAIL], level);
  }
  assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', bitrate: 1_234_567, platform: 'darwin' }),
    ['-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-b:v', '1234567', '-pix_fmt', 'yuv420p', ...TAIL]);
  // the device alone says it is a Mac's (a node's attempts, the planner's probes)
  assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', quality: 'balanced', device: VT }),
    ['-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-q:v', '55', '-pix_fmt', 'yuv420p', ...TAIL]);
  // zero-copy frames are VideoToolbox frames: no -pix_fmt
  assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', quality: 'balanced', device: VT, hwFrames: true, platform: 'darwin' }),
    ['-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-q:v', '55', ...TAIL]);
  // held lead-in frames: the clone pad in front, never with GPU frames
  assert.deepEqual(buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', quality: 'small', device: VT, holdFrames: 3, platform: 'darwin' }),
    ['-vf', 'tpad=start=3:start_mode=clone', '-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-q:v', '45', '-pix_fmt', 'yuv420p', ...TAIL]);
  assert.throws(() => buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', quality: 'small', device: VT, holdFrames: 3, hwFrames: true, platform: 'darwin' }), /system-memory/);
  // AV1 has no VideoToolbox encoder
  assert.throws(() => buildEncodeArgs({ codec: 'av1', encoder: 'av1_videotoolbox', bitrate: 1e6, platform: 'darwin' }), /not a av1 candidate/);
});

test('no VideoToolbox argument ever allows or requires the software encoder', () => {
  for (const quality of [null, 'high', 'balanced', 'small']) {
    for (const device of [null, VT]) {
      for (const hwFrames of [false, true]) {
        const args = buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', bitrate: 900_000, quality, hwFrames, platform: 'darwin', ...(device ? { device } : {}) });
        assert.equal(args.includes('-require_sw'), false);
        assert.deepEqual(args.slice(args.indexOf('-allow_sw'), args.indexOf('-allow_sw') + 2), ['-allow_sw', '0']);
        assert.equal(args.filter((a) => a === '-allow_sw').length, 1);
        assert.equal(args.some((a) => a.startsWith('/dev/')), false, 'no /dev/ path on a Mac');
      }
    }
  }
});

test('golden: every Linux and Windows argument is the same with the platform passed as without it', () => {
  // the snapshot's matrix, rebuilt with platform: 'linux' and 'win32' (and
  // 'darwin' for every encoder but VideoToolbox) gives the same table
  const original = encodeMatrix();
  for (const platform of ['linux', 'win32', 'darwin']) {
    const withPlatform = encodeMatrixWith((options) => buildEncodeArgs({ ...options, platform }));
    assert.deepEqual(Object.keys(withPlatform).sort(), Object.keys(original).sort());
    for (const [key, value] of Object.entries(original)) {
      if (platform === 'darwin' && key.includes('videotoolbox')) continue;
      assert.deepEqual(withPlatform[key], value, `${platform} ${key}`);
    }
  }
  // and VideoToolbox without a Mac's platform or device keeps its old arguments
  assert.deepEqual(original['h265 hevc_videotoolbox balanced none hold0 sw'],
    ['-c:v', 'hevc_videotoolbox', '-q:v', '55', '-pix_fmt', 'yuv420p', ...TAIL]);
});

// encodeArgsMatrix's encode table, through `build`
function encodeMatrixWith(build) {
  const ENCODERS = {
    h265: ['hevc_nvenc', 'hevc_qsv', 'hevc_amf', 'hevc_vaapi', 'hevc_videotoolbox', 'libx265'],
    av1: ['av1_nvenc', 'av1_qsv', 'av1_amf', 'av1_vaapi', 'libsvtav1', 'libaom-av1']
  };
  const RATES = { bitrate: { bitrate: 1_234_567 }, high: { quality: 'high' }, balanced: { quality: 'balanced' }, small: { quality: 'small' } };
  const DEVICES = { none: null, arc: ARC, radeon: RADEON, rtx: RTX };
  const outcome = (fn) => { try { return fn(); } catch (error) { return { error: error.message }; } };
  const out = {};
  for (const [codec, encoders] of Object.entries(ENCODERS)) {
    for (const encoder of encoders) {
      for (const [rateName, rate] of Object.entries(RATES)) {
        for (const [deviceName, device] of Object.entries(DEVICES)) {
          for (const holdFrames of [0, 3]) {
            for (const hwFrames of [false, true]) {
              if (hwFrames && !encoder.endsWith('_nvenc')) continue;
              const key = [codec, encoder, rateName, deviceName, `hold${holdFrames}`, hwFrames ? 'hw' : 'sw'].join(' ');
              out[key] = outcome(() => build({ codec, encoder, ...rate, hwFrames, ...(device ? { device } : {}), ...(holdFrames ? { holdFrames } : {}) }));
            }
          }
        }
      }
      out[`${codec} ${encoder} vaapiDevice`] = outcome(() => build({ codec, encoder, bitrate: 800_000, vaapiDevice: '/dev/dri/renderD130' }));
    }
  }
  return out;
}

test('the jobService /dev/ rule finds no GPU path in a Mac\'s arguments', () => {
  // (renderDevice reads -vaapi_device / -hwaccel_device /dev/... values)
  const line = [...decodeAccelArgs('hevc_videotoolbox', { platform: 'darwin', zeroCopy: true, device: VT }),
    ...buildEncodeArgs({ codec: 'h265', encoder: 'hevc_videotoolbox', quality: 'balanced', device: VT, hwFrames: true, platform: 'darwin' })];
  assert.equal(line.includes('-hwaccel_device'), false);
  assert.equal(line.includes('-vaapi_device'), false);
  assert.equal(line.some((a) => a.startsWith('/dev/')), false);
});

test('decode tiers on a Mac: zero-copy keeps VideoToolbox frames, GPU decode copies them back, CPU encoders get none', () => {
  assert.deepEqual(decodeAccelArgs('hevc_videotoolbox', { platform: 'darwin', zeroCopy: true, device: VT }),
    ['-hwaccel', 'videotoolbox', '-hwaccel_output_format', 'videotoolbox_vld']);
  assert.deepEqual(decodeAccelArgs('hevc_videotoolbox', { platform: 'darwin', device: VT }), ['-hwaccel', 'videotoolbox']);
  assert.deepEqual(decodeAccelArgs('hevc_videotoolbox', { platform: 'darwin' }), ['-hwaccel', 'videotoolbox']);
  assert.equal(decodeAccelArgs('libx265', { platform: 'darwin' }), null);
  assert.equal(decodeAccelArgs('libx265', { platform: 'darwin', zeroCopy: true }), null);
  assert.equal(decodeAccelArgs('hevc_qsv', { platform: 'darwin', zeroCopy: true }), null);
  // VideoToolbox's zero-copy tier exists only on a Mac
  assert.equal(decodeAccelArgs('hevc_videotoolbox', { platform: 'linux', zeroCopy: true }), null);
  assert.equal(decodeAccelArgs('hevc_videotoolbox', { platform: 'win32', zeroCopy: true }), null);
});

test('per device: the VideoToolbox device encodes H.265 with hevc_videotoolbox, AV1 on the CPU; Linux and Windows unchanged', () => {
  assert.deepEqual(deviceCandidates('h265', VT, 'darwin'), ['hevc_videotoolbox']);
  assert.deepEqual(deviceCandidates('av1', VT, 'darwin'), []);
  assert.deepEqual(deviceCandidates('h265', CPU, 'darwin'), []);
  assert.deepEqual(deviceCandidates('h265', ARC, 'darwin'), []);
  assert.deepEqual(deviceCandidates('h265', VT, 'linux'), []);
  assert.deepEqual(deviceCandidates('h265', VT, 'win32'), []);
  assert.deepEqual(deviceCandidates('h265', ARC, 'linux'), ['hevc_qsv', 'hevc_vaapi']);
  assert.deepEqual(deviceCandidates('av1', RTX, 'linux'), ['av1_nvenc']);
});

test('the Mac\'s test encode uses -q:v 55 and -allow_sw 0; Linux keeps today\'s probe line', async () => {
  const seen = [];
  const runProbe = async (_cmd, args) => { seen.push(args); return { status: 0, stdout: '', stderr: '' }; };
  const mac = await probeCandidate({ runProbe, platform: 'darwin', family: 'h265', candidate: 'hevc_videotoolbox', device: VT });
  assert.equal(mac.usable, true);
  assert.deepEqual(seen[0], [
    '-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc2=duration=0.5:size=640x360:rate=8', '-an', '-frames:v', '4',
    '-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-q:v', '55', '-pix_fmt', 'yuv420p', ...TAIL, '-f', 'mp4', '/dev/null'
  ]);
  // without a device (a job without lanes) the same
  await probeCandidate({ runProbe, platform: 'darwin', family: 'h265', candidate: 'hevc_videotoolbox' });
  assert.deepEqual(seen[1], seen[0]);
  // the CPU encoders' probes on a Mac are the bitrate ones of everywhere
  await probeCandidate({ runProbe, platform: 'darwin', family: 'h265', candidate: 'libx265' });
  await probeCandidate({ runProbe, platform: 'linux', family: 'h265', candidate: 'libx265' });
  assert.deepEqual(seen[2], seen[3]);
  assert.ok(seen[3].includes('500000'));
  // Linux and Windows probes of every encoder are exactly today's
  for (const platform of ['linux', 'win32']) {
    for (const candidate of ['hevc_nvenc', 'hevc_qsv', 'hevc_amf', 'hevc_vaapi', 'libx265']) {
      const before = seen.length;
      await probeCandidate({ runProbe, platform, family: 'h265', candidate });
      const args = seen[before];
      const encode = args.slice(11, -3);
      assert.deepEqual(encode, buildEncodeArgs({ codec: 'h265', encoder: candidate, bitrate: 500_000 }), `${platform} ${candidate}`);
    }
  }
});

const ADVERTISED = ['hevc_videotoolbox', 'h264_videotoolbox', 'libx265', 'libsvtav1', 'libaom-av1'].map((name) => ` V....D ${name}  x`).join('\n');

test('the planner on a Mac: the VideoToolbox lane encodes H.265 in hardware, AV1 on the CPU', async () => {
  const tried = [];
  const planner = createEncoderPlanner({
    platform: 'darwin',
    runProbe: async (_cmd, args) => {
      if (args.includes('-encoders')) return { status: 0, stdout: ADVERTISED, stderr: '' };
      tried.push(args);
      return { status: 0, stdout: '', stderr: '' };
    }
  });
  const warn = console.warn;
  console.warn = () => {};
  try {
    const h265 = await planner.selectFor('h265', VT);
    assert.deepEqual({ codec: h265.codec, hardware: h265.hardware, kind: h265.device.kind }, { codec: 'hevc_videotoolbox', hardware: true, kind: 'videotoolbox' });
    const av1 = await planner.selectFor('av1', VT);
    assert.deepEqual({ codec: av1.codec, hardware: av1.hardware }, { codec: 'libsvtav1', hardware: false });
    assert.deepEqual(await planner.select('h265'), { family: 'h265', codec: 'hevc_videotoolbox', hardware: true });
  } finally {
    console.warn = warn;
  }
  const vt = tried.filter((args) => encoderOf(args) === 'hevc_videotoolbox');
  assert.ok(vt.length >= 1);
  for (const args of vt) {
    assert.ok(args.join(' ').includes('-c:v hevc_videotoolbox -allow_sw 0 -q:v 55'));
    assert.equal(args.includes('-b:v'), false);
  }
});

// sysctl, system_profiler and ffmpeg as a Mac answers them
function macRunner({ hevc = true, arm64 = '1' } = {}) {
  const calls = [];
  const runCommand = async (command, args) => {
    calls.push([command, ...args]);
    if (command === '/usr/sbin/sysctl') {
      const key = args[1];
      if (key === 'hw.optional.arm64') return { status: 0, stdout: `${arm64}\n`, stderr: '' };
      return { status: 0, stdout: '0\n', stderr: '' };
    }
    if (command === '/usr/sbin/system_profiler') {
      return { status: 0, stdout: JSON.stringify({ SPDisplaysDataType: [{ _name: 'Apple M2 Max', sppci_model: 'Apple M2 Max', sppci_cores: '38', spdisplays_vendor: 'sppci_vendor_Apple' }] }), stderr: '' };
    }
    if (command === 'ffmpeg') return hevc ? { status: 0, stdout: '', stderr: '' } : { status: 1, stdout: '', stderr: 'Error: cannot create compression session: -12908' };
    return { status: 1, stdout: '', stderr: '' };
  };
  return { runCommand, calls };
}
const macOs = { cpus: () => [{ model: 'Apple M2 Max' }] };

test('encodeDevices on a Mac: the VideoToolbox device then the CPU after a passing test encode, the CPU only otherwise', async () => {
  const good = macRunner();
  const list = await createDeviceDiscovery({ platform: 'darwin', runCommand: good.runCommand, os: macOs, arch: 'arm64' }).list();
  assert.deepEqual(list.map((d) => d.id), ['pci:0000:00:00.0', 'cpu']);
  assert.deepEqual([list[0].kind, list[0].name, list[0].encodeEngines, list[0].gpuCores], ['videotoolbox', 'Apple M2 Max media engine', 2, 38]);
  const probes = good.calls.filter((c) => c[0] === 'ffmpeg');
  assert.equal(probes.length, 1);
  assert.ok(probes[0].join(' ').includes('-c:v hevc_videotoolbox -allow_sw 0 -q:v 55'));
  // the pass is kept: no second test encode
  const discovery = createDeviceDiscovery({ platform: 'darwin', runCommand: good.runCommand, os: macOs, arch: 'arm64' });
  await discovery.list();
  await discovery.list();
  assert.equal(good.calls.filter((c) => c[0] === 'ffmpeg').length, 2);

  // a Mac VM, a daemon without GPU access: only the CPU
  const bad = macRunner({ hevc: false });
  assert.deepEqual((await createDeviceDiscovery({ platform: 'darwin', runCommand: bad.runCommand, os: macOs, arch: 'arm64' }).list()).map((d) => d.id), ['cpu']);
  // an Intel Mac or Rosetta: the CPU, nothing test-encoded
  const intel = macRunner({ arm64: '' });
  assert.deepEqual((await createDeviceDiscovery({ platform: 'darwin', runCommand: intel.runCommand, os: macOs, arch: 'x64' }).list()).map((d) => d.id), ['cpu']);
  assert.equal(intel.calls.some((c) => c[0] === 'ffmpeg'), false);
  // without a runner nothing can be tested: the CPU
  assert.deepEqual((await createDeviceDiscovery({ platform: 'darwin', os: macOs, arch: 'arm64' }).list()).map((d) => d.id), ['cpu']);
  // an injected test encode decides
  const injected = await createDeviceDiscovery({ platform: 'darwin', runCommand: bad.runCommand, os: macOs, arch: 'arm64', hevcProbe: async () => true }).list();
  assert.deepEqual(injected.map((d) => d.id), ['pci:0000:00:00.0', 'cpu']);
});

test('the runtime on a Mac: discovery gets ffmpeg, a job without lanes defaults to the VideoToolbox device', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-rt-darwin-'));
  try {
    for (const name of ['data', 'temp']) fs.mkdirSync(path.join(dir, name));
    const captured = [];
    const node = { id: 'node:abcdefabcdef', kind: 'node', name: 'garage (node)', online: true, encoders: { h265: 'hevc_nvenc', av1: null } };
    const runtime = await createConverterRuntime({
      userDataPath: fs.realpathSync(path.join(dir, 'data')), tempPath: fs.realpathSync(path.join(dir, 'temp')),
      ffmpegPath: '/Library/SentrySixManager/ffmpeg/ffmpeg', ffprobePath: 'ffprobe', spawn, autoConvert: false, platform: 'darwin',
      remoteNodes: { list: async () => [node] },
      factories: {
        createEncoderPlanner: () => createEncoderPlanner({
          platform: 'darwin',
          runProbe: async (_cmd, args) => (args.includes('-encoders') ? { status: 0, stdout: ADVERTISED, stderr: '' } : { status: 0, stdout: '', stderr: '' })
        }),
        createDeviceDiscovery: (options) => {
          captured.push(options);
          return { list: async () => [VT, CPU] };
        }
      }
    });
    try {
      assert.equal(captured[0].platform, 'darwin');
      assert.equal(captured[0].ffmpegPath, '/Library/SentrySixManager/ffmpeg/ffmpeg');
      const warn = console.warn;
      console.warn = () => {};
      let out;
      try { out = await runtime.devices(); } finally { console.warn = warn; }
      assert.deepEqual(out.defaultDevices, { h265: VT.id, av1: 'cpu' });
    } finally {
      await runtime.shutdown();
    }
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Linux runtimes hand discovery exactly what they did (no ffmpeg path)', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-rt-linux-'));
  try {
    for (const name of ['data', 'temp']) fs.mkdirSync(path.join(dir, name));
    const captured = [];
    const runtime = await createConverterRuntime({
      userDataPath: fs.realpathSync(path.join(dir, 'data')), tempPath: fs.realpathSync(path.join(dir, 'temp')),
      ffmpegPath: 'ffmpeg', ffprobePath: 'ffprobe', spawn, autoConvert: false, platform: 'linux',
      factories: {
        createDeviceDiscovery: (options) => {
          captured.push(options);
          return { list: async () => [CPU] };
        }
      }
    });
    try {
      assert.deepEqual(Object.keys(captured[0]).sort(), ['fs', 'pathApi', 'platform', 'runCommand']);
    } finally {
      await runtime.shutdown();
    }
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

// A job service on a Mac with one VideoToolbox lane; ffmpeg is a fake child
// that fails whichever tier `behave` says.
function macRig({ behave, clips = 1, comparePicture, quality = null, video = TESLA, platform = 'darwin' }) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-vt-'));
  const store = createJobStore({ rootDir: path.join(dir, 'jobs'), fs: fs.promises });
  const planner = createEncoderPlanner({
    platform: 'darwin',
    runProbe: async (_cmd, args) => (args.includes('-encoders') ? { status: 0, stdout: ADVERTISED, stderr: '' } : { status: 0, stdout: '', stderr: '' })
  });
  const spawned = [];
  const compared = [];
  let pid = 3000;
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
    setImmediate(() => {
      if (code === 0) child.stdout.emit('data', 'fps=100\nprogress=end\n');
      finish(code);
    });
    return child;
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
    readTimeline: async () => ({ frames: [] }),
    writers: { h265: async () => {}, av1: async () => {} },
    verifier: async () => ({ ok: true, outputBytes: 1_000_000, publicationArtifact: null }),
    spawnProcess: fakeSpawn,
    ffmpegPath: 'ffmpeg',
    platform,
    decodeAccel: (encoder, accel = {}) => decodeAccelArgs(encoder, {
      platform: 'darwin', zeroCopy: accel.zeroCopy === true, ...(accel.device ? { device: accel.device } : {})
    }),
    resolveDevice: async (id) => [VT, CPU].find((entry) => entry.id === id) || null,
    defaultDevice: async () => null,
    encodeStallMs: 60_000,
    ...(comparePicture ? { comparePicture: async (request) => { compared.push(request); return comparePicture(request); } } : {})
  });
  const config = () => ({
    sourceRoot: path.join(dir, 'footage'), destinationRoot: path.join(dir, 'out'),
    codec: 'h265', ...(quality ? { profileId: 'quality', qualityLevel: quality } : { profileId: 'custom', bitrateKbps: 250 }),
    networkMode: 'direct', outputMode: 'separate',
    includedGroups: ['RecentClips', 'SavedClips', 'SentryClips'], groupOrder: ['SavedClips', 'SentryClips', 'RecentClips'],
    replacementConfirmed: false, concurrency: 1, verifyMode: 'quick', lanes: [{ device: VT.id, concurrency: 1 }]
  });
  async function run() {
    const id = await service.start(config());
    await Promise.race([service.whenIdle(id), sleep(20_000).then(() => { throw new Error('the job did not finish'); })]);
    return service.get(id);
  }
  const encodes = () => spawned.filter((entry) => entry.args.includes('pipe:1'));
  const tierOf = (args) => (args.includes('-hwaccel_output_format') ? 'zero-copy' : args.includes('-hwaccel') ? 'gpu' : 'cpu');
  async function close() {
    await service.shutdown().catch(() => {});
    fs.rmSync(dir, { recursive: true, force: true });
  }
  return { run, encodes, tierOf, compared, close };
}

test('a VideoToolbox lane encodes a Tesla clip zero-copy with exactly the contract\'s line', async () => {
  const rig = macRig({ quality: 'balanced' });
  try {
    assert.equal((await rig.run()).state, 'completed');
    const [encode] = rig.encodes();
    const at = encode.args.indexOf('-i');
    assert.deepEqual(encode.args.slice(5, at), ['-hwaccel', 'videotoolbox', '-hwaccel_output_format', 'videotoolbox_vld']);
    assert.deepEqual(encode.args.slice(at + 2, at + 2 + 6 + TAIL.length), ['-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-q:v', '55', ...TAIL]);
  } finally {
    await rig.close();
  }
  // a bitrate profile: -b:v
  const bitrate = macRig({});
  try {
    assert.equal((await bitrate.run()).state, 'completed');
    const [encode] = bitrate.encodes();
    const at = encode.args.indexOf('-c:v');
    assert.deepEqual(encode.args.slice(at, at + 6), ['-c:v', 'hevc_videotoolbox', '-allow_sw', '0', '-b:v', '250000']);
  } finally {
    await bitrate.close();
  }
});

test('10-bit or odd-sized clips skip VideoToolbox zero-copy and decode on the GPU, copied back', async () => {
  for (const video of [{ ...TESLA, pixFmt: 'yuv420p10le' }, { ...TESLA, height: 961 }]) {
    const rig = macRig({ quality: 'high', video });
    try {
      assert.equal((await rig.run()).state, 'completed');
      const [encode] = rig.encodes();
      assert.equal(rig.tierOf(encode.args), 'gpu');
      assert.ok(encode.args.join(' ').includes('-pix_fmt yuv420p'));
    } finally {
      await rig.close();
    }
  }
});

test('VideoToolbox zero-copy failing falls back to GPU decode, then the CPU; off after the third time', async () => {
  const rig = macRig({ clips: 4, behave: (args) => (args.includes('-hwaccel_output_format') ? 1 : 0) });
  try {
    assert.equal((await rig.run()).state, 'completed');
    assert.deepEqual(rig.encodes().map((e) => rig.tierOf(e.args)), ['zero-copy', 'gpu', 'zero-copy', 'gpu', 'zero-copy', 'gpu', 'gpu']);
  } finally {
    await rig.close();
  }
  const both = macRig({ behave: (args) => (encoderOf(args) === 'hevc_videotoolbox' ? 1 : 0) });
  try {
    assert.equal((await both.run()).state, 'completed');
    assert.deepEqual(both.encodes().map((e) => `${both.tierOf(e.args)}:${encoderOf(e.args)}`),
      ['zero-copy:hevc_videotoolbox', 'gpu:hevc_videotoolbox', 'cpu:hevc_videotoolbox', 'cpu:libx265']);
  } finally {
    await both.close();
  }
});

test('the first VideoToolbox zero-copy clip has its pictures checked; a mismatch turns zero-copy off', async () => {
  const ok = macRig({ clips: 2, comparePicture: async () => ({ ok: true, mean: 0.98, low: 0.95 }) });
  try {
    assert.equal((await ok.run()).state, 'completed');
    assert.deepEqual(ok.encodes().map((e) => ok.tierOf(e.args)), ['zero-copy', 'zero-copy']);
    assert.equal(ok.compared.length, 1);
  } finally {
    await ok.close();
  }
  const bad = macRig({ clips: 2, comparePicture: async () => ({ ok: false, mean: 0.2, low: 0.1 }) });
  try {
    assert.equal((await bad.run()).state, 'completed');
    assert.deepEqual(bad.encodes().map((e) => bad.tierOf(e.args)), ['zero-copy', 'gpu', 'gpu']);
  } finally {
    await bad.close();
  }
});
