'use strict';

// The Jobs page's states for the parity pictures (capture.js VARIANTS, the
// native render list's page names): jobs (a conversion running with its
// energy so far, a finished scan with energy, average power and cost, a
// failed Recycle Bin job, an interrupted conversion, a cancelled policy run),
// jobs-running-only (every job running: Clear jobs is disabled), jobs-long
// (a long title, type and details). jobs-empty is the fixture server's own
// answer (no jobs). Times are from `now` (ms), the fixture's clock.

const H = 3600000;

function jobsVariants(vehicle, now) {
  const path = `GET /api/jobs?vehicle=${encodeURIComponent(vehicle)}`;
  const running = { id: 31, type: 'conversion', status: 'running', title: 'Convert 2026-10-30 (12 clips)', detail: '5 of 12 clips · H.265', startedAt: now - 754000, finishedAt: null, energy: { wh: 12.44, avgW: 61.3, cost: null } };
  const scan = { id: 30, type: 'scan', status: 'done', title: 'Scan Model 3', detail: '1,204 recordings', startedAt: now - 2 * H, finishedAt: now - 2 * H + 42400, energy: { wh: 412.6, avgW: 8.04, cost: 0.1449 } };
  const failed = { id: 29, type: 'bin', status: 'failed', title: 'Empty Recycle Bin', detail: 'EACCES: permission denied, unlink \'/footage/TeslaCam/RecentClips/2026-10-29_08-11-02-front.mp4\'', startedAt: now - 26 * H, finishedAt: now - 26 * H + 3725000, energy: null };
  const interrupted = { id: 28, type: 'conversion', status: 'interrupted', title: 'Convert 2026-10-28', detail: 'The server stopped during the conversion.', startedAt: now - 50 * H, finishedAt: null };
  const cancelled = { id: 27, type: 'policy', status: 'cancelled', title: 'Policy run', detail: '', startedAt: now - 51 * H, finishedAt: now - 51 * H + 61000, energy: null };
  const long = {
    id: 26,
    type: 'conversion-with-a-long-type-name',
    status: 'done',
    title: 'Convert 2026-10-27 Model 3 Long Range with a name far too long for one table cell at all',
    detail: 'TeslaCam/SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line · 48 of 48 clips',
    startedAt: now - 80 * H,
    finishedAt: now - 80 * H + 9 * H + 61000,
    energy: { wh: 5210, avgW: 245.5, cost: 1.5636 }
  };
  return {
    jobs: { route: 'jobs', api: { [path]: { jobs: [running, scan, failed, interrupted, cancelled] } } },
    'jobs-running-only': { route: 'jobs', api: { [path]: { jobs: [running, { ...running, id: 32, type: 'scan', title: 'Scan Model 3', detail: '', startedAt: now - 8000, energy: null }] } } },
    'jobs-long': { route: 'jobs', api: { [path]: { jobs: [long, running] } } }
  };
}

const JOBS_VARIANTS = ['jobs', 'jobs-running-only', 'jobs-long'];

module.exports = { jobsVariants, JOBS_VARIANTS };
