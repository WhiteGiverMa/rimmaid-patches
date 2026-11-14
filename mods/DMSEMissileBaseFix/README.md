# DMSE Missile Base Shuttle & Reform Fix
# DMSE 导弹阵地穿梭机与重组远行队修复

`packageId: whitegiver.dmse.missilebasefix` · `1.0.0` · RimWorld `1.7` · `net10.0`

Standalone sidecar for **The Dead Man's Switch - Expedition** (`Aoba.DeadManSwitch.Expedition`, Workshop `3689879335`). It does not modify Workshop files and contains no custom save fields.

这是 DMSE 敌方导弹/防空阵地的独立修复，不修改工坊原件，不写入自定义存档字段，可安全增删。

---

## 中文：根因分析

### 1. 穿梭机/运输舱无法进入

三个阵地的 `WorldObjectDef` 都显式写了 `<validLaunchTarget>false</validLaunchTarget>`：

- `DMSE_MissileBase`
- `DMSE_FieldLaunchSite`
- `DMSE_EarlyLaunchSite`

原版 `CompLaunchable.ChoseWorldTarget` 在生成降落菜单之前检查此字段并直接拒绝目标；`TargetingLabelGetter` 也用它隐藏标签。DMS Core 的自定义 lifter 有同样的检查。因此这不是降落点、燃料或穿梭机本体的问题。

该上游设置还使 DMSE 自己为 `TransportersArrivalAction_VisitSite.CanVisit` 编写的撤退后重入冷却拦截失去入口。修复仅把三个阵地改回 `true`；信号干扰、射程、燃料、无人殖民者、原版进入冷却、DMSE 重入冷却、BVR 风险确认和实际防空拦截仍走原路径。

### 2. 清场后不能「重组远行队」

真实 1.7 调用链是：

`ExitMapGrid.MapUsesExitGrid` → `FormCaravanComp.CanFormOrReformCaravanNow` → `AnyActiveThreatNow` → `GenHostility.AnyHostileActiveThreatToPlayer(...)`。

三个 Def 已包含 `WorldObjectCompProperties_FormCaravan`，`OnBaseDestroyed()` 只负责阵地摧毁信件、站点存续和相关状态，**不会直接解锁重组远行队**。实际还有两类阻塞：

1. `Site` 继承默认 `WorldObject.CanReformFoggedEnemies=false`，所以地图未揭开区域的休眠/迷雾敌人也计入威胁。
2. 原版 `Building_Turret.ThreatDisabled` 检查电力、操作员、休眠等状态，却不检查炮管/弹药是否够 `AttackVerb` 发射一发。仍有电但彻底打空弹药的炮塔因此永久阻塞。

**纠正旧草案的错误前提：** `DMSE_Turret_Vulcan_WingBack` 的 `initialFuelPercent=0.5` 在 600 容量上表示初始 **300 个绝对燃料单位**；`minimumFueledThreshold=0.9` 是绝对数量，不是 90% 百分比。它出生时 `HasFuel=true`、能够开火，绝不是天生哑炮。

`DMSE_Building_FieldAirDefense` 也有 200 × 0.5 = **100** 机炮弹，并且另有独立的拦截弹舱。仅有电且武装的自动炮塔仍是真实敌人；本补丁不会把「只杀光人员但武装炮塔仍在」误算成安全。请摧毁、断电或真正耗尽其作战能力。

### 3. 敌方导弹带不走

- 地面导弹本来就可由远行队/装载界面枚举。
- `DMSE.Building_MissileRack` 实现 `IHaulSource`，其弹架内导弹本来就会被原版 `ListerThings` 扫描；无需认领弹架，真实容器转移动作会减少库存。
- `DMSE.CompMissileRackStorage` 只是组件上的 `IThingHolder`，不实现 `IHaulSource`；它用于野战防空炮和 `DMSE_FieldStructure_AmmoStorage` 等建筑。原版列表扫不到，而且只把对象塞进 UI 列表也不够：`GenClosest` 的搬运搜索仍然找不到未生成对象。

本补丁在阵地已无可见活跃敌对威胁、容器建筑本身也已安全时，通过真实 `ThingOwner.TryDrop` 把合规物品放落地面并加入当前列表。这样 `Dialog_FormCaravan`、`Dialog_LoadTransporters`、远行队收集和 `LoadTransportersJobUtility` 都使用正常地面战利品流程，不会出现「界面看得到但小人搬不了」。

活跃敌对 SAM/CIWS 不会被远程清空；迷雾、预订物品、普通非 DMSE 地图和 `allowEvenIfOutsideHomeArea=false` 的调用均受保护。

---

## English: root causes

### Shuttle/pod entry

`DMSE_MissileBase`, `DMSE_FieldLaunchSite`, and `DMSE_EarlyLaunchSite` explicitly set `validLaunchTarget=false`. Vanilla `CompLaunchable.ChoseWorldTarget` rejects such a world object before it builds arrival options; DMS Core’s lifter repeats the same gate. This is not a landing-cell or fuel problem.

The fix enables only those three site defs. Range, fuel, signal-jammer, colonist, vanilla cooldown, DMSE post-retreat re-entry cooldown, BVR interception confirmation, and actual air defense remain intact.

### Reforming after combat

The real 1.7 path is `ExitMapGrid` → `FormCaravanComp` → `GenHostility`. DMSE already supplies the form-caravan comp. `OnBaseDestroyed()` controls objective/site lifetime; it does not itself unlock caravan reformation.

Two blockers are fixed on DMSE maps only:

1. Default `Site.CanReformFoggedEnemies=false` includes dormant threats in still-fogged areas.
2. Vanilla `Building_Turret.ThreatDisabled` ignores whether a refuelable gun has enough ammunition for even one shot.

The old draft’s Wing-Back theory was false: 50% of 600 is **300 absolute fuel units**, while `minimumFueledThreshold=0.9` is an absolute count, not 90%. Wing-Back starts armed. Field Air-Defense likewise starts with 100 cannon rounds and has a separate interceptor system. Armed, powered automated turrets remain real threats and must be destroyed, depowered, or genuinely exhausted.

### Missile loot

Ground missiles and missiles in `Building_MissileRack` already use vanilla item enumeration because the rack implements `IHaulSource`. `CompMissileRackStorage` does not; its contents are absent from both caravan and transporter lists, and list-only patching would still leave gathering jobs unable to discover them.

Once the site and storage are safe, the fix uses the real `ThingOwner.TryDrop` path to turn eligible contents into ordinary ground loot. Both collection pipelines then work without a custom job system or save data. Active hostile air-defense storage, fogged storage, disallowed reservations, non-DMSE maps, and home-area-only queries are not looted remotely.

---

## 修复机制 / Mechanism

1. `1.7/Patches/MissileBaseLaunchTargetPatch.xml` changes exactly three `validLaunchTarget` nodes. A startup fallback applies the same narrowly identified change if needed; interceptor/projectile world objects stay `false`.
2. A `WorldObject.CanReformFoggedEnemies` postfix returns `true` only for DMSE missile-base sites, matching vanilla temporary-camp behavior.
3. A `Building_Turret.ThreatDisabled` postfix applies only on those maps and only when the real gun lacks its per-shot fuel. Operational interceptor missiles or a fueled CIWS keep the building protected.
4. An `AllReachableColonyItems` postfix safely drops eligible `CompMissileRackStorage` contents only on those maps after visible hostiles are gone. Rack contents and ground items stay on vanilla paths.

No prefixes replace DMSE’s risk, re-entry, radar, launcher, or objective methods.

---

## 离线验证 / Offline validation

From this directory:

```powershell
dotnet build .\Source\DMSEMissileBaseFix.csproj -c Release
dotnet build .\Validation\Validation.csproj -c Release
dotnet .\Validation\bin\Release\net10.0\Validation.dll red
dotnet .\Validation\bin\Release\net10.0\Validation.dll green
```

Final results:

- **RED: 57 assertions passed**, reproducing the upstream launch rejection, fogged/dry-turret reform deadlocks, and comp-storage list/job omission.
- **GREEN: 87 assertions passed**, covering all three sites, both arrival options and real visit actions, re-entry cooldown, BVR risk confirmation, armed/dry/refueled/unpowered/unmanned turrets, ground/rack/comp-storage missiles, caravan/transporter discovery, real carry/transfer, reservation/fog/home/non-DMSE/active-hostile edge cases.

The harness loads real 1.7 game code, real `DMSE.dll`, and real DMSE XML. It reuses the QA copy of `Assembly-CSharp.dll` whose `ModsConfig` static initializer is disabled; the live `ModsConfig.xml` is never read or written by the harness. Only rendering, translation/UI presentation, spawn/despawn engine registration, pathfinding/region traversal, and the pawn carrying-capacity stat are headless boundaries. This is not a substitute for a final rendered in-game smoke test.

The active 309-mod configuration was audited read-only. Fortified’s relevant additive gizmo hook and capacity-turret hook were checked. The live log marks `CN.TrailblazerShuttle` and `smallmine.HeavyShuttle` incompatible with this 1.7 run, so they are not hidden active entry gates.

---

## 失效/退役条件 / Expiration conditions

Retire the corresponding fix when upstream:

1. Removes `validLaunchTarget=false` or sets it to `true` on all three site defs.
2. Gives these sites appropriate fogged-threat reform behavior and ensures genuinely disabled/out-of-ammo defenses do not block reformation while operational SAM/CIWS capability remains protected.
3. Makes every `CompMissileRackStorage` content discoverable **and actually collectible** by caravan and transporter workflows, not merely visible in a dialog.

Also revalidate after a RimWorld API change, DMSE type/def renames, or changes to turret ammunition and rack ownership semantics. Until then, removing this mod is safe: it adds no custom scribed fields; already dropped missiles remain ordinary map items.
