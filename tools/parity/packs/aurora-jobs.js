'use strict';

// Aurora's Jobs window (aurora/pages/jobs.js) for the parity pictures: the
// page names RENDER-PAGES.md lists as "aurora <name>", how capture.js takes
// the web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same states as the built-in Jobs page (jobsVariants.js), but Aurora
// keeps its own scope ("All vehicles" until another is picked, and no
// picker with one vehicle), so it asks GET /api/jobs; the dock's badge asks
// GET /api/jobs?limit=50, answered the same so the badge agrees.

const { jobsVariants } = require('../jobsVariants');

module.exports = {
  pack: 'aurora',
  pages: {
    jobs: { path: '/#/jobs', variant: true },
    'jobs-empty': { path: '/#/jobs' },
    'jobs-running-only': { path: '/#/jobs', variant: true },
    'jobs-long': { path: '/#/jobs', variant: true },
    'jobs-error': { path: '/#/jobs', variant: true }
  },
  variants(api, vehicle, now) {
    const v = jobsVariants(vehicle, now);
    const own = `GET /api/jobs?vehicle=${encodeURIComponent(vehicle)}`;
    const every = (x) => ({ route: 'jobs', api: { 'GET /api/jobs': x.api[own], 'GET /api/jobs?limit=50': x.api[own] } });
    return {
      jobs: every(v.jobs),
      'jobs-empty': { route: 'jobs' },
      'jobs-running-only': every(v['jobs-running-only']),
      'jobs-long': every(v['jobs-long']),
      'jobs-error': { route: 'jobs', api: { 'GET /api/jobs': { $error: 'The job list could not be read.', $status: 500 } } }
    };
  }
};
