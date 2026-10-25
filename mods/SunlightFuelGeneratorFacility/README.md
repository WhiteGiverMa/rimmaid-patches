# 日光燃料发电设施 / Sunlight Fuel Generator Facility

RimWorld **1.7** 独立 mod，`whitegiver.sunlightfuel.generator`，版本 1.0.0。依赖 **Milira Race**（`Ancot.MiliraRace`）与 **Fortification Industrial - Citadel**（`Aoba.Fortress.Industrial.Citadel`，同时依赖 `Aoba.Fortress.Industrial`）。无需 Harmony。

## 这个 mod 做什么

新增建筑 **日光燃料发电设施**：

- 贴图、9x5 绘制尺寸与 7x3 占地沿用城塞的「燃油发电设施」（`FT_CitadelGenerator`）。
- 燃料只接受米莉拉的 **日光燃料**（`Milira_SunLightFuel`）。
- 带「排出燃料」gizmo：选中建筑后点击按钮即可指定排出燃料工作，和原版飞船燃料箱一致。城塞的燃油发电设施也会被本 mod 补上同一个按钮。
- 继承城塞永久结构的属性：1500 HP、3000 建造工作量、不可拆除、对切割/钝击等伤害有高抗性。
- 造价沿用城塞设施，并额外需要 20 个日凌晶（5 台日光燃料发电机的用量）。
- 发电量、最大燃料量、每日消耗可在「选项 → Mod 设置 → 日光燃料发电设施」中调整；修改立即应用到已建成设施，关闭窗口自动保存。

## 默认值是怎么推出来的

RimWorld 1 年 = 60 天。三个参考对象（均取本机实际加载的数值）：

| 建筑 | 发电量 | 燃料容量 | 每日消耗 | 满燃料续航 |
|---|---:|---:|---:|---:|
| 原版化合燃料发电机 | 1000 W | 30 | 4.5 | 6.67 天 |
| 米莉拉日光燃料发电机 | 1500 W | 10 | 0.5 | 20 天（1/3 年） |
| 城塞燃油发电设施 | 5000 W | 2400 | 20 | 120 天（2 年） |

把城塞燃油发电设施的每一项，按「米莉拉发电机 ÷ 原版发电机」的同一项比例缩放：

- 发电量：`5000 × (1500 / 1000) = 7500 W`
- 燃料容量：`2400 × (10 / 30) = 800`（比例推算值）
- 每日消耗：`20 × (0.5 / 4.5) = 2.22`
- 比例推算的续航：`800 / 2.22 ≈ 360 天 = 6 年`，是城塞 2 年的 3 倍，也等于米莉拉 1/3 年相对原版 6.67 天的 3 倍。

比例推算的 800 容量能烧 6 年，实际手感太长，所以**默认容量取 400**：

- 默认续航：`400 / 2.22 ≈ 180 天 = 3 年`
- 仍可在设置里把容量改回 800 或其他数值。

爆炸也按同一口径缩放：米莉拉发电机满燃料爆炸半径为 `10 × 1.25 = 12.5` 格；本设施把 `explosiveExpandPerFuel` 设为 `12.5 / 当前最大燃料量`，所以无论玩家把容量调成多少，满燃料爆炸半径都保持在 12.5 格。默认 400 容量时系数为 `0.03125`。

## 安装

1. 确认已启用 Milira Race、Fortifications - Industrial、Fortification Industrial - Citadel。
2. 只把本 mod 目录的 `About`、`Assemblies`、`Defs`、`Languages`、`Patches` 复制到游戏 `Mods/SunlightFuelGeneratorFacility/`。
3. 在模组列表启用并重启。不要复制 `Source` 或构建输出。

可用于已有存档；停用后建筑会变成缺失 def，建议先拆除或保留启用。

## 构建

```text
dotnet build Source/SunlightFuelGeneratorFacility.csproj -c Release
```

默认从 `A:\SteamLibrary\steamapps\common\RimWorld` 读取 `Assembly-CSharp.dll` 与 `UnityStubs.dll`，可用 `-p:GameRoot=...` 覆盖。构建产物在 `Source/bin/Release/net10.0/`，手工复制 DLL 到 `Assemblies/`。

## 实现与维护

- XML 定义新 `ThingDef`，`ParentName="FTC_BasePermanentStructure"` 复用城塞永久结构基类；`graphicData/texPath` 指向城塞的 `Things/FortGenerator/Generator`。
- `CompProperties_Power.basePowerConsumption` 是私有字段，代码用缓存的反射字段写入；`CompProperties_Refuelable` 与 `CompProperties_Explosive` 的字段是公开的，直接写。
- 启动时 `[StaticConstructorOnStartup]` 应用一次设置；设置窗口每次重绘时检测三元组变化并热应用；`WriteSettings` 关闭窗口时再应用一次。
- 热应用会对已有建筑钳制目标燃料与当前燃料，并调用 `CompPowerPlant.UpdateDesiredPowerOutput()`；发电网在下一 tick 汇总新功率。
- 「排出燃料」gizmo 完全走原版：`CompProperties_Refuelable` 的 `canEjectFuel=true` 会让 `Designator_EjectFuel` 进入反向命令集合，选中建筑时 `GizmoGridDrawer` 自动把它画成 gizmo。没有燃料或已经指定时原版会自行隐藏/拒绝，不需要自定义 comp 或 Harmony。
- `Patches/CitadelGeneratorEjectFuel.xml` 只给城塞的 `FT_CitadelGenerator` 补 `canEjectFuel` 字段，带存在性检查；上游若已自带不会重复添加。

### 依赖更新后的复核项

- `FT_CitadelGenerator` 的 `size`、`drawSize`、贴图路径、`FTC_BasePermanentStructure` 的 parent 名。
- `Milira_SunLightFuel`、`Milira_SolarCrystal` 的 defName。
- `CompProperties_Power` 中 `basePowerConsumption` 字段名与 `CompPowerPlant.UpdateDesiredPowerOutput()` 签名。
- `CompProperties_Refuelable.canEjectFuel` 字段，以及 `GizmoGridDrawer` 对 `Designator_EjectFuel` 的反向 gizmo 行为。
- 原版/两个参考 mod 的发电量、容量、日耗变化时，重新评估默认值。

### 退役信号

- 上游直接提供「可调数值的日光燃料大型发电机」。
- 城塞移除永久结构基类或改名，或自行给 `FT_CitadelGenerator` 加上可用的排出燃料 gizmo。
- 米莉拉移除日光燃料、日凌晶或其发电机。

## 验证状态

本机已构建、XML 解析、仓库校验通过；用户已确认游戏内加载、设置窗口和排出燃料 gizmo 正常。跨版本（1.6）未适配。
