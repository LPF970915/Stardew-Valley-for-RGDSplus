# Stardew Valley for RGDSplus

## Current Release - 2026-10-09

当前发布包为 `20261009-02`，包含 PortMaster 适配层、ARM64 随包运行依赖和
Windows Steam 正版资源本地导入工具，不包含游戏本体、存档、账号或测试状态。
菜单入口为 **Stardew Valley for RGDSplus**。将 ZIP 内 `Ports/` 合并到卡二根目录，
再运行包内 `steam-import/Import Steam Assets.cmd` 导入自己购买的 Windows 主线版。
安装、升级和旧 D2 目录兼容说明见 [发布包安装说明](packaging/README.zh-CN.md)。

```powershell
& tools/dotnet6/dotnet.exe build dualscreen/VerticalWorld/VerticalWorld.csproj -c Release
py -3 tools/build-release.py
py -3 tools/test-release.py -v
```

产物：`build/releases/20261009-02/Stardew Valley for RGDSplus.zip`，
以及整体 SHA256 与审计 JSON。构建不会导入游戏，也不会联网获取本体。
正常发布入口解除输出静音但保留音量；显式 QA 运行不解除测试静音。

## Verified Dual-Screen Baseline

当前维护目标是 `0.6.0-reference-v2`。开发验收入口为 **Ports -> Stardew Valley Vertical World D2 Card2**，发布入口为 **Stardew Valley for RGDSplus**，双屏物理承载为 `2048x768`，游戏逻辑和菜单保持 `1024x768`，当前物理映射为左半上屏、右半下屏。

进入存档后，默认是 `SceneHud` 连续双屏世界；按 Select 切换到 `ExpandedHud`，上屏保留世界/对话，下屏显示原模组 2.0 参考结构的常驻页面。副屏模式下 Start 不再打开暂停弹窗；连续双屏模式保留原生 Start。两种模式共享 UI 缩放，默认 150%，并使用 PC 游戏运行时原生像素资源。

当前参考版式、限制和未实现范围见 [Reference v2 UI](docs/REFERENCE_V2_UI.zh-CN.md) 与 [原模组 2.0 布局审计](docs/UPSTREAM_V2_LAYOUT_AUDIT.zh-CN.md)。交接和防回归规则见 [双屏已验证经验](docs/DUAL_SCREEN_EXPERIENCE_HANDOFF.zh-CN.md)。

当前实机证据：`build/acceptance-reference-v2/`、`build/acceptance-visible-ui/` 和 `build/releases/reference-v2-final/`。自动化只在显式设置 `SDV_DUAL_UI_TEST_DIR` 时使用内存农场，不读取或修改真实存档。

## Upstream baseline

- Port: stardewvalleymainline.zip
- PortMaster release: 2026-07-07_1030
- MD5: 8557782b70cd56dbbe897f8e980ddc32
- Source: https://github.com/PortsMaster/PortMaster-New/releases/download/2026-07-07_1030/stardewvalleymainline.zip
- Target runtime: aarch64, bundled .NET 6.0.32

The upstream port does not redistribute Stardew Valley. Game data must come from the user's legally purchased Windows Steam copy. Do not commit or distribute gamedata, saves, or Steam credentials.

## Prepare the single-screen package

Run tools/prepare-steam-assets.ps1 from PowerShell. The default source is D:\Program Files\Steam\steamapps\common\Stardew Valley; use -SteamDir for another installation. The script copies the managed game payload and Content into port/stardewvalleymainline/gamedata and excludes Windows native binaries replaced by the bundled ARM64 runtime.

Run tools/make-package.ps1 to create a POSIX tar.gz archive without game data. Install that archive through PortMaster, then copy the prepared gamedata contents into ports/stardewvalleymainline/gamedata on the device.

## RGDSplus card-two deployment

RGDSplus scans the second card from `/mnt/sdcard/Ports` rather than the card-one `/roms/ports` tree. The final release layout is intentionally flat: `Ports/Stardew Valley for RGDSplus.sh` is beside `Ports/Stardew Valley for RGDSplus/`, and that same-named folder directly contains the runtime, saves, logs, and `gamedata`. Copy the package to the card, then copy the prepared Windows game files into `Stardew Valley for RGDSplus/gamedata/`. The launcher resolves its payload relative to the entry path, so it does not depend on the legacy PortMaster `directory` value.

## Single-screen acceptance

1. PortMaster launches StardewValleyMainline.sh from the handheld menu.
2. The title screen has video and audio.
3. D-pad/left stick, A/B/X/Y, shoulders, Start, and Select work.
4. A new farm can be created, saved, and loaded after restarting.
5. Exit returns to the launcher and leaves log.txt with status 0.
6. Peak memory is suitable for the 1 GB device; collect meminfo and RSS.

Only after all six checks pass should the dual-screen UI split be enabled. It must keep one game process, one update loop, and one save location. On RGDSplus the physical outputs are currently exposed as two horizontal 1024x768 viewports (2048x768 composite); 1024x1536 is only the vertical design/validation coordinate space. The lower panel must not be populated by simply cropping a horizontally stretched frame.

The dual-screen modes are `SceneHud` by default (continuous world extension with lower HUD) and `ExpandedHud` on Select (upper world/dialogue with persistent lower pages). Both modes run in one process and keep the companion pages outside the native active menu. The original single-screen launcher remains a fallback.
