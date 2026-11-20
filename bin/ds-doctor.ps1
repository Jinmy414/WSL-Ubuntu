# Ubuntu-DS connection diagnostics. Run without changing the distro or system services.
[CmdletBinding()]
param([switch]$TestHttps)

$ErrorActionPreference = 'Stop'
$distro = 'Ubuntu-DS'

function Invoke-WslCheck([string]$arguments, [int]$timeoutSeconds = 8) {
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
        if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
            try { $process.Kill() } catch {}
            throw [TimeoutException]::new("wsl.exe 未在 $timeoutSeconds 秒内响应。")
        }
        $null = $errorOutput.GetAwaiter().GetResult()
        return [pscustomobject]@{
            Code = $process.ExitCode
            Text = $output.GetAwaiter().GetResult().Replace([string][char]0, '').Trim()
        }
    } finally {
        $process.Dispose()
    }
}

function Write-ProxyStatus {
    $settingsPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    $settings = Get-ItemProperty -LiteralPath $settingsPath -ErrorAction SilentlyContinue
    $manualProxy = $settings.ProxyEnable -eq 1
    if ($null -eq $settings) {
        Write-Host '[代理] 无法读取当前 Windows 用户的代理设置。' -ForegroundColor Yellow
    } elseif ($manualProxy -and [string]::IsNullOrWhiteSpace($settings.ProxyServer)) {
        Write-Host '[代理] Windows 手动代理开关已打开，代理地址未设置。' -ForegroundColor Yellow
    } elseif ($manualProxy) {
        Write-Host '[代理] Windows 手动代理已开启。'
    } else {
        Write-Host '[代理] Windows 手动代理未开启。'
    }
    if ($settings.AutoConfigURL) {
        Write-Host '[代理] Windows 还配置了自动代理脚本（PAC）。Windows 同步模式读取手动代理设置。'
    }

    try {
        $sync = Invoke-WslCheck "-d $distro --exec systemctl --user is-active ds-proxy-watch.service"
        if ($sync.Code -eq 0 -and $sync.Text -eq 'active') {
            Write-Host '[代理] Ubuntu 代理同步服务正在运行。' -ForegroundColor Green
        } else {
            Write-Host '[代理] Ubuntu 代理同步服务未运行。检查用户会话和安装步骤。' -ForegroundColor Yellow
        }

        # Earlier releases start the daemon on unknown arguments. Check support before invoking --status.
        $statusCommand = "-d $distro --exec /bin/sh -c " +
            '"grep -Fq -- --status /usr/local/bin/ds-proxy-watch && exec /usr/local/bin/ds-proxy-watch --status"'
        $status = Invoke-WslCheck $statusCommand
        $source = $null
        $effective = $null
        if ($status.Code -eq 0 -and $status.Text -cmatch '^mode=(windows|clash)\r?\neffective=(windows|clash|direct)$') {
            $source = $Matches[1]
            $effective = $Matches[2]
            if ($source -eq 'clash') {
                Write-Host '[代理] Ubuntu 已选择本机 Clash。'
            } else {
                Write-Host '[代理] Ubuntu 已选择跟随 Windows。'
            }
            switch ($effective) {
                'windows' { Write-Host '[代理] Windows 手动代理可达。' -ForegroundColor Green }
                'clash' { Write-Host '[代理] Ubuntu 本机 Clash 代理可达。' -ForegroundColor Green }
                'direct' {
                    if ($source -eq 'clash') {
                        Write-Host '[代理] Ubuntu 本机 Clash 不可达，当前尝试直连。检查 Clash 是否启动及混合端口是否一致。' -ForegroundColor Yellow
                    } elseif ($manualProxy) {
                        Write-Host '[代理] Windows 手动代理不可用，当前尝试直连。' -ForegroundColor Yellow
                    } else {
                        Write-Host '[代理] 当前尝试直连。'
                    }
                }
            }
        } else {
            Write-Host '[代理] 未能读取代理来源。若同步程序较旧，请重新运行网络同步安装。' -ForegroundColor Yellow
        }

        $setting = Invoke-WslCheck "-d $distro --exec gsettings get org.gnome.system.proxy mode"
        if ($setting.Code -eq 0 -and $setting.Text -match "^'(none|manual|auto)'$") {
            $mode = $Matches[1]
            Write-Host "[代理] GNOME 代理模式 $mode。"
            if ($effective) {
                $expected = if ($effective -eq 'direct') { 'none' } else { 'manual' }
                if ($mode -ne $expected) {
                    Write-Host '[代理] GNOME 与当前连通性探测结果不同。稍候再次检查同步状态。' -ForegroundColor Yellow
                }
            } elseif ($manualProxy -and $mode -eq 'none') {
                Write-Host '[代理] GNOME 当前使用直连。检查同步服务和代理是否可达。' -ForegroundColor Yellow
            }
        } else {
            Write-Host '[代理] 无法读取 GNOME 代理模式。' -ForegroundColor Yellow
        }
    } catch [TimeoutException] {
        Write-Host '[代理] Ubuntu 代理状态查询超时。' -ForegroundColor Yellow
    }
}

function Test-GuestHttps {
    $command = "-d $distro --exec /usr/local/bin/ds-proxy-watch --exec curl --disable --silent --head " +
        '--output /dev/null --connect-timeout 5 --max-time 10 --write-out "%{http_connect} %{http_code}" https://example.com/'
    try {
        $result = Invoke-WslCheck $command 22
    } catch [TimeoutException] {
        Write-Host '[HTTPS] Ubuntu 测试超时。' -ForegroundColor Yellow
        return $false
    }
    if ($result.Text -notmatch '^(\d{3}) (\d{3})$') {
        Write-Host '[HTTPS] 无法完成测试。请检查代理同步程序和 Ubuntu 中的 curl。' -ForegroundColor Yellow
        return $false
    }

    $connectStatus = $Matches[1]
    $status = $Matches[2]
    $transport = if ($connectStatus -eq '000') { '未观察到 HTTP CONNECT' } else { "HTTP CONNECT $connectStatus" }
    $passed = $result.Code -eq 0 -and [int]$status -ge 200 -and [int]$status -lt 400
    $color = if ($passed) { 'Green' } else { 'Yellow' }
    Write-Host "[HTTPS] example.com，$transport，目标 HTTP $status。" -ForegroundColor $color
    return $passed
}

try {
    Write-Host 'Ubuntu-DS 连接诊断' -ForegroundColor Cyan
    $registered = Invoke-WslCheck '--list --quiet'
    if ($registered.Code -ne 0) { throw '无法读取 WSL 发行版列表。请运行 wsl.exe --status。' }
    if (($registered.Text -split '\r?\n').Trim() -notcontains $distro) {
        throw 'Windows 未注册 Ubuntu-DS。'
    }

    $running = Invoke-WslCheck '--list --quiet --running'
    if ($running.Code -ne 0) { throw '无法读取 WSL 运行状态。请运行 wsl.exe --status。' }
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
    Write-ProxyStatus

    $relay = Invoke-WslCheck "-d $distro --exec systemctl is-active ds-rdp-v4.service"
    $desktop = Invoke-WslCheck "-d $distro --exec systemctl --user is-active ds-shell-headless.service gnome-remote-desktop-headless.service"
    $desktopServices = @($desktop.Text -split '\r?\n')
    $desktopReady = $desktop.Code -eq 0 -and $desktopServices.Count -eq 2 -and ($desktopServices -notmatch '^active$').Count -eq 0
    $relayReady = $relay.Code -eq 0 -and $relay.Text -eq 'active'
    if ($relayReady -and $desktopReady) {
        Write-Host '[桌面] GNOME、远程桌面服务和 IPv4 中继已启动。' -ForegroundColor Green
    } else {
        if (-not $desktopReady) { Write-Host '[桌面] GNOME Shell 或远程桌面服务未就绪。请检查 Ubuntu 的用户服务。' -ForegroundColor Yellow }
        if (-not $relayReady) { Write-Host '[桌面] IPv4 中继未运行。请检查 ds-rdp-v4.service。' -ForegroundColor Yellow }
    }

    $probePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'setup\rdp-probe.ps1'
    if (-not (Test-Path -LiteralPath $probePath -PathType Leaf)) { throw '项目中缺少 setup\rdp-probe.ps1。' }
    $probe = & $probePath '127.0.0.1' 3393 v4
    $rdpReady = $probe -match '^RDP_OK\b'
    if ($rdpReady) {
        Write-Host '[RDP] Windows 本地端口 3393 握手成功。' -ForegroundColor Green
    } else {
        Write-Host '[RDP] Windows 本地端口 3393 握手失败。' -ForegroundColor Yellow
        if (Get-NetTCPConnection -LocalPort 3393 -State Listen -ErrorAction SilentlyContinue) {
            Write-Host '[RDP] 端口已有监听。请检查 Windows 端口是否被其他程序占用，以及 Ubuntu 的 GNOME 服务是否就绪。' -ForegroundColor Yellow
        } else {
            Write-Host '[RDP] Windows 未发现端口监听。请检查 WSL 的本地端口转发和 Ubuntu 的 IPv4 中继。' -ForegroundColor Yellow
        }
    }
    $httpsPassed = if ($TestHttps) { Test-GuestHttps } else { $true }
    if (-not ($hasRoute -and $relayReady -and $desktopReady -and $rdpReady -and $httpsPassed)) { exit 1 }
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
