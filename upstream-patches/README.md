# Upstream patch archive

[中文说明](README.zh-CN.md)

This directory stores small diffs for locally maintained changes that live inside full third-party projects. The upstream projects themselves are deliberately not vendored into this monorepo.

| Patch | Base | Notes |
|---|---|---|
| `combat-extended-local.patch` | Combat Extended commit `2d2b8d20c` | Aggregate recovery diff: Pigeon compatibility, suppression/tending guards, manual bipod deployment, fragment self-hit prevention, natural-armor cast cleanup, Trailblazer reload protection, inventory-ammo reload support and AmmoThing idle fast path. GenRadial already matches this base and has no hunk. Apply selectively when rebasing. |
| `combat-extended-wall-fragment.patch` | `7a7ed38eb3c4736694f296491408fdc766fbd992` | Focused fragment self-hit patch, also retained in the CE fork branch `fix/wall-fragment-self-hit`. The mail header identifies the patch commit, not this base. |
| `adaptive-storage-eject-rendering.patch` | Adaptive Storage Framework commit `31ee88b` | Historical experiment retained for recovery. Upstream review later showed that part of its storage-membership assumption was incorrect; do not apply wholesale without revalidating current ASF behavior. |

These files are archival inputs, not standalone RimWorld mods. Prefer the dedicated upstream fork branches when they still exist.

License notices and provenance for every item in this archive (CE, ASF, Killfeed, and the Workshop XML edits) are in `LICENSES.md`. In short: CE is CC BY-NC-SA 4.0 (declared in the upstream README, no standalone LICENSE file), ASF is MIT (local LICENSE file), and Killfeed has no license, so only the locally authored fragment is stored with attribution and no license claim.

Investigations that did not result in a deployed patch are omitted from the recovery set. In particular, the PocketSand equip/despawn race was diagnosed but never patched locally.
