#Requires -Version 5.1
<#
  Prints DOMAIN\name of the person who started SentrySixManager-Setup, for
  install.ps1 -ForUser (in app mode the server runs as that person and gets
  the data folder). Only the Setup runs this (SentrySixManager.iss, app mode).

  Setup runs as administrator. When the person who started it is not one,
  Windows asked for an administrator's password, and Setup (with this
  script) runs as that administrator. The Setup that asked is this
  process's ancestor, still running as the person: its owner is the answer.
  Only ancestors that are this Setup (<SetupName>.exe, or its .tmp) are
  looked at, and a "parent" started after its child is a reused process id,
  so it is not one. Prints the account running this when it finds nothing
  else (the usual case: the person approved the prompt themselves).

  -SetupName   the Setup's file name without .exe, as Setup passes it
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$SetupName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# a person's account (local, domain or Microsoft Entra), as install.ps1 has it
function Is-PersonSid([string]$Sid) {
  return ($Sid -match '^S-1-5-21-\d+-\d+-\d+-\d+$' -or $Sid -match '^S-1-12-1-\d+-\d+-\d+-\d+$')
}

$who = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$names = @("$SetupName.exe", "$SetupName.tmp")
$byId = @{}
foreach ($p in @(Get-CimInstance Win32_Process)) { $byId[[int]$p.ProcessId] = $p }

$child = $byId[[int]$PID]
for ($i = 0; $i -lt 8 -and $null -ne $child; $i++) {
  $parent = $byId[[int]$child.ParentProcessId]
  if ($null -eq $parent) { break }
  if ($names -notcontains [string]$parent.Name) { break }
  if ($null -ne $parent.CreationDate -and $null -ne $child.CreationDate -and $parent.CreationDate -gt $child.CreationDate) { break }
  try {
    $owner = Invoke-CimMethod -InputObject $parent -MethodName GetOwnerSid
    if ($owner.ReturnValue -eq 0 -and $owner.Sid -and (Is-PersonSid ([string]$owner.Sid))) { $who = [string]$owner.Sid }
  } catch { }
  $child = $parent
}

(New-Object System.Security.Principal.SecurityIdentifier($who)).Translate([System.Security.Principal.NTAccount]).Value
