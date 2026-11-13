# DMSSR 孤立子活性衰减 / Soliton Charge Decay

RimWorld 1.7，版本 **1.0.0**，packageId **`whitegiver.dmssr.solitonchargedecay`**。

## 已退役归档 — 2026-11-13

**本补丁已由用户停用，从 `mods/` 移入 `archive/`，不再作为推荐安装项。下文行为、安装命令、115 项验证与旧上游 hash 均为 1.0.0 的历史记录。** About、发布 DLL、源码、验证脚本和调查文档完整保留；发布 DLL 未重新编译，SHA-256 仍为 `116e1a68a316640e4670c9847ee52bc84b42d75c3be2c32f0bf46881e4083a99`。本次归档只移动仓库副本，游戏 `Mods/DMSSRSolitonChargeDecay` 保留原状。

作者 Cephalopod 在[原反馈串](https://steamcommunity.com/workshop/filedetails/discussion/3835512735/738187822665612482/#c738187822665619893)回复：「1方法用错了，2没做边界情况保护，3是刻意改成的停火清零 / 我现在修一下」（2026-11-12 14:53:16 UTC）。因此，Idle UI 取值与失败 Burst 捕获属于 Bug，重型集束器、执迷之眼的停火清零是明确设计，不再将其列作漏配。

2026-11-13 实查结果：

- **工坊远端**：主页与更新页已有 Nov 12 / Nov 13 的新记录，最新发布时间为 **2026-11-13 08:25:16 UTC**（页面显示 Nov 13 @ 12:25am PST）。
- **本机磁盘**：`DMSSR.dll` 已更新为 **729088 bytes**，最后写入 **2026-11-13 09:47:18.937676 UTC**，SHA-256 `19714ac9acd16d78d093fd7b973b5e5d05397794986e82abd89557f4a0fb1033`；Steam 的已安装 manifest 与最新 manifest 均为 `161770291871422498`，`timeupdated` 与远端一致。
- **已实装**：Idle `GetChargePercent` 读取 `ComputeCarryTicks`；`StoreWarmupOnStop` 直接用时间差计算余量，不再依赖 Bursting 状态；`WarmupComplete` 和 `BurstingTick` 在失败结束后补 `Reset`；重型/炮塔继续使用缺省 `beamSwitchFire=false`；散布器 XML 的 `beamCount` 已从 6 改为 **12**，与三语描述一致。
- **验证与剩余边界**：当前上游真实 DLL 离线驱动 **63 项检查通过**，确认 Idle 衰减、两种失败入口、首次射击失败、重复 Reset、上限/负时间及安全清空均正常工作。另观察到一处细微边界：失败清理经原版 `Reset` 会清空炮塔的 `castCompleteCallback`，使 `Building_TurretGun` 在经历一次失败 Burst 后不再登记后续 Burst 完成冷却（执迷之眼本身未开启 carry，对聚焦器等手持武器无影响）。

逐行变更、XML/三语对照及验证详情见 [UPSTREAM-VERIFICATION-20261113.md](UPSTREAM-VERIFICATION-20261113.md)。

## 历史说明（1.0.0）

让 Synthriada 的孤立子光束在普通停火后保留剩余活性，按作者已有的 `coldTime` 线性消散；能量条同步显示剩余值，再次开火继承尚未消散的活性。沿用原生参数和存储字段，无额外设置。

## 行为

| ThingDef | 名称 | 原生聚焦时间 | 停火衰减时间 |
|---|---|---:|---:|
| `DMSSR_Soliton_Beam_Light` | 孤立子手枪 / soliton pistol | 300 tick / 5 秒 | 60 tick / 1 秒 |
| `DMSSR_Soliton_Beam` | 机兵孤立子聚焦器 / automatroid soliton focuser | 600 tick / 10 秒 | 120 tick / 2 秒 |
| `DMSSR_Soliton_Beam_Heavy` | 机兵孤立子集束器 / automatroid soliton concentrator | 1800 tick / 30 秒 | 360 tick / 6 秒 |
| `DMSSR_Soliton_Beam_Shotgun` | 机兵孤立子散布器 / automatroid soliton disperser | 300 tick / 5 秒 | 60 tick / 1 秒 |
| `DMS_Gun_BeamLaser` | 光束激光系统 / Beam laser system；安装在执迷之眼 / Eye of Obsession | 1800 tick / 30 秒 | 360 tick / 6 秒 |

- 停火时保存 `min(聚焦上限, 已开火 tick + 继承活性 tick)`。
- 在上述整个冷却周期内，剩余活性从停火时的数值线性降至零。50% 活性的重型光束停火 3 秒后剩 25%，6 秒后为零。
- 瞄准时间也计入冷却；不会因为开始瞄准而停止消散。
- 每发之间仍处于同一次 Burst，照常积累活性。目标转移若走原生无缝扫射路径，仍然连续充能。
- 保留安全模式达到过热阈值后立即清空活性、强制冷却、禁止再射击的规则。
- 保留过热双倍伤害、反馈伤害、耗电、散布、光束数和所有 XML 参数。
- `DMSSR_ResentfulFlame_SGWC`（憎恶烈炬 / Resentful Flame）使用 FFF 实弹换弹种 Verb，不属于这套充能系统。

完整中英文名称、描述、XML 参数及原版逐行分析见 [INVESTIGATION.md](INVESTIGATION.md)。[AUTHOR-FEEDBACK.md](AUTHOR-FEEDBACK.md) 包含可以发给 Cephalopod 的中英文草稿。

## 根因与设计判定

1. 原生 `StoreWarmupOnStop` 在 `beamSwitchFire=false` 时明确清零。五个系统中只有聚焦器的 XML 启用该开关。因此重型与炮塔的瞬清是现有代码的直接行为；开关取舍属于作者需确认的平衡决策。
2. 聚焦器已有真实的 `ComputeCarryTicks` 线性衰减，`GetChargePercent` 却通过 `GetFiringTicksRaw` 在 Idle 返回零；UI 最多 9 tick 就清空。这是显示取值与实际储能脱节。
3. `Verb.TryCastNextBurstShot` 失败时先转 Idle，又不调用 DMSSR 的 `Reset`；稍后 `Reset` 若用依赖 Bursting 的 `GetWarmupTicks` 保存活性，会捕获到零。这是停火生命周期的遗漏。

此补丁接通后两处逻辑，并让全部五种粒子光束使用已有衰减机制。

## 构建与验证

```powershell
dotnet build Source/DMSSRSolitonChargeDecay.csproj -c Release
pwsh -File Validation/run.ps1
pwsh -File Validation/run.ps1 -PatchPath red
pwsh -File Validation/run.ps1 -PatchPath expect-decay
pwsh -File Validation/inventory.ps1
```

`expect-decay` 在原版运行停火应保留活性的断言，预期退出码为 1。`red` 核对原版故障链。默认运行构建补丁并完成红绿测试；可用 `-PatchPath <DLL>` 验证发布或已安装副本。

2026-11-12，生产项目与验证驱动构建均 **0 警告 / 0 错误**；发布 DLL 通过 **115 项检查（10 项原版基线 + 105 项补丁检查）**：

- 实际 Mod 构造注册、四个 hook、重复 Apply 幂等性。
- 读取实际 Defs，五种系统停火保留、每个冷却 tick 的实际活性/UI 线性曲线、重复 Reset、不倒退为负数、到期归零。
- 实际 `WarmupComplete` 继承余量；实际成功/失败 Burst、回调保留、普通射击间隔仍连续充能；其他 Verb 行为不变。
- 真实 `Building_TurretGun` 的 holdFire、强制换目标，以及 Pawn 无 Job 停火，经实际 `BurstingTick` 与 `Reset` 验证。
- 原生安全模式强制排空、开火阻止、冷却到期，以及过热阈值和双倍伤害。
- 真实 `ExposeData` / Scribe 文件往返，读取已有 `storedWarmupTicks` / `lastStopTick`，继续按原时间冷却。

驱动直接加载原版 `DMSSR.dll` 与真实游戏程序集的离线副本；副本禁用 `ModsConfig`/`ModLister` 静态扫描、日志输出和炮塔静态材质初始化。脚本逐指令比对 `Verb`、`VerbTracker`、`Pawn_StanceTracker`、`Building_TurretGun` 的 **145 个非静态构造方法体**，全部保持原样。成功命中测试替换射击交付，Pawn 测试提供生命/控制/任务查询，普通间隔测试替换视觉提交；充能、冷却、状态转换、Reset、WarmupComplete 和 Scribe 路径均执行真实方法。

**2026-11-12 用户确认游戏内验证通过。**

## 安装与启用

依赖 Harmony、The Dead Man's Switch - Synthriada，排在 Synthriada 后。运行资源为 `About/`、`LoadFolders.xml`、`1.7/Assemblies/`，附带三份说明文档。

```powershell
# 同步文件，配置不变
pwsh -File Validation/deploy.ps1

# 游戏正常退出后同步并启用
pwsh -File Validation/deploy.ps1 -Enable
```

`-Enable` 会在写配置前备份原配置，只在 `cephalopod.deadmanswitch.synthriada` 后插入本补丁，逐项校验原条目与顺序保持一致。

2026-11-12 已将补丁目录同步到 `Mods/DMSSRSolitonChargeDecay` 并在 `ModsConfig.xml` 紧跟 `cephalopod.deadmanswitch.synthriada` 之后启用（301→302，原条目与顺序保持不变）；用户正常重启后确认游戏内验证通过。源码和发布产物位于本仓库，本次 QA 状态更新保持已验证 DLL 逐字节不变。

## 基线与 SHA-256

磁盘 `Version.txt`：**1.7.5005 rev1082**。

| 文件 | SHA-256 |
|---|---|
| 发布 `DMSSRSolitonChargeDecay.dll` | `116e1a68a316640e4670c9847ee52bc84b42d75c3be2c32f0bf46881e4083a99` |
| `DMSSR.dll`（Workshop 3835512735） | `d28367b8af9e44fe6354dd78b07082bdb10984ad0b8c93343f69e29660a2964d` |
| `Assembly-CSharp.dll` | `82387f78262461e53bb06a67c3945912b876d1fdb07235122bbb6e59d073f125` |
| `0Harmony.dll` | `c8ef7856b2be3cf24b345c9b5c2f197f68fcf92649635064fce7b3ccc8f77cb0` |

## 更新、退役与回退

- 依赖更新后先比较 DLL 时间/哈希，再检查四个目标方法和 `burstStartTick`、`warmupCarryTicks`、`storedWarmupTicks`、`lastStopTick` 字段、`coldTime` 语义，重跑 `Validation/run.ps1`。
- 若上游已接通 Idle UI 衰减和完整 Burst 结束捕获，并提供希望使用的五种武器余热规则，停用此补丁。
- 若作者保留重型/炮塔的“每次重新聚焦”设计，保留本补丁即可继续使用平滑衰减玩法。
- 回退：在模组列表停用并正常重启。原生字段负责保存充能数据；无自定义存档类型或新增存档字段。

## English summary

Enables Synthriada's existing linear `coldTime` charge carry for all five particle-beam systems, displays remaining idle activity and captures interrupted bursts after vanilla changes the verb to Idle. Uses native save fields and timing parameters, preserving safety-mode forced venting and overheat rules. The heavy weapon and Eye of Obsession originally opt out of carry in XML; the medium focuser already supports carry but its idle UI reads zero. The report separates that balance choice from the reproducible display/lifecycle faults.
