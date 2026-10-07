# Reference v2 下屏重构与实机验收

日期：2026-10-07。版本：0.6.0-reference-v2。

本版依据作者 2.0 APK 的实际 ReferencePage 分发与游戏端资源导出代码，重写 RGDSplus 的 ExpandedHud。它是 PC/MonoGame 上的 1024×768 横向适配，不是把 Android 截图缩小，也不声称与 Compose 字体逐像素相同。此前旧实现与原版的差异见 [审计](UPSTREAM_V2_LAYOUT_AUDIT.zh-CN.md)。

## 机器入口与操作

- 机器菜单：**Ports → Stardew Valley Vertical World D2 Card2**。
- 进入存档后按物理 Select 切换连续双屏与上屏世界／下屏常驻界面。
- 下屏顶部三个标签、左右像素箭头切页；横滑一次最多切一页。
- 副屏窗口模式 Start 不再重复打开菜单，连续双屏保留原生 Start。
- 两种模式默认及适配基准均为 150%。用户此后主动调整的缩放会保留。
- 旧布局配置只迁移一次：重置不兼容的页面编号和默认缩放；不迁移或改写游戏存档。

本机已有入口指向嵌套目录 `/mnt/sdcard/Ports/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline`。新干净包使用单层目录；入口兼容两者。不要另选同名旧测试入口。

## 美术依据与资源拼接

直接证据：`build/analysis/nexus-51038/decompiled/StardewSecondScreen/ModEntry.cs` 的界面素材 Piece 导出表（1367–1407 行附近），以及 `build/analysis/upstream-v2-audit/decompiled/sources/com/wemu/session/secondscreen/` 下的 StardewInventoryKt、StardewCraftingKt、StardewReferenceSocialKt、StardewReferenceFieldsKt、StardewReferenceBundlesKt。

| 原版引用 | 本版使用 | 拼接方式 |
| --- | --- | --- |
| ui:panel | Game1.menuTexture (0,256,60,60) | 外层原生窗口九宫格，源切边 20 |
| ui:button | mouseCursors (432,439,9,9) | 橙色实心按钮九宫格，源切边 3；用于标签、分类、任务／配方选项与动作 |
| ui:arrow_left/right | (352,495,12,11)、(365,495,12,11) | 原生像素箭头居中，不画文字箭头 |
| ui:scroll | (325,318,25,18) | 任务卷轴标题横向三段拼接，左右源切边 12，无纵向切边 |
| ui:slot/slot_selected | menuTexture tile 10 / 56 | 背包格与选中格 |
| ui:heart/heart_empty | (211,428,7,6)、(218,428,7,6) | 关系页好感度 |
| ui:check_off/on | (227,425,9,9)、(236,425,9,9) | 赠礼、交谈、材料齐备状态 |
| ui:sort | (162,440,16,16) | 整理按钮图标 |
| ui:tab_* | mouseCursors y=368 的 16×16 图标 | 顶部导航与相关页标题 |
| season/time | 原生季节与天气图标 | 背包摘要、底部日期 |
| 物品、人物、地图 | ItemRegistry、FarmerRenderer、NPC.Portrait、LooseSprites/map | 使用用户 PC 游戏运行时内容 |

原版通用正文色 #321B0F，辅助色 #70451F，暖羊皮纸色 #FFD98E。内容区保留轻色块和细分隔线，只有外框和按钮使用原生厚像素边。选中按钮采用浅色叠加和底部标记，保留橙色实心底。

田地页按原代码 READY → THIRSTY → WATERED 排序，使用 #19721F / #2A67A8 / #477A36 状态文字；背景使用原版带透明度的 #247DCB65 / #223E8CD1 / #18A9D38A 色值。按地点、状态、作物合并数量，并展示成熟天数范围。不是按截图猜测颜色。

没有复制 APK 内游戏素材；安装包不含 PC 资源。Android 的 Compose 字体改用游戏自带中文／英文字体。

## 4:3 重排

统一物理画布为 1024×768，内部坐标再映射到游戏 UI viewport。顶部 3×280px 标签和两侧箭头；内容区 992×630px。使用固定物理格子和独立图标绘制原点，避免界面缩放改变后道具偏离槽位。150% 为设计基准。

| 页面 | 本版结构 |
| --- | --- |
| 背包 | 左 160px 人物／日期／货币／生命体力；右 12×3 个 64px 槽位；技能横排；底部选中物品。未解锁行显示升级提示 |
| 制作 | 9 类按上游配方英文键分类，横向显示 5 类；左 6 项配方；右结果、描述、材料与制作；底部物品轮播；保留页内烹饪切换 |
| 进度 | 左矿井／博物馆／可捐赠物品；右今日出货与独立滚动；底部最近 7 天记录 |
| 地图 | 大地图、1–4 倍缩放（0.5 步进）、有界拖动；底部已认识村民头像并可定位 |
| 任务 | 左列表、右卷轴标题与独立滚动详情；奖励和二次确认取消均在当前页完成 |
| 关系 | 头像／姓名／生日、像素爱心、每周赠礼与今日交谈分列 |
| 送礼指南 | 左村民，右最爱礼物及背包匹配，各自滚动；与上游一样仅展示明确物品 ID |
| 田地 | 地点与状态分组标题、作物数量、成熟时间；轻色块和状态色 |
| 机器 | 机器、果树、动物在同一滚动列表；没有旧 Farm 分类切页 |
| 社区中心 | 房间与完成进度、收集包、横向缺失物品卡；品质、持有／需求与勾选状态 |
| 日历 | 7×4 格、生日、节日、旅行货车、选中日期摘要 |
| 设置 | 滚动选项、世界与 UI 缩放、声音、手柄吸附、原生按钮与页面显隐 |

长正文换行并独立滚动；单行过长名称省略，不自动缩成小字。背包只保留一个物品栏，没有底部重复工具栏。人物立绘使用独立动画帧，不冻结上屏农夫。

每日记录位于 Mod 自身 `companion-history/`，按存档 ID 与农夫 ID 分开，保留 28 天并显示最近 7 天。从首次观察开始，不伪造未记录的过去日期。QA 模式不写该历史文件。

## 验收方法与边界

本地：`tools/tests/GeometryTests.csproj`、`tools/patch-tests/PatchTests.csproj`。实机：`tools/qa-reference-v2.py suite`，通过受环境变量限制的测试驱动建立内存农场，实际执行触摸输入并用游戏渲染器抓取 1024×768 画面。结果与截图在 `build/acceptance-reference-v2/`。

测试覆盖 12 页真实切换、背包选择／拖放／长按／整理、12/24/36 格、中文英文 75/100/125/150% 图标定位、单次滑动边界、分类过滤、田地状态分组、每日记录去重、任务奖励与取消、地图边界、材料消耗、设置与两种模式 Start 行为。QA 不加载用户存档。

最终结果：587 项本地几何／缩放／相机／设备 IL 检查通过；Harmony 对实际设备游戏程序集安装通过，20 组动态缩放、192 个图标中心与 112 个日历边界检查通过；实机 103/103 项通过（包含页面截图尺寸检查）。中英文截图已人工查看，重点核对按钮素材、数值边界、长正文与分区。该验证不替代用户在 4.3 英寸实物上的主观可读性判断。

部署版 DLL SHA-256：`e7a1e470405b87697851cdf9ffb7c7ac7b876cf2b96e2c0e705465674752f0cc`。实机配置 LayoutVersion=2、UiScale=1.5。证据索引：`build/acceptance-reference-v2/results.json`、`deployment.json` 与 `reference-v2-contact.png`。

验收后已通过正常 Card2 入口重启，确认 Mods loaded and ready、仅一个游戏进程且环境中不存在 SDV_DUAL_UI_TEST_DIR。设备、构建产物、压缩包中的 Mod DLL 三方哈希一致。

地图本次保证单指拖动与缩放按钮，未实现多指捏合。献祭与博物馆保持追踪显示，不增加远程捐赠。历史只记录实际观察到的数据。连续双屏场景与原有标题动画不在本次改动范围内。

干净包脚本：`tools/make-clean-dualscreen-package.ps1`；包校验：`tools/verify-clean-package.py`。打包只包含运行组件、Mod 与入口，不包含游戏资源、存档、配置、个人历史、截图和测试日志。

发布包：`build/releases/reference-v2-final/stardew-rgdsplus-dualscreen-0.6.0-reference-v2-clean.tar.gz`（约 35.2 MB）。282 个文件哈希及启动权限校验通过，压缩包 SHA-256：`c059e1f92093616c5949ad70653c7a01642cc49f1db11fd10890ae376aca338c`。
