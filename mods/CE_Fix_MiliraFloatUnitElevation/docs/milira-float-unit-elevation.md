# CE Fix: Milira Float Unit Elevation

## Purpose

`Milian_AutonomousFloatUnit` and its sibling float units are visually airborne but have no `pawn.Flying` state. Combat Extended therefore gave them a ground-level collision range based on their tiny `drawSize=0.2` sprite.

This local Harmony patch applies CE's existing native-flight offset, `+0.5`, to the collision range and shot height of every pawn whose body Def is `Milira_FloatUnit`. It leaves collision width, plants, weapon values, damage, and all non-float-unit pawns untouched.

## Compatibility contract

- RimWorld 1.6
- Combat Extended 16.7.3.0
- Milira Race (`Ancot.MiliraRace`)
- Compatible with the third-party Milira CE Patch (`pntfvur.RimPatches.Milira.CE`)

If a future Milira version gives float units a real `pawn.Flying` state, this patch does nothing for those pawns and CE's own flight handling applies the offset once.

## Automatic target policy

A persistent three-state setting controls whether automatic weapon selection may pick Milira Float Units. The filter only checks `Pawn` targets whose `RaceProps.body.defName` equals `Milira_FloatUnit`; all other pawns are never affected.

| Policy | Label in settings | Effect |
|---|---|---|
| `PlayerOnly` (default) | Player only | Reject float units for player-faction automatic searches only. |
| `AllFactions` | All factions | Reject float units for every faction's automatic searches, including hostile AI. |
| `AllowAll` | Allow all | No filtering; all automatic searches may pick float units. |

### Scope and exclusions

- **Covered**: standard `AttackTargetFinder.BestAttackTarget`, CE `NonSnapAttackTargetFinder.BestAttackTarget`, and CE mid-burst retarget in `Verb_LaunchProjectileCE.Retarget()`.
- **Unaffected**: manual player right-click attacks, forced turret targets, CIWS projectile interception, hunting designations, and world-map artillery. You can always manually force-fire on a float unit regardless of the current policy.

The policy is saved through `Scribe_Values.Look` and survives save/load cycles.

## Shallow verification (one dev quick-game session)

### 1. Startup log
Start a dev quick game and confirm the log contains:
```
[CE_Fix_MiliraFloatUnitElevation] Applied +0.5 CE elevation to Milira float units.
```

### 2. Player only — automatic selection + manual forced target
Set the policy to `Player only` (the default). Spawn `Milian_Mechanoid_BishopIII` and let it release `Milian_AutonomousFloatUnit` units alongside a normal hostile pawn. With a drafted colonist:
- Let auto-target pick a target. It must select the normal hostile, not a float unit.
- Right-click to manually force-fire on a float unit. The manual attack must succeed.

### 3. All factions — enemy auto-attacker
Change the policy to `All factions`. Spawn a hostile turret or drafted enemy near a float unit that is hostile to it. The enemy automatic search must not pick the float unit. Manually trigger an attack on the turret if needed to force it to search for targets.

### 4. Allow all — re-enable automatic float-unit selection
Change the policy to `Allow all`. The drafted colonist's auto-target from step 2 must now be able to pick a float unit. This confirms the filter is policy-gated and not a permanent exclusion.

### 5. Elevation (existing test)
Aim a CE gun across low vegetation at a spawned float unit. The target must no longer be treated as ground-level behind the vegetation. If it still reports `Cannot hit`, capture the full tooltip reason because ordinary grass is not a CE LoS blocker.
