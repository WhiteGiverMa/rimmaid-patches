# DMS 军团指挥官与侦察性能修复 / DMS Legion Commander Perf Fix

| 项目 / Item | 值 / Value |
| --- | --- |
| packageId | `whitegiver.dmsl.commanderperffix` |
| 版本 / Version | `1.0.0` |
| 支持 / Supported | RimWorld `1.7`（本机验证 `1.7.5005 rev1082`） |
| 依赖 / Dependencies | Harmony `brrainz.harmony`；FFF `AOBA.Framework`；Legion `Srxf.DeadManSwitch.Legion` |
| 构建 / Build | `Release`, `net10.0`, `TreatWarningsAsErrors=true`，0 warnings / 0 errors |
| 类型 / Type | 独立运行时 Harmony 补丁；不修改上游 DLL/XML；不写任何存档字段 |

> 纯性能优化，不增加设置、物品或玩法。本次部署不修改 `ModsConfig.xml`；若mod列表中尚未启用，请手动启用。`loadAfter` 已保证它位于 Harmony、FFF 与 Legion 之后。
>
> Pure performance work: no settings, items, gameplay additions or scribed state. Deployment does not edit `ModsConfig.xml`; enable the mod manually if it is not already enabled. `loadAfter` places it after Harmony, FFF and Legion.

---

## 1. 症状 / Profiled symptom

用户的 Dubs Performance Analyzer `Harmony Patches` 页面中，`The Dead Man's Switch - Legion` 合计 **2.656 ms/frame**，前三项为：

| 上游补丁 / Upstream patch | 耗时 / Time |
| --- | ---: |
| `CommanderAutonomy_SubjectOverseer:Prefix` | 1.272 ms |
| `CommanderAutonomy_State:Prefix` | 1.060 ms |
| `AXF12ObservedMap_CheckRemoveMapNow_Patch:Prefix` | 0.167 ms |

DPA 还给前两项标出了 `(tick, update, other threads)`：Waylith 的 `PawnRenderer` 并行预绘制会读取 `CompOverseerSubject`，因此不能只做一个“主线程碰巧能用”的 Dictionary 缓存。

DPA also explicitly marks the commander prefixes as executing on tick, update and other threads: Waylith parallel pre-draw queries `CompOverseerSubject`. The replacement therefore has to be reader-thread-safe, not merely fast in a single-threaded test.

---

## 2. 根因 A：指挥官标记与 Overseer 查询 / Root cause A

### 2.1 非指挥官机兵也反复查 comps / Non-commanders repeatedly walk comps

当前 Legion XML 中只有 `DMSL_Mech_Commander` 的 `ThingDef.comps` 含 `CompProperties_MechCommanderMarker`。但是：

```csharp
CompMechCommanderMarker.PawnHasMarker(pawn)
    => pawn.GetComp<CompMechCommanderMarker>() != null;
```

`CommanderAutonomy_SubjectOverseer`、`CommanderAutonomy_State`、`CommanderAutonomy_Overseer` 等补丁会对**每一种机兵**调用它。真实 RimWorld 1.7 的 `ThingWithComps.GetComp<T>` 虽然有 `compsByType`，但 `CompMechCommanderMarker` 不是 sealed；字典没有该类型时仍会落入尾部循环。于是一只带 20 个 comp 的普通机兵，每次 State/Overseer 查询都要做一次 marker miss + 20 次类型判断。

Although 1.7 has a `compsByType` index, the marker class is non-sealed. A missing marker therefore falls through to the final linear loop. Every ordinary mechanoid pays that miss before Legion discovers that commander autonomy is irrelevant.

### 2.2 非自律指挥官每次 FindOverseer 都分配闭包 / Closure garbage

`FindOverseer` 找到一个 Overseer relation 后调用 `HasAssignment`。后者用两层捕获 `subject` 的 `GenCollection.Any` predicate 扫描 control groups。实际 RED 测量为 **160 B/call**；这不是偶发 GC 噪声，而是每次调用的嵌套闭包。

For a non-autonomous commander, every `FindOverseer` can allocate the nested `HasAssignment` predicates. The measured real-IL baseline is 320,000 B over 2,000 calls, or 160 B/call.

### 2.3 修复 / Fix

1. 从 `DefDatabase<ThingDef>.AllDefsListForReading` 动态构造“含 `CompProperties_MechCommanderMarker` 的 ThingDef”不可变 `HashSet`；`DefCount` 改变时重建。因此未来新增或 XML patch 出来的指挥官 def 也自动支持，不写死唯一 defName。
2. 非 marker def 在 O(1) 集合查询后立即返回，**不调用 `GetComp`**。marker def 的真实 `CompMechCommanderMarker` 用 `ConditionalWeakTable<Pawn, CompMechCommanderMarker>` 缓存；冷路径仍调用真实 `GetComp`，不伪造 comp。
3. 直接替换 `PawnHasMarker`、`IsAutonomous`、`FindOverseer`、`HasAssignment`、`GetWorkMode`、`GetRechargeThresholds` 的昂贵实现；后两者继续读取原 comp 的 `PersonalWorkMode` / `PersonalRechargeThresholds`。
4. 精确摘除并替换 Legion 在 `CompOverseerSubject.get_State`、私有 `get_Overseer`、`MechanitorUtility.GetOverseer` 上的三个 Prefix。普通机兵不再支付多层上游 utility/Harmony 调用，指挥官则走同样的设置、Biotech、存活、阵营与 assignment 守卫。
5. `FindOverseer` / `HasAssignment` 使用索引 `for` 循环；无 LINQ、无捕获闭包、无枚举器分配。

The fix builds the marker-def set dynamically, caches actual comps weakly per pawn, and replaces closure predicates with indexed loops. It also replaces the three hot prefixes themselves, so non-commanders avoid both the comps walk and the old utility-call chain.

---

## 3. 根因 B：世界每 tick 的 1,300 次组件扫描 / Root cause B

原版 `MapParent.CheckRemoveMapNow()` 首先用 `if (!HasMap) return;` 离开；但 Harmony Prefix 在原方法之前运行。Legion 的 Prefix 先求值：

```csharp
AXF12ReconMissionManager.Instance?.ObservedMapParent == __instance
```

`Instance` 又执行 `Current.Game.GetComponent<AXF12ReconMissionManager>()`，线性扫描 `Game.components`。因此世界每 tick 对约 1,300 个没有地图的 settlement 也各扫一次，vanilla 的早退完全来不及生效。

### 修复 / Fix

- `AXF12ReconMissionManager.get_Instance` 使用按 `(Game, List<GameComponent>, manager)` 发布的原子快照；冷路径的第一次查找保持 `Game.GetComponent<T>` 的“第一个 `is T`”语义，暖路径只做两次引用比较。
- `CheckRemoveMapNow` 的替换 Prefix 直接做 `ReferenceEquals(cachedManager?.ObservedMapParent, __instance)`。这是 O(1)，并且**不依赖 `HasMap`**；即使观察到异常 `HasMap == false` 的 `ObservedMapParent`，上游“阻止移除”的判断也保持不变。
- 更换 `Current.Game` 或 components 列表引用会失效重建；返回 null game 时与上游一样得到 null，并释放旧快照。

The manager is resolved once per Game/components-list identity, then published as one immutable volatile snapshot. The observed-map guard becomes two pointer reads and retains its answer independently of `HasMap`.

---

## 4. 安装顺序与回滚 / Installation order and rollback

Legion 在 `[StaticConstructorOnStartup] DMS_Legion.HarmonyPatches` 中执行 `new Harmony("DMS_Legion").PatchAll()`。本补丁自己的 `[StaticConstructorOnStartup]` 入口先对其执行：

```csharp
RuntimeHelpers.RunClassConstructor(typeof(DMS_Legion.HarmonyPatches).TypeHandle);
```

所以即使 RimWorld 先轮到本补丁，上游 PatchAll 也一定先完成。替换只针对四个已核实的上游 Prefix 方法：`Unpatch(original, exactPatchMethod)`，绝不 `UnpatchAll("DMS_Legion")`。上游若已移除对应补丁，本补丁会跳过而不是凭空引入旧行为。安装失败时仅移除自己的 replacement，保留上游实现。

GREEN 不是手工模拟这条链路：它实际运行 Legion 的 `HarmonyPatches` / `PatchAll`，然后调用正常的 `CommanderPerfFix.Apply(true)`，确认 11 组补丁全部安装。

The real GREEN run executes Legion's full `PatchAll` before the normal `Apply(true)` path. Only the four exact hot prefixes are replaced; unrelated Legion patches are untouched.

---

## 5. Waylith 线程安全论证 / Waylith thread-safety proof

1. **Def 集快照不可变**：`HashSet<ThingDef>` 发布后不再写入；`Volatile.Read/Write` 发布整个快照。只有 `DefCount` 改变才进入 `lock`，锁外暖路径没有共享可变字典写操作。
2. **CWT 原生支持并发**：`ConditionalWeakTable` 的读取与 `GetValue` 是线程安全的；非捕获 static callback 只有一个进程级委托。多个冷读者即使同时进入，也从同一只已初始化的 pawn 得到同一个 comp，并由 CWT 仲裁唯一值。弱键不延长 pawn 生命周期。
3. **查询只读**：`FindOverseer` 与 `HasAssignment` 只读 upstream 本来就会读取的 `DirectRelations`、`controlGroups`、`AssignedMechs`；本补丁不写这些列表，也不新增后台生产者。Waylith 的工作线程只能读取已经完成 comp 初始化的 pawn，主线程仍按 RimWorld 原规则负责结构变更。
4. **侦察缓存原子发布**：snapshot 的三个字段均 readonly，先完整构造，再 `Volatile.Write`；读者不会看到半初始化 tuple。冷读者最多重复一次只读扫描，结果相同，没有“部分初始化 manager”。
5. **实证**：16 个 LongRunning 工作线程同时读取非指挥官、指挥官、冷 CWT pawn 与观察地图。自律阶段 320,000 次迭代、非自律阶段 320,000 次迭代均为 **0 errors / 0 mismatches**。

Immutable def snapshots, CWT, read-only indexed loops and a single volatile recon snapshot avoid shared-dictionary mutation and torn publication. Both 16-thread phases completed 320,000 iterations with zero errors and zero result mismatches. As with upstream, structural pawn/list mutation remains a main-thread responsibility.

---

## 6. 离线 RED/GREEN 结果 / Offline RED/GREEN results

验证工程编译时引用真实 `Assembly-CSharp.dll`、`0Harmony.dll`、`DMS_Legion.dll`。运行时先加载指定的、已禁用 `ModsConfig` 静态构造的 bound game DLL，再加载私有 bound Legion。私有 game 副本只额外加入 `GetComp<T>` / `GetComponent<T>` 入口计数器；游戏目录、工坊 DLL 与 `ModsConfig.xml` 均不修改。

The harness uses the real managed IL. Its private copy of the specified config-safe `Assembly-CSharp.dll` adds QA-only entry counters; no installed/Workshop assembly or user config is written.

### 输入指纹 / Input fingerprints

| 文件 / File | SHA256 |
| --- | --- |
| RimWorld 1.7.5005 `Assembly-CSharp.dll` | `82387F78262461E53BB06A67C3945912B876D1FDB07235122BBB6E59D073F125` |
| Legion `DMS_Legion.dll` | `00AFEC1D5F2B22210E7CCD2FC502D88BC97711155AC160D39FBC1034C85D51EA` |
| Harmony `0Harmony.dll` | `C8EF7856B2BE3CF24B345C9B5C2F197F68FCF92649635064FCE7B3CCC8F77CB0` |
| 指定 config-safe bound base | `20FB0D97A55AB0568870BA3CCF6CBC28B861952B39AC82276A81DA81D6DCEBF4` |

### RED — 13/13 PASS

- 20-comp 非指挥官上，100 次 State + 100 次 Overseer 产生 **300 次 marker-miss GetComp**，即至少 6,000 次 comp 类型判断。
- 非自主指挥官 2,000 次 `FindOverseer`：**320,000 B = 160 B/call**。
- 1,300 个 settlement 的 `CheckRemoveMapNow`：**恰好 1,300 次** `Game.GetComponent`。
- 观察据点确实有已注册 Map；上游 Prefix 仍能阻止进入 `Settlement.ShouldRemoveMapNow`。

### GREEN — 41/41 PASS

- 真实 Legion `PatchAll` → 本补丁正常 `Apply(true)`：11/11 安装；四个旧 Prefix 精确移除，新 Prefix 均存在。
- 自律：`State == Overseen`、`Overseer == null`；关闭自律：`Overseer == assignedMechanitor`、真实 vanilla State 仍为 `Overseen`；普通机兵结果不变。
- 非指挥官 200,000 轮 × 5 种查询：**0 B、0 GetComp、0 mismatches**。
- 非自主指挥官 200,000 轮 × 5 种查询：**0 B、0 GetComp、0 mismatches**。
- 世界首轮 1,300 次调用：**总共 1 次列表扫描**；第二轮：**0 次**。观察地图仍未进入移除流程。
- 新 ThingDef 触发 `DefCount` 重建后自动识别；components 列表引用替换会正确失效。
- 两个 16 线程阶段合计 640,000 次迭代：**0 errors / 0 mismatches**；重复 Apply 幂等。

完整日志 / Full logs:

`C:\Users\A1337\AppData\Local\Temp\opencode\dmsl-commander-perf-20261114\`

> 这些毫秒数只是离线托管夹具，不冒充游戏帧时间。游戏内改善应在下一次正常启动后用同一 DPA 页面复测。
>
> Offline timings are not claimed as in-game frame times. Re-profile the same DPA page after the next normal launch.

### 复现 / Reproduce

```powershell
dotnet build -c Release G:\dev\rimmaid-patches\mods\DMSLCommanderPerfFix\Source\DMSLCommanderPerfFix.csproj
dotnet build -c Release G:\dev\rimmaid-patches\mods\DMSLCommanderPerfFix\Validation\Validation.csproj
$v = 'G:\dev\rimmaid-patches\mods\DMSLCommanderPerfFix\Validation\bin\Release\net10.0\Validation.dll'
dotnet $v bind
dotnet $v red
dotnet $v green
```

---

## 7. 何时过期或卸载 / Expiration conditions

**上游更新后必须重跑 RED/GREEN / always revalidate after updates:**

- `DMS_Legion.dll` 更新，尤其 `PawnHasMarker`、`IsAutonomous`、`FindOverseer`、`HasAssignment`、三个 Overseer prefix、`AXF12ReconMissionManager.Instance` 或 `CheckRemoveMapNow` 的守卫/顺序/签名变化。
- RimWorld 更新改变 `ThingWithComps.GetComp`、`Pawn.IsColonyMech`、relations/control-group 结构、`Game.components` 或 `MapParent.CheckRemoveMapNow`。
- 缺失方法或预期上游 Prefix 时，本补丁会记录 skipped 并保持上游行为；不要把“部分启用”当作完整验证。

**应当直接退役 / remove when:**

- 作者在正式版中实现等价的 def/comp 缓存、零闭包 assignment 查询和按 Game 的 AXF12 manager 缓存；本补丁便不再必要。
- 另一个 mod 在运行时向**非 marker ThingDef** 动态注入 marker comp，或在 worker thread 增删 pawn comps/relations/control groups。本优化与上游一样要求查询期只读，并依赖 def 声明识别 marker；这种新写入模型需要重新设计。
- 另一个 mod 在第一次世界查询后原地增删 `Game.components` 而不更换列表引用。正常 RimWorld 在 Game 初始化/读档期间完成组件列表；若引入运行时增删，应为本缓存增加相应失效通知。

**存档安全 / Save safety:** 源码没有任何 `Scribe_*` 调用；缓存均为瞬时内存结构。中途添加或移除不会留下自定义存档字段，移除只恢复上游性能行为。

No custom save fields are scribed. Adding or removing the patch mid-save leaves no patch-specific data behind; removal simply restores upstream performance behaviour.

---

## 8. 给 DMS Legion 作者的友好反馈草稿 / Friendly Chinese feedback draft

> 你好！很喜欢军团的指挥官自主性和 AXF-12 侦察玩法。我在 1.7 的 Dubs Performance Analyzer 里看到 Legion 的 Harmony Patches 合计约 2.656 ms/frame，追了一下调用链，发现有两个可以低成本消除的热点。我做了独立 Harmony 补丁验证，**没有改动你的任何 DLL/XML**，供你参考：
>
> 1. 目前只有 `DMSL_Mech_Commander` 声明 `CompProperties_MechCommanderMarker`，但 `PawnHasMarker` 对每种机兵都 `GetComp<CompMechCommanderMarker>()`。1.7 的 `GetComp` 对 non-sealed 类型的字典 miss 仍会走 comps 循环，所以普通机兵的 `CompOverseerSubject.State/Overseer` 也会反复扫描。可以从 DefDatabase 预建 marker ThingDef 集 + 每 pawn 缓存 comp；如果 1.7 代码里确认该类不需要继承，把 `CompMechCommanderMarker` 标成 sealed 也能让字典 miss 直接返回，是一个更小的上游改法。
> 2. 关闭自主性时，`FindOverseer` → `HasAssignment` 的两层 `Any` predicate 每次都会分配捕获闭包。我这里实测 160 B/call；换成普通双层 `for` 后结果相同且 200,000 轮为 0 B。
> 3. `AXF12ObservedMap_CheckRemoveMapNow_Patch` 在 vanilla 的 `HasMap` 早退前调用 `AXF12ReconMissionManager.Instance`，导致无地图据点也每个都扫描 `Game.components`。按 `Current.Game` 缓存 manager 后直接 `ReferenceEquals(manager?.ObservedMapParent, __instance)` 即可；1,300 据点首轮只查 1 次，之后 0 次，观察地图保护不变。
>
> 我用真实 RimWorld 1.7 `Assembly-CSharp.dll` 和当前 `DMS_Legion.dll` 做了离线 RED/GREEN：13 项 RED 复现、41 项 GREEN 全过，另做了 Waylith 场景的 16 线程两阶段压力测试，0 异常、0 结果差异。如果你愿意，我可以把补丁源码、验证工程和详细日志发给你，或按你的项目格式整理 PR。谢谢你的持续更新！

---

## 9. 最小游戏内浅测 / Minimal in-game smoke test

1. 正常启动游戏，在mod列表启用本补丁并确认它位于 Legion 后；加载现有存档。
2. 打开 DPA 的同一 `Harmony Patches` 页面，确认旧的 `CommanderAutonomy_SubjectOverseer`、`CommanderAutonomy_State`、`AXF12ObservedMap_CheckRemoveMapNow_Patch` 热点消失，并做一次普通观察/指挥官查看。

无需专门改设置、读档重写或生成测试物品。代理无法自行执行游戏 GUI；在用户提供该浅测结果前，状态仅为“已部署 + 离线验证通过”。

Enable after Legion, load an existing save, and re-check the same DPA page plus one commander/observation view. Until the user reports that smoke test, the honest status is **deployed and offline-validated**, not user-confirmed in-game.

---

## 10. 目录 / Layout

```text
DMSLCommanderPerfFix/
├─ About/About.xml
├─ LoadFolders.xml
├─ README.md
├─ 1.7/Assemblies/DMSLCommanderPerfFix.dll
├─ Source/
│  ├─ DMSLCommanderPerfFix.csproj
│  ├─ CommanderPerfFix.cs          # ordered install + exact replacement/rollback
│  ├─ CommanderMarkerCache.cs      # immutable def set + CWT
│  ├─ CommanderPatches.cs          # autonomy/Overseer fast paths
│  └─ ReconManagerCache.cs         # per-Game manager + O(1) removal guard
└─ Validation/
   ├─ Validation.csproj
   ├─ Program.cs                   # private binding + QA counters
   ├─ Fixture.cs                   # real pawns/comps/relations/Game/1,300 settlements
   └─ Tests.cs                     # 13 RED + 41 GREEN assertions
```
