$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $projectRoot 'bin'
$console = Join-Path $bin 'UbuntuDS-Console.exe'
$taskName = 'UbuntuDS-KeepAlive'

& (Join-Path $PSScriptRoot 'build-console.cmd')
if ($LASTEXITCODE -ne 0) { throw 'Ubuntu-DS console build failed.' }

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
$shortcut.Description = 'Start and connect to Ubuntu-DS.'
$shortcut.Save()
Write-Output 'Ubuntu-DS Console is ready on the Windows desktop.'
