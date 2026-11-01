'use strict';

// Horizon's Notifications page (hz/pages/alerts.js) for the parity pictures:
// the page names RENDER-PAGES.md lists as "horizon <name>", how capture.js
// takes the web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same states as the built-in Notifications page
// (notificationsVariants.js: two open, one resolved, a long one;
// alerts-empty is the fixture server's own answer; alerts-error): the pack
// asks the same GET /api/notifications. alerts-all is "All, with resolved"
// clicked (the resolved one shown, faded); alerts-error shows an empty list,
// as the pack's loadNotes().catch() leaves it.

const { notificationsVariants } = require('../notificationsVariants');

module.exports = {
  pack: 'horizon',
  pages: {
    alerts: { path: '/#/alerts', variant: true },
    'alerts-empty': { path: '/#/alerts' },
    'alerts-all': { path: '/#/alerts', variant: true, clicks: ['[data-segs="as"] [data-v="all"]'] },
    'alerts-error': { path: '/#/alerts', variant: true }
  },
  variants(api, vehicle, now) {
    const v = notificationsVariants(now).notifications;
    return {
      alerts: { route: 'alerts', api: v.api },
      'alerts-empty': { route: 'alerts' },
      // (the native render opens the page with "All, with resolved" picked: state.alerts.show)
      'alerts-all': { route: 'alerts', state: { alerts: { show: 'all' } }, api: v.api },
      'alerts-error': { route: 'alerts', api: { 'GET /api/notifications': { $error: 'The notifications could not be read.', $status: 500 } } }
    };
  }
};
