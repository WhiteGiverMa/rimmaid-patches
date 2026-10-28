# 米莉拉：代达罗斯之翼不再受击掉耐久

适用于 RimWorld 1.6 / 1.7 的纯 XML sidecar。依赖 Milira Race（工坊 `3256974620`），加载顺序在其之后。packageId：`whitegiver.milira.daedaluswingswaist`。

## 问题

「代达罗斯之翼」（`Milira_DaedalusWings`）是 Belt 层 utility 装备，不是防御衣物，但它的 `bodyPartGroups` 写着 `Torso`。原版 `ArmorUtility.ApplyArmor` 会对**任何覆盖受击部位的 worn apparel** 无条件扣耐久，与 armor rating 无关：

```csharp
if (apparel.def.apparel.CoversBodyPart(part))
    ApplyArmor(..., armorThing: apparel, ...);
...
armorThing.TakeDamage(new DamageInfo(damageDef,
    GenMath.RoundRandom(damAmount * 0.25f)));
```

所以每次躯干中弹都会扣掉这次伤害 25% 的耐久。原版护盾腰带看起来不掉的真实原因是它覆盖 `Waist`：`Waist` 是一个 `coverage=0` 的概念部位，随机命中按 `coverageAbs` 加权，永远不会选中它；并不是「掉得慢」。

## 修改

1. 新增 `BodyPartGroupDef` / `BodyPartDef` `Milira_DaedalusMount`（中文名「后方悬浮」），`conceptual=true`。
2. 给 `Milira_Body` 与 `Human` 的 `corePart/parts` 各追加一个该部位的记录：`coverage=0`、`depth=Outside`、`groups=[Milira_DaedalusMount]`。
3. 把代达罗斯之翼的 `bodyPartGroups` 从 `Torso` 改为 `Milira_DaedalusMount`。

`depth` 用 `Outside` 是因为原版 `ApparelProperties.GetCoveredOuterPartsString` 只把 `depth == Outside` 的部位列进装备信息卡的「覆盖」栏。之前用 `Inside` 时 Character Editor 能看到 group，但装备卡显示「覆盖：无」。`coverage=0` 仍然保证它不会被 `GetRandomNotMissingPart` 的加权命中选中，所以改成 `Outside` 不会重新引入掉耐久。

结果是：

- 普通枪弹、近战、爆炸的随机命中都不会落在这个部位上，`CoversBodyPart` 不为真，`ApplyArmor` 不再对翅膀调用 `TakeDamage`。
- 它仍然是一件有耐久的物品（`useHitPoints=true`，保留 HP 条）；掉在地上被爆炸、火焰等**直接**伤害命中时仍会损坏。
- 装备信息卡的「覆盖」会显示「后方悬浮」，不再是「无」。
- 仍然占 `Belt` 层：两件代达罗斯之翼互斥。
- 与 `Waist` / `Torso` 组的腰带装备继续可以叠穿：超级计算背包、护盾腰带、寻路口袋、空投信标包、概念武器挂载等。**这是有意保留的**：原版代达罗斯（`Torso`）本来就和 `Waist` 装备可叠，但会和 `Torso` 组腰带（如空投信标包）冲突；改到独立组后，它不再和这些 `Torso` 腰带冲突，可以一起穿。
- 米莉拉与人类身体都获得「后方悬浮」部位；米莉安身体不改。其他使用自定义 body 的种族不会自动获得该部位。

## 安装

把本目录的 `About/`、`Defs/`、`Patches/`、`Languages/` 放进单独的 `Mods/MiliraDaedalusWingsWaist/`，在模组列表中启用，并保证它排在 Milira Race 之后。只改 Def，不修改上游工坊文件。游戏运行中不会热加载，需正常重启。

## 验证

```text
python Validation/validate.py
```

脚本会读取真实的 Milira 1.6 Defs 与 RimWorld Core 的 `Bodies` / `Apparel_Belts.xml`，回放本补丁，并检查：

- 上游代达罗斯仍是 `Belt` + `Torso`（上游变更时提醒复核）。
- 新部位同时出现在 `Milira_Body` 与 `Human`，不在任何 `coverage>0` 的部位上，`depth=Outside`。
- 代达罗斯的组已改为 `Milira_DaedalusMount`。
- `Milira_DaedalusMount` 不与任何可命中部位共享，`HasPartsToWear` 能找到该部位。
- `GetCoveredOuterPartsString` 的筛选条件能列出「后方悬浮」。
- 同款互斥；与 `Waist` / `Torso` 腰带装备可叠。
- Milian 身体没有新增部位。

当前输出：

```text
upstream Milira_DaedalusWings: layers=['Belt'] groups=['Torso']
patch operations applied: 3 (PatchOperationAdd, PatchOperationAdd, PatchOperationReplace)
patched Milira_DaedalusWings: layers=['Belt'] groups=['Milira_DaedalusMount']
...
upstream Torso wings + Torso drop beacon: CONFLICT

PASS: wings moved to a conceptual coverage=0 rear mount on Milira and Human;
no hittable part shares the group; the apparel card lists the mount;
same wings conflict; Waist/Torso belts stack.
```

## 退役信号

上游把代达罗斯之翼的 `bodyPartGroups` 改成别的组、或修改 `Milira_Body` / `Human` 的 `corePart/parts` 结构时，重新跑验证并核对 XPath；不要盲目重打。
