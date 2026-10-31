'use strict';

// The Notifications page's states for the parity pictures (capture.js
// VARIANTS, the native render list's page names): notifications (one
// critical and one warning open, one info resolved, one with a long title
// and message); the shell's open list (its red badge) is the open ones.
// notifications-empty is the fixture server's own answer (none).
// Times are from `now` (ms), the fixture's clock.

const H = 3600000;

function notificationsVariants(now) {
  const critical = { id: 'n1', level: 'critical', title: 'The share is not reachable', message: 'Model 3: //nas/teslacam could not be mounted (permission denied).', createdAt: now - 2 * H, updatedAt: now - 2 * H, readAt: null, resolvedAt: null };
  const warning = { id: 'n2', level: 'warning', title: 'Disk almost full', message: 'The archive has 4.1 GiB left.', createdAt: now - 30 * H, updatedAt: now - 30 * H, readAt: null, resolvedAt: null };
  const info = { id: 'n3', level: 'info', title: 'Update installed', message: 'Sentry Six Manager is up to date.', createdAt: now - 80 * H, updatedAt: now - 79 * H, readAt: now - 79 * H, resolvedAt: now - 79 * H };
  const long = { id: 'n4', level: 'warning', title: 'A notification with a title that is far too long to fit on one line of the list at all', message: 'A message that goes on and on, far longer than one line of the list, so that it wraps under the title and pushes the times down below it.', createdAt: now - 400 * H, updatedAt: now - 400 * H, readAt: null, resolvedAt: null };
  return {
    notifications: {
      route: 'notifications',
      api: {
        'GET /api/notifications': { notifications: [critical, warning, info, long] },
        'GET /api/notifications?open=1': { notifications: [critical, warning, long] }
      }
    }
  };
}

const NOTIFICATIONS_VARIANTS = ['notifications'];

module.exports = { notificationsVariants, NOTIFICATIONS_VARIANTS };
