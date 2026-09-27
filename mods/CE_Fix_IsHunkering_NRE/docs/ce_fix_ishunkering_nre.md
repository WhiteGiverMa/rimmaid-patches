# CE Fix: IsHunkering NRE + TendSelf Reservation（v1.2 收窄版）

**日期**: 2026-09-27
**补丁版本**: 1.2（收窄；原 1.1，2026-07-05）
**适用 CE 版本**: 16.7.3.0 (RW 1.6)
**活动 CE 基线**: 本机 `CombatExtendedLocal`（`G:/dev/CombatExtended`，HEAD `6a0bc4ed1`），已包含下面两个上游修复；工坊 CE（2890901044）尚未包含（2026-09-27 反编译核实）。

## 为什么收窄

v1.1 的两个 hook 在活动 CE 上均为冗余，且各自带来真实风险：

1. **Fix 1 完整遮蔽 CE 方法**：v1.1 用 Prefix 完整替代私有 `CompTacticalManager.TryGiveTacticalJobs()`
   （`return false` 永不执行原版）。当前 CE 已在 `CompSuppressable.IsHunkering` 使用
   `pawn.CurJob?.def`（本地 fork commit `68349a5fc`，上游 PR
   [#4652](https://github.com/CombatExtended-Continued/CombatExtended/pull/4652)），替代逻辑纯冗余；
   一旦上游改动该方法（新增战术 comp、调整检查顺序等），v1.1 的陈旧副本会继续独立运行 → 行为漂移。
2. **Fix 2 提前抑制撤退**：v1.1 的 Prefix 在 `CompTend.TryGiveTacticalJob()` **所有逻辑之前**
   检查“是否被其他 pawn 预约”，命中即返回 null。但当前 CE（commit `1d7a420c0`，上游 PR
   [#4653](https://github.com/CombatExtended-Continued/CombatExtended/pull/4653)）的预约检查位于
   `CompTend.cs:76-89`，而**遇敌逃进掩体分支在 `CompTend.cs:71-74` 更早**：
   `return SuppressionUtility.GetRunForCoverJob(SelPawn);`。
   因此 v1.1 会让“被预约且本该撤退”的 Pawn 也拿不到撤退 job —— 这是行为回归，而非等价冗余钩子。

## v1.2 修复方式（窄化后的两个钩子）

| # | 精确目标（Harmony 补丁） | 类型 | 补丁方法 | 行为 |
|---|---|---|---|---|
| 1 | `CombatExtended.CompSuppressable` 的 `IsHunkering` 属性 getter（`get_IsHunkering`） | **Finalizer** | `Patch_IsHunkering.Finalizer_IsHunkering` | 仅当 getter 抛出 `NullReferenceException` 时吞掉异常并令结果为 `false`（“不蹲伏”）；其他异常原样返回给 Harmony 重抛；CE 已修复时不抛异常 → **永不触发（inert）**。 |
| 2 | `CombatExtended.AI.CompTend.TryGiveTacticalJob()` | **Postfix** | `Patch_IsHunkering.Postfix_CompTend_TryGiveTacticalJob` | 仅当返回值已构造为 `CE_JobDefOf.TendSelf` 且 `SelPawn` 被其他 pawn 预约（`ReservationManager.ReservationsReadOnly` 中 `Target == SelPawn && Claimant != SelPawn`）时，把结果置 `null` 并同步 `lastTendJobCheckedAt` 冷却。**在原方法之后执行**：逃进掩体返回的 `GetRunForCoverJob`（非 TendSelf）永远原样通过；此处从不调用 `MakeJob`/`StartJob` → 不会重复分配 job。 |

**不再 patch** `CompTacticalManager.TryGiveTacticalJobs`（私有）；不再使用任何会在原方法体之前改变控制流的 Prefix。

行为矩阵：

| 场景 | 活动本地 CE（含修复） | 工坊 CE（未含修复）+ v1.2 | v1.1（旧） |
|---|---|---|---|
| `pawn.CurJob == null` 的 IsHunkering 恢复路径 | `CurJob?.def`，无异常 | Finalizer 降级为 false 并 `WarningOnce` | Prefix 完整替代方法体 |
| 被 TendPatient 预约 + 无敌人（本想 TendSelf） | 内部 guard 返回 null | Postfix 置 null | 提前 Prefix 置 null（等价） |
| 被 TendPatient 预约 + 有敌人（本该逃进掩体） | `GetRunForCoverJob` 正常返回 | `GetRunForCoverJob` 原样通过 | **Prefix 提前拦截 → 撤退被抑制（bug）** |
| 未来上游改动 `TryGiveTacticalJobs` | 无补丁，无漂移 | 无补丁，无漂移 | **被陈旧副本遮蔽（bug）** |

## 验证（正确缝：真实 CE 程序集上的 Harmony 拓扑 + 补丁入口行为）

`Validation/validate.ps1` 在进程内加载 `Assembly-CSharp.dll`、活动 `CombatExtended.dll`、
`0Harmony.dll` 与被测补丁 DLL，像 RimWorld 一样执行 `[StaticConstructorOnStartup]` 静态构造，
再用 `Harmony.GetPatchInfo` 检查精确目标/补丁类型，并反射执行 finalizer/postfix 的可无游戏状态路径。
（RimWorld 的 MonoMod 版 Harmony 无法在 CoreCLR 初始化共享状态，脚本会自动转交 Windows PowerShell 5.1 运行。）

```powershell
# GREEN：仓库内 v1.2 DLL
pwsh -NoProfile -File mods/CE_Fix_IsHunkering_NRE/Validation/validate.ps1

# RED：先取出 v1.1 DLL 再跑同一脚本（预期 6 项 FAIL，退出码 1）
git -C <repo> show HEAD:mods/CE_Fix_IsHunkering_NRE/1.6/Assemblies/CE_Fix_IsHunkering_NRE.dll > old.dll
pwsh -NoProfile -File mods/CE_Fix_IsHunkering_NRE/Validation/validate.ps1 -PatchDll old.dll
```

- **RED（v1.1 DLL `d8f9b19d...5adbc4`）**：`CompTacticalManager.TryGiveTacticalJobs` 被
  `Prefix_TryGiveTacticalJobs` 遮蔽；`CompTend.TryGiveTacticalJob` 存在提前 Prefix
  `Prefix_CompTend_TryGiveTacticalJob`；无 finalizer/postfix；共 6 项 FAIL。
- **GREEN（v1.2 DLL `a769ff83...de60f`）**：`TryGiveTacticalJobs` 无任何 sidecar 补丁；
  `IsHunkering` getter 恰有 `Finalizer_IsHunkering`；`CompTend.TryGiveTacticalJob` 恰有
  `Postfix_CompTend_TryGiveTacticalJob` 且无 Prefix；finalizer NRE→false / 其他异常重抛 /
  无异常直通；postfix 对 null、非 TendSelf、无 map/pawn 状态均 no-op 或不抛；19 项全 PASS，
  退出码 0。同一 GREEN 也对工坊 CE 程序集跑通（证明未修复 CE 上补丁同样可应用）。
- 未覆盖（诚实边界）：预约命中分支需要 Verse `Map`/`Pawn` 活动状态，无法离线执行；
  该分支代码与 CE 内部已合入的 guard 逐行同义（`CompTend.cs:76-89`），且以 Postfix 形式存在，
  控制流上不可能抑制其之前的逃进掩体分支。
- 未启动 RimWorld、未做游戏内浅测。

## 构建与产物

```powershell
dotnet build mods/CE_Fix_IsHunkering_NRE/Source/CE_Fix_IsHunkering_NRE.csproj -c Release
# 可覆盖引用：-p:CombatExtendedDll=... -p:HarmonyDll=...
```

- `Source/Patch_IsHunkering.cs` — v1.2 源码（2 个钩子）
- `Source/CE_Fix_IsHunkering_NRE.csproj` — 引用路径可用 `-p:CombatExtendedDll` / `-p:HarmonyDll` 覆盖
- `Validation/Driver.cs` + `Validation/validate.ps1` — 上述验证
- `1.6/Assemblies/CE_Fix_IsHunkering_NRE.dll` — 发布 DLL，SHA-256
  `a769ff835024e4be2a897bc9e6cacb1bd2955a68adcd538457d20625888de60f`（v1.1 为
  `d8f9b19d6160e5f4ec540bb82130e93a4e5c5b5dd9fc54f51f0afed8ad5adbc4`）

## 部署状态与时效性

- 本文件更新的是仓库真源；**游戏 Mods 目录与 ModsConfig 由父级部署，未动**。部署前游戏内仍是 v1.1。
- 活动 CE（本地 fork `0249E0F0...274D` 对应 DLL）已含两修复 → v1.2 为惰性安全网；
  若切回工坊 CE（当前未含修复），v1.2 的两个钩子会以窄方式生效。
- 判断失效/是否还需要：反编译活动 `CombatExtended.dll` 检查
  `CompSuppressable.IsHunkering` 是否含 `CurJob?.def`、`CompTend.TryGiveTacticalJob` 是否含
  `ReservationsReadOnly` 检查；两者都在时本 mod 可安全停用（惰性 ≠ 无意义，它不再改变任何行为）。
  上游 PR：[#4652](https://github.com/CombatExtended-Continued/CombatExtended/pull/4652)（IsHunkering）、
  [#4653](https://github.com/CombatExtended-Continued/CombatExtended/pull/4653)（预约冲突）。
