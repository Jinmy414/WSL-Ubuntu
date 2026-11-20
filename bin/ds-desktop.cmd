@echo off
rem Prepare the same GNOME session as the console, then use standalone mstsc.
echo Waiting for the Ubuntu-DS desktop ...
start "" /wait "%~dp0UbuntuDS-Console.exe" --prepare-desktop
if errorlevel 1 (
    echo Ubuntu-DS desktop did not become ready. Run ds-doctor.cmd for diagnostics.
    echo Startup log: "%LOCALAPPDATA%\UbuntuDS\connection.log"
    exit /b 1
)
start "" mstsc.exe "%~dp0ds-desktop.rdp"
