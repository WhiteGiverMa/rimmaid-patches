# DMSSR Neuroglue & Auto-Refill Fix / 死手开关：合成体 受体凝胶药效与自动补给修复

> Standalone Harmony + ThinkTree sidecar patch for **The Dead Man's Switch - Synthriada**
> (`Cephalopod.DeadManSwitch.Synthriada`, Workshop `3835512735`) & **The Dead Man's Switch II** (`Aoba.DeadManSwitch.Core`, Workshop `3819799478`).
>
> PackageId: `whitegiver.dmssr.neurogluefix` · Version `1.0.0` · RimWorld 1.7 · `net10.0` · No custom save data, no Workshop edits.

---

## 1. 现象与根因（Root Cause）

### 1.1 机兵使用受体凝胶后「退行性病变」只增不减
在《失能机关：复乐园》中：
1. 每当神经信号效率（`DMSSR_BiogelReservoir`）向下跌破 `90`、`80`……`10`、`0` 等整十刻度时，`Hediff_BiogelReservoir.OnSignalEfficiencyDrop` 按 `(100 - 刻度)%` 概率向随机非缺失部位施加 `DMS_DegenerativeSyndrome`（退行性病变）。
2. `DMS_DegenerativeSyndrome` 自带 `<severityPerDay>0.15</severityPerDay>` 与 `HediffComp_DegenerativeSyndromeSpread`（每跨过 `0.25` 严重度向另一随机部位扩散），**没有随神经信号效率自然回退的机制**；它的唯一消除机制是 XML 上的 `HediffCompProperties_RemoveIfOtherHediff`，监听 `NeuroglueHigh`（受体凝胶生效，持续约 5.56 天）。
3. 人类殖民者走原版 `Thing.Ingested` 服用 `Neuroglue` 时，会依次执行全部 3 个 `IngestionOutcomeDoer`：
   - `IngestionOutcomeDoer_GiveHediff` → 赋予 `NeuroglueHigh`（立即清除全身所有 `DMS_DegenerativeSyndrome`、`DMS_SyntheticRejection`、`DMS_BionicShockRemove`，并在药效期内免疫复发）；
   - `DMS.IngestionOutcomeDoer_ChangeHediffLevel` → 将 `DMS_BionicShock`（异肢症候群）降低 1 级；
   - `DMSSR.Hediffs.IngestionOutcomeDoer_RefillBiogel` → 恢复 `25` 点神经信号效率。
4. 但上游 `DMSSR.Jobs.JobDriver_UseNeuroglue`（为 `IsMechanoid` 合成机兵专门实现的用药任务）在最后一个 Toil 结算时写成了：
   ```csharp
   foreach (IngestionOutcomeDoer item in list)
   {
       if (item is IngestionOutcomeDoer_RefillBiogel)
       {
           item.DoIngestionOutcome(Recipient, carriedThing, 1);
           flag = true;
       }
   }
   ```
   导致合成机兵使用受体凝胶时**只加了 25 点神经信号效率，漏掉了 `NeuroglueHigh` 与异肢症降级**，身上的退行性病变永远无法消除，只能无限增长并传遍全身。

### 1.2 满效率或无储槽的患病机兵被 `CanRefill` 拦截
上游 `JobDriver_UseNeuroglue.CanRefill(Pawn)` 要求 `Hediff_BiogelReservoir.Find(target)?.Fuel < 100f`：
- 若机兵在神经信号效率 `100 / 100` 时仍携带有旧的退行性病变；
- 或普通改装机兵因消耗免死次数（`DMS_DeathCheat`）在 `HediffComp_MechCustomization.Advance` 中染上了退行性病变（此类机兵没有 `Hediff_BiogelReservoir`）；
上游会直接返回 `false`，导致手动右键喂胶与自用全部失效。

### 1.3 缺乏机兵自动使用 AI 与右键菜单全图扫描
- 合成人形机兵底层为 `IsMechanoid == true`、`Humanlike == false`，无 `Pawn_DrugPolicyTracker` 用药方针，上游也未在 `DMS_HumanlikeMech` / `DMS_Mechanoid` / `DMSSR_Cephalopoda` / `DMSSR_Hydra` 行为树中接入自动使用 `DMS_UseNeuroglue` 的 `JobGiver`。
- 上游 `FloatMenuOptionProvider_UseNeuroglue.GetOptionsFor` 在右键机兵时使用 `actor.Map.listerThings.AllThings.Where(t => t.def.defName == "Neuroglue" && Available(actor, t)).OrderBy(...)` 遍历全图数万个 `Thing` 并逐个跑寻路检查。

---

## 2. 修复与性能设计

1. **完整执行受体凝胶药效（`UseNeurogluePatches.MakeNewToilsPostfix`）**：替换 `JobDriver_UseNeuroglue` 末尾结算 Toil 的 `initAction`，对目标机兵完整执行 `Neuroglue.ingestible.outcomeDoers` 中的所有效果器（赋予 `NeuroglueHigh`、降低 `DMS_BionicShock` 等级、恢复 `25` 点神经信号效率），不走人类 `CompDrug` 过量或饮食心情。
2. **允许患病机兵在满效率或无储槽时用药（`UseNeurogluePatches.CanRefillPostfix`）**：当机兵存在未受 `NeuroglueHigh` 压制的 `DMS_DegenerativeSyndrome` / `DMS_SyntheticRejection` / `DMS_BionicShockRemove` 或存在 `DMS_BionicShock` 时，`CanRefill` 放行返回 `true`。
3. **零分配机兵自动补胶 AI（`JobGiver_AutoUseNeuroglue` + `MechBiogelCache`）**：
   - 通过条件 XML 补丁插入 `ThinkNode_ConditionalRecharging` 之后，覆盖 `DMS_HumanlikeMech`、`DMS_Mechanoid`、`DMSSR_Cephalopoda`、`DMSSR_Hydra` 及原版 `Mechanoid` 行为树。
   - **触发条件**：己方受控机兵在非征召、非 `SelfShutdown` 休眠、非交战（无 `enemyTarget` 且距上次攻击 ≥ 300 ticks）时，若①存在上述待治疗病变，或②神经信号效率 `<= 75`（每支正好回 25 点至 100，零浪费），自动前往取用 1 支最近的可用 `Neuroglue`。
   - **性能保障**：
     - `HediffSet.DirtyCache` 驱动的每 Pawn 快照缓存：普通无湿件机兵和健康合成机兵在 `TryGiveJob` 中只需一次引用读取即可短路返回 `null`（离线实测 200,000 次求值总耗时 `19.23 ms`，平均 `96.1 ns/次`，**`0 B` GC 分配**）。
     - 地图库存查询直接读 `map.listerThings.ThingsOfDef(Neuroglue)`：基地无库存时 O(1) 空列表直接返回；有库存时先按水平距离平方过滤，仅对更近的堆叠检查 `IsForbidden`、`CanReserve` 与 `CanReach`，跳过原版 `ClosestThingReachable` 的 30 区域 BFS 预扫。
     - 当地图无可用/可达受体凝胶时，为该机兵记录 **600 ticks（10 秒）退避冷却**（`HediffSet.DirtyCache` 时立即重置），避免每轮 ThinkTree 重复寻路。
4. **右键菜单快路径（`UseNeurogluePatches.GetOptionsForPrefix/Postfix`）**：用同一 `NeuroglueFinder` 替代上游 `listerThings.AllThings` LINQ 扫描。

---

## 3. 验证结果（Offline RED / GREEN）

运行 `pwsh -NoProfile -File Validation/validate.ps1`（使用真实 `Assembly-CSharp.dll`、`DMS.dll`、`DMSSR.dll`、`Fortified.dll`）：
- **RED（7 项断言通过）**：复现上游机兵在 `62.98` 效率、9 处退行性病变时使用 1 支受体凝胶后效率升至 `87.98`，但无 `NeuroglueHigh`、9 处退行性病变原封不动；复现 `100 / 100` 效率或无储槽患病机兵被 `CanRefill` 拒绝。
- **GREEN（96 项断言通过）**：验证 `87.98` 效率带 9 处退行性病变 + 排异 + 2 级异肢症的机兵自动取用 1 支受体凝胶后，效率升至 `100`、获得 `NeuroglueHigh`、9 处退行性病变与排异在下一 tick 全部清零、异肢症降为 1 级并在第二支后痊愈停止用药；验证无储槽机兵清除免死病变、`75.05 / 75.00` 阈值边界、征召/休眠/交战保护、禁用/不可达过滤与 600 ticks 零查询退避、4 棵真实行为树 XML XPath 命中，以及 20 万次热路径 `0 B` GC 分配。

---

## 4. 时效性与退役条件（Lifecycle & Retirement）

- **监测上游文件**：`A:\SteamLibrary\steamapps\workshop\content\294100\3835512735\1.7\Assemblies\DMSSR.dll`（基线写入时间 `2026-11-13 09:47:18 UTC`，SHA256 `19714AC9ACD16D78D093FD7B973B5E5D05397794986E82ABD89557F4A0FB1033`）。
- **自动诊断日志**：启动时日志打印 `[DMSSR NeuroglueFix] installed: ... upstreamRefillOnlyFilter=True/False`。
  - 当上游修复 `JobDriver_UseNeuroglue` 不再只过滤 `IngestionOutcomeDoer_RefillBiogel` 时，`upstreamRefillOnlyFilter` 会变为 `False`（此时结算补丁仍幂等安全，不会重复扣药或重复加药效）。
  - 若上游后续同时在 `DMSSR` 内置了机兵自动补胶 `JobGiver` 并放宽了 `CanRefill` 对患病机兵的限制，即可停用并归档本补丁。
