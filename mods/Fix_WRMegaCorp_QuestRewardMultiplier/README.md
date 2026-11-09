# WRMegaCorp Quest Reward Multiplier Fix / 任务倍率初始化补丁

RimWorld **1.6 / 1.7** 纯 XML 补丁，packageId `whitegiver.fix_wrmegacorp_questrewardmultiplier`。只依赖并 `loadAfter` Wolfein Race MegaCorp Edition，不修改上游文件。

## 症状与根因

自然任务刷新（`NaturalRandomQuestChooser` → `QuestScriptDef.CanRun`）时报：

```text
Tried to use variable "questRewardMultiplier" in a math expression but it doesn't exist.
  at RimWorld.QuestGen.SlateRef`1.RegexMatchEvaluatorConcatenateZeroIfEmpty
  at RimWorld.QuestGen.QuestNode.TestRun
  at RimWorld.QuestScriptDef.CanRun
```

WRMegaCorp 的三个任务脚本引用了 `$questRewardMultiplier`，但没有任何 `QuestNode_Set` 在第一次使用前初始化它：

| Def | 使用点 |
|---|---|
| `WRMC_MeatOrder` | `rewardValue` 的数学表达式直接乘该变量 |
| `WRMC_DepositExploitation` | `QuestNode_Multiply` 的 `value2` |
| `WRMC_ProjectLiquidation` | `QuestNode_Multiply` 的 `value2` |

`SlateRef.TryGetConvertedValue` 在数学表达式里遇到缺失变量时按 **0** 处理（`RegexMatchEvaluatorConcatenateZeroIfEmpty` 返回 `"0"`）。所以这不只是一条红字：三个任务的 `rewardValue` 都会变成 0。`WRMC_MeatOrder` 的 `QuestNode_GiveRewards` 还设置了 `useDifficultyFactor="false"`，完全依赖这个倍率。

历史上本机曾在 2026-07 直接改工坊 XML 补过，后来工坊包更新覆盖了本地修改；本次改为独立补丁 mod，上游更新不会丢失。

## 修复范围

用 XCEL 在第一次使用前插入一个 `QuestNode_Set`，把 `questRewardMultiplier` 设为 `1`：

- 只加变量初始化，不改奖励公式、难度系数、任务权重、材料或任何上游 Def。
- `PatchOperationConditional` 包裹：上游删除/改名这些 Def 时安静跳过，不刷 patch 失败红字。
- 若上游以后自己补了初始化，这里多一个同值 `Set` 也无行为影响。

## 安装与游戏浅测

游戏关闭时，把本 mod 的 `About/`、`Patches/` 放到游戏 `Mods/Fix_WRMegaCorp_QuestRewardMultiplier/`，启用并排在 WRMegaCorp 之后（例如排在 `whitegiver.fix_wrmegacorp_quest_exposedata` 之后，顺序不敏感）。

下次正常启动后，等自然任务刷新一次（或开发者模式生成任务），确认：

- `Player.log` 不再出现 `Tried to use variable "questRewardMultiplier"`。
- 接到 `WRMC_MeatOrder` / `WRMC_DepositExploitation` / `WRMC_ProjectLiquidation` 时，奖励不再为空/0。

不需要改档、重建建筑或切换其他 mod。

## 维护与退役

- 当前验证等级：**XML 语法与 XPath 命中离线通过**（三处各命中 1 次，插入后各有 1 个 `Set` 与 1 处使用）；游戏加载与任务奖励浅测待确认。
- 上游若给三个脚本补回 `QuestNode_Set`，本补丁可停用；停用不需要存档迁移（不添加 Def 或持久化字段）。
- 上游若继续删改变量名，`PatchOperationConditional` 会跳过，届时先核对新脚本再更新本补丁。
