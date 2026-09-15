# 许可声明与来源

[English / 许可原文](LICENSES.md)。本中文说明不是许可证法律文本的替代。

本目录保留本地修改及必要的上游上下文，不收录完整第三方项目，也不为所有独立补丁统一指定许可证。

## Combat Extended

- 原项目与署名：[Combat Extended、CE Team](https://github.com/CombatExtended-Continued/CombatExtended)。基准 README 的许可链接明确指向 **CC BY-NC-SA 4.0**，不是根据“依赖 CE”推定许可。
- [上游声明](https://github.com/CombatExtended-Continued/CombatExtended/blob/2d2b8d20c43c09121ef9e444bda19b219cb1fcf7/README.md#license)、[许可摘要](https://creativecommons.org/licenses/by-nc-sa/4.0/)、[法律文本](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode)。CE 衍生 diff 保留署名、非商业及相同方式共享条款，不表示上游认可这些修复。
- 本地修改者：WhiteGiverMa / Meidocho。聚合 diff 基于 `2d2b8d20c43c09121ef9e444bda19b219cb1fcf7`，对应 fork `6a0bc4ed1cb2ce689441fce150047f3dc5f48351` 加五个受控工作树修改。GenRadial 恢复后与基准相同，因此聚合 diff 中没有其 hunk。
- 墙体破片补丁提交为 `1ec3785bc8c4a28fafef825bfded5ed3711dbf52`，父提交/应用基准为 `7a7ed38eb3c4736694f296491408fdc766fbd992`。

## Adaptive Storage Framework

上游作者 bradson、Soul、Phaneron；本地修改者 WhiteGiverMa / Meidocho。许可从检出的 `LICENSE` 读取为 **MIT**，完整版权及许可文本保留在英文伴随文档中。

补丁 `7cb475af19bf4a8dbb68d781961b5c0b1e364b3e` 基于 `31ee88b`，是**已否决、已回退的历史实验**。保存 diff 不等于建议部署；其中部分存储模型假设已被上游纠正。

## Killfeed 与工坊 XML

- Killfeed（工坊 `1362098265`，作者 キャデグ / kahdeg）安装包未找到许可声明。仅保存本地编写的前缀和接入说明，不包含原版 postfix 主体、完整源码或 DLL，也不替原作者指定许可。
- MobileDragoon `3377130226`、Wolfein `3473140562`、Wolfein CE `3485371294`、WRMegaCorp `3687841204`、UF CE `3748582975` 只保存本地新增/修正的最小 XML 片段。周边模组内容的权利仍归相应作者，不表示获准重发整个模组。
- UF 原先复制的第三方占位贴图没有重新发布；其原始文件名和恢复位置见 XML 归档说明。
