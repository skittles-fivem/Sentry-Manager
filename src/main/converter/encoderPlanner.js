'use strict';

// These conservative 8-bit 4:2:0 floors avoid known encoder initialization
// failures at impractically low bitrates. They are the only bitrate clamps;
// the planner deliberately has no maximum clamp that could defeat a byte target.
const SAFE_VIDEO_BITRATE_FLOORS = Object.freeze({ h265: 250_000, av1: 200_000 });
const RETRY_REASONS = Object.freeze({ PROFILE_SIZE: 'profile-size' });
const DEFAULT_PROBE_BITRATE = 500_000;
const DEFAULT_PROBE_TIMEOUT_MS = 10_000;
// A CPU encoder that ffmpeg lists is always there; a slow probe only means a
// busy or slow computer, so it gets longer before it counts as failed.
const CPU_PROBE_TIMEOUT_MS = 45_000;

const CANDIDATES = Object.freeze({
  h265: Object.freeze({
    win32: Object.freeze(['hevc_nvenc', 'hevc_qsv', 'hevc_amf']),
    darwin: Object.freeze(['hevc_videotoolbox']),
    linux: Object.freeze(['hevc_nvenc', 'hevc_qsv', 'hevc_amf', 'hevc_vaapi']),
    cpu: Object.freeze(['libx265'])
  }),
  av1: Object.freeze({
    win32: Object.freeze(['av1_nvenc', 'av1_qsv', 'av1_amf']),
    // FFmpeg has no AV1 VideoToolbox encoder, so a Mac encodes AV1 on the CPU.
    darwin: Object.freeze([]),
    linux: Object.freeze(['av1_nvenc', 'av1_qsv', 'av1_amf', 'av1_vaapi']),
    cpu: Object.freeze(['libsvtav1', 'libaom-av1'])
  })
});

function requireFamily(codec) {
  if (!Object.prototype.hasOwnProperty.call(CANDIDATES, codec)) {
    throw new RangeError(`Unsupported encoder family: ${codec}`);
  }
  return codec;
}

function requirePositive(name, value) {
  if (!Number.isFinite(value) || value <= 0) throw new RangeError(`${name} must be a positive finite number`);
  return value;
}

function requireNonNegative(name, value) {
  if (!Number.isFinite(value) || value < 0) throw new RangeError(`${name} must be a non-negative finite number`);
  return value;
}

function requirePositiveSafeInteger(name, value) {
  if (!Number.isSafeInteger(value) || value <= 0) throw new RangeError(`${name} must be a positive finite safe integer`);
  return value;
}

function bitrateFloor(codec) {
  return SAFE_VIDEO_BITRATE_FLOORS[requireFamily(codec)];
}

function encoderBelongsToFamily(codec, encoder) {
  const family = CANDIDATES[codec];
  return family.cpu.includes(encoder) || Object.entries(family)
    .filter(([key]) => key !== 'cpu')
    .map(([, value]) => value)
    .filter(Array.isArray)
    .some((encoders) => encoders.includes(encoder));
}

/**
 * Build output arguments shared by real probes and converter jobs.
 * The MP4 muxer is forced to avoid edit lists and shift negative timestamps to
 * zero while passthrough frame mode prevents implicit duplication or dropping.
 */
// Constant-quality settings per encoder family, for each named level. Scales
// differ by encoder (NVENC/QSV 0-51, AMF/VAAPI AV1 0-255, VideoToolbox 1-100
// where higher is better, x265 CRF 0-51, SVT-AV1/libaom CRF 0-63).
const QUALITY_VALUES = Object.freeze({
  hw51: Object.freeze({ high: 24, balanced: 28, small: 32 }),
  hw255: Object.freeze({ high: 96, balanced: 112, small: 128 }),
  videotoolbox: Object.freeze({ high: 65, balanced: 55, small: 45 }),
  x265: Object.freeze({ high: 22, balanced: 26, small: 30 }),
  av1crf: Object.freeze({ high: 30, balanced: 36, small: 42 })
});

function qualityRateArgs(codec, encoder, level) {
  if (!Object.prototype.hasOwnProperty.call(QUALITY_VALUES.hw51, level)) throw new RangeError(`Unknown quality level: ${level}`);
  const pick = (table) => String(QUALITY_VALUES[table][level]);
  if (encoder.endsWith('_nvenc')) return ['-rc', 'vbr', '-cq', pick('hw51'), '-b:v', '0'];
  if (encoder.endsWith('_qsv')) return ['-global_quality', pick('hw51')];
  if (encoder.endsWith('_amf')) {
    const qp = codec === 'av1' ? pick('hw255') : pick('hw51');
    return ['-rc', 'cqp', '-qp_i', qp, '-qp_p', qp];
  }
  // av1_vaapi has no -qp option (FFmpeg ignores it with a warning); every
  // VA-API encoder takes the constant-QP value from -global_quality instead.
  if (encoder.endsWith('_vaapi')) return codec === 'av1'
    ? ['-rc_mode', 'CQP', '-global_quality', pick('hw255')]
    : ['-rc_mode', 'CQP', '-qp', pick('hw51')];
  if (encoder.endsWith('_videotoolbox')) return ['-q:v', pick('videotoolbox')];
  if (encoder === 'libx265') return ['-crf', pick('x265')];
  if (encoder === 'libaom-av1') return ['-crf', pick('av1crf'), '-b:v', '0'];
  return ['-crf', pick('av1crf')]; // libsvtav1
}

// A bitrate (size profiles, custom bitrate). hevc_nvenc alone also gets a
// peak rate equal to the bitrate and a two-second buffer: with only -b:v its
// VBR mode left the average to the encoder, which went far over it on busy
// and night Tesla clips (more than twice it; GPU tier test on an RTX 4070 Ti, see
// tools/nvencRate.js), and a lower -b:v on the size retry barely changed the
// size. With the cap every such clip landed under its target on the first
// try, at a picture check score within 0.001 and SSIM within 0.002 at equal size.
// VBR rather than CBR so a still (Sentry) scene is not padded up to the
// bitrate. av1_nvenc met every target with -b:v alone and stays as it is.
function bitrateArgs(encoder, bitrate) {
  const rate = Math.round(bitrate);
  if (encoder === 'hevc_nvenc') return ['-rc', 'vbr', '-b:v', String(rate), '-maxrate', String(rate), '-bufsize', String(2 * rate)];
  return ['-b:v', String(rate)];
}

// A render node (Intel/AMD) or a CUDA index (NVIDIA) of an encoding device
// chosen per GPU; without one, every argument is what it always was.
function deviceNode(device) {
  const node = device?.node;
  if (typeof node !== 'string' || node.length === 0) throw new RangeError('device.node must be a non-empty path');
  return node;
}

function deviceIndex(device) {
  if (!Number.isSafeInteger(device?.index) || device.index < 0) throw new RangeError('device.index must be a non-negative integer');
  return String(device.index);
}

// Windows: an Intel or AMD GPU is a Direct3D 11 adapter, named to ffmpeg by
// its DXGI index (the order Windows lists the adapters in).
const isD3d11 = (device) => device?.kind === 'd3d11';
function deviceAdapter(device) {
  if (!Number.isSafeInteger(device?.adapter) || device.adapter < 0) throw new RangeError('device.adapter must be a non-negative integer');
  return String(device.adapter);
}

// The source a zero-copy tier may keep on the GPU: 8-bit 4:2:0 (what the
// hardware decoders hand over as NV12 without a conversion) with even
// dimensions. Anything else, or a source not inspected that far, decodes the
// way it always did. NVENC's own zero-copy tier predates this and is not
// limited by it.
const ZERO_COPY_PIXEL_FORMATS = new Set(['yuv420p', 'yuvj420p']);
function zeroCopySource(video) {
  if (!video || !ZERO_COPY_PIXEL_FORMATS.has(video.pixFmt)) return false;
  const even = (value) => Number.isSafeInteger(value) && value > 0 && value % 2 === 0;
  return even(video.width) && even(video.height);
}

// VideoToolbox on a Mac (a videotoolbox device, or platform 'darwin'):
// hardware only (-allow_sw 0), so a Mac that cannot reach its media engine
// (a VM) fails instead of encoding in software. Without either, the
// arguments are what they always were.
const videotoolboxOn = (encoder, device, platform) => encoder.endsWith('_videotoolbox')
  && (platform === 'darwin' || device?.kind === 'videotoolbox');

// holdFrames: a clip that starts before its first keyframe (see leadIn.js)
// gets the first decodable frame repeated that many times in front, so the
// encoded file has every frame of the original. tpad clones the frame it is
// given, so it runs on system-memory frames before any upload; GPU-resident
// (zero-copy) frames are not used for such clips (jobService).
function buildEncodeArgs({ codec, encoder, bitrate, quality = null, vaapiDevice = '/dev/dri/renderD128', hwFrames = false, device = null, holdFrames = 0, platform = null }) {
  requireFamily(codec);
  if (quality === null) requirePositive('bitrate', bitrate);
  if (typeof encoder !== 'string' || !encoderBelongsToFamily(codec, encoder)) {
    throw new RangeError(`Encoder ${encoder} is not a ${codec} candidate`);
  }
  if (!Number.isSafeInteger(holdFrames) || holdFrames < 0) throw new RangeError('holdFrames must be a non-negative integer');
  if (holdFrames > 0 && hwFrames) throw new RangeError('Held lead-in frames need system-memory frames');
  const hold = holdFrames > 0 ? [`tpad=start=${holdFrames}:start_mode=clone`] : [];

  const args = [];
  // Zero-copy input (decodeAccelArgs with zeroCopy) for VA-API, QSV and AMF:
  // the decoder's frames are already on the encoding GPU, so there is no
  // device to open and nothing to upload. QSV maps the VA-API or Direct3D 11
  // frames into its own (the input arguments set up the qs device).
  const zeroCopyOther = hwFrames && !encoder.endsWith('_nvenc');
  const d3d11 = isD3d11(device) && (encoder.endsWith('_qsv') || encoder.endsWith('_amf'));
  if (zeroCopyOther) {
    if (encoder.endsWith('_qsv')) args.push('-vf', 'hwmap=derive_device=qsv,format=qsv');
  } else if (encoder.endsWith('_vaapi')) {
    const node = device ? deviceNode(device) : vaapiDevice;
    if (typeof node !== 'string' || node.length === 0) throw new RangeError('vaapiDevice must be a non-empty path');
    args.push('-vaapi_device', node, '-vf', [...hold, 'format=nv12,hwupload'].join(','));
  } else if (d3d11 && encoder.endsWith('_amf')) {
    // Windows, AMF on a chosen adapter: AMF opens the system's default
    // adapter for frames in system memory, so they are uploaded to the
    // chosen one first and the encoder follows them there.
    args.push('-init_hw_device', `d3d11va=amfenc:${deviceAdapter(device)}`, '-filter_hw_device', 'amfenc',
      '-vf', [...hold, 'format=nv12,hwupload'].join(','));
  } else if (hold.length > 0) {
    args.push('-vf', hold.join(','));
  }
  // A global option: the QSV session opens on this GPU's render node
  // (Linux) or Direct3D 11 adapter (Windows).
  if (device && encoder.endsWith('_qsv') && !zeroCopyOther) {
    args.push('-init_hw_device', d3d11
      ? `qsv=qsvenc:hw_any,child_device_type=d3d11va,child_device=${deviceAdapter(device)}`
      : `qsv=qsvenc:hw_any,child_device=${deviceNode(device)}`);
  }

  args.push('-c:v', encoder);
  if (videotoolboxOn(encoder, device, platform)) args.push('-allow_sw', '0');
  if (device && encoder.endsWith('_nvenc')) args.push('-gpu', deviceIndex(device));
  if (quality !== null) args.push(...qualityRateArgs(codec, encoder, quality));
  else args.push(...bitrateArgs(encoder, bitrate));

  // GPU-resident input frames (zero-copy decode) are already 8-bit 4:2:0 and
  // cannot pass through a software pixel-format conversion.
  // (frames uploaded for AMF on a chosen adapter are GPU frames as well)
  if (hwFrames || (d3d11 && encoder.endsWith('_amf'))) { /* keep frames on the GPU */ }
  else if (encoder.endsWith('_qsv')) args.push('-pix_fmt', 'nv12');
  else if (!encoder.endsWith('_vaapi')) args.push('-pix_fmt', 'yuv420p');

  if (encoder.endsWith('_amf') && quality === null) args.push('-rc', 'vbr_peak');
  if (encoder === 'libx265') args.push('-preset', 'medium');
  if (encoder === 'libsvtav1') args.push('-preset', '8');
  if (encoder === 'libaom-av1') args.push('-cpu-used', '6', '-row-mt', '1');
  if (codec === 'h265') args.push('-bf', '0');

  args.push(
    '-tag:v', codec === 'h265' ? 'hvc1' : 'av01',
    '-fps_mode', 'passthrough',
    '-start_at_zero',
    '-avoid_negative_ts', 'make_zero',
    '-use_editlist', '0'
  );
  return args;
}

/**
 * Input arguments that decode on the same GPU family as a hardware encoder.
 * Decoded frames are copied back to system memory, so the encode arguments
 * stay identical with or without them. CPU encoders get no GPU decode.
 *
 * zeroCopy: the decoded frames stay in GPU memory and go straight into the
 * encoder (buildEncodeArgs with hwFrames). NVENC as always; VA-API and QSV on
 * Linux, AMF and QSV on Windows, each only on the GPU that encodes;
 * VideoToolbox on a Mac. null when the encoder has no such tier here (the
 * caller then decodes the usual way).
 */
function decodeAccelArgs(encoder, { platform = process.platform, vaapiDevice = '/dev/dri/renderD128', zeroCopy = false, device = null } = {}) {
  if (typeof encoder !== 'string') return null;
  // With a device, the GPU that encodes also decodes.
  const cudaDevice = () => (device ? ['-hwaccel_device', deviceIndex(device)] : []);
  const renderNode = () => (device ? deviceNode(device) : vaapiDevice);
  if (zeroCopy) return zeroCopyArgs(encoder, { platform, cudaDevice, renderNode, device });
  const vaapi = () => ['-hwaccel', 'vaapi', '-hwaccel_device', renderNode()];
  if (encoder.endsWith('_nvenc')) return ['-hwaccel', 'cuda', ...cudaDevice()];
  if (encoder.endsWith('_videotoolbox')) return ['-hwaccel', 'videotoolbox'];
  if (encoder.endsWith('_vaapi')) return vaapi();
  if (encoder.endsWith('_qsv') || encoder.endsWith('_amf')) {
    if (platform === 'win32') return ['-hwaccel', 'd3d11va', ...(isD3d11(device) ? ['-hwaccel_device', deviceAdapter(device)] : [])];
    if (platform === 'linux') return vaapi();
  }
  return null;
}

// The zero-copy input arguments per encoder (docs/plans/windows.md, section 8).
function zeroCopyArgs(encoder, { platform, cudaDevice, renderNode, device }) {
  // NVDEC frames stay in GPU memory and go straight into NVENC.
  if (encoder.endsWith('_nvenc')) return ['-hwaccel', 'cuda', '-hwaccel_output_format', 'cuda', ...cudaDevice()];
  // A Mac: VideoToolbox decodes into frames hevc_videotoolbox takes as they are.
  if (platform === 'darwin') {
    return encoder.endsWith('_videotoolbox') ? ['-hwaccel', 'videotoolbox', '-hwaccel_output_format', 'videotoolbox_vld'] : null;
  }
  if (platform === 'linux') {
    if (encoder.endsWith('_vaapi')) {
      return ['-hwaccel', 'vaapi', '-hwaccel_device', renderNode(), '-hwaccel_output_format', 'vaapi'];
    }
    // VA-API decodes; QSV maps the same surfaces (qs is derived from va).
    if (encoder.endsWith('_qsv')) {
      return ['-init_hw_device', `vaapi=va:${renderNode()}`, '-init_hw_device', 'qsv=qs@va', '-filter_hw_device', 'qs',
        '-hwaccel', 'vaapi', '-hwaccel_device', 'va', '-hwaccel_output_format', 'vaapi'];
    }
    return null;
  }
  if (platform === 'win32') {
    // Without a chosen adapter AMF encodes on the system's default one, which
    // is where a d3d11va device without an index opens too. QSV picks its
    // Intel GPU itself, which need not be the default adapter: no zero-copy
    // for it without a chosen one.
    const adapter = isD3d11(device) ? deviceAdapter(device) : null;
    const d3d11 = adapter === null ? 'd3d11va=dx' : `d3d11va=dx:${adapter}`;
    const input = ['-hwaccel', 'd3d11va', '-hwaccel_device', 'dx', '-hwaccel_output_format', 'd3d11'];
    if (encoder.endsWith('_amf')) return ['-init_hw_device', d3d11, ...input, '-extra_hw_frames', '16'];
    if (encoder.endsWith('_qsv') && adapter !== null) {
      return ['-init_hw_device', d3d11, '-init_hw_device', 'qsv=qs@dx', '-filter_hw_device', 'qs', ...input, '-extra_hw_frames', '8'];
    }
  }
  return null;
}

function failedProof(candidate, reason, details) {
  return { ...candidate, usable: false, reason, details: details || null };
}

function processFailure(result) {
  if (!result || result.timedOut || result.timeout) return 'timeout';
  if (result.error) return 'runner error result';
  if (result.status !== 0) return 'non-zero status';
  return null;
}

/**
 * Prove one advertised candidate with a short, real MP4 encode. The runner
 * contract is (command, argumentArray, options) and shell use is forbidden.
 */
async function probeCandidate({
  runProbe,
  ffmpegPath = 'ffmpeg',
  platform = process.platform,
  family,
  candidate,
  bitrate = DEFAULT_PROBE_BITRATE,
  timeoutMs = DEFAULT_PROBE_TIMEOUT_MS,
  vaapiDevice = '/dev/dri/renderD128',
  device = null
}) {
  if (typeof runProbe !== 'function') throw new TypeError('runProbe must be a function');
  requireFamily(family);
  requirePositiveSafeInteger('timeoutMs', timeoutMs);
  const normalized = typeof candidate === 'string'
    ? { codec: candidate, hardware: !CANDIDATES[family].cpu.includes(candidate) }
    : { ...candidate };
  // A Mac tests VideoToolbox with the quality setting its clips use, so a
  // broken -q:v shows up here once instead of failing every clip.
  const rate = platform === 'darwin' && String(normalized.codec).endsWith('_videotoolbox') ? { quality: 'balanced' } : { bitrate };
  let encodeArgs;
  try {
    encodeArgs = buildEncodeArgs({ codec: family, encoder: normalized.codec, ...rate, vaapiDevice, platform, ...(device ? { device } : {}) });
  } catch (error) {
    if (!device) throw error;
    return failedProof(normalized, 'invalid device', error.message);
  }
  const outputPath = platform === 'win32' ? 'NUL' : '/dev/null';
  const args = [
    '-hide_banner', '-loglevel', 'error', '-y',
    '-f', 'lavfi', '-i', 'testsrc2=duration=0.5:size=640x360:rate=8',
    '-an', '-frames:v', '4',
    ...encodeArgs,
    '-f', 'mp4', outputPath
  ];

  try {
    const result = await Promise.resolve(runProbe(ffmpegPath, args, {
      timeoutMs,
      windowsHide: true,
      shell: false
    }));
    const reason = processFailure(result);
    if (reason) return failedProof(normalized, reason, result.stderr || result.error?.message);
    return { ...normalized, usable: true, reason: null, probeArgs: args };
  } catch (error) {
    return failedProof(normalized, 'runner error', error.message);
  }
}

function encoderIsAdvertised(output, codec) {
  const escaped = codec.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(^|\\s)${escaped}(?=\\s|$)`, 'm').test(output);
}

const FAMILY_LABELS = Object.freeze({ h265: 'H.265', av1: 'AV1' });
const ENCODER_PREFIX = Object.freeze({ h265: 'hevc', av1: 'av1' });

// Hardware encoders worth trying on one GPU: on Linux QSV then VA-API on
// Intel, VA-API on AMD; on Windows QSV on Intel, AMF on AMD; NVENC on NVIDIA
// on both. A Mac's one VideoToolbox device encodes H.265 only (there is no
// AV1 VideoToolbox encoder). Other systems have no per-GPU choice.
function deviceCandidates(family, device, platform) {
  const prefix = ENCODER_PREFIX[family];
  if (platform === 'win32') {
    if (device.kind === 'd3d11' && device.vendor === 'intel') return [`${prefix}_qsv`];
    if (device.kind === 'd3d11' && device.vendor === 'amd') return [`${prefix}_amf`];
    if (device.kind === 'nvidia') return [`${prefix}_nvenc`];
    return [];
  }
  if (platform === 'darwin') return device.kind === 'videotoolbox' && family === 'h265' ? ['hevc_videotoolbox'] : [];
  if (platform !== 'linux') return [];
  if (device.kind === 'dri' && device.vendor === 'intel') return [`${prefix}_qsv`, `${prefix}_vaapi`];
  if (device.kind === 'dri' && device.vendor === 'amd') return [`${prefix}_vaapi`];
  if (device.kind === 'nvidia') return [`${prefix}_nvenc`];
  return [];
}

function platformCandidates(family, platform) {
  const hardware = CANDIDATES[family][platform] || [];
  return [
    ...hardware.map((codec) => Object.freeze({ codec, hardware: true })),
    ...CANDIDATES[family].cpu.map((codec) => Object.freeze({ codec, hardware: false }))
  ];
}

function createEncoderPlanner({
  runProbe,
  platform = process.platform,
  ffmpegPath = 'ffmpeg',
  timeoutMs = DEFAULT_PROBE_TIMEOUT_MS,
  vaapiDevice = '/dev/dri/renderD128'
} = {}) {
  if (typeof runProbe !== 'function') throw new TypeError('runProbe must be a function');
  requirePositiveSafeInteger('timeoutMs', timeoutMs);
  let advertisedPromise;
  const proofCache = new Map();
  const selectionCache = new Map();

  async function advertisedEncoders() {
    if (!advertisedPromise) {
      advertisedPromise = Promise.resolve().then(() => runProbe(ffmpegPath, ['-hide_banner', '-encoders'], {
        timeoutMs,
        windowsHide: true,
        shell: false
      })).then((result) => {
        const reason = processFailure(result);
        if (reason) throw new Error(`Encoder discovery failed: ${reason}`);
        return `${result.stdout || ''}\n${result.stderr || ''}`;
      }).catch((error) => {
        advertisedPromise = undefined;
        throw error instanceof Error && /Encoder discovery failed/.test(error.message)
          ? error
          : new Error(`Encoder discovery failed: ${error.message || error}`);
      });
    }
    return advertisedPromise;
  }

  async function prove(family, candidate, device = null) {
    const key = device ? `${family}:${candidate.codec}:${device.id}` : `${family}:${candidate.codec}`;
    if (!proofCache.has(key)) {
      const pending = probeCandidate({
        runProbe,
        ffmpegPath,
        platform,
        family,
        candidate,
        timeoutMs: candidate.hardware === false ? Math.max(timeoutMs, CPU_PROBE_TIMEOUT_MS) : timeoutMs,
        vaapiDevice,
        ...(device ? { device } : {})
      });
      proofCache.set(key, pending);
      // A probe that only ran out of time is tried again next time instead of
      // ruling the encoder out for the rest of the session.
      pending.then((proof) => {
        if (proof?.reason === 'timeout' && proofCache.get(key) === pending) proofCache.delete(key);
      }, () => {});
    }
    return proofCache.get(key);
  }

  async function selectFrom(family, candidates, cacheKey) {
    requireFamily(family);
    if (selectionCache.has(cacheKey)) return selectionCache.get(cacheKey);
    const output = await advertisedEncoders();
    for (const candidate of candidates) {
      if (!encoderIsAdvertised(output, candidate.codec)) continue;
      const proof = await prove(family, candidate);
      if (proof.usable) {
        const selected = Object.freeze({ family, codec: candidate.codec, hardware: candidate.hardware });
        selectionCache.set(cacheKey, selected);
        return selected;
      }
    }
    throw new Error(`No proven ${family} encoder is available`);
  }

  function selectCpuFallback(family) {
    requireFamily(family);
    return selectFrom(
      family,
      CANDIDATES[family].cpu.map((codec) => ({ codec, hardware: false })),
      `${family}:cpu`
    );
  }

  // The encoder for one chosen GPU, proven with that GPU's own arguments. A
  // GPU that cannot encode the family converts its clips on the CPU encoder.
  // With `probe: false` only an answer known already is given (null when
  // there is none), so a GPU that is converting right now is not tested:
  // a test encode it fails only for being busy would rule it out for good.
  const cpuWarned = new Set();
  async function selectFor(family, device, { probe = true } = {}) {
    requireFamily(family);
    if (!device || typeof device.id !== 'string') throw new TypeError('device is required');
    // Another server paired under Nodes: it chose and tested its own encoder,
    // so nothing is probed here; its clips are sent there (remote: true).
    if (device.kind === 'node') {
      const encoder = device.encoders?.[family];
      // offline, or it cannot encode this codec: like a GPU that cannot, the
      // lane's clips use this server's CPU encoder
      if (typeof encoder !== 'string' || !encoder) {
        const cpu = await selectCpuFallback(family);
        return Object.freeze({ family, codec: cpu.codec, hardware: false, device: Object.freeze({ id: device.id, kind: 'node' }) });
      }
      return Object.freeze({ family, codec: encoder, hardware: true, remote: true, device: Object.freeze({ id: device.id, kind: 'node', name: device.name || device.id }) });
    }
    if (device.kind === 'cpu') {
      if (!probe && !selectionCache.has(`${family}:cpu`)) return null;
      const cpu = await selectCpuFallback(family);
      return Object.freeze({ family, codec: cpu.codec, hardware: false, device: Object.freeze({ id: 'cpu', kind: 'cpu' }) });
    }
    const cacheKey = `${family}:device:${device.id}`;
    if (selectionCache.has(cacheKey)) return selectionCache.get(cacheKey);
    if (!probe) return null;
    const chosen = Object.freeze({
      id: device.id, kind: device.kind, node: device.node ?? null, index: device.index ?? null,
      // (Windows: the Direct3D 11 adapter of an Intel or AMD GPU)
      ...(device.kind === 'd3d11' ? { adapter: device.adapter ?? null } : {})
    });
    const output = await advertisedEncoders();
    let timedOut = false;
    for (const codec of deviceCandidates(family, device, platform)) {
      if (!encoderIsAdvertised(output, codec)) continue;
      const proof = await prove(family, { codec, hardware: true }, chosen);
      if (proof.usable) {
        const selected = Object.freeze({ family, codec, hardware: true, device: chosen });
        selectionCache.set(cacheKey, selected);
        return selected;
      }
      if (proof.reason === 'timeout') timedOut = true;
    }
    const cpu = await selectCpuFallback(family);
    // A probe that only ran out of time proves nothing: this time the CPU
    // encodes (timedOut says why), the GPU is tried again next time and is
    // not said to be unable.
    if (timedOut) return Object.freeze({ family, codec: cpu.codec, hardware: false, device: chosen, timedOut: true });
    if (!cpuWarned.has(cacheKey)) {
      cpuWarned.add(cacheKey);
      console.warn(`[CONVERTER] ${device.name || device.id} cannot encode ${FAMILY_LABELS[family]}; its conversions use the CPU encoder.`);
    }
    const selected = Object.freeze({ family, codec: cpu.codec, hardware: false, device: chosen });
    selectionCache.set(cacheKey, selected);
    return selected;
  }

  return Object.freeze({
    // `probe: false`: only a choice made already, else null (see selectFor)
    select(family, { probe = true } = {}) {
      requireFamily(family);
      if (!probe) return Promise.resolve(selectionCache.get(`${family}:preferred`) || null);
      return selectFrom(family, platformCandidates(family, platform), `${family}:preferred`);
    },
    selectFor,
    selectCpuFallback
  });
}

function calculateInitialBitrate({
  codec,
  targetBytes,
  durationSeconds,
  containerReserveBytes,
  telemetryReserveBytes
}) {
  requireFamily(codec);
  requirePositive('targetBytes', targetBytes);
  requirePositive('durationSeconds', durationSeconds);
  requireNonNegative('containerReserveBytes', containerReserveBytes);
  requireNonNegative('telemetryReserveBytes', telemetryReserveBytes);
  const reservedBytes = containerReserveBytes + telemetryReserveBytes;
  if (reservedBytes >= targetBytes) throw new RangeError('Container and telemetry reserve must be smaller than targetBytes');
  const calculated = Math.floor(((targetBytes - reservedBytes) * 8) / durationSeconds);
  return requirePositiveSafeInteger('calculated bitrate', Math.max(bitrateFloor(codec), calculated));
}

function calculateRetryBitrate(currentBitrate, actualBytes, targetBytes, options = {}) {
  requirePositive('currentBitrate', currentBitrate);
  requirePositive('actualBytes', actualBytes);
  requirePositive('targetBytes', targetBytes);
  const codec = typeof options === 'string' ? options : (options.codec || 'h265');
  const calculated = Math.round(currentBitrate * (targetBytes / actualBytes));
  return requirePositiveSafeInteger('calculated bitrate', Math.max(bitrateFloor(codec), calculated));
}

function decideSizeRetry({
  retryCount,
  failureReason,
  codec,
  encoder,
  currentBitrate,
  actualBytes,
  targetBytes
}) {
  if (!Number.isInteger(retryCount) || retryCount < 0) throw new RangeError('retryCount must be a non-negative integer');
  const correctionLimit = encoder === 'libaom-av1' ? 2 : 1;
  if (failureReason !== RETRY_REASONS.PROFILE_SIZE || retryCount >= correctionLimit) {
    return { retry: false, bitrate: null };
  }
  return {
    retry: true,
    bitrate: calculateRetryBitrate(currentBitrate, actualBytes, targetBytes, { codec })
  };
}

module.exports = {
  SAFE_VIDEO_BITRATE_FLOORS,
  decodeAccelArgs,
  zeroCopySource,
  deviceCandidates,
  RETRY_REASONS,
  buildEncodeArgs,
  probeCandidate,
  createEncoderPlanner,
  calculateInitialBitrate,
  calculateRetryBitrate,
  decideSizeRetry,
  nextBitrate: calculateRetryBitrate
};
