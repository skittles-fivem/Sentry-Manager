'use strict';

// Flux's Encoding per vehicle page (flux/pages/vehicles.js, the shared
// web/vehicleEncoding.js with the pack's options) for the parity pictures:
// the page names RENDER-PAGES.md lists as "flux <name>", how capture.js
// takes the web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same answers as the built-in page's states (percarVariants.js:
// percar, percar-edit, percar-empty, percar-error), on Flux's own route
// (#/vehicles, ?edit=<id> for the editor).

const { percarVariants } = require('../percarVariants');

module.exports = {
  pack: 'flux',
  pages: {
    vehicles: { path: '/#/vehicles', variant: true },
    'vehicles-edit': { path: '/#/vehicles?edit=model-3', variant: true },
    'vehicles-empty': { path: '/#/vehicles', variant: true },
    'vehicles-error': { path: '/#/vehicles', variant: true }
  },
  variants(api, vehicle) {
    const v = percarVariants(api, vehicle);
    return {
      vehicles: { route: 'vehicles', api: v.percar.api },
      'vehicles-edit': { route: 'vehicles', sub: `?edit=${encodeURIComponent(vehicle)}`, api: v['percar-edit'].api },
      'vehicles-empty': { route: 'vehicles', api: v['percar-empty'].api },
      'vehicles-error': { route: 'vehicles', api: v['percar-error'].api }
    };
  }
};
