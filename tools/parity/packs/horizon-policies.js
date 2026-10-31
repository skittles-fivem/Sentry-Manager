'use strict';

// Horizon's Policies page (hz/pages/policies.js) for the parity pictures: the
// page names RENDER-PAGES.md lists as "horizon <name>", how capture.js takes
// the web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same states as the built-in Policies page (capture.js VARIANTS:
// policies, policies-off, policies-novehicle, policies-error): the pack asks
// the same GET /api/vehicles/<id>/policy (its id encoded), for every
// vehicle; policies-long adds a second vehicle with a long name.

const DAY = 86400000;

/** capture.js policiesVariant's answer: on, two kinds kept, the next run moving something, a last run. */
const onPolicy = (now) => ({
  policy: { enabled: true, runAt: '04:15', keepDays: { Recent: 30, Sentry: 90, Saved: null, Other: null }, lastRunAt: now - DAY, lastResult: { moved: 12, skipped: 3 } },
  preview: { recordings: 7, bytes: 3435973837, byCategory: { Recent: 5, Sentry: 2 }, ids: [] }
});

module.exports = {
  pack: 'horizon',
  pages: {
    policies: { path: '/#/policies', variant: true },
    'policies-off': { path: '/#/policies' },
    'policies-novehicle': { path: '/#/policies', variant: true },
    'policies-error': { path: '/#/policies', variant: true },
    'policies-long': { path: '/#/policies', variant: true }
  },
  variants(api, vehicle, now) {
    const path = (id) => `GET /api/vehicles/${encodeURIComponent(id)}/policy`;
    const cars = (api['GET /api/vehicles'] && api['GET /api/vehicles'].vehicles) || [];
    const own = cars.find((v) => String(v.id) === String(vehicle)) || cars[0] || { id: vehicle, name: 'Model 3' };
    const second = { ...own, id: 'model-s', name: 'Model S Plaid with a name far too long for one line of a panel head at all', binItems: 0 };
    return {
      policies: { route: 'policies', api: { [path(vehicle)]: onPolicy(now) } },
      'policies-off': { route: 'policies' },
      'policies-novehicle': { route: 'policies', api: { 'GET /api/vehicles': { vehicles: [] } } },
      'policies-error': { route: 'policies', api: { [path(vehicle)]: { $error: 'The policy could not be read.', $status: 500 } } },
      'policies-long': {
        route: 'policies',
        api: {
          'GET /api/vehicles': { vehicles: [own, second] },
          [path(vehicle)]: onPolicy(now),
          [path(second.id)]: { policy: { enabled: true, runAt: '23:05', keepDays: { Recent: 1, Sentry: 3650, Saved: 365, Other: 7 }, lastRunAt: now - 3 * 3600000, lastResult: { error: 'The share is not reachable right now: //nas/teslacam could not be mounted (permission denied).' } }, preview: { recordings: 1, bytes: 1536 } }
        }
      }
    };
  }
};
