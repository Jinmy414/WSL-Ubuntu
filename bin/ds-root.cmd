@echo off
rem Run a command or open a shell as root with the current Windows proxy.
if "%~1"=="" goto :shell
wsl.exe -d Ubuntu-DS -u root -- /usr/local/bin/ds-proxy-watch --exec %*
exit /b %ERRORLEVEL%
:shell
wsl.exe -d Ubuntu-DS -u root --exec /usr/local/bin/ds-proxy-watch --exec bash -il
