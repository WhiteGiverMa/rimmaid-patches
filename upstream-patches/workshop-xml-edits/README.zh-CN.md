# 工坊 XML 编辑归档（中文说明）

[English](README.md)。中英文说明对应同一归档状态。

这些是对直接在 Steam 创意工坊目录内所做的本地改动的最小恢复描述。它们不是独立 mod，Steam 更新可能覆盖它们。

可恢复载荷以实际 XML 工件形式存放在本 README 旁（许可证与来源见上级目录 `LICENSES.md` / `LICENSES.zh-CN.md`）：

| 工件 | 覆盖内容 |
|---|---|
| `dms-mobiledragoon-ce-armor.xml` | DMS MobileDragoon CE 装甲耐久（五个操作逐字恢复） |
| `wolfein-sabre-ce-tag.xml` | Wolfein 合金军刀 `CE_OneHandedWeapon` 标签（操作逐字恢复） |
| `wolfein-empty-numeric-fields.xml` | Wolfein 空数字字段移除（等价的 PatchOperationRemove 形式） |
| `wrmegacorp-quest-multiplier.xml` | WRMegaCorp `questRewardMultiplier` 初始化片段 |
| `DamageDefs_UFC_Stubs.xml` | UF 占位 DamageDef（本地文件的逐字副本，完全为本方内容） |

## Wolfein Race 空数字字段

- 工坊物品：`3473140562`
- 文件：`1.6/Defs/ThingDefs_Weapon/Melee_Industrial.xml`
- 移除空的 `relicChance` 与 `equippedAngleOffset` 元素。缺失值会落到预期的零默认值，而空字符串会导致浮点解析失败。
- 原始改动是直接编辑文件移除（各五处）；改动前状态保留在工坊目录的 `Melee_Industrial.xml.bak`。归档工件把同样的移除表达为带 `not(node())` 谓词的 `PatchOperationRemove`，因此有值的字段不受影响。
- 同一缺陷在工坊更新后曾复发，重打前先检查当前 XML。

## Wolfein CE 合金军刀标签

- 工坊物品：`3485371294`
- 文件：`1.6/Patches/ThingDefs_Weapon/W_Weapon_Melee.xml`
- 针对 `Defs/ThingDef[defName="W_Weapon_Melee_AlloySabre"]/weaponTags` 的 `PatchOperationAdd`，其 value 必须直接包含 `<li>CE_OneHandedWeapon</li>`，而不是再嵌套一层 `<weaponTags>` 元素。
- 当前工坊文件已是正确形式（2026-09-15 核实）；工件逐字保存了该形式。

## DMS MobileDragoon CE 装甲耐久

- 工坊物品：`3377130226`
- 文件：`1.5/CE/Patches/CE_Frames.xml` 与 `1.6/CE/Patches/CE_Frames.xml`
- 五个框架核心被赋予 `CombatExtended.CompProperties_ArmorDurability`：PF-3 1400、AT-34 2000、FA-47 3000、PV-4 4200、PV-8 4400。均为可用钢铁 10 修理、修理时间 300、修理值 200、最低装甲百分比 0.6。
- 五个操作从现行 1.6 文件逐字恢复；1.5 副本携带相同代码块（归档时 diff 核实）。
- 本地 CE fork 同时移除了 `StatPart_NaturalArmorDurability` 中不安全的 Pawn 强转；只要护甲仍使用该组件，两侧必须一起保留。

## WRMegaCorp 任务奖励倍率

- 工坊物品：`3687841204`
- 在 MeatOrder、ProjectLiquidation、DepositExploitation 中，于首次使用前加入 `QuestNode_Set`，设 `questRewardMultiplier = 1`。
- 归档时第一、第三个脚本仍含本地初始化，而 ProjectLiquidation 没有。每次工坊更新后都要复查。
- 归档工件仅保存插入片段；ProjectLiquidation 的奖励计算不同，未先复现原始失败前不得插入。

## UF CE 补丁缺失定义

- 工坊物品：`3748582975`
- 已安装的 CE 补丁引用了三个 UFPC 伤害定义和一个贴图，而 UFPC 未安装。本地副本添加了 `UFC_Bullet_50P`、`UFC_Bullet_100P`、`UFC_Bomb_Buildingkiller` 的占位定义，以及占位贴图 `Textures/Things/Projectile/Bullet_UFC_SA.png`。
- 占位定义文件逐字保存在 `DamageDefs_UFC_Stubs.xml`。原贴图是在原模组内把 `Bullet_kt_small.png` 复制到 `Textures/Things/Projectile/Bullet_UFC_SA.png`；这里不重新发布第三方图像。如需恢复，应从原包取得该图片，或提供许可合适的替代图。本次未生成新贴图、未部署。
- 这些占位只在没有已安装内容使用那些弹丸时用于压制未解析引用。若上游包加入条件定义或真实资产，应移除它们。

## 当前缺失的历史补丁

这些被有意记录而非恢复。再次应用前，先针对当前依赖版本复现原始失败。

- **RPG Style Inventory Revamped（`2478833213`）**：较早的本地 IL 编辑给 `CEPatches.RPG_CEPatch+RPG_CEPatches.TryDrawOverallArmor` 与 `TryDrawOverallArmor1` 加了 pawn 空保护。后来的工坊更新替换了 DLL，保护目前缺失。
- **Combat Extended 压制边界**：`SuppressionUtility.IsOccupiedByEnemies` 此前在 `ThingsListAt(cell)` 前接收 `cell.InBounds(pawn.Map)`。当前本地 CE 构建不含这一历史保护。
- **Combat Extended 目标高度**：较早的实验让完全被遮挡的矮小目标保留真实高度，而不是把目标射程抬到附近掩体之上。当前本地 CE 构建不含它；这会改变战斗行为，未经新验证不得重打。
