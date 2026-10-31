#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
  Installs or upgrades Sentry Six Manager.

  Run it from the unpacked release folder (Install.cmd does that for you):
    node\node.exe  ffmpeg\ffmpeg.exe ffprobe.exe  bin\SentryService.exe SentryHelper.exe
    app-ui\SentrySix.exe  app\...

  Program files go to %ProgramFiles%\SentrySixManager, everything the app keeps
  (accounts, settings, certificates, logs) to %ProgramData%\SentrySixManager.
  Running it again upgrades in place and keeps that data; manager.env is only
  ever created, never overwritten.

  Two ways to run the server:
    app mode (default)  the Sentry Six Manager app starts the server when it
                        starts (at sign-in, in the tray) and stops it on Quit;
                        it runs as the person who installed it
    service mode        a Windows service (NT SERVICE\SentrySixManager): keeps
                        converting after everyone signs out, starts with Windows;
                        the app connects to it
  Both get a Start menu entry and the app in the tray at every sign-in.
  With neither switch an installed copy keeps its mode, and a first install
  asks once (no answer, or no console to ask on: app mode).

  -Service             service mode (switches an app-mode install over)
  -NoService           app mode (switches a service install over: the service is removed)
  -ForUser DOMAIN\me   app mode: the account the server runs as, given read and
                       change rights on the data (Install.cmd passes whoever
                       double-clicked it; default: the account running this)
  -Port 9443           port for the web UI, first install only (later: PORT in manager.env)
  -DefenderExclusion   tell Microsoft Defender not to scan the data folder (faster saves)
#>
[CmdletBinding()]
param(
  [ValidateRange(1, 65535)]
  [int]$Port = 0,
  [switch]$DefenderExclusion,
  [switch]$Service,
  [switch]$NoService,
  [string]$ForUser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'SentrySixManager'
$ServiceAccount = 'NT SERVICE\SentrySixManager'
$FirewallRuleName = 'SentrySixManager-WebUI'
$DefaultPort = 9000
# which mode is installed, and for whom (read by this script on the next run,
# by update.ps1's run of it, and by uninstall.ps1)
$ModeKey = 'HKLM:\SOFTWARE\SentrySixManager'
$RunKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$RunValueName = 'SentrySixManager'
$ShortcutName = 'Sentry Six Manager.lnk'
# the Settings > Apps entry of SentrySixManager-Setup.exe (its AppId in
# windows\setup\SentrySixManager.iss); this script writes none of its own
$SetupAppsKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{37874499-7900-4DA9-8103-133AFC180A56}_is1'

function Say([string]$Text) { Write-Host $Text }
function Warn([string]$Text) { Write-Host "Warning: $Text" -ForegroundColor Yellow }

# Native tools do not throw on failure in PowerShell 5.1; check the exit code.
# stderr is never redirected: with Stop, 5.1 would turn each stderr line into
# a terminating error even when the tool succeeded.
function Run-Native([string]$File, [string[]]$Arguments, [string]$What) {
  & $File @Arguments | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "$What failed ($File exit code $LASTEXITCODE)." }
}

# The same with the command line written out as is. PowerShell re-quotes
# arguments that hold quotes (5.1 and 7 each their own way), and sc.exe then
# refuses the line (exit code 1639), so binPath goes this way.
function Run-NativeLine([string]$File, [string]$Line, [string]$What) {
  $info = New-Object System.Diagnostics.ProcessStartInfo
  $info.FileName = $File
  $info.Arguments = $Line
  $info.UseShellExecute = $false
  $proc = [System.Diagnostics.Process]::Start($info)
  try { $proc.WaitForExit(); $code = $proc.ExitCode } finally { $proc.Dispose() }
  if ($code -ne 0) { throw "$What failed ($File exit code $code)." }
}

# Removes every entry this SID has on a folder, if it has any. icacls cannot:
# it refuses a SID no account has (exit code 1332), such as the service's
# when there never was one, or it was just removed.
function Remove-SidEntries([string]$Path, [string]$Sid) {
  if (-not (Test-Path -LiteralPath $Path)) { return }
  $target = New-Object System.Security.Principal.SecurityIdentifier($Sid)
  $acl = Get-Acl -LiteralPath $Path
  $mine = @($acl.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference.Value -eq $target.Value })
  if ($mine.Count -eq 0) { return }
  $acl.PurgeAccessRules($target)
  Set-Acl -LiteralPath $Path -AclObject $acl
}

function Same-Path([string]$A, [string]$B) {
  $fa = [System.IO.Path]::GetFullPath($A).TrimEnd('\')
  $fb = [System.IO.Path]::GetFullPath($B).TrimEnd('\')
  return [string]::Equals($fa, $fb, [System.StringComparison]::OrdinalIgnoreCase)
}

# KEY=VALUE lines, # comments, optional surrounding quotes; the last one wins
# (the same reading the service host does).
function Read-EnvValue([string]$Path, [string]$Key) {
  $value = $null
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
  foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
    $t = $line.Trim()
    if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
    $eq = $t.IndexOf('=')
    if ($eq -lt 1) { continue }
    if ($t.Substring(0, $eq).Trim() -ne $Key) { continue }
    $v = $t.Substring($eq + 1).Trim()
    if ($v.Length -ge 2 -and (($v.StartsWith('"') -and $v.EndsWith('"')) -or ($v.StartsWith("'") -and $v.EndsWith("'")))) {
      $v = $v.Substring(1, $v.Length - 2)
    }
    $value = $v
  }
  return $value
}

function Wait-ServiceStopped($Service, [int]$Seconds) {
  try {
    $Service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds($Seconds))
  } catch {
    return $false
  }
  return $true
}

# The service log is open for writing while we read it.
function Read-LogFrom([string]$Path, [long]$From) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
  $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
  $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
  try {
    # smaller than before: it was rotated, so all of it is new
    if ($From -gt $fs.Length) { $From = 0 }
    $null = $fs.Seek($From, [System.IO.SeekOrigin]::Begin)
    $reader = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    return $reader.ReadToEnd()
  } finally {
    $fs.Dispose()
  }
}

function Log-Length([string]$Path) {
  if (Test-Path -LiteralPath $Path -PathType Leaf) { return (Get-Item -LiteralPath $Path).Length }
  return [long]0
}

function Last-Match([string]$Text, [string]$Pattern) {
  $found = $null
  foreach ($m in [regex]::Matches($Text, $Pattern)) { $found = $m.Groups[1].Value }
  return $found
}

# Local health probe. The app's certificate comes from its own CA, which this
# PC does not trust yet, so this one request (and only it) accepts any
# certificate: the callback sits on the request, not on the whole process.
# Windows PowerShell's .NET may not offer TLS 1.2 by default; it is added for
# the probe and the old setting put back.
$ProbeSource = @'
using System;
using System.Net;
namespace SentrySixInstall {
  public static class Probe {
    public static int Get(string url, int timeoutMs) {
      SecurityProtocolType saved = ServicePointManager.SecurityProtocol;
      try {
        ServicePointManager.SecurityProtocol = saved | SecurityProtocolType.Tls12;
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Timeout = timeoutMs;
        req.ReadWriteTimeout = timeoutMs;
        req.Proxy = null;
        req.KeepAlive = false;
        req.AllowAutoRedirect = false;
        req.ServerCertificateValidationCallback = delegate { return true; };
        using (HttpWebResponse res = (HttpWebResponse)req.GetResponse()) {
          return (int)res.StatusCode;
        }
      } catch (WebException e) {
        HttpWebResponse r = e.Response as HttpWebResponse;
        if (r != null) { int code = (int)r.StatusCode; r.Close(); return code; }
        return 0;
      } finally {
        ServicePointManager.SecurityProtocol = saved;
      }
    }
  }
}
'@

function Test-Health([string]$Url) {
  if (-not ('SentrySixInstall.Probe' -as [type])) { Add-Type -TypeDefinition $ProbeSource -Language CSharp }
  return ([SentrySixInstall.Probe]::Get($Url, 5000) -eq 200)
}

function Is-ReparsePoint([string]$Path) {
  $item = Get-Item -LiteralPath $Path -Force
  return (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

# First reparse point (junction, symlink, mount point) under Root, without
# following any: Get-ChildItem -Recurse in 5.1 walks into junctions.
function Find-ReparsePoint([string]$Root) {
  $stack = New-Object System.Collections.Generic.Stack[string]
  $stack.Push($Root)
  while ($stack.Count -gt 0) {
    $dir = New-Object System.IO.DirectoryInfo($stack.Pop())
    foreach ($e in $dir.EnumerateFileSystemInfos()) {
      if (($e.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $e.FullName }
      if (($e.Attributes -band [System.IO.FileAttributes]::Directory) -ne 0) { $stack.Push($e.FullName) }
    }
  }
  return $null
}

# Every user may create folders in ProgramData and owns what they create, so
# a manager.env found there is only trusted when an administrator owns it.
function Is-AdminOwned([string]$Path) {
  try {
    $owner = (Get-Acl -LiteralPath $Path).GetOwner([System.Security.Principal.SecurityIdentifier]).Value
  } catch {
    return $false
  }
  if ($owner -eq 'S-1-5-18' -or $owner -eq 'S-1-5-32-544') { return $true }
  if ($owner -eq [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value) { return $true }
  try {
    foreach ($m in @(Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop)) {
      if ($null -ne $m.SID -and $m.SID.Value -eq $owner) { return $true }
    }
  } catch { }
  return $false
}

# NT SERVICE\<name>'s SID, worked out as Windows does (SHA-1 of the upper-case
# name): its permission entries can be removed even once the service is gone.
function Get-ServiceSid([string]$Name) {
  $sha = [System.Security.Cryptography.SHA1]::Create()
  try { $h = $sha.ComputeHash([System.Text.Encoding]::Unicode.GetBytes($Name.ToUpperInvariant())) } finally { $sha.Dispose() }
  $parts = for ($i = 0; $i -lt 20; $i += 4) { [BitConverter]::ToUInt32($h, $i) }
  return 'S-1-5-80-' + ($parts -join '-')
}

function Get-RegValue([string]$Key, [string]$Name) {
  if (-not (Test-Path -LiteralPath $Key)) { return $null }
  $p = (Get-ItemProperty -LiteralPath $Key).PSObject.Properties[$Name]
  if ($null -eq $p) { return $null }
  return [string]$p.Value
}

# Programs running from the install folder: the app, and in app mode the
# server it started (SentryService.exe, node, the helper, ffmpeg).
function Get-OurProcesses([string]$Dir) {
  return @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith("$Dir\", [System.StringComparison]::OrdinalIgnoreCase) })
}

# A person's account (local, domain or Microsoft Entra), not a group or a
# built-in account: only such an account gets the data folder in app mode.
function Is-PersonSid([string]$Sid) {
  return ($Sid -match '^S-1-5-21-\d+-\d+-\d+-\d+$' -or $Sid -match '^S-1-12-1-\d+-\d+-\d+-\d+$')
}

function Sid-Name([string]$Sid) {
  try { return (New-Object System.Security.Principal.SecurityIdentifier($Sid)).Translate([System.Security.Principal.NTAccount]).Value } catch { return $Sid }
}

# ---------------------------------------------------------------------------

$programFiles = $env:ProgramFiles
$programData = $env:ProgramData
if ([string]::IsNullOrEmpty($programFiles) -or [string]::IsNullOrEmpty($programData) -or -not [System.IO.Path]::IsPathRooted($programFiles) -or -not [System.IO.Path]::IsPathRooted($programData)) {
  throw 'ProgramFiles or ProgramData is not set; cannot tell where to install.'
}
# 32-bit PowerShell would see Program Files (x86) and a redirected System32
if (-not [Environment]::Is64BitProcess) {
  throw 'Run this from 64-bit Windows PowerShell (Install.cmd does), not the 32-bit one.'
}

$Source = $PSScriptRoot
if ([string]::IsNullOrEmpty($Source)) { throw 'Run this as a script file (Install.cmd), not pasted into a window.' }
$InstallDir = Join-Path $programFiles 'SentrySixManager'
$Staging = "$InstallDir.new"
$OldDir = "$InstallDir.old"
$DataRoot = Join-Path $programData 'SentrySixManager'
$EnvFile = Join-Path $DataRoot 'manager.env'
$DataDir = Join-Path $DataRoot 'data'
$LogFile = Join-Path $DataDir 'logs\service.log'
$AppLogFile = Join-Path $DataDir 'logs\manager.log'
$SetupCodeFile = Join-Path $DataDir 'setup-code.txt'
$ServiceExe = Join-Path $InstallDir 'bin\SentryService.exe'
$NodeExe = Join-Path $InstallDir 'node\node.exe'
$AppExe = Join-Path $InstallDir 'app-ui\SentrySix.exe'
$ServiceSid = Get-ServiceSid $ServiceName
$ShortcutFile = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $ShortcutName

$stoppedByUs = $false
$finished = $false
$ver = $null

try {
  Say 'Sentry Six Manager setup'
  Say "  from:     $Source"
  Say "  programs: $InstallDir"
  Say "  data:     $DataRoot"

  # --- 1. the release folder must be complete before anything changes ---
  $fromInstallDir = Same-Path $Source $InstallDir
  if ($fromInstallDir) {
    Say 'Running from the installed folder: the program files stay as they are; service, permissions and firewall are set up again.'
  } else {
    Say 'Checking the release folder...'
    $required = @('node\node.exe', 'ffmpeg\ffmpeg.exe', 'ffmpeg\ffprobe.exe', 'bin\SentryService.exe', 'bin\SentryHelper.exe', 'app-ui\SentrySix.exe', 'app\server\index.js', 'app\version.json')
    $missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Source $_) -PathType Leaf) })
    if ($missing.Count -gt 0) {
      throw ("The release folder is incomplete; missing: " + ($missing -join ', ') + '. Unpack the whole zip again and run Install.cmd from it.')
    }
    try {
      $ver = (Get-Content -LiteralPath (Join-Path $Source 'app\version.json') -Raw | ConvertFrom-Json).version
      Say "Installing version $ver."
    } catch {
      Say 'Installing (version unknown).'
    }
  }

  # --- 1b. which mode: service or app ---
  if ($Service -and $NoService) { throw 'Choose -Service or -NoService, not both.' }
  $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
  $hadService = $null -ne $svc
  $prevMode = Get-RegValue $ModeKey 'Mode'
  $prevUserSid = Get-RegValue $ModeKey 'AppUserSid'
  # (installs from before the two modes had the service and no record)
  if ($null -eq $prevMode -and $hadService) { $prevMode = 'service' }
  if ($Service) {
    $Mode = 'service'
  } elseif ($NoService) {
    $Mode = 'app'
  } elseif ($prevMode -eq 'service' -or $prevMode -eq 'app') {
    $Mode = $prevMode
    Say "Keeping the installed mode ($Mode mode)."
  } else {
    $answer = $null
    try {
      $answer = Read-Host 'Run in the background as a Windows service? It keeps converting after you sign out and starts with Windows. (y/N)'
    } catch {
      Say 'No console to ask on: installing without the service (pass -Service for it).'
    }
    if ("$answer".Trim() -match '^(?i:y|yes)$') { $Mode = 'service' } else { $Mode = 'app' }
  }

  $AppUserSid = $null
  if ($Mode -eq 'app') {
    if (-not [string]::IsNullOrEmpty($ForUser)) {
      try {
        $AppUserSid = (New-Object System.Security.Principal.NTAccount($ForUser)).Translate([System.Security.Principal.SecurityIdentifier]).Value
      } catch {
        throw "Windows does not know the account '$ForUser' (-ForUser). Nothing was changed."
      }
    } elseif ($prevMode -eq 'app' -and -not [string]::IsNullOrEmpty($prevUserSid)) {
      # an upgrade (update.ps1, another administrator): the same person as before
      $AppUserSid = $prevUserSid
    } else {
      $AppUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    }
    if (-not (Is-PersonSid $AppUserSid)) {
      throw "$(Sid-Name $AppUserSid) is not a person's account, so the server cannot run as it. Run the installer with -ForUser DOMAIN\name, or with -Service. Nothing was changed."
    }
    Say "Mode: app (the Sentry Six Manager app runs the server as $(Sid-Name $AppUserSid))."
  } else {
    Say 'Mode: Windows service (runs without anyone signed in).'
  }

  # --- 2. stop the running service and the app (an upgrade keeps all data) ---
  if ($null -ne $svc) {
    if ($svc.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
      Say 'Stopping the running service (it finishes the clip it is saving; up to 2 minutes)...'
      try { $svc.Stop() } catch { }
      if (-not (Wait-ServiceStopped $svc 120)) {
        throw 'The service did not stop within 2 minutes. Nothing was changed; try again, or stop it in Services first.'
      }
      $stoppedByUs = $true
      Say 'Service stopped.'
    } else {
      Say 'The service is installed and stopped; upgrading it.'
    }
  } elseif ($prevMode -eq 'app') {
    Say 'Installed without a service (app mode).'
  } else {
    Say 'No service yet: this is a first install.'
  }

  # the app, and in app mode the server it runs, hold files in the program
  # folder and the port: they must close first. Quitting the app stops its
  # server after the clip it is saving.
  $running = @(Get-OurProcesses $InstallDir)
  if ($running.Count -gt 0) {
    Say "Sentry Six Manager is running ($((@($running | ForEach-Object { $_.Name } | Sort-Object -Unique)) -join ', ')). Quit it: right-click its tray icon > Quit (for every signed-in user). Waiting up to 2 minutes..."
    $until = (Get-Date).AddSeconds(120)
    while ($running.Count -gt 0 -and (Get-Date) -lt $until) {
      Start-Sleep -Seconds 2
      $running = @(Get-OurProcesses $InstallDir)
    }
    if ($running.Count -gt 0) {
      throw "Programs from $InstallDir are still running ($((@($running | ForEach-Object { "$($_.Name) (pid $($_.ProcessId))" })) -join ', ')). Nothing was changed; quit Sentry Six Manager and run the installer again."
    }
    Say 'It has quit.'
  }

  # --- 3. program files: copy to a staging folder, then swap ---
  # A failed or interrupted copy leaves only the staging folder behind; the
  # installed version is replaced only once the whole new copy is in place.
  if (-not $fromInstallDir) {
    foreach ($leftover in @($Staging, $OldDir)) {
      if (Test-Path -LiteralPath $leftover) {
        Say "Removing a leftover folder from an earlier run: $leftover"
        Remove-Item -LiteralPath $leftover -Recurse -Force
      }
    }
    Say 'Copying program files...'
    $null = New-Item -ItemType Directory -Path $Staging -Force
    # only the release's own folders and scripts: if the zip was unpacked
    # straight into Downloads, nothing else from there is copied
    foreach ($dir in @('node', 'ffmpeg', 'bin', 'app-ui', 'app')) {
      $robo = @((Join-Path $Source $dir), (Join-Path $Staging $dir), '/MIR', '/XJ', '/R:2', '/W:2', '/NP', '/NFL', '/NDL', '/NJH', '/NJS')
      & robocopy.exe @robo | Out-Host
      # robocopy: 0-7 is success (files copied, extra files removed, ...), 8+ is failure
      if ($LASTEXITCODE -ge 8) { throw "Copying the $dir folder failed (robocopy exit code $LASTEXITCODE). The installed version was not changed." }
    }
    foreach ($file in @('install.ps1', 'uninstall.ps1', 'update.ps1', 'Install.cmd')) {
      $from = Join-Path $Source $file
      if (Test-Path -LiteralPath $from -PathType Leaf) { Copy-Item -LiteralPath $from -Destination (Join-Path $Staging $file) -Force }
    }

    Say 'Putting the new program files in place...'
    if (Test-Path -LiteralPath $InstallDir) {
      try {
        Rename-Item -LiteralPath $InstallDir -NewName (Split-Path $OldDir -Leaf)
      } catch {
        Remove-Item -LiteralPath $Staging -Recurse -Force -ErrorAction SilentlyContinue
        throw "Could not replace $InstallDir (a window or program has a file in it open?). Close it and run the installer again. The installed version was not changed."
      }
    }
    try {
      Rename-Item -LiteralPath $Staging -NewName (Split-Path $InstallDir -Leaf)
    } catch {
      if (Test-Path -LiteralPath $OldDir) { Rename-Item -LiteralPath $OldDir -NewName (Split-Path $InstallDir -Leaf) }
      throw "Could not move the new program files into $InstallDir; the previous version was put back."
    }
    if (Test-Path -LiteralPath $OldDir) {
      try {
        Remove-Item -LiteralPath $OldDir -Recurse -Force
      } catch {
        Warn "Could not remove the previous version in $OldDir; delete it later."
      }
    }
    Say 'Program files are in place.'
  }

  # --- 4. data folders and manager.env ---
  # Every user may create folders in ProgramData and owns what they create, so
  # anything already here may have been planted before the first install. The
  # top folder is locked first (owner Administrators, nothing inherited, only
  # SYSTEM and Administrators; the service is added in step 6), so nobody else
  # can add or swap entries while the rest is checked and written.
  Say 'Preparing the data folder...'
  $dataPreExisted = Test-Path -LiteralPath $DataRoot
  if ($dataPreExisted) {
    if (-not (Test-Path -LiteralPath $DataRoot -PathType Container) -or (Is-ReparsePoint $DataRoot)) {
      throw "$DataRoot is not a plain folder (a file, junction or link). Remove it and run the installer again."
    }
  } else {
    $null = New-Item -ItemType Directory -Path $DataRoot
  }
  # owner first: icacls may take it even when the old owner shut us out, and
  # the owner may always rewrite the permissions
  Run-Native 'icacls.exe' @($DataRoot, '/setowner', '*S-1-5-32-544', '/C', '/L', '/Q') 'Taking ownership of the data folder'
  $rootAcl = New-Object System.Security.AccessControl.DirectorySecurity
  $rootAcl.SetOwner((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')))
  $rootAcl.SetAccessRuleProtection($true, $false)
  foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule((New-Object System.Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
    $rootAcl.AddAccessRule($rule)
  }
  (New-Object System.IO.DirectoryInfo($DataRoot)).SetAccessControl($rootAcl)

  # a manager.env someone else put here would choose the first account's
  # password, TLS_MODE and more: move it aside instead of using it
  if ((Test-Path -LiteralPath $EnvFile) -and -not (Is-AdminOwned $EnvFile)) {
    $aside = "$EnvFile.untrusted-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
    Rename-Item -LiteralPath $EnvFile -NewName (Split-Path $aside -Leaf)
    Write-Host ''
    Write-Host "WARNING: $EnvFile was not created by an administrator, so it is not used." -ForegroundColor Red
    Write-Host "  It was moved to $aside. Check it before copying anything from it." -ForegroundColor Red
    Write-Host ''
  }

  # the entries below the locked top folder: take them over (owner
  # Administrators, only inherited permissions; an owner could otherwise
  # grant itself access again) and refuse links. /L: the link itself, never
  # its target. On a first install over a folder that was already there, the
  # whole of data\ and run\ is taken over too (accounts, keys, sockets).
  $fixed = @($EnvFile, $DataDir, (Join-Path $DataDir 'logs'), (Join-Path $DataRoot 'run'), (Join-Path $DataRoot 'footage'))
  foreach ($p in $fixed) {
    if (-not (Test-Path -LiteralPath $p)) { continue }
    Run-Native 'icacls.exe' @($p, '/setowner', '*S-1-5-32-544', '/C', '/L', '/Q') "Taking ownership of $p"
    Run-Native 'icacls.exe' @($p, '/reset', '/C', '/L', '/Q') "Resetting permissions on $p"
    if (Is-ReparsePoint $p) { throw "$p is a junction or link, not a plain file or folder. Remove it and run the installer again." }
  }
  # (and when an app-mode install becomes a service: what the person's
  # account wrote is theirs, and an owner could grant itself access again)
  if ($dataPreExisted -and (($null -eq $prevMode -and -not $hadService) -or ($prevMode -eq 'app' -and $Mode -eq 'service'))) {
    foreach ($p in @($DataDir, (Join-Path $DataRoot 'run'))) {
      if (-not (Test-Path -LiteralPath $p -PathType Container)) { continue }
      $link = Find-ReparsePoint $p
      if ($null -ne $link) { throw "$link is a junction or link; nothing in $p may be one. Remove it and run the installer again." }
      Run-Native 'icacls.exe' @($p, '/setowner', '*S-1-5-32-544', '/T', '/C', '/L', '/Q') "Taking ownership of everything in $p"
      Run-Native 'icacls.exe' @($p, '/reset', '/T', '/C', '/L', '/Q') "Resetting permissions on everything in $p"
    }
  }
  foreach ($dir in @($DataDir, (Join-Path $DataDir 'logs'), (Join-Path $DataRoot 'run'), (Join-Path $DataRoot 'footage'))) {
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { $null = New-Item -ItemType Directory -Path $dir -Force }
  }

  if (Test-Path -LiteralPath $EnvFile -PathType Leaf) {
    Say "Keeping your settings in $EnvFile."
    if ($Port -ne 0) {
      Warn "-Port is only used on the first install. To change the port, edit PORT in $EnvFile and run the installer again."
    }
  } else {
    Say "Creating $EnvFile with the defaults (all optional; edit it, then restart the service)."
    if ($Port -ne 0) { $portLine = "PORT=$Port" } else { $portLine = "# PORT=$DefaultPort" }
    $envText = @"
# Sentry Six Manager settings. Everything is optional: vehicles, shares and
# the account are all managed in the web UI. After a change, restart the
# service (Services > Sentry Six Manager > Restart, or Restart-Service
# SentrySixManager in an administrator PowerShell).
#
# To turn a line on, delete the # in front of it.

# Port of the web UI (HTTPS).
$portLine

# Time zone (policy run times, logs). Leave it off to use this PC's time zone.
# TZ=America/Chicago

# Create the account on first start instead of with the setup code from
# the log (%ProgramData%\SentrySixManager\data\logs\service.log).
# UI_USERNAME=admin
# UI_PASSWORD=change-me

# HTTPS: the names / IP addresses you use to reach the server, so the
# certificate covers them (more can be added in Settings > Security).
# TLS_HOSTNAMES=192.168.1.20,mypc.local
# Your own certificate instead of the built-in local CA:
# TLS_CERT_FILE=C:\ProgramData\SentrySixManager\data\my-cert\fullchain.pem
# TLS_KEY_FILE=C:\ProgramData\SentrySixManager\data\my-cert\privkey.pem
# Plain HTTP, only behind a reverse proxy that does HTTPS:
# TLS_MODE=off
# TRUST_PROXY=true
# Refuse requests for any other host name (DNS rebinding protection):
# TRUSTED_HOSTS=192.168.1.20,mypc.local

# Create a first vehicle on first start (more can be added in Settings).
# VEHICLE_NAME=Daily Driver
# SMB_SHARE=//192.168.1.50/TeslaCam
# SMB_USERNAME=tesla
# SMB_PASSWORD=change-me
# SMB version: 3.1.1, 3.02, 3.0 or 3 (= 3.0 or newer); SMB 1/2 are not allowed.
# SMB_VERSION=3.1.1
# Encryption; false = signed only (others on the network could watch).
# SMB_ENCRYPT=true

# Settings > Updates checks twice a day whether a newer release is out (it
# never installs anything; you run the new Install.cmd). Turn it off:
# UPDATE_CHECK=off

# Extra folders the converter may write to, ';'-separated.
# EXTRA_ROOTS=D:\Converted

# Re-scan every vehicle this often (minutes).
# SCAN_INTERVAL_MINUTES=15

# Folder vehicles must be inside %ProgramData%\SentrySixManager\footage; more roots, ';'-separated:
# FOLDER_ROOTS=D:\TeslaCam
"@
    # no byte-order mark, Windows line ends (Notepad-friendly)
    $envText = ($envText -replace "`r?`n", "`r`n") + "`r`n"
    [System.IO.File]::WriteAllText($EnvFile, $envText, (New-Object System.Text.UTF8Encoding($false)))
  }

  $portText = Read-EnvValue $EnvFile 'PORT'
  $listenPort = $DefaultPort
  if (-not [string]::IsNullOrEmpty($portText)) {
    $parsed = 0
    if ([int]::TryParse($portText, [ref]$parsed) -and $parsed -ge 1 -and $parsed -le 65535) {
      $listenPort = $parsed
    } else {
      Warn "PORT=$portText in manager.env is not a port number; the app will not start until it is fixed. Using $DefaultPort for the firewall rule."
    }
  }
  $tlsMode = Read-EnvValue $EnvFile 'TLS_MODE'
  $https = -not ($null -ne $tlsMode -and $tlsMode.ToLowerInvariant() -eq 'off')
  if ($https) { $scheme = 'https' } else { $scheme = 'http' }
  # tls.js uses your own certificate only when both are set, and then logs no
  # fingerprint (the browser trusts it already)
  $ownCert = -not [string]::IsNullOrEmpty((Read-EnvValue $EnvFile 'TLS_CERT_FILE')) -and -not [string]::IsNullOrEmpty((Read-EnvValue $EnvFile 'TLS_KEY_FILE'))
  # HOST binds the app to one address; then only that address answers
  $listenHost = Read-EnvValue $EnvFile 'HOST'
  $anyHost = [string]::IsNullOrEmpty($listenHost) -or $listenHost -eq '0.0.0.0' -or $listenHost -eq '::'
  if ($anyHost) {
    $probeHost = '127.0.0.1'
  } elseif ($listenHost.Contains(':') -and -not $listenHost.StartsWith('[')) {
    $probeHost = "[$listenHost]"
  } else {
    $probeHost = $listenHost
  }

  # --- 5. the service (service mode), or none (app mode) ---
  if ($Mode -eq 'service') {
    # binPath must reach the service manager in quotes (the path has a space):
    # "\"C:\...\SentryService.exe\"" on the command line, written out as is
    # (Run-NativeLine); the registry is checked afterwards either way.
    $scArgs = 'binPath= "\"' + $ServiceExe + '\"" obj= "' + $ServiceAccount + '" start= delayed-auto depend= Tcpip/LanmanWorkstation DisplayName= "Sentry Six Manager"'
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
      Say "Creating the service $ServiceName (runs as ${ServiceAccount}: no password, not an administrator)..."
      Run-NativeLine 'sc.exe' "create $ServiceName $scArgs" 'Creating the service'
    } else {
      Say "Updating the service $ServiceName..."
      Run-NativeLine 'sc.exe' "config $ServiceName $scArgs" 'Updating the service'
    }
    $regKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $wantImage = '"' + $ServiceExe + '"'
    $haveImage = (Get-ItemProperty -LiteralPath $regKey -Name 'ImagePath').ImagePath
    if ($haveImage -ne $wantImage) {
      # an unquoted path with spaces is a known privilege-escalation hole
      Say 'Fixing the quoting of the service path.'
      Set-ItemProperty -LiteralPath $regKey -Name 'ImagePath' -Value $wantImage
    }
    # the virtual account is the service's own SID; make sure it has one
    Run-Native 'sc.exe' @('sidtype', $ServiceName, 'unrestricted') 'Setting the service SID type'
    Say 'Setting the service to restart 5 seconds after a failure...'
    Run-Native 'sc.exe' @('failure', $ServiceName, 'reset=', '60', 'actions=', 'restart/5000') 'Setting the failure actions'
    # also when it stops with an error code (the service gives up after 5 crashes in 60 s, exit code 2)
    Run-Native 'sc.exe' @('failureflag', $ServiceName, '1') 'Setting the failure actions for error exits'
    Run-Native 'sc.exe' @('description', $ServiceName, "Sentry Six Manager web app ($scheme`://localhost:$listenPort). Data and settings: $DataRoot") 'Setting the description'
  } else {
    # app mode: the app runs the server; a service from before is removed
    # (step 2 stopped it). The data stays as it is.
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $svc) {
      Say "Removing the service $ServiceName (the app runs the server from now on)..."
      & sc.exe delete $ServiceName | Out-Host
      # 1072: already marked for deletion (Services window open); it goes once that closes
      if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1072) { throw "Removing the service failed (sc.exe exit code $LASTEXITCODE)." }
      for ($i = 0; $i -lt 10; $i++) {
        if ($null -eq (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Seconds 1
      }
      if ($null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        Warn 'Windows removes the service once the Services window (or Task Manager) is closed.'
      }
      $stoppedByUs = $false
    }
  }
  # the mode is recorded once it is in effect
  if (-not (Test-Path -LiteralPath $ModeKey)) { $null = New-Item -Path $ModeKey -Force }
  Set-ItemProperty -LiteralPath $ModeKey -Name 'Mode' -Value $Mode -Type String
  if ($Mode -eq 'app') {
    Set-ItemProperty -LiteralPath $ModeKey -Name 'AppUserSid' -Value $AppUserSid -Type String
  } else {
    Remove-ItemProperty -LiteralPath $ModeKey -Name 'AppUserSid' -ErrorAction SilentlyContinue
  }

  # --- 6. permissions ---
  # Program files: everyone may read and run them (inherited from Program
  # Files, so only administrators can change them); the service the same.
  # Entries for whichever account the other mode used are removed, by SID,
  # so they go even when that service or account no longer exists.
  $subs = @('data', 'run', 'footage')
  $gone = @()
  if ($Mode -eq 'app') { $gone += $ServiceSid }
  if (-not [string]::IsNullOrEmpty($prevUserSid) -and $prevUserSid -ne $AppUserSid -and (Is-PersonSid $prevUserSid)) { $gone += $prevUserSid }
  foreach ($sid in $gone) {
    foreach ($p in @($InstallDir, $DataRoot) + @($subs | ForEach-Object { Join-Path $DataRoot $_ })) {
      Say "Removing $(Sid-Name $sid) from $p..."
      Remove-SidEntries $p $sid
    }
  }
  if ($Mode -eq 'service') {
    Say 'Setting permissions on the program files (users and the service: read and run)...'
    Run-Native 'icacls.exe' @($InstallDir, '/grant:r', '*S-1-5-32-545:(OI)(CI)RX', "$ServiceAccount`:(OI)(CI)RX", '/C', '/Q') 'Setting program file permissions'
  } else {
    Say 'Setting permissions on the program files (users: read and run)...'
    Run-Native 'icacls.exe' @($InstallDir, '/grant:r', '*S-1-5-32-545:(OI)(CI)RX', '/C', '/Q') 'Setting program file permissions'
  }

  # Data: nothing inherited from ProgramData (where every user may create
  # files); SYSTEM and Administrators full control; the account the server
  # runs as (the service, or in app mode the person's own) may change data,
  # run and footage but only read the top folder, so it cannot rewrite
  # manager.env (as in Docker, where .env is outside the container); other
  # users get nothing (the account, keys and passwords live here). Step 4
  # already replaced the top folder's permissions and reset the ones below
  # it, so only the server's entries are added here.
  if ($Mode -eq 'service') { $runAs = $ServiceAccount; $runAsName = 'the service' } else { $runAs = "*$AppUserSid"; $runAsName = Sid-Name $AppUserSid }
  Say "Setting permissions on the data folder (SYSTEM and Administrators: full; ${runAsName}: modify; other users: none)..."
  Run-Native 'icacls.exe' @($DataRoot, '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', "$runAs`:(OI)(CI)RX", '/C', '/Q') 'Setting data folder permissions'
  Run-Native 'icacls.exe' @($DataRoot, '/inheritance:r', '/C', '/Q') 'Turning off inherited permissions on the data folder'
  foreach ($sub in $subs) {
    $p = Join-Path $DataRoot $sub
    Run-Native 'icacls.exe' @($p, '/grant:r', "$runAs`:(OI)(CI)M", '/C', '/Q') "Letting $runAsName change $sub"
  }

  # --- 7. firewall ---
  # Private and Domain networks only: not reachable on public Wi-Fi.
  Say "Allowing TCP port $listenPort for node.exe on private and domain networks (not public)..."
  try {
    Get-NetFirewallRule -Name $FirewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    $null = New-NetFirewallRule -Name $FirewallRuleName -DisplayName 'Sentry Six Manager (web UI)' -Description "Sentry Six Manager on TCP $listenPort" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $listenPort -Program $NodeExe -Profile Private, Domain
  } catch {
    Warn "Could not add the firewall rule ($($_.Exception.Message)). Other devices may not reach the web UI until TCP $listenPort is allowed."
  }

  # --- 8. Defender (optional) ---
  if ($DefenderExclusion) {
    Say "Asking Microsoft Defender not to scan $DataDir (converter staging, thumbnails)..."
    try {
      Add-MpPreference -ExclusionPath $DataDir
    } catch {
      Warn "Could not add the Defender exclusion ($($_.Exception.Message)). Another antivirus, or Defender is managed by your organisation."
    }
  }

  # --- 8b. Start menu and sign-in (both modes) ---
  # The app for everyone: a Start menu entry, and in the tray at every
  # sign-in (in app mode it starts the server; in service mode it connects).
  Say "Adding Sentry Six Manager to the Start menu and starting it in the tray at sign-in..."
  try {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($ShortcutFile)
    $lnk.TargetPath = $AppExe
    $lnk.Arguments = ''
    $lnk.WorkingDirectory = Split-Path $AppExe
    $lnk.IconLocation = "$AppExe,0"
    $lnk.Description = 'Sentry Six Manager'
    $lnk.Save()
    $null = [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
  } catch {
    Warn "Could not add the Start menu entry ($($_.Exception.Message)). The app is $AppExe."
  }
  if (-not (Test-Path -LiteralPath $RunKey)) { $null = New-Item -Path $RunKey -Force }
  Set-ItemProperty -LiteralPath $RunKey -Name $RunValueName -Value ('"' + $AppExe + '" --tray') -Type String

  # --- 9. start and wait until it answers (service mode; in app mode the app starts it) ---
  $healthy = $false
  $logStart = Log-Length $LogFile
  $appLogStart = Log-Length $AppLogFile
  if ($Mode -eq 'service') {
    $logStart = Log-Length $LogFile
    $appLogStart = Log-Length $AppLogFile
    Say 'Starting the service...'
    try {
      Start-Service -Name $ServiceName
    } catch {
      throw "The service did not start ($($_.Exception.Message)). See $LogFile and Event Viewer > Windows Logs > Application."
    }
    $stoppedByUs = $false

    $healthUrl = "$scheme`://$probeHost`:$listenPort/healthz"
    Say "Waiting for the web UI to answer on $healthUrl ..."
    $deadline = (Get-Date).AddSeconds(180)
    $healthy = $false
    $probeBroken = $false
    while ((Get-Date) -lt $deadline) {
      try {
        if (Test-Health $healthUrl) { $healthy = $true; break }
      } catch {
        # the probe itself could not be built; do not report the app as broken
        $probeBroken = $true
        Warn "Could not check the web UI from here ($($_.Exception.Message))."
        break
      }
      $state = (Get-Service -Name $ServiceName).Status
      if ($state -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        throw "The service stopped again right after starting. See $LogFile and Event Viewer > Windows Logs > Application."
      }
      Start-Sleep -Seconds 2
    }
    if (-not $healthy -and -not $probeBroken) {
      throw "The service is running but the web UI did not answer within 3 minutes. See $LogFile."
    }
    if ($healthy) { Say 'The web UI is up.' }
  }

  # --- 10. what the user needs: URL, setup code, fingerprint (the docker logs analogue) ---
  $codePattern = 'enter the setup code ([A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5})'
  $fpPattern = 'HTTPS certificate fingerprint \(SHA-256\), as browsers show it: ([0-9A-Fa-f:]+)'
  $code = $null
  $fingerprint = $null
  # the lines are written while it starts; give them a moment to reach the file
  $readUntil = (Get-Date).AddSeconds(15)
  if ($Mode -ne 'service') { $readUntil = Get-Date }
  do {
    $text = (Read-LogFrom $LogFile $logStart) + "`n" + (Read-LogFrom $AppLogFile $appLogStart)
    $code = Last-Match $text $codePattern
    $fingerprint = Last-Match $text $fpPattern
    if ($null -ne $fingerprint -or -not $https -or $ownCert) { break }
    Start-Sleep -Seconds 1
  } while ((Get-Date) -lt $readUntil)
  if ($null -eq $code -and (Test-Path -LiteralPath $SetupCodeFile -PathType Leaf)) {
    $code = ([System.IO.File]::ReadAllText($SetupCodeFile)).Trim()
  }

  $urls = New-Object System.Collections.Generic.List[string]
  if (-not $anyHost) {
    $urls.Add("$scheme`://$probeHost`:$listenPort")
  } else {
    $urls.Add("$scheme`://localhost:$listenPort")
    try {
      $up = @(Get-NetAdapter -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { $_.ifIndex })
      if ($up.Count -gt 0) {
        $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object { ($up -contains $_.InterfaceIndex) -and -not $_.IPAddress.StartsWith('127.') -and -not $_.IPAddress.StartsWith('169.254.') } | ForEach-Object { $_.IPAddress } | Sort-Object -Unique)
        foreach ($ip in $ips) { $urls.Add("$scheme`://$ip`:$listenPort") }
      }
    } catch {
      Warn 'Could not list the network addresses of this PC.'
    }
  }

  Say ''
  if ($Mode -eq 'service') {
    Say 'Sentry Six Manager is installed and running.'
    Say 'Open the Sentry Six Manager app from the Start menu, or in a browser:'
  } else {
    Say 'Sentry Six Manager is installed.'
    Say "Start the Sentry Six Manager app from the Start menu (signed in as $(Sid-Name $AppUserSid)): it starts the server"
    Say 'and stays in the tray (it starts there at every sign-in). While it runs, browsers reach it at:'
  }
  foreach ($u in $urls) { Say "  $u" }
  Say ''
  if ($null -ne $code -and $code.Length -gt 0) {
    Say "Setup code for the first account: $code"
  } elseif (Test-Path -LiteralPath $SetupCodeFile -PathType Leaf) {
    Say "The setup code for the first account is in $SetupCodeFile and $LogFile."
  } elseif ($Mode -ne 'service') {
    Say "If there is no account yet, the setup code for the first one is written to $SetupCodeFile when the server first starts."
  } else {
    Say 'An account already exists: sign in with it.'
  }
  if ($https) {
    if ($ownCert) {
      Say 'HTTPS uses your own certificate (TLS_CERT_FILE, TLS_KEY_FILE in manager.env).'
    } elseif ($null -ne $fingerprint) {
      Say 'The browser warns about the certificate the first time. Go on only if it shows this fingerprint (SHA-256):'
      Say "  $fingerprint"
    } else {
      Say "The certificate fingerprint is in $LogFile (line 'HTTPS certificate fingerprint')."
    }
  } else {
    Warn 'TLS_MODE=off: plain HTTP. Only use this behind a reverse proxy that does HTTPS.'
  }
  Say ''
  $self = Join-Path $InstallDir 'install.ps1'
  if ($Mode -eq 'service') {
    Say 'Mode: Windows service. It runs without anyone signed in and starts with Windows.'
    Say "  To let the app run the server instead: the app's tray menu, or as administrator: powershell -ExecutionPolicy Bypass -File `"$self`" -NoService"
  } else {
    Say "Mode: app. The server runs while the app does (as $(Sid-Name $AppUserSid)) and stops when you quit it."
    Say "  To run it as a Windows service instead: the app's tray menu, or as administrator: powershell -ExecutionPolicy Bypass -File `"$self`" -Service"
  }
  Say "Settings: $EnvFile    Log: $LogFile"
  Say "Uninstall: $(Join-Path $InstallDir 'uninstall.ps1') (keeps your data unless -RemoveData)"
  # installed with the Setup: its entry shows this version after an
  # update.ps1 update too (only now that it works; update.ps1 writes the old
  # one back when it rolls back)
  if ($ver -and (Test-Path -LiteralPath $SetupAppsKey)) {
    try { Set-ItemProperty -LiteralPath $SetupAppsKey -Name 'DisplayVersion' -Value ([string]$ver) -Type String }
    catch { Warn "Could not update the version shown in Settings > Apps ($($_.Exception.Message))." }
  }
  $finished = $true
} catch {
  Write-Host ''
  Write-Host "Install did not finish: $($_.Exception.Message)" -ForegroundColor Red
  # an upgrade that failed before the new start: bring the service back
  if ($stoppedByUs) {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $svc) {
      Write-Host 'Starting the service again...'
      try { Start-Service -Name $ServiceName } catch { Write-Host "It did not start: $($_.Exception.Message)" -ForegroundColor Red }
    }
  }
}

if ($finished) { exit 0 } else { exit 1 }
