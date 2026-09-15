# BugFix: CE 红字合集

**日期**: 2026-07-03
**补丁模组**: `BugFix_CE_BodyShapeFallback`
**影响模组**: Combat Extended (ceteam.combatextended)

本模组合并了三个 CE 相关的红字修复，均为 CE 自身的误报/边界情况处理不足。

---

## Bug #1: CE returning BodyType Undefined

**触发**: Dead Man's Switch Legion 等缺少 CE bodyShape 的 mod
**场景**: 鼠标悬停显示射击精度提示时

### 堆栈

```
CE returning BodyType Undefined for pawn <illustrative pawn>
  at CombatExtended.CE_Utility:GetCollisionBodyFactors(Pawn)
  ...
  (鼠标悬停提示时触发)
```

### 根因

CE 的 `GetCollisionBodyFactors` 通过 `RacePropertiesExtensionCE.bodyShape` 获取碰撞体型。DMS Legion 15 个中型机械体未定义 bodyShape → CE fallback 到 Invalid 并打 ErrorOnce。不影响游戏功能，但刷屏红字。

### 修复

- **Harmony Prefix**: bodyShape 缺失时用 `pawn.BodySize` 计算合理 fallback（`w=BodySize*0.4, h=BodySize*1.25`），跳过原方法
- **XML Patch**: 为 DMS Legion 15 个机械体补充 `<bodyShape>Humanoid</bodyShape>`

---

## Bug #2: penAmount or armorAmount are zero for Blunt

**触发**: CE 近战格挡伤害计算
**场景**: 战斗中用武器格挡时

### 堆栈

```
penAmount or armorAmount are zero for Blunt on <illustrative weapon>
  at CombatExtended.ArmorUtilityCE:TryPenetrateArmor(...)
  at CombatExtended.ArmorUtilityCE:ApplyParryDamage(...)
  at CombatExtended.Verb_MeleeAttackCE:DoParry(...)
```

### 根因

`TryPenetrateArmor` 对武器做格挡伤害计算时，`penAmount`（穿甲值）和 `armorAmount`（ToughnessRating）同时为零。CE 的报错检查只看 `ArmorRating_Sharp/Blunt/Heat` 三维——武器没有这些护甲统计，自然全是零。**行为正确**（零穿甲对零护甲 = 无伤害），红字是误报。

### 修复

- **Harmony Prefix**: 当 `penAmount==0 && armorAmount==0` 时直接返回（不格挡，无伤害），跳过原方法的错误日志

---

## 源码

- `Source/BodyShapeFallbackPatch.cs` - Bug #1
- `Source/ParryDamageFix.cs` - Bug #2
- `Patches/DMSL_Mech_BodyShape.xml` - DMS 特定修复

## 向上游报告

### CE 侧
- GitHub: https://github.com/CombatExtended-Continued/CombatExtended
- Bug #1: 建议 `GetCollisionBodyFactors` 中 bodyShape 为 null 时用 bodySize 推算默认值而非报错
- Bug #2: 建议 `TryPenetrateArmor` 对非护甲物品（武器）跳过护甲三维检查

### DMS 侧（Bug #1）
- QQ 群: 1040461987（AOBA）
- DMS Legion CE patch 漏了 15 个中型机械体的 `bodyShape`
