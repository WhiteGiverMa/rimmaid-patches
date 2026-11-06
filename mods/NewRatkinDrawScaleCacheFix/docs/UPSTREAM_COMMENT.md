# 给 NewRatkinPlus 作者的上游反馈 / Upstream report for NewRatkinPlus

可直接复制到 Steam 创意工坊讨论区或作者的联系渠道。两版内容相同，中文在前，英文在后。

---

## 中文版

**标题：1.7 并行预绘制下 GeneDrawScaleCache 的 Dictionary 并发损坏**

作者你好，感谢维护 NewRatkinPlus。这里报告一个 1.7 下的线程安全问题，附调用链、根因和最小改法。

**环境**

- RimWorld 1.7.5000 rev1275（Waylith / .NET 10）
- NewRatkinPlus 1.7 构建（Workshop `1578693166`）
- Harmony 1.7 构建
- 鼠族 xenotype 使用 `RK_Gene_SmallBody`（`GeneDrawScaleDef.drawScale = 0.8`）

**现象**

游戏运行中偶发红字，位置在 `NewRatkin.GeneDrawScaleCache`：

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

**根因**

`GeneDrawScaleCache` 用普通 `Dictionary<Pawn, float>` 缓存：

```csharp
private static readonly Dictionary<Pawn, float> drawScaleByPawn = new Dictionary<Pawn, float>();
```

1.7 的 `DynamicDrawManager.PreDrawThings` 是 `IJobParallelFor`，由 `WaylithJobRuntime` 的多个 worker 执行；`PawnRenderTree.ParallelPreDraw` 会在这些线程上调用节点的 `CanDrawNow`。
鼠族每只都带 `RK_Gene_SmallBody`，因此每只鼠族都有一个 `PawnRenderNodeWorker_DrawScale` 节点：

1. 两个 worker 同时为不同鼠族调用 `Get`；
2. 缓存未命中时都进入 `Recalculate`，同时写 `drawScaleByPawn[pawn]`；
3. bucket/entry 被并发写坏，.NET 抛出 `InvalidOperationException`；
4. 静态字典不会自动重置，后续查找可能继续抛异常或返回错误值。

主线程的 `Gene_DrawScale.PostAdd` / `PostRemove` 和 `Pawn_GeneTracker.Notify_GenesChanged` postfix 也会写这个缓存，所以即使只有一个渲染 worker，主线程与 worker 之间仍有同类竞争。

**最小改法**

给两个直接访问字典的方法加同一个可重入锁即可，缓存算法和返回值不变：

```csharp
private static readonly object DrawScaleCacheLock = new object();

internal static float Get(Pawn pawn)
{
    if (pawn == null) return 1f;
    lock (DrawScaleCacheLock)
    {
        if (drawScaleByPawn.TryGetValue(pawn, out float drawScale)) return drawScale;
        Recalculate(pawn, out drawScale);
        return drawScale;
    }
}

internal static bool Recalculate(Pawn pawn, out float drawScale)
{
    lock (DrawScaleCacheLock)
    {
        // 保留现有实现
    }
}
```

`Get` 内部再调用 `Recalculate` 时，`lock` 对同一线程可重入，不会死锁。也可以把字典换成 `ConcurrentDictionary<Pawn, float>`；如果同时想解决死亡 Pawn 长期留在字典里的问题，可考虑在 `Pawn.DeSpawn` 时移除，或改用 `ConditionalWeakTable` 一类的弱引用容器（注意 `ConditionalWeakTable` 不能直接枚举，且值需要是可引用类型）。

**验证边界**

我在本地用真实 NewRatkin.dll 做了离线压力验证：16 个线程各对 10,000 个唯一 Pawn 键调用 `Get`。

- 未打补丁：复现 `InvalidOperationException`，缓存 `Count` 远小于 160,000（多次运行在 339 / 10,002 等值）。
- 打补丁后：0 异常，`Count == 160,000`，`Get → Recalculate` 嵌套加锁正常完成。

这不是完整游戏内验证；我没有在你的开发环境里跑实际殖民地渲染。实际游玩中该错误只在 1.7 并行预绘制、缓存未命中或主线程同时改基因时出现，所以可能看起来偶发。

**未覆盖的相邻风险**

- `DrawScaleForPawn` 在渲染 worker 上读取 `pawn.genes.GenesListForReading`，主线程同时增删基因时仍可能竞争；本次报告的红字只来自字典。
- `ApplyScaleToRootVisualNodes` 会从 worker 写 `PawnRenderNode.debugScale`；如果同一棵树可能被多个线程处理，也值得复核。

再次感谢维护这个 mod。若你已经在 1.7 分支修过这一处，请忽略本报告。

---

## English version

**Title: GeneDrawScaleCache dictionary corruption under 1.7 parallel pre-draw**

Hi, thanks for maintaining NewRatkinPlus. This is a 1.7 thread-safety report with the call chain, root cause and a minimal fix.

**Environment**

- RimWorld 1.7.5000 rev1275 (Waylith / .NET 10)
- NewRatkinPlus 1.7 build (Workshop `1578693166`)
- Harmony 1.7 build
- Ratkin xenotype includes `RK_Gene_SmallBody` (`GeneDrawScaleDef.drawScale = 0.8`)

**Symptom**

An occasional red error points at `NewRatkin.GeneDrawScaleCache`:

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

**Root cause**

`GeneDrawScaleCache` caches into a plain `Dictionary<Pawn, float>`:

```csharp
private static readonly Dictionary<Pawn, float> drawScaleByPawn = new Dictionary<Pawn, float>();
```

In 1.7, `DynamicDrawManager.PreDrawThings` is an `IJobParallelFor` executed by multiple `WaylithJobRuntime` workers, and `PawnRenderTree.ParallelPreDraw` calls each node's `CanDrawNow` on those threads.
Every ratkin has `RK_Gene_SmallBody`, hence a `PawnRenderNodeWorker_DrawScale` node:

1. Two workers call `Get` for different ratkins at the same time.
2. Both miss the cache and enter `Recalculate`, writing `drawScaleByPawn[pawn]` concurrently.
3. The bucket/entry state is corrupted and .NET throws `InvalidOperationException`.
4. The static dictionary is never reset, so later lookups may throw again or return wrong values.

The main thread also writes this cache from `Gene_DrawScale.PostAdd` / `PostRemove` and the `Pawn_GeneTracker.Notify_GenesChanged` postfix, so the same race exists between the main thread and a render worker even with a single worker.

**Minimal fix**

Put both dictionary accesses under one reentrant lock; cache values and the algorithm stay unchanged:

```csharp
private static readonly object DrawScaleCacheLock = new object();

internal static float Get(Pawn pawn)
{
    if (pawn == null) return 1f;
    lock (DrawScaleCacheLock)
    {
        if (drawScaleByPawn.TryGetValue(pawn, out float drawScale)) return drawScale;
        Recalculate(pawn, out drawScale);
        return drawScale;
    }
}

internal static bool Recalculate(Pawn pawn, out float drawScale)
{
    lock (DrawScaleCacheLock)
    {
        // keep the existing implementation
    }
}
```

`lock` is reentrant on the same thread, so `Get` calling `Recalculate` will not deadlock. A `ConcurrentDictionary<Pawn, float>` would also work. If you also want to stop dead pawns from living in the dictionary forever, remove entries on `Pawn.DeSpawn` or use a weak-key container (note that `ConditionalWeakTable` is not enumerable and its value must be a reference type).

**Verification boundary**

I ran an offline stress test against the real NewRatkin.dll: 16 threads, 10,000 unique Pawn keys per thread, calling `Get`.

- Unpatched: reproduced `InvalidOperationException`; the dictionary `Count` was far below 160,000 (339 / 10,002 in repeated runs).
- Patched: zero exceptions, `Count == 160,000`, and the nested `Get → Recalculate` lock path completed.

This is not a full in-game colony test. The failure only appears during 1.7 parallel pre-draw, on cache misses or while the main thread changes genes, which is why it looks intermittent.

**Adjacent risks not covered by this report**

- `DrawScaleForPawn` reads `pawn.genes.GenesListForReading` on render workers; main-thread gene changes can still race with that read.
- `ApplyScaleToRootVisualNodes` writes `PawnRenderNode.debugScale` from workers; if the same tree can be processed by multiple threads, that deserves a separate check.

Thanks again for maintaining the mod. If this is already fixed on your 1.7 branch, please ignore this report.
