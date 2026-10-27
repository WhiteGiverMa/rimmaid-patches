# 给作者的反馈草稿

仅为草稿，尚未发布。以下不包含私人存档、人物信息或本机路径。

---

你好，反馈一个自动化中枢的材料消耗问题，辛苦啦。

环境：RimWorld 1.7.4982 rev1280 与 1.7.4990 rev1069（Waylith），CeleTech Arsenal MKIII，工坊 ID 3446237098。建造并启用自动化中枢后，本机出现：

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

---

再补一个产物生成的问题（2026-10-27，RimWorld 1.7.4990 rev1069）。中枢完成一单后没有产物，日志是：

```text
[ManagedWorkTable] MakeProduct error: System.NullReferenceException
   at Verse.GenRecipe.MakeRecipeProducts(...)+MoveNext()
   at OberoniaAurea.MakeRecipeProducts_Patch.Postfix(...)
   at WeaponUpgrade.WeaponUpgradeHarmony.Patch_MakeRecipeProducts.Postfix(...)
   at CeleTech.Base.Building_ManagedWorkTable.CompleteCurrentBill()
```

`CompleteCurrentBill()` 调用的是 `GenRecipe.MakeRecipeProducts(recipe, workerPawn, new List<Thing>(), null, this, null, null, null)`。1.7 原版在产物 `thingDef.MadeFromStuff` 为真时会直接读取 `dominantIngredient.def`（反编译 IL `IL_00ec`），没有空检查；普通工作台总会传入实际原料，所以不会触发。自动化中枢固定传 `null`，因此武器、防具、家具这类需要材质的产物会必现空引用。

上面的 OberoniaAurea / WeaponUpgrade 只是在枚举原版返回值，属于路过；异常仍在原版 `MakeRecipeProducts` 里。更麻烦的是 catch 之后代码仍会 `innerContainer.ClearAndDestroyContents` 并重置 `sourceBill`/`progress`，所以结果是材料被消耗、账单被清掉，却没有产物。

最小修复方向：在 `CompleteCurrentBill` 里，如果产物需要材质而主导材料为空，使用 `GenStuff.DefaultStuffFor(product.thingDef)`（与 `workerPawn == null` 分支的 `ThingMaker.MakeThing(product.thingDef, null)` 行为一致）；如果希望产物材质跟随实际消耗，则在扣料时记录材料 def 并作为 `dominantIngredient` 传入。

谢谢维护这个 mod！
