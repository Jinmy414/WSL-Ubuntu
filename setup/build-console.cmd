@echo off
rem Build WPF console with in-box .NET Framework compiler (no SDK needed)
setlocal
set "W=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319"
set "OUT=%~dp0..\bin\UbuntuDS-Console.exe"
if not "%~1"=="" set "OUT=%~f1"
"%W%\csc.exe" -nologo -codepage:65001 -target:winexe -optimize+ -win32manifest:"%~dp0app.manifest" -out:"%OUT%" -r:"%W%\WPF\PresentationFramework.dll" -r:"%W%\WPF\PresentationCore.dll" -r:"%W%\WPF\WindowsBase.dll" -r:"%W%\System.Xaml.dll" -r:System.dll "%~dp0UbuntuDSConsole.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK
