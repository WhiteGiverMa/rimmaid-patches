# 失能机关：复乐园 综合修复与优化 / DMSSR Unified Fix

**1.1.4 · RimWorld 1.7 / .NET 10 · WhiteGiverMa**
Package ID: `whitegiver.dmssr.unifiedfix` · Assembly: `DMSSRUnifiedFix.dll`

面向《The Dead Man's Switch - Synthriada》（`Cephalopod.DeadManSwitch.Synthriada`，工坊 `3835512735`）的 Harmony + XML 侧挂补丁。1.0.0 整合原有的 `DMSSRStatPathPerfFix` 1.1.0、`DMSSRLegionCustomizationCompat` 1.0.0、`DMSSRNeuroglueFix` 1.0.0、`DMSSRScourgeDeathFix` 1.0.0 与停滞仓溶解尸体搬运修复；1.1.0 追加 12 项审计修复；1.1.1 增加觉醒机兵零带宽控制组支持；1.1.2 修复实验性虹移装置的可用原料过滤器；1.1.3 修复该装置销毁使用者时的空引用；1.1.4 对齐新版 `DMSSR.dll` / `Fortified.dll`，退役已由上游实现的寻路代价子修复，并更新验证基线。

## 1.1.4 上游对齐与分项判定

- **退役寻路代价补丁（`PathCostPatches`）**：新版 `DMSSR.dll`（`983A2C8A...`）已移除 `DMSSR.Patches.Patch_Ability_PathCost_Instance`，仅保留 `Patch_Ability_PathCost_Static`（单一 `CostToMoveIntoCell(Pawn, IntVec3)` 后置，不再双重扣减地形代价），同时在 `AbilityPathCostUtility` 中新增 `TerrainPenaltyStates` 弱引用表缓存并由 `Patch_Ability_PathCost_HediffCache` 在 `HediffSet.DirtyCache` 前缀失效。`PathCostPatches.cs` 及 `HediffFlagCache` 中仅服务寻路的扩展/组件标记已删除。
- **受体凝胶基线更新（`UseNeurogluePatches`）**：新版 `DMSSR.Jobs.JobDriver_UseNeuroglue.MakeNewToils` 已开始调用 `NeuroglueHigh` 的 `IngestionOutcomeDoer_GiveHediff`，但仍跳过 `DMS.IngestionOutcomeDoer_ChangeHediffLevel`（`DMS_BionicShock` 降 1 级），且 `CanRefill` 仍拒绝满燃料或无储槽的患病机兵，右键仍全图 `AllThings` 扫描且无自动用药节点。因此保留 `UseNeurogluePatches` 与 `JobGiver_AutoUseNeuroglue`，并将 `Validation\Neuroglue\Tests.cs` RED 断言对齐为验证 `BionicShock` 等级卡滞与 `CanRefill` 拦截。

| 子系统 | 判定 | 上游依据（2026-11-20 反编译） |
|---|---|---|
| 属性门控与 `DMS_MechCustomization` 缓存（`MechStatPatch`、`MechLookupPatches`） | 仍需保留 | `Patch_MechCustomization_ApplyStatPreFinalize` 及 5 个 helper、`SkillBonus` 仍先做线性 hediff 扫描 |
| 寻路代价去重与地形豁免缓存（`PathCostPatches`） | **已退役** | `Patch_Ability_PathCost_Instance` 已删除；`AbilityPathCostUtility` 与 `Patch_Ability_PathCost_HediffCache` 已实现每 Pawn 缓存与 `DirtyCache` 失效 |
| 能力计算与精神分裂链接缓存（`CapacityPatches`） | 仍需保留 | `Patch_MechCustomization_ApplyCapacity`、`Patch_Schizophrenia_ConsciousnessFloor`、`DMSSR.Patch_PawnCapacity_Calc` 仍未门控或仍每调用加锁遍历 |
| 并行预绘制优化（`RenderPatches`） | 仍需保留 | `Patch_EmbryteWeaver_UnfinishedDraw/Print`、`HydraFlightRendering.TryGetTransform`、`Patch_SyntheticPresentationScale` 未改动 |
| 军团机兵参数调节兼容（`MechCustomizationPatches`） | 仍需保留 | `MechCustomizationUtility.IsSupportedPawn` 与 `Patch_MechCustomization_UnlockOnResearch` 仍只匹配 `DMS_Mech*` / `Cinder_Mech_` |
| 受体凝胶完整药效与自动用药（`UseNeurogluePatches`、`JobGiver_AutoUseNeuroglue`） | **已适配保留** | `JobDriver_UseNeuroglue.MakeNewToils` 已补 `NeuroglueHigh`，但仍跳过 `IngestionOutcomeDoer_ChangeHediffLevel`；`CanRefill`、`FloatMenuOptionProvider_UseNeuroglue` 及自动用药缺失未变 |
| 天灾与植入物死亡重入保护（`DeathHediffRemoval`） | 仍需保留 | `Patch_ScourgeNextDeath.Prefix` 与 `Fortified.HumanlikeMech.Kill` 仍未置位 `health.isBeingKilled` |
| 停滞仓溶解尸体搬运计数修正（`CorpseCarryPatches`） | 仍需保留 | `JobDriver_CarryCorpseToStasisPod` 仍未将 `job.count <= 0` 归一为 1 |
| A1 PR 协议（`DeathCheatProtocolFix` + XML） | 仍需保留 | `DMS_DeathCheat` 仍缺 `HediffCompProperties_Modification`、`HediffComp_DeathCheatCharges` 未实现合并接口、初始添加仍 `Part == null` |
| A2 缺失肢体合成仿生（`SyntheticBionicRestoreFix`） | 仍需保留 | `JobDriver_ApplySyntheticBionic` 仍未在 `AddHediff` 前调用 `RestorePart` |
| A3 远程控制植入体机械师链路（`RemoteControlFix`） | 仍需保留 | `JobDriver_OperateMechRemote` 与 `Fortified` 仍未为 `DMS_RemoteController` 创建或支持 `Pawn_MechanitorTracker` |
| B1 CascadeDock / Blackwater 安装部位（`ModificationPartDefaultsFix` + XML） | 仍需保留 | `Hediffs_Schizophrenia.xml` 与 `Hediffs_Blackwater.xml` 仍保留 `<defaultInstallPart>Reactor</defaultInstallPart>` |
| B2 连锁网络根节点过期（`CascadeControlFix`） | 仍需保留 | `HediffComp_SchizophreniaLink.IsLinkValid` 与 `HediffComp_SchizophreniaEmitter` 仍未校验或清理 `DMS_Schizophrenia_CasterMark` |
| B3 Skylark 电力冲锋（`PowerChargeFix`） | 仍需保留 | `JobDriver_PowerChargeDash.ShouldCollectPawn` 仍缺敌对判定，且无 `AddFinishAction` |
| B4 头足类投掷 AI 空引用（`CephalopodaThrowFix`） | 仍需保留 | `JobGiver_AICastCephalopodaGraspOnBestEnemyValue.TryGiveJob` 末段仍直接解引用 `val3.Position` |
| B5 Hirsch 应急战地包扎品质（`EmergencyTendFix`） | 仍需保留 | `CompAbilityEffect_EmergencyTend.Apply` 仍传 `item.Tended(0f, Mathf.Clamp01(Props.maxTendQuality), 0)` |
| B6 Scourge 不朽护甲（`UndyingArmorFix`） | 仍需保留 | `HediffComp_ArmorBoost.GetTargetValue` 仍无任何 `StatPart` 读取，`Hediffs_Undying.xml` 仍为 `10000` ticks |
| B7 机械纳米感染识别（`MechaniteRecognitionFix`） | 仍需保留 | `GeneSetUtility.IsNanomechInfection` 仍未匹配 `mechanite` 子串 |
| B8 基因装配舱右键入舱（`GeneFittingPodFix`） | 仍需保留 | `FloatMenuOptionProvider_GeneFittingPodEntry` 仍未在派发任务前调用 `TryQueueSubject` |
| B9 Hydra 渗透者伪装控制门控（`FacedancerControlFix`） | 仍需保留 | 四个 `Patch_Facechange_*` 与 `EnsureControllable` 仍未校验 `PlayerCanRevert` |
| 1.1.1 觉醒控制组（`AwakenedControlGroupFix`） | 仍需保留 | `Fortified.CompDeadManSwitch.Wake`/`TryTriggerDMS`（IL 不变）、`ThinkNode_Condition_Wake`、`Patch_JobGiver_GetEnergy_Min`、`OutgoingMech.Trigger` 与 `CompAbilityEffect_LocalMesh` 均未改变 |
| 1.1.2 实验性虹移装置原料过滤器（XML） | 仍需保留 | `TableMachinePrinter.xml` 的 `DMSSR_Make_ExperimentalWarp` 仍未配置 `fixedIngredientFilter` |
| 1.1.3 实验性虹移装置使用者移除（`ExperimentalWarpFix`） | 仍需保留 | `CompUseEffect_ExperimentalWarp.DoEffect` 仍保留两处未保护的 `Thing.Destroy(DestroyMode)` |

## 1.1.3 实验性虹移装置使用者移除

- 成功分支会销毁使用者以表现「消失」。`Verse.Pawn.Destroy` 被多个模组挂补丁（Milira 装了两处 Prefix 与 Finalizer），整条链在正常殖民者身上会抛 `NullReferenceException`，结果是物品已消耗、`CompleteExperiment()` 已推进、却没有成功信，且使用者留在原地。
- 用 Transpiler 只把该分支的第二个 `Thing.Destroy` 换成签名兼容的包裹方法：正常时行为不变，异常时回退 `Pawn.Kill` 并清除尸体，同时记一条带异常类型的警告。上游改用别的写法导致 IL 不匹配时，本子系统拒绝安装，其余功能不受影响。

## 1.1.2 实验性虹移装置原料过滤器

- `DMSSR_Make_ExperimentalWarp` 的第一格原料提供纯通流水晶与含杂通流水晶两种选择，但配方没有声明 `fixedIngredientFilter`。原版 `IngredientCount.IsFixedIngredient` 只在候选唯一时成立，其余情况要求 `recipe.fixedIngredientFilter.Allows`，空过滤器使两种水晶都被拒绝，订单永远停在「需要 1x 可用原料」。
- 以条件补丁补齐仅含这两种水晶的 `fixedIngredientFilter`；已有订单在重新勾选或重建后即可正常投料，其余三个固定原料格子走 `IsFixedIngredient` 分支不受影响。启用本包前请停用上述四个独立旧包。

## 1.1.1 觉醒控制组

- `CompDeadManSwitch.Wake` 保留监督关系、同一控制组、工作模式及充电上下限；立即清除带宽/废料包缓存并刷新机械师追踪器。命名、觉醒信件与互动组件保持上游行为。死亡时恢复常规带宽并再次刷新缓存。
- `TryTriggerDMS` 的精确 IL 匹配仅移除 `woken = false`，保留潜伏标志清理、觉醒倒计时与出走逻辑；IL 漂移时拒绝安装本子系统。
- `BandwidthCost` 使用可查看说明的 `StatPart` 归零。废料包的 `FinalizeValue` 和 DMSSR `LocalMesh.GetBandwidthMultiplier` 使用线程局部、异常安全的物理带宽作用域，保留原有带宽权重及参数调节倍率；作用域内绕过缓存，避免污染控制成本。
- 受监督机兵跳过 FFF 独立觉醒行为分支，使用原版控制组的 Work、Recharge、SelfShutdown、Escort 及扩展模式。无组觉醒机兵的最低自动充电阈值为默认 5%，仍受 DMSSR `supply` 调整。
- 可选检测 `DMSMechStorage.RequiresEnrollment`，使收纳释放重新征募觉醒机兵；使用类型名查找，不增加程序集依赖。
- `OutgoingMech.Trigger` 保留 50% 攻击／25% 战利品／25% 无事件结构；规则、战利品生成器、敌对据点或基地地图缺失时跳过对应事件。结算通过 `removedCache` 在下一 tick 枚举前移除。

已脱组的觉醒机兵可由任意机械师使用原生“控制机兵”重新接管，即使空余带宽为 0。接管后进入该机械师的默认控制组，需要时手动改为 Recharge。FFF 的无限命令范围及机械师倒地／离图时的觉醒征召能力继续生效。

## 1.0.0 五大子系统

| 子系统 | 修复与优化行为 |
|---|---|
| **属性／寻路／渲染性能** | 在 `[StaticConstructorOnStartup]` 中先确保上游 `DMSSR.Misc.HarmonyBootstrap.EnsurePatched()` 执行完毕，再摘除 `Pawn_PathFollower.CostToMoveIntoCell(IntVec3)` 上重复挂载的实例 Postfix（地形豁免从重复扣减两次修正为扣减一次）；共享按 `HediffSet.DirtyCache` 失效的无锁健康快照覆盖参数调节、地形豁免、精神分裂意识链接与动态能力 provider；无关能力 O(1) 提前返回；收窄普通物品绘制前缀的未完成物品查询；按 `ThingDef` 并发缓存飞行与缩放扩展。 |
| **军团机兵参数调节兼容（可选）** | 在 `MechCustomizationUtility.IsSupportedPawn` 与 `Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs` 放行 `DMSL_Mech_*`（排除 `DMSL_Drone_*` 无人机，保持 `DMSL_Body_Terra` 人形身体不入湿件白名单）。本程序集不引用 `DMS_Legion.dll`，军团仅列于 `loadAfter` 作为可选兼容。 |
| **受体凝胶完整药效与自动用药** | 接管 `JobDriver_UseNeuroglue` 末段结算，执行全部非人类专属 `IngestionOutcomeDoer`：授予 `NeuroglueHigh`（触发清除退行性病变、仿生排异反应与幻肢症候群）、降 1 级异肢症候群并恢复 25 点神经信号效率；放行满效率或无储槽患病机兵的 `CanRefill`；通过条件 XML 将零分配 `JobGiver_AutoUseNeuroglue` 注入受控机兵行为树（效率 `<= 75` 或存在待清病症时在非征召／非休眠／非交战下自动取用 1 支，无药时 600 ticks 退避）；将右键菜单全图 `AllThings` + LINQ 扫描替换为 `ThingsOfDef` 距离预筛。 |
| **天灾与植入物死亡重入保护** | 以两处 Transpiler 包裹 `DMSSR.Content.Patch_ScourgeNextDeath.Prefix` 与 `Fortified.HumanlikeMech.Kill` 在 `base.Kill` 置位 `health.isBeingKilled` 之前调用的 `RemoveHediff`：临时置位 `isBeingKilled = true` 并在 `finally` 中恢复原值，保留移除回调、复活次数消耗与植入物掉落，消除递归二次死亡与空尸体报错。 |
| **停滞仓溶解尸体搬运修复** | 为 `DMSSR.Content.JobDriver_CarryCorpseToStasisPod` 的 `TryMakePreToilReservations` 与 `MakeNewToils` 挂载前置修正，将 `job.count <= 0` 归一为 `1`，消除 `Toils_Haul.ErrorCheckForCarry` 的 `Invalid count: -1, setting to 1.` 红字，覆盖新派发、强制执行、排队任务与旧档重读重建 Toil。 |

## 1.1.0 十二项审计修复

| # | 机制 | 根因与修复 |
|---|---|---|
| A1 | **PR 协议叠层、槽位、卸装与压迫性重构** | `DMS_DeathCheat` 缺少 `Fortified.HediffCompProperties_Modification`，改装窗口看不到、`JobDriver_RemoveModification` 找不到，第二次安装因 `HediffComp_DeathCheatCharges` 不是合并参与者而失败。XML 补齐改装 comp，并由启动期兜底；自定义安装路径统一处理首次/后续安装，按 `charges + queued < 20` 校验，压迫性重构期间在消耗物品前拒绝；初始 `Part == null` 整身充能在 `HediffSet.AddDirect` 与读档时归一到核心部位。`spawnThingOnRemoved` 保持为空，避免花费充能后重复刷取物品。 |
| A2 | **缺失肢体上的合成仿生** | `JobDriver_ApplySyntheticBionic` 直接 `AddHediff(def, part)`，原版 `HediffSet.AddDirect` 以 “Tried to add health diff to missing part” 丢弃并仍然销毁物品。安装前按原版 `Recipe_InstallArtificialBodyPart` 的语义对缺失的 `Hediff_AddedPart` 目标调用 `RestorePart`。 |
| A3 | **远程控制植入体的机械师链路** | `ShouldBeMechanitor` 只认 `MechlinkImplant`，非机械师殖民者取得监督关系后 `overseer.mechanitor == null`，`InMechanitorCommandRange` / `CanDraftMech` NRE。为带 `DMS_RemoteController` 的玩家殖民者放行机械师判定，在植入时建立追踪器；仅为遥控者派生其实际监督机兵的带宽与 1 个控制组；监督关系建立后补发带宽通知；移除植入时清理监督关系。 |
| B1 | **CascadeDock / Blackwater 错误安装部位** | 两个部位目标改装被写成 `<defaultInstallPart>Reactor</defaultInstallPart>`，`AddHediff(hediff, null)` 会把 Brain/Spine 覆盖为 Reactor 并允许重复叠加。XML 删除错误默认值，启动期再做一次兜底。 |
| B2 | **连锁网络根节点过期** | 根节点 `DMS_Schizophrenia_CasterMark` 1800 ticks 后消失，但发射器没有移除回调、叶节点也不检查根节点。链接校验要求来源仍持有 CasterMark；根节点移除/过期时按 `sourceThingID` 立即删除对应叶节点。 |
| B3 | **Skylark 电力冲锋** | 冲锋路径抓取缺少敌对判定，且 `Burst`（以及未成功完成时的 `Buff`）只在最终到达 Toil 清理。抓取仅接受 `GenHostility.HostileTo` 目标；注册 `JobDriver.AddFinishAction`，任何 `JobCondition` 结束都清除 `Burst`，非 `Succeeded` 时同时清除 `Buff`，成功完成保留作者设计的 600 tick 增益。 |
| B4 | **头足类投掷 AI 空引用** | 附近有敌人但没有合法“盟友-敌人”组合时，两个候选局部变量保持 null，末段解引用 `enemy.Position`。Transpiler 将结果对替换为一个空值安全的 `GetJobOrNull(ability, ally, enemy)`。 |
| B5 | **Hirsch 应急战地包扎品质** | `item.Tended(0f, Mathf.Clamp01(1.3f), 0)` 把基础品质传 0，均值仅 5.7%、上限 25%。Transpiler 以 `Props.maxTendQuality` 同时作为基础品质与上限，保留 1.3 上限与原有化学燃料消耗。 |
| B6 | **Scourge 不朽护甲** | `HediffComp_ArmorBoost` 计算了目标护甲但没有任何属性路径读取；XML `HediffCompProperties_Disappears` 又是 10,000 ticks，截断 20,000 ticks 设计。为 Sharp/Blunt 护甲注册 `StatPart`（读取共享健康快照中的 comp），`Setup` 与读档同步 `ticksToDisappear`/`disappearsAfterTicks` 到 20,000。 |
| B7 | **免疫异质化 / 免疫突变识别机械纳米感染** | `GeneSetUtility.IsNanomechInfection` 只匹配 nano/nanite/embryt 等子串，漏掉原版 `FibrousMechanites` 与 `SensoryMechanites`。后置识别所有 defName 或 tag 含 `mechanite`（不区分大小写）的感染/可自然免疫 hediff。 |
| B8 | **基因装配舱右键入舱** | 浮动菜单只检查 `CanStartEntryFor` 就派发 `DMS_EnterGeneFittingPod`，没有调用 `TryQueueSubject`，驱动器的 `IsQueuedSubject` 失败条件立即以 `Incompletable` 结束。`TryMakePreToilReservations` 前置入队，保留所有原有占用/可达检查。 |
| B9 | **Hydra 渗透者伪装控制标志** | DMSSR 的 4 个控制/征召后置只判断 `HasFacechange`，Hydra 遭遇与非玩家伪装也会获得 `IsColonistPlayerControlled`、`IsColonyMechPlayerControlled`、`ShowDraftGizmo` 与 `CanDraftMech`。以 `Priority.Last` 后置复用 comp 自身的 `PlayerCanRevert` 归属判定，仅对真正玩家 Facedancer 保留控制。 |

### 共享缓存与隔离安装

- 全模组仅注册 **1 个** `HediffSet.DirtyCache` Postfix，同步失效 `HediffFlagCache` 与 `MechBiogelCache` 并唤醒找药退避；1.1.0 的 Undying 护甲也复用同一快照，普通兵种查询护甲时不增加额外 hediff 扫描。
- 每个子系统独立 `try/catch` 安装；单一子系统签名或 Def 缺失仅降级跳过该子系统并打印命名警告，不影响其余功能。远程控制、电力冲锋、Undying 护甲在没有对应 Def/属性时静默跳过，便于最小化测试夹具和其他内容组合。
- Harmony 与 XML Def 补丁复用原有 Def：补齐 comp、移除错误默认值并接通原生机制。

## 构建与离线真实 DLL 验证

```powershell
dotnet build .\Source\DMSSRUnifiedFix.csproj -c Release
pwsh -NoProfile -File .\Validation\validate.ps1
```

`Source` 与 `Validation` 均面向 `net10.0` 并开启 `TreatWarningsAsErrors=true`（0 警告 0 错误）。离线验证套件直接加载安装的 `Assembly-CSharp.dll`、`0Harmony.dll`、`DMS.dll`、`Fortified.dll`、`DMS_Legion.dll`、`DMSSR.dll` 与 `DMSMechStorage.dll`，分 **21** 个隔离进程阶段执行：

| 验证子集 | RED 断言 | GREEN 断言 |
|---|---:|---:|
| 属性／能力／健康／渲染（含等价性与 16 线程并发读压测） | 637 | 764 |
| 军团机兵窗口、全部 20 种定义、上限与 26 字段 Scribe 往返 | 107 | 179 |
| 受体凝胶治愈、自动用药 AI、共享失效、真实 XML XPath 与零 GC 热路径 | 9 | 102 |
| 天灾与植入物死亡生命周期 | 54 | 88 |
| 停滞仓尸体搬运、真实搬运与入仓溶解、排队与 `Job` Scribe 重读恢复 | 13 | 50 |
| 1.1.0 十二项审计修复（真实 Def/Pawn/Map/JobDriver/StatWorker/Ability） | 69 | 80 |
| 1.1.1 觉醒控制组、物理带宽隔离、充电、收纳及出走结算 | 52 | 104 |
| **合计** | **941** | **1,367** |

另含早启动换序（3 项）、死亡补丁先注册（88 项）、未安装军团环境（15 项）、核心子系统联合运行（20 项，最小夹具下共 52 个受控方法）、觉醒与远程控制组合（108 项）及单子系统签名缺失隔离降级（16 项）专项检查。觉醒测试覆盖原控制组实例、零空余／零总带宽重新接管、死亡缓存恢复、DMSSR 参数倍率、异常与嵌套／跨线程作用域、真实收纳释放和 `GameComponentTick` 延迟移除。正常 GREEN 阶段全程 `0 Error / 0 Warning`。

### 依赖基线（2026-11-20）

| 文件 | SHA-256 |
|---|---|
| `Assembly-CSharp.dll` | `11d7c9faf52be22933da840d906b1af7b48f7ea574a886c045b1fb12bd366124` |
| `UnityStubs.dll` | `118c6fbe3a2d2ccd3a93331e30f4a5d54af73e0bc2ad7fccdcdeb86bfc7ef6cd` |
| `0Harmony.dll` | `7252fbe77bc96fcfb5e6aa446a4957f4547de312d889046fe3ec7d15f7f0fcf5` |
| `DMS.dll` | `b94cd0359f9e276240cbb35659b057cac568d6f1a83de3f743c0503116b3234a` |
| `Fortified.dll` | `d8ac6883d1b2f497b9254ee18fb04383b90175abf3f4cba56fafdee24f7aeba4` |
| `DMS_Legion.dll`（仅验证加载） | `00afec1d5f2b22210e7ccd2fc502d88bc97711155ac160d39fbc1034c85d51ea` |
| `DMSSR.dll` | `983a2c8a1649f1bae5e113924069e9846a77dffecdcd39d1ea5ab4f3d8e8cb2b` |
| `DMSMechStorage.dll`（可选兼容验证） | `797fff30b8752cb52d6fe4849de71e64b8a588cba6ea96efd7b4355cb5c215d2` |

## 维护与退役条件

上游或游戏本体更新后请重跑 `Validation\validate.ps1`：寻路代价补丁已于 1.1.4 退役；当其余某一子系统的 RED 前提不再复现（原生放行 `DMSL_Mech_*`、补齐机兵受体凝胶 `ChangeHediffLevel`/自动用药、`RemoveHediff` 前置位 `isBeingKilled`、在 `CarryCorpseToStasisPod` 补齐 `job.count`，或审计问题被上游修复）时，单独移除对应子系统。觉醒子系统需核对 `Wake`、`TryTriggerDMS` 的唯一复位块、`Notify_Killed`、属性缓存与后处理、LocalMesh、充电阈值、收纳征募和出走列表移除契约；FFF 原生保留觉醒控制组并覆盖这些边界后退役本子系统。全部由上游实装后退役整个补丁包。四个历史独立补丁保留于 `archive/`。
