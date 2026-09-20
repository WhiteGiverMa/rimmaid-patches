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

Historical local mirrors of full upstream mods are intentionally excluded. `archive/` is reserved for retired experiments; `tools/` is reserved for diagnostics that are not gameplay patches.

Changes that must remain inside a third-party project are captured as small recovery diffs under [`upstream-patches/`](upstream-patches/README.md). Direct Workshop XML edits are described there without republishing entire third-party mods.

## Local builds

See [BUILDING.md](BUILDING.md). Legacy projects require local game and Workshop references, which are not included here. Builds must not deploy to an installed game unless deployment is explicitly enabled. Retained DLLs are backup artifacts, not proof of a fresh build or game test.

## Licensing

There is no repository-wide license. Individual directories retain any license or attribution shipped with that patch. Third-party projects referenced by a patch remain governed by their own licenses.
