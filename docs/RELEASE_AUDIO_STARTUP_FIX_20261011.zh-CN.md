# 正式入口黑屏退出：音频启动修复

修复包：`20261011-02`。入口保持 `Stardew Valley for RGDSplus`。

## 实机原因

设备原始 `log.txt` 显示游戏资源预处理已完成，随后报错：

```text
PulseAudio: Unable to connect: Connection refused
Microsoft.Xna.Framework.Audio.NoAudioHardwareException
OpenAL device could not be initialized
Stardew Valley exited with status 1.
```

当时没有 PulseAudio 进程，也没有可连接的 `/tmp/pulse-socket`。
固件 `/etc/init.d/S50pulseaudio start` 虽然打印 `OK`，实际仍然启动失败。
详细日志停在 `a52:1` 探测；前台 `strace` 证实其加载相关原生依赖后收到
`SIGSEGV`。尚未对具体系统库做符号级归因，因此没有替换固件库。

直接打开 `hw:rockchipaw883xx` 立体声 PCM 后，PulseAudio 可以启动，
同一份游戏和双屏模组可正常进入中文标题菜单。

## 修改

- 主启动脚本在占用触摸和菜单状态之前调用 `tools/rgds-audio.py`。
- 已有可用音频服务时直接继续，保持服务 PID、音量及静音状态。
- 服务不可用时先尝试固件启动脚本，再实际检查连接结果。
- 固件启动失败且没有其他 PulseAudio 进程时，直接创建 RGDSplus 扬声器输出，
  跳过声卡自动探测，不使用空音频输出，不修改固件配置文件。
- 新恢复的服务在普通启动时解除静音；显式 UI/性能 QA 启动保持静音。
- 自定义音频服务器或已有但不可连接的服务报明确错误，不强杀其他服务。
- 每次外部命令均有超时；诊断位于 `logs/audio-service-start.txt`、
  `logs/audio-recovery.log` 和原有 `log.txt`。
- 发布构建显式包含新辅助脚本并检查其存在，保留原游戏资源和存档策略。

恢复路径当前验收的是内置扬声器 PCM；耳机、蓝牙切换尚未验收。

## 验证

1. 通过同一个正式入口脚本进行无 QA 环境启动，上下实体屏 DRM 抓图均为正常中文标题菜单。
2. 停止本次诊断创建的游戏进程和音频服务后，再从正式入口脚本启动：
   日志明确出现固件服务启动失败、直接 PCM 恢复成功；再次进入标题菜单。
3. PulseAudio 中游戏音频流连接 `rgds_speakers`，硬件 PCM 状态为 `RUNNING`。
   约两秒 monitor 采样共 85826 个样本，峰值 25362、RMS 3497.38，确认存在非静音游戏信号。
   这属于音频链路检查，不代替用户对实体扬声器的听感确认。
4. 对运行中的音频服务再次调用检测脚本，服务 PID、音量、静音状态完全一致。
5. `py -3 tools/test-audio-recovery.py -v`：9/9 通过。
6. `py -3 tools/test-release.py -v`：7/7 通过，含包校验、篡改检测、入口和资源导入测试。
7. 生成并校验 `build/releases/20261011-02/Stardew Valley for RGDSplus.zip`。

本次没有重启整台设备，也没有加载、迁移或保存真实农场。
设备端保留普通启动的标题菜单供用户继续操作。

## 证据与回退

本地证据目录：`build/launch-fix-20261011/`，包含原始日志、PulseAudio 崩溃跟踪摘要、
恢复日志、双屏截图和音频采样统计。原设备文件已保存于
`/mnt/sdcard/Ports/.backup-stardew-audio-20261012-000230/`。

回退应在退出游戏后恢复备份中的 `StardewValleyMainline.sh`、`README.zh-CN.md`、
`RELEASE.json`、`SHA256SUMS.txt`；新增辅助脚本可保留为未调用文件。
恢复前版本会失去本次音频启动保护。
