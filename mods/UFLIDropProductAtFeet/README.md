# 联合轻工打印机脚底输出 / UFLI Drop Product At Feet

- **packageId**：`whitegiver.ufli.dropproductatfeet`
- **目标**：RimWorld 1.7 +「联合轻工家具拓展 - UF Light Industries Furniture」（`kindseal.ufli`，工坊 `3651799915`）
- **类型**：独立本地 sidecar Harmony 补丁；不修改工坊文件、UFLI 原 DLL 或任何存档
- **默认行为**：`dropAtFeet = true`，产物直接刷在打印机脚底
- **游戏内验证**：2026-11-09 用户确认游戏内验证通过。

## 使用方法

### Mod 设置

在 RimWorld 的「选项 → Mod 设置 → 联合轻工：脚底输出」中切换：

> 全息打印机：产物刷在打印机脚底

设置由本补丁自己的 `ModSettings` 持久化保存，**全局生效、跨存档共享**，不是每台打印机单独保存。

### 打印机底部快捷开关

选中 `UFRecipes.Building_SuperWorkbench`（联合全息打印机）后，底部 Gizmo 栏会多出一个带勾选状态的「脚底输出 / Drop at feet」按钮。点击后立即切换并写入设置；下一次产物交付即使用新模式，无需读档或重启。

## 两种模式

### 开启：脚底输出（默认）

Harmony Prefix 拦截：

```csharp
UFRecipes.Building_SuperWorkbench.DeliverProduct(Thing product)
```

完整保留上游前置检查：

```csharp
if (product == null || product.Destroyed || bench.Map == null ||
    !product.def.EverStorable(false)) return false;
```

随后直接调用上游私有方法 `TryPlaceProductNear(product)`，并把其返回值原样交给调用方。该方法以 `ThingPlaceMode.Near` 在 `InteractionCell` 附近放置产物。vanilla `Thing.InteractionCell` 在没有显式交互格时会寻找相邻可站立/可行走格，最终回退到 `Position`，因此补丁不另写一套选址规则。

此模式会跳过上游的：

1. `Comp_UFAutoOutput.TryAccept` 内部输出容器；
2. `AllHaulDestinationsListInPriorityOrder` 全图货架/容器；
3. `AllGroupsListInPriorityOrder` 全图地面储存区。

只要 `TryPlaceProductNear` 成功，`product.Spawned == true` 且 `DeliverProduct == true`，不会误入 `Produce` 的 `PauseForMissingIngredients` 或免费制作的 `NotifyFreeCraftDeliveryFailed` 分支。若脚底附近确实无法放置，仍返回 `false`，保留上游既有的失败处理，不吞掉异常状态。

### 关闭：UFLI 原版输出

Prefix 直接放行，未替换或复制上游原方法。产物仍按上游原顺序投递：内部输出容器 → 全图货架/容器 → 全图储存区 → 脚底兜底。

## 反编译确认的调用链

- `Produce`：逐个产物调用 `DeliverProduct`；返回 `false` 时尝试兜底放置并调用 `PauseForMissingIngredients`。
- `TryProduceFreeCraft`：循环交付所有产物；任一返回 `false` 时销毁未生成产物并调用 `NotifyFreeCraftDeliveryFailed`。
- `Comp_UFAutoOutput`：实现 `IThingHolder` 的内部 `ThingOwner` 容器，容量满时可选择从建筑位置弹出；不是另一个独立投递器。
- `UFSettingsPage` / `UFRecipesSettings` / `UFRecipesMod`：UFLI 自己已有固定布局的全局设置页。本补丁选择独立 Mod 设置 + 打印机 Gizmo，不改写上游页面布局。

## 构建与离线验证

```powershell
dotnet build -c Release Source/UFLIDropProductAtFeet.csproj
pwsh -NoProfile -File Validation/validate.ps1
```

验证针对本机真实：

- `A:/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll`
- `A:/SteamLibrary/steamapps/workshop/content/294100/3651799915/1.7/Assemblies/UFRecipes.dll`

本机最终结果：

```text
RED: original DeliverProduct used AllHaulDestinationsListInPriorityOrder;
     remoteAccepts=1, remoteStack=2, nearPlacements=0.
GREEN: drop-at-feet used real TryPlaceProductNear -> GenPlace(Near, InteractionCell),
       returned true, skipped remote storage; off/on switching restored each mode immediately.
GREEN: Produce and TryProduceFreeCraft succeeded;
       PauseForMissingIngredients=0, NotifyFreeCraftDeliveryFailed=0, totalNearPlacements=4.
```

日志、真实 DLL 的 QA 绑定副本、反编译证据与配置备份位于：

`C:/Users/A1337/AppData/Local/Temp/opencode/ufli-drop-at-feet-20261109/`

离线 harness 仅在独立 .NET 进程中替换 Unity/地图 UI 边界（如 `GenPlace` 的最终地图登记、远程投递的 Home Area 判定）并记录参数；真实 `DeliverProduct`、`TryPlaceProductNear`、`ThingOwner` 合并、调用方分支和本补丁 IL 均实际执行。它不是游戏内表面 QA。

## 安装与加载顺序

部署目录：`RimWorld/Mods/UFLIDropProductAtFeet`。

依赖并晚于：

1. `brrainz.harmony`
2. `kindseal.ufli`
3. 若同时存在，建议放在 `whitegiver.ufli.virtualworkerfix` 之后

修改 Mod 列表后需**下次启动 RimWorld**才加载；不要为应用磁盘部署而强制关闭正在运行的游戏。

## 失效与退役条件

- UFLI 改名/删除 `DeliverProduct(Thing)` 或 `TryPlaceProductNear(Thing)`：补丁会记录错误并跳过投递 Patch，应停用并重新适配。
- 上游原生加入相同的输出模式开关：移除本补丁，避免双重 Patch。
- 升级到 RimWorld 1.8+ 或 UFLI 更新：重新反编译并运行离线 RED/GREEN；不要未经验证直接沿用旧 DLL。
- 若希望恢复上游行为但不卸载补丁：直接在 Mod 设置或打印机 Gizmo 中关闭即可。
