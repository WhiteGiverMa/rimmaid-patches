# Standalone DMSE missile-base bug report / DMSE 导弹阵地独立问题反馈

This file intentionally covers **only** shuttle/pod entry, post-combat caravan reformation, and missile looting. It contains no performance feedback.

本文件只覆盖穿梭机/运输舱进入、清场后重组远行队和导弹战利品问题，可独立发送，不混入性能话题。

---

## 中文版（可直接发送）

AOBA 你好！我在 RimWorld 1.7 的 DMSE 敌方阵地（`DMSE_MissileBase` / `DMSE_FieldLaunchSite` / `DMSE_EarlyLaunchSite`）遇到一组站点交互问题。对照真实 XML、1.7 `Assembly-CSharp.dll` 和当前 `DMSE.dll` 后，定位如下。

### 1. 穿梭机/运输舱无法突击进入阵地

三个阵地的 `WorldObjectDef` 都设置了：

```xml
<validLaunchTarget>false</validLaunchTarget>
```

原版 `CompLaunchable.ChoseWorldTarget` 会在显示边缘/中心降落选项之前，用这个字段直接拒绝目标；DMS Core 的 lifter 也有同样检查。因此无法进入不是燃料或降落格导致的。

建议三个阵地删除该行（默认 `true`）或改为 `true`。这样仍会通过：

- `TransportersArrivalAction_VisitSite.CanVisit` 和你在 `Patch_MissileBaseReentry` 中的重入冷却；
- `Patch_InterceptRisk_TryLaunch` / `Patch_InterceptRisk_CaravanShuttle` 的防空风险确认；
- 后续真实防空/BVR 拦截。

也就是说，玩家可以主动顶着防空网突击，而不是绕过防空机制。

### 2. 步行清场后仍不能「重组远行队」

三个 Def 已有 `WorldObjectCompProperties_FormCaravan`，不是缺少组件。`MapComponent_MissileBase.OnBaseDestroyed()` 负责阵地摧毁/站点存续，但不会直接改变 `FormCaravanComp` 的威胁判定。

我确认有两种会锁住 `CanFormOrReformCaravanNow` 的情况：

1. `Site` 使用默认 `CanReformFoggedEnemies=false`，未揭开区域里的休眠敌人也计入 `AnyActiveThreatNow`。若这不是设计意图，可让这类阵地采用与临时营地类似的迷雾威胁规则。
2. 原版 `Building_Turret.ThreatDisabled` 检查电源/操作员/休眠等，却不检查炮塔当前弹药能否满足 `AttackVerb` 的单发消耗。彻底无弹但仍然供电的炮塔会一直阻塞重组。

这里特别需要区分武器系统：`DMSE_Building_FieldAirDefense` 即使机炮弹耗尽，只要独立拦截弹舱仍有弹且设备运行，就仍是活跃防空系统；不能仅因 `CompRefuelable` 为空就把它视为可安全拆取。

另外，旧有排查中曾把 Wing-Back 的 `initialFuelPercent=0.5` 与 `minimumFueledThreshold=0.9` 误读为「50% 低于 90%」。实际 600 × 0.5 = **300**，而阈值 **0.9 是绝对数量**，所以它出生时 `HasFuel=true`。不是初始哑炮问题；需要处理的是实际耗尽后的状态。

### 3. 阵地内导弹战利品无法完整带走

- 地面物品正常；
- `Building_MissileRack` 已实现 `IHaulSource`，弹架内导弹可以被原版远行队/运输装载枚举；
- `CompMissileRackStorage` 虽然是 `IThingHolder`，但不是 `IHaulSource`，所以野战防空炮、`DMSE_FieldStructure_AmmoStorage` 等建筑里的实体导弹不会进入 `AllReachableColonyItems` / `AllSendableItems`。

而仅把这些对象加到 UI 列表还不够，因为它们仍未生成到地图上，`GenClosest` 驱动的远行队收集/运输舱搬运搜索找不到它们。

建议让这类建筑通过建筑侧 `IHaulSource` 暴露库存，或在安全/失效后提供正常的卸弹到地面流程。最好同时验证「界面可见 → 小人能搜索到 → 实际搬运 → 容器扣减且不复制」完整链路。活跃敌对防空系统仍应阻止远程取弹。

### 离线验证情况

我用真实程序集和 XML 建立了独立 RED/GREEN 验证：上游 RED 57 项断言复现问题；侧车修复 GREEN 87 项断言覆盖三个站点、进入菜单、重入冷却、BVR 风险确认、炮塔各状态、三种导弹位置及实际搬运转移。没有修改任何工坊文件或存档。

很喜欢阵地突袭和防空风险设计，以上供你更新时参考。谢谢！

---

## English version (ready to send)

Hello AOBA,

On RimWorld 1.7 I encountered a set of site-interaction bugs in DMSE’s enemy positions (`DMSE_MissileBase`, `DMSE_FieldLaunchSite`, and `DMSE_EarlyLaunchSite`). I checked the actual XML, the 1.7 `Assembly-CSharp.dll`, and the current `DMSE.dll`; the findings below are limited to those issues.

### 1. Shuttles/transport pods cannot assault the sites

All three `WorldObjectDef`s explicitly contain:

```xml
<validLaunchTarget>false</validLaunchTarget>
```

Vanilla `CompLaunchable.ChoseWorldTarget` rejects the target on this field before displaying edge/center arrival options. DMS Core’s lifter repeats the same gate. This is not a fuel or landing-cell problem.

Please remove that line (the default is `true`) or set it to `true` for these three sites. Entry will still pass through:

- `TransportersArrivalAction_VisitSite.CanVisit` and your `Patch_MissileBaseReentry` cooldown;
- `Patch_InterceptRisk_TryLaunch` / `Patch_InterceptRisk_CaravanShuttle` interception-risk confirmation;
- the actual air-defense/BVR interception path.

Players can then deliberately assault through the air-defense network rather than bypass it.

### 2. Clearing the site on foot does not unlock “Reform caravan”

The three defs already include `WorldObjectCompProperties_FormCaravan`, so the comp is not missing. `MapComponent_MissileBase.OnBaseDestroyed()` controls the destroyed state/site lifetime, but does not directly unlock `FormCaravanComp`.

I confirmed two blockers for `CanFormOrReformCaravanNow`:

1. `Site` inherits `CanReformFoggedEnemies=false`, so dormant enemies in still-fogged areas count toward `AnyActiveThreatNow`. If that is not intended, these sites could use temporary-camp-like fogged-threat behavior.
2. Vanilla `Building_Turret.ThreatDisabled` checks power/manning/dormancy but not whether the current ammunition satisfies the attack verb’s per-shot cost. A powered but completely dry turret can therefore block reformation indefinitely.

The weapon systems need to remain distinct: when Field Air-Defense’s cannon ammunition is empty but its separate interceptor magazine is loaded and operational, it is still an active air-defense system. It must not become remotely lootable just because its cannon `CompRefuelable` is empty.

One earlier investigation incorrectly interpreted Wing-Back’s `initialFuelPercent=0.5` and `minimumFueledThreshold=0.9` as “50% below 90%.” In reality, 600 × 0.5 = **300**, and **0.9 is an absolute fuel count**, not a percentage. Wing-Back starts with `HasFuel=true`; it is not initially inert. The relevant case is genuine ammunition exhaustion.

### 3. Not all missile loot can be taken home

- Ground items work normally.
- `Building_MissileRack` implements `IHaulSource`, so its missiles are visible to vanilla caravan/transporter enumeration.
- `CompMissileRackStorage` is an `IThingHolder` but not an `IHaulSource`. Missiles held by Field Air-Defense, `DMSE_FieldStructure_AmmoStorage`, and similar buildings are therefore omitted from `AllReachableColonyItems` and `AllSendableItems`.

Adding those objects only to a dialog is insufficient: they remain unspawned and undiscoverable by the `GenClosest`-based caravan/transporter gathering searches.

Please consider exposing the inventory through building-side `IHaulSource`, or providing a normal unload-to-ground path once the storage is safe/disabled. Ideally verify the whole chain: visible in UI → discoverable by a pawn → physically collected → source decremented with no duplication. Active hostile air-defense storage should still reject remote extraction.

### Validation

My standalone RED/GREEN harness uses the real assemblies and XML. RED reproduces the upstream issues with 57 assertions; the sidecar GREEN suite passes 87 assertions covering all three sites, arrival menus, re-entry cooldown, BVR risk confirmation, turret states, all three missile locations, and actual pickup/transfer. No Workshop files or saves were modified.

I really enjoy the site-assault and interception-risk design. I hope this isolated report is useful. Thank you!
