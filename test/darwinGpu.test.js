'use strict';

// Settings > System on a Mac (docs/plans/macos.md, section 7): one Apple GPU
// with VideoToolbox, HEVC from a real test encode (-allow_sw 0, -q:v 55),
// AV1 never in hardware, and plain hints. Nothing Linux-only is run.

const test = require('node:test');
const assert = require('node:assert/strict');
const { detectGpus } = require('../server/gpu');
const { createDarwinGpus, HINT_NO_HEVC, HINT_AV1 } = require('../server/platform/darwinGpu');

const PROFILER = JSON.stringify({ SPDisplaysDataType: [{ _name: 'Apple M3 Pro', sppci_model: 'Apple M3 Pro', sppci_cores: '18', spdisplays_vendor: 'sppci_vendor_Apple' }] });

function macRun({ hevc = true, profiler = PROFILER } = {}) {
  const calls = [];
  const runImpl = async (cmd, args, options) => {
    calls.push({ cmd, args, options });
    if (cmd === '/usr/sbin/system_profiler') return profiler === null ? { code: 1, stdout: '', stderr: 'no' } : { code: 0, stdout: profiler, stderr: '' };
    if (cmd === 'ffmpeg') return hevc ? { code: 0, stdout: '', stderr: '' } : { code: 1, stdout: '', stderr: 'cannot create compression session' };
    throw new Error(`not on a Mac: ${cmd}`);
  };
  return { runImpl, calls };
}
const macOs = (model = 'Apple M3 Pro') => ({ cpus: () => [{ model }] });

test('a Mac shows one Apple GPU with VideoToolbox; HEVC from a real test encode, AV1 on the CPU', async () => {
  const { runImpl, calls } = macRun();
  const gpus = createDarwinGpus({ runImpl, os: macOs(), ffmpegPath: 'ffmpeg' });
  const out = await gpus.detect();
  assert.deepEqual(out, {
    gpus: [{
      node: 'VideoToolbox', vendor: 'apple', vendorName: 'Apple', name: 'Apple M3 Pro GPU', cores: 18, driver: 'VideoToolbox',
      passedThrough: true, encodes: { h264: true, hevc: true, av1: false }, lowPowerOnly: false
    }],
    nvidia: [],
    hints: [HINT_AV1]
  });
  const probe = calls.find((c) => c.cmd === 'ffmpeg');
  assert.ok(probe.args.join(' ').includes('-c:v hevc_videotoolbox -allow_sw 0 -q:v 55'));
  assert.equal(probe.args.includes('-require_sw'), false);
  // the test encode and system_profiler run once
  await gpus.detect();
  assert.equal(calls.filter((c) => c.cmd === 'ffmpeg').length, 1);
  assert.equal(calls.filter((c) => c.cmd === '/usr/sbin/system_profiler').length, 1);
  assert.deepEqual(calls[0].args, ['SPDisplaysDataType', '-json']);
  assert.equal(calls.some((c) => /vainfo|nvidia-smi/.test(c.cmd)), false);
});

test('no reachable media engine (a VM): HEVC false with a plain hint, tried again after 10 minutes', async () => {
  const { runImpl, calls } = macRun({ hevc: false, profiler: null });
  let clock = 0;
  const gpus = createDarwinGpus({ runImpl, os: macOs('Apple M1'), ffmpegPath: 'ffmpeg', now: () => clock });
  const out = await gpus.detect();
  assert.deepEqual(out.gpus[0].encodes, { h264: false, hevc: false, av1: false });
  assert.equal(out.gpus[0].name, 'Apple M1 GPU');
  assert.equal(out.gpus[0].cores, null);
  assert.deepEqual(out.hints, [HINT_NO_HEVC, HINT_AV1]);
  await gpus.detect();
  assert.equal(calls.filter((c) => c.cmd === 'ffmpeg').length, 1);
  clock = 10 * 60_000;
  await gpus.detect();
  assert.equal(calls.filter((c) => c.cmd === 'ffmpeg').length, 2);
  // the hints say nothing about Docker, containers or /dev
  for (const hint of [HINT_NO_HEVC, HINT_AV1]) assert.doesNotMatch(hint, /docker|container|\/dev|compose/i);
});

test('without a CPU model the GPU is named from system_profiler', async () => {
  const { runImpl } = macRun();
  const out = await createDarwinGpus({ runImpl, os: { cpus: () => [] }, ffmpegPath: 'ffmpeg' }).detect();
  assert.equal(out.gpus[0].name, 'Apple M3 Pro GPU');
});

test('detectGpus takes the Mac branch only on darwin', async () => {
  let asked = 0;
  const darwin = { detect: async () => { asked += 1; return { gpus: [], nvidia: [], hints: ['mac'] }; } };
  assert.deepEqual(await detectGpus({ platform: 'darwin', darwin }), { gpus: [], nvidia: [], hints: ['mac'] });
  // Linux never asks it
  const linuxRun = async () => ({ code: 1, stdout: '', stderr: '' });
  const linux = await detectGpus({ platform: 'linux', darwin, runImpl: linuxRun, driDir: '/nonexistent', sysDrm: '/nonexistent', dxg: '/nonexistent', procVersion: '/nonexistent' });
  assert.deepEqual(linux, { gpus: [], nvidia: [], hints: [] });
  assert.equal(asked, 1);
});
