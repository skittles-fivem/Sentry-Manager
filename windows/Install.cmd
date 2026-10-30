@echo off
rem Sentry Six Manager: double-click to install or upgrade (keeps your data).
rem Asks Windows for administrator rights, then runs install.ps1 from this
rem folder. Windows blocks scripts by default, so the policy is relaxed for
rem this one PowerShell process only (nothing is changed for the system).
rem Options such as -Port or -DefenderExclusion: run install.ps1 directly
rem from an administrator PowerShell.
setlocal

rem fltmc only works for administrators
fltmc >nul 2>&1
if errorlevel 1 goto elevate

rem 64-bit PowerShell even when started from a 32-bit program
set "SSM_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "SSM_PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"

"%SSM_PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "SSM_RC=%ERRORLEVEL%"
echo.
if not "%SSM_RC%"=="0" echo The install did not finish. Read the messages above.
pause
exit /b %SSM_RC%

:elevate
rem The path goes through an environment variable, so quotes or apostrophes
rem in the folder name cannot break the command.
set "SSM_SELF=%~f0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath $env:SSM_SELF -Verb RunAs } catch { Write-Host 'Windows did not give administrator rights, so nothing was installed.'; exit 1 }"
if errorlevel 1 (
  pause
  exit /b 1
)
exit /b 0
