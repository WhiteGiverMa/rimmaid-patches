# Killfeed fog guard

[中文说明](README.zh-CN.md)

Historical source-level edit for the third-party Killfeed mod, Workshop item `1362098265`.

Killfeed originally queried `Pawn.Fogged()` from a `Pawn.Kill` postfix, after normal pawns had already despawned. The fix adds a prefix that captures whether the event should be hidden while the pawn still has a valid map, passes it through Harmony `__state`, and lets the postfix consume only that boolean.

The full Killfeed mirror is intentionally not included. `HarmonyPatches.fragment.cs` contains the locally maintained portion recovered from the Workshop source tree. The local mirror is currently inactive in favor of the Workshop package ID variant.

## Provenance and license status

- Upstream: Killfeed, Workshop item `1362098265`, packageId `kahdeg.Killfeed`, author キャデグ (kahdeg). Source tree at `Sources/Source/HarmonyPatches.cs`.
- License: none found. No LICENSE file exists in the Workshop package and About.xml declares no license. No license is claimed for the upstream file; only the locally authored fragment is stored here, with the surrounding upstream code left out.
- The fragment contains the prefix method and the `__state` contract; the postfix change is described in the trailing comment rather than reproduced, since the postfix body is upstream code with a two-line local insertion.
- Local edit summary: the prefix captures `Fogged()` while the pawn still has a valid map into `bool __state`; the existing `Pawn.Kill` postfix gains `bool __state` and returns before creating an announcement when `__state` is true.
