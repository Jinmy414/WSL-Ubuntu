@echo off
rem Ubuntu-DS connection diagnostics
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ds-doctor.ps1"
pause
