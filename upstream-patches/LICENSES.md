# License notices and provenance

[中文说明](LICENSES.zh-CN.md)

This archive preserves local edits and minimal upstream context, not full third-party projects. It does not apply a blanket license to unrelated standalone mods.

## Combat Extended

Applies to `combat-extended-local.patch` and `combat-extended-wall-fragment.patch`.

- Original project and attribution: [Combat Extended and the CE Team](https://github.com/CombatExtended-Continued/CombatExtended).
- [Upstream license declaration](https://github.com/CombatExtended-Continued/CombatExtended/blob/2d2b8d20c43c09121ef9e444bda19b219cb1fcf7/README.md#license): [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-nc-sa/4.0/), [legal text](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode).
- Local modifications by WhiteGiverMa / Meidocho are recorded in the diffs, with minimal context from upstream; the CE-derived patches retain those terms. No upstream endorsement is implied.
- Aggregate base: `2d2b8d20c43c09121ef9e444bda19b219cb1fcf7`. Captured working tree: fork commit `6a0bc4ed1cb2ce689441fce150047f3dc5f48351` plus five tracked modified files. The GenRadial restoration cancels an earlier local commit, so it has no hunk relative to this base.
- Wall-fragment patch commit: `1ec3785bc8c4a28fafef825bfded5ed3711dbf52`; parent/base: `7a7ed38eb3c4736694f296491408fdc766fbd992`.

## Adaptive Storage Framework

Applies to `adaptive-storage-eject-rendering.patch`. Source: [bbradson/Adaptive-Storage-Framework](https://github.com/bbradson/Adaptive-Storage-Framework); license read from its checked-out `LICENSE` at the local experiment based on `31ee88b`. Patch commit `7cb475af19bf4a8dbb68d781961b5c0b1e364b3e` is **rejected/reverted history, not an active fix**. The local modification is by WhiteGiverMa / Meidocho. Original license notice:

MIT License

Copyright (c) 2023 bradson, Soul, Phaneron

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Killfeed and Workshop XML recovery

- Killfeed: [Workshop 1362098265](https://steamcommunity.com/sharedfiles/filedetails/?id=1362098265), author キャデグ (kahdeg), package ID `kahdeg.Killfeed`. No license was found in the installed package. Only the locally written prefix and instructions for its integration are included; the original postfix body, full source and binaries are excluded. No license is assigned to the original author's content.
- Workshop XML artifacts preserve the locally added/changed elements for MobileDragoon (`3377130226`), Wolfein (`3473140562`), Wolfein CE (`3485371294`), WRMegaCorp (`3687841204`) and UF CE (`3748582975`). Respective authors retain rights to the surrounding mod content. These are recovery fragments, not copies of the complete mods; no blanket redistribution permission for those mods is claimed.
- UF's previously copied placeholder texture is not republished. Its source filename and recovery location are recorded in the accompanying guide.
