$ErrorActionPreference = 'Stop'
$probe = Join-Path (Split-Path -Parent $PSScriptRoot) 'setup\rdp-probe.ps1'

$cases = @(
    @{
        Name = 'fragmented X.224 reply'
        Reply = [byte[]](3, 0, 0, 19, 14, 208, 0, 0, 0, 0, 0, 2, 0, 8, 0, 1, 0, 0, 0)
        Expected = 'RDP_OK 03-00-00-13-0E-D0'
    }
    @{
        Name = 'unrelated server on the port'
        Reply = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`n`r`n")
        Expected = 'INVALID_RDP_RESPONSE'
    }
    @{
        Name = 'listener closes without a reply'
        Reply = [byte[]]::new(0)
        Expected = 'CONNECTED_NO_DATA'
    }
    @{
        Name = 'truncated X.224 reply'
        Reply = [byte[]](3, 0, 0, 19, 14)
        Expected = 'INVALID_RDP_RESPONSE'
    }
)

foreach ($case in $cases) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $job = $null
    try {
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $job = Start-Job -ScriptBlock {
            param($ProbePath, $Port)
            & $ProbePath 127.0.0.1 $Port v4
        } -ArgumentList $probe, $port

        $accept = $listener.AcceptTcpClientAsync()
        if (-not $accept.Wait(10000)) { throw 'The RDP probe did not connect to the test listener.' }
        $client = $accept.Result
        try {
            $stream = $client.GetStream()
            $stream.ReadTimeout = 2000
            $request = [byte[]]::new(19)
            $offset = 0
            while ($offset -lt $request.Length) {
                $read = $stream.Read($request, $offset, $request.Length - $offset)
                if ($read -le 0) { throw 'The RDP probe sent an incomplete request.' }
                $offset += $read
            }
            if ([BitConverter]::ToString($request, 0, 6) -cne '03-00-00-13-0E-E0') {
                throw 'The RDP probe did not send an X.224 Connection Request.'
            }
            if ($case.Reply.Length -gt 0) {
                $stream.Write($case.Reply, 0, 2)
                Start-Sleep -Milliseconds 50
                $stream.Write($case.Reply, 2, $case.Reply.Length - 2)
            }
        } finally {
            $client.Dispose()
        }

        if (-not (Wait-Job -Job $job -Timeout 8)) { throw 'The RDP probe did not finish.' }
        $actual = (Receive-Job -Job $job) -join "`n"
        if ($actual -cne $case.Expected) {
            throw "$($case.Name) returned '$actual', expected '$($case.Expected)'."
        }
        Write-Output "Passed $($case.Name)"
    } finally {
        $listener.Stop()
        if ($job) { Remove-Job -Job $job -Force }
    }
}
