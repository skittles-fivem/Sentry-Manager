'use strict';

// Horizon's Nodes page (hz/pages/nodes.js) for the parity pictures: the page
// names RENDER-PAGES.md lists as "horizon <name>", how capture.js takes the
// web side, and the fixture variants the native side renders
// (tools/parity/packFixtures.js writes them to windows/app/Shell/Fixtures/).
// The same states as the built-in Nodes page (nodesVariants.js): the pack asks
// the same GET /api/nodes and GET /api/nodes/<id>/benchmark (not the built-in
// page's /api/settings/network, which nodes-closed answers too and the pack
// leaves unread). nodes-empty is the fixture server's own answer (no nodes).

const { nodesVariants } = require('../nodesVariants');

module.exports = {
  pack: 'horizon',
  pages: {
    'nodes-empty': { path: '/#/nodes' },
    nodes: { path: '/#/nodes', variant: true },
    'nodes-running': { path: '/#/nodes', variant: true },
    'nodes-noworker': { path: '/#/nodes', variant: true },
    'nodes-closed': { path: '/#/nodes', variant: true }
  },
  variants(api, vehicle, now) {
    const v = nodesVariants(api, vehicle, now);
    return {
      'nodes-empty': { route: 'nodes' },
      nodes: v.nodes,
      'nodes-running': v['nodes-running'],
      'nodes-noworker': v['nodes-noworker'],
      'nodes-closed': v['nodes-closed']
    };
  }
};
