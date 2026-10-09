[CmdletBinding()]
param(
    [string]$SteamDir = '',
    [string]$PortDir = '',
    [switch]$Clean,
    [switch]$Replace
)
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($PortDir)) {
    if (Test-Path -LiteralPath (Join-Path $scriptRoot '..\StardewValleyMainline.sh')) {
        $PortDir = Join-Path $scriptRoot '..\gamedata'
    } else {
        $PortDir = Join-Path $scriptRoot '..\port\stardewvalleymainline\gamedata'
    }
}
if ([string]::IsNullOrWhiteSpace($SteamDir)) {
    $steam = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
    if ($steam -and $steam.SteamPath) {
        $candidate = Join-Path $steam.SteamPath 'steamapps\common\Stardew Valley'
        if (Test-Path -LiteralPath (Join-Path $candidate 'Stardew Valley.dll')) { $SteamDir = $candidate }
    }
    if ([string]::IsNullOrWhiteSpace($SteamDir)) {
        $SteamDir = Read-Host 'Steam: Manage > Browse local files. Enter the Stardew Valley folder'
    }
}
$source = (Resolve-Path -LiteralPath $SteamDir).Path
$destination = [IO.Path]::GetFullPath($PortDir)
$comparison = [StringComparison]::OrdinalIgnoreCase
$sourcePrefix = $source.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
$destinationPrefix = $destination.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if ($source.Equals($destination, $comparison) -or $destinationPrefix.StartsWith($sourcePrefix, $comparison) -or $sourcePrefix.StartsWith($destinationPrefix, $comparison)) {
    throw 'Source and destination must be separate directories, neither inside the other.'
}
if ((Split-Path $destination -Leaf) -ne 'gamedata') { throw 'PortDir must name the adapter gamedata directory, not its parent.' }
$required = @('Stardew Valley.dll','Stardew Valley.deps.json','Stardew Valley.runtimeconfig.json','StardewValley.GameData.dll','MonoGame.Framework.dll','xTile.dll','Content')
foreach ($item in $required) { if (-not (Test-Path -LiteralPath (Join-Path $source $item))) { throw "Missing required Steam item: $item" } }
$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $source 'Stardew Valley.runtimeconfig.json') | ConvertFrom-Json
$framework = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App')[0].version
if ([string]::IsNullOrWhiteSpace($framework)) { $framework = $runtimeConfig.runtimeOptions.framework.version }
if ($framework -notlike '6.*') { throw "Unsupported Stardew Valley runtime version '$framework'; this port expects .NET 6." }
Get-Content -Raw -LiteralPath (Join-Path $source 'Stardew Valley.deps.json') | ConvertFrom-Json | Out-Null
$contentCount = @(Get-ChildItem -LiteralPath (Join-Path $source 'Content') -Recurse -File).Count
if ($contentCount -lt 3000) { throw "Content copy appears incomplete: only $contentCount source files." }
if (@(Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
    throw 'Steam source contains links or reparse points; use a regular local installation.'
}
if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Destination must not be a link or reparse point.'
}
if ((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Force | Where-Object Name -ne '.gitkeep').Count -and -not ($Replace -or $Clean)) {
    throw 'gamedata is not empty. Use -Replace to import with a retained backup; savedata and Mods are never touched.'
}
# Keep all managed assemblies, JSON, XML and Content assets. The port supplies ARM64
# replacements for Windows-native binaries, so omit PE executables, DLL import shims,
# and native .so/.dylib files from the Steam directory.
$nativePatterns = @('*.exe','*.dylib*','*.so*','api-ms-*.dll','Galaxy64.dll','SDL2.dll','steam_api64.dll','ucrtbase.dll','soft_oal.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','clrjit.dll','createdump.exe','dbgshim.dll','mscordaccore*.dll','mscordbi.dll','mscorrc.dll','msquic.dll')
$parent = Split-Path -Parent $destination
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$staging = Join-Path $parent ('gamedata.import-' + [guid]::NewGuid().ToString('N'))
$backup = $null
try {
New-Item -ItemType Directory -Path $staging | Out-Null
Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
    if ($_.PSIsContainer) {
        if ($_.Name -eq 'Content') { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $staging $_.Name) -Recurse }
        return
    }
    if ($_.Extension -notin @('.dll','.json','.xml')) { return }
    $skip = $false
    foreach ($pattern in $nativePatterns) { if ($_.Name -like $pattern) { $skip = $true; break } }
    if (-not $skip) { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $staging $_.Name) }
}
$files = @(Get-ChildItem -LiteralPath $staging -Recurse -File)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($staging.Length + 1)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source $relative)).Hash) {
        throw "Copy hash mismatch: $relative"
    }
}
$copiedCount = @(Get-ChildItem -LiteralPath (Join-Path $staging 'Content') -Recurse -File).Count
if ($copiedCount -ne $contentCount) { throw 'Content file count changed during import.' }
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $parent ('gamedata.backup-' + [guid]::NewGuid().ToString('N'))
    Move-Item -LiteralPath $destination -Destination $backup
}
try { Move-Item -LiteralPath $staging -Destination $destination }
catch {
    if ($backup -and -not (Test-Path -LiteralPath $destination)) { Move-Item -LiteralPath $backup -Destination $destination }
    throw
}
} finally {
    $resolved = [IO.Path]::GetFullPath($staging)
    if (-not $resolved.StartsWith($parent.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, $comparison) -or (Split-Path $resolved -Leaf) -notlike 'gamedata.import-*') {
        throw 'Unexpected import cleanup path.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Write-Host "Prepared $contentCount Content files in $destination"
if ($backup) { Write-Host "Previous gamedata retained at $backup" }
Write-Host 'Windows native binaries were intentionally excluded; the PortMaster package supplies ARM64 replacements.'
