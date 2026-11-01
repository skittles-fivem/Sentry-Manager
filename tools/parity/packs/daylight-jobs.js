'use strict';

// Daylight's Activity page (dl/pages/jobs.js) for the parity pictures: the
// page names RENDER-PAGES.md lists as "daylight <name>", how capture.js takes
// the web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same jobs as the built-in Jobs page (jobsVariants.js); with the
// fixture's one vehicle the pack shows no vehicle picker and asks for every
// vehicle's jobs (GET /api/jobs). jobs-dark is the same page in the dark look
// (the web's localStorage daylight:theme, the native prefs).

const { jobsVariants } = require('../jobsVariants');

const ALL = 'GET /api/jobs';
// jobsVariants answers GET /api/jobs?vehicle=<id>: the same answer, asked as Daylight asks
const asked = (v) => ({ route: 'jobs', api: { [ALL]: Object.values(v.api)[0] } });

module.exports = {
  pack: 'daylight',
  pages: {
    jobs: { path: '/#/jobs', variant: true },
    'jobs-empty': { path: '/#/jobs' },
    'jobs-running-only': { path: '/#/jobs', variant: true },
    'jobs-long': { path: '/#/jobs', variant: true },
    'jobs-error': { path: '/#/jobs', variant: true },
    'jobs-dark': { path: '/#/jobs', variant: true, storage: { 'daylight:theme': '"dark"' } }
  },
  variants(api, vehicle, now) {
    const v = jobsVariants(vehicle, now);
    return {
      jobs: asked(v.jobs),
      'jobs-empty': { route: 'jobs' },
      'jobs-running-only': asked(v['jobs-running-only']),
      'jobs-long': asked(v['jobs-long']),
      'jobs-error': { route: 'jobs', api: { [ALL]: { $error: 'The job list could not be read.', $status: 500 } } },
      'jobs-dark': { ...asked(v.jobs), prefs: { 'daylight:theme': 'dark' } }
    };
  }
};
