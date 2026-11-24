# 排查记录：UFLI 全自动虚拟工具人禁用技能红字（2026-11-08）

## 1. 环境与结论

- 游戏：RimWorld 1.7（`Assembly-CSharp, Version=1.7.9805.38275`）
- 目标模组：`KindSeal.UFLI`（工坊 `3651799915`）
- 目标 DLL：`A:/SteamLibrary/steamapps/workshop/content/294100/3651799915/1.7/Assemblies/UFRecipes.dll`
  - sha256 `439713cfc6986565a2c18ea15805d4dbbccbb8f173d08206c6fb9bdf8bcda57a`
- `Assembly-CSharp.dll` sha256 `d55aa8e47fa3c5463ca85b0eccd51f195b4bbcd04f56da3af21c2f6688a81254`
- 补丁：`UFLIVirtualWorkerFix.dll` sha256 `356a424667fe629df92cfed4df35c347b1d0d61779fd4db4dd801d672d2b1f35`
- 修复：`whitegiver.ufli.virtualworkerfix`（本地 sidecar，Harmony）
- 结论：**RED 复现成功、GREEN 修复成功**，已部署到本地 `Mods/UFLIVirtualWorkerFix`，并在 `ModsConfig.xml` 的 `kindseal.ufli` 后插入本补丁。

### 进程状态

- 调查开始时 RimWorld 正在运行：PID `92768`，启动时间 `2026/11/8 22:46:59`。
- 部署前复查：系统中已无 `RimWorld*` 进程（游戏自行退出/关闭，非本代理终止）。
- 全程未执行 `Stop-Process` / 任务管理器结束进程 / 重启游戏。

## 2. 根因链（反编译实证）

### 2.1 虚拟工具人使用随机背景故事

`UFRecipes.MapComponent_UFAutoVirtualWorkerPool.CreateWorker()`：

```csharp
Pawn val2 = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
    PawnKindDefOf.Colonist,
    (Faction)null,
    PawnGenerationContext.NonPlayer,
    (PlanetTile?)null,
    forceGenerateNewPawn: true,
    ...
    fixedBiologicalAge: 20f,
    fixedGender: Gender.Male,
    forceNoIdeo: true,        // 第 36 个实参
    forceNoBackstory: false,  // 第 37 个实参 ← 根因
    ...
    forceNoGear: true));
```

`forceNoBackstory = false` 会随机生成童年/成年背景故事；随机背景可能带
`WorkTags.Crafting` / `Cooking` / `Artistic` 等 `workDisables`。

### 2.2 背景故事的禁用标签进入 `CombinedDisabledWorkTags`

`RimWorld.Pawn_StoryTracker.DisabledWorkTagsBackstoryAndTraits`：

```csharp
WorkTags workTags = WorkTags.None;
if (childhood != null)  workTags |= childhood.workDisables;
if (adulthood != null)  workTags |= adulthood.workDisables;
for (int i = 0; i < traits.allTraits.Count; i++)
    if (!traits.allTraits[i].Suppressed)
        workTags |= traits.allTraits[i].def.disabledWorkTags;
return workTags;
```

`RimWorld.Pawn.CombinedDisabledWorkTags` 还会叠加
`genes.DisabledWorkTags`、Hediff 阶段的 `disabledWorkTags`、
`kindDef.disabledWorkTags`、Ideo 角色、QuestPart 等。

### 2.3 禁用后 `Level` 永远读 0

`RimWorld.SkillRecord`：

```csharp
public bool TotallyDisabled
{
    get
    {
        if (cachedTotallyDisabled == BoolUnknown.Unknown)
            cachedTotallyDisabled = CalculateTotallyDisabled() ? BoolUnknown.True : BoolUnknown.False;
        if (PermanentlyDisabled) return true;
        return cachedTotallyDisabled == BoolUnknown.True;
    }
}

public int GetLevel(bool includeAptitudes = true)
{
    if (TotallyDisabled) return 0;       // ← Refresh 写的 levelInt 被忽略
    ...
}
```

`UFRecipes.MapComponent_UFAutoVirtualWorkerPool.Refresh()` 只写：

```csharp
if (workSkill != null && pawn.skills != null)
    pawn.skills.GetSkill(workSkill).Level = skillLevel;   // 只写 levelInt；只写一个技能
```

于是 `Refresh(pawn, Crafting, 15, ...)` 之后：

- `skill.levelInt == 15`
- `skill.TotallyDisabled == true`
- `skill.Level == 0`

### 2.4 红字与 0.75 产量

`Verse.GenRecipe.MakeRecipeProducts`：

```csharp
float efficiency = recipeDef.efficiencyStat != null
    ? worker.GetStatValue(recipeDef.efficiencyStat)
    : 1f;
```

屠宰机械体的 `RecipeDef` 在 `Data/Core/Defs/RecipeDefs/Recipes_Butchery.xml` 中：

```xml
<efficiencyStat>ButcheryMechanoidEfficiency</efficiencyStat>
```

`RimWorld.StatWorker.GetValueUnfinalized`：

```csharp
if (!stat.supressDisabledError && Prefs.DevMode && IsDisabledFor(req.Thing))
{
    Log.ErrorOnce($"Attempted to calculate value for disabled stat {stat}; ... (thing={req.Thing})", ...);
}
```

`StatWorker.IsDisabledFor` 检查 `skillNeedFactors` 的 `required` 技能：

```csharp
if (skillNeedFactor.required && pawn.skills.GetSkill(skillNeedFactor.skill).TotallyDisabled)
    return true;
```

`ButcheryMechanoidEfficiency` 的 `SkillNeed_BaseBonus(Crafting, 0.75, +0.025/级)`
在 0 级时得到 **0.75**；15 级应为 **1.125**。

> 注：本机 1.7 `Assembly-CSharp` 的 `StatDefOf` **没有**
> `ButcheryMechanoidEfficiency` 静态字段（该 stat 只通过 XML 的
> `RecipeDef.efficiencyStat` 引用）。离线验证因此构造了一个同名
> `StatDef`，其 `SkillNeed_BaseBonus` 参数与 vanilla XML 完全一致
> （0.75 + 0.025/级），用于复现 0.75 / 1.125。

## 3. 修复设计

接缝：`UFRecipes.MapComponent_UFAutoVirtualWorkerPool.TryAcquire(SkillDef, int, AutoWorkerIdeoMode, out Pawn)`。

Harmony Postfix：

```csharp
private static void TryAcquirePostfix(bool __result, ref Pawn result, int skillLevel)
{
    if (!__result || result == null) return;
    Normalize(result, skillLevel);
}
```

`Normalize`：

1. `story.Childhood` / `story.Adulthood` 若 `null`、slot 不匹配或
   `workDisables != WorkTags.None`，替换为 `workDisables == None` 的
   `BackstoryDef`（优先 `DefDatabase` 真实 Def；离线/无可用 Def 时使用
   合成 Def）。
2. 移除 `disabledWorkTags != None` 或 `disabledWorkTypes.Count > 0` 的
   Trait；同时移除对任意技能提供非零 `Aptitude` 的 Trait，保证
   `Level == skillLevel` 而不是 `skillLevel + aptitude`。
3. 移除 `disabledWorkTags != None` 或提供非零 Aptitude 的 Gene（Biotech）。
4. 移除 `CurStage.disabledWorkTags != None` 或提供非零 Aptitude 的 Hediff。
5. `pawn.Notify_DisabledWorkTypesChanged()`（内部
   `skills.Notify_SkillDisablesChanged()`）+ `skills.DirtyAptitudes()`。
6. 若仍有 `CombinedDisabledWorkTags != None`，记录警告并强制把
   `SkillRecord.cachedTotallyDisabled` / `cachedPermanentlyDisabled`
   置为 `BoolUnknown.False`。
7. 遍历 `pawn.skills.skills`，把每个技能的 `levelInt` 设为
   `clamp(skillLevel - aptitude, 0, 20)`，清零 `xpSinceLastLevel`。

### 3.1 为什么替换 Backstory 是安全的

反编译确认：

- `RimWorld.TaleData_Pawn.GenerateFrom(Pawn)`：

  ```csharp
  if (pawn.story != null)
      taleData_Pawn.title = pawn.story.title;   // string 字段，不是 BackstoryDef
  ```

  只读 `story.title`（可空 string），不读 `Childhood` / `Adulthood`。

- `RimWorld.CompArt.JustCreatedBy(Pawn)`：

  ```csharp
  public virtual void JustCreatedBy(Pawn pawn)
  {
      if (CanShowArt) authorNameInt = pawn.NameFullColored;
  }
  ```

  只读 `NameFullColored`，不读背景故事。

- `Pawn_StoryTracker.Childhood` 的 setter 会解引用 `value.spawnCategories`：

  ```csharp
  set
  {
      if (pawn.DevelopmentalStage.Adult() && value.spawnCategories.Contains("Child"))
          Log.Warning(...);
      backstoriesCache = null;
      childhood = value;
  }
  ```

  因此**绝不能**把 `Childhood` 设为 `null`；补丁始终赋一个有效
  `BackstoryDef`。

- 虚拟工具人由 UFLI 的 `MapComponent` 持有，但该 MapComponent **没有**
  `ExposeData`，`worker` 字段不存档；换背景故事不会进入存档。
  合成 Def 仅用于离线验证的兜底路径。

### 3.2 为什么要同步所有技能

- `GenRecipe.MakeRecipeProducts` 的 `efficiencyStat` 可能是 Crafting
  （机械体屠宰）或 Cooking（肉体屠宰）。
- `WeaponUpgrade` 的 Postfix 读取
  `worker.skills.GetSkill(SkillDefOf.Crafting).Level`。
- 只写 `workSkill` 会留下其他技能 0 级，仍可能触发
  `disabled stat` 或第三方补丁的 0 级惩罚。
- 因此补丁把所有 `skills.skills` 都设为传入的 `skillLevel`。

## 4. 离线 RED / GREEN 验证

驱动：`Validation/Program.cs`（net10.0，Mono.Cecil 绑定）。

`bind` 只把 QA 副本里的 `UnityEngine*` 引用改名为 `UnityStubs`，
不改真实游戏/工坊 DLL：

```
PASS: QA-only Unity reference binding; real game and UFRecipes method bodies remain unchanged.
```

### RED

```
RED: TotallyDisabled=True Level=0 IsDisabledFor=True efficiency=0.750
```

对应断言：

- `pawn.skills.GetSkill(SkillDefOf.Crafting).TotallyDisabled == true`
- `Level == 0`
- `ButcheryMechanoidEfficiency.Worker.IsDisabledFor(pawn) == true`
- `GetValueUnfinalized == 0.75`
- 捕获到 `disabled stat ButcheryMechanoidEfficiency` 错误文本

> 说明：离线 harness 构造的 `ButcheryMechanoidEfficiency` 只保留 vanilla
> `SkillNeed_BaseBonus(Crafting, 0.75, +0.025/级)`，`capacityFactors` 留空
> 以隔离技能因子。真实 stat 的 Sight/Manipulation 对健康成年虚拟工人为
> 1.0，因此 15 级同样是 1.125；随机健康/特性降低能力时会按原版
> `capacityFactors` 再缩放，不属于本次禁用技能 bug。

### GREEN

```
GREEN: all skills level=15, CombinedDisabledWorkTags=None, IsDisabledFor=False efficiency=1.125; TaleData_Pawn/CompArt OK.
```

GREEN 不是直接调用 `Normalize`，而是：

1. `VirtualWorkerFix.Apply()` 注册 Harmony；
2. `Harmony.GetPatchInfo(TryAcquire)` 确认 owner 包含
   `whitegiver.ufli.virtualworkerfix`；
3. 用反射预置 pool 的 `worker` 字段，调用**真实的**
   `TryAcquire(Crafting, 15, Null, out result)`；
4. 断言 `result` 就是该 pawn、Crafting 与 Cooking 均为 15、
   `CombinedDisabledWorkTags == None`、`IsDisabledFor == false`、
   efficiency `1.125`；
5. 调用 `TaleData_Pawn.GenerateFrom(pawn)` 与 `CompArt.JustCreatedBy(pawn)`
   均无异常。

> 离线 harness 的妥协：`ModsConfig` / `Prefs` 在 .NET 进程里没有 Unity
> 初始化，驱动使用临时 `fake-save/Config/ModsConfig.xml`、空 Mods 目录和
> `PrefsData.devMode = true` 让 vanilla 路径可运行；`PawnUtility.EverBeen*`
> 被测试用 Harmony 前缀固定为 false，避免触发 `Find.WorldPawns`。
> 这些只影响测试进程，不改变真实 DLL。

## 5. 部署

- 本地目录：`A:/SteamLibrary/steamapps/common/RimWorld/Mods/UFLIVirtualWorkerFix`
  - `About/About.xml`
  - `LoadFolders.xml`（v1.7: `/` + `1.7`）
  - `1.7/Assemblies/UFLIVirtualWorkerFix.dll`
  - `README.md`
- `ModsConfig.xml` 备份：
  `C:/Users/A1337/AppData/Local/Temp/opencode/ufli-virtualworker-fix-20261108/ModsConfig-before.xml`
  （sha256 `af3f7685bfb32b36293d317f8452663195890220812ad48fb20b5760286a823d`）
- 修改后：
  `.../ModsConfig-after.xml`（sha256 `347e4eff2d7a23bffcac5d6cfb9c8e58e790224fe6355f4a85cbd080d4d6d1a5`）
- 变更：在 `<activeMods>` 的 `kindseal.ufli` 之后插入
  `whitegiver.ufli.virtualworkerfix`。
- 校验：`activeMods` 条目 290 → 291；除新增一行外，其余顺序逐项完全一致
  （已用 XML 解析逐项比较）；文件仍是合法 XML。
- 生效：**重启 RimWorld 后**。本次部署时游戏进程已自行退出，未重启游戏。
- 部署后用户自行启动了 RimWorld（PID `86268`，启动时间 `2026/11/8 23:46:08`）。
  `Player.log` 第 2047 行确认 sidecar 已加载并打上补丁：

  ```
  2026-11-08 23:46:22.603  INFO [Verse] [UFLI Virtual Worker Fix]
  Patched UFRecipes.MapComponent_UFAutoVirtualWorkerPool.TryAcquire;
  pooled virtual workers are normalized on every acquire.
  ```

  当前日志中未再出现 `disabled stat ButcheryMechanoidEfficiency`，也没有
  提及 UFLI/UFRecipes 的 error/exception。全程未终止或重启该进程。

## 6. 已知边界 / 后续

- 若上游 UFLI 改了 `TryAcquire` 或 `Refresh` 签名，补丁会打印
  `Expected TryAcquire(...) and Refresh(...) were not found; patch not applied.`
  并跳过；按 README 退役。
- 若上游自行修复 `forceNoBackstory` 或做虚拟工具人归一化，本补丁应移除。
- 本补丁不改变 `ApplyFixedQuality` 的最终品质覆盖，也不把灵感/记录/
  食物中毒/金属恐怖等原版 worker 语义转移给真人 Holder。
- 虚拟工具人仍然不是真殖民者：不存档、不参与工作/需求/社交；补丁只
  修正它的技能/禁用状态，使它作为 vanilla API 的占位 worker 正常工作。
