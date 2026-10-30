'use strict';

// Fake command-line tools for the tests (an ffmpeg that stalls on one clip,
// an ffprobe that waits to be released, ...) written as Node scripts instead
// of #!/bin/sh ones, so they need no shell and read the same on every OS.
//
// The code under test spawns a tool by its path with shell: false. On Linux
// the script is made executable with this very Node as its interpreter
// (#!<process.execPath>), so spawning its path runs it. Windows cannot run a
// script by its path without a shell (and Node refuses .cmd files with
// shell: false), so there writeNodeTool() says so plainly instead of leaving
// a confusing spawn error; the tests that use it need the Linux renameat2
// helper and are skipped on Windows before they get here.
//
// Every tool starts with a small prelude:
//   args            its arguments, as the code under test passed them
//   joined          args.join(' '), what sh's "$*" was
//   parent          the pid that started this tool, read once as sh's $PPID
//                   is (process.ppid is re-read on every access, so once the
//                   parent dies it names whoever adopted the tool instead)
//   exec(cmd, argv) run cmd in this script's place, as sh's exec did: same
//                   stdin/stdout/stderr, a signal sent here is passed on, and
//                   it ends the way cmd ended (same exit code, same signal)
//   run(cmd, argv, done)  run cmd and call done(status) when it ends, status
//                   as sh's $? reads it (128 + signal number when killed)
//   until(check, then, ms)  poll check() every ms (default 50) until it is
//                   true, then call then(); sh's while [ ! -f x ]; do sleep; done

const fs = require('fs');
const path = require('path');

const PRELUDE = `'use strict';
const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawn } = require('child_process');
const args = process.argv.slice(2);
const joined = args.join(' ');
const parent = process.ppid;
const PASSED_ON = process.platform === 'win32' ? ['SIGINT'] : ['SIGTERM', 'SIGINT', 'SIGHUP'];
function exec(command, argv) {
  const child = spawn(command, argv, { stdio: 'inherit' });
  const pass = (signal) => { try { child.kill(signal); } catch (_) { /* already gone */ } };
  for (const signal of PASSED_ON) process.on(signal, pass);
  child.on('error', (error) => { process.stderr.write(command + ': ' + error.message + '\\n'); process.exit(127); });
  child.on('exit', (code, signal) => {
    if (!signal) process.exit(code);
    // die of the same signal, as the exec'd command would have been seen to
    for (const s of PASSED_ON) process.removeListener(s, pass);
    setTimeout(() => process.exit(128 + (os.constants.signals[signal] || 0)), 1000);
    process.kill(process.pid, signal);
  });
}
function run(command, argv, done) {
  const child = spawn(command, argv, { stdio: 'inherit' });
  child.on('error', () => done(127));
  child.on('exit', (code, signal) => done(signal ? 128 + (os.constants.signals[signal] || 0) : code));
}
function until(check, then, ms = 50) {
  if (check()) return then();
  const timer = setInterval(() => { if (check()) { clearInterval(timer); then(); } }, ms);
}
`;

/**
 * Write `body` (JavaScript, after the prelude above) as an executable tool
 * named `name` in `dir`, and return the path to hand to the code under test.
 */
function writeNodeTool(dir, name, body, { platform = process.platform } = {}) {
  if (platform === 'win32') {
    throw new Error(`a fake ${name} written as a script cannot be spawned by path on Windows; this test is Linux-only`);
  }
  const file = path.join(dir, name);
  fs.writeFileSync(file, `#!${process.execPath}\n${PRELUDE}\n${body}\n`, { mode: 0o755 });
  return file;
}

module.exports = { writeNodeTool, PRELUDE };
