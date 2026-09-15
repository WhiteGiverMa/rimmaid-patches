# 快照迁移说明，2026-09-15

[English](MIGRATION.md) | 简体中文

旧仓库保留完整的提交历史。本仓库从经过审查的快照开始一段全新的历史。整个过程不涉及 submodule、subtree 合并、package ID 变更，也不涉及向正在使用的游戏部署。

## 迁移对照表

| 原仓库 / 快照 | 新目录 |
|---|---|
| [CombatExtended-Overpenetration @ 2e7cc6a](https://github.com/WhiteGiverMa/CombatExtended-Overpenetration/tree/2e7cc6a602491dce8b46cb44b01c61189e4ca086) | [mods/CEOverpenetration](mods/CEOverpenetration) |
| [CombatExtended-EliteCombatTweaks @ 1814172](https://github.com/WhiteGiverMa/CombatExtended-EliteCombatTweaks/tree/18141725cd0207c5259b890cde9340e5c5ac0000) | [mods/CEEliteCombatTweaks](mods/CEEliteCombatTweaks) |
| [CombatExtended-BreachingFix @ 0b88bc7](https://github.com/WhiteGiverMa/CombatExtended-BreachingFix/tree/0b88bc736d50fdd0016efb3f40a459c38dffe9cd) | [mods/CEBreachingFix](mods/CEBreachingFix) |
| [DMS-ApparelRepair @ ce5daae](https://github.com/WhiteGiverMa/DMS-ApparelRepair/tree/ce5daaedbe06f4d617316cd4355dc5abbb92ccfa) | [mods/DMS_Synthetic_Apparel_Repair](mods/DMS_Synthetic_Apparel_Repair) |
| [Fix_ExosuitDummyMeat @ 82f4063](https://github.com/WhiteGiverMa/Fix_ExosuitDummyMeat/tree/82f4063f17e562a8e480d88231ee3284a503be4c) | [mods/Fix_ExosuitDummyMeat](mods/Fix_ExosuitDummyMeat) |

## 迁移允许的差异

允许的差异包括：可迁移的构建引用、可选的部署开关、公开文档、移除生成文件与私有文件，以及明确标注为「重建」的源码。mod 的运行时逻辑、package ID 和保留的 DLL 均原样保留。源码注释中包含私有存档标识符的内容已做脱敏处理。

`checksums.sha256` 记录了保留的二进制文件。重建的源码只是帮助阅读和重新构建的辅助材料，不保证能逐字节复现原始文件或复现每一个运行时细节。特别说明：WVC 原始 DLL 目标是 .NET Standard 2.1，而它的恢复工程是一个 .NET Framework 4.8 的构建辅助项目。

## 上游 fork 的处理

完整的上游 fork（包括 Combat Extended 和 Adaptive Storage Framework）保持独立，不随本次迁移归档。它们的本地补丁索引在 `upstream-patches/` 中；对创意工坊 mod 的直接 XML 修改存档不会被自动应用。

## 后续安排

迁移顺序是：先发布并从远端取回验证新快照，再给五个原始仓库的 README 和简介加上新目录入口，最后封存旧仓库以保留完整历史。之后所有新增自有补丁改动只进入本 monorepo。本地既有的源码/部署目录不删除，也不转换成链接；请避免在那里维护第二份活跃源码副本。实际封存状态可在原仓库页面核对。
