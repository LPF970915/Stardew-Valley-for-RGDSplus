# Stardew Valley for RGDSplus

RGDSplus 双屏设备上的非官方 Stardew Valley 适配项目，由 Blood_roc 维护。
本项目和 Releases 只提供适配层及其运行依赖，**不包含 Stardew Valley 游戏本体**。
请购买游戏，并通过项目提供的本地导入工具使用自己合法取得的 Windows Steam 主线版资源。

## 下载

从 [GitHub Releases](https://github.com/LPF970915/Stardew-Valley-for-RGDSplus/releases)
下载最新的 `Stardew Valley for RGDSplus.zip`，并可用同名 `.sha256` 文件校验完整性。
GitHub 自动生成的 “Source code” 压缩包是源码快照，不是可安装的掌机适配包。

当前发布版本：`20261015-01`，双屏模组版本：`0.6.2-reference-v2`。
完整安装、升级、兼容说明和手动验证范围见
[发布包说明](packaging/README.zh-CN.md)。
本次变更摘要见 [20261015-01 发布说明](docs/RELEASE_20261015-01.zh-CN.md)。

## 适配内容

- RGDSplus 双 1024x768 屏幕，默认连续双屏世界；按 Select 切换副屏页面模式。
- 原生对话框、肖像 MOD 兼容、商店及标题/存档菜单布局调整。
- 新游戏和读档时显示实际加载阶段与进度提示。
- 下屏背包、制作、任务、关系、地图、田地、机器、献祭、日历及设置页面。
- 私有 MOD 运行副本、MOD 开关及肖像资源兼容处理；不改写用户的原始 MOD 和 Steam 游戏资源。
- 离线随包运行依赖和 Windows Steam 资源本地导入工具。

目标环境为 RGDSplus 双 1024x768 屏、ARM64 Linux/Wayland 固件。当前已验证的游戏版本为
`1.6.15.24356`，随包 .NET 为 `6.0.32`，SMAPI 为 `4.5.2`；其他游戏版本、固件以及所有
NPC 商店和 MOD 组合仍需用户按发布说明手动验证。

## 安装概览

1. 将 ZIP 中唯一的 `Ports/` 文件夹合并到第二张卡的根目录，不要创建 `Ports/Ports/`。
2. 在 Windows Steam 中打开 Stardew Valley 安装目录，使用包内的
   `steam-import/Import Steam Assets.cmd` 导入自己购买的游戏资源。
3. 安全弹出内存卡，再从掌机 Ports 菜单启动 **Stardew Valley for RGDSplus**。

详细步骤、升级和存档备份方式见 [发布包说明](packaging/README.zh-CN.md)。

## 构建和验证

需要 .NET 6 SDK、Python 3 和本地仓库中的已审核 ARM64 运行依赖。构建不会下载或导入游戏本体：

```powershell
& tools/dotnet6/dotnet.exe build dualscreen/VerticalWorld/VerticalWorld.csproj -c Release
py -3 tools/build-release.py
py -3 tools/test-release.py -v
```

设备 QA 测试需显式设置 `RGDSPLUS_SSH_HOST`、`RGDSPLUS_SSH_PASSWORD`，且只使用隔离的内存农场。
不要将正版游戏资源、存档、日志、设备凭据或本地 QA 截图提交到仓库。

## 版权与第三方组件

Stardew Valley 名称、游戏内容和相关商标归其各自权利人所有。本项目是非官方移植适配，
不隶属于 ConcernedApe、Chucklefish、Steam、PortMaster 或 RGDSplus 固件维护方。
随包第三方运行组件的来源和许可证见
[THIRD_PARTY_NOTICES.md](packaging/THIRD_PARTY_NOTICES.md) 与发布包中的 `licenses/`。
发布包不包含游戏本体、玩家存档、Steam 账号信息或用户的第三方 MOD。
