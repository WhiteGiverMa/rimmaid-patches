# Vanilla Fire Modes 性能修复 / Vanilla Fire Modes Perf Fix

- packageId: `whitegiver.vfm.perffix`
- 版本 / version: `1.0.0`
- 目标 / target: RimWorld `1.7`（验证于 `1.7.5005 rev1082`）
- 依赖 / requires: `brrainz.harmony`, `aliza.vanillafiremodes`（Vanilla Fire Modes `1.8.0`，Workshop `3662471742`）
- 加载顺序 / load order: `loadAfter` 两者
- 存档 / saves: 不写入任何自定义字段，可随时增删

---

## 1. 一句话 / In one sentence

**中文**：Vanilla Fire Modes 为了清理「刚结束连发」的少数 Verb，让殖民地里**每一个** Verb 每 tick 都去查一次 `ConditionalWeakTable`；本补丁把那三个热路径补丁整体接管，改成**用 `Verb.state` 直接推导缓存是否有效**，于是空闲殖民地每帧 0 次查表、0 次分配，而连发期间的弹数与 VFM 逐位相同。

**English**: To clean up the handful of verbs that just finished a burst, Vanilla Fire Modes makes **every** verb in the colony probe a `ConditionalWeakTable` every tick. This mod takes over VFM's three hot patches and derives cache validity from `Verb.state` instead, so an idle colony performs zero table probes and zero allocations per frame while every live burst keeps the exact same shot count as before.

---

## 2. 现象 / Symptom

Dubs Performance Analyzer 在 Harmony 页把 `Vanilla Fire Modes` 记为 **0.327 ms/frame**，且该值随殖民地**总 Verb 数**线性增长 —— 与「有多少人在开枪」无关。一份 1,200 个 Verb（其中 900 个是动物喙/爪之类的近战 Verb）的殖民地，每帧约有 **10,000 次 `Verb.VerbTick` 调用**，即约 10,000 次纯查表开销。

Dubs Performance Analyzer reports **0.327 ms/frame** for `Vanilla Fire Modes` on its Harmony page, and the number scales with the **total number of verbs** in the colony rather than with how many pawns are shooting. A colony with 1,200 verbs (900 of them animal melee verbs) makes roughly **10,000 `Verb.VerbTick` calls per frame**, i.e. roughly 10,000 table probes.

---

## 3. 根因 / Root cause

反编译对象：`A:\SteamLibrary\steamapps\workshop\content\294100\3662471742\1.7\Assemblies\VFM_VanillaFireModes.dll`
（51,712 bytes，sha256 `51a9e8696ec63c0cdd7dc8da515c082cc036fb099783776fc61f7ac439eab849`，`ilspycmd -il` 全量阅读）

整个 DLL 里只有两个打了 Harmony 补丁的类：

| 类 | 目标 | 类型 |
|---|---|---|
| `VFM_VanillaFireModes.Patches.Patch_BurstShotCount.LockCount` | `Verse.Verb.WarmupComplete` | Prefix |
| `VFM_VanillaFireModes.Patches.Patch_BurstShotCount.BurstShotCountPostFix` | `Verse.Verb.get_BurstShotCount`（`MethodType.Getter`） | Postfix |
| `VFM_VanillaFireModes.Patches.Patch_BurstShotCount.Postfix_Cleanup` | `Verse.Verb.VerbTick` | Postfix |
| `VFM_VanillaFireModes.Patches.Patch_TryStartCastOn.Prefix` | `Verb.TryStartCastOn(6 args)` | Prefix（**本补丁不动它**） |

存储是一个按 Verb 弱键的 `ConditionalWeakTable`：

```csharp
private static readonly ConditionalWeakTable<Verb, BurstCacheState> _burstCache = new();
private sealed class BurstCacheState { public int Count; public bool IsValid; }
```

### 3.1 `VerbTick`：每 tick 对每个 Verb 无条件查表 / one probe per verb per tick

```csharp
[HarmonyPatch(typeof(Verb), "VerbTick")]
[HarmonyPostfix]
public static void Postfix_Cleanup(Verb __instance)
{
    if ((int)__instance.state != 1 && _burstCache.TryGetValue(__instance, out BurstCacheState value) && value.IsValid)
        value.IsValid = false;
}
```

- `state != VerbState.Bursting` 对**不是正在连发**的 Verb 恒为真。殖民地里 99.99% 的 Verb（鸡喙、拳头、待命步枪、炮塔、能力）永远不处于 `Bursting`，所以**每一 tick 都会真正执行 `TryGetValue`**。
- `TryGetValue` 在 .NET 里是「哈希 + `DependentHandle` 弱引用解析 + 内部锁」的一条不便宜路径；10,000 次/帧就是可观测的固定开销。
- 关键在于：**即使一个缓存条目都没有**（开局、纯近战殖民地），这个补丁依然对每个 Verb 查一次表。

- `state != VerbState.Bursting` is true for every verb that is not bursting, which is 99.99% of them, so the `TryGetValue` really runs on every tick for every verb: chickens' beaks, colonists' fists, idle rifles, turrets, abilities.
- `TryGetValue` is not cheap (hash, weak-handle resolution, internal lock); 10,000 of them per frame is a measurable constant cost.
- The important part: even with **zero** cached entries (early game, melee-only colony) it still probes once per verb per tick.

### 3.2 `BurstShotCount`：每次读取都查表，命中后还要重跑一次资格判断 / probe on every read

```csharp
[HarmonyPatch(/* Verb.BurstShotCount, MethodType.Getter */)]
[HarmonyPostfix]
public static void BurstShotCountPostFix(Verb __instance, ref int __result)
{
    if (_burstCache.TryGetValue(__instance, out BurstCacheState value) && value.IsValid && ShouldModify(__instance, out Pawn _, out ThingWithComps _))
        __result = value.Count;
}
```

`Verb.BurstShotCount` 在连发期间每次 `TryCastShot` 都会被读（`Verb_LaunchProjectile.TryCastShot` 里的 `forcedMissRadius` 检查），`VerbProperties.AdjustedFullCycleTime` 也会读它。每次读取都先查表，命中后还要重新执行 `ShouldModify`（`VFM_ShouldApplyFireMode` → `IsColonistPlayerControlled` / `IsColonyMechPlayerControlled`、`verbProps`、`EquipmentSource.def.IsRangedWeapon` 等一串属性）。

`Verb.BurstShotCount` is read on every `TryCastShot` during a burst (the `forcedMissRadius` check in `Verb_LaunchProjectile.TryCastShot`) and by `VerbProperties.AdjustedFullCycleTime`. Every one of those reads probes the table first and, on a hit, re-runs `ShouldModify`.

### 3.3 `WarmupComplete`：先查表，再判断是不是「持枪小人」 / probe before the cheap eligibility test

```csharp
[HarmonyPatch(typeof(Verb), "WarmupComplete")]
[HarmonyPrefix]
public static void LockCount(Verb __instance)
{
    if (_burstCache.TryGetValue(__instance, out BurstCacheState value)) value.IsValid = false;
    if (!ShouldModify(__instance, out Pawn pawn, out ThingWithComps weapon)) return;
    int num = FireModeDB.GetBurstCount(pawn.VFM_GetFireMode(), weapon.def.defName, __instance.BurstShotCount);
    ...
}
```

每次预热（= 每次连发开始）先查表再判断资格。频率远低于 tick，但顺序是可优化的。

### 3.4 为什么缓存不能简单地「一直有效」 / why the cache cannot simply stay valid

原版 `Verb.WarmupComplete()` 的顺序是：

```csharp
burstShotsLeft = ShotsPerBurst;     // Verb_Shoot.ShotsPerBurst => BurstShotCount  ← 这里读 getter
state = VerbState.Bursting;         // 之后才进入 Bursting
TryCastNextBurstShot();
```

所以「冻结值」必须覆盖两个时刻：**预热体内部的那一次读取**（此时 `state` 还是 `Idle`），以及**整个连发期间**（`state == Bursting`）。VFM 用「`IsValid` 标志 + 每 tick 清理」来表达这件事；清理本身就成了 3.1 的开销。

The vanilla order is `burstShotsLeft = ShotsPerBurst` (which reads the getter) **before** `state = VerbState.Bursting`. The frozen value therefore has to cover the read inside the warm-up body (state still `Idle`) *and* the whole burst (`state == Bursting`). VFM expresses that with an `IsValid` flag plus a per-tick sweep, and the sweep is exactly the cost in 3.1.

### 3.5 顺带核查过的其它路径 / other hot paths that were audited

- `VFM_FireMode_StatPart.TransformValue` 注入在 `ShootingAccuracyPawn` / `RangedCooldownFactor` / `AimingDelayFactor` 上，每次查询做 `TryGetComp`（`ThingWithComps.GetComp<T>` 有按类型缓存）+ 一次 `Dictionary<string, VFM_WeaponProfile>` 查询，**不分配**；它只在「每次射击 / 每次连发开始」被查询，不在每 tick 路径上。**不修改**。
- `Patch_TryStartCastOn.Prefix` 只在开火开始时执行，纯属性判断 + 一次距离计算，**不分配**。**不修改**。
- `VFM_CompsPatcher` / `VFM_StatPatcher` 是 `[StaticConstructorOnStartup]` 的一次性注入。**不修改**。

- `VFM_FireMode_StatPart.TransformValue` (injected into `ShootingAccuracyPawn`, `RangedCooldownFactor`, `AimingDelayFactor`) does a `TryGetComp` (cached by `ThingWithComps.GetComp<T>`) and one dictionary lookup, **no allocation**, and only runs when a shot starts or a stat is queried - it is not on the per-tick path. **Not modified.**
- `Patch_TryStartCastOn.Prefix` runs only when a cast starts; pure property reads plus one distance calculation, **no allocation**. **Not modified.**
- `VFM_CompsPatcher` / `VFM_StatPatcher` are one-shot `[StaticConstructorOnStartup]` injections. **Not modified.**

---

## 4. 机制 / Mechanism

本补丁**不是**「把 VFM 的补丁关掉」，而是**逐位等价地接管**它们。安装时：

1. 通过 `Harmony.GetPatchInfo` 证明 VFM 的三个补丁确实挂在三个预期方法上；
2. 只移除这三个 **patch method**（按方法精确移除，不碰 VFM 在同一方法上的其它补丁，也不碰任何其它 mod 的补丁）；
3. 安装等价实现；
4. 再次用 `GetPatchInfo` 验证三个新补丁在位；任一环节失败 → 回滚并**按原优先级重新挂回 VFM 原始补丁**。

This mod does not disable VFM; it takes the three patches over with bit-identical behaviour: verify VFM's patches are really attached, remove exactly those three patch methods, install the replacements, verify, and roll back to VFM's own patches (with their original priorities) if anything fails.

### 4.1 替换后的三个补丁 / the three replacements

| 原 / original | 新 / replacement | 变化 / change |
|---|---|---|
| `LockCount` prefix on `WarmupComplete` | `BurstShotCountPatches.LockCount` prefix + `UnlockWarmup` **finalizer** | 先算资格再查表；用 finalizer 关闭预热窗口（原方法抛异常也能关闭） |
| `BurstShotCountPostFix` on `get_BurstShotCount` | `BurstShotCountPatches.ApplyLockedCount` postfix | 两次字段读取即可快速拒绝，只有连发中/预热中才查表 |
| `Postfix_Cleanup` on `VerbTick` | **删除，不替换** | 有效性由 `Verb.state` 推导，不再需要每 tick 清理 |

```csharp
// getter postfix：空闲/近战/动物/炮塔在这里就返回，0 次查表
if (__instance.state != VerbState.Bursting && warmupDepth == 0) return;
if (!BurstCountCache.TryGet(__instance, out var state) || state.Count == 0) return;
if (!ShouldModify(__instance, out _, out _)) return;   // 与 VFM 相同的资格判断
__result = state.Count;
```

`warmupDepth` 由 `WarmupComplete` 的 prefix/finalizer 维护（`0 → 1 → 0`），因此**预热体里那一次 `ShotsPerBurst` 读取**依然能看到刚锁定的弹数，而预热一结束窗口就关闭。

`warmupDepth` is maintained by the `WarmupComplete` prefix/finalizer (`0 -> 1 -> 0`), so the single `ShotsPerBurst` read inside the warm-up body still sees the freshly locked count, and the window closes as soon as the warm-up returns.

### 4.2 为什么不会读到过期值 / why a stale value is impossible

有效性条件只依赖 Verb 自身的状态：

- `state == Bursting` → 连发进行中，返回冻结值；
- `warmupDepth > 0` → 正在完成预热（即将消费这个值）；
- 其它一切情况 → 直接返回原版值，**连查表都不做**。

因此不需要「事后清理」，也就不需要 `VerbTick` 钩子。VFM 的 `IsValid` 标志被 `Count == 0` 取代（`Count` 恒 ≥ 1，0 即「无冻结连发」）。

Validity is derived only from the verb's own state: bursting, or inside the warm-up call that is about to consume it. Everything else returns vanilla without even touching the table, so no sweep is needed and no stale value can be observed.

### 4.3 内存与 GC / memory and GC

仍然是 `ConditionalWeakTable<Verb, BurstCountState>`：键是弱引用，Verb 被回收时条目随之消失，**不会**因为「补丁持有 Verb」而把 Pawn / Map / 整个殖民地钉在内存里。每个真正开过火的 Verb 只分配一个 24 字节左右的状态对象（一次性）；稳态零分配。

The storage stays a `ConditionalWeakTable`, so verbs are weak keys and a destroyed pawn can never be pinned in memory through this patch. One small state object per verb that actually fired (once); steady state allocates nothing.

### 4.4 行为等价范围 / behavioural parity

已验证（285 个采样点，逐位相同）：

- 4 种射击模式 × 4 种弹数算法（Linear / Additive / Tent / Adaptive）× 基础弹数 1 / 3 / 8；
- 连发中途切换射击模式 → 仍然冻结本连发的弹数；
- 每个武器的自定义配置（`CustomWeaponProfiles`，4 种模式各自的弹数）；
- `enableFireModeForNPC = false` → 完全不改；
- 排除项：`Verb_ShootOneUse`、近战 Verb、非 Pawn 施法者（炮塔/建筑）、单发武器（`burstShotCount <= 1` 时 VFM 的算法本身返回原值）；
- 没有 VFM comp 的 Pawn 走 `Default` 模式；
- 预热 / 冷却 / 精度：这三个值来自 VFM 的 `StatPart`，本补丁完全不碰，安装前后补丁图里也不存在对这些路径的任何改动。

Verified with 285 samples, bit-identical: 4 fire modes x 4 burst algorithms x base 1/3/8, mid-burst mode changes, per-weapon profiles, the NPC toggle, all VFM exclusions, and pawns without the VFM comp. Warm-up/cooldown/accuracy come from VFM's `StatPart`s, which this mod does not touch at all (the patch-graph diff proves it).

### 4.5 唯一有意的差异 / the single intentional difference

| | VFM | 本补丁 / this mod |
|---|---|---|
| 冻结值有效期 | 从 `WarmupComplete` 到**该 Verb 的下一次 `VerbTick`** | 到 Verb 离开 `Bursting`（或预热调用返回）为止 |

也就是说：连发刚结束、下一次 tick 还没来的那一小段窗口里，VFM 的 `BurstShotCount` 仍会返回上一个连发的弹数，本补丁立刻返回原版值。这只影响「连发结束后、下一次 tick 前」对 `BurstShotCount` 的读取（例如 `VerbProperties.AdjustedFullCycleTime` 用于射击经验结算），**不影响任何一次连发实际发射的弹数**。离线验证里 285 个采样点中有 37 个正好落在这个窗口，全部是「VFM=冻结值，本补丁=原版值」。

In the short window between the end of a burst and that verb's next `VerbTick`, VFM still returns the previous burst's frozen count; this mod returns the vanilla value immediately. It cannot affect how many shots any burst fires (37 of the 285 samples land in that window and show exactly this difference).

---

## 5. 安装、幂等与回滚 / install, idempotency, rollback

VFM 在 **`Mod` 构造函数**里打补丁：

```csharp
public class VanillaFireModes : Mod
{
    public VanillaFireModes(ModContentPack contentPack) : base(contentPack)
    {
        settings = GetSettings<VanillaFireModesModSettings>();
        new Harmony("Aliza.VanillaFireModes").PatchAll();   // ← 加载期，不是 SCoS
    }
}
```

所以本补丁在**两个阶段**都调用同一个幂等安装器：

1. `VanillaFireModesPerfFixMod`（`Mod` 构造函数）—— 由 `loadAfter` 保证晚于 VFM，第一个游戏 tick 之前热补丁就已消失；
2. `[StaticConstructorOnStartup]` —— RimWorld 在所有 `Mod` 构造函数之后执行，即使加载顺序被别的 mod 打乱，也会在开档前把状态修好（验证里模拟了「VFM 后打补丁」的场景）。

安装器返回 `3`（替换了三个 patch method）。重复调用只做校验，不会重复挂补丁。

失败处理：

- VFM 的补丁方法名/签名变了 → 记录 warning，**什么都不改**；
- VFM 的补丁根本不在（未加载 / 已被别的 mod 接管）→ 记录 warning，**什么都不改**（避免出现两个「弹数权威」）；
- 安装过程中抛异常 → 撤销自己的补丁，按原优先级把 VFM 的三个补丁挂回去。

VFM patches in its `Mod` constructor, so this mod installs from both its own `Mod` constructor (guaranteed after VFM by `loadAfter`) and from `[StaticConstructorOnStartup]` (after every mod constructor, repairing a broken load order). A changed VFM signature or a missing VFM patch results in a log warning and no changes; a failed install rolls back to VFM's own patches.

补丁优先级使用 `Priority.First`（800）而不是默认的 `Normal`（400）：VFM 在加载早期挂补丁，天然排在其它 mod 的同优先级 postfix 之前；本补丁安装得晚，用 `First` 才能保持「其它 mod 看到的是 VFM 处理后的值」这一相对顺序。

`Priority.First` is used instead of the default `Normal` so the getter postfix keeps VFM's original "installed early, runs before later mods' postfixes" ordering even though this mod is installed later.

---

## 6. 验证 / validation

`Validation\Validation.csproj` 是一个完全离线的 RED/GREEN 测试台，**不读也不写 `ModsConfig.xml`**，不启动 RimWorld，不改任何 Workshop 文件。

- 真实 `Assembly-CSharp.dll`（`1.7.5005 rev1082`）：使用 `DMSEWorldPerfFix` 验证输出里那份 **唯一的改动是把 `Verse.ModsConfig..cctor` 清空**的 QA 副本（sha256 `20fb0d97a55ab0568870ba3ccf6cbc28b861952b39ac82276a81da81d6dcebf4`）；`bind` 会校验该 `..cctor` 确实只剩一条 `ret`。
- 真实 `VFM_VanillaFireModes.dll`（1.8.0）：原样加载，未修改。
- 真实 `0Harmony.dll`（3.0.0.0）。
- fixture 使用真实的 `Pawn` / `ThingWithComps` / `CompEquippable` / `Verb_Shoot` / `Verb_MeleeAttackDamage` / `VFM_PawnCompFireMode` / `VanillaFireModesModSetting`，只在私有 `Verb.TryCastNextBurstShot` 上挂一个「记录本次连发弹数并跳过实际弹道」的 prefix，以及一个跳过 `BattleLog.Add` 的 prefix。

A fully offline RED/GREEN harness: the real 1.7 game assembly (the QA copy whose only change is an emptied `Verse.ModsConfig..cctor`), the real VFM 1.8.0 DLL unmodified, real Harmony 3, and real RimWorld/VFM objects, with a capture hook on the private `Verb.TryCastNextBurstShot` so a burst starts without a map, projectiles or a battle log.

### 6.1 运行 / how to run

```powershell
cd G:\dev\rimmaid-patches\mods\VanillaFireModesPerfFix\Validation
dotnet build -c Release

$G = 'A:\SteamLibrary\steamapps\common\RimWorld'
$H = 'A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
$V = 'A:\SteamLibrary\steamapps\workshop\content\294100\3662471742\1.7\Assemblies\VFM_VanillaFireModes.dll'
$M = 'bin\Release\net10.0\VanillaFireModesPerfFix.dll'

bin\Release\net10.0\Validation.exe bind   $G $H $V $M
bin\Release\net10.0\Validation.exe red    $G $H $V $M   # VFM only
bin\Release\net10.0\Validation.exe green  $G $H $V $M   # VFM + this mod
bin\Release\net10.0\Validation.exe verify $G $H $V $M   # red + green + diff, one process
```

`red` / `green` 会各自写出 `red-transcript.txt` / `green-transcript.txt`（285 行）。跨进程 diff 的结果是 **37 行差异，全部落在 `burst-ended-before-tick` 这一个点上**（见 4.5），其余 248 行逐字节相同。

`red` / `green` write 285-line transcripts. The cross-process diff is exactly 37 lines, all at `burst-ended-before-tick` (section 4.5); the other 248 lines are byte-identical.

### 6.2 结果 / results

`verify` 模式（同一进程内 RED → GREEN）：

```
RED   idle colony (1200 verbs, 900 melee):
  VerbTick          300,000 calls    2.324 ms     7.75 ns/call       0 B
  BurstShotCount    300,000 reads    2.370 ms     7.90 ns/call       0 B
  modelled 10,000 VerbTick calls per frame: 0.0775 ms/frame

GREEN idle colony (1200 verbs, 900 melee):
  VerbTick          300,000 calls    0.702 ms     2.34 ns/call       0 B
  BurstShotCount    300,000 reads    0.747 ms     2.49 ns/call       0 B
  modelled 10,000 VerbTick calls per frame: 0.0234 ms/frame

VerbTick: 7.75 -> 2.34 ns/call (3.3x); BurstShotCount: 7.90 -> 2.49 ns/call (3.2x)
RED   probes: LockCount=0, getter=24000, tick=120000, this mod=0
GREEN probes: LockCount=0, getter=0,     tick=0,      this mod=0
compared 285 samples: 0 mismatches, 37 post-burst samples where this mod returns the vanilla value earlier than VFM
OK: 48/48 checks passed.
```

（三次连续运行的中位数，跨运行区间 RED 7.52–7.98 / GREEN 2.19–2.63 ns/call，比值 2.9x–3.4x；探测次数与分配是确定值。）
(Medians of three consecutive runs; across runs RED 7.52-7.98 / GREEN 2.19-2.63 ns/call, i.e. 2.9x-3.4x. Probe counts and allocations are deterministic.)

读数说明 / how to read the numbers:

- `getter=24000` 对应 24,000 次 `BurstShotCount` 读取，`tick=120000` 对应 120,000 次 `VerbTick` —— **VFM 的每个清理补丁确实一次不漏地查表**；GREEN 全部为 0。
- 本机 7.5 ns/call 是「裸 CWT 探测 + Harmony wrapper」；Dubs 的 0.327 ms/frame 里还包含它自己的每调用计时插桩，所以绝对值更大。**形状**才是重点：与 Verb 数线性、每 tick 发生，现在为 0。
- 3.3x 是整条 `VerbTick` 调用（含循环）的比值；纯探测增量约 5.4 ns/call，按 10,000 次/帧计算约 0.054 ms/frame 的净收益。
- 两个循环的稳态分配都是 **0 B**。

### 6.3 验证覆盖的断言 / what the 48 checks cover

- VFM 三个补丁在替换前确实存在（真实 `GetPatchInfo`）；
- 替换后补丁图**只有**这 3 条变化（`before.Except(after)` / `after.Except(before)` 全量比对），`Verb.VerbTick` 上不再有任何 postfix；
- 我们的 3 个补丁（prefix / finalizer / postfix）确实在位；
- 第二次安装幂等（补丁图完全不变）；
- `Mod` 构造函数阶段、`[StaticConstructorOnStartup]` 阶段都能安装；「VFM 后打补丁」的乱序场景能被 SCoS 修好，且最终只有 **1 个**弹数权威；
- VFM 补丁缺失时安装器返回 0 且不留下任何东西；
- 285 个采样点全部符合模型（`pre` / `after-tick` = 原版值，`warmed` = 模型弹数且实际发射数相同，`mode-changed-mid-burst` = 冻结值）；
- RED/GREEN 逐点比对：连发期间 0 处不同，37 处落在文档化的窗口且 GREEN 全部等于原版值；
- 清理语义：连发结束后立刻回到原版值、切换模式不会复活旧值、下一次连发用新模式、`VerbTick` 之后仍是原版值；
- 稳态零分配。

---

## 7. 什么时候可以删掉这个补丁 / when this patch can be deleted

满足任意一条即可移除本 mod（移除后 VFM 自己的补丁会原样恢复，存档无需处理）：

1. **VFM 上游修复**：Aliza 把 `Postfix_Cleanup` 从 `VerbTick` 上撤掉，或改成「只在 `state == Bursting` 时维护一个小的活动集合」。判断方法：`ilspycmd` 打开新版 `VFM_VanillaFireModes.dll`，若 `Patch_BurstShotCount` 不再有 `VerbTick` 的 postfix，本补丁就没有意义了。
2. **VFM 改掉了三个补丁方法的名字/签名**：本补丁会检测不到并自动变成 no-op（日志里会有 `Vanilla Fire Modes changed its patch method names`），此时应当更新或删除本补丁。
3. **RimWorld 版本升级**（例如 1.8）：`Verb.WarmupComplete` / `Verb.VerbTick` / `Verb.get_BurstShotCount` 的实现或 `VerbState` 语义一旦变化，就需要重新跑一遍 `Validation` 的 RED/GREEN；`VerbTick` 若被删除或改名，安装器会直接放弃安装。
4. **VFM 不再用 `ConditionalWeakTable` 存弹数**（例如改为 `ThingComp` 字段或 `Verb` 派生类字段）：本补丁的等价前提消失。
5. **Dubs 的 Harmony 页面上 `Vanilla Fire Modes` 不再出现在 `Verb.VerbTick` 下**：说明问题已从别处解决。

Remove this mod when any of these is true; VFM's own patches come back untouched and no save data is involved. The three most useful triggers: VFM removes its `VerbTick` postfix upstream, VFM renames its patch methods (this mod then logs a warning and does nothing), or RimWorld changes `Verb.WarmupComplete` / `VerbTick` / `VerbState` (re-run the RED/GREEN harness first).

---

## 8. 给作者的话（草稿）/ message to the author (draft)

> Hi Aliza,
>
> I really like Vanilla Fire Modes and I use it in every colony, but Dubs Performance Analyzer kept flagging it at around 0.33 ms/frame, and the cost scaled with the total number of verbs in the colony rather than with how many pawns were actually shooting. I tracked it down to `Patch_BurstShotCount.Postfix_Cleanup` on `Verb.VerbTick`.
>
> The cleanup only needs to run for verbs that have a valid cached burst, but as written it calls `_burstCache.TryGetValue` for every verb on every tick. In a colony with ~1,200 verbs (most of them animal melee verbs that never burst) that is ~10,000 table probes per frame, even when nobody is firing. Two smaller things in the same class: `BurstShotCountPostFix` probes unconditionally on every getter read, and `LockCount` probes before it checks whether the verb is even a ranged pawn weapon.
>
> One way to remove the whole cost without changing behaviour: derive cache validity from the verb instead of sweeping. The frozen value only has to be visible (a) while `state == VerbState.Bursting`, and (b) during the `WarmupComplete` body, which reads `ShotsPerBurst` *before* it sets `Bursting`. So a `WarmupComplete` prefix/finalizer pair that brackets the call, plus a getter postfix that first checks `state != Bursting && warmupDepth == 0`, lets you delete the `VerbTick` postfix entirely. Idle, melee, animal and turret verbs then cost two field reads and never touch the table, and the value can never go stale because validity is re-derived on every read.
>
> I packaged that as a small standalone patch mod (`whitegiver.vfm.perffix`) so I would not have to touch your DLL. It removes exactly your three patch methods (verified through `Harmony.GetPatchInfo`, with rollback to your patches on any failure) and installs the equivalent three. An offline RED/GREEN harness runs against your real 1.8.0 DLL and the real 1.7 assembly: 285 sampled points across 4 modes x 4 burst algorithms x base 1/3/8, mid-burst mode changes, per-weapon profiles, the NPC toggle and all of your exclusions - 0 mismatches during bursts, ~3x faster idle `VerbTick`, zero table probes. The only behavioural difference I could find is that your frozen value survives until the verb's next `VerbTick` (at most one tick), while mine returns the vanilla value as soon as the burst is over.
>
> If you would like, I can send the harness or a patch file - and of course feel free to fold the idea into VFM itself, in which case my mod becomes unnecessary and I will retire it.
>
> Thanks for the mod!
> WhiteGiverMa

---

## 9. 文件清单 / files

```
About\About.xml                                  packageId / dependencies / loadAfter
LoadFolders.xml                                  1.7 -> / + 1.7
1.7\Assemblies\VanillaFireModesPerfFix.dll       Release net10.0, 0 warnings / 0 errors
Source\VanillaFireModesPerfFix.csproj
Source\VanillaFireModesPerfFixMod.cs             Mod ctor + [StaticConstructorOnStartup] entry points
Source\PatchInstaller.cs                         verify / unpatch / install / rollback
Source\BurstShotCountPatches.cs                  the three replacements + ShouldModify
Source\BurstCountCache.cs                        weak-keyed storage + probe counter
Validation\Validation.csproj                     offline harness (Mono.Cecil for the bind step)
Validation\Program.cs                            bind / red / green / verify
Validation\Fixture.cs                            real Pawn/weapon/verb/comp fixture
Validation\Tests.cs                              transcript, parity, patch graph, probes, timings
README.md                                        this file
```

编译 / build:

```powershell
dotnet build -c Release G:\dev\rimmaid-patches\mods\VanillaFireModesPerfFix\Source\VanillaFireModesPerfFix.csproj
# 0 warnings, 0 errors; the DLL is copied to 1.7\Assemblies automatically
```
