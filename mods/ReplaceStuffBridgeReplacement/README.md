# Replace Stuff：桥梁原位替换

RimWorld 1.7 独立 sidecar，package ID：`whitegiver.replacestuff.bridgereplacement`。
需要 Harmony 与 [Replace Stuff - Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3526354009)（工坊 `3526354009`），排在二者之后；不修改工坊 DLL，也不硬引用其程序集。

## 行为

- 在**已建成**的木桥上可直接放置重型桥梁蓝图，不再要求先手动拆桥。
- 同样支持 `WhiteGiver_ReinforcedConcreteHeavyBridge`：木桥可直接升级为钢筋混凝土重型桥梁，原版钢铁重桥也可与它互换。
- 判定使用 `TerrainDef.isFoundation && TerrainDef.bridge`，不按 defName 维护白名单；符合相同属性的其他模组桥也能使用。
- 施工期间旧桥保留，取消蓝图或工地不会消耗旧桥。新地基实际覆盖成功后，按原版 `resourcesFractionWhenDeconstructed` 返还旧桥材料：12 木材返 6，14 钢铁返 7。
- 不额外增加拆除工时，只消耗新桥原本的建造工时；不触发拆桥坍塌、自动重建或一次裸露河床的地形刷新。
- 桥面已有建筑继续服从原版共存与承重规则。特别是重桥降级为木桥时，不允许借即将被替换的旧重桥支撑重型建筑。

本补丁不添加自定义 Frame、设置或持久化数据。停用后只恢复原版放置限制；已经建成的桥仍属于原版或相应桥梁模组，不引入本 sidecar 的存档依赖。

## 根因与实现

Replace Stuff 的 `ReplaceFrame` 只处理 `Thing`；桥梁是 `TerrainDef` 地基，不能复用墙体替换对象。原版还有两道独立限制：

1. `GenConstruct.CanPlaceBlueprintAt_NewTemp` 在目标为地基且该格已有地基时返回 `FoundationAlreadyExists`。
2. `GenConstruct.CanBuildOnTerrain` 通过 `GridsUtility.GetAffordances` 只读旧木桥的 `Light`，而新重桥要求下层地形的 `Bridgeable`，因此即使绕过第一道限制也仍会失败。

本补丁只替换三个原版方法中的定点调用：

- `CanPlaceBlueprintAt_NewTemp`：两处 `FoundationAt(IntVec3)` 读取在合法桥到桥替换时视为“即将替换”，其余校验原样执行，包括桥面建筑的承重兜底。
- `CanBuildOnTerrain`：`GetTerrain` 与 `GetAffordances` 在合法替换时读取虚拟移除旧桥后的下层地形。此接缝同时覆盖放置与后续 `TerrainGrid.DoTerrainChangedEffects`，避免工地被地形刷新误取消。本 transpiler 明确排在 Replace Stuff 的 `Uuugggg.rimworld.Replace_Stuff.main` 之后，保留其自动补桥逻辑。
- `Frame.CompleteConstruction`：唯一的 `SetFoundation` 调用由包装器执行。包装器先记录旧桥，调用真实 `SetFoundation`，确认新地基已经生效后才调用一次 `GenLeaving.DoLeavingsFor`；不调用 `RemoveFoundation` 或 `Notify_FoundationDestroyed`。

查询过程不临时改写任何地形数组。三处调用的数量、目标签名和完工方法的 `Map` local 都会在应用前核对；漂移或应用失败会回滚本 sidecar 的 transpiler 并记录错误，不静默退化成半套行为。

## 范围边界

- 只替换已经建成的地基桥；旧桥仍是蓝图或 Frame 时不增加互换能力。
- `underGrid` 非空的叠层格子不允许替换，因为原版 `SetFoundation` 本身不支持该状态。
- 如果旧桥已经有独立的 `RemoveFoundation` 指定或拆除作业，本补丁不会替你取消它；请先取消拆除，再安排替换。
- 不额外提供桥面建筑共存豁免，也不修改研究、材料或其他放置规则。

## 构建与安装

```text
dotnet build Source/ReplaceStuffBridgeReplacement.csproj -c Release
```

可用 MSBuild 属性 `GameRoot`、`HarmonyPath` 指向自己的 RimWorld 1.7 与 Harmony 1.7 版本。引用均不复制到输出，构建不自动部署。只把本目录的 `About`、`Assemblies` 复制到 `RimWorld/Mods/ReplaceStuffBridgeReplacement` 并启用，正常重启游戏后生效。不要复制 `Source`、`Validation` 或它们的 `bin/obj`。

## 回归验证

2026-10-25：Release 构建零警告、零错误。进程外加载真实 RimWorld 1.7、Harmony 3 与 Replace Stuff 程序集，先红后绿验证：

- 原流程对木桥到重桥分别在地基与 affordance 校验拒绝；接入后原版重桥和钢筋混凝土重桥均可直接放置。
- 同 Def、非桥地基、`underGrid`、不可桥下层和不可改变地形仍拒绝。
- 250 次重复查询及一次 `PlaceWorker` 异常前后，四层地形、漆色和地形事件完全不变。
- 共存建筑保留原版规则；重桥到自定义重桥可放行，重桥到木桥不能借旧重桥承重。
- 普通建筑在裸水面仍可借 Replace Stuff 的既有重桥蓝图兜底通过，组合 patch 没有破坏这条上游路径。
- 真实 `TerrainGrid.DoTerrainChangedEffects` 不再取消替换工地，当前施工物仍按原版规则忽略自身。
- 真实 `SetFoundation` 与 `GenLeaving` 依次验证：木桥到自定义重桥返 6 木材、同 Def 重复完成不重复返料、钢铁重桥到自定义重桥返 7 钢铁、木桥到原版重桥返 6 木材。
- 实际 Mod 构造重复执行仍只注册三个目标 transpiler；Replace Stuff 与本补丁顺序和组合 IL 正确。部署 IL 保证 `SetFoundation` 在 `DoLeavingsFor` 前，且无 `RemoveFoundation`／坍塌调用。

验证使用最小地图和真实目标方法。仅配置/静态 Def 初始化、无世界边缘查询、翻译以及最终物品落图外壳使用测试替身；材料计算与物品生成保持真实。**完整游戏内 pawn 施工、渲染和材料落地仍未实测，不能由离线结果代替。**

```text
dotnet build Validation/Validation.csproj -c Release
dotnet Validation/bin/Release/net10.0/Validation.dll bind A:/SteamLibrary/steamapps/common/RimWorld <临时QA目录>
dotnet Validation/bin/Release/net10.0/Validation.dll run A:/SteamLibrary/steamapps/common/RimWorld Assemblies/ReplaceStuffBridgeReplacement.dll <临时QA目录>
```

`REPLACE_STUFF_ASSEMBLY` 与 `HARMONY_DIRECTORY` 可覆盖默认依赖路径。`bind` 只写临时 QA 副本，不改安装原件。

游戏内浅测：读档后在已建成的木桥上直接放一格重型桥梁蓝图，让殖民者建成，确认无需先拆桥、木桥不再残留且返还 6 木材；再在另一格木桥上对钢筋混凝土重型桥梁重复一次。取消一次未完工替换，旧桥应保持不变。

## 上游更新与退役

上游源码：[MemeGoddess/RimWorld-ReplaceStuff](https://github.com/MemeGoddess/RimWorld-ReplaceStuff)，MIT。核验基线为其 2026-10-22 提交 `8ed21a01f974e7a3d77e52fee9f8026305f1e9ea`；该版本没有桥到桥或地形到地形替换。

- Replace Stuff `1.7/Assemblies/Replace_Stuff.dll`：`cc39816e84438c6723ee247eefae206a984bc5b7d6df6b72a3b9cd20d91ebb9e`。
- RimWorld `Assembly-CSharp.dll`：`0be7a7ea4b3111d4430f45b36c234a5e43287880b0514b23915f2ef9a0629e16`。
- 本补丁发布 DLL：`b100344a7e91b191e720c991bed9c9d1f7bd4905dd87ab4d17bc01fe469bc2f0`。

独立 mod 不会被 Steam 覆盖，但游戏或 Replace Stuff 更新都可能改变调用契约。更新后先重跑验证并核对三处签名/调用数；出现 `Could not apply bridge replacement patches` 时视为未生效，不要当作已经修复。

如果 Replace Stuff 原生支持等价的地基桥原位替换，并能保住持续地形校验、承重规则和成功后返料，可停用本 sidecar。不要仅凭版本号或更新公告假定已经支持。
