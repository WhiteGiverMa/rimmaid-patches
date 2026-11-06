# NewRatkin Draw Scale Cache Fix / 鼠族缩放缓存线程安全修复

RimWorld **1.7** 独立 Harmony sidecar，版本 **1.0.0**，packageId `whitegiver.newratkin.drawscalecachefix`。

依赖 Harmony 与 [NewRatkinPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=1578693166)（packageId `Solaris.RatkinRaceMod`）。本补丁不修改工坊文件、存档或 NewRatkinPlus 的 DLL。

## 症状

在 1.7 的多线程预绘制下，日志出现：

```text
Job <id> faulted with exception: System.InvalidOperationException:
Operations that change non-concurrent collections must have exclusive access.
A concurrent update was performed on this collection and corrupted its state.
The collection's state is no longer correct.
[Ref F279D16B]
   at System.Collections.Generic.Dictionary`2.set_Item(...)
   at NewRatkin.GeneDrawScaleCache.Recalculate(Pawn pawn, Single& drawScale)
   at NewRatkin.GeneDrawScaleCache.Get(Pawn pawn)
   at NewRatkin.PawnRenderNodeWorker_DrawScale.CanDrawNow(...)
   at Verse.PawnRenderNode.AppendRequests(...)
   at Verse.PawnRenderTree.ParallelPreDraw_Patch2(...)
   at Verse.PawnRenderer.ParallelGetPreRenderResults_Patch6(...)
   at Verse.DynamicDrawManager.PreDrawThings.Execute(Int32 index)
   at Waylith.Jobs.WaylithJobRuntime.HandleState.ExecuteRun()
```

失败的是 Waylith 的一个预绘制 job；日志只记录一次 `faulted`，但静态缓存本身已经被破坏。

## 根因

`NewRatkin.GeneDrawScaleCache` 用一个普通 `Dictionary<Pawn, float>` 做体型缩放缓存：

```csharp
private static readonly Dictionary<Pawn, float> drawScaleByPawn = new Dictionary<Pawn, float>();

internal static float Get(Pawn pawn)
{
    if (pawn == null) return 1f;
    if (drawScaleByPawn.TryGetValue(pawn, out float drawScale)) return drawScale;
    Recalculate(pawn, out drawScale);
    return drawScale;
}

internal static bool Recalculate(Pawn pawn, out float drawScale)
{
    // ...
    drawScaleByPawn[pawn] = drawScale;
    // ...
}
```

RimWorld 1.7 的 `DynamicDrawManager.PreDrawThings` 是 `IJobParallelFor`，由 `WaylithJobRuntime` 的多个 worker 线程执行；`PawnRenderTree.ParallelPreDraw` 会调用各节点的 `CanDrawNow`。NewRatkinPlus 的鼠族 xenotype 自带 `RK_Gene_SmallBody`，所以每只鼠族都有一个 `PawnRenderNodeWorker_DrawScale` 节点：

1. 两个 worker 同时为首帧/重建树后的不同鼠族调用 `Get`；
2. 两者都缓存未命中，同时进入 `Recalculate` 并写 `Dictionary`；
3. `Dictionary` 的 bucket/entry 被并发写坏，.NET 抛出 `InvalidOperationException`；
4. 静态字典不会重置，后续查找仍可能返回错误结果或继续抛异常。

主线程的 `PostAdd`、`PostRemove`、`Notify_GenesChanged` 也会写同一个缓存，因此即使只有一个渲染 worker，主线程与 worker 之间仍有同类竞争。

## 修复范围

只给两个直接访问该字典的方法加同一个 `Monitor`：

1. `NewRatkin.GeneDrawScaleCache.Get(Pawn)`：前缀 `Monitor.Enter`，finalizer `Monitor.Exit`。
2. `NewRatkin.GeneDrawScaleCache.Recalculate(Pawn, out float)`：同样前后加锁。

`Get` 在缓存未命中时会调用 `Recalculate`；`Monitor` 可重入，所以嵌套调用不会死锁。缓存值、基因判定、缩放计算、装备绘制与脏标记逻辑全部保持原样。`Recalculate(Pawn)` 只是转调 out 重载，不直接访问字典，因此不需要额外补丁。

正常启动应出现：

```text
[NewRatkin Draw Scale Cache Fix] Patched GeneDrawScaleCache.Get and Recalculate; dictionary access is serialized.
```

若 `NewRatkin.GeneDrawScaleCache` 或其两个方法签名不存在，补丁输出 `[NewRatkin Draw Scale Cache Fix]` 错误并放弃挂载，不会留下半套补丁。

## 安装

游戏关闭时，将本目录中的 `About/`、`Assemblies/` 和 `README.md` 部署为独立 `Mods/NewRatkinDrawScaleCacheFix`。不要把 `Source/`、`Validation/`、`bin/` 或 `obj/` 放进游戏。

加载顺序：Harmony → NewRatkinPlus → 本补丁。本补丁只支持 RimWorld 1.7。

## 构建与真实 DLL 验证

需要 .NET 10 SDK。`GameRoot`、`HarmonyPath`、`NewRatkinPath` 可由构建环境覆盖；构建不自动部署。

```text
dotnet build Source/NewRatkinDrawScaleCacheFix.csproj -c Release
pwsh -NoProfile -File Validation/validate.ps1 -QaDirectory "独立的临时 QA 目录"
```

`validate.ps1` 依次执行 `bind`、`red`、`green`：

- `bind` 只在指定 QA 目录生成 `Assembly-CSharp.dll` 与 `NewRatkin.dll` 的临时副本，把 UnityEngine 引用重绑到 `UnityStubs`；真实方法体不改，工坊与游戏安装文件不写。
- `red` 不挂载本补丁，16 个线程各自对 10,000 个唯一 `Pawn` 键调用真实 `GeneDrawScaleCache.Get`；要求复现并发破坏。本机结果：`InvalidOperationException`，缓存 `Count` 明显小于 160,000。
- `green` 挂载本补丁后跑同一压力；要求零异常、缓存 `Count == 160,000`，并确认两个目标方法各有一个锁前缀和一个锁 finalizer。`Get → Recalculate` 的嵌套加锁在 green 中完成，证明 `Monitor` 可重入。

测试用 Pawn 通过 `GetUninitializedObject` 创建，并写入共享 `ThingDef.defName` 与唯一 `thingIDNumber`，避免触发未初始化 Pawn 的 `Equals`；这是夹具边界，不代表游戏内实体。离线验证通过不等于游戏内渲染、读档和缩放视觉已全部验证。

## 游戏内浅测（2026-11-07）

补丁已部署并在 03:34:25 正常重启加载，日志出现注册行。03:36:24、03:36:39、03:38:29 三次从最小化恢复，和原始红字相同的触发路径，均未出现 `faulted with exception`，`GeneDrawScaleCache` 仅剩注册行。该错误是概率性竞争，短时间不复发不能证明所有场景已覆盖；按浅测观察通过记录，仍保留概率性验证口径。

## 基线、维护与退役

验证基线：

- RimWorld `1.7.5000 rev1275`，`Assembly-CSharp.dll` SHA-256 `d55aa8e47fa3c5463ca85b0eccd51f195b4bbcd04f56da3af21c2f6688a81254`；
- NewRatkinPlus 1.7 构建，`NewRatkin.dll` SHA-256 `D808EA4AFA60C64A33E3714385AAC67846E82B52A0727A236F4EECDD495A9FFE`；
- Harmony 1.7 构建，`0Harmony.dll` SHA-256 `C8EF7856B2BE3CF24B345C9B5C2F197F68FCF92649635064FCE7B3CCC8F77CB0`。

上游更新后必须复核 `GeneDrawScaleCache` 的类型名、`drawScaleByPawn` 字段类型与两个目标方法签名，并重跑真实 DLL 红绿。若上游把缓存改为 `ConcurrentDictionary`、统一加锁或不再在渲染线程访问该缓存，本 sidecar 可停用。停用不会改动存档或已加载的鼠族基因。

## 未处理 / 残余风险

- 本补丁只修报告中的 `Dictionary` 并发。`Gene_DrawScale.DrawScaleForPawn` 仍会在渲染 worker 上读取 `pawn.genes.GenesListForReading`；主线程同时增删基因时，这是另一条未覆盖的读写竞争，本次未观察到对应报错。
- `drawScaleByPawn` 从不移除死亡或离场 Pawn，长期存档会缓慢积累引用。这不是本次红字的原因，本补丁也不改变该行为。
- `ApplyScaleToRootVisualNodes` 会从 worker 写 `PawnRenderNode.debugScale`；同一棵树若被多个线程同时处理，仍有风险，本次未观察到。

给上游作者的根因与建议改法见 [`docs/UPSTREAM_COMMENT.md`](docs/UPSTREAM_COMMENT.md)。
