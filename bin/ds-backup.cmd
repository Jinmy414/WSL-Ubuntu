@echo off
rem Stop Ubuntu-DS and export a local VHDX backup.
setlocal
set "BACKUPS=%~dp0..\backups"
if not exist "%BACKUPS%" mkdir "%BACKUPS%" || exit /b 1
for /f %%I in ('powershell.exe -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss-fff"') do set "STAMP=%%I"
if not defined STAMP exit /b 1
set "OUT=%BACKUPS%\Ubuntu-DS-%STAMP%.vhdx"
schtasks.exe /Change /TN "UbuntuDS-KeepAlive" /DISABLE >nul 2>&1
wsl.exe --terminate Ubuntu-DS >nul 2>&1
wsl.exe --export Ubuntu-DS "%OUT%" --vhd
if errorlevel 1 exit /b 1
echo Export complete: "%OUT%"
