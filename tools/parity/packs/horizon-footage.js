'use strict';

// Horizon's Footage page (hz/pages/footage.js, the "track list") for the parity
// pictures: the page names RENDER-PAGES.md lists as "horizon <name>", how
// capture.js takes the web side, and the fixture variants the native side
// renders (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The pack asks its own paths (days?category=all&limit=100,
// recordings?category=all&limit=500[&date=]), answered here from the recorded
// answers of the built-in Footage page (its days, each day's recordings); the
// thumbnails are the recorded ones (the fixture's BYTES answers). The inventory
// is 5 minutes old on both sides. The web side opens a day and ticks
// checkboxes with clicks; the native side gets the same as the variant's
// sub (?day=) and state ({ sel: [ids] }).

const DAYS_PATH = (v) => `/api/vehicles/${encodeURIComponent(v)}/days?${new URLSearchParams({ category: 'all', offset: '0', limit: '30' })}`;
const DAY_RECS = (v, day) => `/api/vehicles/${encodeURIComponent(v)}/recordings?${new URLSearchParams({ category: 'all', date: day, limit: '500' })}`;
// the pack's own
const HZ_DAYS = (v) => `GET /api/vehicles/${encodeURIComponent(v)}/days?category=all&limit=100`;
const HZ_RECS = (v, day) => `GET /api/vehicles/${encodeURIComponent(v)}/recordings?category=all&limit=500${day ? `&date=${day}` : ''}`;
const LONG_NAME = 'Model 3 Long Range with a name far too long for one pill or one tile title';
const LONG_TYPE = 'SentryClips_from_a_category_of_events_with_a_name_far_too_long_for_one_line';

/** The recorded days, every day's recordings and all of them, newest first. */
function library(api, vehicle) {
  const days = api[`GET ${DAYS_PATH(vehicle)}`] || { days: [] };
  const byDay = {};
  for (const d of days.days || []) byDay[d.day] = (api[`GET ${DAY_RECS(vehicle, d.day)}`] || {}).recordings || [];
  const all = Object.values(byDay).flat();
  const sorted = [...all].sort((a, b) => String(b.startKey).localeCompare(String(a.startKey)));
  return { days, byDay, all, sorted };
}

/** The pack's answers over a library (fix: the recordings changed, vehicles: GET /api/vehicles). */
function answers(api, vehicle, now, fix = (r) => r) {
  const { days, byDay, all } = library(api, vehicle);
  const meta = { scannedAt: now - 300000, scanning: false, error: null, dates: days.dates || [] };
  const out = {
    [HZ_DAYS(vehicle)]: { ...days, limit: 100, scannedAt: now - 300000 },
    [HZ_RECS(vehicle)]: { total: all.length, offset: 0, limit: 500, ...meta, recordings: all.map(fix) }
  };
  for (const [day, recs] of Object.entries(byDay)) out[HZ_RECS(vehicle, day)] = { total: recs.length, offset: 0, limit: 500, ...meta, recordings: recs.map(fix) };
  return out;
}

const EMPTY_DAYS = { total: 0, recordings: 0, offset: 0, limit: 100, scannedAt: null, scanning: false, error: null, dates: [], days: [] };
const EMPTY_RECS = { total: 0, offset: 0, limit: 500, scannedAt: null, scanning: false, error: null, dates: [], recordings: [] };

module.exports = {
  pack: 'horizon',
  pages: {
    // a day open with its tracks (the first album)
    footage: { path: '/#/footage', variant: true, clicks: ['#ft-days .album[data-day^="2"]'] },
    'footage-empty': { path: '/#/footage', variant: true },
    'footage-scanning': { path: '/#/footage', variant: true },
    'footage-error': { path: '/#/footage', variant: true },
    'footage-novehicle': { path: '/#/footage', variant: true },
    'footage-long': { path: '/#/footage', variant: true },
    // every day, the two newest tracks ticked: the selection bar
    'footage-selected': { path: '/#/footage', variant: true, clicks: ['#ft-tracks li[data-i="0"] .tk-sel input', '#ft-tracks li[data-i="1"] .tk-sel input'] }
  },
  variants(api, vehicle, now) {
    const lib = library(api, vehicle);
    const first = (lib.days.days || [])[0];
    const vehicles = api['GET /api/vehicles'] || { vehicles: [] };
    return {
      footage: { route: 'footage', sub: first ? `?day=${first.day}` : '', api: answers(api, vehicle, now) },
      'footage-empty': { route: 'footage', api: { [HZ_DAYS(vehicle)]: EMPTY_DAYS, [HZ_RECS(vehicle)]: EMPTY_RECS } },
      'footage-scanning': { route: 'footage', api: { [HZ_DAYS(vehicle)]: { ...EMPTY_DAYS, scanning: true }, [HZ_RECS(vehicle)]: { ...EMPTY_RECS, scanning: true, scannedAt: now - 60000 } } },
      'footage-error': { route: 'footage', api: { [HZ_DAYS(vehicle)]: { $error: 'The footage list could not be read.', $status: 500 }, [HZ_RECS(vehicle)]: { $error: 'The recordings could not be read.', $status: 500 } } },
      'footage-novehicle': { route: 'footage', api: { 'GET /api/vehicles': { vehicles: [] } } },
      'footage-long': {
        route: 'footage',
        api: {
          'GET /api/vehicles': { ...vehicles, vehicles: (vehicles.vehicles || []).map((v, i) => (i ? v : { ...v, name: LONG_NAME, timezone: 'America/Argentina/ComodRivadavia' })) },
          ...answers(api, vehicle, now, (r, i) => ({ ...r, type: LONG_TYPE, protected: i === 0, encoded: { clips: 6, encoded: 3, unknown: 0, codecs: ['hevc', 'av1'] } }))
        }
      },
      'footage-selected': { route: 'footage', state: { sel: lib.sorted.slice(0, 2).map((r) => r.id) }, api: answers(api, vehicle, now) }
    };
  }
};
