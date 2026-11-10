# 装备精神能力读档修复（RimWorld 1.7）

独立 sidecar，消除读档时 `Constructor on type 'RimWorld.Psycast' not found` 红字，并恢复被重置的装备能力状态。需要 Harmony，排在 Harmony 之后。不改工坊文件、不改存档，正常重启后生效。

**当前为 1.0.1。不要使用 1.0.0：该版读档前缀通过反射调用虚基类方法，会重新分派到同一派生前缀而无限递归，导致栈溢出闪退。1.0.1 已移除该反射调用。**

## 现象

读档时出现红字，栈顶为：

```
SaveableFromNode exception: System.MissingMethodException: Constructor on type 'RimWorld.Psycast' not found.
[Ref 0886167D]
   at Verse.ScribeExtractor.SaveableFromNode[T](XmlNode subNode, Object[] ctorArgs)
   at RimWorld.CompEquippableAbility.PostExposeData()
   at Verse.Map.ExposeData()
```

Subnode 是某个武器 comp 下的 `<ability Class="Psycast"><def>DMST_BroomFlight</def>...`。

## 根因

原版 `CompEquippableAbility`（1.7 新增的「装备附带能力」组件）这样存档：

```csharp
public override void PostExposeData()
{
    base.PostExposeData();
    Scribe_Deep.Look(ref ability, "ability");   // 没有 ctorArgs
    ...
}
```

`Scribe_Deep.Look` 在无 `ctorArgs` 时用 `Activator.CreateInstance(type)` 构造，即要求目标能力类型有**无参构造函数**。原版能力类（`Ability` 及其原版子类）都有无参构造，所以原版路径正常。

但 `Psycast` 只有 `(Pawn)` 和 `(Pawn, AbilityDef)` 两个构造函数。任何通过装备提供 Psycast 的 mod（本机是 **The Dead Man's Switch - Taktikmagier** 的 `DMST_MagierBroom` → `DMST_BroomFlight`，父模板 `PsycastBase`）都会在读档时命中 `MissingMethodException`。

`Scribe_Deep.Look` 捕获该异常并记红字，`ability = null`；随后 `PostLoadInit` 的 `AbilityForReading` 会按 `abilityDef` 懒创建一个新的 Psycast。所以：

* 不会崩档，读档会继续；
* 但每次读档都会刷一条红字；
* 该能力的 `Id`、冷却、充能等存档状态会丢失并重置。

## 修复

Harmony prefix 替换 `CompEquippableAbility.PostExposeData`，保存阶段完全走原逻辑；读档阶段先直接执行当前基类的深读操作 `Scribe_Deep.Look(ref comp.verbTracker, "verbTracker", comp)`，再深读 ability 并补构造参数。不能用 `MethodInfo.Invoke` 调用虚基类方法，这不是非虚 `base` 调用：

* 能力类型有**无参构造** → 传空参数，原版行为不变（包括原版 `CompEquippableAbilityReloadable` 的普通能力）；
* 否则依次匹配 `(Pawn)`、`(Pawn, AbilityDef)`、`(Pawn, Precept, AbilityDef)`。

`Psycast` 命中 `(Pawn)`；`Ability.ExposeData` 仍从存档读回 `<def>` 并调用 `Initialize()`，所以不会双重初始化。`PostLoadInit` 阶段照抄原方法，把 `pawn` / `verb.caster` 指回持握者。

## 验证

2026-11-10，Release 构建 0 警告 / 0 错误。离线驱动使用真实游戏 `Assembly-CSharp.dll`（1.7.5000 rev1275，Waylith / .NET 10）、Harmony 3 和真实补丁 DLL，原有 11 项检查继续保留：

* `Psycast` 确实没有无参构造函数（红字根因）；
* `Psycast` 有 `(Pawn)` 构造；
* 无参 `Activator.CreateInstance` 复现 `MissingMethodException`；
* `BuildCtorArgsFor(typeof(Psycast))` 返回单参 `{null}`，用该参数成功构造出 `Psycast`；
* `Ability` 仍返回空参数，原版路径不变；
* 目标方法、`ability` 私有字段、基类 `CompEquippable.PostExposeData` 均可解析；
* Harmony prefix 已绑定到 `CompEquippableAbility.PostExposeData`，owner 与方法正确。

**这11项只覆盖构造参数和注册，没有执行真实读档入口，因此未能发现1.0.0递归。不能再将旧11项通过称为读档通过。**

1.0.1新增真实目标入口回归：`Inactive / LoadingVars / ResolvingCrossRefs / PostLoadInit` 各执行一次。旧1.0.0 DLL在四个阶段全部RED（第3次递归时由测试guard安全中止，并未真的耗尽进程栈），修复后四项GREEN。

另外以真实 `ScribeSaver / Scribe_Deep / ScribeExtractor` 对普通Ability与Psycast各做保存→读取→交叉引用→PostLoadInit，共20项检查：能力与verbTracker节点只各写一次，保留类型、def、ID、冷却、充能和施法时间，恢复两组verb列表及owner，回绑holder/caster且不重复构造。**总计35项检查通过。**

隔离仅在临时游戏DLL副本中跳过配置、模组目录、基础贴图与日志初始化，不替换CompEquippableAbility、Ability/Psycast或Scribe目标方法。只读写临时XML夹具，不读写玩家存档或配置。驱动：`Validation/Driver`，运行（隔离脚本需本机Mono.Cecil 0.11.6）：

```powershell
pwsh -NoProfile -File Validation/run.ps1
```

2026-11-10：**用户确认1.0.1重启后读档成功，读档闪退浅测通过。** 扫帚能力和冷却的实际玩法效果尚未单独确认，在正常游玩时观察即可；不把一次读档成功扩大成整个模组列表或全部能力行为已验证。

## 部署

* 游戏目录：`Mods/EquippableAbilityPsycastLoadFix/1.7/Assemblies/EquippableAbilityPsycastLoadFix.dll`
* 配置：启用 `whitegiver.equippableability.psycastloadfix`，排在 Harmony 之后，保留其他模组的既有顺序。
* 游戏重启后加载。

## 时效性

* 本补丁针对原版 `CompEquippableAbility.PostExposeData` 的无参 `Scribe_Deep.Look`。若官方更新该方法（例如给它补上 ctorArgs），本补丁可停用。
* 判断失效：反编译当前 `Assembly-CSharp.dll`，若 `CompEquippableAbility.PostExposeData` 的 `Scribe_Deep.Look` 已带构造参数，或方法体与上面不再一致，需重新核对后再决定是否保留。
* 若 `RimWorld.Psycast` 新增无参构造函数，本补丁也不再必要（但仍不会造成错误）。
* 1.0.1直接复用当前基类唯一有效的verbTracker深序列化操作；游戏更新后还须对比`ThingComp.PostExposeData`（当前为空）和`CompEquippable.PostExposeData`，基类增加新逻辑时重新同步，不用反射虚调用代替`base`。
* 工坊 mod 更新不覆盖本补丁；它只依赖原版类与 Harmony。

## 给上游 / 未来的说明

问题不在 Taktikmagier 的 XML：`DMST_BroomFlight` 继承 `PsycastBase` 是正确用法，`CompEquippableAbility` 也是官方推荐的「装备给能力」组件。缺陷在原版 `CompEquippableAbility` 假定装备能力都有一个无参构造函数，而 `Psycast` 不是。mod 作者可以改用自定义 `CompEquippableAbility` 子类（带 ctorArgs 的 `Scribe_Deep.Look`）来规避；否则每个用装备提供 Psycast 的 mod 都会踩到。
