'use strict';

// Horizon's Settings page (hz/pages/settings.js) for the parity pictures: the
// page names RENDER-PAGES.md lists as "horizon <name>", how capture.js takes
// the web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// One page per tab, on the built-in Settings page's states where the pack asks
// the same paths (settingsVariants.js): settings (the Vehicles tab, two
// vehicles), settings-vehicle (the fixture vehicle's form, an SMB share),
// settings-vehicle-new (a new vehicle on a Windows PC, "Folder on this PC"
// picked: the web clicks it, the native side keeps it as the page's state),
// settings-account, settings-interface (the shipped packs and an uploaded one
// in use), settings-display (Background), settings-updates (an image update
// available), settings-security (a host the certificate does not cover),
// settings-system (a GPU), settings-power (a time of use plan with two rates),
// settings-windows (the Updates tab on the Windows service: the release
// wording), settings-novehicles (no vehicles, /api/system failing) and
// settings-error (the Electricity tab with GET /api/settings failing: the
// router's "could not load" box).

const { settingsVariants } = require('../settingsVariants');

module.exports = {
  pack: 'horizon',
  pages: {
    settings: { path: '/#/settings', variant: true },
    'settings-vehicle': { path: '/#/settings/vehicles?edit=model-3', variant: true },
    'settings-vehicle-new': { path: '/#/settings/vehicles?add=1', variant: true, clicks: ['[data-segs="src"] [data-v="path"]'] },
    'settings-account': { path: '/#/settings/account' },
    'settings-interface': { path: '/#/settings/interface', variant: true },
    'settings-display': { path: '/#/settings/display' },
    'settings-updates': { path: '/#/settings/updates', variant: true },
    'settings-security': { path: '/#/settings/security', variant: true },
    'settings-system': { path: '/#/settings/system', variant: true },
    'settings-power': { path: '/#/settings/power', variant: true },
    'settings-windows': { path: '/#/settings/updates', variant: true },
    'settings-novehicles': { path: '/#/settings', variant: true },
    'settings-error': { path: '/#/settings/power', variant: true }
  },
  variants(api, vehicle, now) {
    const v = settingsVariants(api, vehicle, now);
    const me = api['GET /api/auth/me'] || {};
    // the Windows service: the web asks /api/info, the native shell reads /api/auth/me
    const windows = { 'GET /api/info': { deployment: 'windows' }, 'GET /api/auth/me': { ...me, deployment: 'windows' } };
    return {
      settings: { route: 'settings', api: v.settings.api },
      'settings-vehicle': { route: 'settings', sub: `vehicles?edit=${vehicle}`, api: v['settings-vehicle'].api },
      'settings-vehicle-new': { route: 'settings', sub: 'vehicles?add=1', state: { sourceType: 'path' }, api: { ...v['settings-vehicle-new'].api, ...windows } },
      'settings-account': { route: 'settings', sub: 'account' },
      'settings-interface': { route: 'settings', sub: 'interface', api: { 'GET /api/ui-packs': v.settings.api['GET /api/ui-packs'] } },
      'settings-display': { route: 'settings', sub: 'display' },
      'settings-updates': { route: 'settings', sub: 'updates', api: { 'GET /api/updates': v.settings.api['GET /api/updates'] } },
      'settings-security': { route: 'settings', sub: 'security', api: v['settings-security'].api },
      'settings-system': { route: 'settings', sub: 'system', api: { 'GET /api/system': v.settings.api['GET /api/system'] } },
      'settings-power': { route: 'settings', sub: 'power', api: v['settings-system'].api },
      'settings-windows': { route: 'settings', sub: 'updates', api: { ...v['settings-windows'].api, ...windows } },
      'settings-novehicles': { route: 'settings', api: v['settings-novehicles'].api },
      'settings-error': { route: 'settings', sub: 'power', api: v['settings-error'].api }
    };
  }
};
