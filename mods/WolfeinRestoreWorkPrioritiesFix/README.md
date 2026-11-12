# 沃芬工作优先级初始化修复（RimWorld 1.7）

独立 sidecar，修复 Wolfein Race 在生成袭击者时刷的 `Sale did not have work settings initialized.` 红字。需要 Harmony 与 Wolfein Race，排在二者之后。不改工坊文件、不改存档，正常重启后生效。

## 现象

生成袭击者时出现红字，栈顶为：

```
Sale did not have work settings initialized.
  at RimWorld.Pawn_WorkSettings.GetPriority(WorkTypeDef w)
  at Wolfein.HediffComp_RestoreWorkPriorities.CompPostPostAdd(Nullable`1 dinfo)
  at Verse.Hediff_AddedPart.PostAdd(...)
  at RimWorld.Recipe_InstallArtificialBodyPart.ApplyOnPawn(...)
  at RimWorld.PawnTechHediffsGenerator.GenerateTechHediffsFor(Pawn pawn)
  at Verse.PawnGenerator.GenerateInitialHediffs(...)
```

`Wolfein_HeavyGearAugmentationInstalled` / `Wolfein_HeavyGearAugmentationLightInstalled` 两个外骨骼 Hediff 带有 `Wolfein.HediffCompProperties_RestoreWorkPriorities`。它们的设计意图是：安装时保存被禁用工作的当前优先级，移除时恢复。

## 根因

1.7 的 `PawnGenerator.GenerateNewPawnInternal` 只在**玩家派系**生成时调用 `workSettings.EnableAndInitialize()`（`PawnGenerator.cs:898`），随后才在 `GenerateInitialHediffs`（`:929`）里给袭击者安装科技义体。

`PawnTechHediffsGenerator.InstallPart` → `Recipe_InstallArtificialBodyPart.ApplyOnPawn` → `Hediff_AddedPart.PostAdd` → `HediffComp_RestoreWorkPriorities.CompPostPostAdd` 读取 `pawn.workSettings.GetPriority(...)`。此时非玩家派系 pawn 的 `workSettings` 非空但 `priorities == null`，触发 1.7 的 `ConfirmInitializedDebug()`：

```csharp
if (priorities == null)
{
    Log.Error(pawn + " did not have work settings initialized.");
    EnableAndInitialize();
}
```

所以这是**日志红字 + 自愈初始化**，不是崩溃，也不坏档。但它会：每次生成带外骨骼的敌对沃芬都刷一条红字；并在生成中途提前初始化工作优先级，使之后被招募的这类 pawn 不再按原版时机重新分配初始工作。

## 修复

Harmony prefix 同时挂到 `CompPostPostAdd` 与 `CompPostPostRemoved`：

* `workSettings == null`、非 humanlike、pawn 为空：放行原方法（原方法本来就会 return）。
* `workSettings` 已初始化：放行原方法，保存/恢复逻辑完全不变。
* `workSettings` 非空但未初始化：跳过原方法，不读也不写优先级，把初始化留给原版流程。

跳过后 `savedPriorities` 为 null，`CompPostPostRemoved` 的既有 fallback 会处理：该工作当前优先级为 0 时恢复为 3。对生成阶段本来就没有优先级的敌人生成 pawn，这是正确语义。

未改工坊 DLL，未改 XML，未改存档。

## 验证

2026-11-09，Release 构建 0 警告 / 0 错误。离线驱动使用真实 `Wolfein.dll`（工坊 3473140562，1.6 目录）、真实游戏 `Assembly-CSharp.dll`（`d55aa8e4…81254`）、Harmony 3，13 项全部通过：

* `CompPostPostAdd` / `CompPostPostRemoved` 均可解析。
* 未打补丁时真实原方法确实执行并分配 `savedPriorities`（红对照）。
* 打补丁后两个入口各 1 个 prefix，owner 与方法正确。
* 未初始化 + humanlike → 跳过；已初始化 → 放行；非 humanlike → 放行；`workSettings == null` → 放行。
* 真实 `CompPostPostAdd(null)` 在未初始化 pawn 上被跳过，工作设置保持未初始化。

驱动：`Validation/Driver`。运行：

```
dotnet run -c Release --project Validation/Driver/Driver.csproj
```

**游戏内验证**：2026-11-09 部署并启动烟测通过（`Logs/Player.log:1977` 注册成功）；2026-11-12 用户确认多日实际游玩未再出现 `did not have work settings initialized.` 红字。

## 构建、部署、退役、回退

```
dotnet build Source/WolfeinRestoreWorkPrioritiesFix.csproj -c Release
```

可覆盖 `GameRoot`、`HarmonyPath`。安装包只需 `About`、`LoadFolders.xml`、`1.7/Assemblies`。

核验的上游：

* Wolfein Race 工坊 ID `3473140562`，`1.6/Assemblies/Wolfein.dll` SHA-256 `da150377a172276fc87622873e9e77289582390927881a41845c0a500bf3e322`（2026-11-08 更新版本，在 1.7 加载）。
* 工坊评论区 2026-11-09 已有同样报错反馈，作者回复“之后会检查相关代码”。

上游更新后：若作者已修复（`CompPostPostAdd` 增加初始化判断或改在 `PostAdd` 之外处理），禁用本补丁并复核袭击生成即可退役；若方法名变化，启动日志会报 `not found; patch skipped`，此时重新核对补丁目标。回退只需禁用本补丁并重启，不改存档。

## 给作者的反馈草稿

你好，1.7 下生成带外骨骼的敌对沃芬袭击者时会刷 `Sale did not have work settings initialized.`，栈顶是 `HediffComp_RestoreWorkPriorities.CompPostPostAdd` 调 `Pawn_WorkSettings.GetPriority`。1.7 的 `PawnGenerator` 只在玩家派系生成时初始化 workSettings，袭击者生成走到 `GenerateInitialHediffs` 时还没初始化；`GetPriority` 会自行初始化并打日志。建议在保存优先级前判断 `pawn.workSettings.Initialized`（未初始化就跳过保存），这样不改变玩家 pawn 的既有行为，也能消掉生成期红字。感谢维护。
