# 联合轻工虚拟工具人修复 / UFLI Virtual Worker Fix

- **packageId**: `whitegiver.ufli.virtualworkerfix`
- **目标**: RimWorld 1.7 + 「联合轻工家具拓展 - UF Light Industries Furniture」（`kindseal.ufli`）
- **类型**: 本地 sidecar Harmony 补丁（不修改工坊文件、原 DLL 或存档）

## 问题

全自动超级工作台 `UFRecipes.Building_SuperWorkbench.Produce` / `TryProduceFreeCraft`
在 `fullAuto` 下会从 `MapComponent_UFAutoVirtualWorkerPool` 取一个虚拟工具人：

```csharp
// UFRecipes.MapComponent_UFAutoVirtualWorkerPool.CreateWorker()
Pawn val2 = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
    PawnKindDefOf.Colonist, null, PawnGenerationContext.NonPlayer, null,
    forceGenerateNewPawn: true, ...,
    forceNoIdeo: true,        // 第 36 个实参 = true
    forceNoBackstory: false,  // 第 37 个实参 = false  ← 根因
    ...));
```

`forceNoBackstory = false` 会随机生成童年/成年背景故事。某些背景故事带
`WorkTags.Crafting`（或 Cooking/Artistic 等）的 `workDisables`，使
`SkillRecord.TotallyDisabled == true`。

`Refresh` 虽然把工作台的 `skillLevel` 写进了 `skill.levelInt`：

```csharp
pawn.skills.GetSkill(workSkill).Level = skillLevel;
```

但 `SkillRecord.GetLevel()` 在 `TotallyDisabled` 时直接返回 0：

```csharp
public int GetLevel(bool includeAptitudes = true)
{
    if (TotallyDisabled) return 0;
    ...
}
```

随后 `GenRecipe.MakeRecipeProducts` 对 `recipeDef.efficiencyStat`（例如
`ButcheryMechanoidEfficiency`）调用 `worker.GetStatValue(...)`，
`StatWorker.GetValueUnfinalized` 在开发者模式下打印：

```
Attempted to calculate value for disabled stat ButcheryMechanoidEfficiency;
this is meant as a consistency check, either set the stat to neverDisabled
or ensure this pawn cannot accidentally use this stat (thing=Manfred)
```

并且 `SkillNeed_BaseBonus` 读到 0 级，`ButcheryMechanoidEfficiency`
按 `0.75` 而不是 15 级的 `1.125` 结算。

## 补丁行为

在 `UFRecipes.MapComponent_UFAutoVirtualWorkerPool.TryAcquire(...)` 返回
`result` 后执行一次归一化（`TryAcquire` 签名变化时自动退化到 `Refresh`）：

1. 把会禁用工作的 `story.Childhood` / `story.Adulthood` 替换为
   `workDisables == WorkTags.None` 的背景故事（优先使用 `DefDatabase`
   里真实存在的 Def，找不到时才创建合成 Def；虚拟工具人本身不被 UFLI
   存档，合成 Def 仅作兜底）。
2. 移除 `disabledWorkTags != None` 或 `disabledWorkTypes.Count > 0` 的特性；
   同时移除会给任意技能提供非零 `Aptitude` 的特性/基因/Hediff
   （保证 `Level` 精确等于传入值）。
3. 移除 `disabledWorkTags != None` 的基因（Biotech）与 Hediff 阶段。
4. 调用 `Pawn.Notify_DisabledWorkTypesChanged()` /
   `Pawn_SkillTracker.Notify_SkillDisablesChanged()` 刷新缓存；
   若仍有无法清除的残余 tag，则强制把 `SkillRecord` 的禁用缓存置为
   `BoolUnknown.False` 并记录警告。
5. 把 `pawn.skills.skills` 里**所有**技能的 `levelInt` 同步为工作台的
   `skillLevel`（不再只写 `workSkill`），并清零 `xpSinceLastLevel`。

因此：

- `ButcheryMechanoidEfficiency` / `ButcheryFleshEfficiency` 等
  `efficiencyStat` 按 15 级结算；
- `WeaponUpgrade` 等读取 `worker.skills.GetSkill(SkillDefOf.Crafting).Level`
  的第三方补丁也能拿到正确等级；
- `TaleData_Pawn.GenerateFrom`、`CompArt.JustCreatedBy`、
  `TaleRecorder.RecordTale(TaleDefOf.CraftedArt, ...)` 仍然正常
  （补丁不删除 `Pawn.story`，只替换其中的 BackstoryDef；`TaleData_Pawn`
  只读 `pawn.story.title` 字段，`CompArt` 只读 `pawn.NameFullColored`）。

补丁不改变：工作台自身 `SkillLevel` / `QualityOffset` / `WorkSpeedFactor` 的
计算，`ApplyFixedQuality` 的最终品质覆盖，灵感/记录/食物中毒/金属恐怖等
原版 worker 语义（仍然作用在虚拟工具人身上，不转移到真人）。

## 安装 / 加载顺序

- 依赖 `brrainz.harmony` 与 `kindseal.ufli`。
- `loadAfter`: `brrainz.harmony`, `kindseal.ufli`。
- 将本文件夹放到 `RimWorld/Mods/UFLIVirtualWorkerFix`，在 Mod 列表中排在
  「联合轻工家具拓展」之后。
- **重启游戏后生效**（当前正在运行的进程不会被终止或重启）。

## 离线验证

验证驱动位于 `Validation/`，针对本机真实 DLL：

- `Assembly-CSharp.dll`：`A:/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll`
- `UFRecipes.dll`：`A:/SteamLibrary/steamapps/workshop/content/294100/3651799915/1.7/Assemblies/UFRecipes.dll`

运行（只写入 QA 临时目录，不接触游戏/工坊/存档）：

```powershell
dotnet run -c Release --project Validation -- bind  C:\Users\A1337\AppData\Local\Temp\opencode\ufli-virtualworker-fix-20261108
dotnet run -c Release --project Validation -- red   C:\Users\A1337\AppData\Local\Temp\opencode\ufli-virtualworker-fix-20261108
dotnet run -c Release --project Validation -- green C:\Users\A1337\AppData\Local\Temp\opencode\ufli-virtualworker-fix-20261108
```

本机结果：

```
RED:   TotallyDisabled=True  Level=0   IsDisabledFor=True   efficiency=0.750
GREEN: all skills level=15, CombinedDisabledWorkTags=None,
       IsDisabledFor=False efficiency=1.125; TaleData_Pawn/CompArt OK.
```

> 说明：离线 harness 构造的 `ButcheryMechanoidEfficiency` 只保留
> vanilla `SkillNeed_BaseBonus(Crafting, 0.75, +0.025/级)`，`capacityFactors`
> 留空以隔离技能因子；真实 stat 的 Sight/Manipulation 对健康成年虚拟工人
> 为 1.0，因此 15 级同样是 1.125。若随机健康/特性降低了能力，真实产量会
> 按原版 `capacityFactors` 再缩放，这属于原版身体能力计算，不是本次
> `TotallyDisabled` 红字。

证据与日志：`C:/Users/A1337/AppData/Local/Temp/opencode/ufli-virtualworker-fix-20261108/`
（`validation-red.log`、`validation-green.log`、`bound/`、`ModsConfig-before.xml` 等）。

## 失效与退役条件

出现以下任一情况时应停用/删除本补丁，不要继续叠加：

1. **上游修复**：UFRecipes 自行把 `CreateWorker` 的 `forceNoBackstory` 设为
   `true`，或在 `TryAcquire`/`Refresh` 后清除禁用 WorkTags 并把所有技能同步
   为 `skillLevel`。此时本补丁是冗余的。
2. **签名变化**：`MapComponent_UFAutoVirtualWorkerPool.TryAcquire` 与
   `Refresh` 的参数列表发生变化，补丁会在日志中打印
   `Expected TryAcquire(...) and Refresh(...) were not found; patch not applied`
   并自动跳过；请退役本补丁，等上游修复或重新适配。
3. **类型/包名变化**：`UFRecipes.MapComponent_UFAutoVirtualWorkerPool` 被重命名，
   或 UFLI 的 `packageId` 不再是 `kindseal.ufli`。
4. **游戏版本升级**：从 1.7 升级到更高版本后，`Assembly-CSharp` 版本变化，
   需要重新编译/验证；不要直接沿用旧 DLL。
5. **上游已修复但补丁仍在**：如果日志出现本补丁的 `Normalize` 警告，或
   上游开始自行处理虚拟工具人，优先移除本补丁，避免双重清理。

## 给上游作者「善良海豹」的简明修复建议

根因在 `MapComponent_UFAutoVirtualWorkerPool`，不在 `Produce`：

1. `CreateWorker()` 的 `PawnGenerationRequest` 当前是
   `forceNoBackstory: false`（第 37 个实参）。虚拟工具人不需要背景故事，
   建议改为 `true`，或显式从 `DefDatabase<BackstoryDef>` 里挑选
   `workDisables == WorkTags.None` 的童年/成年背景。
2. `Refresh()` 目前只写 `workSkill`：

   ```csharp
   if (workSkill != null && pawn.skills != null)
       pawn.skills.GetSkill(workSkill).Level = skillLevel;
   ```

   建议在 `TryAcquire` 返回前做一次归一化：
   - 清除所有 `TotallyDisabled` 来源（背景/特性/基因/Hediff）；
   - `pawn.Notify_DisabledWorkTypesChanged()`（内部会调用
     `skills.Notify_SkillDisablesChanged()`）；
   - 把 `pawn.skills.skills` 的所有技能都设为 `skillLevel`，而不只是
     `workSkill`（屠宰的 `efficiencyStat` 可能依赖 Crafting/Cooking，
     第三方补丁也可能读 Crafting）。
3. 如果只想最小改动：至少在 `Refresh` 里检测
   `pawn.skills.GetSkill(workSkill).TotallyDisabled`，为 true 时换一个
   不使用该背景/特性的 pawn，或直接设置
   `forceNoBackstory: true` 后重新生成。
4. `TaleData_Pawn.GenerateFrom` 只读 `pawn.story.title`（string 字段），
   `CompArt.JustCreatedBy` 只读 `pawn.NameFullColored`；把
   `story.Childhood`/`Adulthood` 换成合法的 `BackstoryDef` 不会破坏
   Tale/艺术作者链路。不要把 `Childhood` 设为 `null`：其 setter 会解引用
   `value.spawnCategories`。
