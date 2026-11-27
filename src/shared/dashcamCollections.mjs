import { getDashcamProfile } from './dashcamProfiles.mjs';

function timestampKeyToEpochMs(timestampKey) {
  const match = String(timestampKey || '').match(
    /^(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})$/
  );
  if (!match) return null;
  const [, year, month, day, hour, minute, second] = match;
  return new Date(
    Number(year),
    Number(month) - 1,
    Number(day),
    Number(hour),
    Number(minute),
    Number(second),
    0
  ).getTime();
}

function collectionDeletion(day, options) {
  const explicit = options.deletionByDate?.get(day);
  if (explicit) {
    return {
      allowed: explicit.allowed === true,
      path: explicit.allowed === true ? explicit.path || null : null
    };
  }
  if (options.sourceKind === 'gmc-raw-flat') {
    return { allowed: false, path: null };
  }
  return undefined;
}

function buildCollection(id, day, clipType, groups, options) {
  const profile = getDashcamProfile(options.profileId);
  const startEpochMs = timestampKeyToEpochMs(groups[0]?.timestampKey) ?? 0;
  const lastStart = timestampKeyToEpochMs(
    groups[groups.length - 1]?.timestampKey
  ) ?? startEpochMs;
  const endEpochMs =
    lastStart + profile.defaultSegmentDurationSeconds * 1000;
  const deletion = collectionDeletion(day, options);

  const collection = {
    id,
    key: id,
    day,
    profileId: profile.id,
    clipType,
    tag: clipType === 'Custom'
      ? profile.customCollectionLabel
      : clipType,
    groups,
    meta: null,
    durationMs: Math.max(1, endEpochMs - startEpochMs),
    segmentStartsMs: groups.map(group => {
      const timestamp = timestampKeyToEpochMs(group.timestampKey) ?? startEpochMs;
      return Math.max(0, timestamp - startEpochMs);
    }),
    anchorMs: 0,
    anchorGroupId: groups[0]?.id || null,
    sortEpoch: endEpochMs
  };

  if (deletion) collection.deletion = deletion;
  return collection;
}

// Saving a clip or a Sentry event makes the car move those minutes out of
// RecentClips into the event folder, which leaves a hole in the day's
// footage. As SentryUSB's player does, a hole between two RecentClips clips
// wider than one missing minute and at most 30 minutes is filled with the
// event clips of those minutes: one per minute, SavedClips before
// SentryClips. An event clip at most 30 s after a clip already there is the
// same footage under another path (Tesla stamps such twins 0-1 s apart).
const GAP_FILL_MIN_MS = 90 * 1000;
const GAP_FILL_MAX_MS = 30 * 60 * 1000;
const GAP_FILL_DUP_MS = 30 * 1000;

// a clip's wall-clock stamp counted as SentryUSB does, without a time zone:
// the gaps are the same on every server and across a daylight saving change;
// an impossible date or time (Feb 30, 25:00) is no stamp at all, as there
function clipStampMs(timestampKey) {
  const match = String(timestampKey || '').match(
    /^(\d{4})-(\d{2})-(\d{2})_(\d{2})-(\d{2})-(\d{2})$/
  );
  if (!match) return null;
  const [, year, month, day, hour, minute, second] = match.map(Number);
  const date = new Date(0);
  date.setUTCFullYear(year, month - 1, day);
  date.setUTCHours(hour, minute, second, 0);
  const same = date.getUTCFullYear() === year && date.getUTCMonth() === month - 1
    && date.getUTCDate() === day && date.getUTCHours() === hour
    && date.getUTCMinutes() === minute && date.getUTCSeconds() === second;
  return same ? date.getTime() : null;
}

const byTimestampKey = (a, b) =>
  String(a.timestampKey || '').localeCompare(String(b.timestampKey || ''));

function fillRecentHoles(recent, dayData) {
  const recentMs = [...new Set(recent
    .map(group => clipStampMs(group.timestampKey))
    .filter(ms => ms != null))].sort((a, b) => a - b);
  const holes = [];
  for (let i = 1; i < recentMs.length; i++) {
    const gap = recentMs[i] - recentMs[i - 1];
    if (gap > GAP_FILL_MIN_MS && gap <= GAP_FILL_MAX_MS) {
      holes.push([recentMs[i - 1], recentMs[i]]);
    }
  }
  if (!holes.length) return recent;

  // the first event clip of each minute stamp: SavedClips, then SentryClips,
  // each by event folder
  const picked = new Map();
  for (const events of [dayData.saved, dayData.sentry]) {
    for (const eventId of [...events.keys()].sort()) {
      for (const group of [...events.get(eventId)].sort(byTimestampKey)) {
        const ms = clipStampMs(group.timestampKey);
        if (ms != null && !picked.has(ms)) picked.set(ms, group);
      }
    }
  }
  const lastRecentAtOrBefore = ms => {
    let lo = 0;
    let hi = recentMs.length;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (recentMs[mid] <= ms) lo = mid + 1;
      else hi = mid;
    }
    return lo > 0 ? recentMs[lo - 1] : null;
  };

  const fills = [];
  let lastFilled = -Infinity;
  for (const ms of [...picked.keys()].sort((a, b) => a - b)) {
    if (!holes.some(([start, end]) => ms > start && ms < end)) continue;
    if (ms - lastRecentAtOrBefore(ms) <= GAP_FILL_DUP_MS) continue;
    if (ms - lastFilled <= GAP_FILL_DUP_MS) continue;
    // in the day these minutes are footage like the rest, not an event the
    // Viewer jumps to, marks or pins on the map: a copy without the event
    const fill = { ...picked.get(ms) };
    for (const key of ['eventId', 'eventMeta', 'eventJsonFile', 'eventPngFile', 'eventMp4File']) {
      delete fill[key];
    }
    fills.push(fill);
    lastFilled = ms;
  }
  return fills.length ? [...recent, ...fills].sort(byTimestampKey) : recent;
}

// Whether a collection plays files from inside a folder: a day's RecentClips
// timeline also plays the minutes the car moved into Saved and Sentry events.
export function collectionUsesFolder(collection, folderPath) {
  if (!collection?.groups?.length || !folderPath) return false;
  const norm = path => String(path || '').replace(/\\/g, '/');
  const inside = norm(folderPath).replace(/\/+$/, '') + '/';
  return collection.groups.some(group =>
    [...(group.filesByCamera?.values() || [])]
      .some(entry => norm(entry?.file?.path).startsWith(inside))
  );
}

export function buildDashcamCollections(groups, options = {}) {
  const normalizedOptions = {
    ...options,
    profileId: options.profileId || groups?.[0]?.profileId || 'tesla'
  };
  const byDay = new Map();
  const allDates = new Set();

  for (const group of groups || []) {
    const day = String(group.timestampKey || '').split('_')[0] || 'Unknown';
    const type = String(group.tag || '').toLowerCase();
    allDates.add(day);

    if (!byDay.has(day)) {
      byDay.set(day, {
        recent: [],
        sentry: new Map(),
        saved: new Map(),
        custom: []
      });
    }
    const dayData = byDay.get(day);

    if (type === 'recentclips') {
      dayData.recent.push(group);
    } else if (type === 'sentryclips' && group.eventId) {
      if (!dayData.sentry.has(group.eventId)) {
        dayData.sentry.set(group.eventId, []);
      }
      dayData.sentry.get(group.eventId).push(group);
    } else if (type === 'savedclips' && group.eventId) {
      if (!dayData.saved.has(group.eventId)) {
        dayData.saved.set(group.eventId, []);
      }
      dayData.saved.get(group.eventId).push(group);
    } else {
      dayData.custom.push(group);
    }
  }

  const collections = new Map();
  const sortGroups = list => list.sort((a, b) =>
    String(a.timestampKey || '').localeCompare(String(b.timestampKey || ''))
  );

  for (const [day, dayData] of byDay) {
    if (dayData.recent.length) {
      const id = `recent:${day}`;
      collections.set(id, buildCollection(
        id,
        day,
        'RecentClips',
        fillRecentHoles(sortGroups(dayData.recent), dayData),
        normalizedOptions
      ));
    }

    for (const [eventId, eventGroups] of dayData.sentry) {
      const id = `sentry:${day}:${eventId}`;
      const collection = buildCollection(
        id,
        day,
        'SentryClips',
        sortGroups(eventGroups),
        normalizedOptions
      );
      collection.eventId = eventId;
      collection.eventTime = eventId.split('_')[1]?.replace(/-/g, ':') || '';
      collections.set(id, collection);
    }

    for (const [eventId, eventGroups] of dayData.saved) {
      const id = `saved:${day}:${eventId}`;
      const collection = buildCollection(
        id,
        day,
        'SavedClips',
        sortGroups(eventGroups),
        normalizedOptions
      );
      collection.eventId = eventId;
      collection.eventTime = eventId.split('_')[1]?.replace(/-/g, ':') || '';
      collections.set(id, collection);
    }

    if (dayData.custom.length) {
      const id = `custom:${day}`;
      const collection = buildCollection(
        id,
        day,
        'Custom',
        sortGroups(dayData.custom),
        normalizedOptions
      );
      collection.isCustomStructure = true;
      collections.set(id, collection);
    }
  }

  return {
    collections,
    allDates: Array.from(allDates).sort().reverse(),
    dayData: byDay
  };
}
