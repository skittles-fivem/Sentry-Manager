'use strict';

// A day's RecentClips timeline plays through the minutes the car moved into
// a SavedClips or SentryClips event folder (src/shared/dashcamCollections.mjs),
// with the same rules as SentryUSB's player.
// clip stamps carry no time zone: the gaps must come out the same on a server
// in a zone with daylight saving time
process.env.TZ = 'America/New_York';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const load = () => import(pathToFileURL(path.join(__dirname, '../src/shared/dashcamCollections.mjs')).href);

const recent = (key) => ({ id: `recent-${key}`, timestampKey: key, tag: 'RecentClips' });
const saved = (eventId, key) => ({ id: `saved-${eventId}-${key}`, timestampKey: key, tag: 'SavedClips', eventId });
const sentry = (eventId, key) => ({ id: `sentry-${eventId}-${key}`, timestampKey: key, tag: 'SentryClips', eventId });
const day = (...times) => times.map(t => `2025-03-14_${t}`);
const recentKeys = (result, d = '2025-03-14') =>
  result.collections.get(`recent:${d}`).groups.map(g => `${g.tag}:${g.timestampKey.slice(11)}`);

test('a saved clip\'s minutes play inside the day', async () => {
  const { buildDashcamCollections } = await load();
  const recents = day('10-00-30', '10-00-40', '10-01-41', '10-02-42', '10-03-43', '10-04-43', '10-05-44',
    '10-17-45', '10-18-48', '10-19-49').map(recent);
  const event = '2025-03-14_10-17-53';
  const meta = { timestamp: '2025-03-14T10:17:53', reason: 'user_interaction_dashcam_icon_tapped' };
  const saves = day('10-06-44', '10-07-44', '10-08-45', '10-09-46', '10-10-47', '10-11-47', '10-12-48',
    '10-13-49', '10-14-49', '10-15-50', '10-16-50').map(k => ({
    ...saved(event, k), eventMeta: meta, eventJsonFile: { path: 'e.json' }, eventPngFile: { path: 'e.png' }, eventMp4File: { path: 'e.mp4' }
  }));
  const result = buildDashcamCollections([...saves.slice().reverse(), ...recents]);
  assert.deepEqual(recentKeys(result), [
    'RecentClips:10-00-30', 'RecentClips:10-00-40', 'RecentClips:10-01-41', 'RecentClips:10-02-42',
    'RecentClips:10-03-43', 'RecentClips:10-04-43', 'RecentClips:10-05-44',
    'SavedClips:10-06-44', 'SavedClips:10-07-44', 'SavedClips:10-08-45', 'SavedClips:10-09-46',
    'SavedClips:10-10-47', 'SavedClips:10-11-47', 'SavedClips:10-12-48', 'SavedClips:10-13-49',
    'SavedClips:10-14-49', 'SavedClips:10-15-50', 'SavedClips:10-16-50',
    'RecentClips:10-17-45', 'RecentClips:10-18-48', 'RecentClips:10-19-49'
  ]);
  const coll = result.collections.get('recent:2025-03-14');
  assert.equal(coll.segmentStartsMs.length, 21);
  assert.deepEqual(coll.segmentStartsMs.slice(6, 9), [314_000, 374_000, 434_000]);
  // the event itself and the day's own RecentClips list are as before
  const eventGroups = result.collections.get(`saved:2025-03-14:${event}`).groups;
  assert.equal(eventGroups.length, 11);
  assert.ok(eventGroups.every(g => g.eventId === event && g.eventMeta?.reason === 'user_interaction_dashcam_icon_tapped'));
  assert.equal(result.dayData.get('2025-03-14').recent.length, 10);
  assert.ok(result.dayData.get('2025-03-14').recent.every(g => g.tag === 'RecentClips'));
  // in the day the minutes are plain footage: no event to jump to or pin
  const fills = coll.groups.filter(g => g.tag === 'SavedClips');
  assert.equal(fills.length, 11);
  for (const g of fills) {
    for (const key of ['eventId', 'eventMeta', 'eventJsonFile', 'eventPngFile', 'eventMp4File']) {
      assert.equal(key in g, false, key);
    }
    const own = eventGroups.find(e => e.id === g.id);
    assert.ok(own && own !== g && own.timestampKey === g.timestampKey);
  }
});

test('a Sentry event fills a hole too; SavedClips wins a minute both have', async () => {
  const { buildDashcamCollections } = await load();
  const result = buildDashcamCollections([
    ...day('10-00-00', '10-01-00', '10-05-00').map(recent),
    sentry('2025-03-14_10-04-30', '2025-03-14_10-02-00'),
    sentry('2025-03-14_10-04-30', '2025-03-14_10-03-00'),
    saved('2025-03-14_10-06-00', '2025-03-14_10-02-00')
  ]);
  assert.deepEqual(recentKeys(result), [
    'RecentClips:10-00-00', 'RecentClips:10-01-00', 'SavedClips:10-02-00', 'SentryClips:10-03-00', 'RecentClips:10-05-00'
  ]);
});

test('twins and near duplicates are not added twice', async () => {
  const { buildDashcamCollections } = await load();
  const e = '2025-03-14_10-06-00';
  const result = buildDashcamCollections([
    ...day('10-00-00', '10-01-00', '10-05-00').map(recent),
    // 1 s and 30 s after a RecentClips clip: the same footage under the event path
    saved(e, '2025-03-14_10-01-01'),
    saved(e, '2025-03-14_10-01-30'),
    saved(e, '2025-03-14_10-02-00'),
    // 1 s and 30 s after a minute already filled, from another event folder
    sentry('2025-03-14_10-09-00', '2025-03-14_10-02-01'),
    sentry('2025-03-14_10-09-00', '2025-03-14_10-02-30'),
    // shortly BEFORE the next RecentClips clip: the event's last, cut-off minute
    saved(e, '2025-03-14_10-04-50')
  ]);
  assert.deepEqual(recentKeys(result), [
    'RecentClips:10-00-00', 'RecentClips:10-01-00', 'SavedClips:10-02-00', 'SavedClips:10-04-50', 'RecentClips:10-05-00'
  ]);
});

test('only holes wider than 90 s and at most 30 minutes, between two RecentClips clips', async () => {
  const { buildDashcamCollections } = await load();
  const e = '2025-03-14_23-59-00';
  const result = buildDashcamCollections([
    ...day('08-00-00', '08-01-30', '09-00-00', '09-30-00', '10-00-00', '10-31-00').map(recent),
    saved(e, '2025-03-14_07-59-00'), // before the day's first clip
    saved(e, '2025-03-14_08-00-45'), // in a 90 s gap: normal spacing, not a hole
    saved(e, '2025-03-14_09-15-00'), // in a hole of exactly 30 minutes
    saved(e, '2025-03-14_10-15-00'), // in a 31-minute gap: recording stopped
    saved(e, '2025-03-14_10-32-00') // after the day's last clip
  ]);
  assert.deepEqual(recentKeys(result), [
    'RecentClips:08-00-00', 'RecentClips:08-01-30', 'RecentClips:09-00-00', 'SavedClips:09-15-00',
    'RecentClips:09-30-00', 'RecentClips:10-00-00', 'RecentClips:10-31-00'
  ]);
});

test('a hole across midnight stays a hole; days without RecentClips get no timeline', async () => {
  const { buildDashcamCollections } = await load();
  const e = '2025-03-15_00-12-00';
  const result = buildDashcamCollections([
    recent('2025-03-14_23-55-00'),
    recent('2025-03-15_00-10-00'),
    saved(e, '2025-03-14_23-58-00'),
    saved(e, '2025-03-15_00-05-00'),
    saved('2025-03-16_12-00-00', '2025-03-16_11-58-00')
  ]);
  assert.deepEqual(recentKeys(result, '2025-03-14'), ['RecentClips:23-55-00']);
  assert.deepEqual(recentKeys(result, '2025-03-15'), ['RecentClips:00-10-00']);
  assert.equal(result.collections.has('recent:2025-03-16'), false);
  assert.equal(result.collections.get('saved:2025-03-16:2025-03-16_12-00-00').groups.length, 1);
});

test('limits: just inside each rule is filled', async () => {
  const { buildDashcamCollections } = await load();
  const e = '2025-03-14_23-59-00';
  const result = buildDashcamCollections([
    ...day('08-00-00', '08-01-31', '09-00-00', '09-30-01', '10-00-00', '10-05-00').map(recent),
    saved(e, '2025-03-14_08-00-45'), // in a 91 s gap
    saved(e, '2025-03-14_09-15-00'), // in a gap of 30 minutes and 1 s: not filled
    saved(e, '2025-03-14_10-00-31'), // 31 s after a RecentClips clip
    saved(e, '2025-03-14_10-01-02') // 31 s after a filled minute
  ]);
  assert.deepEqual(recentKeys(result), [
    'RecentClips:08-00-00', 'SavedClips:08-00-45', 'RecentClips:08-01-31', 'RecentClips:09-00-00',
    'RecentClips:09-30-01', 'RecentClips:10-00-00', 'SavedClips:10-00-31', 'SavedClips:10-01-02', 'RecentClips:10-05-00'
  ]);
});

test('a minute in two events comes from the first event folder by name', async () => {
  const { buildDashcamCollections } = await load();
  const result = buildDashcamCollections([
    ...day('10-00-00', '10-01-00', '10-05-00').map(recent),
    saved('2025-03-14_10-20-00', '2025-03-14_10-02-00'),
    saved('2025-03-14_10-10-00', '2025-03-14_10-02-00'),
    sentry('2025-03-14_10-30-00', '2025-03-14_10-03-00'),
    sentry('2025-03-14_10-25-00', '2025-03-14_10-03-00')
  ]);
  const ids = result.collections.get('recent:2025-03-14').groups.map(g => g.id);
  assert.deepEqual(ids, [
    'recent-2025-03-14_10-00-00', 'recent-2025-03-14_10-01-00',
    'saved-2025-03-14_10-10-00-2025-03-14_10-02-00', 'sentry-2025-03-14_10-25-00-2025-03-14_10-03-00',
    'recent-2025-03-14_10-05-00'
  ]);
});

test('daylight saving changes do not move the gaps', async () => {
  const { buildDashcamCollections } = await load();
  // clocks go back: 01:55 to 02:05 is a 10-minute hole on the car's clock
  const back = buildDashcamCollections([
    recent('2025-11-02_01-50-00'), recent('2025-11-02_01-55-00'), recent('2025-11-02_02-05-00'),
    saved('2025-11-02_02-06-00', '2025-11-02_02-00-00')
  ]);
  assert.deepEqual(recentKeys(back, '2025-11-02'),
    ['RecentClips:01-50-00', 'RecentClips:01-55-00', 'SavedClips:02-00-00', 'RecentClips:02-05-00']);
  // clocks go forward: 02:10 is before the 03:00 to 03:20 hole, not in it
  const forward = buildDashcamCollections([
    recent('2025-03-09_03-00-00'), recent('2025-03-09_03-20-00'),
    saved('2025-03-09_03-21-00', '2025-03-09_02-10-00')
  ]);
  assert.deepEqual(recentKeys(forward, '2025-03-09'), ['RecentClips:03-00-00', 'RecentClips:03-20-00']);
});

test('collectionUsesFolder: files inside the folder, whatever the separators', async () => {
  const { collectionUsesFolder } = await load();
  const group = (...paths) => ({ filesByCamera: new Map(paths.map((p, i) => [`cam${i}`, { file: { path: p } }])) });
  const day = { groups: [group('/f/RecentClips/2025-03-14/a-front.mp4'), group('/f/SavedClips/E1/b-front.mp4', '/f/SavedClips/E1/b-back.mp4')] };
  assert.equal(collectionUsesFolder(day, '/f/SavedClips/E1'), true);
  assert.equal(collectionUsesFolder(day, '/f/SavedClips/E1/'), true);
  assert.equal(collectionUsesFolder(day, '/f/SavedClips/E'), false);
  assert.equal(collectionUsesFolder(day, '/f/SentryClips/E1'), false);
  const windows = { groups: [group('C:\\cam\\SavedClips\\E1\\b-front.mp4')] };
  assert.equal(collectionUsesFolder(windows, 'C:\\cam\\SavedClips\\E1'), true);
  assert.equal(collectionUsesFolder(null, '/f/SavedClips/E1'), false);
  assert.equal(collectionUsesFolder(day, ''), false);
  assert.equal(collectionUsesFolder({ groups: [{}] }, '/f'), false);
});

test('impossible stamps are no clip times: they neither make nor fill a hole', async () => {
  const { buildDashcamCollections } = await load();
  const e = '2025-03-14_23-59-00';
  const result = buildDashcamCollections([
    ...day('10-00-00', '10-05-00').map(recent),
    recent('2025-03-14_10-02-61'), // no 61st second: does not split the hole
    saved(e, '2025-03-14_10-02-60'), // no 60th second
    saved(e, '2025-03-14_10-03-00'),
    ...day('11-00-00', '11-05-00').map(recent),
    saved(e, '2025-03-14_11-60-00') // no 60th minute (would be 12:00 if rolled over)
  ]);
  assert.deepEqual(recentKeys(result), [
    'RecentClips:10-00-00', 'RecentClips:10-02-61', 'SavedClips:10-03-00', 'RecentClips:10-05-00',
    'RecentClips:11-00-00', 'RecentClips:11-05-00'
  ]);
  const feb = buildDashcamCollections([
    recent('2025-02-28_23-50-00'), recent('2025-02-28_23-58-00'),
    saved('2025-03-01_00-00-00', '2025-02-28_23-54-00'),
    saved('2025-03-01_00-00-00', '2025-02-30_00-01-00') // February 30th
  ]);
  assert.deepEqual(recentKeys(feb, '2025-02-28'), ['RecentClips:23-50-00', 'SavedClips:23-54-00', 'RecentClips:23-58-00']);
  assert.equal(feb.collections.get('recent:2025-02-30'), undefined);
});
