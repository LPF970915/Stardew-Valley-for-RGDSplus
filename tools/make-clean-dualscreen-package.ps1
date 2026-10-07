[CmdletBinding()]
param([string]$OutputDir = '', [string]$ModSource = '', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$port = Join-Path $root 'port/stardewvalleymainline'
$source = if ($ModSource) { (Resolve-Path -LiteralPath $ModSource).Path } else { Join-Path $root 'dualscreen/VerticalWorld' }
$manifest = Get-Content (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.Version
if ($version -notmatch '^[0-9a-zA-Z.-]+$') { throw 'Invalid release version.' }
if (!$OutputDir) { $OutputDir = Join-Path $root 'build/releases' }
$out = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $out | Out-Null
if (!$SkipBuild) {
  & (Join-Path $root 'tools/dotnet6/dotnet.exe') build (Join-Path $source 'VerticalWorld.csproj') -c Release -v:q
  if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
}
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$staging = Join-Path $tempRoot ('sdv-clean-' + [guid]::NewGuid().ToString('N'))
$archive = Join-Path $out "stardew-rgdsplus-dualscreen-$version-clean.tar.gz"
if (Test-Path -LiteralPath $archive) { throw "Release already exists; choose another OutputDir: $archive" }
try {
  $payload = Join-Path $staging 'StardewVerticalWorldD2/StardewValleyMainline'
  New-Item -ItemType Directory -Force -Path $payload | Out-Null
  # Only runtime inputs; never copy local gamedata, saves, Mods or debug directories.
  foreach ($name in @('dlls','dotnet','gl4es.aarch64','libs.aarch64','licenses','overrides','tools')) {
    Copy-Item -LiteralPath (Join-Path $port $name) -Destination $payload -Recurse
  }
  foreach ($name in @('cover.png','screenshot.png','gameinfo.xml','port.json','stardewvalleymainline.md')) {
    Copy-Item -LiteralPath (Join-Path $port $name) -Destination $payload
  }
  Copy-Item -LiteralPath (Join-Path $root 'port/StardewValleyMainline.sh') -Destination $payload
  $mod = Join-Path $payload 'Mods/RGDSplus.VerticalWorld'
  New-Item -ItemType Directory -Force -Path $mod,(Join-Path $payload 'gamedata'),(Join-Path $payload 'savedata') | Out-Null
  Copy-Item -LiteralPath (Join-Path $source 'bin/Release/net6.0/VerticalWorld.dll'),(Join-Path $source 'manifest.json') -Destination $mod
  if (Test-Path -LiteralPath (Join-Path $source 'i18n')) { Copy-Item -LiteralPath (Join-Path $source 'i18n') -Destination $mod -Recurse }
  Copy-Item -LiteralPath (Join-Path $source 'SMAPI-config.json') -Destination (Join-Path $payload 'Mods')
  $entryName = 'Stardew Valley Vertical World D2 Card2.sh'
  Copy-Item -LiteralPath (Join-Path $root ('port/' + $entryName)) -Destination $staging
  # Runtime docs/XML are retained; temporary diagnostics and debug symbols are omitted.
  Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object {
    $_.Extension -in @('.log','.pdb','.next') -or $_.Name -like 'frame-metrics*.csv' -or $_.Name -eq 'profiling.enabled'
  } | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
  foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File -Filter '*.sh') {
    [IO.File]::WriteAllText($file.FullName, ([IO.File]::ReadAllText($file.FullName) -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
  }
  $dllHash = (Get-FileHash -LiteralPath (Join-Path $mod 'VerticalWorld.dll')).Hash.ToLowerInvariant()
  $hasDashboard = Test-Path -LiteralPath (Join-Path $source 'DisplayModeController.cs')
  $controls = if ($hasDashboard) {
    "进入存档后按物理 Select 切换：连续双屏场景 <-> 上屏世界/对话 + 下屏常驻 UI。Select 兼容 SDL ControllerBack、BigButton 和键盘 Back。`n副屏窗口模式使用原模组 2.0 ReferencePage 结构：顶部三个橙色原生标签与像素箭头切页，制作/任务左列表右详情，背包、关系、田地、献祭等各有专用分区。Start 不重复打开弹窗；连续双屏模式保留原生 Start 菜单。`nL/R 轮换背包行；下屏触摸可选择物品、拖动物品换位、长按食物食用，纵向拖动列表滚动。地图支持单指拖动和缩放按钮。设置提供游戏选项、外观和页面显隐。`n存档、过场、使用工具或手持菜单物品未放下时不切换。测试使用独立内存农场，不包含或修改用户存档。"
  } else { '本冻结包保留连续双屏场景与下屏 UI，尚不含 Select 第二模式。' }
  [ordered]@{ version=$version; modSha256=$dllHash; physicalBuffer='2048x768'; upperHalf='left'; lowerHalf='right'; gameAssetsIncluded=$false; savesIncluded=$false; requiredGame='Windows Steam mainline copy supplied by user' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'RELEASE.json') -Encoding utf8
  @(
    "RGDSplus 双屏干净部署包 $version",
    '',
    '请将包内入口脚本和 StardewVerticalWorldD2 文件夹直接解压到卡二 /mnt/sdcard/Ports，不要再套一层同名目录。',
    '运行时完整路径：/mnt/sdcard/Ports/StardewVerticalWorldD2/StardewValleyMainline/',
    '请将自有 Windows Steam 主线版游戏数据放入其中的 gamedata/；本包不含游戏资源、存档或运行日志。',
    '从掌机菜单启动 Stardew Valley Vertical World D2 Card2。入口兼容此前设备已有的嵌套目录。',
    '',
    '升级已有设备：先正常退出游戏并备份原 Mods/RGDSplus.VerticalWorld 和入口，再覆盖对应运行文件；保留 gamedata/ 与 savedata/。',
    '如果已有 Mods/SMAPI-config.json，请合并 ListenForConsoleInput=false，保留其他自定义配置。',
    '不要删除或替换现有存档。本包没有存档迁移步骤。',
    '',
    '默认仍是连续双屏场景；具体版本功能与验收范围见项目 docs/ 下相应记录。',
    '两个显示模式共用界面缩放，默认 150%，适配 4.3 英寸 1024x768；设置中的主动调整会保留，不改变世界缩放。',
    $controls,
    'SHA256SUMS.txt 校验全部包内文件；RELEASE.json 标识 Mod 版本和 DLL 校验值。'
  ) -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $staging 'INSTALL.zh-CN.txt') -Encoding utf8
  $unexpected = @(Get-ChildItem -LiteralPath (Join-Path $payload 'gamedata'),(Join-Path $payload 'savedata') -Recurse -File)
  if ($unexpected.Count) { throw 'Release contains game data or saves.' }
  Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.FullName.Substring($staging.Length + 1).Replace('\','/')
  } | Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS.txt') -Encoding ascii
  & py -3 (Join-Path $PSScriptRoot 'create-clean-tar.py') $staging $archive
  if ($LASTEXITCODE -ne 0) { throw 'Portable archive creation/validation failed.' }
} finally {
  $resolved = [IO.Path]::GetFullPath($staging)
  if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'sdv-clean-*') { throw 'Unexpected staging cleanup path.' }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
'{0}  {1}' -f (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant(), (Split-Path $archive -Leaf) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Host "Created $archive"
Get-Content -LiteralPath ($archive + '.sha256')
