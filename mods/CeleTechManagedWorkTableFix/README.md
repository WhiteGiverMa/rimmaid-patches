# 天工铸造自动化中枢修复

独立 RimWorld **1.7 / Waylith** 兼容 Mod，依赖 Harmony 与 CeleTech Arsenal MKIII（工坊 `3446237098`）。

`packageId`: `whitegiver.celetech.managedworktablefix`，版本 `1.1.0`。

## 修复行为

修复自动化中枢的两条生产中断路径：

1. **消耗整堆材料**：`TryConsumeGatheredMaterials()` 枚举地图物资列表时 `SplitOff(...).Destroy()` 改动同一列表，抛 `Collection was modified`，材料已扣而进度停在 0（1.0.0）。
2. **产物生成空引用**：`CompleteCurrentBill()` 把 `dominantIngredient = null` 传给原版 `GenRecipe.MakeRecipeProducts`；产物 `MadeFromStuff` 时原版直接读取 `dominantIngredient.def`，抛 `NullReferenceException`。上游 catch 只记 `[ManagedWorkTable] MakeProduct error`，随后仍清空内部容器并重置账单，于是**材料消失、没有产物**（1.1.0）。

保留材料筛选、扣料数量、材料顺序、配方、品质和进度规则；不修改工坊 DLL、Def、存档，不全局修改 `ListerThings`，不吞掉生产异常；不依赖 CE，也不与武器重复组件修复合并。

**不返还修复前已经损失的材料。** 产物使用默认材料（与中枢无工人时的上游回退一致），因为中枢在扣料时没有记录实际消耗的材料种类。

## 根因与实现

### 材料列表

上游直接 `foreach` 枚举 `Map.listerThings.ThingsInGroup(HaulableEver)` 返回的内部列表，并在循环中 `SplitOff(...).Destroy()`。取走整堆时，原版 `SplitOff → DeSpawn → ListerThings.Remove → RemoveSwap` 修改正在枚举的同一列表，下一次 `MoveNext` 失败；即使刚好扣足，也先执行 `MoveNext` 才有机会进入循环内的 `break`。

异常发生在末尾 `progress = 0.001f` 之前；上游 catch 只 `Log.ErrorOnce`，不回滚扣料，后续重试可能再次消耗。这是单线程可达路径，不需要把其他模组或 Waylith 并发当作前提。

本补丁通过软引用定位无参 `void TryConsumeGatheredMaterials()`，只将其中唯一的 `ListerThings.ThingsInGroup(ThingRequestGroup)` 调用替换为返回 `new List<Thing>(lister.ThingsInGroup(group))` 的包装器。其余 IL、标签、异常块与上游调用保持原样。目标缺失或调用数不为一时明确记录并拒绝应用，不静默兜底。

### 产物生成

`CompleteCurrentBill()` 的调用是：

```csharp
GenRecipe.MakeRecipeProducts(recipe, workerPawn, new List<Thing>(), null, this, null, null, null);
```

1.7 原版在产物 `thingDef.MadeFromStuff` 为真时执行 `dominantIngredient.def`（反编译 IL `IL_00ec`），没有空检查。普通工作台总会传入实际原料，所以原版路径不会触发；自动化中枢固定传 `null`，遇到武器、防具、家具等需要材质的产物就必炸。

上游的 `workerPawn == null` 回退分支使用 `ThingMaker.MakeThing(product.thingDef, null)`，即由游戏选择默认材质。本补丁在 `GenRecipe.MakeRecipeProducts` 的 prefix 中，仅在 **billGiver 是 CeleTech 自动化中枢、主导材料为空、且产物需要材质** 时，用 `GenStuff.DefaultStuffFor` 生成同等的默认主导材料；其他调用者、已有主导材料和非材质产物完全不变。

## 构建与安装

```powershell
dotnet build .\Source\CeleTechManagedWorkTableFix.csproj -c Release
```

可通过 `-p:GameRoot=... -p:HarmonyPath=...` 指定本机 1.7 游戏根目录与 Harmony 3 路径；不需要 CeleTech 编译期引用。不分发第三方程序集，构建不会自动部署。

关闭游戏后，将本目录的 **About/** 与 **Assemblies/** 复制到游戏 `Mods/CeleTechManagedWorkTableFix/`，不要复制 Source/Validation/bin/obj。在模组列表中排在 Harmony、CeleTech Arsenal MKIII 之后并启用，正常启动生效。卸载时关闭游戏、禁用此独立补丁即可，无新增存档数据。

成功日志：

```text
[CeleTech Managed WorkTable Fix] Patched TryConsumeGatheredMaterials (one material-list snapshot).
[CeleTech Managed WorkTable Fix] Patched MakeRecipeProducts (default stuff for automation-hub products).
```

出现 `patch not applied` 或 `Could not apply` 视为**未生效**。原 DLL 不在进程中时会明确记录 skipped。

## 回归验证

```powershell
pwsh -File .\Validation\validate.ps1
```

可指定 `-GameRoot`、`-HarmonyPath`、`-CeleTechPath` 和 `-PatchPath`；默认使用本目录的发布 DLL。2026-10-27 验证结果：

- Release 构建零警告、零错误。
- RED：直接调用实际 CeleTech 私有方法，整堆 10 份材料已经销毁，随后抛原始 `Collection was modified`，进度仍为 0。
- RED：以真实 CeleTech 中枢作为 bill giver、真实 `GenRecipe.MakeRecipeProducts`、需要材质的产物和空主导材料调用，复现原始 `NullReferenceException`。
- GREEN：通过真实 Mod 构造函数注册两个 Harmony 补丁；材料列表只改一个调用点，指令、标签、异常块保持；0/2 个调用点的漂移输入被拒绝。
- GREEN：整堆、多整堆、部分取料、材料不足、无材料配方、无当前账单六类边界通过，扣料数量与进度均核对。
- GREEN：中枢产物获得默认材质；已有主导材料、非材质产物、非 CeleTech bill giver 三条路径行为不变。
- GREEN：缺少 CeleTech 时不注册补丁。
- 发布程序集只有 .NET 标准库、Assembly-CSharp 与 Harmony 引用，无 CeleTech、CE 或旧 Unity 编译依赖。

**离线接缝边界**：材料路径的物资分组列表、`ListerThings`、`SplitOff/Destroy` 和过滤，以及产物路径的 `GenRecipe.MakeRecipeProducts`、`GenStuff.DefaultStuffFor`、`ThingMaker.MakeThing` 都执行真实游戏代码。产物测试使用最小 `ThingDef`/`RecipeDef`，不覆盖地图生产、放置、品质、美术或存读档；测试 `Thing` 子类仅替换 `DeSpawn`，QA-only Harmony 跳过预留/指定清理。`Validation/bin/.../bound/` 中的临时副本将旧 Unity 引用绑定到 UnityStubs，并禁用游戏副本的 `ModsConfig` 初始化，避免接触用户配置。游戏/工坊原件及部署版补丁不作这些测试改写。

游戏内仍需下一次正常读档后，让已有中枢完成一单**需要材质的产物**（武器/防具/家具等），确认产物出现且材料数量合理；不必重建中枢。只有红字消失不等于验收通过，因为上游 `ErrorOnce` 会去重。

2026-10-27 小若确认游戏内验证通过：中枢完成一单需材质产物，未再出现 `MakeProduct` 红字，两条补丁注册日志均出现。离线调用仍不能替代完整地图生产、搬运、存读档或 GUI 验证。

## 上游更新与退役

核验基线：2026-10-27，RimWorld `1.7.4990 rev1069`，CeleTech `1.7/Assemblies/CeleTech.Base.dll` SHA-256：

```text
8DB0C896BBF8C66DBEC31E32F69F0FACF4A8A02EFD11EF8A3A8E3FCDC82F3678
```

独立补丁不受 Steam 更新覆盖，但上游更新后仍须复核：

- `TryConsumeGatheredMaterials` 的无参 `void` 签名与唯一调用点；作者改为安全枚举后停用对应部分。
- `CompleteCurrentBill` 是否仍向 `MakeRecipeProducts` 传空主导材料；作者改为传入实际材料或自行处理材质后停用对应部分。
- `GenRecipe.MakeRecipeProducts` 的 `dominantIngredient.def` 读取是否已被原版或作者修复。

DLL 哈希或最后修改时间变化是复核信号，不是自动退役证据。作者重写生产流程时先复核再决定是否需要新版补丁。

给作者的中文反馈草稿见 [`docs/UPSTREAM_COMMENT.zh-CN.md`](docs/UPSTREAM_COMMENT.zh-CN.md)，尚未发布。
