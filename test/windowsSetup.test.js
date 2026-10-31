'use strict';

// SentrySixManager-Setup-<version>.exe (windows/setup/SentrySixManager.iss):
// Inno Setup cannot run here, so this checks what can go wrong without it:
// the Setup is wired to what build.ps1 passes and stages, it runs
// install.ps1 / uninstall.ps1 with switches they really have, its AppId and
// folders stay what install.ps1, uninstall.ps1 and windows.yml expect, the
// compiler is pinned like the other downloads, and the [Code] section's
// blocks balance.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');
const read = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8').replace(/\r\n/g, '\n');
const ISS = read('windows/setup/SentrySixManager.iss');
const BUILD = read('windows/build.ps1');
const INSTALL = read('windows/install.ps1');
const UNINSTALL = read('windows/uninstall.ps1');
const WINDOWS_YML = read('.github/workflows/windows.yml');
const APP_ID = '37874499-7900-4DA9-8103-133AFC180A56';
const APPS_KEY = `HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{${APP_ID}}_is1`;

function section(name) {
  const m = ISS.match(new RegExp(`^\\[${name}\\]\\n([\\s\\S]*?)(?=^\\[[A-Za-z]+\\]\\n|(?![\\s\\S]))`, 'm'));
  assert.ok(m, `the .iss has a [${name}] section`);
  return m[1];
}
function directive(name) {
  const m = section('Setup').match(new RegExp(`^${name}=(.*)$`, 'm'));
  return m ? m[1].trim() : null;
}
// the switches a script's param() block declares
function params(script) {
  const m = script.match(/\nparam\(([\s\S]*?)\n\)/);
  assert.ok(m, 'a param() block');
  return [...m[1].matchAll(/\$([A-Za-z]+)/g)].map((x) => x[1]);
}
const code = section('Code');
// [Code] without comments and string literals, scanned left to right (a
// comment may hold a quote, a string may hold a brace)
function stripPascal(src) {
  let out = '';
  for (let i = 0; i < src.length;) {
    const c = src[i];
    if (c === "'") {
      let j = i + 1;
      for (;;) {
        if (j >= src.length) throw new Error('unterminated string in [Code]');
        if (src[j] === "'" && src[j + 1] === "'") { j += 2; continue; }
        if (src[j] === "'") break;
        j++;
      }
      out += "''";
      i = j + 1;
    } else if (c === '{') {
      const j = src.indexOf('}', i);
      if (j < 0) throw new Error('unterminated { comment in [Code]');
      out += ' ';
      i = j + 1;
    } else if (c === '(' && src[i + 1] === '*') {
      const j = src.indexOf('*)', i + 2);
      if (j < 0) throw new Error('unterminated (* comment in [Code]');
      out += ' ';
      i = j + 2;
    } else if (c === '/' && src[i + 1] === '/') {
      const j = src.indexOf('\n', i);
      i = j < 0 ? src.length : j;
    } else {
      out += c;
      i++;
    }
  }
  return out;
}
const bare = stripPascal(code);

test('the AppId is fixed forever, and install.ps1, uninstall.ps1 and windows.yml use the same one', () => {
  assert.equal(directive('AppId'), `{{${APP_ID}}`);
  for (const [name, text] of [['install.ps1', INSTALL], ['uninstall.ps1', UNINSTALL], ['windows.yml', WINDOWS_YML]]) {
    assert.ok(text.includes(APPS_KEY), `${name} names ${APPS_KEY}`);
  }
});

test('the Setup installs where install.ps1 does, needs administrator rights, is 64-bit, and says who it is', () => {
  assert.equal(directive('DefaultDirName'), '{commonpf64}\\SentrySixManager');
  assert.match(INSTALL, /\$InstallDir = Join-Path \$programFiles 'SentrySixManager'/);
  assert.equal(directive('DisableDirPage'), 'yes');
  assert.equal(directive('UsePreviousAppDir'), 'no');
  assert.equal(directive('PrivilegesRequired'), 'admin');
  assert.equal(directive('ArchitecturesInstallIn64BitMode'), 'x64compatible');
  assert.equal(directive('ArchitecturesAllowed'), 'x64compatible');
  assert.equal(directive('AppName'), 'Sentry Six Manager');
  assert.equal(directive('UninstallDisplayName'), 'Sentry Six Manager');
  assert.equal(directive('AppPublisher'), 'Sentry Six');
  assert.equal(directive('UninstallDisplayIcon'), '{app}\\app-ui\\SentrySix.exe');
  assert.equal(directive('DisableWelcomePage'), 'no');
  // the signing hook is there, not switched on
  assert.match(section('Setup'), /^;SignTool=/m);
  assert.doesNotMatch(section('Setup'), /^SignTool=/m);
});

test('the uninstaller lives outside the program folder install.ps1 replaces, where uninstall.ps1 and windows.yml look', () => {
  const dir = directive('UninstallFilesDir');
  assert.equal(dir, '{commonpf64}\\SentrySixManager-Uninstall');
  assert.ok(!dir.startsWith('{app}'));
  assert.match(UNINSTALL, /\$SetupUninstallDir = Join-Path \$programFiles 'SentrySixManager-Uninstall'/);
  assert.match(WINDOWS_YML, /SentrySixManager-Uninstall\\unins000\.exe/);
  // install.ps1's swap would take it along if it were in there
  assert.match(INSTALL, /Rename-Item -LiteralPath \$InstallDir -NewName \(Split-Path \$OldDir -Leaf\)/);
});

test('every {#define} the .iss uses is defined there or passed by build.ps1, and the version is the zip\'s', () => {
  const used = new Set([...ISS.matchAll(/\{#([A-Za-z]+)\}/g)].map((m) => m[1]));
  const required = new Set([...ISS.matchAll(/^#ifndef ([A-Za-z]+)$/gm)].map((m) => m[1]));
  const passed = new Set([...BUILD.matchAll(/"\/D([A-Za-z]+)=/g)].map((m) => m[1]));
  for (const name of used) {
    if (name === 'SourcePath') continue; // ISPP's own
    assert.ok(required.has(name), `the .iss stops with #error when ${name} is missing`);
    assert.ok(passed.has(name), `build.ps1 passes /D${name}=`);
  }
  for (const name of required) assert.ok(passed.has(name), `build.ps1 passes /D${name}=`);
  assert.match(BUILD, /"\/DAppVersion=\$Version"/);
  assert.match(BUILD, /"\/DPayloadDir=\$Stage"/);
  assert.match(BUILD, /\$ZipName = "SentrySixManager-\$Version-\$Short-win-x64\.zip"/);
  assert.match(BUILD, /\$SetupName = "SentrySixManager-Setup-\$Version\.exe"/);
  assert.equal(directive('OutputBaseFilename'), 'SentrySixManager-Setup-{#AppVersion}');
  assert.equal(directive('AppVersion'), '{#AppVersion}');
  assert.match(BUILD, /Join-Path \$PSScriptRoot 'setup\\SentrySixManager\.iss'/);
  // in SHA256SUMS beside the zip, and the stage is still there when it compiles
  assert.match(BUILD, /foreach \(\$f in @\(\$ZipName, 'release\.json', \$SetupName\)\)/);
  assert.ok(BUILD.indexOf('& $Iscc @isccArgs') < BUILD.lastIndexOf('Remove-Item -LiteralPath $Stage -Recurse -Force'));
});

test('the payload is the stage folder build.ps1 zips, with the files the Setup runs', () => {
  const files = section('Files');
  assert.match(files, /^Source: "\{#PayloadDir\}\\\*"; DestDir: "\{tmp\}\\payload"; Flags: dontcopy recursesubdirs createallsubdirs$/m);
  assert.match(files, /^Source: "\{#SourcePath\}setup-user\.ps1"; DestDir: "\{tmp\}"; Flags: dontcopy$/m);
  assert.ok(fs.existsSync(path.join(REPO, 'windows/setup/setup-user.ps1')));
  // build.ps1 stages these, and checks them before compiling
  assert.match(BUILD, /foreach \(\$s in 'install\.ps1', 'uninstall\.ps1', 'update\.ps1', 'Install\.cmd'\)/);
  assert.match(BUILD, /foreach \(\$f in 'install\.ps1', 'uninstall\.ps1', 'app-ui\\SentrySix\.exe', 'app-ui\\SentrySix\.ico'\)/);
  assert.equal(directive('SetupIconFile'), '{#PayloadDir}\\app-ui\\SentrySix.ico');
  assert.ok(fs.existsSync(path.join(REPO, 'web/icon.png')), 'the wizard image');
  assert.match(code, /ExtractTemporaryFiles\('\{tmp\}\\payload\\\*'\)/);
  assert.match(code, /ExpandConstant\('\{tmp\}\\payload\\install\.ps1'\)/);
  assert.match(code, /ExpandConstant\('\{app\}\\uninstall\.ps1'\)/);
  // what "Open Sentry Six Manager" starts is what install.ps1 puts in the Start menu
  assert.match(section('Run'), /^Filename: "\{commonpf64\}\\SentrySixManager\\app-ui\\SentrySix\.exe";.*Flags: postinstall nowait skipifsilent$/m);
  assert.match(INSTALL, /\$AppExe = Join-Path \$InstallDir 'app-ui\\SentrySix\.exe'/);
  assert.match(INSTALL, /\$lnk\.TargetPath = \$AppExe/);
});

test('install.ps1 and uninstall.ps1 run hidden in 64-bit Windows PowerShell with switches they have', () => {
  assert.match(code, /\{sysnative\}\\WindowsPowerShell\\v1\.0\\powershell\.exe/);
  assert.match(code, /'-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' \+ Script \+ '" ' \+ Params/);
  assert.match(code, /ExecAndLogOutput\(PowerShellExe,[\s\S]*?SW_HIDE, ewWaitUntilTerminated, ResultCode, @OnOutputLine\)/);
  const inst = params(INSTALL);
  for (const sw of ['Service', 'NoService', 'ForUser']) {
    assert.ok(inst.includes(sw), `install.ps1 has -${sw}`);
    assert.ok(code.includes(`-${sw}`), `the Setup passes -${sw}`);
  }
  // always a mode: install.ps1 would otherwise ask on a console nobody sees
  assert.match(code, /Params := '-Service'\s+else\s+Params := '-NoService';/);
  const uninst = params(UNINSTALL);
  for (const sw of ['RemoveData', 'Force', 'FromSetup']) {
    assert.ok(uninst.includes(sw), `uninstall.ps1 has -${sw}`);
    assert.ok(code.includes(`-${sw}`), `the uninstaller passes -${sw}`);
  }
  // data is kept unless asked for
  assert.match(code, /\{param:REMOVEDATA\|0\}'\) = '1'/);
  assert.match(code, /MB_YESNO or MB_DEFBUTTON2\) = IDYES/);
  const helper = params(read('windows/setup/setup-user.ps1'));
  assert.ok(helper.includes('SetupName') && code.includes('-SetupName'));
});

test('/MODE= takes service or app, and the mode page says it in plain words', () => {
  assert.match(code, /ExpandConstant\('\{param:MODE\|\}'\)/);
  assert.match(code, /\(CmdMode <> 'service'\) and \(CmdMode <> 'app'\)/);
  assert.match(code, /Run in the background as a Windows service - always on, even when nobody is signed in \(recommended for a dedicated PC\)/);
  assert.match(code, /Run as an app when I sign in/);
  // install.ps1's record of the installed mode
  assert.match(INSTALL, /\$ModeKey = 'HKLM:\\SOFTWARE\\SentrySixManager'/);
  assert.match(code, /ModeKey = 'SOFTWARE\\SentrySixManager';/);
  assert.match(INSTALL, /-Name 'Mode' -Value \$Mode/);
  // a failure stops the Setup before Inno writes its entry
  assert.match(code, /function PrepareToInstall\(var NeedsRestart: Boolean\): String;/);
  assert.match(code, /procedure CurUninstallStepChanged\(CurUninstallStep: TUninstallStep\);[\s\S]*Abort;/);
});

test('the [Code] section balances its blocks and declares each function before calling it', () => {
  const words = bare.toLowerCase().match(/[a-z_][a-z0-9_]*/g);
  let depth = 0;
  for (const w of words) {
    if (w === 'begin' || w === 'try' || w === 'case' || w === 'record') depth++;
    else if (w === 'end') depth--;
    assert.ok(depth >= 0, 'an end without its begin');
  }
  assert.equal(depth, 0, 'every begin/try has its end');
  const declared = [...bare.matchAll(/^(?:function|procedure)\s+([A-Za-z]+)/gm)].map((m) => ({ name: m[1], at: m.index }));
  for (const { name, at } of declared) {
    const first = bare.search(new RegExp(`\\b${name}\\b`));
    assert.ok(first >= at, `${name} is used before it is declared`);
  }
  // ASCII only: ISCC and PowerShell 5.1 read BOM-less files as ANSI
  for (const rel of ['windows/setup/SentrySixManager.iss', 'windows/setup/setup-user.ps1']) {
    assert.doesNotMatch(read(rel), /[^\x00-\x7f]/, rel);
  }
});

test('the Inno Setup compiler is pinned like the other downloads', () => {
  const pin = JSON.parse(read('windows/versions.json')).innosetup;
  assert.ok(pin, 'versions.json has innosetup');
  assert.match(pin.version, /^6\.\d+\.\d+$/);
  assert.match(pin.sha256, /^[0-9a-f]{64}$/);
  assert.equal(pin.url, `https://github.com/jrsoftware/issrc/releases/download/is-${pin.version.replace(/\./g, '_')}/innosetup-${pin.version}.exe`);
  assert.match(BUILD, /foreach \(\$n in 'node', 'ffmpeg', 'innosetup'\)/);
  assert.match(BUILD, /Get-Pinned 'innosetup'/);
  assert.match(WINDOWS_YML, /foreach \(\$name in 'node', 'ffmpeg', 'innosetup'\)/);
});

test('windows.yml builds, proves and ships the Setup', () => {
  const job = (name) => {
    const m = WINDOWS_YML.match(new RegExp(`\\n  ${name}:\\n([\\s\\S]*?)(?=\\n  [a-z-]+:\\n|$)`));
    assert.ok(m, name);
    return m[1];
  };
  assert.match(job('package'), /name: SentrySixManager-Setup\n\s+path: dist\/\$\{\{ steps\.made\.outputs\.setup \}\}/);
  assert.match(job('package'), /setup: \$\{\{ steps\.made\.outputs\.setup \}\}/);
  const svc = job('install-check');
  const app = job('install-check-app');
  assert.match(svc, /'\/VERYSILENT', '\/SUPPRESSMSGBOXES', '\/NORESTART', '\/MODE=service'/);
  assert.match(app, /'\/VERYSILENT', '\/SUPPRESSMSGBOXES', '\/NORESTART', '\/MODE=app'/);
  for (const j of [svc, app]) {
    assert.match(j, /unins000\.exe/);
    assert.match(j, /'\/REMOVEDATA=1'/);
    assert.doesNotMatch(j, /-File pkg\\install\.ps1|uninstall\.ps1" -RemoveData/, 'through the Setup, not the scripts');
    assert.match(j, /Nothing is left/);
    assert.match(j, /https:\/\/127\.0\.0\.1/);
  }
  assert.match(svc, /not an administrator/);
  assert.match(svc, /expected one Settings > Apps entry/);
  const pub = job('publish');
  assert.match(pub, /gh release create "\$tag" "\$SETUP" "\$ZIP" SHA256SUMS release\.json/);
  assert.match(pub, /gh release upload "\$tag" "\$SETUP" "\$ZIP"/);
  // the 5.1 parse check covers the Setup's helper
  assert.match(WINDOWS_YML, /Get-ChildItem -Path windows, windows\\setup -Filter \*\.ps1/);
});

test('setup-user.ps1 writes the account name as UTF-8 bytes (Setup reads its output as UTF-8)', () => {
  const s = read('windows/setup/setup-user.ps1');
  assert.match(s, /New-Object System\.Text\.UTF8Encoding\(\$false\)/);
  assert.match(s, /\[Console\]::OpenStandardOutput\(\)/);
  assert.doesNotMatch(s, /\[Console\]::OutputEncoding\s*=/, 'setting OutputEncoding can throw without a console');
  // the last statement is the write, not a bare value through the host
  const code = s.split('\n').filter((l) => l.trim() && !l.trim().startsWith('#'));
  assert.match(code[code.length - 1], /\$out\.Flush\(\)/);
  assert.ok(/^[\x00-\x7f]*$/.test(s), 'the script is ASCII (5.1 reads BOM-less files as ANSI)');
});

test('a first install that fails is rolled back, so nothing is left without a Settings > Apps entry', () => {
  const m = ISS.match(/function PrepareToInstall[\s\S]*?\nend;\n/);
  assert.ok(m, 'PrepareToInstall');
  const body = m[0];
  assert.match(body, /HadFolder := DirExists\(ExpandConstant\('\{commonpf64\}\\SentrySixManager'\)\)/);
  assert.match(body, /\(PrevMode = ''\) and not HadFolder and FileExists\(Undo\)/);
  assert.match(body, /RunScript\(Undo, '-Force -FromSetup', UndoCode\)/);
  assert.doesNotMatch(body, /-RemoveData/, 'the rollback keeps the data');
});

test('Settings > Apps shows a version only once it works, and update.ps1 writes the old one back when it rolls back', () => {
  const at = INSTALL.indexOf("-Name 'DisplayVersion'");
  assert.ok(at > 0);
  assert.ok(at > INSTALL.indexOf("Say \"Uninstall: "), 'written after the summary, on the success path');
  assert.ok(at < INSTALL.indexOf('$finished = $true'));
  const UPDATE = read('windows/update.ps1');
  assert.ok(UPDATE.includes(APPS_KEY), 'update.ps1 uses the Setup\'s entry');
  const back = UPDATE.indexOf("-Name 'DisplayVersion'");
  assert.ok(back > UPDATE.indexOf('Move-Folder $Backup $InstallDir'), 'after the previous version is back');
});

test('install-check still proves the zip the updater installs holds what the Setup installed', () => {
  const job = WINDOWS_YML.slice(WINDOWS_YML.indexOf('\n  install-check:'), WINDOWS_YML.indexOf('\n  install-check-app:'));
  assert.match(job, /ZIP: \$\{\{ needs\.package\.outputs\.zip \}\}/);
  assert.match(job, /\[IO\.Compression\.ZipFile\]::ExtractToDirectory\(\$zip, \$pkg\)/);
  assert.match(job, /SHA256SUMS has no line for \$env:ZIP/);
  assert.match(job, /Get-FileHash -Algorithm SHA256 -LiteralPath \$there/);
});

test('no .iss line starts with # unless it is a preprocessor directive (ISPP reads every such line as one)', () => {
  const bad = ISS.split('\n').map((l, i) => [i + 1, l])
    .filter(([, l]) => /^\s*#/.test(l) && !/^\s*#(if|ifdef|ifndef|else|elif|endif|error|define|undef|include|emit|expr|sub|endsub|pragma|for)\b/.test(l));
  assert.deepEqual(bad, [], 'a Pascal line like "#13#10 + ..." must not start a line');
});

test('"Open Sentry Six Manager" on the last page opens the app as the person, not as administrator', () => {
  const run = section('Run');
  assert.match(run, /SentrySix\.exe".*Flags: [^\n]*\bpostinstall\b[^\n]*\bskipifsilent\b[^\n]*\brunasoriginaluser\b/);
});
