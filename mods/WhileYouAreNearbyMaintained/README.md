# While You Are Nearby - Maintained / 就近工作维护版

本地 **RimWorld 1.7** 维护分支（forked），版本 **1.0.0**。替代 pureMJ 的 While You Are Nearby 0.8.0，不是叠加补丁。保留原 packageId `PureMJ.MjRimMods.WhileYouAreNearby`，依赖 Harmony；不改创意工坊原文件、不添加 Def 或存档字段。

## 本版变更

1. 格子候选先比较距离平方，再检查禁区、工作可用性和可达性，避免对半径外或已经不够近的格子进行昂贵验证。
2. 替换出的 Job 补上正确 `workGiverDef`，使工作取消与儿童观摩等原版消费者能识别工作来源。
3. 仅在新 Job 成功生成并替换结果后，把弃用的旧 Job 交回原版 `JobMaker` 池；同引用、当前任务、驱动持有、队列成员、FromQueue 结果不回收。
4. 移除三个空的全局 GenClosest 调试 Postfix。运行时只给 `JobGiver_Work.TryIssueJobPackage` 注册一个 Postfix。
5. 使用 .NET 10 / Harmony 3 / UnityStubs 构建，明确标记 1.7；保留原设置字段、类名与三种语言。

没有创建线程、Task、并行搜索或自定义工作调度。代码仍在原版工作分配调用上下文中使用原版检查、搜索与对象池。1.7 的其它并行子系统继续由原版管理；这不表示工作生成 API 可以从任意线程调用。

## 保留的规则与限制

- 默认远距阈值 **32**，附近半径 **12**，允许优先级差 **1**，随机保留原版 **5%**。
- 保留远距离/附近工作排除列表及其开关；殖民者与受控机械体范围、紧急任务/优先工作/精神状态排除均不变。
- 保留原先的 nearest-first、扫描顺序、距离评分、thing/cell 分支规则。不擅自调整研究台速度优先、携带物、subhuman 工作覆盖等行为。
- 仍是原版找到远处工作后的第二次搜索，仍须枚举 scanner 提供的候选。过滤没有消除枚举器内部的种植区遍历或可达性检查，也不是 TPS 收益承诺。
- 不替 scanner 缓存或重新枚举候选，不改寻路算法；保留 grower 枚举器的逐区状态生命周期。
- 其它补丁若私自保留已经被替换、且不属于当前/驱动/队列的旧 Job 引用，超出原版所有权契约，不能保证兼容。不要同时启用第三方 While You Are Nearby Performance Fix，它的预过滤已包含在本版中。

## 安装与设置继承

将本目录的 `About/`、`Assemblies/`、`Languages/`、`README.md`、`NOTICE.md` 复制到 `RimWorld/Mods/WhileYouAreNearbyMaintained/`，不要复制 Source、Validation、bin、obj 或工坊 PublishedFileId。

本地与工坊包使用相同 packageId。原版会保留本地 plain ID，把工坊副本标为 `_steam`；当前列表只启用 plain ID 时，本地维护版接替原包，原排序不变。**只启用一个版本，勿手工同时启用本地与工坊副本。** 若已有其它同 ID 本地版本，先处理冲突再安装。

设置文件按“目录名 + Mod 类名”而不是 packageId 命名：

```text
旧：Config/Mod_2784585275_ModMain.xml
新：Config/Mod_WhileYouAreNearbyMaintained_ModMain.xml
```

首次安装时可复制旧文件到新路径；已有新设置时不覆盖。保留 `MjRimMods.WhileYouAreNearby.SimpleSettings` 类型名及全部 Scribe 键，数值和启用的 Def 排除列表可原样读回。保持原版延迟加载，避免定义尚未就绪时加载 Def 列表。旧设置文件不删除、不覆写。

正常重启后检查加载行：

```text
[While You Are Nearby Maintained 1.0.0] Loaded: distance-first cells, work giver metadata, discarded-job pooling; vanilla scheduling.
```

最小浅测：进入原存档后打开本模组设置，确认 32 / 12 / 1 / 5 和两种排除列表可用；日常种植/建造照常观察即可，不必制造复杂场景。当前离线验证通过，真实设置窗口绘制与自然工作联测仍待游戏浅测，不把“无红字”当作全部行为已验证。

回退：退出游戏，将 `Mods/WhileYouAreNearbyMaintained/` 移出 Mods，保留工坊订阅；原 plain packageId 会重新指向工坊版。原设置文件仍在，不需要转换存档。不要运行中移除正在加载的程序集。

## 构建与验证

```text
dotnet build Source/WhileYouAreNearbyMaintained.csproj -c Release
pwsh -NoProfile -File Validation/validate.ps1 -QaDirectory "独立临时目录" -LegacyDll "原作1.6/Assemblies/WhileYouAreNearby.dll"
```

`GameRoot`、`HarmonyPath` 可覆盖。构建不部署，发布 DLL 需从 `Source/bin/Release/net10.0/` 显式同步到 `Assemblies/`。

离线驱动调用实际新旧 DLL：原版对照 24 检查、维护版 46 检查，包含 10,002 候选的验证调用 10,002 → 2、相同目标、来源字段、真实 Notify→EndCurrentJob→Cleanup→ReturnToPool、同引用/当前/驱动/队列/FromQueue/null/异常所有权、原版 GenClosest 全局分支、原生 Scribe 数值/Def 排除列表及旧设置读回、实际 Mod 构造的延迟设置加载、唯一补丁入口和经 Harmony 包装后的实际入口调用。

QA 仅改临时游戏 DLL 副本的 ModsConfig/ModLister/BaseContent 静态初始化器；原工作方法、JobMaker 池和 Scribe 方法体不改。人物身份/权限、禁区、地图可达性与下一次 rethink 等世界边界由 fixture 隔离。它不是整局 AI、区域 BFS、真实地图寻路、GUI 或 TPS 基准。旧版 24 项是缺陷行为的对照记录，不代表旧版满足修复后断言。

## 维护与退役

基线原作 DLL SHA256：`c1458b90b3091b9a7463121072dd40d13e9c750151bc2f51351341b3f58bfee4`。游戏基线：`1.7.5000 rev1275`，Assembly-CSharp SHA256 `d55aa8e47fa3c5463ca85b0eccd51f195b4bbcd04f56da3af21c2f6688a81254`。

工坊更新不会覆盖本地版，也不会自动合入。可比较原 DLL 修改时间或 SHA256，发现变化时先审查上游；游戏更新后复核 TryIssueJobPackage/PawnCanUseWorkGiver 签名、ThinkResult 来源、GenClosest、JobMaker.Clear/队列所有权、设置 Scribe 键与加载时机，重跑离线驱动。作者原生合入这些修复并通过同类验证后，可回退上游并退役本版。

来源、forked 声明与作者撤回条款见 [NOTICE.md](NOTICE.md)。许可证未定，作者要求时立即删除。

## English summary

Local RimWorld 1.7 maintenance fork of pureMJ's While You Are Nearby 0.8.0. Same package ID; use either the local or Workshop version, not both. Filters cell distance before expensive checks, preserves replacement job work-giver metadata, safely returns discarded unowned jobs to vanilla's pool, and removes three empty global hooks. Original defaults and nearby-work policy remain unchanged. Uses native scheduling and APIs without creating threads. Source/validation builds do not deploy. Copy the old settings file to the local folder-based settings filename only if the latter does not exist. Offline verification is not an in-game performance benchmark or GUI test. See [NOTICE.md](NOTICE.md) for fork provenance: no license is declared, and the original author may request removal at any time.
