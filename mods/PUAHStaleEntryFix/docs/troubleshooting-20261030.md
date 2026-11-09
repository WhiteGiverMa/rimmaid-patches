# BulkLoad SoftlockCleaner 重复日志排查（2026-10-30）

## 症状

日志反复出现：

```
[BulkLoad] SoftlockCleaner detected on <pawn>. Dropping 1 soft-locked PUAH items.
```

同一行在约 1 分钟内出现多次，间隔随游戏速度变化。玩家检查该 pawn 时，背包为空，只有手持武器。

## 存档证据

存档中该 pawn 的状态：

- `inventory.innerContainer` 为空。
- `carryTracker` 为空。
- `curJob` 为 `SelfShutdown`，属于休眠中的机兵。
- PUAH 的 `ThingsHauledToInventory` 中仍保留一条物品引用；该引用对应的对象已经作为独立机兵存在于地图上，并不在背包里。

因此这不是物品被吞或隐藏，而是 PUAH 记录中的残留引用。

## 代码链

1. `BulkLoadForTransporters.Core.SoftlockCleaner.TryCleanPawn` 的 Stage 1 只用
   `thing != null && !thing.Destroyed && thing.stackCount > 0` 判断记录是否有效，
   不检查物品是否仍在背包。
2. Stage 2 把 `SelfShutdown` 的机兵判定为 softlock 候选。
3. Stage 3 只对 `pawn.inventory.innerContainer.Contains(item)` 的物品调用 `TryDrop`；
   不在背包的残留会被跳过，也永远不会从 hashset 移除。
4. 于是每一轮检查都会重复打印，无法自愈。

PUAH 侧，`CompHauledToInventory` 只在 `JobDriver_HaulToInventory` 注册物品，
并在 `Pawn_InventoryTracker.Notify_ItemRemoved` 的后置补丁中移除。
某些移除路径（第三方直接改 `holdingOwner`、容器转移等）不会触发该回调，于是留下残留。

## 修复

sidecar `PUAHStaleEntryFix`：Harmony prefix `SoftlockCleaner.TryCleanPawn`，
在原始检查前移除既不在 `inventory.innerContainer`、也不在 `carryTracker.CarriedThing` 的记录。

- 真正卡在背包里的物品保持原样，继续由 Bulk Load 原逻辑掉落。
- 不修改工坊 DLL、存档或 ModsConfig。
- `TryCleanPawn` 方法在 Bulk Load 更新后不存在时，补丁打印警告并跳过。

## 验证边界

- 构建：0 警告、0 错误。
- 静态绑定：反编译确认 `BulkLoadForTransporters.Core.SoftlockCleaner.TryCleanPawn(Pawn)` 存在。
- 游戏内浅测：下次正常启动加载后，Bulk Load 的重复日志应消失。
  若仍有日志，说明记录中确实有物品在背包里，此时会按原逻辑掉落。
- 未在游戏内自动验证（RimWorld 启动与存档加载无法由工具代替）。

## 维护提示

- Bulk Load 更新后检查 `SoftlockCleaner.TryCleanPawn(Pawn)` 是否仍在。
- PUAH 若替换为其他 fork，检查 `PickUpAndHaul.CompHauledToInventory` 是否仍存在。
