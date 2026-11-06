# MTOEWY Health Inheritance Fix / 米莉拉养成健康继承修复

RimWorld **1.7** 独立 Harmony sidecar，版本 **1.0.0**，packageId `whitegiver.mtoewy.healthinheritancefix`。

依赖 Harmony 与「边缘米莉拉养成：与你相连之人」（Workshop `3814027507`，packageId `FunnyFlandre.milira.theoneentwinedwithyou`）。本补丁不修改工坊文件、存档或既有 `.aidier` 记录。

## 症状与根因

上游 `IdealTransferNeural.Capture` 只采集 `PsychicAmplifier` 的等级和 `MechlinkImplant` 是否存在，`IdealTransferCodec.Project` 又只投影 `neural/psylinkLevel` 与 `neural/mechlink`。因此跨存档继承会保留标准启灵/机控，却丢失艾蒂尔的仿生部位、植入体、等级植入体及其他持久健康状态。

本补丁在采集端增加纯标量健康快照，并让快照经过上游真实的保存、投影、校验、包装、载入和恢复链路。旧记录里从未写入的健康状态无法反推；要继承这些状态，需在启用本补丁后回到旧源档重新导出，再导入新档。

## 修复范围

四个显式 Harmony 目标：

1. `IdealTransferNeural.Capture(Pawn)` 后置：采集持久 Hediff，保存 defName、身体/部位标识、部位索引与标签、严重程度、等级和可见性。
2. `IdealTransferNeural.ExposeData()` 后置：用 `Scribe_Collections.Look(..., LookMode.Deep)` 读写 `<hediffs>`；每个条目强制保存八个标量字段，不生成 `Class` 等类型属性。
3. `IdealTransferCodec.Project(XmlDocument)` 前置/后置：暂时取出 `neural/hediffs`，待上游白名单结束后清理并重新挂回，避免被上游删除。
4. `IdealTransferNeural.Restore(Pawn)` 后置：先恢复 `Hediff_AddedPart`，再恢复其余状态，避免 `RestorePart` 清除同部位植入体；即使上游因无启灵且无机控而提前返回，后置仍会执行。

采集保留所有 `Hediff_AddedPart`、`Hediff_Implant`、`Hediff_Level`，以及符合条件的其他持久 Hediff，包括非标准 defName 的 `Hediff_Psylink`。排除：

- 上游已经特判的 `PsychicAmplifier`、`MechlinkImplant` 与运行时自管理的 `MTOEWY_EntwinedBond`、`MTOEWY_KitAura`、`MTOEWY_RewardAura`；
- 伤口、缺失部位和妊娠；
- 装备掉落即移除、装备实例派生，以及非部位持久类的消退/死亡消退状态；
- 非永久、非植入/仿生/等级类的急性有害状态。

恢复先按部位索引并核对 def/自定义标签，索引不适用时优先按自定义标签回退，再按部位 def 与占用情况寻找未安装同类 Hediff 的部位，以保留左右成对器官。缺失 def 或有效部位时逐条警告并跳过，不中断其他条目。最后刷新健康与容量缓存。

## 安装与使用

游戏关闭时，将本目录中的 `About/`、`Assemblies/` 和本说明部署为独立 `Mods/MTOEWYHealthInheritanceFix`。不要把 `Source/`、`Validation/`、`bin/` 或 `obj/` 放进游戏。

启用顺序：Harmony → 上游米莉拉养成模组 → 本补丁。正常启动应出现：

```text
[MTOEWY Health Inheritance Fix] Patched neural capture, persistence, projection and restoration.
```

若目标类型或四个方法签名任一不匹配，补丁会输出 `[MTOEWY Health Inheritance Fix]` 错误并撤销自身全部挂载，不会留下半套补丁。安装后需要在旧源档重新导出；本补丁不修复已经缺少数据的旧导出文件。

## 构建与真实 DLL 离线验证

需要 .NET 10 SDK。工程的 `GameRoot`、`HarmonyPath` 与验证工程的 `MTOEWYPath` 可由本机构建环境覆盖；构建不自动部署。

```text
dotnet build Source/MTOEWYHealthInheritanceFix.csproj -c Release
pwsh -NoProfile -File Validation/validate.ps1 -QaDirectory "独立的临时 QA 目录"
```

`validate.ps1` 依次执行 `bind`、`red`、`green`：

- `bind` 只在指定 QA 目录生成游戏与上游 DLL 的临时副本，重绑 Unity 引用并隔离配置/美术静态初始化；真实方法体不改，工坊与游戏安装文件不写。
- `red` 不挂载本补丁，走真实 `IdealTransferNeural.Capture → IdealTransferCodec.Capture → Wrap/Unwrap → Load → IdealTransferNeural.Restore`，确认标准启灵/机控保留而其他健康状态丢失。
- `green` 挂载四个补丁后走同一链路，并要求真实 `IdealTransferCodec.Parse` 接受数据。

GREEN 覆盖四个仿生肢体、左右成对植入体、与仿生臂同部位的等级植入体、三种持久状态、非标准启灵 Hediff、原版启灵/机控、急性状态过滤、子部位非新鲜状态，以及无标准启灵/机控时的恢复。目标身体故意反转左右部位顺序，以实际测试标签回退与成对去重。XML 检查要求 11 个条目的八字段白名单且 `<hediffs>` 内无任何 XML Attribute。

离线夹具只隔离游戏会话边界：配置/美术初始化、渲染缓存外壳、健康状态转换、启灵能力授予副作用，以及由正常 `LoadedModManager` 提供的类型程序集枚举。2026-11-06 用户已在游戏内完成一次跨存档继承验证；离线通过仍不等于所有第三方 Hediff 的自定义行为均已验证。

## 基线、维护与退役

验证基线：RimWorld 1.7 游戏程序集 SHA-256 `d55aa8e47fa3c5463ca85b0eccd51f195b4bbcd04f56da3af21c2f6688a81254`；上游 `MiliraTheOneEntwinedWithYou.dll` SHA-256 `f7b0b60b17faac3407a4ab99835fb74ed30e40ebe5bafaa4e0ebac4cf0c69913`。

上游更新后必须复核四个目标签名、`Project` 对 `neural` 的白名单、`Capture`/`Restore` 的标准启灵与机控语义，并重跑真实 DLL 红绿验证。若上游已完整保存并恢复通用持久健康状态，且本 sidecar 不再必要，可停用；停用不会删除现有存档 Hediff，但之后的新导出会回到上游行为。

排错见 [`docs/troubleshooting-20261106.md`](docs/troubleshooting-20261106.md)。

## 致上游作者（可直接参考）

如果上游打算原生支持，按下面这条链路改一处即可覆盖全套，不需要逐类 Hediff 特判。

**现象**：跨存档继承后，艾蒂尔只剩启灵等级与机控神经，仿生部位、植入体、等级植入体与其余持久状态全部消失。

**根因在采集端与投影白名单两处**：

1. `IdealTransferNeural.Capture` 只对 `PsychicAmplifier` 与 `MechlinkImplant` 做了 defName 特判，其余 Hediff 从未写入记录；
2. `IdealTransferCodec.Project` 对 `neural` 执行 `Keep(..., "psylinkLevel mechlink")`，即使采集端写了其他子节点也会被删除。

所以问题不在恢复端，也不在 `PawnGenerator` 之后的流程：`Restore` 里没有 `ClearHediffs`，`IdealHediffSynchronizer` 只增删 `MTOEWY_EntwinedBond`。

**建议实现**（本目录 sidecar 即按此实现的，可对照 `Source/HealthInheritanceFix.cs`）：

- 采集：遍历 `pawn.health.hediffSet.hediffs`，保留 `Hediff_AddedPart`、`Hediff_Implant`、`Hediff_Level` 与其他持久状态；排除已特判项、`Hediff_Injury`、`Hediff_MissingPart`、`Hediff_Pregnant`、带 `HediffComp_Disappears`/`HediffComp_RemoveIfApparelDropped` 的临时或装备派生状态，以及非永久的 `isBad` 状态。
- 持久化：写入 `neural/hediffs`，每条只存标量（defName、bodyDefName、partIndex、partDefName、partCustomLabel、severity、level、visible）。
- 投影：`Project` 的白名单放行 `neural/hediffs`，并对 `li` 做同样的字段裁剪。
- 恢复：先恢复 `Hediff_AddedPart`，再恢复其余。`Hediff_AddedPart.PostAdd` 会调用 `RestorePart`，先装植入体会被它清掉；装完仿生部位后把子部位的 `Hediff_MissingPart.IsFresh` 置 false，避免新装肢体显示为新鲜创伤。部位按索引 + 部位 def + `customLabel` 核对并回退，同 def 同部位去重，成对器官才不会叠到同一侧。

**两个容易踩的约束**：

- `IdealTransferCodec.Parse` 只允许 `IsNull="True"` 这一个 XML Attribute，节点也不能带多态 `Class="..."`。所以 `<hediffs>` 条目必须是纯标量；直接 `Scribe_Deep` 整个 Hediff 会被 Parse 拒绝。
- 已生成的旧 `.aidier` 不含这些数据，补丁无法反推；上游若修好，也建议在说明里提示玩家回旧源档重新导出一次。

本 sidecar 只是给现有版本兜底，上游原生实现后可直接停用；停用不会删掉存档里已有的 Hediff。
