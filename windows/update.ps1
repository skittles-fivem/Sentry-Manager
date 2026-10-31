<#
  Updates an installed Sentry Six Manager to the latest release, as administrator:

    powershell -ExecutionPolicy Bypass -File "%ProgramFiles%\SentrySixManager\update.ps1"
    ... update.ps1 -Zip D:\SentrySixManager-<version>-<sha>-win-x64.zip   (a zip you downloaded)

  Asks GitHub, anonymously, for the latest release of the repository the
  installed copy was built from (app\BUILD_INFO.json), downloads its zip and
  SHA256SUMS and checks the zip against them before using anything in it.
  The upgrade is side by side: the current program folder is kept as
  SentrySixManager.previous while the new package's install.ps1 runs (which
  keeps manager.env and the data); if /healthz does not answer within a
  minute the old folder comes back and starts again. Settings, footage and
  the Recycle Bin in %ProgramData%\SentrySixManager are never touched here.

  Exit codes: 0 updated or already up to date, 1 nothing installed (an error
  before or during the install of a first copy), 2 the new version failed
  and the previous one was put back, 3 the new version failed and putting the
  previous one back did not finish (the message says which folder holds which).

  Windows PowerShell 5.1 (inbox), no modules.
#>
[CmdletBinding()]
param(
  # a release zip already on this PC; checked against the SHA256SUMS beside it, or -Sha256
  [string]$Zip,
  [string]$Sha256,
  # reinstall even when the release is the one installed
  [switch]$Force,
  [int]$HealthSeconds = 60,
  # set when this script restarts itself from a copy outside the install folder
  [switch]$Relaunched
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ServiceName = 'SentrySixManager'
$ZipRe = '^SentrySixManager-[A-Za-z0-9._+-]+-[0-9a-f]{7,40}-win-x64\.zip$'
$RepoRe = '^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}/[A-Za-z0-9._-]{1,100}$'

function Fail([string]$msg) { Write-Host "update.ps1: $msg" -ForegroundColor Red; exit 1 }
function Get-Field($obj, [string]$name) {
  if ($null -eq $obj) { return $null }
  $p = $obj.PSObject.Properties[$name]
  if ($p) { $p.Value } else { $null }
}

$me = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  Fail 'run this as administrator (right-click PowerShell, Run as administrator)'
}

# install.ps1 always installs here, so this is the only folder there is to update
if ([string]::IsNullOrEmpty($env:ProgramFiles) -or -not [IO.Path]::IsPathRooted($env:ProgramFiles)) { Fail '%ProgramFiles% is not set' }
$InstallDir = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'SentrySixManager')).TrimEnd('\')

# against PowerShell's current folder: 5.1 does not keep the process one (what
# GetFullPath uses) in step after cd, and the Set-Location below moves it anyway
if ($Zip) { $Zip = $PSCmdlet.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Zip) }

# This script usually lives in the folder it is about to rename, and Windows
# will not rename a folder a running script sits in: run from a copy in TEMP.
if (-not $Relaunched -and $PSCommandPath -and
    [IO.Path]::GetFullPath($PSCommandPath).StartsWith("$InstallDir\", [StringComparison]::OrdinalIgnoreCase)) {
  $copy = Join-Path ([IO.Path]::GetTempPath()) ("SentrySixManager-update-" + [Guid]::NewGuid().ToString('N') + '.ps1')
  Copy-Item -LiteralPath $PSCommandPath -Destination $copy
  $pass = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $copy, '-Relaunched', '-HealthSeconds', $HealthSeconds)
  if ($Zip) { $pass += @('-Zip', $Zip) }
  if ($Sha256) { $pass += @('-Sha256', $Sha256) }
  if ($Force) { $pass += '-Force' }
  try { & "$PSHOME\powershell.exe" @pass; $code = $LASTEXITCODE } finally { Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue }
  exit $code
}
Set-Location -LiteralPath ([IO.Path]::GetTempPath())

$infoPath = Join-Path $InstallDir 'app\BUILD_INFO.json'
$installed = $null
if (Test-Path -LiteralPath $infoPath) { $installed = Get-Content -Raw -LiteralPath $infoPath | ConvertFrom-Json }
elseif (-not $Zip) { Fail "no installed copy at $InstallDir (no app\BUILD_INFO.json); install with Install.cmd first" }
$installedSha = [string](Get-Field $installed 'sha')

$Temp = Join-Path ([IO.Path]::GetTempPath()) ("SentrySixManager-update-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Temp | Out-Null

function Get-Sums([string]$path) {
  $map = @{}
  foreach ($l in (Get-Content -LiteralPath $path)) {
    if ($l -match '^([0-9a-fA-F]{64}) [ *](.+)$') { $map[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant() }
  }
  $map
}

try {
  # ---- the package -----------------------------------------------------------------
  if ($Zip) {
    if (-not (Test-Path -LiteralPath $Zip -PathType Leaf)) { Fail "no file at $Zip" }
    $zipName = [IO.Path]::GetFileName($Zip)
    # The zip may sit where a non-admin can write (a second drive, a Downloads
    # folder): checked and unpacked from a copy in this admin's own TEMP, so it
    # cannot be swapped between the check and the install.ps1 run as admin.
    $zipPath = Join-Path $Temp $zipName
    Copy-Item -LiteralPath $Zip -Destination $zipPath
    $want = $null
    if ($Sha256) {
      if ($Sha256 -notmatch '^[0-9a-fA-F]{64}$') { Fail '-Sha256 takes the 64 hex digits of the zip''s SHA-256' }
      $want = $Sha256.ToLowerInvariant()
    } else {
      $sumsFrom = Join-Path (Split-Path $Zip) 'SHA256SUMS'
      if (-not (Test-Path -LiteralPath $sumsFrom -PathType Leaf)) { Fail "no SHA256SUMS next to the zip: download it from the same release, or pass -Sha256" }
      $sumsPath = Join-Path $Temp 'SHA256SUMS'
      Copy-Item -LiteralPath $sumsFrom -Destination $sumsPath
      $want = (Get-Sums $sumsPath)[$zipName]
      if (-not $want) { Fail "SHA256SUMS has no line for $zipName" }
    }
  } else {
    $repo = [string](Get-Field $installed 'repo')
    if ($repo -notmatch $RepoRe) { Fail 'the installed copy records no GitHub repository, so there is nowhere to look; use -Zip' }
    $headers = @{ 'User-Agent' = 'SentrySixManager-update'; 'Accept' = 'application/vnd.github+json' }
    Write-Host "Asking GitHub for the latest release of $repo"
    try { $rel = Invoke-RestMethod -UseBasicParsing -Headers $headers -Uri "https://api.github.com/repos/$repo/releases/latest" }
    catch { Fail "could not ask GitHub for the latest release: $($_.Exception.Message)" }
    # /releases/latest skips drafts and pre-releases already; never trust that alone
    if ((Get-Field $rel 'draft') -or (Get-Field $rel 'prerelease')) { Fail 'the latest release is a draft or pre-release: not installing it' }
    $assets = @(Get-Field $rel 'assets')
    $prefix = "https://github.com/$repo/releases/download/"
    function Get-Asset([scriptblock]$match, [string]$what) {
      $a = @($assets | Where-Object { $_ -and (& $match ([string](Get-Field $_ 'name'))) })
      if ($a.Count -ne 1) { Fail "release $(Get-Field $rel 'tag_name') has $($a.Count) $what files, expected one" }
      $url = [string](Get-Field $a[0] 'browser_download_url')
      # the release's own download link, never somewhere else
      if (-not $url.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { Fail "release $what link is not a $repo download" }
      @{ name = [string](Get-Field $a[0] 'name'); url = $url }
    }
    $zipAsset = Get-Asset { param($n) $n -cmatch $ZipRe } 'package zip'
    $sumsAsset = Get-Asset { param($n) $n -ceq 'SHA256SUMS' } 'SHA256SUMS'

    # release.json is small: when it names the installed commit there is nothing to download
    $relJson = @($assets | Where-Object { $_ -and (Get-Field $_ 'name') -ceq 'release.json' })
    if (-not $Force -and $installedSha -and $relJson.Count -eq 1) {
      try {
        $relPath = Join-Path $Temp 'release.json'
        Invoke-WebRequest -UseBasicParsing -Uri ([string](Get-Field $relJson[0] 'browser_download_url')) -OutFile $relPath
        $r = Get-Content -Raw -LiteralPath $relPath | ConvertFrom-Json
        if ([string](Get-Field $r 'sha') -ceq $installedSha) { Write-Host "Already up to date ($($installedSha.Substring(0, 7)))."; exit 0 }
      } catch { }  # the zip's own BUILD_INFO.json decides below
    }

    Write-Host "Downloading $($zipAsset.name)"
    $zipPath = Join-Path $Temp $zipAsset.name
    $sumsPath = Join-Path $Temp 'SHA256SUMS'
    Invoke-WebRequest -UseBasicParsing -Uri $sumsAsset.url -OutFile $sumsPath
    Invoke-WebRequest -UseBasicParsing -Uri $zipAsset.url -OutFile $zipPath
    $want = (Get-Sums $sumsPath)[$zipAsset.name]
    if (-not $want) { Fail "SHA256SUMS has no line for $($zipAsset.name)" }
  }

  $got = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
  if ($got -ne $want) { Fail "the zip's SHA-256 is $got, expected ${want}: not installing it (download it again)" }
  Write-Host 'SHA-256 checked.'

  $Pkg = Join-Path $Temp 'package'
  # refuses entries that would land outside the folder
  [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $Pkg)
  $newInfoPath = Join-Path $Pkg 'app\BUILD_INFO.json'
  if (-not (Test-Path -LiteralPath $newInfoPath) -or -not (Test-Path -LiteralPath (Join-Path $Pkg 'install.ps1'))) {
    Fail 'the zip is not a Sentry Six Manager Windows package (no app\BUILD_INFO.json or install.ps1)'
  }
  $newInfo = Get-Content -Raw -LiteralPath $newInfoPath | ConvertFrom-Json
  if ((Get-Field $newInfo 'channel') -ne 'windows') { Fail 'the zip is not a Windows package' }
  $newSha = [string](Get-Field $newInfo 'sha')
  if (-not $Force -and $installedSha -and $newSha -ceq $installedSha) {
    Write-Host "Already up to date ($($installedSha.Substring(0, 7)))."; exit 0
  }
  Write-Host "Updating $(if ($installedSha) { $installedSha.Substring(0, 7) } else { 'nothing installed' }) to $(Get-Field $newInfo 'version') ($($newSha.Substring(0, [Math]::Min(7, $newSha.Length))))"

  # ---- the port /healthz answers on (manager.env is only read) -------------------------
  $port = 9000
  $envFile = Join-Path $env:ProgramData 'SentrySixManager\manager.env'
  if (Test-Path -LiteralPath $envFile) {
    foreach ($l in (Get-Content -LiteralPath $envFile)) {
      if ($l -match '^\s*PORT\s*=\s*["'']?(\d{1,5})["'']?\s*$' -and [int]$Matches[1] -ge 1 -and [int]$Matches[1] -le 65535) { $port = [int]$Matches[1] }
    }
  }

  function Test-Ours([int]$procId) {
    $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
    try { [bool]($p -and $p.Path -and $p.Path.StartsWith("$InstallDir\", [StringComparison]::OrdinalIgnoreCase)) } catch { $false }
  }
  function Get-Leftovers {
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
      try { $_.Path -and $_.Path.StartsWith("$InstallDir\", [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
  }
  function Stop-App([switch]$Kill) {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    # the service gives node up to 60 s to finish its jobs
    if ($svc -and $svc.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force -WarningAction SilentlyContinue -ErrorAction SilentlyContinue }
    # node, the helper and ffmpeg must be gone before the folder can move
    for ($i = 0; $i -lt 30; $i++) {
      if (-not (Get-Leftovers).Count) { return $true }
      Start-Sleep -Seconds 1
    }
    if (-not $Kill) { return $false }
    # going back to the old version: a failed new one gets no more patience
    Get-Leftovers | Stop-Process -Force -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 10; $i++) {
      if (-not (Get-Leftovers).Count) { return $true }
      Start-Sleep -Seconds 1
    }
    $false
  }
  # A 200 alone proves nothing: any local user can bind the port while the
  # service is down, and a pass deletes the backup. The service must be running
  # and every listener on the port must be a program from the install folder.
  function Wait-Healthy {
    $until = (Get-Date).AddSeconds($HealthSeconds)
    while ((Get-Date) -lt $until) {
      try {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -Uri "http://127.0.0.1:$port/healthz"
        if ($svc -and $svc.Status -eq 'Running' -and $r.StatusCode -eq 200) {
          $owners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | ForEach-Object { $_.OwningProcess } | Sort-Object -Unique)
          if ($owners.Count -and -not @($owners | Where-Object { -not (Test-Ours $_) }).Count) { return $true }
        }
      } catch { }
      Start-Sleep -Seconds 2
    }
    $false
  }
  # antivirus and the search indexer hold files for a moment after a stop
  function Move-Folder([string]$from, [string]$to) {
    for ($i = 0; ; $i++) {
      try { [IO.Directory]::Move($from, $to); return }
      catch { if ($i -ge 9) { throw }; Start-Sleep -Seconds 2 }
    }
  }

  # ---- side by side ---------------------------------------------------------------------
  $Backup = "$InstallDir.previous"
  if (Test-Path -LiteralPath $Backup) {
    Fail "$Backup is still there from an earlier update and may be the only working copy; only if the installed version works, delete it and run update.ps1 again"
  }
  $hadInstall = Test-Path -LiteralPath $InstallDir
  if ($hadInstall) {
    Write-Host 'Stopping the service'
    $why = 'programs from that folder are still running'
    $moved = $false
    if (Stop-App) {
      try { Move-Folder $InstallDir $Backup; $moved = $true } catch { $why = $_.Exception.Message }
    }
    if (-not $moved) {
      Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
      Fail "could not set the current version aside ($why); nothing was changed"
    }
  }

  Write-Host 'Installing the new version'
  $ok = $false
  try {
    & "$PSHOME\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Pkg 'install.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "install.ps1 failed (exit $LASTEXITCODE)" -ForegroundColor Red }
    else { $ok = Wait-Healthy }
  } catch { Write-Host "install.ps1 failed: $($_.Exception.Message)" -ForegroundColor Red }

  if ($ok) {
    if ($hadInstall) { Remove-Item -LiteralPath $Backup -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "Updated: Sentry Six Manager $(Get-Field $newInfo 'version') answers on port $port." -ForegroundColor Green
    exit 0
  }

  if (-not $hadInstall) { Fail "the new version did not answer on port $port within $HealthSeconds s; see %ProgramData%\SentrySixManager\data\logs\service.log" }
  Write-Host "The new version did not answer on port $port within $HealthSeconds s: going back" -ForegroundColor Yellow
  if (-not (Stop-App -Kill)) { Write-Host 'some of its programs would not stop; trying anyway' -ForegroundColor Yellow }
  # Any failure here must still say where each version is: the next run refuses
  # while .previous exists, and that folder may be the only working copy.
  $failed = "$InstallDir.failed"
  $newAt = $InstallDir
  try {
    if (Test-Path -LiteralPath $InstallDir) {
      if (Test-Path -LiteralPath $failed) { Remove-Item -LiteralPath $failed -Recurse -Force }
      Move-Folder $InstallDir $failed
      $newAt = $failed
      Remove-Item -LiteralPath $failed -Recurse -Force -ErrorAction SilentlyContinue
    }
    Move-Folder $Backup $InstallDir
  } catch {
    Write-Host "Could not put the previous version back: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  The previous (working) version is in $Backup. Do not delete it." -ForegroundColor Red
    if (Test-Path -LiteralPath $newAt) { Write-Host "  The new (failed) version is in $newAt." -ForegroundColor Red }
    Write-Host "  Close anything using these folders, delete the new one, rename $Backup to $InstallDir, and start the $ServiceName service." -ForegroundColor Red
    Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
    exit 3
  }
  # the Setup's Settings > Apps entry: the version that is installed again
  $setupAppsKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{37874499-7900-4DA9-8103-133AFC180A56}_is1'
  if (Test-Path -LiteralPath $setupAppsKey) {
    try {
      $oldVer = (Get-Content -LiteralPath (Join-Path $InstallDir 'app\version.json') -Raw | ConvertFrom-Json).version
      if ($oldVer) { Set-ItemProperty -LiteralPath $setupAppsKey -Name 'DisplayVersion' -Value ([string]$oldVer) -Type String }
    } catch { Write-Host "Could not set the version shown in Settings > Apps back: $($_.Exception.Message)" -ForegroundColor Yellow }
  }
  Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
  $back = Wait-Healthy
  Write-Host ("Rolled back to $(if ($installedSha) { $installedSha.Substring(0, 7) } else { 'the previous version' })" +
    $(if ($back) { '; it answers again.' } else { ", but it does not answer on port $port either: see %ProgramData%\SentrySixManager\data\logs\service.log" })) -ForegroundColor Yellow
  exit 2
} finally {
  Remove-Item -LiteralPath $Temp -Recurse -Force -ErrorAction SilentlyContinue
}
