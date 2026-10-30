'use strict';

// The Mac's own tools as fakeMacServer.js runs them: an M2 Pro Mac mini.
//   node fakeMacTool.js /usr/sbin/sysctl -n hw.optional.arm64
// Mounted shares come from FAKE_MAC_STATE/mounts.json.

const fs = require('fs');
const path = require('path');

const [cmd, ...args] = process.argv.slice(2);
const out = (text) => { process.stdout.write(`${text}\n`); process.exit(0); };
const fail = (text) => { process.stderr.write(`${text}\n`); process.exit(1); };

const SYSCTL = {
  'hw.optional.arm64': '1',
  'sysctl.proc_translated': '0',
  'kern.hv_vmm_present': '0',
  'hw.perflevel0.physicalcpu': '8',
  'hw.perflevel1.physicalcpu': '4'
};

if (cmd === '/usr/sbin/sysctl') {
  const key = args[args.length - 1];
  if (key in SYSCTL) out(args.includes('-n') ? SYSCTL[key] : `${key}: ${SYSCTL[key]}`);
  fail(`sysctl: unknown oid '${key}'`);
}
if (cmd === '/usr/sbin/system_profiler') {
  if (args[0] === 'SPDisplaysDataType') {
    out(JSON.stringify({ SPDisplaysDataType: [{ _name: 'Apple M2 Pro', sppci_model: 'Apple M2 Pro', spdisplays_vendor: 'sppci_vendor_Apple', sppci_cores: '19' }] }));
  }
  out('{}');
}
if (cmd === '/sbin/mount') {
  let mounts = [];
  try { mounts = JSON.parse(fs.readFileSync(path.join(process.env.FAKE_MAC_STATE, 'mounts.json'), 'utf8')); } catch (_) { /* none */ }
  out([
    '/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)',
    '/dev/disk3s5 on /System/Volumes/Data (apfs, local, journaled, nobrowse)',
    ...mounts.map((m) => `${m.source} on ${m.dir} (smbfs, nodev, noexec, nosuid, nobrowse, mounted by _sentrysix)`)
  ].join('\n'));
}
fail(`${cmd}: not faked`);
