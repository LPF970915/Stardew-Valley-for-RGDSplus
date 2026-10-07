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
$zip = Join-Path $out 'stardewvalleymainline-rgdsplus-single-screen.tar.gz'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
$staging = Join-Path ([IO.Path]::GetTempPath()) ('sdv-port-' + [guid]::NewGuid().ToString('N'))
try {
    Copy-Item -LiteralPath $port -Destination $staging -Recurse -Force
    $stagedGameData = Join-Path $staging 'stardewvalleymainline/gamedata'
    if (Test-Path -LiteralPath $stagedGameData) {
        Get-ChildItem -LiteralPath $stagedGameData -Force | Remove-Item -Recurse -Force
        New-Item -ItemType File -Path (Join-Path $stagedGameData '.gitkeep') | Out-Null
    }
    tar -C $staging -czf $zip .
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
$hash.Hash.ToLowerInvariant() + '  ' + (Split-Path $zip -Leaf) | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Host "Created $zip"
Write-Host "SHA256 $($hash.Hash.ToLowerInvariant())"
