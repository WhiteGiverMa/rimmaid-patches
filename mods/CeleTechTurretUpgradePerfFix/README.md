# 天工铸造炮塔升级扫描优化 / CeleTech Turret Upgrade Perf Fix

| 项目 / Item | 值 / Value |
| --- | --- |
| packageId | `whitegiver.celetech.turretupgradeperffix` |
| 版本 / Version | 1.0.0 |
| 支持 / Supported | RimWorld 1.7 |
| 依赖 / Depends on | Harmony (`brrainz.harmony`), CeleTech Arsenal MKIII (`TOT.CeleTech.MKIII`, Steam `3446237098`) |
| 目标 / Target | `CeleTech.Base.WorkGiver_TurretUpgrade` 的全地图工作扫描 / full-map work scan |
| 类型 / Type | 纯 Harmony 运行时补丁，不改 DLL/XML，不写存档 / runtime Harmony only, no DLL/XML edits, nothing scribed |

---

## 1. 症状 / Symptom

只要地图上有殖民地居民或机械体在找 `Construction` 工作，`WorkGiver_TurretUpgrade` 就会参与工作搜索。即使地图上**没有任何天工炮塔**、`Comp_TurretUpgrade.queued` 全空，它仍会遍历 `ThingRequestGroup.BuildingArtificial` 中的每一面墙、导管、门、货架和建筑。大型基地有数千个建筑时，单次工作搜索的扫描成本可达 10+ ms，并且每个殖民者/机械体、每次工作思考都会重复。

Every `Construction` work search for every colonist/mech includes `WorkGiver_TurretUpgrade`. Even with **zero CeleTech turrets** on the map and every `Comp_TurretUpgrade.queued` list empty, it walks every wall, conduit, door, shelf and building in `ThingRequestGroup.BuildingArtificial`. On a large base with thousands of buildings this costs 10+ ms per work search and repeats for every pawn and every work think.

---

## 2. 根因 / Root cause

上游 `CeleTech.Base.WorkGiver_TurretUpgrade`（1.7 反编译）只覆盖了三个成员：

```csharp
public override ThingRequest PotentialWorkThingRequest
{
    get { return ThingRequest.ForGroup((ThingRequestGroup)10); } // BuildingArtificial
}

public override PathEndMode PathEndMode => PathEndMode.Touch;

public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
{
    Comp_TurretUpgrade comp = t.TryGetComp<Comp_TurretUpgrade>();
    if (comp == null || comp.queued.Count == 0) return false;
    // ... reservation / FirstDoable / resource piles
}
```

它**没有覆盖** `ShouldSkip(Pawn, bool)`，也**没有覆盖** `PotentialWorkThingsGlobal(Pawn)`。两者都继承自 `WorkGiver_Scanner`，分别返回 `false` 和 `null`。工坊 `Defs` 中 `1x1Turret.xml` 的 `CMC_CSGTurret` 系列、`PrismTower.xml`、`AACannon.xml` 等 10 个 XML 文件里的炮塔都挂了 `CompProperties_TurretUpgrade`，且都继承 `BuildingBase`，因此都属于 `BuildingArtificial`；候选集里绝大多数却是没有该 comp 的墙/导管/门/货架。

`JobGiver_Work.TryIssueJobPackage` 的调用顺序是：

1. `PawnCanUseWorkGiver(pawn, giver)` → `ShouldSkip` 返回 `false`，所以继续。
2. `giver.NonScanJob(pawn)` → 继承实现返回 `null`。
3. `scanner.PotentialWorkThingsGlobal(pawn)` → 继承实现返回 `null`。
4. `GenClosest.ClosestThingReachable(..., ThingRequest.ForGroup(BuildingArtificial), ..., customGlobalSearchSet: null, ..., forceAllowGlobalSearch: false)`。

`WorkGiver_TurretUpgrade` 也没有覆盖 `MaxRegionsToScanBeforeGlobalSearch`（默认 `-1`），所以 `ClosestThingReachable` 先做最多 30 个 region 的 BFS，然后进入全图 global 阶段：`customGlobalSearchSet ?? map.listerThings.ThingsMatching(thingReq)`。global 阶段对**每个** `BuildingArtificial` 候选调用 `GlobalValidator` → `scanner.HasJobOnThing(pawn, t)`；而 `HasJobOnThing` 的第一步 `TryGetComp<Comp_TurretUpgrade>(t)` 对墙、导管、门、货架等全部返回 `null`。于是几千个候选全部白扫。

The upstream giver advertises `BuildingArtificial` as its candidate group but does not override `ShouldSkip` or `PotentialWorkThingsGlobal`. `JobGiver_Work` therefore calls `GenClosest.ClosestThingReachable` with a null custom search set; after the bounded region pre-pass it falls back to the global lister for **every** `BuildingArtificial` thing on the map and invokes `HasJobOnThing` on each one. `HasJobOnThing` immediately fails on anything without `Comp_TurretUpgrade`, which is every wall, conduit, door and shelf. The work is pure waste whenever the player has no queued turret upgrades.

### 为什么不是“区域洪泛”而是“区域 + 全图” / Why it is both

`ThingRequestGroup.BuildingArtificial` 是存储在 region 里的组，所以 `ClosestThingReachable` 会先做最多 30 个 region 的 BFS；找不到目标后，因为 `searchRegionsMax = -1` 使 `forceAllowGlobalSearch` 为真，它继续扫描全图 lister。真正造成 10+ ms 的是后者：候选数量等于地图上所有人工建筑，而不是 30 个 region 里的建筑。

`BuildingArtificial` is region-stored, so the first phase is a bounded BFS over at most 30 regions. The global phase is the expensive one: it enumerates every artificial building on the map, not just those 30 regions.

---

## 3. 修复机制 / Mechanism

本补丁维护一份**每张地图**的 `Comp_TurretUpgrade` 注册表：

- **懒初始化**：某张地图第一次被查询时，扫描一次 `map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)`，记录所有带 `Comp_TurretUpgrade` 的建筑。读取已有存档时只需这一次。
- **增量注册**：`Comp_TurretUpgrade.PostSpawnSetup(bool)` 的 postfix 把新生成/新加载的 comp 注册到 `parent.Map`，因此之后生成的炮塔不会漏。
- **读取时修剪**：查询时移除 `parent == null`、`parent.Destroyed`、`!parent.Spawned` 或 `parent.Map != map` 的条目；销毁、收纳、跨地图移动都不会留下脏数据。
- **热路径 O(1)**：`HasAnyQueuedUpgrade(map)` 在没有注册项时只做一次 `list.Count == 0` 判断（第一次查询后）；有注册项时只遍历少量 CeleTech 炮塔，不触碰墙/导管/货架。

The patch keeps a **per-map registry** of `Comp_TurretUpgrade` instances. It is filled lazily once from the `BuildingArtificial` lister (for save loads), then incrementally from `Comp_TurretUpgrade.PostSpawnSetup`. Queries prune entries whose parent is destroyed, despawned or on another map. With no turrets, `HasAnyQueuedUpgrade` is a single count check after the first query; with *k* turrets it is O(k), never O(buildings).

四个 Harmony 补丁 / Four Harmony patches:

| # | 目标 / Target | 类型 / Kind | 作用 / Effect |
| --- | --- | --- | --- |
| 1 | `JobGiver_Work.PawnCanUseWorkGiver(Pawn, WorkGiver)` | postfix | 当天工 giver 通过原版检查、但地图上没有任何排队升级时，把结果改为 `false`，在 `NonScanJob` / `PotentialWorkThingsGlobal` / `GenClosest` 之前直接跳过。 |
| 2 | `WorkGiver_Scanner.PotentialWorkThingsGlobal(Pawn)` | prefix（仅 `__instance is WorkGiver_TurretUpgrade`） | 返回**恰好有排队升级的炮塔列表**，作为 `ClosestThingReachable` 的 `customGlobalSearchSet`；global 阶段只检查这些炮塔。没有排队时返回空列表。 |
| 3 | `WorkGiver_TurretUpgrade.HasJobOnThing(Pawn, Thing, bool)` | prefix | 覆盖不经过 `PawnCanUseWorkGiver` 的直接调用（右键浮动菜单、紧急优先工作、其他模组）：地图上没有排队升级时直接返回 `false`。 |
| 4 | `Comp_TurretUpgrade.PostSpawnSetup(bool)` | postfix | 把新生成/加载的 comp 注册到其地图。注册是 fast-reject 正确性的前提；若此方法缺失，补丁整体不安装。 |

没有全局修改 `WorkGiver_Scanner.ShouldSkip`，没有修改 `GenClosest`，没有修改 `ListerThings`，也没有改任何上游 DLL/XML。补丁只在 `__instance is WorkGiver_TurretUpgrade` 时接管。

No global `ShouldSkip` patch, no `GenClosest` patch, no `ListerThings` patch, and no upstream DLL/XML edits. Every fast path is scoped to `WorkGiver_TurretUpgrade` or `Comp_TurretUpgrade`.

---

## 4. 为什么行为不变 / Why behavior is preserved

1. **“没有排队升级”确实意味着没有工作**：上游 `HasJobOnThing` 对任意 `t` 的第一条判断是 `comp == null || comp.queued.Count == 0 → false`。如果注册表中没有任何 live comp 的 `queued.Count > 0`，那么 `BuildingArtificial` 中的每个候选都会被上游拒绝。补丁只是提前得出同一个结论。
2. **有排队升级时仍然调用上游**：`PawnCanUseWorkGiver` 返回 `true`，`HasJobOnThing` prefix 放行，`JobOnThing`、`FirstDoable`、`FindResourcePiles`、`ReservationUtility.CanReserve`、`TryCompleteFromCarried` 全部原样执行。补丁只改变“看哪些候选”，不改变“候选是否可做”。
3. **搜索集是上游候选集的子集**：列表只包含 `PotentialWorkThingRequest.Accepts(parent)` 为真的已生成炮塔，并且只有 `queued.Count > 0` 的才会进入。上游 global 阶段会对同一组目标调用同一个 `HasJobOnThing`；区域预扫描（≤30 region）保持不变，所以正常情况下的选择顺序仍由原版决定。
4. **注册完整性**：已存在的炮塔由首次 lister 扫描覆盖；之后生成/加载的炮塔由 `PostSpawnSetup` postfix 覆盖；销毁/收纳/换图由查询时修剪处理。没有新增存档字段，卸载补丁不会留下数据。
5. **边界保守**：地图为 `null`、lister 还不可用、或注册表无法初始化时，查询返回“可能有工作”（`true` / `null`），调用方回退原版；未生成 pawn 的 `PawnCanUseWorkGiver` 也保持原版路径。

When nothing is queued, upstream would reject every `BuildingArtificial` candidate anyway, so the short-circuit only reaches the same conclusion earlier. When something is queued, every upstream method still runs; the patch only narrows the global search set to the turrets that can actually pass `HasJobOnThing`. The registry is complete for existing save loads (one lister scan), future spawns (`PostSpawnSetup`), and removals (query-time pruning). No new save data is introduced.

**注意 / Note**：有排队升级时，原版的 ≤30 region 预扫描仍然存在；本补丁消除的是其后对全图数千个建筑的 global 扫描，以及无排队时的整次工作搜索。前者是 10+ ms 的主要来源，后者是用户存档中最常见的 0 炮塔/0 排队场景。

When something *is* queued, the bounded ≤30-region pre-pass still exists. What is removed is the subsequent global walk over thousands of buildings, and the entire giver search when nothing is queued (the common zero-turret case).

---

## 5. 不改什么 / What it does not change

- 不修改 `A:\SteamLibrary\steamapps\workshop\content\294100\3446237098` 下的任何文件。
- 不修改 `Assembly-CSharp.dll`、`CeleTech.Base.dll`、Def、XML 或存档。
- 不改变炮塔升级定义、材料消耗、工作耗时、升级顺序、品质、reservation 或 job 结果。
- 不新增 `ThingComp`、`MapComponent`、`GameComponent` 或 scribed 字段。
- 不处理“有排队但材料不足/被禁用/不可达”的情况；这些仍由原版 `HasJobOnThing`/`JobOnThing` 判定。
- 不保证在 1.8+ 或上游重写该类后仍自动生效；见失效条件。

No Workshop/game DLL, Def, XML or save edits. No new comps/components/scribed fields. Upgrade definitions, costs, work ticks, ordering, quality, reservations and job results are untouched.

---

## 6. 构建 / Build

```powershell
dotnet build .\Source\CeleTechTurretUpgradePerfFix.csproj -c Release
```

`Source` 默认引用本机路径，可用属性覆盖：

```powershell
dotnet build .\Source\CeleTechTurretUpgradePerfFix.csproj -c Release `
  -p:GameRoot="A:\SteamLibrary\steamapps\common\RimWorld" `
  -p:HarmonyPath="A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll" `
  -p:CeleTechPath="A:\SteamLibrary\steamapps\workshop\content\294100\3446237098\1.7\Assemblies\CeleTech.Base.dll"
```

构建目标会把 DLL 复制到 `1.7\Assemblies\`。本补丁在编译期引用真实 `Assembly-CSharp.dll`、`0Harmony.dll` 和 `CeleTech.Base.dll`；运行时由 RimWorld 解析，不分发第三方程序集。

The build references the real game, Harmony and CeleTech assemblies and copies the output DLL to `1.7\Assemblies\`. No third-party assemblies are redistributed.

---

## 7. 离线验证 / Offline validation

验证工程 `Validation\Validation.csproj` 是一个独立控制台程序，加载**真实的** RimWorld 1.7 `Assembly-CSharp.dll` 和**真实的**工坊 `CeleTech.Base.dll`。`bind` 步骤只写 QA 副本到 `Validation\bin\Release\net10.0\bound\`：

- 把 `CeleTech.Base.dll` 的 `UnityEngine.*` 引用重定向到 `UnityStubs.dll`，以便在普通 .NET 宿主中加载；
- 清空 QA 副本中 `Verse.ModsConfig` 的静态构造函数，避免测试读取/写入用户的 `ModsConfig.xml`；
- 原始游戏 DLL 和工坊 DLL **不被修改**。

The harness loads the real 1.7 game assembly and the real Workshop CeleTech assembly. It writes QA-only copies (UnityStubs rebinding + emptied `ModsConfig` cctor) and never touches the installed game or Workshop files.

```powershell
pwsh -File .\Validation\validate.ps1
```

### RED（打补丁前 / before the patch）

Fixture：真实 `Map` + `ListerThings`，5,000 个 `BuildingArtificial` 墙/导管建筑，0 个天工炮塔。

| 场景 / Case | 上游结果 / Upstream |
| --- | --- |
| 5,000 建筑、0 炮塔 | `PawnCanUseWorkGiver == true`；`PotentialWorkThingsGlobal == null`；`ClosestThing_Global` 对 5,000 个候选调用 `HasJobOnThing`，0 个工作 |
| 5,000 建筑 + 1 个排队炮塔 | 对 5,001 个候选调用 `HasJobOnThing`；最后命中炮塔，返回 job |

### GREEN（打补丁后 / after the patch）

| 场景 / Case | 结果 / Result |
| --- | --- |
| 5,000 面墙、0 炮塔的地图 | `PawnCanUseWorkGiver == false`；`PotentialWorkThingsGlobal` 返回空列表；验证器调用 0 次 |
| 5,000 建筑 + 1 个排队炮塔 | `PawnCanUseWorkGiver == true`；`PotentialWorkThingsGlobal` 恰好 1 项；验证器调用 1 次（0 面墙）；`HasJobOnThing`/`JobOnThing` 与上游一致 |
| `TryCompleteFromCarried` / `DevComplete` 完成升级 | 立即回到 0 排队：`PawnCanUseWorkGiver == false`，列表空，验证器 0 次 |
| 炮塔 despawn / destroy | 注册表立即修剪，回到 `false` |
| 炮塔 respawn / 新生成 | `PostSpawnSetup` postfix 立即重新注册，回到 `true` |
| 第二张地图 | 独立懒初始化，互不串扰 |
| 非 CeleTech 的 `WorkGiver_Scanner` | `PotentialWorkThingsGlobal` 仍是原版 `null`，prefix 不泄漏 |
| 未生成 pawn（`Map == null`） | 保持原版路径，不强制 `false` |

验证输出摘要：

```text
RED:   upstream zero-turret scan: 5,000 candidates validated, 0 jobs.
RED:   upstream one-turret scan: 5,001 candidates validated (5,000 walls + 1 turret).
GREEN: four patches registered on the intended methods only.
GREEN: zero-turret case: 5,000 walls present, 0 candidates validated.
GREEN: queued case: 1 candidate (the turret), 0 walls.
GREEN: completed case: 0 candidates, 0 walls; unspawned-pawn fallback preserved.
GREEN: despawn/respawn/destroy/new-spawn registration all agree with upstream state.
GREEN: O(1) zero-queued tracker query: ~0.12 us/call (100,000 calls).
PASS: 9 RED assertions + 46 GREEN assertions.
```

离线测试不运行 Unity 场景、游戏循环或 XML 加载；它验证的是补丁实际改变的那条 managed 路径（`PawnCanUseWorkGiver`、`PotentialWorkThingsGlobal`、`HasJobOnThing`、`Comp_TurretUpgrade.PostSpawnSetup`、注册表与 `GenClosest.ClosestThing_Global` 的搜索集）。

The offline test does not run a Unity scene, game loop or XML load. It exercises the actual managed path the patch changes: the giver gate, the search set, the validator and the registration/pruning lifecycle.

**2026-11-13 用户确认游戏内验证通过**：日志 `[CeleTech Turret Upgrade Perf Fix] active (4/4 patches)`，DPA `Work Giver` 中不再出现炮塔升级扫描热点；本次 QA 状态更新保持已验证的 1.0.0 DLL 逐字节不变。

**In-game QA confirmed by the user on 2026-11-13**: the log shows `[CeleTech Turret Upgrade Perf Fix] active (4/4 patches)` and the turret-upgrade scan no longer appears in DPA's `Work Giver`; the validated 1.0.0 DLL is unchanged.

---

## 8. 安装 / Install

1. 关闭 RimWorld（本补丁不改运行中的进程；新 DLL 需要下次启动才加载）。
2. 将 `About\`、`LoadFolders.xml`、`1.7\` 复制到 `RimWorld\Mods\CeleTechTurretUpgradePerfFix\`。不要复制 `Source\`、`Validation\`、`bin\`、`obj\`。
3. 在模组列表中把本补丁排在 Harmony 和 CeleTech Arsenal MKIII 之后并启用。
4. 启动游戏。成功日志：

```text
[CeleTech Turret Upgrade Perf Fix] active (4/4 patches): per-map queued-turret registry.
```

若看到 `patch not applied`、`could not resolve`、`startup failed` 或 `a target method was not found`，说明上游签名已变化，补丁未生效；按失效条件处理。

Close the game, copy `About\`, `LoadFolders.xml` and `1.7\` into `RimWorld\Mods\CeleTechTurretUpgradePerfFix\`, and enable it after Harmony and CeleTech Arsenal MKIII. The success log line is shown above; any `patch not applied` / `could not resolve` / `startup failed` line means the patch is inactive.

卸载：关闭游戏、禁用本模组即可；没有存档字段需要清理。

Uninstall: disable the mod. Nothing is scribed, so there is nothing to clean up in saves.

---

## 9. 失效条件 / Expiration conditions

本补丁针对 **CeleTech Arsenal MKIII 1.7 工坊 `3446237098`，`CeleTech.Base.dll` SHA256 `8db0c896bbf8c66dbec31e32f69f0facf4a8a02efd11ef8a3a8e3fcdc82f3678`** 验证。出现以下任一情况时应重新验证，通常可以直接退役：

1. 上游在 `WorkGiver_TurretUpgrade` 中覆盖 `ShouldSkip` 或 `PotentialWorkThingsGlobal`，或把 `PotentialWorkThingRequest` 从 `BuildingArtificial` 收窄。
2. 上游把 `Comp_TurretUpgrade.queued` 改名为其他字段/属性，或改为非 `IList` 类型。启动时会记录 `Comp_TurretUpgrade.queued is no longer a list field` 并拒绝安装。
3. 上游移除/改名 `Comp_TurretUpgrade.PostSpawnSetup(bool)`、`WorkGiver_TurretUpgrade.HasJobOnThing(Pawn, Thing, bool)`、`JobGiver_Work.PawnCanUseWorkGiver(Pawn, WorkGiver)` 或 `WorkGiver_Scanner.PotentialWorkThingsGlobal(Pawn)`。对应补丁会记录错误并停用；`PostSpawnSetup` 缺失时整个补丁不安装。
4. 游戏升级到 1.8+ 或 `Assembly-CSharp.dll` 的工作搜索逻辑变化。重新跑 `Validation\validate.ps1`，RED/GREEN 任一失败即不要部署。
5. 上游自己修复后，不要与本补丁叠加；删除本补丁即可。

This build was validated against the exact DLL hash above. If upstream adds `ShouldSkip`/`PotentialWorkThingsGlobal`, renames `queued`, changes `PostSpawnSetup`/`HasJobOnThing`/`PawnCanUseWorkGiver`, or the game version changes, re-run the harness. Any startup error line means the patch did not apply.

---

## 10. 给作者的留言草稿 / Friendly message draft for the author

### 中文

> 你好！我是天工铸造（CeleTech Arsenal MKIII，工坊 `3446237098`）的玩家，也是一个写 RimWorld 补丁的开发者。冒昧打扰，想反馈一个 1.7 的性能问题，并附上我本地验证过的独立补丁，供你参考。
>
> **现象**：在大型基地里，每个殖民者/机械体每次 `Construction` 工作搜索都会卡一下；用性能分析器看，热点在 `CeleTech.Base.WorkGiver_TurretUpgrade`。即使地图上一座天工炮塔都没有、没有任何排队升级，也会扫描全地图的墙、导管、门和货架。
>
> **原因**：`WorkGiver_TurretUpgrade.PotentialWorkThingRequest` 返回 `ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial)`，但没有覆盖 `ShouldSkip` 或 `PotentialWorkThingsGlobal`。`JobGiver_Work` 于是用 `null` 的自定义搜索集调用 `GenClosest.ClosestThingReachable`；区域预扫描之后，global 阶段会对地图上每一个 `BuildingArtificial` 调用 `HasJobOnThing`，而其中绝大多数在 `TryGetComp<Comp_TurretUpgrade>` 就返回 `null`。
>
> **建议的最小上游修复**：在 `WorkGiver_TurretUpgrade` 内维护/查询一个“有排队升级的炮塔”集合（或在 `Comp_TurretUpgrade` 的 `Enqueue`/`DevComplete`/`TryCompleteFromCarried`/`PostSpawnSetup` 处维护），然后：
>
> ```csharp
> public override bool ShouldSkip(Pawn pawn, bool forced = false)
>     => pawn?.Map == null || !AnyQueuedUpgradeOn(pawn.Map);
>
> public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
>     => QueuedUpgradeTurretsOn(pawn?.Map) ?? base.PotentialWorkThingsGlobal(pawn);
> ```
>
> 这样 0 排队时在 `PawnCanUseWorkGiver` 就跳过，有排队时也只把真正有升级的炮塔交给原版验证器；`HasJobOnThing`、材料、reservation 和升级顺序都不用改。
>
> 我写了一个不修改你任何文件的独立 Harmony 补丁（`whitegiver.celetech.turretupgradeperffix`），用真实 1.7 `Assembly-CSharp.dll` + 真实 `CeleTech.Base.dll` 做了离线 RED/GREEN：5,000 个建筑 + 0 炮塔时，上游验证 5,000 次，补丁后 0 次；1 个排队炮塔时上游 5,001 次，补丁后 1 次。补丁会在你的类/字段缺失时记录错误并自动停用，不会静默改行为。如果你愿意，我可以把实现细节、测试夹具或 PR 草稿发给你；也完全理解如果你选择在自己的代码里用更合适的方式修复。
>
> 感谢你做的模组，天工铸造的炮塔和升级系统真的很有特色。

### English

> Hi! I'm a player of CeleTech Arsenal MKIII (Workshop `3446237098`) and a RimWorld patch developer. Sorry for the unsolicited message — I wanted to report a 1.7 performance issue and share a locally validated standalone patch for reference.
>
> **Symptom**: on large bases, every `Construction` work search for every colonist/mech hitches. Profiling points at `CeleTech.Base.WorkGiver_TurretUpgrade`. It scans every wall, conduit, door and shelf on the map even when there are zero CeleTech turrets and zero queued upgrades.
>
> **Cause**: `WorkGiver_TurretUpgrade.PotentialWorkThingRequest` returns `ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial)`, but the class does not override `ShouldSkip` or `PotentialWorkThingsGlobal`. `JobGiver_Work` therefore calls `GenClosest.ClosestThingReachable` with a null custom search set; after the bounded region pre-pass, the global phase calls `HasJobOnThing` for every `BuildingArtificial` thing, and almost all of them return `null` at `TryGetComp<Comp_TurretUpgrade>`.
>
> **Suggested minimal upstream fix**: keep a per-map set of turrets with queued upgrades (updated from `Comp_TurretUpgrade.Enqueue` / `DevComplete` / `TryCompleteFromCarried` / `PostSpawnSetup`), then override:
>
> ```csharp
> public override bool ShouldSkip(Pawn pawn, bool forced = false)
>     => pawn?.Map == null || !AnyQueuedUpgradeOn(pawn.Map);
>
> public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
>     => QueuedUpgradeTurretsOn(pawn?.Map) ?? base.PotentialWorkThingsGlobal(pawn);
> ```
>
> With nothing queued the giver is skipped at `PawnCanUseWorkGiver`; with something queued only the turrets that can actually pass the original validator are handed to `GenClosest`. `HasJobOnThing`, costs, reservations and upgrade order stay untouched.
>
> I wrote a standalone Harmony patch (`whitegiver.celetech.turretupgradeperffix`) that edits none of your files. I validated it offline against the real 1.7 `Assembly-CSharp.dll` and the real `CeleTech.Base.dll`: with 5,000 buildings and 0 turrets, upstream validates 5,000 candidates and the patch validates 0; with one queued turret, upstream validates 5,001 and the patch validates 1. The patch logs and disables itself if your classes/fields are missing rather than silently changing behavior. Happy to share implementation details, the test fixture, or a PR draft — and I completely understand if you'd rather fix it upstream in your own way.
>
> Thanks for the mod — the turret and upgrade system is really distinctive.

---

## 11. 文件与哈希 / Files and hashes

| 文件 / File | SHA256 |
| --- | --- |
| `1.7/Assemblies/CeleTechTurretUpgradePerfFix.dll` | `ce73e437a65d417647f7e72e908844aed3e0b473b3bdd65e79f918cffc9d4350` |
| 上游 `CeleTech.Base.dll`（验证基准 / validation baseline） | `8db0c896bbf8c66dbec31e32f69f0facf4a8a02efd11ef8a3a8e3fcdc82f3678` |

构建：`net10.0`，`TreatWarningsAsErrors=true`，0 warnings / 0 errors。验证：9 RED + 46 GREEN assertions。

Built with `net10.0`, `TreatWarningsAsErrors=true`, 0 warnings / 0 errors. Validation: 9 RED + 46 GREEN assertions.
