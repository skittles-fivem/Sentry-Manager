'use strict';

// Flux's Notifications page (flux/pages/notifications.js) for the parity
// pictures: the page names RENDER-PAGES.md lists as "flux <name>", how
// capture.js takes the web side, and the fixture variants the native side
// renders (tools/parity/packFixtures.js writes them to
// windows/app/Shell/Fixtures/). The built-in Notifications page's answers
// (notificationsVariants.js: two open, one resolved, a long one), here with
// the warning read and the critical one of a kind, so the list has read,
// unread and resolved items; notifications-empty is the fixture server's own
// answer; notifications-all is "All, incl. resolved" clicked (the resolved
// one shown, faded); notifications-error shows an empty list, as the pack's
// loadNotes().catch() leaves it.

const { notificationsVariants } = require('../notificationsVariants');

module.exports = {
  pack: 'flux',
  pages: {
    notifications: { path: '/#/notifications', variant: true },
    'notifications-empty': { path: '/#/notifications' },
    'notifications-all': { path: '/#/notifications', variant: true, clicks: ['[data-seg="nshow"] [data-v="all"]'] },
    'notifications-error': { path: '/#/notifications', variant: true }
  },
  variants(api, vehicle, now) {
    const base = notificationsVariants(now).notifications.api['GET /api/notifications'].notifications;
    const list = base.map((n) => (n.id === 'n1' ? { ...n, kind: 'share_down' } : n.id === 'n2' ? { ...n, readAt: now - 29 * 3600000 } : n));
    const answers = { 'GET /api/notifications': { notifications: list }, 'GET /api/notifications?open=1': { notifications: list.filter((n) => !n.resolvedAt) } };
    return {
      notifications: { route: 'notifications', api: answers },
      'notifications-empty': { route: 'notifications' },
      // (the native render opens the page with "All, incl. resolved" picked: state.notifications.show)
      'notifications-all': { route: 'notifications', state: { notifications: { show: 'all' } }, api: answers },
      'notifications-error': { route: 'notifications', api: { 'GET /api/notifications': { $error: 'The notifications could not be read.', $status: 500 } } }
    };
  }
};
