# 上游补丁归档（中文说明）

[English](README.md)。中英文说明对应同一归档状态。

本目录存放位于完整第三方项目内部的本地维护改动的小型 diff。上游项目本身被有意地不纳入本 monorepo。

| 补丁 | 基线 | 说明 |
|---|---|---|
| `combat-extended-local.patch` | Combat Extended 提交 `2d2b8d20c` | 本地混合构建聚合 diff：Pigeon、压制/治疗、手动脚架、破片自击、自然装甲强转、Trailblazer 装填、背包弹药装填和 AmmoThing 空闲快速路径。GenRadial 已与该基准相同，**没有对应 hunk**。重基时逐项判断是否保留。 |
| `combat-extended-wall-fragment.patch` | `7a7ed38eb3c4736694f296491408fdc766fbd992` | 防止墙体/建筑破片与来源碰撞；保留于 fork 的 `fix/wall-fragment-self-hit`。邮件头标的是补丁提交，不是应用基准。 |
| `adaptive-storage-eject-rendering.patch` | Adaptive Storage Framework 提交 `31ee88b` | 历史实验，仅作恢复留档。上游评审后来表明其部分「存储成员资格」假设有误；未经重新验证当前 ASF 行为前，不要整体套用。 |

这些文件是归档输入，不是可独立安装的 RimWorld mod。若专用 fork 分支仍存在，优先使用分支。

未产生已部署补丁的调查不纳入恢复集。特别是 PocketSand 的装备/消散竞态只做了诊断，本地从未打补丁。

许可证与来源信息见 `LICENSES.md`（中文说明见 `LICENSES.zh-CN.md`）。
