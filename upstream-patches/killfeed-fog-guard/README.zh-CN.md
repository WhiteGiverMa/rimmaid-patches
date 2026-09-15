# Killfeed 雾中守卫（中文说明）

[English](README.md)。中英文说明对应同一归档状态。

第三方 Killfeed mod（工坊物品 `1362098265`）的历史源码级本地改动。

Killfeed 原本在 `Pawn.Kill` 的 postfix 中查询 `Pawn.Fogged()`，此时普通 pawn 已经从地图移除。修复方案是新增一个前缀，在 pawn 仍持有有效地图时捕获「该事件是否应隐藏」，通过 Harmony `__state` 传递，并让 postfix 只消费该布尔值。

有意不包含完整的 Killfeed 镜像。`HarmonyPatches.fragment.cs` 是从工坊源码树恢复的本地维护部分。本地镜像当前未启用，改用工坊包 ID 变体。

## 来源与许可证状态

- 上游：Killfeed，工坊物品 `1362098265`，packageId `kahdeg.Killfeed`，作者 キャデグ（kahdeg）。源码树位于 `Sources/Source/HarmonyPatches.cs`。
- 许可证：未找到。工坊包内无 LICENSE 文件，About.xml 也未声明许可证。不为上游文件主张任何许可证；此处仅存储本地自创片段并附归属说明，周边上游代码不包含在内。
- 片段包含前缀方法与 `__state` 契约；postfix 的改动以尾部注释描述而非复刻，因为 postfix 主体是上游代码加两行本地插入。
- 本地改动摘要：前缀在 pawn 仍持有有效地图时把 `Fogged()` 捕获进 `bool __state`；现有 `Pawn.Kill` postfix 增加 `bool __state` 参数，当 `__state` 为真时在创建公告前返回。
