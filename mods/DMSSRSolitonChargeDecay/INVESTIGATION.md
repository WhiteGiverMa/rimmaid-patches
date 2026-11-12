# Synthriada 孤立子活性调查 — 2026-11-12

## 结论

“开火越久越强”是明确的设计。普通停火瞬清由两种情况叠加：**重型/炮塔未启用储能开关，确实清零；聚焦器启用了储能，但 UI 没有显示实际余量。** 还有一个 Burst 失败后先变 Idle、随后捕获充能得到零的生命周期遗漏。

衰减并非需要从零设计：`ComputeCarryTicks`、`coldTime`、`storedWarmupTicks`、`lastStopTick` 和续射 `warmupCarryTicks` 都已经存在，聚焦器 XML 配有 `beamSwitchFire=true` / `coldTime=120`。重型和炮塔默认走 `beamSwitchFire=false` 的显式清空分支。描述说明缓慢启动、输出随聚焦增加和过热冷却；重型/炮塔文案没有承诺普通停火的衰减规则。

**设计判断分层：**

| 项目 | 证据判断 |
|---|---|
| 持续开火增长活性、射速、伤害与穿甲 | 代码、XML、文案一致，明确设计 |
| 重型/炮塔停火立即丢弃储能 | 显式分支 + XML 未开启 carry；现有实现选择清零，漏配还是平衡取舍由作者确认 |
| 聚焦器停火 UI 归零但真实储能尚在 | 显示路径遗漏，真实 DLL 可复现 |
| Burst 失败后 Idle 状态捕获到零、或一直未保存旧 burst | 生命周期遗漏，真实 DLL 可复现 |
| 安全模式过热停火清空并锁定冷却 | 文案与 `StartSafetyCooldown` 一致，明确设计，补丁保留 |

## 调查基线与完整反编译

- Synthriada：Workshop **3835512735**，packageId `Cephalopod.DeadManSwitch.Synthriada`，作者 Cephalopod。
- 本机 `1.7/Assemblies/DMSSR.dll`：719360 bytes，最后写入 UTC `2026-11-08T20:02:13.2405816Z`。
- 实际磁盘游戏：**1.7.5005 rev1082**；任务提供的旧基线为 1.7.5000 rev1275。
- 使用 **ilspycmd 11.1.0.9782**，`-p` 完整反编译三套程序集并补全游戏/FFF/DMS 引用：
  - `DMSSR/`：458 个 C# 文件。
  - `Fortified/`：906 个 C# 文件。
  - `DMS/`：335 个 C# 文件。
- 输出根：`C:/Users/A1337/AppData/Local/Temp/opencode/dmssr-soliton-20261112/decompiled/`。
- 同目录保存 `Verse.Verb.cs`、`Verse.VerbTracker.cs`、`Verse.Pawn_StanceTracker.cs`、`RimWorld.Building_TurretGun.cs`、`RimWorld.Building_Turret.cs`、`Verse.Mod.cs` 与 `Verb_MechParticleBeam.il`。
- 下述行号为补全引用后的反编译版本 `DMSSR/DMSSR.Verbs/Verb_MechParticleBeam.cs`。
- XML 清点覆盖 **104 个 Defs XML、79 个中/英/繁中语言 XML**；`Validation/inventory.ps1` 导出全部相关 Def 的原始 Verb XML 和三语文本。

| 上游文件 | SHA-256 |
|---|---|
| DMSSR | `d28367b8af9e44fe6354dd78b07082bdb10984ad0b8c93343f69e29660a2964d` |
| Assembly-CSharp | `82387f78262461e53bb06a67c3945912b876d1fdb07235122bbb6e59d073f125` |
| Fortified | `9c135095c0a389ae63c08a514b2751b9b168bdc125aea9da8d303f81f8b0e952` |
| DMS | `d7da222fe2744030d2e8a538ccedd0f854fc848ce0976f953b356fa0e964f030` |

## Def 与类型清单

`SolitonBeamSmall/Medium/Heavy/Shotgun` 是 `graphicData.texPath` 的贴图文件名，实际 defName 如下。

| ThingDef | 中文 / English | 位置 | 类型/关联 |
|---|---|---|---|
| `DMSSR_Soliton_Beam_Light` | 孤立子手枪 / soliton pistol | `Things_Weapon/Weapons_Light.xml:181–284` | `ThingWithComps` + `CompEquippable`；`Verb_MechParticleBeam` |
| `DMSSR_Soliton_Beam` | 机兵孤立子聚焦器 / automatroid soliton focuser | `Things_Weapon/Weapons.xml:552–662` | 同上 |
| `DMSSR_Soliton_Beam_Heavy` | 机兵孤立子集束器 / automatroid soliton concentrator | `Things_Weapon/Weapons_Heavy.xml:3–112` | 同上，`DMS_AutomatroidHeavyGun` 父 Def |
| `DMSSR_Soliton_Beam_Shotgun` | 机兵孤立子散布器 / automatroid soliton disperser | `Things_Weapon/Weapons.xml:663–766` | 同上 |
| `DMS_Turret_BeamLaser` | 执迷之眼 / Eye of Obsession | `Things_Building/DMSSR_Security.xml:3–57` | 原版 `RimWorld.Building_TurretGun`；`turretGunDef=DMS_Gun_BeamLaser` |
| `DMS_Gun_BeamLaser` | 光束激光系统 / Beam laser system | `Things_Building/DMSSR_Security.xml:58–126` | 炮塔内部 `ThingWithComps` + `CompEquippable`；同一粒子束 Verb |
| `DMSSR_ResentfulFlame_SGWC` | 憎恶烈炬 / Resentful Flame | `Things_Weapon/Weapons_Heavy.xml:113–226` | `Fortified.Verb_LaunchProjectile_AmmoSwitch` + `Fortified.CompAmmoSwitch`；使用炮弹 |

所有五个粒子束 Verb 的属性类为 **`DMSSR.Verbs.VerbProperties_MechParticleBeam : Verse.VerbProperties`**；Verb 为 **`DMSSR.Verbs.Verb_MechParticleBeam : Verse.Verb`**，没有 FFF 或 DMS 的充能基类，没有专门的充能 ThingComp。`CompEquippable` 持有 VerbTracker。

炮塔继承的 DMS 父 Def 位于 Workshop 3819799478 `1.7/Defs/Things_Building/DMS_Security.xml:3–84`，第 4 行指定原版 `Building_TurretGun`。DMS 武器父 Def 在 `Things_Weapon/Base.xml`，最终继承 Core 的 `BaseWeapon`（`ThingWithComps`、`CompEquippable`）。FFF 的 `HeavyEquippableExtension` 决定谁能装备；不负责活性。FFF 的换弹种组件与 Resentful Flame 逐项检查后，未发现共享粒子束活性路径；DMS.dll 对 `MechParticleBeam` 没有引用。

### 原生参数

| Def 简写 | warmupTime（瞄准秒） | warmupExponentParam（聚焦 tick） | beamSwitchFire | coldTime XML → 实际 tick | beamCount | damageLinear / damageMax | 耗电 |
|---|---:|---:|---|---:|---:|---|---|
| Light | 0.1 | 300 | 缺省 false | 缺省 0 → 60 | 1 | 1 / 2 | false |
| Medium | 1.0 | 600 | **true** | **120 → 120** | 1 | 3 / 16 | 0.33%/秒 |
| Heavy | 2.0 | 1800 | 缺省 false | 缺省 0 → 360 | 1 | 3 / 45 | 0.66%/秒 |
| Shotgun | 0.1 | 300 | 缺省 false | 缺省 0 → 60 | **6** | 1 / 5 | 0.33%/秒 |
| Turret gun | 2.0 | 1800 | 缺省 false | 缺省 0 → 360 | **4** | 3 / 30 | Verb false，建筑 7500W |

`GetColdTimeTicks`：正数 `coldTime` 优先；否则 `round(warmupExponentParam / 5)`，最少 1 tick。Shotgun 文案写 **12 条**，XML 实际 `beamCount=6`，是独立的文案/参数差异。

### 全部中英文描述

来源：Defs 英文正文，以及 `Languages/English`、`Languages/ChineseSimplified (简体中文)`、`Languages/ChineseTraditional (繁體中文)/DefInjected/ThingDef/DMSSR_Localization_Core.xml`。繁中内容与简中对应，完整导出保存在 `def-inventory.json`。

#### 孤立子手枪 / soliton pistol

> 基于孤立子理论设计的粒子流能束武器之一，小巧的外形和无需担心备弹的特质使得其一度被列装为标准制式副武器。但作为初代能束武器其顶部所搭载的小型散热器似乎并不能胜任连续射击任务。

> A particle-beam weapon based on soliton theory. Its compact shape and freedom from ammunition concerns once made it a standard-issue sidearm. As a first-generation beam weapon, however, its small top-mounted radiator seems inadequate for sustained fire.

#### 机兵孤立子聚焦器 / automatroid soliton focuser

> 基于孤立子理论设计的粒子流能束武器之一，标准型发射的粒子束可有效烧蚀相当厚度的装甲钢，其尾部配有大功率散热器，通过一种设计新颖的放射性电池提供能量和中子通量。
> 由于其体量过大，大部分常规机兵只能利用自身储能为其供电。

> A particle-beam weapon based on soliton theory. The standard beam can ablate substantial thicknesses of armor steel. A high-capacity radiator is mounted at the rear, while an innovative radiological battery supplies energy and neutron flux.
> Its bulk means most ordinary mechs must power it from their own energy reserves.

#### 机兵孤立子集束器 / automatroid soliton concentrator

> 基于孤立子理论设计的粒子流能束武器之一, 又被称为“执迷之眼”。作为粒子武器重型化的早期实现方案，激发器需要相当长的时间达到最大输出功率。当然，完全启动后的粒子束含能是毁天灭地的。
> 由于其体量过大，大部分常规机兵只能利用自身储能为其供电。

> A particle-beam weapon based on soliton theory, also called the “Eye of Obsession”. As an early attempt at a heavy particle weapon, its exciter needs considerable time to reach maximum output. Once fully activated, however, the beam carries devastating energy.
> Its bulk means most ordinary mechs must power it from their own energy reserves.

#### 机兵孤立子散布器 / automatroid soliton disperser

> 基于孤立子理论设计的粒子流能束武器之一，其前身为用于有色金属热能蚀刻的重型孤立子投射器，在去除常规SPA武器的整流管之后，其发射的12条能束呈现独特的随机散布和游走独立性。
> 由于其体量过大，大部分常规机兵只能利用自身储能为其供电。

> A particle-beam weapon based on soliton theory, descended from a heavy soliton projector used for thermal etching of nonferrous metals. Removing the rectifier tube found in conventional SPA weapons gives its 12 beams distinctive random spread and independently wandering trajectories.
> Its bulk means most ordinary mechs must power it from their own energy reserves.

#### 执迷之眼 / Eye of Obsession

> 基于孤立子理论设计的粒子流能束武器之一, 又被称为“执迷之眼”。作为粒子武器重型化的早期实现方案，激发器需要相当长的时间达到最大输出功率。当然，完全启动后的粒子束含能是毁天灭地的。
> 这种炮塔版本将4台“执迷之眼”并联安装在炮塔基座上，并增添了热容和供电系统，使得其成为极其恐怖的烧蚀武器。

> A particle-beam weapon based on soliton theory, also called the “Eye of Obsession”. As an early attempt at a heavy particle weapon, its exciter needs considerable time to reach maximum output. Once fully activated, however, the beam carries devastating energy.
> This turret version mounts four “Eyes of Obsession” in parallel on a turret base, with expanded heat capacity and power systems, making it a terrifying ablation weapon.

#### 光束激光系统 / Beam laser system

> 炮塔用光束激光武器系统。

> Beam laser turret weapon system.

#### 憎恶烈炬 / Resentful Flame

> 在殖民舰队涉足希泽路事务之前，一种关于光束武器的实验型方案曾多次被从舰队数据库的积压档案中翻出，但相关项目往往都因各种原因不疾而终。
> 直到十日行动之后，主战师团才授权奈良重工下属某研究所利用孤立子技术对这一古老的设计进行再利用。出乎意料的是，该设计使用的三管集束装置似乎天然适合作为孤立束流管约束实弹。因此通过加装放射性电池，散热器和半自动装填机后，著名的40mm突击炮——又称“憎恶烈炬”——得以问世。

> Before the Colonial Fleet became involved in Heatheroo's affairs, an experimental beam-weapon proposal repeatedly resurfaced from the fleet database's backlog. Each associated project ended prematurely for one reason or another.
> Only after the Ten Days Operation did a frontline division authorize a research institute under Nara Industries to revisit the old design using soliton technology. Unexpectedly, its triple-tube cluster proved naturally suited to constraining physical ammunition within soliton streams. With a radiological battery, radiator, and semi-automatic loader added, the famous 40mm assault cannon—also called “Resentful Flame”—was born.

### 相关机制文案

- `DMS.MechParticleBeam.Activity`：**孤立子活性 / Soliton activity**，并非机兵的电池 Need。
- `StatFireRateDesc`：粒子束武器造成伤害的速度，会随时间变快。/ The rate at which a particle-beam weapon deals damage. It increases over time.
- `StatMaxDistanceDamageDesc`：与目标距离为 0 时的伤害。数值随聚焦增长。/ Damage at zero range. Increases as the beam focuses.
- `StatWarmupTimeDesc`：粒子束完全启动所需时间。/ Time required for the particle beam to reach full output.
- `StatPowerDrainDesc`：开火时每秒消耗的机械体能量。/ 原生耗电说明，与活性条不同。
- 安全模式：安全模式下，光束达到过热阈值时立即停止射击，完成冷却后才能再次开火。高能模式下允许持续过热射击，并照常承受反馈伤害。
- English safety description: Safety mode stops firing as soon as the beam overheats and requires cooling before firing again. High-energy mode allows continued overheat firing and its usual feedback damage.

## 原版核心代码逐行分析

### 1. 为何边开火边充能

`WarmupComplete:800–859`：

```csharp
int ticksGame = Find.TickManager.TicksGame;       // 807：实际开始开火时间
warmupCarryTicks = ComputeCarryTicks(ticksGame);  // 808：接回停火余量
burstStartTick = ticksGame;                       // 809：本次连续射击起点
// 初始化目标、光束、状态
burstShotsLeft = ShotsPerBurst;                   // 825
state = VerbState.Bursting;                       // 826
TryCastNextBurstShot();                          // 855：立即发射第一发
```

`ShotsPerBurst` 在本类第 126 行附近重写为 `int.MaxValue`。因此 XML 的 `burstShotCount=1` 不会让光束每发结束 Burst。

`GetFiringTicksRaw:2134–2150`：

```csharp
if (burstStartTick < 0 || state != VerbState.Bursting) return 0f;  // 2136–2138
int elapsed = Find.TickManager.TicksGame - burstStartTick;       // 2140
elapsed = Math.Max(elapsed, 0);                                  // 2141–2144
return Math.Max(0f, elapsed + warmupCarryTicks);                  // 2145–2150
```

`GetWarmupTicks` 把 tick 限制到 `warmupExponentParam`；`GetFiringSeconds` 除以 60。`GetChargePercent` 是未截断 tick / 聚焦上限，再 clamp 0–1。

`UpdateDerivedStats:2069–2093` 用聚焦秒数 `t` 更新：

```text
穿甲 = baseArmorPenetration + (penetrationExponentParam^t - 1) / 100
发间隔 tick = max(1, round(30 / fireRateExponentParam^t))
```

`GetBaseDamage:2096–2116`：`damageLinearParam * t`，限制到 `damageMax`；实际持续开火达到 `warmupExponentParam` 则过热，伤害变 `damageMax * 2`。`TryApplyOverheatSelfDamage` 每 60 tick 反馈 `damageMax * 0.1`。

`BurstingTick:862–929` 在每个射击 tick 更新派生参数、光束显示、自伤与耗电。停止充能前已经能发射，只是输出随连续开火上升。瞄准 `warmupTime` 与数十秒聚焦 `warmupExponentParam` 是不同参数。

### 2. 普通停火的精确清零点

`Reset:1546–1565` 首先调用 `StoreWarmupOnStop`，然后 `base.Reset()` 转 Idle，清效果与 `burstStartTick=-1`。

`StoreWarmupOnStop:312–325`：

```csharp
if (!IsBeamSwitchFireEnabled())      // 314：重型/炮塔/手枪/散布器为 false
{
    storedWarmupTicks = 0f;          // 316：实际余量清零
    warmupCarryTicks = 0f;           // 317：本次继承量清零
    lastStopTick = -1;              // 318：丢弃冷却起点
}
else if (burstStartTick >= 0)
{
    storedWarmupTicks = GetWarmupTicks(); // 322：聚焦器正常停火保存
    lastStopTick = Find.TickManager.TicksGame;
    warmupCarryTicks = 0f;
}
```

准确 IL：`StoreWarmupOnStop` 的 `IL_000e` / `IL_0019` / `IL_0020` 分别写这三个字段（反编译 IL 文件第 2533/2536/2539 行）。

`IsBeamSwitchFireEnabled:367–370` 返回 `Props?.beamSwitchFire ?? false`。属性类第 29、31 行声明默认 false 的 bool 和默认 0 的 `coldTime`。只有 Medium XML `Weapons.xml:622–623` 显式配置 true / 120。

### 3. 已存在、但 UI 没接上的线性衰减

`ComputeCarryTicks:328–350`：

```csharp
if (!IsBeamSwitchFireEnabled()) return 0f;
if (storedWarmupTicks <= 0f || lastStopTick < 0) return 0f;
int coldTimeTicks = GetColdTimeTicks();
float elapsed = Mathf.Max(0f, nowTick - lastStopTick);
float remaining = 1f - elapsed / coldTimeTicks;
if (remaining <= 0f) return 0f;
return storedWarmupTicks * remaining;
```

衰减不是逐 tick 写回字段，而是按绝对游戏 tick 惰性计算；暂停不变、游戏加速同倍率变快、再次开火时计算当前余量。所有字段已在 `ExposeData:2291–2299` 原生保存。

UI `Gizmo_MechParticleBeamCharge.DrawContent:49–55` 读取 `DisplayChargePercent`，后者 `GetDisplayChargePercent:2168–2192` 先读 `GetChargePercent`。后者没有读 `ComputeCarryTicks`，仍调用 `GetFiringTicksRaw`；后者只要 Idle 就返回 **0**（`2136–2138`，IL `IL_0012 ldc.r4 0` / `IL_0017 ret`）。

显示平滑只做 `smoothedChargePercent - 0.12f * elapsedTick`。满值降零最多 **9 tick ≈ 0.15 秒（1 倍速）**，3 倍速更短。这段是快速 UI 插值，不是储能物理衰减。

真实 DLL 复现：Medium 储存 100 tick 活性，Reset 后 `ComputeCarryTicks(同 tick)=100`，`ChargePercent=0`；满条停火 9 tick 后 UI 已为零，但 native carry 仍大于零。

### 4. 停火/换目标/发间隔的真实调用链

**普通发间隔**：`Verb.VerbTick:382–398` 在 Bursting 中递减 `ticksToNextBurstShot`。即使未到下一发也调用 `BurstingTick()`，state 保持 Bursting，因此继续充能。原版不会因普通发间隔自动清零。

**手动停火与换 Job**：`BurstingTick:879–882` → `ShouldInterruptBeam:1567–1662`：Pawn 无 Job、换成非 AttackStatic/UseVerbOnThing/Wait_Combat、换 Verb、攻击 Job 目标不同、死亡/倒地/眩晕都会中断 → `Reset` → 清零或储存分支。

**炮塔禁止开火**：`ShouldInterruptForTurret:1664–1712` 读取原版私有 `holdFire`，true 时 `manualStop=true` / return true → 同一 Reset 路径。强制目标与开始时 `forcedTargetAtStart` 不同也会 Reset。`Building_TurretGun.Tick` 在其 Active 路径经 `GunCompEq.verbTracker.VerbsTick()` 驱动该 Verb。

**目标消失/倒地/死亡/过近**：`IsCurrentTargetInvalidForBeam` + `TryTransferSweepTarget`。找到当前目标周围 `range * 0.25` 范围内的合适敌人时直接换目标、不改 `burstStartTick`，持续充能。找不到才 Reset。换目标不必然丢活性，取决于走无缝扫射还是重启路径。

**射击失败/自然 Burst 完成**：原版 `Verb.TryCastNextBurstShot:459–540`，`Available()` 或 `TryCastShot()` 失败会令 `burstShotsLeft=0`，第 528 行直接 `state=Idle`，执行 cooldown/callback，却不调用 DMSSR Reset。

这之后 `GetFiringTicksRaw` 立即为零；稍后 `Reset` 保存时也因 Idle 只能取得零。`VerbTick` 在完成发射后仍接着调用一次 `BurstingTick`，可能当 tick 触发 Reset，也可能再走光束更新路径，直到后续事件中断。成功射击间隔和失败后结束 Burst 必须区分。

**安全模式过热**：`StartSafetyCooldown:222–230` 先 Reset，然后再次显式 `storedWarmupTicks=0` / `warmupCarryTicks=0` / `lastStopTick=-1`，将 `safetyCooldownUntilTick` 设为当前 tick + native coldTime。该分支和文案一致，补丁不覆盖。

## 补丁实现

源码 `Source/SolitonChargeDecay.cs`，四处 Harmony：

1. `IsBeamSwitchFireEnabled` postfix 返回 true，让原生 `ComputeCarryTicks` / `WarmupComplete` 对全部五种 Verb 生效，XML 字段保持原值。
2. `StoreWarmupOnStop` prefix 在 `burstStartTick>=0` 时，以 `max(0, now-start)+warmupCarryTicks` 捕获，沿用原生聚焦上限截断；即使 state 已变 Idle 也保存成功。已停火的重复 Reset 不重置冷却起点。
3. `GetChargePercent` postfix 在 Idle 返回 `ComputeCarryTicks(now)/configuredWarmupTicks`，从而接通原生 Display 平滑与 Gizmo。开火时计算保持原样。
4. `Verb.TryCastNextBurstShot` postfix 发现 DMSSR Verb 已转 Idle 且旧 burst 尚未清理时调用真实 `Reset`，保存充能并清效果；保存/恢复原 `castCompleteCallback`，其原始一次调用及后续炮塔 cooldown 回调保持原样。其他 Verb 快速返回。

保留 `GetFiringTicksRaw` 的“开火时间”含义；Idle 显示修复独立进行，安全模式/自伤不会因剩余显示值而误判正在过热开火。无逐世界 tick 扫描、无新增组件、无新存档字段、无自选衰减率。正数 `coldTime` 优先，缺省继续使用作者的聚焦时间 / 5 公式。

## 离线 RED / GREEN 与部署证据

临时目录 `C:/Users/A1337/AppData/Local/Temp/opencode/dmssr-soliton-20261112/`：

- `def-inventory.json`：完整 XML/三语文本清点。
- `red-before-patch-final.txt`：10 项原版基线通过，停火应保留 50% 的首个回归断言失败，退出 1。
- `green-release.txt`：发布 DLL **115 项 PASS**；测试类与脚本保存在 `Validation/`。
- `validation/isolation-receipt.json`：真实上游/补丁哈希、离线剥离列表、145 方法体逐指令相同的回执。
- `validation/charge-roundtrip.xml`：Scribe 测试生成的合成样本。
- `deploy-*/deploy-receipt.json`：安装资源逐文件 SHA-256、进程及配置 hash。

生产与 Driver 编译均零警告/错误，LSP 两个 C# 文件零诊断。发布 DLL SHA-256：`116e1a68a316640e4670c9847ee52bc84b42d75c3be2c32f0bf46881e4083a99`。

2026-11-12 已复制到 `Mods/DMSSRSolitonChargeDecay`，并在 `ModsConfig.xml` 紧跟 `cephalopod.deadmanswitch.synthriada` 后插入 `whitegiver.dmssr.solitonchargedecay`（activeMods 301→302，配置 SHA-256 `d59e38c3…` → `7157e8d4…`）。用户正常重启后于 2026-11-12 确认游戏内验证通过；已验证 DLL 保持原样。

## 工坊即时核对

2026-11-12 读取 [Workshop 页面](https://steamcommunity.com/sharedfiles/filedetails/?id=3835512735)、[更新记录](https://steamcommunity.com/sharedfiles/filedetails/changelog/3835512735)、[bug report 两页](https://steamcommunity.com/workshop/filedetails/discussion/3835512735/567050417882807085/)。页面最新更新为 Nov 8，更新说明涉及本地化、基因、BCAT 和研究；当前公开说明/17 条 bug report 未描述普通停火活性规则。页面提供 bug report 入口；未列出源码 GitHub 链接。建议把本文结论与中英文草稿发到该讨论串，优先询问重型/炮塔是否有意要求每次重新聚焦。
