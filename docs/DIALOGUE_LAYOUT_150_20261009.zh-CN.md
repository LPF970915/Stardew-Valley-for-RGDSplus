# 对话框 150% 缩放边界修复

日期：2026-10-09。

## 范围与原因

用户报告上屏世界/下屏常驻 UI 模式下 NPC 对话左右裁切。
游戏 `DialogueBox(Dialogue)` 固定正文宽度为 1200，
普通文字对话上限为 1200 或 1240，问答框默认宽度为 1200。
150% 时有效缩放为 `1.5 × 1024 / 1366`，
一块屏幕只有约 911 UI 单位宽，故原生对话框本体已经超出屏幕。
其 `drawBox` 的角框还在左侧延伸 44、右侧延伸 40 UI 单位。

新增 `DialogueLayout` 将标准构造器、普通对话下一页和窗口尺寸刷新中的
1200/1240 宽度限制为 `min(原宽度, uiViewport.Width - 96)`，
问答构造器的宽度参数使用相同规则。发生在原生测量、换行和 NPC 分页之前，
不是把已绘制内容横向压扁或把超出文字隐藏。

在 150% 时正文宽度为 815 UI 单位，中心位置 x=48，
连同原生角框都在单屏范围内。短对话不被扩大，
100%/75% 下能够容纳的原生宽度也不被缩小。
头像、姓名、好友宝石和原生字体保留；长 NPC 对话按新文字列宽分页。

仅在双屏模组启用时生效，并保留地图截图的原生旁路。
不修改存档、游戏 DLL、UI 缩放偏好、相机、模式切换或触摸映射。
没有改变显示路由：

- `ExpandedHud`：上屏世界和对话，下屏常驻 UI。
- `SceneHud`：连续双屏世界，原生 UI/对话仍在下屏。

本次实机范围是正常 Card2 双屏入口的两种模式，不是独立旧单屏入口。

## 回归与证据

- 精确游戏程序集的 Harmony 安装、缩放、触摸、菜单、天气及标题释放保护测试通过。
- 新增 75%/100%/125%/150%、两种模式的对话宽度与角框范围检查。
- 设备实际补丁后游戏 DLL 的 587 项几何/IL 检查通过。
- 实机使用独立内存农场；不加载真实存档，不调用保存。
- 中英文 × 四个缩放档 × 两种模式 × 四类对话，共 64 组。
- 四类对话：带头像的长 NPC 对话、普通文本、文本列表、两选项问答。
- 检查框体与角框的水平/垂直边界，150% 时实际宽度 815；
  长 NPC 对话必须有头像且分页栈超过一页。
- 150% 的每组抓取 `kmsgrab --crtc 130/90` 两个实体屏幕及纵向合成图，
  人工检查中英文头像和中文问答框，没有左右裁切。
- 首轮证据：`build/dialogue-layout-20261009-013406/`。
  首轮显示测试 64/64 通过，但用 TERM 清理 QA 时出现 Wayland 原生退出错误
  （139）；不将这次外部终止视为正常退出验收。
- QA 已改为仅在内存 fixture 中调用游戏自身 `Exit()`，
  不再通过信号终止；第二轮 64/64 通过，退出码 0，没有残留游戏进程。
- 最终证据：`build/dialogue-layout-20261009-013912/`，包含结果 JSON、
  16 组两屏截图与设备日志。
- 最终设备 DLL 和本地构建 SHA256 一致：
  `e85230b044260d3a45f9c38e1a908180b3da1487f6f09d92360eea4583ff4dc9`。
- 部署前备份：`/mnt/sdcard/Ports/.backup-stardew-reference-v2-20261009-013911/`。

本地原分析 DLL 并不是启动补丁处理后的 DLL，旧几何测试对它的物理显示
补丁断言失败。取回设备实际 DLL 后同一测试 587 项通过，
未放宽或删除该断言。

## 文件与复现

- `dualscreen/VerticalWorld/DialogueLayout.cs`
- `dualscreen/VerticalWorld/DialogueRouting.cs`：安装布局补丁。
- `tools/patch-tests/Program.cs`：边框和头像文字宽度回归。
- `dualscreen/VerticalWorld/UiTestDriver.cs`：仅 QA 模式的对话 fixture 与退出命令。
- `tools/qa-dialogue-layout.py`：拒绝中断现有游戏的实机检查及物理截图。

```powershell
& tools/dotnet6/dotnet.exe build dualscreen/VerticalWorld/VerticalWorld.csproj -c Release
& tools/dotnet6/dotnet.exe build tools/patch-tests/PatchTests.csproj -c Release
& tools/dotnet6/dotnet.exe tools/patch-tests/bin/Release/net6.0/PatchTests.dll (Get-Location).Path
$env:RGDSPLUS_SSH_PASSWORD = '<设备密码>'
py -3 tools/qa-device-ui.py deploy
py -3 tools/qa-dialogue-layout.py
```

部署保留旧 DLL/manifest 备份，上传使用 `.next`、SHA256 校验和原子替换。
正常游戏无 QA 环境变量时新增 fixture/退出命令不可用。
