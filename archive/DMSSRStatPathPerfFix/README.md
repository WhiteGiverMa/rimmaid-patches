# DMSSR Stat & Path Perf Fix / 合成体属性·寻路性能修复

**1.1.0** · RimWorld **1.7 / .NET 10** · `whitegiver.dmssr.statpathperffix`

为 [The Dead Man's Switch - Synthriada](https://steamcommunity.com/sharedfiles/filedetails/?id=3835512735) 减少全局属性、健康、寻路和预绘制钩子中的重复工作。依赖 Harmony 与 Synthriada，排在二者之后加载。只包含运行时补丁，不增加存档字段。

Reduces repeated work in Synthriada's global stat, health, pathing and pre-draw hooks. Requires Harmony and Synthriada; load after both. No save fields are added.

## 1.1.0 改动 / Changes

| 路径 / Path | 修复 / Fix |
|---|---|
| 启动、地形移动代价 / Startup and path cost | 在 `[StaticConstructorOnStartup]` 中先调用真实 `DMSSR.Misc.HarmonyBootstrap.EnsurePatched()`，再安装优化。原版实例重载的纯转发 IL 与 DMSSR 静态 Postfix 均核对后，才移除重复的实例 Postfix。修复 1.0.0 在 Mod 构造阶段过早安装、永久落入 cache-only 的问题。 |
| 机械体改造 / Mech customization | 共享缓存覆盖属性、能力、体型、电量上限、充电上下限、技能等级与 UI 等级。能力查询先筛 `Consciousness / Manipulation / Moving`，再筛机械体；其余直接返回。保留各入口原有数值计算与技能禁用规则。 |
| 精神分裂链接 / Schizophrenia link | 先筛 `Consciousness / Sight / Hearing`，再读取缓存的首个链接 Hediff。保留原版 0.5 意识下限、来源生物解析、视听转发与递归保护。 |
| 动态能力 / Dynamic capacity providers | 快照保存有序 `IDynamicCapacityProvider` 引用数组；没有 provider 时不再遍历 Hediff 或逐条获取 `DefHasProviderCache` 锁。有 provider 时仍实时读取倍率/偏移。保留 DMSSR 原 Prefix、线程局部深度字段、Postfix 顺序及 `finally` 平衡。 |
| 未完成物品绘制 / Unfinished-item drawing | DMSSR 的 `ThingWithComps.DrawAt / Print` Prefix 先做 `is UnfinishedThing` 判断。普通物品立即返回 `true`；未完成物品继续走原来的绑定工作台、地图与占地区域检查。此处原来已是常数时间，收益只是省一次调用，不是消除建筑扫描。 |
| 并行预绘制 / Parallel pre-draw | 只替换 Hydra `TryGetTransform` 和合成体 `ScaleFor` Postfix 内的具体扩展查询调用。按 `ThingDef` 使用 `ConcurrentDictionary` 缓存正/负结果，空扩展表直接返回，每线程 memo 加速相邻节点。保留原来的载具乘员解析、矩阵、根节点判断与缩放计算。 |

Installation now waits for DMSSR's actual Harmony bootstrap, so the single-clamp branch works in the real load order. All customization consumers share one health snapshot; irrelevant capacities return before health access. Schizophrenia links retain upstream forwarding/recursion behavior, and dynamic providers retain their live values and thread-local nesting rules. Ordinary things bypass unfinished-display ownership checks. Concrete render-extension call sites use concurrent per-def caches rather than globally patching `Def.GetModExtension<T>()`.

### 缓存与行为 / Cache and behavior

- 每 Pawn 不可变快照通过 `Volatile.Read/Write` 发布；`HediffSet.DirtyCache` 使其失效，并同时校验 `HediffSet` 引用和 `hediffs.Count`。并行读者可以重复构建，但不会看到半成品。Hediff 列表写入仍属于游戏模拟阶段。
- 缓存保存 comp、Hediff、provider 与扩展的**引用**，不冻结参数。改造倍率、provider 输出与扩展的缩放/倾斜字段仍实时读取；Def 扩展列表按加载后稳定的数据处理。
- 同 Def 多个 Hediff 仍以**第一个**为准；第一个没有 comp 时不会取后面的。provider 资格继续使用上游自身的按 Def 规则，锁只在快照重建时支付。
- 唯一有意改变的数值是地形豁免的重复扣减：夹具原始代价 200，地形代价 40，旧版扣两次得到 120；本版只扣一次得到 160。不会低于原来的每步移动下限。无法证明重载转发关系时保留两个上游 Postfix，只缓存扫描。
- 不修改 `MechCustomizationUtility.IsSupportedPawn` 或研究解锁逻辑；这些由独立的 `DMSSRLegionCustomizationCompat` 负责。

Snapshots are invalidated by `DirtyCache`, set identity and count. Cached objects remain live; only lookup/membership is cached. The intentional path-cost difference is **one terrain subtraction instead of two** (200 → 160, not 120 in the fixture). The original minimum movement cost is retained. Legion eligibility and research-unlock hooks are untouched.

## 验证 / Validation

2026-11-14 基线：

| 实际依赖 / Actual dependency | SHA256 |
|---|---|
| RimWorld 1.7 `Assembly-CSharp.dll` | `82387F78262461E53BB06A67C3945912B876D1FDB07235122BBB6E59D073F125` |
| Synthriada `DMSSR.dll` | `19714AC9ACD16D78D093FD7B973B5E5D05397794986E82ABD89557F4A0FB1033` |

```powershell
dotnet build .\Source\DMSSRStatPathPerfFix.csproj -c Release
pwsh -File .\Validation\validate.ps1
# 可选：对保存的 1.0.0 DLL 重放真实启动时序失败
pwsh -File .\Validation\validate.ps1 -BaselinePatchPath 'path\to\1.0.0\DMSSRStatPathPerfFix.dll'
```

`net10.0`，`TreatWarningsAsErrors=true`；Release 构建 **0 警告 / 0 错误**。

- **648 RED**：无本补丁时断言双重 clamp、各查询线性扫描、provider 逐条锁与渲染扩展重复查询。
- **784 GREEN**：包含 1.0.0 原有回归、36 组属性等价、全部新增热路径、首项语义、失效与同数量换表、provider 顺序/实时参数/嵌套/异常、链接视听、充电边界、禁用技能、未完成物品正反例、真实 `TryGetMatrix / ScaleFor` 与乘员分支。
- **3 alternate-order checks**：本补丁启动类先于 DMSSR 启动类运行，仍先完成真实 `EnsurePatched`，后续上游构造不恢复重复 clamp。
- 保存的 **1.0.0 DLL 另有 6 RED 检查**，确认 `Mod ctor → EnsurePatched → startup` 后仍为 `SingleClampInstalled=False`；1.1.0 相同顺序为 `True`。
- 并发覆盖：16 路共享健康快照读取、16,000 次 provider 调用、160,000 次乘员矩阵查询，以及共享 Def 冷/热扩展缓存。

驱动加载真实游戏、Harmony、DMSSR 与 Fortified DLL；只在 QA 副本中清空 `ModsConfig` 静态构造，避免配置 IO。真实 `EnsurePatched` 的 `PatchAll` 在离线驱动中限定到被测的 16 个上游补丁类，其他 Unity 内容初始化不运行。健康失效测试跳过 `DirtyCache` 的 Unity/身体缓存主体但保留 Postfix 信号，绘制夹具提供固定位置；生产代码没有这些替身。脚本核对被测 DLL 与指定发布/安装 DLL 的 SHA256。

The offline harness uses real assemblies and target methods. Its QA-only game copy disables config initialization, and the real bootstrap's `PatchAll` is scoped to the 16 relevant upstream patch classes. Unity-only leaf state is supplied by fixtures. It checks the requested runtime DLL's bytes, not just the project-reference build.

### 离线微基准 / Offline microbenchmarks

同进程交错 3 轮、每项 200,000 次，取最优（30 Hediff，渲染项目为 30 个无关扩展的 Def）：

| 被测钩子 / Hook | 上游 / Upstream | 1.1.0 |
|---|---:|---:|
| 无关改造能力 / Irrelevant customization capacity | 30.7 ns | 3.8 ns |
| 无关链接能力 / Irrelevant linked capacity | 31.2 ns | 3.8 ns |
| 没有动态 provider / No dynamic providers | 995.9 ns | 13.4 ns |
| 普通物品绘制 Prefix / Ordinary draw prefix | 4.9 ns | 4.0 ns |
| 非飞行生物矩阵 / Non-winged transform | 83.9 ns | 15.9 ns |
| 无缩放根节点 / Unscaled root | 75.0 ns | 5.7 ns |

## 更新与退役 / Updates and retirement

启动日志应包含：

```text
[DMSSR StatPathPerfFix] installed: pathCostSingleClamp=True pathCostCacheOnly=False
delegationVerified=True statGate=True mechCompCache=True dirtyCacheHook=True
mechHelpers=5 capacityCache=True renderCache=True
```

上游更新或游戏 DLL 改变后，先记录新 hash 并重跑验证；RED 断言不再成立可能表示上游已修好，不要直接更新预期值。重点核对 bootstrap、两个寻路重载、provider 的深度/排序规则、缓存生命周期与渲染查询调用形状。上游自行前置筛选/缓存/去重后可逐项退役；游戏跨大版本前需重新验证。运行时替换 Def 扩展列表或直接改 comp 列表却不发健康失效信号的模组不在当前缓存契约内。

Revalidate after upstream/game updates. A failing RED assumption can mean an upstream fix, not a broken test. Retire overlapping optimizations once upstream handles them. Runtime replacement of def-extension lists or comp-list mutation without a health invalidation signal requires a separate cache-lifecycle review.

## 给作者的反馈 / Author feedback

**中文**

> Cephalopod 你好！补充几处用真实 1.7 DLL 验证过的热点，供你优化时参考：
>
> 1. 两个 `CostToMoveIntoCell` Postfix 会在同一步重复施加 clamp；实例重载只是转发。只留静态重载即可，也能避免地形代价扣两次。我的旧补丁在 Mod 构造时检查得太早——你的 `EnsurePatched` 到启动静态构造阶段才运行——1.1.0 已修正自己的安装时机。
> 2. 改造属性和能力查询最好先判断 Stat/Capacity，再查改造 Hediff。体型、电量、充电阈值及两个技能等级入口也可以共用以 `DirtyCache` 为失效点的 comp 缓存。
> 3. `Patch_Schizophrenia_ConsciousnessFloor` 可先排除意识/视力/听力以外的查询；目前所有生物的其他能力也先扫描链接 Hediff。
> 4. `Patch_PawnCapacity_Calc` 会遍历每个 HediffWithComps，并逐个获取 `DefHasProviderCache` 的锁。按 Pawn 缓存 provider 引用列表，绝大多数没有 provider 的生物就不必支付这段工作。注意保留线程局部调用深度、嵌套行为与 finally。
> 5. 两个未完成物品绘制 Prefix 可以把 `is UnfinishedThing` 提到调用处；原 owner 方法已有这个判断，所以这里仅是小幅省调用。
> 6. Hydra 矩阵与合成体根节点缩放的扩展查询可按 ThingDef 缓存正/负结果，使用线程安全容器适配并行预绘制。Hydra 必须先解析挂载载具，再查载具 Def，不能按乘员自己的 Def 提前排除。
>
> 本地已做真实入口、正反例和并发回归验证。希望这些定位能帮上忙，谢谢你的模组！

**English**

> Hi Cephalopod! A few verified 1.7 findings that may be useful:
>
> 1. Both path-cost postfixes run for one step because the instance overload delegates to the static one. Keeping only the static postfix also avoids subtracting terrain cost twice. My old sidecar checked too early in its Mod constructor; 1.1.0 now waits for your real `EnsurePatched` bootstrap.
> 2. Check the requested stat/capacity before resolving customization. Body size, energy, recharge thresholds and both skill-level paths can share a `DirtyCache`-invalidated comp lookup.
> 3. Gate schizophrenia handling to consciousness/sight/hearing before looking up the link.
> 4. The dynamic-capacity postfix scans and locks per HediffWithComps. A per-pawn provider-reference list removes that work for provider-free pawns while retaining live adjustments, thread-local nesting and finally cleanup.
> 5. The unfinished-item drawing prefixes can inline the existing type gate; this is a small call-saving change, not a building-scan fix.
> 6. Cache positive and negative render-extension lookups per ThingDef using a thread-safe container. Hydra must resolve a mounted carrier first; filtering by the passenger's def would lose inherited transforms.
>
> Real-entry and concurrent regressions pass offline. Thanks for the mod!
