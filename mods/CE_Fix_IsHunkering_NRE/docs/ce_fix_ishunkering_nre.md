# CE Fix: IsHunkering NRE + TendSelf Reservation

**日期**: 2026-07-05
**补丁版本**: 1.1
**适用 CE 版本**: 16.7.3.0 (RW 1.6)

## Fix 1: IsHunkering NRE

### 问题

战斗中出现红字：
```
Exception ticking Pawn: System.NullReferenceException: Object reference not set to an instance of an object
  at CombatExtended.CompSuppressable.get_IsHunkering()
  at CombatExtended.CompTacticalManager.TryGiveTacticalJobs()
  at CombatExtended.CompTacticalManager.CompTickRare()
```

### 根因

`CompSuppressable.IsHunkering` 属性中 `pawn.CurJob.def` 在 `pawn.CurJob` 为 null 时引发 NRE。

### 修复

在 `CompTacticalManager.TryGiveTacticalJobs` 加 Harmony Prefix，对 `IsHunkering` 调用做 try-catch 保护。

### 上游状态

已提交 PR：[#4652](https://github.com/CombatExtended-Continued/CombatExtended/pull/4652)

---

## Fix 2: TendSelf vs TendPatient Reservation 冲突

### 问题

战斗中频繁出现红字刷屏：
```
Could not reserve target pawn for a TendSelf job
Existing reserver is performing TendPatient on the same pawn
```

### 根因

CE 的 `CompTend.TryGiveTacticalJob()` 为 pawn 分配 TendSelf 任务时，不检查该 pawn 是否已被其他 pawn 预约（如正在被 TendPatient 治疗）。两个 job 都通过 `JobDriver_TendPatient.TryMakePreToilReservations` 尝试 `pawn.Reserve(Deliveree, ...)` 预约同一个 pawn，由于 `maxPawns: 1`，第二个预约失败并输出 `LogCouldNotReserveError` 红字。

调用链：
```
CompTacticalManager.CompTickRare()
  → TryGiveTacticalJobs() [CE_Fix Prefix]
    → CompTend.TryGiveTacticalJob() → 创建 TendSelf job
    → SelPawn.jobs.StartJob(job, InterruptForced)
      → JobDriver_TendPatient.TryMakePreToilReservations(errorOnFailed=true)
        → pawn.Reserve(Deliveree, ...) → 冲突! → Log.Error()
```

### 修复

在 `CompTend.TryGiveTacticalJob` 加 Harmony Prefix，创建 job 前遍历 `ReservationManager.ReservationsReadOnly`，如果目标 pawn 已被其他 pawn 预约，则跳过本次 TendSelf 分配并更新冷却计数器。

### 上游修复（CE 本体）

同类修复也曾应用到本地 Combat Extended fork；本 sidecar 不依赖该 fork 才能生效。

---

## 文件

- `1.6/Assemblies/CE_Fix_IsHunkering_NRE.dll` — 编译好的补丁
- `Source/Patch_IsHunkering.cs` — 源码（含 Fix 1 + Fix 2）
- `Source/CE_Fix_IsHunkering_NRE.csproj` — 项目文件
