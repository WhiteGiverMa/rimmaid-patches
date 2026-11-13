# Synthriada 上游实装复核 — 2026-11-13

## 结论

工坊 `3835512735` 已发布更新，本机也已安装同一最新 Steam manifest。作者确认的两处活性 Bug 已在新版 `DMSSR.dll` 实装并通过独立离线检查；重型集束器与执迷之眼保持有意设计的停火清零。散布器已改成 12 束。失败结束后的炮塔完成回调仍有遗漏，详见下文。

`DMSSRSolitonChargeDecay` 1.0.0 已由用户停用并归档；不修改上游，不重新编译或替换补丁 DLL，也不删除游戏 Mods 中的历史安装副本。

## 远端页面与本机磁盘分开核验

页面与 API 于 2026-11-13 10:06–10:07 UTC 抓取：

- [工坊主页](https://steamcommunity.com/sharedfiles/filedetails/?id=3835512735&l=english)：`Updated Nov 13 @ 12:25am`，`8 Change Notes`，17.559 MB。
- [更新记录](https://steamcommunity.com/sharedfiles/filedetails/changelog/3835512735?l=english)：Nov 12 @ 7:30am 与 Nov 13 @ 12:25am 两条新记录。HTML 中的时间戳分别为 `1794497427` 与 `1794558316`，对应 **2026-11-12 15:30:27 UTC**、**2026-11-13 08:25:16 UTC**（页面按 PST 渲染）。
- Nov 12 说明涉及设施链接、FA 头部位置、手枪 DPS/启动时间、基因包/基因/合成体及移除 CE 兼容；Nov 13 说明为减少敌人持有孤立子武器概率、修复调高功率因子后机械体反复充电。更新文字未单独列出本次活性修复，实装情况由本机最新 `DMSSR.dll` 反编译核实。
- Steam `GetPublishedFileDetails` 返回 `result=1`，`time_updated=1794558316`、`file_size=17558643`。本机 `appworkshop_294100.acf` 的安装与最新 manifest 均为 **`161770291871422498`**，`timeupdated` / `latest_timeupdated` 与远端一致。
- [作者回复](https://steamcommunity.com/workshop/filedetails/discussion/3835512735/738187822665612482/#c738187822665619893)：HTML `data-timestamp=1794495196`，即 **2026-11-12 14:53:16 UTC**（PST 6:53:16am）：「1方法用错了，2没做边界情况保护，3是刻意改成的停火清零 / 我现在修一下」。

| 本机 DLL 基线 | 最后写入 UTC | 字节数 | SHA-256 |
|---|---|---:|---|
| 2026-11-12 调查时 | 2026-11-08 20:02:13.2405816 | 719360 | `d28367b8af9e44fe6354dd78b07082bdb10984ad0b8c93343f69e29660a2964d` |
| 2026-11-13 本次读取 | 2026-11-13 09:47:18.9376760 | 729088 | `19714ac9acd16d78d093fd7b973b5e5d05397794986e82abd89557f4a0fb1033` |

文件位于工坊目录 `1.7/Assemblies/DMSSR.dll`，本机最后写入对应北京时间 **2026-11-13 17:47:18**。

## 逐行对比 `Verb_MechParticleBeam`

用 ilspycmd 11.1.0.9782 完整反编译当前 DLL，与 2026-11-12 完整反编译结果逐行比对。下述行号指本次输出 `DMSSR.Verbs/Verb_MechParticleBeam.cs`。该类仅有以下四处变更，属性类 `VerbProperties_MechParticleBeam` 未变。

### 1. Idle 活性 UI：已修复

`GetChargePercent:2175–2183` 先读配置上限；Bursting 继续用 `GetFiringTicksRaw()`，非 Bursting 改为 `ComputeCarryTicks(Find.TickManager?.TicksGame ?? 0)`，结果除以上限并 `Clamp01`。

`GetDisplayChargePercent:2185–2210` 的原生平滑逻辑保留。聚焦器仍为 `beamSwitchFire=true`、`coldTime=120`。300/600 tick 的半活性停火后显示 50%，60 tick 后 25%，120 tick 后归零；独立驱动逐 tick 核对实际活性与显示一致。不再出现原来 9 tick 内 UI 清零、后台仍有储能的脱节。

### 2. 失败 Burst 捕获：两条入口已补，回调仍有遗漏

`StoreWarmupOnStop:312–328`：

- false 开关分支继续显式清零。
- true 且 `burstStartTick>=0` 时，用 `max(0, now-burstStartTick)+warmupCarryTicks`，再限制到 `0..GetConfiguredWarmupTicks()`，直接保存 `storedWarmupTicks` 与 `lastStopTick`；不再调用依赖 Bursting 的 `GetWarmupTicks()`。
- `Reset` 把 `burstStartTick` 设为 -1，所以重复 Idle Reset 不会重启衰减计时。

`WarmupComplete:857–868`：首次 `TryCastNextBurstShot()` 后，如果已不再 Bursting 且旧起点仍有效，立即 `Reset()`；只有继续 Bursting 才创建射击音效 sustainer。

`BurstingTick:871–880`：入口发现已非 Bursting 时，在旧起点有效的情况下 `Reset()`，然后返回。原版 `Verb.VerbTick` 在 `TryCastNextBurstShot()` 后同 tick 继续调用 `BurstingTick()`，所以正常的 `Available=false` 与 `TryCastShot=false` 结束路径当 tick 就完成捕获和清理。

独立驱动确认两种失败、首发失败、继承余量、重复 Reset、配置上限、负 elapsed、安全模式强制排空与成功发间隔均按新规则执行。

**剩余回调边界（已离线复现）**：

1. 原版 `Verb.TryCastNextBurstShot` 结束时先转 Idle 并调用 `castCompleteCallback`。
2. 新增失败清理随后调用 `DMSSR.Reset` → `Verse.Verb.Reset`；原版 `Reset:576–585` 会将 `castCompleteCallback=null`，本次上游没有保存/恢复它。
3. `Building_TurretGun.UpdateGunVerbs:690–698` 在设置 gun verbs 时绑定 `BurstComplete`；`BeginBurst` 不重新绑定。于是首个失败 Burst 能登记冷却，清理后同一 Verb 的后续 Burst 丢失该回调。
4. 驱动绑定真实 `Building_TurretGun.BurstComplete` 并使用原生 1 秒冷却：首个失败 Burst 写入 **60** ticks；将计数消耗至 0 后再次发起失败 Burst，仍为 **0**，验证了后续 Burst 完成冷却回调丢失。

由于炮塔（执迷之眼）未开启 carry 且手持孤立子武器不依赖炮塔 `BurstComplete`，该边界留档备查。

### 3. 重型与执迷之眼：刻意清零保留

`IsBeamSwitchFireEnabled:369–372` 仍为 `Props?.beamSwitchFire ?? false`；重型 `DMSSR_Soliton_Beam_Heavy` 与炮塔内部枪 `DMS_Gun_BeamLaser` 的 XML 仍未设置 true，实际为 **false**。手枪与散布器也保持 false，只有聚焦器保留 true/120。两种重型系统的 `coldTime` 缺省值仍可计算为 360 ticks，但不会因此自动启用 carry。

`StartSafetyCooldown:222–230` 仍在 Reset 后显式清空三个余量字段并锁定冷却，未被普通余量显示修复改变。

## XML 与三语文案

重新清点 104 个 Defs XML、85 个中/英/繁中语言 XML；对比旧的 104/79 清单中全部七个相关 Def 的 Verb XML、三语 label 和 description。相关描述均未变，Verb 参数只有三项差异：

| Def | 参数 | 旧 | 新 |
|---|---|---:|---:|
| `DMSSR_Soliton_Beam_Light` | `warmupExponentParam` | 300 | **180** |
| `DMSSR_Soliton_Beam_Light` | `fireRateExponentParam` | 1.5848931925 | **1.5** |
| `DMSSR_Soliton_Beam_Shotgun` | `beamCount` | 6 | **12** |

散布器中英繁文案保留「12 条 / 12 beams」，现已与 XML 一致；聚焦器、重型、炮塔的 Verb XML 与旧清单一致。

## 验证范围与归档说明

- 直接加载当前工坊版 `DMSSR.dll`（不加载退役补丁）与真实 `Assembly-CSharp.dll` 离线副本（`Verb`、`VerbTracker`、`Pawn_StanceTracker`、`Building_TurretGun` 的 **145 个非静态构造方法体逐指令保持原样**）。
- 驱动构建 **0 警告 / 0 错误**，**63 项检查通过**，涵盖真实捕获、状态转换、UI 衰减、Reset 与炮塔回调。
- 已验证补丁 DLL 保持 `116e1a68a316640e4670c9847ee52bc84b42d75c3be2c32f0bf46881e4083a99`。源码、发布二进制、115 项旧基线验证脚本及历史说明完整归档。
