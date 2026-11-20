[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?$')]
    [string]$Version,

    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$packageName = "WSL-Ubuntu-$Version-windows"
$zipName = "$packageName.zip"
$checksumName = "$zipName.sha256"

# Source files permitted in a download. The console binary is compiled separately.
$releaseFiles = @(
    'README.md'
    'BUILDING.md'
    'CHANGELOG.md'
    'CONTRIBUTING.md'
    'SECURITY.md'
    'LICENSE'
    'docs/architecture.md'
    'docs/installation.md'
    'docs/troubleshooting.md'
    'bin/ds-backup.cmd'
    'bin/ds-console-embed.rdp'
    'bin/ds-desktop.cmd'
    'bin/ds-desktop.rdp'
    'bin/ds-doctor.cmd'
    'bin/ds-doctor.ps1'
    'bin/ds-root.cmd'
    'bin/ds-stop.cmd'
    'bin/ds.cmd'
    'bin/start-ubuntu.cmd'
    'setup/app.manifest'
    'setup/build-console.cmd'
    'setup/ds-headless-shell'
    'setup/ds-proxy-bash'
    'setup/ds-proxy-watch'
    'setup/ds-proxy-watch.service'
    'setup/ds-rdp-v4.service'
    'setup/install-guest-desktop.sh'
    'setup/install-host-network.sh'
    'setup/install-windows.ps1'
    'setup/package-windows.ps1'
    'setup/rdp-probe.ps1'
    'setup/UbuntuDSConsole.cs'
    'setup/wslconfig.example'
)

foreach ($directory in 'bin', 'docs', 'setup') {
    $item = Get-Item -LiteralPath (Join-Path $projectRoot $directory) -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected an ordinary project directory at $directory."
    }
}
foreach ($relativePath in $releaseFiles) {
    $item = Get-Item -LiteralPath (Join-Path $projectRoot $relativePath) -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected an ordinary project file at $relativePath."
    }
}

$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($destination)
$archivePath = Join-Path $destination $zipName
$checksumPath = Join-Path $destination $checksumName
if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw 'The output already exists. Use a new destination or a different version.'
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$scratch = Join-Path $destination ('.wsl-ubuntu-package-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$scratchExe = Join-Path $scratch 'UbuntuDS-Console.exe'
$scratchZip = Join-Path $scratch $zipName
$scratchChecksum = Join-Path $scratch $checksumName
$archiveMoved = $false

try {
    & (Join-Path $PSScriptRoot 'build-console.cmd') $scratchExe
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $scratchExe -PathType Leaf)) {
        throw 'The Windows console build did not produce an executable.'
    }

    $archive = [IO.Compression.ZipFile]::Open($scratchZip, [IO.Compression.ZipArchiveMode]::Create)
    $compression = [IO.Compression.CompressionLevel]::Optimal
    $archiveTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $entries = [ordered]@{}
    foreach ($relativePath in $releaseFiles) {
        $entries[$relativePath] = Join-Path $projectRoot $relativePath
    }
    $entries['bin/UbuntuDS-Console.exe'] = $scratchExe
    try {
        foreach ($relativePath in $entries.Keys) {
            $entry = $archive.CreateEntry("$packageName/$relativePath", $compression)
            $entry.LastWriteTime = $archiveTimestamp
            $target = $entry.Open()
            try {
                $source = [IO.File]::OpenRead($entries[$relativePath])
                try { $source.CopyTo($target) } finally { $source.Dispose() }
            } finally {
                $target.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }

    $sha256 = (Get-FileHash -LiteralPath $scratchZip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($scratchChecksum, "$sha256  $zipName`n", [Text.Encoding]::ASCII)
    [IO.File]::Move($scratchZip, $archivePath)
    $archiveMoved = $true
    [IO.File]::Move($scratchChecksum, $checksumPath)
} catch {
    if ($archiveMoved) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    throw
} finally {
    foreach ($file in $scratchExe, $scratchZip, $scratchChecksum) {
        if (Test-Path -LiteralPath $file) {
            Remove-Item -LiteralPath $file -Force
        }
    }
    [IO.Directory]::Delete($scratch)
}

Write-Output "Package: $archivePath"
Write-Output "SHA-256: $checksumPath"
