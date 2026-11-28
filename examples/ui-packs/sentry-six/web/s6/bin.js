import { plural } from './format.js';
export function binNote(policy) {
  return policy?.enabled && Number(policy.binDays) > 0
    ? `Your cleanup rule deletes items for good ${plural(policy.binDays, 'day')} after they arrive here. Restore them before then.`
    : 'Nothing here is deleted until you empty the bin or press Delete forever. Restore puts footage back exactly where it was.';
}
export function expiresIn(item, policy, now = Date.now()) {
  return policy?.enabled && Number(policy.binDays) > 0 && Number.isFinite(Number(item.trashedAt))
    ? Math.max(
        0,
        Math.ceil((Number(item.trashedAt) + policy.binDays * 86400000 - Number(now)) / 86400000),
      )
    : null;
}

export function restoreResult(results) {
  const restored = results.reduce((n, r) => n + Number(r.restored || 0), 0);
  const sum = (k) => results.reduce((n, r) => n + Number(r[k] || 0), 0);
  const conflicts = sum('conflicts');
  const missing = sum('missing');
  const failed = sum('failed');
  const error = results.find((r) => Number(r.failed) > 0)?.error || 'unknown error';
  const why = [
    conflicts ? `${conflicts} could not be restored (a file with that name exists)` : '',
    missing ? `${missing} not found in the Recycle Bin` : '',
    failed ? `${failed} could not be moved back: ${error}` : '',
  ].filter(Boolean).join('; ');
  return {
    text: `Restored ${plural(restored, 'file')}${why ? `; ${why}` : ''}.`,
    bad: Boolean(why),
  };
}
