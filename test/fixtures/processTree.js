'use strict';

// Processes for the converter suites that kill the converter or run fake
// tools, on Linux and on Windows.
//
// Linux kills the converter's whole process group (docker kill ends the
// container, ffmpeg included). Windows has no process groups and no signals
// besides "end it": there the service's Job Object ends node.exe and every
// ffmpeg together, which taskkill /T /F does for a test (the process and
// everything it started, children before parents).
//
// A fake tool is a Node script (test/fixtures/nodeTool.js). Linux runs it by
// its path (#!node); Windows cannot run a script by its path without a shell,
// so the code under test is handed toolSpawn(), which runs a .js tool with
// this very Node, and whose kill() ends the tool with the real one it started
// (ending only the script would leave that ffmpeg running).

const childProcess = require('child_process');
const fs = require('fs');
const path = require('path');
const { writeNodeTool, PRELUDE } = require('./nodeTool');

/** pid and everything it started, at once (SIGKILL / TerminateProcess). */
function killTree(pid, platform = process.platform) {
  if (platform === 'win32') {
    childProcess.spawnSync('taskkill', ['/F', '/T', '/PID', String(pid)], { stdio: 'ignore', windowsHide: true });
    return;
  }
  try { process.kill(-pid, 'SIGKILL'); } catch (_) { /* gone, or not a group leader */ }
}

/**
 * A fake tool the code under test spawns by the returned path. Linux:
 * writeNodeTool itself (an executable #! script). Windows: `name` as a .js
 * file, to be spawned through toolSpawn().
 */
function writeTool(dir, name, body, platform = process.platform) {
  if (platform !== 'win32') return writeNodeTool(dir, name, body);
  const file = path.join(dir, name.endsWith('.js') ? name : `${name}.js`);
  fs.writeFileSync(file, `${PRELUDE}\n${body}\n`);
  return file;
}

/**
 * spawn as the code under test takes it. Linux: `spawn` unchanged. Windows:
 * a .js command is run by this Node, and killing it ends the real tool too.
 */
function toolSpawn(spawn = childProcess.spawn, platform = process.platform) {
  if (platform !== 'win32') return spawn;
  return (command, args = [], options = {}) => {
    if (!/\.js$/i.test(String(command))) return spawn(command, args, options);
    const child = spawn(process.execPath, [command, ...args], options);
    const kill = child.kill.bind(child);
    child.kill = (signal) => {
      if (child.exitCode !== null || child.signalCode !== null || !child.pid) return false;
      killTree(child.pid, platform);
      try { kill(signal); } catch (_) { /* already ended by taskkill */ }
      return true;
    };
    return child;
  };
}

// A process ended by TerminateProcess (taskkill /F, process.kill on Windows)
// only reads as exit code 1, as does one that failed. A test process that
// ends itself at a kill point leaves this mark in its folder first, so the
// test can tell the two apart as it does by SIGKILL on Linux.
const KILLED_MARK = 'killed-at-point';

function markKilled(base, platform = process.platform) {
  if (platform !== 'win32') return;
  try { fs.writeFileSync(path.join(String(base), KILLED_MARK), ''); } catch (_) { /* then it reads as not killed */ }
}

/** The signal that ended a process ({ code | status, signal }) as Linux reports it. */
function exitSignal(result, base, platform = process.platform) {
  if (platform === 'win32' && !result.signal && (result.code ?? result.status) === 1
    && fs.existsSync(path.join(String(base), KILLED_MARK))) return 'SIGKILL';
  return result.signal;
}

module.exports = { killTree, writeTool, toolSpawn, markKilled, exitSignal, KILLED_MARK };
