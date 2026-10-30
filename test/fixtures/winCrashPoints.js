'use strict';

// The crash-recovery kill points of test/fixtures/crashPoints.js, with the
// converter using the Windows owned-file adapter (server/platform/winOwnedFs.js)
// over a stand-in for SentryHelper.exe that works on this machine's files
// (test/fixtures/winFsHelperFake.js). Run as a script (env BASE, POINT, MODE,
// NETMODE, CONCURRENCY) it starts a job in BASE and dies at POINT, exactly as
// crashPoints.js does; crashAndRecover() then starts the converter again on
// the same folders.

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const REPO = path.join(__dirname, '..', '..');
const posix = require(path.join(REPO, 'server/posixOwnedFs'));
const { createWinOwnedFs } = require(path.join(REPO, 'server/platform/winOwnedFs'));
const { createLinuxFsHelper } = require('./winFsHelperFake');

// crashPoints.js builds its adapter with createPosixOwnedFs({ noReplace }),
// where noReplace.rename is the real no-replace move wrapped with the kill
// point's hook: the stand-in helper makes its moves through it, so a point
// such as "the backup was half removed" fires on the Windows path too.
posix.createPosixOwnedFs = (options = {}) => createWinOwnedFs({
  helper: createLinuxFsHelper({
    ...(options.noReplace?.available ? { rename: options.noReplace.rename } : {})
  })
});

const points = require('./crashPoints');

function crash(base, { point, concurrency = 1, mode = 'replace', networkMode = 'direct', timeout = 120_000 }) {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [__filename], {
      env: { ...process.env, BASE: base, POINT: point, CONCURRENCY: String(concurrency), MODE: mode, NETMODE: networkMode },
      detached: true, stdio: ['ignore', 'ignore', 'pipe']
    });
    let stderr = '';
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    const timer = setTimeout(() => { try { process.kill(-child.pid, 'SIGKILL'); } catch (_) { /* gone */ } }, timeout);
    child.on('exit', (code, signal) => {
      clearTimeout(timer);
      resolve({ code, signal, stderr });
    });
  });
}

// crashPoints.crashAndRecover with this script as the dying child.
async function crashAndRecover({ point, mode = 'replace', networkMode = 'direct', concurrency = 1, clips = concurrency }) {
  const { base, folder } = await points.makeBase(clips);
  const names = fs.readdirSync(folder).sort();
  const before = Object.fromEntries(names.map((name) => [name, points.sha256(path.join(folder, name))]));
  const died = await crash(base, { point, concurrency, mode, networkMode });
  let env;
  let timer;
  try {
    env = await points.open(base, { concurrency, mode, networkMode });
    await env.runtime.start();
    const [listed] = await env.runtime.service.list();
    await Promise.race([env.runtime.service.whenIdle(listed.id), new Promise((resolve) => { timer = setTimeout(resolve, 90_000); })]);
    const job = await env.runtime.service.get(listed.id);
    const clipsAfter = names.map((name) => {
      const output = mode === 'replace' ? path.join(folder, name)
        : path.join(base, 'out', 'SentryClips', path.basename(path.dirname(path.join(folder, name))), name);
      return {
        name,
        codec: fs.existsSync(output) ? points.codecOf(output) : null,
        originalKept: fs.existsSync(path.join(folder, name)) && points.sha256(path.join(folder, name)) === before[name]
      };
    });
    return { died, job, clips: clipsAfter, leftovers: points.leftovers(base, folder), names: fs.readdirSync(folder).sort(), expectedNames: names };
  } finally {
    clearTimeout(timer);
    await env?.runtime.shutdown().catch(() => {});
    fs.rmSync(base, { recursive: true, force: true });
  }
}

module.exports = { crashAndRecover, assertRecovered: points.assertRecovered, available: points.available };

if (require.main === module) {
  (async () => {
    const env = await points.open(process.env.BASE, {
      point: process.env.POINT,
      concurrency: Number(process.env.CONCURRENCY || 1),
      mode: process.env.MODE || 'replace',
      networkMode: process.env.NETMODE || 'direct'
    });
    if (typeof env.renameExclusive.replacementSupport !== 'function') throw new Error('not the Windows adapter');
    await env.runtime.start();
    const id = await env.runtime.service.start(env.config);
    await env.runtime.service.whenIdle(id);
    process.stderr.write('kill point not reached\n');
    process.exit(3);
  })().catch((error) => {
    process.stderr.write(`${error?.stack || error}\n`);
    process.exit(2);
  });
}
