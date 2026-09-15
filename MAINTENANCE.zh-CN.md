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
| Fix_DMS_BookGrammarAndHeavyShield | DMS 研究语法与机体 body part group | 上游补齐规则或盾牌装备条件，避免重复追加 |
| Fix_WRMegaCorp_QuestExposeData | WRMegaCorp ChoiceLetter_CSC.ExposeData 的 IL | 上游删除重复 Scribe_Deep 调用，或 transpiler 锚点改变 |
| LocalFix_MiliraFlightNullCheck | Milira FlightTracker 前缀 | 上游自行检查缺失飞行组件，或改用不同飞行机制 |

`upstream-patches/` 的直接工坊修改须结合记录的基准使用。标为历史缺失、已撤回或不建议安装的内容只留档，不因这次合仓自动重打。
