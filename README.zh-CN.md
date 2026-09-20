# Rimmaid Patches（中文说明）

**中文** | [English](README.md)

由 WhiteGiverMa 维护的一组小型、可独立加载的 RimWorld 补丁 mod。

这是一个源码与恢复用的 monorepo。`mods/` 下的每个目录都保留自己的 `About.xml`、package ID、依赖、加载顺序和部署生命周期。

## 安装方式（重要）

- 每个 mod 都是独立目录。安装时**只把单个 mod 目录复制到游戏的 `Mods/` 文件夹**，例如 `Mods/CEOverpenetration`。
- 不要把整个 monorepo 根目录复制进 `Mods/`，也不要不加区分地安装全部目录。
- `archive/` 是已退役的实验，`tools/` 是诊断工具，`upstream-patches/` 是给第三方项目用的补丁记录，这三类都不应安装进游戏。
- 本次迁移不修改正在运行的游戏、配置或已安装的 mod。独立补丁放在 `mods/`；无法独立安装的第三方项目内改动另存为恢复片段。

## 仓库政策

- `mods/` 只包含本地编写或维护的补丁。
- 上游项目和完整的第三方 mod 镜像保留在它们自己的仓库中，不收录在这里。
- 保留既有的 package ID，已安装游戏的 mod 列表可以继续正常工作。
- 当发布用程序集属于 mod 可部署包的一部分时，允许随 mod 一起提交；临时的 `bin/` 和 `obj/` 输出不提交。
- 存档、日志、本地配置、包含个人游玩状态的诊断信息、agent 工作文件一律不发布。
- 每个补丁都应记录：依赖项、失败链路、验证状态，以及上游更新后的退役信号。

这是一次快照迁移。更早的历史保留在 [MIGRATION.md](MIGRATION.zh-CN.md) 列出的原始仓库中，这些仓库只在新快照发布并验证之后才会归档。五个由 Git 管理的导入记录了精确的源提交；原本没有版本管理的补丁则通过来源描述和保留二进制的哈希来标识。

## 补丁列表

机器可读的元数据见 [`manifest.json`](manifest.json)。

「Active（活跃）」描述的是导入时的本地配置状态，不代表与当前所有上游版本兼容。本次迁移没有用游戏会话做验证。Fortified 缩小（minify）实验被保留下来作为历史记录，但已知它并没有实现声称的防护，**不推荐安装**。

| 目录 | 用途 | 导入时状态 |
|---|---|---|
| `CEOverpenetration` | CE 弹道过穿 | 活跃，游戏内 QA 待做 |
| `CEEliteCombatTweaks` | CE 精英殖民地瞄准与冷却调整 | 活跃，游戏内 QA 待做 |
| `CEBreachingFix` | CE 破墙 AI 与射击位置防护 | 活跃，游戏内 QA 待做 |
| `DMS_Synthetic_Apparel_Repair` | DMS 护甲与武器修复 | 活跃，游戏内 QA 待做 |
| `Fix_ExosuitDummyMeat` | 防止 Exosuit 假人肉类 def 重复 | 活跃 |
| `BossgroupConcurrentSummons` | 可选的 bossgroup 并发召唤 | 活跃，游戏内 QA 待做 |
| `DMSWorkTypeCacheFix` | 派系变更后使机械体工作类型缓存失效 | 活跃，游戏内 QA 待做 |
| `KeyzAllowUtilitiesFinishOffFix` | 防护已消失目标的 Finish Off | 活跃，游戏内 QA 待做 |
| `WVC_CE_CompatPatch` | 限制不兼容的大范围 GenRadial 搜索 | 活跃的旧版 sidecar；含重建源码 |
| `DubsPerformanceAnalyzerFix` | 防护 DPA 中格式异常的 Tick Things 条目（[中文说明](mods/DubsPerformanceAnalyzerFix/README.zh-CN.md)） | 活跃，游戏内 QA 待做 |
| `BugFix_CE_BodyShapeFallback` | CE 体型与格挡边缘情况修复 | 活跃 |
| `BugFix_FortifiedCE_MinifyNullGuard` | Fortified 空引用防护的历史尝试（[中文说明](mods/BugFix_FortifiedCE_MinifyNullGuard/README.zh-CN.md)） | 已知无效；不要安装 |
| `CE_Fix_IsHunkering_NRE` | CE 压制与治疗防护 | 活跃，游戏内 QA 待做 |
| `CE_Fix_MiliraFloatUnitElevation` | Milira 浮空单位的碰撞高度与目标策略 | 活跃，游戏内 QA 待做 |
| `Fix_CeleTech_DuplicateEquippableComps` | 移除重复的 verb owner，同时保留卸装行为 | 活跃，游戏内 QA 待做 |
| `Fix_DMS_BookGrammarAndHeavyShield` | DMS 书本语法与重盾身体部位 XML 修复 | 活跃 |
| `Fix_WRMegaCorp_QuestExposeData` | 移除任务数据的重复深度序列化 | 活跃，游戏内 QA 待做 |
| `LocalFix_MiliraFlightNullCheck` | 防护没有飞行组件的 Milira 飞行追踪器 | 活跃，游戏内 QA 待做 |
| `WolfeinWeaponBoxCE` | [沃芬超容武器箱 CE 容量滑条](mods/WolfeinWeaponBoxCE/README.md)，默认体积 +350、重量 +0 | 2026-09-20 新增，游戏内 QA 待做 |

完整的上游 mod 历史本地镜像被有意排除。`archive/` 保留给已退役的实验（如 [ConduitRoomStatsFix](archive/ConduitRoomStatsFix/README.zh-CN.md)）；`tools/` 保留给非玩法补丁的诊断工具（如 [GizmoDiag](tools/GizmoDiag/README.zh-CN.md)）。

必须留在第三方项目内部的改动，以小型恢复补丁的形式记录在 [`upstream-patches/`](upstream-patches/README.md) 中（[中文说明](upstream-patches/README.zh-CN.md)）。对创意工坊 mod 的直接 XML 修改也在那里描述，不会重新发布完整的第三方 mod。

## 维护与完整性校验

- 各补丁的日常维护要求、依赖更新后的复核项和退役信号，见 [MAINTENANCE.zh-CN.md](MAINTENANCE.zh-CN.md)。
- 所有保留 DLL 的 SHA-256 哈希记录在 [`checksums.sha256`](checksums.sha256)，可用于校验随 mod 提交的二进制是否与导入时一致。

## 关于依赖与更新

- 每个补丁的依赖（如 Harmony、CE、对应第三方 mod 及其 Workshop ID）写在该 mod 的 `About.xml` 里，安装前请先确认依赖已就位。
- 依赖 mod 更新后，补丁可能失效。请检查各补丁文档中的「退役信号」：如果上游已经内置了同样的修复，或补丁目标的方法签名已改变，就应停用对应补丁。

## 本地构建

见[中文构建说明](BUILDING.zh-CN.md)，[英文原版](BUILDING.md)保留。旧项目需要本地的游戏与创意工坊引用，这些不包含在仓库里。除非显式启用部署，构建不得部署到已安装的游戏。保留的 DLL 是备份产物，不能证明经过新构建或游戏测试。

## 许可

仓库整体没有统一许可证。各目录保留其补丁自带的许可证与署名信息。补丁引用的第三方项目仍受其自身许可证约束。上游补丁涉及的第三方许可与来源说明见 [`upstream-patches/LICENSES.md`](upstream-patches/LICENSES.md)（[中文说明](upstream-patches/LICENSES.zh-CN.md)）。
