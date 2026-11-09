# ITab Genes Width Fix / 基因标签页宽度修复

**1.0.0 · RimWorld 1.7 · 独立 sidecar · 游戏内浅测待确认**

- packageId: `whitegiver.itabgenes.widthfix`
- author: `WhiteGiverMa`
- 依赖：Harmony、Biotech

## 症状

打开带基因的 Pawn 的基因标签页时红字（`ErrorOnce`，每次进程只报一次）：

```
Exception filling tab RimWorld.ITab_Genes: System.ArgumentOutOfRangeException:
length ('-1') must be a non-negative value. (Parameter 'length')
Actual value was -1.
   at System.String.ThrowSubstringArgumentOutOfRange(Int32 startIndex, Int32 length)
   at Verse.GenText.Truncate(String str, Single width, Dictionary`2 cache)
   at RimWorld.BiostatsTable.Draw(Rect rect, Int32 gcx, Int32 met, Int32 arc, Boolean drawMax, Boolean ignoreLimits, Int32 maxGCX)
   at RimWorld.GeneUIUtility.DrawGenesInfo(Rect rect, Thing target, ...)
   at RimWorld.ITab_Genes.FillTab()
```

## 根因

1.7 原版的 `ITab_Genes` 是单例，在 `ThingDef.ResolveReferences`（游戏加载阶段）通过 `InspectTabManager.GetSharedInstance` 构造：

```csharp
size = new Vector2(Mathf.Min(736f, UI.screenWidth), 550f);
```

但 `UI.screenWidth` 是 `static int`，默认 0，只在 `Root.OnGUI()` 里由 `UI.ApplyUIScale()` 赋值。**def 解析远早于第一次 OnGUI**，所以构造时 `size.x = 0`，之后没有 `UpdateSize` 覆盖，永久为 0。

打开基因标签页时：

- `DrawGenesInfo` 的 `rect4.width = position.width - 144 = 0 - 20 - 144 = -164`；
- `BiostatsTable.Draw` 里 `width = rect.width - num2 - 90 - 22 - 4` 更负；
- 普通 Pawn 的基因代谢总和 `met == 0`，`MetabolismDescAt(0)` 返回 `string.Empty`；
- 1.7 的 `GenText.Truncate` 没有空串保护，`"".Substring(0, -1)` 抛 `ArgumentOutOfRangeException`。

RimHUD 的 `DrawGenesInfo` transpiler 只替换 `165f` 常量，BetterGeneGraphicsFramework 只在 `FillTab` 后追加按钮，均非原因，只是出现在调用栈里。

## 修复

Harmony postfix `InspectTabBase.UpdateSize`（`TabRect` 每次读取时调用）：

```csharp
if (__instance is not ITab_Genes) return;
ref var size = ref SizeField(__instance);
if (size.x > 0f) return;
size.x = Mathf.Min(736f, UI.screenWidth);
```

只修 `ITab_Genes` 的宽度，不改变 `GenText.Truncate` 的全局行为，不影响其他标签页。

## 验证

真实 1.7 `Assembly-CSharp.dll` 离线驱动：

- **RED**：未打补丁时，`UI.screenWidth = 0` 构造的 `ITab_Genes.size.x = 0`，调用 `UpdateSize` 后仍为 0。
- **GREEN**：打补丁后，`UI.screenWidth = 1920` 时 `size.x` 修复为 736；`UI.screenWidth = 500` 时修复为 500；第二次调用保持 736（幂等）。
- 输出：`GREEN: ITab_Genes zero width repaired to 736 (and 500 on a small screen); RED confirmed unpatched behavior.`

## 部署

- 2026-11-09 部署到 `A:/SteamLibrary/steamapps/common/RimWorld/Mods/ITabGenesWidthFix`。
- DLL SHA256：`33e590bd1b8d5b563ab1d99abef3f17371bd531134d3b66f8f7af923d5ebfe3c`
- `ModsConfig.xml` 在 `</activeMods>` 前追加 `whitegiver.itabgenes.widthfix`，activeMods **295 → 296**，原有条目顺序不变；备份 `Temp/opencode/itabgenes-width-20261109/ModsConfig-before.xml`。
- 部署时游戏进程仍在运行（PID 37924），未启停游戏；**下次正常重启后加载**。

## 恢复

删除 `Mods/ITabGenesWidthFix` 并从 `ModsConfig.xml` 移除 `whitegiver.itabgenes.widthfix` 即可。不触碰存档或工坊文件。

## 时效性

- 本补丁针对 **RimWorld 1.7.5000 rev1275** 的初始化顺序。
- 若 Ludeon 后续修复 `ITab_Genes` 构造时使用 `UI.screenWidth` 的时机（或在 `UpdateSize` 里重算），本补丁可以删除。
- 升级游戏后先确认 `ITab_Genes` 构造函数与 `InspectTabBase.UpdateSize` 仍存在；工具会在 Harmony 绑定时自然失败并记录。

## 上游反馈建议

这是 1.7 原版回归：`ITab_Genes` 在 `UI.ApplyUIScale` 之前构造。可向 Ludeon 报告，建议在 `ITab_Genes.UpdateSize` 或 `TabRect` 里按当前 `UI.screenWidth` 重算，而不是只在构造函数里取一次。

## 构建与验证

```powershell
dotnet build mods/ITabGenesWidthFix/Source/ITabGenesWidthFix.csproj -c Release
dotnet build mods/ITabGenesWidthFix/Validation/Validation.csproj -c Release
dotnet mods/ITabGenesWidthFix/Validation/bin/Release/net10.0/Validation.dll
```
