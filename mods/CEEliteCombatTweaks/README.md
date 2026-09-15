# CE Elite Combat Tweaks

Combat Extended 爽局 / 少人精英局数值补丁。

## 功能

- **瞄准时间**：`AimingDelayFactor` 的后处理曲线最低从 50% 放到 **1%**。
- **远程冷却**：操作能力、移动能力、呼吸能力给 `RangedCooldownFactor` 提供额外 offset，最多由本 mod 提供 `-75%`，角色面板会展开完整公式，并受原版 1% 最低值保护。
- **枪机循环下限**：最终远程冷却不会低于 `ticksBetweenBurstShots` 对应的射击间隔，避免前一段 burst 的最后一发到后一段 burst 的第一发快过枪械循环射速。

## 曲线

### 瞄准时间

`AimingDelayFactor` 的后处理曲线改为：

```text
(0.01, 0.01) → (0.25, 0.25) → (0.75, 0.75) → (1.0, 1.0) → (1.25, 1.25) → (2.0, 1.5)
```

也就是最低瞄准时间倍率从 CE 的 `50%` 降到 `1%`。

### 远程冷却

原版 `RangedCooldownFactor` 和 `AimingDelayFactor` 的 `1%` 是 `StatDef.minValue`，不是最终 `Verb` 层统一兜底。`VerbProperties.AdjustedCooldown()` 只是把武器冷却、`RangedCooldownFactor` 等相乘后转为秒数；所以本 mod 不再在最终秒数上 postfix，也不在 `StatPart.TransformValue` 里做最终乘算，而是在 `StatWorker.GetValueUnfinalized()` 的 base value 后注入一个 `-n%` offset。这样它位于 RimWorld 原本的“先加减，后乘除”管线里，后续 vanilla factors 仍照常生效。

```text
M = Manipulation / 操作能力
V = Moving / 移动能力
B = Breathing / 呼吸能力

score =
  0.55 * max(0, M - 1)
+ 0.30 * max(0, V - 1)
+ 0.15 * max(0, B - 1)

offsetReduction = 0.75 * (1 - exp(-score / 0.65))

unfinalizedValue = baseValue - offsetReduction + otherOffsets
finalRangedCooldownFactor = clamp(unfinalizedValue * factors, 0.01, maxValue)
```

`offsetReduction` 渐近 `0.75`，表示**本 mod 自己最多提供 -75% offset**；最终由原版 `StatDef.minValue = 0.01` 收到底。

### 枪机循环下限

CE 的 burst 内射击间隔由 `ticksBetweenBurstShots` 控制，原版/CE 也用它展示 burst fire rate（rpm）。本 mod 额外保证 burst 与 burst 之间的最终冷却也不低于这个间隔：

```text
cyclicInterval = ticksBetweenBurstShots / 60
finalCooldown = max(adjustedCooldown, cyclicInterval)
```

这样切到半自动时，连续两次扣扳机也不会比同一把枪的连发循环更快。CE 的 GunPatcher 中全自动模板会显式标 `ticksBetweenBurstShots`（如 AssaultRifle 约 5 ticks）；半自动模板未标时使用 vanilla 默认 15 ticks，即约 0.25s。

预期值：

| 操作 / 移动 / 呼吸 | offset | 本 mod 减少 |
|---:|---:|---:|
| 100% / 100% / 100% | -0% | 0% |
| 125% / 125% / 125% | -24% | 24% |
| 150% / 150% / 150% | -40% | 40% |
| 200% / 200% / 200% | -59% | 59% |
| 250% / 250% / 250% | -68% | 68% |
| 300% / 300% / 300% | -72% | 72% |
| 极限趋近∞ | -75% | 75% |

单项强化大致效果：

| 操作 / 移动 / 呼吸 | offset |
|---:|---:|
| 200% / 100% / 100% | -43% |
| 100% / 200% / 100% | -28% |
| 100% / 100% / 200% | -15% |
| 180% / 150% / 125% | -46% |
| 250% / 200% / 150% | -63% |

### 极端值保护

- 远程冷却 stat 值或能力值异常：转为非负有限值；最终 `RangedCooldownFactor` 不低于原版 1% 最低值。

## 启动自检

加载完成后会检查 XML patch 是否生效：

- 生效：打印 `[CE Elite Combat Tweaks] Stat XML patches active...`
- 失败：打印 `Log.Warning`，包含当前 `AimingDelayFactor@0.01`、`RangedCooldownFactor.elitePart`。

## 构建

```bash
cd Source/CEEliteCombatTweaks
dotnet build -c Release
```

DLL 输出到 `Assemblies/CEEliteCombatTweaks.dll`。
