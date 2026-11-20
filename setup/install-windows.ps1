[CmdletBinding()]
param([switch]$Check, [switch]$UsePackagedConsole)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $projectRoot 'bin'
$console = Join-Path $bin 'UbuntuDS-Console.exe'
$taskName = 'UbuntuDS-KeepAlive'

Write-Output '[1/3] Check Windows, WSL and the Remote Desktop port'
$windows = Get-CimInstance -ClassName Win32_OperatingSystem
if ([int]$windows.BuildNumber -lt 22000 -or $windows.ProductType -ne 1) {
    throw 'WSL-Ubuntu requires Windows 11.'
}
$wsl = Get-Command -Name wsl.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $wsl) { throw 'Install WSL and register Ubuntu-DS before running Windows setup.' }
if (-not (Get-Command -Name mstsc.exe -CommandType Application -ErrorAction SilentlyContinue)) {
    throw 'Windows Remote Desktop Connection (mstsc.exe) is required.'
}
if ($UsePackagedConsole) {
    if (-not (Test-Path -LiteralPath $console -PathType Leaf)) {
        throw 'The prebuilt console was not found. Extract the Windows release ZIP or build from source.'
    }
} else {
    $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        throw 'The .NET Framework C# compiler required for the WPF console was not found.'
    }
}

$distroList = @(& $wsl.Source --list --verbose)
if ($LASTEXITCODE -ne 0) { throw 'Could not list WSL distributions. Run wsl.exe --status for details.' }
$wslVersion = foreach ($entry in $distroList) {
    $line = ([string]$entry).Replace([string][char]0, '').TrimStart([char]0xFEFF)
    if ($line -match '^\s*(?:\*\s*)?Ubuntu-DS\s{2,}.+?\s+([12])\s*$') { $Matches[1] }
}
if (-not $wslVersion) { throw 'Ubuntu-DS is not registered. Follow the Ubuntu installation steps in the README.' }
if ($wslVersion -ne '2') { throw 'Ubuntu-DS must use WSL 2. Run wsl.exe --list --verbose to check its version.' }

$listeners = Get-NetTCPConnection -LocalPort 3393 -State Listen -ErrorAction SilentlyContinue
if ($listeners) {
    Write-Warning 'Windows TCP 3393 already has a listener. This is expected if the Ubuntu-DS relay is running. If setup cannot connect, check the owner with Get-NetTCPConnection -LocalPort 3393.'
} else {
    Write-Output 'No active Windows listener was found on TCP 3393.'
}
if ($Check) {
    Write-Output 'Windows preflight passed. Run setup without -Check to install the console shortcut.'
    return
}

if ($UsePackagedConsole) {
    Write-Output '[2/3] Use the prebuilt Windows console'
} else {
    Write-Output '[2/3] Build the Windows console'
    & (Join-Path $PSScriptRoot 'build-console.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'Ubuntu-DS console build failed.' }
}

Write-Output '[3/3] Set up the desktop shortcut and keepalive task'
if (-not (Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue)) {
    $action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\wsl.exe') `
        -Argument '-d Ubuntu-DS --exec /usr/bin/sleep infinity'
    $account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal `
        -Settings $settings -Description 'Keep Ubuntu-DS active after a manual launch.' | Out-Null
}

$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $desktop 'Ubuntu-DS Console.lnk'))
$shortcut.TargetPath = $console
$shortcut.WorkingDirectory = $bin
$shortcut.IconLocation = "$console,0"
$shortcut.Description = 'Start and connect to the WSL-Ubuntu desktop.'
$shortcut.Save()
Write-Output 'Open Ubuntu-DS Console from the Windows desktop and enter the RDP credentials set in Ubuntu.'
Write-Output 'If the connection fails, run bin\ds-doctor.cmd to check WSL, GNOME and RDP.'
