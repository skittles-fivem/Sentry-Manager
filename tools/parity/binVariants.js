'use strict';

// The Recycle Bin page's states for the parity pictures (capture.js
// VARIANTS, the native render list's page names): bin (three items: one
// moved by you, one by the retention policy, one whose restore left a file
// behind because its name was taken), bin-long (a long label beside a short
// one). bin-empty is the fixture server's own answer (nothing in the
// Recycle Bin). Times are from `now` (ms), the fixture's clock.

const H = 3600000;
const MiB = 1024 * 1024;

function binVariants(vehicle, now) {
  const path = `GET /api/bin?vehicle=${encodeURIComponent(vehicle)}`;
  const saved = { id: 'b1', label: 'SavedClips/2026-10-29_18-04-11', bytes: 412 * MiB + 123456, files: [{ name: '2026-10-29_18-04-11-front.mp4' }, { name: '2026-10-29_18-04-11-back.mp4' }, { name: 'event.json' }], trashedAt: now - 2 * H, reason: 'manual' };
  const policy = { id: 'b2', label: 'RecentClips/2026-10-20', bytes: 3.2 * 1024 * MiB, files: Array.from({ length: 48 }, (_, i) => ({ name: `2026-10-20_${String(i).padStart(2, '0')}-front.mp4` })), trashedAt: now - 30 * H, reason: 'policy' };
  const conflict = { id: 'b3', label: 'SentryClips/2026-10-18_22-41-07', bytes: 96 * MiB, files: [{ name: '2026-10-18_22-41-07-front.mp4', conflict: true }, { name: '2026-10-18_22-41-07-back.mp4' }], trashedAt: now - 80 * H, reason: 'manual' };
  const long = { id: 'b4', label: 'SavedClips/2026-10-27_18-04-11/a-folder-name-that-is-far-too-long-to-fit-on-one-line-of-the-table', bytes: 5 * 1024 * 1024 * MiB, files: [{ name: 'x.mp4' }], trashedAt: now - 400 * H, reason: 'policy' };
  return {
    bin: { route: 'bin', api: { [path]: { items: [saved, policy, conflict] } } },
    'bin-long': { route: 'bin', api: { [path]: { items: [long, saved] } } }
  };
}

const BIN_VARIANTS = ['bin', 'bin-long'];

module.exports = { binVariants, BIN_VARIANTS };
