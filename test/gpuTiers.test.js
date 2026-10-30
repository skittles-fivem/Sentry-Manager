'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('path');

const {
  parseArgs, groupClipSets, pickTimestamps, cameraOrder, stderrTail, progressFrames,
  summaryRows, tierTotals, formatTable, summaryText, isInside,
  shortfallOf, zeroCopyEvidence, expectedHwFormat, streamPixFmt, evidenceLabel, exitCodeFor
} = require('../tools/gpuTiers');
const { decodeTierCandidates, ffmpegEncodeCommand } = require('../src/main/converter/jobService');
const { decodeAccelArgs } = require('../src/main/converter/encoderPlanner');

const names = (stamp, cameras) => cameras.map((camera) => `${stamp}-${camera}.mp4`);
const HW4 = ['front', 'back', 'left_pillar', 'right_pillar', 'left_repeater', 'right_repeater'];

test('clips are grouped by their minute; other files are ignored', () => {
  const groups = groupClipSets([
    ...names('2026-07-21_10-01-00', HW4),
    ...names('2026-07-21_10-00-00', ['front', 'back']),
    'event.json', 'thumb.png', '2026-07-21_10-00-00-front.MP4.part', 'notes.mp4'
  ]);
  assert.deepEqual(groups.map((group) => group.timestamp), ['2026-07-21_10-00-00', '2026-07-21_10-01-00']);
  assert.deepEqual(Object.keys(groups[0].cameras).sort(), ['back', 'front']);
  assert.equal(groups[1].cameras.left_pillar, '2026-07-21_10-01-00-left_pillar.mp4');
});

test('whole minutes only, never the last one, spread over the folder', () => {
  const stamps = Array.from({ length: 10 }, (_, n) => `2026-10-19_00-${String(n).padStart(2, '0')}-29`);
  const files = stamps.flatMap((stamp, n) => names(stamp, n === 4 ? ['front', 'back'] : ['front', 'back', 'left_repeater', 'right_repeater']));
  const groups = groupClipSets(files);
  const one = pickTimestamps(groups, 1);
  assert.equal(one.length, 1);
  assert.equal(Object.keys(one[0].cameras).length, 4);
  const three = pickTimestamps(groups, 3).map((group) => group.timestamp);
  assert.equal(new Set(three).size, 3);
  assert.ok(!three.includes(stamps[4]), 'a minute missing cameras is not picked');
  assert.ok(!three.includes(stamps[9]), 'the newest minute (often cut short) is not picked');
  // fewer whole minutes than asked: all of them
  assert.equal(pickTimestamps(groupClipSets(names(stamps[0], HW4)), 3).length, 1);
  assert.deepEqual(pickTimestamps([], 2), []);
});

test('the newest minute is dropped whenever there is more than one, and a short set is recorded', () => {
  const stamps = Array.from({ length: 3 }, (_, n) => `2026-10-19_00-0${n}-29`);
  const groups = groupClipSets(stamps.flatMap((stamp) => names(stamp, HW4)));
  // three whole minutes, three asked: the newest still goes
  const three = pickTimestamps(groups, 3);
  assert.deepEqual(three.map((group) => group.timestamp), stamps.slice(0, 2));
  assert.deepEqual(shortfallOf(three, 3), { asked: 3, picked: 2 });
  // two whole minutes, one asked: the older one
  assert.deepEqual(pickTimestamps(groups.slice(0, 2), 1).map((group) => group.timestamp), [stamps[0]]);
  // a single minute is all there is: kept, and short of two
  const one = pickTimestamps(groups.slice(0, 1), 2);
  assert.equal(one.length, 1);
  assert.deepEqual(shortfallOf(one, 2), { asked: 2, picked: 1 });
  assert.equal(shortfallOf(pickTimestamps(groups, 2), 2), null);
  // ten whole minutes, three asked: spread, newest never
  const ten = groupClipSets(Array.from({ length: 10 }, (_, n) => names(`2026-10-19_01-0${n}-29`, HW4)).flat());
  const picked = pickTimestamps(ten, 3).map((group) => group.timestamp);
  assert.equal(new Set(picked).size, 3);
  assert.ok(!picked.includes('2026-10-19_01-09-29'));
});

test('cameras in a fixed order', () => {
  const cameras = Object.fromEntries(['right_repeater', 'left_pillar', 'back', 'front', 'zzz'].map((camera) => [camera, `${camera}.mp4`]));
  assert.deepEqual(cameraOrder(cameras), ['front', 'back', 'left_pillar', 'right_repeater', 'zzz']);
});

test('arguments', () => {
  const options = parseArgs(['--clips', 'a', '--clips', 'b', '--out', 'o', '--minutes', '2', '--keep']);
  assert.deepEqual(options.clips, ['a', 'b']);
  assert.equal(options.minutes, 2);
  assert.equal(options.keep, true);
  assert.equal(options.ffmpeg, 'ffmpeg');
  assert.throws(() => parseArgs(['--out', 'o']), /--clips/);
  assert.throws(() => parseArgs(['--clips', 'a']), /--out/);
  assert.throws(() => parseArgs(['--clips', 'a', '--out', 'o', '--minutes', '0']), /--minutes/);
  assert.throws(() => parseArgs(['--clips', 'a', '--out', 'o', '--bogus']), /unknown/);
});

test('--out is never inside a clip folder (or the other way round)', () => {
  const clips = path.resolve('/x/clips');
  assert.equal(isInside(path.join(clips, 'out'), clips), true);
  assert.equal(isInside(clips, clips), true);
  assert.equal(isInside(path.resolve('/x/clips-out'), clips), false);
  assert.equal(isInside(path.resolve('/x'), path.resolve('/x/clips')), false);
});

test('ffmpeg output: last frame count and a short stderr tail', () => {
  assert.equal(progressFrames('frame=10\nfps=1\nprogress=continue\nframe=2160\nprogress=end\n'), 2160);
  assert.equal(progressFrames(''), null);
  const long = Array.from({ length: 40 }, (_, n) => `line ${n}`).join('\n');
  assert.equal(stderrTail(long, 3), 'line 37\nline 38\nline 39');
});

test('the tiers and command lines the app uses on NVENC (Windows)', () => {
  const device = { id: 'pci:0000:01:00.0', kind: 'nvidia', index: 0, node: null };
  const selected = { family: 'h265', codec: 'hevc_nvenc', hardware: true, device };
  const decodeAccel = (encoder, accel = {}) => decodeAccelArgs(encoder, { platform: 'win32', zeroCopy: accel.zeroCopy === true, ...(accel.device ? { device: accel.device } : {}) });
  const video = { pixFmt: 'yuvj420p', width: 1280, height: 960 };
  const tiers = decodeTierCandidates(selected, decodeAccel, { zeroCopy: true, video });
  assert.deepEqual(tiers.map((tier) => tier.key), ['pci:0000:01:00.0|hevc_nvenc:zero-copy', 'pci:0000:01:00.0|hevc_nvenc:gpu']);
  assert.deepEqual(tiers[0].args, ['-hwaccel', 'cuda', '-hwaccel_output_format', 'cuda', '-hwaccel_device', '0']);
  // held lead-in frames: no zero-copy
  assert.deepEqual(decodeTierCandidates(selected, decodeAccel, { zeroCopy: false, video }).map((tier) => tier.key), ['pci:0000:01:00.0|hevc_nvenc:gpu']);
  const { args } = ffmpegEncodeCommand({ source: 'in.mp4', target: 'out.mp4', codec: 'h265', encoder: selected, bitrate: 4_000_000, decodeAccel: tiers[0].args });
  assert.deepEqual(args.slice(0, 12), ['-nostdin', '-hide_banner', '-loglevel', 'error', '-n', '-hwaccel', 'cuda', '-hwaccel_output_format', 'cuda', '-hwaccel_device', '0', '-i']);
  assert.ok(!args.includes('-pix_fmt'), 'GPU frames are not converted');
  assert.deepEqual(args.slice(-2), ['mp4', 'out.mp4']);
  const cpu = ffmpegEncodeCommand({ source: 'in.mp4', target: 'out.mp4', codec: 'h265', encoder: selected, bitrate: 4_000_000, decodeAccel: null }).args;
  assert.equal(cpu[5], '-i');
  assert.ok(cpu.includes('-pix_fmt'));
  // AMF keeps frames on the GPU only for an 8-bit 4:2:0 source with even dimensions
  const amd = { id: 'pci:0000:7a:00.0', kind: 'd3d11', adapter: 1, node: null, index: null };
  const amf = { family: 'av1', codec: 'av1_amf', hardware: true, device: amd };
  assert.equal(decodeTierCandidates(amf, decodeAccel, { video }).length, 2);
  assert.equal(decodeTierCandidates(amf, decodeAccel, { video: { ...video, pixFmt: 'yuv420p10le' } }).length, 1);
  assert.equal(decodeTierCandidates(amf, decodeAccel, { video: { ...video, width: 1281 } }).length, 1);
  assert.deepEqual(decodeTierCandidates({ codec: 'libx265', hardware: false }, decodeAccel, { video }), []);
});

// What FFmpeg 8.1 printed at -loglevel verbose for the zero-copy command
// (-hwaccel cuda -hwaccel_output_format cuda ... -c:v hevc_nvenc) on water
// (RTX 4070 Ti, runs 41106279972 and 41111295446), frames on the GPU from decoder to encoder.
// The output stream line names the frames context's software format (nv12).
const ZERO_COPY_LOG = [
  '[h264 @ 00000178af41f000] Reinit context to 1280x960, pix_fmt: yuv420p',
  "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:\\gputiers\\sources\\2026-10-19_07-33-24-front.mp4':",
  '  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, progressive), 1280x960, 4994 kb/s, 36.02 fps, 36 tbr, 10k tbn (default)',
  "[vist#0:0/h264 @ 00000178af41edc0] Selecting decoder 'h264' because of requested hwaccel method cuda",
  '[vost#0:0/hevc_nvenc @ 00000178ad63dd00] Created video stream from input stream 0:0',
  'Stream mapping:',
  '  Stream #0:0 -> #0:0 (h264 (native) -> hevc (hevc_nvenc))',
  '[vost#0:0/hevc_nvenc @ 00000178ad63dd00] Starting thread...',
  '[h264 @ 00000178c664e980] NVDEC capabilities:',
  '[h264 @ 00000178c664e980] format supported: yes, max_mb_count: 65536',
  '[h264 @ 00000178c664e980] Reinit context to 1280x960, pix_fmt: cuda',
  '[graph -1 input from stream 0:0 @ 00000178c6f67c80] w:1280 h:960 pixfmt:cuda tb:1/10000 fr:36/1 sar:1/1 csp:unknown range:tv alpha:unspecified',
  '[vost#0:0/hevc_nvenc @ 00000178ad63dd00] [enc:hevc_nvenc @ 00000178af41e0c0] Using input frames context (format cuda) with hevc_nvenc encoder.',
  '[hevc_nvenc @ 00000178ad646a80] Loaded Nvenc version 13.1',
  '[hevc_nvenc @ 00000178ad646a80] Nvenc initialized successfully',
  "Output #0, mp4, to 'C:\\gputiers\\zero-copy-verbose.mp4':",
  '  Stream #0:0(und): Video: hevc (Main), 1 reference frame (hvc1 / 0x31637668), nv12(tv, progressive, left), 1280x960 [SAR 1:1 DAR 4:3], q=2-31, 3902 kb/s, 36 fps, 18432 tbn (default)',
  '[vost#0:0/hevc_nvenc @ 00000178ad63dd00] Non-monotonic DTS; previous: 733696, current: 733696; changing to 733697. This may result in incorrect timestamps in the output file.',
  '[out#0/mp4 @ 00000178ad640000] Output file #0 (C:\\gputiers\\zero-copy-verbose.mp4):'
].join('\r\n');

// The same command when NVDEC could not be set up: the decoder retried
// get_format without CUDA, frames came out in system memory, a software
// conversion was inserted and the encoder opened its own CUDA device.
const FALLBACK_LOG = [
  'Stream mapping:',
  '  Stream #0:0 -> #0:0 (h264 (native) -> hevc (hevc_nvenc))',
  '[h264 @ 0x2b0] Failed setup for format cuda: hwaccel initialisation returned error.',
  '[h264 @ 0x2b0] Reinit context to 1280x960, pix_fmt: yuv420p',
  '[graph -1 input from stream 0:0 @ 0x3c0] w:1280 h:960 pixfmt:yuv420p tb:1/10000 fr:36/1 sar:1/1 csp:unknown range:tv alpha:unspecified',
  "[format @ 0x3d0] auto-inserting filter 'auto_scale_0' between the filter 'Parsed_null_0' and the filter 'format'",
  '[vost#0:0/hevc_nvenc @ 0x2a0] [enc:hevc_nvenc @ 0x2c0] Using device 0 (type cuda) with hevc_nvenc encoder.',
  "Output #0, mp4, to 'x.mp4':",
  '  Stream #0:0: Video: hevc (Main) (hvc1 / 0x31637668), nv12(tv, progressive, left), 1280x960, q=2-31, 3902 kb/s, 36 fps, 18432 tbn (default)'
].join('\n');

test('zero-copy evidence: frames on the GPU from decoder to encoder (FFmpeg 8.1 as printed on water)', () => {
  const evidence = zeroCopyEvidence(ZERO_COPY_LOG, 'cuda', { withLog: true });
  assert.equal(evidence.ok, true, evidence.reason);
  // the probing decoder (before "Stream mapping:") is not the one that runs
  assert.deepEqual(evidence.decoder, ['cuda']);
  assert.deepEqual(evidence.filterInput, ['cuda']);
  assert.deepEqual(evidence.encoderInput, ['cuda']);
  assert.deepEqual(evidence.outputStream, ['nv12'], 'shown, not judged');
  assert.deepEqual(evidence.fallbacks, []);
  assert.equal(evidence.noiseLines, 1);
  assert.ok(evidence.lines.some((line) => /pixfmt:cuda/.test(line)));
  assert.ok(evidence.lines.some((line) => /Using input frames context \(format cuda\)/.test(line)));
  assert.ok(evidence.lines.some((line) => /requested hwaccel method cuda/.test(line)));
  assert.ok(evidence.lines.some((line) => /NVDEC capabilities/.test(line)));
  assert.ok(!evidence.lines.some((line) => /Non-monotonic/.test(line)));
  assert.equal(evidence.log[0], 'Stream mapping:');
  assert.ok(!evidence.log.some((line) => /Non-monotonic/.test(line)));
  assert.equal(zeroCopyEvidence(ZERO_COPY_LOG).log, undefined);
  assert.match(evidenceLabel(evidence), /^ok dec cuda > filter cuda > enc cuda$/);
  // AV1 the same way
  const av1 = zeroCopyEvidence(ZERO_COPY_LOG.replace(/hevc_nvenc/g, 'av1_nvenc').replace('hevc (Main)', 'av1 (Main)'), 'cuda');
  assert.equal(av1.ok, true, av1.reason);
  // a decoder context after the mapping that printed a software format fails
  const extra = zeroCopyEvidence(ZERO_COPY_LOG.replace('[out#0', '[h264 @ 0000018a03c47c80] Reinit context to 1280x960, pix_fmt: yuv420p\r\n[out#0'), 'cuda');
  assert.equal(extra.ok, false);
  assert.match(extra.reason, /decoder frames yuv420p/);
  assert.deepEqual(extra.decoderContexts.map((c) => c.format), ['cuda', 'yuv420p']);
});

test('zero-copy evidence: a fallback to system memory fails, whichever way it shows', () => {
  const evidence = zeroCopyEvidence(FALLBACK_LOG, 'cuda');
  assert.equal(evidence.ok, false);
  assert.deepEqual(evidence.decoder, ['yuv420p']);
  assert.deepEqual(evidence.filterInput, ['yuv420p']);
  assert.deepEqual(evidence.encoderInput, []);
  assert.deepEqual(evidence.encoderDevice, ['cuda 0']);
  assert.equal(evidence.fallbacks.length, 2);
  assert.match(evidence.reason, /fallback message; no decoder chose cuda; decoder frames yuv420p; filter input yuv420p; no cuda frames context at the encoder \(encoder device cuda 0\)/);
  assert.match(evidenceLabel(evidence), /^FAIL /);
  // each sign alone is enough
  const only = (line) => zeroCopyEvidence(ZERO_COPY_LOG.replace('[out#0', `${line}\n[out#0`), 'cuda');
  for (const line of [
    '[h264 @ 0x2b0] Failed setup for format cuda: hwaccel initialisation returned error.',
    '[h264 @ 0x2b0] Format cuda not usable, retrying get_format() without it.',
    '[vist#0:0/h264 @ 0x2b0] Hardware device setup failed for decoder: Generic error in an external library',
    "[format @ 0x3d0] auto-inserting filter 'auto_scale_0' between the filter 'Parsed_null_0' and the filter 'format'",
    '[Parsed_hwdownload_0 @ 0x3d0] hwdownload',
    // the graph reconfigured half way for system-memory frames
    '[graph -1 input from stream 0:0 @ 0x3c1] w:1280 h:960 pixfmt:yuv420p tb:1/10000 fr:36/1 sar:1/1'
  ]) assert.equal(only(line).ok, false, line);
  assert.equal(zeroCopyEvidence(ZERO_COPY_LOG.replace('pixfmt:cuda', 'pixfmt:nv12'), 'cuda').ok, false);
  assert.equal(zeroCopyEvidence(ZERO_COPY_LOG.replace('Using input frames context (format cuda)', 'Using input frames context (format d3d11)'), 'cuda').ok, false);
  assert.match(zeroCopyEvidence(ZERO_COPY_LOG.replace(/^.*Using input frames context.*$/m, ''), 'cuda').reason, /no cuda frames context at the encoder/);
  assert.match(zeroCopyEvidence(ZERO_COPY_LOG.replace('pix_fmt: cuda', 'pix_fmt: yuv420p'), 'cuda').reason, /no decoder chose cuda; decoder frames yuv420p/);
  // nothing printed about the frames: no proof
  const empty = zeroCopyEvidence('Stream mapping:\n  Stream #0:0 -> #0:0 (h264 (native) -> hevc (hevc_nvenc))\n', 'cuda');
  assert.equal(empty.ok, false);
  assert.match(empty.reason, /no filter graph input printed/);
});

test('zero-copy evidence helpers', () => {
  assert.equal(expectedHwFormat(['-hwaccel', 'cuda', '-hwaccel_output_format', 'cuda', '-i', 'x']), 'cuda');
  assert.equal(expectedHwFormat(['-hwaccel', 'cuda']), null);
  assert.equal(streamPixFmt('hevc (Main) (hvc1 / 0x31637668), cuda(pc, bt709, progressive, left), 1280x960, q=2-31'), 'cuda');
  assert.equal(streamPixFmt('hevc, 1 reference frame (hev1 / 0x31766568), yuv420p(tv, progressive, left), 320x240 (0x0)'), 'yuv420p');
  assert.equal(streamPixFmt('h264'), null);
});

test('exit code: 3 for an app failure, 2 for a harness problem, 0 only when all passed', () => {
  const ok = (over = {}) => ({ offered: true, ok: true, ...over });
  const base = (over) => ({ runs: [ok(), ok({ tier: 'zero-copy' }), { offered: false }], comparisons: [{ result: { ok: true } }], problems: [], ...over });
  assert.equal(exitCodeFor(base()), 0);
  for (const failedAt of ['encode', 'verify', 'picture', 'zero-copy']) {
    assert.equal(exitCodeFor(base({ runs: [ok(), ok({ ok: false, failedAt })] })), 3, failedAt);
  }
  assert.equal(exitCodeFor(base({ comparisons: [{ result: { ok: false, mean: 0.9 } }] })), 3);
  assert.equal(exitCodeFor(base({ comparisons: [{ result: null }] })), 3);
  assert.equal(exitCodeFor(base({ problems: ['clip: copy: EACCES'] })), 2);
  assert.equal(exitCodeFor(base({ runs: [{ offered: false }] })), 2, 'nothing ran');
  assert.equal(exitCodeFor(base({ runs: [] })), 2);
  // an app failure is reported even with a harness problem beside it
  assert.equal(exitCodeFor(base({ runs: [ok({ ok: false, failedAt: 'verify' })], problems: ['x'] })), 3);
  assert.equal(exitCodeFor(sampleReport()), 3);
});

function sampleReport() {
  const run = (over) => ({
    set: 'HW4', timestamp: 't', camera: 'front', device: 'pci:0000:01:00.0', deviceName: 'RTX', codec: 'h265', encoder: 'hevc_nvenc',
    tier: 'gpu', offered: true, ok: true, failedAt: null, wallSeconds: 10, frames: 2160, fps: 216, outputBytes: 60, sizeRatio: 0.8,
    verify: { ok: true }, picture: { ok: true, mean: 0.99 }, stderrTail: null, ...over
  });
  return {
    profile: 'balanced',
    verifyMode: 'full',
    devices: [{ id: 'pci:0000:01:00.0', name: 'RTX' }],
    sets: [{ name: 'HW4', timestamps: ['t'], sources: [{ camera: 'front', bytes: 75 * 1048576, probe: { width: 2896, height: 1876, pixFmt: 'yuvj420p', colorRange: 'pc', durationSeconds: 60 }, zeroCopyGuard: true, holdFrames: 0 }] }],
    runs: [
      run({ tier: 'zero-copy', ok: false, failedAt: 'encode', fps: null, verify: null, picture: null, stderrTail: 'Impossible to convert between the formats' }),
      run({ tier: 'gpu', fps: 200 }),
      run({ tier: 'cpu-decode', fps: 100, picture: { ok: false, reason: 'frames-missing' }, ok: false, failedAt: 'picture' }),
      run({ camera: 'left_repeater', tier: 'zero-copy', offered: false, notOffered: 'lead-in hold 3' })
    ],
    comparisons: [{ set: 'HW4', camera: 'front', deviceName: 'RTX', codec: 'h265', a: 'gpu', b: 'cpu-decode', result: { ok: true, mean: 0.995 } }],
    problems: []
  };
}

test('summary rows, per-tier totals and the table', () => {
  const report = sampleReport();
  const rows = summaryRows(report);
  assert.equal(rows.length, 4);
  assert.equal(rows[0].result, 'FAIL encode');
  assert.equal(rows[0].fps, '-');
  assert.equal(rows[1].result, 'ok');
  assert.equal(rows[1].picture, 'ok 0.99');
  assert.equal(rows[2].picture, 'FAIL frames-missing');
  assert.equal(rows[3].result, 'not offered (lead-in hold 3)');
  const totals = tierTotals(report);
  assert.deepEqual(totals.map((total) => [total.tier, total.ok, total['mean fps']]), [['zero-copy', '0/1', '-'], ['gpu', '1/1', '200'], ['cpu-decode', '0/1', '100']]);
  const table = formatTable([{ a: '1', b: 'long value' }, { a: '22', b: '' }], ['a', 'b']);
  assert.deepEqual(table.split('\n'), ['a  | b', '---|-----------', '1  | long value', '22 |']);
  assert.equal(formatTable([], ['a']), '(nothing ran)');
  const text = summaryText(report);
  assert.match(text, /HW4 \(t\)/);
  assert.match(text, /2896x1876/);
  assert.match(text, /Failures:\n- HW4 front hevc_nvenc zero-copy: encode\n {4}Impossible to convert/);
  assert.match(text, /gpu vs cpu-decode/);
  assert.match(text, /zero-copy evidence/);
  assert.match(text, /Exit code 3/);
  const withEvidence = sampleReport();
  withEvidence.runs[0] = { ...withEvidence.runs[0], ok: false, failedAt: 'zero-copy', stderrTail: null, fps: 300, verify: { ok: true }, picture: { ok: true, mean: 0.99 }, zeroCopyEvidence: zeroCopyEvidence(FALLBACK_LOG, 'cuda') };
  withEvidence.sets[0].short = { asked: 3, picked: 2 };
  const evidenceText = summaryText(withEvidence);
  assert.match(evidenceText, /only 2 of 3 whole minutes/);
  assert.match(evidenceText, /Zero-copy evidence[^\n]*\n- HW4 front hevc_nvenc: FAIL fallback message/);
  assert.match(evidenceText, /Failed setup for format cuda/);
  assert.match(evidenceText, /zero-copy: zero-copy \(fallback message/);
  assert.equal(summaryRows(withEvidence)[0]['zero-copy evidence'].startsWith('FAIL'), true);
  assert.equal(summaryRows(withEvidence)[1]['zero-copy evidence'], '-');
  assert.equal(tierTotals(withEvidence)[0]['frames on GPU'], '0/1');
});
