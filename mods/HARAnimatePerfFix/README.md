# HAR 动画缓存性能修复 / HARAnimate Perf Fix

| 项目 / Item | 值 / Value |
| --- | --- |
| packageId | `whitegiver.haranimate.perffix` |
| 版本 / Version | 1.0.0 |
| 支持 / Supported | RimWorld 1.7（Waylith / 1.7.5005 rev1082 实测） |
| 依赖 / Depends on | Harmony (`brrainz.harmony`)、HAR BodyPart Animate FrameWork (`dtrndg.haranimate`, Steam `3761190956`) |
| 目标 / Target | `HARAnimate.Animation.AnimatorCache` / `AnimatorSystem` / `AnimatorCacheCleaner` |
| 类型 / Type | 纯 Harmony 运行时补丁；不改 DLL/XML，不写存档 / runtime Harmony only, no DLL/XML edits, nothing scribed |
| 加载顺序 / Load order | `loadAfter: dtrndg.haranimate`, `erdelf.HumanoidAlienRaces`, `brrainz.harmony` |

上游 `About/About.xml` 的 packageId 已实机核对为 **`dtrndg.haranimate`**（Steam 创意工坊 `3761190956`，DLL 为 `Assemblies/HARAnimate.dll`，1.0.0.0，sha256 `cbeb177c…26ac26e`）。

The upstream `About/About.xml` packageId was verified on disk as **`dtrndg.haranimate`** (Steam Workshop `3761190956`, assembly `Assemblies/HARAnimate.dll`, version 1.0.0.0, sha256 `cbeb177c…26ac26e`).

---

## 1. 症状 / Symptom

装了 HAR 身体部件动画（耳朵呼吸、尾巴摆动、翅膀抖动等）之后，即使只有十几个小人，帧率也会随着"有动画的节点数 × 帧数"线性下滑；GC 每帧都会有稳定的小额分配，长时间游戏后表现为周期性卡顿与 GC 峰值。Dubs Performance Analyzer 之类的工具会把热点指到 `HARAnimate.Animation.AnimatorCache.Get` 和 `AnimatorSystem.Evaluate`。

With HAR body-part animation (ear breathing, tail flicking, wing sway…) installed, frame time grows linearly with *animated node count × frame rate* even with a dozen pawns, and the GC sees a steady trickle of small allocations every frame — periodic hitches and GC spikes in long sessions. Profilers point at `HARAnimate.Animation.AnimatorCache.Get` and `AnimatorSystem.Evaluate`.

---

## 2. 根因 / Root cause

### Bug 1 — 每个节点每次查询都装箱 32 字节的缓存键 / a 32-byte boxed key on every node lookup

`Source/Animation/AnimatorCache.cs`（与 `Assemblies/HARAnimate.dll` 反编译结果逐行一致）：

```csharp
public struct CacheKey            // 注意：没有实现 IEquatable<CacheKey>
{
    public int pawnId;
    public int addonId;
    public string label;

    public override int GetHashCode() { ... }
    public override bool Equals(object obj) { ... }
}

internal static readonly ConcurrentDictionary<CacheKey, CacheEntry> cache = new();
```

`CacheKey` 只重写了 `object.Equals` / `object.GetHashCode`，**没有实现 `IEquatable<CacheKey>`**。因此
`ConcurrentDictionary<CacheKey, CacheEntry>` 的 `EqualityComparer<T>.Default` 解析成
`ObjectEqualityComparer<CacheKey>`：`GetHashCode(T)` 与 `Equals(T,T)` 都通过 `object` 重载调用，
**每一次 `TryGetValue`/`GetOrAdd` 都会把这个 24 字节的结构体装箱成 32 字节的堆对象**（一次哈希 + 每个桶比较各一次）。`GetOrAdd(key, factory)` 又比"先 `TryGetValue` 命中就返回"多做一次委托路径判断。

`CacheKey` overrides `Equals(object)`/`GetHashCode()` but does **not** implement `IEquatable<CacheKey>`. `EqualityComparer<T>.Default` therefore resolves to `ObjectEqualityComparer<CacheKey>`, which calls the `object` overloads and **boxes the 24-byte struct into a 32-byte heap object** on every `TryGetValue`/`GetOrAdd` (once for the hash, once per bucket comparison). `GetOrAdd(key, factory)` also does more work than a plain `TryGetValue` fast path.

### Bug 2 — 每个节点每 4 tick 拼接一次 64 字节字符串 / a 64-byte string concatenation per node every 4 ticks

`Source/Animation/AnimatorSystem.cs`：

```csharp
public static BodyAddonTransform Evaluate(Pawn pawn, PawnDrawParms parms, string label, int tick)
{
    string race = pawn.def.defName;
    string key = race + ":" + label;          // ← 每次查询都新建字符串
    if (!stacks.TryGetValue(key, out var stack))
        return BodyAddonTransform.Identity;
    ...
}
```

`stacks` 是 `Dictionary<string, AnimatorStack>`，键是 `"race:label"`。为了查一次字典，每帧每个身体部件都要分配一个新字符串（`"ValidationRace" + ":" + "Ear"` = 18 字符 → 26 + 2×18 = **64 字节**，实测正好 64.0 B/call）。这个拼接在 `AnimatorCache` 命中缓存刷新时每 `CacheRefreshTick`（默认 4 tick）发生一次，且与渲染节点数成正比。

`stacks` is keyed by `"race:label"`, so every lookup builds a fresh string just to probe the dictionary: 18 chars → 26 + 2×18 = **64 bytes**, measured at exactly 64.0 B/call. It happens once per `CacheRefreshTick` (default 4) per node, scaling with node count.

### Bug 3 — 每 30,000 tick 复制整个缓存并打一条空日志 / a full cache copy plus a no-op log line every 30,000 ticks

`Source/Animation/AnimatorCacheCleaner.cs` + `AnimatorCache.Cleanup`：

```csharp
foreach (var kv in cache.ToArray())        // ← 把整个 ConcurrentDictionary 复制成数组
    if (kv.Value.lastUpdateTick < minValidTick) cache.TryRemove(kv.Key, out _);

Log.Message($"[HARAnimate]Cache cleanup: {before} -> {after} (removed {before - after})");
```

`ConcurrentDictionary` 本身支持无锁枚举，`ToArray()` 纯属多余；而 1.7 的 `Log.Message` 走 `Brutal.Logging.LogCategory.Info` + `LogString` 插值处理器，会带上 **15 帧完整调用栈**落盘。即使 `removed == 0`，每 30,000 tick（正常速度约 8.3 分钟）也会写一条带栈的日志。

`ConcurrentDictionary` enumerates lock-free, so `ToArray()` is pure waste; and in 1.7 `Log.Message` goes through `Brutal.Logging.LogCategory.Info` with a full **15-frame stack trace**, even when `removed == 0` — one stack-trace log line every 30,000 ticks (~8.3 min at normal speed).

### Bug 4 — 刷新时 24 字节结构体会被撕裂读 / torn 24-byte struct reads during refresh

```csharp
entry.slot = slot;                     // ← 先写 slot
entry.lastUpdateTick = tick;
entry.value = AnimatorSystem.Evaluate(...);   // ← 后写 24 字节 value
```

`entry` 是**没有加锁**的普通对象。1.7 的并行预绘制里，两个渲染线程可能同时命中同一个节点：线程 A 刚把 `slot` 写成新值、`value` 还没写完，线程 B 通过 `entry.slot == slot` 检查后直接读 `entry.value` —— 读到的是**半新半旧的 `Vector3`/`float`/`Vector2` 组合**。`Vector3` 是 12 字节，不是单次原子写，因此这不是理论问题。离线压力测试在未打补丁的上游实现上每次运行都能复现 **约 2,500 ~ 4,000 次**落在错误值上的读取（见 §5）。

`entry` is a plain object with no lock. In 1.7 the pre-draw phase runs on multiple worker threads, so two threads can hit the same node: thread A has written the new `slot` but not the 24-byte `value`, thread B passes the slot check and reads a half-written `Vector3`/`float`/`Vector2` mix. `Vector3` is 12 bytes, not an atomic store, so this is a real hazard. The offline stress test reproduces **roughly 2,500–4,000** wrong reads on unpatched upstream on every run (see §5).

### 额外的崩溃点 / Bonus crash

`int slot = tick / HARAnimateSettings.CacheRefreshTick;` 没有保护。设置界面滑条最小值是 1，但手改配置文件成 `0` 会让渲染线程直接 `DivideByZeroException`。本补丁把它钳制为 1。

`tick / HARAnimateSettings.CacheRefreshTick` has no guard. The settings slider bottoms out at 1, but a hand-edited config with `0` makes the render thread throw `DivideByZeroException`. This fix clamps it to 1.

---

## 3. 修复机制 / Mechanism

四个替换式实现 + 七个 Harmony 补丁，全部保持原方法的签名、返回值与副作用：

| # | 目标 / Target | 类型 / Kind | 作用 / Effect |
| --- | --- | --- | --- |
| 1 | `AnimatorCache.Get(Pawn, PawnDrawParms, int, string, int)` | prefix → `false` | 整体替换为 `FastAnimatorCache.Get`：`ConcurrentDictionary<FastCacheKey, FastCacheEntry>` + `TryGetValue` 快路径 + 每条目 `lock (entry)` 刷新。 |
| 2 | `AnimatorCache.Cleanup(int, int)` | prefix → `false` | 直接在 `ConcurrentDictionary` 上 `foreach`，不再 `ToArray()`。 |
| 3 | `AnimatorCache.CleanAll()` | prefix → `false` | 清空新缓存，保留原 `[HARAnimate]Cache clear` 日志。 |
| 4 | `AnimatorCache.CacheCount()` | prefix → `false` | 返回新缓存条目数，让其他模组/调试工具读到的语义不变。 |
| 5 | `AnimatorCacheCleaner.CacheCleaner()` | prefix → `false` | 相同的 30,000 tick 节流 + 15,000 tick 过期；只在 `Prefs.DevMode` **且确有删除**时打日志。 |
| 6 | `AnimatorSystem.Register(string, AnimatorStack)` | prefix + postfix | 把 `"race:label"` 镜像进 `ConcurrentDictionary<StackLookupKey, AnimatorStack>`，不发日志、不改上游字典。prefix 与 postfix 都写，容错其他 prefix 抢先吞掉原方法。 |
| 7 | `AnimatorSystem.Evaluate(Pawn, PawnDrawParms, string, int)` | prefix → `false` | 用 `new StackLookupKey(pawn.def.defName, label)` 直接查镜像字典（**零分配**）；未注册则返回 `BodyAddonTransform.Identity`，与上游一致。 |

### 3.1 为什么键类型必须是 `IEquatable` / why the key must implement `IEquatable`

```csharp
internal readonly struct FastCacheKey : IEquatable<FastCacheKey>
{
    internal readonly int PawnId;
    internal readonly int AddonId;
    internal readonly string? Label;
    // Equals: string.Equals(Label, other.Label, StringComparison.Ordinal)
    // GetHashCode: StringComparer.Ordinal.GetHashCode(Label)（null 单独处理）
}
```

实现了 `IEquatable<T>` 之后，`EqualityComparer<T>.Default` 走 `GenericEqualityComparer<T>`，键永远留在栈上 —— 这就是 RED 的 98 B/call 变成 GREEN 的 0 B/call 的关键。相等性语义与上游逐位一致：`null` 与 `""` 在这里**不相等**（上游是 `label == other.label`）。

With `IEquatable<T>`, `EqualityComparer<T>.Default` resolves to `GenericEqualityComparer<T>` and the key never leaves the stack — this is what turns RED's 98 B/call into GREEN's 0 B/call. Equality matches upstream bit for bit: `null` and `""` stay distinct here (upstream used `label == other.label`).

### 3.2 栈查找键 / stack lookup key

```csharp
internal readonly struct StackLookupKey : IEquatable<StackLookupKey>
{
    internal readonly string Race;   // null 归一化为 ""
    internal readonly string Label;  // null 归一化为 ""
}
```

`null` 与 `""` 归一化为同一个值，因为上游 `race + ":" + label` 对两者都得到 `"race:"`。

镜像同时保留两份：
* `ByRaceAndLabel`（快路径）—— 注册时按**唯一一个** `':'` 切分得到 `(race, label)`；
* `ByRawKey`（精确副本）—— 如果某个注册键里出现**不止一个** `':'`（种族 defName 或翻译后的部件名里带冒号），无法安全切分，就把整个查找路径切到 `ByRawKey`，用与上游完全相同的 `race + ":" + label` 查询。这种情况下会分配字符串（和上游一样），但**行为 100% 一致**。正常模组环境下永远走快路径。

`null` and `""` collapse to the same value because upstream's concatenation maps both to `"race:"`. The mirror keeps two maps: a fast `(race, label)` map split on the single `':'`, and an exact raw-key copy used as a fallback if any registration ever contains more than one `':'` (behaviour then degrades to exactly upstream's allocating path, never to different results).

### 3.3 加载顺序：两条路径都覆盖 / both load orders are covered

`HARAnimate.HarmonyInit` 是 `[StaticConstructorOnStartup]`，它的静态构造函数里才调用 `AnimatorSystem.Register`。而**所有 `Mod` 构造函数都在 `[StaticConstructorOnStartup]` 之前执行**，所以本模组在 `Mod` 构造函数里装补丁：

1. **HARAnimate 先注册**（理论上不会，但如果别的模组用反射提前塞了条目）：`Bootstrap.Apply()` 结束时调用 `FastAnimatorSystem.Sync()`，`AccessTools.FieldRefAccess` 扫描 `AnimatorSystem.stacks` 现存条目并镜像。
2. **HARAnimate 后注册**（正常路径）：`Register` 的 prefix 把每个 `"race:label"` 立刻镜像。
3. **另有 `[StaticConstructorOnStartup]`**：所有 Def 加载完后再 `Apply()`（幂等）+ `Sync()`，兜住任何在 `Mod` 构造函数之后、静态构造之前发生的注册。
4. **运行期兜底**：`Evaluate` 未命中时只做一次 `stacks.Count` 整数比较；计数变化才重扫（例如其他模组用反射写私有字典）。

`HARAnimate.HarmonyInit` runs as a `[StaticConstructorOnStartup]` type, and **all `Mod` constructors run before any `[StaticConstructorOnStartup]`**. Patching in the `Mod` constructor therefore installs the `Register` prefix before HARAnimate's registration pass. The startup scan (`AccessTools.FieldRefAccess` on `AnimatorSystem.stacks`), the idempotent `[StaticConstructorOnStartup]` re-apply + `Sync()`, and the count-checked rescan on an `Evaluate` miss cover every ordering.

---

## 4. 线程安全（Waylith 并行渲染）/ Thread safety under Waylith's parallel pre-draw

### 4.1 证据链 / the call chain

RimWorld 1.7（Waylith）把"预绘制"整批丢进 `IJobParallelFor`：

```
Verse.DynamicDrawManager.PreDrawVisibleThings
  └─ new ManagedJobParallelFor(new PreDrawThings{...}).Schedule(count, batch).Complete()
       └─ PreDrawThings.Execute(int index)            // 多个 worker 线程并发
            └─ thing.DynamicDrawPhase(DrawPhase.ParallelPreDraw)
                 └─ PawnRenderer.DynamicDrawPhaseAt(ParallelPreDraw, ...)
                      └─ PawnRenderer.ParallelPreRenderPawnAt
                           └─ PawnRenderer.ParallelGetPreRenderResults
                                └─ PawnRenderTree.ParallelPreDraw
                                     └─ PawnRenderTree.TryGetMatrix
                                          └─ PawnRenderNode.GetTransform
                                               ├─ Worker.OffsetFor(...)     // HARAnimate postfix
                                               ├─ Worker.RotationFor(...)   // HARAnimate transpiler
                                               └─ Worker.ScaleFor(...)      // HARAnimate postfix
                                                    └─ AnimatorCache.Get(...)  ← 本补丁替换
```

`AlienPawnRenderNodeWorker_BodyAddon.OffsetFor/ScaleFor` 与 `PawnRenderNodeWorker.RotationFor` 里的 HARAnimate 补丁都会调用 `AnimatorCache.Get`，因此 **`OffsetFor`/`ScaleFor`/`RotationFor` 是在多个线程上并行执行的**。

HARAnimate's postfixes/transpiler on `OffsetFor`/`ScaleFor`/`RotationFor` all call `AnimatorCache.Get`, and those workers run on multiple threads because the whole pre-draw batch is an `IJobParallelFor`.

### 4.2 因此替换缓存必须是线程安全的 / the replacement cache must be thread-safe

```csharp
private static readonly ConcurrentDictionary<FastCacheKey, FastCacheEntry> Entries = new();

if (!Entries.TryGetValue(key, out FastCacheEntry? entry))
    entry = Entries.GetOrAdd(key, static _ => new FastCacheEntry());

lock (entry)                                  // 每个 (pawn, addon, label) 一个锁对象
{
    if (entry.Slot == slot) return entry.Value;
    entry.Slot = slot;
    entry.LastUpdateTick = tick;
    entry.Value = AnimatorSystem.Evaluate(pawn, parms, label!, tick);
    return entry.Value;
}
```

* **绝不使用未同步的 `Dictionary`**：多线程并发读+写普通 `Dictionary` 会抛 `InvalidOperationException`、死循环或读到损坏的桶（NewRatkinPlus 的 `GeneDrawScaleCache` 就栽在这里）。
* **`lock (entry)` 而不是全局锁**：锁对象是条目自身，不同节点永不竞争；同一节点的两个线程最多排队一次评估。
* **消除撕裂读**：`slot` / `lastUpdateTick` / `value`（24 字节）全部在锁内读写，读到的 `BodyAddonTransform` 一定来自同一次 `Evaluate`。上游"先写 slot 后写 value"的窗口被彻底关闭。
* **`Cleanup` 可以并发**：`ConcurrentDictionary` 的无锁枚举 + `TryRemove`。极端情况下删掉一个正被刷新的条目不会产生错误值 —— 那个线程返回自己刚算出的值，下次查询重新插入。
* **`CleanAll`/`Clear` 可并发**：`ConcurrentDictionary.Clear()` 是线程安全的。

* Never an unsynchronized `Dictionary`: concurrent read+write on a plain `Dictionary` throws `InvalidOperationException`, spins forever, or returns corrupted buckets.
* `lock (entry)`, not a global lock: the lock object is the entry itself, so distinct nodes never contend and two threads on the same node queue at most one evaluation.
* Torn reads are gone: `slot`, `lastUpdateTick` and the 24-byte `value` are only touched under that lock, so a returned `BodyAddonTransform` always comes from a single `Evaluate`. Upstream's "write slot, then value" window is closed.
* `Cleanup` and `CleanAll` stay concurrent-safe: lock-free enumeration + `TryRemove` / `Clear`.

### 4.3 验证结果 / verified result

GREEN 用 16 个线程、4 个共享缓存键、每键 64 个 slot、其中每 4 个线程还并发调用 `Cleanup`，共 800,000 次查询：**0 异常、0 次撕裂/错值**。同一个压力测试跑在未打补丁的上游实现上，每次运行都观测到 **约 2,500 ~ 4,000 次**落在错误 slot 值上的读取（与具体调度有关）。

GREEN runs 16 threads over 4 shared cache keys × 64 slots with every 4th thread also calling `Cleanup`: 800,000 lookups, **0 exceptions, 0 torn/wrong transforms**. The same stress test on unpatched upstream observes **roughly 2,500–4,000** reads that do not match their slot on every run.

---

## 5. 离线验证 / Offline validation

`Validation/` 是一个独立的 `net10.0` 控制台程序，直接加载**真实的** `Assembly-CSharp.dll`（1.7.5005 rev1082）、真实的 `UnityStubs.dll`、真实的 `HARAnimate.dll` 与真实的 `AlienRace.dll`（`AlienPawnRenderNodeWorker_BodyAddon.OffsetFor/ScaleFor` 均被断言存在），不需要游戏循环、不需要 Unity 场景。

The `Validation/` project is a standalone `net10.0` console that loads the **real** `Assembly-CSharp.dll` (1.7.5005 rev1082), the real `UnityStubs.dll`, the real `HARAnimate.dll` and the real `AlienRace.dll`, with no game loop and no Unity scene.

```powershell
cd Validation
pwsh -File .\validate.ps1
```

三步 / three steps:

1. **bind** — 写一份 QA 专用 `bound/Assembly-CSharp.dll`，把 `Verse.ModsConfig..cctor` 清空，**保证验证过程绝不读取或改写玩家的 `ModsConfig.xml`**；同时为 HARAnimate 的 `UnityEngine.CoreModule` 引用生成一个只含类型转发（type forwarder）的 `bound/UnityEngine.CoreModule.dll`，模拟 1.7 运行时的 `WaylithClr.AssemblyNameRedirects = UnityEngine*=UnityStubs`。工坊 DLL 与游戏 DLL 只读打开。
2. **red** — 不打补丁，测上游行为并断言 bug 存在。
3. **green** — 加载本模组、执行 `Mod` 构造函数装补丁，重跑同一套断言并额外做线程压力测试。

1. **bind** — writes a QA-only `bound/Assembly-CSharp.dll` with `Verse.ModsConfig..cctor` emptied, so the validator **can never read or rewrite the user's `ModsConfig.xml`**, plus a type-forwarder-only `bound/UnityEngine.CoreModule.dll` that reproduces 1.7's `WaylithClr.AssemblyNameRedirects = UnityEngine*=UnityStubs`. Workshop and game DLLs are opened read-only.
2. **red** — no patch; measure upstream and assert the bugs exist.
3. **green** — load the mod, run its `Mod` constructor, re-run everything plus a thread stress test.

实测数据 / measured results:

| 指标 / Metric | RED（上游） | GREEN（本补丁） |
| --- | --- | --- |
| `AnimatorSystem.Evaluate`，200,000 次 | **12,800,000 B**（64.0 B/call，字符串拼接） | **0 B**（第二次测量；首次 96 B 为一次性 JIT/初始化开销） |
| `AnimatorCache.Get`，200,000 次（每次跨刷新边界） | **19,600,000 B**（98.0 B/call） | **0 B** |
| 16 线程 × 50,000 次并发查询 + Cleanup | 约 2,500 ~ 4,000 次错值读（撕裂，每次运行不同） | **0 错值 / 0 异常** |
| `CacheRefreshTick = 0` | `DivideByZeroException` | 钳制为 1，正常返回 |
| 冷条目 tick 0..3 | 返回默认变换（不评估） | 逐位一致 |
| 槽位语义（tick 1000/1001 同槽、1004 刷新） | — | 逐位一致 |
| 过期（`Cleanup(40000, 15000)`，3 → 1） | — | 逐位一致 |
| `CleanAll` | — | 一致 |

断言总数 / assertions: **RED 21 + GREEN 51**，全部通过。数值比对使用 24 字节的 `RawTransform` 视图（`Unsafe.As`），是 float 位级比较，不是字符串格式化比较。

**RED 21 + GREEN 51** assertions, all passing. Comparisons use a 24-byte `RawTransform` view (`Unsafe.As`), i.e. bit-level float comparison, not formatted strings.

---

## 6. 保留的上游怪癖 / Preserved upstream quirks

为了 100% 行为一致，以下上游行为**故意没有修**（都已在 GREEN 中断言）：

* **冷条目 tick 0..3**：新条目 `slot` 初始为 0，而 `tick / 4 == 0`，于是前 4 tick 直接返回默认 `BodyAddonTransform`（`Scale = (0,0)`，节点不可见），到 tick 4 才自愈。本补丁完全保留。
* **`CacheRefreshTick` 的实际粒度**：缓存只在 `tick / CacheRefreshTick` 变化时刷新，所以同一槽内的动画值是"冻结"的，这是上游的设计选择，未改动。
* **`null` label 与 `""` label**：在缓存键里不相等，在栈键里相等（都映射到 `"race:"`）—— 与上游的 `label == other.label` 和 `race + ":" + label` 行为逐位一致。

For 100% behavioural parity the following upstream quirks are **intentionally preserved** (and asserted in GREEN): the cold-entry ticks 0..3 default transform, the slot-granular refresh, and the `null` vs `""` label asymmetry between the cache key and the stack key.

---

## 7. 失效条件 / When this fix becomes obsolete

满足任意一条即应停用本补丁（并优先使用上游修复）：

1. **上游修了缓存**：`dtrndg.haranimate` 更新到把 `CacheKey` 改成 `IEquatable<CacheKey>`、去掉 `race + ":" + label`、`Cleanup` 不再 `ToArray()`、日志只在 dev mode 输出，并且给 `CacheEntry` 加了同步。此时本补丁只会重复劳动。
2. **上游改了内部布局**：`AnimatorCache.Get/Cleanup/CleanAll/CacheCount` 或 `AnimatorSystem.Register/Evaluate` 的签名/语义变化，或 `AnimatorSystem.stacks` 字段改名 → `Bootstrap` 会打 `Log.Warning` 并跳过对应补丁（不会崩，但也就不再优化）。
3. **HARAnimate 不再用 `AnimatorCache`**：例如改成完全不同的渲染管线。
4. **游戏版本变化**：1.7 Waylith 的 `DynamicDrawManager.PreDrawThings` / `PawnRenderer.ParallelGetPreRenderResults` / `PawnRenderNodeWorker.OffsetFor|ScaleFor|RotationFor` 任一链路变化，或并行预绘制被移除（此时补丁仍然正确，只是线程安全论证不再必要）。
5. **另一个模组也替换了 `AnimatorCache`**：两个替换式 prefix 会互相覆盖（Harmony 按优先级取第一个返回 `false` 的）。请二选一。

Retire this mod as soon as upstream fixes the cache, changes those method signatures/`stacks`, stops using `AnimatorCache`, or the 1.7 render chain changes. If another mod also replaces `AnimatorCache`, keep only one.

补丁本身是**热插拔安全**的：任何目标方法找不到 / 打补丁失败都只写一条 `Log.Error` 并保留上游行为，不会让游戏崩溃。

The patch is fail-open: a missing target or a failed `Harmony.Patch` only logs and keeps upstream behaviour.

---

## 8. 安装 / Install

1. 订阅 Harmony 与 `HAR BodyPart Animate FrameWork`。
2. 把 `HARAnimatePerfFix` 文件夹放进 `RimWorld/Mods/`。
3. 加载顺序放在 `dtrndg.haranimate` **之后**（`About.xml` 已声明 `loadAfter`，RimSort/RimPy 会自动处理）。
4. 无需新开档；本模组不写任何存档数据、不改任何 Def、不改 `ModsConfig.xml`。

1. Subscribe to Harmony and `HAR BodyPart Animate FrameWork`.
2. Drop the `HARAnimatePerfFix` folder into `RimWorld/Mods/`.
3. Load **after** `dtrndg.haranimate` (`loadAfter` is declared; RimSort/RimPy handle it).
4. No new save needed: nothing is scribed, no Def is patched, `ModsConfig.xml` is untouched.

---

## 9. 给作者的留言草稿 / Friendly message draft for the mod author

> **主题 / Subject:** 一个可选的 `AnimatorCache` 性能补丁（含线程安全修复）/ An optional `AnimatorCache` perf patch (with a thread-safety fix)
>
> 你好 DtrndG，
>
> 我很喜欢 HAR BodyPart Animate FrameWork 的设计 —— 用数学曲线而不是逐帧贴图驱动身体部件，组合性和扩展性都很好。我在 1.7（Waylith）下做了一个**纯运行时、不改你任何文件**的 Harmony 补丁，因为分析下来 `AnimatorCache` 的热路径有几个可以几乎零成本消掉的开销，想把这些发现回馈给你，你是否采纳完全由你决定。
>
> 我在离线环境里用真实的 `Assembly-CSharp.dll`（1.7.5005 rev1082）、真实的 `HARAnimate.dll` 与 `AlienRace.dll` 做了 RED/GREEN 对照，200,000 次查询的结果：
>
> | | 上游 | 补丁后 |
> | --- | --- | --- |
> | `AnimatorCache.Get` 分配 | 98.0 B/call（19.6 MB / 20 万次） | 0 B |
> | `AnimatorSystem.Evaluate` 分配 | 64.0 B/call（12.8 MB / 20 万次） | 0 B |
>
> 具体三处（都在 `Source/Animation/`）：
>
> 1. **`AnimatorCache.CacheKey` 缺 `IEquatable<CacheKey>`**。`ConcurrentDictionary<CacheKey, CacheEntry>` 因此用 `ObjectEqualityComparer<CacheKey>`，每次 `TryGetValue`/`GetOrAdd` 都把 24 字节结构体装箱成 32 字节（哈希一次、桶比较再一次）。加一行 `: IEquatable<CacheKey>` + `public bool Equals(CacheKey other)`，并把 `GetOrAdd` 换成"先 `TryGetValue` 命中就返回，未命中才 `GetOrAdd`"，这一项就没了。
> 2. **`AnimatorSystem.Evaluate` 里的 `string key = race + ":" + label;`**。每个节点每 `CacheRefreshTick` 都要新建一个 64 字节字符串。可以改成 `(string race, string label)` 的 `readonly struct` 键 + `ConcurrentDictionary`，注册时把 `"race:label"` 按第一个 `':'` 拆开镜像即可（我这边还额外处理了"部件名里带冒号"的退化情况）。
> 3. **`AnimatorCacheCleaner.CacheCleaner` 的 `cache.ToArray()` 与 `Log.Message`**。`ConcurrentDictionary` 本身支持无锁枚举，`ToArray()` 可以去掉；`Log.Message` 在 1.7 会带 15 帧栈落盘，建议改成 `if (Prefs.DevMode && removed > 0)`。
>
> 另外有一个**并发**问题想提醒你（可能比分配更重要）：`AnimatorCache.Get` 是
> `entry.slot = slot; ... entry.value = Evaluate(...);` 而 `CacheEntry` 没有同步。1.7 的预绘制是
> `DynamicDrawManager.PreDrawThings : IJobParallelFor` 多线程跑的，`PawnRenderer.ParallelGetPreRenderResults`
> → `PawnRenderTree.TryGetMatrix` → `Worker.OffsetFor/ScaleFor/RotationFor` 都在 worker 线程上，
> 所以两个线程可能同时刷新同一个节点。线程 B 看到新 `slot` 后读 `value`，而 `Vector3`（12 字节）
> 不是原子写，会读到半新半旧的 `BodyAddonTransform`。我的离线压力测试（16 线程 / 4 个共享键 /
> 800,000 次）在上游实现上每次运行都能观测到约 2,500~4,000 次错值读。最小的修法是给每个 `CacheEntry` 一把
> `lock (entry)`（不同节点互不竞争）；我这边就是这么做的，同一压力测试 0 错值。
>
> 还有一个小的健壮性问题：`tick / HARAnimateSettings.CacheRefreshTick` 没有保护，配置成 0 会在渲染线程里
> `DivideByZeroException`。`Math.Max(1, ...)` 即可。
>
> 我的补丁在 `Mods/HARAnimatePerfFix`，源码与离线验证器都在里面（`Validation/validate.ps1` 可一键跑
> RED/GREEN）。如果你希望我把它整理成 PR、或者你打算自己改而我直接撤掉这个补丁，告我一声就行。
> 感谢你做出这么好用的框架！
>
> 祝好，
> WhiteGiverMa
>
> ---
>
> Hi DtrndG,
>
> I really like HAR BodyPart Animate FrameWork — driving body parts with curves instead of frame-by-frame sprites is elegant and very composable. I built a **runtime-only Harmony patch** for 1.7 (Waylith) that touches none of your files, because profiling showed a few near-zero-cost wins in the `AnimatorCache` hot path. Sharing the findings in case they are useful; whether you adopt them is entirely up to you.
>
> I ran a RED/GREEN comparison offline against the real `Assembly-CSharp.dll` (1.7.5005 rev1082), the real `HARAnimate.dll` and `AlienRace.dll`. Over 200,000 lookups:
>
> | | upstream | patched |
> | --- | --- | --- |
> | `AnimatorCache.Get` allocation | 98.0 B/call (19.6 MB / 200k) | 0 B |
> | `AnimatorSystem.Evaluate` allocation | 64.0 B/call (12.8 MB / 200k) | 0 B |
>
> Three spots, all under `Source/Animation/`:
>
> 1. **`AnimatorCache.CacheKey` does not implement `IEquatable<CacheKey>`**, so `ConcurrentDictionary<CacheKey, CacheEntry>` resolves to `ObjectEqualityComparer<CacheKey>` and boxes the 24-byte struct into a 32-byte object on every `TryGetValue`/`GetOrAdd` (once for the hash, once per bucket comparison). Adding `: IEquatable<CacheKey>` plus a `TryGetValue` fast path before `GetOrAdd` removes it.
> 2. **`string key = race + ":" + label;` in `AnimatorSystem.Evaluate`** allocates a fresh 64-byte string per node per `CacheRefreshTick`. A `readonly struct` key of `(race, label)` over a `ConcurrentDictionary`, mirrored at registration by splitting `"race:label"` at its single `':'`, avoids it (I also handle the degenerate "colon inside the label" case).
> 3. **`cache.ToArray()` and `Log.Message` in `AnimatorCacheCleaner.CacheCleaner`**: `ConcurrentDictionary` enumerates lock-free, so the copy is redundant, and in 1.7 `Log.Message` writes a 15-frame stack trace — `if (Prefs.DevMode && removed > 0)` is enough.
>
> One more thing, possibly more important than the allocations: `AnimatorCache.Get` does `entry.slot = slot; ... entry.value = Evaluate(...);` with no synchronization on `CacheEntry`. 1.7 pre-draws pawns on workers (`DynamicDrawManager.PreDrawThings : IJobParallelFor` → `PawnRenderer.ParallelGetPreRenderResults` → `PawnRenderTree.TryGetMatrix` → `Worker.OffsetFor/ScaleFor/RotationFor`), so two threads can refresh the same node. A reader that sees the new `slot` can read a half-written 24-byte `BodyAddonTransform` (`Vector3` is 12 bytes, not an atomic store). My offline stress test (16 threads / 4 shared keys / 800,000 lookups) observes roughly 2,500–4,000 wrong reads on upstream, every run. The minimal fix is `lock (entry)` per entry — no contention between different nodes — which is what my patch does; the same stress test then reports zero.
>
> Minor robustness issue: `tick / HARAnimateSettings.CacheRefreshTick` has no guard, so a config value of 0 throws `DivideByZeroException` on the render thread. `Math.Max(1, ...)` fixes it.
>
> The patch lives in `Mods/HARAnimatePerfFix`, with sources and the offline validator (`Validation/validate.ps1` runs RED/GREEN). Happy to turn it into a PR, or to retire it if you would rather fix it upstream — just let me know. Thanks for the great framework!
>
> Best,
> WhiteGiverMa

---

## 10. 文件 / Files

```
HARAnimatePerfFix/
  About/About.xml                    # packageId whitegiver.haranimate.perffix
  LoadFolders.xml                    # 1.7 -> "/" + "1.7"
  1.7/Assemblies/HARAnimatePerfFix.dll
  README.md
  Source/
    HARAnimatePerfFix.csproj         # net10.0, TreatWarningsAsErrors, references game/Harmony/HARAnimate
    FastAnimatorCache.cs             # FastCacheKey / FastCacheEntry / FastAnimatorCache
    FastAnimatorSystem.cs            # StackLookupKey / stack mirror
    Patches.cs                       # 7 Harmony patch bodies
    Bootstrap.cs                     # Mod ctor + [StaticConstructorOnStartup], idempotent install
  Validation/
    Validation.csproj                # net10.0 console + Mono.Cecil
    validate.ps1                     # build -> bind -> red -> green
    Program.cs                       # assembly binding, UnityEngine facet, MODS CONFIG ISOLATION
    Fixture.cs                       # real Pawn/ThingDef/AnimatorStack fixture + RawTransform view
    Tests.cs                         # 21 RED + 51 GREEN assertions
```

补丁程序集只引用 `System.Runtime` / `System.Collections*` / `System.Threading` / `0Harmony` / `Assembly-CSharp` / `HARAnimate`，**不引用任何 `UnityEngine*` 程序集**，因此不依赖 Waylith 的 `UnityEngine*→UnityStubs` 重定向细节。

The patch assembly references only `System.Runtime`, `System.Collections*`, `System.Threading`, `0Harmony`, `Assembly-CSharp` and `HARAnimate` — **no `UnityEngine*` assembly at all**, so it does not depend on the `UnityEngine*→UnityStubs` redirect.
