'use strict';

// windows/cpupower.ps1: when Setup installs PawnIO (the driver other
// programs share) and when its uninstall removes it again. Checked on every
// OS from the source, and on Windows (or wherever PowerShell is) by running
// its decisions against made-up PCs (test/fixtures/cpupowerHarness.ps1):
// nothing is installed or removed there.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const { needsTool } = require('./fixtures/platformSkip');

const REPO = path.join(__dirname, '..');
const SCRIPT = path.join(REPO, 'windows', 'cpupower.ps1');
const HARNESS = path.join(__dirname, 'fixtures', 'cpupowerHarness.ps1');
const cpu = fs.readFileSync(SCRIPT, 'utf8');
const build = fs.readFileSync(path.join(REPO, 'windows', 'build.ps1'), 'utf8');
const install = fs.readFileSync(path.join(REPO, 'windows', 'install.ps1'), 'utf8');

function findPowerShell() {
  const candidates = process.platform === 'win32'
    ? [path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe'), 'pwsh']
    : ['pwsh'];
  for (const c of candidates) {
    if (spawnSync(c, ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.Major'], { encoding: 'utf8' }).status === 0) return c;
  }
  return null;
}
// the harness needs the registry (HKCU) for the marker: Windows only
const ps = process.platform === 'win32' ? findPowerShell() : null;
const noPs = process.platform !== 'win32'
  ? 'Windows only: the harness keeps its marker in HKCU (runs in the Windows workflow)'
  : needsTool(Boolean(ps), 'needs Windows PowerShell');

test('cpupower.ps1: the installer is checked against its pinned SHA-256 and signed CN=namazso exactly', () => {
  assert.match(cpu, /\$PawnSetupSha256 = '@PAWNIO_SETUP_SHA256@'/);
  assert.match(cpu, /\(Get-FileHash -Algorithm SHA256 -LiteralPath \$SetupExe\)\.Hash\.ToLowerInvariant\(\)/);
  assert.match(cpu, /\$signer -notmatch '\(\^\|, \)CN=namazso\(,\|\$\)'/);
  // the hash check comes before the installer runs
  assert.ok(cpu.indexOf('Get-FileHash') < cpu.indexOf("CpuPower-Run $SetupExe '-install -silent'"));
  // build.ps1 writes the pinned hash into the packaged copy
  assert.match(build, /\$cpuText\.Replace\("'@PAWNIO_SETUP_SHA256@'", "'\$pawnSha'"\)/);
  assert.match(build, /\$pawnSha = \(\[string\]\(Prop \$pawnSetup 'sha256'\)\)\.ToLowerInvariant\(\)/);
  const pins = JSON.parse(fs.readFileSync(path.join(REPO, 'windows', 'versions.json'), 'utf8'));
  assert.match(pins['pawnio-setup'].sha256, /^[0-9a-f]{64}$/);
});

test('cpupower.ps1: PawnIO counts as there by its Uninstall entry, its driver service or its device', () => {
  assert.match(cpu, /\$PawnServiceKey = 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\PawnIO'/);
  assert.match(cpu, /QueryDosDevice\('PawnIO'/);
  const inst = cpu.slice(cpu.indexOf('function Install-PawnIO'), cpu.indexOf('function Get-PawnUserCandidates'));
  assert.match(inst, /^\s+Clear-StalePawnMarker\s+\$have = Find-PawnIO/m);
  assert.doesNotMatch(inst, /Get-PawnIO\s*\n\s*if \(\$null -ne \$have\)/);
  // install.ps1 forgets a stale record before it decides anything
  const step = install.slice(install.indexOf('# --- 5b.'));
  assert.ok(step.indexOf('Clear-StalePawnMarker') < step.indexOf('if ($WantCpuPower)'));
});

test('cpupower.ps1: programs without an Uninstall entry are looked for before PawnIO is removed', () => {
  for (const where of ['Win32_Process', 'Get-ScheduledTask', 'CurrentVersion\\Run', 'RegistryHive]::Users', 'CommonStartup', 'Win32_Service']) {
    assert.ok(cpu.includes(where), where);
  }
  assert.match(cpu, /Fingerprint/);
});

test('cpupower.ps1 keeps PawnIO when another program may use it, removes only our own copy', { skip: noPs }, () => {
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'sm-cpupower-'));
  const testRoot = `HKCU:\\Software\\SentrySixManagerTest-${crypto.randomBytes(6).toString('hex')}`;
  const marker = `${testRoot}\\PawnIO`;
  try {
    const r = spawnSync(ps, ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', HARNESS,
      '-Script', SCRIPT, '-MarkerKey', marker, '-Work', work], { encoding: 'utf8' });
    assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
    const got = Object.fromEntries(r.stdout.trim().split(/\r?\n/).filter((l) => / /.test(l)).map((l) => { const i = l.indexOf(' '); return [l.slice(0, i), l.slice(i + 1)]; }));
    assert.equal(got.nothingElse, 'removed', 'ours, the same copy, nothing else uses it');
    assert.equal(got.runValue, 'kept', 'a Run value with FanControl.exe');
    assert.equal(got.scheduledTask, 'kept', 'a scheduled task that starts FanControl.exe');
    assert.equal(got.running, 'kept', 'LibreHardwareMonitor.exe running');
    assert.equal(got.unrelated, 'removed', 'other programs (and PawnIO\'s own service) are no reason to keep it');
    assert.equal(got.unsure, 'kept', 'in doubt it stays');
    assert.equal(got.reinstalled, 'kept', 'a copy installed again since is not ours');
    assert.equal(got.updated, 'kept');
    assert.equal(got.serviceOnly, 'ready ran=0 marked=False', 'a driver without an Uninstall entry is used, never installed over or marked');
    assert.equal(got.noPin, 'unavailable ran=0 marked=False', 'no pinned hash, no installer');
    assert.equal(got.wrongHash, 'unavailable ran=0 marked=False', 'a different file is never run');
  } finally {
    spawnSync(ps, ['-NoProfile', '-NonInteractive', '-Command', `Remove-Item -LiteralPath '${testRoot}' -Recurse -Force -ErrorAction SilentlyContinue`]);
    fs.rmSync(work, { recursive: true, force: true });
  }
});

test('cpupower.ps1 looks at this PC without failing', { skip: noPs }, () => {
  const r = spawnSync(ps, ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command',
    `$ErrorActionPreference = 'Stop'; Set-StrictMode -Version Latest; . '${SCRIPT}'; $s = Get-PawnUserCandidates; ` +
    "[Console]::Out.WriteLine(\"candidates $(@($s.Candidates).Count) unsure $(@($s.Unsure).Count)\"); " +
    "$null = Find-PawnIO; $null = Get-PawnFingerprint; $null = Select-PawnUsers $s.Candidates; [Console]::Out.WriteLine('ok')"], { encoding: 'utf8' });
  assert.equal(r.status, 0, `${r.stdout}${r.stderr}`);
  assert.match(r.stdout, /candidates [1-9]\d* unsure \d+/);
  assert.match(r.stdout, /^ok$/m);
});
