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
| `MilianWeaponInteractions` | [Milian ground equipment interactions](mods/MilianWeaponInteractions/README.md); left-click weapon-cell picker, original right-click list and selected-Milian ground weapon/apparel equip menu | v1.1.0 2026-11-11, 100 real-DLL checks passed, in-game apparel QA confirmed by user |
| `DefaultsWorkPrioritiesLifeStageFix` | [Defaults custom child life-stage compatibility](mods/DefaultsWorkPrioritiesLifeStageFix/README.md); fix hardcoded HumanlikeChild lookups in work priorities and birthday policies | Added 2026-10-26, local sidecar, game QA pending |
| `WeaponStationFireModes` | [Weapon Station fire modes and equipped offsets](mods/WeaponStationFireModes/README.md); VFM modes, automatic selection, weapon profiles and equipped offsets for unmanned stations only; preserve manned operator logic | Added 2026-10-27, local sidecar, game QA confirmed by user |
| `MiliraDailyExpendRecipeFix` | [Milira Daily Expend nutrition accounting](mods/MiliraDailyExpendRecipeFix/README.md); switch 20 cooking recipes to nutrition-based ingredients, reprice soy sauce and jam, fix bulk ratios and three over-generous recipes; XML only | Added 2026-10-27, local sidecar, game QA confirmed by user |
| `DMSMechStorage` | [DMS mech storage](mods/DMSMechStorage/README.md); mechanitor right-click sealing into native FFF capsules, native seal/release reuse, drafted-idle allowed, bandwidth release, ground weapon draw, safe autonomous/commander restoration; DMS + Cinders Tombstone only | Added 2026-10-28, v1.1.4, game QA confirmed by user |
| `StartCarryReentrancyFix` | [StartCarry reentrancy fix](mods/StartCarryReentrancyFix/README.md); stop stale pickup callbacks after failed carry/job replacement, preserving pooled replacement targets and normal full/partial carrying | Added 2026-10-30, real callback red/green passed, game QA pending |
| `DMSFloggerWeaponTags` | [AncientCorps Flogger equipment tags](mods/DMSFloggerWeaponTags/README.md); append Heavy and V_Gatling30mm to this weapon only, preserving existing tags and size 2.5 | Added 2026-11-02, XML-only sidecar, game QA confirmed by user |
| `DMSArcSprayFireMode` | [Player-only DMS direct/spray toggle](mods/DMSArcSprayFireMode/README.md); vanilla ShotReport or unchanged FFF spray, per-gun persistence; enemies always spray, CE no-op; v1.0.1 prevents More weapon sounds inherited-method aliases from overwriting the shot wrapper | 2026-11-10, 1041 baseline / 1083 sound-combination checks passed; game QA pending |
| `PUAHStaleEntryFix` | [Bulk Load stale PUAH entry cleanup](mods/PUAHStaleEntryFix/README.md); drop ghost Pick Up And Haul inventory references that are in neither `innerContainer` nor `CarriedThing` before Bulk Load's SoftlockCleaner runs, so the same drop attempt cannot repeat forever; a renamed upstream method logs a warning and skips | Added 2026-10-30, local sidecar, troubleshooting notes included; in-game log observation pending |
| `Fix_WRMegaCorp_QuestRewardMultiplier` | [WRMegaCorp quest reward multiplier](mods/Fix_WRMegaCorp_QuestRewardMultiplier/README.md); initialize the missing `questRewardMultiplier` slate variable in three WRMegaCorp quest scripts so rewards stop evaluating to zero; `PatchOperationConditional` skips quietly if upstream renames those Defs; XML only | Added 2026-11-09, XML syntax and XPath hits verified offline; game QA pending |
| `WhileYouAreNearbyMaintained` | [While You Are Nearby 1.7 maintenance fork](mods/WhileYouAreNearbyMaintained/README.md); distance-first cell checks, work-giver metadata and safe discarded-job pooling; same-ID local replacement, no extra threads; forked, license unset | Added 2026-11-09, offline verification passed, game QA pending; taken down on author request |
| `PUAHDuplicateCompFix` | [PUAH duplicate comp fix](mods/PUAHDuplicateCompFix/README.md); dedupe inherited `CompHauledToInventory` by `compClass` at startup, keep the first, leave other comps untouched | Added 2026-11-02, local sidecar, offline red/green passed, in-game save reload pending |
| `NewRatkinDrawScaleCacheFix` | [NewRatkin draw-scale cache thread-safety fix](mods/NewRatkinDrawScaleCacheFix/README.md); serialize `GeneDrawScaleCache.Get` and `Recalculate(Pawn, out float)` so 1.7 parallel pre-draw cannot corrupt NewRatkinPlus's plain `Dictionary`; includes a note for the upstream author | Added 2026-11-07, local sidecar, real DLL 16-thread red/green passed; patch registered after restart, three minimize/resume cycles without job faults, shallow observation passed (probabilistic caveat retained) |
| `UFLIVirtualWorkerFix` | [UFLI virtual worker fix](mods/UFLIVirtualWorkerFix/README.md); normalize the full-auto pooled ghost worker so a random disabling backstory/trait/gene/hediff cannot make `SkillRecord.TotallyDisabled`; replaces disabling backstories, strips work-disabling or aptitude traits/genes/hediffs, refreshes caches and sets every skill to the bench level | Added 2026-11-08, local sidecar, real DLL red/green passed; patch registered after restart, in-game production QA pending |
| `UFLIDropProductAtFeet` | [UFLI drop product at feet](mods/UFLIDropProductAtFeet/README.md); add a persistent Mod Settings and printer-gizmo toggle that places holographic printer products near the InteractionCell instead of first teleporting them to remote storage; disabling restores original UFLI delivery | Added 2026-11-09, local sidecar, real DLL red/green passed, in-game QA confirmed by user |
| `ITabGenesWidthFix` | [Gene tab width fix](mods/ITabGenesWidthFix/README.md); repair vanilla 1.7 `ITab_Genes` singleton built before `UI.screenWidth` is initialized, where a zero-width layout makes `GenText.Truncate` throw `Substring(0,-1)` for `met==0` pawns | Added 2026-11-09, local sidecar, real Assembly-CSharp red/green passed, in-game QA pending |
| `PocketSandEquipReservationFix` | [Pocket Sand ground-equip reservations](mods/PocketSandEquipReservationFix/README.md); reserve ground weapons before walking, leaving inventory swaps, mass checks and delays unchanged | Added 2026-11-10, independent sidecar, save loading confirmed by user; ground-equip gameplay QA pending |
| `EquippableAbilityPsycastLoadFix` | [Equippable Psycast save loading](mods/EquippableAbilityPsycastLoadFix/README.md); restore missing ability constructor arguments; v1.0.1 removes recursive virtual base reflection from the earlier load prefix | Added 2026-11-10, 35 real entry/Scribe round-trip checks passed, save loading confirmed by user; ability gameplay QA pending; do not use v1.0.0 |
| `FFFMechModificationQueueFix` | [FFF mech modification queue fix](mods/FFFMechModificationQueueFix/README.md); preserve queued modification jobs across `JobIsSameAs` and reserve 1 item per job with per-job availability checks (including optional DMSSR synthetic bionics) | Added 2026-11-12, 33 real-DLL checks passed, in-game QA confirmed by user |
| `WolfeinRestoreWorkPrioritiesFix` | [Wolfein work priorities init fix](mods/WolfeinRestoreWorkPrioritiesFix/README.md); skip `HediffComp_RestoreWorkPriorities` priority bookkeeping while `Pawn_WorkSettings` is uninitialized during non-player raider generation, preventing `did not have work settings initialized` errors and premature priority initialization | Added 2026-11-10, 13 real-DLL red/green checks passed, startup registration and multi-day in-game QA confirmed by user |
| `DMSLDrillingBargePerfFix` | [DMS Legion drilling barge perf fix](mods/DMSLDrillingBargePerfFix/README.md); replace the full-map deep-drill work scan with a per-map index built from the upstream `TryGetDeepResource`, and pre-hash the deep-resource exclusion set | Added 2026-11-13, 45 real-DLL checks and 211,250 cell/mode equivalence comparisons passed; in-game QA confirmed by user 2026-11-13 |
| `CeleTechTurretUpgradePerfFix` | [CeleTech turret upgrade perf fix](mods/CeleTechTurretUpgradePerfFix/README.md); index queued turret upgrades per map so the giver is skipped in O(1) and `GenClosest` only receives turrets with queued upgrades | Added 2026-11-13, 9 RED / 46 GREEN real-DLL checks passed; in-game QA confirmed by user 2026-11-13 |
| `HARAnimatePerfFix` | [HARAnimate cache & thread-safety perf fix](mods/HARAnimatePerfFix/README.md); replace boxing struct keys and string concatenation with `IEquatable` `ConcurrentDictionary` lookups and per-node locks for 1.7 parallel pre-draw | Added 2026-11-13, 21 RED / 51 GREEN real-DLL checks passed; in-game QA confirmed by user 2026-11-13 |

Historical local mirrors of full upstream mods are intentionally excluded. `archive/` is reserved for retired experiments (for example [ConduitRoomStatsFix](archive/ConduitRoomStatsFix/README.zh-CN.md), [MTOEWYHealthInheritanceFix](archive/MTOEWYHealthInheritanceFix/README.md), [Fix_WRMegaCorp_QuestPartCSC](archive/Fix_WRMegaCorp_QuestPartCSC/README.md) and [DMSSRSolitonChargeDecay](archive/DMSSRSolitonChargeDecay/README.md), retired 2026-11-13 after upstream fixes and author confirmation of the heavy-beam reset design); `tools/` is reserved for diagnostics that are not gameplay patches.

[HaulDestinationTrace](tools/HaulDestinationTrace/README.md) is a temporary, explicitly opt-in diagnostic for workbench hauling targets. It does not fix or suppress errors; disable it after capturing one reproduction.

Changes that must remain inside a third-party project are captured as small recovery diffs under [`upstream-patches/`](upstream-patches/README.md). Direct Workshop XML edits are described there without republishing entire third-party mods.

## Local builds

See [BUILDING.md](BUILDING.md). Legacy projects require local game and Workshop references, which are not included here. Builds must not deploy to an installed game unless deployment is explicitly enabled. Retained DLLs are backup artifacts, not proof of a fresh build or game test.

## Licensing

There is no repository-wide license. Individual directories retain any license or attribution shipped with that patch. Third-party projects referenced by a patch remain governed by their own licenses.
