# 边缘米莉拉养成：属性查询性能修复 / MTOEWY Stat Perf Fix

`packageId: whitegiver.mtoewy.statperffix` · RimWorld 1.7 · Harmony 3.x（随 1.7 工坊版本，实测 3.0.0.0）

一个针对 **「边缘米莉拉养成：与你相连之人」**（`FunnyFlandre.milira.theoneentwinedwithyou`，Workshop `3814027507`）的纯运行时性能补丁。它把该 mod 挂在 `StatExtension.GetStatValue` 上的属性 Postfix 换成**零分配、单次字典分派**的等价实现，不改工坊文件、不写存档、可随时增删。

A standalone runtime performance patch for **MTOEWY** (Workshop `3814027507`). It replaces the mod's `StatExtension.GetStatValue` postfix with an allocation-free dispatcher that preserves every upstream result bit-for-bit. No Workshop files are edited, nothing is scribed, and the mod can be added or removed at any time.

---

## 1. 根因 / Root cause

### 1.1 挂点 / The hook

MTOEWY 1.4.2（程序集 `MiliraTheOneEntwinedWithYou, Version=1.4.0.0`）在 `MiliraTheOneEntwinedWithYou.Patches.IdealExclusiveStatPatch` 中给**全游戏最热的属性入口**加了一个 Postfix：

```csharp
[HarmonyPatch(typeof(StatExtension), "GetStatValue",
    new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) })]
internal static class IdealExclusiveStatPatch
{
    private static void Postfix(Thing __0, StatDef __1, ref float __result)
    {
        Pawn pawn = __0 as Pawn;
        if (pawn == null) return;

        __result = IdealBackstoryBonuses.Adjust(pawn, __1.defName, __result);
        if (__1.defName == "MoveSpeed" || __1.defName == "ConstructionSpeed" || __1.defName == "JoyFallRateFactor")
            __result = IdealExclusiveTraits.AdjustStat(pawn, __1.defName, __result);
        if (__1 == StatDefOf.MoveSpeed)
            __result *= IdealWorldEffectService.MoveFactor(pawn);
        if (__1.defName == "MoveSpeed" || __1.defName == "AimingDelayFactor" || __1.defName == "MeleeDodgeChance")
            __result = IdealEquipment.Stat(pawn, __1.defName, __result);
    }
}
```

`StatExtension.GetStatValue`（`Assembly-CSharp` 反编译行 712076）只是 `stat.Worker.GetValue(thing, applyPostProcess, cacheStaleAfterTicks)` 的一层薄封装。它是**所有** Thing（不是只有 Pawn）的属性读取入口，被移动、寻路、工作、战斗、UI、AI 每帧成千上万次调用。只要它被 Harmony 改写过一次，每次调用都要走替换方法。

### 1.2 每一次 Pawn 属性查询都做了什么 / What every pawn stat query does

**(a) `IdealBackstoryBonuses.Adjust`（Progression，反编译行 ~31477）**

```csharp
internal static float Adjust(Pawn pawn, string stat, float value)
{
    if (Array.IndexOf(ChildStats, stat) < 0 && Array.IndexOf(AdultStats, stat) < 0)
        return value;                                  // 4 + 5 个 defName 逐项比较
    if (IdealExclusiveTraits.Owner(pawn) != null)
        return Value(stat, value, ChildhoodActive(pawn), AdulthoodActive(pawn));
    return value;
}
```

对**所有** Pawn（鸡、野兔、机械体……）都先做最多 9 次字符串比较，再决定是否继续；即使这个 Pawn 根本没有 `story`（动物/机械体永远不可能有童年/成年背景），也照做不误。

**(b) `IdealExclusiveTraits.Owner`（Progression，反编译行 ~31661）**

```csharp
GameComponent_IdealTracker t = Current.Game?.components == null
    ? null
    : Current.Game.GetComponent<GameComponent_IdealTracker>();  // 线性扫描 Game.components
if (pawn == null || t == null || Scribe.mode != LoadSaveMode.Inactive
    || !t.IsCurrentIdeal(pawn)                                // ← 关键
    || t.RuntimeCache.ResolvedPawn != pawn
    || t.State.P4Phase != IdealP4Phase.Active
    || t.HasOriginalRecoveryTransaction
    || pawn.Dead || pawn.Destroyed || pawn.Discarded)
    return null;
return t;
```

`Game.GetComponent<T>()`（`Assembly-CSharp` 行 50431）是 `for (i = 0; i < components.Count; i++) if (components[i] is T) ...`——每调用一次就从头扫描整个组件列表。

`IsCurrentIdeal(Pawn)` 会调用 `state.IsCurrentIdeal(pawn.GetUniqueLoadID())`，而 `Thing.GetUniqueLoadID()` 是：

```csharp
public string GetUniqueLoadID() => "Thing_" + ThingID;      // ThingID => def.defName + thingIDNumber
```

即**每次调用分配 2~3 个字符串**（`int.ToString()`、`defName + number`、`"Thing_" + ...`）。哪怕这个 Pawn 不是艾蒂尔、哪怕 `RuntimeCache.ResolvedPawn != pawn` 早就该短路，字符串也已经分配完了。

**(c) `IdealWorldEffectService.MoveFactor`（Abilities，反编译行 ~73705）**

```csharp
GameComponent_IdealTracker t = IdealKitRuntime.Owner;        // 又扫描一次 Game.components
if (target == null || !target.Spawned || t?.State.WorldEffects == null) return 1f;
foreach (IdealWorldEffect effect in t.State.WorldEffects.Effects) { ... }
return 1f;
```

`Effect` 列表为空（99.9% 的游戏时间）时，它仍然会：解析一次 `IdealKitRuntime.Owner`（再扫一遍组件）+ 跑一次 `foreach`。注意 `IdealWorldEffectState.Effects` 是 `public List<IdealWorldEffect>`（`Data`，反编译行 64775），`foreach` 用的是 `List<T>.Enumerator` 结构体，**本身不分配**——真正的分配在 (b) 的 `GetUniqueLoadID()`。这里白白付出的是第二次组件扫描和一次永远为 1.0 的乘法。

**(d) `IdealEquipment.Stat`（Abilities，反编译行 ~69205）**

```csharp
bool aidier = IsAidier(pawn);                                // 第三次 Owner() → 第三次扫描 + 字符串
if (stat == "MoveSpeed" && Holds(pawn, "MTOEWY_StarlightRailgun") && !aidier) value = Math.Max(0.1f, value - 0.3f);
if (stat == "AimingDelayFactor" && Wears(pawn, "MTOEWY_NightwingSuit")) value *= 0.8f;
...
```

`Holds` / `Wears` 对没有武器、没有穿衣服的动物/机械体必然是 `false`，但 `IsAidier` 仍然先跑完整个 `Owner()`。

### 1.3 实测代价 / Measured cost

离线 harness（真实 `Assembly-CSharp.dll` + 真实 `MiliraTheOneEntwinedWithYou.dll`，24 个 `GameComponent` 以模拟真实组件列表长度），每个场景 100,000 次真实 `StatExtension.GetStatValue`：

| 场景 | 上游分配 | 每次 | 组件扫描（计数下限） | 本补丁 |
|---|---:|---:|---:|---:|
| 普通人类 Pawn 的 `MoveSpeed` | 32,000,000 B | **320 B** | ≥ 3 次 | **0 B** |
| 动物 `MoveSpeed` | 32,000,000 B | **320 B** | ≥ 3 次 | **0 B** |
| 艾蒂尔（4 特性 + 长枪 + 夜翼服）`MoveSpeed` | 64,000,000 B | **640 B** | ≥ 6 次 | **0 B** |
| 艾蒂尔（圣辉长枪）`MoveSpeed` | 64,000,000 B | **640 B** | ≥ 4 次 | **0 B** |
| 艾蒂尔 `GlobalLearningFactor`（童年背景） | 16,000,000 B | **160 B** | ≥ 1 次 | **0 B** |
| 无关属性 `ShootingAccuracyPawn` | 0 B | 0 B | 0 次 | **0 B** |

隔离 Postfix 后的微基准（1,000,000 次，排除 `StatWorker` 噪声）：

| 场景 | 上游 | 本补丁 |
|---|---:|---:|
| `MoveSpeed`（普通 Pawn） | 489.1 ns / 320 B | **25.4 ns / 0 B** |
| `MoveSpeed`（艾蒂尔 + 长枪） | 395.4 ns / 640 B | **82.2 ns / 0 B** |
| `GlobalLearningFactor`（艾蒂尔） | 95.1 ns / 160 B | **29.9 ns / 0 B** |
| 无关属性 | 26.0 ns / 0 B | **8.3 ns / 0 B** |

> 绝对值会随机器负载浮动（多次运行可见 2 倍以上差异，README 与本目录 `validation.log` 的绝对值可能不同），但“上游分配、本补丁 0 分配”以及每次运行中本补丁单次耗时更低这两个结论是稳定的。真正要命的不是 300 ns，而是 330 个 Pawn 持续产生的 32~64 MB/10 万次查询的垃圾——这会把 GC 压力推高，表现为随殖民地规模增长的卡顿。

---

## 2. 修复机制 / Mechanism

### 2.1 三道快速闸门 / Three fast gates

1. **StatDef 分派（Fast Gate 1）**：启动后第一次查询时，从 `DefDatabase<StatDef>` 解析**上游唯一可能修改的 14 个 `StatDef`**（4 个童年 + 5 个成年 + 5 个特性/世界/装备），存进以**引用相等**为键的 `Dictionary<StatDef, StatId>`。其余几百个属性一次查表即返回：**零字符串比较、零 `Game.GetComponent`、零分配**。`DefDatabase<StatDef>` 数量、首个 Def 实例或 `StatDefOf.MoveSpeed` 变化时自动重建（覆盖 dev-mode 重载 defs）。
2. **缓存 `GameComponent_IdealTracker`（Fast Gate 2）**：以 `Current.Game` 引用和 `Game.components` 列表引用为缓存键，每个游戏实例只做一次线性扫描；`FastOwner` 进一步把 `RuntimeCache.ResolvedPawn == pawn` 提到 `IsCurrentIdeal` 之前（上游顺序相反），并缓存 `pawn.GetUniqueLoadID()` 字符串——**每个 Pawn 只分配一次**，之后 0 分配。`Current.Game` setter 上有一个极小的生命周期钩子，在换存档/新殖民地/回主菜单时释放缓存，避免静态字段持有旧存档的对象图。
3. **零分配 `MoveFactor` 与快速背景/装备检查（Fast Gate 3）**：
   - 9 个背景属性：`pawn.story == null` 直接返回（动物/机械体永远如此）；否则只在 `FastOwner != null` 时做与上游完全相同的 1.1/0.9/+0.1/+10/`Max(0, v-0.05)` 运算。
   - `MoveSpeed` 等 5 个属性：检查 `WorldEffects.Effects.Count`；为 0 时直接跳过（连 `MoveFactor` 都不调用，0 分配）；**大于 0 时仍然调用上游的 `IdealWorldEffectService.MoveFactor(pawn)`**，保证自我 1.2×、敌人 0.3×、`BlocksControl` 等逻辑 100% 由上游执行。
   - `IdealEquipment.Stat` 的等价实现：`equipment?.Primary == null && (apparel == null || WornApparelCount == 0)` 时整段无操作直接返回；否则用缓存 Owner + 直接 `defName` 比较完成 Holds/Wears，只在真正持有圣辉长枪/圣辉之矛时调用上游 `IdealKitRuntime.Active(...)`。

### 2.2 安装顺序与回滚 / Install order and rollback

```
1. UpstreamRefs.Resolve()             // 通过反射绑定 4 个上游内部方法（失败则完全禁用）
2. 找到 GetStatValue 上真实的 MTOEWY Postfix（按声明类型全名匹配）
3. 先挂上我们的 Postfix
4. 再 Unpatch 上游 Postfix
5. 用 Harmony.GetPatchInfo 验证：我们的在、上游的不在
6. 任一验证失败 → 撤掉我们的 Postfix，保留上游原样（绝不出现“两边都没了”）
```

另外在 `LongEventHandler.ExecuteWhenFinished` 时再检查一次（`RepairIfNeeded`）：如果因为异常的加载顺序让上游 Postfix 又出现，则再次移除；重复调用 `Install()` 是幂等的。如果加载的是未验证过的上游版本（程序集版本 ≠ `1.4.0.0`），会在日志里给出警告。

### 2.3 为什么不是 prefix 上游的 Postfix / Why not prefix the upstream postfix

Harmony 3 在生成 `GetStatValue` 包装方法时会把 Postfix 直接绑定到生成时的委托/方法入口；之后再给这个 Postfix 方法挂 Prefix 不会拦截包装方法里的调用（本仓库 harness 已实测：前缀调用计数为 0，原 Postfix 照常执行）。因此可靠的做法只有“从 `GetStatValue` 上移除 + 安装等价 Postfix”，本补丁采用该方案，并在安装时校验补丁表。

---

## 3. 验证 / Validation

`Validation/` 是一个离线可执行程序，加载**真实的** RimWorld 1.7 `Assembly-CSharp.dll`（`1.7.9810.32479`）与**真实的** `MiliraTheOneEntwinedWithYou.dll`（`1.4.0.0`，SHA256 `57be3c90…287240`），构造真实的 `Game` / `Map` / `Pawn` / `Pawn_HealthTracker` / `Pawn_AgeTracker` / `Pawn_StoryTracker` / `TraitSet` / `Pawn_EquipmentTracker` / `Pawn_ApparelTracker` / `GameComponent_IdealTracker` 图，并直接调用真实的 `StatExtension.GetStatValue`。

```
Validation> dotnet run -c Release
```

验证流程：

1. **Vanilla 基线**：未加任何补丁时 `GetStatValue` 稳态 0 分配（先跑两遍，第二遍计入）。
2. **挂上真实上游 Postfix**：用 Harmony 把 `IdealExclusiveStatPatch.Postfix` 挂到真实 `GetStatValue`。
3. **RED**：100,000 次调用，记录 `GC.GetAllocatedBytesForCurrentThread()`、`Game.GetComponent<...>` 调用次数、耗时。断言上游在 `MoveSpeed` / 背景属性上确实分配，且 `MoveSpeed` 至少 2 次组件扫描。
4. **Phase A 矩阵**：18 个场景 × 16 个属性（14 个受影响 + 2 个无关）= **288 个值**，全部通过真实上游 Postfix 计算。
5. **安装本补丁**：断言补丁表只剩我们的一个 Postfix。
6. **GREEN**：同样 100,000 次调用，断言 **0 字节分配**；重跑 288 个值，**逐位比较**（NaN 用 `IsNaN` 归一，其余用 `BitConverter.SingleToInt32Bits`）。
7. **隔离 Postfix 微基准**：1,000,000 次直接调用上游/本补丁 Postfix，断言本补丁 0 分配且更快。
8. **幂等性**：第二次 `Install()` 不重复挂载。
9. **安全网**：模拟“上游在之后又挂回来”，调用 `RepairIfNeeded()`，断言被再次移除且 288 个值仍逐位一致。

覆盖的场景：童年+成年背景激活 / 未激活、未生成（unspawned）、死亡、`Discarded`、4 特性全开、`BurstActive`、`NeedsStudy`、Repair 工作、圣辉之矛 / 圣辉长枪 / 夜翼服、非艾蒂尔持枪、动物、机械体、Saving 模式（`Scribe.mode = Saving`）、世界效果命中自己/敌人/范围外、`MoveSpeed = NaN`、`ConstructionSpeed = +∞`。

本机最后一次完整运行：**60 checks, 0 failures**（日志见 `C:\Users\A1337\AppData\Local\Temp\opencode\mtoewy-stat-perf-20261113\validation.log`）。

> 说明：整个 `GetStatValue` 的耗时由 `StatWorker` 的属性计算主导，因此“隔离 Postfix 微基准”才是补丁本身开销的可靠指标；整调用耗时仅作参考。

---

## 4. 过期条件 / Expiration conditions

本补丁是**版本钉死**的等价替换。出现以下任一情况时请重新审计，或直接停用本补丁：

1. **上游不再把 `IdealExclusiveStatPatch.Postfix` 挂在 `StatExtension.GetStatValue` 上**（删除/改名/改挂点）。安装器找不到目标会记录警告并保持禁用，上游行为不受影响。
2. **上游修改了受影响的属性列表**（新增第 15 个属性）**或修改了任何倍率/常量**（`1.1 / 0.9 / +0.1 / +10 / -0.05 / +0.3 / +0.4 / ×1.35 / ×1.15 / ×0.8 / -0.3 / +0.1`）。本补丁的 `UpstreamLogic` 是 1.4.2 逻辑的逐行移植；新增属性会被**漏掉**。
3. **上游修改了特性/装备的 `defName`**（`MTOEWY_Trait_Skystriker` / `DawnAegis` / `StarlightTinkerer` / `InquisitiveMind`、`MTOEWY_StarlightRailgun` / `StarlightLance` / `NightwingSuit`、`MTOEWY_Reward_MeleeMastery` / `RangedMastery`、`MTOEWY_IdealChildhoodPlaceholder` / `Remembered` / `AdulthoodPlaceholder`）。
4. **其它 mod 也给 `StatExtension.GetStatValue` 加 Postfix**：数值仍然只应用一次，但 Harmony 同优先级 Postfix 按挂载顺序执行；我们的替换位于列表末尾，组合顺序可能与上游原位不同。安装器会记录警告。若对方是乘法型修正且与 MTOEWY 的加法型修正组合，结果可能有细微差异。
5. **Harmony 大版本改变 `Unpatch`/补丁表语义**：安装器会校验并在失败时回滚，不会留下“半套”状态。

已验证目标：`MiliraTheOneEntwinedWithYou.dll` 1.4.0.0（Workshop `3814027507`，1452032 字节，SHA256 `57be3c906c46ad9cec957b812fb1398cf650edec70ec79aefdacaec753287240`），RimWorld `Assembly-CSharp` 1.7.9810.32479。

---

## 5. 构建与部署 / Build and deploy

```powershell
# 构建（Release，0 warning / 0 error，自动复制 DLL 到 1.7/Assemblies）
dotnet build Source\MTOEWYStatPerfFix.csproj -c Release

# 离线 RED/GREEN 验证
dotnet run --project Validation\Validation.csproj -c Release
```

部署：把 `About\About.xml`、`LoadFolders.xml`、`1.7\Assemblies\MTOEWYStatPerfFix.dll`、`README.md` 复制到 `RimWorld\Mods\MTOEWYStatPerfFix\`，保持 SHA256 一致，然后在 Mod 列表中启用（放在 MTOEWY 之后，`loadAfter` 已声明依赖）。**不要**手动改 `ModsConfig.xml`（由上层流程统一处理）。

---

## 6. 给作者的留言草稿 / Friendly message draft

> FunnyFlandre 你好：
>
> 我在 1.7 上做性能排查时发现，`IdealExclusiveStatPatch.Postfix` 挂在 `StatExtension.GetStatValue` 上，会让**每个 Pawn 的每一次属性查询**都走一遍：
>
> - `IdealBackstoryBonuses.Adjust` 对 9 个 `defName` 做 `Array.IndexOf`（动物/机械体也会走）；
> - `MoveSpeed` 查询里 `IdealExclusiveTraits.Owner`、`IdealWorldEffectService.MoveFactor`、`IdealEquipment.Stat` 各自再调用一次 `Current.Game.GetComponent<GameComponent_IdealTracker>()`，而 `Owner` 里的 `IsCurrentIdeal(pawn)` 会调用 `pawn.GetUniqueLoadID()`，每次分配 2~3 个字符串。
>
> 离线实测（真实 Assembly-CSharp + 你的 DLL，24 个 GameComponent）：非艾蒂尔 Pawn 的 `MoveSpeed` 每次查询分配 **320 B**，艾蒂尔本人 **640 B**；10 万次就是 32~64 MB。330 只动物/机械体一起跑时 GC 压力会很明显。
>
> 我写了一个独立 Harmony 补丁（不改工坊文件），把 Postfix 换成等价快速实现：按 StatDef 分派（只处理你会改的 14 个属性）、缓存 `GameComponent_IdealTracker`、世界效果列表为空时直接跳过 `MoveFactor`；世界效果生效时仍调用你的 `MoveFactor`。离线对比 288 组场景（童年/成年背景、特性、装备、世界效果、动物/机械体、Saving、NaN/Inf）逐位相同。
>
> 如果你的版本以后新增了会被 Postfix 修改的属性，或调整了倍率常量，请告诉我，我会同步更新；也欢迎你直接把这套 gate 合进上游——那样这个补丁就可以退休了。
>
> 谢谢！

---

## 7. 文件 / Files

```
About/About.xml
LoadFolders.xml
1.7/Assemblies/MTOEWYStatPerfFix.dll
Source/MTOEWYStatPerfFix.csproj
Source/Bootstrap.cs             安装/回滚/校验
Source/FastStatPostfix.cs       替换 Postfix + 14 属性分派
Source/StatDispatch.cs          StatDef → StatId 表（延迟解析）
Source/FastOwner.cs             缓存的 Owner 判定
Source/TrackerCache.cs          Game/组件/load-id 缓存
Source/UpstreamRefs.cs          上游内部方法委托绑定
Source/UpstreamLogic.cs         背景/特性/世界/装备逻辑的逐行移植
Validation/                     离线 RED/GREEN harness
README.md
```
