# 天工铸造自动化中枢材料列表修复

独立 RimWorld **1.7 / Waylith** 兼容 Mod，依赖 Harmony 与 CeleTech Arsenal MKIII（工坊 `3446237098`）。

`packageId`: `meidocho.celetech.managedworktablefix`，版本 `1.0.0`。

## 修复行为

只修复 `CeleTech.Base.Building_ManagedWorkTable.TryConsumeGatheredMaterials()` 从地图消耗整堆材料时的 `Collection was modified; enumeration operation may not execute`。

- 保留材料筛选、扣料数量、材料顺序、配方、产物、品质和进度规则。
- 不修改工坊 DLL、Def、存档，不全局修改 `ListerThings`，不吞掉生产异常。
- 不依赖 CE，也不与武器重复组件修复合并。
- **不返还修复前已经消耗的材料**，不处理该建筑其他潜在缺陷。

## 根因与实现

上游直接 `foreach` 枚举 `Map.listerThings.ThingsInGroup(HaulableEver)` 返回的内部列表，并在循环中 `SplitOff(...).Destroy()`。取走整堆时，原版 `SplitOff → DeSpawn → ListerThings.Remove → RemoveSwap` 修改正在枚举的同一列表，下一次 `MoveNext` 失败；即使刚好扣足，也先执行 `MoveNext` 才有机会进入循环内的 `break`。

异常发生在末尾 `progress = 0.001f` 之前；上游 catch 只 `Log.ErrorOnce`，不回滚扣料，后续重试可能再次消耗。这是单线程可达路径，不需要把其他模组或 Waylith 并发当作前提。

本补丁通过软引用定位无参 `void TryConsumeGatheredMaterials()`，只将其中唯一的 `ListerThings.ThingsInGroup(ThingRequestGroup)` 调用替换为返回 `new List<Thing>(lister.ThingsInGroup(group))` 的包装器。其余 IL、标签、异常块与上游调用保持原样。目标缺失或调用数不为一时明确记录并拒绝应用，不静默兜底。

## 构建与安装

```powershell
dotnet build .\Source\CeleTechManagedWorkTableFix.csproj -c Release
```

可通过 `-p:GameRoot=... -p:HarmonyPath=...` 指定本机 1.7 游戏根目录与 Harmony 3 路径；不需要 CeleTech 编译期引用。不分发第三方程序集，构建不会自动部署。

关闭游戏后，将本目录的 **About/** 与 **Assemblies/** 复制到游戏 `Mods/CeleTechManagedWorkTableFix/`，不要复制 Source/Validation/bin/obj。在模组列表中排在 Harmony、CeleTech Arsenal MKIII 之后并启用，正常启动生效。卸载时关闭游戏、禁用此独立补丁即可，无新增存档数据。

成功日志：

```text
[CeleTech Managed WorkTable Fix] Patched TryConsumeGatheredMaterials (one material-list snapshot).
```

出现 `patch not applied` 或 `Could not apply` 视为**未生效**。原 DLL 不在进程中时会明确记录 skipped。

## 回归验证

```powershell
pwsh -File .\Validation\validate.ps1
```

可指定 `-GameRoot`、`-HarmonyPath`、`-CeleTechPath` 和 `-PatchPath`；默认使用本目录的发布 DLL。2026-10-20 验证结果：

- Release 构建零警告、零错误；两个 C# 文件诊断干净。
- RED：直接调用实际 CeleTech 私有方法，整堆 10 份材料已经销毁，随后抛原始 `Collection was modified`，进度仍为 0。
- GREEN：通过真实 Mod 构造函数注册 Harmony 3 补丁；只改一个调用点，其他指令、标签、异常块保持；0/2 个调用点的漂移输入被拒绝。
- GREEN：整堆、多整堆、部分取料、材料不足、无材料配方、无当前账单六类边界通过，扣料数量与进度均核对；缺少 CeleTech 时不注册补丁。
- 发布程序集只有 .NET 标准库、Assembly-CSharp 与 Harmony 引用，无 CeleTech、CE 或旧 Unity 编译依赖。

**离线接缝边界**：目标方法、物资分组列表、`ListerThings.Add/Remove/RemoveSwap`、`SplitOff/Destroy` 和材料过滤均执行真实代码。测试 `Thing` 子类仅替换 `DeSpawn`，保留真实列表移除与 map 状态变更，省略图形、区域和 AI 记账；QA-only Harmony 跳过预留/指定清理。`Validation/bin/.../bound/` 中的临时副本将旧 Unity 引用绑定到 UnityStubs，并禁用游戏副本的 `ModsConfig` 初始化，避免接触用户配置，DLC 开关默认为 false。游戏/工坊原件及部署版补丁不作这些测试改写。

游戏内仍需下一次正常读档后，让已有中枢完成一单生产，观察实际产物和材料数量；不必重建中枢。只有红字消失不等于验收通过，因为上游 `ErrorOnce` 会去重。

当前状态保留为 **game-qa-pending**；离线调用不能替代真实地图生产、搬运、存读档或 GUI 验证。

## 上游更新与退役

核验基线：2026-10-20，RimWorld `1.7.4982 rev1280`，CeleTech `1.7/Assemblies/CeleTech.Base.dll` SHA-256：

```text
8DB0C896BBF8C66DBEC31E32F69F0FACF4A8A02EFD11EF8A3A8E3FCDC82F3678
```

独立补丁不受 Steam 更新覆盖，但上游更新后仍须复核目标无参 void 签名、唯一调用点和循环行为。DLL 哈希或最后修改时间变化是复核信号，不是自动退役证据。作者改为快照或其他不会被扣料修改的安全枚举后，停用本补丁，避免重复复制列表。若作者重写生产流程，则先复核再决定是否需要新版补丁。

给作者的中文反馈草稿见 [`docs/UPSTREAM_COMMENT.zh-CN.md`](docs/UPSTREAM_COMMENT.zh-CN.md)，尚未发布。
