# DMS 复乐园兼容军团参数调节 / Synthriada Legion Customization Compat

| 项目 / Item | 值 / Value |
| --- | --- |
| packageId | `whitegiver.dmssr.legioncustomizationcompat` |
| 版本 / Version | 1.0.0 |
| 支持 / Supported | RimWorld 1.7 |
| 依赖 / Depends on | Harmony (`brrainz.harmony`), Fortified (`AOBA.Framework`, Steam `3498575851`), DMS Legion (`Srxf.DeadManSwitch.Legion`, Steam `3666117000`), DMS Synthriada (`Cephalopod.DeadManSwitch.Synthriada`, Steam `3835512735`) |
| 目标 / Target | `DMSSR.UI.MechCustomizationUtility.IsSupportedPawn` + `DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs` |
| 类型 / Type | 两条 Harmony Postfix，不改 DLL/XML，不新增 Def，不写存档字段 / two Harmony postfixes, no DLL/XML edits, no new Defs, nothing scribed |

---

## 1. 症状 / Symptom

给《死手开关：军团》的机兵（`DMSL_Mech_leopard` 豹、`DMSL_Mech_AgriculturalFrame` 农业框架、`DMSL_Mech_ZBZ59D`、`DMSL_Mech_Terra` 尘世……）打开改装界面时：

- 只出现 Fortified 的普通 `Window_MechModification`：左侧部位、右侧已装改装、底部应用/重置/返回；
- **没有** Synthriada 的“机兵参数调节”面板：没有供给/算力/电控/驱动/子系统的五条指数滑条，没有上限刻度、没有“当前/预览”上限、没有协议模拟按钮与结果条；
- 同一套 Mod 列表下，`DMS_Mech_*`（例如 `DMS_Mech_Krepost`）和 `Cinder_Mech_*` 的窗口是完整的。

When opening the modification window for a Legion mech (`DMSL_Mech_leopard`, `DMSL_Mech_AgriculturalFrame`, `DMSL_Mech_ZBZ59D`, `DMSL_Mech_Terra`, …), only Fortified's plain `Window_MechModification` appears. Synthriada's five-parameter chassis panel — supply / computation / electro / drive / subsystem sliders, cap ticks, preview caps, and the protocol-simulation panel — is missing. `DMS_Mech_*` and `Cinder_Mech_*` mechs show the full window with the same mod list.

---

## 2. 根因 / Root cause

### 2.1 调用链 / Call chain

```
Fortified.MechModificationWindowUtility.OpenFor(pawn)
  -> CanOpenFor(pawn)                         // spawned, player faction, mechanoid, MicroelectronicsBasics
  -> for each registered factory: factory(pawn)
       -> DMSSR.UI.MechCustomizationUtility.CreateWindowFor(pawn)   // registered in its static ctor
            -> if (!IsSupportedPawn(pawn)) return null;
            -> return new Window_MechCustomization(pawn);
  -> first non-null window is added
  -> if every factory returned null: new Window_MechModification(pawn)   // plain fallback
```

`MechCustomizationUtility.CanOpenFor(pawn)` also calls `IsSupportedPawn(pawn)` after the Fortified gate, so both the public entry point and the factory use the same predicate.

### 2.2 上游代码 / Upstream code

`DMSSR.decompiled.cs` lines 4799–4861 (`DMSSR.UI.MechCustomizationUtility`):

```csharp
private static Window CreateWindowFor(Pawn pawn)
{
    if (!IsSupportedPawn(pawn)) return null;
    return new Window_MechCustomization(pawn);
}

private static bool IsSupportedPawn(Pawn pawn)
{
    string text = pawn?.def?.defName ?? string.Empty;
    if (!text.StartsWith("DMS_Mech_") && !text.Contains("DMS_Mech"))
        return text.StartsWith("Cinder_Mech_");
    return true;
}
```

### 2.3 为什么军团机兵失败 / Why Legion fails

Legion's entire mech family is named `DMSL_Mech_*`:

- `StartsWith("DMS_Mech_")` is **false** for `DMSL_Mech_leopard` (the fifth character is `L`, not `_`);
- `Contains("DMS_Mech")` is also **false** — the string is `DMSL_Mech`, which does not contain the contiguous substring `DMS_Mech`;
- `StartsWith("Cinder_Mech_")` is false.

Therefore `IsSupportedPawn` returns `false`, `CreateWindowFor` returns `null`, and Fortified's factory loop falls through to the plain `Window_MechModification`. The five-parameter panel is never constructed. This is a pure name-gate bug; the rest of the customization system already works on Legion bodies (verified in RED: `GetOrCreateCustomizationComp` and `GetParamCaps` succeed before any patch).

---

## 3. 全量硬编码前缀审计 / Complete hardcoded-prefix audit

The audit covered every `StartsWith`/`Contains` occurrence in the decompiled `DMSSR.dll`. Four of them are mechanoid-def gates; the others are unrelated string prefixes.

| # | 位置 / Location (decompiled lines) | 判定 / Predicate | 用途 / Used by | 处理 / Decision |
| --- | --- | --- | --- | --- |
| 1 | `MechCustomizationUtility.IsSupportedPawn` 4815–4822 | `DMS_Mech_`, `Contains("DMS_Mech")`, `Cinder_Mech_` | `CanOpenFor` + the Fortified window factory | **Patched** (postfix false→true for `DMSL_Mech_`) |
| 2 | `GeneSetUtility.IsDmsMechPawn` 18917–18925 | `DMS_Mech_` only | only `Patch_GeneSet_ImprintAuthority` (`GenHostility.HostileTo`) | **Not patched** — adding `DMSL_Mech_` would make every Legion mech non-hostile to any pawn carrying the imprint-authority gene/hediff, changing combat balance far beyond customization |
| 3 | `CompMechCarrier.HasAllowedMechPrefix` 22908–22918 | `DMS_Mech_`, `DMSSR_Mech_`, `Cinder_Mech_` | `EnsureMountedSpawnCandidatesCached` → random auto-spawned carrier passengers | **Not patched** — adding `DMSL_Mech_` would let DMS carriers spawn Legion mechs as random mounted passengers |
| 4 | `Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs` 37234–37278 | `DMS_Mech_` | grants `DMS_MechCustomization` to already-spawned mechs when MicroelectronicsBasics finishes | **Patched** (postfix performs the same scan for `DMSL_Mech_`) |

非机兵前缀 / unrelated prefixes (out of scope, untouched): `DMSSR_ACC_Crop_` (gene-library crop recipes), `DMSSR_Mounted_` (turret IDs), `DMS_AccessConsole_` (access console buildings), `Shell_BlasterSmoke_Releasing` (weather shell), and specific-def checks such as `DMS_Mech_Hydrake`, `DMS_Mech_Shell`, `DMS_Mech_Scourge`, `DMS_Mech_Bullfrog`, `DMS_Mech_Crank`, `DMS_Mech_Facedancer`. None of these gate mech customization, and none can match a `DMSL_Mech_*` def.

---

## 4. 本补丁做了什么 / What this patch does

Two postfixes, both installed by `DMSSRLegionCustomizationCompatMod` with Harmony id `whitegiver.dmssr.legioncustomizationcompat`:

1. `DMSSR.UI.MechCustomizationUtility.IsSupportedPawn(Pawn)` — if upstream returned `false` and `pawn.def.defName` starts with `DMSL_Mech_`, set the result to `true`. `CanOpenFor`, the Fortified factory, and `MechCustomizationUtility.OpenFor` then behave exactly as they already do for `DMS_Mech_*`.
2. `DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs()` — after the upstream scan, grant the same `DMS_MechCustomization` hediff to every spawned, non-dead, mechanoid `DMSL_Mech_*` pawn that does not already have it. It runs only from the same MicroelectronicsBasics completion event as upstream, so the timing and save semantics match.

The patch deliberately does **not**:

- construct its own window, copy the slider/commit code, or inject recipes;
- add any `HediffDef`, `ThingDef`, `RecipeDef`, `ResearchProjectDef`, or XML patch;
- scribe its own fields;
- change any stat, capacity, pathing, or work code.

The five-parameter panel, cap computation, preview caps, `SetParams`, Scribe persistence, protocol simulation, presets, and modification slots are all upstream DMSSR/Fortified code.

---

## 5. 下游接缝审计 / Downstream seam audit

### 5.1 `Window_MechCustomization` (lines 4862–~6000)

| 接缝 / Seam | 检查结果 / Result |
| --- | --- |
| `OnCorePreOpen` | Calls `GetOrCreateCustomizationComp` then `MechCustomizationCapUtility.GetParamCaps`; no name check, no Legion-specific assumption. |
| `DrawRight` / `DrawSliders` | Draws the five sliders only after `DMS_Hybrid` research; generic `Mathf`/`Widgets` code. |
| `ValidateBeforeCommit` / `CommitCustomSettings` | Recomputes caps and calls `HediffComp_MechCustomization.SetParams`; generic. |
| `GetPreviewCaps` | Rebuilds the part list from installed `HediffComp_Modification` hediffs and queued installs; generic. |
| `GetAdditionalInstallItemDefs` / `AddCustomTargetPartDefs` / `CustomPartMatches` / `TryBuildCustomInstallEntry` / `TryRestoreCustomInstallEntry` | All gated by `SyntheticBionicUtility.IsSupportedSyntheticBody`; returns empty for Legion bodies (see 5.5). No crash. |
| `TryCreateCustomJob` / `GetCustomQueueLabel` / protocol queue | `DMSSR.ProtocolSimulation` entry is accepted; job creation is still gated by `DMSSR_PulsatedEmbryte`. Verified with a real `MechModificationQueueEntry`. |
| Preset save/load | `Fortified.MechModificationPresetUtility` stores `MechModificationPreset` in `FFF_MechModificationPresets`; no DMS prefix or mech-family check. |
| Footer icon/textures | Upstream `ContentFinder` path; unchanged. |

### 5.2 `MechCustomizationCapUtility` (lines 32082–32282) vs Legion bodies

`GetParamCaps` walks every hediff carrying `HediffComp_Modification` and applies part bonuses from `BodyPartDef.tags`:

- `BloodPumpingSource` / `MetabolismSource` → +0.50 supply
- `ConsciousnessSource`, or `Head` / `FullHead` / `UpperHead` / `Eyes` groups → +0.25 compute
- `ManipulationLimbCore` / `Segment` / `Digit` → +0.25 electro
- `MovingLimbCore` / `Segment` / `Digit` → +0.25 drive
- anything else → +0.05 to compute/electro/drive/subsystem

The installed Legion 1.7 tree uses eight bodies:

| BodyDef | 机兵 / Mechs |
| --- | --- |
| `DMS_Bipedal` | AgriculturalFrame, DoctorFrame, DrillingBarge, SupportFrame, IronFace, leopard, ZBZ59D |
| `DMS_Humanoid_TypeA` | ArmoredGrenadiers, Arsonist, Commandos, DesignatedMarksman, Engineer, Guardian, Shaman, SpecialPolice |
| `DMS_Humanoid_TypeB` | Commander |
| `DMSL_MechanicsCavalryman_TypeA` | Scout |
| `DMSL_Body_Terra` | Terra |
| `DMS_KillDozer` | panzer |
| `DMS_LandCrab` | STGeorge (body defined by DMS AncientCorps, Steam `3469398006`) |
| `DMS_Chop` | CommandingBird drone — deliberately excluded |

注：安装的 `3666117000/1.7` 实际包含 **20 个 `DMSL_Mech_*` 与 1 个 `DMSL_Drone_CommandingBird`**（共 21 个机兵单位）；上表按实际内容审计。任务简报中的“22”比实际安装内容多 1，验证以真实 XML 为准。
Note: the installed `3666117000/1.7` tree contains **20 `DMSL_Mech_*` defs and 1 `DMSL_Drone_CommandingBird`** (21 mech units total). The table follows the installed content; the brief's “22” is one higher than what is actually installed, and validation follows the real XML.

All seven mech bodies contain vanilla/DMS tag-bearing parts (`MechanicalArm`, `MechanicalHand`, `MechanicalLeg`, `MechanicalFoot`, `MechanicalFinger`, `ArtificialBrain`, `DMS_HeavyChassis`, `DMS_Battery`, `Reactor`, `Head`, `Eye`, `Leg`, `Arm`, `Foot`, etc.). The offline harness parsed the real `BodyPartDef` tags and real `BodyDef` trees and opened `Window_MechCustomization` for **20/20** `DMSL_Mech_*` defs; `OnCorePreOpen` computed valid caps for every body. No body needs a compatibility exception.

### 5.3 `MechCustomizationHeteroUtility` / `MechCustomizationRecipeUtility` (lines 32283–32650)

- `GetBaseImmunity` checks whether the mech's *gestator recipe* (`RecipeDef.products` containing the mech def) uses `SiliCompound`. No Legion 1.7 recipe contains `SiliCompound`, so the base hetero immunity remains 1.1 — the upstream default, not a Legion-specific failure.
- `GetTotalDeathCheatCharges`, `CountInstalledMods`, `TryApplyDegenerativeSyndrome` are plain hediff/part scans and work on any humanlike/mechanoid body.
- `GetMechRecipe` caches by `ThingDef` and reads `RecipeDef.products`; it has no name prefix and does not depend on `ThingDef.recipes`/`recipeUsers`.
- DMSSR does **not** inject a customization recipe into `DMS_Mech_*` or `Cinder_Mech_*` in C# or XML. The only XML patches in `3835512735/1.7/Patches` are the spawner options and the corpse/resurrection list; neither is required for the window. Fortified's modification items are discovered from `CompProperties_AddHediffOnTarget`, so Legion mechs use the same item pool as any other mechanoid.

### 5.4 `Patch_MechCustomization_UnlockOnResearch` (lines 37210–37278)

Upstream `ApplyToSpawnedMechs` scans `Find.Maps` → `mapPawns.AllPawnsSpawned`, requires `RaceProps.IsMechanoid`, `defName.StartsWith("DMS_Mech_")`, and no existing `DMS_MechCustomization` hediff. The postfix repeats the scan for `DMSL_Mech_*` only. It runs after the same `MicroelectronicsBasics` `Prefix`/`Postfix` state check, so it cannot fire on an unrelated research. The drone is excluded by the `DMSL_Mech_` prefix.

在已有存档中，如果 MicroelectronicsBasics 早已完成，这个“研究完成”扫描不会追溯执行；第一次打开某台军团机兵的改装窗口时，`Window_MechCustomization.OnCorePreOpen` 会像上游对 `DMS_Mech_*` 一样调用 `GetOrCreateCustomizationComp` 创建 hediff。两种路径得到的是同一个 `DMS_MechCustomization`。
On an existing save where MicroelectronicsBasics is already finished, the completion scan does not run retroactively; the first time a Legion mech's customization window opens, `Window_MechCustomization.OnCorePreOpen` calls `GetOrCreateCustomizationComp` and creates the same `DMS_MechCustomization` hediff, exactly as upstream does for `DMS_Mech_*`.

### 5.5 `SyntheticBionicUtility` (lines 27486–27800) and Terra

Upstream's synthetic body whitelist is exactly `{ "DMS_Synthroid", "DMS_Armored_Synthroid_Cephalopoda" }`. `DMSL_Body_Terra` is **not** added, by design:

- `DMSL_Body_Terra` is a humanlike body (`Torso`, `Head`, `Skull`, `Brain`, `Shoulder`, `Arm`, `Hand`, `Leg`, `Foot`, `Eye`, `Neck`, `Waist`) explicitly built so Terra can wear most human apparel. It is not a wetware synthroid chassis.
- `DMS_Synthroid` uses a different part set: `DMS_SynthicArm`, `DMS_SynthicHand`, `DMS_SynthicLeg`, `DMS_SynthicFoot`, `SightSensor`, `HearingSensor`, plus organic-style organs. The synthetic install recipes and alias table were written for that structure.
- Adding Terra would let synthetic added-part hediffs replace its vanilla limbs, with possible apparel/graphic/part-efficiency side effects. No Legion XML asks for this.
- Terra still gets the full five-parameter panel and correct caps: the harness installs a `DMS_SyntheticTendon` on a vanilla `Leg` and gets `driveMax = 1.50`, while `IsSupportedSyntheticBody(Terra)` stays `false` and `GetAdditionalInstallItemDefs` stays empty. The `DMS_Synthroid` control returns `true`, proving the whitelist itself is intact.

If the Synthriada author later confirms Terra should be a wetware synthetic body, that is a one-line upstream whitelist addition and a separate design decision; this compatibility mod deliberately does not make it silently.

### 5.6 `CompMechCarrier.HasAllowedMechPrefix` / `GeneSetUtility.IsDmsMechPawn`

Both are real omissions of `DMSL_Mech_`, but neither belongs to customization:

- `HasAllowedMechPrefix` feeds `EnsureMountedSpawnCandidatesCached`; adding Legion mechs would let any DMS carrier randomly spawn them as passengers. That is a gameplay/balance change with no customization benefit.
- `IsDmsMechPawn` feeds only `Patch_GeneSet_ImprintAuthority`, which suppresses hostility toward a pawn with imprint-authority genes/hediffs. Adding Legion mechs would make every Legion mech (including hostiles) refuse to attack such a colonist.

They are documented here so the next maintainer does not mistake them for missed customization seams.

### 5.7 Drone

`DMSL_Drone_CommandingBird` is a `DMS_BaseDroidRace` drone with `DMS_Chop` body and no `CompMechCarrier`/`CompDrone` comp. Upstream also excludes its own `DMS_Drone_*` family from `IsSupportedPawn`. The compatibility prefix intentionally matches only `DMSL_Mech_`, so the drone keeps the plain Fortified modification window and never receives the parameter panel or the research-time hediff. Verified in both RED and GREEN.

---

## 6. 验证 / Validation

Everything is offline against the real installed assemblies:

```
A:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll
A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll
A:\SteamLibrary\steamapps\workshop\content\294100\3835512735\1.7\Assemblies\DMSSR.dll
A:\SteamLibrary\steamapps\workshop\content\294100\3498575851\1.7\Assemblies\Fortified.dll
A:\SteamLibrary\steamapps\workshop\content\294100\3666117000\1.7\Assemblies\DMS_Legion.dll
A:\SteamLibrary\steamapps\workshop\content\294100\3819799478\1.7\Assemblies\DMS.dll
```

The harness also parses the real XML:

- vanilla `Data/Core/Defs/Bodies/BodyParts_*.xml`;
- DMS Core `1.7/Defs/Things_Race/BodyPartsDef*.xml`;
- DMS Legion `Defs/Defs/ThingDefs_Races/*.xml` + `1.7/AncientCorps/Defs/ThingDefs_Races/*.xml`;
- DMS AncientCorps `1.7/Defs/BodyDef/*.xml` (for `DMS_LandCrab`).

Run:

```powershell
pwsh -File Validation/validate.ps1
```

- **bind** writes a QA-only `Assembly-CSharp.dll` with `Verse.ModsConfig..cctor` stubbed out, so the harness never reads the user's `ModsConfig.xml` or saves. The installed game DLL and all Workshop DLLs are untouched.
- **RED** (no mod): 20/20 `DMSL_Mech_*` pawns return `IsSupportedPawn=false` and `CreateWindowFor=null`; `DMS_Mech_Krepost` and `Cinder_Mech_Test` return true/`Window_MechCustomization`; `Mech_Centipede`, `Human`, `Chicken`, and `DMSL_Drone_CommandingBird` are rejected; `GetOrCreateCustomizationComp` and base caps already work on the leopard. **107 assertions.**
- **GREEN**: 20/20 Legion mechs return true, create `Window_MechCustomization`, and survive `PreOpen`; the real Fortified factory list selects the DMSSR window; `MechModificationWindowUtility.OpenFor` and `MechCustomizationUtility.OpenFor` both add `Window_MechCustomization` for Legion and the plain `Window_MechModification` for `Mech_Centipede`; caps with tendon/frame/battery are `1.75 / 1.30 / 1.30 / 1.55 / 1.30`; `SetParams` clamps and writes all derived coefficients; a 26-field Scribe round-trip restores every parameter and coefficient exactly; `CommitCustomSettings` writes the five sliders; the protocol entry is accepted and remains research-locked; the MicroelectronicsBasics postfix grants exactly one hediff to spawned Legion mechs and none to the drone; Terra opens the window with vanilla-body caps and stays outside the synthetic whitelist; `DMS_Synthroid` remains inside it. **179 assertions.**

Harness-only shims (never present in the mod): `HediffSet.DirtyCache()` and `Pawn_HealthTracker.CheckForStateChange()` are no-ops because they touch Unity renderers/portraits and full needs state; `Prefs.data` is filled with defaults so `ScribeLoader.FinalizeLoading` does not read a real user config.

---

## 7. 失效条件 / Expiration conditions

This mod is intentionally a narrow, self-expiring compatibility layer. Remove it when any of the following becomes true:

1. **Upstream accepts `DMSL_Mech_`** in `MechCustomizationUtility.IsSupportedPawn` (and, for parity, grants the hediff in `ApplyToSpawnedMechs`). The postfix is then a no-op (`__result` is already true), but the mod is redundant; unsubscribing is cleaner.
2. **DMSSR renames/removes those two methods.** `AccessTools` cannot resolve them; the mod logs `Could not resolve DMSSR.UI.MechCustomizationUtility.IsSupportedPawn...` or `...ApplyToSpawnedMechs...` and installs only the part that still resolves. It never throws into the game. Update the type/method names or drop the mod.
3. **DMS Legion renames `DMSL_Mech_*`.** The prefix no longer matches; the mod becomes inert for the new names. Update `LegionMechUtility.LegionMechPrefix`.
4. **Fortified changes the window factory contract** (no longer registers/iterates `Func<Pawn, Window>` or no longer falls back to `Window_MechModification`). This mod does not patch Fortified; it relies on that public integration point.
5. **RimWorld leaves 1.7 / DMSSR or Legion changes its assembly identity.** Re-validate; the About declares only 1.7.
6. **Another mod postfixes `IsSupportedPawn` and forces `false` after this one.** Harmony postfix order depends on priorities; in that case the other mod wins. This mod uses the default priority on purpose so it composes rather than overrides.
7. **The user removes DMSSR or Legion.** The declared dependencies prevent loading; the mod does not try to run without them.
8. **Removing this mod from an existing save.** No custom defs or scribed fields exist; the `DMS_MechCustomization` hediff is DMSSR's own def and remains valid. Removing the compatibility layer simply returns Legion mechs to the plain modification window.

---

## 8. 安全与组合 / Safety and composition

- **No overlap with performance patches.** This mod patches only `MechCustomizationUtility.IsSupportedPawn` and `Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs`. The parallel `DMSSRStatPathPerfFix` patches `AbilityPathCostUtility.ClampPathCostForAbility`, `HediffSet.DirtyCache`, `Patch_MechCustomization_ApplyStatPreFinalize.Postfix`, and its private `TryGetCompFast` helper. The method sets are disjoint; Harmony can install both in either order.
- **No stat/path changes.** The mod never touches `Patch_MechCustomization_ApplyStatPreFinalize`, `Patch_MechCustomization_ApplyCapacity`, `Patch_MechCustomization_BodySize`, `Patch_MechCustomization_MechEnergy*`, `Patch_MechCustomization_MechWorkSkill`, or `AbilityPathCostUtility`.
- **No Def/XML edits.** It relies on DMSSR's existing `DMS_MechCustomization` HediffDef and Fortified's existing modification items.
- **No save schema.** Nothing is scribed by the mod; the only persisted object is the upstream hediff, which already has a stable Scribe layout.
- **No `ModsConfig.xml` changes, no Workshop file changes, no git operations** were performed by the validation harness.

---

## 9. 作者反馈 / Author feedback

A ready-to-send Chinese + English report for the Synthriada author is kept separately in [`AUTHOR-FEEDBACK.md`](AUTHOR-FEEDBACK.md). It contains only this customization-compatibility request, the exact predicate, the 20 Legion def names, and the two minimal upstream changes; performance topics are intentionally excluded.

### 中文草稿（可直接发送，完整版见 AUTHOR-FEEDBACK.md） / Chinese draft

> **主题：`MechCustomizationUtility.IsSupportedPawn` 未接受 `DMSL_Mech_*`，军团机兵无法打开“机兵参数调节”**
>
> 你好，我在 RimWorld 1.7 下同时使用 Synthriada（`3835512735`）与 Legion（`3666117000`）。对 `DMSL_Mech_leopard`、`DMSL_Mech_AgriculturalFrame`、`DMSL_Mech_ZBZ59D`、`DMSL_Mech_Terra` 等军团机兵打开改装时，只出现 Fortified 的普通 `Window_MechModification`，没有你的五条参数滑条、上限刻度和协议模拟面板；同样列表下 `DMS_Mech_Krepost` 和 `Cinder_Mech_*` 是完整的。
>
> 根因是 `DMSSR.UI.MechCustomizationUtility.IsSupportedPawn`（反编译约 4815–4822 行）只接受 `DMS_Mech_` / `Contains("DMS_Mech")` / `Cinder_Mech_`。军团 20 台机兵全部是 `DMSL_Mech_*`，既不满足前缀，也不包含连续子串 `DMS_Mech`（实际是 `DMSL_Mech`），于是 `CreateWindowFor` 返回 null，Fortified 回退到普通窗口。
>
> 建议的最小修改：在 `IsSupportedPawn` 中加入 `text.StartsWith("DMSL_Mech_")`；并在 `DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs()`（约 37234–37278 行）的 `DMS_Mech_` 判断旁边加入 `DMSL_Mech_`，让已生成的军团机兵在 MicroelectronicsBasics 完成时也得到 `DMS_MechCustomization`。也可考虑公开一个 `DefModExtension` 让机兵 Mod 自行声明支持。
>
> 我验证了军团使用的 8 种 BodyDef 与 20/20 台机兵：`GetParamCaps` 能正确读取 `MechanicalLeg` / `DMS_Battery` / `ArtificialBrain` 等部件标签，`SetParams` 与 Scribe 往返正常。`DMSL_Drone_CommandingBird` 不建议纳入（上游 `DMS_Drone_*` 也不在参数调节内）。另外 `DMSL_Mech_Terra` 使用人类装备兼容的 `DMSL_Body_Terra`，不是 `DMS_Synthroid` 湿件身体；我没有把它加进 `SupportedBodyDefNames`，如果你认为它应算湿件合成体，请告诉我。
>
> 这份反馈只涉及参数调节窗口，不包含性能/寻路/属性计算话题。

### English draft (ready to send; full version in AUTHOR-FEEDBACK.md)

> **Subject: `MechCustomizationUtility.IsSupportedPawn` rejects `DMSL_Mech_*`, so Legion mechs cannot open the customization window**
>
> Hello, I am running RimWorld 1.7 with Synthriada (`3835512735`) and Legion (`3666117000`). Opening the modification window for `DMSL_Mech_leopard`, `DMSL_Mech_AgriculturalFrame`, `DMSL_Mech_ZBZ59D`, `DMSL_Mech_Terra`, etc. shows only Fortified's plain `Window_MechModification`; your five-parameter sliders, cap ticks, and protocol panel are missing. `DMS_Mech_Krepost` and `Cinder_Mech_*` are complete with the same mod list.
>
> The cause is `DMSSR.UI.MechCustomizationUtility.IsSupportedPawn` (decompiled around lines 4815–4822), which accepts only `DMS_Mech_` / `Contains("DMS_Mech")` / `Cinder_Mech_`. All 20 Legion mechs are `DMSL_Mech_*`, which fails both the prefix and the contiguous substring (`DMSL_Mech`), so `CreateWindowFor` returns null and Fortified falls back.
>
> Minimal fix: add `text.StartsWith("DMSL_Mech_")` to `IsSupportedPawn`, and add `DMSL_Mech_` to the `DMS_Mech_` test in `DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs()` (around lines 37234–37278) so already-spawned Legion mechs receive `DMS_MechCustomization` when MicroelectronicsBasics finishes. A public `DefModExtension` opt-in would also work.
>
> I validated the 8 Legion BodyDefs and 20/20 mechs: `GetParamCaps` reads `MechanicalLeg` / `DMS_Battery` / `ArtificialBrain` tags correctly, and `SetParams` plus Scribe round-trip are exact. I would not include `DMSL_Drone_CommandingBird` (upstream excludes `DMS_Drone_*` too). `DMSL_Mech_Terra` uses the humanlike `DMSL_Body_Terra`, not the `DMS_Synthroid` wetware body; I did not add it to `SupportedBodyDefNames` — if you consider Terra a wetware synthetic, please say so.
>
> This report is only about the customization window; it contains no performance, pathing, or stat-calculation topics.
