# WVC Work Modes × Combat Extended 兼容补丁

> 创建日期: 2026-07-02
> 补丁版本: 1.1.0

---

## 问题

- **触发条件**: 同时使用 Combat Extended (`CETeam.CombatExtended`) + 任意调用 `GenRadial.RadialCellsAround` 传大半径的 mod
- **错误信息**: `Not enough squares to get to radius 199. Max is 119`

### 三条触发路径（v1.0 只修了第一条，v1.1 修了前两条）

```
路径 A: WVC Work Modes
  WVC_WorkModes.ShutdownUtility:TryFindNearbyMechSelfShutdownSpot
  → GenRadial.RadialCellsAround(root, MaxRadialPatternRadius - 1f, ...)  // ~199

路径 B: Vanilla RimWorld (v1.1 新增修复)
  RimWorld.RCellFinder:TryFindNearbyMechSelfShutdownSpot
  → Need_MechEnergy:NeedInterval()  // 所有机械体每 tick 触发

路径 C: Vanilla RimWorld (v1.2 新增修复 — CE 上游)
  RimWorld.CompPolluteOverTime:Pollute
  → GenRadial.NumCellsInRadius(199f)  // 直接调用，不经过 RadialCellsAround
```

### 根因

`GenRadial.MaxRadialPatternRadius` 是编译期 const（vanilla ~200）。CE 的 Harmony patch 将 `NumCellsInRadius` 的预计算数组上限设为 `MAX_RADIUS=119`。大半径触发 `Log.Error`（v1.0 版本中是 Error）。

---

## 修复（v1.1.0）

| 版本 | 策略 | 覆盖范围 |
|---|---|---|
| v1.0.0 | 精准拦截 WVC 的 `TryFindNearbyMechSelfShutdownSpot` | ❌ 只有 WVC |
| **v1.1.0** | **全局 Prefix 拦截 `GenRadial.RadialCellsAround`** | ✅ 原版 + WVC + 所有 mod（走 `RadialCellsAround` 的） |
| **v1.2.0** | **补 CE 上游：`Log.Error` → `Log.Warning`** | ✅ 所有路径（包括直接调用 `NumCellsInRadius` 的，如 `CompPolluteOverTime`） |

v1.1.0 用一个 Harmony Prefix 直接 patch `GenRadial.RadialCellsAround(IntVec3, float, bool)`，将 radius 参数 cap 到 118。一刀截流，所有调用方都不需要单独 patch。

### 工作原理

```csharp
[HarmonyPatch(typeof(GenRadial), "RadialCellsAround")]
static void CapRadius_Prefix(ref float radius)
{
    if (radius > 118f)
        radius = 118f;
}
```

CE 的 `NumCellsInRadius(119)` 仍返回 44469 个格子——118 范围内完全够用。

---

## 上游 PR

| PR | 仓库 | 改动 |
|---|---|---|
| [#6](https://github.com/WVCSergkart/MoreMechanoidsWorkModes/pull/6) | WVC Work Modes | 搜索半径 `Math.Min(MaxRadialPatternRadius - 1f, 118f)` |
| [#4645](https://github.com/CombatExtended-Continued/CombatExtended/pull/4645) | Combat Extended | `Log.Error` → `Log.Warning` |

---

## 加载顺序

```
Harmony → Core/DLC → Combat Extended → WVC Work Modes → 本补丁
```

补丁在 `StaticConstructorOnStartup` 激活，全部 mod 就位后运行。

---

## 后续清理

等上游 PR 合并、Steam Workshop 更新后可删除本补丁。
