@echo off
rem Sentry Six Manager: double-click to install or upgrade (keeps your data).
rem Asks Windows for administrator rights, then runs install.ps1 from this
rem folder. Windows blocks scripts by default, so the policy is relaxed for
rem this one PowerShell process only (nothing is changed for the system).
rem Options such as -Port, -Service or -NoService: run install.ps1 directly
rem from an administrator PowerShell. With none, it asks whether to install
rem the Windows service (a first install) or keeps the installed mode.
rem Without the service the server runs as whoever double-clicked this, so
rem that account (captured before Windows asks for administrator rights,
rem which may sign in as another one) is passed on as -ForUser.
setlocal

rem fltmc only works for administrators
fltmc >nul 2>&1
if errorlevel 1 goto elevate

rem 64-bit PowerShell even when started from a 32-bit program
set "SSM_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "SSM_PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"

rem the person who double-clicked it: passed in by the elevation below, or
rem (started as administrator already) this account
set "SSM_FORUSER=%~1"
if not defined SSM_FORUSER set "SSM_FORUSER=%USERDOMAIN%\%USERNAME%"

"%SSM_PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -ForUser "%SSM_FORUSER%"
set "SSM_RC=%ERRORLEVEL%"
echo.
if not "%SSM_RC%"=="0" echo The install did not finish. Read the messages above.
pause
exit /b %SSM_RC%

:elevate
rem The path and the account go through environment variables, so quotes or
rem apostrophes in them cannot break the command.
set "SSM_SELF=%~f0"
set "SSM_FORUSER=%USERDOMAIN%\%USERNAME%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath $env:SSM_SELF -ArgumentList ('\"' + $env:SSM_FORUSER + '\"') -Verb RunAs } catch { Write-Host 'Windows did not give administrator rights, so nothing was installed.'; exit 1 }"
if errorlevel 1 (
  pause
  exit /b 1
)
exit /b 0
