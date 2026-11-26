# DMS Tribute Collector Isolation / 征募代理隔离修复

RimWorld **1.7**，版本 **1.0.0**，packageId `whitegiver.dms.tributecollectorfix`。

## 问题与根因

启用「失能机关」（The Dead Man's Switch II）后，原版皇权 DLC 破碎帝国到访的「皇家声望征募商队」会有 50% 概率变成「征募代理（recruitment agency）」，交易界面只收购冲压零件、神经计算机等 DMS 物资而不收黄金，但交货后结算的依然是破碎帝国皇家声望（荣誉）。

造成该现象的原因并非 DMS 覆盖或修改了原版 `Empire_Caravan_TributeCollector` 的 XML 定义，而是**原版皇家征募商队事件按 `category == "TributeCollector"` 全局抽取商队类型时未过滤阵营**：

1. 原版 `IncidentWorker_CaravanArrivalTributeCollector.TryResolveParmsGeneral` 先将事件阵营固定为破碎帝国（`parms.faction = Faction.OfEmpire`），随后从全局 `DefDatabase<TraderKindDef>` 中筛选所有 `t.category == "TributeCollector"` 的商队定义并按 `TraderKindCommonality` 加权随机抽取：
   ```csharp
   parms.faction = Faction.OfEmpire;
   parms.traderKind = DefDatabase<TraderKindDef>.AllDefsListForReading
       .Where(t => t.category == "TributeCollector")
       .RandomElementByWeight(t => TraderKindCommonality(t, map, parms.faction));
   ```
2. 基类 `IncidentWorker_TraderCaravanArrival.TraderKindCommonality` 原本包含 `if (traderKind.faction != null && faction.def != traderKind.faction) return 0f;` 的阵营匹配检查，但 `IncidentWorker_CaravanArrivalTributeCollector.TraderKindCommonality` 重写了该方法并直接返回 `traderKind.CalculatedCommonality`，绕过了 `traderKind.faction` 过滤。
3. DMS 核心模组（`3819799478`）的 `DMS_Caravan_TributeCollector` 虽然声明了 `<faction>DMS_Army</faction>`，但复用了 `<category>TributeCollector</category>`（`commonality` 同为 `1`）。
4. 因此，每次触发原版破碎帝国皇家声望征募商队事件时，`Empire_Caravan_TributeCollector` 与 `DMS_Caravan_TributeCollector` 会以 1:1 权重进入同一随机池。当抽中 `DMS_Caravan_TributeCollector` 时，商队阵营与来信仍是破碎帝国（`Tradeable_RoyalFavor.ResolveTrade` 也按 `TradeSession.trader.Faction` 给破碎帝国加荣誉），但商队持有的 `TraderKindDef` 却是 DMS 征募代理的交易表。

### 相关文件位置

| 文件 | 关键位置与内容 |
| --- | --- |
| `Data/Royalty/Defs/TraderKindDefs/TraderKinds_Caravan_Empire.xml` | **168–184**：`Empire_Caravan_TributeCollector`，`<category>TributeCollector</category>`，`<tradeCurrency>Favor</tradeCurrency>`，`<faction>Empire</faction>`，收购奴隶与 `Gold` |
| `3819799478/1.7/Defs/Pawnkinds/TraderKinds.xml` | **441–470**：`DMS_Caravan_TributeCollector`，**446** `<category>TributeCollector</category>`，**448** `<faction>DMS_Army</faction>`，收购奴隶及 `ComponentStamped`、`Neurocomputer`、`ComponentTransmitter`、`ComponentControlChip`、`ComponentFireControl` |
| `3819799478/1.7/Defs/Faction/Faction.xml` | **96–102**：`DMS_Army.caravanTraderKinds` 按 `defName` 列出 `DMS_Caravan_TributeCollector` |
| `3819799478/1.7/Royalty/Defs/Title/DMS_Permits.xml` | **49–69**：呼叫许可 `DMS_Call_TributeCollector`（`Fortified.RoyalTitlePermitWorker_Trader`）按 `defName` 指定 `DMS_Caravan_TributeCollector` |
| `3666117000/Defs/Defs/StorytellerDefs/Elisa.xml` | **32–38**：Legion 叙事者艾丽萨定期征募商队组件按 `defName` 指定 `DMS_Caravan_TributeCollector` |
| `3835512735/1.7/Patches/ZZ_Content_Trade.xml` | **6–15**：Synthriada 向所有 `tradeCurrency="Favor"` 的 `TraderKindDef` 追加 `DMSSR_FlowCrystal` 与 `DMSSR_ImpureFlowCrystal` |

## 修复方案

`Patches/IsolateRecruitmentAgency.xml` 通过 `PatchOperationConditional` + `PatchOperationReplace` 将 `DMS_Caravan_TributeCollector` 的 `<category>` 从 `TributeCollector` 改为 `DMS_TributeCollector`：

```xml
<Operation Class="PatchOperationConditional">
  <xpath>Defs/TraderKindDef[defName="DMS_Caravan_TributeCollector" and faction="DMS_Army"]/category[text()="TributeCollector"]</xpath>
  <match Class="PatchOperationReplace">
    <xpath>Defs/TraderKindDef[defName="DMS_Caravan_TributeCollector" and faction="DMS_Army"]/category[text()="TributeCollector"]</xpath>
    <value>
      <category>DMS_TributeCollector</category>
    </value>
  </match>
</Operation>
```

- **破碎帝国皇家声望征募商队**：`category == "TributeCollector"` 池中只剩 `Empire_Caravan_TributeCollector`，100% 正常收购黄金与奴隶（启用 Synthriada 时包含追加的两种流晶，共 4 项）。
- **DMS 征募代理**：DMS 普通商队（按 `DMS_Army.caravanTraderKinds` 抽取并经 `IncidentWorker_TraderCaravanArrival.TraderKindCommonality` 检查阵营）、FFF 头衔呼叫许可（`Fortified.RoyalTitlePermitWorker_Trader`）与 Legion 艾丽萨叙事者组件均直接按 `defName` 引用 `DMS_Caravan_TributeCollector`，正常收购冲压零件等 6 项物资（启用 Synthriada 时为 8 项）并结算 `DMS_Army` 声望。
- **生效范围**：下次启动游戏加载 Def 后对新生成的征募商队生效；当前存档地图上已生成的商队实例已绑定旧 `TraderKindDef`，需等待下一支新商队到访。

## 离线验证与游戏内浅测

### 离线 RED / GREEN 验证

基于本机 RimWorld `1.7.5000 rev1275` 的 `Assembly-CSharp.dll`、Royalty 原始 XML、DMS `3819799478` 原始 XML 与 Synthriada `3835512735` 贸易补丁，固定随机种子 `20261112` 连续执行 256 次 `IncidentWorker_CaravanArrivalTributeCollector.TryResolveParmsGeneral`：

| 场景 | 256 次帝国征募事件抽取结果 | 检查项 |
| --- | --- | ---: |
| RED（Royalty + DMS） | `Empire_Caravan_TributeCollector` 128 次、`DMS_Caravan_TributeCollector` 128 次 | 16 PASS |
| GREEN（Royalty + DMS + 本补丁） | `Empire_Caravan_TributeCollector` 256 次、`DMS_Caravan_TributeCollector` 0 次 | 27 PASS |
| RED（含 Synthriada 贸易补丁） | `Empire_Caravan_TributeCollector` 128 次、`DMS_Caravan_TributeCollector` 128 次 | 17 PASS |
| GREEN（含 Synthriada 贸易补丁 + 本补丁） | `Empire_Caravan_TributeCollector` 256 次、`DMS_Caravan_TributeCollector` 0 次 | 28 PASS |

同时验证了双方 `WillTrade` 清单、黄金 `0.015` 与冲压零件 `0.05` 的声望换算、`Tradeable_RoyalFavor.ResolveTrade -> GainFavor` 阵营声望入账、DMS 普通商队与呼叫许可引用，以及本机已启用 306 个程序集的 `TributeCollector` 引用扫描。

### 游戏内最小浅测

下次正常启动游戏后，可通过开发者模式 `Execute incident -> CaravanArrivalTributeCollector`（或等待新的破碎帝国皇家声望征募商队自然到访）打开交易窗口，确认商队名称为皇家征募商队且正常收购黄金换取帝国荣誉。

## 更新监测、失效与退役条件

| 上游文件 | 检查时最后修改时间（UTC+8） | SHA256 |
| --- | --- | --- |
| `Assembly-CSharp.dll` | 2026-11-11 03:37:52 | `82387F78262461E53BB06A67C3945912B876D1FDB07235122BBB6E59D073F125` |
| Royalty `TraderKinds_Caravan_Empire.xml` | 2025-07-12 08:48:54 | `BF0679B2440E189272AA5767E366A49909F1743E9ED3FDE0DA111E824763C067` |
| DMS `1.7/Defs/Pawnkinds/TraderKinds.xml` | 2026-10-15 02:38:02 | `AFC69C25D0949C46D5E276E7D226A634114D9AF6747B034A57A49C60B08BAF5E` |
| DMS `1.7/Assemblies/DMS.dll` | 2026-11-12 21:17:03 | `B94CD0359F9E276240CBB35659B057CAC568D6F1A83DE3F743C0503116B3234A` |

- **自动跳过与退役**：若上游 DMS 更新后将 `DMS_Caravan_TributeCollector` 的 `<category>` 改离 `TributeCollector`，或原版 `IncidentWorker_CaravanArrivalTributeCollector.TraderKindCommonality` 补上 `traderKind.faction` 匹配过滤，本补丁的 `PatchOperationConditional` 会安全跳过，届时可直接停用移除。
- **失效检查**：可通过对比 `3819799478/1.7/Defs/Pawnkinds/TraderKinds.xml` 的修改时间或 SHA256 监测上游变动；若上游改名 `defName` 或调整 `<faction>`，需复核 `TraderKinds.xml` 中的 `category` 字段。

## 给上游作者的反馈草稿

> 作者大大好呀～在 1.7 + DMS2 环境下发现一个皇家声望征募商队串表的小问题：原版皇权 DLC 的 `IncidentWorker_CaravanArrivalTributeCollector` 会先把事件阵营固定为 `Faction.OfEmpire`，然后从全局所有 `category == "TributeCollector"` 的 `TraderKindDef` 中随机抽取商队类型，而且它重写的 `TraderKindCommonality` 直接返回 `CalculatedCommonality`，没有像普通商队那样检查 `traderKind.faction == faction.def`。因为 DMS2 的征募代理 `DMS_Caravan_TributeCollector`（`1.7/Defs/Pawnkinds/TraderKinds.xml:446`）也写了 `<category>TributeCollector</category>`，导致原版破碎帝国的皇家征募商队事件有 50% 概率抽中 DMS 的征募代理交易表（来的是帝国商队、给帝国荣誉，但交易列表变成收冲压零件而不收黄金）。因为 DMS 普通商队、FFF 呼叫许可和艾丽萨叙事者都是按 `defName` 直接引用 `DMS_Caravan_TributeCollector` 的，只要把 `DMS_Caravan_TributeCollector` 的 `<category>` 改成独立名称（比如 `DMS_TributeCollector`）或者去掉原版专用分类，两边就完全不会串表啦～感谢大大制作这么棒的模组！
