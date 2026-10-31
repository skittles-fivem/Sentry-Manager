#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
  Removes Sentry Six Manager in either mode: the service (service mode), the
  app's Start menu entry and its start at sign-in, the firewall rule and the
  program files (%ProgramFiles%\SentrySixManager). The app, and in app mode
  the server it runs, must be quit first (tray icon > Quit); it waits up to 2
  minutes for that (-Force: then ends them).

  Your data (%ProgramData%\SentrySixManager: accounts, settings, certificates,
  logs, footage folder) is kept, so installing again carries on where it left
  off. -RemoveData deletes it too, after asking you to type DELETE (-Force
  skips that question, for scripts).

  Safe to run more than once: whatever is already gone is skipped.

  Installed with SentrySixManager-Setup.exe, it also removes the Setup's
  entry in Settings > Apps and its uninstaller
  (%ProgramFiles%\SentrySixManager-Uninstall), once everything else is gone.
  That uninstaller runs this script with -FromSetup and removes those itself.
#>
[CmdletBinding()]
param(
  [switch]$RemoveData,
  [switch]$Force,
  [switch]$FromSetup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'SentrySixManager'
$FirewallRuleName = 'SentrySixManager-WebUI'
$ModeKey = 'HKLM:\SOFTWARE\SentrySixManager'
$RunKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$RunValueName = 'SentrySixManager'
$ShortcutName = 'Sentry Six Manager.lnk'
# SentrySixManager-Setup.exe's entry (its AppId in windows\setup\SentrySixManager.iss)
$SetupAppsKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{37874499-7900-4DA9-8103-133AFC180A56}_is1'

function Say([string]$Text) { Write-Host $Text }
function Warn([string]$Text) { Write-Host "Warning: $Text" -ForegroundColor Yellow }

# Remove-Item -Recurse in 5.1 walks into junctions and symlinks and deletes
# what is in their targets (a footage\Car junction to D:\TeslaCam would lose
# the real footage). So every link in the tree is removed first, as a link,
# without being followed; only then is the rest deleted.
function Remove-Links([string]$Root) {
  $rp = [System.IO.FileAttributes]::ReparsePoint
  $top = Get-Item -LiteralPath $Root -Force
  if (($top.Attributes -band $rp) -ne 0) {
    if ($top.PSIsContainer) { [System.IO.Directory]::Delete($top.FullName, $false) } else { [System.IO.File]::Delete($top.FullName) }
    return
  }
  if (-not $top.PSIsContainer) { return }
  $stack = New-Object System.Collections.Generic.Stack[string]
  $stack.Push($top.FullName)
  while ($stack.Count -gt 0) {
    $dir = New-Object System.IO.DirectoryInfo($stack.Pop())
    foreach ($e in @($dir.EnumerateFileSystemInfos())) {
      $isDir = ($e.Attributes -band [System.IO.FileAttributes]::Directory) -ne 0
      if (($e.Attributes -band $rp) -ne 0) {
        # deletes the link itself, never what it points to
        if ($isDir) { [System.IO.Directory]::Delete($e.FullName, $false) } else { [System.IO.File]::Delete($e.FullName) }
      } elseif ($isDir) {
        $stack.Push($e.FullName)
      }
    }
  }
}

# Files can stay locked for a moment after the service stops (antivirus,
# Explorer previews); try a few times before giving up.
function Remove-Folder([string]$Path) {
  for ($try = 1; $try -le 5; $try++) {
    if (-not (Test-Path -LiteralPath $Path)) { return $true }
    try {
      Remove-Links $Path
      if (-not (Test-Path -LiteralPath $Path)) { return $true }
      Remove-Item -LiteralPath $Path -Recurse -Force
      return $true
    } catch {
      if ($try -eq 5) {
        Warn "Could not remove $Path ($($_.Exception.Message)). Close anything using it and delete it by hand, or run this again."
        return $false
      }
      Start-Sleep -Seconds 2
    }
  }
  return $true
}

function Get-OurProcesses([string]$Dir) {
  return @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith("$Dir\", [System.StringComparison]::OrdinalIgnoreCase) })
}

$programFiles = $env:ProgramFiles
$programData = $env:ProgramData
if ([string]::IsNullOrEmpty($programFiles) -or [string]::IsNullOrEmpty($programData) -or -not [System.IO.Path]::IsPathRooted($programFiles) -or -not [System.IO.Path]::IsPathRooted($programData)) {
  throw 'ProgramFiles or ProgramData is not set; cannot tell what to remove.'
}
if (-not [Environment]::Is64BitProcess) {
  throw 'Run this from 64-bit Windows PowerShell, not the 32-bit one.'
}
$InstallDir = Join-Path $programFiles 'SentrySixManager'
$DataRoot = Join-Path $programData 'SentrySixManager'
$DataDir = Join-Path $DataRoot 'data'
$SetupUninstallDir = Join-Path $programFiles 'SentrySixManager-Uninstall'

$ok = $true
try {
  if ($RemoveData -and (Test-Path -LiteralPath $DataRoot)) {
    Write-Host ''
    Write-Host 'WARNING: -RemoveData deletes everything Sentry Six Manager keeps on this PC:' -ForegroundColor Red
    Write-Host "  $DataRoot" -ForegroundColor Red
    Write-Host '  accounts, settings (manager.env), vehicles, certificates, node pairings, logs,' -ForegroundColor Red
    Write-Host '  converted files and the Recycle Bin in data\, and everything in footage\.' -ForegroundColor Red
    Write-Host '  Footage on your NAS shares is not touched, nor anything a junction or link in' -ForegroundColor Red
    Write-Host '  there points to (only the link is removed). This cannot be undone.' -ForegroundColor Red
    if (-not $Force) {
      $answer = Read-Host 'Type DELETE to remove all of it (anything else keeps the data)'
      if ($answer -cne 'DELETE') {
        Say 'Keeping the data; uninstalling the program only.'
        $RemoveData = $false
      }
    }
  }

  # we may be running from inside the folder we are about to remove
  Set-Location -LiteralPath $env:SystemRoot

  # --- service ---
  $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
  if ($null -ne $svc) {
    if ($svc.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
      Say 'Stopping the service (it finishes the clip it is saving; up to 2 minutes)...'
      try { $svc.Stop() } catch { }
      try {
        $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(120))
      } catch {
        throw 'The service did not stop within 2 minutes. Nothing was removed; stop it in Services and run this again.'
      }
      Say 'Service stopped.'
    }
    Say "Removing the service $ServiceName..."
    & sc.exe delete $ServiceName | Out-Host
    # 1072: already marked for deletion (Services window open); it goes once that closes
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1072) { throw "Removing the service failed (sc.exe exit code $LASTEXITCODE)." }
    for ($i = 0; $i -lt 10; $i++) {
      if ($null -eq (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) { break }
      Start-Sleep -Seconds 1
    }
    if ($null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
      Warn 'Windows removes the service once the Services window (or Task Manager) is closed.'
    } else {
      Say 'Service removed.'
    }
  } else {
    Say 'No service installed; skipping.'
  }

  # --- the app (and in app mode its server): quit before its files go ---
  $running = @(Get-OurProcesses $InstallDir)
  if ($running.Count -gt 0) {
    Say "Sentry Six Manager is running ($((@($running | ForEach-Object { $_.Name } | Sort-Object -Unique)) -join ', ')). Quit it: right-click its tray icon > Quit (for every signed-in user). Waiting up to 2 minutes..."
    $until = (Get-Date).AddSeconds(120)
    while ($running.Count -gt 0 -and (Get-Date) -lt $until) {
      Start-Sleep -Seconds 2
      $running = @(Get-OurProcesses $InstallDir)
    }
    if ($running.Count -gt 0) {
      if (-not $Force) {
        throw "Programs from $InstallDir are still running ($((@($running | ForEach-Object { "$($_.Name) (pid $($_.ProcessId))" })) -join ', ')). Quit Sentry Six Manager and run this again (-Force ends them)."
      }
      Warn 'Ending them (-Force).'
      foreach ($p in $running) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
      for ($i = 0; $i -lt 10 -and @(Get-OurProcesses $InstallDir).Count -gt 0; $i++) { Start-Sleep -Seconds 1 }
    }
    Say 'It has quit.'
  }

  # --- Start menu entry and the start at sign-in ---
  $shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $ShortcutName
  if (Test-Path -LiteralPath $shortcut) {
    Say 'Removing the Start menu entry...'
    Remove-Item -LiteralPath $shortcut -Force
  } else {
    Say 'No Start menu entry; skipping.'
  }
  if ((Test-Path -LiteralPath $RunKey) -and $null -ne (Get-ItemProperty -LiteralPath $RunKey).PSObject.Properties[$RunValueName]) {
    Say 'Removing the start at sign-in...'
    Remove-ItemProperty -LiteralPath $RunKey -Name $RunValueName
  } else {
    Say 'No start at sign-in; skipping.'
  }
  # which mode was installed (install.ps1 reads it on the next install)
  if (Test-Path -LiteralPath $ModeKey) { Remove-Item -LiteralPath $ModeKey -Recurse -Force }

  # --- firewall ---
  $rule = @(Get-NetFirewallRule -Name $FirewallRuleName -ErrorAction SilentlyContinue)
  if ($rule.Count -gt 0) {
    Say 'Removing the firewall rule...'
    $rule | Remove-NetFirewallRule
  } else {
    Say 'No firewall rule; skipping.'
  }

  # --- Defender exclusion (added by install.ps1 -DefenderExclusion) ---
  try {
    $excluded = @((Get-MpPreference).ExclusionPath)
    if ($excluded -contains $DataDir) {
      Say 'Removing the Microsoft Defender exclusion for the data folder...'
      Remove-MpPreference -ExclusionPath $DataDir
    }
  } catch {
    # Defender not present or managed elsewhere: nothing of ours to remove
  }

  # --- program files ---
  $leftovers = @("$InstallDir.new", "$InstallDir.old")
  if (Test-Path -LiteralPath $InstallDir) {
    Say "Removing the program files in $InstallDir..."
    if (Remove-Folder $InstallDir) { Say 'Program files removed.' } else { $ok = $false }
  } else {
    Say 'No program files; skipping.'
  }
  foreach ($p in $leftovers) {
    if (Test-Path -LiteralPath $p) {
      if (-not (Remove-Folder $p)) { $ok = $false }
    }
  }

  # --- the Setup's entry in Settings > Apps (its own uninstaller does this itself) ---
  if (-not $FromSetup -and $ok) {
    if (Test-Path -LiteralPath $SetupAppsKey) {
      Say 'Removing the entry in Settings > Apps...'
      Remove-Item -LiteralPath $SetupAppsKey -Recurse -Force
    }
    if (Test-Path -LiteralPath $SetupUninstallDir) {
      if (-not (Remove-Folder $SetupUninstallDir)) { $ok = $false }
    }
  }

  # --- data ---
  if ($RemoveData) {
    if (Test-Path -LiteralPath $DataRoot) {
      Say "Removing the data in $DataRoot..."
      if (Remove-Folder $DataRoot) { Say 'Data removed.' } else { $ok = $false }
    } else {
      Say 'No data folder; skipping.'
    }
  } elseif (Test-Path -LiteralPath $DataRoot) {
    Say "Your data is kept in $DataRoot (installing again picks it up; -RemoveData deletes it)."
  }

  Say ''
  if ($ok) { Say 'Sentry Six Manager is uninstalled.' } else { Warn 'Uninstalled, but some files are left (see above).' }
} catch {
  Write-Host ''
  Write-Host "Uninstall did not finish: $($_.Exception.Message)" -ForegroundColor Red
  $ok = $false
}

if ($ok) { exit 0 } else { exit 1 }
