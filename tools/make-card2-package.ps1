[CmdletBinding()]
param(
    [string]$PortDir = '',
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($PortDir)) { $PortDir = Join-Path $scriptRoot '..\port' }
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $scriptRoot '..\build' }
$port = (Resolve-Path -LiteralPath $PortDir).Path
$out = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $out | Out-Null
$staging = Join-Path ([IO.Path]::GetTempPath()) ('sdv-card2-' + [guid]::NewGuid().ToString('N'))
$archive = Join-Path $out 'stardewvalley-rgdsplus-card2.tar.gz'
try {
    $payload = Join-Path $staging 'StardewValleyMainline'
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    Copy-Item -LiteralPath (Join-Path $port 'StardewValleyMainline.sh') -Destination (Join-Path $payload 'StardewValleyMainline.sh') -Force
    Get-ChildItem -LiteralPath (Join-Path $port 'stardewvalleymainline') -Force |
        Copy-Item -Destination $payload -Recurse -Force
    $gamedata = Join-Path $payload 'gamedata'
    if (Test-Path -LiteralPath $gamedata) {
        Get-ChildItem -LiteralPath $gamedata -Force | Remove-Item -Recurse -Force
        New-Item -ItemType File -Path (Join-Path $gamedata '.gitkeep') | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $port 'Stardew Valley for RGDSplus.sh') -Destination $staging -Force
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    tar -C $staging -czf $archive .
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
$hash.Hash.ToLowerInvariant() + '  ' + (Split-Path $archive -Leaf) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Host "Created $archive"
Write-Host "SHA256 $($hash.Hash.ToLowerInvariant())"
