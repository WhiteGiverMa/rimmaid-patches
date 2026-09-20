# 沃芬超容武器箱 CE / Wolfein Weapon Box CE

RimWorld **1.6** 独立补丁，packageId：`meidocho.wolfein.weaponboxce`，版本 1.0.0。

## 功能

只调整 Wolfein Race 的 `Wolfein_WeaponCase`（沃芬族超容武器箱）：

| 设置 | 默认值 | 滑条范围 |
|---|---:|---:|
| CE 体积携带加成 `CarryBulk` | +350 | 0–1000，步长 1 |
| CE 重量携带加成 `CarryWeight` | +0 kg | 0–1000，步长 1 |

在「选项 → Mod 设置 → 沃芬超容武器箱 CE」调整。拖动后即时生效，已有装备无需重穿；关闭设置窗口时保存。提供恢复默认值按钮与中英文本地化。降低容量可能导致超载，并触发 CE 的正常超限物品处理。

这些数值是**装备的 CE 属性加成**，不是强制覆盖角色最终上限。角色最终容量仍遵循 CE 的体型、身体能力等计算。当前武器箱没有品质组件，因此不会按装备品质缩放；其他模组若添加品质组件，CE 自身的品质规则仍生效。

## 安装与兼容

1. 将本目录作为 `Mods/WolfeinWeaponBoxCE` 安装，确认 `1.6/Assemblies/WolfeinWeaponBoxCE.dll` 存在。
2. 启用 Wolfein Race（及其自身前置）和本补丁。使用 CE 时排在 Combat Extended 后面；同时使用 Wolfein CE Patches 时也排在它后面。
3. 首次安装后重启游戏。以后的滑条调整不需要重启。

CE 为可选依赖：未启用 CE 时不修改装备，设置页显示未生效提示。无需 Harmony，也不引用 CE 的 DLL。

本补丁不替代 Wolfein CE Patches 的武器、护甲等兼容内容。不改装备自身 Mass/Bulk/WornBulk、不改原 `CarryingCapacity +350`、`MoveSpeed -0.1`，不修改工坊文件或存档结构。已有存档可使用。设置为全局 Mod 设置，适用于所有存档与所有穿戴者，而非单件物品。

## 为什么需要这个补丁

核对的 Wolfein Race 定义带有 `CarryingCapacity +350`。Wolfein CE Patches 对武器箱仅添加护甲、`Bulk 12` 与 `WornBulk 11`，未提供 `CarryBulk`/`CarryWeight` 装备加成。

补丁在 Def 加载完成后更新这两项 `equippedStatOffsets`，并在设置改变时重写它们。重复应用不会累计；未来其他 XML 补丁添加同名加成时，本设置值也将作为该装备这两项加成的最终值，不与已有值叠加。其他装备与其他属性不受影响。

原生 `StatWorker.StatOffsetFromGear` 每次读取装备定义；CE 的 `CompInventory.capacityBulk/capacityWeight` 动态查询角色属性。仅改变容量不改变库存内容，因此不需要 Harmony、逐 tick 检查或库存缓存刷新。

## 构建与验证

```text
dotnet build 1.6/Source/WolfeinWeaponBoxCE.csproj -c Release -p:GameRoot="你的 RimWorld 目录"
pwsh -File Validation/validate.ps1 -GameRoot "你的 RimWorld 目录" -WorkshopRoot "你的 294100 工坊目录" -CombatExtendedRoot "你的 CE 模组目录"
```

需要 .NET Framework 4.8 引用程序集及可构建该项目的 .NET SDK。验证脚本使用 PowerShell 7 的 .NET 运行时及真实游戏程序集。默认检查刚构建的 DLL；可用 `-PatchDll` 检查打包版本。构建不自动部署；发布时手动将 `1.6/Source/bin/Release/net48/WolfeinWeaponBoxCE.dll` 复制到 `1.6/Assemblies/`。

已验证：Release 构建、C# LSP、XML 与翻译键；真实补丁 DLL 与原生装备属性链路的默认值、0/1000 边界、同一已有装备的数值变更、重复调用不叠加、保留原属性；真实 Scribe 保存/读取、缺省值与越界钳制。独立源码复核未发现阻塞性问题。

**未验证**：Unity 内实际滑条交互/字体显示、完整启动与无 CE 启动、完整模组列表及真实角色最终容量。离线驱动为绕过 Unity 内容系统只构造 ThingDef/Apparel/Mod 外壳，注入已解析 Def 后执行真实更新方法；没有伪造完整游戏通过。

游戏浅测：重启并读档后，在此设置页拖动体积滑条，关闭窗口查看已穿戴武器箱角色的 CE 装备/库存容量。应随设定改变；不需要脱下重穿。

## 更新监测与退役

- 本补丁是独立本地 Mod，不会被 Steam 更新直接覆盖。
- 上游：[Wolfein Race 3473140562](https://steamcommunity.com/sharedfiles/filedetails/?id=3473140562)，核对版本更新于 2026-09-16；[Wolfein CE Patches 3485371294](https://steamcommunity.com/sharedfiles/filedetails/?id=3485371294)，核对版本更新于 2026-09-09。两者 About 未提供语义版本号。
- 更新后检查 `1.6/Defs/ThingDefs_Apparel/Apparel_Sundry.xml` 中 `Wolfein_WeaponCase`、CE `Defs/Stats/Stats_Pawns_Inventory.xml`、CE `CompInventory` 容量 getter，以及其 `LastWriteTime`/SHA-256，再运行验证脚本。
- 目标 Def 或 CE StatDef 改名时会记录 `[Wolfein Weapon Box CE] Required defs are missing`，并停止修改；设置页显示未生效。
- 上游若提供了相同的可调功能，可停用本补丁；如果只是补上固定容量，本补丁仍可用来覆盖该装备的这两项加成。
- 若 CE 改为缓存容量，需重新检查更新时机；不要在未确认前添加每 tick 刷新。这里只支持 1.6，因为当前 1.5 定义没有此装备。

## English summary

Adds configurable CE equipped-stat offsets to `Wolfein_WeaponCase`: **+350 CarryBulk / +0 CarryWeight** by default, with two live 0–1000 integer sliders and a reset button. Close the settings window to save. Existing worn equipment updates without re-equipping. CE body-size/capacity rules still apply. Does nothing without CE; keeps all unrelated stats unchanged. This is not a replacement for the full Wolfein CE compatibility patch. Actual Unity UI and full-game integration still require an in-game check.
