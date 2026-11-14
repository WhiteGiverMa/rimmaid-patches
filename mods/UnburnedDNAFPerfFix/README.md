# Unburned + Do Not Avoid Fire Performance Fix

**1.0.0 · RimWorld 1.7 · `whitegiver.unburned.dnaf.perffix`**

减少无火、局部火灾和战斗地图中的无效待命扫描，修复密封装备缓存，并为加载中的 Do Not Avoid Fire 提供安全的寻路网格实现。加载顺序：Harmony → Do Not Avoid Fire（可选）→ Unburned → 本补丁。

Reduces unnecessary idle scans on peaceful, burning and combat maps, replaces the sealed-apparel cache, and repairs the optional Do Not Avoid Fire path-grid integration. Requires Harmony and Unburned; DNAF is detected at runtime. No custom save fields.

## 审计 / Audit — 2026-11-14

### Unburned

`Pretty_Patch.Prefix` 基本复制原版 `CheckForAutoAttack`，再用 `PawnShouldBeatFire` 改变灭火意愿。每次合法待命检查仍遍历 9 格；即使鸡不会灭火、地图没有火或火在远处，扫描照常执行（325×325、333 pawn 含 82 只鸡的雨天殖民地 DPA 采样为 0.557 ms/frame）。

The whole assembly was inspected: the wait prefix, flammability worker, both vacuum-resistance postfixes, settings/UI/default generation and startup. The vacuum/stat explanations continue to use upstream logic. `sealedCache` strongly retains every queried pawn, including animals without apparel, and concurrent dictionary writes are unsafe. Its refresh is `900 + pawn.GetHashCode() % 63` ticks.

### Do Not Avoid Fire 1.6 on 1.7

源文件与实际 DLL 全部核对；真实 `HarmonyInit` 在离线宿主执行，其产生的原始/补丁后 IL 已导出。结果不是“所有 transpiler 都坏了”：

| Patch | Actual 1.7 result / 实测结果 |
|---|---|
| `Fire.SpawnSetup` / `DeSpawn` | Signatures still match. Loading uses `Map` load initialization → `GenSpawn.Spawn(..., respawningAfterLoad: true)` → `Fire.SpawnSetup`. **读档并非必然漏计火。** Raw `firecount += amount` is not idempotent; an unmatched remove reproduces −1. Parent changes and multiple fires in one cell also defeat a simple bool/count pair. |
| `PersistentDangerSource.ComputeAll` | Local **0** is still the cell index. The transpiler runs and records fire-only danger, but ignores `TerrainDef.dangerous`. |
| `PersistentDangerSource.UpdateIncrementally` | Local **2** is still the cell index. Branches execute correctly, but “fire-only” state can remain stale when no dangerous thing is encountered. |
| `PathGrid.CalculatedCostAt` | Local **0** remains the cost accumulator; the `ldc.i4.s 9` loop anchor still matches. Early impassable returns never clear a previously recorded fire delta. A single shared fire-cost array also loses provenance between pathing contexts. |
| `PathFinder.ParameterizePathJob` | Arguments **1/2/4/6/7** are still job/grid request/unique state/scheduled grid/request; the first `AsReadOnly` still selects the path grid. However, upstream emits **`ldarga` for already-byref arguments 1 and 2**, passing an extra level of indirection. The tested path executes because upstream currently does not read those two arguments; this is a latent invalid forwarding defect, not a reproduced crash. |
| `ProcessIgnoreFire` | Calls Flammability before checking ground fires. Zero-fire reproduction: one stat lookup per eligible request. |
| `GridRemoveFireCost` | A 325×325 map incurs 105,625 managed iterations on rebuild. `prevGrid.Equals` compares buffer identity, not contents. 1.7 recycles `GridJobOutput` and `PathGridJob.Execute` writes the same buffer again: reproduced stale cost **13 instead of new wall cost 10000**. One shared output can also be overwritten while another path job is reading it. |
| Native allocations | Constructor allocates **422,500 + 4 bytes** with `Allocator.Persistent`. First use overwrites `prevGrid` with a borrowed vanilla array, losing its original allocation. `Map.Dispose` leaves `cachedGrid` allocated because the component is not `IDisposable`. |

`FindPath_Patch`, `PathFinderMapData_Patch` contain no active patches; `Mine_Patch` is commented out. `RemoveFireCostJob` exists but is not scheduled. Dormant float-menu/path helper transpilers in `HarmonyPrivate_Patch` are not installed by `HarmonyInit`.

## 实现 / Mechanism

- **待命宽阶段筛选。** 每个 `(Map, tick)` 一次火点和 pawn 快照，分别得到向外扩一格的火/近战候选包围矩形。矩形外只需 4 次整数比较；不会灭火的 pawn 不进行火邻格检查。生成、移动、精神状态/阵营目标更新和捕猎事件在同 tick 扩展候选范围，防止负缓存漏掉新目标。
- **Exact narrow phase.** Inside either relevant rectangle, retain the original cell/list order, melee checks, smallest-fire selection, center-cell override, own-parent exclusion, `PawnShouldBeatFire`, initial guard and `collideWithPawns`. Keep the original ranged path and flags for `Wait_Combat`: “active melee threat” and “shootable target” are not equivalent.
- **Hostility audit.** `HostileTo(Thing,Thing)` includes destroyed/self checks, factionless condition-causers, dormancy, `hostileToAll`, trait suppression, forced mental-state hostility, predator/prey history, prison breaks, slave rebellion, guest/prisoner/slave restrictions, rescue joining, roamers, overseers, factionless humanlikes, shamblers, turret invisibility and faction relations. `HostileTo(Thing,Faction)` has a different subset/order. An empty `AttackTargetsCache` is **not** proof of no melee target. Our broad phase reads spawned pawns; upstream's final `IsActiveThreatTo(target, player)` requires faction hostility, so pair-only postfixes cannot independently make a skipped target attackable. DMSSR `Patch_GeneSet_ImprintAuthority` only changes true→false, and its live result remains respected. Unknown patches to the faction overload or `IsActiveThreatTo` conservatively disable only the melee gate.
- **弱引用密封缓存。** 无 apparel/空 apparel 立即返回 false；穿衣 pawn 使用带每条目锁的 `ConditionalWeakTable`。保持约 900 tick 到期，换装即时失效；设置生成的装备列表预编译为不可变发布的 `HashSet<ThingDef>`。
- **Sparse path costs.** Replace DNAF's six installed hooks after its startup completes. Record the actual additive fire delta at `CalculatedCostAt`, including zero on early returns; copy provenance alongside each `CostSource` generation. Capture fire-only danger after the real danger source computation, preserving dangerous terrain and other dangerous things.
- **Generation-owned native outputs.** No fire: return the original read-only grid before stat evaluation. Otherwise finish the source job, perform one native `NativeArray<int>.Copy`, and subtract only affected cells—including the eight neighbors, not just the burning cell. Each scheduled grid owns a distinct immutable adjusted buffer; recycling/disposal frees it only after vanilla completes readers. Respect impassability, water overrides, persistent-danger settings and darkness's alternative danger cost. Original DNAF allocations are disposed immediately after construction; both fields remain empty. `Map.Dispose` supplies the final cleanup boundary.

## 验证 / Validation

```powershell
dotnet build .\Source\UnburnedDNAFPerfFix.csproj -c Release
pwsh -File .\Validation\validate.ps1
```

Release `net10.0`, `TreatWarningsAsErrors=true`: **0 warnings / 0 errors**. Four process-isolated modes pass: **31 RED checks**, **60 GREEN checks**, **29 GREEN checks without DNAF**, and **60 GREEN checks with sidecar startup first**.

The harness uses the real installed game DLL through the config-safe `DMSEWorldPerfFix/Validation/bin/Release/net10.0/bound/Assembly-CSharp.dll`. It compares all method IL in the eight audited game classes against the installed DLL. QA copies of both Workshop DLLs only redirect Unity assembly references to the installation's `UnityStubs`; upstream method IL is unchanged. It executes real `HarmonyInit`, `CheckForAutoAttack`, `CalculatedCostAt`, both danger methods, `PathGridJob.Execute`, emitted `ParameterizePathJob` and `Map.Dispose`. Headless leaf hooks supply work eligibility, awake state, fixed Flammability, attack/beat outputs and ranged selection, and skip graphics invalidation. Rendering-startup warnings in these logs come from the headless fixture.

| Reproduction / regression | RED → GREEN |
|---|---|
| 333 pawns, 82 crowded chickens, 20 rounds, no fires | **59,940 → 0** `GetThingList` calls |
| Same workload, localized distant fire | **59,940 → 0** calls |
| Zero-fire path request | **1 → 0** Flammability reads |
| Unmatched ground-fire removal | **−1 → 0** |
| Recycled output with new wall | **13 → 10000** |
| One fire on 105,625-cell map | native copy + **9** sparse cell adjustments |
| Native buffers after teardown | leaked upstream allocation → **0 live adjusted buffers** |

Also covers active threats far away, same-tick target movement, mental-state/hostile-to-all targets absent from faction cache, attachments, duplicate callbacks, multiple fires per cell, exact fire/melee priority, ranged gating, 200 seeded differential-selection cases, 10,000 parallel sealed reads, apparel invalidation/expiry, pawn garbage collection, mixed hazards, darkness, water and independent tuned grids.

## 适用范围与退役 / Maintenance

Audited inputs (SHA256):

| Assembly | SHA256 |
|---|---|
| RimWorld 1.7 `Assembly-CSharp.dll` | `82387F78262461E53BB06A67C3945912B876D1FDB07235122BBB6E59D073F125` |
| Unburned `FireIsNotMyNightmare.dll` | `304BCD4D07348107024545264D52467CB991CC58B5DE576C7078F3E612D3D4BB` |
| DNAF 1.6 `DoNotAvoidFire.dll` | `0DFAA06B1F74EE2AB3B7E3D7490E1D67D766104F16FD45AEF773B0CA1744E516` |

游戏或任一上游 DLL 哈希变化后重新运行验证，重点检查待命选择逻辑、路径生产/回收边界和 IL 锚点。若上游已具备对应的空间筛选、弱引用并发缓存与按生成批次管理的稀疏路径输出，即可退役本补丁。启动日志：`[UnburnedDNAFPerfFix] 1.0.0 installed; DNAF=True/False`。此目录可独立部署；只复制 `About/`、`LoadFolders.xml`、`1.7/Assemblies/` 和本 README。

## 给作者的反馈 / Author notes

[Unburned](https://steamcommunity.com/sharedfiles/filedetails/?id=3538336699) was updated for 1.7 on October 23; [DNAF](https://steamcommunity.com/sharedfiles/filedetails/?id=3303359387) still advertises 1.6. Both Workshop descriptions were checked on the audit date; no source repository was linked there.

> 你好！谢谢这两个模组，让火场战斗顺畅了很多。我用真实 1.7 DLL 做了离线复现：待命 pawn 无火时仍扫描 9 格；DNAF 无火寻路仍查询 Flammability，输出网格复用后还会返回旧墙体成本。已整理带 RED/GREEN 测试的独立修复，愿意提供给你参考。另核对了读档流程，`SpawnSetup` 会执行，所以不是“读档一定漏计火”。
>
> Thanks for these mods! I reproduced redundant idle/stat work and stale recycled path-grid costs against the real 1.7 assemblies. A standalone fix with RED/GREEN tests is available for review. The old danger/cost local indices still match; the important issues are cache generation/ownership, danger provenance and double-addressed ref arguments. Save loading does call Fire.SpawnSetup. Happy to share the tests and implementation.
