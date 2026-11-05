# Haul Destination Trace / 搬运目的地诊断

RimWorld **1.7** 临时诊断 mod，版本 **1.0.0**，packageId `whitegiver.diagnostics.hauldestination`。仅依赖 Harmony；不依赖 RimIOT，不是 RimIOT 适配包或搬运修复。

## 用途与边界

用于定位工作台在开工前搬开物品时，`HaulToCell` 任务的目的地 `B` 为什么失效。记录真实任务创建、工作台来源、步骤切换、目标赋值、清理/对象池复用，以及寻路入口和异常出口。不能仅凭一条 NRE 或补丁列表认定责任模组。

- 所有观察 hook 都返回 `void`，不以 `ref/out` 改写原方法参数、结果或异常；不使用 transpiler，不跳过原方法，不吞掉原异常。
- 不改 Def、存档、工坊程序集、物品、路径、工作优先级或随机数。不写独立游戏文件，不通过网络发送信息。
- 正常轨迹仅保留在内存：最多 128 个任务代、每代最近 16 个事件；每局最多输出 8 个诊断报告、每代最多 2 个。缓存或嵌套上限触发会在报告标注 `trace_dropped`。
- 日志包含本机游戏对象的 defName、ID、坐标和调用方法，因此日志属于私有诊断信息，不要整份公开上传。
- 这是观测工具，不是零开销保证。定位完成后停用，不作为常驻性能 mod。

## 安装与一次复现

将本目录的 `About/`、`Assemblies/` 复制为游戏内 `Mods/HaulDestinationTrace/`，在模组列表启用并确保 Harmony 在前。不要复制整个 monorepo、Source、Validation、bin 或 obj。工具位于 `tools/`，需要明确授权后才临时安装。

下次正常启动应有：

```text
[HaulDestinationTrace] Loaded 1.0.0; diagnostic only, original jobs and exceptions are unchanged.
```

正常让联合灶台继续工作，遇到一次相同搬运报错即可。日志搜索 `[HaulDestinationTrace] report=`；原有红字依然保留，这正是没有吞异常的预期表现。捕获报告后停用该诊断 mod；无需改存档或拆建筑。已在执行中的任务若没有创建期记录，寻路异常仍会输出 `missing_creation_observation; producer_unknown`，不能把缺记录解释为「创建时没有异常」。当前任务被 `Clear()` 清空时也会保留原代及调用现场。

## 构建与离线验证

```text
dotnet build Source/HaulDestinationTrace.csproj -c Release
pwsh -NoProfile -File Validation/validate.ps1 -QaDirectory "独立临时 QA 目录"
```

.NET 10 SDK；`GameRoot` 与 `HarmonyPath` 可用 MSBuild 参数覆盖。构建不自动部署。发布时手工将 `Source/bin/Release/net10.0/HaulDestinationTrace.dll` 复制至 `Assemblies/`，并同步仓库 checksum。

Validation 使用真实 1.7 游戏 DLL、真实 Harmony 注册及原方法。仅临时 QA 副本的 ModsConfig、ModLister、BaseContent 静态初始化被隔离，避免读取真实配置或初始化贴图；`CanHaulAside` 的选格和成功寻路的引擎边界用明确 fixture 隔离，非法目标仍执行真实 GenPath 并抛同类异常。

已覆盖任务创建/赋值/清理/池复用、普通 Wait 复用不误报、活跃任务 Clear 后继续保留现场、缺创建记录的已有任务异常回退、工作台关联、步骤内清空、任务实例分叉、异常对象保留、日志重入/失败、缓存与输出上限。**不冒充游戏内烹饪、真实地图移动或本次未知根因已复现。**

## 维护与移除

基线：本机 `1.7.4990 rev1069`。游戏/Harmony 更新后先复核 `HaulAIUtility.HaulAsideJobFor`、`WorkGiverUtility.HaulStuffOffBillGiverJob`、`JobDriver.TryActuallyStartNextToil`、`Job.Clear/SetTarget`、`Pawn_PathFollower.StartPath` 与 `Game` 无参构造。注册失败会回滚本工具自己的 hooks，不改上游。

最优退役时机是拿到一次有效诊断报告。停用只移除观察代码与内存，不产生需要迁移的存档数据。未公开发布、未替代任何上游 mod。
