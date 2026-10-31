<#
  Makes the Windows release zip (used by .github/workflows/windows.yml).

    powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1

  Out come dist\SentrySixManager-<version>-<shortsha>-win-x64.zip, its
  SHA256SUMS and release.json (what server/releaseUpdates.js reads). The zip's
  root is the program folder as installed: node\ ffmpeg\ bin\ app-ui\ app\
  plus the install, uninstall and update scripts. Then
  dist\SentrySixManager-Setup-<version>.exe, what people download and run:
  the same files in one installer (windows\setup\SentrySixManager.iss, Inno
  Setup) that runs the same install.ps1. The zip stays for update.ps1 and
  the in-app updates.

    bin\      SentryService.exe, SentryHelper.exe (csc from .NET Framework)
    app-ui\   SentrySix.exe, the native app, with the files it reads at run
             time (Ui\<interface>\ themes and fonts, SentrySix.ico); built
             with the .NET SDK from windows\app\SentrySix.csproj
    app\      the server, as the Dockerfile copies it

  The only network use is the three pinned downloads in windows\versions.json
  (node, ffmpeg, the Inno Setup compiler), each checked against its SHA-256
  before anything in it is used. The app
  files are the ones the Dockerfile copies into /app, read from the Dockerfile
  and .dockerignore themselves, so the two builds ship the same app.

  Windows PowerShell 5.1 (inbox), no modules. The native app needs the .NET
  SDK (dotnet on PATH; GitHub's windows-latest has it) and the .NET Framework
  4.8 targeting pack (likewise); without that pack the build fetches the
  reference assemblies from NuGet.
#>
[CmdletBinding()]
param(
  [string]$OutDir,
  [string]$Repo,
  [string]$Sha,
  [string]$Branch,
  # downloads are kept here and reused when their SHA-256 still matches
  [string]$WorkDir,
  # the build time recorded in BUILD_INFO.json and release.json (ISO, UTC)
  [string]$BuiltAt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Invoke-WebRequest's progress bar slows downloads to a crawl in 5.1
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Inv = [Globalization.CultureInfo]::InvariantCulture
# node's JSON.parse refuses a byte order mark, so every file we write has none
$Utf8 = New-Object System.Text.UTF8Encoding $false

function Fail([string]$msg) { throw "build.ps1: $msg" }
function Write-Text([string]$path, [string]$text) { [IO.File]::WriteAllText($path, $text, $Utf8) }
function Prop($obj, [string]$name) {
  # StrictMode throws on a missing property; versions.json mistakes should read as such
  $p = $obj.PSObject.Properties[$name]
  if (-not $p) { Fail "versions.json: '$name' is missing" }
  $p.Value
}
# Windows PowerShell turns a native program's stderr into errors, which Stop
# would throw on; these callers check the exit code instead
function Invoke-Native([scriptblock]$block) {
  $eap = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try { & $block 2>$null } finally { $ErrorActionPreference = $eap }
}
function Git([string[]]$gitArgs) {
  $out = Invoke-Native { & git -C $Root @gitArgs }
  if ($LASTEXITCODE -ne 0) { return $null }
  ("$out").Trim()
}

if (-not $OutDir) { $OutDir = Join-Path $Root 'dist' }
if (-not $WorkDir) { $WorkDir = Join-Path $Root 'build\windows' }
New-Item -ItemType Directory -Force -Path $OutDir, $WorkDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$WorkDir = (Resolve-Path $WorkDir).Path

# ---- what is being built ---------------------------------------------------

if (-not $Sha) { $Sha = $env:GITHUB_SHA }
if (-not $Sha) { $Sha = Git @('rev-parse', 'HEAD') }
$Sha = "$Sha".ToLowerInvariant()
if ($Sha -notmatch '^[0-9a-f]{40}$') { Fail "no commit id: pass -Sha with the full 40-character id" }

if (-not $Branch) { $Branch = $env:GITHUB_REF_NAME }
if (-not $Branch) { $Branch = Git @('rev-parse', '--abbrev-ref', 'HEAD') }
if (-not $Branch -or $Branch -eq 'HEAD') { $Branch = '' }

if (-not $Repo) { $Repo = $env:GITHUB_REPOSITORY }
if (-not $Repo) {
  # a local build: the origin remote, as https://github.com/o/r(.git) or git@github.com:o/r(.git)
  $url = Git @('remote', 'get-url', 'origin')
  if ($url -match 'github\.com[/:]([^/]+)/([^/]+?)(\.git)?/?$') { $Repo = "$($Matches[1])/$($Matches[2])" }
}
# GitHub's naming rules, the same check releaseUpdates.js makes before it asks
if ("$Repo" -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}/[A-Za-z0-9._-]{1,100}$' -or $Repo -match '/\.\.?$') {
  Fail "no repository: pass -Repo owner/name (update notices and update.ps1 ask its GitHub Releases)"
}

if (-not $BuiltAt) { $BuiltAt = $env:BUILD_DATE }
if ($BuiltAt) {
  $when = [DateTimeOffset]::Parse($BuiltAt, $Inv, [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
} else {
  $when = [DateTimeOffset]::UtcNow
}
$BuiltAt = $when.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $Inv)

$Version = [string](Get-Content -Raw -LiteralPath (Join-Path $Root 'version.json') | ConvertFrom-Json).version
if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{0,39}$') { Fail "version.json has no usable version" }

# CRITICAL_UPDATE.txt read exactly as criticalFromFile (server/imageUpdates.js)
# and image.yml read it: `level: N` and `reason: text`, # lines are comments,
# the last of each wins; the reason becomes one line without quotes,
# backslashes or control characters, commas made semicolons, at most 300
# characters. A level without a reason is 0.
function Get-Critical([string]$path) {
  $level = 0; $reason = $null
  if (Test-Path -LiteralPath $path) {
    foreach ($line in ([IO.File]::ReadAllText($path, $Utf8) -split '\r?\n')) {
      if ($line -match '^\s*#') { continue }
      if ($line -notmatch '^\s*(level|reason):(.*)$') { continue }
      $key = $Matches[1].ToLowerInvariant(); $val = $Matches[2]
      if ($key -eq 'level') {
        $t = $val.Trim()
        $level = if ($t -match '^\d{1,6}$') { [int]$t } else { 0 }
      } else {
        $t = $val -replace '[\t\r]', ' ' -replace '[\x00-\x1f\x7f"''`\\]', '' -replace ',', ';'
        # escapes, not the characters: 5.1 reads a BOM-less script as ANSI, which mangles them
        $t = ($t -replace '[\x00-\x1f\x7f\u200B-\u200F\u202A-\u202E\u2066-\u2069]', ' ' -replace '\s+', ' ').Trim()
        if ($t.Length -gt 300) { $t = $t.Substring(0, 300).Trim() }
        $reason = if ($t) { $t } else { $null }
      }
    }
  }
  if ($level -gt 0 -and $reason) { [ordered]@{ level = $level; reason = $reason } }
  else { [ordered]@{ level = 0; reason = $null } }
}
$Critical = Get-Critical (Join-Path $Root 'CRITICAL_UPDATE.txt')

$Short = $Sha.Substring(0, 7)
$ZipName = "SentrySixManager-$Version-$Short-win-x64.zip"
$SetupName = "SentrySixManager-Setup-$Version.exe"
Write-Host "Building $ZipName ($Repo, branch '$Branch', $BuiltAt, critical level $($Critical.level))"

# ---- pinned downloads --------------------------------------------------------

$Pins = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'versions.json') | ConvertFrom-Json
# every pin: node, ffmpeg, Inno Setup, and PawnIO's installer and modules
# (the optional CPU power measurement, see windows\power\SentryPower.cs)
$PinNames = @('node', 'ffmpeg', 'innosetup', 'pawnio-setup', 'pawnio-modules')
$unset = @(foreach ($n in $PinNames) { if ((Prop (Prop $Pins $n) 'sha256') -notmatch '^[0-9a-fA-F]{64}$') { $n } })
if ($unset.Count) {
  Fail ("windows\versions.json has no SHA-256 for: $($unset -join ', '). " +
    "Download each url yourself, check it against the publisher's own checksum where there is one " +
    "(nodejs.org/dist/v<version>/SHASUMS256.txt for node), then run " +
    "(Get-FileHash -Algorithm SHA256 <file>).Hash.ToLower() and put the result in its sha256 field.")
}

function Get-Pinned([string]$name) {
  $pin = Prop $Pins $name
  $url = [string](Prop $pin 'url')
  $want = ([string](Prop $pin 'sha256')).ToLowerInvariant()
  if ($url -notmatch '^https://') { Fail "versions.json: $name url must be https" }
  $file = Join-Path $WorkDir ("$name-" + [IO.Path]::GetFileName(([Uri]$url).AbsolutePath))
  if (Test-Path -LiteralPath $file) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant() -eq $want) { return $file }
    Remove-Item -LiteralPath $file -Force
  }
  Write-Host "Downloading $name $(Prop $pin 'version')"
  $part = "$file.part"
  Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $part
  $got = (Get-FileHash -Algorithm SHA256 -LiteralPath $part).Hash.ToLowerInvariant()
  if ($got -ne $want) {
    Remove-Item -LiteralPath $part -Force
    Fail "$name download does not match its pinned SHA-256 (expected $want, got $got): refusing to build with it"
  }
  Move-Item -LiteralPath $part -Destination $file -Force
  $file
}

# Only the named entries leave the archive, each to a fixed name: nothing
# else in a downloaded zip can land anywhere.
function Expand-Pinned([string]$name, [string]$zipPath, [string]$destDir) {
  $pin = Prop $Pins $name
  New-Item -ItemType Directory -Force -Path $destDir | Out-Null
  $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
  try {
    $wanted = @(@((Prop $pin 'files').PSObject.Properties) | ForEach-Object { @{ to = $_.Name; from = [string]$_.Value; required = $true } })
    $notices = $pin.PSObject.Properties['notices']
    if ($notices) { $wanted += @(@($notices.Value.PSObject.Properties) | ForEach-Object { @{ to = $_.Name; from = [string]$_.Value; required = $false } }) }
    foreach ($w in $wanted) {
      if ($w.to -notmatch '^[A-Za-z0-9._-]+$') { Fail "versions.json: $name file name '$($w.to)' is not a plain name" }
      $entry = $zip.GetEntry($w.from)
      if (-not $entry) {
        if ($w.required) { Fail "$name archive has no $($w.from): fix its path in versions.json" }
        Write-Warning "$name archive has no $($w.from) (licence notice); the package goes without it"
        continue
      }
      [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destDir $w.to), $true)
    }
  } finally { $zip.Dispose() }
}

$Stage = Join-Path $WorkDir 'stage'
if (Test-Path -LiteralPath $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
New-Item -ItemType Directory -Path $Stage | Out-Null

Expand-Pinned 'node' (Get-Pinned 'node') (Join-Path $Stage 'node')
Expand-Pinned 'ffmpeg' (Get-Pinned 'ffmpeg') (Join-Path $Stage 'ffmpeg')

# PawnIO (https://pawnio.eu), for Setup's optional "Measure CPU power":
# its official signed installer, shipped byte for byte as published (its
# terms allow redistributing it unmodified, nothing else: no repacking, and
# never the -unrestricted developer edition), and the two signed modules
# SentryPower.exe embeds (AMDFamily17.bin, IntelMSR.bin; LGPL-2.1), each
# checked against its own SHA-256 as well as the zip's.
$PawnDir = Join-Path $Stage 'pawnio'
New-Item -ItemType Directory -Path $PawnDir | Out-Null
$pawnSetup = Prop $Pins 'pawnio-setup'
$pawnFile = [string](Prop $pawnSetup 'file')
if ($pawnFile -notmatch '^[A-Za-z0-9._-]+\.exe$') { Fail "versions.json: pawnio-setup file '$pawnFile' is not a plain .exe name" }
Copy-Item -LiteralPath (Get-Pinned 'pawnio-setup') -Destination (Join-Path $PawnDir $pawnFile)
$ModDir = Join-Path $WorkDir 'pawnio-modules'
if (Test-Path -LiteralPath $ModDir) { Remove-Item -LiteralPath $ModDir -Recurse -Force }
$pawnMods = Prop $Pins 'pawnio-modules'
Expand-Pinned 'pawnio-modules' (Get-Pinned 'pawnio-modules') $ModDir
foreach ($f in @((Prop $pawnMods 'fileSha256').PSObject.Properties)) {
  $p = Join-Path $ModDir $f.Name
  if (-not (Test-Path -LiteralPath $p)) { Fail "pawnio-modules: $($f.Name) was not extracted" }
  $got = (Get-FileHash -Algorithm SHA256 -LiteralPath $p).Hash.ToLowerInvariant()
  if ($got -ne ([string]$f.Value).ToLowerInvariant()) { Fail "pawnio-modules: $($f.Name) does not match its pinned SHA-256 (expected $($f.Value), got $got)" }
}
foreach ($n in @(@((Prop $pawnMods 'notices').PSObject.Properties) | ForEach-Object { $_.Name })) {
  $p = Join-Path $ModDir $n
  if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination (Join-Path $PawnDir $n) }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'notices\PawnIO.txt') -Destination (Join-Path $PawnDir 'PawnIO.txt')
$modLines = @(@((Prop $pawnMods 'fileSha256').PSObject.Properties) | ForEach-Object { "  $($_.Name)  sha256 $($_.Value)" })
$pawnReadme = @"
PawnIO, for the optional CPU power measurement

$pawnFile is PawnIO $($pawnSetup.version) by namazso (https://pawnio.eu,
source $($pawnSetup.source)), the official signed installer, exactly as
published at $($pawnSetup.url)
(sha256 $($pawnSetup.sha256)). Sentry Six Manager Setup runs it only when
"Measure CPU power" is ticked and PawnIO is not installed yet. See PawnIO.txt.

bin\SentryPower.exe embeds these signed modules of PawnIO.Modules
$($pawnMods.version) ($($pawnMods.source), release $($pawnMods.url)),
unmodified, under the GNU Lesser General Public License 2.1
(PawnIO.Modules-COPYING.txt):
$($modLines -join "`n")
"@
Write-Text (Join-Path $PawnDir 'README.txt') (($pawnReadme -replace '\r?\n', "`r`n") + "`r`n")

# what was pinned is what runs
$nodeVer = (& (Join-Path $Stage 'node\node.exe') --version).Trim()
if ($nodeVer -ne "v$(Prop $Pins.node 'version')") { Fail "node.exe says $nodeVer, versions.json pins $($Pins.node.version)" }
$encoders = (Invoke-Native { & (Join-Path $Stage 'ffmpeg\ffmpeg.exe') -hide_banner -encoders }) -join "`n"
$req = $Pins.ffmpeg.PSObject.Properties['requireEncoders']
if ($req) {
  $missing = @($req.Value | Where-Object { $encoders -notmatch "(?m)^\s*\S{6}\s+$([regex]::Escape($_))\s" })
  if ($missing.Count) { Fail "the pinned ffmpeg has no $($missing -join ', '): pick a build that has them" }
}

# ---- the two C# programs -------------------------------------------------------

# The csc.exe that comes with .NET Framework 4.8 on every Windows: no SDK, no NuGet.
$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $Csc)) { Fail "no .NET Framework 4.8 compiler at $Csc" }
$Bin = Join-Path $Stage 'bin'
New-Item -ItemType Directory -Path $Bin | Out-Null

function Build-Exe([string]$source, [string]$exe, [string[]]$refs, [string[]]$extra = @()) {
  $src = Join-Path $PSScriptRoot $source
  if (-not (Test-Path -LiteralPath $src)) { Fail "windows\$source is missing: it has to be written before a package can be built" }
  $cscArgs = @('/nologo', '/target:exe', '/optimize+', '/platform:x64', "/out:$(Join-Path $Bin $exe)")
  $cscArgs += @($refs | ForEach-Object { "/reference:$_" })
  $cscArgs += $extra
  $cscArgs += $src
  & $Csc @cscArgs
  if ($LASTEXITCODE -ne 0) { Fail "csc could not compile windows\$source" }
}
# the helper: WMI (smb.status, sys.info) and JSON lines
Build-Exe 'helper\SentryHelper.cs' 'SentryHelper.exe' @('System.dll', 'System.Core.dll', 'System.Management.dll', 'System.Web.Extensions.dll')
Build-Exe 'service\SentryService.cs' 'SentryService.exe' @('System.dll', 'System.Core.dll', 'System.ServiceProcess.dll')
# the CPU power service, with the two signed PawnIO modules embedded by name
Build-Exe 'power\SentryPower.cs' 'SentryPower.exe' @('System.dll', 'System.Core.dll', 'System.ServiceProcess.dll') @(
  @((Prop $pawnMods 'files').PSObject.Properties) | ForEach-Object { "/resource:$(Join-Path $ModDir $_.Name),$($_.Name)" })

# ---- the native app (SentrySix.exe) ----------------------------------------------------
#
# A WPF app for .NET Framework 4.8 (inbox on Windows 10/11), so nothing extra
# is installed with it; the .NET SDK only builds it. Its own build output
# (obj, bin) stays in build\windows-app; the finished files go to app-ui\.

$Dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $Dotnet) {
  Fail ("the .NET SDK is not installed (no dotnet on PATH): SentrySix.exe is built with it. " +
    "Install the .NET 8 SDK or newer from https://dotnet.microsoft.com/download and run build.ps1 again.")
}
$AppProject = Join-Path $PSScriptRoot 'app\SentrySix.csproj'
if (-not (Test-Path -LiteralPath $AppProject)) { Fail "windows\app\SentrySix.csproj is missing" }
$AppUi = Join-Path $Stage 'app-ui'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
Write-Host "Building SentrySix.exe with $($Dotnet.Source)"
$eap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
  & $Dotnet.Source build $AppProject -c Release -o $AppUi -nologo '-p:ContinuousIntegrationBuild=true' "-p:SourceRevisionId=$Sha" | Out-Host
  $rc = $LASTEXITCODE
} finally { $ErrorActionPreference = $eap }
if ($rc -ne 0) { Fail "dotnet build could not build windows\app\SentrySix.csproj (exit code $rc)" }
if (-not (Test-Path -LiteralPath (Join-Path $AppUi 'SentrySix.exe'))) { Fail "dotnet build made no SentrySix.exe in $AppUi" }
Write-Host "App UI: $(@(Get-ChildItem -LiteralPath $AppUi -Recurse -File).Count) files in app-ui\"

# ---- the app files: the Dockerfile's own list ------------------------------------
#
# The final stage's COPY lines after `WORKDIR /app` that copy from the build
# context (not --from another stage) are the app; .dockerignore then drops
# files from them as `docker build` does. Anything that does not fit this
# (another WORKDIR, a wildcard, a destination outside /app) stops the build
# rather than being guessed at, so a Dockerfile change cannot quietly leave
# the Windows package behind.

function Read-DockerLines([string]$path) {
  $lines = New-Object System.Collections.Generic.List[string]
  $cur = ''
  foreach ($l in ([IO.File]::ReadAllText($path, $Utf8) -split '\r?\n')) {
    if ($l -match '^\s*#') { continue }
    if ($l -match '\\\s*$') { $cur += ($l -replace '\\\s*$', ' '); continue }
    $lines.Add(($cur + $l).Trim()); $cur = ''
  }
  if ($cur) { $lines.Add($cur.Trim()) }
  $lines | Where-Object { $_ }
}

function Get-DockerCopies {
  $lines = @(Read-DockerLines (Join-Path $Root 'Dockerfile'))
  $last = -1
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^FROM\s') { $last = $i } }
  if ($last -lt 0) { Fail "Dockerfile has no FROM" }
  $workdir = $null
  $copies = @()
  for ($i = $last + 1; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match '^WORKDIR\s+(\S+)$') { $workdir = $Matches[1]; continue }
    if ($l -notmatch '^COPY\s+(.*)$') { continue }
    $words = @($Matches[1] -split '\s+' | Where-Object { $_ })
    if (@($words | Where-Object { $_ -like '--from=*' }).Count) { continue }  # from another stage: node, native
    $words = @($words | Where-Object { $_ -notlike '--*' })                   # --chown, --chmod: not on Windows
    if ($null -eq $workdir) { continue }                                      # system files (jellyfin.gpg)
    if ($workdir -ne '/app') { Fail "Dockerfile WORKDIR is $workdir, build.ps1 knows /app only" }
    if ($words.Count -lt 2) { Fail "Dockerfile line not understood: $l" }
    $dest = $words[-1]
    if ($dest -like '/app/*' -or $dest -eq '/app') { $dest = '.' + $dest.Substring(4) }
    if ($dest -like '/*') { Fail "Dockerfile copies to $dest, outside /app: $l" }
    foreach ($s in $words[0..($words.Count - 2)]) {
      if ($s -match '[*?\[]') { Fail "Dockerfile COPY with a wildcard is not supported here: $l" }
      $copies += [pscustomobject]@{ src = $s.TrimEnd('/'); dest = $dest; intoDir = ($words.Count -gt 2 -or $dest.EndsWith('/')) }
    }
  }
  if (-not $copies.Count) { Fail "found no COPY into /app in the Dockerfile" }
  ,$copies
}

# .dockerignore patterns as Docker reads them: paths from the context root,
# * and ? within one name, ** across names, ! re-includes, the last match wins,
# and a pattern that matches a folder takes everything in it.
function Get-IgnoreRules {
  $file = Join-Path $Root '.dockerignore'
  if (-not (Test-Path -LiteralPath $file)) { return @() }
  foreach ($l in ([IO.File]::ReadAllText($file, $Utf8) -split '\r?\n')) {
    $p = $l.Trim()
    if (-not $p -or $p.StartsWith('#')) { continue }
    $neg = $p.StartsWith('!')
    if ($neg) { $p = $p.Substring(1).Trim() }
    $p = ($p -replace '\\', '/').Trim('/') -replace '^\./', ''
    $rx = ''
    for ($i = 0; $i -lt $p.Length; $i++) {
      $c = $p[$i]
      if ($c -eq '*' -and $i + 1 -lt $p.Length -and $p[$i + 1] -eq '*') {
        $i++
        if ($i + 1 -lt $p.Length -and $p[$i + 1] -eq '/') { $i++; $rx += '(?:.*/)?' } else { $rx += '.*' }
      }
      elseif ($c -eq '*') { $rx += '[^/]*' }
      elseif ($c -eq '?') { $rx += '[^/]' }
      else { $rx += [regex]::Escape([string]$c) }
    }
    [pscustomobject]@{ rx = (New-Object regex ('^' + $rx + '$')); negate = $neg }
  }
}
$IgnoreRules = @(Get-IgnoreRules)

function Test-Ignored([string]$rel) {
  $parts = $rel -split '/'
  $ignored = $false
  foreach ($r in $IgnoreRules) {
    for ($n = 1; $n -le $parts.Count; $n++) {
      if ($r.rx.IsMatch(($parts[0..($n - 1)] -join '/'))) { $ignored = -not $r.negate; break }
    }
  }
  $ignored
}

$App = Join-Path $Stage 'app'
New-Item -ItemType Directory -Path $App | Out-Null
$appFiles = New-Object System.Collections.Generic.List[string]
foreach ($c in (Get-DockerCopies)) {
  $src = $c.src; $dest = $c.dest; $intoDir = $c.intoDir
  $srcPath = Join-Path $Root ($src -replace '/', '\')
  $destRel = ($dest -replace '^\./?', '').TrimEnd('/')
  if (-not (Test-Path -LiteralPath $srcPath)) { Fail "the Dockerfile copies $src, which does not exist" }
  $item = Get-Item -LiteralPath $srcPath -Force
  if ($item.PSIsContainer) {
    # COPY dir dest: the folder's contents go into dest
    $files = @(Get-ChildItem -LiteralPath $srcPath -Recurse -File -Force | Sort-Object FullName)
    foreach ($f in $files) {
      $inside = $f.FullName.Substring($srcPath.Length).TrimStart('\') -replace '\\', '/'
      if (Test-Ignored "$src/$inside") { continue }
      $to = (@($destRel, $inside) | Where-Object { $_ }) -join '/'
      $appFiles.Add("$src/$inside|$to")
    }
  } else {
    if (Test-Ignored $src) { continue }
    $to = if ($intoDir -or -not $destRel) { (@($destRel, [IO.Path]::GetFileName($srcPath)) | Where-Object { $_ }) -join '/' } else { $destRel }
    $appFiles.Add("$src|$to")
  }
}
foreach ($pair in $appFiles) {
  $from, $to = $pair -split '\|', 2
  $target = Join-Path $App ($to -replace '/', '\')
  New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
  Copy-Item -LiteralPath (Join-Path $Root ($from -replace '/', '\')) -Destination $target
}
Write-Host "App: $($appFiles.Count) files, as the Dockerfile copies them"

# BUILD_INFO.json: what releaseUpdates.js and Settings > Updates read
# (fields as agreed in docs/plans/windows-handoff.md; no image/tag, so
# imageUpdates stays idle and releaseUpdates takes over)
$buildInfo = [ordered]@{
  sha      = $Sha
  branch   = $Branch
  builtAt  = $BuiltAt
  channel  = 'windows'
  release  = $Repo
  version  = $Version
  platform = 'win32-x64'
  node     = [string](Prop $Pins.node 'version')
  ffmpeg   = [string](Prop $Pins.ffmpeg 'version')
  critical = $Critical
}
Write-Text (Join-Path $App 'BUILD_INFO.json') (($buildInfo | ConvertTo-Json -Depth 5) + "`n")

# ---- scripts and readme at the zip root ---------------------------------------------

foreach ($s in 'install.ps1', 'uninstall.ps1', 'update.ps1', 'cpupower.ps1', 'Install.cmd') {
  $p = Join-Path $PSScriptRoot $s
  if (-not (Test-Path -LiteralPath $p)) { Fail "windows\$s is missing: it has to be written before a package can be built" }
  Copy-Item -LiteralPath $p -Destination (Join-Path $Stage $s)
}
# cpupower.ps1 checks the PawnIO installer against its pinned SHA-256 again
# before it runs it: the hash goes into the packaged copy
$pawnSha = ([string](Prop $pawnSetup 'sha256')).ToLowerInvariant()
if ($pawnSha -notmatch '^[0-9a-f]{64}$') { Fail "versions.json: pawnio-setup has no SHA-256" }
$cpuStage = Join-Path $Stage 'cpupower.ps1'
$cpuText = [IO.File]::ReadAllText($cpuStage)
if (-not $cpuText.Contains("'@PAWNIO_SETUP_SHA256@'")) { Fail "windows\cpupower.ps1 has no '@PAWNIO_SETUP_SHA256@' for the pinned hash" }
Write-Text $cpuStage $cpuText.Replace("'@PAWNIO_SETUP_SHA256@'", "'$pawnSha'")

$readme = @"
Sentry Six Manager $Version for Windows (x64)
Built $BuiltAt from $Repo at $Short.

Install or upgrade
  The easy way: download SentrySixManager-Setup-$Version.exe from the same
  release and run it. It asks how to run (a Windows service, always on, or
  an app while you are signed in), installs, and shows the address and the
  setup code at the end. Running a newer one upgrades in place and keeps
  your data. Uninstall it in Settings > Apps.

  From this zip instead (what update.ps1 uses):
  Double-click Install.cmd and allow it to run as administrator. It installs to
  %ProgramFiles%\SentrySixManager, keeps settings and data in
  %ProgramData%\SentrySixManager, adds Sentry Six Manager to the Start menu
  (it also starts in the tray when you sign in) and prints the address, the
  setup code and the certificate fingerprint. The first time it asks whether
  to run the server as a Windows service (keeps converting after you sign
  out, starts with Windows); otherwise the app runs it while it is open.
  Running it again upgrades in place, keeps your data and the mode
  (install.ps1 -Service or -NoService switches).

Settings
  %ProgramData%\SentrySixManager\manager.env takes the same keys as the
  Docker .env file (PORT, TLS_MODE, FOOTAGE_PATH, ...). Restart the service
  after changing it.

Update
  As administrator: powershell -ExecutionPolicy Bypass -File "%ProgramFiles%\SentrySixManager\update.ps1"
  It downloads the latest release, checks its SHA-256, installs it and goes
  back to the old version by itself if the new one does not start.

Uninstall
  Settings > Apps > Sentry Six Manager > Uninstall, or as administrator:
  powershell -ExecutionPolicy Bypass -File "%ProgramFiles%\SentrySixManager\uninstall.ps1"
  Your data stays unless you choose to delete it (uninstall.ps1 -RemoveData).

Included: Node.js $($Pins.node.version) (node\) and FFmpeg $($Pins.ffmpeg.version) (ffmpeg\, GPL),
each with its licence, and PawnIO $($pawnSetup.version) with PawnIO.Modules $($pawnMods.version)
(pawnio\, for the optional CPU power measurement; see pawnio\README.txt).

CPU power (optional)
  install.ps1 -CpuPower installs the signed PawnIO driver (only if it is not
  installed yet) and the Sentry Six Manager CPU power service, so the server
  shows the CPU package watts; -NoCpuPower removes them again (PawnIO only if
  Sentry Six Manager installed it). Restart Windows if PawnIO asks for it.
"@
Write-Text (Join-Path $Stage 'README-Windows.txt') (($readme -replace '\r?\n', "`r`n") + "`r`n")

# ---- zip, SHA256SUMS, release.json ------------------------------------------------------
#
# Written entry by entry rather than with Compress-Archive: names sorted, with
# forward slashes (5.1's Compress-Archive writes backslashes that other unzip
# tools take as part of the name), and every entry dated with the build time.

$zipPath = Join-Path $OutDir $ZipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$names = [string[]]@(Get-ChildItem -LiteralPath $Stage -Recurse -File -Force |
  ForEach-Object { $_.FullName.Substring($Stage.Length).TrimStart('\') -replace '\\', '/' })
[Array]::Sort($names, [StringComparer]::Ordinal)
$fs = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
  $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
  try {
    foreach ($name in $names) {
      $ze = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
      $ze.LastWriteTime = $when
      $in = [IO.File]::OpenRead((Join-Path $Stage ($name -replace '/', '\')))
      try { $out = $ze.Open(); try { $in.CopyTo($out) } finally { $out.Dispose() } } finally { $in.Dispose() }
    }
  } finally { $zip.Dispose() }
} finally { $fs.Dispose() }

$release = [ordered]@{ sha = $Sha; builtAt = $BuiltAt; version = $Version; critical = $Critical }
$releasePath = Join-Path $OutDir 'release.json'
Write-Text $releasePath (($release | ConvertTo-Json -Depth 5) + "`n")

# ---- the Setup: SentrySixManager-Setup-<version>.exe ------------------------------------
#
# The stage folder (the zip's contents) inside one installer, compiled with
# the pinned Inno Setup. Its installer is run in its own portable mode into
# WorkDir: nothing is installed on this PC, and it is reused while its
# SHA-256 still matches.

function Get-Iscc {
  $pin = Prop $Pins 'innosetup'
  $ver = [string](Prop $pin 'version')
  if ($ver -notmatch '^6\.\d+\.\d+$') { Fail "versions.json: innosetup version '$ver' is not Inno Setup 6.x.y" }
  $want = ([string](Prop $pin 'sha256')).ToLowerInvariant()
  $dir = Join-Path $WorkDir "innosetup-$ver"
  $iscc = Join-Path $dir 'ISCC.exe'
  $mark = Join-Path $dir 'pinned.sha256'
  if ((Test-Path -LiteralPath $iscc) -and (Test-Path -LiteralPath $mark) -and ([IO.File]::ReadAllText($mark).Trim() -eq $want)) { return $iscc }
  $installer = Get-Pinned 'innosetup'
  if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
  $log = Join-Path $WorkDir "innosetup-$ver-unpack.log"
  Write-Host "Unpacking Inno Setup $ver into $dir (portable mode)"
  $p = Start-Process -FilePath $installer -Wait -PassThru -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/PORTABLE=1', '/NOICONS',
    "/DIR=`"$dir`"", "/LOG=`"$log`"")
  if ($p.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $iscc)) {
    Fail "the Inno Setup $ver installer did not unpack ISCC.exe into $dir (exit code $($p.ExitCode); see $log)"
  }
  Write-Text $mark "$want`n"
  $iscc
}

# VERSIONINFO wants four numbers: the leading digits of the version, the rest 0
$num = @(0, 0, 0, 0)
if ($Version -match '^(\d{1,5})(?:\.(\d{1,5}))?(?:\.(\d{1,5}))?(?:\.(\d{1,5}))?') {
  for ($i = 1; $i -le 4; $i++) { if ($Matches[$i]) { $num[$i - 1] = [Math]::Min(65535, [int]$Matches[$i]) } }
}
$NumVersion = $num -join '.'
$PayloadBytes = [long](Get-ChildItem -LiteralPath $Stage -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum
foreach ($f in 'install.ps1', 'uninstall.ps1', 'cpupower.ps1', 'app-ui\SentrySix.exe', 'app-ui\SentrySix.ico') {
  if (-not (Test-Path -LiteralPath (Join-Path $Stage $f))) { Fail "the stage has no $f, which the Setup needs" }
}
$Iscc = Get-Iscc
$setupPath = Join-Path $OutDir $SetupName
if (Test-Path -LiteralPath $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
Write-Host "Compiling $SetupName with $Iscc"
# (code signing, once set up: add /S<name>=<command> here; see the .iss)
$isccArgs = @('/Qp', "/O$OutDir", "/DAppVersion=$Version", "/DAppNumVersion=$NumVersion",
  "/DPayloadDir=$Stage", "/DPayloadBytes=$PayloadBytes", "/DRepo=$Repo",
  (Join-Path $PSScriptRoot 'setup\SentrySixManager.iss'))
$eap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
  & $Iscc @isccArgs | Out-Host
  $rc = $LASTEXITCODE
} finally { $ErrorActionPreference = $eap }
if ($rc -ne 0) { Fail "ISCC could not compile windows\setup\SentrySixManager.iss (exit code $rc)" }
if (-not (Test-Path -LiteralPath $setupPath)) { Fail "ISCC made no $setupPath" }

# sha256sum's format: "<hash>  <name>", LF, so `sha256sum -c` works too
$sums = foreach ($f in @($ZipName, 'release.json', $SetupName)) {
  "$((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutDir $f)).Hash.ToLowerInvariant())  $f"
}
Write-Text (Join-Path $OutDir 'SHA256SUMS') (($sums -join "`n") + "`n")

Remove-Item -LiteralPath $Stage -Recurse -Force
Write-Host "Wrote $zipPath, $SetupName, SHA256SUMS and release.json to $OutDir"
