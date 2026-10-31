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
    - ours (HKLM\SOFTWARE\SentrySixManager\PawnIO InstalledBySentry = 1) is
      removed with Sentry Six Manager only while its version is still the one
      we installed (someone else updating it has made it theirs) and no
      program known to use PawnIO is installed; otherwise it stays and only
      our record of it goes
  Nothing here downloads anything: the installer is pawnio\PawnIO_setup.exe
  of the release (pinned in windows\versions.json, SHA-256 checked when the
  package was built).
#>

$PowerServiceName = 'SentrySixPower'
$PawnMarkerKey = 'HKLM:\SOFTWARE\SentrySixManager\PawnIO'
$PawnMinVersion = [version]'2.0.0'
# programs that install with PawnIO or need it (their Uninstall entries' DisplayName)
$PawnDependents = 'FanControl|Fan Control|Libre ?Hardware ?Monitor|Open ?Hardware ?Monitor|OpenRGB|SignalRGB|HWiNFO|OmenMon|G-Helper|ZenTimings|ThrottleStop|Armoury Crate|AIDA64|Core Temp|HWMonitor'

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

# PawnIO as its setup records it (Uninstall\PawnIO), or $null when it is not installed.
function Get-PawnIO {
  foreach ($e in @(Get-UninstallEntries)) {
    if ($e.Key -eq 'PawnIO') { return $e }
  }
  return $null
}

function Get-PawnMarker {
  if (-not (Test-Path -LiteralPath $PawnMarkerKey)) { return $null }
  $p = Get-ItemProperty -LiteralPath $PawnMarkerKey
  if ($null -eq $p.PSObject.Properties['InstalledBySentry'] -or [int]$p.InstalledBySentry -ne 1) { return $null }
  $ver = ''
  if ($null -ne $p.PSObject.Properties['Version']) { $ver = [string]$p.Version }
  return [pscustomobject]@{ Version = $ver }
}

<#
  PawnIO for the CPU power service. Returns 'ready', 'restart' (installed,
  Windows must restart before it loads) or 'unavailable' (it says why).
#>
function Install-PawnIO([string]$SetupExe) {
  $have = Get-PawnIO
  if ($null -ne $have) {
    $v = CpuPower-Version $have.Version
    if ($null -ne $v -and $v -ge $PawnMinVersion) {
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
  $sig = Get-AuthenticodeSignature -LiteralPath $SetupExe
  if ($sig.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    CpuPower-Warn "The PawnIO installer's signature is not valid ($($sig.Status)); it was not run."
    return 'unavailable'
  }
  # (signed by its author: Azure Trusted Signing, subject namazso)
  $signer = ''
  if ($null -ne $sig.SignerCertificate) { $signer = [string]$sig.SignerCertificate.Subject }
  if ($signer -notmatch 'namazso') {
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
  if (-not (Test-Path -LiteralPath $PawnMarkerKey)) { $null = New-Item -Path $PawnMarkerKey -Force }
  Set-ItemProperty -LiteralPath $PawnMarkerKey -Name 'InstalledBySentry' -Value 1 -Type DWord
  $ver = ''
  if ($null -ne $now) { $ver = $now.Version }
  Set-ItemProperty -LiteralPath $PawnMarkerKey -Name 'Version' -Value $ver -Type String
  if ($code -eq 3010) {
    CpuPower-Say "PawnIO $ver is installed. Restart Windows to finish: CPU power shows after the restart."
    return 'restart'
  }
  CpuPower-Say "PawnIO $ver is installed."
  return 'ready'
}

<#
  Removes PawnIO if Sentry Six Manager installed it and nothing else has
  taken it over (see the rules at the top); always forgets our record of it.
#>
function Remove-PawnIOIfOurs {
  $mark = Get-PawnMarker
  if ($null -eq $mark) {
    if (Test-Path -LiteralPath $PawnMarkerKey) { Remove-Item -LiteralPath $PawnMarkerKey -Recurse -Force }
    if ($null -ne (Get-PawnIO)) { CpuPower-Say 'PawnIO was not installed by Sentry Six Manager; it is left as it is.' }
    return
  }
  $have = Get-PawnIO
  $keep = $null
  if ($null -eq $have) {
    $keep = 'it is not installed any more'
  } elseif ($have.Version -ne $mark.Version) {
    $keep = "it was updated since (now $($have.Version)), so another program uses it"
  } else {
    $users = @(Get-UninstallEntries | Where-Object { $_.Key -ne 'PawnIO' -and $_.Name -match $PawnDependents } | ForEach-Object { $_.Name } | Sort-Object -Unique)
    if ($users.Count -gt 0) { $keep = "$($users -join ', ') may use it" }
  }
  if ($null -eq $keep) {
    $uninstaller = $null
    if (-not [string]::IsNullOrEmpty($have.Location)) { $uninstaller = Join-Path $have.Location 'uninstall.exe' }
    $pf = [Environment]::GetFolderPath('ProgramFiles')
    if ($null -eq $uninstaller -or -not (Test-Path -LiteralPath $uninstaller -PathType Leaf) -or -not $uninstaller.StartsWith("$pf\", [System.StringComparison]::OrdinalIgnoreCase)) {
      CpuPower-Warn "PawnIO's uninstaller was not found; PawnIO stays. Remove it in Settings > Apps if you do not need it."
    } else {
      CpuPower-Say 'Removing PawnIO (Sentry Six Manager installed it, and no other program uses it)...'
      $code = CpuPower-Run $uninstaller '-uninstall -silent'
      if ($code -eq 0) { CpuPower-Say 'PawnIO removed.' }
      elseif ($code -eq 3010) { CpuPower-Say 'PawnIO removed; it is gone completely after Windows restarts.' }
      else { CpuPower-Warn "PawnIO's uninstaller failed (exit code $code); PawnIO stays. Remove it in Settings > Apps if you do not need it." }
    }
  } else {
    CpuPower-Say "Keeping PawnIO: $keep."
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
