# rdp-probe.ps1 —— 对指定主机:端口做标准 X.224 Connection Request 探测
# 用法: rdp-probe.ps1 <host> <port> [v4|v6]
param(
    [Parameter(Mandatory=$true)][string]$Host_,
    [Parameter(Mandatory=$true)][int]$Port,
    [string]$Fam = 'v6'
)
$af = if ($Fam -eq 'v4') { [System.Net.Sockets.AddressFamily]::InterNetwork } else { [System.Net.Sockets.AddressFamily]::InterNetworkV6 }
$c = [System.Net.Sockets.TcpClient]::new($af)
try {
    $pending = $c.BeginConnect($Host_, $Port, $null, $null)
    try {
        if (-not $pending.AsyncWaitHandle.WaitOne(2500)) { throw 'TCP_TIMEOUT' }
        $c.EndConnect($pending)
    } finally {
        $pending.AsyncWaitHandle.Dispose()
    }
    $s = $c.GetStream()
    # 标准 X.224 CR: TPKT(03 00 00 13) + CR(0e) + dst/src/class + RDP_NEG_REQ(SSL+Hybrid)
    [byte[]]$req = 0x03,0x00,0x00,0x13,0x0e,0xe0,0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x08,0x00,0x03,0x00,0x00,0x00
    $s.WriteTimeout = 2500
    $s.Write($req, 0, $req.Length)
    $b = [byte[]]::new(6)
    $read = 0
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($read -lt $b.Length) {
        $remaining = 2500 - [int]$clock.ElapsedMilliseconds
        if ($remaining -le 0) { throw 'RDP_TIMEOUT' }
        $s.ReadTimeout = $remaining
        $count = $s.Read($b, $read, $b.Length - $read)
        if ($count -eq 0) { break }
        $read += $count
    }
    $tpktLength = 256 * [int]$b[2] + [int]$b[3]
    if ($read -eq $b.Length -and $b[0] -eq 3 -and $b[1] -eq 0 -and $tpktLength -ge 11 -and $b[5] -eq 0xd0) {
        Write-Output ('RDP_OK ' + [BitConverter]::ToString($b))
    } elseif ($read -eq 0) {
        Write-Output 'CONNECTED_NO_DATA'
    } else {
        Write-Output 'INVALID_RDP_RESPONSE'
    }
} catch {
    Write-Output ('FAIL: ' + $_.Exception.Message)
} finally {
    $c.Dispose()
}
