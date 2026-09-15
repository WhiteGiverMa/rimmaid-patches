# Workshop XML edit archive

[中文说明](README.zh-CN.md)

These are minimal recovery descriptions for local edits that were made directly inside Steam Workshop directories. They are not standalone mods and Steam updates may overwrite them.

Recoverable payloads are stored as actual XML artifacts next to this README (see `LICENSES.md` in the parent directory for license and provenance status):

| Artifact | Covers |
|---|---|
| `dms-mobiledragoon-ce-armor.xml` | DMS MobileDragoon CE armor durability (verbatim five operations) |
| `wolfein-sabre-ce-tag.xml` | Wolfein AlloySabre `CE_OneHandedWeapon` tag (verbatim operation) |
| `wolfein-empty-numeric-fields.xml` | Wolfein empty numeric field removal (equivalent PatchOperationRemove form) |
| `wrmegacorp-quest-multiplier.xml` | WRMegaCorp `questRewardMultiplier` init snippet |
| `DamageDefs_UFC_Stubs.xml` | UF stub DamageDefs (verbatim copy of the local file, entirely ours) |

## Wolfein Race empty numeric fields

- Workshop item: `3473140562`
- File: `1.6/Defs/ThingDefs_Weapon/Melee_Industrial.xml`
- Remove empty `relicChance` and `equippedAngleOffset` elements. Missing values use the intended zero defaults, while empty strings fail float parsing.
- The original edit was a direct file removal (five occurrences each); the pre-edit state is preserved in the Workshop directory as `Melee_Industrial.xml.bak`. The archive artifact expresses the same removal as a `PatchOperationRemove` with a `not(node())` predicate so populated fields are untouched.
- The same defect has returned after Workshop updates, so inspect the current XML before reapplying.

## Wolfein CE alloy sabre tag

- Workshop item: `3485371294`
- File: `1.6/Patches/ThingDefs_Weapon/W_Weapon_Melee.xml`
- For `Defs/ThingDef[defName="W_Weapon_Melee_AlloySabre"]/weaponTags`, the `PatchOperationAdd` value must directly contain `<li>CE_OneHandedWeapon</li>` rather than another nested `<weaponTags>` element.
- The live file currently carries the correct form (verified 2026-09-15); the artifact preserves it verbatim.

## DMS MobileDragoon CE armor durability

- Workshop item: `3377130226`
- Files: `1.5/CE/Patches/CE_Frames.xml` and `1.6/CE/Patches/CE_Frames.xml`
- Five frame cores were given `CombatExtended.CompProperties_ArmorDurability`: PF-3 1400, AT-34 2000, FA-47 3000, PV-4 4200, PV-8 4400. Each is repairable with Steel 10, repair time 300, repair value 200, and minimum armor percentage 0.6.
- The five operations were recovered verbatim from the live 1.6 file; the 1.5 copy carries the identical block (diff-verified at archive time).
- The local CE fork also removes an unsafe Pawn cast from `StatPart_NaturalArmorDurability`; retain both sides together while apparel uses this component.

## WRMegaCorp quest reward multiplier

- Workshop item: `3687841204`
- Add `QuestNode_Set` with `questRewardMultiplier = 1` before the first use in MeatOrder, ProjectLiquidation, and DepositExploitation.
- At archive time the first and third scripts still contained the local initialization, while ProjectLiquidation did not. Recheck after every Workshop update.
- The archive artifact stores the insertion snippet only; ProjectLiquidation's reward math differs and must not receive the snippet without reproducing the original failure first.

## UF CE patch missing definitions

- Workshop item: `3748582975`
- The installed CE patch referenced three UFPC damage definitions and one texture while UFPC was absent. The local copy added stub definitions for `UFC_Bullet_50P`, `UFC_Bullet_100P`, and `UFC_Bomb_Buildingkiller`, plus a placeholder `Textures/Things/Projectile/Bullet_UFC_SA.png`.
- The stub file is archived verbatim as `DamageDefs_UFC_Stubs.xml`. The old texture was copied from `Bullet_kt_small.png` to `Textures/Things/Projectile/Bullet_UFC_SA.png` inside the original mod; that third-party image is not republished here. Recover it from the original package if needed, or provide an appropriately licensed replacement. This migration generates no new asset and performs no deployment.
- These stubs only suppress unresolved references when no installed content uses those projectiles. Remove them if the upstream package gains a conditional definition or real assets.

## Historical patches currently absent

These are deliberately documented rather than restored. Reproduce the original failure against the current dependency version before applying them again.

- **RPG Style Inventory Revamped (`2478833213`)**: an older local IL edit added a pawn null guard to `CEPatches.RPG_CEPatch+RPG_CEPatches.TryDrawOverallArmor` and `TryDrawOverallArmor1`. A later Workshop update replaced the DLL and the guard is currently absent.
- **Combat Extended suppression bounds**: `SuppressionUtility.IsOccupiedByEnemies` previously received `cell.InBounds(pawn.Map)` before `ThingsListAt(cell)`. The current local CE build does not contain this historical guard.
- **Combat Extended target height**: an older experiment kept a fully obscured short target's real height instead of lifting the target range above nearby cover. The current local CE build does not contain it; this changes combat behavior and must not be reapplied without fresh validation.
