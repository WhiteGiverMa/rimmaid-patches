# BulkLoad PUAH 残留清理 / BulkLoad PUAH Stale Entry Fix

Bulk Load For Transporters 的 `SoftlockCleaner` 会周期检查玩家机兵身上的 Pick Up And Haul 记录。
如果 PUAH 记录中残留了一个已经离开背包的物品引用，清理器会持续打印：

```
[BulkLoad] SoftlockCleaner detected on <pawn>. Dropping 1 soft-locked PUAH items.
```

但对应物品并不在背包里，所以 `Contains` 检查会跳过 `TryDrop`，记录也不会被移除。结果是每轮检查都重复同一条日志。

本补丁在 `SoftlockCleaner.TryCleanPawn` 前清理这类幽灵条目：

- 只移除既不在 `pawn.inventory.innerContainer`，也不在 `pawn.carryTracker.CarriedThing` 的记录。
- 真正卡在背包里的物品保持原样，继续由 Bulk Load 原逻辑掉落。
- 如果 `TryCleanPawn` 方法在 Bulk Load 更新后改名，补丁会打印警告并跳过，不会让游戏报错。

依赖：RimWorld 1.7、Harmony、Bulk Load For Transporters、Pick Up And Haul。

排查记录见 `docs/troubleshooting-20261030.md`。
