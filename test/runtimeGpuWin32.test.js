'use strict';

// The converter runtime's GPU facts for zero-copy and Windows: the probe
// reads the picture format and size (zero-copy only for 8-bit 4:2:0 with
// even dimensions), the helper is handed to device discovery, and on
// Windows the GPU a job without lanes encodes on is the one AMF or QSV
// opens by itself (docs/plans/windows.md, sections 7 and 8).

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');
const { createConverterRuntime, parseVideoProbe } = require('../src/main/converter/runtime');
const { createEncoderPlanner } = require('../src/main/converter/encoderPlanner');
const { fakeProbe, CPU } = require('./fixtures/lanesHarness');

const probeResult = (stream) => ({ status: 0, stderr: '', stdout: JSON.stringify({ streams: [{ codec_name: 'h264', duration: '60.03', ...stream }], format: { duration: '60.03' } }) });

test('the video probe reads the picture format and size; anything odd is unknown', () => {
  const tesla = parseVideoProbe(probeResult({ pix_fmt: 'yuv420p', width: 1280, height: 960 }));
  assert.deepEqual(tesla, { codec: 'h264', durationSeconds: 60.03, containerReserveBytes: 0, telemetryReserveBytes: 0, pixFmt: 'yuv420p', width: 1280, height: 960 });
  const odd = parseVideoProbe(probeResult({ pix_fmt: 'yuv420p; rm', width: -2, height: 1.5 }));
  assert.equal(odd.pixFmt, null);
  assert.equal(odd.width, null);
  assert.equal(odd.height, null);
  const older = parseVideoProbe(probeResult({}));
  assert.deepEqual([older.pixFmt, older.width, older.height], [null, null, null]);
});

const WIN_AMD0 = Object.freeze({ id: 'pci:0000:03:00.0', kind: 'd3d11', vendor: 'amd', name: 'AMD Radeon RX 7600', node: null, index: null, pci: '0000:03:00.0', adapter: 0, luid: '0x1' });
const WIN_AMD1 = Object.freeze({ ...WIN_AMD0, id: 'pci:0000:04:00.0', pci: '0000:04:00.0', adapter: 1, luid: '0x2' });
const WIN_INTEL = Object.freeze({ id: 'pci:0000:00:02.0', kind: 'd3d11', vendor: 'intel', name: 'Intel UHD Graphics 770', node: null, index: null, pci: '0000:00:02.0', adapter: 2, luid: '0x3' });

async function openRuntime(dir, { devices, works, captured }) {
  const probe = fakeProbe(works);
  for (const name of ['data', 'temp']) fs.mkdirSync(path.join(dir, name), { recursive: true });
  const runtime = await createConverterRuntime({
    userDataPath: fs.realpathSync(path.join(dir, 'data')), tempPath: fs.realpathSync(path.join(dir, 'temp')),
    ffmpegPath: 'ffmpeg', ffprobePath: 'ffprobe', spawn, autoConvert: false, platform: 'win32',
    requestHelper: async () => ({ adapters: [] }),
    factories: {
      createEncoderPlanner: () => createEncoderPlanner({
        // (what a Windows ffmpeg build lists, AMF among them)
        runProbe: async (command, args, options) => (args.includes('-encoders')
          ? { status: 0, stdout: ['libx265', 'libsvtav1', 'hevc_nvenc', 'hevc_qsv', 'hevc_amf'].map((name) => ` V..... ${name}  x`).join('\n'), stderr: '' }
          : probe.runProbe(command, args, options)),
        platform: 'win32'
      }),
      createDeviceDiscovery: (options) => {
        captured.push(options);
        return { list: async () => devices };
      }
    }
  });
  return runtime;
}

test('Windows: AMF without lanes is on the default adapter (0), QSV on the one Intel GPU; the helper reaches discovery', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-rt-win32-'));
  const captured = [];
  try {
    const amf = await openRuntime(fs.mkdtempSync(path.join(dir, 'a-')), {
      devices: [WIN_AMD0, WIN_AMD1, CPU], captured,
      works: (encoder) => encoder.startsWith('lib') || encoder === 'hevc_amf'
    });
    assert.equal(typeof captured[0].requestHelper, 'function');
    assert.equal(captured[0].platform, 'win32');
    assert.equal((await amf.devices()).defaultDevices.h265, WIN_AMD0.id);
    await amf.shutdown();

    const qsv = await openRuntime(fs.mkdtempSync(path.join(dir, 'q-')), {
      devices: [WIN_AMD1, WIN_INTEL, CPU], captured,
      works: (encoder) => encoder.startsWith('lib') || encoder === 'hevc_qsv'
    });
    assert.equal((await qsv.devices()).defaultDevices.h265, WIN_INTEL.id);
    await qsv.shutdown();

    // AMF whose default adapter is not an AMD GPU: which one it opens is not known
    const unknown = await openRuntime(fs.mkdtempSync(path.join(dir, 'u-')), {
      devices: [WIN_INTEL, WIN_AMD1, CPU], captured,
      works: (encoder) => encoder.startsWith('lib') || encoder === 'hevc_amf'
    });
    assert.equal((await unknown.devices()).defaultDevices.h265, null);
    await unknown.shutdown();
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
