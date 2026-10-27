# Rimmaid Patches

[中文](README.zh-CN.md) | **Engligh**

Small, independently loadable RimWorld patch mods maintained by WhiteGiverMa.

This is a source and recovery monorepo. Each directory under `mods/` keeps its own `About.xml`, package ID, dependencies, load order, and deployment lifecycle.

## Repository policy

- `mods/` contains only patches authored or maintained locally.
- Upstream projects and full third-party mod mirrors remain in their own repositories and are not vendored here.
- Existing package IDs are preserved so installed games and mod lists keep working.
- Release assemblies may be committed with a mod when they are part of its deployable package; transient `bin/` and `obj/` output is excluded.
- Saves, logs, local configuration, diagnostics containing personal play state, and agent working files are never published.
- Each patch should document its dependency, failure chain, validation status, and retirement signal after an upstream update.

This is a snapshot migration. Earlier history stays in the original repositories listed in [MIGRATION.md](MIGRATION.md), which are archived only after the new snapshot is published and verified. Exact source commits are recorded for the five Git-backed imports; formerly unversioned patches are identified by source description and retained-binary hashes.

## Patches

See [`manifest.json`](manifest.json) for machine-readable metadata.

"Active" describes the local configuration at import, not compatibility with every current upstream release. No game session was used to validate this migration. The Fortified minification experiment is preserved for history but is known not to implement the claimed guard.

| Directory | Purpose | Status at import |
|---|---|---|
| `CEOverpenetration` | CE ballistic overpenetration | Active, game QA pending |
| `CEEliteCombatTweaks` | CE elite-colony aiming and cooldown tuning | Active, game QA pending |
| `CEBreachingFix` | CE breaching AI and firing-position guards | Active, game QA pending |
| `DMS_Synthetic_Apparel_Repair` | DMS apparel and weapon repair | Active, game QA pending |
| `Fix_ExosuitDummyMeat` | Prevent duplicate Exosuit dummy meat defs | Active |
| `BossgroupConcurrentSummons` | Optional concurrent bossgroup summons | Active, game QA pending |
| `DMSWorkTypeCacheFix` | Invalidate mech work-type cache after faction changes | Active, game QA pending |
| `KeyzAllowUtilitiesFinishOffFix` | Guard despawned Finish Off targets | Active, game QA pending |
| `WVC_CE_CompatPatch` | Cap incompatible large GenRadial searches | Active legacy sidecar; reconstructed source included |
| `DubsPerformanceAnalyzerFix` | Guard malformed DPA Tick Things entries | Active, game QA pending |
| `BugFix_CE_BodyShapeFallback` | CE body-shape and parry edge-case fixes | Active |
| `BugFix_FortifiedCE_MinifyNullGuard` | Historical attempt at a Fortified null guard | Known ineffective; do not install |
| `CE_Fix_IsHunkering_NRE` | CE suppression and tending guards | Active, game QA pending |
| `CE_Fix_MiliraFloatUnitElevation` | CE collision height and target policy for Milira float units | Active, game QA pending |
| `Fix_CeleTech_DuplicateEquippableComps` | Remove duplicate verb owners while preserving unequip behavior | Active, game QA pending |
| `Fix_DMS_BookGrammarAndHeavyShield` | DMS book grammar and shield body-part XML fixes | Active |
| `Fix_WRMegaCorp_QuestExposeData` | Remove duplicate quest deep serialization | Active, game QA pending |
| `LocalFix_MiliraFlightNullCheck` | Guard Milira flight tracker without a flight component | Active, game QA pending |
| `WolfeinWeaponBoxCE` | [Live Wolfein weapon-box CE capacity settings](mods/WolfeinWeaponBoxCE/README.md), default +350 bulk / +0 weight | Added 2026-09-20, game QA pending |
| `WeaponStationPickTarget` | [Weapon Station left-click weapon picker](mods/WeaponStationPickTarget/README.md); right-click keeps the original list | Added 2026-10-18, game QA pending |
| `WeaponStationCERecoilFix` | [Weapon Station CE recoil compatibility](mods/WeaponStationCERecoilFix/README.md); preserve special weapon attacks and normal CE animations | Added 2026-10-20, user-confirmed game QA |
| `CeleTechManagedWorkTableFix` | [CeleTech automation-hub fix](mods/CeleTechManagedWorkTableFix/README.md); snapshot material enumeration and supply default stuff for stuff-based products | Added 2026-10-20, v1.1.0 2026-10-27, user-confirmed game QA |
| `AutoAbilityNullTargetFix` | [Auto Ability null-target fix](mods/AutoAbilityNullTargetFix/README.md); null-safe friendly-fire AOE target checks in MyValidate | Added 2026-10-27, local sidecar, user-confirmed game QA |
| `CicadaVolumeControl` | [Independent cicada/insect ambience volume](mods/CicadaVolumeControl/README.md); live 0–100% Mod Settings slider, no Harmony | Added 2026-10-23, game QA pending |
| `SunlightFuelGeneratorFacility` | [Solar-fuel facility](mods/SunlightFuelGeneratorFacility/README.md); citadel art/footprint, Milira solar fuel, adjustable power/capacity/consumption, eject-fuel gizmo also patched onto the citadel generator | Added 2026-10-25, user-confirmed game QA |
| `ReinforcedConcreteHeavyBridge` | [Reinforced concrete heavy bridge](mods/ReinforcedConcreteHeavyBridge/README.md); adjustable 14 concrete/cell default, steel/concrete automatic bridge choice, uninstall conversion and generated Workshop preview | Added 2026-10-25, game QA pending |
| `ReplaceStuffBridgeReplacement` | [In-place bridge replacement](mods/ReplaceStuffBridgeReplacement/README.md); place vanilla or reinforced-concrete heavy bridge blueprints directly on existing bridges, with vanilla-ratio refunds after successful completion | Added 2026-10-25, game QA pending |
| `MilianWeaponInteractions` | [Milian ground weapon interactions](mods/MilianWeaponInteractions/README.md); left-click weapon-cell picker, original right-click list and selected-Milian ground equip menu | Added 2026-10-26, local sidecar, game QA pending |
| `DefaultsWorkPrioritiesLifeStageFix` | [Defaults custom child life-stage compatibility](mods/DefaultsWorkPrioritiesLifeStageFix/README.md); fix hardcoded HumanlikeChild lookups in work priorities and birthday policies | Added 2026-10-26, local sidecar, game QA pending |
| `WeaponStationFireModes` | [Weapon Station fire modes and equipped offsets](mods/WeaponStationFireModes/README.md); VFM modes, automatic selection, weapon profiles and equipped offsets for unmanned stations only; preserve manned operator logic | Added 2026-10-27, local sidecar, game QA confirmed by user |
| `MiliraDailyExpendRecipeFix` | [Milira Daily Expend nutrition accounting](mods/MiliraDailyExpendRecipeFix/README.md); switch 20 cooking recipes to nutrition-based ingredients, reprice soy sauce and jam, fix bulk ratios and three over-generous recipes; XML only | Added 2026-10-27, local sidecar, game QA confirmed by user |

Historical local mirrors of full upstream mods are intentionally excluded. `archive/` is reserved for retired experiments; `tools/` is reserved for diagnostics that are not gameplay patches.

Changes that must remain inside a third-party project are captured as small recovery diffs under [`upstream-patches/`](upstream-patches/README.md). Direct Workshop XML edits are described there without republishing entire third-party mods.

## Local builds

See [BUILDING.md](BUILDING.md). Legacy projects require local game and Workshop references, which are not included here. Builds must not deploy to an installed game unless deployment is explicitly enabled. Retained DLLs are backup artifacts, not proof of a fresh build or game test.

## Licensing

There is no repository-wide license. Individual directories retain any license or attribution shipped with that patch. Third-party projects referenced by a patch remain governed by their own licenses.
