# PATCH: WRMegaCorp ChoiceLetter_CSC.ExposeData Quest NRE

**补丁日期**: 2026-07-09
**Mod**: Wolfein Race MegaCorp Edition (`3687841204`)
**补丁形式**: 独立 Harmony 补丁 mod (`Fix_WRMegaCorp_QuestExposeData`)
**位置**: `A:\SteamLibrary\steamapps\common\RimWorld\Mods\Fix_WRMegaCorp_QuestExposeData\`

## 症状

加载存档时崩溃：

```
SaveableFromNode exception: System.NullReferenceException: Object reference not set to an instance of an object
[Ref omitted]
  at RimWorld.Quest.ExposeData()
  at Verse.ScribeExtractor.SaveableFromNode[T]
...
Subnode (illustrative): <quest>Quest_example</quest>
```

调用链：
```
SavedGameLoaderNow.LoadGameFromSaveFileNow
  → Game.LoadGame → Game.ExposeSmallComponents
    → History.ExposeData → Archive.ExposeData
      → ChoiceLetter_CSC.ExposeData  ← WRMegaCorp mod
        → Scribe_Deep.Look<Quest>    ← 问题调用
          → SaveableFromNode<Quest>
            → Quest.ExposeData       ← NRE
```

## 根因

`ChoiceLetter_CSC.ExposeData()` 的反编译代码：

```csharp
public override void ExposeData()
{
    ((ChoiceLetter)this).ExposeData();  // ✅ 基类: Scribe_References.Look(ref quest, "quest")
    Scribe_Values.Look(ref crashReason, "crashReason");
    Scribe_Values.Look(ref signalAccept, "signalAccept");
    Scribe_Values.Look(ref signalReject, "signalReject");
    Scribe_Deep.Look<Quest>(ref base.quest, "quest", ...);  // ❌ 重复深序列化
}
```

**逻辑错误**: `base.ExposeData()`（`ChoiceLetter.ExposeData`）已经用 `Scribe_References.Look` 正确处理 quest — 保存时存引用 ID，加载时从 quest 数据库查找。但 `ChoiceLetter_CSC` 又额外用 `Scribe_Deep.Look` 对同一字段做深序列化。

- **保存时**: `Scribe_Deep` 覆盖 `Scribe_References` 的输出，导致 quest 被 deep-saved（整个对象内联在 XML 中）
- **加载时**: quest 以 reference 保存（示例：`<quest>Quest_example</quest>`）。`Scribe_Deep` 试图从该引用节点创建新 Quest → 调用 `Quest.ExposeData()` → XML 没有子元素 → **NRE**

## 修复方式

**Harmony Transpiler** 移除 `Scribe_Deep.Look<Quest>` 调用。

`ChoiceLetter_CSC.ExposeData()` → 最终 IL:
1. `call ChoiceLetter::ExposeData` — quest 由 Scribe_References 正确处理
2. `Scribe_Values.Look(ref crashReason)` — 保持不变
3. `Scribe_Values.Look(ref signalAccept)` — 保持不变
4. `Scribe_Values.Look(ref signalReject)` — 保持不变
5. ~~`Scribe_Deep.Look<Quest>(ref quest, "quest")`~~ — **已移除**
6. `ret`

补丁逻辑: Transpiler 遍历方法 IL，匹配 `call Scribe_Deep.Look`（DeclaringType = Scribe_Deep, 泛型参数 = Quest），移除其 5 条 IL 指令。

## 表面验证

1. 在 ModsConfig 中启用 `Fix: WRMegaCorp Quest ExposeData NRE`
2. 确保排在 "Wolfein Race MegaCorp Edition" 之后
3. 加载之前报错的存档，确认不再出现该 NRE

## 风险

- **Steam 更新覆盖**: 补丁以独立 mod 形式存在，不受 WRMegaCorp Steam 更新影响
- **Harmony 兼容性**: 若 WRMegaCorp 未来更新修改 ExposeData 方法结构，Transpiler 可能匹配失败。失败时日志会有 `Failed to apply patch` 消息，此刻需更新补丁
- **上游修复**: 建议向 mod 作者报告此 bug（移除 Scribe_Deep.Look 调用即可）
