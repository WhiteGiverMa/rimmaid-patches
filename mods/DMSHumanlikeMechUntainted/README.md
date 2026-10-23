# DMS Humanlike Mech Untainted

让失能机关（The Dead Man's Switch）系列里**能穿人类衣服的机兵**在死亡时不再把穿着的衣物标记为「死人衣服」（tainted / worn by corpse）。

## 背景

RimWorld 原生逻辑里，任何 pawn 死亡时，其 `Pawn_ApparelTracker.Notify_PawnKilled` 会对每件 worn apparel 调用 `Apparel.Notify_PawnKilled()`，后者在 `def.apparel.careIfWornByCorpse` 为真时把 `wornByCorpseInt` 置为 `true`，于是这件衣服就成为「死人衣服」，带来大量 debuff。

DMS 的 Synthetic（合成人形）与 Legion（军团）里有一批机兵设计上可以穿人类衣服——包括：

- **DMS Core**：女士 `DMS_Mech_Lady`（`Races_Synthroid.xml`）、蛙人 `DMS_Mech_Frogman`（`Races_Synthroid_Frogman.xml`，经父类 `DMS_ArtificialOrganismHumanlikeBase` 继承）
- **DMS Synthetic**：侍女 `DMS_Mech_Maiden`、军人 `DMS_Mech_Armiman`、大姐头 `DMS_Mech_BigSister`
- 以上均通过 `Fortified.CompProperties_MechApparel` 获得穿衣能力
- **DMS Legion**：尘世 `DMSL_Mech_Terra`，通过 `DMS_Legion.CompProperties_TerraBodyType` 及专属 render patch 获得穿衣能力

这些机兵本身就是机械，不是人类，也不存在「人死了衣服穿过就忌讳」的叙事。穿着的衣服被死亡打上 tainted 标记不符合直觉。本补丁只针对这些机兵生效。

## 工作原理

对 `RimWorld.Apparel.Notify_PawnKilled()` 注册 Harmony prefix：

1. 读取 `Apparel.Wearer`；
2. 当 wearer 满足下面**全部**条件时，跳过原方法的 `wornByCorpseInt = true` 赋值，但手动触发其所有 `ThingComp.Notify_WearerDied()`，保持其他死亡回调不丢失：
   - `pawn.RaceProps.IsMechanoid` 为真（确为机兵）
   - `pawn.def.modContentPack.PackageIdPlayerFacing` 属于 `aoba.deadmanswitch.core` / `aoba.deadmanswitch.synthetic` / `srxf.deadmanswitch.legion` 三者之一（忽略大小写，防止其他 mod 撞 defName）
   - `pawn.AllComps` 里存在 `Fortified.CompMechApparel`（FFF 穿衣组件，未来同机制机兵自动兼容）**或** `DMS_Legion.CompTerraBodyType`（Legion 尘世专用组件）
3. 其他 pawn 一律放行原方法，行为完全不变。

**耐久损伤**由 `Pawn_ApparelTracker.Notify_PawnKilled` 的前半段（`dinfo.Def.ExternalViolenceFor` 分支）独立处理，本补丁不影响。

## 边界

- 只影响三个 DMS 包提供的机兵；其他 mod 的穿衣机兵、人类、动物不受影响。
- 对机兵死后已被卸下的衣服（例如 Legion Terra 自带 `CompTerraBodyType.Notify_Killed` 里的 `DropAllEquipment`）也生效：因为 vanilla 的 tainted 标记发生在 `Pawn.PreDeathPawnModifications` 阶段，早于 `base.Kill` 触发 comps 的 `Notify_Killed`，所以这件衣服在掉落前已被本补丁免于 tainted。
- 不清理存档里已经被标记为 tainted 的旧衣服。
- 不改工坊原件、不全局 patch `Apparel` 的其他方法、不吞任何异常、无设置 UI。

## 依赖

- Harmony（`brrainz.harmony`）
- 仅在加载了以下任一 mod 时才有意义（补丁对未安装的内容软容错，不报错）：
  - The Dead Man's Switch（`Aoba.DeadManSwitch.Core`，1.7 版工坊 3819799478）
  - The Dead Man's Switch - Synthetic（`Aoba.DeadManSwitch.Synthetic`，工坊 3374615241）
  - The Dead Man's Switch - Legion（`Srxf.DeadManSwitch.Legion`，工坊 3666117000）

## 退役信号

- DMS Core / Synthetic / Legion 任一上游提供「机兵死亡不染污」的原生开关或修复；
- 或 RimWorld 本体调整 `Apparel.Notify_PawnKilled` 的行为，使得机兵死亡默认不标 tainted；
- 届时直接停用并删除本 mod。

上游 XML/DLL 更新后需复核：本补丁的识别逻辑基于 `Fortified.CompMechApparel` 与 `DMS_Legion.CompTerraBodyType` 两个类型的 FullName，若上游重命名/移动这两个类需同步更新。

## 源码

- 真源：`G:\dev\rimmaid-patches\mods\DMSHumanlikeMechUntainted`
- 部署：`A:\SteamLibrary\steamapps\common\RimWorld\Mods\DMSHumanlikeMechUntainted`
- 构建：`dotnet build -c Release`（TargetFramework `net10.0`，RimWorld 1.7 Waylith）
