# Stardew Valley for RGDSplus

发布版本：`20261009-02`，双屏模组 `0.6.0-reference-v2`。
维护：Blood_roc。本包是非官方 RGDSplus 适配包，不是游戏本体。

只提供 PortMaster 适配层、随包 ARM64 依赖和本地 Steam 资源导入工具。
请购买正版 Stardew Valley，使用自己合法取得的 Windows Steam 主线版资源。
包内没有正式游戏资源、个人存档、测试农场、日志、账号或凭据。

## 安装

1. 退出游戏，将 ZIP 内唯一的 `Ports` 文件夹合并到卡二根目录。
   不要形成 `Ports/Ports`。适用 RGDSplus 双 1024x768、Linux/Wayland 固件。
2. 在 Steam 中选择 Stardew Valley → 管理 → 浏览本地文件。
   使用常规 Windows 主线分支，不使用 compatibility 分支。
   已验证版本为 `1.6.15.24356` / .NET `6.0.32`；其他游戏版本未实机验收。
3. 电脑双击
   `Ports/Stardew Valley for RGDSplus/steam-import/Import Steam Assets.cmd`，
   输入上述游戏安装目录。自动识别仅覆盖 Steam 默认库，自定义库请输入路径。
   工具在本地复制托管程序集、JSON/XML 与完整 `Content`，逐文件检查 SHA256；
   不登录 Steam、不读取账号、不执行 Windows 游戏程序。
4. 安全弹出内存卡，从掌机 Ports 菜单选择 **Stardew Valley for RGDSplus**。
   首次启动由随包 PortMaster 补丁器在导入副本上应用 ARM64 和 SMAPI 适配。
   不修改电脑 Steam 原文件。首次启动可能比以后慢，期间不要拔卡或断电。

```text
内存卡根目录/
  Ports/
    Stardew Valley for RGDSplus.sh
    Stardew Valley for RGDSplus/
      StardewValleyMainline.sh
      dlls/ dotnet/ gl4es.aarch64/ libs.aarch64/
      tools/ overrides/ licenses/
      Mods/RGDSplus.VerticalWorld/
      steam-import/                   <- 电脑端本地导入工具
      gamedata/                       <- 用户导入正版资源
      savedata/                       <- 游戏运行后生成
      RELEASE.json SHA256SUMS.txt
```

本包附带 .NET 6.0.32、SMAPI 4.5.2、PortMaster 游戏补丁器、
MonoGame/GL4ES 与 ARM64 原生库，包括联机需要的 `liblwjgl_lz4.so`。
不需要联网下载这些游戏运行依赖。
仍需要固件自带的 PortMaster `control.txt`、输入工具、Python 3、
Wayland、PulseAudio 和系统驱动；本包不替换固件或共享 PortMaster。
请预留至少 2 GB 可用空间；重新导入保留旧资源备份时需要更多空间。

也可手动将 Windows Steam 安装目录的完整 `Content` 与根目录托管
DLL、JSON/XML 放入 `gamedata`；推荐使用导入工具避免遗漏或混入 Windows 原生库。
不要复制 Steam 账号目录、存档、SMAPI 用户安装、Windows 运行时或整个 Steam 库。
运行时检查只能检查内容完整性与兼容性，不构成购买凭证认证。

## 升级与存档

升级前备份原 `savedata` 以及 `Mods/RGDSplus.VerticalWorld` 中的用户配置。
合并覆盖适配文件，保留 `gamedata`、`savedata`、模组配置与历史记录。
本包中的这两个空目录不会清空已有内容；不要先删除整个游戏目录。

新安装使用与菜单脚本同名的 `Stardew Valley for RGDSplus/` 文件夹，
文件夹根目录直接就是运行文件，不再套 `StardewVerticalWorldD2` 或
`StardewValleyMainline`。旧设备若已有 `StardewVerticalWorldD2` 目录，
入口会在新目录没有完整正版资源时回退使用旧目录，避免分裂存档；
后续升级可把新包根目录文件合并到旧实际运行目录。

导入器遇到非空 `gamedata` 默认拒绝覆盖。需要更新正版资源时，在电脑 PowerShell 运行：

```powershell
& '.\Import Steam Assets.ps1' -SteamDir '你的Steam游戏安装目录' -Replace
```

旧目录会保留为相邻 `gamedata.backup-*`，导入失败会回滚目录切换；
`savedata` 和 `Mods` 始终不参与导入。确认正常后再自行处理备份。

## 当前适配

- 默认 `SceneHud` 连续双屏世界；Select 切换 `ExpandedHud` 上屏世界/对话、
  下屏常驻背包、制作、任务、关系、地图、田地、机器、献祭、日历和设置。
- 两种模式共享默认 150% UI。副屏模式 Start 不重复弹出暂停菜单，
  连续双屏模式保留原生 Start。存档、过场及工具动作期间限制切换。
- 下屏支持物品选择、拖动、长按食用、列表滚动和地图操作；
  L/R 切换背包行。Select 长按约 1.5 秒应急退出。
- 标题、角色创建、加载/合作菜单和 NPC 对话包含 150% 布局修复。
  物理传输帧仍为 `2048x768`，游戏逻辑保持单屏 4:3，不是横向拉伸。
- 正常启动解除主输出静音但不调整音量；自动测试显式启用 QA 后保持静音。
  发布入口不设置 QA，包内不含静音配置、性能探针或测试农场，QA 默认关闭。

联机压缩库已通过 PC/ARM64 双向数据兼容测试；
这不等于两台掌机实际局域网游玩、掌机主持或长时间稳定性验收。
实际联机、实体触摸手感、声音听感仍需后续人工体验。

## 校验与排错

ZIP 旁的 `.sha256` 校验整个发布包。`SHA256SUMS.txt` 覆盖包内所有文件
（自身除外），`RELEASE.json` 标识构建、依赖与模组哈希。
校验原始适配包；导入和首次启动会生成或修改用户自己的运行副本。

无法启动时检查游戏根目录 `log.txt` 与
`Mods/RGDSplus.VerticalWorld/vertical-world.log`。
缺资源先重新导入；缺 PortMaster 工具则检查固件，勿用 Windows DLL 替换 ARM64 库。
问题反馈注明固件、版本、入口、复现步骤及输入来源，日志公开前脱敏。
不要上传正版资源、存档、生成的完整游戏副本或账号信息。
第三方来源与许可证见 `THIRD_PARTY_NOTICES.md` 和 `licenses/`。
