# StartCarry Reentrancy Fix / 搬运取料重入修复

RimWorld **1.7** 独立 sidecar，版本 **1.0.0**，packageId `whitegiver.hauling.startcarryreentrancyfix`。仅依赖 Harmony，不依赖或修改 RimIOT、联合轻工或机兵模组。

## 症状与根因

工作台烹饪派生的 `HaulToCell` 任务在 `CarryHauledThingToCell` 开始寻路时，可能因目的地 B 已失效而在 `GenPath.ShouldNotEnterCell` 抛出空引用。Ref 短码会随会话/补丁栈变化，不能只按 Ref 是否相同判断。

本地运行时取证抓到：搬开任务创建时 B 有效，随后旧 `StartCarryThing` 取料回调请求把 B 写成空目标。旧烹饪回调使用 B 表示“原料”，新搬开任务却使用 B 表示“目的地”。

当前原版 `StartCarryThing` 在 `TryStartCarry(...)` 返回 0 时调用 `EndCurrentJob(Incompletable)`，但没有立即退出，仍继续修改数量/队列/目标。结束任务会同步寻找新任务，旧 Job 还可能回到对象池并被同引用复用。旧回调后续写入于是落到新任务上，导致目的地丢失。

真实 DLL 离线红检重现了“取料返回 0 → 结束旧任务 → 同一个 Job 对象复用为 HaulToCell → 旧回调清空新任务 B”。这确认了损坏后续任务的失败链；**现场最初为何取料失败仍未定因，也没有证据据此归罪 RimIOT。**

## 修复范围

只对实际编译生成的 `StartCarryThing` initAction 加一个取料调用后的提前退出：

1. 调用前记录 Job 引用、`loadID` 与当前 Driver。
2. 返回后若任务引用、代际 ID、Driver 已改变，或旧 Driver 已结束，直接停止旧回调，不结束/改写新任务。
3. 同一任务取料返回 0 时，保留原 `EndCurrentJob(Incompletable)`，然后立即退出。
4. 正常完整/部分取料继续执行原版逻辑，保留数量扣减、余量入队、预约释放、目标更新与搬运记录。

不恢复猜测的目的地、不吞异常、不改寻路可达性、物品数量、存档或仓储规则；不做 RimIOT 1.7 全功能适配，也不需要重建灶台。

## 安装与游戏浅测

游戏关闭时，将本 mod 的 `About/`、`Assemblies/` 放到游戏 `Mods/StartCarryReentrancyFix/`，启用并确保 Harmony 在前。不要把整个 monorepo、Source、Validation、bin 或 obj 放入游戏。它可以替代本次临时 `HaulDestinationTrace`；诊断取证已完成，应停用诊断而不是长期并用。

下次正常启动检查：

```text
[StartCarry Reentrancy Fix] Patched StartCarryThing initAction (failed/replaced-job early exit).
```

让原先的灶台正常完成一轮取料/做饭即可。不要求删肉、改档、重建或卸载内容模组。**当前离线通过，游戏内浅测待确认**；无新红字不自动等于全部烹饪流程已验证。

## 构建与真实方法验证

```text
dotnet build Source/StartCarryReentrancyFix.csproj -c Release
pwsh -NoProfile -File Validation/validate.ps1 -QaDirectory "独立临时 QA 目录"
```

需要 .NET 10 SDK；工程的 `GameRoot`、`HarmonyPath` 可覆盖。构建不自动部署；发布 DLL 手工同步到 `Assemblies/` 并更新 checksum。

Validation 的 bind 只隔离临时游戏 DLL 副本中 ModsConfig/ModLister/BaseContent 的静态初始化，避免读取真实配置/初始化美术；被测 StartCarryThing、JobMaker 对象池不改。取料结果和 EndCurrentJob 的同步换任务由明确 fixture 模拟，故不冒充完整地图/AI 运行。

验证覆盖：真实回调 red/green；同引用对象池新代；调用内零/非零返回的任务替换；独立 loadID/Driver/引用变化；已结束驱动；16 组完整/部分拿取及 A/B 槽、数量扣减、余量队列和预约回归；原异常对象保留；单点注册与缺失/重复调用、上游已加 return 的 IL 漂移拒绝。

## 维护与退役

基线：`1.7.4990 rev1069`，本机 Assembly-CSharp SHA256 `4e61d57205944c113631234bc55136a1a7c1679667b26008bee5425bcffa1463`。

游戏更新后复核：唯一 `<StartCarryThing>b__*` 回调、actor/Job 局部变量、唯一 `TryStartCarry(Thing,int,bool)` 与失败 `EndCurrentJob` 分支。补丁应用前后都检查形状；发现方法体变化或已加失败返回时拒绝应用并记录错误，不盲目扩大匹配。

原版若在取料失败结束任务后立即退出，并正确防止任务替换后的旧回调写入，可停用本补丁。它不添加 Def 或持久化字段，停用不需要存档迁移。未提交推送、未发布工坊，游戏 QA 状态按用户实际反馈更新。
