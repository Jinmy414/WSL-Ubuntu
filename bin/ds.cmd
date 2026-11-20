@echo off
rem Run a command in Ubuntu-DS with the current Windows proxy, or open a shell.
if "%~1"=="" goto :shell
wsl.exe -d Ubuntu-DS -- /usr/local/bin/ds-proxy-watch --exec %*
exit /b %ERRORLEVEL%
:shell
wsl.exe -d Ubuntu-DS
