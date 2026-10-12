# Stardew Valley for RGDSplus

发布版本：`20261012-04`，双屏模组 `0.6.0-reference-v2`。
本版按用户要求启用首用预热，供手动复测；首用对照通过，完整长测尚未通过。
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
   粘贴上述游戏安装目录并按 Enter，即自动执行复制；路径带空格或两端引号也可。
   工具在本地复制托管程序集、JSON/XML 与完整 `Content`，逐文件检查 SHA256；
   不登录 Steam、不读取账号、不执行 Windows 游戏程序。
   双击后始终先询问正版目录，空输入不会执行复制。窗口显示检查、复制、
   SHA256 校验、安装四个阶段和文件数量。内存卡较慢时需等待，
   看到 `Import complete` 后才能关闭窗口。不要同时启动多个导入窗口。
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

## 20261011-02 音频启动修复

如果固件 PulseAudio 服务未启动，入口会先尝试恢复服务。针对固件在 ALSA `a52` 探测阶段崩溃的情况，自动改用 RGDSplus 扬声器声卡的立体声 PCM。该恢复路径适用于内置扬声器；耳机和蓝牙切换尚未验收。已有可用音频服务保持原状，不改写系统配置或音量。故障诊断保存在 `log.txt` 和 `logs/audio-*`。

## 20261012-02 原画质与 30 帧目标

恢复原始世界分辨率，开启垂直同步，并将绘制上限设为约 30 帧。
100% 游戏缩放下，连续双屏使用 `1024x1536` 世界纹理，副屏页面模式使用
`1024x768`；最终双屏输出仍为 `2048x768`。不再默认低分辨率放大。
游戏逻辑仍以原来的 60 Hz 更新，菜单、文字、触控、视野和游戏速度保持原设置。

世界绘制跳过全透明像素的混合工作，保留半透明颜色与原始像素尺寸。
静止树冠裁去完全空白的边缘，摇动、倒下和自定义效果保持原绘制方式。
这不是全场景锁稳 30 帧的保证：极密树木和未覆盖的首次动作仍可能掉帧。
排查时可在入口将 `SDV_TARGET_FPS` 改为 `0` 取消绘制上限，
将 `SDV_WORLD_ALPHA_DISCARD` 改为 `0` 恢复原世界着色器；均须退出游戏后重启。
`SDV_TREE_TRIM=0` 可单独关闭树冠空白裁剪。
原有音频启动修复保留；测试探针、测试农场和 RGA 原型不包含在发布包中。


## 20261012-03 邮件、MOD 与退出

- 邮件窗口按下屏可用尺寸布局，保留原生文字分页、附件领取和任务接受；支持 75%–150% UI。
- 按 Select 切到上屏游戏、下屏页面的模式后，导航末尾依次为“设置 / MOD / 退出”。
- MOD 页自动发现 `Mods` 内的已安装模组。开关保存为下次启动状态，须退出到桌面后重新进入游戏；仅返回标题不会应用开关。
- 双屏适配、控制台、存档备份等基础组件固定开启。依赖未开启时不能启用，其他已开启 MOD 依赖的组件不能直接关闭。
- 关闭的 MOD 在下次启动时改为同目录的 `.rgds-disabled--原文件夹名`，文件和配置保留；重新开启会恢复原名。
  选择保存在 `Mods/.rgds-mod-state.json`。若需手动恢复，可移除此选择文件并将对应文件夹恢复原名。
- “退出”页有“退出到标题”“退出到桌面”两个按钮。“退出到标题”会先结束当前游戏进程，再在同一会话中重新启动到标题，以避开掌机切换标题时的内存峰值；需要等待重新加载，保留本次 MOD 启用集合。退出到桌面会返回掌机系统；当天未保存进度不会自动保存。

## 20261012-04 首用预热与手动复测

正式菜单入口默认启用首次工具动作预热。启动时准备砍树、割草、敲石及掉落物路径的代码，
不执行游戏动作或加载存档；每次启动约增加 2 秒，返回标题重启进程时也会执行。
采用三轮实机首用对照中的同一份 DLL：首用两秒内最大帧中位数分别从
641→54 ms、519→39 ms、206→42 ms。首次走动、所有交互及第三方 MOD 不在此保证范围内。

本版按用户要求用于手动复测，未宣称完整性能验收通过：30 分钟自动长测中曾出现一次
尚未解释的密树帧率下降、内存增长及约 528 ms 后续长帧。原分辨率、垂直同步、30 帧目标与
60 Hz 逻辑更新保持，未启用试验中的出帧节奏或 RGA 方案。

退出游戏后，将 ZIP 内的 `Ports` 合并覆盖到卡二根目录，保留已有 `gamedata`、`savedata`
和 MOD 配置；已安装完整正版资源无需重新导入。从 **Stardew Valley for RGDSplus** 手动启动。
建议完全退出到桌面后重开，先测试首次砍树、割草、敲石，再测试重复动作、密树和两种屏幕模式。
反馈请注明动作、模式、首次还是重复、游玩多久后发生。游戏目录
`Mods/RGDSplus.VerticalWorld/vertical-world.log` 中的 `first-use-warmup` 记录准备结果。

如需同包对照，可把入口脚本中 `SDV_FIRST_USE_WARMUP="${SDV_FIRST_USE_WARMUP:-1}"`
末尾默认值 `1` 改为 `0`，完全退出后重启；恢复为 `1` 即重新启用。启动耗时会相应变化。
本包没有 QA 插件或静音设置，正常手动启动按原逻辑恢复声音。
