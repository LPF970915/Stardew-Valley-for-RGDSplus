# Stardew Valley for RGDSplus

## Current Release - 2026-10-12

当前发布包为 `20261012-04`，包含 PortMaster 适配层、ARM64 随包运行依赖和
Windows Steam 正版资源本地导入工具，不包含游戏本体、存档、账号或测试状态。
菜单入口为 **Stardew Valley for RGDSplus**。将 ZIP 内 `Ports/` 合并到卡二根目录，
再运行包内 `steam-import/Import Steam Assets.cmd` 导入自己购买的 Windows 主线版。
安装、升级和旧 D2 目录兼容说明见 [发布包安装说明](packaging/README.zh-CN.md)。

```powershell
& tools/dotnet6/dotnet.exe build dualscreen/VerticalWorld/VerticalWorld.csproj -c Release
py -3 tools/build-release.py
py -3 tools/test-release.py -v
```

产物：`build/releases/20261012-04/Stardew Valley for RGDSplus.zip`，
以及整体 SHA256 与审计 JSON。构建不会导入游戏，也不会联网获取本体。
正常发布入口解除输出静音但保留音量；显式 QA 运行不解除测试静音。
启动前检测 PulseAudio；固件音频服务启动失败时，直接打开 RGDSplus 扬声器 PCM，
绕过导致崩溃的环绕声插件探测。诊断记录在游戏目录 `logs/audio-*`。

世界画面恢复原始分辨率和垂直同步，绘制目标为 30 帧，逻辑更新保持原来的 60 Hz。
在 100% 游戏缩放下，连续双屏世界为 `1024x1536`，最终物理帧仍为 `2048x768`。
原画质下减少透明像素混合开销；不启用 RGA 或低分辨率放大。
实测范围及仍存在的密集场景、首次动作长帧见 [原画质 30 帧记录](docs/PERFORMANCE_NATIVE30_20261012.zh-CN.md)。
`20261012-04` 默认启用首次工具动作预热，启动约增加 2 秒。用户手动实机复测后确认基本达到游玩要求。
三类工具首用对照已通过；此前自动长测中的密树回退、内存趋势和偶发长帧仍保留为已知限制。
未采用出帧节奏试验。完整结果与本次启用说明见 [首用与帧间隔记录](docs/PERFORMANCE_FOLLOWUP_20261012.zh-CN.md)。

邮件窗口已适配下屏并保留附件/任务交互；单屏模式下屏导航末尾增加 MOD 和退出页。
MOD 开关在退出到桌面并重新进入后生效，退出页支持返回标题或掌机系统。
返回标题会重新加载游戏，以避开原生切换时的内存峰值；此过程保留本次 MOD 启用集合。

仓库保留生产源码、发布脚本、回归测试和验收报告。一次性设备实验已移出维护目录；
测试入口及本地资料管理说明见 [开发与验证工具](tools/README.zh-CN.md)。

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
