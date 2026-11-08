# 排查与验证记录：UFLI 打印机脚底输出开关（2026-11-09）

## 1. 环境与边界

- 目标模组：`KindSeal.UFLI`（工坊 `3651799915`）
- 游戏 DLL：`A:/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll`
  - SHA-256：`d55aa8e47fa3c5463ca85b0eccd51f195b4bbcd04f56da3af21c2f6688a81254`
- UFLI DLL：`A:/SteamLibrary/steamapps/workshop/content/294100/3651799915/1.7/Assemblies/UFRecipes.dll`
  - SHA-256：`439713cfc6986565a2c18ea15805d4dbbccbb8f173d08206c6fb9bdf8bcda57a`
- sidecar：`whitegiver.ufli.dropproductatfeet`
- 补丁 DLL：`1.7/Assemblies/UFLIDropProductAtFeet.dll`
  - SHA-256：`93353523ebaaf6fcdf07d4a077be84e401966c9977b0e9a523ede8bb1e02814b`
- 工坊页面于任务期间抓取：标记 RimWorld 1.6/1.7，页面显示更新时间为 2026-10-28；本机实际验证以本地 DLL 哈希为准。
- 调查与部署时 RimWorld 正在运行：PID `86268`，启动时间 `2026/11/8 23:46:08`。
- 未终止、重启或向该进程注入任何内容；未修改 `.rws`；未修改工坊目录。

## 2. 上游反编译实证

### 2.1 `DeliverProduct` 原顺序

`UFRecipes.Building_SuperWorkbench.DeliverProduct(Thing product)`：

1. 空对象、已销毁、无 Map、非可储存物品检查；
2. `GetComp<Comp_UFAutoOutput>()` 并尝试 `TryAccept(product)`；
3. 遍历 `Map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder`，寻找玩家派系、未禁运、Home Area 内且接受产物的容器，调用真实 `ThingOwner.TryAddOrTransfer`；
4. 遍历 `AllGroupsListInPriorityOrder`，对储存区格子调用 `GenPlace.TryPlaceThing(..., ThingPlaceMode.Direct, ...)`；
5. 最终回退 `product.Spawned || product.Destroyed || product.stackCount <= 0 || TryPlaceProductNear(product)`。

### 2.2 `TryPlaceProductNear`

上游方法循环调用：

```csharp
GenPlace.TryPlaceThing(product, InteractionCell, Map, ThingPlaceMode.Near, ...);
```

成功生成即返回 `true`。`Verse.ThingUtility.InteractionCellWhenAt` 在无显式交互格时尝试相邻格，最后返回建筑中心，因此天然具备 `Position` 回退。

### 2.3 为什么返回值必须保持 true

- `Produce` 在 `!DeliverProduct(item)` 时进入 `PauseForMissingIngredients()`，即便稍后有兜底放置，也会暂停序列。
- `TryProduceFreeCraft` 在某个 `DeliverProduct` 返回 `false` 时进入 `DestroyUnspawnedProducts` 与 `NotifyFreeCraftDeliveryFailed`。

所以脚底模式不能只补一个 Postfix 落点；必须在原远程扫描之前直接执行 `TryPlaceProductNear`，并用其真实成功结果设置 `__result`。

### 2.4 设置交互选择

- `UFRecipesMod.DoSettingsWindowContents` 只调用 `UFSettingsPage.Draw`。
- `UFSettingsPage` 使用固定的左右栏、176/300 像素面板布局，并向 `UFRecipesSettings` 写入自己的设置。
- `UFRecipesSettings` 是上游 `ModSettings`，没有产物输出选项。
- 为减少上游 UI 布局升级造成冲突，本补丁不改写 `UFSettingsPage`，而使用自己的 `DropProductAtFeetSettings : ModSettings`，并通过 Harmony Postfix 在 `Building_SuperWorkbench.GetGizmos` 后追加 `Command_Toggle`。
- 两处 UI 读写同一个全局 `dropAtFeet`；Gizmo 点击后立即 `Write()`，无需等设置窗口关闭。

## 3. 补丁结构

```text
UFLIDropProductAtFeet/
  About/About.xml
  LoadFolders.xml
  Languages/ChineseSimplified/Keyed/UFLIDropProductAtFeet.xml
  Languages/English/Keyed/UFLIDropProductAtFeet.xml
  1.7/Assemblies/UFLIDropProductAtFeet.dll
  Source/UFLIDropProductAtFeet.csproj        (net10.0)
  Source/DropProductAtFeet.cs
  Validation/Validation.csproj
  Validation/Program.cs
  Validation/validate.ps1
  README.md
  docs/troubleshooting-20261109.md
```

Patch 启动时检查两个目标方法都仍是私有 `bool Method(Thing)`；签名不符会记录退役提示，不会猜着 patch。`TryPlaceProductNear` 通过 Harmony `AccessTools.MethodDelegate` 调用，不复制上游实现。

## 4. 离线 RED / GREEN

证据目录：`C:/Users/A1337/AppData/Local/Temp/opencode/ufli-drop-at-feet-20261109/`

- `validation-bind.log`
- `validation-red.log`
- `validation-green.log`
- `bind.txt`
- `bound/Assembly-CSharp.dll`、`bound/UFRecipes.dll`
- `decompiled/`（反编译文本）

`bind` 使用 Mono.Cecil 仅改写 **QA 副本** 的 `UnityEngine*` 引用为游戏自带 `UnityStubs`，真实游戏与工坊 DLL 不写入。

### RED：原版远程货架优先

真实原版 `DeliverProduct` 在存在可接受产物的远程 `IHaulDestination` 时：

- `RemoteCrate.Accepts` 调用 1 次；
- 产物与容器内同 Def 物品经真实 `ThingOwner.TryAddOrTransfer` / `TryAbsorbStack` 合并，远程堆叠从 1 变为 2；
- 源产物被消费；
- 脚底 `GenPlace(Near)` 调用 0 次。

```text
RED: original DeliverProduct used AllHaulDestinationsListInPriorityOrder; remoteAccepts=1, remoteStack=2, nearPlacements=0.
```

### GREEN：开关与真实调用方

应用本补丁真实 DLL 后：

1. `Harmony.GetPatchInfo(DeliverProduct)` 确认 owner 为 `whitegiver.ufli.dropproductatfeet`。
2. 开关开启：真实 `DeliverProduct` Prefix → 真实 `TryPlaceProductNear`；QA 边界记录到 `ThingPlaceMode.Near` 与真实 `bench.InteractionCell`；返回 `true`，产物为 Spawned，远程 `Accepts` 仍为 0。
3. `null`、Destroyed、无 Map、`EverStorable(false) == false` 四项保护均返回 `false` 且不访问储存/落点。
4. 开关关闭：同一进程下一次调用立刻走真实远程容器合并；再开启：立刻恢复脚底生成。
5. 真实 `Produce` 完整跑过产物循环并完成/清除序列，`PauseForMissingIngredients` 计数 0。
6. 真实 `TryProduceFreeCraft` 返回 `Succeeded`，消耗 1 次免费制作充能，`NotifyFreeCraftDeliveryFailed` 计数 0。

```text
GREEN: drop-at-feet used real TryPlaceProductNear -> GenPlace(Near, InteractionCell), returned true, skipped remote storage; off/on switching restored each mode immediately.
GREEN: Produce and TryProduceFreeCraft succeeded; PauseForMissingIngredients=0, NotifyFreeCraftDeliveryFailed=0, totalNearPlacements=4.
```

### QA 夹具透明说明

离线进程没有 Unity 主线程和完整游戏 UI。为使真实目标方法可执行，验证用 Harmony 仅在 QA 进程中：

- 把 `GenPlace.TryPlaceThing` 的最终生成边界替换为记录参数 + 写入最小 `mapIndexOrState` / `positionInt`，从而保留并执行真实 `TryPlaceProductNear` 的 `product.Spawned` 判断；
- 固定 `Faction.OfPlayer` 与 `InHomeArea`，隔离派系/区域 UI 环境；
- 跳过销毁后的地图 Designation/Reservation 清理；
- 为普通 `Produce` 固定 `GenRecipe.MakeRecipeProducts` 的单产物输出，为免费制作固定效率为 1；
- 给 Bill 明确的非 RepeatCount 模式，避免无语言环境的完成消息。

这些不写入真实 DLL，也不改变本补丁的判定。它证明 IL/调用链与状态转换，但不等同于游戏内表面 QA。

## 5. 部署与下次验证

- 源目录：`G:/dev/rimmaid-patches/mods/UFLIDropProductAtFeet`
- 部署目录：`A:/SteamLibrary/steamapps/common/RimWorld/Mods/UFLIDropProductAtFeet`
- 配置备份：`C:/Users/A1337/AppData/Local/Temp/opencode/ufli-drop-at-feet-20261109/ModsConfig-before.xml`
- 配置候选/校验副本：同目录 `ModsConfig-after.xml`
- `whitegiver.ufli.dropproductatfeet` 插入在 `whitegiver.ufli.virtualworkerfix` 后；若该补丁不存在才回退到 `kindseal.ufli` 后。
- XML 校验要求 activeMods 仅增加该 1 项，移除新增项后与原顺序逐项一致；原始文本移除新增整行后应与备份完全一致。

由于当前游戏正在运行，本次不会获得新日志。下次由用户正常启动后，可在 `Player.log` 查找：

```text
[UFLI Drop Product At Feet] Patched Building_SuperWorkbench.DeliverProduct; drop-at-feet=on.
```

浅度表面验证建议：启动并读入一个已有联合全息打印机的存档，选中打印机，确认底部出现「脚底输出」且默认勾选；点击一次应取消勾选，再点击恢复。该一步操作即可验证 Mod 设置、翻译与 Gizmo 链路；实际产物路径已由上述离线调用链验证。
