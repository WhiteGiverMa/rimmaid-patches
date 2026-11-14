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
| MilianWeaponInteractions | Milira 武器列 `DoCell`、原菜单、`CompTargetableWeapon.Available`、身份／双目标装备契约；衣物 `CompDressMilian`／`CompTargetable_Milian` 资格与 `Milira_DressMilian` 双预约及 `Wear(true,true)`；原版 Targeter／菜单 provider | 1.1.0；更新 DLL 后比较时间/hash 并跑真实驱动；上游原生提供三种交互后停用。签名/接缝漂移拒用，不放宽型号/配件限制、不阻止原生锁定衣物替换；未发布到创意工坊 |
| DefaultsWorkPrioritiesLifeStageFix | Defaults 两个 Postfix 的签名、各自唯一 `First<LifeStageAge>`／`LifeStageMinAge` 调用、`developmentalStage.Child()` 与 `Pawn_AgeTracker.pawn`；哨兵返回值的后续 IL 用途 | 正式版同时并入上游 `00e16b1` 两处修复后停用；任一调用、签名或返回值用途变化须整组拒用并复核，不放宽校验 |
| WeaponStationFireModes | 无人/有人 Def 的 comps 隔离、实际预热前唯一 `turretBurstWarmupTime` 字段地址读取、WS 预热/冷却入口、VFM FireModeDB/profile 与原版精度曲线、装备偏移及 burst 生命周期；1.1.0 另依赖 `Verb.caster` 类型判断、`VerbState` 迁移点（`Reset`/`TryCastNextBurstShot`/`VerbTick`）与 `activeBurstLocks` 计数 | 1.0.0 经用户确认游戏内验证通过；1.1.0 于 2026-11-14 离线 RED/GREEN 与 IL 路径检查通过，游戏内 DPA 复测待做。上游提供等价无人基站适配后避免重复应用；任一接缝、曲线或模式语义漂移须复核；有人支架维持上游操作员逻辑，CE 不支持 |
| MiliraDailyExpendRecipeFix | 27 个 RecipeDef 的 defName 与 `ingredients/li` 顺序、20 个 getter 注入点、日光酱油/果酱 Nutrition、批量酱油 60→10+10、鸡翅/米跳墙的非营养原料边界 | 上游自行设置 `ingredientValueGetterClass` 后先删对应注入；配方 defName/li 顺序、酱油果酱营养或上游数量变化时重算 README 收支表；新增含非营养原料的烹饪配方不得套营养值 getter |
| DMSMechStorage | FFF 胶囊选择与 `ThingOwner` 释放契约、`CanActivate`/`ActivateMech`、原版完整占地/自身 edifice 豁免、Overseer 控制组与带宽、指挥/觉醒/无人机机制、恢复时 `PostSpawnSetup(true)`、`MinifiedThing.LabelNoCount`、`PawnUtility.GetPosture`、`Building_MechCapsule.DynamicDrawPhaseAt` 与古代军团 `weaponDraw` 偏移、`GenSpawn.Spawn` 恢复标志；Cinders `1.7/DMS` 兼容层与 `MechWeaponExtension`/`IWeaponUsable` 标记 | 上游提供等价 DMS 全系列机械师封存后退役；任一接口、来源 packageId 或 Cinders DMS 兼容层变化须重核并跑真实 DLL 驱动；停用前唤醒独立机兵并结束自定义作业，不改变非 DMS 胶囊 |
| StartCarryReentrancyFix | 唯一 `StartCarryThing` 编译 initAction、actor/Job locals、唯一 `TryStartCarry(Thing,int,bool)` 和无 return 的零拿取失败分支；Job loadID/Driver 所有权与正常部分拿取队列/数量契约 | 原版加入失败结束后立即退出且防止换任务重入后退役；IL 形状变化拒绝应用，不以寻路 null guard 替代；不依赖或修改 RimIOT |
| DMSFloggerWeaponTags | AncientCorps `DMS_Gsh30Gatlinggun/weaponTags` 与 FFF 标签筛选；保留原标签、HeavyAutomatroid 2.5，仅补 Heavy/V30 且不重复 | 上游原生补齐两项标签可停用；defName/列表结构变化导致 XPath 失配时先停用核对，不覆盖新定义或改全局体型限制 |
| DMSArcSprayFireMode | FFF `Verb_ArcSprayProjectile -> Verb_ArcSpray -> Verb_Spray -> Verb` 继承、`PreparePath/HitCell`、原版 launcher `TryCastShot` IL、末发弹药、连发/燃料、共同 Gizmo/Scribe 与玩家/操作员门控；可选 More weapon sounds `TargetMethods()` 的继承 MethodInfo 归一 | 1.0.1；游戏/FFF/GAUSOUND 更新后比 hash/时间并重跑无音效/组合红绿。签名/IL 漂移整组拒用，敌人永远扫射、CE 零 hooks；音效上游按声明方法去重后可移除兼容 hook，原生提供等价模式后整体退役 |
| PUAHStaleEntryFix | Bulk Load `SoftlockCleaner.TryCleanPawn` 前的 PUAH 记录过滤；`pawn.inventory.innerContainer` 与 `pawn.carryTracker.CarriedThing` 双向判定，真实掉落路径不动 | 上游自行清掉残留引用或方法改名后复核；改名时警告跳过而非抛错，不让补丁成为新的报错源 |
| Fix_WRMegaCorp_QuestRewardMultiplier | `WRMC_MeatOrder`/`WRMC_DepositExploitation`/`WRMC_ProjectLiquidation` 三处脚本插入点、`questRewardMultiplier` 变量名与各 1 处使用；`PatchOperationConditional` 包裹，`Set` 值为 1 | 上游补回 `QuestNode_Set` 后停用；Def 改名或结构变化时先核对新脚本再更新，不直接改成无条件 Patch |
| WhileYouAreNearbyMaintained | 原版 TryIssueJobPackage/PawnCanUseWorkGiver、ThinkResult/JobMaker/JobQueue 所有权，原作 DLL hash 与设置 Scribe 键；保留单遍 grower 状态和唯一 Postfix | 上游原生合入相同修复并通过验证后回退上游；工坊更新不覆盖本地版，也不自动同步。forked 本地维护分支，许可未定，作者要求即退役 |
| PocketSandEquipReservationFix | PocketSand 自己声明的 `JobDriver_Equip.TryMakePreToilReservations(bool)`、原版 `pawn.Reserve` 的 job/整堆预约与强制抢占；背包不预约、衣架预约源建筑，原有装备 toils 不动 | 上游补地面装备预约后可退役；DLL修改时间/hash改变先复核。目标类型/签名漂移警告跳过，不扩展成全局Reserve补丁，不因1.7标签更新假定已修 |
| EquippableAbilityPsycastLoadFix | `CompEquippableAbility.PostExposeData`、Ability构造及holder回绑；`CompEquippable.PostExposeData`当前仅深序列化verbTracker，ThingComp基类当前为空 | 官方补ctorArgs后退役；每次游戏更新对照两层基类和派生方法。绝不能用MethodInfo.Invoke模拟virtual base调用；1.0.0已知读档递归栈溢出，使用1.0.1并跑真实Scribe往返 |
| UFLIIwbClipboardFix | UFLI `ImprovedWorkbenchesCompat.GetCopiedBills(): List<Bill>`；BWM `Main.Instance` 属性、`BillCopyPasteHandler` 字段及 `_copiedBills` 可协变枚举；原生来源优先级/混合订单过滤 | 比较两个上游 DLL 的 hash/时间，变化先停用复核；接口漂移不安装或交回原方法，不屏蔽警告。UFLI 同时修正字段读取和 List 转换且转贴验证通过后退役；不泛化支持不同包名的非官方移植 |
| FFFMechModificationQueueFix | `Job.JobIsSameAs`、`Fortified.JobDriver_ApplyModification.TryResolveAndReserveItem`、`Window_MechModification.TryIssueEntry` 及可选 `DMSSR.Jobs.JobDriver_ApplySyntheticBionic.TryMakePreToilReservations` | 上游 FFF/DMSSR 区分排队改装 Job 实例且改为按 `stackCount=1` 扣除同 Pawn 异 Job 预留后退役；更新后核对 `Fortified.dll`/`DMSSR.dll` hash 并复跑 33 项驱动 |
| WolfeinRestoreWorkPrioritiesFix | `Wolfein.HediffComp_RestoreWorkPriorities.CompPostPostAdd`/`CompPostPostRemoved`、`Pawn_WorkSettings.Initialized` 与原版 `PawnGenerator` 非玩家派系未初始化 `workSettings` 时序 | 上游在保存/恢复优先级前判断 `workSettings.Initialized`（或改到 `PostAdd` 之外）后退役；更新后核对 `Wolfein.dll` hash 与方法名，签名变化时日志报跳过而非抛错 |
| DMSLDrillingBargePerfFix | `WorkGiver_DrillingBargeDeepDrill.PotentialWorkCellsGlobal`、`CellHasValuableDeepResource`、`CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource`、`DrillingBargeValidDrillCellsCacheComponent.MapComponentTick`，以及 `DeepResourceGrid.SetAt`/`ExposeData` 与 `DeepResourceGrid.map` 私有字段 | 2026-11-13 经用户确认游戏内验证通过，已验证 1.0.0 DLL 保持原样。上游改 `TryGetDeepResource` 半径或判定使候选集变成真子集时立即停用并重跑 211,250 组对比；`map` 字段漂移时只保留排除名单缓存；绕过 `SetAt` 直接写内部数组的模组无法被观察 |
| CeleTechTurretUpgradePerfFix | `JobGiver_Work.PawnCanUseWorkGiver`、`WorkGiver_Scanner.PotentialWorkThingsGlobal`、`WorkGiver_TurretUpgrade.HasJobOnThing`、`Comp_TurretUpgrade.PostSpawnSetup` 与 `queued` 列表字段 | 2026-11-13 经用户确认游戏内验证通过，已验证 1.0.0 DLL 保持原样。上游覆盖 `ShouldSkip`/`PotentialWorkThingsGlobal` 或收窄 `PotentialWorkThingRequest` 后退役；`queued` 改名/改类型或 `PostSpawnSetup` 缺失时启动日志报错并拒绝安装 |
| HARAnimatePerfFix | `HARAnimate.Animation.AnimatorCache.Get/Cleanup/CleanAll/CacheCount`、`AnimatorCacheCleaner.CacheCleaner`、`AnimatorSystem.Register/Evaluate` 与 `stacks` 私有字典 | 2026-11-13 经用户确认游戏内验证通过，已验证 1.0.0 DLL 保持原样。上游将 `CacheKey` 改为 `IEquatable`、移除 `"race:label"` 拼接并加节点同步锁后退役；签名变化时打警告并跳过 |
| DMSEWorldPerfFix | `WorldComponent_FactionAirDefenseAlert.StageOf/RadarWarm/WorldComponentTick/Prune`、`siteList`/`sites`/`neutralWarned`、`MissileDoctrine.For`、`WorldComponent_BVRNetwork.BuildNodes` | 2026-11-13 经用户确认游戏内验证通过，已验证 1.0.0 DLL 保持原样。上游让只读查询传 `create:false` 且休眠和平基线据点后退役；基线超过 4 或成员改名时对应补丁组跳过 |
| MTOEWYStatPerfFix | `StatExtension.GetStatValue` 上 `MiliraTheOneEntwinedWithYou.Patches.IdealExclusiveStatPatch.Postfix`、14 个受影响 `StatDef`、`GameComponent_IdealTracker` 与 `IdealWorldEffectService.MoveFactor` | 2026-11-13 经用户确认游戏内验证通过，已验证 1.0.0 DLL 保持原样。上游改为 `StatDef` 分派并避免非艾蒂尔单位调用 `GetUniqueLoadID()` 后退役；上游版本变更（非 `1.4.0.0`）时重核 14 属性与倍率常量 |
| DMSSRStatPathPerfFix | `Pawn_PathFollower.CostToMoveIntoCell` 两重载、`AbilityPathCostUtility.ClampPathCostForAbility/HasIgnoreTerrainMovePenalty`、`Patch_MechCustomization_ApplyStatPreFinalize.Postfix/TryGetCompFast`、`HediffSet.DirtyCache` | 2026-11-13 经用户确认游戏内验证通过，已验证 1.0.0 DLL 保持原样。上游去重 `CostToMoveIntoCell` Postfix、提前属性闸门并缓存 Hediff 查询后退役 |
| DMSSRLegionCustomizationCompat | `DMSSR.UI.MechCustomizationUtility.IsSupportedPawn(Pawn)` 与 `DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs()` | 2026-11-13 经用户确认游戏内验证通过，已验证 1.0.0 DLL 保持原样。上游原生放行 `DMSL_Mech_` 或提供 `DefModExtension` 后退役 |
| DMSEMissileBaseFix | `DMSE_MissileBase`/`DMSE_FieldLaunchSite`/`DMSE_EarlyLaunchSite` 的 `validLaunchTarget`、`CompLaunchable.ChoseWorldTarget`、`TransportersArrivalAction_VisitSite.CanVisit`、`FormCaravanComp.CanFormOrReformCaravanNow`→`GenHostility.AnyHostileActiveThreatToPlayer`、炮塔有无弹药与 `CompMissileRackStorage` 的 `ThingOwner.TryDrop` | 2026-11-14 新增，真实 DLL RED/GREEN 通过，游戏内 QA 待做。上游自行放行三类阵地或修好清场后的远行队重组后退役；阵地 Def 改名、威胁判定或容器掉落契约变化时先停用核对，不把仍通电武装的炮塔当作安全目标 |
| DMSSRNeuroglueFix | `DMSSR.Jobs.JobDriver_UseNeuroglue` 末段 `IngestionOutcomeDoer` 结算与 `CanRefill` 门槛、`Neuroglue` 上的 `IngestionOutcomeDoer_GiveHediff`/`DMS.IngestionOutcomeDoer_ChangeHediffLevel`/`DMSSR.Hediffs.IngestionOutcomeDoer_RefillBiogel`、`HediffComp_RemoveIfOtherHediff` 监听的 `NeuroglueHigh`、四棵机械体行为树插入点与 `HediffSet.DirtyCache` | 2026-11-14 新增，真实 DLL 7 项 RED / 96 项 GREEN 通过，游戏内 QA 待做。上游补齐机兵全部用药效果并允许满效率/无储槽用药后退役；`JobDriver_UseNeuroglue` 或行为树改名时跳过并记录，不改人类过量与饮食记忆 |
| VanillaFireModesPerfFix | VFM 1.8.0 三个补丁方法（`VerbTick` 后置、`BurstShotCount` 后置、`WarmupComplete` 前置）与 `Harmony.GetPatchInfo` 归属、`Verb.WarmupComplete`/`VerbTick`/`get_BurstShotCount` 与 `VerbState` 语义 | 2026-11-14 新增，48 项离线 RED/GREEN 通过，游戏内 QA 待做。VFM 上游删除 `VerbTick` 清扫或补丁改名后停用（安装器记录警告并不做改动）；签名漂移时回滚到 VFM 原补丁，不直接改 VFM DLL |
| DMSLCommanderPerfFix | `CommanderAutonomy_SubjectOverseer/State` 前缀、`AXF12ObservedMap_CheckRemoveMapNow_Patch` 前缀、`CompOverseerSubject`/Overseer relation 查找、control group 扫描的闭包分配与侦察管理器缓存，以及 Waylith 并行预绘制读取 | 2026-11-14 新增，13 项 RED / 41 项 GREEN 与 16 线程两阶段压力测试通过，游戏内 QA 待做。上游去掉 comps 线性扫描或闭包分配后退役；命名或关系契约变化时跳过对应补丁组，不写存档字段 |

`upstream-patches/` 的直接工坊修改须结合记录的基准使用。标为历史缺失、已撤回或不建议安装的内容只留档，不因这次合仓自动重打。
