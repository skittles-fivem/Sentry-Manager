'use strict';

// Horizon's Recycle Bin page (hz/pages/bin.js) for the parity pictures: the
// page names RENDER-PAGES.md lists as "horizon <name>", how capture.js takes
// the web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same states as the built-in Recycle Bin page (binVariants.js: bin,
// bin-long; bin-empty is the fixture server's own answer; bin-error): the
// pack asks the same GET /api/bin?vehicle=<id>. bin-every-vehicle is the
// "Every vehicle" filter clicked (GET /api/bin), its items named by vehicle.

const { binVariants } = require('../binVariants');

module.exports = {
  pack: 'horizon',
  pages: {
    bin: { path: '/#/bin', variant: true },
    'bin-empty': { path: '/#/bin' },
    'bin-long': { path: '/#/bin', variant: true },
    'bin-error': { path: '/#/bin', variant: true },
    'bin-every-vehicle': { path: '/#/bin', variant: true, clicks: ['[data-segs="bs"] [data-v="all"]'] }
  },
  variants(api, vehicle, now) {
    const v = binVariants(vehicle, now);
    const items = v.bin.api[`GET /api/bin?vehicle=${encodeURIComponent(vehicle)}`].items;
    return {
      bin: v.bin,
      'bin-empty': { route: 'bin' },
      'bin-long': v['bin-long'],
      'bin-error': { route: 'bin', api: { [`GET /api/bin?vehicle=${encodeURIComponent(vehicle)}`]: { $error: 'The Recycle Bin could not be read.', $status: 500 } } },
      // (the native render opens the page with "Every vehicle" picked: state.bin.scope)
      'bin-every-vehicle': { route: 'bin', state: { bin: { scope: 'all' } }, api: { 'GET /api/bin': { items: items.map((i, n) => ({ ...i, vehicleId: n === 1 ? 'model-y' : vehicle })) } } }
    };
  }
};
