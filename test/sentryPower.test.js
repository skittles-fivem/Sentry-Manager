'use strict';

// SentryPower.exe (windows/power/SentryPower.cs), the CPU power service of
// the Windows build: it reads the CPU package energy counters through the
// PawnIO driver for SentryHelper's perf.sample 'rapl' set. Checked two ways:
//   - the source keeps its promises, on every OS: only the module's
//     ioctl_read_msr is ever called, with a fixed list of four MSR numbers;
//     the pipe takes no input and keeps out network logons;
//   - its maths (energy units, the 32-bit counter's wrap, which CPUs and
//     which module) through a small harness compiled with it: Mono's mcs
//     and mono on Linux, the csc.exe that ships with Windows there.
// The driver itself is not available here (it needs a real PC and an
// administrator), so a reading by hand is SentryPower.exe --once.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const REPO = path.join(__dirname, '..');
const CS = path.join(REPO, 'windows', 'power', 'SentryPower.cs');
const HARNESS = path.join(__dirname, 'fixtures', 'sentryPowerHarness.cs');
const source = fs.readFileSync(CS, 'utf8');

function findCompiler() {
  if (process.platform === 'win32') {
    const csc = path.join(process.env.SystemRoot || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319', 'csc.exe');
    if (fs.existsSync(csc)) return { kind: 'csc', csc };
  }
  if (spawnSync('mcs', ['--version']).status === 0) {
    return { kind: 'mcs', canRun: spawnSync('mono', ['--version']).status === 0 };
  }
  return null;
}
const compiler = findCompiler();
const noCompiler = compiler ? false : 'no C# compiler (Mono\'s mcs, or csc.exe on Windows) is installed';
const noRunner = noCompiler || (compiler.kind === 'mcs' && !compiler.canRun ? 'mono is not installed' : false);

function compile(out, files, main) {
  if (compiler.kind === 'csc') {
    return spawnSync(compiler.csc, ['/nologo', '/target:exe', '/r:System.ServiceProcess.dll',
      ...(main ? [`/main:${main}`] : []), `/out:${out}`, ...files], { encoding: 'utf8' });
  }
  // Mono's mcs: C# 7.2 against the .NET Framework 4.8 reference assemblies
  // when Mono ships them (the same as the helper's and the service's tests)
  const api = '/usr/lib/mono/4.8-api';
  const refs = fs.existsSync(path.join(api, 'mscorlib.dll'))
    ? ['-nostdlib', '-noconfig', ...['mscorlib', 'System', 'System.Core', 'System.ServiceProcess'].map((n) => `-r:${path.join(api, `${n}.dll`)}`)]
    : ['-r:System.ServiceProcess.dll', '-r:System.Core.dll'];
  const args = [...refs, ...(main ? [`-main:${main}`] : []), `-out:${out}`, ...files];
  let r = spawnSync('mcs', ['-langversion:7.3', ...args], { encoding: 'utf8' });
  if (/CS1617/.test(`${r.stdout}${r.stderr}`)) r = spawnSync('mcs', ['-langversion:7.2', ...args], { encoding: 'utf8' });
  return r;
}

const tmpRoot = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'sm-power-')));
test.after(() => fs.rmSync(tmpRoot, { recursive: true, force: true, maxRetries: 8, retryDelay: 200 }));

test('SentryPower.cs: only ioctl_read_msr, only the four RAPL MSRs, a pipe that takes no input', () => {
  // the one module function it calls; the modules' write and SMN functions never
  const fns = [...source.matchAll(/"(ioctl_[a-z_]+)"/g)].map((m) => m[1]);
  assert.deepEqual([...new Set(fns)], ['ioctl_read_msr']);
  assert.doesNotMatch(source, /write_msr|read_smn|-unrestricted/);
  for (const msr of ['0xC0010299', '0xC001029B', '0x606', '0x611']) assert.ok(source.includes(msr), msr);
  // ReadMsr refuses any other MSR
  assert.match(source, /if \(msr != Rapl\.AmdPowerUnit && msr != Rapl\.AmdPackageEnergy && msr != Rapl\.IntelPowerUnit && msr != Rapl\.IntelPackageEnergy\) return false;/);
  // local clients only, one instance that nobody else may hold first, and
  // read-only for everyone but SYSTEM and Administrators
  assert.match(source, /PIPE_REJECT_REMOTE_CLIENTS/);
  assert.match(source, /FILE_FLAG_FIRST_PIPE_INSTANCE/);
  assert.match(source, /\(D;;GA;;;NU\)/);
  const sddl = /const string Sddl = "([^"]+)"/.exec(source)[1];
  for (const ace of sddl.match(/\(A;;[^;]+;;;[A-Z]+\)/g)) {
    if (!/;;;(SY|BA)\)$/.test(ace)) assert.match(ace, /^\(A;;0x120189;;;/, `${ace} may read only`);
  }
  // the device as PawnIO names it, the module's MSR read in the documented layout
  assert.match(source, /\\\\\?\\GLOBALROOT\\Device\\PawnIO/);
  assert.match(source, /IoctlExecuteFn = 0xA1B22104/);
  assert.match(source, /IoctlLoadBinary = 0xA1B22084/);
  assert.match(source, /MinVersion = 0x020000/);
});

test('SentryPower.cs compiles, and its maths match the Linux energy_uj figure', { skip: noRunner }, () => {
  const out = path.join(tmpRoot, 'power-harness.exe');
  const r = compile(out, [CS, HARNESS], 'SentrySixManager.Tests.PowerHarness');
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
  const run = compiler.kind === 'csc' ? spawnSync(out, [], { encoding: 'utf8' }) : spawnSync('mono', [out], { encoding: 'utf8' });
  assert.equal(run.status, 0, `${run.stdout}${run.stderr}`);
  const got = Object.fromEntries(run.stdout.trim().split(/\r?\n/).map((l) => { const i = l.indexOf(' '); return [l.slice(0, i), l.slice(i + 1)]; }));
  assert.equal(Number(got.amdJpc), 0.5 ** 16, 'AMD ESU 16: 15.26 uJ per count');
  assert.equal(Number(got.intelJpc), 0.5 ** 14);
  assert.equal(Number(got.atomJpc), 32e-6, 'Silvermont: 2^ESU uJ');
  assert.equal(got.amd19, 'True');
  assert.equal(got.amd15, 'False', 'family 15h has no AMDFamily17 support');
  assert.equal(got.intelRaptor, 'True');
  assert.equal(got.intelNehalem, 'False');
  assert.equal(got.intelNetburst, 'False');
  assert.equal(got.cpu, 'amd 25 97 AMDFamily17.bin');
  assert.equal(got.cpuIntel, 'intel 6 151 IntelMSR.bin');
  assert.equal(got.cpuOld, 'none');
  assert.equal(got.wrapCounts, '6553600', 'one wrap of the 32-bit counter is counted');
  assert.equal(got.wrapUj, '100000000', '100 W for a second is 100 J');
  assert.equal(got.gapCounts, '6553600', 'a step after more than a minute is not counted');
  assert.equal(got.afterGapCounts, '7208960', 'counting goes on from the new value');
});
