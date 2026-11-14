# 武器基站：射击模式与装备属性

适用于 RimWorld 1.7 原版战斗管线的独立 sidecar。依赖 Harmony、Weapon Station（工坊 `3807251471`）与 Vanilla Fire Modes（工坊 `3662471742`），加载顺序在三者之后。packageId：`whitegiver.weaponstation.firemodes`。当前版本 `1.1.0`。

## 验证状态

2026-10-27，用户确认游戏内验证通过。发布 DLL 与已安装、经用户验证的版本一致；下方保留离线验证的覆盖范围，不将此次确认扩展为所有模组组合或边缘场景均已实测。

2026-11-14，`1.1.0` 针对 Dubs Performance Analyzer 报出的 `0.291 ms/frame` 重做了逐帧路径，离线验证程序新增 RED/GREEN 逐调用对比与 IL 调用路径检查（见“性能”）。游戏内 DPA 复测尚未进行。

## 范围

- 只增强无人 `VortexWS_WeaponStation`；有人 `VortexWS_WeaponMount` 继续使用上游的操作员精度、技能经验及射击循环，不添加本补丁组件或模式按钮。
- 无人基站拥有独立的默认、精准、短点射、压制模式及自动选择开关；模式与开关随建筑存档，不随枪或安装者转移。
- 参数直接读取 VFM 全局设置或安装武器的自定义 profile，不另存一套倍率设置。
- 应用安装武器的装备时属性偏移，射击精度按原版评分曲线换算，瞄准与冷却使用基站自己的对应属性。建筑不会因此获得狩猎等小人专属行为。
- 保留安装枪本身的四档精度、品质、弹丸、射程、射速等数据，不修改共享的武器 Def 或 `verbProps`，不把建筑伪装成 Pawn。
- 基站检查栏显示当前模式、每格射击精度与瞄准/冷却时间；属性报告中保持同一套数值，不受上游临时替换 `turretGunDef` 的显示逻辑影响。
- 不支持 Combat Extended；启用 CE 时不应用代码适配，不应同时启用。

## 数值语义

未额外修改的基站每格精度为 96%，对应原版射击评分 8。装备 `ShootingAccuracyPawn +0.4` 后按评分 8.4 换算，而不是给 96% 加 40 个百分点；VFM 精度倍率也作用于评分，再换算每格精度。

以 REAPR 为例，无其他修正时：

- 默认模式：评分 `8 + 0.4 = 8.4`，每格精度 96.2%，瞄准 `3.5 × 0.9 = 3.15` 秒，冷却 2.3 秒。
- 精准模式：评分 `8.4 × 1.5 = 12.6`，每格精度 97.65%，瞄准 `3.15 × 1.2 = 3.78` 秒，冷却 `2.3 × 0.5 = 1.15` 秒。
- 每格精度不是最终命中率；距离、武器自身精度、品质、掩体、天气和目标体型等仍参与原版命中计算。时间按游戏 tick 舍入。

自动选择使用 VFM 设置中的两个距离阈值，在基站开始瞄准时选模式，因此瞄准时间与本轮模式一致。全局禁用玩家自动选择时，基站可手动选择；禁用 NPC 模式效果时，非玩家基站仍保留装备属性，但不应用模式修正。

## 性能（1.1.0）

`1.0.0` 对 `Verb.VerbTick` 与 `Verb.get_BurstShotCount` 做了全局补丁。前者对地图上每一个 Verb 都会执行，而 `CleanupBurst` 只有在 Verb 正在连射时才需要清锁；其余 99.99% 的调用（每个小人、动物、机械体、普通炮塔的拳、牙、喙、枪、能力）仍会走一次 `ConditionalWeakTable<Verb, BurstState>.TryGetValue`。`CompStationFireModes.Eligible` 还会在每次查询时线性遍历 `ThingWithComps.comps` 找 `CompMannable`，并比较一次 `defName`。DPA 在 28 座基站的存档上把这段记到 `0.291 ms/frame`。

`1.1.0` 的处理：

- `CleanupBurst`、`BurstCount`、`LockBurst` 的第一行都是 `if (__instance.caster is not Building_WeaponStation) return;`：一次字段读取加一次类型判断，过滤掉全部非基站 Verb，无锁、无哈希、无分配。
- 第二行是 `Volatile.Read(ref activeBurstLocks) == 0`。计数只在 `BurstState.Valid` 真正发生 `false → true` / `true → false` 时用 `Interlocked` 增减，因此没有基站在连射时，连基站自己的空闲 Verb 也在两次读取后返回。
- `BurstState` 自带终结器：Verb 在连射途中被销毁（拆除、换图）而没有回到 Idle 时，终结器补一次减计数，计数器不会被永久钉在 1 以上。
- `BurstState` 缓存锁定当时的 `CompStationFireModes`，`BurstCount` 不再重新解析组件；`CompStationFireModes` 在 `Initialize`/`PostSpawnSetup` 缓存 `targetStation` 与 `mannable`，属性、检查栏和模式查询不再扫描 `comps` 或比较 `defName`。
- 存档字段 `wsfm_mode`、`wsfm_auto` 与所有模式语义未变，旧档直接可读。

离线对比（96 个无关 Verb：小人步枪、近战、普通炮塔，6 万次 `VerbTick` + `BurstShotCount`，三轮取最快；两组共用同一套 Harmony 跳板与原版方法体，只替换补丁方法体）：

| 项 | 结果 |
|---|---|
| `1.0.0` 方法体 | 每次调用 1 次表查找，13.6–14.5 ns/call |
| `1.1.0` 方法体 | 0 次表查找、0 个新状态、0 B 分配，8.2–8.7 ns/call（约 −36%~−43%） |
| 空补丁体下限 | 约 4–7 ns/call（随机器负载波动），即剩余部分主要是 Harmony 跳板与原版 `VerbTick`/`BurstShotCount` 本身 |

按 DPA 报告中约 10,000 次 `VerbTick`/帧估算，被删掉的查表约合 `0.06 ms/frame`。DPA 对热补丁方法的每次调用都会计时，其自身插桩开销也计入该行；若 1.1.0 复测仍有非零值，剩余部分是 Harmony 跳板而不是本补丁的表操作。要彻底消掉这一行只能不再补丁 `VerbTick`（改用 `Verb.Reset` 等状态迁移信号），本版没有这么做：`Verb.state` 是公开字段，子类可以直接改写，漏一次就会把锁定值留到下一轮。

运行时可用公开只读计数器核对：`StationFireModesDiagnostics.ActiveBurstLocks`、`BurstTableLookups`、`BurstTableLocks`。正常的殖民地只有在基站真的开火时 `BurstTableLookups` 才会增长。

## 安装与存档

将本目录的 `About/`、`Assemblies/`、`Patches/`、`Languages/` 复制为游戏 `Mods/WeaponStationFireModes/`，启用后重新启动游戏。已有基站在读档时取得新组件，无需拆除或重新安装武器。

停用补丁会移除模式 UI 和本补丁的属性转接，基站与枪仍由上游模组持有；不需要改存档或转换建筑。正常游玩前仍建议保留常规存档备份。

## 构建

```text
dotnet build Source/WeaponStationFireModes.csproj -c Release
```

引用路径可通过 `GameRoot`、`WorkshopRoot`、`HarmonyPath`、`WeaponStationPath`、`FireModesPath` 覆盖。第三方依赖仅编译引用，`Private=false`，不随本补丁分发。构建默认只输出 `bin/`，不部署到游戏或覆盖 `Assemblies/`。

## 维护与退役

本实现针对游戏 `1.7.4990 rev1069`（`1.1.0` 离线复核于 `1.7.5005 rev1082`）、Weapon Station 当前基础 DLL 与 VFM `1.8.0`。上游更新后复核：

1. XML 只匹配一次无人基站的 `comps`，不匹配有人支架。
2. Weapon Station 的 `WeaponWarmupTime`、`BurstCooldownTime` 和实际预热调用链。
3. VFM 公共 `FireModeDB`、模式枚举、图标、自动阈值与 profile 覆盖语义。
4. 原版装备 offset、精度曲线、`Verb.WarmupComplete`、`BurstShotCount` 和 burst 状态的接线。
5. `Verb` 的 `caster` 字段、`VerbState` 迁移点（`Reset`、`TryCastNextBurstShot`、`VerbTick`）与 `VerbTick` 是否仍对每个 Verb 调用；`1.1.0` 的快路径依赖这三处。

若任一上游已经提供同等无人基站适配，应先对照属性是否重复应用，再停用本补丁；不要并行叠加两套相同倍率。本目录不包含上游源码、DLL 或贴图，不自动上传到创意工坊。

## 离线验证

```text
pwsh -File Validation/validate.ps1
pwsh -File Validation/validate.ps1 -PatchPath path/to/deployed/WeaponStationFireModes.dll
```

验证程序使用当前本机的真实游戏、Weapon Station 与 VFM 程序集。`bind/red/run/ce` 分别在独立进程运行，输出到指定的 `QaRoot`，不加载用户存档、不读取真实游戏配置，也不启动游戏。硬依赖已在 About 声明，不提供“未安装 VFM 仍可独立使用”的兼容层。

第三方 ModExtension（例如 GTek 的 `GTekFunctions.GunDrawExtension`、`MuzzleFlash.MuzzleFlashProps`）在沙盒里没有对应程序集，验证程序会显式剔除这些节点并在日志里列出；它们只影响绘制，不参与本补丁的任何数值。

| 层次 | 本轮覆盖 |
|---|---|
| 全真 | XML PatchOperation、基站/枪 Def 解析、真实 Harmony 3 注册与幂等、StatWorker/精度曲线/装备偏移、VFM 参数及 profile、基站预热/冷却 getter、模式 gizmo 的 action、模式与自动开关的 Scribe 文件往返、真实基站检查栏文本及 StatsReport prefix/finalizer 临时换 Def 期间的数值 |
| 全真调用链 | 首轮 `TryStartShootSomething` 经过强制目标或寻敌分支，自动选模后进入真实 `TryStartCastOn → WarmupComplete`；覆盖立即连射与常规预热。真实 burst getter/状态/清理和单发、一次性排除均参与 |
| 全真热路径（1.1.0 新增） | 96 个非基站 Verb（小人步枪/近战、原版炮塔）各 6 万次 `VerbTick` + `BurstShotCount`：真实补丁跳板下 0 次 `ConditionalWeakTable` 查找、0 个新状态、0 B 分配；把 `1.0.0` 方法体换进同一套跳板做 RED 对照（每次调用 1 次查找），并测空补丁体作为开销下限 |
| 全真调用路径（1.1.0 新增） | 用 Mono.Cecil 读取实际发布 DLL 的 IL：只有 `CleanupBurst`/`BurstCount`/`LockBurst` 引用 burst 表，且每个引用点之前都先有 `isinst Building_WeaponStation` 与读取 `activeBurstLocks` 的 `Volatile.Read` |
| 全真计数器（1.1.0 新增） | `ActiveBurstLocks` 在锁定/空闲 `VerbTick` 上 0↔1 正确迁移；连射途中被丢弃的 Verb 经终结器把计数放回 0；无锁时空闲基站不产生任何表查找 |
| 测试替身 | QA 专用副本只重绑 Unity 引用，禁用真实配置与静态贴图/材质初始化；只种本题 StatDef，DLC 条件节点不加载；枪的弹丸引用用测试 Def。目标可命中判定、寻敌结果、最终发射/FX 和战斗日志为边界替身；操作员属性值隔离，操作员转接和 1.2 倍加成使用上游真实代码 |
| 离线驱动未覆盖 | 游戏窗口绘制、鼠标命中、弹丸飞行/伤害、完整地图 AI、完整旧档加载、DPA 的实际采样。用户游戏验证结论见上方，离线驱动本身不覆盖这些使用面 |

负向覆盖包括空基站、两个基站模式互不影响、换枪后属性即时变化、空/有人操作的武器支架不改、NPC 禁用模式时连自定义 Default 倍率也不应用、CE 启用时补丁与组件回调均不生效。

最小游戏浅测：读档后选中一座已装枪的无人基站，切一次“默认 → 精准”，观察检查栏的瞄准时间/每格精度随之变化；有人支架不应出现本补丁按钮。不要求重建基站。要复核性能，可在同一存档用 DPA 对比 `1.0.0`/`1.1.0` 的 `whitegiver.weaponstation.firemodes` 一行，或读取上面三个诊断计数器。
