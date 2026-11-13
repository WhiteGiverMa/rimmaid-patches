# DMSE 世界防空性能修复 / DMSE World Air-Defense Perf Fix

`packageId: whitegiver.dmse.worldperffix` · RimWorld 1.7 · 纯运行时 Harmony 补丁 / pure runtime Harmony patch
适配 **The Dead Man's Switch - Expedition**（`Aoba.DeadManSwitch.Expedition`，Workshop `3689879335`）

> 本目录是补丁的源码与验证工作区。实际部署文件只有 `About/About.xml`、`LoadFolders.xml`、`1.7/Assemblies/DMSEWorldPerfFix.dll`、`README.md`。

---

## 0. TL;DR

在 100% 海洋覆盖、约 1,300 个据点（其中 1,028 个工业级以上）的星球上，DMSE 的世界层防空警戒会：

| 现象 | 原版（未打补丁） | 本补丁 |
|---|---|---|
| 一次只读的 BVR 节点重建后 `siteList` 里的状态数 | **1,052**（全部是和平基线空状态） | **0** |
| 每 250 tick 的 `WorldComponentTick` | **1.5–2.3 ms，约 235 KB 垃圾**（每次都一样） | 空表时 **0 B / O(1)**；清退 1,000 个空闲状态 **约 0.11 ms / 40 B** |
| `MissileDoctrine.For` 每次调用 | 88 B 闭包垃圾 | **0 B**（缓存命中） |
| `BuildNodes(activeOnly:true)`（每 15 tick） | 200 次 **15.7–16.3 ms** | 200 次 **0.36–0.37 ms**（约 43×） |
| `StageOf` / `RadarWarm` 结果 | 基准 | **1,300/1,300 完全一致，0 处偏差** |

---

## 1. 根因分析 / Root-cause analysis

### 根因 A：只读查询把全星球的和平据点写进 `siteList` 和存档

`WorldComponent_BVRNetwork.BuildNodes(nodes, activeOnly:false)` → `ApplyAlertStages(nodes)`：

```csharp
// DMSE 1.7, ApplyAlertStages
int num = current.StageOf(node.parent);
if (num > 0)
{
    node.alertStage = num;
    node.rangeFactor = AirDefenseAlertPolicy.DetectionFactor(num, current.RadarWarm(node.parent));
}
```

而上游实现是：

```csharp
public int StageOf(MapParent parent) => StateFor(parent)?.stage ?? 0;               // create 默认 true
public bool RadarWarm(MapParent parent)
{
    var s = StateFor(parent);
    return s == null || Now >= s.radarWarmUntil;                                    // create 默认 true
}
public AirDefenseSiteAlert StateFor(MapParent parent, bool create = true) { ... }
```

`StateFor(parent)` 的 `create` 参数默认是 **true**。于是每一次「读一下阶段」都会：

1. `new AirDefenseSiteAlert { siteId, parent }`；
2. `Reset(...)`：`baseline = BaselineFor(parent)`、`stage = baseline`、`local = regional = 0`、`radarWarmUntil = -1`、`directThreatUntil = -1`、`blockadeAt = -1`、`warnedStage = 0`、清空 `records` / `waves`；
3. `sites[id] = state`、`siteList.Add(state)`；
4. 因为 `siteList` 被 `Scribe_Collections` 序列化，这些空对象**永久进入 `.rws` 存档**。

触发点不止一处：`BuildNodes` 每 2,000 tick（以及任何 `MarkNodesDirty` 之后）都会重建节点；`AirspaceRestrictionUtility.Rebuild` 也会对每个节点调用 `StageOf`。在 1,300 据点的星球上，一次重建就会实例化 1,052 个状态（1,028 个据点和 24 个军事基地），其中绝大多数是中立/盟友阵营、离玩家几百格远的和平据点。

**关键观察：这些被创建出来的状态与「根本没有状态」在行为上完全等价。** 因为：

- `StageOf` 对无状态据点会新建一个 `Reset` 后的状态，其 `stage` 正是 `BaselineFor(parent)`；
- `RadarWarm` 对无状态据点返回 `true`，而新建状态的 `radarWarmUntil == -1` 也返回 `true`；
- `AirspaceRestrictionUtility` 只关心 `StageOf >= 5`，而基线只有 `1/2/3`（XML：`baselineNoRadar=1`、`baselineRadar=2`、`baselineMilitary=3`）；
- 警告/拦截/封锁需要 `stage >= 3`，而基线 `1/2` 的据点本来就不会触发；基线 `3` 的军事基地在 `NextStage` 中同样停在 3；
- `ReportObservation` / `ReportHit` / `DevInject` / `DevAddPressure` / `DevSetStage` / `AirDefenseAlertUI.InspectFor` 在真正需要状态时**仍然会调用 `StateFor(parent)`（create:true）**，状态会在那一刻按当时的 `Now` 重建；
- `AllStates` 的唯一消费者是开发者调试动作 `LogStates`。

### 根因 B：`Prune` 永远不会清退回到和平基线的状态

```csharp
private void Prune(int now)
{
    int keep = Mathf.Max(Tuning.attackWindowTicks, Tuning.salvoGroupTicks * 4) + 60000;
    for (int num = siteList.Count - 1; num >= 0; num--)
    {
        var s = siteList[num];
        var val = ParentOf(s);
        if (val == null || ...) { sites.Remove(s.siteId); siteList.RemoveAt(num); }
        else
        {
            s.records.RemoveAll(r => now - r.tick > keep);          // 每个据点 1 个闭包
            s.waves.RemoveAll(w => now - w.tick > attackWindow);    // 每个据点 1 个闭包
        }
    }
}
```

`Prune` 只在「父对象没了 / 阵营没了 / 变成玩家」时移除状态，**不会**移除「已经衰减回基线」的状态。于是根因 A 制造的 1,052 个空状态会永远留着：

- 每 250 tick：`Prune` + `EvaluateAll` 遍历全部 1,052 个据点；
- 每个据点分配 2 个 `RemoveAll` 闭包 → **2,104 个委托 / ≈235 KB**；
- `Evaluate` 还会对每个据点跑 `ParentOf`、`Decay`、`BaselineFor`（内部 `GetComponent<WorldObjectComp_AirDefense>()` 是 LINQ，每次分配一个迭代器）、`RadarCapable`（`PropsFor` → `MissileDoctrine.For`）。

这些据点什么都没发生，却每 250 tick 消耗 1.5–2 ms 并制造 235 KB 垃圾——在 60 TPS 下相当于每秒 4 次、约 940 KB/s 的纯垃圾。

### 根因 C：重复的线性扫描与闭包分配

```csharp
public static MissileDoctrineDef For(FactionDef def, IEnumerable<MissileDoctrineDef> candidates)
{
    var d = def.GetModExtension<MissileDoctrineExtension>()?.doctrine;
    if (d != null) return d;
    return candidates.FirstOrDefault(d => d.IsDefaultFor(def.techLevel));   // 每次一个闭包
}
```

`MissileDoctrine.For` 位于 `PropsFor` / `RadarCapable` / `BaselineFor` 的热路径上，每次 BVR 重建都会被调用上百次。

```csharp
private void BuildNodes(List<BVRNode> nodes, bool activeOnly)
{
    ...
    foreach (MapParent item2 in Find.WorldObjects.AllWorldObjects.OfType<MapParent>())   // 全部 1,342 个世界对象
    {
        ...
        var component2 = item2.GetComponent<WorldObjectComp_AirDefense>();             // 每个都查组件
        if (activeOnly && (!nodeActiveUntil.TryGetValue(item2.ID, out var v) || v < ticksGame)) continue;
        ...
        WorldComponent_SettlementDefense.Current?.DefenseFor(item2);                  // 每个都判 Eligible
    }
}
```

`activeOnly:true` 只在「有导弹/空投航迹的扫描窗口」内被每 15 tick 调用一次，但它每次都遍历全部世界对象，只为挑出 `nodeActiveUntil` 里的 1–3 个 id。`OfType<MapParent>()` 还会分配一个 LINQ 迭代器。

---

## 2. 补丁机制 / What this patch does

四个互相独立的 Harmony 补丁组（`PatchInstaller` 按名称解析 DMSE 类型，任何一组失败只跳过该组，不会让启动崩掉）：

### Fix 1 — `StageOf` / `RadarWarm` 不再创建状态
`Source/AirDefenseQueryPatches.cs`

```csharp
// StageOfPrefix
if (parent == null || parent.Destroyed || parent.Faction == null || parent.Faction.IsPlayer) { __result = 0; return false; }
var state = __instance.StateFor(parent, false);                 // 只读
__result = state != null ? state.stage : WorldComponent_FactionAirDefenseAlert.BaselineFor(parent);
```

- 对上游会拒绝的 parent（null / 已销毁 / 无阵营 / 玩家）→ `0`，与上游一致；
- 有状态 → 返回 `state.stage`（`StateFor(create:false)` 仍会执行上游的「阵营变了就 Reset」逻辑）；
- 无状态 → 返回 `BaselineFor(parent)`，**正是**上游新建状态会写入的 `stage`，但不创建、不入表、不存档。

`RadarWarmPrefix` 同理：有状态返回 `Now >= state.radarWarmUntil`，无状态返回 `true`（新建状态的 `radarWarmUntil == -1` 也返回 `true`）。

### Fix 2 — 零闭包 `Prune` + 静止状态自动休眠
`Source/AirDefenseTickPatches.cs`

- `WorldComponentTick` 前缀：`siteList.Count == 0` → O(1) 返回（实测 0 B）；否则保持上游 `now % 250 == 0 && Enabled` 的节奏。
- `PruneCore`：用倒序 `for` 循环替代两个 `RemoveAll`，**0 闭包**；然后对每个状态做「100% 静止」判定：

```csharp
state.faction == parent.Faction
&& records.Count == 0 && waves.Count == 0
&& local <= 0 && regional <= 0
&& blockadeAt < 0 && stage < 5
&& now > directThreatUntil && now >= radarWarmUntil
&& warnedStage == 0
&& stage == baseline && baseline == FastBaselineFor(parent)
```

  满足即从 `siteList`/`sites` 休眠移除；不满足则保留。
- 之后照常调用上游私有的 `EvaluateAll(now)`，**剩余（活跃）据点仍然每 250 tick 评估一次，顺序不变、次数不变**——没有做任何「分片/延迟评估」，因此不存在语义漂移。
- `FastBaselineFor` 是 `BaselineFor` 的零分配镜像：上游 `WorldObject.GetComponent<T>()` 在 1.7 里是 LINQ（每次分配迭代器），所以这里直接遍历 `parent.AllComps`；唯一无法用缓存回答的情况（据点自己拥有地图，即玩家正在上面）回退到原方法，保证那部分仍是原版行为。
- `Prune` 本身也加了同样的前缀（防御性）：如果其他补丁/未来版本直接调用 `Prune`，拿到的也是同一套逻辑。

### Fix 3 — 教义缓存 + 活动扫描节点索引
`Source/DoctrineCachePatches.cs`、`Source/BvrScanPatches.cs`

- `MissileDoctrine.For(FactionDef, IEnumerable<MissileDoctrineDef>)`：只有当 `candidates` 就是 `DefDatabase<MissileDoctrineDef>.AllDefsListForReading` 时才查缓存；缓存未命中时带重入保护调用**原方法**（教义选择逻辑不复制），所以任何自定义候选序列仍然走原版。缓存按 `FactionDef` 建，`DefCount` 或 `Find.World` 变化时整体失效。
- `BuildNodes(nodes, activeOnly:true)`：先按原顺序构建 `Find.Maps` 的本地节点，然后经 `id → (MapParent, AllWorldObjects 下标)` 索引只解析 `nodeActiveUntil` 里未过期的 id，按世界对象下标排序后处理。索引只在「holder/list 实例变化、元素数量变化、或命中项的下标发生位移」时重建；过期的 id（对象已删除）直接跳过，不触发重扫。随后仍调用原 `ApplyAlertStages` 与 `EWEnvironment.NodeJam`，输出**逐元素一致**。
- `activeOnly:false`（每 2,000 tick 的完整重建）保持原版，交给 Fix 1 消除副作用。

---

## 3. 为什么是 100% 行为等价 / Why this is behaviour-preserving

| 上游路径 | 补丁后 | 等价性 |
|---|---|---|
| `StageOf(parent)`（无状态） | `BaselineFor(parent)` | 上游会 `Reset` 一个状态，`Reset` 里 `stage = BaselineFor(parent)` |
| `RadarWarm(parent)`（无状态） | `true` | 上游 `Reset` 后 `radarWarmUntil = -1` ⇒ `Now >= -1` ⇒ `true` |
| `AirspaceRestrictionUtility` | 只读 `StageOf >= 5` | 基线最大 3（`baselineMilitary`），永不影响封锁圈 |
| `AllStates` | 少掉的全是基线空状态 | 唯一消费者是 `LogStates` 调试动作，它自己也会 `Decay` 一个空状态 |
| `ReportObservation` / `ReportHit` / `DevInject` / `DevAddPressure` / `DevSetStage` / `InspectFor` | 不变（仍 `StateFor(create:true)`） | 真实事件发生的那一刻按当时的 `Now` 重建状态，`lastDecayTick = now`、压力为 0，与「保留一个空状态」完全一致 |
| 休眠判定 | 只有 `stage == baseline`、无记录/波次/压力/计时器/警告时才移除 | 这种状态下 `Evaluate` 只会执行 `Decay()`（0 → 0）与 `NextStage()`（`current == baseline`、`threat == 0` ⇒ 返回基线本身），不会 `SetStage`，不广播，不封锁 |
| `BuildNodes(activeOnly:true)` | 元素与顺序相同 | GREEN 验证逐元素字符串比对通过（含 `parent.ID`、`Label`、阵营、防御类型、`alertStage`、`rangeFactor`、`jamEff`） |
| `MissileDoctrine.For` | 缓存值 = 首次调用原方法的结果 | 教义 def 在运行期不变；`DefCount` / 世界变化即失效 |

**不写任何自定义存档字段。** 移除补丁后，被休眠的状态会在下一次 `StageOf` / 观测时按原版逻辑重新创建；已经清掉的空状态本来就不承载信息。

---

## 4. 离线验证 / Offline validation

`Validation/` 是一个独立的 `net10.0` 控制台工程，**不使用游戏进程、不加载 XML**，直接绑定真实的
`Assembly-CSharp.dll`（1.7，仅清空 `ModsConfig..cctor` 以避免读写用户配置）与真实的
`DMSE.dll` / `DMS.dll` / `Fortified.dll`（仅把 `UnityEngine*` 引用重定向到游戏自带的 `UnityStubs.dll`；
工坊 DLL 本体不被修改）。世界夹具是真实的 `World` + 真实注册的 `PlanetLayer`（`WorldGrid.RegisterPlanetLayer`）
+ 1,300 个真实 `Settlement`：

```
0     .. 1027  1,028 个工业/太空/超凡据点（有 settlementDefense，基线 2）
1028  .. 1271    244 个部落据点（无雷达，基线 1）
1272  .. 1295     24 个军事基地（真实 WorldObjectComp_AirDefense，基线 3）
1296  .. 1297      2 个玩家据点
1298               1 个已销毁据点
1299               1 个无阵营据点
```

构建与运行：

```bash
cd Validation
dotnet build -c Release

cd bin/Release/net10.0
# 1) 绑定（生成 bound/ 下的 QA 副本）
dotnet Validation.dll bind "A:/SteamLibrary/steamapps/common/RimWorld" \
  "A:/SteamLibrary/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll" \
  "A:/SteamLibrary/steamapps/workshop/content/294100/3689879335/1.7/Assemblies/DMSE.dll" \
  "../../../../../1.7/Assemblies/DMSEWorldPerfFix.dll"

# 2) RED：未打补丁，证明缺陷存在
dotnet Validation.dll red   <同上四个参数>
# 3) GREEN：先在同一进程里采集原版基准，再装补丁并逐项比对
dotnet Validation.dll green <同上四个参数>
```

`green` 的流程是：**先**在未打补丁的世界上采集 `StageOf`/`RadarWarm`/`BuildNodes`/`MissileDoctrine` 的原版结果，
**再**安装补丁、新建同样布局的世界逐项比对。因此 GREEN 不是「自己和自己比」，而是与真实上游对照。

### 实测结果（本机，2026-11-13）

RED（未打补丁）：

```
BuildNodes(activeOnly:false) materialised 1052 AirDefenseSiteAlert objects
of those, 1052/1052 are 100% idle baseline states
WorldComponentTick at t=250 over 1052 idle sites: 2.267 ms, 235,224 B allocated
second 250-tick pass: 231,040 B allocated, siteList still 1052
1,000 x MissileDoctrine.For(industrial): 88,000 B allocated
200 x BuildNodes(activeOnly:true): 15.701 ms
OK: 12/12 checks passed
```

GREEN（已打补丁）：

```
GREEN cold prune of the 1052-state polluted save: 1.434 ms, 5,720 B (one-time JIT/reflection warm-up)
GREEN warm prune of 1,000 idle states: 0.107 ms, 40 B
GREEN idle-world tick with an empty siteList: 0 B, 0 B, 0 B
GREEN StageOf/RadarWarm parity over 1300 settlements: 0 mismatches
baseline distribution (stage 0..5): 0=4, 1=244, 2=1028, 3=24, 4=0, 5=0
G3 created 1290 idle + 6 active states (1296 in siteList)
G3 after one tick: 6 states remain
G4 after 7 targeted injections: stage 4, L=100, R=70, waves=7, blockadeAt=4996
G4 after blockade notice delay: stage 5, blockadeActive=True
G4 after decay + 12 ticks: siteList=0
active scan rebuild: same count (3), identical BVRNode sequence
patched 200 x active scan rebuild: 0.363 ms (upstream 15.946 ms)
GREEN 10,000 x MissileDoctrine.For(industrial): 96 B
OK: 40/40 checks passed
```

覆盖的 GREEN 断言：

1. 一个 tick 清退 1,052 个污染状态（含 24 个基线 3 的军事基地），`sites` 同步清空；
2. 1,300 个据点的 `StageOf` / `RadarWarm` 与上游 **0 偏差**，且不新增任何状态；
3. 1,290 个空闲状态 + 6 种活跃状态（压力 / 波次 / 待封锁 / 雷达暖机 / 直接威胁 / 已警告）混合时，**只**清退空闲的 1,290 个；
4. `DevAddPressure` → stage 3；`DevSetStage` → 雷达暖机窗口生效并在 600 tick 后恢复；`DevInject("targeted")` → 支援半径内（约 1 格）的友军据点被唤醒、半径外（约 40 格）保持冷状态；7 次注入 → 波次累计 → stage 4 + 待封锁 → 5,000 tick 后 stage 5 封锁生效 → 衰减 + 12 个评估周期后全部休眠；
5. `BuildNodes(activeOnly:true)` 的输出与原版逐元素一致（含过期 id、已销毁 id、不存在的 id）；
6. 教义缓存值与上游一致；10,000 次调用仅 96 B（首次解析）；
7. 重复 `ApplyPatches()` 幂等。

计时数字会随机器负载有约 ±20% 波动，上表为一次代表性运行；完整日志在
`C:\Users\A1337\AppData\Local\Temp\opencode\dmse-world-perf-20261113\{red,green,green-deployed}.log`。

---

## 5. 安装 / 兼容 / 存档安全

- 需要 Harmony（`brrainz.harmony`）与 DMSE（`Aoba.DeadManSwitch.Expedition`）。
- `loadAfter`：Harmony、`Aoba.DeadManSwitch.Core`、`AOBA.Framework`、`Aoba.DeadManSwitch.Expedition`。
- 不含 XML、不含贴图、不改工坊文件、不写 `ModsConfig.xml`。
- **存档安全**：补丁不新增任何 scribed 字段，只移除「基线空状态」。旧存档第一次载入后的第一个 250 tick 会一次性清退历史遗留的污染状态（实测 1,052 个约 2 ms，含一次性的 JIT/反射预热），之后稳态为零。
- 与其它 DMSE 补丁兼容：所有 Harmony 目标都是 DMSE 的非虚方法；`Prune` 同时被前缀保护，避免第三方直接调用绕过。

---

## 6. 补丁何时可以删除 / Expiration conditions

本补丁只在下列条件**全部**成立时才需要；任意一条被上游修复后即可删除：

1. `WorldComponent_FactionAirDefenseAlert.StateFor` 的 `create` 仍然是默认 `true`，且 `StageOf` / `RadarWarm` 仍以默认参数调用它（根因 A）；
2. `Prune` 仍然不会移除 `stage == baseline`、无记录/波次/压力/计时器/警告的状态，且 `WorldComponentTick` 仍然每 250 tick 无条件 `EvaluateAll`（根因 B）；
3. `MissileDoctrine.For(FactionDef, IEnumerable<MissileDoctrineDef>)` 仍然每次 `GetModExtension` + `FirstOrDefault`，且 `BuildNodes(activeOnly:true)` 仍然遍历 `AllWorldObjects`（根因 C）；
4. `AirDefenseAlertTuningDef` 的基线仍是 `baselineNoRadar=1`、`baselineRadar=2`、`baselineMilitary=3`（本补丁的「基线最大 3、永不参与 `stage >= 5` 封锁」论证依赖它；`Validation` 会在启动时断言这组值）；
5. `WorldComponent_FactionAirDefenseAlert` 的 `siteList` / `sites` / `neutralWarned` / `ParentOf` / `EvaluateAll` / `NotifyBlockade` 成员名仍然存在（`PatchInstaller` 会逐项探测，缺失则跳过对应补丁组并在日志里说明，不会崩溃）。

如果 DMSE 更新后日志出现：

```
[DMSE World Perf Fix] skipped '<...>': MissingMethodException...
```

说明该组补丁已失效（通常是上游改名或重构），此时补丁仍在跑但那一组不再生效——可以直接删除本补丁并向上游反馈。

---

## 7. 给作者的留言草稿 / Friendly message draft for the mod author

### 中文

> Aoba 你好，
>
> 我在一个 100% 海洋覆盖、约 1,300 个据点（1,028 个工业级以上）的星球上做性能分析时，发现世界层防空警戒有三处可以零行为代价修掉的开销，冒昧整理如下，希望能对后续版本有帮助：
>
> 1. **只读查询会创建状态**：`StageOf` / `RadarWarm` 都调用 `StateFor(parent)`，而 `create` 默认是 `true`。`WorldComponent_BVRNetwork.ApplyAlertStages` 每次重建节点都会对全星球每个有防空的据点调用它们，于是把上千个和平据点实例化成 `AirDefenseSiteAlert` 并写进 `siteList`/存档。改成 `StateFor(parent, create:false)`、无状态时返回 `BaselineFor(parent)` / `true` 即可，结果与新建状态完全一致（`Reset` 后 `stage = baseline`、`radarWarmUntil = -1`）。
> 2. **`Prune` 不会清退回到基线的状态**：所以上面那些空状态会永远留着，每 250 tick 被 `Prune + EvaluateAll` 全量扫一遍，每个据点还分配 2 个 `RemoveAll` 闭包。可以在 `Prune` 里加一个「静止」判定（无 records/waves、压力为 0、无封锁/直接威胁/暖机/警告、`stage == baseline`）并休眠移除；真正的观测/攻击仍会通过 `StateFor(create:true)` 立刻重建。
> 3. **两个小热点**：`MissileDoctrine.For` 每次 `GetModExtension` + `FirstOrDefault` 闭包，可以按 `FactionDef` 缓存（`DefDatabase` 变化时失效）；`BuildNodes(activeOnly:true)` 每 15 tick 遍历全部世界对象只为挑 `nodeActiveUntil` 里的几个 id，可以维护一个 id→MapParent 索引。
>
> 我写了一个独立的离线验证工程（真实 `Assembly-CSharp.dll` + 真实 `DMSE.dll`，1,300 据点夹具），RED/GREEN 对照确认：1,052 个空状态、每 250 tick 约 235 KB 垃圾、活动扫描 16.3 ms/200 次，分别降到 0、0 B、0.37 ms/200 次，且 `StageOf`/`RadarWarm` 在 1,300 个据点上 0 偏差、`BuildNodes` 输出逐元素一致。补丁源码与验证都在 `DMSEWorldPerfFix` 里，欢迎直接取用或改写成上游实现。
>
> 感谢你们做的模组，防空警戒这套系统在设计上很有意思。

### English

> Hi Aoba,
>
> While profiling a 100% globe-coverage world with ~1,300 settlements (1,028 Industrial+), I found three world-layer air-defense costs that look fixable with zero behaviour change:
>
> 1. **Read-only queries create state.** `StageOf` / `RadarWarm` call `StateFor(parent)` whose `create` parameter defaults to `true`. `WorldComponent_BVRNetwork.ApplyAlertStages` calls both for every settlement with air defense on every node rebuild, so a single rebuild instantiates ~1,000 peaceful settlements as `AirDefenseSiteAlert` objects and scribes them into `siteList`/the save. Passing `create:false` and returning `BaselineFor(parent)` / `true` is exactly what a fresh `Reset` state would have produced (`stage = baseline`, `radarWarmUntil = -1`).
> 2. **`Prune` never retires a site that decayed back to its baseline.** Those empty states therefore stay forever, and every 250 ticks `Prune + EvaluateAll` walks all of them, allocating two `RemoveAll` closures per site. A quiescence check (empty records/waves, zero pressure, no blockade/direct threat/warm-up/warning, `stage == baseline`) plus hibernation would remove them; real observations still recreate the state through `StateFor(create:true)` at the moment they happen.
> 3. **Two smaller hot spots.** `MissileDoctrine.For` re-runs `GetModExtension` + a `FirstOrDefault` closure on every call (cacheable per `FactionDef`, invalidated when the DefDatabase changes), and `BuildNodes(activeOnly:true)` walks every world object every 15 ticks just to pick the few ids in `nodeActiveUntil` (an id→MapParent index would do).
>
> I built a standalone offline harness (real `Assembly-CSharp.dll` + real `DMSE.dll`, 1,300-settlement fixture). RED/GREEN shows 1,052 idle states, ~235 KB per 250 ticks and 16.3 ms/200 active scans dropping to 0, 0 B and 0.37 ms/200, with 0 mismatches for `StageOf`/`RadarWarm` across all 1,300 settlements and an element-by-element identical `BuildNodes` output. The patch source and the harness live in `DMSEWorldPerfFix` — feel free to lift any of it into upstream.
>
> Thanks for the mod; the air-defense alert design is genuinely interesting.

---

## 8. 目录结构 / Layout

```
DMSEWorldPerfFix/
├─ About/About.xml
├─ LoadFolders.xml
├─ README.md
├─ 1.7/Assemblies/DMSEWorldPerfFix.dll      ← 部署产物
├─ Source/
│  ├─ DMSEWorldPerfFix.csproj               (net10.0, TreatWarningsAsErrors)
│  ├─ WorldPerfFixMod.cs                    (入口 / StaticConstructorOnStartup)
│  ├─ PatchInstaller.cs                     (按名称解析 + 逐组安装)
│  ├─ AirDefenseQueryPatches.cs             (Fix 1)
│  ├─ AirDefenseTickPatches.cs              (Fix 2)
│  ├─ DoctrineCachePatches.cs               (Fix 3a)
│  └─ BvrScanPatches.cs                     (Fix 3b)
└─ Validation/
   ├─ Validation.csproj
   ├─ Program.cs                            (bind / probe / red / green)
   ├─ WorldFixture.cs                       (真实 World + 1,300 Settlement 夹具)
   └─ Tests.cs                              (RED 12 项 / GREEN 40 项断言)
```
