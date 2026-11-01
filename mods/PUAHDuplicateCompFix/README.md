# PUAH 重复 Comp 修复 / PUAH Duplicate Comp Fix

Pick Up And Haul 的 XML patch 会给部分 pawn def 挂上两份 `CompHauledToInventory`，
读档时同一个 `ThingsHauledToInventory` 路径被注册两次，日志出现：

```
Tried to register the same list of load IDs twice. pathRelToParent=/ThingsHauledToInventory, parent=<pawn>
Could not get load IDs list. We're asking for something which was never added during LoadingVars. pathRelToParent=/ThingsHauledToInventory, parent=<pawn>
```

本补丁在 DefDatabase 完成后按 `compClass` 去掉重复的 `PickUpAndHaul.CompHauledToInventory`，
每个 def 只保留一份。不修改工坊文件、存档或其他 comp。

依赖：RimWorld 1.7、Pick Up And Haul。

- 排查记录：`docs/troubleshooting-20261102.md`
- 生效时机：**重启游戏后**。读档时按修复后的 def 生成 comp，存档里多余的 `<ThingsHauledToInventory />` 节点会被忽略，下次保存自动消失。
- 已有存档不需要清理；空列表不会丢物品。

## 上游建议

PUAH 的 `Patches/PickUpAndHaul.xml` 当前是：

```xml
<xpath>Defs/ThingDef[thingClass="Pawn"]/comps</xpath>
```

因为 patch 在继承之前执行，父 def 和重复声明 `thingClass` 的具体 def 会各命中一次。
可考虑只给 `BasePawn` 添加，或排除 `@Abstract="True"` 的中间 def，避免继承后重复。
