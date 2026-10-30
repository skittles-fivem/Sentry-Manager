'use strict';

// Skips for suites that need a real Linux machine: /proc, /sys, setpriv.

// Pass a test context to skip the running test, or a reason to get a value
// for the `skip` option. Either way nothing changes on Linux.
function linuxOnly(tOrReason, reason, platform = process.platform) {
  const isContext = tOrReason && typeof tOrReason.skip === 'function';
  const why = `Linux only: ${(isContext ? reason : tOrReason) || 'needs Linux'}`;
  if (platform === 'linux') return false;
  if (isContext) tOrReason.skip(why);
  return why;
}

module.exports = { linuxOnly };
