'use strict';

// Horizon's Jobs page (hz/pages/jobs.js) for the parity pictures: the page
// names RENDER-PAGES.md lists as "horizon <name>", how capture.js takes the
// web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same states as the built-in Jobs page (jobsVariants.js): the pack asks
// the same GET /api/jobs?vehicle=<id>.

const { jobsVariants } = require('../jobsVariants');

module.exports = {
  pack: 'horizon',
  pages: {
    jobs: { path: '/#/jobs', variant: true },
    'jobs-empty': { path: '/#/jobs' },
    'jobs-running-only': { path: '/#/jobs', variant: true },
    'jobs-long': { path: '/#/jobs', variant: true },
    'jobs-error': { path: '/#/jobs', variant: true }
  },
  variants(api, vehicle, now) {
    const v = jobsVariants(vehicle, now);
    return {
      jobs: v.jobs,
      'jobs-empty': { route: 'jobs' },
      'jobs-running-only': v['jobs-running-only'],
      'jobs-long': v['jobs-long'],
      'jobs-error': { route: 'jobs', api: { [`GET /api/jobs?vehicle=${encodeURIComponent(vehicle)}`]: { $error: 'The job list could not be read.', $status: 500 } } }
    };
  }
};
