# CE Breaching Fix

Combat Extended 本地兼容补丁：修复破墙袭击对墙/山体目标的 LoS 与施法位链路问题。

## 功能

- 对 `Fillage=Full` 或 `mineable` 的建筑目标，放宽 CE `CanHitTargetFrom` / `TryFindCEShootLineFromTo` 的 LoS 失败，但保留射程与最小射程检查。
- `JobGiver_AIBreaching` 生成无 firing position 的 `UseVerbOnThing` 时转为短 `Wait`，避免同 tick job 雪崩。
- `Pawn_JobTracker.StartJob` 对同一个 pawn 同 tick 重复启动破墙 `UseVerbOnThing` 做熔断。
- `Toils_Combat.GotoCastPosition` 对破墙 job 直接使用 `targetB` 里已有 firing position，避免二次 `CastPositionFinder.TryFindCastPosition` 触发补丁链异常。
- 开始移动前刷新 stale breaching verb，避免 `Verb ... needs caster to work` 红字。

## 判定公式 / 规则

本 mod 不改伤害、命中率或弹道，只改“破墙目标是否允许被 CE 射击线接受”的控制流。

### 可放宽 LoS 的目标

```text
target is Building
AND (target.def.Fillage == Full OR target.def.mineable == true)
```

即普通墙、山体岩壁等破墙目标；Pawn、门外普通目标、非 full-fillage 建筑不会被放宽。

### 射程仍然必须合法

```text
distSq = (root - target.Cell).LengthHorizontalSquared
allow = distSq <= EffectiveRange^2 AND distSq >= verbProps.minRange^2
```

所以这是 LoS 兼容修复，不是隔地图开火。

### 同 tick 熔断

```text
same pawn starts a breaching UseVerbOnThing more than once in the same game tick
→ replace new job with Wait(60)
```

无 firing position 的破墙 job 也会转为 `Wait(60)`，避免原版 `started 10 jobs in one tick` 雪崩。

## 不包含

- 不改 CE 弹道/护甲/过穿透。
- 不改 pawn 瞄准时间、武器掌握、瞄准精度等战斗数值。

## 构建

```bash
cd Source/CEBreachingFix
dotnet build -c Release
```

DLL 输出到 `Assemblies/CEBreachingFix.dll`。
