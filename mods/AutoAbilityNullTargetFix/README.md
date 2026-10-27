# 自动技能：空目标检查修复

独立 RimWorld **1.7 / Waylith** 兼容 Mod，依赖 Harmony 与 Auto Ability（工坊 `3578731212`）。

`packageId`: `whitegiver.autoability.nulltargetfix`，版本 `1.0.0`。

## 修复行为

修复自动技能在“避免对友军使用范围技能”（`autoPreventAlly`）检查中的 `NullReferenceException`。

- 只把 `AutoAbility_Utils.MyValidate` 中三处 `current2.Pawn.IsColonist` / `IsColonyMech` / `IsColonyAnimal` 的读取改为空安全判断。
- 保留技能选择、冷却、目标过滤、距离、弹窗、无效目标和其他全部检查。
- 不修改工坊 DLL、Def、存档，不改变敌人逻辑。
- 非 Pawn 的受影响目标（物品、尸体、格子等）会被视为“不是友军”，而不是让整个技能验证抛红字。

## 根因与实现

当前工坊版本（2026-10-15 更新）的 `MyValidate` 在方法末尾有：

```csharp
if (tmp.autoPreventAlly && ability_item.def.HasAreaOfEffect)
{
    foreach (LocalTargetInfo item in ability_item.GetAffectedTargets(thing))
    {
        LocalTargetInfo current2 = item;
        if (current2.Pawn.IsColonist || current2.Pawn.IsColonyMech || current2.Pawn.IsColonyAnimal)
        {
            return false;
        }
    }
}
```

`GetAffectedTargets` 在范围技能没有启用 `canUseAoeToGetTargets` 时会直接返回原目标；工作台/物品/格子等目标的 `Pawn` 为 `null`。`current2.Pawn` 没有空检查，于是自动施放这类技能时每个 Tick 都可能记录一次：

```text
Exception in Verse.AI.ThinkNode_Tagger TryIssueJobPackage: System.NullReferenceException
   at AutoAbility.AutoAbility_Utils.MyValidate(...AutoAbility_Utils.cs:line 137)
   at AutoAbility.AutoAbility_Utils.AutoTryGiveJob(...line 469)
   at AutoAbility.JobGiver_AutoFightWithAbility.TryGiveJob(...line 77)
```

同一行 137 对应反编译 IL `IL_03f5`：`LocalTargetInfo.get_Pawn` 之后直接 `callvirt Pawn.get_IsColonist`。

本补丁通过 Harmony transpiler 仅替换这三处 `callvirt` 为静态辅助方法（null → false），其余 IL、分支和异常块保持原样。找不到恰好三处模式时抛错并拒绝应用，不静默兜底。

## 构建与安装

```powershell
dotnet build .\Source\AutoAbilityNullTargetFix.csproj -c Release
```

可通过 `-p:GameRoot=... -p:HarmonyPath=...` 指定本机 1.7 游戏根目录与 Harmony 3 路径；不需要 Auto Ability 编译期引用。不分发第三方程序集，构建不会自动部署。

关闭游戏后，将本目录的 **About/** 与 **Assemblies/** 复制到游戏 `Mods/AutoAbilityNullTargetFix/`，不要复制 Source/Validation/bin/obj。在模组列表中排在 Harmony、Auto Ability 之后并启用，正常启动生效。卸载时关闭游戏、禁用此独立补丁即可，无新增存档数据。

成功日志：

```text
[AutoAbility Null Target Fix] Patched MyValidate (three null-safe target checks).
```

出现 `patch not applied` 或 `Could not apply` 视为**未生效**。Auto Ability 未加载时会明确记录 skipped。

## 回归验证

```powershell
pwsh -File .\Validation\validate.ps1
```

可指定 `-GameRoot`、`-HarmonyPath`、`-AutoAbilityPath` 和 `-PatchPath`；默认使用本目录的发布 DLL。2026-10-27 验证结果：

- Release 构建零警告、零错误。
- RED：直接调用实际 `AutoAbility_Utils.MyValidate`，使用非 Pawn 受影响目标和 `autoPreventAlly = true`，复现原始 `NullReferenceException`。
- GREEN：通过真实 Mod 构造函数注册 Harmony 3 transpiler；实际方法中三处不安全 Pawn 读取全部替换，三处辅助调用存在。
- GREEN：同一个实际 `MyValidate` 返回 `true`；空目标辅助方法安全返回 `false`，无派系 Pawn 的行为不变。
- 缺少 Auto Ability 时不注册补丁。

**离线接缝边界**：`MyValidate` 本体、`GetAffectedTargets` 的目标分支、`LocalTargetInfo`、`Ability` 的虚属性分派均执行真实代码。测试用 `Ability` 子类只接管 `CanCast`、`CanApplyOn`、`GizmoDisabled` 三个前置判断，并提供最小 `AbilityDef`/目标 `Thing`；不覆盖完整 JobGiver 链路、真实技能定义、地图范围目标或游戏内自动施放。

2026-10-27 小若确认游戏内验证通过：补丁注册日志出现，未再观察到该 `MyValidate` 红字。离线调用仍不能替代完整 JobGiver 链路、真实技能定义或所有地图范围目标的覆盖。

## 上游更新与退役

核验基线：2026-10-27，RimWorld `1.7.4990 rev1069`，Auto Ability `1.7/Assemblies/AutoAbility.dll` SHA-256：

```text
C2479340C2813E3408E52383599C8A3C4062361D5B18CF18F864B42ECE4500E7
```

独立补丁不受 Steam 更新覆盖。上游更新后复核 `MyValidate` 是否仍有三个 `Pawn` 属性读取且未加空检查；作者加上 `current2.Pawn != null` 或改用安全目标筛选后，停用本补丁。DLL 哈希或最后修改时间变化是复核信号，不是自动退役证据。

给作者的中文反馈草稿见 [`docs/UPSTREAM_COMMENT.zh-CN.md`](docs/UPSTREAM_COMMENT.zh-CN.md)，尚未发布。
