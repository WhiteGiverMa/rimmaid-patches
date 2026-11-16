# 失能机关：复乐园 综合修复与优化 / DMSSR Unified Fix

**1.1.0 · RimWorld 1.7 / .NET 10 · WhiteGiverMa**
Package ID: `whitegiver.dmssr.unifiedfix` · Assembly: `DMSSRUnifiedFix.dll`

面向《The Dead Man's Switch - Synthriada》（`Cephalopod.DeadManSwitch.Synthriada`，工坊 `3835512735`）的 Harmony + XML 侧挂补丁。1.0.0 整合原有的 `DMSSRStatPathPerfFix` 1.1.0、`DMSSRLegionCustomizationCompat` 1.0.0、`DMSSRNeuroglueFix` 1.0.0、`DMSSRScourgeDeathFix` 1.0.0 与停滞仓溶解尸体搬运修复；1.1.0 追加 2026-11-16 全模组审计确认的 12 项失效/半接线机制修复。启用本包前请停用上述四个独立旧包。

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
- 纯运行时 Harmony + XML Def 补丁，无自定义存档字段、无新增 Def（仅给原 Def 补 comp/移除错误默认值），可随时安全启用或移除。

## 构建与离线真实 DLL 验证

```powershell
dotnet build .\Source\DMSSRUnifiedFix.csproj -c Release
pwsh -NoProfile -File .\Validation\validate.ps1
```

`Source` 与 `Validation` 均面向 `net10.0` 并开启 `TreatWarningsAsErrors=true`（0 警告 0 错误）。离线验证套件直接加载本机安装的 `Assembly-CSharp.dll`、`0Harmony.dll`、`DMS.dll`、`Fortified.dll`、`DMS_Legion.dll` 与 `DMSSR.dll`，分 **18** 个隔离进程阶段执行：

| 验证子集 | RED 断言 | GREEN 断言 |
|---|---:|---:|
| 属性／寻路／健康／渲染（含等价性与 16 线程并发读压测） | 648 | 784 |
| 军团机兵窗口、全部 20 种定义、上限与 26 字段 Scribe 往返 | 107 | 179 |
| 受体凝胶治愈、自动用药 AI、共享失效、真实 XML XPath 与零 GC 热路径 | 7 | 102 |
| 天灾与植入物死亡生命周期 | 54 | 88 |
| 停滞仓尸体搬运、真实搬运与入仓溶解、排队与 `Job` Scribe 重读恢复 | 13 | 50 |
| 1.1.0 十二项审计修复（真实 Def/Pawn/Map/JobDriver/StatWorker/Ability） | 69 | 80 |
| **合计** | **898** | **1,283** |

另含早启动换序（3 项）、死亡补丁先注册（88 项）、未安装军团环境（15 项）、五子系统联合运行（20 项，最小夹具下共 43 个受控方法）与单子系统签名缺失隔离降级（16 项）专项检查，正常 GREEN 阶段全程 `0 Error / 0 Warning`。

### 依赖基线（2026-11-17）

| 文件 | SHA-256 |
|---|---|
| `Assembly-CSharp.dll` | `7fbce7a5b2a26cac500150d3cff408cdbbd8f1797456bc741252e44c2d27bdc4` |
| `UnityStubs.dll` | `b90e4f18956c8c548b27106171cb7e9320d04c5116499a17ea7e16db294ae0d6` |
| `0Harmony.dll` | `c8ef7856b2be3cf24b345c9b5c2f197f68fcf92649635064fce7b3ccc8f77cb0` |
| `DMS.dll` | `b94cd0359f9e276240cbb35659b057cac568d6f1a83de3f743c0503116b3234a` |
| `Fortified.dll` | `9c135095c0a389ae63c08a514b2751b9b168bdc125aea9da8d303f81f8b0e952` |
| `DMS_Legion.dll`（仅验证加载） | `00afec1d5f2b22210e7ccd2fc502d88bc97711155ac160d39fbc1034c85d51ea` |
| `DMSSR.dll` | `19714ac9acd16d78d093fd7b973b5e5d05397794986e82abd89557f4a0fb1033` |

## 维护与退役条件

上游或游戏本体更新后请重跑 `Validation\validate.ps1`：当某一子系统的 RED 前提不再复现（上游去重寻路钩子、原生放行 `DMSL_Mech_*`、补齐机兵受体凝胶结算与自动用药、保护死亡前 `RemoveHediff`、在 `CarryCorpseToStasisPod` 补齐 `job.count`，或 12 项审计问题任一项被上游修复）时，单独移除对应子系统；全部由上游实装后退役整个补丁包。四个历史独立补丁的原版发布 DLL 与验证套件保留于仓库 `archive/` 目录归档。
