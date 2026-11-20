@echo off
rem Disable the keepalive task and stop the local Ubuntu-DS distribution.
schtasks.exe /Change /TN "UbuntuDS-KeepAlive" /DISABLE >nul 2>&1
wsl.exe --terminate Ubuntu-DS
exit /b %ERRORLEVEL%
