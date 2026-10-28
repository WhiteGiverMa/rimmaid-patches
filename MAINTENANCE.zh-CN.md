# 补丁维护与停用条件

本仓库是已有本地补丁的备份，不是一套应全部启用的整合包。`manifest.json` 的 active 只描述导入时的本机配置；不证明对最新上游、其他存档或所有模组组合都有效。每个补丁的硬依赖和顺序以自身 `About/About.xml` 为准。

## 日常维护

1. 只在本仓库对应目录修改自有补丁。上游 fork 继续在各自仓库维护。
2. 更新依赖后核对 XML XPath 的实际命中、Harmony 目标签名及方法体，不能只看编译通过。
3. 构建到隔离输出；部署前比较新旧 DLL，并保留可回退版本。不要让构建顺带写入正在游玩的游戏目录。
4. 需要换发布 DLL 时，同步源码、说明与 `checksums.sha256`，并运行 `pwsh -File tools/verify-repository.ps1`。
5. 构建和归档校验不能替代游戏测试。对行为修复至少确认一次最小触发动作；没有测试过就保留“待验证”。

## 分项复核

| 补丁目录 | 依赖更新后必须复核 | 可退役或需重做的信号 |
|---|---|---|
| CEOverpenetration | CE 弹丸命中、Tick、碰撞历史及保存成员 | 弹丸重构或上游已实现同等过穿机制；这是功能模组，不因某个 bug 修复就自动退役 |
| CEEliteCombatTweaks | 原版/CE 冷却统计、连发间隔 | 枪机循环约束或统计管线改变；数值偏好仍由使用者决定 |
| CEBreachingFix | CE 射线/射击位及原版破墙作业 | 上游修复同一失败链，或旧 Harmony 目标/假设不再成立 |
| DMS_Synthetic_Apparel_Repair | DMS、Fortified、Legion、Milira 维修入口 | 上游提供等效装备维修或回调签名改变 |
| Fix_ExosuitDummyMeat | Exosuit 的 Dummy race | 上游为 Dummy 声明正确的 useMeatFrom 或取消该肉类定义 |
| BossgroupConcurrentSummons | Bossgroup 呼叫可用性、冷却判断及 Cinders 入口 | 呼叫链重构；这是可选玩法改动，不视为通用 bug 修复 |
| DMSWorkTypeCacheFix | 原版阵营变化、机兵组件初始化与工作缓存 | 上游在正确生命周期自动刷新缓存，或早期初始化边界变化 |
| KeyzAllowUtilitiesFinishOffFix | Keyz Finish Off 目标校验 | 上游已经在解引用前处理 mapless 目标 |
| WVC_CE_CompatPatch | GenRadial 重载、CE 半径上限 | 所有触发入口均正确限制半径；不要仅凭一个 WVC PR 合并删除全局补丁 |
| DubsPerformanceAnalyzerFix | DPA Tick Things 分组与 def-less tick | 上游修复相同畸形 Def 路径；正常 tick 必须保留 |
| BugFix_CE_BodyShapeFallback | CE 体型、格挡方法和 DMS XML | 上游补齐定义或改变穿甲/碰撞管线，旧回退可能过时 |
| BugFix_FortifiedCE_MinifyNullGuard | 已知历史实现错误 | **不建议安装。Prefix 返回 false 不会跳过 Postfix，不能实现原说明声称的保护。此次迁移未修复或停用本机副本。** |
| CE_Fix_IsHunkering_NRE | CE 战术作业、压制与治疗预约 | 必须分别检查两项功能；一个上游修复合入不足以证明整包可移除 |
| CE_Fix_MiliraFloatUnitElevation | CE 碰撞高度、自动目标选择和 Retarget 锚点 | CE/Milira 改为原生支持浮游高度或更换目标选择路径 |
| Fix_CeleTech_DuplicateEquippableComps | CeleTech 七个武器 Def 继承、传奇武器卸装回调 | 作者去掉重复组件会使 Remove 命中数变化；作者补全卸装回调后须避免重复通知 |
| CeleTechManagedWorkTableFix | `TryConsumeGatheredMaterials()` 的唯一 `ListerThings.ThingsInGroup(ThingRequestGroup)` 调用及整堆扣料路径 | 上游改为快照/安全枚举后停用；方法签名或调用数变化须复核，拒绝应用日志代表未生效 |
| CicadaVolumeControl | 三个 `Ambient_*Insects_*` SoundDef 的 Main 参数映射、`SoundParamSource.ValueFor(Sample)` 与每帧参数更新 | 原版提供同等独立调节可停用；SoundDef 改名/拆分或其他声音 mod 替换映射后须重核 |
| SunlightFuelGeneratorFacility | `FT_CitadelGenerator` 的 size/drawSize/贴图、`FTC_BasePermanentStructure` 基类、`Milira_SunLightFuel`/`Milira_SolarCrystal`、`CompProperties_Power.basePowerConsumption` 与 `UpdateDesiredPowerOutput()`、`CompProperties_Refuelable.canEjectFuel` 与 `GizmoGridDrawer` 的反向 gizmo 行为 | 上游提供可调数值的大型日光燃料发电机；城塞移除永久结构基类或自行补上排出燃料 gizmo；米莉拉移除日光燃料/日凌晶/发电机 |
| ReinforcedConcreteHeavyBridge | Odyssey 的 `HeavyBridge`、`TerrainGrid.foundationGrid` 分层/存档、`Frame.Destroy(Cancel)`、材料/研究；Harmony3与可选 Replace Stuff `GetNeededBridge(BuildableDef,IntVec3,Map,ThingDef)` 及其预览/补桥共用调用 | 上游提供等价桥梁时先转换存档再停用；桥梁分层、资源/研究或返料变化时重核；Replace Stuff 缺席不注册，接口漂移报未应用 |
| ReplaceStuffBridgeReplacement | 原版 `CanPlaceBlueprintAt_NewTemp` 两处 `FoundationAt`、`CanBuildOnTerrain` 的 `GetTerrain`/`GetAffordances`、`Frame.CompleteConstruction` 唯一 `SetFoundation`；Replace Stuff `CanBuildOnTerrain` transpiler 顺序 | 上游原生支持地基桥原位替换且保留持续校验/承重/返料后退役；调用数、Map local、地形分层或返料规则漂移时重核，应用失败日志代表未生效 |
| Fix_DMS_BookGrammarAndHeavyShield | DMS 研究语法与机体 body part group | 上游补齐规则或盾牌装备条件，避免重复追加 |
| Fix_WRMegaCorp_QuestExposeData | WRMegaCorp ChoiceLetter_CSC.ExposeData 的 IL | 上游删除重复 Scribe_Deep 调用，或 transpiler 锚点改变 |
| LocalFix_MiliraFlightNullCheck | Milira FlightTracker 前缀 | 上游自行检查缺失飞行组件，或改用不同飞行机制 |
| MilianWeaponInteractions | Milira 武器列 `DoCell`、原菜单、`CompTargetableWeapon.Available`、机体身份及 `Milira_EquipMilian` 两目标契约；原版表格窗口关闭、Targeter 与浮动菜单 provider | 上游原生提供两种交互后停用；签名/接缝漂移应拒绝应用而非放宽武器限制；未发布到创意工坊 |
| DefaultsWorkPrioritiesLifeStageFix | Defaults 两个 Postfix 的签名、各自唯一 `First<LifeStageAge>`／`LifeStageMinAge` 调用、`developmentalStage.Child()` 与 `Pawn_AgeTracker.pawn`；哨兵返回值的后续 IL 用途 | 正式版同时并入上游 `00e16b1` 两处修复后停用；任一调用、签名或返回值用途变化须整组拒用并复核，不放宽校验 |
| WeaponStationFireModes | 无人/有人 Def 的 comps 隔离、实际预热前唯一 `turretBurstWarmupTime` 字段地址读取、WS 预热/冷却入口、VFM FireModeDB/profile 与原版精度曲线、装备偏移及 burst 生命周期 | 上游提供等价无人基站适配后避免重复应用；任一接缝、曲线或模式语义漂移须复核；有人支架维持上游操作员逻辑，CE 不支持 |
| MiliraDailyExpendRecipeFix | 27 个 RecipeDef 的 defName 与 `ingredients/li` 顺序、20 个 getter 注入点、日光酱油/果酱 Nutrition、批量酱油 60→10+10、鸡翅/米跳墙的非营养原料边界 | 上游自行设置 `ingredientValueGetterClass` 后先删对应注入；配方 defName/li 顺序、酱油果酱营养或上游数量变化时重算 README 收支表；新增含非营养原料的烹饪配方不得套营养值 getter |
| DMSMechStorage | FFF 胶囊选择与 `ThingOwner` 释放契约、`CanActivate`/`ActivateMech`、原版完整占地/自身 edifice 豁免、Overseer 控制组与带宽、指挥/觉醒/无人机机制、恢复时 `PostSpawnSetup(true)`、`MinifiedThing.LabelNoCount`、`PawnUtility.GetPosture`、`Building_MechCapsule.DynamicDrawPhaseAt` 与古代军团 `weaponDraw` 偏移、`GenSpawn.Spawn` 恢复标志；Cinders `1.7/DMS` 兼容层与 `MechWeaponExtension`/`IWeaponUsable` 标记 | 上游提供等价 DMS 全系列机械师封存后退役；任一接口、来源 packageId 或 Cinders DMS 兼容层变化须重核并跑真实 DLL 驱动；停用前唤醒独立机兵并结束自定义作业，不改变非 DMS 胶囊 |

`upstream-patches/` 的直接工坊修改须结合记录的基准使用。标为历史缺失、已撤回或不建议安装的内容只留档，不因这次合仓自动重打。
