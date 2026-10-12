[CmdletBinding()]
param(
    [string]$SteamDir = '',
    [string]$PortDir = '',
    [switch]$PromptSteamDir,
    [switch]$Clean,
    [switch]$Replace
)
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = $null
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        $hash = $algorithm.ComputeHash($stream)
        return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
    } finally {
        if ($null -ne $algorithm) { $algorithm.Dispose() }
        $stream.Dispose()
    }
}
Write-Host ''
Write-Host 'Stardew Valley for RGDSplus - Steam asset import' -ForegroundColor Cyan
Write-Host 'The Steam folder must contain Stardew Valley.dll and Content.'
if ($PromptSteamDir) {
    Write-Host ''
    Write-Host 'In Steam: Stardew Valley -> Manage -> Browse local files.'
    $SteamDir = Read-Host 'Paste the Steam Stardew Valley folder and press Enter'
    if ([string]::IsNullOrWhiteSpace($SteamDir)) { throw 'No Steam folder was entered. Nothing was copied.' }
}
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
        Write-Host ''
        Write-Host 'Steam was not found automatically.' -ForegroundColor Yellow
        Write-Host 'In Steam: Stardew Valley -> Manage -> Browse local files.'
        $SteamDir = Read-Host 'Steam: Manage > Browse local files. Enter the Stardew Valley folder'
    }
}
if ([string]::IsNullOrWhiteSpace($SteamDir)) { throw 'No Steam folder was entered.' }
$SteamDir = $SteamDir.Trim().Trim('"')
try { $source = (Resolve-Path -LiteralPath $SteamDir -ErrorAction Stop).Path }
catch { throw "Steam folder not found: $SteamDir" }
$destination = [IO.Path]::GetFullPath($PortDir)
Write-Host "Source:      $source"
Write-Host "Destination: $destination"
Write-Host '[1/4] Checking game files and building the import list...'
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
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $source 'Content') -Recurse -File)
$sourceFiles += @(Get-ChildItem -LiteralPath $source -File -Force | Where-Object {
    if ($_.Extension -notin @('.dll','.json','.xml')) { return $false }
    $skip = $false
    foreach ($pattern in $nativePatterns) { if ($_.Name -like $pattern) { $skip = $true; break } }
    return -not $skip
})
$totalBytes = ($sourceFiles | Measure-Object -Property Length -Sum).Sum
$copiedBytes = 0L
$done = 0
$timer = [Diagnostics.Stopwatch]::StartNew()
$lastReport = -2.0
Write-Host ("[2/4] Copying {0} files ({1:N0} MiB). Keep this window open." -f $sourceFiles.Count, ($totalBytes / 1MB))
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($source.Length + 1)
    $target = Join-Path $staging $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    if ($file.Length -ge 16MB) { Write-Host ("Copying large file: {0} ({1:N0} MiB)..." -f $relative, ($file.Length / 1MB)) }
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $done++
    $copiedBytes += $file.Length
    if ($done -eq 1 -or $done -eq $sourceFiles.Count -or ($timer.Elapsed.TotalSeconds - $lastReport) -ge 2) {
        Write-Host ("Copied {0}/{1} files, {2:N0}/{3:N0} MiB ({4:N0}%)" -f $done, $sourceFiles.Count, ($copiedBytes / 1MB), ($totalBytes / 1MB), (100 * $done / $sourceFiles.Count))
        $lastReport = $timer.Elapsed.TotalSeconds
    }
}
$files = @(Get-ChildItem -LiteralPath $staging -Recurse -File)
if ($files.Count -ne $sourceFiles.Count) { throw 'Copied file count does not match the import list.' }
$done = 0
$lastReport = -2.0
$timer.Restart()
Write-Host '[3/4] Checking SHA256 of every copied file. Reading the card may take several minutes.'
foreach ($file in $files) {
    $relative = $file.FullName.Substring($staging.Length + 1)
    if ((Get-Sha256 $file.FullName) -ne (Get-Sha256 (Join-Path $source $relative))) {
        throw "Copy hash mismatch: $relative"
    }
    $done++
    if ($done -eq 1 -or $done -eq $files.Count -or ($timer.Elapsed.TotalSeconds - $lastReport) -ge 2) {
        Write-Host ("Verified {0}/{1} files ({2:N0}%)" -f $done, $files.Count, (100 * $done / $files.Count))
        $lastReport = $timer.Elapsed.TotalSeconds
    }
}
$copiedCount = @(Get-ChildItem -LiteralPath (Join-Path $staging 'Content') -Recurse -File).Count
if ($copiedCount -ne $contentCount) { throw 'Content file count changed during import.' }
Write-Host '[4/4] All hashes passed. Installing the verified game data...'
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
