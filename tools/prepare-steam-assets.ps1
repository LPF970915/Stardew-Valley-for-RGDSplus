[CmdletBinding()]
param(
    [string]$SteamDir = 'D:\Program Files\Steam\steamapps\common\Stardew Valley',
    [string]$PortDir = '',
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($PortDir)) { $PortDir = Join-Path $scriptRoot '..\port\stardewvalleymainline\gamedata' }
$source = (Resolve-Path -LiteralPath $SteamDir).Path
$destination = [IO.Path]::GetFullPath($PortDir)
$required = @('Stardew Valley.dll','Stardew Valley.deps.json','Stardew Valley.runtimeconfig.json','Content')
foreach ($item in $required) { if (-not (Test-Path -LiteralPath (Join-Path $source $item))) { throw "Missing required Steam item: $item" } }
$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $source 'Stardew Valley.runtimeconfig.json') | ConvertFrom-Json
$framework = $runtimeConfig.runtimeOptions.includedFrameworks[0].version
if ([string]::IsNullOrWhiteSpace($framework)) { $framework = $runtimeConfig.runtimeOptions.framework.version }
if ($framework -notlike '6.*') { throw "Unsupported Stardew Valley runtime version '$framework'; this port expects .NET 6." }
if ($Clean -and (Test-Path -LiteralPath $destination)) { Remove-Item -LiteralPath $destination -Recurse -Force }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
# Keep all managed assemblies, JSON, XML and Content assets. The port supplies ARM64
# replacements for Windows-native binaries, so omit PE executables, DLL import shims,
# and native .so/.dylib files from the Steam directory.
$nativePatterns = @('*.exe','*.dylib*','*.so*','api-ms-*.dll','Galaxy64.dll','SDL2.dll','steam_api64.dll','ucrtbase.dll','soft_oal.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','clrjit.dll','createdump.exe','dbgshim.dll','mscordaccore*.dll','mscordbi.dll','mscorrc.dll','msquic.dll')
Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
    if ($_.PSIsContainer) {
        if ($_.Name -eq 'Content') { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name) -Recurse -Force }
        return
    }
    $skip = $false
    foreach ($pattern in $nativePatterns) { if ($_.Name -like $pattern) { $skip = $true; break } }
    if (-not $skip) { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name) -Force }
}
$contentCount = (Get-ChildItem -LiteralPath (Join-Path $destination 'Content') -Recurse -File).Count
if ($contentCount -lt 3000) { throw "Content copy appears incomplete: only $contentCount files were copied." }
Write-Host "Prepared $contentCount Content files in $destination"
Write-Host 'Windows native binaries were intentionally excluded; the PortMaster package supplies ARM64 replacements.'
