# DMS Synthetic Apparel Repair：机兵装备维修兼容

让 DMS 系列可穿装备的机兵在维修时同时修复磨损的 apparel/武器；不做维修时不影响原版行为。

## 2026-11-05 / 1.7 更新

- 目标框架改为 `net10.0`，直接引用本地 1.7 `Assembly-CSharp.dll`、`UnityStubs.dll` 和 1.7 `0Harmony.dll`。Release 构建 0 warnings / 0 errors，LSP 无诊断。
- 删除 `Patches/DMS_Synthetic_Repairable.xml`：`DMS_Mech_Maiden/Armiman/BigSister/Lady` 经 `BaseAutomatroidHumanRace → BaseMechanoid` 已继承 1 个 `CompProperties_MechRepairable`，旧 XPath 补丁会再叠 1 个（同 def 两个同类型 comp）。
- 运行时由 `Patches.EnsureSyntheticRepairableComps()` 统一保证“恰好 1 个”：`>1` 去重；`0` 时新增并仅对新实例调用 `compProps.ResolveReferences(def)`（不重跑 `ThingDef.ResolveReferences()`）。后者覆盖 `DMS_Mech_Frogman`：其基类 `DMS_ArtificialOrganismBase` 使用 `<comps Inherit="False">`，因此不会继承原版 `BaseMechanoid` 的维修 comp。
- 合格机兵（`IsSynthetic`）扩展为与 `DMSHumanlikeMechUntainted` 对齐：`PackageIdPlayerFacing` 属于 `aoba.deadmanswitch.synthetic` / `aoba.deadmanswitch.core` / `srxf.deadmanswitch.legion`，`race.IsMechanoid`，且满足其一：
  - `SyntheticDefNames`：`DMS_Mech_Maiden`、`DMS_Mech_Armiman`、`DMS_Mech_BigSister`、`DMS_Mech_Lady`、`DMSL_Mech_Terra`、`DMS_Mech_Frogman`；
  - `def.comps` 命中 `Fortified.CompMechApparel` / `DMS_Legion.CompTerraBodyType`（同时按 `compClass` 与 `CompProperties` 类型判断），运行时再兜底检查 `pawn.AllComps`。
- DMS Legion 1.7 维修回调为 `JobDriver_MechRepairMech.<MakeNewToils>b__4_3`（`void(int)`，实际调用单参 `MechRepairUtility.RepairTick(Pawn)`）：优先按此名匹配，回退 `<MakeNewToils>b__3_2`（1.6），最后按 IL 结构在整个驱动类型中寻找 `void(int)`、名字以 `<MakeNewToils>b__` 开头且调用 `RepairTick` 的方法。
- 故障隔离：原单一全局闸门 `gearRepairTransactionsAvailable` 拆为 `vanillaRepairTransactionAvailable` / `dmsLegionRepairTransactionAvailable` / `miliraRepairTransactionAvailable`。可选驱动（Legion、Milira）解析或转译失败时只停用该驱动的装备维修并单独 `Log.Error`，不再连带停用 vanilla/其他可用驱动；`HasDamagedGear`、`TryCreateGearRepairPlan` 只要 `AnyGearRepairTransactionAvailable` 为真就继续工作，各 `TryRun...GearTransaction` 各自检查本驱动标志。

历史：2026-10-13 因 Legion 回调由双参 `RepairTick(Pawn,int)` 改为单参调用而全局停用装备维修，当时按实际 IL 分流修复；1.7 的 Legion 2.0.1 再次重建，回调从 `b__3_2` 改名为 `b__4_3`，本次据此重新对齐。

## 离线验证（隔离进程，2026-11-05）

验证工程：`C:\Users\A1337\AppData\Local\Temp\opencode\dms-apparel-repair-17-20261105\harness`。使用真实 1.7 `Assembly-CSharp.dll`、`DMS_Legion.dll`、`Milira.dll`、`Fortified.dll` 和安装的 DMS race XML。仅对引擎程序集做最小桩化（清空 `ModsConfig`/`BaseContent`/`StatDefOf` 静态构造、固定 `MaxHitPoints=100` 与 `GetStatValue=1`），并把 1.6 Milira 的 `UnityEngine.*` 引用重定向到 `UnityStubs`；补丁逻辑本身不改。

- RED：旧部署 DLL（SHA256 `2E256B2F9D50FBCE7080CF2D9C829FD15ADAA59C826EFEBA855ED78F85F39093`）在 1.7 Legion 上找不到 `<MakeNewToils>b__3_2`，全局闸门翻为 `false`，Legion 回调 0 个 transpiler。
- GREEN（原版 Legion）：vanilla `<MakeNewToils>b__10_2`、Legion `<MakeNewToils>b__4_3`、Milira `<MakeNewToils>b__20_2` 各注册 1 个 transpiler；把真实 IL 交给 transpiler 后各插入恰好 1 个事务守卫。
- GREEN（结构回退）：把 Legion 回调改名为 `<MakeNewToils>b__4_9` 后，仍按 IL 结构命中并插入 1 个守卫。
- GREEN（隔离）：把 Legion 回调改成空 `ret` 且改名后，`dmsLegionRepairTransactionAvailable=false`，vanilla / Milira / `AnyGearRepairTransactionAvailable` 仍为 `true`。
- `IsSynthetic` 识别 Maiden、Armiman、BigSister、Lady、Terra、Frogman，并拒绝非 DMS 包、非机械体、无穿衣机制 comp 的未知机兵。
- `EnsureSyntheticRepairableComps` 对 6 个机兵 def 都留下恰好 1 个 `CompProperties_MechRepairable`（含重复去重与 Frogman 新增），且新增时不会重跑已有 comp 的 `ResolveReferences`。
- 真实 XML 继承链：Maiden/Armiman/BigSister/Lady/Terra 各继承 1 个、Frogman 0 个；6 个 def 都没有自行声明 `CompProperties_MechRepairable`。

未验证：游戏内实际装备耐久恢复、能量扣除、读档与旧 1.6 双参 Legion 分支。验证只覆盖注册、守卫插入与检测/补 comp 逻辑，不等同于实机验收。

## 发布与退役

- 发布 SHA256：`BA1D44EF3B69D93547D3FE4DE56E879DF603A239F704B77E6F971B4BFD3442E2`（`Assemblies/DmsSyntheticApparelRepair.dll`）。
- Legion / 原版 / Milira 维修回调或事务模式一旦变化，必须重新核对签名与实际 IL，并按驱动分别确认守卫注册；不要为了让警告消失而放宽匹配或吞异常。
- 若上游（DMS Synthetic / Legion / Fortified）原生支持这些机兵的装备维修，评估退役本补丁，而不是只隐藏安全警告。

浅测建议：正常重启读档，确认无 “repair transaction changed” 红字；让维修者维修一名装备受损的 Maiden/Lady/Terra/Frogman，观察耐久与能量变化；再分别只保留 Legion、只保留 Milira 的场景验证隔离。
