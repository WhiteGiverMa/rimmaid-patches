# 给作者的反馈草稿

仅为草稿，尚未发布。以下不包含私人存档、人物信息或本机路径。

---

你好，反馈一个自动化中枢的材料消耗问题，辛苦啦。

环境：RimWorld 1.7.4982 rev1280（Waylith），CeleTech Arsenal MKIII，工坊 ID 3446237098。建造并启用自动化中枢后，本机出现：

```text
[ManagedWorkTable] Tick error: System.InvalidOperationException:
Collection was modified; enumeration operation may not execute.
at System.Collections.Generic.List`1.Enumerator.MoveNext()
at CeleTech.Base.Building_ManagedWorkTable.TryConsumeGatheredMaterials()
at CeleTech.Base.Building_ManagedWorkTable.Tick()
```

核对当前 DLL 后发现，`TryConsumeGatheredMaterials()` 直接 `foreach` 遍历 `Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)`，并在循环里调用 `item.SplitOff(num4)?.Destroy()`。这个列表是地图内部的实时列表；如果取走整堆物资，`SplitOff → DeSpawn → ListerThings.Remove` 会修改同一个列表，下一次 `MoveNext()` 就会抛异常。这条路径在单线程下也会发生，不需要其他 mod 并发修改。

比较担心的是，异常发生时材料已经扣掉，但末尾的 `progress = 0.001f` 尚未执行，catch 也没有回滚；下次尝试可能再次扣料。本机尚未统计实际损失量，也未确认其他玩家环境的复现情况。`ErrorOnce` 会隐藏同一建筑后续的错误，因此只看到一条红字也不代表已经恢复。

建议排查“从地图取料时恰好消耗整堆”的情况。最小修复方向是在这个循环中遍历物资列表快照，例如对 `ThingsInGroup(...)` 的结果先做 `ToList()`；原来的筛选和扣料逻辑可以保持不变。

谢谢维护这个 mod！
