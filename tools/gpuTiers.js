#!/usr/bin/env node
'use strict';

// Real-hardware test of the converter's decode tiers on every GPU of this
// computer (docs/plans/windows.md, section 8): a few whole minutes of Tesla
// clips are copied out of each clip folder, and each clip is encoded on each
// GPU, codec (H.265, AV1) and decode tier the app would try (zero-copy, GPU
// decode, CPU decode) with the app's own argument lists, then verified with
// the app's verifier and picture-checked against the original and against
// the other tiers' encodes.
//
//   node tools/gpuTiers.js --ffmpeg ffmpeg.exe --ffprobe ffprobe.exe \
//     --clips D:\clips\2026-10-19 --clips D:\clips\2026-07-21 \
//     --out %TEMP%\gputiers [--minutes 1] [--report report.json] [--keep] [--cpu]
//
// The clip folders are only read (each clip is copied into --out first);
// --out must not exist yet (or be empty) and is deleted at the end unless
// --keep. Windows: SENTRY_HELPER names SentryHelper.exe for the Intel/AMD
// adapters (without it only NVIDIA GPUs, through nvidia-smi, are found).
// --cpu adds the CPU encoder as a device (a baseline, slow).
//
// Every zero-copy encode is run once more, the same command at -loglevel
// verbose into a throwaway file, and fails unless what FFmpeg printed shows
// the frames on the GPU from decoder to encoder (zeroCopyEvidence).
// Exit code: 0 everything passed, 3 a run or a tier-vs-tier comparison
// failed (an app finding), 2 a harness problem.

const fs = require('fs');
const path = require('path');
const childProcess = require('child_process');

const { decodeAccelArgs, zeroCopySource, createEncoderPlanner, calculateInitialBitrate, decideSizeRetry, SAFE_VIDEO_BITRATE_FLOORS } = require('../src/main/converter/encoderPlanner');
const { decodeTierCandidates, ffmpegEncodeCommand } = require('../src/main/converter/jobService');
const { createDeviceDiscovery, CPU_DEVICE } = require('../src/main/converter/encodeDevices');
const { createBoundedProcessRunner, cudaOrderOptions, parseVideoProbe } = require('../src/main/converter/runtime');
const { createConverterAdapters } = require('../src/main/converter/converterAdapters');
const { createVerificationSnapshot } = require('../src/main/converter/verificationSnapshot');
const { comparePictures, ffmpegDecoder } = require('../src/main/converter/pictureCheck');
const { extractH264TelemetryTimeline } = require('../src/main/converter/telemetryTimeline');
const { leadInHoldFrames } = require('../src/main/converter/leadIn');
const { targetOutputBytes } = require('../src/main/converter/contracts');

const PROFILE_ID = 'balanced';
const VERIFY_MODE = 'full';
const CODECS = Object.freeze(['h265', 'av1']);
const TIERS = Object.freeze(['zero-copy', 'gpu', 'cpu-decode']);
const CLIP_NAME = /^(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})-([a-z_]+)\.mp4$/i;
const ENCODE_TIMEOUT_MS = 20 * 60_000;
const STDERR_KEEP = 64 * 1024;

// ---------------------------------------------------------------- pure parts

function parseArgs(argv) {
  const options = { clips: [], minutes: 1, keep: false, cpu: false, ffmpeg: null, ffprobe: null, out: null, report: null };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const value = () => {
      const next = argv[i + 1];
      if (next === undefined) throw new Error(`${arg} needs a value`);
      i += 1;
      return next;
    };
    if (arg === '--clips') options.clips.push(value());
    else if (arg === '--ffmpeg') options.ffmpeg = value();
    else if (arg === '--ffprobe') options.ffprobe = value();
    else if (arg === '--out') options.out = value();
    else if (arg === '--report') options.report = value();
    else if (arg === '--minutes') {
      const n = Number(value());
      if (!Number.isSafeInteger(n) || n < 1 || n > 60) throw new Error('--minutes must be a whole number from 1 to 60');
      options.minutes = n;
    } else if (arg === '--keep') options.keep = true;
    else if (arg === '--cpu') options.cpu = true;
    else throw new Error(`unknown argument: ${arg}`);
  }
  if (options.clips.length === 0) throw new Error('--clips is required');
  if (!options.out) throw new Error('--out is required');
  options.ffmpeg = options.ffmpeg || 'ffmpeg';
  options.ffprobe = options.ffprobe || 'ffprobe';
  return options;
}

// Clip file names -> one entry per timestamp (minute), oldest first:
// [{ timestamp, cameras: { front: '2026-10-19_00-01-29-front.mp4', ... } }]
function groupClipSets(names) {
  const byTimestamp = new Map();
  for (const name of names) {
    const match = CLIP_NAME.exec(name);
    if (!match) continue;
    const [, timestamp, rawCamera] = match;
    const camera = rawCamera.toLowerCase();
    if (!byTimestamp.has(timestamp)) byTimestamp.set(timestamp, {});
    const cameras = byTimestamp.get(timestamp);
    if (!cameras[camera]) cameras[camera] = name;
  }
  return [...byTimestamp.keys()].sort().map((timestamp) => ({ timestamp, cameras: byTimestamp.get(timestamp) }));
}

// N whole minutes: only minutes with every camera the folder has, never the
// newest one when there is more than one (Tesla's last file of a recording is
// usually cut short), spread evenly over what is left. Fewer whole minutes
// than asked: all of them (the caller records the shortfall, shortfallOf).
function pickTimestamps(groups, count) {
  const cameraCount = (group) => Object.keys(group.cameras).length;
  const most = groups.reduce((max, group) => Math.max(max, cameraCount(group)), 0);
  let whole = groups.filter((group) => cameraCount(group) === most);
  if (whole.length > 1) whole = whole.slice(0, -1);
  if (whole.length <= count) return whole;
  const picked = [];
  for (let n = 0; n < count; n += 1) {
    const index = Math.floor(((n + 0.5) * whole.length) / count);
    picked.push(whole[Math.min(index, whole.length - 1)]);
  }
  return picked;
}

// A set with fewer whole minutes than asked: { asked, picked }, else null.
function shortfallOf(picked, asked) {
  return picked.length < asked ? { asked, picked: picked.length } : null;
}

// ---------------------------------------------------------------- zero-copy evidence
//
// A zero-copy run is repeated with -loglevel verbose (to a throwaway output)
// and what FFmpeg prints shows where the frames were:
//  - the decoder, once get_format has chosen (libavcodec h264_slice.c /
//    hevc): "[h264 @ 0x..] Reinit context to 1280x960, pix_fmt: cuda"
//  - the filter graph's input (libavfilter/buffersrc.c, verbose):
//    "[graph 0 input from stream 0:0 @ 0x..] w:1280 h:960 pixfmt:cuda tb:.."
//  - the encoder's input, in the output stream line (dump_format, info):
//    "Stream #0:0: Video: hevc (Main) (hvc1 / 0x31637668), cuda(pc, ..), .."
// A fallback shows as one of those in a system-memory format (nv12, yuv420p,
// yuvj420p) or as one of FFmpeg's fallback messages:
//  - libavcodec/decode.c hwaccel_init: "Failed setup for format cuda:
//    hwaccel initialisation returned error." (then get_format retries
//    without it: "Format cuda not usable, retrying get_format() without it.")
//  - decode.c: "Invalid setup for format cuda: missing configuration."
//  - fftools/ffmpeg_dec.c hw_device_setup_for_decode / ffmpeg_hw.c:
//    "Hardware device setup failed for decoder: ..", "Device creation failed: .."
//  - libavfilter/avfiltergraph.c: "auto-inserting filter 'auto_scale_0'
//    between .." (a software conversion) or a hwdownload in the graph.
const FALLBACK_PATTERNS = Object.freeze([
  /Failed setup for format/i,
  /hwaccel initiali[sz]ation returned error/i,
  /Invalid setup for format/i,
  /Format \S+ not usable/i,
  /Hardware device setup failed/i,
  /Device creation failed/i,
  /hwaccel[^\r\n]*cannot be initiali[sz]ed/i,
  /No device available for decoder/i,
  /auto-?inserting/i,
  /\bhwdownload\b/i,
  /Impossible to convert between the formats/i
]);
const DECODER_FORMAT = /^\[(h264|hevc|av1|vp9|mpeg2video)[^\]]*\] Reinit context to \d+x\d+, pix_fmt: ([a-z0-9_]+)/;
// (the buffer source's own line, whatever the graph calls it)
const FILTER_INPUT = /^\[([^\]]*)\] w:\d+ h:\d+ pixfmt:([a-z0-9_]+) tb:/;
const STREAM_VIDEO = /^\s*Stream #\d+:\d+[^:]*: Video: (.*)$/;
// Informational: NVENC/CUDA/hwaccel lines that say which device did what.
const HW_INFO = /Using auto hwaccel type|hwaccel|cuda|nvenc|CUDA capable devices|Nvenc|GPU #|Stream #\d+:\d+ -> #/i;

// the pixel format in a stream line's "Video: codec (..), fmt(..), WxH, .."
function streamPixFmt(description) {
  const parts = [];
  let depth = 0;
  let current = '';
  for (const char of description) {
    if (char === '(') depth += 1;
    if (char === ')') depth = Math.max(0, depth - 1);
    if (char === ',' && depth === 0) { parts.push(current.trim()); current = ''; continue; }
    current += char;
  }
  parts.push(current.trim());
  for (const part of parts.slice(1)) {
    const match = /^([a-z][a-z0-9_]*)(?:\(|$)/.exec(part);
    if (match) return match[1];
  }
  return null;
}

// The -hwaccel_output_format of an argument list: the format the frames are
// to stay in (cuda, d3d11, vaapi, videotoolbox_vld), else null.
function expectedHwFormat(args) {
  const index = (args || []).indexOf('-hwaccel_output_format');
  return index === -1 ? null : args[index + 1] || null;
}

// What a verbose zero-copy run printed -> { ok, expected, decoder, filterInput,
// encoderInput, fallbacks, lines, reason }.
function zeroCopyEvidence(stderr, expected = 'cuda') {
  const evidence = { ok: false, expected, decoder: [], filterInput: [], encoderInput: [], fallbacks: [], lines: [], reason: null };
  let inOutput = false;
  // (before "Stream mapping:" the decoder lines are avformat's probing, which
  // never uses the hwaccel)
  let mapped = false;
  const keep = (line) => { if (evidence.lines.length < 40 && !evidence.lines.includes(line)) evidence.lines.push(line); };
  for (const raw of String(stderr || '').split(/\r\n|\n|\r/)) {
    const line = raw.replace(/\s+$/, '');
    if (!line.trim()) continue;
    if (/^Stream mapping:/.test(line)) mapped = true;
    if (/^Output #\d+/.test(line)) inOutput = true;
    else if (/^Input #\d+/.test(line)) inOutput = false;
    let match;
    if (mapped && (match = DECODER_FORMAT.exec(line))) {
      if (!evidence.decoder.includes(match[2])) evidence.decoder.push(match[2]);
      keep(line);
    } else if ((match = FILTER_INPUT.exec(line))) {
      if (!evidence.filterInput.includes(match[2])) evidence.filterInput.push(match[2]);
      keep(line);
    } else if (inOutput && (match = STREAM_VIDEO.exec(line))) {
      const format = streamPixFmt(match[1]);
      if (format && !evidence.encoderInput.includes(format)) evidence.encoderInput.push(format);
      keep(line.trim());
    } else if (HW_INFO.test(line)) {
      keep(line.trim());
    }
    if (FALLBACK_PATTERNS.some((pattern) => pattern.test(line))) {
      evidence.fallbacks.push(line.trim());
      keep(line.trim());
    }
  }
  const wrong = (formats) => formats.filter((format) => format !== expected);
  const problems = [];
  if (evidence.fallbacks.length) problems.push('fallback message');
  if (wrong(evidence.decoder).length) problems.push(`decoder frames ${wrong(evidence.decoder).join('/')}`);
  if (wrong(evidence.filterInput).length) problems.push(`filter input ${wrong(evidence.filterInput).join('/')}`);
  if (wrong(evidence.encoderInput).length) problems.push(`encoder input ${wrong(evidence.encoderInput).join('/')}`);
  if (!evidence.filterInput.length && !evidence.encoderInput.length) problems.push('no frame format printed');
  evidence.ok = problems.length === 0;
  evidence.reason = problems.length ? problems.join('; ') : null;
  return evidence;
}

function evidenceLabel(evidence) {
  if (!evidence) return '-';
  const formats = `dec ${evidence.decoder.join('/') || '?'} > filter ${evidence.filterInput.join('/') || '?'} > enc ${evidence.encoderInput.join('/') || '?'}`;
  return evidence.ok ? `ok ${formats}` : `FAIL ${evidence.reason} (${formats})`;
}

// 0: every offered run and every tier-vs-tier comparison passed and the
// harness had nothing to report; 3: an app finding (a run failed its encode,
// verify, picture or zero-copy check, or two tiers' encodes differ); 2: a
// harness problem only (nothing ran, a clip could not be read, ...).
function exitCodeFor(report) {
  const runs = report?.runs || [];
  const offered = runs.filter((run) => run.offered !== false);
  if (offered.some((run) => !run.ok) || (report?.comparisons || []).some((c) => !c.result?.ok)) return 3;
  if ((report?.problems || []).length > 0 || offered.length === 0) return 2;
  return 0;
}

const CAMERA_ORDER = ['front', 'back', 'left_pillar', 'right_pillar', 'left_repeater', 'right_repeater'];
function cameraOrder(cameras) {
  const rank = (camera) => {
    const index = CAMERA_ORDER.indexOf(camera);
    return index === -1 ? CAMERA_ORDER.length : index;
  };
  return Object.keys(cameras).sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
}

function stderrTail(text, lines = 20) {
  const all = String(text || '').split(/\r?\n/).filter((line) => line.trim().length > 0);
  return all.slice(-lines).join('\n');
}

// the last "frame=N" of ffmpeg's -progress output
function progressFrames(stdout) {
  let frames = null;
  for (const line of String(stdout || '').split(/\r?\n/)) {
    const match = /^frame=\s*(\d+)\s*$/.exec(line);
    if (match) frames = Number(match[1]);
  }
  return frames;
}

const round = (value, places = 2) => (Number.isFinite(value) ? Math.round(value * 10 ** places) / 10 ** places : null);

function pictureLabel(result) {
  if (!result) return '-';
  if (result.ok) return `ok ${result.mean}`;
  return result.reason ? `FAIL ${result.reason}` : `FAIL ${result.mean ?? ''}/${result.low ?? ''}`.trim();
}

function verifyLabel(result) {
  if (!result) return '-';
  return result.ok ? 'ok' : `FAIL ${result.reason || 'unknown'}`;
}

// One row per set, camera, device, codec and tier, in the order they ran.
function summaryRows(report) {
  return report.runs.map((run) => ({
    set: run.set,
    camera: run.camera,
    device: run.deviceName || run.device,
    codec: run.codec,
    encoder: run.encoder,
    tier: run.tier,
    result: run.offered === false ? `not offered (${run.notOffered})` : run.ok ? 'ok' : `FAIL ${run.failedAt || ''}`.trim(),
    fps: run.fps === null || run.fps === undefined ? '-' : String(run.fps),
    seconds: run.wallSeconds === null || run.wallSeconds === undefined ? '-' : String(run.wallSeconds),
    ratio: run.sizeRatio === null || run.sizeRatio === undefined ? '-' : String(run.sizeRatio),
    verify: run.offered === false ? '-' : verifyLabel(run.verify),
    picture: run.offered === false ? '-' : pictureLabel(run.picture),
    'zero-copy evidence': run.tier !== 'zero-copy' || run.offered === false ? '-' : evidenceLabel(run.zeroCopyEvidence)
  }));
}

function formatTable(rows, columns) {
  if (rows.length === 0) return '(nothing ran)';
  const widths = columns.map((column) => Math.max(column.length, ...rows.map((row) => String(row[column] ?? '').length)));
  const line = (cells) => cells.map((cell, index) => String(cell ?? '').padEnd(widths[index])).join(' | ').trimEnd();
  return [line(columns), widths.map((width) => '-'.repeat(width)).join('-|-'), ...rows.map((row) => line(columns.map((column) => row[column])))].join('\n');
}

// Totals per device, codec and tier (fps averaged over the clips that ran).
function tierTotals(report) {
  const totals = new Map();
  for (const run of report.runs) {
    if (run.offered === false) continue;
    const key = `${run.deviceName || run.device}|${run.codec}|${run.tier}`;
    if (!totals.has(key)) {
      totals.set(key, { device: run.deviceName || run.device, codec: run.codec, tier: run.tier, clips: 0, ok: 0, verified: 0, pictureOk: 0, onGpu: 0, fpsSum: 0, fpsCount: 0 });
    }
    const total = totals.get(key);
    total.clips += 1;
    if (run.ok) total.ok += 1;
    if (run.verify?.ok) total.verified += 1;
    if (run.picture?.ok) total.pictureOk += 1;
    if (run.zeroCopyEvidence?.ok) total.onGpu += 1;
    if (Number.isFinite(run.fps)) { total.fpsSum += run.fps; total.fpsCount += 1; }
  }
  return [...totals.values()].map((total) => ({
    device: total.device,
    codec: total.codec,
    tier: total.tier,
    clips: String(total.clips),
    ok: `${total.ok}/${total.clips}`,
    verified: `${total.verified}/${total.clips}`,
    picture: `${total.pictureOk}/${total.clips}`,
    'frames on GPU': total.tier === 'zero-copy' ? `${total.onGpu}/${total.clips}` : '-',
    'mean fps': total.fpsCount ? String(round(total.fpsSum / total.fpsCount, 1)) : '-'
  }));
}

function summaryText(report) {
  const parts = [];
  parts.push(`GPU tier test: ${report.sets.map((set) => `${set.name} (${set.timestamps.join(', ')}${set.short ? `; only ${set.short.picked} of ${set.short.asked} whole minutes` : ''})`).join('; ')}`);
  parts.push(`profile ${report.profile}, verify ${report.verifyMode}; devices: ${report.devices.map((d) => `${d.name} [${d.id}]`).join(', ') || 'none'}`);
  parts.push('');
  parts.push('Sources:');
  parts.push(formatTable(report.sets.flatMap((set) => set.sources.map((source) => ({
    set: set.name,
    camera: source.camera,
    MB: source.bytes ? String(round(source.bytes / 1048576, 1)) : '-',
    size: source.probe ? `${source.probe.width}x${source.probe.height}` : '-',
    pix_fmt: source.probe?.pixFmt || '-',
    range: source.probe?.colorRange || '-',
    seconds: source.probe ? String(round(source.probe.durationSeconds, 2)) : '-',
    'zero-copy guard': source.zeroCopyGuard ? 'allows' : 'refuses',
    'lead-in hold': source.holdFrames === null || source.holdFrames === undefined ? '-' : String(source.holdFrames)
  }))), ['set', 'camera', 'MB', 'size', 'pix_fmt', 'range', 'seconds', 'zero-copy guard', 'lead-in hold']));
  parts.push('');
  parts.push('Encodes:');
  parts.push(formatTable(summaryRows(report), ['set', 'camera', 'device', 'codec', 'tier', 'result', 'fps', 'seconds', 'ratio', 'verify', 'picture', 'zero-copy evidence']));
  parts.push('');
  parts.push('Per tier:');
  parts.push(formatTable(tierTotals(report), ['device', 'codec', 'tier', 'clips', 'ok', 'verified', 'picture', 'frames on GPU', 'mean fps']));
  if (report.comparisons.length) {
    parts.push('');
    parts.push('Tier against tier (picture check):');
    parts.push(formatTable(report.comparisons.map((c) => ({
      set: c.set, camera: c.camera, device: c.deviceName || c.device, codec: c.codec, pair: `${c.a} vs ${c.b}`, picture: pictureLabel(c.result)
    })), ['set', 'camera', 'device', 'codec', 'pair', 'picture']));
  }
  const evidenceRuns = report.runs.filter((run) => run.zeroCopyEvidence);
  if (evidenceRuns.length) {
    parts.push('');
    parts.push('Zero-copy evidence (the same command at -loglevel verbose):');
    for (const run of evidenceRuns) {
      parts.push(`- ${run.set} ${run.camera} ${run.encoder}: ${evidenceLabel(run.zeroCopyEvidence)}`);
      for (const line of run.zeroCopyEvidence.lines.slice(0, 12)) parts.push(`    ${line}`);
    }
  }
  const failedComparisons = report.comparisons.filter((c) => !c.result?.ok);
  const failures = report.runs.filter((run) => run.offered !== false && !run.ok);
  if (failures.length || failedComparisons.length || report.problems.length) {
    parts.push('');
    parts.push('Failures:');
    for (const run of failures) {
      parts.push(`- ${run.set} ${run.camera} ${run.encoder} ${run.tier}: ${run.failedAt}${run.verify && !run.verify.ok ? ` (${run.verify.reason})` : ''}${run.failedAt === 'zero-copy' ? ` (${run.zeroCopyEvidence?.reason})` : ''}`);
      const detail = run.stderrTail || run.verify?.decodeStderr;
      if (detail) parts.push(detail.split('\n').map((line) => `    ${line}`).join('\n'));
    }
    for (const c of failedComparisons) parts.push(`- ${c.set} ${c.camera} ${c.codec} ${c.a} vs ${c.b}: picture ${pictureLabel(c.result)}`);
    for (const problem of report.problems) parts.push(`- harness: ${problem}`);
  }
  parts.push('');
  parts.push(`Exit code ${exitCodeFor(report)} (0 all passed, 3 an app failure, 2 a harness problem)`);
  return parts.join('\n');
}

// ---------------------------------------------------------------- the run

function isInside(child, parent) {
  const relative = path.relative(path.resolve(parent), path.resolve(child));
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

// a fresh (or empty) --out that is nowhere near the clip folders
function prepareOut(out, clipDirs) {
  for (const dir of clipDirs) {
    if (isInside(out, dir) || isInside(dir, out)) throw new Error(`--out must be outside the clip folder ${dir}`);
  }
  if (fs.existsSync(out)) {
    if (fs.readdirSync(out).length > 0) throw new Error(`--out ${out} is not empty`);
  } else {
    fs.mkdirSync(out, { recursive: true });
  }
}

// Read-only snapshots and the like: made writable first so Windows lets
// them go.
function removeTree(target) {
  if (!fs.existsSync(target)) return;
  const walk = (entry) => {
    let stat;
    try { stat = fs.lstatSync(entry); } catch (_) { return; }
    if (stat.isDirectory()) for (const name of fs.readdirSync(entry)) walk(path.join(entry, name));
    try { fs.chmodSync(entry, stat.isDirectory() ? 0o700 : 0o600); } catch (_) { /* best effort */ }
  };
  walk(target);
  fs.rmSync(target, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
}

function runEncode(ffmpeg, args, { keepHead = 0 } = {}) {
  return new Promise((resolve) => {
    const started = process.hrtime.bigint();
    let stdout = '';
    let stderr = '';
    let head = '';
    let child;
    try {
      // as the app spawns it (runtime.js spawnProcess): CUDA numbers the
      // GPUs in PCI order when one is chosen by index
      child = childProcess.spawn(ffmpeg, args, { shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], ...cudaOrderOptions(args) });
    } catch (error) {
      resolve({ status: null, error: String(error?.message || error), stdout, stderr, seconds: 0 });
      return;
    }
    const timer = setTimeout(() => child.kill('SIGKILL'), ENCODE_TIMEOUT_MS);
    child.stdout.on('data', (chunk) => { stdout = (stdout + chunk).slice(-STDERR_KEEP); });
    child.stderr.on('data', (chunk) => {
      if (head.length < keepHead) head += String(chunk).slice(0, keepHead - head.length);
      stderr = (stderr + chunk).slice(-STDERR_KEEP);
    });
    child.once('error', (error) => { stderr += `\n${error.message}`; });
    child.once('close', (status, signal) => {
      clearTimeout(timer);
      const seconds = Number(process.hrtime.bigint() - started) / 1e9;
      resolve({ status, signal, stdout, stderr, head, seconds });
    });
  });
}

function versionLine(runner, command) {
  return runner(command, ['-hide_banner', '-version'], { timeoutMs: 30_000 })
    .then((result) => String(result.stdout || '').split(/\r?\n/)[0] || null)
    .catch(() => null);
}

async function main(argv) {
  const options = parseArgs(argv);
  const out = path.resolve(options.out);
  const clipDirs = options.clips.map((dir) => path.resolve(dir));
  prepareOut(out, clipDirs);
  const reportPath = options.report ? path.resolve(options.report) : null;
  if (reportPath && isInside(reportPath, out)) throw new Error('--report must be outside --out (it is deleted at the end)');
  for (const dir of clipDirs) if (reportPath && isInside(reportPath, dir)) throw new Error('--report must be outside the clip folders');

  const platform = process.platform;
  // The app's process limits (server/converterHost.js: 30 minutes, and the
  // runtime's default 1 MB of output, no maxProcessOutputBytes) for what the
  // app runs: ffprobe, the planner's probes and the verifier's full decode.
  // The larger one is for this harness's own diagnostics only.
  const appRunner = createBoundedProcessRunner({ spawn: childProcess.spawn, timeoutMs: 30 * 60_000 });
  const runner = createBoundedProcessRunner({ spawn: childProcess.spawn, timeoutMs: 30 * 60_000, maxOutputBytes: 8 * 1024 * 1024 });
  const report = {
    tool: 'gpuTiers',
    version: 1,
    startedAt: new Date().toISOString(),
    finishedAt: null,
    host: { platform, arch: process.arch, node: process.version, ffmpeg: await versionLine(runner, options.ffmpeg), nvidiaSmi: null, helper: null },
    profile: PROFILE_ID,
    verifyMode: VERIFY_MODE,
    minutes: options.minutes,
    devices: [],
    encoders: [],
    sets: [],
    runs: [],
    comparisons: [],
    problems: []
  };

  let helper = null;
  try {
    const nvidia = await runner('nvidia-smi', ['--query-gpu=index,name,driver_version,pci.bus_id', '--format=csv,noheader'], { timeoutMs: 15_000 }).catch(() => null);
    report.host.nvidiaSmi = nvidia && nvidia.status === 0 ? nvidia.stdout.trim() : null;

    // The devices, as the app finds them (encodeDevices.js); on Windows the
    // helper lists the Intel/AMD adapters when SENTRY_HELPER names it.
    let requestHelper = () => Promise.reject(Object.assign(new Error('no helper'), { code: 'HELPER_UNAVAILABLE' }));
    if (platform === 'win32' && process.env.SENTRY_HELPER && fs.existsSync(process.env.SENTRY_HELPER)) {
      const { createWinHelper } = require('../server/platform/winHelper');
      helper = createWinHelper({ path: process.env.SENTRY_HELPER });
      requestHelper = (op, args, requestOptions) => helper.request(op, args, requestOptions);
      report.host.helper = process.env.SENTRY_HELPER;
      try {
        report.host.adapters = (await helper.request('gpu.list', {}, { timeoutMs: 15_000 }))?.adapters || null;
      } catch (error) {
        report.problems.push(`helper gpu.list: ${error?.message || error}`);
      }
    }
    const discovery = createDeviceDiscovery({ platform, runCommand: runner, requestHelper });
    const devices = (await discovery.list()).filter((device) => device.kind !== 'cpu' || options.cpu);
    if (options.cpu && !devices.some((device) => device.kind === 'cpu')) devices.push(CPU_DEVICE);
    report.devices = devices.map((device) => ({ ...device }));

    const planner = createEncoderPlanner({ runProbe: appRunner, platform, ffmpegPath: options.ffmpeg });
    // Each device's encoder per codec, chosen and proven as a lane's is
    // (planner.selectFor); a GPU that cannot encode a codec is left out.
    const lanes = [];
    for (const device of devices) {
      for (const codec of CODECS) {
        let selected = null;
        let error = null;
        try { selected = await planner.selectFor(codec, device); } catch (err) { error = String(err?.message || err); }
        const usable = Boolean(selected) && (device.kind === 'cpu' ? selected.hardware === false : selected.hardware === true);
        report.encoders.push({ device: device.id, deviceName: device.name, codec, encoder: selected?.codec || null, hardware: selected?.hardware ?? null, usable, ...(error ? { error } : {}) });
        if (usable) lanes.push({ device, codec, selected });
      }
    }
    if (!lanes.some((lane) => lane.device.kind !== 'cpu') && !options.cpu) report.problems.push('no GPU encoder found');

    // The app's decode tiers come from runtime.js's decodeAccel.
    const decodeAccel = (encoder, accel = {}) => decodeAccelArgs(encoder, {
      platform, zeroCopy: accel.zeroCopy === true, ...(accel.device ? { device: accel.device } : {})
    });
    const snapshotRoot = path.join(out, 'snapshots');
    fs.mkdirSync(snapshotRoot, { recursive: true });
    const adapters = createConverterAdapters({
      fs: fs.promises,
      runFfprobe: appRunner,
      runFullDecode: appRunner,
      verifierOptions: {
        ffmpegPath: options.ffmpeg,
        ffprobePath: options.ffprobe,
        createSnapshot: (request) => createVerificationSnapshot({ ...request, tempRoot: snapshotRoot })
      }
    });
    // The picture check decodes on the GPU the app's default H.265 encoder
    // uses (runtime.js decodeGrey), the CPU when there is none.
    const decodeGrey = ffmpegDecoder({
      spawn: childProcess.spawn,
      ffmpegPath: options.ffmpeg,
      accel: async () => {
        const encoder = await planner.select('h265').catch(() => null);
        if (encoder?.hardware !== true) return null;
        return decodeAccelArgs(encoder.codec, { platform, ...(encoder.device ? { device: encoder.device } : {}) });
      }
    });
    const picture = (source, candidate, durationSeconds) => comparePictures({ source, candidate, decode: decodeGrey, durationSeconds })
      .catch((error) => ({ ok: false, reason: 'compare-failed', detail: String(error?.message || error).slice(0, 120) }));

    for (const dir of clipDirs) {
      const name = path.basename(dir);
      let names;
      try { names = fs.readdirSync(dir); } catch (error) {
        report.problems.push(`${dir}: ${error.message}`);
        continue;
      }
      const picked = pickTimestamps(groupClipSets(names), options.minutes);
      const set = { name, dir, timestamps: picked.map((group) => group.timestamp), sources: [] };
      const short = shortfallOf(picked, options.minutes);
      if (short) set.short = short;
      report.sets.push(set);
      if (picked.length === 0) report.problems.push(`${dir}: no Tesla clips`);
      const copies = path.join(out, 'sources', name);
      fs.mkdirSync(copies, { recursive: true });
      for (const group of picked) {
        for (const camera of cameraOrder(group.cameras)) {
          const fileName = group.cameras[camera];
          const original = path.join(dir, fileName);
          const source = path.join(copies, fileName);
          const entry = { timestamp: group.timestamp, camera, file: fileName, bytes: null, probe: null, zeroCopyGuard: false, holdFrames: null };
          set.sources.push(entry);
          try {
            // copy only: the original is read, nothing is written next to it
            fs.copyFileSync(original, source, fs.constants.COPYFILE_EXCL);
            entry.bytes = fs.statSync(source).size;
          } catch (error) {
            entry.error = `copy: ${error.message}`;
            report.problems.push(`${fileName}: ${entry.error}`);
            continue;
          }
          console.log(`[gpuTiers] ${name} ${group.timestamp} ${camera} (${round(entry.bytes / 1048576, 1)} MB)`);
          await runClip({ report, set, entry, source, lanes, options, runner: appRunner, diagnostics: runner, adapters, decodeAccel, picture, out });
        }
      }
    }
    if (!report.runs.some((run) => run.offered !== false)) report.problems.push('nothing ran');
  } finally {
    helper?.close();
    report.finishedAt = new Date().toISOString();
    if (reportPath) {
      fs.mkdirSync(path.dirname(reportPath), { recursive: true });
      fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`);
    }
    if (!options.keep) {
      try { removeTree(out); } catch (error) { console.error(`[gpuTiers] could not delete ${out}: ${error.message}`); }
    }
  }
  const text = summaryText(report);
  if (reportPath) fs.writeFileSync(reportPath.replace(/\.json$/i, '') + '.txt', `${text}\n`);
  console.log('');
  console.log(text);
  if (reportPath) console.log(`\nReport: ${reportPath}`);
  return report;
}

async function runClip({ report, set, entry, source, lanes, options, runner, diagnostics, adapters, decodeAccel, picture, out }) {
  // What the app reads first (runtime.js inspectVideo), then the colour
  // facts for the report.
  const appProbe = await runner(options.ffprobe, ['-v', 'error', '-select_streams', 'v:0',
    '-show_entries', 'stream=codec_name,duration,pix_fmt,width,height:format=duration', '-of', 'json', source], { timeoutMs: 120_000 });
  let inspected;
  try { inspected = parseVideoProbe(appProbe); } catch (error) {
    entry.error = `inspect: ${error.code || error.message}`;
    report.problems.push(`${entry.file}: ${entry.error} ${stderrTail(appProbe?.stderr, 5)}`);
    return;
  }
  const colour = await runner(options.ffprobe, ['-v', 'error', '-select_streams', 'v:0',
    '-show_entries', 'stream=profile,pix_fmt,color_range,color_space,color_primaries,color_transfer,bits_per_raw_sample,avg_frame_rate,nb_frames,bit_rate', '-of', 'json', source], { timeoutMs: 120_000 });
  let stream = {};
  try { stream = JSON.parse(colour.stdout).streams?.[0] || {}; } catch (_) { stream = {}; }
  entry.probe = {
    ...inspected,
    colorRange: stream.color_range || null,
    colorSpace: stream.color_space || null,
    profile: stream.profile || null,
    bitsPerRawSample: stream.bits_per_raw_sample || null,
    avgFrameRate: stream.avg_frame_rate || null,
    frames: stream.nb_frames ? Number(stream.nb_frames) : null,
    bitRate: stream.bit_rate ? Number(stream.bit_rate) : null
  };
  entry.zeroCopyGuard = zeroCopySource(inspected);

  let timeline;
  try {
    timeline = extractH264TelemetryTimeline(fs.readFileSync(source));
  } catch (error) {
    entry.error = `timeline: ${error.code || error.message}`;
    report.problems.push(`${entry.file}: ${entry.error} (the app fails such a clip before encoding)`);
    return;
  }
  const holdFrames = leadInHoldFrames(timeline);
  entry.holdFrames = holdFrames;
  const durationUs = Math.round((inspected.durationSeconds || 0) * 1_000_000) || undefined;

  for (const { device, codec, selected } of lanes) {
    // The app's bitrate for the size profile (jobService.js): the profile's
    // target over the clip's length, 7% under it on a GPU.
    const targetBytes = targetOutputBytes(entry.bytes, PROFILE_ID);
    let startBitrate = calculateInitialBitrate({ codec, targetBytes, durationSeconds: inspected.durationSeconds, containerReserveBytes: 0, telemetryReserveBytes: 0 });
    if (selected.hardware === true) startBitrate = Math.max(SAFE_VIDEO_BITRATE_FLOORS[codec], Math.floor(startBitrate * 0.93));
    const offered = decodeTierCandidates(selected, decodeAccel, { zeroCopy: holdFrames === 0, video: inspected });
    const tierArgs = {
      'zero-copy': offered.find((tier) => tier.key.endsWith(':zero-copy')) || null,
      gpu: offered.find((tier) => tier.key.endsWith(':gpu')) || null,
      'cpu-decode': { key: 'cpu', args: null }
    };
    const outputs = {};
    const dir = path.join(out, 'encodes', set.name, `${entry.timestamp}-${entry.camera}`, `${device.id.replace(/[^a-z0-9]+/gi, '_')}-${codec}`);
    fs.mkdirSync(dir, { recursive: true });
    // (a CPU encoder decodes on the CPU only)
    for (const tier of selected.hardware === true ? TIERS : ['cpu-decode']) {
      const run = {
        set: set.name, timestamp: entry.timestamp, camera: entry.camera, device: device.id, deviceName: device.name,
        codec, encoder: selected.codec, tier, offered: true, ok: false, failedAt: null,
        wallSeconds: null, frames: null, fps: null, bitrate: null, outputBytes: null, sizeRatio: null,
        attempts: [], verify: null, picture: null, zeroCopyEvidence: null, stderrTail: null, args: null
      };
      report.runs.push(run);
      const chosen = tierArgs[tier];
      if (!chosen || (tier !== 'cpu-decode' && !chosen.args)) {
        run.offered = false;
        run.notOffered = tier === 'zero-copy' && holdFrames > 0 ? `lead-in hold ${holdFrames}`
            : tier === 'zero-copy' && !entry.zeroCopyGuard ? 'source guard'
              : 'no arguments';
        continue;
      }
      let bitrate = startBitrate;
      let sizeRetryCount = 0;
      let lastArgs = null;
      const target = path.join(dir, `${tier}.mp4`);
      while (true) {
        removeTree(target);
        const { args } = ffmpegEncodeCommand({ source, target, codec, encoder: selected, bitrate, holdFrames, decodeAccel: chosen.args });
        run.args = args.map((arg) => (arg === source ? '<source>' : arg === target ? '<output>' : arg));
        run.bitrate = bitrate;
        const attempt = { bitrate };
        run.attempts.push(attempt);
        const encoded = await runEncode(options.ffmpeg, args);
        attempt.wallSeconds = round(encoded.seconds, 2);
        lastArgs = args;
        attempt.frames = progressFrames(encoded.stdout);
        run.wallSeconds = attempt.wallSeconds;
        run.frames = attempt.frames;
        run.fps = attempt.frames && encoded.seconds > 0 ? round(attempt.frames / encoded.seconds, 1) : null;
        if (encoded.status !== 0 || !fs.existsSync(target)) {
          run.failedAt = 'encode';
          run.exit = encoded.status ?? encoded.signal ?? encoded.error ?? null;
          run.stderrTail = stderrTail(encoded.stderr) || encoded.error || null;
          break;
        }
        if (encoded.stderr.trim()) run.stderrTail = stderrTail(encoded.stderr);
        attempt.encodedBytes = fs.statSync(target).size;
        try {
          await adapters.writers[codec]({ candidatePath: target, timeline, item: {} });
        } catch (error) {
          run.failedAt = 'telemetry';
          run.stderrTail = `${error.code || ''} ${error.message}`.trim();
          break;
        }
        run.outputBytes = fs.statSync(target).size;
        run.sizeRatio = round(run.outputBytes / entry.bytes, 3);
        // As jobService verifies: GPU decoding of the encode when the
        // encoder is a GPU's (the verifier re-checks a GPU failure on the CPU).
        const verifyAccel = selected.hardware === true ? decodeAccel(selected.codec, laneOptions(selected)) : null;
        let gpuVerifyFallbacks = 0;
        const verification = await adapters.verifier({
          source,
          candidate: target,
          codec,
          profileId: PROFILE_ID,
          sourceTimeline: timeline,
          sourceDurationUs: durationUs,
          verifyMode: VERIFY_MODE,
          ...(Array.isArray(verifyAccel) && verifyAccel.length ? { decodeAccel: verifyAccel, onDecodeAccelFallback: () => { gpuVerifyFallbacks += 1; } } : {})
        });
        if (verification?.publicationArtifact?.path) removeTree(path.dirname(verification.publicationArtifact.path));
        attempt.verify = { ok: verification?.ok === true, reason: verification?.reason || null, outputBytes: verification?.outputBytes ?? null };
        run.verify = {
          ok: verification?.ok === true,
          reason: verification?.reason || null,
          details: verification?.details || null,
          ...(gpuVerifyFallbacks ? { gpuDecodeFellBack: gpuVerifyFallbacks } : {})
        };
        if (verification?.reason === 'decode-failed') {
          // The verifier keeps only the kind of failure: the same complete
          // decode (verifier.js decodeArgs), on the CPU, says what ffmpeg
          // printed.
          const decode = await diagnostics(options.ffmpeg, ['-nostdin', '-hide_banner', '-loglevel', 'error', '-xerror', '-err_detect', 'explode',
            '-i', target, '-map', '0:v:0', '-an', '-sn', '-dn', '-fps_mode', 'passthrough', '-progress', 'pipe:1', '-nostats', '-f', 'null', '-'], { timeoutMs: 30 * 60_000 });
          run.verify.decodeStatus = decode?.status ?? null;
          // (less the null muxer's duplicate-dts lines the verifier tolerates)
          const tolerated = /^\[null[^\r\n]*\] Application provided invalid, non monotonically increasing dts to muxer in stream 0: \d+ >= \d+$/;
          const lines = String(decode?.stderr || '').split(/\r?\n/).filter((line) => line.trim().length > 0);
          run.verify.decodeToleratedLines = lines.filter((line) => tolerated.test(line)).length;
          run.verify.decodeStderr = lines.filter((line) => !tolerated.test(line)).slice(0, 12).map((line) => JSON.stringify(line).slice(1, -1)).join('\n') || null;
          run.verify.decodedFrames = progressFrames(decode?.stdout);
        }
        if (!verification?.ok) {
          // the app's size retry (decideSizeRetry): once more at a corrected bitrate
          const retry = decideSizeRetry({
            retryCount: sizeRetryCount, failureReason: verification?.reason, codec, encoder: selected.codec,
            currentBitrate: bitrate, actualBytes: verification?.outputBytes, targetBytes
          });
          if (retry.retry) {
            sizeRetryCount += 1;
            bitrate = retry.bitrate;
            continue;
          }
          run.failedAt = 'verify';
        }
        break;
      }
      if (run.failedAt === 'encode' || run.failedAt === 'telemetry') continue;
      outputs[tier] = target;
      run.picture = await picture(source, target, inspected.durationSeconds);
      if (tier === 'zero-copy') run.zeroCopyEvidence = await zeroCopyCheck(options.ffmpeg, lastArgs, target, path.join(dir, 'zero-copy-verbose.mp4'));
      run.ok = run.verify?.ok === true && run.picture?.ok === true && (tier !== 'zero-copy' || run.zeroCopyEvidence?.ok === true);
      if (!run.failedAt && !run.picture?.ok) run.failedAt = 'picture';
      if (!run.failedAt && tier === 'zero-copy' && !run.zeroCopyEvidence?.ok) run.failedAt = 'zero-copy';
      console.log(`[gpuTiers]   ${selected.codec} ${tier}: ${run.ok ? 'ok' : `FAIL ${run.failedAt}`} ${run.fps ?? '-'} fps, ratio ${run.sizeRatio}, verify ${verifyLabel(run.verify)}, picture ${pictureLabel(run.picture)}${tier === 'zero-copy' ? `, frames ${evidenceLabel(run.zeroCopyEvidence)}` : ''}`);
    }
    // the tiers' encodes against each other
    const present = TIERS.filter((tier) => outputs[tier]);
    for (let i = 0; i < present.length; i += 1) {
      for (let j = i + 1; j < present.length; j += 1) {
        const result = await picture(outputs[present[j]], outputs[present[i]], inspected.durationSeconds);
        report.comparisons.push({ set: set.name, timestamp: entry.timestamp, camera: entry.camera, device: device.id, deviceName: device.name, codec, a: present[i], b: present[j], result });
      }
    }
    if (!options.keep) removeTree(dir);
  }
  if (!options.keep) removeTree(source);
}

// The zero-copy encode's exact command again, at -loglevel verbose and into a
// throwaway file: what FFmpeg prints shows whether the frames stayed on the GPU.
async function zeroCopyCheck(ffmpeg, args, target, throwaway) {
  if (!Array.isArray(args)) return { ...zeroCopyEvidence('', null), reason: 'no command' };
  const level = args.indexOf('-loglevel');
  const verbose = args.map((arg, index) => (index === level + 1 && level !== -1 ? 'verbose' : arg === target ? throwaway : arg));
  removeTree(throwaway);
  const result = await runEncode(ffmpeg, verbose, { keepHead: 256 * 1024 });
  removeTree(throwaway);
  const evidence = zeroCopyEvidence(`${result.head}\n${result.stderr}`, expectedHwFormat(args) || 'cuda');
  evidence.exit = result.status ?? result.signal ?? null;
  if (result.status !== 0) {
    evidence.ok = false;
    evidence.reason = [evidence.reason, `verbose run exit ${evidence.exit}`].filter(Boolean).join('; ');
    evidence.stderrTail = stderrTail(result.stderr, 10);
  }
  return evidence;
}

function laneOptions(selected) {
  const device = selected?.device && selected.device.kind !== 'cpu' ? selected.device : null;
  return device ? { device } : {};
}

module.exports = {
  parseArgs,
  groupClipSets,
  pickTimestamps,
  shortfallOf,
  zeroCopyEvidence,
  expectedHwFormat,
  streamPixFmt,
  evidenceLabel,
  exitCodeFor,
  cameraOrder,
  stderrTail,
  progressFrames,
  summaryRows,
  tierTotals,
  formatTable,
  summaryText,
  isInside
};

if (require.main === module) {
  main(process.argv.slice(2)).then((report) => {
    process.exitCode = exitCodeFor(report);
  }, (error) => {
    // the harness could not run (arguments, folders, ...)
    console.error(`[gpuTiers] ${error?.stack || error}`);
    process.exitCode = 2;
  });
}
