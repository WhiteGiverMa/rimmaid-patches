# DMS 军团钻井驳机性能优化 / DMS Legion Drilling Barge Perf Fix

| 项目 / Item | 值 / Value |
| --- | --- |
| packageId | `whitegiver.dmsl.drillingbargeperffix` |
| 版本 / Version | 1.0.0 |
| 支持 / Supported | RimWorld 1.7 |
| 依赖 / Depends on | Harmony (`brrainz.harmony`), The Dead Man's Switch - Legion (`Srxf.DeadManSwitch.Legion`, Steam `3666117000`) |
| 目标 / Target | `DMSL_Mech_DrillingBarge` 的深钻井工作扫描 / drilling barge deep-drill work scan |
| 类型 / Type | 纯 Harmony 运行时补丁，不改 DLL/XML，不写存档 / runtime Harmony only, no DLL/XML edits, nothing scribed |

---

## 1. 症状 / Symptom

地图为 325×325（105,625 格）且有基岩（`Biome.hasBedrock`）时，只要岛上有 `DMSL_Mech_DrillingBarge`：

1. **默认设置**（`enableDrillingBargeExperimentalWorkLogic = false`）下，每一次 `JobGiver_Work` 思考都会：
   - 由 `WorkGiver_DrillingBargeDeepDrill.PotentialWorkCellsGlobal` **yield 整张地图的全部 105,625 个格子**；
   - `JobGiver_Work.ProcessCell` 对每格调用 `HasJobOnCell` → `CanDrillAt` + `ForbidUtility` + `CellHasValuableDeepResource`；
   - `CellHasValuableDeepResource` → `TryGetDeepResource` 再对每格做 **21 格半径**的 `deepResourceGrid.CountAt/ThingDefAt` 扫描；
   - 每遇到一个非空格还会调用 `CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource`，它每次都执行 `DefDatabase<ThingDef>.GetNamedSilentFail("DMSL_Mech_DrillingBarge")` 并遍历 `ThingDef.comps`。
   合计约 **105,625 × 21 ≈ 220 万次网格读取 + 大量 DefDatabase/comps 查找**，每个钻井驳机、每个思考周期一次。装了 `WhileYouAreNearby` 之类会直接调用 `PotentialWorkCellsGlobal` 的模组时同样会触发。
2. **实验设置**（`enableDrillingBargeExperimentalWorkLogic = true`）下，`DrillingBargeValidDrillCellsCacheComponent.MapComponentTick` 每 tick 只扫描 4 格，重建完整缓存需要 **26,406 tick**（1× 速度约 7 分钟，约 10.6 游戏小时）；在这段时间里 `GetCachedCells()` 返回旧/空列表，钻井驳机会"看不见"新矿脉，而且这个 4 格/tick 的扫描会永久持续。

On a 325×325 map with bedrock, every `JobGiver_Work` pass for a `DMSL_Mech_DrillingBarge`:

1. With default settings, `PotentialWorkCellsGlobal` yields **all 105,625 cells**, and `HasJobOnCell` runs `TryGetDeepResource` (21 radial cells) on each of them, calling `IsExcludedDeepResource` (a `DefDatabase` lookup plus a comps walk) for every non-empty cell. That is roughly **2.2 million grid reads per pawn per think**, plus the def lookups.
2. With the experimental setting, the map component rebuilds its cache 4 cells per tick — **26,406 ticks** (about 7 real minutes at 1x, ~10.6 in-game hours) — and returns a stale/empty list during the whole rebuild, while the 4-cells-per-tick scan never stops.

---

## 2. 根因 / Root cause

### A. `CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource(ThingDef? resourceDef)`

```csharp
ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("DMSL_Mech_DrillingBarge");
if (namedSilentFail?.comps == null) return false;
for (int i = 0; i < namedSilentFail.comps.Count; i++)
    if (namedSilentFail.comps[i] is CompProperties_DrillingBargeDeepResourceOverlay overlay)
        return overlay.excludedDeepResourceDefNames.Contains(resourceDef.defName);
```

这个方法本身不贵，但它被放在 `TryGetDeepResource` 的 21 格循环里，于是"每格一次"变成"每格最多 21 次"。`List<string>.Contains` 还是线性扫描。

The method is cheap in isolation, but it sits inside the 21-cell loop of `TryGetDeepResource`, turning one call per cell into up to 21 calls per cell, each with a fresh def-database lookup and a linear `List<string>.Contains`.

### B. `WorkGiver_DrillingBargeDeepDrill.PotentialWorkCellsGlobal(Pawn pawn)`

```csharp
if (settings.enableDrillingBargeExperimentalWorkLogic) {
    IReadOnlyList<IntVec3> cached = DrillingBargeValidDrillCellsCacheComponent.GetOrCreate(map)?.GetCachedCells();
    return cached ?? Enumerable.Empty<IntVec3>();
}
return GetPotentialDrillCells(map);   // for (int i = 0; i < map.cellIndices.NumGridCells; i++) yield return ...;
```

默认路径就是"整张地图"。所有过滤都发生在调用方 `HasJobOnCell` 里，而 `JobGiver_Work` 会为每个格子付一次完整校验的代价。实验路径只是把"一次性全图扫描"改成了"永久 4 格/tick 扫描 + 长时间空缓存"，并没有消除工作量。

The default path is literally "the whole map"; all filtering happens later in `HasJobOnCell`, and `JobGiver_Work` pays a full validation per cell. The experimental path just spreads the same full scan over 26,406 ticks and serves an empty list while it runs.

---

## 3. 修复机制 / Mechanism

### 3.1 每张地图一份深层资源索引 / Per-map deep-resource index

`MapDrillCache` 以 `ConditionalWeakTable<Map, MapDrillCache>` 挂在 `Map` 上：

- **候选集构造**：遍历 `i = 0 .. NumGridCells-1`，把 `CountAt(i) > 0 && ThingDefAt(i) != null` 的格子视为矿脉格 `v`；对每个 `v`，把它加上 `GenRadial.RadialPattern[0..20]` 及其反向偏移（去重后 21 个）得到的所有格子标为候选中心。因为 `TryGetDeepResource(center)` 只扫描 `center + RadialPattern[k]`，所以任何可能返回 `true` 的 `center` 必然满足 `center = v - pattern[k]`，即一定在候选集里。
- **精确求值**：对每个候选中心，直接调用**上游的** `TryGetDeepResource(..., applyExclusions: true)` 和 `(..., false)`，把结果写进每格一字节的位图（bit0 = 带排除，bit1 = 不带排除）。不是重写算法，而是调用原函数，所以逐格结果与上游逐字节一致。
- **顺序**：候选集用 `SortedSet<int>` 按 `CellToIndex` 排序，`List<IntVec3>` 的输出顺序与上游 `GetPotentialDrillCells`（索引 0→N-1）完全一致。
- **查询**：`CellHasValuableDeepResource` 退化为一次 `InBounds` + 一次数组读取；`PotentialWorkCellsGlobal` 直接返回 `List<IntVec3>`，于是原版 `JobGiver_Work` 走 `if (enumerable2 is IList<IntVec3> list2)` 的零枚举器快路径。
- **增量维护**：Harmony postfix 挂在 `DeepResourceGrid.SetAt(IntVec3, ThingDef, int)` 上。只有被改格子本身及其 21 个径向邻居的答案可能变化，所以每次更新是 O(21×21) 次网格读取 + O(log n) 集合操作，与地图大小无关。只改数量（仍 > 0）时直接返回，不触发任何重建。真正的成员变化才重建一次 `List<IntVec3>`（新分配，旧列表不会被就地修改）。
- **失效**：`DeepResourceGrid.ExposeData` 在读档时（非保存）丢弃该地图的缓存；换图/新游戏由 `ConditionalWeakTable` 自动回收。
- **实验缓存**：`DrillingBargeValidDrillCellsCacheComponent.MapComponentTick` 被 prefix 成 no-op——它的唯一消费者 `PotentialWorkCellsGlobal` 已被替换，继续每 tick 扫 4 格没有意义。

The index is built once per map by marking every cell `v + offset` for each vein cell `v` (offsets = `RadialPattern[0..20]` plus its negation), then evaluating every candidate by calling the **upstream** `TryGetDeepResource` itself. Queries become one bounds check plus one array read; `PotentialWorkCellsGlobal` returns a `List<IntVec3>`, which vanilla `JobGiver_Work` consumes through its `IList<IntVec3>` fast path. A `SetAt` postfix updates only the edited cell and its 21 neighbours, so the cost is independent of map size. Count-only changes are no-ops. Loading (`ExposeData` in non-saving mode) drops the cache; `ConditionalWeakTable` releases it with the map.

### 3.2 排除名单缓存 / Exclusion-set cache

`OverlayExclusionCache` 复刻上游语义（取**第一个** `CompProperties_DrillingBargeDeepResourceOverlay`，`StringComparer.Ordinal` 比较），但把 `excludedDeepResourceDefNames` 预先转成 `HashSet<string>`。ThingDef 只在 `DefDatabase<ThingDef>.DefCount` 变化时重新解析；`comps` 列表、props 实例、名单实例与 `Count` 都按引用/计数校验，所以运行时被其他模组替换/重填的名单也能被观察到。查询从"DefDatabase 查找 + 遍历 comps + 线性 Contains"变成一次哈希查找。

The exclusion set is pre-hashed and the def is re-resolved only when the ThingDef database size changes; the comps list, props instance and names list are validated by reference and count, so runtime changes are still observed. Matching semantics (first overlay comp, ordinal comparison) are identical to upstream.

---

## 4. 为什么结果完全一致 / Why results stay identical

1. `TryGetDeepResource` 只看 21 格；候选集是它的严格超集，非候选格必然返回 `false`，与位图的 0 一致。
2. 候选格的值是**调用上游函数**得到的，不是重新实现。
3. `PotentialWorkCellsGlobal` 的守卫链（`pawn`、`pawn.Map`、`defName`、`settings.enableDrillingBargeDeepDrill`、`Biome.hasBedrock`、`DeepResourceGridTrackerComponent.HasAnyDeepResource`）逐条照抄；任何一条不满足时直接 `return true` 交回原函数，绝不自行决定。
4. `CellHasValuableDeepResource` 的 `applyExclusions` 判定与上游一致（`pawn?.def?.defName == "DMSL_Mech_DrillingBarge"`），越界/空网格返回 `false`。
5. 即使 `enableDrillingBargeExperimentalWorkLogic = true`，返回的也是"精确集合"而不是上游那份可能过期/不完整的缓存；`HasJobOnCell` 仍会照常执行 `CanDrillAt`、禁止区、预约与可达性检查，所以**能发出的工作不会多也不会少**。

离线验证（见第 5 节）在 325×325 地图上对 **105,625 格 × 2 种排除模式 = 211,250 个 (格子, 模式) 组合**逐一对比上游 `TryGetDeepResource`，零差异。

---

## 5. 验证 / Validation

`Validation/` 是一个独立的离线控制台工程，加载**真实的** `Assembly-CSharp.dll`、`0Harmony.dll` 和 `A:\SteamLibrary\steamapps\workshop\content\294100\3666117000\1.7\Assemblies\DMS_Legion.dll`，构造真实的 325×325 `Map` + `DeepResourceGrid` + `DMSL_Mech_DrillingBarge` ThingDef/CompProperties 与矿脉，然后：

- **RED（打补丁前）**：确认 `PotentialWorkCellsGlobal` 产出 105,625 格；`IsExcludedDeepResource`、`CellHasValuableDeepResource` 在允许矿/排除矿/空格/越界上的行为；`MapComponentTick` 确实进入 `CanDrillAt` 并推进扫描下标。
- **GREEN（打补丁后）**：返回值是 `IList<IntVec3>`、只含上游 `TryGetDeepResource(..., true)` 为真的格子且顺序一致；全图 211,250 组等价性；`SetAt` 新增矿脉 / 改成排除矿 / 采空 / 仅改数量 的增量失效；`MapComponentTick` 10,000 次调用后扫描下标不动、上游组件缓存保持为空；`enableDrillingBargeDeepDrill` 与 `Biome.hasBedrock` 守卫保真。

本机离线结果（仅作参考，非游戏内帧时间）：

```
upstream full scan: 105,625 cells in 29.8 ms
patched  full scan:      42 cells in 0.011 ms
ALL GREEN - 45 checks passed
```

运行方式 / How to run:

```powershell
dotnet build -c Release G:\dev\rimmaid-patches\mods\DMSLDrillingBargePerfFix\Source\DMSLDrillingBargePerfFix.csproj
dotnet build -c Release G:\dev\rimmaid-patches\mods\DMSLDrillingBargePerfFix\Validation\Validation.csproj
dotnet G:\dev\rimmaid-patches\mods\DMSLDrillingBargePerfFix\Validation\bin\Release\net10.0\Validation.dll
```

**2026-11-13 用户确认游戏内验证通过**：日志 `[DMS Legion Drilling Barge Perf Fix] active (6 patches)`，DPA `Work Giver` 中不再出现钻井驳机热点；本次 QA 状态更新保持已验证的 1.0.0 DLL 逐字节不变。

**In-game QA confirmed by the user on 2026-11-13**: the log shows `[DMS Legion Drilling Barge Perf Fix] active (6 patches)` and the drilling barge hot spot no longer appears in DPA's `Work Giver`; the validated 1.0.0 DLL is unchanged.

---

## 6. 何时应当卸载 / Expiration & upstream updates

**必须重新跑验证 / always re-run the harness when:**

- `DMS_Legion.dll` 更新（Steam 创意工坊自动更新）。若上游改了 `TryGetDeepResource` 的半径（不再是 `RadialPattern[0..20]`）或判定语义（`count > 0`、`ThingDefAt != null`、首个命中即返回），候选集可能变成真子集，本补丁就会**漏格**。验证工程的 211,250 组对比就是为此设计的：任何失败都说明应当立刻停用本补丁。
- 上游重命名了 `PotentialWorkCellsGlobal` / `CellHasValuableDeepResource` / `IsExcludedDeepResource` / `MapComponentTick`。此时对应 patch 会在日志里打印 `[DMS Legion Drilling Barge Perf Fix] ... not found`，只有那一条优化失效，其余继续工作；但请检查日志。

**应当直接卸载 / remove the mod when:**

- 作者在官方版本里实现了等价或更好的索引（例如基于区域/事物的候选集，或事件驱动的增量缓存）。本补丁会变成纯冗余。
- 游戏本体大版本更新导致 `DeepResourceGrid.map` 私有字段改名。补丁会拒绝安装依赖该字段的优化（只保留排除名单缓存），日志会明确说明；此时请更新或卸载本补丁。
- 你使用了其它会**绕过 `DeepResourceGrid.SetAt`** 直接改写 `defGrid`/`countGrid` 的模组。本补丁只监听 `SetAt` 与读档，直接改数组的第三方代码无法被观察到。目前没有已知模组这样做，但这是唯一的语义假设。

**安全性 / Safety:** 补丁不写入任何存档字段（没有 `Scribe_*`），所有缓存都是瞬时内存结构，随地图释放。存档中途添加或移除都不会留下脏数据；移除后立即回到上游行为。

**Known limitation:** 若某个模组绕过 `SetAt` 直接写 `DeepResourceGrid` 的内部数组，索引不会更新。除此之外没有行为差异。

---

## 7. 给作者的留言草稿 / Message draft for the mod author

> 你好！我很喜欢 Legion，钻井驳机的设定很棒。1.7 上我做性能剖析时发现两个可以几乎零成本消除的热点，附上细节供你参考（我已经写了本地 Harmony 补丁验证过，**没有改动你的任何文件**）：
>
> 1. `WorkGiver_DrillingBargeDeepDrill.PotentialWorkCellsGlobal` 在 `enableDrillingBargeExperimentalWorkLogic = false`（默认）时返回整张地图。325×325 = 105,625 格，`JobGiver_Work` 会对每格跑一次 `HasJobOnCell`，也就是每格一次 21 格半径的 `TryGetDeepResource`。其实只有"离矿脉 21 格以内"的格子才可能返回 true——把矿脉格反向展开成候选集（`v + RadialPattern[k]` 及其反向），再对候选逐个调用你原来的 `TryGetDeepResource`，结果逐格完全一致，但候选通常只有几十个。返回 `List<IntVec3>` 还能让原版走 `IList<IntVec3>` 的快路径。
> 2. `CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource` 每次调用都 `DefDatabase.GetNamedSilentFail` + 遍历 `comps`。把 `excludedDeepResourceDefNames` 预先做成 `HashSet<string>` 并缓存 ThingDef（用 `DefDatabase<ThingDef>.DefCount` 当失效令牌）就够了。
> 3. 另外 `DrillingBargeValidDrillCellsCacheComponent` 的 4 格/tick 重建在 325×325 上要 26,406 tick，期间 `GetCachedCells()` 是空的；如果上面第 1 点做了，这个组件其实可以整个去掉，或者改成"事件驱动 + 懒重建"。
>
> 我这边有一个离线验证工程，会在真实 `Assembly-CSharp.dll` + `DMS_Legion.dll` 上对 105,625 格 × 2 种排除模式逐一比对上游 `TryGetDeepResource`，目前 45 项全绿。需要的话我可以把补丁和验证代码发给你，或者直接开 PR。谢谢！

> Hi! I really enjoy Legion, and the drilling barge is a great idea. While profiling on 1.7 I found two hot spots that can be removed almost for free. I validated this with a local Harmony patch and **did not modify any of your files**:
>
> 1. `WorkGiver_DrillingBargeDeepDrill.PotentialWorkCellsGlobal` returns the entire map when `enableDrillingBargeExperimentalWorkLogic = false` (the default). On 325×325 that is 105,625 cells, and `JobGiver_Work` runs `HasJobOnCell` on every one of them, i.e. a 21-cell `TryGetDeepResource` each. Only cells within 21 tiles of a vein can ever be true, so expanding each vein cell by `v + RadialPattern[k]` (and the negated offsets) gives a candidate set of a few dozen cells; evaluating each candidate with your own `TryGetDeepResource` keeps results cell-for-cell identical. Returning a `List<IntVec3>` also lets vanilla take its `IList<IntVec3>` fast path.
> 2. `CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource` performs a `DefDatabase.GetNamedSilentFail` plus a comps walk on every call. Pre-hashing `excludedDeepResourceDefNames` into a `HashSet<string>` and caching the ThingDef (invalidated by `DefDatabase<ThingDef>.DefCount`) is enough.
> 3. The 4-cells-per-tick rebuild in `DrillingBargeValidDrillCellsCacheComponent` needs 26,406 ticks on 325×325 and serves an empty list meanwhile. With (1) in place the component can go away entirely, or become event-driven with lazy rebuilds.
>
> I have an offline harness that compares the patched result against your `TryGetDeepResource` on all 105,625 cells × 2 exclusion modes using the real `Assembly-CSharp.dll` and `DMS_Legion.dll`; 45/45 checks pass. Happy to send the patch and the harness, or open a PR. Thanks!

---

## 8. 目录结构 / Layout

```
DMSLDrillingBargePerfFix/
├─ About/About.xml
├─ LoadFolders.xml
├─ README.md
├─ 1.7/Assemblies/DMSLDrillingBargePerfFix.dll   # 构建产物 / build output
├─ Source/
│  ├─ DMSLDrillingBargePerfFix.csproj
│  ├─ DrillingBargePerfFix.cs        # Apply() + Mod 入口 / entry point
│  ├─ DrillingBargePatches.cs        # Harmony prefix/postfix 本体
│  ├─ MapDrillCache.cs               # 每图索引 / per-map index
│  ├─ MapDrillCacheSet.cs            # ConditionalWeakTable + SetAt/读档同步
│  └─ OverlayExclusionCache.cs       # 排除名单缓存 / exclusion-set cache
└─ Validation/
   ├─ Validation.csproj
   ├─ Program.cs                    # 程序集解析 / assembly resolution
   ├─ Fixture.cs                    # 325×325 离线地图夹具 / offline map fixture
   └─ Tests.cs                      # RED/GREEN 断言 / assertions
```

构建 / Build:

```powershell
dotnet build -c Release Source\DMSLDrillingBargePerfFix.csproj   # 自动复制到 1.7\Assemblies
dotnet build -c Release Validation\Validation.csproj
```
