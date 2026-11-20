# Ubuntu-DS connection diagnostics. Run without changing the distro or system services.
$ErrorActionPreference = 'Stop'
$distro = 'Ubuntu-DS'

function Invoke-WslCheck([string]$arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new('wsl.exe', $arguments)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(8000)) {
            try { $process.Kill() } catch {}
            throw 'wsl.exe 未在 8 秒内响应。'
        }
        return [pscustomobject]@{
            Code = $process.ExitCode
            Text = $output.GetAwaiter().GetResult().Replace([string][char]0, '').Trim()
            ErrorText = $errorOutput.GetAwaiter().GetResult().Replace([string][char]0, '').Trim()
        }
    } finally {
        $process.Dispose()
    }
}

try {
    Write-Host 'Ubuntu-DS 连接诊断' -ForegroundColor Cyan
    $registered = Invoke-WslCheck '--list --quiet'
    if ($registered.Code -ne 0) { throw "无法读取 WSL 发行版列表。$($registered.ErrorText)" }
    if (($registered.Text -split '\r?\n').Trim() -notcontains $distro) {
        throw 'Windows 未注册 Ubuntu-DS。'
    }

    $running = Invoke-WslCheck '--list --quiet --running'
    if ($running.Code -ne 0) { throw "无法读取 WSL 运行状态。$($running.ErrorText)" }
    if (($running.Text -split '\r?\n').Trim() -notcontains $distro) {
        Write-Host '[WSL] Ubuntu-DS 已关机。打开控制台后可再次检查。'
        exit 0
    }
    Write-Host '[WSL] Ubuntu-DS 正在运行。' -ForegroundColor Green

    $mode = Invoke-WslCheck "-d $distro --exec wslinfo --networking-mode"
    if ($mode.Code -eq 0) { Write-Host "[网络] WSL 网络模式 $($mode.Text)。" }
    $route = Invoke-WslCheck "-d $distro --exec ip -4 route show default"
    $hasRoute = $route.Code -eq 0 -and $route.Text -match '(?m)^default\b'
    if ($hasRoute) {
        Write-Host '[网络] Ubuntu 已取得 IPv4 默认路由。' -ForegroundColor Green
    } else {
        Write-Host '[网络] Ubuntu 暂无 IPv4 默认路由。' -ForegroundColor Yellow
    }

    $relay = Invoke-WslCheck "-d $distro --exec systemctl is-active ds-rdp-v4.service"
    $desktop = Invoke-WslCheck "-d $distro --exec systemctl --user is-active ds-shell-headless.service gnome-remote-desktop-headless.service"
    $desktopServices = @($desktop.Text -split '\r?\n')
    $desktopReady = $desktop.Code -eq 0 -and $desktopServices.Count -eq 2 -and ($desktopServices -notmatch '^active$').Count -eq 0
    if ($relay.Code -eq 0 -and $desktopReady) {
        Write-Host '[桌面] GNOME、远程桌面服务和 IPv4 中继已启动。' -ForegroundColor Green
    } else {
        Write-Host "[桌面] 中继状态 $($relay.Text)，GNOME 服务状态 $($desktop.Text -replace '\s+', '/')。" -ForegroundColor Yellow
    }

    $probePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'setup\rdp-probe.ps1'
    $probe = & $probePath '127.0.0.1' 3393 v4
    if ($probe -match '^RDP_OK\b') {
        Write-Host '[RDP] Windows 本地端口 3393 握手成功。' -ForegroundColor Green
    } else {
        Write-Host "[RDP] Windows 本地端口 3393 握手失败。$probe" -ForegroundColor Yellow
    }
    if (-not $hasRoute -or $relay.Code -ne 0 -or -not $desktopReady -or $probe -notmatch '^RDP_OK\b') { exit 1 }
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
