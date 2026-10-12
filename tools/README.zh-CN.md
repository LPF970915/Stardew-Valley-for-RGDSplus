# 开发与验证工具

`20261012-04` 已获用户手动实机反馈：基本满足游玩要求。历史自动长测的局限仍见
`docs/PERFORMANCE_FOLLOWUP_20261012.zh-CN.md`，人工反馈不等于所有场景锁定 30 FPS。

## 日常检查

在仓库根目录执行，需本地 .NET 6 SDK 和合法导入的游戏程序集：

```powershell
& tools/dotnet6/dotnet.exe build dualscreen/VerticalWorld/VerticalWorld.csproj -c Release
& tools/dotnet6/dotnet.exe run --project tools/patch-tests/PatchTests.csproj -c Release -- (Get-Location).Path
Get-ChildItem tools/test-*.py | Sort-Object Name | ForEach-Object {
    py -3 $_.FullName -v
    if ($LASTEXITCODE -ne 0) { throw "Test failed: $($_.Name)" }
}
py -3 tools/build-release.py
```

PatchTests 默认使用本地 `build/analysis/device-game.dll`；第二个参数可指定对应设备的游戏程序集。
入口/启动器回归测试在 Windows 上使用 WSL Ubuntu 的 Bash。音频、MOD 状态、退出流程、
发布完整性和性能分析测试都使用本地测试数据，不操作掌机真实存档。

## 保留的诊断工具

- `performance-probe/`：显式 QA 环境下的帧/JIT 计时、有效石块测试和像素对比；不会包含在发布包。
- `performance-probe/presentation-probe.c`：Wayland 提交/呈现反馈探针源码，需要目标平台工具链另行构建。
- `analyze-*.py`、`validate-first-use.py`：离线分析已保存数据，不启动设备。
- `qa-device-*`、`qa-first-use-matrix.py` 等已跟踪设备工具：历史 D2 布局的手动诊断工具。
  运行前核对各脚本中的设备地址和安装路径；它们不属于新发布包的安装流程。
- `native-lz4/`：原有 ARM64 联机压缩库构建与兼容性测试。

## 本地资料与清理

`build/`、各项目的 `bin/obj`、Python 缓存、游戏本体与存档均由 `.gitignore` 排除。
正式包和上一版回退包保留在 `build/releases/`，不把 ZIP 或设备私有数据提交 Git。

本次将 12 个一次性设备驱动/验收脚本及 RGA、低分辨率实验项目移至本地忽略目录
`build/archived-experiments-20261012/`；需要复现历史试验时按原相对路径恢复后再核查设备配置。
失败的节奏补丁从维护中的性能探针移除。原始证据、作废数据和包均保留在忽略的 `build/`，
历史报告中的 `build/...` 路径是本地证据索引，并非仓库附件。
