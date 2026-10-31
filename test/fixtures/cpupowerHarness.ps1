# Runs windows\cpupower.ps1's PawnIO decisions against made-up PCs, for
# test/cpuPowerScript.test.js. Nothing is installed or removed: the parts
# that look at the PC or run a program are replaced after cpupower.ps1 is
# dot-sourced, and the marker lives under HKCU in a key of the test's own.
param([string]$Script, [string]$MarkerKey, [string]$Work)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. $Script
$PawnMarkerKey = $MarkerKey

$global:Ran = @()
function CpuPower-Run([string]$File, [string]$Line) { $global:Ran += "$File $Line"; return 0 }
function CpuPower-Say([string]$Text) { }
function CpuPower-Warn([string]$Text) { }

function Say-Result([string]$Name, [string]$Value) { [Console]::Out.WriteLine("$Name $Value") }

# --- Remove-PawnIOIfOurs: ours, the same copy, PawnIO's uninstaller there ---
$global:Fp = 'version=2.2.0;driver=c:\program files\pawnio\pawnio.sys@1'
$global:Seen = [pscustomobject]@{ Candidates = @(); Unsure = @() }
function Get-PawnMarker { [pscustomobject]@{ Version = '2.2.0'; Fingerprint = 'version=2.2.0;driver=c:\program files\pawnio\pawnio.sys@1' } }
function Find-PawnIO { [pscustomobject]@{ Version = '2.2.0'; Entry = [pscustomobject]@{ Location = 'C:\Program Files\PawnIO' } } }
function Get-PawnFingerprint { $global:Fp }
function Get-PawnUninstaller($Have) { 'C:\Program Files\PawnIO\uninstall.exe' }
function Get-PawnUserCandidates { $global:Seen }

function Try-Remove([string]$Name) {
  $global:Ran = @()
  Remove-PawnIOIfOurs
  if ($global:Ran.Count -gt 0) { Say-Result $Name 'removed' } else { Say-Result $Name 'kept' }
}

Try-Remove 'nothingElse'
$global:Seen = [pscustomobject]@{ Candidates = @([pscustomobject]@{ Where = 'HKU\S-1-5-21-1\Software\Microsoft\Windows\CurrentVersion\Run'; Text = 'FanControl "C:\Users\me\Tools\FanControl\FanControl.exe" -m' }); Unsure = @() }
Try-Remove 'runValue'
$global:Seen = [pscustomobject]@{ Candidates = @([pscustomobject]@{ Where = 'scheduled task'; Text = '\FanControl C:\Portable\FanControl.exe' }); Unsure = @() }
Try-Remove 'scheduledTask'
$global:Seen = [pscustomobject]@{ Candidates = @([pscustomobject]@{ Where = 'running'; Text = 'LibreHardwareMonitor.exe D:\lhm\LibreHardwareMonitor.exe' }); Unsure = @() }
Try-Remove 'running'
$global:Seen = [pscustomobject]@{ Candidates = @([pscustomobject]@{ Where = 'running'; Text = 'explorer.exe C:\Windows\explorer.exe' }, [pscustomobject]@{ Where = 'service'; Text = 'PawnIO PawnIO \SystemRoot\System32\drivers\PawnIO.sys' }); Unsure = @() }
Try-Remove 'unrelated'
$global:Seen = [pscustomobject]@{ Candidates = @(); Unsure = @('the scheduled tasks') }
Try-Remove 'unsure'
$global:Seen = [pscustomobject]@{ Candidates = @(); Unsure = @() }
$global:Fp = 'version=2.2.0;driver=c:\program files\pawnio\pawnio.sys@2'
Try-Remove 'reinstalled'
$global:Fp = 'version=2.2.0;driver=c:\program files\pawnio\pawnio.sys@1'
function Find-PawnIO { [pscustomobject]@{ Version = '2.3.0'; Entry = [pscustomobject]@{ Location = 'C:\Program Files\PawnIO' } } }
Try-Remove 'updated'

# --- Install-PawnIO ---
$exe = Join-Path $Work 'PawnIO_setup.exe'
[IO.File]::WriteAllBytes($exe, [byte[]](1, 2, 3))
function Install-Result([string]$Name) {
  $global:Ran = @()
  $r = Install-PawnIO $exe
  $marked = Test-Path -LiteralPath $PawnMarkerKey
  Say-Result $Name "$r ran=$($global:Ran.Count) marked=$marked"
}
# there without an Uninstall entry (its driver service only): used, not installed over, not ours
function Find-PawnIO { [pscustomobject]@{ Version = '2.3.1'; Entry = $null } }
Install-Result 'serviceOnly'
# not there: the installer must match the pinned SHA-256
function Find-PawnIO { $null }
$PawnSetupSha256 = '@PAWNIO_SETUP_SHA256@'
Install-Result 'noPin'
$PawnSetupSha256 = '0000000000000000000000000000000000000000000000000000000000000000'
Install-Result 'wrongHash'
