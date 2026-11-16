# DMS Synthriada — Scourge Death Fix / 天灾重复死亡修复

**1.0.0 · WhiteGiverMa · RimWorld 1.7**  
Package ID: `whitegiver.dmssr.scourgedeathfix`

独立 Harmony sidecar，修复天灾复活后再次死亡时的 `set dead while already dead.` 与 `Corpse_DMS_Mech_Scourge… spawned in bugged state.`。同时修复 Fortified 人形机械体在 `Kill(null)` 中移除掉落植入物造成的同类重入。没有设置、Scribe 字段或存档组件。

Standalone Harmony sidecar for reentrant Scourge death and empty-corpse errors, including the related implant-removal path in Fortified's `HumanlikeMech.Kill(null)`. No settings or saved state.

## 原因 / Why it happens

1. 致命伤经 `DamageWorker_AddInjury → AddHediff → CheckForStateChange` 调用天灾的 `HumanlikeMech.Kill`。这时 `ShouldBeDead()` 已为 true，但 Pawn 尚未进入 Dead 状态。
2. `HumanlikeMech.Kill` 调用基类 `Pawn.Kill` 时，Synthriada 的 `DMSSR.Content.Patch_ScourgeNextDeath.Prefix` 先移除 `DMSSR_BornFromDeath`。原版只在 **Kill 方法体内部**设置 `health.isBeingKilled`，所以此前的前缀尚未受到保护。
3. `RemoveHediff` 正常执行移除回调、缓存更新，然后调用 `CheckForStateChange(null, hediff)`。致命伤仍在，`Dead` 和 `isBeingKilled` 又均为 false，于是再次调用虚方法 `Kill(null, hediff)`。
4. 内层死亡先完成，Pawn 被放入第一具尸体。外层恢复执行后再次 `SetDead`，报第一条错误；接着把 Pawn 从第一具尸体移出，装入第二具尸体。
5. 第二具尸体在同一格生成，真实 `GenSpawn.Spawn` 的**物品格已满分支**将第一具移至附近。第一具此时已为空，重新进入 `Corpse.SpawnSetup` 就报第二条错误。这解释了堆栈中嵌套的 `GenSpawn → GenPlace → GenSpawn`。空尸体在基类 SpawnSetup 前就返回，结果是一个未生成、未销毁的空对象，而非已经登记到地图的“半生成尸体”。

The Scourge's pre-death Harmony prefix removes its resurrection buff before vanilla `Pawn.Kill` sets the reentrancy flag. Removal immediately rechecks the still-fatal injury and recursively calls `Kill`. The inner death creates corpse A; the outer death repeats `SetDead`, takes the pawn out of A, and creates corpse B. Spawning B moves A out of the occupied item cell, causing the now-empty A to fail its real `Corpse.SpawnSetup` check.

`Fortified.HumanlikeMech.Kill` 另有同样的窗口：`dinfo == null` 时，在 `base.Kill` 前逐个 `RemoveHediff`，随后生成其 `spawnThingOnRemoved` 物品。重入死亡会先让 Pawn 离图，还额外导致掉落物尝试放入 null map。此路径不限于天灾。

Fortified has the same window when null-damage death removes implants before `base.Kill`. Reentrant death also despawns the pawn before the implant drop, producing a null-map placement error.

核对的 1.7.5005 rev1082 中，`Pawn.Kill` 不以 `isBeingKilled` 做入口检查；它在方法体开头设 true、正常尾部设 false，**没有负责恢复此标志的 finally**。

## 修复方式 / Mechanism

仅对两个调用者做 Harmony transpiler：

- `DMSSR.Content.Patch_ScourgeNextDeath.Prefix`
- `Fortified.HumanlikeMech.Kill`

每个方法中唯一的 `RemoveHediff` 调用被替换为一个短作用域包装：保存原 `isBeingKilled`，临时设 true，调用原版 `RemoveHediff`，最后在 `finally` 中恢复原值。`PreRemoved`、`PostRemoved`、缓存与图像刷新仍正常执行；只是移除末尾的状态检查不再启动第二次死亡。

Each transpiler replaces exactly one removal call with a scoped wrapper. It temporarily raises `isBeingKilled`, calls the original removal, and restores the previous value in `finally`. All removal callbacks and cache/graphics invalidation remain intact. No global health patch or replacement death implementation is used.

复活、次数消耗、掉落规则不变。其他 `Pawn.Kill` 前缀执行时看到的仍是正常标志；取消死亡或抛异常不会把本补丁的临时标志留在 Pawn 上。取消死亡时是否失去 buff 保持上游行为，不额外改变护盾/能力规则。

Resurrection, charge consumption and drops remain upstream-owned. Other death prefixes see the normal flag state; cancellation and exceptions do not leak this wrapper's temporary flag. Buff removal on cancelled death retains upstream semantics.

## 离线验证 / Offline validation

2026-11-14，使用指定的真实 1.7 bound `Assembly-CSharp.dll`，直接加载未修改的 Workshop `Fortified.dll`、`DMSSR.dll` 与 Harmony。另以 Cecil 比较安装版与 bound 版的死亡、健康检查、尸体、放置及复活方法 IL，确认一致。

| 场景 / Scenario | RED（无补丁） | GREEN（有补丁） |
|---|---|---|
| NPC 首死 → 自动排程复活 → 致命枪伤再死 | 2 Kill、2 尸体、2 条目标错误 | 1 Kill、1 有效尸体、0 Log.Error |
| 玩家首死 → 主动排程复活 → 再死 | 同上 | 同上 |
| 再次外部复活 → 第三次死亡 | — | buff 重新应用后正常移除，1 有效尸体、0 错误 |
| 天灾及其他 HumanlikeMech，致命状态下 `Kill(null)` + 掉落植入物 | 重复死亡、空尸体、null-map 掉落错误 | 正常回调、恰好一件掉落、1 有效尸体、0 错误 |
| 死亡取消/抛异常（先/后于上游前缀）、移除回调抛异常、原标志为 true | — | 标志正确恢复或保留 |
| buff 与植入物同时存在；死亡之外正常移除 Hediff | — | 两处保护可组合，正常健康检查不受影响 |

RED **54** 项断言通过（确认原错误）；GREEN **88** 项通过；补丁先于 DMSSR 安装的 GREEN-FIRST **88** 项通过。Release 与验证工程均 `net10.0`、`TreatWarningsAsErrors=true`，**0 warnings / 0 errors**。

The harness runs real injury finalization, health checks, the Fortified override, all four DMSSR `Pawn.Kill` patches, corpse creation/ownership, placement, and resurrection/tracker code. A small headless map replaces rendering/UI/story notifications and engine spawn/despawn bookkeeping; `ThingOwner`, `ThingGrid`, `GenPlace`, `GenSpawn` and the corpse validity check remain real.

已检查其余三处 DMSSR 死亡补丁：Schizophrenia 前缀只记录标记、后缀对其他链接单位执行死亡；ContentDeathDrops 前缀只记录 Dead，掉落在后缀；Hydra 的还原在 Dead 后缀执行，其移除瘫痪发生于另一目标身上。它们没有在此次死亡的前缀中移除当前 Pawn 的 Hediff，因此不扩大修补范围。

### 重跑 / Re-run

```powershell
pwsh -NoProfile -File .\Validation\validate.ps1
```

脚本构建 Release，并在三个独立进程中运行 RED、GREEN、GREEN-FIRST。可通过 `-BoundGamePath`、`-GameRoot`、`-WorkshopRoot`、`-LogDirectory` 指定本机路径；默认 bound 引用为相邻 DMSEWorldPerfFix 验证工程准备的 DLL。不会读写 `ModsConfig.xml`、存档或 Workshop 文件。

The script builds Release, then runs three isolated processes. Paths are overridable with the parameters above; the default bound DLL is supplied by the neighbouring DMSEWorldPerfFix validation setup. Runtime files are only `About/About.xml`, `LoadFolders.xml`, `1.7/Assemblies/DMSSRScourgeDeathFix.dll` and this README.

### 已核对依赖 / Dependency fingerprints

| 文件 / File | SHA256 |
|---|---|
| bound Assembly-CSharp.dll | `20FB0D97A55AB0568870BA3CCF6CBC28B861952B39AC82276A81DA81D6DCEBF4` |
| Fortified.dll | `9C135095C0A389AE63C08A514B2751B9B168BDC125AEA9DA8D303F81F8B0E952` |
| DMSSR.dll | `19714AC9ACD16D78D093FD7B973B5E5D05397794986E82ABD89557F4A0FB1033` |

## 安装与退役 / Install and retire

将运行文件置于 `RimWorld/Mods/DMSSRScourgeDeathFix`，启用并排在 Harmony、DMS Core、Synthriada 后。没有新增存档类型，可在关闭游戏后添加或移除；不会修复已经遗留的空尸体。

Enable after Harmony, DMS Core and Synthriada. No serialized types are added, so the sidecar can be added or removed between game sessions. Existing orphaned corpses are not repaired.

DMSSR、Fortified 或 RimWorld 更新后，先比较依赖哈希并重跑验证。上游若已安全处理这两个死亡前移除窗口，应退役相应补丁；两处均修复后移除本模组。目标方法签名或唯一调用点改变时，transpiler 会明确报错要求复核，而不会猜测新 IL。

Revalidate when dependency hashes change. Retire the affected patch when upstream guards or relocates its pre-death removal; retire the mod when both paths are fixed. A changed method signature or removal-call count requires review.

## 给 Cephalopod 的反馈 / Note for Cephalopod

> 你好！谢谢你制作 Synthriada，天灾的复活设计很有趣。我在复活后的第二次死亡中复现了一个小重入问题：`Patch_ScourgeNextDeath.Prefix` 的 `RemoveHediff` 会在 `Pawn.Kill` 尚未设置 `isBeingKilled` 时重新检查致命伤，结果同一 Pawn 跑完两次死亡，出现重复 SetDead 和空尸体生成报错。给这次移除加一个保存/恢复 `isBeingKilled` 的 try/finally 短作用域就能避免重入，并保留所有回调。另发现 Fortified 的 `HumanlikeMech.Kill(null)` 在 base.Kill 前移除掉落植入物时也有同类窗口，可能值得一起转给 Fortified 作者。已用真实 1.7 DLL 做了独立 RED/GREEN：原版可复现两条报错，保护后只产生一具有效尸体，复活次数和掉落保持正常。欢迎直接取用这里的实现和验证用例，希望能帮你省一点排查时间！

> Hi Cephalopod! Thanks for Synthriada — the Scourge's resurrection is a lovely mechanic. I reproduced a reentrant second death: `RemoveHediff` in `Patch_ScourgeNextDeath.Prefix` rechecks the fatal injury before vanilla `Pawn.Kill` raises `isBeingKilled`. The inner death finishes first, so the outer death repeats SetDead and empties the first corpse. A small try/finally scope that saves and restores the flag around this removal prevents the duplicate death while preserving callbacks. Fortified's implant removal before `base.Kill` has the same issue for `Kill(null)`, which may be useful to pass along to its author. Isolated tests with the real 1.7 assemblies reproduce both original errors and pass with one valid corpse after the guard, including resurrection charges and drops. Please feel free to reuse the implementation and tests — I hope they save you some debugging time!
