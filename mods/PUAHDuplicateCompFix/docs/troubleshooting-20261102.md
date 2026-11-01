# PUAH 读档重复 load ID 排查（2026-11-02）

## 症状

读档时出现一对红字：

```
ERROR Tried to register the same list of load IDs twice. pathRelToParent=/ThingsHauledToInventory, parent=Cinder_Mech_Krepost2869369
ERROR Could not get load IDs list. We're asking for something which was never added during LoadingVars. pathRelToParent=/ThingsHauledToInventory, parent=Cinder_Mech_Krepost2869369
```

栈顶都是 `PickUpAndHaul.CompHauledToInventory.PostExposeData()`
（`CompHauledToInventory.cs:18` 的 `Scribe_Collections.Look(ref takenToInventory, "ThingsHauledToInventory", LookMode.Reference)`）。

日志位置：`Player.log` 8171 / 8278（02:36:53）与 8896 / 8997（02:49:31），两次读档各一对。

## 存档证据

最新存档 `牢兵有机公司原版5.rws` 中 `Cinder_Mech_Krepost2869369` 节点内有两个同名标签：

- 1144343：`<ThingsHauledToInventory />`（在 `<beenRevealed>` 之后）
- 1144431：`<ThingsHauledToInventory />`（在 `<currentTurrent>rocket pod</currentTurrent>` 之后）

同存档还有两个 pawn 也是两份：

- `Wolfein_Mechanoid_CivilA262594`（`Pawn`）
- `DMS_Mech_EscortLifter874177`（`Fortified.WeaponUsableMech`）

说明这不是单只机兵的存档损坏，而是 def 上真的有两个 comp。

## 根因

1. PUAH 的补丁 `Patches/PickUpAndHaul.xml`：

   ```xml
   <xpath>Defs/ThingDef[thingClass="Pawn"]/comps</xpath>
   ```

   注意这是匹配 **显式 `<thingClass>Pawn</thingClass>` 元素**，不是继承后的类型。

2. RimWorld 的 `LoadedModManager.LoadAllActiveMods` 顺序是
   `ApplyPatches()` → `ParseAndProcessXML()`（后者内部做 `XmlInheritance.Resolve()`）。
   也就是 **XML patch 先于继承合并执行**。

3. 原版 `BasePawn`（`Data/Core/Defs/ThingDefs_Races/Races_Animal_Base.xml:4`）
   显式写了 `<thingClass>Pawn</thingClass>`，并且有 `<comps>`。
   PUAH 的 patch 命中它，给 BasePawn 加一份 `CompHauledToInventory`。

4. `Cinder_Mech_Krepost` 在 Cinder mod
   （工坊 3421959154，`1.7/Defs/ThingDefs_Races/RaceDef_Cinder_Krepost.xml:5`）
   自己也显式写了 `<thingClass>Pawn</thingClass>`，同时有自己的 `<comps>`。
   PUAH 的 patch 又命中它，给具体 def 再加一份。

5. 继承合并时，BasePawn 的那一份和具体 def 的那一份都进入最终 comps，
   于是同一个 def 有两个 `CompHauledToInventory`。

6. 读档时 `ThingWithComps.ExposeData()` 遍历 comps 调 `PostExposeData()`，
   两个 comp 都对 `(pawn, "/ThingsHauledToInventory")` 注册。
   `LoadIDsWantedBank` 以 `(IExposable parent, string path)` 为键，第二次注册报
   "Tried to register the same list of load IDs twice"；ResolvingCrossRefs 阶段
   两个 comp 再各取一次，第二次 `TakeList` 报 "Could not get load IDs list"。

`DMS/Patches/DMSPatch.xml` 把 `Cinder_Mech_Krepost` 的 thingClass 改成
`Fortified.WeaponUsableMech`，不参与重复的形成；它只是解释了为什么这只 pawn
的存档类型不是 `Pawn`。

## 影响

- 两个 `takenToInventory` 都由 PUAH 维护，实际只有 `GetComp<T>()` 返回的第一个会被写入；
  第二个列表通常为空。读档时第一个列表正常解析，第二个列表读取失败后按空处理。
- 存档中该 pawn 的两份都是空的，没有物品引用丢失。
- 属于日志噪音，不阻塞读档；但每次读档都会重复出现。

## 修复

sidecar `PUAHDuplicateCompFix`：`[StaticConstructorOnStartup]` 遍历
`DefDatabase<ThingDef>.AllDefsListForReading`，对每个 `ThingDef.comps`
按 `compClass` 去掉重复的 `PickUpAndHaul.CompHauledToInventory`，保留一份。

- 只处理这一个 comp 类型，不碰其他 comp 或其他 mod 的配置。
- 不修改工坊 DLL、XML 或存档。
- 修复在 DefDatabase 完成后、游戏开始前执行；读档时按修复后的 def 生成 comp。
- 存档里多余的 `<ThingsHauledToInventory />` 节点会被忽略，下次保存自动消失。
- 已存在的 pawn 实例在下一次读档时重建 comps，不需要手动改档。

## 验证边界

- 构建：0 警告、0 错误。
- 存档与 XML 证据齐全，根因链可复现。
- 未在游戏内自动验证；需要重启游戏后读档，确认这对红字不再出现。
- 若仍出现，说明还有其他 def 的 comps 重复，需要重新取日志和存档节点。
