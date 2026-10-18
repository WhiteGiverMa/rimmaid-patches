# 武器基站：左键选择安装武器

RimWorld 1.7 独立 sidecar。需要 Harmony 与 Weapon Station（`Vortex.WeaponStation`），排在二者之后。同时兼容原版基站与 Combat Extended 版本。

## 行为

- 选中没有武器的基站，左键点击「安装武器」：进入原版目标选择模式，鼠标变成选择光标；点击地图上的远程武器即可下达安装指派，随后殖民者照常搬运安装。
- 右键点击「安装武器」：直接打开原来的武器列表 FloatMenu。
- 不再出现左右键都弹同一个大菜单的情况。
- 可选择的武器与原菜单一致：地图上的远程武器、排除基站自带的空枪、排除已被其他基站指派的武器、排除被禁止的武器。
- 地图上没有可选武器时，左键点击会显示原模组的「地图上没有可用的远程武器」提示，不进入空的目标选择模式。

## 根因

RimWorld 1.7 的 `GizmoGridDrawer` 在右键点击 gizmo 时，如果该 gizmo 的 `RightClickFloatMenuOptions` 为空，会把 `GizmoState.OpenedFloatMenu` 当作 `Interacted` 处理，回退调用 `ProcessInput`。Weapon Station 的「安装武器」是普通的 `Command_Action`，其 `action` 直接调用 `OpenInstallMenu`，所以左右键都会打开同一个 FloatMenu。

## 实现

- Harmony postfix 到 `WeaponStation.Building_WeaponStation.GetGizmos` 与 `WeaponStation_CE.Building_WeaponStationCE.GetGizmos`。
- 只把标签为 `VortexWS_InstallWeapon` 的 `Command_Action` 替换为 `Command_Target` 子类，其他 gizmo 原样返回。
- 子类复制原命令的图标、标签、描述、快捷键与分组字段；左键走 `Command_Target` 的原版目标选择流程，右键在 `GizmoOnGUI` 中拦截并直接调用原命令的 `action`。
- 目标过滤器复用原菜单的条件：`ThingRequestGroup.Weapon`、`IsRangedWeapon`、排除 `turretGunDef`、排除其他基站的 `PendingWeapon`、排除 `ForbidUtility.IsForbidden`。
- 选中后通过反射调用基站自己的私有 `SetPendingInstall(Thing)`，因此原模组的指派 Desig、取消按钮、WorkGiver 与 JobDriver 流程完全不变。没有复制或修改工坊 DLL。
- 如果未来原模组重命名 `SetPendingInstall`，补丁会记录一条错误并退回原来的菜单行为，而不是留下一个不能用的按钮。

## 兼容与加载顺序

- 原版 `WeaponStation.Building_WeaponStation`。
- Combat Extended 的 `WeaponStation_CE.Building_WeaponStationCE`（CE 激活时原模组会把 ThingDef 的 `thingClass` 替换成它）。
- CE 未加载时按类型名跳过 CE 类，不影响原版基站。
- `loadAfter`：Harmony、Weapon Station、Combat Extended。

## 验证

2026-10-18：Release 构建 0 警告 / 0 错误。进程外使用真实 `WeaponStation.dll`、`WeaponStation_CE.dll`、`Assembly-CSharp.dll` 与 Harmony 3 驱动，确认：

- 两个 `GetGizmos` 方法都能被定位并接受同一 postfix。
- 两个基站类型都存在私有的 `SetPendingInstall(Thing)`。
- 补丁注册后目标方法出现在 Harmony 的已修补方法集中。

另做一次真实游戏启动烟测：`Player.log` 出现两条 `[Weapon Station Pick Target] Patched ...`，附近没有本模组相关错误；启动后已关闭进程，ModsConfig 未被改写。未进入存档，未做游戏内视觉/交互验收。正常重启后的一次浅测：

1. 选中一个空武器基站。
2. 左键点「安装武器」，确认出现选择光标；点击地上的一把远程武器，确认出现「取消安装武器」并等待殖民者安装。
3. 右键点「安装武器」，确认直接弹出原来的武器列表。

## 构建、部署、退役

```text
dotnet build Source/WeaponStationPickTarget.csproj -c Release
```

可覆盖 `GameRoot`、`HarmonyPath`、`WeaponStationPath`。构建不会自动部署。安装包只需 `About`、`Assemblies`、`Languages`。

退役条件：上游 Weapon Station 自己把左键改为目标选择、或重命名/移除 `SetPendingInstall`、或改变 `GetGizmos` 结构。上游更新后重新核验两个基站类的 `GetGizmos` 与 `SetPendingInstall(Thing)`，不能只看启动日志。

工坊 Weapon Station ID 3807251471，当前版本 1.4.3。CE 版本类位于该模组的 `CombatExtended/Assemblies/WeaponStation_CE.dll`，由 CE 激活时加载。
