# DMSSR Stat & Path Perf Fix / 死手开关：合成体 属性·寻路性能修复

> Standalone Harmony performance patch for **The Dead Man's Switch - Synthriada**
> (`Cephalopod.DeadManSwitch.Synthriada`, Workshop `3835512735`).
> 独立 Harmony 性能补丁，针对《死手开关：合成体》。
>
> PackageId: `whitegiver.dmssr.statpathperffix` · RimWorld 1.7 · net10.0 · no save data, no Workshop edits.

---

## 1. TL;DR

| | 上游 DMSSR / upstream | 本补丁 / this mod |
|---|---|---|
| 每次 `pather.CostToMoveIntoCell(c)` | `ClampPathCostForAbility` 跑 **2 次**，每次线性扫全部 Hediff（30 个 Hediff 的生物 = **60 次 Hediff 访问**） | 1 次 clamp，**0 次 Hediff 访问**（缓存读取） |
| 每次无关属性查询（护甲/市场价值/…） | 先做 **1 次 O(Hediff)** 的 `GetFirstHediffOfDef(DMS_MechCustomization)` 扫描 | **0 次**（O(1) 属性闸门先短路） |
| 带「无视地形移动惩罚」的生物 | 地形代价被**扣两次**（120 tick 而非 160 tick） | 只扣一次（160 tick） |
| 30 Hediff 生物的每步耗时（离线基准） | ≈0.25–0.51 µs | ≈0.15–0.20 µs |
| 300 Hediff 生物的每步耗时（离线基准） | ≈1.4–2.8 µs | ≈0.7–1.5 µs（约 1.2–2.0×） |

离线基准为同一进程内 A/B/C 交错测量（vanilla / upstream / fixed，各取 3 轮最优），
但机器上同时跑着 RimWorld，所以只给区间；**结构性计数（扫描次数、Hediff 访问次数）是精确断言**，见第 6 节。

---

## 2. 根因分析（Root cause）

反编译源码：`DMSSR.dll`（1.7，SHA256 `19714ac9…fb1033`）→ `DMSSR.decompiled.cs`。

### 2.1 热点一：每一步跑两遍地形惩罚豁免，且每次全表扫描

`DMSSR.decompiled.cs` 行 `35920..36010`：

```csharp
public static class AbilityPathCostUtility
{
    public static void ClampPathCostForAbility(Pawn pawn, IntVec3 c, ref float pathCost)
    {
        if (!HasIgnoreTerrainMovePenalty(pawn)) return;          // <-- O(hediffs) 扫描
        ...
        int num2 = ((BuildableDef)(GridsUtility.GetTerrain(c, map)?)).pathCost ?? 0;
        float num3 = pathCost - (float)num2;
        pathCost = ((num3 < num) ? num : num3);                  // <-- 减去地形代价
    }

    private static bool HasIgnoreTerrainMovePenalty(Pawn pawn)   // <-- 无缓存
    {
        List<Hediff> list = pawn?.health?.hediffSet?.hediffs;
        ...
        while (num < list.Count)
        {
            Hediff val = list[num];
            if (def.GetModExtension<DefModExt_IgnoreTerrainMovePenalty>()?.enabled == true) return true;
            if (HediffUtility.TryGetComp<HediffComp_IgnoreTerrainMovePenalty>(val) != null) return true;
            num++;
        }
        return false;
    }
}

[HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[]{ typeof(Pawn), typeof(IntVec3) })]
public static class Patch_Ability_PathCost_Static { [HarmonyPostfix] public static void Postfix(Pawn pawn, IntVec3 c, ref float __result) => ...; }

[HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[]{ typeof(IntVec3) })]
public static class Patch_Ability_PathCost_Instance { [HarmonyPostfix] public static void Postfix(Pawn ___pawn, IntVec3 c, ref float __result) => ...; }
```

原版 1.7 `Pawn_PathFollower`（`Assembly-CSharp.dll`，SHA256 `82387f78…73f125`）：

```csharp
private float CostToMoveIntoCell(IntVec3 c)
{
    return CostToMoveIntoCell(pawn, c);     // <-- 纯转发
}
```

于是**同一次调用会依次穿过两个 Postfix**，`ClampPathCostForAbility` 与整段 Hediff 扫描各跑两遍。
全地图每个殖民者、动物、机械体、访客的每一步都如此。

**这同时还是一个行为 bug。** `ClampPathCostForAbility` 的算式是
`pathCost = max(pathCost - terrain.pathCost, ticksPerMove)`。
原版返回值通常是 `ticksPerMove + terrain.pathCost`，单次施加恰好得到 `ticksPerMove`（= 地形完全不影响），
但**施加两次**会得到 `max(ticksPerMove - terrain.pathCost, ticksPerMove)`——在
`原始代价 > ticksPerMove + terrain.pathCost` 时（门/建筑 `PathWalkCostFor`、地形标签 `moveSpeedFactorByTerrainTag`、
`LocomotionUrgency` 倍率、其它 mod 的 Postfix 都会造成这种格），结果会比生物自身基准速度还快。
带 `DefModExt_IgnoreTerrainMovePenalty` 或 `HediffComp_IgnoreTerrainMovePenalty` 的生物因此实际移动更快。

### 2.2 热点二：属性查询先扫 Hediff，后看是哪条属性

`DMSSR.decompiled.cs` 行 `36500..36610`：

```csharp
[HarmonyPatch(typeof(StatWorker), "GetValueUnfinalized", new[]{ typeof(StatRequest), typeof(bool) })]
public static class Patch_MechCustomization_ApplyStatPreFinalize
{
    [HarmonyPostfix]
    public static void Postfix(StatWorker __instance, StatRequest req, bool applyPostProcess, ref float __result)
    {
        if (!req.HasThing) return;                                    // 1
        Thing thing = req.Thing;
        Pawn val = thing as Pawn; if (val == null) return;            // 2
        RaceProperties raceProps = val.RaceProps;
        if (raceProps == null || !raceProps.IsMechanoid
            || !TryGetCompFast(val, out var comp)) return;             // 3 + 4（O(hediffs) 在这里！）
        StatDef val2 = StatFieldRef.Invoke(__instance);                // 5
        if (val2 != null) { if (StatMove.Contains(val2)) ... }          // 6
    }

    private static bool TryGetCompFast(Pawn pawn, out HediffComp_MechCustomization comp)
    {
        ...
        Hediff firstHediffOfDef = val.GetFirstHediffOfDef(DMS_HediffDefOf.DMS_MechCustomization, false); // 线性扫描
        ...
    }
}
```

`Patch_MechCustomization_ApplyStatPreFinalize.Postfix` 修改的属性（`if/else` 顺序，共 9 组 / 10 条）：

| 集合字段 | StatDef |
|---|---|
| `StatMove` | `MoveSpeed` |
| `StatShootAcc` | `ShootingAccuracyPawn` |
| `StatMeleeDodge` | `MeleeDodgeChance` |
| `StatAim` | `AimingDelayFactor` |
| `StatMeleeCD` | `MeleeCooldownFactor` |
| `StatMeleeDamage` | `MeleeDamageFactor` |
| `StatEnergyUse` | `MechEnergyUsageFactor`, `MechEnergyLossPerHP` |
| `StatWastepacks` | `WastepacksPerRecharge` |
| `StatBandwidthCost` | `BandwidthCost` |

也就是说：机械体的**任何**属性查询（护甲、市场价值、工作速度、饥饿速率……）都要先付一次
`GetFirstHediffOfDef` 的 O(Hediff 数量) 扫描，才轮到「这条属性是否与改造有关」的判断。

---

## 3. 修复机制（Mechanism）

补丁目标一共 **4 个方法**（验证脚本会断言这个白名单）：

| 目标 | 类型 | 作用 |
|---|---|---|
| `AbilityPathCostUtility.ClampPathCostForAbility` | Prefix（替换实现） | 相同算式 + 缓存标志位；`return false` 跳过原方法 |
| `Patch_MechCustomization_ApplyStatPreFinalize.Postfix` | Prefix（闸门） | 属性不属于改造属性 / 不是机械体 → 跳过；上游此时本来就是空操作 |
| `Patch_MechCustomization_ApplyStatPreFinalize.TryGetCompFast` | Prefix（缓存） | 直接给出缓存的 `HediffComp_MechCustomization` |
| `HediffSet.DirtyCache` | Postfix（失效） | 健康状态变化时丢弃缓存 |

### 3.1 双 Postfix 的去重（危险的那一半）

只有当两件事同时成立时才会移除 DMSSR 的实例 Postfix：

1. **IL 证明**原版 `CostToMoveIntoCell(IntVec3)` 就是
   `ldarg.0; ldfld Pawn_PathFollower.pawn; ldarg.1; call CostToMoveIntoCell(Pawn, IntVec3); ret`
   （逐字节校验 opcode 与 `ResolveMethod` 目标）；
2. DMSSR 的**静态重载** Postfix 仍然存在（否则移掉实例 Postfix 会让 clamp 彻底消失）。

任一条件不满足 → 自动降级：**两个 Postfix 全部保留**（连「扣两次」的上游行为也保留），
只把 `HasIgnoreTerrainMovePenalty` 换成缓存读取。降级路径也会写日志。

### 3.2 缓存（HediffFlagCache）

* 每个 `Pawn` 一份不可变快照：`hasIgnoreTerrainPenalty` + `HediffComp_MechCustomization`，
  由 `ConditionalWeakTable` 持有（不阻止 GC）。
* **失效点**：`HediffSet.DirtyCache` —— 原版所有 Hediff 增删（`AddDirect` / `RemoveDirect` / `Clear` /
  `ExposeData`）与 `Pawn_HealthTracker.AddHediff/RemoveHediff` 都会调用它。
* **额外的保险**：快照只在「代数 + `HediffSet` 实例 + `Hediff` 数量」三者都一致时才被信任，
  所以即使有 mod 直接改 `hediffs` 列表而不调 `DirtyCache`，数量变化也会被发现。
* **每线程 memo**：`(thingIDNumber, 代数)` 两个 int 比较即可命中，避免热路径上的哈希查找。
* **按 `HediffDef` 缓存「这个 def 是否带豁免」**：分别检查 `def.GetModExtension<DefModExt_IgnoreTerrainMovePenalty>()?.enabled`
  与 `def.comps` 里是否有 `HediffComp_IgnoreTerrainMovePenalty`。
  与上游严格一致：只有 `HediffWithComps` 才会真正构建 comp，普通 `Hediff` 即使 def 里写了 compClass 也不算
  （验证脚本有专门的反例）。
* 读取无锁；并发竞争最多导致多算一次快照，不会给出错误答案。

### 3.3 属性闸门（MechStatSet）

* 「哪些属性算改造属性」**不是抄一份常量表**，而是在第一次属性查询时（此时 `StatDefOf` 已绑定）
  反射读取 DMSSR 自己的 `HashSet<StatDef>` 私有静态字段并取并集。
  上游将来新增一条属性，这里会自动跟上；读不到时回退到本版本这 10 条 `StatDefOf`。
* 在 `StatDefOf` 尚未绑定（XML 还未加载完）时**绝不去碰** DMSSR 的静态字段——
  否则会提前触发它的类型初始化，把 `HashSet` 永久冻结成 `{null}`。此时闸门返回「相关」，行为与上游完全一致。
* 闸门顺序：`StatWorker.stat`（字段读取）→ 集合 O(1) 查询 → `req.HasThing` → `is Pawn` → `IsMechanoid`。
  上游在这些情况下都是空操作，所以跳过是 100% 等价。

---

## 4. 行为等价性（Behavioural equivalence）

* **没有豁免 Hediff 的生物**：路径代价与上游**逐位相同**（离线断言 `200 == 200`）。
* **任何属性、任何生物**：12 条属性（10 条改造属性 + `ArmorRating_Sharp` + `MarketValue`）× 3 种形态
  （机械体+改造 / 机械体无改造 / 非机械体+改造）= **36 组取值与 DMSSR 自身 Postfix 逐位相同**。
* **唯一有意的差异**：带「无视地形移动惩罚」的生物不再被扣两次地形代价。
  上游 120 tick，本补丁 160 tick，而单次 clamp 的正确值就是 160 tick。
  这修正的是上游 bug（`IgnoreTerrainMovePenalty` 的语义应是「地形不影响移动」，而不是「地形倒贴速度」）。
  如果作者认为 120 才是设计意图，把 `PathCostPatches.Apply` 里的去重分支去掉（走降级路径）即可恢复原值。

---

## 5. 过期条件（When this mod becomes unnecessary / harmful）

本补丁按 `DMSSR.dll` 1.7 构建（SHA256 `19714ac9acd16d78d093fd7b973b5e5d05397794986e82abd89557f4a0fb1033`）验证。
出现以下任一情况请先删掉本补丁再报告问题：

1. DMSSR 更新后 `Pawn_PathFollower` 的任一重载不再被 patch，或原版不再让单参数重载转发到静态重载
   → 补丁会自动降级（日志 `pathCostSingleClamp=False pathCostCacheOnly=True`），不会算错，但也不再省扫描。
2. DMSSR 把 `Patch_MechCustomization_ApplyStatPreFinalize.Postfix` 的前两个参数改掉
   → 闸门不会被安装（日志 `statGate=False`），行为回到上游，只是没有优化。
3. DMSSR 自己修好了这两处（例如把 clamp 合并到静态重载、把属性判断提前）
   → 本补丁的收益消失，请移除。可用 `Validation/validate.ps1` 重跑 RED 段：
   如果 RED 段开始失败（例如 `ClampCalls == 1`），说明上游已经修好。
4. 游戏本体升级到 1.8+ → `Assembly-CSharp.dll` 布局可能变化，IL 校验会失败并自动降级；
   但仍建议等待更新版本。

运行日志（Player.log）里搜 `[DMSSR StatPathPerfFix]` 可看到实际安装状态：

```
installed: pathCostSingleClamp=True pathCostCacheOnly=False delegationVerified=True
           statGate=True mechCompCache=True dirtyCacheHook=True
```

---

## 6. 离线验证（Offline validation）

`Validation/` 是一个独立的 .NET 10 控制台程序，**离线**加载真实的
`Assembly-CSharp.dll`、`UnityStubs.dll`、`0Harmony.dll`、`DMSSR.dll`：

* `bind`：只写一份 QA 副本 `Validation/bin/Release/net10.0/bound/Assembly-CSharp.dll`，
  把 `Verse.ModsConfig` 的静态构造清空（**绝不读写你的 `ModsConfig.xml`**）。
  安装目录里的游戏 DLL 与工坊 DLL 都不会被修改。
* `red`：按 DMSSR 自己的 `PatchAll` 方式挂上它真实的三个 Postfix，断言上游的双重执行/双重扣减/全表扫描。
* `green`：先挂上游补丁（模拟真实加载顺序），再实例化本 mod，断言补丁布线、等价矩阵、缓存失效与 O(1) 路径。

```powershell
pwsh -File "G:\dev\rimmaid-patches\mods\DMSSRStatPathPerfFix\Validation\validate.ps1"
```

夹具（`Validation/Fixture.cs`）是真实的 `Map`/`Pathing`/`PathGrid`/`TerrainGrid`/`ThingGrid`/
`RoofGrid`/`SnowGrid`/`HediffSet`/`StatWorker`，只手工构造叶子数据（地形、StatDef、HediffDef），
等价于 XML 加载器会做的事；`CostToMoveIntoCell` 与 `GetValueUnfinalized` 走的都是真实原版方法体。

### RED（上游，21 条断言）

```
upstream patches: both CostToMoveIntoCell overloads + StatWorker.GetValueUnfinalized.
vanilla CostToMoveIntoCell(IntVec3) is a pure delegation to CostToMoveIntoCell(Pawn, IntVec3).
30-hediff pawn: 2 clamps x 30 hediffs = 60 hediff visits per path step (was 200).
flagged pawn: upstream returns 120 (terrain penalty subtracted twice); one clamp gives 160.
=> this is also a behaviour bug: 160 ticks intended, 120 ticks delivered.
unrelated stat query: 1 x 30-hediff scan; relevant stat: 1 x 30-hediff scan + multiplier.
[perf] vanilla  path step 0.12-0.21 us | unrelated stat 0.08-0.14 us
[perf] upstream path step 0.25-0.51 us | unrelated stat 0.13-0.19 us
[perf] fixed    path step 0.15-0.20 us | unrelated stat 0.09-0.11 us
[perf] 300-hediff pawn: upstream 1.4-2.8 us/step vs fixed 0.7-1.5 us/step (~1.2-2.0x).
```

### GREEN（本补丁，131 条断言）

```
patch wiring: 4 targets (clamp prefix, stat gate, mech lookup cache, DirtyCache postfix).
unflagged 30-hediff pawn: 1 clamp, 0 hediff visits, cost 200 (identical to upstream).
10,000 further path steps: 0 additional hediff scans.
flagged pawn: 160 ticks (single clamp; upstream delivered 120).
extension flag and comp flag both honoured; plain Hediff with compClass-only def correctly ignored.
invalidation: DirtyCache add/remove and direct list mutation all observed.
unrelated stat: 0 lookups, 0 scans; first relevant query: 1 scan; next 10,000: 0 scans.
36 value comparisons over 12 stats x 3 pawn shapes: all bit-identical to DMSSR's own postfix.
all ten customization stats receive their own multiplier; unrelated stats receive none.
mech customization lookup: null -> comp -> null across DirtyCache invalidations.
PASS: 131 assertions.
```

`validate.ps1` 会依次跑 `bind` / `red` / `green`，任一断言失败即非零退出。

---

## 7. 目录结构（Layout）

```
DMSSRStatPathPerfFix/
  About/About.xml
  LoadFolders.xml
  1.7/Assemblies/DMSSRStatPathPerfFix.dll
  Source/
    DMSSRStatPathPerfFix.csproj
    DMSSRStatPathPerfFixMod.cs      Mod 入口，安装全部 patch
    PathCostPatches.cs              热点一
    MechStatPatch.cs                热点二 + 属性集合
    HediffFlagCache.cs              每 Pawn 缓存 + 按 Def 缓存
    HediffCachePatches.cs           DirtyCache 失效钩子
    SafeLog.cs                      日志永不抛异常
  Validation/                       bind / red / green 离线验证
  README.md
```

构建：

```powershell
dotnet build "G:\dev\rimmaid-patches\mods\DMSSRStatPathPerfFix\Source\DMSSRStatPathPerfFix.csproj" -c Release
```

`Release` + `TreatWarningsAsErrors=true`，0 warning / 0 error；构建后自动把 DLL 复制到 `1.7/Assemblies/`。

---

## 8. 给作者的私信草稿（Message draft for the author）

**English**

> Hi Cephalopod,
>
> I've been profiling a late-game colony and found two structural hot spots in Synthriada 1.7. Both are
> cheap to fix in the mod itself, so here's the analysis in case it's useful — I've also published a
> small standalone Harmony patch (`whitegiver.dmssr.statpathperffix`) that does exactly this, in case
> you'd rather not touch it right now.
>
> **1. `CostToMoveIntoCell` is patched twice per step.**
> `Patch_Ability_PathCost_Static` and `Patch_Ability_PathCost_Instance` both call
> `AbilityPathCostUtility.ClampPathCostForAbility`. In vanilla 1.7 the instance overload is literally
> `return CostToMoveIntoCell(pawn, c);`, so every path step of every pawn/animal/mech runs the whole
> hediff walk twice. `HasIgnoreTerrainMovePenalty` also walks every hediff and calls
> `GetModExtension<T>()` / `TryGetComp<T>()` on each one with no caching.
>
> There's a second-order issue: the clamp is `pathCost = max(pathCost - terrain.pathCost, ticksPerMove)`,
> and applying it twice subtracts `terrain.pathCost` twice. Whenever the cell costs more than
> `ticksPerMove + terrain.pathCost` (doors with `PathWalkCostFor`, terrain tags in
> `moveSpeedFactorByTerrainTag`, `LocomotionUrgency` multipliers, other mods' postfixes), a pawn with
> `DefModExt_IgnoreTerrainMovePenalty` ends up moving faster than its own base speed. In my fixture a
> 160-tick cell came out as 120 ticks.
>
> Suggested fix: keep only the static-overload postfix (or drop both and patch
> `ClampPathCostForAbility` once), and cache the per-pawn flag. It only changes when the hediff set
> changes, and every add/remove/load goes through `HediffSet.DirtyCache`.
>
> **2. The mech-customization stat postfix checks the stat last.**
> `Patch_MechCustomization_ApplyStatPreFinalize.Postfix` does `HasThing -> is Pawn -> IsMechanoid ->
> TryGetCompFast -> read stat -> compare against the stat sets`. `TryGetCompFast` is a linear
> `GetFirstHediffOfDef(DMS_MechCustomization)`, so every stat query for a mechanoid (armour, market
> value, work speed, …) pays an O(hediffs) scan before the code even looks at which stat was asked for.
> Moving the stat check to the front and caching the comp lookup makes it O(1) for the ~99% of queries
> that have nothing to do with customization.
>
> Numbers from my offline harness (real `Assembly-CSharp.dll` + real `DMSSR.dll`, 30 hediffs):
> path step ≈0.25–0.51 µs → ≈0.15–0.17 µs, unrelated stat query ≈0.13–0.18 µs → ≈0.09–0.15 µs, and a
> 300-hediff pawn goes from ≈1.4–2.8 µs to ≈0.7–1.5 µs per step. The clamp's hediff scan goes from
> 2×N per step to zero.
>
> No pressure at all — the patch is standalone, writes nothing to saves, and I'll take it down if you'd
> rather handle it in the mod. Thanks for the mod, it's great.
>
> — WhiteGiverMa

**中文**

> Cephalopod 你好，
>
> 我在后期殖民地做性能分析时发现 Synthriada 1.7 里有两处结构性热点，改起来都不难，写给你参考。
> 我也做了一个独立 Harmony 补丁（`whitegiver.dmssr.statpathperffix`），如果你想晚点再改可以先顶着用。
>
> **1. `CostToMoveIntoCell` 每步被 patch 了两次。**
> `Patch_Ability_PathCost_Static` 与 `Patch_Ability_PathCost_Instance` 都会调用
> `AbilityPathCostUtility.ClampPathCostForAbility`；原版 1.7 里单参数重载就是
> `return CostToMoveIntoCell(pawn, c);`，于是每个生物每一步都会把整段 Hediff 扫描跑两遍。
> `HasIgnoreTerrainMovePenalty` 本身也是逐条 Hediff 调 `GetModExtension<T>()` / `TryGetComp<T>()`，完全没有缓存。
>
> 还有个连带问题：clamp 是 `pathCost = max(pathCost - terrain.pathCost, ticksPerMove)`，跑两次就扣两次地形代价。
> 当该格实际代价大于 `ticksPerMove + terrain.pathCost` 时（带 `PathWalkCostFor` 的门、`moveSpeedFactorByTerrainTag`
> 地形标签、`LocomotionUrgency` 倍率、其它 mod 的 Postfix 都会造成），带 `DefModExt_IgnoreTerrainMovePenalty`
> 的生物会比自身基准速度还快。我的夹具里 160 tick 的格算成了 120 tick。
>
> 建议：只保留静态重载的 Postfix（或两个都不要，改成 patch 一次 `ClampPathCostForAbility`），并把「是否豁免」
> 按 Pawn 缓存。它只在 Hediff 集合变化时改变，而原版所有增删/读档都会走 `HediffSet.DirtyCache`。
>
> **2. 机械体改造的属性 Postfix 最后才看属性。**
> `Patch_MechCustomization_ApplyStatPreFinalize.Postfix` 的顺序是
> `HasThing → 是 Pawn → IsMechanoid → TryGetCompFast → 读 stat → 比对属性集合`。
> `TryGetCompFast` 是线性的 `GetFirstHediffOfDef(DMS_MechCustomization)`，所以机械体的每一条属性查询
> （护甲、市场价值、工作速度……）都先付一次 O(Hediff) 扫描。
> 把属性判断提前、把 comp 查询缓存，就能让 99% 无关查询变成 O(1)。
>
> 我这边的离线夹具（真实 `Assembly-CSharp.dll` + 真实 `DMSSR.dll`，30 个 Hediff）数据：
> 每步约 0.25–0.51 µs → 0.15–0.17 µs，无关属性查询约 0.13–0.18 µs → 0.09–0.15 µs，
> 300 Hediff 的生物每步约 1.4–2.8 µs → 0.7–1.5 µs；clamp 的 Hediff 扫描从每步 2×N 次降到 0 次。
>
> 完全不着急——补丁是独立的、不写存档，你要是打算在 mod 里自己改，我随时可以下架。
> 谢谢你的 mod，很好玩。
>
> —— WhiteGiverMa

---

## 9. 已知的非目标（Out of scope）

* `Patch_MechCustomization_Capacity`（`PawnCapacityUtility.CalculateCapacityLevel`）里有同样的
  `TryGetCompFast` 线性扫描，而且 `CalculateCapacityLevel` 在机械体身上比属性查询更热。
  本补丁**没有**改它（保持补丁面最小、便于验证）。若需要，可以照 `MechStatPatch` 的写法加一个
  同款前缀，缓存已经现成。
* `Patch_MechCustomization_BodySize` 里的 `TryGetCompFast` 同理。
* 其它 `GetFirstHediffOfDef` 调用点（非本 mod）不在讨论范围内。
