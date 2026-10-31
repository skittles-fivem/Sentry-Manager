<#
  The optional CPU power measurement: PawnIO (the signed driver, from
  https://pawnio.eu) and the Sentry Six Manager CPU power service
  (bin\SentryPower.exe, service SentrySixPower). Dot-sourced by install.ps1
  (-CpuPower / -NoCpuPower) and uninstall.ps1; Setup's "Measure CPU power"
  box passes -CpuPower or -NoCpuPower to install.ps1.

  The rules for PawnIO, a driver other programs share (FanControl,
  LibreHardwareMonitor and others):
    - installed only when it is not there at all (a copy that is there,
      whatever its version, is never installed over); its installer is the
      official one, unmodified, run with -install -silent
    - a copy that was there before is used when it is 2.0.0 or newer, never
      marked as ours, and never removed
    - PawnIO counts as there when its Uninstall entry, its driver service
      (Services\PawnIO) or its device is (a copy another tool deployed too)
    - ours (HKLM\SOFTWARE\SentrySixManager\PawnIO InstalledBySentry = 1,
      with the version and a fingerprint of the copy installed: its driver
      file's and uninstaller's paths and creation times) is removed with
      Sentry Six Manager only while it is still that very copy (someone
      updating or reinstalling it has made it theirs) and no program that
      uses PawnIO shows up: not installed, not running, not in a scheduled
      task, a Run key, a Startup folder or a service (portable FanControl
      and LibreHardwareMonitor have no Uninstall entry). In any doubt it
      stays, and only our record of it goes; the record also goes once
      PawnIO is gone
  Nothing here downloads anything: the installer is pawnio\PawnIO_setup.exe
  of the release (pinned in windows\versions.json), checked against its
  pinned SHA-256 when the package was built and again before it is run
  (build.ps1 writes the hash into this file), and signed CN=namazso.
#>

$PowerServiceName = 'SentrySixPower'
$PawnMarkerKey = 'HKLM:\SOFTWARE\SentrySixManager\PawnIO'
$PawnServiceKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\PawnIO'
$PawnMinVersion = [version]'2.0.0'
# The SHA-256 of pawnio\PawnIO_setup.exe as windows\versions.json pins it.
# build.ps1 writes it in here; a copy straight from the repository has none
# and so never runs a PawnIO installer.
$PawnSetupSha256 = '@PAWNIO_SETUP_SHA256@'
# programs that install with PawnIO or use it: their Uninstall entries'
# DisplayName, and the names of their programs (FanControl.exe,
# LibreHardwareMonitor.exe, ...) where they run from a folder of their own
$PawnDependents = 'Fan ?Control|Libre ?Hardware ?Monitor|Open ?Hardware ?Monitor|OpenRGB|Signal ?RGB|HWiNFO|OmenMon|G-?Helper|ZenTimings|ThrottleStop|Armou?ry ?Crate|AIDA64|Core ?Temp|HWMonitor'

function CpuPower-Say([string]$Text) { Write-Host $Text }
function CpuPower-Warn([string]$Text) { Write-Host "Warning: $Text" -ForegroundColor Yellow }

# A program with its arguments as written (no PowerShell re-quoting); its exit code.
function CpuPower-Run([string]$File, [string]$Line) {
  $info = New-Object System.Diagnostics.ProcessStartInfo
  $info.FileName = $File
  $info.Arguments = $Line
  $info.UseShellExecute = $false
  $proc = [System.Diagnostics.Process]::Start($info)
  try { $proc.WaitForExit(); return $proc.ExitCode } finally { $proc.Dispose() }
}

function CpuPower-Version([string]$Text) {
  $v = $null
  if ([version]::TryParse(("$Text".Trim() -replace '[^0-9.].*$', ''), [ref]$v)) { return $v }
  return $null
}

# A file's SHA-256, lowercase hex.
function CpuPower-Sha256([string]$Path) {
  $sha = [System.Security.Cryptography.SHA256]::Create()
  $stream = [System.IO.File]::OpenRead($Path)
  try { return (-join ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') })) } finally { $stream.Dispose(); $sha.Dispose() }
}

# One value of a registry key, or '' when the key or the value is not there.
function CpuPower-RegValue([string]$Key, [string]$Name) {
  if (-not (Test-Path -LiteralPath $Key)) { return '' }
  $p = Get-ItemProperty -LiteralPath $Key -ErrorAction SilentlyContinue
  if ($null -eq $p -or $null -eq $p.PSObject.Properties[$Name]) { return '' }
  return [string]$p.$Name
}

# Every Uninstall entry, in both registry views: @{ Name; Version; Location; Key }
function Get-UninstallEntries {
  $out = @()
  foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
    $base = $null
    try {
      $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
      $root = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
      if ($null -eq $root) { continue }
      try {
        foreach ($name in $root.GetSubKeyNames()) {
          $k = $root.OpenSubKey($name)
          if ($null -eq $k) { continue }
          try {
            $out += [pscustomobject]@{
              Key = $name
              Name = [string]$k.GetValue('DisplayName')
              Version = [string]$k.GetValue('DisplayVersion')
              Location = [string]$k.GetValue('InstallLocation')
            }
          } finally { $k.Close() }
        }
      } finally { $root.Close() }
    } catch { } finally { if ($null -ne $base) { $base.Close() } }
  }
  return $out
}

# PawnIO as its setup records it (Uninstall\PawnIO), or $null.
function Get-PawnIO {
  foreach ($e in @(Get-UninstallEntries)) {
    if ($e.Key -eq 'PawnIO') { return $e }
  }
  return $null
}

# The PawnIO driver's file, from its driver service (Services\PawnIO
# ImagePath), or $null.
function Get-PawnDriverFile {
  $img = (CpuPower-RegValue $PawnServiceKey 'ImagePath').Trim().Trim('"')
  if ($img -eq '') { return $null }
  $win = $env:SystemRoot
  if ($img -match '^\\SystemRoot\\(.+)$') { $img = Join-Path $win $Matches[1] }
  elseif ($img -match '^\\\?\?\\(.+)$') { $img = $Matches[1] }
  elseif ($img -match '^(?i:system32)\\') { $img = Join-Path $win $img }
  if ($img -notmatch '^[A-Za-z]:\\') { return $null }
  if (Test-Path -LiteralPath $img -PathType Leaf) { return $img }
  return $null
}

# Whether Windows has a \\.\PawnIO device (the driver is loaded).
function Test-PawnDevice {
  try {
    if (-not ('SentrySixCpuPower.Dos' -as [type])) {
      Add-Type -Namespace SentrySixCpuPower -Name Dos -MemberDefinition '[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint QueryDosDevice(string name, System.Text.StringBuilder target, int max);'
    }
    $sb = New-Object System.Text.StringBuilder 4096
    return [SentrySixCpuPower.Dos]::QueryDosDevice('PawnIO', $sb, 4096) -ne 0
  } catch { return $false }
}

<#
  PawnIO in any form, or $null: its setup's Uninstall entry, its driver
  service, or its device (a copy whose Uninstall entry is broken or gone,
  or that another tool deployed, counts as there too). @{ Version; Entry }:
  the version from the Uninstall entry, else from the driver file.
#>
function Find-PawnIO {
  $entry = Get-PawnIO
  $service = Test-Path -LiteralPath $PawnServiceKey
  if ($null -eq $entry -and -not $service -and -not (Test-PawnDevice)) { return $null }
  $ver = ''
  if ($null -ne $entry) { $ver = [string]$entry.Version }
  if ($ver -eq '') {
    $file = Get-PawnDriverFile
    if ($null -ne $file) {
      $fv = (Get-Item -LiteralPath $file).VersionInfo
      if ($null -ne $fv) { $ver = '{0}.{1}.{2}' -f $fv.FileMajorPart, $fv.FileMinorPart, $fv.FileBuildPart }
    }
  }
  return [pscustomobject]@{ Version = $ver; Entry = $entry }
}

<#
  What tells this very copy of PawnIO apart from one installed later (by
  anyone, after this one was removed): its version and the path and
  creation time of its driver file and of its uninstaller. '' when nothing
  identifies it.
#>
function Get-PawnFingerprint {
  $parts = @()
  $entry = Get-PawnIO
  if ($null -ne $entry -and -not [string]::IsNullOrEmpty($entry.Location)) {
    $u = Join-Path $entry.Location 'uninstall.exe'
    if (Test-Path -LiteralPath $u -PathType Leaf) { $parts += "uninstaller=$($u.ToLowerInvariant())@$((Get-Item -LiteralPath $u).CreationTimeUtc.Ticks)" }
  }
  $file = Get-PawnDriverFile
  if ($null -ne $file) { $parts += "driver=$($file.ToLowerInvariant())@$((Get-Item -LiteralPath $file).CreationTimeUtc.Ticks)" }
  if ($parts.Count -eq 0) { return '' }
  $ver = ''
  if ($null -ne $entry) { $ver = [string]$entry.Version }
  return (@("version=$ver") + $parts) -join ';'
}

function Get-PawnMarker {
  if (-not (Test-Path -LiteralPath $PawnMarkerKey)) { return $null }
  if ((CpuPower-RegValue $PawnMarkerKey 'InstalledBySentry') -ne '1') { return $null }
  return [pscustomobject]@{
    Version = CpuPower-RegValue $PawnMarkerKey 'Version'
    Fingerprint = CpuPower-RegValue $PawnMarkerKey 'Fingerprint'
  }
}

# Our record of installing PawnIO goes once PawnIO is gone (whoever removed it).
function Clear-StalePawnMarker {
  if ((Test-Path -LiteralPath $PawnMarkerKey) -and $null -eq (Find-PawnIO)) {
    Remove-Item -LiteralPath $PawnMarkerKey -Recurse -Force -ErrorAction SilentlyContinue
    CpuPower-Say 'PawnIO was removed since Sentry Six Manager installed it; its record of that is cleared.'
  }
}

<#
  PawnIO for the CPU power service. Returns 'ready', 'restart' (installed,
  Windows must restart before it loads) or 'unavailable' (it says why).
#>
function Install-PawnIO([string]$SetupExe) {
  Clear-StalePawnMarker
  $have = Find-PawnIO
  if ($null -ne $have) {
    # (not ours: never installed over, never marked as ours)
    $v = CpuPower-Version $have.Version
    if ($null -eq $v) {
      CpuPower-Say 'PawnIO is already installed (its version is not known); using it (it is left as it is).'
      return 'ready'
    }
    if ($v -ge $PawnMinVersion) {
      CpuPower-Say "PawnIO $($have.Version) is already installed; using it (it is left as it is)."
      return 'ready'
    }
    CpuPower-Warn "PawnIO $($have.Version) is installed, which is older than 2.0.0, so CPU power cannot be measured with it. It is left as it is: update it from https://pawnio.eu (or the program that installed it), then CPU power shows by itself."
    return 'unavailable'
  }
  if (-not (Test-Path -LiteralPath $SetupExe -PathType Leaf)) {
    CpuPower-Warn "The PawnIO installer is not in this release ($SetupExe), so CPU power cannot be measured."
    return 'unavailable'
  }
  # the very file windows\versions.json pins, checked again here (the
  # package may have been unpacked to a folder anyone could write to)
  $want = "$PawnSetupSha256".ToLowerInvariant()
  if ($want -notmatch '^[0-9a-f]{64}$') {
    CpuPower-Warn 'This copy of cpupower.ps1 has no pinned SHA-256 for the PawnIO installer (it was not built by build.ps1), so PawnIO is not installed.'
    return 'unavailable'
  }
  # (.NET, not Get-FileHash: that cmdlet is missing when PSModulePath points at PowerShell 7's modules)
  $got = CpuPower-Sha256 $SetupExe
  if ($got -ne $want) {
    CpuPower-Warn "The PawnIO installer is not the one this release pins (SHA-256 $got, expected $want); it was not run."
    return 'unavailable'
  }
  $sig = Get-AuthenticodeSignature -LiteralPath $SetupExe
  if ($sig.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    CpuPower-Warn "The PawnIO installer's signature is not valid ($($sig.Status)); it was not run."
    return 'unavailable'
  }
  # (signed by its author: Azure Trusted Signing, CN=namazso exactly)
  $signer = ''
  if ($null -ne $sig.SignerCertificate) { $signer = [string]$sig.SignerCertificate.Subject }
  if ($signer -notmatch '(^|, )CN=namazso(,|$)') {
    CpuPower-Warn "The PawnIO installer is not signed by its author ($signer); it was not run."
    return 'unavailable'
  }
  CpuPower-Say 'Installing PawnIO, the signed driver that reads the CPU energy counters (https://pawnio.eu)...'
  $code = CpuPower-Run $SetupExe '-install -silent'
  if ($code -ne 0 -and $code -ne 3010) {
    CpuPower-Warn "PawnIO's installer failed (exit code $code), so CPU power cannot be measured. Sentry Six Manager works without it."
    return 'unavailable'
  }
  $now = Get-PawnIO
  $ver = ''
  if ($null -ne $now) { $ver = [string]$now.Version }
  if (-not (Test-Path -LiteralPath $PawnMarkerKey)) { $null = New-Item -Path $PawnMarkerKey -Force }
  Set-ItemProperty -LiteralPath $PawnMarkerKey -Name 'InstalledBySentry' -Value 1 -Type DWord
  Set-ItemProperty -LiteralPath $PawnMarkerKey -Name 'Version' -Value $ver -Type String
  Set-ItemProperty -LiteralPath $PawnMarkerKey -Name 'Fingerprint' -Value (Get-PawnFingerprint) -Type String
  if ($code -eq 3010) {
    CpuPower-Say "PawnIO $ver is installed. Restart Windows to finish: CPU power shows after the restart."
    return 'restart'
  }
  CpuPower-Say "PawnIO $ver is installed."
  return 'ready'
}

<#
  Every place a program that uses PawnIO can show up, also one with no
  Uninstall entry (portable FanControl or LibreHardwareMonitor started from
  a folder of their own): installed programs, running processes, scheduled
  tasks (both register one to start at sign-in), the Run keys of the PC and
  of every loaded user, the Startup folders and the services.
  @{ Candidates = @(@{ Where; Text }); Unsure = @(what could not be read) }
#>
function Get-PawnUserCandidates {
  $c = New-Object System.Collections.ArrayList
  $unsure = New-Object System.Collections.ArrayList
  foreach ($e in @(Get-UninstallEntries)) {
    if ($e.Key -ne 'PawnIO') { $null = $c.Add([pscustomobject]@{ Where = 'installed program'; Text = "$($e.Name) $($e.Location)" }) }
  }
  try {
    foreach ($p in @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)) {
      $null = $c.Add([pscustomobject]@{ Where = 'running'; Text = "$($p.Name) $($p.ExecutablePath)" })
    }
  } catch { $null = $unsure.Add('the running programs') }
  try {
    foreach ($t in @(Get-ScheduledTask -ErrorAction Stop)) {
      $text = "$($t.TaskPath)$($t.TaskName)"
      foreach ($a in @($t.Actions)) {
        if ($null -eq $a) { continue }
        foreach ($n in 'Execute', 'Arguments') {
          if ($null -ne $a.PSObject.Properties[$n]) { $text += " $($a.$n)" }
        }
      }
      $null = $c.Add([pscustomobject]@{ Where = 'scheduled task'; Text = $text })
    }
  } catch { $null = $unsure.Add('the scheduled tasks') }
  $runPaths = @('Software\Microsoft\Windows\CurrentVersion\Run', 'Software\Microsoft\Windows\CurrentVersion\RunOnce',
    'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run', 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce')
  try {
    $hives = @(@{ Name = 'HKLM'; Key = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64) })
    $users = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::Users, [Microsoft.Win32.RegistryView]::Registry64)
    foreach ($sid in $users.GetSubKeyNames()) {
      if ($sid -match '_Classes$') { continue }
      $k = $users.OpenSubKey($sid)
      if ($null -ne $k) { $hives += @{ Name = "HKU\$sid"; Key = $k } }
    }
    foreach ($h in $hives) {
      foreach ($rp in $runPaths) {
        $run = $h.Key.OpenSubKey($rp)
        if ($null -eq $run) { continue }
        try {
          foreach ($v in $run.GetValueNames()) {
            $null = $c.Add([pscustomobject]@{ Where = "$($h.Name)\$rp"; Text = "$v $($run.GetValue($v))" })
          }
        } finally { $run.Close() }
      }
      $h.Key.Close()
    }
    $users.Close()
  } catch { $null = $unsure.Add('the Run keys') }
  try {
    $folders = @([Environment]::GetFolderPath('CommonStartup'))
    $profiles = Split-Path -Parent ([Environment]::GetFolderPath('UserProfile'))
    if (-not [string]::IsNullOrEmpty($profiles) -and (Test-Path -LiteralPath $profiles)) {
      foreach ($d in @(Get-ChildItem -LiteralPath $profiles -Directory -Force -ErrorAction SilentlyContinue)) {
        $folders += Join-Path $d.FullName 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
      }
    }
    foreach ($f in $folders) {
      if ([string]::IsNullOrEmpty($f) -or -not (Test-Path -LiteralPath $f)) { continue }
      foreach ($i in @(Get-ChildItem -LiteralPath $f -File -Force -ErrorAction SilentlyContinue)) {
        $null = $c.Add([pscustomobject]@{ Where = 'Startup folder'; Text = $i.FullName })
      }
    }
  } catch { $null = $unsure.Add('the Startup folders') }
  try {
    foreach ($s in @(Get-CimInstance -ClassName Win32_Service -ErrorAction Stop)) {
      $null = $c.Add([pscustomobject]@{ Where = 'service'; Text = "$($s.Name) $($s.DisplayName) $($s.PathName)" })
    }
  } catch { $null = $unsure.Add('the services') }
  return [pscustomobject]@{ Candidates = @($c.ToArray()); Unsure = @($unsure.ToArray()) }
}

# The programs among the candidates that use PawnIO: "text (where)", sorted.
function Select-PawnUsers($Candidates) {
  $found = @()
  foreach ($x in @($Candidates)) {
    if ($null -eq $x) { continue }
    $text = "$($x.Text)".Trim()
    if ($text -match $PawnDependents) { $found += "$text ($($x.Where))" }
  }
  return @($found | Sort-Object -Unique)
}

# PawnIO's own uninstaller, only from its folder under Program Files, or $null.
function Get-PawnUninstaller($Have) {
  if ($null -eq $Have -or $null -eq $Have.Entry -or [string]::IsNullOrEmpty($Have.Entry.Location)) { return $null }
  $u = Join-Path $Have.Entry.Location 'uninstall.exe'
  $pf = [Environment]::GetFolderPath('ProgramFiles')
  if (-not $u.StartsWith("$pf\", [System.StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $u -PathType Leaf)) { return $null }
  return $u
}

<#
  Removes PawnIO if Sentry Six Manager installed it and nothing else has
  taken it over (see the rules at the top); always forgets our record of it.
  In any doubt PawnIO stays.
#>
function Remove-PawnIOIfOurs {
  $mark = Get-PawnMarker
  if ($null -eq $mark) {
    if (Test-Path -LiteralPath $PawnMarkerKey) { Remove-Item -LiteralPath $PawnMarkerKey -Recurse -Force }
    if ($null -ne (Find-PawnIO)) { CpuPower-Say 'PawnIO was not installed by Sentry Six Manager; it is left as it is.' }
    return
  }
  $have = Find-PawnIO
  $keep = $null
  if ($null -eq $have) {
    $keep = 'it is not installed any more'
  } elseif ($have.Version -ne $mark.Version) {
    $keep = "it was updated since (now $($have.Version)), so another program uses it"
  } elseif ([string]::IsNullOrEmpty($mark.Fingerprint) -or (Get-PawnFingerprint) -ne $mark.Fingerprint) {
    $keep = 'it is not the copy Sentry Six Manager installed (it was installed again since), so another program may use it'
  } else {
    $seen = Get-PawnUserCandidates
    $users = @(Select-PawnUsers $seen.Candidates)
    if ($users.Count -gt 0) {
      $keep = "other programs may use it: $($users -join '; ')"
    } elseif (@($seen.Unsure).Count -gt 0) {
      $keep = "$(@($seen.Unsure) -join ', ') could not be checked for programs that use it"
    }
  }
  if ($null -eq $keep) {
    $uninstaller = Get-PawnUninstaller $have
    if ($null -eq $uninstaller) {
      CpuPower-Warn "PawnIO's uninstaller was not found; PawnIO stays. Remove it in Settings > Apps (PawnIO) if you do not need it."
    } else {
      CpuPower-Say 'Removing PawnIO (Sentry Six Manager installed it, and no other program uses it)...'
      $code = CpuPower-Run $uninstaller '-uninstall -silent'
      if ($code -eq 0) { CpuPower-Say 'PawnIO removed.' }
      elseif ($code -eq 3010) { CpuPower-Say 'PawnIO removed; it is gone completely after Windows restarts.' }
      else { CpuPower-Warn "PawnIO's uninstaller failed (exit code $code); PawnIO stays. Remove it in Settings > Apps (PawnIO) if you do not need it." }
    }
  } elseif ($null -eq $have) {
    CpuPower-Say "Keeping PawnIO: $keep."
  } else {
    CpuPower-Say "Keeping PawnIO: $keep. If no program needs it, remove it in Settings > Apps (PawnIO)."
  }
  Remove-Item -LiteralPath $PawnMarkerKey -Recurse -Force -ErrorAction SilentlyContinue
}

function Stop-PowerService {
  $svc = Get-Service -Name $PowerServiceName -ErrorAction SilentlyContinue
  if ($null -eq $svc -or $svc.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) { return }
  try {
    $svc.Stop()
    $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
  } catch {
    CpuPower-Warn "The CPU power service did not stop ($($_.Exception.Message))."
  }
}

function Remove-PowerService {
  $svc = Get-Service -Name $PowerServiceName -ErrorAction SilentlyContinue
  if ($null -eq $svc) { return }
  Stop-PowerService
  CpuPower-Say "Removing the CPU power service ($PowerServiceName)..."
  & sc.exe delete $PowerServiceName | Out-Host
  # 1072: already marked for deletion (Services window open); it goes once that closes
  if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1072) { CpuPower-Warn "Removing the CPU power service failed (sc.exe exit code $LASTEXITCODE)." }
  for ($i = 0; $i -lt 10; $i++) {
    if ($null -eq (Get-Service -Name $PowerServiceName -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Seconds 1
  }
}

<#
  The CPU power service: LocalSystem (only SYSTEM and Administrators may
  open PawnIO's device), with every privilege taken away but the one any
  process needs (it opens a device and reads two MSRs through it), its own
  service SID, and restarted after a failure. It starts with Windows.
#>
function Install-PowerService([string]$Exe) {
  if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    CpuPower-Warn "$Exe is missing, so CPU power cannot be measured."
    return $false
  }
  $scArgs = 'binPath= "\"' + $Exe + '\"" obj= LocalSystem start= auto DisplayName= "Sentry Six Manager CPU power"'
  $svc = Get-Service -Name $PowerServiceName -ErrorAction SilentlyContinue
  if ($null -eq $svc) {
    CpuPower-Say "Creating the CPU power service ($PowerServiceName)..."
    $code = CpuPower-Run 'sc.exe' "create $PowerServiceName $scArgs"
  } else {
    $code = CpuPower-Run 'sc.exe' "config $PowerServiceName $scArgs"
  }
  if ($code -ne 0) { CpuPower-Warn "Setting up the CPU power service failed (sc.exe exit code $code)."; return $false }
  $regKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$PowerServiceName"
  $wantImage = '"' + $Exe + '"'
  if ((Get-ItemProperty -LiteralPath $regKey -Name 'ImagePath').ImagePath -ne $wantImage) {
    Set-ItemProperty -LiteralPath $regKey -Name 'ImagePath' -Value $wantImage
  }
  foreach ($line in @(
      "privs $PowerServiceName SeChangeNotifyPrivilege",
      "sidtype $PowerServiceName unrestricted",
      "failure $PowerServiceName reset= 60 actions= restart/5000",
      "description $PowerServiceName `"Reads the CPU package energy counters through the PawnIO driver for Sentry Six Manager's server stats. Local only: no network.`"")) {
    $code = CpuPower-Run 'sc.exe' $line
    if ($code -ne 0) { CpuPower-Warn "sc.exe $($line.Split(' ')[0]) failed for the CPU power service (exit code $code)." }
  }
  try {
    Start-Service -Name $PowerServiceName
  } catch {
    CpuPower-Warn "The CPU power service did not start ($($_.Exception.Message))."
    return $false
  }
  return $true
}
