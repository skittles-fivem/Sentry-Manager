'use strict';

// Settings > System on a Mac: the Apple GPU and whether VideoToolbox can
// encode on it, in the shape gpu.js gives on Linux ({ gpus, nvidia, hints }).
// There is always one entry: every Apple Silicon Mac has one GPU with one
// media engine. Nothing reads /dev/dri or runs vainfo or nvidia-smi.
//
// The HEVC answer is a real 0.5 s hevc_videotoolbox test encode (-allow_sw 0,
// -q:v 55), the converter's own probe. A pass is kept for the process; a
// failure (a VM, a daemon without GPU access) is tried again after 10
// minutes. system_profiler (GPU core count) runs once.

const nodeOs = require('os');
const { parseDisplays, VT_DEVICE_ID } = require('../../src/main/converter/darwinDevices');
const { probeCandidate } = require('../../src/main/converter/encoderPlanner');

const SYSTEM_PROFILER = '/usr/sbin/system_profiler';
const PROFILER_TIMEOUT_MS = 15_000;
const RETRY_MS = 10 * 60_000;

const HINT_NO_HEVC = 'This Mac\'s hardware HEVC encoder could not be reached, so conversions use the CPU encoder.';
const HINT_AV1 = 'AV1 uses the CPU on a Mac: there is no AV1 hardware encoder.';

const cleanText = (value) => String(value ?? '').replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ').replace(/\s+/g, ' ').trim();

function defaultFfmpeg() {
  if (process.env.FFMPEG_PATH) return process.env.FFMPEG_PATH;
  return require('./darwin').createDarwinPlatform().defaults.ffmpeg;
}

/**
 * runImpl: proc.run ((cmd, args, { timeoutMs }) => { code, stdout, stderr,
 * timedOut }); os, ffmpegPath and now for tests.
 */
function createDarwinGpus({ runImpl, os = nodeOs, ffmpegPath, now = Date.now, retryMs = RETRY_MS } = {}) {
  const run = async (cmd, args, timeoutMs) => {
    try {
      return await runImpl(cmd, args, { timeoutMs });
    } catch {
      return null;
    }
  };

  let displays = null;
  function readDisplays() {
    if (!displays) {
      displays = run(SYSTEM_PROFILER, ['SPDisplaysDataType', '-json'], PROFILER_TIMEOUT_MS)
        .then((r) => (r && r.code === 0 && !r.timedOut ? parseDisplays(r.stdout) : null))
        .catch(() => null);
    }
    return displays;
  }

  let passed = false;
  let failedAt = null;
  let pending = null;
  function hevc() {
    if (passed) return Promise.resolve(true);
    if (failedAt !== null && now() - failedAt < retryMs) return Promise.resolve(false);
    if (!pending) {
      const runProbe = async (cmd, args, options) => {
        const r = await run(cmd, args, options.timeoutMs);
        return r ? { status: r.code, stdout: r.stdout, stderr: r.stderr, timedOut: r.timedOut === true } : { status: null, error: { code: 'PROCESS_START_FAILED' } };
      };
      pending = probeCandidate({
        runProbe, ffmpegPath: ffmpegPath || defaultFfmpeg(), platform: 'darwin', family: 'h265',
        candidate: { codec: 'hevc_videotoolbox', hardware: true }, device: { id: VT_DEVICE_ID, kind: 'videotoolbox' }
      }).then((proof) => proof.usable === true, () => false).then((ok) => {
        if (ok) passed = true;
        else failedAt = now();
        return ok;
      }).finally(() => { pending = null; });
    }
    return pending;
  }

  async function detect() {
    const [gpu, encodes] = await Promise.all([readDisplays(), hevc()]);
    let chip = '';
    try {
      chip = cleanText(os.cpus()?.[0]?.model);
    } catch {
      chip = '';
    }
    chip = chip || gpu?.model || 'Apple Silicon';
    const hints = [];
    if (!encodes) hints.push(HINT_NO_HEVC);
    hints.push(HINT_AV1);
    return {
      gpus: [{
        node: 'VideoToolbox', vendor: 'apple', vendorName: 'Apple', name: `${chip.slice(0, 120)} GPU`, cores: gpu?.cores ?? null,
        driver: 'VideoToolbox', passedThrough: true, encodes: { h264: encodes, hevc: encodes, av1: false }, lowPowerOnly: false
      }],
      nvidia: [],
      hints
    };
  }

  return Object.freeze({ detect });
}

module.exports = { createDarwinGpus, HINT_NO_HEVC, HINT_AV1 };
