# 给 Cephalopod 的反馈草稿

建议发到 [Synthriada bug report](https://steamcommunity.com/workshop/filedetails/discussion/3835512735/567050417882807085/)。

## 中文

你好！查了 1.7 版孤立子武器停火活性的代码，有两个小问题和一个想确认的设计点，供参考：

1. **聚焦器的实际余量与 UI 不一致。** `DMSSR_Soliton_Beam` 的 XML 已设 `beamSwitchFire=true`、`coldTime=120`；`Verb_MechParticleBeam.StoreWarmupOnStop` 会保存活性，`ComputeCarryTicks` 会线性衰减并在下次 `WarmupComplete` 接回。但 `GetChargePercent` 仍通过 `GetFiringTicksRaw` 取值，后者在非 Bursting 状态直接返回 0。于是停火后 UI 在最多 9 tick 内掉光，真实余量还在。Idle 时让 UI 读取 `ComputeCarryTicks(now)` 就能显示实际冷却进度。

2. **射击失败结束 Burst 时可能丢失储能。** 原版 `Verse.Verb.TryCastNextBurstShot` 在 `Available` 或 `TryCastShot` 失败后先置 `state=Idle`，不调用派生类 `Reset`。之后 `StoreWarmupOnStop -> GetWarmupTicks -> GetFiringTicksRaw` 因 Idle 得到 0，即使打开 `beamSwitchFire` 也留不住活性。建议在结束 Burst 时统一捕获余量，或用 `now-burstStartTick+warmupCarryTicks` 捕获最后区间，避免依赖已经改变的 state；保留原来的聚焦上限与安全冷却的显式清零。

3. **重型/炮塔是否有意停火清空？** `DMSSR_Soliton_Beam_Heavy` 和执迷之眼内部 `DMS_Gun_BeamLaser` 都未设 `beamSwitchFire`，所以 `StoreWarmupOnStop` 会明确清零三个字段；手枪与散布器也是如此。聚焦器则明确启用。请问这个差异是平衡设计还是漏配？现有 `GetColdTimeTicks` 已提供重型/炮塔 360 tick、手枪/散布器 60 tick 的默认冷却，若希望它们也保留余量，可以直接复用。

我用实际 DMSSR.dll 与游戏程序集做了离线调用验证：已复现聚焦器“carry>0、Idle charge=0”以及失败 Burst 后捕获到 0；本地 sidecar 接通余量/UI/结束捕获后，115 项检查通过，包含真实炮塔禁止开火、切目标和安全模式强制冷却。游戏版本为 1.7.5005 rev1082，DMSSR.dll SHA-256 `d28367b8af9e44fe6354dd78b07082bdb10984ad0b8c93343f69e29660a2964d`。

另有一个文案差异：散布器描述写 12 条能束，目前 XML 是 `beamCount=6`。谢谢维护，孤立子武器的持续聚焦机制很有意思！

## English

Hi! I checked the 1.7 soliton activity code and found two small issues, plus a design question:

1. **The medium focuser's idle UI disagrees with its retained charge.** `DMSSR_Soliton_Beam` already specifies `beamSwitchFire=true` and `coldTime=120`. `Verb_MechParticleBeam.StoreWarmupOnStop` saves its activity, and `ComputeCarryTicks` linearly decays it for the next `WarmupComplete`. However, `GetChargePercent` still reads `GetFiringTicksRaw`, which returns zero whenever the verb is not Bursting. The UI therefore empties within at most 9 ticks even while carry remains. Reading `ComputeCarryTicks(now)` for idle display would expose the actual cooling progress.

2. **A failed shot can lose charge when the burst ends.** Vanilla `Verse.Verb.TryCastNextBurstShot` sets `state=Idle` when `Available` or `TryCastShot` fails, without calling the derived `Reset`. A later `StoreWarmupOnStop -> GetWarmupTicks -> GetFiringTicksRaw` then captures zero because the state is already Idle, even with `beamSwitchFire` enabled. Capturing on every burst-end path, or calculating the final interval from `now-burstStartTick+warmupCarryTicks` independently of the changed state, would retain the intended charge. The existing warmup cap and safety cooldown's explicit venting can stay intact.

3. **Is instant clearing on the heavy weapon and turret intentional?** Neither `DMSSR_Soliton_Beam_Heavy` nor the Eye of Obsession's internal `DMS_Gun_BeamLaser` sets `beamSwitchFire`, so `StoreWarmupOnStop` explicitly clears the three carry fields. The pistol and disperser behave the same way, while the medium focuser opts in. Is that a balance choice or a missing Def setting? `GetColdTimeTicks` already supplies 360-tick cooling for the heavy/turret and 60 ticks for the pistol/disperser, so their native decay parameters could be reused if carry is desired.

I reproduced the focuser's “carry > 0, idle charge = 0” mismatch and the failed-burst zero capture using the actual DMSSR.dll and game assembly in an offline driver. A local sidecar connecting carry, display and burst-end capture passes 115 checks, including real turret hold-fire/retarget paths and safety-mode forced cooling. Game version: 1.7.5005 rev1082; DMSSR.dll SHA-256: `d28367b8af9e44fe6354dd78b07082bdb10984ad0b8c93343f69e29660a2964d`.

One additional text mismatch: the disperser description says 12 beams, while its XML currently sets `beamCount=6`. Thanks for maintaining the mod—the sustained-focus beam mechanic is a great idea!
