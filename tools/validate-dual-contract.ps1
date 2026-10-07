[CmdletBinding()]
param(
    [string]$Root = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}
$Root = (Resolve-Path -LiteralPath $Root).Path

$failures = New-Object System.Collections.Generic.List[string]
function Require-Text([string]$Path, [string]$Pattern, [string]$Description) {
    $full = Join-Path $Root $Path
    if (-not (Test-Path -LiteralPath $full)) {
        $failures.Add("missing $Path ($Description)")
        return
    }
    $text = Get-Content -LiteralPath $full -Raw
    if ($text -notmatch $Pattern) {
        $failures.Add("$Path does not contain $Description")
    }
}

Require-Text "README.md" "2048x768" "physical composite output contract"
Require-Text "README.md" "SceneHud" "two-state design"
Require-Text "docs/DUAL_SCREEN_EXPERIENCE_HANDOFF.zh-CN.md" "同一进程、同一更新循环" "single-process constraint"
Require-Text "docs/DUAL_SCREEN_EXPERIENCE_HANDOFF.zh-CN.md" "1024x1536" "vertical design coordinate space is not global logic height"
Require-Text "docs/REFERENCE_V2_UI.zh-CN.md" "ReferencePage" "reference-v2 UI scope"
Require-Text "docs/DUAL_SCREEN_EXPERIENCE_HANDOFF.zh-CN.md" "SDV_DUAL_UI_TEST_DIR" "QA boundary"
Require-Text "port/Stardew Valley Vertical World D2 Card2.sh" "SDV_DUAL_SCREEN_VERTICAL_WORLD=1" "D2 vertical-world switch"

$launcher = Join-Path $Root "port/StardewValleyMainline.sh"
if (Test-Path -LiteralPath $launcher) {
    $launcherText = Get-Content -LiteralPath $launcher -Raw
    if ($launcherText -match "(?m)^\s*export\s+DISPLAY_HEIGHT\s*=\s*1536") {
        $failures.Add("formal launcher must not force DISPLAY_HEIGHT=1536")
    }
    if ($launcherText -match "(?m)^\s*export\s+DISPLAY_WIDTH\s*=\s*2048") {
        $failures.Add("formal launcher must not force DISPLAY_WIDTH=2048; 2048x768 is physical transport only")
    }
} else {
    $failures.Add("missing formal launcher port/StardewValleyMainline.sh")
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Dual-screen contract: PASS"
Write-Output "Reference-v2 dual-screen contract is current; 2048x768 remains physical transport only."
