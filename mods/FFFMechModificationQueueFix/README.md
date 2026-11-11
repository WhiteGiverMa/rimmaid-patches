# FFF 机械体改装排程修复

独立 sidecar 补丁，面向 RimWorld 1.7 与 [Fortified Features Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3498575851)（`3498575851`）及失能机关系列（`The Dead Man's Switch II` `3819799478`、`AncientCorps` `3469398006`、`Synthriada` `3835512735`）。不修改工坊文件或存档。

## 问题现象与根因

在「机械体改装」窗口（`Fortified.Window_MechModification` 及其子类 `DMSSR.UI.Window_MechCustomization`）中，如果一次性排入两个或更多**安装同一种配件**的工作（例如：将两条「合成肌腱」分别排入左腿和右腿；或将 2 个「抛弃式陶瓷装甲」排入主体结构），点击「保存 / 应用」后，机兵只会安装其中一个就结束工作，吃掉另一个排程。

源码层面的两条失败链路：

1. **`Job.JobIsSameAs` 误判导致 `TryTakeOrderedJob` 在入队瞬间直接吞掉后续同种改件排程（100% 必现）**：
   - `Window_MechModification.RebuildAvailableItems()` 对每种改件 `ThingDef` 只取距离最近的 1 个 `Thing` 实例（`candidates[0]`）放入可选列表，因此同种改件的多个排程条目共享同一个 `entry.item`。
   - 点击「保存 / 应用」执行 `StartQueuedJobs()` 时，第 1 个排程以 `queue: false` 调用 `MechModificationOrders.OrderInstall` → `mech.jobs.TryTakeOrderedJob(job0, JobTag.MiscWork, requestQueueing: false)`，立即打断待机/休眠并成为 `mech.jobs.curJob`。
   - 紧接着第 2 个排程以 `queue: true` 调用 `mech.jobs.TryTakeOrderedJob(job1, JobTag.MiscWork, requestQueueing: true)`。
   - 原版 `Pawn_JobTracker.TryTakeOrderedJob` 第一行检查 `if (curJob != null && curJob.JobIsSameAs(pawn, job)) return true;`。
   - 由于 `Fortified.JobDriver_ApplyModification`（以及 `JobDriver_RemoveModification`、`DMSSR.Jobs.JobDriver_ApplySyntheticBionic`、`JobDriver_RemoveSyntheticBionic`）未重写 `JobDriver.IsSameJobAs(Job other)`，原版回退到比较 `def`、`targetA`（机兵自身）、`targetB`（同一个 `candidates[0]`）。即便 `Job_Modification.targetPartIndex` 不同（如左腿 vs 右腿）或是同一部位叠装第 2 层装甲，`curJob.JobIsSameAs` 都返回 `true`，导致 `TryTakeOrderedJob` 直接忽略 `job1` 并返回 `true`，第 2 个排程根本未进入 `jobQueue`。

2. **`TryResolveAndReserveItem` 整堆预留（`stackCount: -1`）与多堆/同堆分配冲突**：
   - `JobDriver_ApplyModification.TryResolveAndReserveItem` 每次固定只消耗 1 个改件（`job.count = 1`），但调用 `ReservationUtility.Reserve` 时传入了 `stackCount: -1`（整堆预留），导致 `ModificationUtility.AvailableCount` 在第 1 个任务预留后立即将整堆可用量算为 `0`。
   - 同时，当场上存在两个分开的单件改件（如两堆 `stackCount = 1` 的合成肌腱）时，`TryResolveAndReserveItem` 直接检查初始 `Item` 时未扣除同一机兵其他排队任务已占用的数量（原版 `ReservationManager.CanReserve` 的 `IsAlreadyReserved` 只认 `Claimant == pawn`，不区分同一 Pawn 的不同 `Job`），导致两个任务都绑死在第 1 个单件上；而在任务从 `jobQueue` 启动第二次调用 `TryMakePreToilReservations` 时，若回退调用 `ModificationUtility.GetCandidates`，又会把当前任务自己在入队时的预留扣掉。
   - `DMSSR` 的 `JobDriver_ApplySyntheticBionic` 同样按 `stackCount: -1` 预留且缺少备选同种物品重解析、未分配 `loadID`。

## 修复内容

1. **`Verse.AI.Job.JobIsSameAs` Postfix**：对 `Fortified.Job_Modification` 及可选的 `DMSSR` 合成义体改装任务（`Job_ApplySyntheticBionic` / `Job_RemoveSyntheticBionic`），仅在 `ReferenceEquals(__instance, other)` 时视为同一任务，防止后续排队任务被 `TryTakeOrderedJob` 吞掉。
2. **`Fortified.JobDriver_ApplyModification.TryResolveAndReserveItem` Prefix**：
   - 按实际单次消耗量 `stackCount = 1`（`maxPawns = 1`）进行预留，既阻止其他 Pawn 抢占，又让同堆剩余数量可被同一机兵的后续排队任务继续预留。
   - 计算 `AvailableCountForJob(thing, pawn, currentJob)` 时只扣除同一机兵**其他任务**（`r.Job != currentJob`）的预留量：当初始物品堆已被前序排队任务占满时自动切换到地图上最近的可达可用同种物品堆；当任务从队列正式启动第二次调用 `TryMakePreToilReservations` 时不与自身入队预留自锁。
3. **`Fortified.Window_MechModification.TryIssueEntry` Prefix**：当机兵当前正在执行改装任务时，中途再次打开改装窗口提交新排程会自动以 `queue = true` 追加到队列末尾，不会打断正在读条的改装工作。
4. **可选 `DMSSR` 兼容**：当启用 `The Dead Man's Switch - Synthriada` 时，为 `JobDriver_ApplySyntheticBionic.TryMakePreToilReservations` 提供相同的单件预留与多堆自动切换，并为 `Job_ApplySyntheticBionic` / `Job_RemoveSyntheticBionic` 补齐 `loadID` 与对象池跳过保护。

## 安装与卸载

1. 将本目录安装到游戏 `Mods/FFFMechModificationQueueFix`。
2. 在 Harmony 与 Fortified Features Framework（及 DMS 相关模组）之后启用 `whitegiver.fff.mechmodificationqueuefix`。
3. 正常重启游戏生效。卸载时直接停用即可，无存档数据残留。

## 构建与验证

```powershell
dotnet build Source/FFFMechModificationQueueFix.csproj -c Release
pwsh -NoProfile -File Validation/run.ps1
```

基线：
- 游戏：`1.7.5000 rev1275`
- `Fortified.dll`（`3498575851/1.7/Assemblies/Fortified.dll`）SHA256：`EB7B2A3AF12257C9868BF37B8DCCBF92E63CC352E9717F7730AC61AB655723B9`
- `DMSSR.dll`（`3835512735/1.7/Assemblies/DMSSR.dll`）SHA256：`D28367B8AF9E44FE6354DD78B07082BDB10984AD0B8C93343F69E29660A2964D`
- `FFFMechModificationQueueFix.dll`（`1.7/Assemblies/FFFMechModificationQueueFix.dll`）SHA256：`A798313298FE8A93687062D43E2CD87A693D8943ABCB05FE6F4695C63F7CB0AD`

真实 DLL 离线驱动 **33 项 RED/GREEN 检查通过**，且 **2026-11-12 用户已确认游戏内验证通过**，涵盖：
- RED 复现：同堆 2 条合成肌腱装左右腿第 2 个任务被 `JobIsSameAs` 吞掉、整堆预留使 `AvailableCount` 归零、同部位 2 块抛弃式陶瓷装甲第 2 个任务被吞掉、分开两堆单件不自动切换。
- GREEN 验证：同堆 2 条合成肌腱、分开两堆单件合成肌腱自动切换 `targetB`、同一部位叠装 2 块抛弃式陶瓷装甲、改装中途重开窗口追加排程、以及 DMSSR 合成义体双排与单件切换。

## 更新监测与退役条件

- 当上游 FFF 在 `JobDriver_ApplyModification` / `JobDriver_RemoveModification` 中重写 `IsSameJobAs`（或区分 `Job_Modification` 排队实例）并将 `TryResolveAndReserveItem` 改为按单件 `stackCount = 1` 且扣除同一机兵其他排队任务预留量后，可停用本补丁。
- 上游更新后可对比 `3498575851/1.7/Assemblies/Fortified.dll` 与 `3835512735/1.7/Assemblies/DMSSR.dll` 的修改时间及 SHA256，并复跑 `Validation/run.ps1`。若目标方法签名变更，补丁会打印警告并安全跳过。

## 创意工坊反馈情况与给作者的说明

- **工坊反馈检索**：在 `The Dead Man's Switch II`（`3819799478`）评论区（10月24日 9:39am，用户 `3474677760`）有人反馈过同一现象：*“机械族如果只改腿若连续应用则只会生效一次，第二次仍需手动安装。”*；在 `Fortified Features Framework`（`3498575851`）、`AncientCorps`（`3469398006`）评论区及 GitHub Issues 中尚未有人定位到具体代码原因。
- **可直接发给作者 AOBA 的反馈文案**：
  > 大佬好！反馈一个 FFF「机械体改装」窗口排程同种改件时只装第一个、吃掉第二个排程的 bug（比如同时排入两条合成肌腱装左右腿，或排入 2 个抛弃式陶瓷装甲）：
  > 1. `Window_MechModification.RebuildAvailableItems` 对同一种 `ThingDef` 只取 `candidates[0]`，所以排入的两个条目共享同一个 `entry.item`。点保存执行 `StartQueuedJobs` 时，第 1 个任务 `queue: false` 立即成为 `curJob`；第 2 个任务 `queue: true` 调 `TryTakeOrderedJob` 时，第一行 `curJob.JobIsSameAs(pawn, job)` 因为 `JobDriver_ApplyModification` 没重写 `IsSameJobAs(Job other)`，只比对了 `def`、`targetA`、`targetB`，直接返回 `true` 把第 2 个任务当成重复指令吞掉了（没进 `jobQueue`）。
  > 2. 另外 `JobDriver_ApplyModification.TryResolveAndReserveItem` 调 `Reserve` 时传了 `stackCount: -1`（整堆预留）而不是 `1`，且直接检查 `Item` 时没扣掉同一机兵其他排队任务已占用的数量（`CanReserve` 对同一 `claimant` 恒为 `true`），如果是地上分开摆的两个单件也会撞车。
  > 在 `JobDriver_ApplyModification` / `JobDriver_RemoveModification` 重写 `IsSameJobAs(Job other) => ReferenceEquals(job, other)`，并把 `TryResolveAndReserveItem` 改为按 `stackCount: 1` 预留且扣除 `r.Job != base.job` 的同机兵预留量后即可完美解决～感谢大佬的模组！
