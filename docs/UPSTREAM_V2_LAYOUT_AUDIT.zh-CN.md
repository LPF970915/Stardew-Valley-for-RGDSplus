# 原模组 2.0 副屏布局审计

日期：2026-10-07。本文是发行二进制、代码和已有截图的审计，不代表新布局已修改或部署。

后续实施记录：本审计之后的 0.6.0-reference-v2 重构见 [Reference v2 下屏重构与实机验收](REFERENCE_V2_UI.zh-CN.md)。下文“当前”“尚未实施”均指审计时的旧 D9 版本。

## 结论

旧版游戏内 Dashboard 并未还原原模组 2.0 的副屏布局。它沿用旧 Today/Farm 信息结构，叠加本地左侧 12 行导航、全局顶栏、全局底部工具栏和通用列表→详情；当前实现已由 Reference v2 取代。

本次已追踪 APK 实际主页面分发，运行的是 StardewBackpackReferencePage、StardewCraftingReferencePage、StardewJournalReferencePage 等专用新版布局。二进制仍保留旧 FarmPage，但主分发不调用它。内部枚举 TODAY 的显示标题已经是 Backpack，不能按旧枚举名字继续设计“今日”。

## 版本与证据边界

- 用户指定 Nexus 51038、file_id=179455；在线页面返回 HTTP 403，本次没有独立核验该 file_id 的文件记录。
- 本机 Downloads 既有 Nexus ZIP 与作者 GitHub v2.0 发布 ZIP 哈希完全一致；解出的 DLL 哈希也一致。以下结论明确针对作者 v2.0 发行物。
- 作者仓库：JoeCorrell/AYN-Thor-Dualscreen-Mods。发布于 2026-08-20 23:04:48 UTC，即北京时间 2026-08-21 07:04:48。
- 游戏侧 manifest：StardewDualScreenMod，2.0.0，Profmags.StardewValleyDualScreenMod；Stardew Valley 1.6，SMAPI 最低 4.0.0。
- 配套 Android：DualScreen.Mods.apk，versionName=2.0，versionCode=20，包名 com.wemu.dualscreenmods。
- APK manifest：minSdkVersion=26（Android 8.0），targetSdkVersion=28，compileSdkVersion=35。后两者不是最低系统要求。
- README 要求 current WEMU 与 matching companion，但没有给出唯一的 WEMU 最低版本号；明确警告新 DLL 配旧 APK 会缺字段、操作或页面。
- 原设计面向 1240×1080 下屏。APK 是 Android/Compose 界面，不能当成 Linux/MonoGame 的即插即用界面库。

| 发行物 | 已核验 SHA-256 |
| --- | --- |
| StardewDualScreenMod.zip（与本地 Nexus ZIP 一致） | bc0dd16b968e74d69dc7da1e5f8282eca9e093d487e5145899d029f999347243 |
| StardewDualScreen.dll | 9f272f1f95b78a2a027e26a1648670ff3e7520e5631b4615b21da1e756587e61 |
| DualScreen.Mods.apk（本次下载） | 170579df869e476894040cf1504a3fc34dd686c75259f342d6fbc016987983fd |

证据位于 build/analysis/upstream-v2-audit/：release-v2.json 为作者发布记录原文；DualScreen.Mods.apk 为已校验下载；apk-resources/resources/AndroidManifest.xml 为版本与系统要求；dispatch/ 为禁用匿名类内联后的反编译结果。

decompiled/ 便于读 Compose 布局，但主文件匿名类反编译有错误；dispatch/ 已恢复主分发。反编译是阅读证据，不是作者可编译 Kotlin 源码；个别大方法存在还原警告/省略，不能将缺失部分当作完整源码。

## 实际路由与全局布局

主分发证据：build/analysis/upstream-v2-audit/dispatch/sources/com/wemu/session/secondscreen/StardewScreenKt$StardewScreen$8$1$1$1.java。先 PageBar，再分隔线，再在剩余空间显示专用页面；160–254 行覆盖前 11 页，之后 Settings 使用整页纵向滚动。

| 页面 | 主分发实际调用 |
| --- | --- |
| Backpack | StardewBackpackReferencePage |
| Crafting | StardewCraftingReferencePage |
| Progress | StardewProgressReferencePage |
| Map | MapCard，reference=true |
| Journal | StardewJournalReferencePage |
| Relationships | StardewRelationshipsReferencePage |
| Gift Guide | StardewGiftGuideReferencePage |
| Fields | StardewFieldsReferencePage |
| Machines | StardewMachinesReferencePage |
| Bundles | StardewBundlesReferencePage |
| Calendar | CalendarCard，reference=true |
| Settings | StardewSettings |

顶部导航：VISIBLE_PAGE_BUTTONS=3；StardewScreenKt$PageBar$2$2.java:80–84 按（可用宽度−2×间距）/3 计算等宽按钮，LazyRow 可横滑并吸附，固定左右箭头循环翻页。不是左侧永久占宽的 12 行菜单。

用户宣传图包含更多同时可见标签和独立 Skills；作者 v2.0 发布说明与实际代码已改成 3 个等宽标签，并取消独立 Skills。截图适合核对分区和视觉，但不能覆盖最终二进制的行为。

只有 Settings 在主分发中整页纵向滚动；其他页的主要分区固定，只让可增长列表、材料栏、头像栏等独立滚动。保留的 FitToPage/旧 Farm 方法不等于当前入口。

## 六张图片对应的页面差异

| 页面 | 原模组 2.0 布局和交互 | 当前 RGDS 实现的差距 |
| --- | --- | --- |
| 背包 | 左侧农场/时间/金钱/人物立绘/状态；右侧 12 列最多 3 行背包；技能和选中物品在下部；锁定行有升级说明；Sort、L/R、选择/转移/长按 | 全局顶栏与左导航占位，右边再挤 Selected/Skills 窄栏；背包仅 50×52px 槽；下方重复工具栏；没有原版分区 |
| 制作 | 顶部分类，左配方列表，右结果/描述/材料现有与需求/制作按钮，底部配方横栏 | 通用 5 行列表，点入后整区变详情，再返回；没有分类、同屏左右栏、配方轮播 |
| 社区中心 | 房间标题/进度→包标题/完成数→横向缺失物品卡；房间纵向滚动，物品横向滚动；数量品质和背包匹配 | 包平铺为通用列表，再点入缺失物品；缺房间结构与横向需求 |
| 任务 | 左选择列表与右详情同时在场，卷轴标题、描述、目标、奖励/期限/取消；底部日期/时间/已完成过滤 | 列表→详情→返回，长文本在固定盒子内缩字；不能同时看选择列表与详情 |
| 地图 | 地图占主要面积，1×–4× 缩放、0.5× 步进、有界拖动/捏合；底部农场摘要与已知村民横栏，头像点选定位 | 固定地图与农夫点；Click 对地图直接返回 false；无拖动、缩放、村民定位 |
| 关系 | 头像姓名生日、心数、每周赠礼与今日交谈分列对齐，纵向滚动 | 通用卡片拼接好感/礼物/交谈文字；无心形、勾选状态、生日、表格列布局 |

其它页面：Progress 独立显示矿井/博物馆/出货及历史；Gift Guide 是村民与礼物双栏；Fields 按地点/状态分组；Machines 同一纵向列表包括机器、果树、动物，不再是 Farm 分类切页。

可核对的尺寸规则（Android dp，不可直接当 RGDS 像素）：
- 背包左栏 128dp，右栏占剩余宽；格子同时受 12 列宽度及当前解锁行可用高度约束。见 decompiled/.../StardewInventoryKt.java:582–584、1723–1737。
- 制作宽 <700dp 或高 <520dp 启用 compact：类别高 52/62dp，底部横栏高 86/108dp，左配方栏占 37%/30%。见 decompiled/.../StardewCraftingKt.java:865–871。
- 任务左栏宽度 clamp(0.31×可用宽,150dp,300dp)。见 dispatch/.../StardewReferenceSocialKt$StardewJournalReferencePage$2$1.java:82、136–138。
- 关系三列权重 31% / 42% / 27%。见 dispatch/.../StardewReferenceSocialKt.java:1111、1209、1254。
- 社区中心纵向 LazyColumn 与横向 LazyRow。见 decompiled/.../StardewReferenceBundlesKt.java:377、1108。

## 局促感的代码根因

对照文件：`dualscreen/VerticalWorld/Dashboard.cs`、`DashboardPages.cs`；视觉验收以当前 `build/acceptance-reference-v2/` 和 `build/acceptance-visible-ui/` 为准。

1. 固定左导航宽 170/1024=16.6%；全局 78px 顶栏、底部 hotbar、重复页面标题与边框继续挤占面积。
2. 内容外框 812×570，仅占整屏 58.9%；通用列表 5 行区域 764×400，仅占整屏 38.9%。此比例描述列表占位，不表示其余区域完全无价值。
3. 背包 50×52px 槽位旁再设 120px 窄栏；底部重复同一当前 hotbar，重复信息占用了可用于大字、大格子的空间。
4. DrawRow/DrawDetail 把任务、配方、收集包统一成列表+返回，丢失各自的工作区。
5. Dashboard.Write 把字体缩小到矩形内，不是按最小可读字号换行/滚动。内容越长字越小。
6. Rect 先按 uiViewport/1024 缩放，最终输出再回到 1024；格子和导航物理尺寸基本固定。提高 UI 设定后文字仍受盒子自动缩小限制，因此 150% 不能单独解决布局问题。这与此前已修复的物品 drawInMenu 原点错位是不同问题。
7. 现在的 Today/Farm/Inventory/Skills/Cooking 等 12 页，与上表原版 12 页不是同一集合；页面数相同不能作为还原证据。

旧自动化证明部分输入、几何边界与操作能工作，不证明原模组版式相同，也不证明 4.3 英寸可读。此前“还原原模组”的表述过度。

## PC 正版资源继续作为唯一游戏美术来源

原模组本身也从运行的 PC 游戏读取美术，再编码 PNG 传给 Android。证据：build/analysis/nexus-51038/decompiled/StardewSecondScreen/ModEntry.cs:1367–1407。

| 元素 | PC 游戏运行时资源 |
| --- | --- |
| 窗口框 | Game1.menuTexture，(0,256,60,60) 九宫格 |
| 普通/选中槽 | menuTexture 标准 tile 10 / 56 |
| 实心/空心 | mouseCursors，(211,428,7,6) / (218,428,7,6) |
| 左右箭头 | mouseCursors，(352,495,12,11) / (365,495,12,11) |
| 勾选框 | mouseCursors，(227,425,9,9) / (236,425,9,9) |
| 小按钮、整理 | mouseCursors，(432,439,9,9)、(162,440,16,16) |
| 标签 | mouseCursors，y=368 的原生标签图 |
| 物品/人物/头像/地图 | 游戏内容、ItemRegistry、FarmerRenderer、NPC.Portrait、WorldMapManager 等 |

RGDS 继续使用用户安装的 PC 内容、MonoGame 与同一游戏状态；APK 仅作布局和交互参考。不需要复制 APK 游戏素材、安装 Android 客户端、添加第二份游戏或 WebSocket。保留中文游戏字体并重排；Android 原版文本大量使用 Compose/Material Typography，因此使用 PC 字体是有意适配，不能声称字体逐像素一致。

## 下一次重构的验收基准（尚未实施）

- 只改 ExpandedHud；保持连续双屏、Select、Start 模式约束、上屏世界持续更新。
- 顶部 3 个等宽标签+循环箭头；内容占剩余空间，每页专用布局；取消全局左栏与所有页面强制重复工具栏。
- 1024×768、150%、中文优先；不将 1240×1080 或 Android dp 机械缩小。文字保留最小可读尺寸，多内容用独立滚动/换行。
- 背包几何起点可为 992px 内宽=168px 人物栏+12px 间隔+812px 背包；812px 可容纳 12×64px 槽+11×4px 间隔。此为本地待验证方案，不是原模组常量。
- 任务/制作必须左列表右详情同时存在；关系用列，Bundles 用房间分组与横向需求；地图用交互画布和村民横栏。
- Progress/Gift Guide/Fields/Machines 按真实数据恢复，不拿一个改名摘要页替代。
- 保留游戏原生交易、捐赠权限与存档规则。Bundles 是追踪页，不创造远程捐赠功能。
- RGDS 多触点输入尚需单独核验；地图先保证单指拖动与 +/-，不可直接声称捏合已支持。
- 新验收增加页面结构逐项对照、长中文、12/24/36槽、长任务、多材料/房间/村民、独立滚动和输入裁剪，分开记录自动化通过与实机视觉通过。

本次仅下载核验分析客户端、生成审计和历史文档勘误；未修改运行代码、设备 DLL、存档或标题动画。
