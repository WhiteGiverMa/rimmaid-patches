# Snapshot migration, 2026-09-15

[中文迁移说明](MIGRATION.zh-CN.md)

Old repositories retain their full commit history. This repository starts a new history from reviewed snapshots. No submodules, subtree merges, package-ID changes, or live-game deployment are involved.

| Original repository / snapshot | New directory |
|---|---|
| [CombatExtended-Overpenetration @ 2e7cc6a](https://github.com/WhiteGiverMa/CombatExtended-Overpenetration/tree/2e7cc6a602491dce8b46cb44b01c61189e4ca086) | [mods/CEOverpenetration](mods/CEOverpenetration) |
| [CombatExtended-EliteCombatTweaks @ 1814172](https://github.com/WhiteGiverMa/CombatExtended-EliteCombatTweaks/tree/18141725cd0207c5259b890cde9340e5c5ac0000) | [mods/CEEliteCombatTweaks](mods/CEEliteCombatTweaks) |
| [CombatExtended-BreachingFix @ 0b88bc7](https://github.com/WhiteGiverMa/CombatExtended-BreachingFix/tree/0b88bc736d50fdd0016efb3f40a459c38dffe9cd) | [mods/CEBreachingFix](mods/CEBreachingFix) |
| [DMS-ApparelRepair @ ce5daae](https://github.com/WhiteGiverMa/DMS-ApparelRepair/tree/ce5daaedbe06f4d617316cd4355dc5abbb92ccfa) | [mods/DMS_Synthetic_Apparel_Repair](mods/DMS_Synthetic_Apparel_Repair) |
| [Fix_ExosuitDummyMeat @ 82f4063](https://github.com/WhiteGiverMa/Fix_ExosuitDummyMeat/tree/82f4063f17e562a8e480d88231ee3284a503be4c) | [mods/Fix_ExosuitDummyMeat](mods/Fix_ExosuitDummyMeat) |

Permitted migration differences: relocatable build references, opt-in deployment, public documentation, removal of generated/private files, and reconstructed source explicitly labeled as such. Original mod runtime logic, package IDs and retained DLLs are preserved. Source-comments containing private save identifiers are sanitized.

`checksums.sha256` records retained binaries. Reconstructed sources are readability/rebuild aids and are not asserted to reproduce the original bytes or every runtime detail. In particular, WVC's original DLL targets .NET Standard 2.1 while its recovery project is a .NET Framework 4.8 build aid.

Full upstream forks, including Combat Extended and Adaptive Storage Framework, remain independent and are not archived by this migration. Their local patches are indexed in `upstream-patches/`; direct Workshop XML edit archives are not automatically applied.

After the new public snapshot is verified, each of the five original repository READMEs and descriptions is updated to point to its new directory, then the repository is archived. Only the monorepo should receive future addon changes. Existing local source/deployment directories are not deleted or converted to links; avoid maintaining a second active source copy there.
