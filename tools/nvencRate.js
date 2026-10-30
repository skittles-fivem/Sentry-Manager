#!/usr/bin/env node
'use strict';

// NVENC rate-control experiment on real clips: the clips that came out over
// the size profile's target with hevc_nvenc (GPU tier test, run 41067105858)
// are copied out of the clip folders and encoded with hevc_nvenc and
// av1_nvenc under several rate-control variants, each at the bitrate the app
// would ask for (and once more at the app's corrected bitrate, as the size
// retry does). For each encode the report gives the size against the
// target, the app's picture check against the original, and SSIM/PSNR of
// the first seconds at full resolution.
//
//   node tools/nvencRate.js --ffmpeg ffmpeg.exe --ffprobe ffprobe.exe \
//     --clips D:\clips\2026-10-19 --clips D:\clips\2026-07-21 \
//     --out %TEMP%\nvencrate [--report report.json] [--only <file name>]...
//     [--variants app,bv,vbr-max1.5] [--ssim-seconds 20] [--keep]
//
// The clip folders are only read (each clip is copied into --out first);
// --out must not exist yet (or be empty) and is deleted at the end unless
// --keep. Variant 'app' is whatever buildEncodeArgs gives today; the others
// replace its rate arguments ('-b:v N' and anything the app adds for NVENC)
// with their own.

const fs = require('fs');
const path = require('path');
const childProcess = require('child_process');

const { decodeAccelArgs, createEncoderPlanner, calculateInitialBitrate, decideSizeRetry, SAFE_VIDEO_BITRATE_FLOORS } = require('../src/main/converter/encoderPlanner');
const { ffmpegEncodeCommand } = require('../src/main/converter/jobService');
const { createDeviceDiscovery } = require('../src/main/converter/encodeDevices');
const { createBoundedProcessRunner, cudaOrderOptions, parseVideoProbe } = require('../src/main/converter/runtime');
const { createConverterAdapters } = require('../src/main/converter/converterAdapters');
const { comparePictures, ffmpegDecoder } = require('../src/main/converter/pictureCheck');
const { extractH264TelemetryTimeline } = require('../src/main/converter/telemetryTimeline');
const { leadInHoldFrames } = require('../src/main/converter/leadIn');
const { targetOutputBytes, isWithinProfile } = require('../src/main/converter/contracts');
const { formatTable, isInside, stderrTail, progressFrames } = require('./gpuTiers');

const PROFILE_ID = 'balanced';
const ENCODE_TIMEOUT_MS = 20 * 60_000;
const STDERR_KEEP = 64 * 1024;

// The clips hevc_nvenc overshot (first try or after the size retry) in run
// 41067105858, and two it did not, as controls.
const DEFAULT_CLIPS = Object.freeze([
  '2026-10-19_07-33-24-front.mp4',
  '2026-10-19_07-33-24-left_repeater.mp4',
  '2026-10-19_07-33-24-back.mp4',
  '2026-07-21_20-00-02-front.mp4',
  '2026-07-21_20-00-02-right_pillar.mp4',
  '2026-07-21_20-00-02-left_repeater.mp4',
  '2026-07-21_04-00-49-front.mp4',
  '2026-07-21_12-00-35-front.mp4',
  '2026-10-19_20-32-31-front.mp4',
  '2026-07-21_04-00-49-back.mp4'
]);

// Rate-control variants: each gives the arguments that take the place of the
// app's rate arguments, from the bitrate b (bits per second).
const r = (value) => String(Math.round(value));
const VARIANTS = Object.freeze({
  app: null,
  bv: (b) => ['-b:v', r(b)],
  vbr: (b) => ['-rc', 'vbr', '-b:v', r(b)],
  'vbr-max1.0': (b) => ['-rc', 'vbr', '-b:v', r(b), '-maxrate', r(b), '-bufsize', r(2 * b)],
  'vbr-max1.5': (b) => ['-rc', 'vbr', '-b:v', r(b), '-maxrate', r(1.5 * b), '-bufsize', r(2 * b)],
  'vbr-max2.0': (b) => ['-rc', 'vbr', '-b:v', r(b), '-maxrate', r(2 * b), '-bufsize', r(2 * b)],
  cbr: (b) => ['-rc', 'cbr', '-b:v', r(b), '-bufsize', r(2 * b)],
  'vbr-mp-qres': (b) => ['-rc', 'vbr', '-b:v', r(b), '-multipass', 'qres'],
  'vbr-mp-full': (b) => ['-rc', 'vbr', '-b:v', r(b), '-multipass', 'fullres'],
  'vbr-la32': (b) => ['-rc', 'vbr', '-b:v', r(b), '-rc-lookahead', '32'],
  'vbr-aq': (b) => ['-rc', 'vbr', '-b:v', r(b), '-spatial-aq', '1', '-temporal-aq', '1'],
  'vbr-max1.5-mp-full': (b) => ['-rc', 'vbr', '-b:v', r(b), '-maxrate', r(1.5 * b), '-bufsize', r(2 * b), '-multipass', 'fullres'],
  'vbr-max1.5-mp-qres': (b) => ['-rc', 'vbr', '-b:v', r(b), '-maxrate', r(1.5 * b), '-bufsize', r(2 * b), '-multipass', 'qres']
});
const DEFAULT_VARIANTS = Object.freeze({
  h265: Object.keys(VARIANTS),
  av1: ['app', 'bv', 'vbr-max1.5', 'vbr-max1.5-mp-qres']
});

// The app's rate arguments in an encode argument list: '-b:v N' plus any
// '-rc', '-maxrate', '-bufsize', '-multipass', '-rc-lookahead', '-spatial-aq',
// '-temporal-aq' pair. Returns [args without them, index where they were].
const RATE_OPTIONS = new Set(['-b:v', '-rc', '-maxrate', '-bufsize', '-multipass', '-rc-lookahead', '-spatial-aq', '-temporal-aq', '-cq']);
function withRate(args, rate) {
  if (rate === null) return args.slice();
  const out = [];
  let at = -1;
  for (let i = 0; i < args.length; i += 1) {
    if (RATE_OPTIONS.has(args[i]) && i + 1 < args.length) {
      if (at === -1) at = out.length;
      i += 1;
      continue;
    }
    out.push(args[i]);
  }
  if (at === -1) throw new Error('no rate arguments to replace');
  out.splice(at, 0, ...rate);
  return out;
}

function parseArgs(argv) {
  const options = { clips: [], only: [], variants: null, ssimSeconds: 20, keep: false, retry: true, ffmpeg: null, ffprobe: null, out: null, report: null, codecs: ['h265', 'av1'] };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const value = () => {
      const next = argv[i + 1];
      if (next === undefined) throw new Error(`${arg} needs a value`);
      i += 1;
      return next;
    };
    if (arg === '--clips') options.clips.push(value());
    else if (arg === '--only') options.only.push(value());
    else if (arg === '--variants') {
      options.variants = value().split(',').map((name) => name.trim()).filter(Boolean);
      for (const name of options.variants) if (!Object.prototype.hasOwnProperty.call(VARIANTS, name)) throw new Error(`unknown variant: ${name}`);
    } else if (arg === '--codecs') {
      options.codecs = value().split(',').map((name) => name.trim()).filter(Boolean);
      for (const codec of options.codecs) if (!['h265', 'av1'].includes(codec)) throw new Error(`unknown codec: ${codec}`);
    } else if (arg === '--ssim-seconds') {
      const n = Number(value());
      if (!Number.isFinite(n) || n < 0 || n > 120) throw new Error('--ssim-seconds must be 0 to 120');
      options.ssimSeconds = n;
    } else if (arg === '--no-retry') options.retry = false;
    else if (arg === '--ffmpeg') options.ffmpeg = value();
    else if (arg === '--ffprobe') options.ffprobe = value();
    else if (arg === '--out') options.out = value();
    else if (arg === '--report') options.report = value();
    else if (arg === '--keep') options.keep = true;
    else throw new Error(`unknown argument: ${arg}`);
  }
  if (options.clips.length === 0) throw new Error('--clips is required');
  if (!options.out) throw new Error('--out is required');
  if (options.only.length === 0) options.only = DEFAULT_CLIPS.slice();
  options.ffmpeg = options.ffmpeg || 'ffmpeg';
  options.ffprobe = options.ffprobe || 'ffprobe';
  return options;
}

function runProcess(command, args) {
  return new Promise((resolve) => {
    const started = process.hrtime.bigint();
    let stdout = '';
    let stderr = '';
    let child;
    try {
      child = childProcess.spawn(command, args, { shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], ...cudaOrderOptions(args) });
    } catch (error) {
      resolve({ status: null, stdout, stderr: String(error?.message || error), seconds: 0 });
      return;
    }
    const timer = setTimeout(() => child.kill('SIGKILL'), ENCODE_TIMEOUT_MS);
    child.stdout.on('data', (chunk) => { stdout = (stdout + chunk).slice(-STDERR_KEEP); });
    child.stderr.on('data', (chunk) => { stderr = (stderr + chunk).slice(-STDERR_KEEP); });
    child.once('error', (error) => { stderr += `\n${error.message}`; });
    child.once('close', (status) => {
      clearTimeout(timer);
      resolve({ status, stdout, stderr, seconds: Number(process.hrtime.bigint() - started) / 1e9 });
    });
  });
}

// SSIM (all planes) and PSNR (average) of the first `seconds` of the encode
// against the original, both from zero, at full resolution.
async function fullResScore(ffmpeg, source, candidate, seconds) {
  if (!(seconds > 0)) return null;
  const t = String(seconds);
  const graph = '[0:v]setpts=PTS-STARTPTS,split[a1][a2];[1:v]setpts=PTS-STARTPTS,split[b1][b2];[a1][b1]ssim;[a2][b2]psnr';
  const result = await runProcess(ffmpeg, ['-nostdin', '-hide_banner', '-loglevel', 'info', '-t', t, '-i', candidate, '-t', t, '-i', source,
    '-lavfi', graph, '-f', 'null', '-']);
  const ssim = /SSIM [^\n]*All:([0-9.]+)/.exec(result.stderr);
  const psnr = /PSNR [^\n]*average:([0-9.]+|inf)/.exec(result.stderr);
  return { ssim: ssim ? Number(ssim[1]) : null, psnr: psnr ? (psnr[1] === 'inf' ? Infinity : Number(psnr[1])) : null };
}

const round = (value, places = 2) => (Number.isFinite(value) ? Math.round(value * 10 ** places) / 10 ** places : null);
const mb = (bytes) => (Number.isFinite(bytes) ? String(round(bytes / 1048576, 1)) : '-');

// a fixed sequence, so every variant's picture check samples the same places
function seeded(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state * 1664525 + 1013904223) >>> 0;
    return state / 4294967296;
  };
}

function summaryText(report) {
  const rows = report.runs.map((run) => ({
    clip: run.clip.replace(/\.mp4$/, ''),
    codec: run.codec,
    variant: run.variant,
    'in MB': mb(run.sourceBytes),
    'target MB': mb(run.limitBytes),
    'asked Mbps': run.attempts[0] ? String(round(run.attempts[0].bitrate / 1e6, 2)) : '-',
    'try1 MB': run.attempts[0] ? mb(run.attempts[0].bytes) : '-',
    'try2 MB': run.attempts[1] ? mb(run.attempts[1].bytes) : '-',
    result: run.error ? `ERROR ${run.error}` : run.pass ? (run.attempts.length > 1 ? 'ok after retry' : 'ok') : run.notSmaller ? 'not-smaller' : 'over',
    fps: run.attempts[0]?.fps == null ? '-' : String(run.attempts[0].fps),
    picture: run.picture ? (run.picture.ok ? String(run.picture.mean) : `FAIL ${run.picture.reason || run.picture.mean}`) : '-',
    ssim: run.score?.ssim == null ? '-' : String(round(run.score.ssim, 4)),
    psnr: run.score?.psnr == null ? '-' : String(round(run.score.psnr, 2))
  }));
  const totals = new Map();
  for (const run of report.runs) {
    const key = `${run.codec} ${run.variant}`;
    if (!totals.has(key)) totals.set(key, { codec: run.codec, variant: run.variant, clips: 0, firstTry: 0, pass: 0, ssim: 0, ssimN: 0, fps: 0, fpsN: 0, ratio: 0 });
    const t = totals.get(key);
    t.clips += 1;
    if (run.attempts[0] && run.attempts[0].bytes <= run.limitBytes) t.firstTry += 1;
    if (run.pass) t.pass += 1;
    if (Number.isFinite(run.score?.ssim)) { t.ssim += run.score.ssim; t.ssimN += 1; }
    if (Number.isFinite(run.attempts[0]?.fps)) { t.fps += run.attempts[0].fps; t.fpsN += 1; }
    if (run.attempts[0]) t.ratio += run.attempts[0].bytes / run.targetBytes;
  }
  const totalRows = [...totals.values()].map((t) => ({
    codec: t.codec,
    variant: t.variant,
    'first try in target': `${t.firstTry}/${t.clips}`,
    'in target (with retry)': `${t.pass}/${t.clips}`,
    'mean try1/target': String(round(t.ratio / t.clips, 3)),
    'mean ssim': t.ssimN ? String(round(t.ssim / t.ssimN, 4)) : '-',
    'mean fps': t.fpsN ? String(round(t.fps / t.fpsN, 0)) : '-'
  }));
  return [
    `NVENC rate control: profile ${report.profile}; ${report.host.ffmpeg || 'ffmpeg ?'}; ${report.host.nvidiaSmi || 'no nvidia-smi'}`,
    'target MB = the profile limit (target + tolerance); ssim/psnr = first seconds at full resolution against the original',
    '',
    formatTable(rows, ['clip', 'codec', 'variant', 'in MB', 'target MB', 'asked Mbps', 'try1 MB', 'try2 MB', 'result', 'fps', 'picture', 'ssim', 'psnr']),
    '',
    'Per variant:',
    formatTable(totalRows, ['codec', 'variant', 'first try in target', 'in target (with retry)', 'mean try1/target', 'mean ssim', 'mean fps']),
    ...(report.sources.length ? ['', 'Sources:', formatTable(report.sources.map((s) => ({
      clip: s.clip, MB: mb(s.bytes), size: s.size || '-', frames: String(s.frames ?? '-'), seconds: String(round(s.seconds, 2)),
      r_frame_rate: s.rFrameRate || '-', avg_frame_rate: s.avgFrameRate || '-', time_base: s.timeBase || '-'
    })), ['clip', 'MB', 'size', 'frames', 'seconds', 'r_frame_rate', 'avg_frame_rate', 'time_base'])] : []),
    ...(report.problems.length ? ['', 'Problems:', ...report.problems.map((p) => `- ${p}`)] : [])
  ].join('\n');
}

async function main(argv) {
  const options = parseArgs(argv);
  const out = path.resolve(options.out);
  const clipDirs = options.clips.map((dir) => path.resolve(dir));
  for (const dir of clipDirs) {
    if (isInside(out, dir) || isInside(dir, out)) throw new Error(`--out must be outside the clip folder ${dir}`);
  }
  if (fs.existsSync(out) && fs.readdirSync(out).length > 0) throw new Error(`--out ${out} is not empty`);
  fs.mkdirSync(out, { recursive: true });
  const reportPath = options.report ? path.resolve(options.report) : null;
  if (reportPath && isInside(reportPath, out)) throw new Error('--report must be outside --out');

  const platform = process.platform;
  const runner = createBoundedProcessRunner({ spawn: childProcess.spawn, timeoutMs: 30 * 60_000, maxOutputBytes: 8 * 1024 * 1024 });
  const version = await runner(options.ffmpeg, ['-hide_banner', '-version'], { timeoutMs: 30_000 }).catch(() => null);
  const nvidia = await runner('nvidia-smi', ['--query-gpu=name,driver_version', '--format=csv,noheader'], { timeoutMs: 15_000 }).catch(() => null);
  const report = {
    tool: 'nvencRate', version: 1, startedAt: new Date().toISOString(), finishedAt: null, profile: PROFILE_ID,
    host: { platform, ffmpeg: String(version?.stdout || '').split(/\r?\n/)[0] || null, nvidiaSmi: nvidia?.status === 0 ? nvidia.stdout.trim() : null },
    sources: [], runs: [], problems: []
  };

  try {
    const discovery = createDeviceDiscovery({ platform, runCommand: runner, requestHelper: () => Promise.reject(Object.assign(new Error('no helper'), { code: 'HELPER_UNAVAILABLE' })) });
    const device = (await discovery.list()).find((d) => d.kind === 'nvidia');
    if (!device) throw new Error('no NVIDIA GPU found');
    const planner = createEncoderPlanner({ runProbe: runner, platform, ffmpegPath: options.ffmpeg });
    const lanes = [];
    for (const codec of options.codecs) {
      const selected = await planner.selectFor(codec, device).catch((error) => { report.problems.push(`${codec}: ${error.message}`); return null; });
      if (selected?.hardware === true && selected.codec.endsWith('_nvenc')) lanes.push({ codec, selected });
      else report.problems.push(`${codec}: no NVENC encoder (${selected?.codec || 'none'})`);
    }
    const accel = (encoder, zeroCopy) => decodeAccelArgs(encoder, { platform, zeroCopy, device });
    const adapters = createConverterAdapters({
      fs: fs.promises, runFfprobe: runner, runFullDecode: runner,
      verifierOptions: { ffmpegPath: options.ffmpeg, ffprobePath: options.ffprobe }
    });
    const decodeGrey = ffmpegDecoder({ spawn: childProcess.spawn, ffmpegPath: options.ffmpeg, accel: async () => accel('hevc_nvenc', false) });

    for (const clip of options.only) {
      const dir = clipDirs.find((d) => fs.existsSync(path.join(d, clip)));
      if (!dir) { report.problems.push(`${clip}: not in any --clips folder`); continue; }
      const source = path.join(out, clip);
      fs.copyFileSync(path.join(dir, clip), source, fs.constants.COPYFILE_EXCL);
      const sourceBytes = fs.statSync(source).size;
      const probe = await runner(options.ffprobe, ['-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'stream=codec_name,duration,pix_fmt,width,height:format=duration', '-of', 'json', source], { timeoutMs: 120_000 });
      const inspected = parseVideoProbe(probe);
      const extra = await runner(options.ffprobe, ['-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'stream=r_frame_rate,avg_frame_rate,time_base,nb_frames', '-of', 'json', source], { timeoutMs: 120_000 });
      let stream = {};
      try { stream = JSON.parse(extra.stdout).streams?.[0] || {}; } catch (_) { stream = {}; }
      report.sources.push({
        clip, bytes: sourceBytes, size: `${inspected.width}x${inspected.height}`, seconds: inspected.durationSeconds,
        frames: stream.nb_frames ? Number(stream.nb_frames) : null, rFrameRate: stream.r_frame_rate || null,
        avgFrameRate: stream.avg_frame_rate || null, timeBase: stream.time_base || null
      });
      const timeline = extractH264TelemetryTimeline(fs.readFileSync(source));
      const holdFrames = leadInHoldFrames(timeline);
      const targetBytes = targetOutputBytes(sourceBytes, PROFILE_ID);
      const limitBytes = targetBytes + sourceBytes * 0.02;
      console.log(`[nvencRate] ${clip} (${mb(sourceBytes)} MB, limit ${mb(limitBytes)} MB)`);

      for (const { codec, selected } of lanes) {
        // the app's first bitrate for the size profile, 7% under on a GPU
        let start = calculateInitialBitrate({ codec, targetBytes, durationSeconds: inspected.durationSeconds, containerReserveBytes: 0, telemetryReserveBytes: 0 });
        start = Math.max(SAFE_VIDEO_BITRATE_FLOORS[codec], Math.floor(start * 0.93));
        const decodeAccel = accel(selected.codec, holdFrames === 0);
        const variants = options.variants || DEFAULT_VARIANTS[codec];
        for (const variant of variants) {
          const run = { clip, codec, encoder: selected.codec, variant, sourceBytes, targetBytes, limitBytes, attempts: [], pass: false, notSmaller: false, picture: null, score: null, args: null, error: null };
          report.runs.push(run);
          const target = path.join(out, `${clip}.${codec}.${variant}.mp4`);
          let bitrate = start;
          for (let retryCount = 0; ; retryCount += 1) {
            fs.rmSync(target, { force: true });
            const command = ffmpegEncodeCommand({ source, target, codec, encoder: selected, bitrate, holdFrames, decodeAccel });
            const rate = VARIANTS[variant];
            const args = rate === null ? command.args : withRate(command.args, rate(bitrate));
            run.args = args.map((arg) => (arg === source ? '<source>' : arg === target ? '<output>' : arg));
            const encoded = await runProcess(options.ffmpeg, args);
            if (encoded.status !== 0 || !fs.existsSync(target)) {
              run.error = `encode exit ${encoded.status}`;
              report.problems.push(`${clip} ${codec} ${variant}: ${stderrTail(encoded.stderr, 4)}`);
              break;
            }
            await adapters.writers[codec]({ candidatePath: target, timeline, item: {} });
            const bytes = fs.statSync(target).size;
            const frames = progressFrames(encoded.stdout);
            run.attempts.push({ bitrate, bytes, seconds: round(encoded.seconds, 2), fps: frames && encoded.seconds > 0 ? round(frames / encoded.seconds, 0) : null });
            run.notSmaller = bytes >= sourceBytes;
            run.pass = !run.notSmaller && isWithinProfile(sourceBytes, bytes, PROFILE_ID);
            if (run.pass || run.notSmaller || !options.retry) break;
            const retry = decideSizeRetry({ retryCount, failureReason: 'profile-size', codec, encoder: selected.codec, currentBitrate: bitrate, actualBytes: bytes, targetBytes });
            if (!retry.retry) break;
            bitrate = retry.bitrate;
          }
          if (!run.error) {
            run.picture = await comparePictures({ source, candidate: target, decode: decodeGrey, durationSeconds: inspected.durationSeconds, random: seeded(7) })
              .catch((error) => ({ ok: false, reason: 'compare-failed', detail: String(error?.message || error).slice(0, 120) }));
            run.score = await fullResScore(options.ffmpeg, source, target, options.ssimSeconds);
          }
          console.log(`[nvencRate]   ${selected.codec} ${variant}: ${run.attempts.map((a) => mb(a.bytes)).join(' -> ')} MB (limit ${mb(limitBytes)}) ${run.pass ? 'ok' : 'MISS'}, picture ${run.picture?.mean ?? '-'}, ssim ${run.score?.ssim ?? '-'}`);
          fs.rmSync(target, { force: true });
        }
      }
      if (!options.keep) fs.rmSync(source, { force: true });
    }
  } finally {
    report.finishedAt = new Date().toISOString();
    if (reportPath) {
      fs.mkdirSync(path.dirname(reportPath), { recursive: true });
      fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`);
    }
    if (!options.keep) fs.rmSync(out, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
  }
  const text = summaryText(report);
  if (reportPath) fs.writeFileSync(reportPath.replace(/\.json$/i, '') + '.txt', `${text}\n`);
  console.log('');
  console.log(text);
  return report;
}

module.exports = { parseArgs, withRate, VARIANTS, DEFAULT_CLIPS, summaryText, seeded };

if (require.main === module) {
  main(process.argv.slice(2)).then((report) => {
    process.exitCode = report.problems.length > 0 ? 2 : 0;
  }, (error) => {
    console.error(`[nvencRate] ${error?.stack || error}`);
    process.exitCode = 1;
  });
}
